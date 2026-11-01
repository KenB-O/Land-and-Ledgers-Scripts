using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: sawmill conversion ratios as DATA, never code paths. One profile
    /// per timber family: how many lumber units and how many slab/offcut
    /// fuel-wood units a log yields, how long the sawing takes, and how much
    /// blade the log costs. Canon §8.5A: the causal chain distinguishes
    /// species/sawing behavior; §8.4 keeps byproducts practical (slabs and
    /// offcuts as fuel wood only — no sawdust economy until it materially
    /// improves the simulation).
    ///
    /// The white-pine profile matches the authored T1E calibration exactly
    /// (4 lumber units per log, 30 saw-minutes per log) — T1E remains the
    /// authority; this data table only names it.
    /// </summary>
    [Serializable]
    public sealed class SawmillConversionProfile
    {
        public string ProfileId = string.Empty;   // e.g. "softwood-standard"
        public string DisplayName = string.Empty;
        public string SpeciesMatch = string.Empty; // species this profile serves, e.g. "white pine"
        public int LumberPerLog;                  // lumber units sawn from one log
        public int FuelWoodUnitsPerLog;           // slab/offcut fuel-wood units from one log
        public int SawMinutesPerLog;              // labor calibration per log
        public float BladeWearPerLog01;           // saw-blade wear per log (0..1 scale)

        public SawmillConversionProfile() { }

        public SawmillConversionProfile(
            string profileId, string displayName, string speciesMatch,
            int lumberPerLog, int fuelWoodUnitsPerLog, int sawMinutesPerLog, float bladeWearPerLog01)
        {
            ProfileId = profileId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            SpeciesMatch = speciesMatch ?? string.Empty;
            LumberPerLog = Math.Max(1, lumberPerLog);
            FuelWoodUnitsPerLog = Math.Max(0, fuelWoodUnitsPerLog);
            SawMinutesPerLog = Math.Max(1, sawMinutesPerLog);
            BladeWearPerLog01 = Mathf.Clamp01(Math.Max(0f, bladeWearPerLog01));
        }
    }

    /// <summary>
    /// W4A: the authored conversion-ratio table. Species-specific because
    /// sawing hardwood is historically slower and harder on the blade than
    /// white pine; the table stays data so the proprietor (or a later
    /// package) can tune it without touching conversion code.
    /// </summary>
    public static class SawmillConversionData
    {
        private static readonly List<SawmillConversionProfile> defaultProfiles = new List<SawmillConversionProfile>
        {
            // White pine: easy-sawing softwood. Matches T1E authored
            // calibration (TimberChain.Sawmill: 4 lumber/log, 30 min/log).
            new SawmillConversionProfile(
                "softwood-standard", "Softwood milling (white pine)", "white pine",
                lumberPerLog: 4, fuelWoodUnitsPerLog: 2, sawMinutesPerLog: 30, bladeWearPerLog01: 0.04f),
            // Oak and other hardwoods: historically slower feed, more blade
            // wear, filed more often; denser logs cut heavier boards.
            new SawmillConversionProfile(
                "hardwood-standard", "Hardwood milling (oak and kin)", "oak",
                lumberPerLog: 5, fuelWoodUnitsPerLog: 2, sawMinutesPerLog: 45, bladeWearPerLog01: 0.08f),
        };

        public static IReadOnlyList<SawmillConversionProfile> DefaultProfiles => defaultProfiles;

        /// <summary>
        /// Finds the profile for a species (case-insensitive substring
        /// match); falls back to the softwood profile — never null, never
        /// an invented yield: the fallback IS named and inspectable.
        /// </summary>
        public static SawmillConversionProfile DefaultForSpecies(string species)
        {
            if (!string.IsNullOrWhiteSpace(species))
            {
                foreach (var profile in defaultProfiles)
                {
                    if (!string.IsNullOrWhiteSpace(profile.SpeciesMatch)
                        && species.IndexOf(profile.SpeciesMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return profile;
                    }
                }
            }

            return defaultProfiles[0];
        }
    }
}
