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
        /// </summary>
        public int CollectFreightCharge(string shipmentId, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            LogisticsShipmentState shipment = FindShipment(shipmentId);
            if (shipment == null)
            {
                diagnostics.Add($"Unknown shipment '{shipmentId}'.");
                return 0;
            }

            if (shipment.Status != LogisticsShipmentStatus.Completed)
            {
                diagnostics.Add($"Shipment '{shipmentId}' is {shipment.Status}; " +
                    "freight charges collect only on completed delivery.");
                return 0;
            }

            if (string.IsNullOrWhiteSpace(shipment.FreightPayerBusinessInstanceId)
                || shipment.FreightPayerBusinessInstanceId == businessInstanceId)
            {
                // No external payer (or self) — no revenue event. Internal work never
                // fabricates revenue to the same enterprise (Canon §3.6).
                diagnostics.Add($"Shipment '{shipmentId}' has no external freight payer; " +
                    "no revenue recorded (Canon §3.6).");
                return 0;
            }

            diagnostics.Add($"Collected {shipment.FreightChargeCents}c freight charge for '{shipmentId}' " +
                $"from {shipment.FreightPayerBusinessInstanceId} (external payer).");
            return shipment.FreightChargeCents;
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
