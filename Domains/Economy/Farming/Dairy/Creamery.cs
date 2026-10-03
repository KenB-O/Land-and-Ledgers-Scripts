using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Dairy
{
    /// <summary>
    /// T1G: one farm's daily milk delivery to the creamery. Milk is perishable —
    /// deliveries arrive daily or not at all. Each delivery keeps its farm's identity;
    /// pooling happens at processing, not at the door.
    /// </summary>
    [Serializable]
    public sealed class CreameryDelivery
    {
        public EntityId DeliveryId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FarmId = string.Empty;
        public int DayIndex;
        public List<EntityId> MilkLotIds = new List<EntityId>(); // per-lot provenance
        public int AcceptedUnits;
        public int RejectedUnits; // sour/spoiled — refused at the door, never pooled
        public int PaidCents;
        public string Notes = string.Empty;

        public CreameryDelivery() { }
    }

    /// <summary>
    /// T1G: the creamery — a real business aggregating milk from surrounding farms into
    /// cheese at scale. Explicitly NOT one-click milk-to-value (Canon §9.6): intake is
    /// daily and perishable, sour milk is rejected (never pooled), cheese is made through
    /// the SWN-2 CheeseChain with equipment + named rennet source, and wheels cure for
    /// real days. The creamery is dairy's second commercial outlet: a farm can retail
    /// fresh milk/butter itself, or sell milk to the creamery.
    /// </summary>
    public sealed class Creamery
    {
        public string CreameryBusinessId = string.Empty;
        public string CreameryName = string.Empty;
        public int PricePerMilkUnitCents = 3; // calibration: what the creamery pays patrons
        public int MaxIntakeUnitsPerDay = 500; // physical capacity — vats are finite
        public string Equipment = string.Empty; // required by CheeseChain (vats, press)
        public string RennetSource = string.Empty; // required by CheeseChain (usually the butcher)

        private readonly List<CreameryDelivery> deliveries = new List<CreameryDelivery>();
        private int intakeToday;
        private int intakeDay = -1;

        public Creamery() { }

        public Creamery(string creameryBusinessId, string creameryName)
        {
            CreameryBusinessId = creameryBusinessId ?? string.Empty;
            CreameryName = creameryName ?? string.Empty;
        }

        public IReadOnlyList<CreameryDelivery> Deliveries => deliveries;

        private void RollDay(int dayIndex)
        {
            if (dayIndex != intakeDay)
            {
                intakeDay = dayIndex;
                intakeToday = 0;
            }
        }

        /// <summary>
        /// Accepts one farm's delivery. Fresh units are bought at the patron price;
        /// sour/spoiled units are rejected at the door and NEVER enter the pool.
        /// Returns the delivery record, or null with diagnostics on refusal.
        /// </summary>
        public CreameryDelivery AcceptDelivery(
            EntityIdRegistry idRegistry,
            string farmId,
            List<MilkLot> milkLots,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || string.IsNullOrWhiteSpace(farmId))
            {
                diagnostics.Add("Creamery: need a registry and a named farm — no anonymous milk.");
                return null;
            }

            RollDay(dayIndex);
            var delivery = new CreameryDelivery
            {
                DeliveryId = idRegistry.Allocate(EntityKind.Lot),
                FarmId = farmId,
                DayIndex = dayIndex,
            };

            if (milkLots == null || milkLots.Count == 0)
            {
                diagnostics.Add($"Creamery: farm '{farmId}' delivered no milk.");
                return delivery;
            }

            foreach (MilkLot lot in milkLots)
            {
                if (lot == null) continue;
                delivery.MilkLotIds.Add(lot.LotId);

                int saleable = lot.SaleableUnits(dayIndex);
                if (saleable <= 0)
                {
                    // Sour milk is rejected, not pooled. The farm keeps its loss.
                    int spoiled = Math.Max(0, lot.QuantityUnits);
                    delivery.RejectedUnits += spoiled;
                    diagnostics.Add($"Creamery: rejected {spoiled} spoiled units from farm '{farmId}' — sour milk never enters the vat (Canon §9.1).");
                    continue;
                }

                int room = Math.Max(0, MaxIntakeUnitsPerDay - intakeToday);
                int accepted = Math.Min(saleable, room);
                if (accepted < saleable)
                {
                    delivery.RejectedUnits += saleable - accepted;
                    diagnostics.Add($"Creamery: at daily capacity — {saleable - accepted} units from farm '{farmId}' turned away (finite vats).");
                }

                delivery.AcceptedUnits += accepted;
                intakeToday += accepted;
                lot.QuantityUnits -= accepted; // taken into the creamery pool
            }

            delivery.PaidCents = delivery.AcceptedUnits * Math.Max(0, PricePerMilkUnitCents);
            deliveries.Add(delivery);

            diagnostics.Add($"Creamery {CreameryName}: accepted {delivery.AcceptedUnits} units from '{farmId}', paid {delivery.PaidCents}c, rejected {delivery.RejectedUnits}.");
            return delivery;
        }

        /// <summary>
        /// Makes cheese from today's accepted pool through the SWN-2 chain. The caller
        /// passes the accepted lots back in (re-materialized from delivery records);
        /// wheels carry the creamery as maker and each contributing farm in their notes.
        /// </summary>
        public List<CheeseWheel> ProcessDay(
            EntityIdRegistry idRegistry,
            CheeseChain chain,
            List<MilkLot> pooledLots,
            EntityId workerId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || chain == null)
            {
                diagnostics.Add("Creamery: need a registry and a cheese chain.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(Equipment))
            {
                diagnostics.Add("Creamery: no vats/press named — cheese needs equipment (Canon §9.6).");
                return null;
            }

            if (string.IsNullOrWhiteSpace(RennetSource))
            {
                diagnostics.Add("Creamery: rennet needs a named source — no orphan inputs.");
                return null;
            }

            List<CheeseWheel> wheels = chain.MakeCheese(
                idRegistry, pooledLots, workerId, CreameryBusinessId, dayIndex,
                Equipment, RennetSource, diagnostics);

            if (wheels != null)
            {
                diagnostics.Add($"Creamery {CreameryName}: made {wheels.Count} wheels from today's pool (milk pooled from patron farms; provenance in each wheel's MilkLotIds).");
            }

            return wheels;
        }
    }
}
