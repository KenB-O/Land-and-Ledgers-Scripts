using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// W2A: one flour/notions lot with full upstream provenance. Every pound
    /// of flour and every notion unit must trace to a real lot from a real
    /// supplier — a miller (CRP-3 grain chain: mill ← dealer ← farm), a
    /// declared import order from a named off-map origin, or a named local
    /// supply relationship — or to the explicit one-time bootstrap endowment.
    /// No orphan lots, no synthetic stock.
    /// </summary>
    [Serializable]
    public sealed class BakeryFlourLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FlourName = string.Empty; // "bakery-flour" or "bakery-notions"
        public int Units; // lb of flour, or notion units
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId, or a local supply relationship id).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map or local origin, e.g. "Off-map flour mill, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>The miller whose mill produced this flour (CRP-3 grain chain intake), empty for non-mill supply.</summary>
        public string MillerBusinessId = string.Empty;

        /// <summary>The upstream grain lot id the miller names (dealer ← farm provenance continues through the mill batch).</summary>
        public string SourceGrainLotId = string.Empty;

        /// <summary>Any extra upstream link (wholesaler name, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="BakeryFlourBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through millers and import
        /// orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public BakeryFlourLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via millers and import orders";
            }

            string chain = $"lot {LotId} / day {AcquiredDayIndex}";
            if (!string.IsNullOrWhiteSpace(MillerBusinessId))
            {
                chain += $" / miller {MillerBusinessId}";
                if (!string.IsNullOrWhiteSpace(SourceGrainLotId))
                {
                    chain += $" ← grain lot {SourceGrainLotId}";
                }
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            chain += $" / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// W2A: one line of a dispense result — which lot the units came from and
    /// the full upstream chain. Dough batches carry these so every pound of
    /// flour and every notion unit is traceable into the bread.
    /// </summary>
    [Serializable]
    public sealed class BakeryFlourDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FlourName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public BakeryFlourDispenseLine() { }
    }

    /// <summary>
    /// W2A: the bakery's flour bin (flour by the pound, notions). Units dispense
    /// FIFO (oldest stock first); every dispense returns the lots consumed so
    /// dough batches carry provenance. Refusals are loud — stock shortfalls are
    /// never faked.
    /// </summary>
    public sealed class BakeryFlourStock
    {
        private readonly List<BakeryFlourLot> lots = new List<BakeryFlourLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BakeryFlourLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the bin. Lots must name their supplier chain
        /// (miller + grain provenance, or import order + origin) or carry the
        /// explicit bootstrap flag. Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(BakeryFlourLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "BakeryFlourStock: null lot refused — no flour without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.FlourName))
                return "BakeryFlourStock: lot refused — flour/notions name is required.";
            if (lot.Units <= 0)
                return $"BakeryFlourStock: lot refused — '{lot.FlourName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.MillerBusinessId)
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"BakeryFlourStock: lot refused — '{lot.FlourName}' names no miller and no import order/origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"BakeryFlourStock: lot refused — lot {lot.LotId} already in the bin.";
                }
            }

            lots.Add(lot);
            diag.Add($"BakeryFlourStock: received {lot.Units} units '{lot.FlourName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the bin cannot cover the request — no units are conjured.
        /// </summary>
        public List<BakeryFlourDispenseLine> TryDispenseUnits(string flourName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<BakeryFlourDispenseLine>();
            if (string.IsNullOrWhiteSpace(flourName) || units <= 0)
            {
                diag.Add("BakeryFlourStock: dispense refused — flour name and positive unit count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FlourName, flourName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Units;
                }
            }

            if (available < units)
            {
                diag.Add($"BakeryFlourStock: only {available} units of '{flourName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<BakeryFlourLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FlourName, flourName, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex));

            int remaining = units;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new BakeryFlourDispenseLine
                {
                    LotId = lot.LotId,
                    FlourName = lot.FlourName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the bin never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"BakeryFlourStock: dispensed {units} units '{flourName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand units of a flour/notions kind.</summary>
        public int UnitsOnHand(string flourName)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FlourName, flourName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Units;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BakeryFlourStockSaveDto
        {
            public List<BakeryFlourLot> Lots = new List<BakeryFlourLot>();
        }

        public BakeryFlourStockSaveDto CaptureSaveDto()
        {
            var dto = new BakeryFlourStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(BakeryFlourStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W2A: the one-time opening endowment of the bakery's flour bin. Explicitly
    /// marked BOOTSTRAP — it stands in for the shop's opening stocking (a new
    /// bakery opened with flour and notions on hand) and is NEVER silently
    /// replenished: later stock comes only from millers (CRP-3) or import
    /// orders of <see cref="BakeryFlourSupply.FlourMaterialId"/> and
    /// <see cref="BakeryFlourSupply.NotionsMaterialId"/>.
    /// </summary>
    public static class BakeryFlourBootstrap
    {
        /// <summary>TUNING: opening flour (lb) in the bin.</summary>
        public const int BootstrapFlourUnits = 120;

        /// <summary>TUNING: opening notion units in the bin.</summary>
        public const int BootstrapNotionsUnits = 30;

        public static void ApplyBootstrapEndowment(
            BakeryFlourStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("BakeryFlourBootstrap: no flour stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("BakeryFlourBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new BakeryFlourLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = BootstrapFlourUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via millers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BakeryFlourBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new BakeryFlourLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = BootstrapNotionsUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via millers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BakeryFlourBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("BakeryFlourBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W2A: the flour/notions supply link. Survey result: the CRP-3 grain chain
    /// (farm → dealer → miller) is the primary honest path — the miller's
    /// <see cref="Miller.TakeProduct"/> produces real "flour" MillLots with
    /// provenance (miller business id, source grain lot, milling worker) — so
    /// the bakery takes those lots into its bin with the chain preserved. For
    /// towns with no operating miller, the honest fallback is a DECLARED
    /// off-map trade link (EQU-1 precedent: named origin, real distance and
    /// transit days), matching how frontier bakers bought shipped flour by
    /// rail when local mills were idle or absent. The shop reorders through
    /// <see cref="ImportService"/>.
    /// </summary>
    public static class BakeryFlourSupply
    {
        public const string FlourMaterialId = "bakery-flour";
        public const string NotionsMaterialId = "bakery-notions";

        /// <summary>
        /// Registers flour and baker's notions as importable materials (EQU-1
        /// extension path). Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureBakeryImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                FlourMaterialId, "Flour (by the pound)",
                "Off-map flour mill, via railhead", 250, 14, 3));
            if (problem != null && diag != null)
            {
                diag.Add($"BakeryFlourSupply: flour import registration: {problem}");
            }

            problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                NotionsMaterialId, "Baker's notions (yeast, soda, salt, lard)",
                "Off-map provision wholesaler, via railhead", 250, 14, 10));
            if (problem != null && diag != null)
            {
                diag.Add($"BakeryFlourSupply: notions import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes a miller's flour MillLot into the bakery's bin. The mill lot's
        /// upstream (miller business id, source grain lot, milling worker/day)
        /// is preserved on the bakery lot — the farm ← dealer ← mill chain is
        /// never truncated. Refuses non-flour mill products loudly.
        /// </summary>
        public static string ReceiveMillLot(
            BakeryFlourStock stock,
            MillLot millLot,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BakeryFlourSupply.ReceiveMillLot: no flour stock.";
            if (millLot == null || millLot.QuantityUnits <= 0)
                return "BakeryFlourSupply.ReceiveMillLot: no mill lot offered — flour is not conjured.";
            if (!string.Equals(millLot.ProductKind, "flour", StringComparison.OrdinalIgnoreCase))
                return $"BakeryFlourSupply.ReceiveMillLot: mill lot {millLot.LotId} is '{millLot.ProductKind}', not flour — refused.";
            if (idRegistry == null) return "BakeryFlourSupply.ReceiveMillLot: no id registry.";

            return stock.ReceiveLot(new BakeryFlourLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FlourName = FlourMaterialId,
                Units = millLot.QuantityUnits,
                AcquiredDayIndex = dayIndex,
                MillerBusinessId = millLot.MillerBusinessId,
                SourceGrainLotId = millLot.SourceGrainLotId,
                SupplierNote = $"milled day {millLot.MilledDayIndex} by {millLot.MilledBy} (mill batch from grain lot {millLot.SourceGrainLotId})",
                OriginName = $"Miller {millLot.MillerBusinessId} (CRP-3 grain chain)",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Receives an import order's arrival into the bin as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            BakeryFlourStock stock,
            string flourName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BakeryFlourSupply.ReceiveImportArrival: no flour stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "BakeryFlourSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "BakeryFlourSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new BakeryFlourLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FlourName = flourName ?? string.Empty,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }
    }
}
