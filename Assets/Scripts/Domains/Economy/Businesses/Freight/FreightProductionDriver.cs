using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Freight
{
    /// <summary>
    /// Pass IX WS3 (PASS8-002 / A4-11, A4-12): the production owner for
    /// detailed freight. Before this driver, FreightCompanyRuntime had zero
    /// production instances and FreightShipmentService was driven only,
    /// synchronously, by the NPC construction chain. This driver:
    ///
    /// - constructs and owns one FreightCompanyRuntime per freight business
    ///   (SimulationDrivers' production bootstrap path calls EnsureCompany
    ///   for every live LiveryFreight business — the instancing gap
    ///   documented in SimulationDrivers is closed source-side);
    /// - is the system of record for DETAILED shipments (A4-12): every
    ///   shipment it creates goes through FreightShipmentService and is
    ///   accepted by exactly one FreightCompanyRuntime. The
    ///   LogisticsRuntimeManager category-stock path is untouched and stays
    ///   the path for bulk manager freight — no third freight system;
    /// - advances shipments on the tick through the full lifecycle:
    ///   creation → loading → transport → arrival → unloading → acceptance
    ///   → settlement → completion. Rejected deliveries stay in wagon
    ///   custody as Delayed/Partial (never disappear, never settle);
    ///   revenue is collected only for shipments the recipient actually
    ///   accepted in full (FreightCompanyRuntime.CollectFreightCharge
    ///   already gates on Completed + deliveryApplied, and the delivered
    ///   quantity integrity stays with FreightShipmentService — it is not
    ///   duplicated here);
    /// - saves/restores mid-transport exactly once with cargo intact, via
    ///   the service's own DTO (clear-then-rebuild per company).
    ///
    /// Crew contract: the caller supplies the handling person (loader /
    /// unloader) and, per shipment, a dispatch plan (real driver + draft
    /// animals) recorded at creation. Without a plan or a handler the
    /// shipment honestly waits in its current state — capacity is never
    /// invented.
    /// </summary>
    public sealed class FreightProductionDriver
    {
        /// <summary>Per-shipment dispatch plan recorded at creation.</summary>
        public sealed class DispatchPlan
        {
            public EntityId DriverId = EntityId.Invalid;
            public List<EntityId> AnimalIds = new List<EntityId>();
        }

        private readonly FreightShipmentService service;
        private readonly Dictionary<string, FreightCompanyRuntime> companies =
            new Dictionary<string, FreightCompanyRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, BusinessRuntimeState> businessRuntimes =
            new Dictionary<string, BusinessRuntimeState>(StringComparer.Ordinal);
        private readonly Dictionary<string, DispatchPlan> dispatchPlans =
            new Dictionary<string, DispatchPlan>(StringComparer.Ordinal);
        private readonly FreightServiceAgreementBook serviceAgreements = new FreightServiceAgreementBook();

        public FreightProductionDriver(FreightShipmentService service = null)
        {
            this.service = service ?? new FreightShipmentService();
        }

        /// <summary>The detailed-shipment system of record (A4-12).</summary>
        public FreightShipmentService Service => service;

        public IReadOnlyDictionary<string, FreightCompanyRuntime> Companies => companies;
        public FreightServiceAgreementBook ServiceAgreements => serviceAgreements;

        /// <summary>
        /// Constructs (once) the production FreightCompanyRuntime for a
        /// freight business and binds its ledger runtime for settlement.
        /// Idempotent: the same business always gets the same instance.
        /// </summary>
        public FreightCompanyRuntime EnsureCompany(string businessInstanceId, BusinessRuntimeState runtime = null)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
                return null;
            if (!companies.TryGetValue(businessInstanceId, out FreightCompanyRuntime company))
            {
                company = new FreightCompanyRuntime(businessInstanceId);
                companies[businessInstanceId] = company;
            }
            if (runtime != null)
                businessRuntimes[businessInstanceId] = runtime;
            return company;
        }

        /// <summary>R-04: optional external resolver for a freight payer's
        /// till (e.g. the shared business runtime directory). Resolution
        /// falls back to runtimes registered with this driver; a payer that
        /// resolves nowhere stays uncollected — never credited blind.</summary>
        public Func<string, BusinessRuntimeState> PayerRuntimeResolver;

        /// <summary>R-04: registers any business's runtime so shipment
        /// payers named by id can be resolved (and debited) at collection.</summary>
        public void RegisterBusinessRuntime(string businessInstanceId, BusinessRuntimeState runtime)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId) || runtime == null)
                return;
            businessRuntimes[businessInstanceId] = runtime;
        }

        /// <summary>Resolves a shipment payer's runtime, or null when the
        /// payer genuinely cannot be resolved from the driver's data.</summary>
        public BusinessRuntimeState ResolvePayerRuntime(string payerBusinessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(payerBusinessInstanceId))
                return null;
            if (PayerRuntimeResolver != null)
            {
                BusinessRuntimeState resolved = PayerRuntimeResolver(payerBusinessInstanceId);
                if (resolved != null)
                    return resolved;
            }

            return businessRuntimes.TryGetValue(payerBusinessInstanceId, out BusinessRuntimeState runtime)
                ? runtime : null;
        }

        public FreightCompanyRuntime FindCompany(string businessInstanceId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId))
                return null;
            return companies.TryGetValue(businessInstanceId, out FreightCompanyRuntime company) ? company : null;
        }

        /// <summary>Every detailed shipment across all companies (the driver's live set).</summary>
        public List<LogisticsShipmentState> AllShipments()
        {
            var all = new List<LogisticsShipmentState>();
            foreach (FreightCompanyRuntime company in companies.Values)
            {
                if (company?.ActiveShipments == null)
                    continue;
                foreach (LogisticsShipmentState shipment in company.ActiveShipments)
                {
                    if (shipment != null)
                        all.Add(shipment);
                }
            }
            return all;
        }

        /// <summary>
        /// Slice S4 (2026-11-05): resolves an honest transit duration for a
        /// detailed shipment from its live JourneyModel route — one-way miles
        /// at the Canon wagon speed (4 mph), converted to game-seconds.
        /// Pure and stateless: no invented passage — an unroutable pair
        /// refuses with the route diagnostic, and a null model or missing
        /// location id refuses naming the missing input. No floor here; the
        /// 60 s floor lives in CreateShipment, mirroring the LRM factories.
        /// </summary>
        public static bool TryResolveTransitGameSeconds(
            JourneyModel journeys,
            string originLocationId,
            string destinationLocationId,
            out float transitGameSeconds,
            out string reason)
        {
            transitGameSeconds = 0f;
            reason = string.Empty;
            if (journeys == null)
            {
                reason = "FreightProductionDriver: no journey model supplied for transit resolution.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(originLocationId))
            {
                reason = "FreightProductionDriver: no origin location id supplied for transit resolution.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(destinationLocationId))
            {
                reason = "FreightProductionDriver: no destination location id supplied for transit resolution.";
                return false;
            }
            JourneyRoute route = journeys.FindRoute(originLocationId, destinationLocationId, TravelMode.Wagon);
            if (route == null || !route.Found)
            {
                reason = "FreightProductionDriver: no wagon route from '" + originLocationId
                    + "' to '" + destinationLocationId + "'"
                    + (route != null && !string.IsNullOrWhiteSpace(route.Diagnostic)
                        ? " — " + route.Diagnostic : string.Empty);
                return false;
            }
            transitGameSeconds = route.TotalMinutes * 60f;
            return true;
        }

        /// <summary>
        /// Creates a detailed shipment through the system of record and
        /// accepts it with the carrier company. The dispatch plan (real
        /// driver + animals) is recorded for the tick drive. Returns null
        /// with a reason when the service refuses (phantom wagon,
        /// over-capacity, or an A4-13 exclusivity conflict).
        /// </summary>
        public LogisticsShipmentState CreateShipment(
            string carrierBusinessId,
            string shipmentId,
            string sourceBusinessId,
            string destinationBusinessId,
            int quantityUnits,
            FreightCargoSpec cargo,
            string wagonAssetId,
            FreightShipmentHooks hooks,
            EntityId driverId,
            List<EntityId> animalIds,
            int freightChargeCents,
            string freightPayerBusinessId,
            List<string> diagnostics,
            out string reason,
            string freightServiceAgreementId = "",
            string sourceLotId = "",
            string sourceSkuId = "",
            bool holdForDispatchAuthorization = false,
            float transitDurationGameSeconds = 0f)
        {
            reason = string.Empty;
            diagnostics = diagnostics ?? new List<string>();
            FreightCompanyRuntime company = EnsureCompany(carrierBusinessId);
            if (company == null)
            {
                reason = "FreightProductionDriver: a carrier business id is required.";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(sourceLotId))
            {
                if (string.IsNullOrWhiteSpace(sourceSkuId))
                {
                    reason = "FreightProductionDriver: a source lot requires its SKU; no shipment was created.";
                    return null;
                }
                foreach (LogisticsShipmentState existing in service.TrackedShipments)
                {
                    if (existing != null && FreightShipmentService.IsLiveShipment(existing)
                        && string.Equals(existing.CargoLotId, sourceLotId, StringComparison.Ordinal))
                    {
                        reason = $"FreightProductionDriver: source lot '{sourceLotId}' is already bound to live shipment '{existing.ShipmentId}'.";
                        return null;
                    }
                }
            }

            LogisticsShipmentState shipment = service.CreateShipment(
                shipmentId, sourceBusinessId, destinationBusinessId, quantityUnits,
                cargo, wagonAssetId, hooks, out reason);
            if (shipment == null)
                return null;
            if (shipment.IsPreLoadBlocked)
            {
                reason = "FreightProductionDriver: shipment '" + shipmentId + "' blocked pre-load: "
                    + shipment.BlockedReason;
                return shipment; // terminal record; never accepted, owns zero cargo
            }

            if (!string.IsNullOrWhiteSpace(sourceLotId)
                && !service.TryBindSourceLot(shipment, sourceLotId, sourceSkuId, out reason))
            {
                shipment.state = LogisticsShipmentStatus.Failed;
                shipment.blockedReason = "Source lot binding refused before pickup: " + reason;
                shipment.remainingQuantityUnits = 0;
                shipment.loadApplied = false;
                return shipment;
            }

            shipment.freightChargeCents = Math.Max(0, freightChargeCents);
            shipment.freightPayerBusinessInstanceId = freightPayerBusinessId ?? string.Empty;
            shipment.freightServiceAgreementId = freightServiceAgreementId ?? string.Empty;
            shipment.requiresDispatchAuthorization = holdForDispatchAuthorization;
            // Slice S4: a route-derived duration overrides the 120 s field
            // default (LRM 60 s floor). <= 0 means unset: legacy callers keep
            // the default unchanged.
            if (transitDurationGameSeconds > 0f)
                shipment.transitDurationGameSeconds = Mathf.Max(60f, transitDurationGameSeconds);
            if (!company.TryAcceptShipment(shipment, diagnostics))
            {
                reason = "FreightProductionDriver: carrier '" + carrierBusinessId + "' refused shipment '" + shipmentId + "'.";
                return null;
            }

            dispatchPlans[shipmentId] = new DispatchPlan
            {
                DriverId = driverId,
                AnimalIds = animalIds != null ? new List<EntityId>(animalIds) : new List<EntityId>(),
            };
            return shipment;
        }

        /// <summary>Releases an accepted agreement shipment only after the next dispatch-preparation stage has prepared its real team.</summary>
        public bool AuthorizeShipmentDispatch(string shipmentId, out string reason)
        {
            reason = string.Empty;
            LogisticsShipmentState shipment = AllShipments().Find(s => s != null
                && string.Equals(s.ShipmentId, shipmentId, StringComparison.Ordinal));
            if (shipment == null) { reason = "Unknown freight shipment."; return false; }
            if (!shipment.requiresDispatchAuthorization) return true;
            if (shipment.Status != LogisticsShipmentStatus.Planned)
            { reason = "Only a planned shipment can be released from its dispatch-preparation hold."; return false; }
            shipment.requiresDispatchAuthorization = false;
            return true;
        }

        /// <summary>Records or replaces the dispatch plan for a shipment (e.g. after a restore).</summary>
        public void SetDispatchPlan(string shipmentId, EntityId driverId, List<EntityId> animalIds)
        {
            if (string.IsNullOrWhiteSpace(shipmentId))
                return;
            dispatchPlans[shipmentId] = new DispatchPlan
            {
                DriverId = driverId,
                AnimalIds = animalIds != null ? new List<EntityId>(animalIds) : new List<EntityId>(),
            };
        }

        /// <summary>
        /// Advances every live detailed shipment by one tick step and then
        /// collects revenue for newly completed shipments. Returns the
        /// number of shipments whose state advanced this tick. Delayed
        /// shipments (including refused deliveries) are left exactly as
        /// they are: cargo in legitimate wagon custody, no revenue.
        /// </summary>
        public int AdvanceTick(
            float gameSeconds,
            FreightShipmentHooks hooks,
            EntityId handlerId,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (hooks == null || hooks.Tasks == null || hooks.Budgets == null)
            {
                diagnostics.Add("FreightProductionDriver: tick needs TaskAuthority + budgets — shipments not advanced.");
                return 0;
            }
            int minutes = Math.Max(1, Mathf.CeilToInt(Math.Max(0f, gameSeconds) / 60f));
            int advanced = 0;

            // Pass XI WS2 (A-25): an Invalid handler is safe but silent in
            // the original code — handler-gated shipments (Planned, Loading,
            // Arrived, Unloading, undelivered Partial) simply never move,
            // while InTransit shipments still advance and revenue still
            // collects. Name the wait loudly so a carrier with no owner and
            // no active employees is diagnosable instead of mysteriously
            // idle. Behaviour is unchanged: nothing is dispatched without
            // a real handler.
            if (!handlerId.IsValid)
            {
                int waiting = 0;
                foreach (LogisticsShipmentState shipment in AllShipments())
                {
                    if (shipment == null || !FreightShipmentService.IsLiveShipment(shipment)) continue;
                    if (shipment.Status == LogisticsShipmentStatus.Planned
                        || (shipment.Status == LogisticsShipmentStatus.Loading && !shipment.loadApplied)
                        || shipment.Status == LogisticsShipmentStatus.Arrived
                        || shipment.Status == LogisticsShipmentStatus.Unloading
                        || (shipment.Status == LogisticsShipmentStatus.Partial && !shipment.deliveryApplied))
                        waiting++;
                }
                if (waiting > 0)
                    diagnostics.Add($"FreightProductionDriver: {waiting} shipment(s) wait for a handler — " +
                        "the carrier has no owner and no active employees, so loading and unloading cannot start. " +
                        "In-transit shipments still advance and revenue still collects; nothing is dispatched without a real handler.");
            }

            foreach (LogisticsShipmentState shipment in AllShipments())
            {
                if (shipment == null || !FreightShipmentService.IsLiveShipment(shipment))
                    continue;
                if (shipment.requiresDispatchAuthorization)
                    continue;
                // Slice S10: a shipment whose wagon is off-map waits — the
                // team is away on the Millbrook haul, so the lot must not
                // load in town while the journey record says it is out on
                // Millbrook Road. The hold lifts automatically when the
                // journey returns (the predicate then answers false).
                if (hooks.IsVehicleOffMap != null && hooks.IsVehicleOffMap(shipment.physicalWagonAssetId))
                    continue;
                LogisticsShipmentStatus before = shipment.Status;
                string reason;
                switch (shipment.Status)
                {
                    case LogisticsShipmentStatus.Planned:
                        if (handlerId.IsValid)
                            service.BeginLoading(shipment, handlerId, hooks, out reason);
                        break;
                    case LogisticsShipmentStatus.Loading:
                        if (shipment.loadApplied)
                        {
                            TryDispatch(shipment, hooks, diagnostics);
                        }
                        else if (handlerId.IsValid)
                        {
                            service.WorkLoading(shipment, minutes, hooks, out reason);
                            if (shipment.loadApplied)
                                TryDispatch(shipment, hooks, diagnostics);
                        }
                        break;
                    case LogisticsShipmentStatus.InTransit:
                        service.AdvanceTransit(shipment, Math.Max(0f, gameSeconds), hooks);
                        break;
                    case LogisticsShipmentStatus.Arrived:
                        if (handlerId.IsValid)
                            service.BeginUnloading(shipment, handlerId, hooks, out reason);
                        break;
                    case LogisticsShipmentStatus.Unloading:
                        if (handlerId.IsValid)
                            service.WorkUnloading(shipment, minutes, hooks, out reason);
                        break;
                    case LogisticsShipmentStatus.Partial:
                        // A partial remainder may be re-offered to the
                        // recipient; the service refuses when inappropriate.
                        if (handlerId.IsValid && !shipment.deliveryApplied)
                            service.BeginUnloading(shipment, handlerId, hooks, out reason);
                        break;
                    case LogisticsShipmentStatus.Delayed:
                        // Honest unresolved state: cargo stays in custody.
                        break;
                }
                if (shipment.Status != before || shipment.loadApplied)
                    advanced++;
            }

            // Settlement: exactly the completed-and-accepted shipments, once
            // each (the runtime's collected flag makes re-scans no-ops). No
            // revenue ever settles for cargo that was not accepted.
            // R-04: VERIFIED collection only — each shipment's named payer
            // is resolved to its runtime and debited before the carrier is
            // credited. An unresolvable payer leaves the charge uncollected
            // (no legacy unverified fallback, no phantom carrier credit).
            foreach (KeyValuePair<string, FreightCompanyRuntime> entry in companies)
            {
                businessRuntimes.TryGetValue(entry.Key, out BusinessRuntimeState runtime);
                entry.Value.CollectAndPostUncollectedRevenue(runtime, ResolvePayerRuntime, diagnostics);
            }
            return advanced;
        }

        private void TryDispatch(LogisticsShipmentState shipment, FreightShipmentHooks hooks, List<string> diagnostics)
        {
            if (!dispatchPlans.TryGetValue(shipment.ShipmentId, out DispatchPlan plan) || plan == null)
            {
                diagnostics.Add($"FreightProductionDriver: shipment '{shipment.ShipmentId}' is loaded but has " +
                    "no dispatch plan (real driver + animals) — it waits; capacity is never invented.");
                return;
            }
            if (!service.Dispatch(shipment, plan.DriverId, plan.AnimalIds, hooks, out string reason))
                diagnostics.Add($"FreightProductionDriver: dispatch of '{shipment.ShipmentId}' refused — {reason}");
        }

        // ---------- save / load ----------

        /// <summary>Captures every company's shipments through the service DTO.</summary>
        public FreightShipmentService.FreightServiceSaveDto CaptureSaveDto()
        {
            FreightShipmentService.FreightServiceSaveDto dto = service.CaptureSaveDto(AllShipments());
            dto.ServiceAgreements = serviceAgreements.CaptureSaveDto();
            return dto;
        }

        /// <summary>
        /// Restores shipments exactly once: each company's list is rebuilt
        /// (cleared, then re-filled from the DTO) and the service re-tracks
        /// the restored states, so a mid-transport restore neither loses
        /// nor duplicates cargo. Dispatch plans are re-derived from the
        /// restored shipments' bound driver/animals.
        /// </summary>
        public List<LogisticsShipmentState> RestoreSaveDto(
            FreightShipmentService.FreightServiceSaveDto dto,
            FreightShipmentHooks hooks,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            serviceAgreements.RestoreSaveDto(dto != null ? dto.ServiceAgreements : null);
            List<LogisticsShipmentState> restored = service.RestoreSaveDto(dto, hooks);

            var byCarrier = new Dictionary<string, List<LogisticsShipmentState>>(StringComparer.Ordinal);
            foreach (LogisticsShipmentState shipment in restored)
            {
                if (shipment == null)
                    continue;
                string carrier = shipment.CarrierBusinessInstanceId ?? string.Empty;
                if (!byCarrier.TryGetValue(carrier, out List<LogisticsShipmentState> list))
                {
                    list = new List<LogisticsShipmentState>();
                    byCarrier[carrier] = list;
                }
                list.Add(shipment);

                // Rebuild the dispatch plan from the shipment's own bound
                // transport, so an in-transit restore can finish its route.
                if (shipment.physicalTransportBound
                    && !string.IsNullOrWhiteSpace(shipment.physicalDriverPersonId))
                {
                    var animals = new List<EntityId>();
                    if (shipment.physicalDraftAnimalIds != null)
                    {
                        foreach (string animalText in shipment.physicalDraftAnimalIds)
                        {
                            if (TryParseWithKind(animalText, EntityKind.Animal, out EntityId animal))
                                animals.Add(animal);
                        }
                    }
                    if (TryParseWithKind(shipment.physicalDriverPersonId, EntityKind.Person, out EntityId driver))
                        dispatchPlans[shipment.ShipmentId] = new DispatchPlan { DriverId = driver, AnimalIds = animals };
                }
            }

            foreach (KeyValuePair<string, List<LogisticsShipmentState>> entry in byCarrier)
            {
                FreightCompanyRuntime company = EnsureCompany(entry.Key);
                company?.RestoreActiveShipments(entry.Value);
            }
            // Companies with no restored shipments still clear stale lists.
            foreach (KeyValuePair<string, FreightCompanyRuntime> entry in companies)
            {
                if (!byCarrier.ContainsKey(entry.Key))
                    entry.Value.RestoreActiveShipments(null);
            }
            return restored;
        }

        /// <summary>Parses an EntityId printed by EntityId.ToString for a known kind ("P702", "A11").</summary>
        private static bool TryParseWithKind(string text, EntityKind kind, out EntityId id)
        {
            id = EntityId.Invalid;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!EntityKindCatalog.TryGetPrefix(kind, out string prefix))
                return false;
            string trimmed = text.Trim();
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            if (!int.TryParse(trimmed.Substring(prefix.Length), out int sequence) || sequence < 0)
                return false;
            id = EntityId.For(kind, sequence);
            return true;
        }
    }
}
