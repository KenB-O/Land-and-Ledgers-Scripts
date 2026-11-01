using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Creation;
using EntityId = LandLedgers.Primitives.EntityId;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Bootstrap
{
    /// <summary>
    /// FVS-1: the materialized farm. Animals carry HF-1 EntityIds in the HF-2
    /// registry; the layout is the spatial handoff for the future journey model.
    /// LocationBuildingId on each animal is the index into the definition's
    /// Buildings list (documented convention, not a global building authority).
    /// </summary>
    [Serializable]
    public sealed class FarmInstance
    {
        public string FarmId = string.Empty;
        public string DisplayName = string.Empty;
        public int HouseholdId = -1;
        public List<EntityId> AnimalIds = new List<EntityId>();
        public FarmLayout Layout = new FarmLayout();
        public PremisesResolution DairyPremises;
        public string DairyPremisesNotes = string.Empty;
        public int BootstrapDayIndex;
        public int RandomSeedUsed;

        public int CountSpecies(AnimalRegistry registry, AnimalSpecies species)
        {
            if (registry == null) return 0;
            int count = 0;
            foreach (var id in AnimalIds)
            {
                var animal = registry.GetAnimal(id);
                if (animal != null && animal.IsActive && animal.Species == species)
                {
                    count++;
                }
            }
            return count;
        }
    }

    /// <summary>
    /// FVS-1: materializes an authored <see cref="FarmDefinition"/> into the live
    /// simulation. Loud on invalid input (returns null and reports why); never
    /// invents land, buildings, or animals the definition did not authorize.
    ///
    /// Seeded randomness: a System.Random built from FarmDefinition.RandomSeed
    /// decides WHICH individuals begin lactating (the count is authored), plus
    /// yield-variance and laying-state seeds consumed later by FVS-2. Same seed +
    /// same definition = same farm, every time.
    /// </summary>
    public static class FarmBootstrap
    {
        /// <summary>
        /// Bootstraps the farm. Returns the instance, or null with diagnostics
        /// explaining why the definition was refused.
        /// </summary>
        public static FarmInstance Bootstrap(
            FarmDefinition definition,
            AnimalRegistry animalRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();

            if (definition == null)
            {
                diagnostics.Add("FarmBootstrap: no FarmDefinition supplied.");
                return null;
            }

            if (animalRegistry == null)
            {
                diagnostics.Add($"FarmBootstrap: no AnimalRegistry — cannot register the '{definition.FarmId}' herd.");
                return null;
            }

            List<string> problems = definition.Validate();
            if (problems.Count > 0)
            {
                diagnostics.Add($"FarmBootstrap: definition '{definition.FarmId}' failed validation ({problems.Count} problem(s)):");
                diagnostics.AddRange(problems);
                return null;
            }

            var rng = new System.Random(definition.RandomSeed);
            var instance = new FarmInstance
            {
                FarmId = definition.FarmId,
                DisplayName = definition.DisplayName,
                HouseholdId = definition.HouseholdId,
                BootstrapDayIndex = dayIndex,
                RandomSeedUsed = definition.RandomSeed,
            };

            instance.Layout = FarmLayout.BuildFromDefinition(definition);

            // Premises via the BIZ-1 capability-driven resolver (Tech X §3.2):
            // dairy work needs animal housing + food handling. Requirements win;
            // the assigned building is the dairy-suitable barn's definition index.
            int dairyBarnIndex = FindDairyBarnIndex(definition);
            var dairyRequirement = new PremisesRequirement(
                customerFacing: false,
                workshop: false,
                animalHousing: true,
                yardStorage: false,
                foodHandling: true);
            var premisesDiagnostics = new List<string>();
            instance.DairyPremises = PremisesResolver.Resolve(
                dairyRequirement,
                PremisesPreference.PreferHomeBased,
                dairyBarnIndex,
                premisesDiagnostics);
            instance.DairyPremisesNotes = string.Join(" ", premisesDiagnostics);
            foreach (string d in premisesDiagnostics)
            {
                diagnostics.Add("Premises: " + d);
            }

            // Register the herd/flock. Owner is the farm business (AnimalOwnerKind.Business);
            // the household linkage lives on the definition and the instance.
            string ownerId = "farm:" + definition.FarmId;
            foreach (var line in definition.InitialHerd)
            {
                if (line == null || line.HeadCount <= 0) continue;

                // Seeded shuffle: which individuals begin lactating (count authored).
                var indices = new List<int>(line.HeadCount);
                for (int i = 0; i < line.HeadCount; i++) indices.Add(i);
                for (int i = indices.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    int tmp = indices[i];
                    indices[i] = indices[j];
                    indices[j] = tmp;
                }
                var lactatingSet = new HashSet<int>();
                int lactatingCount = Math.Min(Math.Max(0, line.LactatingAtStart), line.HeadCount);
                for (int k = 0; k < lactatingCount; k++) lactatingSet.Add(indices[k]);

                int buildingIndex = BuildingIndexForSpecies(definition, line.Species);

                for (int i = 0; i < line.HeadCount; i++)
                {
                    AnimalState animal = animalRegistry.RegisterAnimal(
                        line.Species,
                        line.Breed,
                        line.Sex,
                        AnimalOwnerKind.Business,
                        ownerId,
                        dayIndex,
                        $"farm bootstrap '{definition.FarmId}'");

                    if (animal == null)
                    {
                        diagnostics.Add($"FarmBootstrap: registry refused animal {i + 1}/{line.HeadCount} of {line.Species} — see registry diagnostics.");
                        continue;
                    }

                    // Canon §3.4: lactation is a STATE. Heifers stay heifers until
                    // first calving — the registry's Juvenile default is corrected
                    // here from the authored starting state.
                    if (lactatingSet.Contains(i))
                    {
                        animal.BiologicalState = AnimalBiologicalState.Lactating;
                    }
                    else
                    {
                        animal.BiologicalState = line.StartingState;
                    }

                    animal.Parity = Math.Max(0, line.MinParity);
                    if (animal.BiologicalState == AnimalBiologicalState.Lactating && animal.Parity < 1)
                    {
                        // A lactating animal has calved at least once (Tech X §4.5).
                        animal.Parity = 1;
                    }

                    animal.LocationBuildingId = buildingIndex;
                    animal.CommercialStatus = AnimalCommercialStatus.BreedingStock;
                    instance.AnimalIds.Add(animal.AnimalId);
                }
            }

            diagnostics.Add($"FarmBootstrap: '{definition.FarmId}' materialized with {instance.AnimalIds.Count} animals (seed {definition.RandomSeed}).");
            return instance;
        }

        private static int FindDairyBarnIndex(FarmDefinition definition)
        {
            if (definition.Buildings == null) return -1;
            for (int i = 0; i < definition.Buildings.Count; i++)
            {
                var b = definition.Buildings[i];
                if (b == null) continue;
                if (b.Kind == FarmBuildingKind.DairyBarn || (b.Kind == FarmBuildingKind.Barn && b.DairySuitable))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int BuildingIndexForSpecies(FarmDefinition definition, AnimalSpecies species)
        {
            if (definition.Buildings == null) return -1;
            FarmBuildingKind want = FarmBuildingKind.Barn;
            if (species == AnimalSpecies.Chicken) want = FarmBuildingKind.Coop;
            else if (species == AnimalSpecies.Cattle) want = FarmBuildingKind.DairyBarn;

            for (int i = 0; i < definition.Buildings.Count; i++)
            {
                var b = definition.Buildings[i];
                if (b != null && b.Kind == want) return i;
            }
            // Fall back to any barn for cattle, any building at all otherwise.
            for (int i = 0; i < definition.Buildings.Count; i++)
            {
                var b = definition.Buildings[i];
                if (b != null && b.Kind == FarmBuildingKind.Barn) return i;
            }
            return definition.Buildings.Count > 0 ? 0 : -1;
        }
    }
}
