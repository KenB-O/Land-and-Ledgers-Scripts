using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// W1C: one cloth/notions lot with full upstream provenance. Every consumed
    /// yard of cloth and every notion must trace to a real lot from a real
    /// supplier — a declared import order from the named off-map dry-goods
    /// wholesaler, or a named local supply relationship — or to the explicit
    /// one-time bootstrap endowment. No orphan lots, no synthetic stock.
    /// </summary>
    [Serializable]
    public sealed class TailorClothLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string ClothName = string.Empty; // "tailor-cloth" or "tailor-notions"
        public int Units; // yards of cloth, or notion units
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId, or a local supply relationship id).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map or local origin, e.g. "Off-map dry-goods wholesaler, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (wholesaler name, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="TailorClothBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        /// <summary>
        /// D1C: supplier-declared cloth grade (e.g. "utility", "fine") — see
        /// TailorClothGrades. Recorded for provenance; the default cloth-grade
        /// policy (Unrated) gives it no simulation effect. Empty = undeclared.
        /// </summary>
        public string GradeLabel = string.Empty;

        /// <summary>D1C: supplier-declared fiber/weight note (free text, e.g. "coarse wool"). Provenance only.</summary>
        public string FiberNote = string.Empty;

        public TailorClothLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via import orders";
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            string chain = $"lot {LotId} / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// W1C: one line of a dispense result — which lot the units came from and the
    /// full upstream chain. Garment orders carry these so every yard of cloth and
    /// every notion is traceable.
    /// </summary>
    [Serializable]
    public sealed class TailorClothDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string ClothName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        /// <summary>D1C: the lot's supplier-declared grade label, carried for provenance (empty = undeclared).</summary>
        public string GradeLabel = string.Empty;

        public TailorClothDispenseLine() { }
    }

    /// <summary>
    /// W1C: the tailor's cloth shelf (cloth bolts by the yard, notions).
    /// Units dispense FIFO (oldest stock first); every dispense returns the lots
    /// consumed so garment orders carry provenance. Refusals are loud — stock
    /// shortfalls are never faked.
    /// </summary>
    public sealed class TailorClothStock
    {
        private readonly List<TailorClothLot> lots = new List<TailorClothLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<TailorClothLot> Lots => lots;

        /// <summary>
        /// Receives a lot onto the shelf. Lots must name their supplier chain
        /// (import order + origin) or carry the explicit bootstrap flag.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(TailorClothLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "TailorClothStock: null lot refused — no cloth without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.ClothName))
                return "TailorClothStock: lot refused — cloth name is required.";
            if (lot.Units <= 0)
                return $"TailorClothStock: lot refused — '{lot.ClothName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"TailorClothStock: lot refused — '{lot.ClothName}' names no import order or origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"TailorClothStock: lot refused — lot {lot.LotId} already on the shelf.";
                }
            }

            lots.Add(lot);
            diag.Add($"TailorClothStock: received {lot.Units} units '{lot.ClothName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). D1C: when a non-empty
        /// preferredGradeLabel is given, lots declaring that grade are
        /// preferred first (FIFO within grade), then the remainder comes
        /// FIFO from the other lots — the preference never conjures units,
        /// it only orders which real lots give them up. Returns null with a
        /// diagnostic when the shelf cannot cover the request — no units are
        /// conjured.
        /// </summary>
        public List<TailorClothDispenseLine> TryDispenseUnits(
            string clothName, int units, int dayIndex, List<string> diag, string preferredGradeLabel = null)
        {
            diag = diag ?? diagnostics;
            var lines = new List<TailorClothDispenseLine>();
            if (string.IsNullOrWhiteSpace(clothName) || units <= 0)
            {
                diag.Add("TailorClothStock: dispense refused — cloth name and positive unit count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ClothName, clothName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Units;
                }
            }

            if (available < units)
            {
                diag.Add($"TailorClothStock: only {available} units of '{clothName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day. D1C: grade preference orders lots —
            // preferred-grade lots first (FIFO within), then the rest (FIFO).
            var ordered = new List<TailorClothLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ClothName, clothName, StringComparison.OrdinalIgnoreCase))
                    ordered.Add(lot);
            }
            bool preferGrade = !string.IsNullOrWhiteSpace(preferredGradeLabel);
            ordered.Sort((a, b) =>
            {
                if (preferGrade)
                {
                    bool aPref = string.Equals(a.GradeLabel, preferredGradeLabel, StringComparison.OrdinalIgnoreCase);
                    bool bPref = string.Equals(b.GradeLabel, preferredGradeLabel, StringComparison.OrdinalIgnoreCase);
                    if (aPref != bPref) return aPref ? -1 : 1;
                }

                return a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
            });

            int remaining = units;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new TailorClothDispenseLine
                {
                    LotId = lot.LotId,
                    ClothName = lot.ClothName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                    GradeLabel = lot.GradeLabel ?? string.Empty,
                });
            }

            // Drop emptied lots so the shelf never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"TailorClothStock: dispensed {units} units '{clothName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand units of a cloth/notions kind.</summary>
        public int UnitsOnHand(string clothName)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ClothName, clothName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Units;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class TailorClothStockSaveDto
        {
            public List<TailorClothLot> Lots = new List<TailorClothLot>();
        }

        public TailorClothStockSaveDto CaptureSaveDto()
        {
            var dto = new TailorClothStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(TailorClothStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W1C: the one-time opening endowment of the tailor's cloth shelf.
    /// Explicitly marked BOOTSTRAP — it stands in for the shop's opening
    /// stocking (a new tailor opened with cloth bolts and notions on hand) and
    /// is NEVER silently replenished: later stock comes only from import orders
    /// of <see cref="TailorClothSupply.ClothMaterialId"/> and
    /// <see cref="TailorClothSupply.NotionsMaterialId"/> (or named local supply).
    /// </summary>
    public static class TailorClothBootstrap
    {
        /// <summary>TUNING: opening cloth yards on the shelf.</summary>
        public const int BootstrapClothYards = 30;

        /// <summary>TUNING: opening notion units on the shelf.</summary>
        public const int BootstrapNotionsUnits = 20;

        public static void ApplyBootstrapEndowment(
            TailorClothStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("TailorClothBootstrap: no cloth stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("TailorClothBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new TailorClothLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId,
                Units = BootstrapClothYards,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"TailorClothBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new TailorClothLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.NotionsItemId,
                Units = BootstrapNotionsUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"TailorClothBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("TailorClothBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W1C: the cloth/notions supply link. Survey result: no cloth or notions
    /// supplier exists anywhere in the import catalog, the general store's
    /// goods, or the local recurring order network (the general-store-to-tailor
    /// "cloth_notions" template names a flow, not a stocked supplier) — so the
    /// honest path is a DECLARED off-map trade link (EQU-1 precedent: named
    /// origin, real distance and transit days), matching how frontier tailors
    /// stocked cloth and notions from eastern dry-goods wholesale houses by rail.
    /// The shop reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class TailorClothSupply
    {
        public const string ClothMaterialId = "tailor-cloth";
        public const string NotionsMaterialId = "tailor-notions";

        /// <summary>
        /// Registers cloth and notions as importable materials (EQU-1 extension
        /// path). Historical: 1870s frontier tailors ordered cloth bolts and
        /// notions from eastern dry-goods wholesale houses shipped by rail —
        /// named origins, real transit, real cost. Prices/transit are
        /// calibration (Canon Part XV).
        /// </summary>
        public static void EnsureTailorImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                ClothMaterialId, "Cloth (wool/cotton, by the yard)",
                "Off-map dry-goods wholesaler, via railhead", 250, 14, 60));
            if (problem != null && diag != null)
            {
                diag.Add($"TailorClothSupply: cloth import registration: {problem}");
            }

            problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                NotionsMaterialId, "Tailor's notions (thread, buttons, needles)",
                "Off-map dry-goods wholesaler, via railhead", 250, 14, 8));
            if (problem != null && diag != null)
            {
                diag.Add($"TailorClothSupply: notions import registration: {problem}");
            }
        }

        /// <summary>
        /// Receives an import order's arrival onto the shelf as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            TailorClothStock stock,
            string clothName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag,
            string gradeLabel = null)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "TailorClothSupply.ReceiveImportArrival: no cloth stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "TailorClothSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "TailorClothSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new TailorClothLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ClothName = clothName ?? string.Empty,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
                GradeLabel = gradeLabel ?? string.Empty,
            }, diag);
        }
    }
}
