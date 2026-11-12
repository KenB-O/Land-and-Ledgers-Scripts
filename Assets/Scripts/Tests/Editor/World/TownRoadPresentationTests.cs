using System.Reflection;
using System.Collections.Generic;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
namespace LandLedgers.Tests.World
{
    public sealed class TownRoadPresentationTests
    {
        [Test]
        public void SuppressedRoadHasNoMeshAndRetainsItsLogicalCell()
        {
            var owner = new GameObject("Road presentation test");
            var root = new GameObject("Generated visuals");
            var settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            try
            {
                var controller = owner.AddComponent<TownWorldController>();
                controller.Configure(settings, null, root.transform);
                var grid = new TownGrid(2, 1, 4, Vector3.zero);
                var blocked = TownCell.CreateDefault();
                blocked.occupancy = CellOccupancy.Road;
                blocked.roadType = RoadType.MainStreet;
                blocked.blocked = true;
                grid.SetCell(new GridCoord(0, 0), blocked);
                var open = blocked;
                open.blocked = false;
                grid.SetCell(new GridCoord(1, 0), open);
                typeof(TownWorldController).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, grid);
                typeof(TownWorldController).GetMethod("BuildRoadVisuals", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { root.transform });
                Assert.AreEqual(1, root.transform.childCount);
                StringAssert.StartsWith("Road Rect 1_0", root.transform.GetChild(0).name);
                Assert.IsTrue(grid.GetCell(new GridCoord(0, 0)).IsRoad);
                Assert.IsTrue(grid.GetCell(new GridCoord(0, 0)).blocked);
                Assert.IsTrue(grid.GetCell(new GridCoord(1, 0)).IsRoad);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void VisualRebuildDoesNotRecreatePropertyPathDecorations()
        {
            var owner = new GameObject("Property access visual rebuild test");
            var root = new GameObject("Generated visuals");
            var settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            try
            {
                settings.showTerrainOverlay = false;
                settings.showPlots = false;
                settings.showRoads = false;
                settings.showBuildings = false;
                var controller = owner.AddComponent<TownWorldController>();
                controller.Configure(settings, null, root.transform);
                var grid = new TownGrid(3, 3, 2f, Vector3.zero);
                var road = TownCell.CreateDefault(); road.terrainZone = TerrainZone.Buildable;
                road.occupancy = CellOccupancy.Road; road.roadType = RoadType.Spur;
                for (int x = 0; x < 3; x++) grid.SetCell(new GridCoord(x, 1), road);
                typeof(TownWorldController).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(controller, grid);
                var building = new PlacedBuilding { id = 7, footprint = new GridRect(1, 1, 1, 1) };
                building.anchors.Add(new BuildingAnchor
                    { type = AnchorType.FrontDoor, coord = new GridCoord(1,1), fromAuthoredMarker = true });
                ((List<PlacedBuilding>)typeof(TownWorldController).GetField("buildings", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(controller))
                    .Add(building);

                typeof(TownWorldController).GetMethod("BuildVisuals", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
                Assert.IsFalse(ContainsNamedChild(root.transform, "Property Path"));
                typeof(TownWorldController).GetMethod("BuildVisuals", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
                Assert.IsFalse(ContainsNamedChild(root.transform, "Property Path"));
                Assert.AreEqual(1, building.anchors.Count);
                for (int x = 0; x < 3; x++) Assert.IsTrue(grid.GetCell(new GridCoord(x,1)).IsRoad);
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        private static bool ContainsNamedChild(Transform root, string prefix)
        {
            if (root.name.StartsWith(prefix)) return true;
            foreach (Transform child in root)
                if (ContainsNamedChild(child, prefix)) return true;
            return false;
        }
    }
}
