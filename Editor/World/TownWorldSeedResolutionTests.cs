using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class TownWorldSeedResolutionTests
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void RandomFreshRuntimeWorldUsesSuppliedRandomSeed()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.RandomFreshRuntimeWorld;
            settings.useManualSeedOverride = false;

            TownWorldController controller = CreateController(settings);
            try
            {
                InvokeFreshSeedResolution(controller, runtimeFreshGeneration: true, randomCandidateSeed: 456789);

                Assert.That(settings.seed, Is.EqualTo(456789));
                Assert.That(controller.LastSeedResolution.StartupMode, Is.EqualTo(WorldGenerationStartupMode.RandomFreshRuntimeWorld));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Generated Random Seed"));
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void FixedTestingWorldUsesFixedTestSeedAndProfile()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.FixedTestingWorld;
            settings.fixedTestingProfileName = "Default QA Town";
            settings.fixedTestingWorldSeed = 1897;

            TownWorldController controller = CreateController(settings);
            try
            {
                InvokeFreshSeedResolution(controller, runtimeFreshGeneration: true, randomCandidateSeed: 456789);

                Assert.That(settings.seed, Is.EqualTo(1897));
                Assert.That(controller.LastSeedResolution.StartupMode, Is.EqualTo(WorldGenerationStartupMode.FixedTestingWorld));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Fixed Test Profile"));
                Assert.That(controller.LastSeedResolution.ProfileName, Is.EqualTo("Default QA Town"));
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void ManualSeedOverrideWinsForFreshGeneration()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.RandomFreshRuntimeWorld;
            settings.useManualSeedOverride = true;
            settings.manualSeedOverride = 1901;

            TownWorldController controller = CreateController(settings);
            try
            {
                InvokeFreshSeedResolution(controller, runtimeFreshGeneration: true, randomCandidateSeed: 456789);

                Assert.That(settings.seed, Is.EqualTo(1901));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Manual Seed Override"));
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void FreshGenerationPreservesSerializedSeedOutsideRuntimeStartup()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.RandomFreshRuntimeWorld;
            settings.useManualSeedOverride = false;

            TownWorldController controller = CreateController(settings);
            try
            {
                InvokeFreshSeedResolution(controller, runtimeFreshGeneration: false, randomCandidateSeed: 456789);

                Assert.That(settings.seed, Is.EqualTo(1886));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Serialized Settings"));
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void InvalidFixedTestingSeedFallsBackDeterministicallyWithoutRandomCandidate()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.FixedTestingWorld;
            settings.fixedTestingProfileName = "Broken QA Profile";
            settings.fixedTestingWorldSeed = 0;

            TownWorldController controller = CreateController(settings);
            try
            {
                InvokeFreshSeedResolution(controller, runtimeFreshGeneration: true, randomCandidateSeed: 456789);

                Assert.That(settings.seed, Is.EqualTo(1886));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Fixed Test Profile Fallback"));
                Assert.That(controller.LastSeedResolution.UsedFallback, Is.True);
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void SaveLoadAppliesSavedSeedWithoutFreshReroll()
        {
            TownGenerationSettings settings = CreateSettings(seed: 1886);
            settings.startupMode = WorldGenerationStartupMode.RandomFreshRuntimeWorld;
            settings.useManualSeedOverride = false;

            TownWorldController controller = CreateController(settings);
            try
            {
                WorldSaveDto dto = new()
                {
                    settingsName = "Loaded Seed Test",
                    seed = 234567,
                    gridWidthCells = 24,
                    gridDepthCells = 24,
                    cellSizeMeters = 2f,
                    gridOriginX = -24f,
                    gridOriginY = 0f,
                    gridOriginZ = -24f
                };

                bool loaded = controller.LoadFromSaveDto(dto, new SaveReferenceResolver(controller), out string message);

                Assert.That(loaded, Is.True, message);
                Assert.That(settings.seed, Is.EqualTo(234567));
                Assert.That(controller.LastSeedResolution.SeedSource, Is.EqualTo("Save Data"));
            }
            finally
            {
                Destroy(controller, settings);
            }
        }

        [Test]
        public void ChildSeedDerivationIsStableForSameMasterAndCategory()
        {
            int first = WorldSeedAuthority.DeriveChildSeed(1886, "Terrain");
            int second = WorldSeedAuthority.DeriveChildSeed(1886, "Terrain");

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.GreaterThan(0));
        }

        [Test]
        public void ChildSeedDerivationSeparatesCategories()
        {
            int terrain = WorldSeedAuthority.DeriveChildSeed(1886, "Terrain");
            int roads = WorldSeedAuthority.DeriveChildSeed(1886, "Roads");

            Assert.That(terrain, Is.Not.EqualTo(roads));
        }

        [Test]
        public void ChildSeedDerivationChangesWithMasterSeed()
        {
            int first = WorldSeedAuthority.DeriveChildSeed(1886, "Terrain");
            int second = WorldSeedAuthority.DeriveChildSeed(1901, "Terrain");

            Assert.That(first, Is.Not.EqualTo(second));
        }

        private static void InvokeFreshSeedResolution(
            TownWorldController controller,
            bool runtimeFreshGeneration,
            int randomCandidateSeed)
        {
            MethodInfo method = typeof(TownWorldController).GetMethod("ResolveSeedForFreshGeneration", InstancePrivate);
            Assert.NotNull(method, "TownWorldController should expose a focused fresh-generation seed resolver.");
            method.Invoke(controller, new object[] { runtimeFreshGeneration, randomCandidateSeed });
        }

        private static TownWorldController CreateController(TownGenerationSettings settings)
        {
            GameObject root = new("Town World Seed Resolution Test");
            TownWorldController controller = root.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);
            return controller;
        }

        private static TownGenerationSettings CreateSettings(int seed)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.seed = seed;
            settings.gridWidthCells = 24;
            settings.gridDepthCells = 24;
            settings.cellSizeMeters = 2f;
            settings.generateRegionalFoundation = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateTownHall = false;
            settings.generateDetailProps = false;
            settings.generateForestEnvironmentDressing = false;
            settings.Sanitize();
            return settings;
        }

        private static void Destroy(TownWorldController controller, TownGenerationSettings settings)
        {
            if (controller != null)
            {
                Object.DestroyImmediate(controller.gameObject);
            }

            if (settings != null)
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
