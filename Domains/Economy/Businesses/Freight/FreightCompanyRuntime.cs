using EntityId = LandLedgers.Primitives.EntityId;

using System;
using LandLedgers.Economy.Equipment;
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Freight
{
    /// <summary>
    /// BIZ-3: the freight company as a multi-unit enterprise (Canon §3.6) —
    /// freight-hauling + wagon-repair under ONE ledger sharing ONE physical pool.
    /// Internal work consumes time and materials and NEVER fabricates revenue to the
    /// same enterprise: repair work is a cost allocation, not income.
    ///
    /// Shipments honor MR-P001 conservation: cargo moves only through the shipment's
    /// own load/transit/unload stages; blocked shipments hold zero cargo and are never
    /// "completed" by fiat. Tasks flow through TTS-2; loading/unloading use the TTS-5
    /// freight math (per-unit rate × quantity).
    /// </summary>
    [Serializable]
    public sealed class FreightCompanyRuntime
    {
        // TTS-5 freight math, reused here so loading/unloading scale identically.
        public const int StandardUnitsPerMinute = 20;
        public const int FragileUnitsPerMinute = 15;
        public const int PerishableUnitsPerMinute = 12;

        public const string LoadFreightTaskId = "fr.load-freight";
        public const string DriveRouteTaskId = "fr.drive-route";
        public const string UnloadFreightTaskId = "fr.unload-freight";
        public const string RepairWagonTaskId = "fr.repair-wagon";

        /// <summary>
        /// G-02: revenue category for posted freight charges. Mirrors the
        /// canonical "freight_service" category used by the shared business
        /// runtime's LiveryFreight branch.
        /// </summary>
        public const string FreightRevenueCategoryId = "freight_service";

        [SerializeField]
        private string businessInstanceId = string.Empty;

        [SerializeField]
        private FreightResourcePool resourcePool = new FreightResourcePool();

        [SerializeField]
        private List<FreightRoute> routes = new List<FreightRoute>();

        [SerializeField]
        private List<LogisticsShipmentState> activeShipments = new List<LogisticsShipmentState>();

        [SerializeField, Min(0)]
        private int internalRepairCostCents;

        [SerializeField]
        private List<string> ledgerNotes = new List<string>();

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public FreightResourcePool ResourcePool => resourcePool ??= new FreightResourcePool();
        public IReadOnlyList<FreightRoute> Routes => routes;
        public IReadOnlyList<LogisticsShipmentState> ActiveShipments => activeShipments;

        /// <summary>
        /// Internal cost allocation only — repair work is a COST to the enterprise,
        /// never revenue to itself (Canon §3.6). Real revenue comes only from freight
        /// charges paid by external payers.
        /// </summary>
        public int InternalRepairCostCents => Mathf.Max(0, internalRepairCostCents);

        public FreightCompanyRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        /// <summary>
        /// P1: static so the simulation hub can register freight tasks at boot
        /// (no instance required; duplicates are rejected by the authority).
        /// Registers the freight task definitions with a TTS-2 authority.
        /// </summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null)
            {
                return;
            }

            RegisterQuietly(authority, new TaskDefinition(LoadFreightTaskId, "Load freight", 1));
            RegisterQuietly(authority, new TaskDefinition(DriveRouteTaskId, "Drive route", 60));
            RegisterQuietly(authority, new TaskDefinition(UnloadFreightTaskId, "Unload freight", 1));
            // NX-1A: wagon repair needs the wheelwright's hand tools (Canon 4.1;
            // the kit exists in ToolKitCatalog). The wagon + team are reserved
            // through the freight pool, not the equipment gate.
            var repairWagon = new TaskDefinition(RepairWagonTaskId, "Repair wagon", 120);
            repairWagon.EquipmentClasses.Add(EquipmentRequirementCodes.Kit("wheelwright-kit"));
            RegisterQuietly(authority, repairWagon);
        }

        private static void RegisterQuietly(TaskAuthority authority, TaskDefinition definition)
        {
            authority.RegisterDefinition(definition, out _);
        }

        public void AddRoute(FreightRoute route)
        {
            routes ??= new List<FreightRoute>();
            if (route != null && !string.IsNullOrWhiteSpace(route.RouteId))
            {
                routes.Add(route);
            }
        }

        /// <summary>
        /// Accepts a shipment for hauling. MR-P001: pre-load blocked shipments (zero
        /// cargo, loading never occurred) are refused — the company cannot "haul"
        /// phantom cargo.
        /// </summary>
        public bool TryAcceptShipment(
            LogisticsShipmentState shipment,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            activeShipments ??= new List<LogisticsShipmentState>();

            if (shipment == null)
            {
                diagnostics.Add("No shipment to accept.");
                return false;
            }

            if (shipment.IsPreLoadBlocked)
            {
                diagnostics.Add($"Shipment '{shipment.ShipmentId}' is pre-load blocked (zero cargo) — " +
                    "refused; phantom cargo is never hauled (MR-P001).");
                return false;
            }

            shipment.carrierBusinessInstanceId = businessInstanceId;
            activeShipments.Add(shipment);
            diagnostics.Add($"Accepted shipment '{shipment.ShipmentId}' " +
                $"({shipment.PlannedQuantityUnits} units, charge {shipment.FreightChargeCents}c).");
            return true;
        }

        /// <summary>
        /// Dispatches an accepted shipment: reserves a team from the shared pool and
        /// enqueues the loading task. Fails loudly when no team is available — the
        /// shipment waits; the company never invents capacity.
        /// </summary>
        public bool TryDispatchShipment(
            string shipmentId,
            TaskAuthority taskAuthority,
            EntityId businessEntityId,
            int currentDayIndex,
            List<EntityId> availableDraftAnimals,
            List<EntityId> availableDrivers,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();

            LogisticsShipmentState shipment = FindShipment(shipmentId);
            if (shipment == null)
            {
                diagnostics.Add($"Unknown shipment '{shipmentId}'.");
                return false;
            }

            if (shipment.Status != LogisticsShipmentStatus.Planned)
            {
                diagnostics.Add($"Shipment '{shipmentId}' is {shipment.Status}; only planned shipments dispatch.");
                return false;
            }

            if (!ResourcePool.TryReserveTeam(
                shipment.PlannedQuantityUnits, availableDraftAnimals, availableDrivers,
                $"haul {shipmentId}", out DraftTeamReservation team, diagnostics))
            {
                return false;
            }

            // Loading task: TTS-5 freight math — ceil(quantity / unitsPerMinute), min 1.
            int minutes = FreightMinutes(shipment.PlannedQuantityUnits, ToGoodsClass(shipment.CargoClass));
            if (taskAuthority != null)
            {
                taskAuthority.CreateTaskWithPlannedMinutes(
                    LoadFreightTaskId, businessEntityId, currentDayIndex, minutes, shipmentId);
            }

            diagnostics.Add($"Dispatched '{shipmentId}': team {team.ReservationId}, " +
                $"loading task {minutes} min (TTS-5 math).");
            return true;
        }

        /// <summary>
        /// Advances all active shipments by game seconds. Delegates to each shipment's
        /// own stage machine (MR-P001) — the company never teleports cargo.
        /// </summary>
        public void AdvanceGameSeconds(float gameSeconds)
        {
            if (activeShipments == null)
            {
                return;
            }

            foreach (LogisticsShipmentState shipment in activeShipments)
            {
                shipment.AdvanceGameSeconds(gameSeconds);
            }
        }

        /// <summary>
        /// Completes a repair job: internal COST allocation (materials + labor time),
        /// never revenue to the enterprise itself (Canon §3.6).
        /// </summary>
        public bool TryCompleteRepairJob(
            string wagonId,
            int materialsCostCents,
            int laborMinutes,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();

            if (!ResourcePool.TryCompleteRepair(wagonId, 0.5f, diagnostics))
            {
                return false;
            }

            internalRepairCostCents += Mathf.Max(0, materialsCostCents);
            string note = $"Repair '{wagonId}': {Mathf.Max(0, materialsCostCents)}c materials + " +
                $"{Mathf.Max(0, laborMinutes)} min labor — internal COST, not revenue (Canon §3.6).";
            ledgerNotes ??= new List<string>();
            ledgerNotes.Add(note);
            diagnostics.Add(note);
            return true;
        }

        /// <summary>
        /// Records freight revenue — ONLY from the external freight payer named on the
        /// shipment. Returns the charge; the caller posts it to the business ledger
        /// with provenance (Canon XIII §13.2).
        /// Pass IX WS1 (A3-04): this legacy overload only NAMES the payer; it
        /// cannot verify a debit. Production collection must use
        /// <see cref="CollectFreightCharge(string, BusinessRuntimeState, List{string})"/>
        /// (or the resolver overload of CollectAndPostUncollectedRevenue),
        /// which debits the named payer's till BEFORE revenue may post.
        /// </summary>
        public int CollectFreightCharge(string shipmentId, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            LogisticsShipmentState shipment = FindShipment(shipmentId);
            if (!TryValidateCollectable(shipment, shipmentId, diagnostics))
            {
                return 0;
            }

            // G-02: idempotent via the collected flag — a second call for the
            // same shipment finds FreightRevenueCollected and is never
            // rescanned, so the charge posts exactly once.
            shipment.freightRevenueCollected = true;
            diagnostics.Add($"Collected {shipment.FreightChargeCents}c freight charge for '{shipmentId}' " +
                $"from {shipment.FreightPayerBusinessInstanceId} (external payer).");
            return shipment.FreightChargeCents;
        }

        /// <summary>
        /// Pass IX WS1 (A3-04): verified collection. The named payer's till is
        /// debited FIRST (BusinessRuntimeState.TrySpendCents — the business
        /// cash writer); only a successful debit marks the charge collected
        /// and returns it for revenue posting. When the payer till is missing,
        /// is not the named payer, or cannot cover the charge, NOTHING is
        /// collected and the shipment stays uncollected — revenue never posts
        /// for a payer who was not actually debited.
        /// </summary>
        public int CollectFreightCharge(string shipmentId, BusinessRuntimeState payerRuntime, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            LogisticsShipmentState shipment = FindShipment(shipmentId);
            if (!TryValidateCollectable(shipment, shipmentId, diagnostics))
            {
                return 0;
            }

            if (payerRuntime == null
                || !string.Equals(payerRuntime.BusinessId, shipment.FreightPayerBusinessInstanceId, StringComparison.Ordinal))
            {
                diagnostics.Add($"Shipment '{shipmentId}': payer till for '{shipment.FreightPayerBusinessInstanceId}' " +
                    "is unavailable or does not name the freight payer — charge NOT collected.");
                return 0;
            }

            if (!payerRuntime.TrySpendCents(shipment.FreightChargeCents))
            {
                diagnostics.Add($"Shipment '{shipmentId}': payer '{shipment.FreightPayerBusinessInstanceId}' holds " +
                    $"{payerRuntime.CurrentCashCents}c, charge is {shipment.FreightChargeCents}c — charge NOT collected, no revenue posts.");
                return 0;
            }

            shipment.freightRevenueCollected = true;
            diagnostics.Add($"Collected {shipment.FreightChargeCents}c freight charge for '{shipmentId}' " +
                $"from {shipment.FreightPayerBusinessInstanceId} (payer debited first, verified).");
            return shipment.FreightChargeCents;
        }

        private bool TryValidateCollectable(LogisticsShipmentState shipment, string shipmentId, List<string> diagnostics)
        {
            if (shipment == null)
            {
                diagnostics.Add($"Unknown shipment '{shipmentId}'.");
                return false;
            }

            if (shipment.Status != LogisticsShipmentStatus.Completed || !shipment.deliveryApplied)
            {
                diagnostics.Add($"Shipment '{shipmentId}' is {shipment.Status}; " +
                    "freight charges collect only on completed delivery.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(shipment.FreightPayerBusinessInstanceId)
                || shipment.FreightPayerBusinessInstanceId == businessInstanceId)
            {
                // No external payer (or self) — no revenue event. Internal work never
                // fabricates revenue to the same enterprise (Canon §3.6).
                diagnostics.Add($"Shipment '{shipmentId}' has no external freight payer; " +
                    "no revenue recorded (Canon §3.6).");
                return false;
            }

            if (shipment.FreightRevenueCollected)
            {
                diagnostics.Add($"Shipment '{shipmentId}': freight charge already collected — never collected twice.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// G-02: pure scan for completed-uncollected shipments. No mutation:
        /// returns the ids of shipments that are Completed with delivery
        /// applied and whose freight revenue has not been collected. The
        /// external-payer rule (Canon §3.6) is enforced by
        /// <see cref="CollectFreightCharge"/>, which yields zero for
        /// self-payer/missing-payer shipments.
        /// </summary>
        public List<string> FindUncollectedFreightChargeShipmentIds()
        {
            var ids = new List<string>();
            if (activeShipments == null)
                return ids;
            foreach (LogisticsShipmentState shipment in activeShipments)
            {
                if (shipment == null)
                    continue;
                if (shipment.Status == LogisticsShipmentStatus.Completed
                    && shipment.deliveryApplied
                    && !shipment.FreightRevenueCollected)
                    ids.Add(shipment.ShipmentId);
            }

            return ids;
        }

        /// <summary>
        /// G-02: revenue collection for the tick — scans for
        /// completed-uncollected shipments, calls <see cref="CollectFreightCharge"/>,
        /// and posts positive charges to the business ledger with provenance
        /// (Canon XIII §13.2). Exactly-once: the collected flag makes
        /// re-scans yield nothing. Returns total cents posted.
        /// </summary>
        public int CollectAndPostUncollectedRevenue(BusinessRuntimeState businessRuntime, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            int posted = 0;
            foreach (string shipmentId in FindUncollectedFreightChargeShipmentIds())
            {
                int cents = CollectFreightCharge(shipmentId, diagnostics);
                if (cents <= 0 || businessRuntime == null)
                    continue;
                businessRuntime.RecordDailyServiceRevenue(FreightRevenueCategoryId, 1, cents);
                posted += cents;
                diagnostics.Add($"FreightCompanyRuntime: {cents}c freight revenue posted to {businessInstanceId} " +
                    $"(provenance: freight charge, shipment '{shipmentId}').");
            }

            return posted;
        }

        /// <summary>
        /// Pass IX WS1 (A3-04): verified revenue collection for production —
        /// each shipment's named payer is resolved to its till and DEBITED
        /// through the verified CollectFreightCharge before any revenue posts
        /// to the carrier. Shipments whose payer cannot be resolved or cannot
        /// pay stay uncollected (retried on a later tick), never posted.
        /// Returns total cents posted; carrier credit == payer debit per
        /// shipment by construction.
        /// </summary>
        public int CollectAndPostUncollectedRevenue(
            BusinessRuntimeState businessRuntime,
            Func<string, BusinessRuntimeState> payerRuntimeResolver,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (payerRuntimeResolver == null)
            {
                diagnostics.Add("FreightCompanyRuntime: no payer resolver — verified collection cannot run; nothing posted.");
                return 0;
            }

            int posted = 0;
            foreach (string shipmentId in FindUncollectedFreightChargeShipmentIds())
            {
                LogisticsShipmentState shipment = FindShipment(shipmentId);
                if (shipment == null)
                    continue;
                BusinessRuntimeState payerRuntime = payerRuntimeResolver(shipment.FreightPayerBusinessInstanceId);
                int cents = CollectFreightCharge(shipmentId, payerRuntime, diagnostics);
                if (cents <= 0 || businessRuntime == null)
                    continue;
                businessRuntime.RecordDailyServiceRevenue(FreightRevenueCategoryId, 1, cents);
                posted += cents;
                diagnostics.Add($"FreightCompanyRuntime: {cents}c freight revenue posted to {businessInstanceId} " +
                    $"(provenance: freight charge, shipment '{shipmentId}', payer debited and verified).");
            }

            return posted;
        }

        /// <summary>
        /// TTS-5 freight math, shared: ceil(quantity / unitsPerMinute), min 1.
        /// Slowest rate wins for fragile/perishable goods.
        /// </summary>
        public static int FreightMinutes(int quantityUnits, FreightGoodsClass goodsClass)
        {
            int unitsPerMinute = goodsClass switch
            {
                FreightGoodsClass.Fragile => FragileUnitsPerMinute,
                FreightGoodsClass.Perishable => PerishableUnitsPerMinute,
                FreightGoodsClass.FragileAndPerishable => Mathf.Min(FragileUnitsPerMinute, PerishableUnitsPerMinute),
                _ => StandardUnitsPerMinute,
            };

            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, quantityUnits) / (float)unitsPerMinute));
        }

        /// <summary>Maps a shipment's cargo class onto the TTS-5 goods classes.</summary>
        public static FreightGoodsClass ToGoodsClass(LogisticsCargoClass cargoClass)
        {
            return cargoClass switch
            {
                LogisticsCargoClass.Perishables => FreightGoodsClass.Perishable,
                LogisticsCargoClass.LiveAnimals => FreightGoodsClass.Perishable,
                _ => FreightGoodsClass.Standard,
            };
        }

        private LogisticsShipmentState FindShipment(string shipmentId)
        {
            if (activeShipments == null || string.IsNullOrWhiteSpace(shipmentId))
            {
                return null;
            }

            foreach (LogisticsShipmentState shipment in activeShipments)
            {
                if (shipment.ShipmentId == shipmentId)
                {
                    return shipment;
                }
            }

            return null;
        }
    }
}
