using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
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

        public FreightProductionDriver(FreightShipmentService service = null)
        {
            this.service = service ?? new FreightShipmentService();
        }

        /// <summary>The detailed-shipment system of record (A4-12).</summary>
        public FreightShipmentService Service => service;

        public IReadOnlyDictionary<string, FreightCompanyRuntime> Companies => companies;

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
            out string reason)
        {
            reason = string.Empty;
            diagnostics = diagnostics ?? new List<string>();
            FreightCompanyRuntime company = EnsureCompany(carrierBusinessId);
            if (company == null)
            {
                reason = "FreightProductionDriver: a carrier business id is required.";
                return null;
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

            shipment.freightChargeCents = Math.Max(0, freightChargeCents);
            shipment.freightPayerBusinessInstanceId = freightPayerBusinessId ?? string.Empty;
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
            return service.CaptureSaveDto(AllShipments());
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
