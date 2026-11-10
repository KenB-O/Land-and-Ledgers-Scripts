using System.Reflection;
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
    }
}
