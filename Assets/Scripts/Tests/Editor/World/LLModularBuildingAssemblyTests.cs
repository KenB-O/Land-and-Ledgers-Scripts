using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandAndLedgers.Buildings;
using LandAndLedgers.Buildings.Editor;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class LLModularBuildingAssemblyTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void ManualPlacementEditorSectionStartsCollapsed()
        {
            LLModularBuildingAssemblyEditor window = ScriptableObject.CreateInstance<LLModularBuildingAssemblyEditor>();
            try
            {
                FieldInfo foldout = typeof(LLModularBuildingAssemblyEditor).GetField(
                    "showManualPlacements", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(foldout, "The legacy manual-placement foldout must remain backed by serialized editor-window state.");
                Assert.IsFalse((bool)foldout.GetValue(window), "The primary footprint workflow should be visible without expanding advanced manual placements.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(0f, 2f, 0f, 0f)]
        [TestCase(1.2f, 2f, 0f, 2f)]
        [TestCase(1.2f, 2f, 1f, 1f)]
        [TestCase(-2.8f, 2f, 0f, -2f)]
        public void SnapToCellUsesExplicitGridOrigin(float value, float cellSize, float origin, float expected)
        {
            Assert.AreEqual(expected, LLModularBuildingAssembly.SnapToCell(value, cellSize, origin), 0.0001f);
        }

        [Test]
        public void AxisAdapterRotatesExistingZUpFloorIntoUnityHorizontalPlane()
        {
            Vector3 sourceBounds = new(2f, 2f, 0.125f);
            Vector3 unityBounds = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(sourceBounds, new Vector3Int(3, 0, 0));
            Assert.AreEqual(2f, unityBounds.x, 0.001f);
            Assert.AreEqual(0.125f, unityBounds.y, 0.001f);
            Assert.AreEqual(2f, unityBounds.z, 0.001f);
            Assert.AreEqual(0.3375f, LLModularBuildingAssembly.GetFloorModuleCenterY(0.4f, unityBounds.y), 0.0001f,
                "Place the renderer center half its thickness below the walkable top surface.");
        }

        [Test]
        public void ModuleCompatibilityRejectsMixedFamilyCellSizeAndNestedScale()
        {
            GameObject root = Track(new GameObject("assembly"));
            GameObject firstObject = Track(new GameObject("House01 wall"));
            firstObject.transform.SetParent(root.transform);
            LLModularBuildingPiece first = firstObject.AddComponent<LLModularBuildingPiece>();
            first.extractedFromBuildingName = "House01";
            first.cellSizeMeters = 2f;

            GameObject secondObject = Track(new GameObject("House01 floor"));
            secondObject.transform.SetParent(root.transform);
            LLModularBuildingPiece second = secondObject.AddComponent<LLModularBuildingPiece>();
            second.extractedFromBuildingName = "House01";
            second.cellSizeMeters = 2f;

            Assert.IsTrue(LLModularBuildingAssembly.TryValidateModules(new[] { first, second }, 2f, "House01", true, true, root.transform, out var validIssues), string.Join("\n", validIssues));

            second.extractedFromBuildingName = "Saloon";
            Assert.IsFalse(LLModularBuildingAssembly.TryValidateModules(new[] { first, second }, 2f, "House01", true, true, root.transform, out var familyIssues));
            StringAssert.Contains("source family", string.Join("\n", familyIssues));

            second.extractedFromBuildingName = "House01";
            second.cellSizeMeters = 1f;
            Assert.IsFalse(LLModularBuildingAssembly.TryValidateModules(new[] { first, second }, 2f, "House01", true, true, root.transform, out var cellIssues));
            StringAssert.Contains("grid does not match", string.Join("\n", cellIssues));

            second.cellSizeMeters = 2f;
            second.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Assert.IsFalse(LLModularBuildingAssembly.TryValidateModules(new[] { first, second }, 2f, "House01", true, true, root.transform, out var scaleIssues));
            StringAssert.Contains("stacking scale", string.Join("\n", scaleIssues));
        }

        [Test]
        public void FloorElevationLookupSupportsDistinctStoreys()
        {
            GameObject root = Track(new GameObject("assembly"));
            LLModularBuildingAssembly assembly = root.AddComponent<LLModularBuildingAssembly>();
            assembly.Configure("multi", "House01", 2f, 2f, new Vector2Int(2, 3), new List<LLModularBuildingFloor>
            {
                new() { floorIndex = 0, elevationLocal = 0.4f },
                new() { floorIndex = 1, elevationLocal = 2.4f, requiresVerticalConnectionFromFloorBelow = true }
            });
            Assert.IsTrue(assembly.TryGetFloorElevation(0, out float ground));
            Assert.IsTrue(assembly.TryGetFloorElevation(1, out float upper));
            Assert.AreEqual(0.4f, ground, 0.0001f);
            Assert.AreEqual(2.4f, upper, 0.0001f);
            Assert.IsFalse(assembly.TryGetFloorElevation(2, out _));
        }

        [Test]
        public void RectangularFootprintCountsOnlyActualExteriorEdges()
        {
            List<Vector2Int> cells = LLModularBuildingAssembly.CreateRectangleCells(6, 4);
            List<LLBuildingPerimeterSegment> perimeter = LLModularBuildingAssembly.GetPerimeter(cells);

            Assert.AreEqual(24, cells.Count);
            Assert.IsTrue(LLModularBuildingAssembly.AreCellsConnected(cells));
            Assert.AreEqual(20, perimeter.Count);
            Assert.IsFalse(perimeter.Any(edge => cells.Contains(edge.cell + Neighbor(edge.side))),
                "Adjacent occupied cells must not receive an exterior wall segment.");
        }

        [Test]
        public void LFootprintHasSevenCellsAndNoWallsAcrossItsSharedEdges()
        {
            List<Vector2Int> cells = new()
            {
                new(0,0), new(1,0), new(2,0), new(3,0), new(4,0),
                new(4,1), new(4,2)
            };
            List<LLBuildingPerimeterSegment> perimeter = LLModularBuildingAssembly.GetPerimeter(cells);

            Assert.IsTrue(LLModularBuildingAssembly.AreCellsConnected(cells));
            Assert.AreEqual(7, cells.Count);
            Assert.AreEqual(16, perimeter.Count);
            Assert.IsFalse(perimeter.Any(edge => cells.Contains(edge.cell + Neighbor(edge.side))));
        }

        [Test]
        public void DisconnectedAndDuplicateFootprintsAreRejected()
        {
            Assert.IsFalse(LLModularBuildingAssembly.AreCellsConnected(new[]
            {
                new Vector2Int(0,0), new Vector2Int(2,0)
            }));
            Assert.IsFalse(LLModularBuildingAssembly.AreCellsConnected(new[]
            {
                new Vector2Int(0,0), new Vector2Int(0,0)
            }));
            Assert.IsFalse(LLModularBuildingAssembly.AreCellsConnected(System.Array.Empty<Vector2Int>()));
        }

        [Test]
        public void AssemblyConfigurationPersistsOccupiedCellsAndTypedPerimeter()
        {
            GameObject root = Track(new GameObject("assembly-layout"));
            LLModularBuildingAssembly assembly = root.AddComponent<LLModularBuildingAssembly>();
            List<Vector2Int> cells = new() { new(0,0), new(1,0), new(1,1) };
            List<LLBuildingPerimeterSegment> perimeter = LLModularBuildingAssembly.GetPerimeter(cells);
            perimeter[0].type = LLBuildingPerimeterType.OpenPassage;
            perimeter[0].moduleCatalogId = string.Empty;
            assembly.ConfigureLayout("stable.l", "House01", 2f, 2f, cells,
                new List<LLModularBuildingFloor> { new() { floorIndex = 0, elevationLocal = 0.4f } }, perimeter);

            Assert.AreEqual(3, assembly.OccupiedFootprintCells.Count);
            Assert.AreEqual(8, assembly.PerimeterSegments.Count);
            Assert.AreEqual(LLBuildingPerimeterType.OpenPassage, assembly.PerimeterSegments[0].type);
            Assert.AreEqual(new Vector2Int(2, 2), assembly.FootprintCells);
        }

        [Test]
        public void ExistingHouse01ModulesExposeMeasuredTwoMetreGridAndSourceFamily()
        {
            LLModularBuildingPiece floor = LoadPiece("Assets/Environment/Building Materials/Auto Extracted/House 01/Prefabs/Floor 4 X 4 A 009 4x4 Wood Wall 2.prefab");
            LLModularBuildingPiece wall = LoadPiece("Assets/Environment/Building Materials/Auto Extracted/House 01/Prefabs/Wall 2 A 008 Wood Wall.prefab");
            Assert.NotNull(floor);
            Assert.NotNull(wall);
            Assert.AreEqual(LLModuleCategory.Floor, floor.category);
            Assert.AreEqual(LLModuleCategory.Wall, wall.category);
            Assert.AreEqual(2f, floor.cellSizeMeters, 0.001f);
            Assert.AreEqual(floor.cellSizeMeters, wall.cellSizeMeters, 0.001f);
            Assert.AreEqual("House01", floor.extractedFromBuildingName);
            Assert.AreEqual(floor.extractedFromBuildingName, wall.extractedFromBuildingName);
            Assert.AreEqual(Vector3.one, floor.transform.localScale);
            Assert.AreEqual(Vector3.one, wall.transform.localScale);

            Vector3 sourceSize = floor.GetComponentsInChildren<Renderer>(true)[0].bounds.size;
            Vector3 orientedSize = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(sourceSize, new Vector3Int(3, 0, 0));
            Assert.LessOrEqual(orientedSize.y, 0.5f, "The source mesh needs the explicit Z-up to Y-up adapter before it can define walkable floor geometry.");
            Assert.GreaterOrEqual(orientedSize.x, 1f);
            Assert.GreaterOrEqual(orientedSize.z, 1f);
        }

        [Test]
        public void House01DoorWallIsNotAcceptedAsTraversableFromItsNameAlone()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Building Materials/Auto Extracted/House 01/Prefabs/Wall 2 A 02 Door C 001.prefab");
            Assert.NotNull(prefab);
            BoxCollider panelCollider = prefab.GetComponentInChildren<BoxCollider>(true);
            Assert.NotNull(panelCollider);
            Assert.GreaterOrEqual(panelCollider.size.x, 1.9f);
            Assert.GreaterOrEqual(panelCollider.size.y, 1.9f);
            Renderer leaf = System.Array.Find(prefab.GetComponentsInChildren<Renderer>(true), renderer => renderer.name.Contains("Wall45"));
            Assert.NotNull(leaf);
            Assert.Less(leaf.bounds.size.x, 0.8f);
            Assert.Less(leaf.bounds.size.y, 1.9f);
        }

        [Test]
        public void GeneratedDoorLeafOpensAndClosesWithoutDisablingItsCollider()
        {
            GameObject root = Track(new GameObject("Physical door test"));
            root.transform.position = new Vector3(12000f, 1000f, 12000f);
            GameObject leafObject = Track(new GameObject("Door"));
            leafObject.transform.SetParent(root.transform, false);
            BoxCollider collider = leafObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0.45f, 1f, 0f);
            collider.size = new Vector3(0.9f, 2f, 0.1f);
            BuildingDoorAnimator door = root.AddComponent<BuildingDoorAnimator>();
            Assert.IsTrue(door.ConfigurePhysicalLeaf(leafObject.transform, new Vector3(0f, 90f, 0f)));

            Vector3 passageCenter = root.transform.position + new Vector3(0.45f, 1f, 0f);
            Vector3 passageHalfExtents = new(0.12f, 0.12f, 0.12f);
            Physics.SyncTransforms();
            Assert.IsNotEmpty(Physics.OverlapBox(passageCenter, passageHalfExtents), "The closed leaf should physically occupy the passage.");

            Assert.IsTrue(door.SetDoorOpen(true));
            Physics.SyncTransforms();
            Assert.IsTrue(collider.enabled, "Opening moves the leaf; it does not globally disable the doorway collider.");
            Assert.IsEmpty(Physics.OverlapBox(passageCenter, passageHalfExtents), "The open leaf should clear the passage.");

            Assert.IsTrue(door.SetDoorOpen(false));
            Physics.SyncTransforms();
            Assert.IsNotEmpty(Physics.OverlapBox(passageCenter, passageHalfExtents), "Closing restores physical obstruction.");
        }

        [TestCase("Assets/Environment/Building Materials/Examples/Pass 2 Rectangle 12x8.prefab")]
        [TestCase("Assets/Environment/Building Materials/Examples/Pass 2 L 7 Cell Example.prefab")]
        public void GeneratedPrefabInstancingPreservesOneBuildingAuthorityAndEntrance(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab);
            GameObject instance = Track((GameObject)PrefabUtility.InstantiatePrefab(prefab));
            Assert.NotNull(instance);

            Assert.AreEqual(1, instance.GetComponentsInChildren<BuildingFootprintAuthority>(true).Length);
            Assert.AreEqual(1, instance.GetComponentsInChildren<LLModularBuildingAssembly>(true).Length);
            Assert.AreEqual(1, instance.GetComponentsInChildren<BuildingFootprintAuthority>(true)[0].EntrancePairs.Count);
            BuildingFootprintAuthority authority = instance.GetComponent<BuildingFootprintAuthority>();
            Assert.NotNull(authority.EntrancePairs[0].sourceTransform);
            Assert.IsTrue(authority.EntrancePairs[0].sourceTransform.IsChildOf(instance.transform));
            authority.RefreshDerivedInteriorGeometry();

            BuildingDefinition definition = Track(ScriptableObject.CreateInstance<BuildingDefinition>());
            definition.ConfigureRuntimeFallback("test.modular", "Test Modular Building", PlotZone.Business,
                new Vector2Int(1, 1), Color.white, 2f);
            definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);
            Assert.IsTrue(definition.HasPrefabFootprintAuthority,
                "The existing BuildingDefinition visual path must recognize the modular prefab's single root authority.");

            bool isL = path.Contains("L 7");
            Vector3 walkable = isL ? new Vector3(4f, 0.4f, 0f) : new Vector3(0f, 0.4f, 0f);
            Assert.IsTrue(authority.TryEvaluateInteriorLocalPoint(walkable, 0, null, out string reason), reason);
            if (isL)
                Assert.IsFalse(authority.TryEvaluateInteriorLocalPoint(new Vector3(0f, 0.4f, 0f), 0, null, out _),
                    "The notch in an L-shaped assembly must remain non-walkable.");
        }

        [TestCase("Assets/Environment/Building Materials/Examples/Pass 2 Rectangle 12x8.prefab", 24, 20, 43)]
        [TestCase("Assets/Environment/Building Materials/Examples/Pass 2 L 7 Cell Example.prefab", 7, 16, 22)]
        public void GeneratedPass2PrefabPreservesCatalogModulesFootprintAndOpenings(
            string path, int expectedCells, int expectedPerimeter, int expectedPlacements)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.NotNull(prefab, $"Expected generated example at {path}");
            LLModularBuildingAssembly assembly = prefab.GetComponent<LLModularBuildingAssembly>();
            BuildingFootprintAuthority authority = prefab.GetComponent<BuildingFootprintAuthority>();
            Assert.NotNull(assembly);
            Assert.NotNull(authority);
            Assert.IsTrue(assembly.TryValidate(out List<string> issues), string.Join("\n", issues));
            Assert.AreEqual("House01", assembly.SourceFamilyKey);
            Assert.AreEqual(2f, assembly.CellSizeMeters, 0.001f);
            Assert.AreEqual(expectedCells, assembly.OccupiedFootprintCells.Count);
            Assert.AreEqual(expectedPerimeter, assembly.PerimeterSegments.Count);
            Assert.IsTrue(LLModularBuildingAssembly.AreCellsConnected(assembly.OccupiedFootprintCells));

            Assert.AreEqual(1, assembly.PerimeterSegments.Count(edge => edge.type == LLBuildingPerimeterType.OpenPassage));
            Assert.AreEqual(1, assembly.PerimeterSegments.Count(edge => edge.type == LLBuildingPerimeterType.SingleWindow));
            Assert.AreEqual(expectedPerimeter - 2,
                assembly.PerimeterSegments.Count(edge => edge.type == LLBuildingPerimeterType.SolidWall));
            Assert.AreEqual(1, authority.EntrancePairs.Count);
            Assert.NotNull(authority.EntrancePairs[0].sourceTransform);
            Assert.AreEqual(0, authority.EntrancePairs[0].floorIndex);
            Assert.AreEqual(1, authority.InteriorFloors.Count);
            Assert.IsTrue(authority.InteriorFloors[0].deriveWalkableFromFloorGeometry);
            Assert.AreEqual(0.4f, authority.InteriorFloors[0].floorHeightLocal, 0.001f);

            LLBuildingModuleCatalog catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(
                "Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset");
            Assert.NotNull(catalog);
            LLModularBuildingPlacement[] placements = prefab.GetComponentsInChildren<LLModularBuildingPlacement>(true);
            Assert.AreEqual(expectedPlacements, placements.Length);
            Assert.AreEqual(expectedCells, placements.Count(placement => placement.Module.category == LLModuleCategory.Floor));
            Assert.AreEqual(expectedPerimeter - 1,
                placements.Count(placement => placement.Module.category != LLModuleCategory.Floor),
                "The single explicit open passage has no physical wall module.");
            Assert.AreEqual(placements.Length, placements.Select(placement => placement.PlacementId).Distinct().Count());

            foreach (LLModularBuildingPlacement placement in placements)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(placement.ModuleCatalogId));
                Assert.IsTrue(catalog.TryResolveCatalogId(placement.ModuleCatalogId, out LLModularBuildingPiece catalogModule));
                Assert.AreEqual("House01", catalogModule.extractedFromBuildingName);
                Assert.AreEqual(Vector3.one, placement.transform.localScale);
                Assert.AreEqual(0, placement.FloorIndex);
                Assert.AreEqual(placement.Module, placement.GetComponent<LLModularBuildingPiece>());
            }

            HashSet<Vector2Int> floorCells = placements
                .Where(placement => placement.Module.category == LLModuleCategory.Floor)
                .Select(placement => placement.GridCell).ToHashSet();
            CollectionAssert.AreEquivalent(assembly.OccupiedFootprintCells, floorCells);

            foreach (LLModularBuildingPlacement placement in placements)
            {
                Bounds geometry = GetRendererBoundsInRoot(placement.gameObject, prefab.transform);
                if (placement.Module.category == LLModuleCategory.Floor)
                {
                    Vector3 expectedCenter = new(
                        (placement.GridCell.x + 0.5f - assembly.FootprintCells.x * 0.5f) * assembly.CellSizeMeters,
                        0.4f - geometry.size.y * 0.5f,
                        (placement.GridCell.y + 0.5f - assembly.FootprintCells.y * 0.5f) * assembly.CellSizeMeters);
                    Assert.That(geometry.size.x, Is.EqualTo(assembly.CellSizeMeters).Within(0.12f));
                    Assert.That(geometry.size.z, Is.EqualTo(assembly.CellSizeMeters).Within(0.12f));
                    Assert.LessOrEqual(geometry.size.y, 0.5f);
                    Assert.That(geometry.center.x, Is.EqualTo(expectedCenter.x).Within(0.02f));
                    Assert.That(geometry.center.y, Is.EqualTo(expectedCenter.y).Within(0.02f));
                    Assert.That(geometry.center.z, Is.EqualTo(expectedCenter.z).Within(0.02f));
                    continue;
                }

                LLBuildingPerimeterSegment edge = assembly.PerimeterSegments.FirstOrDefault(candidate =>
                    placement.PlacementId.EndsWith($"|edge:{candidate.Key}", System.StringComparison.Ordinal));
                Assert.NotNull(edge, $"Physical perimeter placement {placement.PlacementId} must identify its edge.");
                Vector3 expectedEdgeCenter = new(
                    (edge.cell.x + 0.5f - assembly.FootprintCells.x * 0.5f) * assembly.CellSizeMeters,
                    0.4f + geometry.size.y * 0.5f,
                    (edge.cell.y + 0.5f - assembly.FootprintCells.y * 0.5f) * assembly.CellSizeMeters);
                switch (edge.side)
                {
                    case LLBuildingPerimeterSide.North: expectedEdgeCenter.z += assembly.CellSizeMeters * 0.5f; break;
                    case LLBuildingPerimeterSide.East: expectedEdgeCenter.x += assembly.CellSizeMeters * 0.5f; break;
                    case LLBuildingPerimeterSide.South: expectedEdgeCenter.z -= assembly.CellSizeMeters * 0.5f; break;
                    case LLBuildingPerimeterSide.West: expectedEdgeCenter.x -= assembly.CellSizeMeters * 0.5f; break;
                }
                float span = edge.side == LLBuildingPerimeterSide.North || edge.side == LLBuildingPerimeterSide.South
                    ? geometry.size.x : geometry.size.z;
                float thickness = edge.side == LLBuildingPerimeterSide.North || edge.side == LLBuildingPerimeterSide.South
                    ? geometry.size.z : geometry.size.x;
                Assert.That(span, Is.EqualTo(assembly.CellSizeMeters).Within(0.12f), edge.Key);
                Assert.LessOrEqual(thickness, 0.55f, edge.Key);
                Assert.That(geometry.size.y, Is.InRange(1.5f, 3.1f), edge.Key);
                Assert.That(geometry.center.x, Is.EqualTo(expectedEdgeCenter.x).Within(0.02f), edge.Key);
                Assert.That(geometry.center.y, Is.EqualTo(expectedEdgeCenter.y).Within(0.02f), edge.Key);
                Assert.That(geometry.center.z, Is.EqualTo(expectedEdgeCenter.z).Within(0.02f), edge.Key);
            }
        }

        private static Bounds GetRendererBoundsInRoot(GameObject objectRoot, Transform assemblyRoot)
        {
            Renderer[] renderers = objectRoot.GetComponentsInChildren<Renderer>(true);
            Assert.IsNotEmpty(renderers);
            bool hasBounds = false;
            Bounds combined = default;
            Matrix4x4 toAssembly = assemblyRoot.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Matrix4x4 matrix = toAssembly * renderer.transform.localToWorldMatrix;
                Bounds local = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    point = matrix.MultiplyPoint3x4(point);
                    if (!hasBounds) { combined = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else combined.Encapsulate(point);
                }
            }
            return combined;
        }

        private LLModularBuildingPiece LoadPiece(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return prefab != null ? prefab.GetComponent<LLModularBuildingPiece>() : null;
        }

        private T Track<T>(T item) where T : Object { created.Add(item); return item; }

        private static Vector2Int Neighbor(LLBuildingPerimeterSide side)
        {
            switch (side)
            {
                case LLBuildingPerimeterSide.North: return Vector2Int.up;
                case LLBuildingPerimeterSide.East: return Vector2Int.right;
                case LLBuildingPerimeterSide.South: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }
    }
}
