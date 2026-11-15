using System.Collections.Generic;
using System.Linq;
using LandAndLedgers.Buildings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class LLBuildingModuleCatalogTests
    {
        private readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        [Test]
        public void CatalogIdsQualifyLegacyIdsBySourceFamilyAndDoNotGuessAmbiguousAliases()
        {
            LLBuildingModuleCatalog catalog = Track(ScriptableObject.CreateInstance<LLBuildingModuleCatalog>());
            LLModularBuildingPiece house = MakePiece("Wall 2 A 008 Wood Wall", "House01", "wall_a_wood", LLModuleCategory.Wall);
            LLModularBuildingPiece saloon = MakePiece("Wall 2 A 008 Wood Wall", "Saloon", "wall_a_wood", LLModuleCategory.Wall);
            catalog.SetIndex("source", new List<LLModularBuildingPiece> { saloon, house });

            Assert.AreEqual(2, catalog.Entries.Count);
            Assert.AreNotEqual(catalog.Entries[0].catalogId, catalog.Entries[1].catalogId);
            Assert.IsTrue(catalog.Entries.All(entry => entry.legacyPieceId == "wall_a_wood"));
            Assert.AreEqual(2, catalog.FindLegacyPieceIdMatches("wall_a_wood").Count);
            Assert.IsFalse(catalog.TryResolveLegacyPieceId("wall_a_wood", out _),
                "A duplicate legacy key must not silently select another building family's wall.");
            Assert.IsTrue(catalog.TryResolveCatalogId(catalog.Entries[0].catalogId, out LLModularBuildingPiece resolved));
            Assert.NotNull(resolved);
        }

        [Test]
        public void FloorOrientationRecommendationRotatesStandingFloorIntoHorizontalPlane()
        {
            Vector3Int adapter = LLBuildingModuleCatalog.GetRecommendedAxisQuarterTurns(
                LLModuleCategory.Floor, new Vector3(2f, 2f, 0.4f));
            Assert.AreEqual(new Vector3Int(3, 0, 0), adapter);
            Assert.AreEqual(Vector3Int.zero, LLBuildingModuleCatalog.GetRecommendedAxisQuarterTurns(
                LLModuleCategory.Floor, new Vector3(2f, 0.125f, 2f)));
        }

        [Test]
        public void DoorNamedWallIsCategorizedButNotApprovedWhenColliderBlocksOpening()
        {
            LLBuildingModuleCatalog catalog = Track(ScriptableObject.CreateInstance<LLBuildingModuleCatalog>());
            GameObject root = Track(new GameObject("Wall 2 Door Entrance"));
            LLModularBuildingPiece piece = root.AddComponent<LLModularBuildingPiece>();
            piece.category = LLModuleCategory.Wall;
            piece.extractedFromBuildingName = "House01";
            BoxCollider blocker = root.AddComponent<BoxCollider>();
            blocker.size = new Vector3(2f, 2f, 0.2f);
            Mesh mesh = Track(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(-1f,-1f,-0.1f), new Vector3(1f,-1f,-0.1f), new Vector3(1f,1f,-0.1f), new Vector3(-1f,1f,-0.1f),
                new Vector3(-1f,-1f,0.1f), new Vector3(1f,-1f,0.1f), new Vector3(1f,1f,0.1f), new Vector3(-1f,1f,0.1f)
            };
            mesh.triangles = new[] { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 1,5,6, 1,6,2, 2,6,7, 2,7,3, 3,7,4, 3,4,0 };
            mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            catalog.SetIndex("source", new List<LLModularBuildingPiece> { piece });

            Assert.AreEqual("Walls/Doorways", catalog.Entries[0].categoryPath);
            StringAssert.StartsWith("blocked:", catalog.Entries[0].openingClearance);
        }

        [Test]
        public void WallClassificationSupportsZUpSourceGeometry()
        {
            LLBuildingModuleCatalog catalog = Track(ScriptableObject.CreateInstance<LLBuildingModuleCatalog>());
            GameObject root = Track(new GameObject("Wall Z Up Source"));
            LLModularBuildingPiece piece = root.AddComponent<LLModularBuildingPiece>();
            piece.category = LLModuleCategory.Wall;
            Mesh mesh = Track(new Mesh());
            mesh.vertices = new[]
            {
                new Vector3(-0.5f,-0.04f,0f), new Vector3(0.5f,-0.04f,0f),
                new Vector3(0.5f,0.04f,2f), new Vector3(-0.5f,0.04f,2f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>();
            catalog.SetIndex("source", new List<LLModularBuildingPiece> { piece });

            Assert.AreEqual("Walls/Solid", catalog.Entries[0].categoryPath,
                "Wall categorization should use planar dimensions without assuming the source model's up axis.");
        }

        [Test]
        public void CentralCatalogCoversEveryReadyModuleAndKeepsRegenerationPathsResolvable()
        {
            LLBuildingModuleCatalog catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(
                "Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset");
            Assert.NotNull(catalog);
            Assert.AreEqual(1172, catalog.Modules.Count);
            Assert.AreEqual(catalog.Modules.Count, catalog.Entries.Count);
            Assert.AreEqual(catalog.Entries.Count, catalog.Entries.Select(entry => entry.catalogId).Distinct().Count());
            foreach (LLBuildingModuleCatalogEntry entry in catalog.Entries)
            {
                LLModularBuildingPiece module = catalog.ResolveModule(entry);
                Assert.NotNull(module, entry.catalogId);
                Assert.IsTrue(entry.categoryPath.Length > 0, entry.catalogId);
                Assert.AreEqual(AssetDatabase.GetAssetPath(module), module.outputPrefabPath, entry.catalogId);
                Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>(module.lod0AssetPath), entry.catalogId + " LOD0");
                Assert.NotNull(AssetDatabase.LoadAssetAtPath<GameObject>(module.lod1AssetPath), entry.catalogId + " LOD1");
            }
        }

        [Test]
        public void Floor4X4B003UsesMeasuredFootprintAndRecommendedAxisAdapter()
        {
            LLBuildingModuleCatalog catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(
                "Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset");
            LLBuildingModuleCatalogEntry entry = catalog.Entries.First(candidate =>
                candidate.sourceFamily == "House01"
                && catalog.ResolveModule(candidate).name == "Floor 4 X 4 B 003 4x4 Wood Wall 2");
            Assert.That(entry.measuredRendererBounds.x, Is.EqualTo(2f).Within(0.001f));
            Assert.That(entry.measuredRendererBounds.y, Is.EqualTo(2f).Within(0.001f));
            Assert.That(entry.measuredRendererBounds.z, Is.EqualTo(0.4f).Within(0.001f));
            Assert.AreEqual(2f, catalog.ResolveModule(entry).cellSizeMeters, 0.001f);
            Assert.AreEqual(1, catalog.ResolveModule(entry).gridWidth);
            Assert.AreEqual(1, catalog.ResolveModule(entry).gridDepth);
            Assert.AreEqual(new Vector3Int(3, 0, 0), entry.recommendedAxisQuarterTurns);
        }

        [Test]
        public void ExistingHouse01DoorWallIsListedButColliderBlocksNominalTraversal()
        {
            LLBuildingModuleCatalog catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(
                "Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset");
            LLBuildingModuleCatalogEntry entry = catalog.Entries.First(candidate =>
                candidate.sourceFamily == "House01"
                && catalog.ResolveModule(candidate).name == "Wall 2 A 02 Door C 001");
            Assert.AreEqual("Walls/Doorways", entry.categoryPath);
            StringAssert.StartsWith("blocked:", entry.openingClearance);
        }

        private LLModularBuildingPiece MakePiece(string name, string family, string legacyId, LLModuleCategory category)
        {
            GameObject root = Track(new GameObject(name));
            LLModularBuildingPiece piece = root.AddComponent<LLModularBuildingPiece>();
            piece.name = name;
            piece.extractedFromBuildingName = family;
            piece.pieceId = legacyId;
            piece.sourceKey = legacyId;
            piece.category = category;
            return piece;
        }

        private T Track<T>(T item) where T : Object { created.Add(item); return item; }
    }
}
