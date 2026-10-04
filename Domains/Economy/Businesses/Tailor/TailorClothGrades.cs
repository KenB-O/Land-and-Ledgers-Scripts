using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Tailor
{
    /// <summary>
    /// D1C: the cloth grading/quality fork, parameterized. The canon says
    /// only "stock of cloth/notions" (Part V scale column) and the import
    /// catalog names "Cloth (wool/cotton, by the yard)" — NO canon grade
    /// table exists, and no canon rule ties cloth grade to garment price or
    /// quality outcomes. So grading is DATA the supplier declares on each
    /// lot and a caller-supplied policy, never a canon claim: the default
    /// mode is Unrated and the simulation behaves exactly as W1C did.
    /// </summary>
    public enum TailorClothGradeMode
    {
        /// <summary>Grades are recorded for provenance only; prices and dispense ignore them.</summary>
        Unrated = 0,

        /// <summary>The caller's multiplier table adjusts bespoke piece rates by dominant cloth grade.</summary>
        Rated = 1,
    }

    /// <summary>
    /// D1C: one cloth grade as DATA — a label the caller (or the historical
    /// defaults below) defines, with a note recording WHERE the definition
    /// came from. Never a canon fact.
    /// </summary>
    [Serializable]
    public sealed class TailorClothGrade
    {
        public string GradeId = string.Empty;
        public string DisplayLabel = string.Empty;

        /// <summary>Where this grade definition came from (historical note, supplier catalog, scenario data). Never "the canon" unless the canon actually says it.</summary>
        public string SourceNote = string.Empty;

        public TailorClothGrade() { }
    }

    /// <summary>D1C: one garment → grade-label preference (Unity cannot serialize Dictionary).</summary>
    [Serializable]
    public sealed class TailorGarmentGradePreference
    {
        public string GarmentId = string.Empty;
        public string GradeLabel = string.Empty;

        public TailorGarmentGradePreference() { }
    }

    /// <summary>D1C: one grade → piece-rate multiplier pair (Unity cannot serialize Dictionary).</summary>
    [Serializable]
    public sealed class TailorGradePriceMultiplier
    {
        public string GradeId = string.Empty;

        /// <summary>Multiplier applied to the bespoke piece rate when this grade dominates the garment's cloth. 1.0 = no effect.</summary>
        public float PriceMultiplier = 1f;

        public TailorGradePriceMultiplier() { }
    }

    /// <summary>
    /// D1C: the shop's cloth-grade policy. Default Unrated: grades are
    /// recorded on lots and dispense lines for provenance, and nothing in
    /// the simulation changes. A caller that wants graded pricing opts into
    /// Rated and supplies the multiplier table.
    /// </summary>
    [Serializable]
    public sealed class TailorClothGradePolicy
    {
        public TailorClothGradeMode Mode = TailorClothGradeMode.Unrated;
        public List<TailorClothGrade> KnownGrades = new List<TailorClothGrade>();
        public List<TailorGradePriceMultiplier> PriceMultipliers = new List<TailorGradePriceMultiplier>();

        public TailorClothGradePolicy() { }
    }

    /// <summary>
    /// D1C: grade helpers. The historical defaults are documented 1870s
    /// frontier practice (coarse wool/cotton goods for workwear; finer dress
    /// goods ordered for Sunday clothes) — NOT canon, NOT a claim about the
    /// game world, and clearly marked as such on every grade's SourceNote.
    /// Multiplier values are calibration (Canon Part XV).
    /// </summary>
    public static class TailorClothGrades
    {
        public const string UtilityGradeId = "utility";
        public const string FineGradeId = "fine";

        /// <summary>
        /// Historical-practice example grades, NOT canon. A caller that wants
        /// graded cloth without inventing its own taxonomy can start here —
        /// every grade carries its SourceNote saying exactly that.
        /// </summary>
        public static TailorClothGradePolicy HistoricalFrontierDefaults()
        {
            var policy = new TailorClothGradePolicy
            {
                Mode = TailorClothGradeMode.Rated,
            };
            policy.KnownGrades.Add(new TailorClothGrade
            {
                GradeId = UtilityGradeId,
                DisplayLabel = "Utility (coarse wool/cotton workwear cloth)",
                SourceNote = "D1C historical note — 1870s frontier practice, NOT canon: workwear was cut from coarse woolen and cotton goods stocked on hand.",
            });
            policy.KnownGrades.Add(new TailorClothGrade
            {
                GradeId = FineGradeId,
                DisplayLabel = "Fine (dress goods)",
                SourceNote = "D1C historical note — 1870s frontier practice, NOT canon: finer dress cloth was typically ordered for Sunday clothes rather than stocked.",
            });
            policy.PriceMultipliers.Add(new TailorGradePriceMultiplier { GradeId = UtilityGradeId, PriceMultiplier = 1f });
            policy.PriceMultipliers.Add(new TailorGradePriceMultiplier { GradeId = FineGradeId, PriceMultiplier = 1.5f });
            return policy;
        }

        /// <summary>
        /// Returns the price multiplier for a grade label. Unrated policy,
        /// unknown grade, or empty label all return 1.0 — never guessed.
        /// </summary>
        public static float GetPriceMultiplier(TailorClothGradePolicy policy, string gradeLabel)
        {
            if (policy == null || policy.Mode != TailorClothGradeMode.Rated) return 1f;
            if (string.IsNullOrWhiteSpace(gradeLabel)) return 1f;
            if (policy.PriceMultipliers == null) return 1f;
            foreach (var pair in policy.PriceMultipliers)
            {
                if (pair != null && string.Equals(pair.GradeId, gradeLabel, StringComparison.OrdinalIgnoreCase))
                {
                    return Math.Max(0f, pair.PriceMultiplier);
                }
            }

            return 1f;
        }

        /// <summary>
        /// The grade label contributing the most yards in a dispense list.
        /// Empty string = unrated/undeclared cloth.
        /// </summary>
        public static string DominantGradeLabel(List<TailorClothDispenseLine> lines)
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
