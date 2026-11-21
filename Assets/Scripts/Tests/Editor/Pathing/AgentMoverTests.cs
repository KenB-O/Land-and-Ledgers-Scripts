using LandLedgers.Pathing;
using LandLedgers.World;
using NUnit.Framework;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor;

namespace LandLedgers.EditorTests.Pathing
{
    public sealed class AgentMoverTests
    {
        [Test]
        public void InteriorDepartureReportsExteriorFailureAfterInteriorRouteWasFound()
        {
            string reason = AgentMover.ResolveInteriorDoorToDoorFailure(
                sawOutdoorThresholdStart: false,
                foundWalkableInteriorRoute: true,
                lastInteriorFailure: "Point is outside the authored walkable floor area on floor 0.",
                lastExteriorFailure: "entrance 'main-entrance' exterior probe (12, 8) -> (30, 8) failed (NoPath: disconnected).",
                target: new GridCoord(30, 8));

            StringAssert.Contains("A walkable interior route to an authored entrance exists", reason);
            StringAssert.Contains("requested destination (30, 8)", reason);
            StringAssert.Contains("exterior probe (12, 8) -> (30, 8) failed (NoPath: disconnected)", reason);
            StringAssert.DoesNotContain("outside the authored walkable floor", reason,
                "A stale rejected entrance must not mask the later exterior graph failure.");
        }

        [Test]
        public void InteriorDepartureKeepsFloorFailureWhenNoEntranceHasReachableInterior()
        {
            string reason = AgentMover.ResolveInteriorDoorToDoorFailure(
                sawOutdoorThresholdStart: false,
                foundWalkableInteriorRoute: false,
                lastInteriorFailure: "Point is outside the authored walkable floor area on floor 0.",
                lastExteriorFailure: "",
                target: new GridCoord(30, 8));

            Assert.AreEqual("Point is outside the authored walkable floor area on floor 0.", reason);
        }

        [TestCase(0f, 1.25f)]
        [TestCase(1.1f, 1.65f)]
        [TestCase(2f, 3f)]
        public void BuildingActivityStagingRadiusIsOneSharedDoorwayThreshold(float clearWidth, float expected)
        {
            Assert.AreEqual(expected, AgentMover.BuildingActivityStagingRadius(clearWidth), 0.001f);
        }

