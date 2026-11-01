using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// W5C: one crop variety as DATA. Variety is identity and provenance, not
    /// agronomy: yield/seed-rate calibration stays per <see cref="CropKind"/>
    /// (CRP-1 calibrations). Any variety-driven yield or hardiness effect is a
    /// genuine design fork — recorded here, never guessed into the numbers.
    ///
    /// Starter entries are real historical varieties of 1870s Ontario, per the
    /// W5C research pass: Red Fife wheat (David Fife, Otonabee Township, 1842),
    /// Scotch landrace oats, Yellow Dent corn, and Timothy hay grass.
    /// </summary>
    [Serializable]
    public sealed class CropVariety
    {
        public string VarietyId = string.Empty;      // e.g. "red-fife"
        public CropKind Crop;
        public string DisplayName = string.Empty;    // e.g. "Red Fife"
        public string OriginatorKind = string.Empty; // "breeder", "grower", "landrace", "promoter"
        public string OriginatorName = string.Empty; // person or community of origin
        public string OriginNote = string.Empty;     // historical note
        public int FirstGrownYear;                   // 0 = traditional / year unrecorded

        public CropVariety() { }

        public CropVariety(string varietyId, CropKind crop, string displayName,
            string originatorKind, string originatorName, string originNote, int firstGrownYear)
        {
            VarietyId = varietyId ?? string.Empty;
            Crop = crop;
            DisplayName = displayName ?? string.Empty;
            OriginatorKind = originatorKind ?? string.Empty;
            OriginatorName = originatorName ?? string.Empty;
            OriginNote = originNote ?? string.Empty;
            FirstGrownYear = Math.Max(0, firstGrownYear);
        }
    }

    /// <summary>
    /// W5C: the crop variety registry. Open for extension — new varieties
    /// register through <see cref="Register"/> with their originator named;
    /// no code changes needed. Seed lots reference varieties by id, so the
    /// planting record names WHAT was planted, not just the crop kind.
    /// </summary>
    public static class CropVarietyCatalog
    {
        /// <summary>
        /// Sentinel for seed whose variety was never recorded. This is honesty,
        /// not data: it marks "unrecorded" so saved seed never invents a
        /// variety it cannot prove. Deliberately NOT registered as a variety.
        /// </summary>
        public const string UnknownVarietyId = "unknown-variety";

        private static readonly Dictionary<string, CropVariety> varieties =
            new Dictionary<string, CropVariety>(StringComparer.OrdinalIgnoreCase);

        static CropVarietyCatalog()
        {
            // Real historical varieties of the game's era and place (1870s Ontario).
            Register(new CropVariety("red-fife", CropKind.Wheat, "Red Fife",
                "breeder", "David Fife",
                "First grown 1842, Otonabee Township, Peterborough County, Upper Canada; " +
                "the dominant Canadian spring wheat from the mid-1800s to the early 1900s.",
                1842));
            Register(new CropVariety("scotch-oats", CropKind.Oats, "Scotch Oats",
                "landrace", "Scottish settler stock",
                "The common landrace oat of Upper Canada — farmer-selected season to season, not a bred line.",
                0));
            Register(new CropVariety("yellow-dent", CropKind.Corn, "Yellow Dent",
                "landrace", "American dent landraces",
                "The standard dent field corn of the 1800s — farm-selected yellow dent types.",
                0));
            Register(new CropVariety("timothy", CropKind.Hay, "Timothy",
                "promoter", "Timothy Hanson",
                "Timothy grass (Phleum pratense), the standard hay grass of 19th-century Ontario; " +
                "named for Timothy Hanson, who promoted it in the 1700s.",
                0));
        }

        /// <summary>Registers (or replaces) a variety. Returns a diagnostic on rejection.</summary>
        public static string Register(CropVariety variety)
        {
            if (variety == null) return "CropVarietyCatalog: cannot register a null variety.";
            if (string.IsNullOrWhiteSpace(variety.VarietyId))
                return "CropVarietyCatalog: variety needs an id.";
            if (variety.Crop == CropKind.Unspecified)
                return $"CropVarietyCatalog: variety '{variety.VarietyId}' names no crop kind.";
            if (string.IsNullOrWhiteSpace(variety.OriginatorName))
                return $"CropVarietyCatalog: variety '{variety.VarietyId}' must name its originator — no anonymous varieties.";
            varieties[variety.VarietyId] = variety;
            return null;
        }

        public static CropVariety Get(string varietyId)
        {
            if (string.IsNullOrWhiteSpace(varietyId)) return null;
            CropVariety variety;
            return varieties.TryGetValue(varietyId, out variety) ? variety : null;
        }

        public static IReadOnlyCollection<CropVariety> All => varieties.Values;

        public static List<CropVariety> VarietiesForCrop(CropKind crop)
        {
            var result = new List<CropVariety>();
            foreach (var variety in varieties.Values)
            {
                if (variety.Crop == crop) result.Add(variety);
            }
            return result;
        }

        /// <summary>Display name for a variety id, or "variety unrecorded" for the sentinel/unknown ids.</summary>
        public static string DisplayNameOf(string varietyId)
        {
            CropVariety variety = Get(varietyId);
            return variety != null ? variety.DisplayName : "variety unrecorded";
        }
    }
}
