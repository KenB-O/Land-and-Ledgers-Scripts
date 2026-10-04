using EntityId = LandLedgers.Primitives.EntityId;

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

        // D3F: patron farms (real farms, named), the per-day vat pools, and the
        // creamery's own production-day records. Runtime lists; save via CaptureSaveDto.
        private readonly List<CreameryPatron> patrons = new List<CreameryPatron>();
        private readonly List<CreameryPoolDay> poolDays = new List<CreameryPoolDay>();
        private readonly List<CreameryBatchRecord> batchRecords = new List<CreameryBatchRecord>();

        public Creamery() { }

        public Creamery(string creameryBusinessId, string creameryName)
        {
            CreameryBusinessId = creameryBusinessId ?? string.Empty;
            CreameryName = creameryName ?? string.Empty;
        }

        public IReadOnlyList<CreameryDelivery> Deliveries => deliveries;

        /// <summary>D3F: the creamery's named patron farms.</summary>
        public IReadOnlyList<CreameryPatron> Patrons => patrons;

        /// <summary>D3F: per-day vat pools (accepted vs consumed).</summary>
        public IReadOnlyList<CreameryPoolDay> PoolDays => poolDays;

        /// <summary>D3F: the creamery's auditable production-day records.</summary>
        public IReadOnlyList<CreameryBatchRecord> Batches => batchRecords;

        /// <summary>
        /// D3F: names a patron farm ahead of (or after) its first delivery. The owner
        /// person is a real EntityKind.Person. Refused loudly for a blank farm id;
        /// re-registering updates the name/owner rather than duplicating the farm.
        /// </summary>
        public CreameryPatron RegisterPatron(
            string farmId, string displayName, EntityId ownerPersonId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (string.IsNullOrWhiteSpace(farmId))
            {
                diagnostics.Add("Creamery: cannot register a patron with no farm id — patrons are real farms.");
                return null;
            }

            CreameryPatron patron = GetPatron(farmId);
            if (patron == null)
            {
                patron = new CreameryPatron
                {
                    FarmId = farmId,
                    FirstDeliveryDayIndex = dayIndex,
                };
                patrons.Add(patron);
            }

            if (!string.IsNullOrWhiteSpace(displayName)) patron.DisplayName = displayName;
            if (ownerPersonId.IsValid && ownerPersonId.Kind == EntityKind.Person)
                patron.OwnerPersonId = ownerPersonId;
            else if (ownerPersonId.IsValid)
                diagnostics.Add($"Creamery: owner {ownerPersonId} for farm '{farmId}' is not a person — owner left unnamed, not guessed.");

            diagnostics.Add($"Creamery {CreameryName}: patron '{farmId}' registered" +
                (patron.OwnerPersonId.IsValid ? $" (owner {patron.OwnerPersonId})" : " (owner unnamed)") + ".");
            return patron;
        }

        /// <summary>D3F: finds a patron record by farm id, or null.</summary>
        public CreameryPatron GetPatron(string farmId)
        {
            if (string.IsNullOrWhiteSpace(farmId)) return null;
            foreach (CreameryPatron p in patrons)
            {
                if (p != null && string.Equals(p.FarmId, farmId, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        /// <summary>D3F: the vat pool for a day, created on demand.</summary>
        public CreameryPoolDay GetPoolDay(int dayIndex)
        {
            foreach (CreameryPoolDay pool in poolDays)
            {
                if (pool != null && pool.DayIndex == dayIndex) return pool;
            }
            var created = new CreameryPoolDay { DayIndex = dayIndex };
            poolDays.Add(created);
            return created;
        }

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

            // D3F: the farm is now a named patron (auto-registered on first delivery),
            // and accepted milk enters the day's vat pool. PaidCents is the delivery's
            // accrual memo, not cash: the monthly milk check (W10B) is the sole
            // settlement instrument — the creamery never pays twice for one delivery.
            CreameryPatron patron = RegisterPatron(farmId, farmId, EntityId.Invalid, dayIndex, diagnostics);
            if (patron != null)
            {
                patron.TotalAcceptedUnits += delivery.AcceptedUnits;
                patron.TotalRejectedUnits += delivery.RejectedUnits;
            }

            CreameryPoolDay pool = GetPoolDay(dayIndex);
            pool.AcceptedUnits += delivery.AcceptedUnits;
            pool.PoolLotIds.AddRange(delivery.MilkLotIds);

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

            // D3F: upstream-provenance check — the creamery can only process milk it
            // actually accepted into the day's pool. The caller re-materializes the
            // pooled lots (unit counts, not lot identity); anything the pool doesn't
            // hold is refused loudly, never conjured, and milk already processed is
            // never processed twice.
            int requestedFresh = 0;
            if (pooledLots != null)
            {
                foreach (MilkLot pooled in pooledLots)
                {
                    if (pooled != null) requestedFresh += pooled.SaleableUnits(dayIndex);
                }
            }

            CreameryPoolDay vat = GetPoolDay(dayIndex);
            if (vat.RemainingUnits <= 0)
            {
                diagnostics.Add($"Creamery: no milk in the day-{dayIndex} vat pool — process nothing, conjure nothing (upstream provenance).");
                return null;
            }
            if (requestedFresh > vat.RemainingUnits)
            {
                diagnostics.Add($"Creamery: pool holds {vat.RemainingUnits} unprocessed units for day {dayIndex}, " +
                    $"but {requestedFresh} were offered for processing — refused: milk the creamery never received cannot become cheese.");
                return null;
            }

            List<CheeseWheel> wheels = chain.MakeCheese(
                idRegistry, pooledLots, workerId, CreameryBusinessId, dayIndex,
                Equipment, RennetSource, diagnostics);

            if (wheels != null)
            {
                vat.ConsumedUnits += requestedFresh;

                var batch = new CreameryBatchRecord
                {
                    DayIndex = dayIndex,
                    WorkerId = workerId,
                    InputMilkUnits = requestedFresh,
                    WheyUnits = (requestedFresh * CheeseChain.WheyUnitsPer10Milk) / 10,
                    Notes = $"made by {CreameryName} (business {CreameryBusinessId})",
                };
                if (pooledLots != null)
                {
                    foreach (MilkLot pooled in pooledLots)
                    {
                        if (pooled != null) batch.InputMilkLotIds.Add(pooled.LotId);
                    }
                }
                foreach (CheeseWheel wheel in wheels)
                {
                    if (wheel != null) batch.WheelLotIds.Add(wheel.LotId);
                }
                batchRecords.Add(batch);

                diagnostics.Add($"Creamery {CreameryName}: made {wheels.Count} wheels from today's pool (milk pooled from patron farms; provenance in each wheel's MilkLotIds). " +
                    $"Pool: {vat.RemainingUnits} units remain unprocessed for day {dayIndex}.");
            }

            return wheels;
        }

        /// <summary>D3F: save support (CLN-1 pattern). Deliveries are the settlement
        /// basis for W10B monthly statements — they must survive a save, along with
        /// patrons, vat pools, and batch records.</summary>
        public CreamerySaveDto CaptureSaveDto()
        {
            return new CreamerySaveDto
            {
                creameryBusinessId = CreameryBusinessId,
                creameryName = CreameryName,
                pricePerMilkUnitCents = PricePerMilkUnitCents,
                maxIntakeUnitsPerDay = MaxIntakeUnitsPerDay,
                equipment = Equipment,
                rennetSource = RennetSource,
                intakeDay = intakeDay,
                intakeToday = intakeToday,
                deliveries = new List<CreameryDelivery>(deliveries),
                patrons = new List<CreameryPatron>(patrons),
                poolDays = new List<CreameryPoolDay>(poolDays),
                batchRecords = new List<CreameryBatchRecord>(batchRecords),
            };
        }

        /// <summary>D3F: save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(CreamerySaveDto dto)
        {
            deliveries.Clear();
            patrons.Clear();
            poolDays.Clear();
            batchRecords.Clear();
            intakeDay = -1;
            intakeToday = 0;
            if (dto == null) return;

            CreameryBusinessId = dto.creameryBusinessId ?? string.Empty;
            CreameryName = dto.creameryName ?? string.Empty;
            PricePerMilkUnitCents = dto.pricePerMilkUnitCents;
            MaxIntakeUnitsPerDay = dto.maxIntakeUnitsPerDay;
            Equipment = dto.equipment ?? string.Empty;
            RennetSource = dto.rennetSource ?? string.Empty;
            intakeDay = dto.intakeDay;
            intakeToday = Math.Max(0, dto.intakeToday);
            if (dto.deliveries != null) deliveries.AddRange(dto.deliveries);
            if (dto.patrons != null) patrons.AddRange(dto.patrons);
            if (dto.poolDays != null) poolDays.AddRange(dto.poolDays);
            if (dto.batchRecords != null) batchRecords.AddRange(dto.batchRecords);
        }
    }

    /// <summary>D3F: save DTO for the creamery (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class CreamerySaveDto
    {
        public string creameryBusinessId = string.Empty;
        public string creameryName = string.Empty;
        public int pricePerMilkUnitCents;
        public int maxIntakeUnitsPerDay;
        public string equipment = string.Empty;
        public string rennetSource = string.Empty;
        public int intakeDay = -1;
        public int intakeToday;
        public List<CreameryDelivery> deliveries = new List<CreameryDelivery>();
        public List<CreameryPatron> patrons = new List<CreameryPatron>();
        public List<CreameryPoolDay> poolDays = new List<CreameryPoolDay>();
        public List<CreameryBatchRecord> batchRecords = new List<CreameryBatchRecord>();
    }
}