        [TestCase(false, "authored door threshold", 0)]
        [TestCase(true, "authored porch activity", 0)]
        [TestCase(false, "rotated authored door threshold", 90)]
        [TestCase(true, "rotated authored porch activity", 90)]
        [TestCase(false, "rotated authored door threshold", 180)]
        [TestCase(true, "rotated authored porch activity", 180)]
        [TestCase(false, "rotated authored door threshold", 270)]
        [TestCase(true, "rotated authored porch activity", 270)]
        public void AuthoredOutdoorOriginRoutesOutwardWithoutInteriorFloorStartValidation(
            bool startAtPorch, string originDescription, int rotationDegrees)
        {
            const string prefabPath = "Assets/Environment/Buildings/House Natural Wood.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "The authored household doorway fixture must be available.");
            var cleanup = new List<Object>();
            try
            {
                GameObject buildingObject = Object.Instantiate(prefab);
                cleanup.Add(buildingObject);
                buildingObject.transform.SetPositionAndRotation(new Vector3(10f, 0f, -20f),
                    Quaternion.Euler(0f, rotationDegrees, 0f));
                BuildingFootprintAuthority authority = buildingObject.GetComponent<BuildingFootprintAuthority>();
                Assert.IsNotNull(authority);
                BuildingEntrancePairAuthoring entrance = authority.EntrancePairs
                    .SingleOrDefault(x => x != null && x.id == "main-entrance");
                Assert.IsNotNull(entrance);
                BuildingActivityLocationAuthoring porch = authority.ActivityLocations
                    .SingleOrDefault(x => x != null && x.id == "porch-dropoff");
                Assert.IsNotNull(porch);

                GameObject worldObject = new("Authored doorway route test world");
                cleanup.Add(worldObject);
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(120, 120, 2f, new Vector3(-120f, 0f, -120f));
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(world, grid);
                for (int z = 0; z < grid.Depth; z++)
                for (int x = 0; x < grid.Width; x++) grid.SetCell(new GridCoord(x, z), BuildableCell());

                Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
                GridCoord thresholdCell = grid.WorldToCoord(outdoor);
                TownCell buildingCell = grid.GetCell(thresholdCell);
                buildingCell.occupancy = CellOccupancy.Building;
                buildingCell.buildingId = 9;
                grid.SetCell(thresholdCell, buildingCell);
                Vector3 porchPosition = authority.transform.TransformPoint(porch.positionLocal);
                GridCoord porchCell = grid.WorldToCoord(porchPosition);
                TownCell porchBuildingCell = grid.GetCell(porchCell);
                porchBuildingCell.occupancy = CellOccupancy.Building;
                porchBuildingCell.buildingId = 9;
                grid.SetCell(porchCell, porchBuildingCell);

                GameObject pathingObject = new("Authored doorway route test pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                settings.logFailedRequests = false;
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                GameObject actor = new("Authored outdoor origin route test actor");
                cleanup.Add(actor);
                AgentMover mover = actor.AddComponent<AgentMover>();
                mover.Configure(pathing);
                actor.transform.position = startAtPorch ? porchPosition : outdoor;
                mover.ShowAtCurrentLocation();
                Physics.SyncTransforms();
                GridCoord targetCell = new(100, 60);
                Vector3 physicalStart = actor.transform.position;
                BuildingAnchor target = new() { type = AnchorType.FrontDoor, coord = targetCell };

                Assert.IsTrue(AgentMover.IsAtAuthorizedOutdoorOrigin(authority, physicalStart), originDescription);
                Assert.IsTrue(grid.GetCell(grid.WorldToCoord(physicalStart)).HasBuilding,
                    "The fixture must exercise an authored outdoor point whose coarse pathing cell overlaps the building raster.");
                Assert.IsTrue(mover.BeginInteriorDoorToDoor(authority, target, out string refusal),
                    originDescription + ": " + refusal);
                Assert.IsTrue(mover.IsRouteActive);
                Assert.AreEqual(physicalStart, actor.transform.position,
                    "Starting the route must preserve the actor's real authored outdoor position without a snap or projection.");
                Assert.AreEqual(pathing.CoordToPathWorld(targetCell), mover.RouteDestinationWorld);

                if (startAtPorch)
                {
                    GameObject returningActor = new("Porch resident returning indoors");
                    cleanup.Add(returningActor);
                    AgentMover returningMover = returningActor.AddComponent<AgentMover>();
                    returningMover.Configure(pathing);
                    returningActor.transform.position = porchPosition;
                    returningMover.ShowAtCurrentLocation();
                    Physics.SyncTransforms();
                    Assert.IsTrue(authority.TryBuildInteriorActivityRoute("main-entrance", "cleaning",
                        out List<Vector3> cleaningPath, out string activityRouteFailure), activityRouteFailure);
                    Assert.IsTrue(returningMover.BeginBuildingActivityRoute(authority,
                        "main-entrance", "cleaning"),
                        "A resident at the authored porch must be able to follow the physical door and interior route.");
                    Assert.IsTrue(returningMover.IsRouteActive);
                    Assert.AreEqual(porchPosition, returningActor.transform.position,
                        "Beginning the return route must not teleport the resident onto the doorway threshold.");
                    Assert.AreEqual(cleaningPath[^1], returningMover.RouteDestinationWorld,
                        "The route must terminate at the authored interior activity location.");

                    GameObject unverifiedOriginActor = new("Unverified resident origin");
                    cleanup.Add(unverifiedOriginActor);
                    AgentMover unverifiedOriginMover = unverifiedOriginActor.AddComponent<AgentMover>();
                    unverifiedOriginMover.Configure(pathing);
                    unverifiedOriginActor.transform.position = authority.transform.TransformPoint(
                        entrance.indoorPositionLocal);
                    unverifiedOriginMover.ShowAtCurrentLocation();
                    Assert.IsFalse(unverifiedOriginMover.BeginBuildingActivityRoute(authority,
                            "main-entrance", "cleaning"),
                        "A doorway route must not authorize an arbitrary interior or footprint-overlap origin.");
                    Assert.IsFalse(unverifiedOriginMover.IsRouteActive);
                }
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void MissingActiveRouteDataFailsWithoutEmittingArrival()
        {
            GameObject actor = new("Invalid Route Actor");
            try
            {
                AgentMover mover = actor.AddComponent<AgentMover>();
                FieldInfo state = typeof(AgentMover).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
                state.SetValue(mover, System.Enum.Parse(state.FieldType, "Moving"));
                int arrivals = 0;
                mover.ArrivedAtDestination += _ => arrivals++;
                LogAssert.Expect(LogType.Warning, "AgentMover: route failed for Invalid Route Actor: path or pathing settings became unavailable before arrival.");
                typeof(AgentMover).GetMethod("TickMove", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(mover, new object[] { 10f });
                Assert.IsFalse(mover.IsRouteActive);
                Assert.IsFalse(mover.IsArrivedVisible);
                Assert.AreEqual(0, arrivals);
                Assert.AreEqual(Vector3.zero, actor.transform.position);
            }
            finally { Object.DestroyImmediate(actor); }
        }

        [Test]
        public void GridRouteRoundingSoftensRightAngleWithoutChangingEndpointsOrLeavingCornerCell()
        {
            List<Vector3> route = new()
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(1f, 0f, 1f)
            };
            MethodInfo builder = typeof(AgentMover).GetMethod("BuildCornerRoundedPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(builder);

            List<Vector3> rounded = (List<Vector3>)builder.Invoke(null, new object[] { route, 0.2f });

            Assert.AreEqual(route[0], rounded[0]);
            Assert.AreEqual(route[^1], rounded[^1]);
            Assert.Greater(rounded.Count, route.Count);
            Assert.AreEqual(0.8f, rounded[1].x, 0.001f);
            Assert.AreEqual(0f, rounded[1].z, 0.001f);
            Assert.AreEqual(1f, rounded[^2].x, 0.001f);
            Assert.AreEqual(0.2f, rounded[^2].z, 0.001f);
            for (int i = 2; i < rounded.Count - 2; i++)
                Assert.LessOrEqual(Vector3.Distance(rounded[i], route[1]), 0.201f,
                    "The rounded turn must remain in the already validated corner cell.");
        }

        [Test]
        public void GridRouteRoundingLeavesStraightAndReversingPathsUnchanged()
        {
            MethodInfo builder = typeof(AgentMover).GetMethod("BuildCornerRoundedPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(builder);
            List<Vector3> straight = new()
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f
            };
            List<Vector3> reversal = new()
            {
                Vector3.zero,
                Vector3.right,
                Vector3.zero
            };

            List<Vector3> straightResult = (List<Vector3>)builder.Invoke(null, new object[] { straight, 0.2f });
            List<Vector3> reversalResult = (List<Vector3>)builder.Invoke(null, new object[] { reversal, 0.2f });

            CollectionAssert.AreEqual(straight, straightResult);
            CollectionAssert.AreEqual(reversal, reversalResult);
        }

        [TestCase(0f, 1.5f, false, 0f)]
        [TestCase(1f, 1.5f, false, 1f)]
        [TestCase(10f, 1.5f, false, 1.5f)]
        [TestCase(3000f, 1.5f, false, 1.5f)]
        [TestCase(3000f, 1.5f, true, 0f)]
        public void VisibleMovementRateIsBoundedWithoutChangingClock(float clockRate, float cap, bool paused, float expected)
        {
            Assert.AreEqual(expected, AgentMover.ResolvePresentationTimeScale(clockRate, cap, paused), 0.0001f);
        }

        [Test]
        public void AuthoritativeRouteUsesClockElapsedAcrossObserveAnd500XTransitions()
        {
            Assert.AreEqual(5f, AgentMover.ResolveAuthoritativeRouteDelta(100d, 105d), 0.0001f);
            Assert.AreEqual(30f, AgentMover.ResolveAuthoritativeRouteDelta(105d, 135d), 0.0001f);
            Assert.AreEqual(5f, AgentMover.ResolveAuthoritativeRouteDelta(135d, 140d), 0.0001f);
            Assert.AreEqual(0f, AgentMover.ResolveAuthoritativeRouteDelta(140d, 139d), 0.0001f);
            Assert.AreEqual(1.5f, AgentMover.ResolvePresentationTimeScale(30000f, 1.5f, false), 0.0001f,
                "The animation presentation cap remains independent of route time.");

            var cleanup = new List<Object>();
            var segments = new List<(Vector3 from, Vector3 to)>();
            System.Action<Vector3, Vector3> onSegment = (from, to) => segments.Add((from, to));
            try
            {
                GameObject worldObject = new("Clock-Driven Movement Test World");
                cleanup.Add(worldObject);
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(120, 3, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(world, grid);
                for (int z = 0; z < grid.Depth; z++)
                for (int x = 0; x < grid.Width; x++) grid.SetCell(new GridCoord(x, z), BuildableCell());

                GameObject pathingObject = new("Clock-Driven Movement Test Pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                settings.logFailedRequests = false;
                settings.pedestrianSpeedMetersPerSecond = 3.2f;
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                GameObject actor = new("Clock-Driven Movement Test Actor");
                cleanup.Add(actor);
                AgentMover mover = actor.AddComponent<AgentMover>();
                mover.Configure(pathing);
                GridCoord start = new(1, 1);
                actor.transform.position = grid.CoordToWorldCenter(start);
                mover.ShowAtCurrentLocation();
                Assert.IsTrue(mover.BeginGridRoute(start, new GridCoord(100, 1)));

                int arrivalCount = 0;
                mover.ArrivedAtDestination += _ => arrivalCount++;
                AgentMover.ActualMovementSegment += onSegment;
                MethodInfo tickMove = typeof(AgentMover).GetMethod("TickMove", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo tickDoorPause = typeof(AgentMover).GetMethod("TickDoorPause", BindingFlags.Instance | BindingFlags.NonPublic);

                double clock = 100d;
                AdvanceRoute(mover, tickMove, tickDoorPause, ref clock, 0.5f, 10f);
                Assert.IsTrue(mover.IsMoving, "Observe-speed travel should still be active partway through this long route.");
                float afterObserve = actor.transform.position.x;

                AdvanceRoute(mover, tickMove, tickDoorPause, ref clock, 0.001f, 30000f);
                Assert.IsTrue(mover.IsMoving, "The 500x segment should advance the same route without changing its identity.");
                Assert.Greater(actor.transform.position.x - afterObserve, 90f,
                    "Route progress must use the 30 simulated seconds, not the 1.5x animation cap.");
                float after500x = actor.transform.position.x;

                AdvanceRoute(mover, tickMove, tickDoorPause, ref clock, 0.5f, 10f);
                Assert.IsTrue(mover.IsMoving, "Reducing to Observe should continue the remaining route at its normal simulated rate.");
                Assert.Greater(actor.transform.position.x - after500x, 10f);

                float secondsToDestination = mover.RemainingWorldRouteDistance / settings.pedestrianSpeedMetersPerSecond + 2f;
                float leftover = (float)tickMove.Invoke(mover, new object[] { secondsToDestination });
                if (mover.IsRouteActive && leftover > 0f) tickDoorPause.Invoke(mover, new object[] { leftover });
                else if (mover.IsRouteActive) tickDoorPause.Invoke(mover, new object[] { 2f });

                Assert.IsTrue(mover.IsArrivedVisible, "The valid route should arrive after its simulated distance and doorway pause.");
                Assert.AreEqual(1, arrivalCount, "Authoritative arrival must publish once.");
                Assert.Greater(segments.Count, 2, "Fast movement should publish the actual path legs, not a straight chord through the world.");
                foreach ((Vector3 from, Vector3 to) in segments)
                    Assert.LessOrEqual(Vector3.Distance(from, to), grid.CellSizeMeters + 0.01f,
                        "Traffic wear must receive valid route segments at grid scale.");
            }
            finally
            {
                AgentMover.ActualMovementSegment -= onSegment;
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void BlockedRouteDoesNotEmitAuthoritativeArrival()
        {
            var cleanup = new List<Object>();
            try
            {
                GameObject worldObject = new("Blocked Movement Test World");
                cleanup.Add(worldObject);
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(5, 1, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(world, grid);
                for (int x = 0; x < grid.Width; x++) grid.SetCell(new GridCoord(x, 0), BuildableCell());
                TownCell blocked = grid.GetCell(new GridCoord(2, 0));
                blocked.blocked = true;
                grid.SetCell(new GridCoord(2, 0), blocked);

                GameObject pathingObject = new("Blocked Movement Test Pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                settings.logFailedRequests = false;
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                GameObject actor = new("Blocked Movement Test Actor");
                cleanup.Add(actor);
                AgentMover mover = actor.AddComponent<AgentMover>();
                mover.Configure(pathing);
                actor.transform.position = grid.CoordToWorldCenter(new GridCoord(0, 0));
                mover.ShowAtCurrentLocation();
                int arrivalCount = 0;
                mover.ArrivedAtDestination += _ => arrivalCount++;

                Assert.IsFalse(mover.BeginGridRoute(new GridCoord(0, 0), new GridCoord(4, 0)));
                Assert.AreEqual(0, arrivalCount);
                Assert.IsFalse(mover.IsRouteActive);
                Assert.AreEqual(grid.CoordToWorldCenter(new GridCoord(0, 0)), actor.transform.position);
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void HideImmediatelyDisablesRenderersAndColliders()
        {
            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                AgentMover mover = agentObject.AddComponent<AgentMover>();
                Renderer renderer = agentObject.GetComponent<Renderer>();
                Collider collider = agentObject.GetComponent<Collider>();
                renderer.enabled = true;
                collider.enabled = true;

                mover.HideImmediately();

                Assert.IsTrue(mover.IsHidden);
                Assert.AreEqual(0, mover.CurrentPath.Count);
                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentObject);
            }
        }

        [Test]
        public void HideImmediatelyDisablesConfiguredVisualChild()
        {
            GameObject agentRoot = new("Agent Root");
            GameObject visualChild = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                visualChild.transform.SetParent(agentRoot.transform, false);
                AgentMover mover = agentRoot.AddComponent<AgentMover>();
                mover.ConfigureVisualRoot(visualChild.transform);

                Renderer renderer = visualChild.GetComponent<Renderer>();
                Collider collider = visualChild.GetComponent<Collider>();
                renderer.enabled = true;
                collider.enabled = true;

                mover.HideImmediately();

                Assert.IsTrue(mover.IsHidden);
                Assert.AreEqual(0, mover.CurrentPath.Count);
                Assert.IsFalse(renderer.enabled);
                Assert.IsFalse(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentRoot);
            }
        }

        [Test]
        public void ShowAtCurrentLocationKeepsActorVisibleWithoutStartingMovement()
        {
            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                AgentMover mover = agentObject.AddComponent<AgentMover>();
                Renderer renderer = agentObject.GetComponent<Renderer>();
                Collider collider = agentObject.GetComponent<Collider>();
                renderer.enabled = false;
                collider.enabled = false;

                mover.ShowAtCurrentLocation();

                Assert.IsFalse(mover.IsHidden);
                Assert.IsFalse(mover.IsMoving);
                Assert.IsTrue(mover.IsArrivedVisible);
                Assert.AreEqual(0, mover.CurrentPath.Count);
                Assert.IsTrue(renderer.enabled);
                Assert.IsTrue(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentObject);
            }
        }

        [Test]
        public void RouteArrivalCanRetainTheSameVisibleActor()
        {
            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            try
            {
                AgentMover mover = agentObject.AddComponent<AgentMover>();
                Renderer renderer = agentObject.GetComponent<Renderer>();
                Collider collider = agentObject.GetComponent<Collider>();
                typeof(AgentMover)
                    .GetField("hideOnArrival", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, false);

                typeof(AgentMover)
                    .GetMethod("CompleteRouteArrival", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(mover, null);

                Assert.IsTrue(mover.IsArrivedVisible);
                Assert.IsFalse(mover.IsHidden);
                Assert.IsTrue(renderer.enabled);
                Assert.IsTrue(collider.enabled);
            }
            finally
            {
                Object.DestroyImmediate(agentObject);
            }
        }

        [Test]
        public void OnDisableSkipsAnimatorBlendWriteWhenControllerIsMissing()
        {
            GameObject agentRoot = new("Agent Root");
            try
            {
                LogAssert.Expect(LogType.Warning,
                    "AgentMover on 'Agent Root' found an Animator without a controller. Imported movement animation will be disabled.");
                Animator animator = agentRoot.AddComponent<Animator>();
                AgentMover mover = agentRoot.AddComponent<AgentMover>();

                typeof(AgentMover)
                    .GetField("cachedAnimator", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, animator);
                typeof(AgentMover)
                    .GetField("animatorHasBlendParameter", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, true);

                agentRoot.SetActive(false);

                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                Object.DestroyImmediate(agentRoot);
            }
        }

        [Test]
        public void DoorArrivalDoesNotCompleteWhileSimulationIsPaused()
        {
            GameObject agentRoot = new("Agent Root");
            try
            {
                AgentMover mover = agentRoot.AddComponent<AgentMover>();
                typeof(AgentMover)
                    .GetField("state", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(mover, System.Enum.ToObject(
                        typeof(AgentMover).GetNestedType("AgentMoveState", BindingFlags.NonPublic), 2));

                int arrived = 0;
                mover.ArrivedAtDestination += _ => arrived++;
                typeof(AgentMover)
                    .GetMethod("TickDoorPause", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(mover, new object[] { 0f });

                Assert.IsTrue(mover.IsRouteActive);
                Assert.AreEqual(0, arrived);
            }
            finally
            {
                Object.DestroyImmediate(agentRoot);
            }
        }

        [Test]
        public void BeginGridRouteUsesPathfindingAndKeepsTheActorVisible()
        {
            var cleanup = new System.Collections.Generic.List<Object>();
            try
            {
            GameObject worldObject = new("Transport Staging Test World");
            cleanup.Add(worldObject);
            TownWorldController world = worldObject.AddComponent<TownWorldController>();
            TownGrid grid = new(8, 3, 2f, Vector3.zero);
            typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(world, grid);
            for (int z = 0; z < grid.Depth; z++)
            for (int x = 0; x < grid.Width; x++)
            {
                TownCell cell = TownCell.CreateDefault();
                cell.terrainZone = TerrainZone.Buildable;
                grid.SetCell(new GridCoord(x, z), cell);
            }

            GameObject pathingObject = new("Transport Staging Test Pathing");
            cleanup.Add(pathingObject);
            PathingManager pathing = pathingObject.AddComponent<PathingManager>();
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            settings.logFailedRequests = false;
            settings.allowRoughTerrain = false;
            cleanup.Add(settings);
            pathing.Configure(world, settings);

            GameObject actor = new("Transport Staging Test Actor");
            cleanup.Add(actor);
            AgentMover mover = actor.AddComponent<AgentMover>();
            mover.Configure(pathing);
            GridCoord start = new(1, 1);
            actor.transform.position = grid.CoordToWorldCenter(start);
            mover.ShowAtCurrentLocation();
            Vector3 physicalStart = actor.transform.position;

            Assert.IsTrue(mover.BeginGridRoute(start, new GridCoord(6, 1)));
            Assert.IsTrue(mover.IsRouteActive);
            Assert.IsFalse(mover.IsHidden);
            Assert.Greater(mover.CurrentPath.Count, 1, "Staging must traverse a real path, not snap to the seat.");
            Assert.AreEqual(physicalStart, actor.transform.position, "Starting a route must not teleport the actor to a logical start coordinate.");

            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void ExteriorRouteCanReachAuthorizedPorchPointBeyondWalkableDoorCell()
        {
            var cleanup = new List<Object>();
            try
            {
                GameObject worldObject = new("Porch endpoint test world");
                cleanup.Add(worldObject);
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(8, 3, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(world, grid);
                for (int z = 0; z < grid.Depth; z++)
                for (int x = 0; x < grid.Width; x++) grid.SetCell(new GridCoord(x, z), BuildableCell());
                TownCell blockedPorchCell = BuildableCell();
                blockedPorchCell.occupancy = CellOccupancy.Building;
                blockedPorchCell.buildingId = 7;
                grid.SetCell(new GridCoord(7, 1), blockedPorchCell);

                GameObject pathingObject = new("Porch endpoint test pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                settings.logFailedRequests = false;
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                GameObject actor = new("Porch endpoint test actor");
                cleanup.Add(actor);
                AgentMover mover = actor.AddComponent<AgentMover>();
                mover.Configure(pathing);
                GridCoord start = new(1, 1);
                GridCoord doorway = new(6, 1);
                Vector3 porch = grid.CoordToWorldCenter(new GridCoord(7, 1));
                actor.transform.position = grid.CoordToWorldCenter(start);
                mover.ShowAtCurrentLocation();

                Assert.IsFalse(pathing.TryFindPath(start, new GridCoord(7, 1), out _, mover,
                    "blocked porch cell regression"),
                    "The building cell must remain rejected as a grid-path destination.");
                Assert.IsTrue(mover.BeginGridRouteWithFinalWaypoints(start, doorway,
                    new[] { grid.CoordToWorldCenter(doorway), porch }),
                    "The actor must use the exterior graph to a walkable doorway cell before the short porch approach.");
                Assert.IsTrue(mover.IsRouteActive);
                Assert.AreEqual(new GridCoord(7, 1), mover.CurrentPath[^1],
                    "The final physical waypoint may map to the blocked building cell without making that cell pathable.");
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        [Test]
        public void BeginGridRouteRejectsStartThatDoesNotMatchPhysicalPosition()
        {
            var cleanup = new System.Collections.Generic.List<Object>();
            try
            {
                GameObject worldObject = new("Mismatched route start world");
                cleanup.Add(worldObject);
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(8, 3, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(world, grid);
                for (int z = 0; z < grid.Depth; z++)
                for (int x = 0; x < grid.Width; x++) grid.SetCell(new GridCoord(x,z), BuildableCell());

                GameObject pathingObject = new("Mismatched route start pathing");
                cleanup.Add(pathingObject);
                PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
                settings.logFailedRequests = false;
                cleanup.Add(settings);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                GameObject actor = new("Mismatched route start actor");
                cleanup.Add(actor);
                AgentMover mover = actor.AddComponent<AgentMover>();
                mover.Configure(pathing);
                GridCoord actual = new(1, 1);
                actor.transform.position = grid.CoordToWorldCenter(actual);
                mover.ShowAtCurrentLocation();
                Vector3 physicalStart = actor.transform.position;

                Assert.IsFalse(mover.BeginGridRoute(new GridCoord(3, 1), new GridCoord(6, 1)));
                Assert.AreEqual(physicalStart, actor.transform.position);
                Assert.IsFalse(mover.IsRouteActive);
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                    if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            }
        }

        private static TownCell BuildableCell()
        {
            TownCell cell = TownCell.CreateDefault();
            cell.terrainZone = TerrainZone.Buildable;
            return cell;
        }

        private static void AdvanceRoute(AgentMover mover, MethodInfo tickMove, MethodInfo tickDoorPause,
            ref double clock, float realDeltaSeconds, float gameSecondsPerRealSecond)
        {
            double next = clock + realDeltaSeconds * gameSecondsPerRealSecond;
            float gameDelta = AgentMover.ResolveAuthoritativeRouteDelta(clock, next);
            float leftover = (float)tickMove.Invoke(mover, new object[] { gameDelta });
            if (mover.IsRouteActive && leftover > 0f)
                tickDoorPause.Invoke(mover, new object[] { leftover });
            clock = next;
        }
    }
}
