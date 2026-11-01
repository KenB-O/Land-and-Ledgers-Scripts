using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// D1C: the bespoke vs ready-made design fork, parameterized. Canon Part V
    /// says the tailor "turns cloth and notions into workwear, mending, and
    /// clothing service" but says NOTHING about whether workwear is made only
    /// to order (bespoke) or also sewn ready-made for shelf sale — and the
    /// historical record in-repo is equally silent. So the shop defaults to
    /// Disabled (W1C bespoke-only behavior, byte-for-byte preserved) and a
    /// caller opts into WorkwearBatches explicitly. This parameter, not a
    /// guess, is the fork record.
    /// </summary>
    public enum TailorReadyMadeMode
    {
        Disabled = 0,
        WorkwearBatches = 1,
    }

    /// <summary>
    /// D1C: the ready-made workwear policy as data. One ready-made garment is
    /// a work shirt's sew-and-press work with NO measuring and NO fittings
    /// (fittings are bespoke-only, per the W1C pipeline) — so the labor is
    /// SewMinutes + PressMinutes of the shirt spec, and the cloth is the
    /// shirt's yardage. All quantities are calibration (Canon Part XV);
    /// settable per shop.
    /// </summary>
    [Serializable]
    public sealed class TailorReadyMadePolicy
    {
        /// <summary>TUNING: labor minutes to sew and press one ready-made work garment (no measuring, no fittings).</summary>
        public int LaborMinutesPerGarment = TailorGarmentCatalog.ShirtSewMinutes + TailorGarmentCatalog.ShirtPressMinutes;

        /// <summary>TUNING: cloth yards per ready-made work garment (a work shirt).</summary>
        public int ClothYardsPerGarment = TailorGarmentCatalog.ShirtClothYards;

        /// <summary>TUNING: notion units per ready-made work garment (thread, buttons).</summary>
        public int NotionsUnitsPerGarment = TailorGarmentCatalog.StandardNotionsUnits;

        /// <summary>
        /// Optional cloth-grade preference for ready-made yardage (see
        /// TailorClothGrades). Empty = no preference; the dispense falls back
        /// to plain FIFO exactly like the bespoke reservation path.
        /// </summary>
        public string SuggestedClothGrade = string.Empty;

        public TailorReadyMadePolicy() { }
    }

    /// <summary>
    /// D1C: one ready-made production batch — the shop's DATA record of
    /// garments sewn for the shelf. The caller feeds
    /// <see cref="TailorReadyMadeBatch.UnitsSewn"/> into the business's
    /// "clothing" CategoryStockState (AddStockUnits), which the existing
    /// generic weekly outlet (SellTrackedLocalMarketOutput, up to 28
    /// clothing units/week) already sells. Money moves only through ledger
    /// authorities; this record never touches the ledger.
    /// </summary>
    [Serializable]
    public sealed class TailorReadyMadeBatch
    {
        public string BatchId = string.Empty;
        public int DayIndex;
        public int UnitsSewn;
        public int LaborMinutesSpent;
        public int TableDaysUsed;
        public string ClothGradeUsed = string.Empty;

        /// <summary>Cloth dispensed for this batch, with full upstream provenance.</summary>
        public List<TailorClothDispenseLine> ClothProvenance = new List<TailorClothDispenseLine>();

        /// <summary>Notions dispensed for this batch, with full upstream provenance.</summary>
        public List<TailorClothDispenseLine> NotionsProvenance = new List<TailorClothDispenseLine>();

        public TailorReadyMadeBatch() { }
    }

    /// <summary>
    /// D1C: ready-made workwear production. Consumes REAL cloth and notions
    /// from named lots with provenance and the tailor's real remaining labor
    /// and table-days — a shortfall anywhere produces a loud diagnostic and
    /// a parked shop, never conjured garments.
    /// </summary>
    public static class TailorReadyMadeProduction
    {
        /// <summary>
        /// Sews as many ready-made work garments as labor, free table-days,
        /// cloth, and notions allow. Claims one table-day per garment
        /// (sew + press at one table, mirroring W1C's bench-stage model).
        /// Returns null when the mode is Disabled or nothing can be sewn;
        /// the caller never invents units from a null result.
        /// </summary>
        public static TailorReadyMadeBatch SewBatch(
            TailorClothStock stock,
            TailorReadyMadePolicy policy,
            TailorReadyMadeMode mode,
            ref int laborMinutesAvailable,
            Dictionary<int, bool> tableFree,
            string batchId,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            policy = policy ?? new TailorReadyMadePolicy();

            if (mode != TailorReadyMadeMode.WorkwearBatches)
            {
                return null;
            }

            if (stock == null)
            {
                diag.Add("TailorReadyMadeProduction: no cloth stock — no ready-made garments sewn.");
                return null;
            }

            int laborPer = Math.Max(1, policy.LaborMinutesPerGarment);
            int clothPer = Math.Max(1, policy.ClothYardsPerGarment);
            int notionsPer = Math.Max(0, policy.NotionsUnitsPerGarment);

            int freeTables = 0;
            if (tableFree != null)
            {
                foreach (var kvp in tableFree)
                {
                    if (kvp.Value) freeTables++;
                }
            }

            int byLabor = Math.Max(0, laborMinutesAvailable) / laborPer;
            int byTable = Math.Max(0, freeTables);
            int byCloth = stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId) / clothPer;
            int byNotions = notionsPer <= 0
                ? int.MaxValue
                : stock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId) / notionsPer;

            int units = Math.Min(Math.Min(byLabor, byTable), Math.Min(byCloth, byNotions));
            if (units <= 0)
            {
                diag.Add("TailorReadyMadeProduction: nothing sewable — " +
                    $"labor allows {byLabor}, tables allow {byTable}, cloth allows {byCloth}, notions allow {(notionsPer <= 0 ? "unlimited" : byNotions.ToString())}. " +
                    "Ready-made work waits; nothing conjured.");
                return null;
            }

            // Claim the lowest-index free tables FIRST — one table-day per
            // garment — so a later dispense failure can release them and the
            // shop day stays honest. Nothing is dispensed before its table exists.
            var claimedTables = new List<int>();
            if (tableFree != null)
            {
                for (int i = 0; i < units; i++)
                {
                    int best = int.MaxValue;
                    foreach (var kvp in tableFree)
                    {
                        if (kvp.Value && kvp.Key < best) best = kvp.Key;
                    }

                    if (best == int.MaxValue) break;
                    tableFree[best] = false;
                    claimedTables.Add(best);
                }
            }

            int sewn = claimedTables.Count;
            if (sewn <= 0)
            {
                diag.Add("TailorReadyMadeProduction: no free cutting table — ready-made work waits for a table-day.");
                return null;
            }

            var clothLines = stock.TryDispenseUnits(
                TailorGarmentCatalog.ClothItemId, sewn * clothPer, dayIndex, diag, policy.SuggestedClothGrade);
            if (clothLines == null)
            {
                ReleaseClaims(tableFree, claimedTables);
                diag.Add("TailorReadyMadeProduction: cloth dispense refused mid-batch — claims released, no ready-made garments sewn.");
                return null;
            }

            List<TailorClothDispenseLine> notionLines = new List<TailorClothDispenseLine>();
            if (notionsPer > 0)
            {
                notionLines = stock.TryDispenseUnits(
                    TailorGarmentCatalog.NotionsItemId, sewn * notionsPer, dayIndex, diag);
                if (notionLines == null)
                {
                    ReleaseClaims(tableFree, claimedTables);
                    diag.Add("TailorReadyMadeProduction: notions dispense refused mid-batch — claims released, no ready-made garments sewn.");
                    return null;
                }
            }

            var batch = new TailorReadyMadeBatch
            {
                BatchId = batchId ?? string.Empty,
                DayIndex = dayIndex,
                UnitsSewn = sewn,
                LaborMinutesSpent = sewn * laborPer,
                TableDaysUsed = claimedTables.Count,
            };
            batch.ClothProvenance.AddRange(clothLines);
            batch.NotionsProvenance.AddRange(notionLines);
            batch.ClothGradeUsed = DominantGrade(clothLines);

            laborMinutesAvailable = Math.Max(0, laborMinutesAvailable - batch.LaborMinutesSpent);
            diag.Add($"TailorReadyMadeProduction: batch {batch.BatchId} sewed {sewn} ready-made work garment(s) " +
                $"(day {dayIndex}, {batch.LaborMinutesSpent}m, {claimedTables.Count} table-day(s)) — caller adds {sewn} 'clothing' stock unit(s) to the outlet.");
            return batch;
        }

        private static void ReleaseClaims(Dictionary<int, bool> tableFree, List<int> claimedTables)
        {
            if (tableFree == null || claimedTables == null) return;
            foreach (int table in claimedTables)
            {
                tableFree[table] = true;
            }
        }

        private static string DominantGrade(List<TailorClothDispenseLine> lines)
        {
            var byGrade = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (lines != null)
            {
                foreach (var line in lines)
                {
                    if (line == null) continue;
                    string grade = line.GradeLabel ?? string.Empty;
                    if (!byGrade.TryGetValue(grade, out int soFar)) soFar = 0;
                    byGrade[grade] = soFar + Math.Max(0, line.UnitsTaken);
                }
            }

            string best = string.Empty;
            int bestUnits = 0;
            foreach (var kvp in byGrade)
            {
                if (kvp.Value > bestUnits)
                {
                    bestUnits = kvp.Value;
                    best = kvp.Key;
                }
            }

            return best;
        }
    }
}
