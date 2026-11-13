using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Pathing;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class InteriorFloorAuthoringTests
    {
        [Test]
        public void PolygonValidationAcceptsConcaveAndRejectsCrossingDuplicateWindingAndDegenerate()
        {
            List<Vector3> concave = new()
            {
                new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 1f),
                new(1f, 0f, 1f), new(1f, 0f, 4f), new(0f, 0f, 4f)
            };
            Assert.IsTrue(BuildingFootprintAuthority.TryValidateInteriorPolygon(concave, out string concaveError), concaveError);

            List<Vector3> crossing = new()
            {
                new(0f, 0f, 0f), new(4f, 0f, 4f), new(0f, 0f, 4f), new(3f, 0f, 0f)
            };
            Assert.IsFalse(BuildingFootprintAuthority.TryValidateInteriorPolygon(crossing, out string crossingError));
            StringAssert.Contains("intersect", crossingError);

            List<Vector3> duplicate = new()
            {
                new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 4f),
                new(0f, 0f, 4f), new(4f, 0f, 0f)
            };
            Assert.IsFalse(BuildingFootprintAuthority.TryValidateInteriorPolygon(duplicate, out string duplicateError));
            StringAssert.Contains("duplicates", duplicateError);

            List<Vector3> clockwise = new()
            {
                new(0f, 0f, 0f), new(0f, 0f, 4f), new(4f, 0f, 4f), new(4f, 0f, 0f)
            };
            Assert.IsFalse(BuildingFootprintAuthority.TryValidateInteriorPolygon(clockwise, out string windingError));
            StringAssert.Contains("counter-clockwise", windingError);

            List<Vector3> degenerate = new()
            {
                new(0f, 0f, 0f), new(1f, 0f, 0f), new(2f, 0f, 0f)
            };
            Assert.IsFalse(BuildingFootprintAuthority.TryValidateInteriorPolygon(degenerate, out string areaError));
            StringAssert.Contains("area", areaError);
        }

        [Test]
        public void WalkableAreaExcludesBedAndCabinetAndSurvivesBuildingRotation()
        {
            GameObject building = new("Interior query test building");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { displayName = "Ground Floor", floorIndex = 0, floorHeightLocal = 0f };
                floor.walkableAreas.Add(new InteriorPolygonAuthoring
                {
                    label = "L-shaped floor",
                    vertices = new List<Vector3>
                    {
                        new(0f, 0f, 0f), new(4f, 0f, 0f), new(4f, 0f, 1f),
                        new(1f, 0f, 1f), new(1f, 0f, 4f), new(0f, 0f, 4f)
                    }
                });
                floor.exclusionAreas.Add(Rectangle("Bed", 0.2f, 0.2f, 0.9f, 1.2f));
                floor.exclusionAreas.Add(Rectangle("Cabinet", 2.5f, 0.1f, 3.6f, 0.8f));
                GetInteriorFloors(authority).Add(floor);

                Assert.IsTrue(authority.TryEvaluateInteriorLocalPoint(new Vector3(0.5f, 0f, 2f), 0, null, out string clearReason), clearReason);
                Assert.IsFalse(authority.TryEvaluateInteriorLocalPoint(new Vector3(0.5f, 0f, 0.6f), 0, null, out string bedReason));
                StringAssert.Contains("Bed", bedReason);
                Assert.IsFalse(authority.TryEvaluateInteriorLocalPoint(new Vector3(3f, 0f, 0.5f), 0, null, out string cabinetReason));
                StringAssert.Contains("Cabinet", cabinetReason);
                Assert.IsFalse(authority.TryEvaluateInteriorLocalPoint(new Vector3(3f, 0f, 3f), 0, null, out string notchReason));
                StringAssert.Contains("outside", notchReason);

                building.transform.SetPositionAndRotation(new Vector3(18f, 2f, -7f), Quaternion.Euler(0f, 90f, 0f));
                Vector3 worldPoint = building.transform.TransformPoint(new Vector3(0.5f, 0f, 2f));
                Assert.IsTrue(authority.TryEvaluateInteriorWorldPoint(worldPoint, 0, null, out string rotatedReason), rotatedReason);
                Vector3 worldBed = building.transform.TransformPoint(new Vector3(0.5f, 0f, 0.6f));
                Assert.IsFalse(authority.TryEvaluateInteriorWorldPoint(worldBed, 0, null, out string rotatedBedReason));
                StringAssert.Contains("Bed", rotatedBedReason);
            }
            finally
            {
                Object.DestroyImmediate(building);
            }
        }

        [Test]
        public void FunctionalSpaceSpecificAreaDoesNotBecomeSharedFloorPermission()
        {
            GameObject building = new("Functional space query test");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 2, floorHeightLocal = 4f };
                InteriorPolygonAuthoring area = Rectangle("Workshop", -1f, -1f, 1f, 1f);
                area.functionalSpaceId = "space-workshop-2";
                floor.walkableAreas.Add(area);
                GetInteriorFloors(authority).Add(floor);

                Assert.IsFalse(authority.TryEvaluateInteriorLocalPoint(Vector3.zero, 2, null, out string sharedReason));
                StringAssert.Contains("No walkable area", sharedReason);
                Assert.IsTrue(authority.TryEvaluateInteriorLocalPoint(Vector3.zero, 2, "space-workshop-2", out string spaceReason), spaceReason);
            }
            finally
            {
                Object.DestroyImmediate(building);
            }
        }

        [Test]
        public void ActivityLocationRejectsFurnitureAndResolvesBuildingLocalTransform()
        {
            GameObject building = new("Activity location query test");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
                floor.walkableAreas.Add(Rectangle("Floor", 0f, 0f, 4f, 4f));
                floor.exclusionAreas.Add(Rectangle("Bed", 0f, 0f, 1f, 1f));
                GetInteriorFloors(authority).Add(floor);
                List<BuildingActivityLocationAuthoring> locations = GetActivityLocations(authority);
                locations.Add(new BuildingActivityLocationAuthoring
                {
                    id = "clear-work-point", displayName = "Clear Work Point", type = BuildingActivityLocationType.Workshop,
                    positionLocal = new Vector3(2f, 0f, 2f), forwardLocal = Vector3.forward, requiresWalkableInterior = true
                });
                locations.Add(new BuildingActivityLocationAuthoring
                {
                    id = "bed-point", displayName = "Bed Point", type = BuildingActivityLocationType.Bed,
                    positionLocal = new Vector3(0.5f, 0f, 0.5f), forwardLocal = Vector3.forward, requiresWalkableInterior = true
                });

                building.transform.SetPositionAndRotation(new Vector3(10f, 0f, 5f), Quaternion.Euler(0f, 90f, 0f));
                Assert.IsTrue(authority.TryGetActivityLocation("clear-work-point", 9, out ResolvedBuildingActivityLocation resolved, out string validReason), validReason);
                Assert.AreEqual(9, resolved.BuildingInstanceId);
                Assert.That(Vector3.Distance(resolved.WorldPosition, building.transform.TransformPoint(new Vector3(2f, 0f, 2f))), Is.LessThan(0.001f));
                Assert.IsFalse(authority.TryGetActivityLocation("bed-point", 9, out _, out string blockedReason));
                StringAssert.Contains("Bed", blockedReason);
            }
            finally
            {
                Object.DestroyImmediate(building);
            }
        }

        [Test]
        public void AuthoredEntrancePairGatesBuildingBoundaryAndAllowsDoorwayRouteBothWays()
        {
            GameObject worldObject = new("Entrance route world");
            GameObject pathingObject = new("Entrance route manager");
            GameObject buildingObject = new("Entrance route building");
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            try
            {
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(2, 1, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(world, grid);
                TownCell outside = TownCell.CreateDefault(); outside.terrainZone = TerrainZone.Buildable;
                grid.SetCell(new GridCoord(0, 0), outside);

                buildingObject.transform.position = new Vector3(2f, 0f, 0f);
                BuildingFootprintAuthority authority = buildingObject.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
                floor.walkableAreas.Add(Rectangle("Interior", -1f, -1f, 2f, 2f));
                GetInteriorFloors(authority).Add(floor);
                GetEntrancePairs(authority).Add(new BuildingEntrancePairAuthoring
                {
                    id = "entry", indoorPositionLocal = new Vector3(-.5f, 0f, 1f),
                    outdoorApproachLocal = new Vector3(.5f, 0f, 1f), clearWidthMeters = 1.1f
                });
                PlacedBuilding placed = new() { id = 4, runtimeFootprintAuthority = authority };
                ((List<PlacedBuilding>)typeof(TownWorldController).GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world)).Add(placed);
                TownCell interior = TownCell.CreateDefault(); interior.terrainZone = TerrainZone.Buildable;
                interior.occupancy = CellOccupancy.Building | CellOccupancy.Blocked; interior.buildingId = placed.id;
                grid.SetCell(new GridCoord(1, 0), interior);
                settings.logFailedRequests = false;
                PathingManager pathing = pathingObject.AddComponent<PathingManager>(); pathing.Configure(world, settings);

                Assert.IsTrue(authority.TryEvaluateInteriorWorldPoint(pathing.CoordToPathWorld(new GridCoord(1,0)), 0, null, out string floorReason), floorReason);
                Assert.IsTrue(authority.AllowsDoorwayBoundaryTransition(pathing.CoordToPathWorld(new GridCoord(0,0)), pathing.CoordToPathWorld(new GridCoord(1,0))));
                MethodInfo costCheck = typeof(PathingManager).GetMethod("TryGetTraversalCost", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(GridCoord), typeof(PathingRequest), typeof(int).MakeByRefType() }, null);
                object[] costArgs = { new GridCoord(1,0), new PathingRequest(new GridCoord(0,0), new GridCoord(1,0), suppressFailureLog: true, targetInteriorBuildingId: placed.id), 0 };
                Assert.IsTrue((bool)costCheck.Invoke(pathing, costArgs), "Interior target is not traversable by request: " + costArgs[2]);
                MethodInfo boundaryCheck = typeof(PathingManager).GetMethod("CanCrossBuildingBoundary", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsTrue((bool)boundaryCheck.Invoke(pathing, new object[] { new GridCoord(0,0), new GridCoord(1,0), costArgs[1] }), "Pathing boundary gate rejected its authored door");
                MethodInfo heightCheck = typeof(PathingManager).GetMethod("CanTraverseHeightChange", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsTrue((bool)heightCheck.Invoke(pathing, new object[] { new GridCoord(0,0), new GridCoord(1,0) }), "Grid step height rejected the route");

                Assert.IsTrue(pathing.TryFindPath(new PathingRequest(new GridCoord(0,0), new GridCoord(1,0),
                    suppressFailureLog: true, targetInteriorBuildingId: placed.id), out PathingResult inbound), inbound.FailureReason);
                Assert.IsTrue(pathing.TryFindPath(new PathingRequest(new GridCoord(1,0), new GridCoord(0,0),
                    suppressFailureLog: true, startInteriorBuildingId: placed.id), out PathingResult outbound), outbound.FailureReason);

                GetEntrancePairs(authority)[0].indoorPositionLocal = new Vector3(-.5f, 0f, 2f);
                GetEntrancePairs(authority)[0].outdoorApproachLocal = new Vector3(.5f, 0f, 2f);
                Assert.IsFalse(pathing.TryFindPath(new PathingRequest(new GridCoord(0,0), new GridCoord(1,0),
                    suppressFailureLog: true, targetInteriorBuildingId: placed.id), out PathingResult blocked));
                Assert.That(blocked.FailureCategory, Is.EqualTo(PathingFailureCategory.NoRoute));
            }
            finally
            {
                Object.DestroyImmediate(pathingObject); Object.DestroyImmediate(buildingObject);
                Object.DestroyImmediate(worldObject); Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void InteriorActivityRouteFindsPathAroundBedExclusion()
        {
            GameObject building = new("Local interior route test");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = .8f };
                floor.walkableAreas.Add(Rectangle("House floor", -3.8f, -4.15f, 3.8f, 1.45f));
                floor.exclusionAreas.Add(Rectangle("Bed", .98f, -4.25f, 3.9f, -2f));
                GetInteriorFloors(authority).Add(floor);
                GetEntrancePairs(authority).Add(new BuildingEntrancePairAuthoring
                {
                    id = "main-entrance", indoorPositionLocal = new Vector3(2.815f,.833f,.683f),
                    outdoorApproachLocal = new Vector3(2.815f,.833f,2.683f), clearWidthMeters = 1.1f
                });
                GetActivityLocations(authority).Add(new BuildingActivityLocationAuthoring
                {
                    id = "bed", displayName = "Bed access", type = BuildingActivityLocationType.Bed,
                    positionLocal = new Vector3(.45f,.8f,-3.1f), forwardLocal = Vector3.right, requiresWalkableInterior = true
                });
                Assert.IsTrue(authority.TryBuildInteriorActivityRoute("main-entrance", "bed", out List<Vector3> path, out string reason), reason);
                Assert.That(path.Count, Is.GreaterThan(3));
                foreach (Vector3 world in path.Skip(1).SkipLast(1))
                    Assert.IsTrue(authority.TryEvaluateInteriorWorldPoint(world, 0, null, out string pointReason), pointReason);
                Assert.That(Vector3.Distance(path[0], authority.transform.TransformPoint(new Vector3(2.815f,.833f,.683f))), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(building); }
        }

        [Test]
        public void TypedStationLookupRequiresReachableAuthoredPosition()
        {
            GameObject building = new("Station lookup test");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0 };
                floor.walkableAreas.Add(Rectangle("Floor", -3f, -3f, 3f, 3f));
                floor.exclusionAreas.Add(Rectangle("Cabinet", -.5f, -.5f, .5f, .5f));
                GetInteriorFloors(authority).Add(floor);
                GetEntrancePairs(authority).Add(new BuildingEntrancePairAuthoring
                {
                    id = "main-entrance", indoorPositionLocal = new Vector3(-2f, 0f, -2f),
                    outdoorApproachLocal = new Vector3(-2f, 0f, -4f)
                });
                var stations = GetActivityLocations(authority);
                stations.Add(new BuildingActivityLocationAuthoring
                {
                    id = "blocked", type = BuildingActivityLocationType.Counter,
                    positionLocal = Vector3.zero
                });
                Assert.IsFalse(authority.TryFindReachableActivityLocation(BuildingActivityLocationType.Counter,
                    "main-entrance", 4, out _, out _, out string blocked));
                StringAssert.Contains("Cabinet", blocked);
                stations.Add(new BuildingActivityLocationAuthoring
                {
                    id = "usable", type = BuildingActivityLocationType.Counter,
                    positionLocal = new Vector3(2f, 0f, 2f)
                });
                Assert.IsTrue(authority.TryFindReachableActivityLocation(BuildingActivityLocationType.Counter,
                    "main-entrance", 4, out var station, out var route, out string reason), reason);
                Assert.AreEqual("usable", station.Id);
                Assert.That(route.Count, Is.GreaterThan(1));
                Assert.IsFalse(authority.TryFindReachableActivityLocation(BuildingActivityLocationType.Storage,
                    "main-entrance", 4, out _, out _, out _));
            }
            finally { Object.DestroyImmediate(building); }
        }

        [Test]
        public void ResidentInteriorRouteStartsAtActualPositionAndAvoidsFurnitureExclusion()
        {
            GameObject building = new("Resident interior route test");
            try
            {
                BuildingFootprintAuthority authority = building.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = .8f };
                floor.walkableAreas.Add(Rectangle("House floor", -3f, -3f, 3f, 3f));
                floor.exclusionAreas.Add(Rectangle("Cabinet", -.5f, -1f, .5f, 1f));
                GetInteriorFloors(authority).Add(floor);
                building.transform.SetPositionAndRotation(new Vector3(8f, 2f, -5f), Quaternion.Euler(0f, 90f, 0f));
                Vector3 start = building.transform.TransformPoint(new Vector3(-2f, .8f, 0f));
                Vector3 target = building.transform.TransformPoint(new Vector3(2f, .8f, 0f));

                Assert.IsTrue(authority.TryBuildInteriorPath(start, target, 0, null,
                    out List<Vector3> route, out string reason), reason);
                Assert.That(route.Count, Is.GreaterThan(3), "The path should route around the cabinet exclusion.");
                Assert.That(Vector3.Distance(route[0], start), Is.LessThan(.001f));
                foreach (Vector3 point in route.Skip(1))
                    Assert.IsTrue(authority.TryEvaluateInteriorWorldPoint(point, 0, null, out string pointReason), pointReason);

                Vector3 blocked = building.transform.TransformPoint(new Vector3(0f, .8f, 0f));
                Assert.IsFalse(authority.TryEvaluateInteriorWorldPoint(blocked, 0, null, out string blockedReason));
                StringAssert.Contains("Cabinet", blockedReason);
            }
            finally { Object.DestroyImmediate(building); }
        }

        [Test]
        public void PathingRejectsDestinationInsideFurnitureExclusion()
        {
            GameObject worldObject = new("Interior pathing world");
            GameObject pathingObject = new("Interior pathing manager");
            GameObject buildingObject = new("Pathing interior building");
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            try
            {
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(2, 1, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(world, grid);

                TownCell outside = TownCell.CreateDefault();
                outside.terrainZone = TerrainZone.Buildable;
                grid.SetCell(new GridCoord(0, 0), outside);

                BuildingFootprintAuthority authority = buildingObject.AddComponent<BuildingFootprintAuthority>();
                InteriorFloorAuthoring floor = new() { floorIndex = 0, floorHeightLocal = 0f };
                floor.walkableAreas.Add(Rectangle("Floor", 2.1f, 0.1f, 3.9f, 1.9f));
                floor.exclusionAreas.Add(Rectangle("Bed", 2.5f, 0.5f, 3.5f, 1.5f));
                GetInteriorFloors(authority).Add(floor);

                PlacedBuilding placed = new() { id = 4, runtimeFootprintAuthority = authority };
                ((List<PlacedBuilding>)typeof(TownWorldController)
                    .GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world)).Add(placed);
                TownCell interior = TownCell.CreateDefault();
                interior.terrainZone = TerrainZone.Buildable;
                interior.occupancy = CellOccupancy.Building | CellOccupancy.Anchor;
                interior.buildingId = placed.id;
                grid.SetCell(new GridCoord(1, 0), interior);

                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                settings.logFailedRequests = false;
                pathing.Configure(world, settings);
                Assert.IsFalse(pathing.TryFindPath(
                    new PathingRequest(new GridCoord(0, 0), new GridCoord(1, 0), suppressFailureLog: true),
                    out PathingResult result));
                Assert.That(result.FailureCategory, Is.EqualTo(PathingFailureCategory.TargetNotWalkable));
                StringAssert.Contains("Bed", result.FailureReason);
            }
            finally
            {
                Object.DestroyImmediate(pathingObject);
                Object.DestroyImmediate(buildingObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(settings);
            }
        }

        private static InteriorPolygonAuthoring Rectangle(string label, float minX, float minZ, float maxX, float maxZ)
        {
            return new InteriorPolygonAuthoring
            {
                label = label,
                vertices = new List<Vector3>
                {
                    new(minX, 0f, minZ), new(maxX, 0f, minZ), new(maxX, 0f, maxZ), new(minX, 0f, maxZ)
                }
            };
        }

        private static List<InteriorFloorAuthoring> GetInteriorFloors(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("interiorFloors", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<InteriorFloorAuthoring>)field.GetValue(authority);
        }

        private static List<BuildingActivityLocationAuthoring> GetActivityLocations(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("activityLocations", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<BuildingActivityLocationAuthoring>)field.GetValue(authority);
        }

        private static List<BuildingEntrancePairAuthoring> GetEntrancePairs(BuildingFootprintAuthority authority)
        {
            FieldInfo field = typeof(BuildingFootprintAuthority).GetField("entrancePairs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (List<BuildingEntrancePairAuthoring>)field.GetValue(authority);
        }
    }
}
