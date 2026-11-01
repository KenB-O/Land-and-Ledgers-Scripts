using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.GrainMill
{
    /// <summary>
    /// W5A: one grist conversion profile as DATA, never code paths. One row
    /// per millable grain kind: which main product the grind yields
    /// (wheat -> flour, corn -> meal, oats -> feed), the per-10-unit split
    /// of main product / mill feed / bran, the labor calibration, and how
    /// hard the grain is on the millstones.
    ///
    /// The wheat split (7 flour / 2 feed / 1 bran per 10 units) names the
    /// CRP-3 Miller calibration exactly (Canon R6; Canon Part XV) — the
    /// CRP Miller stays the authority for the raw milling ratios; this
    /// table only names them per crop and adds the main-product kind.
    /// </summary>
    [Serializable]
    public sealed class GristMillConversionProfile
    {
        public CropKind CropMatch = CropKind.Unspecified; // the grain kind this row serves
        public string DisplayName = string.Empty;
        public string MainProductKind = string.Empty; // "flour", "meal", "feed"
        public int MainUnitsPer10;                    // main product units per 10 grain units
        public int FeedUnitsPer10;                    // mill-feed units per 10 grain units
        public int BranUnitsPer10;                    // bran units per 10 grain units
        public int MillMinutesPer100Units;            // labor calibration per 100 grain units
        public float StoneWearPer100Units01;          // millstone wear per 100 grain units (0..1 scale)

        public GristMillConversionProfile() { }

        public GristMillConversionProfile(
            CropKind cropMatch, string displayName, string mainProductKind,
            int mainUnitsPer10, int feedUnitsPer10, int branUnitsPer10,
            int millMinutesPer100Units, float stoneWearPer100Units01)
        {
            CropMatch = cropMatch;
            DisplayName = displayName ?? string.Empty;
            MainProductKind = mainProductKind ?? string.Empty;
            MainUnitsPer10 = Math.Max(0, mainUnitsPer10);
            FeedUnitsPer10 = Math.Max(0, feedUnitsPer10);
            BranUnitsPer10 = Math.Max(0, branUnitsPer10);
            MillMinutesPer100Units = Math.Max(1, millMinutesPer100Units);
            StoneWearPer100Units01 = Mathf.Clamp01(Math.Max(0f, stoneWearPer100Units01));
        }
    }

    /// <summary>
    /// W5A: the authored grist conversion table. Wheat is the bread grain
    /// (flour, the bakery's input — recurring template
    /// grain_mill_flour_to_bakery). Corn is the meal grain (food and feed;
    /// cornmeal is a staple food in its own right — it sells through the
    /// mill's own outlet, not the bakery's flour bin, which takes only
    /// "flour" mill lots). Oats grind straight to feed (Canon: oats are the
    /// feed grain, CRP-1). Hay is not a grain and has no row — grinding hay
    /// is refused loudly at the stock.
    /// </summary>
    public static class GristMillConversionData
    {
        private static readonly List<GristMillConversionProfile> defaultProfiles = new List<GristMillConversionProfile>
        {
            // Wheat: the bread grain. Matches the CRP-3 Miller calibration
            // (7/2/1 per 10) exactly — CRP stays the authority.
            new GristMillConversionProfile(
                CropKind.Wheat, "Wheat grist (flour)", "flour",
                mainUnitsPer10: 7, feedUnitsPer10: 2, branUnitsPer10: 1,
                millMinutesPer100Units: 60, stoneWearPer100Units01: 0.03f),
            // Corn: the meal grain. Same 7/2/1 split as wheat (CRP naming);
            // slightly harder on stones than wheat.
            new GristMillConversionProfile(
                CropKind.Corn, "Corn grist (meal)", "meal",
                mainUnitsPer10: 7, feedUnitsPer10: 2, branUnitsPer10: 1,
                millMinutesPer100Units: 60, stoneWearPer100Units01: 0.04f),
            // Oats: the feed grain. Ground oats go straight to feed; the
            // hull fraction becomes bran. Calibration (Canon Part XV).
            new GristMillConversionProfile(
                CropKind.Oats, "Oat grist (feed)", "feed",
                mainUnitsPer10: 9, feedUnitsPer10: 0, branUnitsPer10: 1,
                millMinutesPer100Units: 45, stoneWearPer100Units01: 0.02f),
        };

        public static IReadOnlyList<GristMillConversionProfile> DefaultProfiles => defaultProfiles;

        /// <summary>
        /// Finds the profile for a crop kind. Returns null when the kind has
        /// no row (e.g. hay) — the caller refuses loudly rather than milling
        /// an unprofiled grain.
        /// </summary>
        public static GristMillConversionProfile ProfileFor(CropKind crop)
        {
            foreach (var profile in defaultProfiles)
            {
                if (profile.CropMatch == crop) return profile;
            }
            return null;
        }
    }
}
