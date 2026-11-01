using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// D2B: custom (toll) sawing — the sawmill's second commercial form,
    /// mirroring the W5A gristmill's toll/merchant distinction (Canon R6:
    /// business form follows actual function — separable commercial forms
    /// are POLICY DATA, not different business types).
    ///
    /// DESIGN FORK (recorded, not guessed): the canon is silent on sawmill
    /// toll sawing — it neither describes nor forbids it. This pass mirrors
    /// the gristmill toll form because the codebase already implements it
    /// there; whether a given mill offers toll work, and at what rate, is
    /// scenario/policy data. The policy defaults to NOT offering toll
    /// sawing (both tolls zero) — custom work is refused until the mill
    /// names its terms. No sawmill toll rate is copied from the gristmill's
    /// 1/16 calibration (a grain figure, not a lumber one).
    ///
    /// Toll form: the customer's logs stay the customer's property in the
    /// mill's CUSTODY — never mill inventory. The customer keeps the lumber
    /// (minus the mill's toll); the mill takes a cash toll per log sawn
    /// and/or an in-kind share of the lumber. Toll lumber the mill keeps
    /// goes to the mill's own lumber yard; the customer's lumber and the
    /// slab/offcut byproducts wait in the pickup register for the customer
    /// (cf. W5A: toll byproducts belong to the customer, not mill
    /// inventory). The runtime never posts ledgers — it returns toll data
    /// on each run so the ledger authorities record the money.
    /// </summary>
    [Serializable]
    public sealed class SawmillTollPolicy
    {
        /// <summary>Cash toll per log sawn on a toll run (0 = no cash toll). Owed by the customer; posted by the ledger authority.</summary>
        public int TollCashCentsPerLog;

        /// <summary>
        /// Share of the toll run's lumber output the mill keeps in kind
        /// (0..1; 0 = no in-kind toll). The customer keeps the rest.
        /// </summary>
        public float TollInKindLumberShare01;

        public SawmillTollPolicy() { }

        public SawmillTollPolicy(int tollCashCentsPerLog, float tollInKindLumberShare01)
        {
            TollCashCentsPerLog = Math.Max(0, tollCashCentsPerLog);
            TollInKindLumberShare01 = Mathf.Clamp01(tollInKindLumberShare01);
        }

        /// <summary>True when the mill has named toll terms — custom sawing is offered.</summary>
        public bool OffersTollSawing => TollCashCentsPerLog > 0 || TollInKindLumberShare01 > 0f;

        /// <summary>
        /// The in-kind toll lumber units the mill keeps from a toll run
        /// producing `lumberUnits` of lumber. Rounded down; the customer
        /// keeps the rest.
        /// </summary>
        public int TollInKindUnits(int lumberUnits)
        {
            float share = Mathf.Clamp01(TollInKindLumberShare01);
            if (lumberUnits <= 0 || share <= 0f) return 0;
            return Math.Min(lumberUnits, (int)Math.Floor(lumberUnits * share));
        }
    }

    /// <summary>
    /// D2B: one toll-custody log lot. The customer's logs in the mill's
    /// custody for custom sawing — ownership never changes hands. Toll logs
    /// are not fungible with the mill's own log yard; they are sawn for the
    /// named customer only.
    /// </summary>
    [Serializable]
    public sealed class TollLogCustodyLot
    {
        public LogLot Lot; // the customer's log lot (provenance: stand, felling)
        public string CustomerId = string.Empty;   // farmer / household id — never anonymous
        public string CustomerName = string.Empty; // named customer — never anonymous
        public int ReceivedDayIndex;

        public TollLogCustodyLot() { }

        public string ProvenanceChain()
        {
            string lot = Lot != null ? Lot.LotId.ToString() : "no-lot";
            string stand = Lot != null ? Lot.StandId : string.Empty;
            return $"toll custody: log lot {lot} (stand {stand}) / customer {CustomerName} ({CustomerId}) / day {ReceivedDayIndex}";
        }
    }

    /// <summary>
    /// D2B: the mill's toll-log custody register — customer-owned logs held
    /// for custom sawing. Custody, never inventory: toll logs are never
    /// sawn as mill production, and mill logs are never sawn as toll.
    /// </summary>
    public sealed class SawmillTollLogStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<TollLogCustodyLot> custody = new List<TollLogCustodyLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<TollLogCustodyLot> CustodyLots => custody;

        public int TotalCustodyLogUnits
        {
            get
            {
                int total = 0;
                foreach (var entry in custody)
                    if (entry.Lot != null) total += Math.Max(0, entry.Lot.LogUnits);
                return total;
            }
        }

        /// <summary>
        /// Receives logs a customer brings for TOLL sawing — custody, not
        /// inventory. The customer must be named; the logs still need their
        /// timber-stand provenance (anonymous logs are refused, same as the
        /// mill's own yard). Returns the refusal, or null.
        /// </summary>
        public string ReceiveTollLogLot(LogLot lot, string customerId, string customerName, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "SawmillTollLogStock.ReceiveTollLogLot: no lot offered — logs are not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "SawmillTollLogStock.ReceiveTollLogLot: a log lot needs an EntityId — anonymous stock is refused.";
            if (lot.LogUnits <= 0)
                return "SawmillTollLogStock.ReceiveTollLogLot: a log lot needs positive log units.";
            if (string.IsNullOrWhiteSpace(lot.StandId))
            {
                diag.Add($"SawmillTollLogStock: REFUSED toll lot {lot.LotId} — no timber stand named. "
                    + "A customer's logs need the same provenance as the mill's own.");
                return "SawmillTollLogStock.ReceiveTollLogLot: no timber stand named — anonymous logs refused.";
            }
            if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(customerName))
                return $"SawmillTollLogStock: toll lot {lot.LotId} refused — the customer must be named (toll logs need an owner to return to).";
            foreach (var existing in custody)
            {
                if (existing.Lot != null && existing.Lot.LotId == lot.LotId)
                    return $"SawmillTollLogStock: toll lot {lot.LotId} already in custody — double intake refused.";
            }

            var entry = new TollLogCustodyLot
            {
                Lot = lot,
                CustomerId = customerId,
                CustomerName = customerName,
                ReceivedDayIndex = dayIndex,
            };
            custody.Add(entry);
            diag.Add($"SawmillTollLogStock: received TOLL logs — {entry.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Takes up to `logsWanted` logs from the named toll custody lot.
        /// Partial takes are allowed — the remainder stays in custody under
        /// the same customer and provenance. Returns null with a loud
        /// refusal when the lot is unknown or empty.
        /// </summary>
        public KeyValuePair<TollLogCustodyLot, int>? TryTakeTollLogs(string tollLotId, int logsWanted, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (logsWanted <= 0)
            {
                diag.Add("SawmillTollLogStock: no logs requested.");
                return null;
            }
            TollLogCustodyLot found = null;
            foreach (var entry in custody)
            {
                if (entry.Lot != null && string.Equals(entry.Lot.LotId.ToString(), tollLotId, StringComparison.Ordinal))
                {
                    found = entry;
                    break;
                }
            }
            if (found == null)
            {
                diag.Add($"SawmillTollLogStock: REFUSED — no toll custody lot '{tollLotId}'. Toll sawing needs the customer's named lot.");
                return null;
            }
            int available = Math.Max(0, found.Lot.LogUnits);
            if (available <= 0)
            {
                diag.Add($"SawmillTollLogStock: REFUSED — toll lot '{tollLotId}' is empty.");
                return null;
            }
            int take = Math.Min(logsWanted, available);
            found.Lot.LogUnits -= take;
            if (found.Lot.LogUnits <= 0) custody.Remove(found);
            return new KeyValuePair<TollLogCustodyLot, int>(found, take);
        }

        /// <summary>D2B save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class SawmillTollLogStockSaveDto
        {
            public List<TollLogCustodyLot> CustodyLots = new List<TollLogCustodyLot>();
        }

        public SawmillTollLogStockSaveDto CaptureSaveDto()
        {
            var dto = new SawmillTollLogStockSaveDto();
            foreach (var entry in custody)
            {
                if (entry == null || entry.Lot == null) continue;
                dto.CustodyLots.Add(new TollLogCustodyLot
                {
                    Lot = new LogLot
                    {
                        LotId = entry.Lot.LotId,
                        LogUnits = entry.Lot.LogUnits,
                        StandId = entry.Lot.StandId,
                        Species = entry.Lot.Species,
                        FelledBy = entry.Lot.FelledBy,
                        FelledDayIndex = entry.Lot.FelledDayIndex,
                    },
                    CustomerId = entry.CustomerId,
                    CustomerName = entry.CustomerName,
                    ReceivedDayIndex = entry.ReceivedDayIndex,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(SawmillTollLogStockSaveDto dto)
        {
            custody.Clear();
            if (dto == null) return;
            foreach (var entry in dto.CustodyLots)
            {
                if (entry == null || entry.Lot == null) continue;
                custody.Add(entry);
            }
        }
    }

    /// <summary>
    /// D2B: one customer-owned product lot waiting in the mill's pickup
    /// register — sawn lumber or slab/offcut byproducts from a toll run.
    /// The mill holds it in custody; it is never mill inventory.
    /// </summary>
    [Serializable]
    public sealed class SawmillTollProductCustody
    {
        public SawmillLumberLot LumberLot;       // set for lumber
        public SlabOffcutFuelLot ByproductLot;   // set for slab/offcut byproducts
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;
        public int ProducedDayIndex;

        public SawmillTollProductCustody() { }

        public int LumberUnits => LumberLot != null ? Math.Max(0, LumberLot.LumberUnits) : 0;
        public int FuelWoodUnits => ByproductLot != null ? Math.Max(0, ByproductLot.FuelWoodUnits) : 0;
    }

    /// <summary>
    /// D2B: one withdrawal of toll products into a customer's hands — the
    /// audit line of what left custody, preserving provenance.
    /// </summary>
    [Serializable]
    public sealed class SawmillTollDispenseLine
    {
        public string LotId = string.Empty;
        public string ProductKind = string.Empty; // "lumber" or "slab-offcut"
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public SawmillTollDispenseLine() { }
    }

    /// <summary>
    /// D2B: the pickup register — customer-owned lumber and byproducts from
    /// toll runs, held in custody until the customer collects. FIFO per
    /// customer so stock ages honestly. A shortfall returns fewer lines —
    /// never invented units.
    /// </summary>
    public sealed class SawmillTollPickupRegister
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<SawmillTollProductCustody> held = new List<SawmillTollProductCustody>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<SawmillTollProductCustody> Held => held;

        public int LumberUnitsFor(string customerId)
        {
            int total = 0;
            foreach (var entry in held)
            {
                if (entry != null && string.Equals(entry.CustomerId, customerId, StringComparison.Ordinal))
                    total += entry.LumberUnits;
            }
            return total;
        }

        public int FuelWoodUnitsFor(string customerId)
        {
            int total = 0;
            foreach (var entry in held)
            {
                if (entry != null && string.Equals(entry.CustomerId, customerId, StringComparison.Ordinal))
                    total += entry.FuelWoodUnits;
            }
            return total;
        }

        public void Hold(SawmillTollProductCustody custody, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (custody == null) return;
            held.Add(custody);
            string what = custody.LumberLot != null
                ? $"{custody.LumberUnits} lumber units (lot {custody.LumberLot.LotId})"
                : $"{custody.FuelWoodUnits} fuel-wood units (lot {custody.ByproductLot.LotId})";
            diag.Add($"SawmillTollPickupRegister: holding {what} for {custody.CustomerName} ({custody.CustomerId}) — custody, not mill inventory.");
        }

        /// <summary>
        /// The customer collects up to `units` of the named product kind,
        /// oldest lots first, with provenance lines. Empty shelves stay
        /// empty; nothing is invented.
        /// </summary>
        public List<SawmillTollDispenseLine> TryCollect(string customerId, string productKind, int units, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<SawmillTollDispenseLine>();
            if (string.IsNullOrWhiteSpace(customerId) || units <= 0) return lines;
            bool wantLumber = string.Equals(productKind, "lumber", StringComparison.OrdinalIgnoreCase);

            held.Sort((a, b) => a.ProducedDayIndex.CompareTo(b.ProducedDayIndex));

            int remaining = units;
            foreach (var entry in held)
            {
                if (remaining <= 0) break;
                if (entry == null || !string.Equals(entry.CustomerId, customerId, StringComparison.Ordinal)) continue;
                if (wantLumber)
                {
                    if (entry.LumberLot == null || entry.LumberUnits <= 0) continue;
                    int take = Math.Min(remaining, entry.LumberUnits);
                    entry.LumberLot.LumberUnits -= take;
                    remaining -= take;
                    lines.Add(new SawmillTollDispenseLine
                    {
                        LotId = entry.LumberLot.LotId.ToString(),
                        ProductKind = "lumber",
                        UnitsTaken = take,
                        ProvenanceChain = entry.LumberLot.ProvenanceChain(),
                    });
                }
                else
                {
                    if (entry.ByproductLot == null || entry.FuelWoodUnits <= 0) continue;
                    int take = Math.Min(remaining, entry.FuelWoodUnits);
                    entry.ByproductLot.FuelWoodUnits -= take;
                    remaining -= take;
                    lines.Add(new SawmillTollDispenseLine
                    {
                        LotId = entry.ByproductLot.LotId.ToString(),
                        ProductKind = "slab-offcut",
                        UnitsTaken = take,
                        ProvenanceChain = entry.ByproductLot.ProvenanceChain(),
                    });
                }
            }

            held.RemoveAll(e => e != null && e.LumberUnits <= 0 && e.FuelWoodUnits <= 0);

            if (remaining > 0)
            {
                diag.Add($"SawmillTollPickupRegister: shortfall — {customerId} asked for {units} {productKind}, collected {units - remaining}. Nothing invented.");
            }
            return lines;
        }

        /// <summary>D2B save contract: lives inside the owning register class.</summary>
        [Serializable]
        public sealed class SawmillTollPickupRegisterSaveDto
        {
            public List<SawmillTollProductCustody> Held = new List<SawmillTollProductCustody>();
        }

        public SawmillTollPickupRegisterSaveDto CaptureSaveDto()
        {
            var dto = new SawmillTollPickupRegisterSaveDto();
            foreach (var entry in held)
            {
                if (entry == null) continue;
                var copy = new SawmillTollProductCustody
                {
                    CustomerId = entry.CustomerId,
                    CustomerName = entry.CustomerName,
                    ProducedDayIndex = entry.ProducedDayIndex,
                };
                if (entry.LumberLot != null)
                {
                    copy.LumberLot = new SawmillLumberLot
                    {
                        LotId = entry.LumberLot.LotId,
                        LumberUnits = entry.LumberLot.LumberUnits,
                        SourceLogLotId = entry.LumberLot.SourceLogLotId,
                        StandId = entry.LumberLot.StandId,
                        Species = entry.LumberLot.Species,
                        MillBusinessId = entry.LumberLot.MillBusinessId,
                        SawedBy = entry.LumberLot.SawedBy,
                        SawedDayIndex = entry.LumberLot.SawedDayIndex,
                        ConversionProfileId = entry.LumberLot.ConversionProfileId,
                        StackedDayIndex = entry.LumberLot.StackedDayIndex,
                    };
                }
                if (entry.ByproductLot != null)
                {
                    copy.ByproductLot = new SlabOffcutFuelLot
                    {
                        LotId = entry.ByproductLot.LotId,
                        FuelWoodUnits = entry.ByproductLot.FuelWoodUnits,
                        SourceMillBusinessId = entry.ByproductLot.SourceMillBusinessId,
                        SourceLogLotId = entry.ByproductLot.SourceLogLotId,
                        StandId = entry.ByproductLot.StandId,
                        Species = entry.ByproductLot.Species,
                        ProducedDayIndex = entry.ByproductLot.ProducedDayIndex,
                    };
                }
                dto.Held.Add(copy);
            }
            return dto;
        }

        public void LoadFromSaveDto(SawmillTollPickupRegisterSaveDto dto)
        {
            held.Clear();
            if (dto == null) return;
            foreach (var entry in dto.Held)
            {
                if (entry == null) continue;
                held.Add(entry);
            }
        }
    }

    /// <summary>
    /// D2B: the result of one toll sawing run — what the customer keeps,
    /// what the mill took as its toll, and what the ledger authority must
    /// post. The customer's lumber and byproducts go to the pickup
    /// register; the in-kind toll lumber goes to the mill's own lumber
    /// yard; the cash toll is returned for the ledger authority (money
    /// moves only through ledger authorities, never here).
    /// </summary>
    [Serializable]
    public sealed class SawmillTollSawingResult
    {
        public int LogsSawn;
        public string SourceTollLotId = string.Empty;
        public string CustomerId = string.Empty;
        public string CustomerName = string.Empty;
        public int CustomerLumberUnits;
        public int TollInKindLumberUnits;
        public int TollCashCents;      // owed by the customer — the ledger authority posts it
        public int CustomerFuelWoodUnits; // slab/offcut byproducts — the customer's, in custody
        public int SawMinutes;         // labor calibration for the run (profile data x logs)

        public SawmillTollSawingResult() { }
    }
}
