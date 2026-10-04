using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// W4B: lumber grades as DATA, not canon. Historical calibration: in the
    /// 1870s US lumber trade, grading was informal — broad yard sorts, not the
    /// standardized association rules that arrive in the 1890s. "Clear" is
    /// largely defect-free stock for finish work; "merchantable" (a.k.a.
    /// "common") is sound, usable stock — the default yard sort; "cull"
    /// (a.k.a. "refuse") is defective stock sold cheap or burned. Grade is
    /// assigned by the YARD at intake (the yard sorts what the mill delivers);
    /// W4A sawmill lots stay grade-free — that authority is untouched.
    /// </summary>
    public static class LumberYardGradeCatalog
    {
        public const string ClearGradeId = "clear";
        public const string MerchantableGradeId = "merchantable";
        public const string CullGradeId = "cull";

        private static readonly Dictionary<string, string> DisplayNames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { ClearGradeId, "Clear (finish stock)" },
                { MerchantableGradeId, "Merchantable (common)" },
                { CullGradeId, "Cull (refuse)" },
            };

        /// <summary>The honest default sort for undeclared lumber: sound, usable stock.</summary>
        public static string DefaultGradeId => MerchantableGradeId;

        public static bool IsKnownGrade(string gradeId)
        {
            return !string.IsNullOrWhiteSpace(gradeId) && DisplayNames.ContainsKey(gradeId.Trim());
        }

        public static string DisplayName(string gradeId)
        {
            if (string.IsNullOrWhiteSpace(gradeId)) return MerchantableGradeId;
            string key = gradeId.Trim();
            return DisplayNames.TryGetValue(key, out string name) ? name : key;
        }
    }

    /// <summary>
    /// W4B: how a lumber lot entered the yard — the acquisition path is part
    /// of the lot's identity, never inferred later.
    /// </summary>
    public enum LumberYardLumberSourceKind
    {
        MillDirect = 0,  // bought/received straight from a named sawmill (W4A lot)
        Purchase = 1,    // bought from a named seller (another yard, regional freight)
        Import = 2,      // arrived on a named import order (EQU-1 off-map trade link)
        Bootstrap = 3,   // one-time opening endowment, explicitly marked
    }

    /// <summary>
    /// W4B: the yard's own lumber lot — yard-side custody of sawn lumber. The
    /// sawmill's provenance (stand, log lot, mill, sawyer, day, conversion
    /// profile) is COPIED, never re-authored: the W4A lot stays the authority
    /// for what the mill produced. Yard-side fields (grade, acquisition cost,
    /// source kind) belong to the yard alone.
    /// </summary>
    [Serializable]
    public sealed class LumberYardLumberLot
    {
        public EntityId LotId = EntityId.Invalid; // yard-side custody id (EntityKind.Lot)
        public int LumberUnits;
        public string YardGradeId = string.Empty;  // LumberYardGradeCatalog id, assigned at intake
        public string Species = string.Empty;
        public int AcquiredDayIndex;
        public LumberYardLumberSourceKind SourceKind = LumberYardLumberSourceKind.MillDirect;

        // Copied sawmill provenance (W4A authority).
        public string SourceSawmillLotId = string.Empty; // the W4A lot this was bought from
        public string SourceMillBusinessId = string.Empty;
        public string SourceLogLotId = string.Empty;
        public string StandId = string.Empty;
        public EntityId SawedBy = EntityId.Invalid;
        public int SawedDayIndex;
        public string ConversionProfileId = string.Empty;

        // Trade provenance.
        public string SellerNote = string.Empty;   // named seller for purchases
        public string ImportOrderId = string.Empty;
        public string OriginName = string.Empty;
        public bool IsBootstrapEndowment;
        public int UnitCostCents;

        public LumberYardLumberLot() { }

        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return "OPENING ENDOWMENT (one-time; never auto-replenishes)"
                    + (string.IsNullOrWhiteSpace(Species) ? string.Empty : $" | {Species}");
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(StandId)) parts.Add($"stand {StandId}");
            if (!string.IsNullOrWhiteSpace(SourceLogLotId)) parts.Add($"log lot {SourceLogLotId}");
            if (!string.IsNullOrWhiteSpace(Species)) parts.Add(Species);
            if (!string.IsNullOrWhiteSpace(SourceMillBusinessId)) parts.Add($"mill {SourceMillBusinessId}");
            if (!string.IsNullOrWhiteSpace(SourceSawmillLotId)) parts.Add($"mill lot {SourceSawmillLotId}");
            if (!string.IsNullOrWhiteSpace(ConversionProfileId)) parts.Add($"profile {ConversionProfileId}");
            if (SourceKind == LumberYardLumberSourceKind.Import && !string.IsNullOrWhiteSpace(ImportOrderId))
                parts.Add($"import {ImportOrderId}");
            if (!string.IsNullOrWhiteSpace(OriginName)) parts.Add($"origin {OriginName}");
            if (!string.IsNullOrWhiteSpace(SellerNote)) parts.Add($"seller {SellerNote}");
            parts.Add($"yard lot {LotId}");
            return string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>
    /// W4B: one withdrawal of lumber units into a sale — the audit line of
    /// what left the yard, preserving each source lot's provenance.
    /// </summary>
    [Serializable]
    public sealed class LumberYardLumberDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public int UnitsTaken;
        public string YardGradeId = string.Empty;
        public string Species = string.Empty;
        public string ProvenanceChain = string.Empty;

        public LumberYardLumberDispenseLine() { }
    }

    /// <summary>
    /// W4B: the lumber yard's lumber inventory — lots in, withdrawn units out,
    /// FIFO so stock ages honestly (oldest acquisition first). Unlike the
    /// mill's own stock (W4A: production, foreign mills refused), the yard is
    /// TRADE (Canon §8.2: "either independent or paired with an owned mill"),
    /// so lumber from ANY named mill is welcome — the source mill is recorded,
    /// never hidden. Anonymous lots are refused loudly on every path.
    /// </summary>
    public sealed class LumberYardLumberStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LumberYardLumberLot> lots = new List<LumberYardLumberLot>();
        private readonly string yardBusinessId;

        public LumberYardLumberStock(string yardBusinessId)
        {
            this.yardBusinessId = yardBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LumberYardLumberLot> Lots => lots;
        public string YardBusinessId => yardBusinessId;

        public int TotalLumberUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.LumberUnits);
                return total;
            }
        }

        /// <summary>Units available under an optional species/grade filter. Empty filter = any.</summary>
        public int AvailableUnits(string speciesFilter, string gradeFilter)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (!MatchesFilter(lot, speciesFilter, gradeFilter)) continue;
                total += Math.Max(0, lot.LumberUnits);
            }
            return total;
        }

        private static bool MatchesFilter(LumberYardLumberLot lot, string speciesFilter, string gradeFilter)
        {
            if (!string.IsNullOrWhiteSpace(speciesFilter)
                && !string.Equals(lot.Species, speciesFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(gradeFilter)
                && !string.Equals(lot.YardGradeId, gradeFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        private void SortFifo()
        {
            lots.Sort((a, b) =>
            {
                int day = a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });
        }

        private string AssignGrade(string gradeId, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(gradeId))
            {
                diag.Add("LumberYardLumberStock: no grade declared at intake — "
                    + $"sorted as '{LumberYardGradeCatalog.DefaultGradeId}' (the honest default yard sort), recorded loudly.");
                return LumberYardGradeCatalog.DefaultGradeId;
            }
            string trimmed = gradeId.Trim();
            if (!LumberYardGradeCatalog.IsKnownGrade(trimmed))
            {
                return null; // caller refuses: grades are data, unknown ids are data errors
            }
            return trimmed;
        }

        private LumberYardLumberLot BuildYardLot(
            EntityId yardLotId, int units, string gradeId, string species, int acquiredDay,
            LumberYardLumberSourceKind sourceKind, int unitCostCents)
        {
            return new LumberYardLumberLot
            {
                LotId = yardLotId,
                LumberUnits = units,
                YardGradeId = gradeId,
                Species = species ?? string.Empty,
                AcquiredDayIndex = acquiredDay,
                SourceKind = sourceKind,
                UnitCostCents = Math.Max(0, unitCostCents),
            };
        }

        private string ValidateSawmillProvenance(SawmillLumberLot lot)
        {
            if (lot == null)
                return "LumberYardLumberStock: no lot offered — lumber is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "LumberYardLumberStock: a lumber lot needs an EntityId — anonymous stock is refused.";
            if (lot.LumberUnits <= 0)
                return "LumberYardLumberStock: a lumber lot needs positive units.";
            if (string.IsNullOrWhiteSpace(lot.SourceLogLotId))
                return "LumberYardLumberStock: no source log lot — orphan lumber refused.";
            if (string.IsNullOrWhiteSpace(lot.StandId))
                return "LumberYardLumberStock: no timber stand — the full provenance chain is required.";
            if (string.IsNullOrWhiteSpace(lot.MillBusinessId))
                return "LumberYardLumberStock: no mill named — orphan production refused.";
            return null;
        }

        /// <summary>
        /// Receives lumber bought/received straight from a named sawmill. The
        /// W4A lot's provenance is validated and copied; the yard mints its
        /// own custody id. Any NAMED mill is welcome (Canon §8.2 independent
        /// yard) — the source mill is recorded on the yard lot. Returns the
        /// refusal, or null.
        /// </summary>
        public string ReceiveFromSawmill(
            SawmillLumberLot millLot,
            string yardGradeId,
            int acquiredDayIndex,
            int unitCostCents,
            EntityId yardLotId,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = ValidateSawmillProvenance(millLot);
            if (refusal != null) return refusal;
            if (yardLotId == EntityId.Invalid)
                return "LumberYardLumberStock.ReceiveFromSawmill: the yard lot needs an EntityId — anonymous custody is refused.";

            string grade = AssignGrade(yardGradeId, diag);
            if (grade == null)
                return $"LumberYardLumberStock.ReceiveFromSawmill: unknown grade '{yardGradeId}' — grades are data, fix the id.";

            var yardLot = BuildYardLot(yardLotId, millLot.LumberUnits, grade, millLot.Species,
                acquiredDayIndex, LumberYardLumberSourceKind.MillDirect, unitCostCents);
            yardLot.SourceSawmillLotId = millLot.LotId.ToString();
            yardLot.SourceMillBusinessId = millLot.MillBusinessId;
            yardLot.SourceLogLotId = millLot.SourceLogLotId;
            yardLot.StandId = millLot.StandId;
            yardLot.SawedBy = millLot.SawedBy;
            yardLot.SawedDayIndex = millLot.SawedDayIndex;
            yardLot.ConversionProfileId = millLot.ConversionProfileId;

            lots.Add(yardLot);
            diag.Add($"LumberYardLumberStock ({yardBusinessId}): received {yardLot.LumberUnits} lumber units "
                + $"(yard lot {yardLot.LotId}, grade '{grade}') from mill '{millLot.MillBusinessId}' — {yardLot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Receives lumber bought from a NAMED seller (another yard, regional
        /// freight). The seller is named and the seller's own provenance is
        /// recorded — never anonymous. Returns the refusal, or null.
        /// </summary>
        public string ReceivePurchaseLot(
            EntityId yardLotId,
            int units,
            string species,
            string yardGradeId,
            string sellerBusinessId,
            string sellerProvenanceNote,
            int acquiredDayIndex,
            int unitCostCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (yardLotId == EntityId.Invalid)
                return "LumberYardLumberStock.ReceivePurchaseLot: the yard lot needs an EntityId — anonymous custody is refused.";
            if (units <= 0)
                return "LumberYardLumberStock.ReceivePurchaseLot: a lumber lot needs positive units.";
            if (string.IsNullOrWhiteSpace(sellerBusinessId))
                return "LumberYardLumberStock.ReceivePurchaseLot: the seller must be named — anonymous purchases are refused.";

            string grade = AssignGrade(yardGradeId, diag);
            if (grade == null)
                return $"LumberYardLumberStock.ReceivePurchaseLot: unknown grade '{yardGradeId}' — grades are data, fix the id.";

            var yardLot = BuildYardLot(yardLotId, units, grade, species,
                acquiredDayIndex, LumberYardLumberSourceKind.Purchase, unitCostCents);
            yardLot.SellerNote = $"{sellerBusinessId.Trim()}"
                + (string.IsNullOrWhiteSpace(sellerProvenanceNote) ? string.Empty : $" ({sellerProvenanceNote.Trim()})");

            lots.Add(yardLot);
            diag.Add($"LumberYardLumberStock ({yardBusinessId}): purchased {units} lumber units "
                + $"(yard lot {yardLot.LotId}, grade '{grade}') from '{sellerBusinessId}'.");
            return null;
        }

        /// <summary>
        /// Receives lumber arriving on a named import order (EQU-1 off-map
        /// trade link). The import order id is required — no orphan stock.
        /// Returns the refusal, or null.
        /// </summary>
        public string ReceiveImportArrival(
            EntityId yardLotId,
            int units,
            string species,
            string yardGradeId,
            string importOrderId,
            string originName,
            int acquiredDayIndex,
            int unitCostCents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (yardLotId == EntityId.Invalid)
                return "LumberYardLumberStock.ReceiveImportArrival: the yard lot needs an EntityId — anonymous custody is refused.";
            if (units <= 0)
                return "LumberYardLumberStock.ReceiveImportArrival: a lumber lot needs positive units.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "LumberYardLumberStock.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (string.IsNullOrWhiteSpace(originName))
                return "LumberYardLumberStock.ReceiveImportArrival: the origin must be named — no anonymous sources.";

            string grade = AssignGrade(yardGradeId, diag);
            if (grade == null)
                return $"LumberYardLumberStock.ReceiveImportArrival: unknown grade '{yardGradeId}' — grades are data, fix the id.";

            var yardLot = BuildYardLot(yardLotId, units, grade, species,
                acquiredDayIndex, LumberYardLumberSourceKind.Import, unitCostCents);
            yardLot.ImportOrderId = importOrderId.Trim();
            yardLot.OriginName = originName.Trim();

            lots.Add(yardLot);
            diag.Add($"LumberYardLumberStock ({yardBusinessId}): import arrival {units} lumber units "
                + $"(yard lot {yardLot.LotId}, grade '{grade}') on order '{importOrderId}' from {originName}.");
            return null;
        }

        /// <summary>
        /// One-time opening endowment path. Only lots explicitly marked as
        /// bootstrap endowments are accepted here; ordinary lots are refused
        /// (they have real intake paths). The once-guard lives on the owning
        /// runtime — this stock never auto-replenishes.
        /// </summary>
        public string ReceiveBootstrapEndowment(LumberYardLumberLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "LumberYardLumberStock.ReceiveBootstrapEndowment: no lot offered.";
            if (!lot.IsBootstrapEndowment)
                return "LumberYardLumberStock.ReceiveBootstrapEndowment: not marked as a bootstrap endowment — use a real intake path.";
            if (lot.LotId == EntityId.Invalid)
                return "LumberYardLumberStock.ReceiveBootstrapEndowment: the lot needs an EntityId.";
            if (lot.LumberUnits <= 0)
                return "LumberYardLumberStock.ReceiveBootstrapEndowment: the lot needs positive units.";
            if (!LumberYardGradeCatalog.IsKnownGrade(lot.YardGradeId))
                lot.YardGradeId = LumberYardGradeCatalog.DefaultGradeId;
            lot.SourceKind = LumberYardLumberSourceKind.Bootstrap;

            lots.Add(lot);
            diag.Add($"LumberYardLumberStock ({yardBusinessId}): BOOTSTRAP endowment {lot.LumberUnits} lumber units "
                + $"(yard lot {lot.LotId}) — one-time opening stock, never auto-replenishes.");
            return null;
        }

        /// <summary>
        /// Withdraws up to the requested units, oldest acquisitions first,
        /// recording the dispense lines with provenance. Optional species /
        /// grade filters (empty = any). A shortfall returns fewer lines —
        /// never invented units.
        /// </summary>
        public List<LumberYardLumberDispenseLine> TryWithdrawUnits(
            int units, string speciesFilter, string gradeFilter, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<LumberYardLumberDispenseLine>();
            if (units <= 0) return lines;

            SortFifo();

            int remaining = units;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                if (!MatchesFilter(lot, speciesFilter, gradeFilter)) continue;
                int available = Math.Max(0, lot.LumberUnits);
                if (available <= 0) continue;
                int take = Math.Min(remaining, available);
                lot.LumberUnits -= take;
                remaining -= take;
                lines.Add(new LumberYardLumberDispenseLine
                {
                    LotId = lot.LotId,
                    UnitsTaken = take,
                    YardGradeId = lot.YardGradeId,
                    Species = lot.Species,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            lots.RemoveAll(l => l.LumberUnits <= 0);

            if (remaining > 0)
            {
                string filterNote = string.IsNullOrWhiteSpace(speciesFilter) && string.IsNullOrWhiteSpace(gradeFilter)
                    ? string.Empty
                    : $" (filter: species '{speciesFilter}', grade '{gradeFilter}')";
                diag.Add($"LumberYardLumberStock ({yardBusinessId}): shortfall — requested {units}, withdrew {units - remaining}{filterNote}. "
                    + "Empty shelves stay empty; nothing invented.");
            }
            return lines;
        }

        /// <summary>W4B save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class LumberYardLumberStockSaveDto
        {
            public List<LumberYardLumberLot> Lots = new List<LumberYardLumberLot>();
        }

        public LumberYardLumberStockSaveDto CaptureSaveDto()
        {
            var dto = new LumberYardLumberStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new LumberYardLumberLot
                {
                    LotId = lot.LotId,
                    LumberUnits = lot.LumberUnits,
                    YardGradeId = lot.YardGradeId,
                    Species = lot.Species,
                    AcquiredDayIndex = lot.AcquiredDayIndex,
                    SourceKind = lot.SourceKind,
                    SourceSawmillLotId = lot.SourceSawmillLotId,
                    SourceMillBusinessId = lot.SourceMillBusinessId,
                    SourceLogLotId = lot.SourceLogLotId,
                    StandId = lot.StandId,
                    SawedBy = lot.SawedBy,
                    SawedDayIndex = lot.SawedDayIndex,
                    ConversionProfileId = lot.ConversionProfileId,
                    SellerNote = lot.SellerNote,
                    ImportOrderId = lot.ImportOrderId,
                    OriginName = lot.OriginName,
                    IsBootstrapEndowment = lot.IsBootstrapEndowment,
                    UnitCostCents = lot.UnitCostCents,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LumberYardLumberStockSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            foreach (var lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
    }
}
