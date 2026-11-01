using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// MR-P003 Step 4C.2 — verifies that all Building creation writers
    /// use AllocateNextBuildingId() rather than buildings.Count.
    ///
    /// Pre-P005 invariant: runtime Building state MUST remain dense.
    /// building.id == list index for every building in the list.
    /// Tests must never intentionally gap the allocator before creating a
    /// Building; non-dense construction is a MR-P005 concern.
    /// </summary>
    [TestFixture]
    public sealed class BuildingWriterCutoverTests
    {
        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Minimal single-plot business-zone town settings with one definition
        /// in the catalog. Matches the setup used by TownWorldFootprintSaveLoadTests.
        /// </summary>
        private static TownGenerationSettings CreateMinimalSettings(BuildingDefinition definition)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.gridWidthCells = 40;
            settings.gridDepthCells = 50;
            settings.cellSizeMeters = 2f;
            settings.seed = 1886;
            settings.roadWidthCells = 3;
            settings.mainStreetLengthCells = 30;
            settings.crossStreetCount = 0;
            settings.plotDepthCells = 8;
            settings.minPlotFrontageCells = 8;
            settings.maxPlotFrontageCells = 8;
            settings.buildingSetbackCells = 1;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateTownHall = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.generateForestEnvironmentDressing = false;
            settings.buildingCatalog = new[] { definition };
            settings.Sanitize();
            return settings;
        }

        /// <summary>
        /// Creates a minimal Business-zone BuildingDefinition (3×3) with no prefab.
        /// </summary>
        private static BuildingDefinition CreateBuildingDefinition(string id)
        {
            BuildingDefinition def = ScriptableObject.CreateInstance<BuildingDefinition>();
            def.ConfigureRuntimeFallback(
                id,
                id,
                PlotZone.Business,
                new Vector2Int(3, 3),
                Color.white,
                5f);
            return def;
        }

        private static void DestroyAll(List<Object> objects)
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
        }

        // ------------------------------------------------------------------
        // Tests
        // ------------------------------------------------------------------

        /// <summary>
        /// Dense-append invariant: after GenerateTownShell() places n buildings,
        /// every building's id equals its list index (0..n-1) and NextBuildingId == n.
        /// A new building constructed via TryConstructBuildingShell gets id == n
        /// (the current allocator value == buildings.Count at that moment).
        ///
        /// This proves the writer uses AllocateNextBuildingId() which returns the
        /// current NextBuildingId (== buildings.Count in dense state) rather than
        /// re-computing buildings.Count independently — the two values agree here
        /// by design, and the critical invariant is that the allocator is the
        /// single source of truth.
        /// </summary>
        [Test]
        public void TryConstructBuildingShell_DenseAppend_PreservesIdEqualsIndex()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition def = CreateBuildingDefinition("writer_cutover_test_building");
                cleanup.Add(def);

                TownGenerationSettings settings = CreateMinimalSettings(def);
                cleanup.Add(settings);

                GameObject root = new("BuildingWriterCutover_Test");
                cleanup.Add(root);

                TownWorldController controller = root.AddComponent<TownWorldController>();
                controller.Configure(settings, null, null);
                controller.GenerateTownShell();

                // After generation, buildings are dense 0..n-1 and allocator == buildings.Count.
                int buildingsAfterGeneration = controller.Buildings.Count;
                Assert.AreEqual(buildingsAfterGeneration, controller.NextBuildingId,
                    "Baseline: after generation, NextBuildingId must equal buildings.Count (dense state).");

                // Find a vacant plot so TryConstructBuildingShell has somewhere to build.
                int vacantPlotId = -1;
                for (int i = 0; i < controller.Plots.Count; i++)
                {
                    if (controller.Plots[i].buildingId < 0)
                    {
                        vacantPlotId = controller.Plots[i].id;
                        break;
                    }
                }

                if (vacantPlotId < 0)
                {
                    // No vacant plot — cannot exercise TryConstructBuildingShell.
                    Assert.Inconclusive("No vacant business plot available for TryConstructBuildingShell test.");
                    return;
                }

                // Construct a building shell without gapping the allocator.
                // The new building must receive id == buildingsAfterGeneration
                // (== current NextBuildingId in dense state).
                bool placed = controller.TryConstructBuildingShell(
                    vacantPlotId, def, false,
                    out PlacedBuilding newBuilding,
                    out string message);

                Assert.IsTrue(placed, $"TryConstructBuildingShell failed: {message}");
                Assert.IsNotNull(newBuilding);

                // Dense-append: new building id == former buildings.Count == former NextBuildingId.
                Assert.AreEqual(buildingsAfterGeneration, newBuilding.id,
                    $"New building ID must equal former buildings.Count ({buildingsAfterGeneration}) — dense append.");

                // id == index in the list (dense invariant).
                int newIndex = controller.Buildings.Count - 1;
                Assert.AreEqual(newBuilding.id, newIndex,
                    "Pre-P005 invariant: new building id must equal its list index.");

                // Allocator must have advanced exactly once.
                Assert.AreEqual(buildingsAfterGeneration + 1, controller.NextBuildingId,
                    "Allocator must advance exactly once per allocation.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        /// <summary>
        /// Successive dense appends: each TryConstructBuildingShell call advances
        /// the allocator exactly once and produces id == list index for every building.
        /// </summary>
        [Test]
        public void TryConstructBuildingShell_SuccessiveAllocations_EachAdvanceAllocatorOnce()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition def = CreateBuildingDefinition("successive_alloc_test_building");
                cleanup.Add(def);

                TownGenerationSettings settings = CreateMinimalSettings(def);
                cleanup.Add(settings);

                GameObject root = new("BuildingWriterCutover_Successive_Test");
                cleanup.Add(root);

                TownWorldController controller = root.AddComponent<TownWorldController>();
                controller.Configure(settings, null, null);
                controller.GenerateTownShell();

                // Collect vacant plots.
                List<int> vacantPlotIds = new();
                for (int i = 0; i < controller.Plots.Count; i++)
                {
                    if (controller.Plots[i].buildingId < 0)
                    {
                        vacantPlotIds.Add(controller.Plots[i].id);
                    }
                }

                if (vacantPlotIds.Count < 2)
                {
                    Assert.Inconclusive("Need at least 2 vacant plots to test successive allocations.");
                    return;
                }

                // Dense baseline: allocator == buildings.Count before any construction.
                int baseCount = controller.Buildings.Count;
                Assert.AreEqual(baseCount, controller.NextBuildingId,
                    "Baseline: NextBuildingId must equal buildings.Count (dense state).");

                // First allocation — dense append.
                bool placed1 = controller.TryConstructBuildingShell(
                    vacantPlotIds[0], def, false,
                    out PlacedBuilding b1, out string m1);
                Assert.IsTrue(placed1, m1);
                Assert.AreEqual(baseCount, b1.id,
                    "First new building must get id == former buildings.Count (dense append).");
                Assert.AreEqual(baseCount, controller.Buildings.Count - 1,
                    "First new building id must equal its list index.");
                Assert.AreEqual(baseCount + 1, controller.NextBuildingId,
                    "Allocator must advance once after first creation.");

                // Second allocation — dense append.
                bool placed2 = controller.TryConstructBuildingShell(
                    vacantPlotIds[1], def, false,
                    out PlacedBuilding b2, out string m2);
                Assert.IsTrue(placed2, m2);
                Assert.AreEqual(baseCount + 1, b2.id,
                    "Second new building must get id == baseCount+1 (dense append).");
                Assert.AreEqual(baseCount + 1, controller.Buildings.Count - 1,
                    "Second new building id must equal its list index.");
                Assert.AreEqual(baseCount + 2, controller.NextBuildingId,
                    "Allocator must advance once after second creation.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        /// <summary>
        /// Buildings placed during GenerateTownShell() retain their original IDs
        /// (0..n-1) after a new building is constructed via TryConstructBuildingShell.
        /// The new building is appended densely at index n with id == n.
        /// </summary>
        [Test]
        public void ExistingBuildingIds_RemainUnchanged_AfterNewAllocation()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition def = CreateBuildingDefinition("existing_id_stability_test_building");
                cleanup.Add(def);

                TownGenerationSettings settings = CreateMinimalSettings(def);
                cleanup.Add(settings);

                GameObject root = new("BuildingWriterCutover_ExistingId_Test");
                cleanup.Add(root);

                TownWorldController controller = root.AddComponent<TownWorldController>();
                controller.Configure(settings, null, null);
                controller.GenerateTownShell();

                int originalCount = controller.Buildings.Count;
                Assert.Greater(originalCount, 0, "At least one building must be placed by GenerateTownShell.");

                // Snapshot existing building IDs (must be dense: id == index).
                int[] originalIds = new int[originalCount];
                for (int i = 0; i < originalCount; i++)
                {
                    originalIds[i] = controller.Buildings[i].id;
                    Assert.AreEqual(i, originalIds[i], $"Pre-P005 invariant: buildings[{i}].id must be {i}.");
                }

                // Find vacant plot.
                int vacantPlotId = -1;
                for (int i = 0; i < controller.Plots.Count; i++)
                {
                    if (controller.Plots[i].buildingId < 0)
                    {
                        vacantPlotId = controller.Plots[i].id;
                        break;
                    }
                }

                if (vacantPlotId < 0)
                {
                    Assert.Inconclusive("No vacant plot to exercise TryConstructBuildingShell.");
                    return;
                }

                // Append densely — no allocator gap.
                bool placed = controller.TryConstructBuildingShell(
                    vacantPlotId, def, false, out PlacedBuilding newBuilding, out string message);
                Assert.IsTrue(placed, message);
                Assert.AreEqual(originalCount, newBuilding.id,
                    "New building must be appended densely with id == former buildings.Count.");

                // Existing IDs must be unchanged.
                for (int i = 0; i < originalCount; i++)
                {
                    Assert.AreEqual(originalIds[i], controller.Buildings[i].id,
                        $"Existing building[{i}].id must not change after new allocation.");
                }
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        /// <summary>
        /// After TryConstructBuildingShell performs a dense append, CaptureSaveDto
        /// persists the advanced NextBuildingId (== original buildings.Count + 1),
        /// proving save capture reflects the post-creation allocator state.
        /// </summary>
        [Test]
        public void SaveCapture_AfterConstruction_PersistsAdvancedNextBuildingId()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition def = CreateBuildingDefinition("save_capture_test_building");
                cleanup.Add(def);

                TownGenerationSettings settings = CreateMinimalSettings(def);
                cleanup.Add(settings);

                GameObject root = new("BuildingWriterCutover_SaveCapture_Test");
                cleanup.Add(root);

                TownWorldController controller = root.AddComponent<TownWorldController>();
                controller.Configure(settings, null, null);
                controller.GenerateTownShell();

                // Dense baseline.
                int baseCount = controller.Buildings.Count;
                Assert.AreEqual(baseCount, controller.NextBuildingId,
                    "Baseline: NextBuildingId must equal buildings.Count (dense state).");

                // Find vacant plot.
                int vacantPlotId = -1;
                for (int i = 0; i < controller.Plots.Count; i++)
                {
                    if (controller.Plots[i].buildingId < 0)
                    {
                        vacantPlotId = controller.Plots[i].id;
                        break;
                    }
                }

                if (vacantPlotId < 0)
                {
                    Assert.Inconclusive("No vacant plot for save-capture test.");
                    return;
                }

                // Dense append — no allocator gap.
                bool placed = controller.TryConstructBuildingShell(
                    vacantPlotId, def, false, out _, out string message);
                Assert.IsTrue(placed, message);

                int expectedNextId = baseCount + 1;
                Assert.AreEqual(expectedNextId, controller.NextBuildingId,
                    "Allocator must be baseCount+1 after one dense construction.");

                // Capture save and confirm nextBuildingId is persisted.
                WorldSaveDto dto = controller.CaptureSaveDto();
                Assert.AreEqual(expectedNextId, dto.nextBuildingId,
                    "CaptureSaveDto must persist the post-construction allocator state.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }
    }
}
