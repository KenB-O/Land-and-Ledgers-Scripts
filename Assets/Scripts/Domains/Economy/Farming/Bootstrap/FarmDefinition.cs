using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Bootstrap
{
    /// <summary>
    /// FVS-1: the kind of rural land control on a farm (Tech X §9.1). These are
    /// distinct rights — they must never be summed into one "Owned Acres" number
    /// (Tech X §9.4; Canon §10.3 CANON LOCK on open range).
    /// </summary>
    public enum FarmLandInterestKind
    {
        Unspecified = 0,
        FeeOwned = 1,        // full ownership; persists until valid disposition
        Lease = 2,           // term right; ends per agreement
        SeasonalGrazing = 3, // seasonal agreement; insecure outside its term
        OpenRangeUse = 4,    // nonexclusive/tolerated use; NOT ownership, NOT capacity (Canon §10.3)
    }

    /// <summary>One parcel-level land interest held by the farm.</summary>
    [Serializable]
    public sealed class FarmLandInterest
    {
        public FarmLandInterestKind Kind;
        public float Acres;
        public string Description = string.Empty; // e.g. "river quarter, deeded 1871"
        public int ExpiryDayIndex = -1;           // -1 = no expiry (fee owned)

        public FarmLandInterest() { }

        public FarmLandInterest(FarmLandInterestKind kind, float acres, string description = "")
        {
            Kind = kind;
            Acres = acres;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>Building kinds a farm may carry. DairyBarn matters: a milking herd
    /// without one fails validation (Canon Part IX dairy equipment/workspace).</summary>
    public enum FarmBuildingKind
    {
        Unspecified = 0,
        Farmhouse = 1,
        DairyBarn = 2,  // milking suitability: stalls, stanchions, milk handling space
        Barn = 3,       // general barn; may or may not suit milking
        Coop = 4,       // poultry housing
        Shed = 5,       // equipment/storage
        Well = 6,       // water
    }

    [Serializable]
    public sealed class FarmBuilding
    {
        public FarmBuildingKind Kind;
        public string Name = string.Empty;   // e.g. "north dairy barn"
        public int CapacityHead;             // rough head capacity (calibration, not canon)
        public bool DairySuitable;           // true for DairyBarn; Barn only if equipped
        public string Condition = string.Empty;

        public FarmBuilding() { }

        public FarmBuilding(FarmBuildingKind kind, string name, int capacityHead, bool dairySuitable = false)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            CapacityHead = capacityHead;
            DairySuitable = dairySuitable || kind == FarmBuildingKind.DairyBarn;
        }
    }

    public enum FarmPlotKind
    {
        Unspecified = 0,
        Pasture = 1,   // grazing
        HayField = 2,  // winter feed production
        CropField = 3, // feed grain / cash crop
        Garden = 4,    // household use
    }

    [Serializable]
    public sealed class FarmPlot
    {
        public FarmPlotKind Kind;
        public float Acres;
        public string Name = string.Empty;
        public string CurrentUse = string.Empty; // e.g. "timothy hay", "oats for feed"

        public FarmPlot() { }

        public FarmPlot(FarmPlotKind kind, float acres, string name = "", string currentUse = "")
        {
            Kind = kind;
            Acres = acres;
            Name = name ?? string.Empty;
            CurrentUse = currentUse ?? string.Empty;
        }
    }

    /// <summary>
    /// One authored animal line: "6 dairy cows". Individuals are materialized by
    /// the bootstrap with seeded randomness (FVS-1 policy below). Canon §3.4:
    /// pregnancy and lactation are STATES, not types — LactatingAtStart is a
    /// starting state, applied to individuals at bootstrap.
    /// </summary>
    [Serializable]
    public sealed class AuthoredAnimal
    {
        public AnimalSpecies Species;
        public AnimalSex Sex = AnimalSex.Female;
        public string Breed = string.Empty;
        public int HeadCount;
        public AnimalBiologicalState StartingState = AnimalBiologicalState.EligibleOrCycling;
        public int LactatingAtStart;  // of the head count, how many begin lactating (dairy)
        public int MinParity;         // parity floor (Tech X §4.5: stored independent of state)
        public string Notes = string.Empty;

        public AuthoredAnimal() { }

        public AuthoredAnimal(AnimalSpecies species, AnimalSex sex, int headCount,
            AnimalBiologicalState startingState, int lactatingAtStart = 0, string breed = "")
        {
            Species = species;
            Sex = sex;
            HeadCount = headCount;
            StartingState = startingState;
            LactatingAtStart = lactatingAtStart;
            Breed = breed ?? string.Empty;
        }
    }

    /// <summary>
    /// FVS-1: the per-scenario authored farm definition. Kennedy authors these
    /// behind the scenes (scenario asset / in-editor); the bootstrap validates
    /// loudly and materializes deterministically.
    ///
    /// SEEDED-RANDOMNESS POLICY (explicit):
    ///   AUTHORED (Kennedy sets): farm id/name, scenario link, household link,
    ///     land interests by kind and acres, buildings, plots, head counts by
    ///     species/sex/state, equipment, starting stores, random seed.
    ///   SEEDED-RANDOM (from RandomSeed): WHICH individual cows begin lactating
    ///     (count is authored via LactatingAtStart), milk-yield variance,
    ///     hen laying state, calf sex ratios where calves are authored.
    /// The seed is stored on the definition, so a scenario reproduces exactly.
    /// </summary>
    [Serializable]
    public sealed class FarmDefinition
    {
        /// <summary>
        /// Calibration, not canon: rough pasture carrying capacity used only for
        /// validation warnings. Real carrying capacity emerges from pasture,
        /// season, and feed state (Canon §7.2: cash alone does not create it).
        /// </summary>
        public const float PastureAcresPerCow = 2.0f;

        public string FarmId = string.Empty;
        public string DisplayName = string.Empty;
        public string ScenarioId = string.Empty; // owning scenario; "" = any
        public int HouseholdId = -1;            // operating household (HF-3)
        public string LocationDescription = string.Empty; // e.g. "3 miles north of town on the river road"
        public float DistanceToTownMiles = -1f;  // -1 = unknown; Tech X §12.1: 1:1 distance when set

        public List<FarmLandInterest> LandInterests = new List<FarmLandInterest>();
        public List<FarmBuilding> Buildings = new List<FarmBuilding>();
        public List<FarmPlot> Plots = new List<FarmPlot>();
        public List<AuthoredAnimal> InitialHerd = new List<AuthoredAnimal>();

        public List<string> Equipment = new List<string>(); // e.g. "churn", "milk pails x4", "wagon"
        public int StartingFeedUnits;      // feed on hand at bootstrap (FVS-4 feed loop consumes this)
        public int RandomSeed = 1;

        public string Notes = string.Empty;

        /// <summary>
        /// Human-readable validation. Empty = valid. The bootstrap refuses to
        /// materialize an invalid definition and reports these (loud, not silent).
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(FarmId))
            {
                problems.Add("FarmId is empty. Set a unique id before bootstrapping.");
            }

            if (HouseholdId <= 0)
            {
                problems.Add($"Farm '{FarmId}': HouseholdId is not set. A farm must be linked to an operating household (HF-3).");
            }

            float controlledAcres = 0f;
            if (LandInterests != null)
            {
                foreach (var interest in LandInterests)
                {
                    if (interest == null) continue;
                    if (interest.Acres <= 0f)
                    {
                        problems.Add($"Farm '{FarmId}': land interest '{interest.Description}' has no acres.");
                        continue;
                    }
                    // Tech X §9.4: categories stay separate; only fee/lease count as controlled.
                    if (interest.Kind == FarmLandInterestKind.FeeOwned || interest.Kind == FarmLandInterestKind.Lease)
                    {
                        controlledAcres += interest.Acres;
                    }
                    if (interest.Kind == FarmLandInterestKind.OpenRangeUse)
                    {
                        problems.Add($"Farm '{FarmId}': open-range use ('{interest.Description}') is not secure carrying capacity (Canon §10.3). It is recorded but never counted toward the farm's land base.");
                    }
                }
            }

            if (controlledAcres <= 0f)
            {
                problems.Add($"Farm '{FarmId}': no fee-owned or leased acres. A farm needs secure land control (Tech X §9.1).");
            }

            int dairyCows = 0;
            int chickens = 0;
            int cattleHead = 0;
            bool otherSpecies = false;
            if (InitialHerd != null)
            {
                foreach (var line in InitialHerd)
                {
                    if (line == null || line.HeadCount <= 0) continue;
                    if (line.Species == AnimalSpecies.Cattle && line.Sex == AnimalSex.Female)
                    {
                        cattleHead += line.HeadCount;
                        dairyCows += Math.Max(0, line.LactatingAtStart);
                        if (line.StartingState == AnimalBiologicalState.Lactating)
                        {
                            dairyCows += line.HeadCount;
                        }
                    }
                    else if (line.Species == AnimalSpecies.Cattle)
                    {
                        cattleHead += line.HeadCount;
                    }
                    else if (line.Species == AnimalSpecies.Chicken)
                    {
                        chickens += line.HeadCount;
                    }
                    else
                    {
                        otherSpecies = true;
                    }

                    if (line.LactatingAtStart > line.HeadCount)
                    {
                        problems.Add($"Farm '{FarmId}': LactatingAtStart ({line.LactatingAtStart}) exceeds HeadCount ({line.HeadCount}) for {line.Species}.");
                    }
                }
            }

            bool hasDairyBarn = false;
            bool hasCoop = false;
            if (Buildings != null)
            {
                foreach (var b in Buildings)
                {
                    if (b == null) continue;
                    if (b.Kind == FarmBuildingKind.DairyBarn || (b.Kind == FarmBuildingKind.Barn && b.DairySuitable))
                    {
                        hasDairyBarn = true;
                    }
                    if (b.Kind == FarmBuildingKind.Coop)
                    {
                        hasCoop = true;
                    }
                }
            }

            if (dairyCows > 0 && !hasDairyBarn)
            {
                problems.Add($"Farm '{FarmId}': milking herd of {dairyCows} requires a dairy barn or dairy-suitable barn — add a FarmBuilding of kind DairyBarn (Canon Part IX: butter requires workspace/equipment).");
            }

            if (chickens > 0 && !hasCoop)
            {
                problems.Add($"Farm '{FarmId}': flock of {chickens} chickens requires a coop (FarmBuildingKind.Coop).");
            }

            float pastureAcres = 0f;
            bool hasHayField = false;
            if (Plots != null)
            {
                foreach (var p in Plots)
                {
                    if (p == null) continue;
                    if (p.Kind == FarmPlotKind.Pasture) pastureAcres += p.Acres;
                    if (p.Kind == FarmPlotKind.HayField && p.Acres > 0f) hasHayField = true;
                }
            }

            if (cattleHead > 0 && pastureAcres < cattleHead * PastureAcresPerCow)
            {
                problems.Add($"Farm '{FarmId}': {cattleHead} cattle on {pastureAcres} pasture acres — below the {PastureAcresPerCow} acres/head calibration guide. Add pasture, arrange grazing rights, or reduce the herd (Canon §7.2: pasture is a binding constraint).");
            }

            if (cattleHead > 0 && !hasHayField && StartingFeedUnits <= 0)
            {
                problems.Add($"Farm '{FarmId}': cattle with no hay field and no starting feed stores. Winter feed must come from somewhere — add a hay field, starting feed units, or a feed purchase arrangement (FVS-4 feed loop).");
            }

            if (otherSpecies)
            {
                problems.Add($"Farm '{FarmId}': non cattle/chicken species are registered but have no production chains in this slice — they will exist as animals only (FVS scope: cows and chickens).");
            }

            return problems;
        }

        /// <summary>Tech X §9.4 read model: controlled acres, by category. Never summed.</summary>
        public void GetAcreageByCategory(out float owned, out float leased, out float seasonalGrazing, out float openRange)
        {
            owned = 0f; leased = 0f; seasonalGrazing = 0f; openRange = 0f;
            if (LandInterests == null) return;
            foreach (var interest in LandInterests)
            {
                if (interest == null) continue;
                switch (interest.Kind)
                {
                    case FarmLandInterestKind.FeeOwned: owned += interest.Acres; break;
                    case FarmLandInterestKind.Lease: leased += interest.Acres; break;
                    case FarmLandInterestKind.SeasonalGrazing: seasonalGrazing += interest.Acres; break;
                    case FarmLandInterestKind.OpenRangeUse: openRange += interest.Acres; break;
                }
            }
        }
    }
}
