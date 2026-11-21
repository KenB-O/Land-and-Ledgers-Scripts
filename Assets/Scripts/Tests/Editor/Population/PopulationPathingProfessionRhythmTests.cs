using System.Reflection;
using System.Linq;
using LandLedgers.FirstLedger;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEngine.TestTools;

namespace LandLedgers.EditorTests.Population
{
    public sealed class PopulationPathingProfessionRhythmTests
    {
        [Test]
        public void NaturalWoodHouse_PorchActivityMayBeReachedAlongPorchBeyondDoorWidthRadius()
        {
            const string prefabPath = "Assets/Environment/Buildings/House Natural Wood.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, "The authored household prefab used by the captured route failures must be available.");
            BuildingFootprintAuthority authority = prefab.GetComponent<BuildingFootprintAuthority>();
            Assert.IsNotNull(authority);
            BuildingEntrancePairAuthoring entrance = authority.EntrancePairs.SingleOrDefault(x => x != null && x.id == "main-entrance");
            BuildingActivityLocationAuthoring porch = authority.ActivityLocations.SingleOrDefault(x => x != null && x.id == "porch-dropoff");
            Assert.IsNotNull(entrance);
            Assert.IsNotNull(porch);

            Vector3 delta = porch.positionLocal - entrance.outdoorApproachLocal;
            delta.y = 0f;
            float distance = delta.magnitude;
            float oldDoorWidthRadius = Mathf.Max(1.5f, entrance.clearWidthMeters * 2f);
            Assert.Greater(distance, oldDoorWidthRadius,
                "This regression must keep representing the authored porch point beyond the former arbitrary radius.");
            Assert.IsTrue(PopulationPathingDirector.CanApproachPorchActivityDoorway(distance, directApproachClear: true),
                "A clear physical approach along the authored porch is reachable regardless of doorway width.");
            Assert.IsFalse(PopulationPathingDirector.CanApproachPorchActivityDoorway(distance, directApproachClear: false),
                "Removing the proximity cap must not permit a physically obstructed approach.");
        }

        [Test]
        public void NaturalWoodHouse_InteriorOriginRequiresAnAuthoredWalkableRouteToItsEntrance()
        {
            const string prefabPath = "Assets/Environment/Buildings/House Natural Wood.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);
            BuildingFootprintAuthority authority = prefab.GetComponent<BuildingFootprintAuthority>();
            Assert.IsNotNull(authority);
            BuildingEntrancePairAuthoring entrance = authority.EntrancePairs
                .SingleOrDefault(x => x != null && x.id == "main-entrance");
            Assert.IsNotNull(entrance);

            Vector3 indoor = authority.transform.TransformPoint(entrance.indoorPositionLocal);
            Assert.IsTrue(PopulationPathingDirector.TryFindReachableInteriorEntrance(
                    authority, indoor, out BuildingEntrancePairAuthoring resolved),
                "An authored indoor point must be recognized through its actual floor and entrance space.");
            Assert.AreEqual(entrance.id, resolved.id);

            Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
            Assert.IsFalse(PopulationPathingDirector.TryFindReachableInteriorEntrance(
                    authority, outdoor, out _),
                "A position outside the authored walkable interior must not become an interior path start merely because its coarse grid cell overlaps the footprint.");
        }

        [Test]
        public void NaturalWoodHouse_ExteriorDoorwayThresholdIsRecognizedWithoutCallingItInterior()
        {
            const string prefabPath = "Assets/Environment/Buildings/House Natural Wood.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(prefab);
            BuildingFootprintAuthority authority = prefab.GetComponent<BuildingFootprintAuthority>();
            Assert.IsNotNull(authority);
            BuildingEntrancePairAuthoring entrance = authority.EntrancePairs
                .SingleOrDefault(x => x != null && x.id == "main-entrance");
            Assert.IsNotNull(entrance);

            Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
            Vector3 indoor = authority.transform.TransformPoint(entrance.indoorPositionLocal);
            Assert.IsTrue(AgentMover.IsAtAuthoredOutdoorApproach(authority, outdoor, entrance.id),
                "The exact authored outside threshold remains a physical start even when its coarse grid cell is assigned to the building.");
            Assert.IsFalse(AgentMover.IsAtAuthoredOutdoorApproach(authority, indoor, entrance.id),
                "The indoor threshold must still use authored walkable-floor validation.");
            Assert.IsFalse(AgentMover.IsAtAuthoredOutdoorApproach(authority,
                    outdoor + Vector3.right, entrance.id),
                "The doorway exception must not expand into an arbitrary building-footprint start radius.");
        }

        [Test]
        public void AuthorizedPhysicalOriginResolvesToItsOwningBuilding()
        {
            Assert.AreEqual(0, PopulationPathingDirector.ResolveAuthorizedPhysicalOriginBuildingId(
                    physicalCellBuildingId: 0,
                    physicalCellHasBuilding: true,
                    actorAtAuthorizedOrigin: true,
                    hasAuthorizedRoute: true),
                "A real, authorized physical start resolves to its actual owning building.");
            Assert.AreEqual(-1, PopulationPathingDirector.ResolveAuthorizedPhysicalOriginBuildingId(
                    physicalCellBuildingId: 0,
                    physicalCellHasBuilding: true,
                    actorAtAuthorizedOrigin: true,
                    hasAuthorizedRoute: false),
                "A physical position without an authored valid route must remain blocked.");
            Assert.AreEqual(-1, PopulationPathingDirector.ResolveAuthorizedPhysicalOriginBuildingId(
                    physicalCellBuildingId: 0,
                    physicalCellHasBuilding: false,
                    actorAtAuthorizedOrigin: true,
                    hasAuthorizedRoute: true),
                "A building-cell match alone cannot authorize entry or interior movement.");
        }

        [Test]
        public void ExteriorRouteRefusal_RecordsPhysicalAndLogicalOriginsSeparately()
        {
            string diagnostic = PopulationPathingDirector.FormatExteriorRouteRefusal(
                personId: 7,
                worldPosition: new Vector3(-67.806f, 1.392f, -5.464f),
                actorCell: new GridCoord(12, 34),
                hasPhysicalBuilding: true,
                physicalBuildingId: 9,
                logicalDestinationBuildingId: 0,
                homeBuildingId: 9,
                targetBuildingId: 0,
                targetDoorCell: new GridCoord(44, 55));

            StringAssert.Contains("P7", diagnostic);
            StringAssert.Contains("physicalBuilding=9", diagnostic);
            StringAssert.Contains("logicalDestination=0", diagnostic);
            StringAssert.Contains("home=9", diagnostic);
            StringAssert.Contains("targetBuilding=0", diagnostic);
            StringAssert.Contains("actorCell=(12, 34)", diagnostic);
        }

        [TestCase("task-1", 4, "stove", "task-1", "stove", 4, true)]
        [TestCase("task-1", 4, "stove", "task-2", "stove", 4, false)]
        [TestCase("task-1", 4, "stove", "task-1", "stove", 5, false)]
        [TestCase("task-1", 4, "stove", "task-1", "cleaning", 4, false)]
        [TestCase(null, -1, null, "task-1", "stove", 4, false)]
        public void DomesticActivityWaitsOnlyForItsOwnStillActiveRoute(string ownerTaskId,
            int ownerRouteGeneration, string ownerActivityId, string requestedTaskId,
            string requestedActivityId, int currentRouteGeneration, bool expected)
        {
            Assert.AreEqual(expected, PopulationPathingDirector.ShouldContinueOwnedDomesticActivityRoute(
                ownerTaskId, ownerRouteGeneration, ownerActivityId, requestedTaskId,
                requestedActivityId, currentRouteGeneration));
        }

        [TestCase(600.0, 0.0, 0.0, 600f, 3600f, 20f, 20f, true)]
        [TestCase(599.9, 0.0, 0.0, 600f, 3600f, 20f, 20f, false)]
        [TestCase(700.0, 0.0, 0.0, 600f, 3600f, 19f, 20f, false)]
        [TestCase(400.0, 0.0, 0.0, 600f, 3600f, 20f, 20f, false)]
        [TestCase(double.NaN, 0.0, 0.0, 600f, 3600f, 20f, 20f, false)]
        public void DomesticActivityRouteWatchdogOnlyAbortsAfterNoProgressAllowance(
            double now, double started, double lastProgress, float noProgressTimeout, float routeTimeout,
            float remaining, float previousRemaining,
            bool expectedAbort)
        {
            Assert.AreEqual(expectedAbort,
                PopulationPathingDirector.ShouldAbortStalledDomesticActivityRoute(
                    now, started, lastProgress, noProgressTimeout, routeTimeout, remaining, previousRemaining));
        }

        [Test]
        public void DomesticActivityRouteWatchdogStopsEvenWhenRouteKeepsMakingUnfinishedProgress()
        {
            Assert.IsTrue(PopulationPathingDirector.ShouldAbortStalledDomesticActivityRoute(
                3600.0, 0.0, 3500.0, 600f, 3600f, 19.0f, 20.0f),
                "The hard route bound must release a task whose physical route makes progress but never arrives.");
        }

        [Test]
        public void DomesticActivityRouteWatchdogDiagnosticIncludesPhysicalFailureContext()
        {
            string diagnostic = PopulationPathingDirector.FormatDomesticActivityRouteWatchdogDiagnostic(
                "domestic-chores:280", "P14", "cleaning", 37,
                true, false, "(12.50,0.80,-3.25)", 4.25f, 4.30f, "0.18",
                720.0, 610.0, 600f, 1800f);

            StringAssert.Contains("person=P14", diagnostic);
            StringAssert.Contains("activity='cleaning'", diagnostic);
            StringAssert.Contains("routeGeneration=37", diagnostic);
            StringAssert.Contains("routeActive=True", diagnostic);
            StringAssert.Contains("hidden=False", diagnostic);
            StringAssert.Contains("position=(12.50,0.80,-3.25)", diagnostic);
            StringAssert.Contains("remainingDistance=4.25m (previous=4.30m)", diagnostic);
            StringAssert.Contains("elapsed=720.0/1800.0s", diagnostic);
            StringAssert.Contains("noProgress=610.0/600.0s", diagnostic);
        }

        [TestCase(true, true, true, false, "off-map journey")]
        [TestCase(false, false, false, false, "no managed on-map physical actor")]
        [TestCase(false, true, false, false, "no movement component")]
        [TestCase(false, true, true, true, "aboard a wagon")]
        [TestCase(false, true, true, false, "")]
        public void DomesticActivityPresentationFailureNamesTheActualUnavailableState(
            bool isOffMap, bool hasManagedActor, bool hasMover, bool isRiding, string expectedFragment)
        {
            string reason = PopulationPathingDirector.DomesticActivityPresentationFailure(
                isOffMap, hasManagedActor, hasMover, isRiding);

            if (string.IsNullOrEmpty(expectedFragment))
                Assert.IsEmpty(reason);
            else
                StringAssert.Contains(expectedFragment, reason);
        }

        [Test]
        public void DomesticActivityRouteClockUsesDriverSampleBeforeDirectorFallback()
        {
            bool fallbackRead = false;
            double value = PopulationPathingDirector.ResolveDomesticActivityRouteClock(
                1440.0, () => { fallbackRead = true; return 12.0; });

            Assert.AreEqual(1440.0, value);
            Assert.IsFalse(fallbackRead,
                "The domestic driver's authoritative clock sample must be used without a second clock lookup.");
        }

        [TestCase(double.NaN, 3600.0, 3600.0)]
        [TestCase(double.PositiveInfinity, 7200.0, 7200.0)]
        public void DomesticActivityRouteClockFallsBackOnlyWhenDriverSampleIsInvalid(
            double supplied, double fallback, double expected)
        {
            Assert.AreEqual(expected,
                PopulationPathingDirector.ResolveDomesticActivityRouteClock(supplied, () => fallback));
        }

        [TestCase(20f, 20f, false)]
        [TestCase(19.96f, 20f, false)]
        [TestCase(19.9f, 20f, true)]
        [TestCase(19.94f, 20f, true)]
        [TestCase(19.94f, 20.1f, true)]
        public void DomesticActivityRouteProgressUsesAuthoritativeDistance(float remaining,
            float previousRemaining, bool expectedProgress)
        {
            Assert.AreEqual(expectedProgress,
                PopulationPathingDirector.IsDomesticActivityRouteMakingProgress(remaining, previousRemaining));
        }

        [TestCase("task-1", "stove", 120.0, "task-1", "stove", true)]
        [TestCase("task-1", "stove", 120.0, "task-2", "stove", false)]
        [TestCase("task-1", "stove", 120.0, "task-1", "table", false)]
        [TestCase("task-1", "stove", double.NaN, "task-1", "stove", false)]
        [TestCase("task-1", "stove", double.PositiveInfinity, "task-1", "stove", false)]
        public void DomesticActivityRouteContinuationPreservesDeadlineOnlyForSameValidTaskAndDestination(
            string previousTaskId, string previousActivityId, double previousStarted,
            string requestedTaskId, string requestedActivityId, bool expected)
        {
            Assert.AreEqual(expected, PopulationPathingDirector.ShouldPreserveDomesticActivityRouteDeadline(
                previousTaskId, previousActivityId, previousStarted, requestedTaskId, requestedActivityId));
        }

        [Test]
        public void ShoppingOwnershipProtectsArrivalUntilTheTripReleasesThePerson()
        {
            GameObject owner = new("Route ownership test");
            GameObject actor = new("Shopper");
            try
            {
                var director = owner.AddComponent<PopulationPathingDirector>();
                var mover = actor.AddComponent<AgentMover>();
                mover.ShowAtCurrentLocation();
                var person = new PersonState { id = 42, homeBuildingId = -1, currentDestinationBuildingId = -1 };
                var managedType = typeof(PopulationPathingDirector).GetNestedType("ManagedPersonAgent", BindingFlags.NonPublic);
                var managed = System.Activator.CreateInstance(managedType, person, mover, default(BuildingAnchor));
                var agents = (System.Collections.IList)typeof(PopulationPathingDirector)
                    .GetField("agents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
                agents.Add(managed);
                var send = typeof(PopulationPathingDirector).GetMethod("SendRouteForState", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(PopulationScheduleState), typeof(System.Predicate<PersonState>), typeof(System.Func<PersonState, int>) }, null);
                int targetQueries = 0;
                System.Func<PersonState, int> target = _ => { targetQueries++; return -1; };
                director.ShoppingTripOwnsPerson = id => id == 42;
                send.Invoke(director, new object[] { PopulationScheduleState.ReturningHome, null, target });
                Assert.AreEqual(0, targetQueries, "scheduled movement must not replace an arrived shopper's destination");
                Assert.IsTrue(mover.IsArrivedVisible);

                director.ShoppingTripOwnsPerson = _ => false;
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PopulationPathingDirector blocked a scheduled population route"));
                send.Invoke(director, new object[] { PopulationScheduleState.ReturningHome, null, target });
                Assert.AreEqual(1, targetQueries, "scheduled movement becomes eligible again when the trip releases ownership");
            }
            finally { Object.DestroyImmediate(actor); Object.DestroyImmediate(owner); }
        }

        [Test]
        public void NewPathingDirectorDoesNotManufactureAmbientStreetJourneysByDefault()
        {
            GameObject owner = new GameObject("Pathing configuration test");
            try
            {
                PopulationPathingDirector director = owner.AddComponent<PopulationPathingDirector>();
                FieldInfo field = typeof(PopulationPathingDirector).GetField(
                    "minimumVisibleStreetAgents",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.NotNull(field);
                Assert.That((int)field.GetValue(director), Is.Zero,
                    "Ambient walking is not a simulated reason for travel and must remain opt-in.");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [TestCase("receiving", true, true)]
        [TestCase("Receiving", true, true)]
        [TestCase("receiving", false, false)]
        [TestCase("storage", true, false)]
        public void FreightDoorwayFallbackRequiresAnAuthoredFrontDoorAndReceivingActivity(
            string activityId, bool authoredFrontDoor, bool expected)
        {
            BuildingAnchor anchor = new() { type = AnchorType.FrontDoor, fromAuthoredMarker = authoredFrontDoor };
            var resolver = typeof(PopulationPathingDirector).GetMethod("CanUseAuthoredDoorForFreightReceiving",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(resolver);
            Assert.AreEqual(expected, resolver.Invoke(null, new object[] { activityId, anchor }));
        }

        [TestCase(true, true, false, 7, 3, true)]
        [TestCase(true, false, true, 7, 3, true)]
        [TestCase(true, true, false, 3, 3, false)]
        [TestCase(false, true, false, 7, 3, false)]
        [TestCase(true, false, false, 7, 3, false)]
        public void ExplicitTaskRouteCannotReplaceAnotherActiveOwnedRoute(bool routeActive,
            bool taskDirected, bool scheduled, int activeDestination, int requestedDestination,
            bool expectedToPreserve)
        {
            Assert.AreEqual(expectedToPreserve,
                PopulationPathingDirector.ShouldPreserveActiveTaskRoute(routeActive, taskDirected,
                    scheduled, activeDestination, requestedDestination));
        }

        [Test]
        public void FailedFreightRouteStartKeepsTheActualOriginForRetry()
        {
            var resident = new PersonState
            {
                id = 7,
                homeBuildingId = 4,
                currentDestinationBuildingId = 4,
                scheduleState = PopulationScheduleState.AtHome
            };

            bool started = PopulationPathingDirector.TryStartFreightRoute(resident, 1,
                () => resident.currentDestinationBuildingId == 1);
            Assert.IsFalse(started);
            Assert.AreEqual(4, resident.currentDestinationBuildingId,
                "A failed receiving route must not overwrite the origin used by the next path attempt.");
            Assert.AreEqual(PopulationScheduleState.AtHome, resident.scheduleState);

            started = PopulationPathingDirector.TryStartFreightRoute(resident, 1,
                () => resident.currentDestinationBuildingId == 4);
            Assert.IsTrue(started);
            Assert.AreEqual(1, resident.currentDestinationBuildingId);
            Assert.AreEqual(PopulationScheduleState.AtWork, resident.scheduleState);
        }

        [Test]
        public void FreightReceivingClaimPreservesArrivedCarrierUntilHandoffConsumesIt()
        {
            GameObject owner = new("Freight route ownership test");
            GameObject actor = new("Freight carrier");
            try
            {
                var director = owner.AddComponent<PopulationPathingDirector>();
                var mover = actor.AddComponent<AgentMover>();
                mover.ShowAtCurrentLocation();
                var person = new PersonState { id = 42, homeBuildingId = -1, currentDestinationBuildingId = 7 };
                var managedType = typeof(PopulationPathingDirector).GetNestedType("ManagedPersonAgent", BindingFlags.NonPublic);
                var managed = System.Activator.CreateInstance(managedType, person, mover, default(BuildingAnchor));
                managedType.GetField("freightReceivingTargetBuildingId").SetValue(managed, 7);
                var agents = (System.Collections.IList)typeof(PopulationPathingDirector)
                    .GetField("agents", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
                agents.Add(managed);

                int targetQueries = 0;
                var send = typeof(PopulationPathingDirector).GetMethod("SendRouteForState", BindingFlags.NonPublic | BindingFlags.Instance,
                    null, new[] { typeof(PopulationScheduleState), typeof(System.Predicate<PersonState>), typeof(System.Func<PersonState, int>) }, null);
                send.Invoke(director, new object[] { PopulationScheduleState.ReturningHome, null,
                    new System.Func<PersonState, int>(_ => { targetQueries++; return -1; }) });
                Assert.AreEqual(0, targetQueries,
                    "A schedule must not replace the carrier's arrived receiving position before FreightShipmentService checks it.");
                Assert.IsTrue(mover.IsArrivedVisible);

                director.ReleaseFreightReceivingRoute(42, 7);
                Assert.AreEqual(-1, managedType.GetField("freightReceivingTargetBuildingId").GetValue(managed));
                Assert.IsFalse(PopulationPathingDirector.ShouldPreserveFreightReceivingRoute(-1, false, true));
                Assert.IsTrue(PopulationPathingDirector.ShouldPreserveFreightReceivingRoute(7, false, true));
                Assert.IsTrue(PopulationPathingDirector.ShouldPreserveFreightReceivingRoute(7, true, false));
                Assert.IsFalse(PopulationPathingDirector.ShouldPreserveFreightReceivingRoute(7, false, false),
                    "An ended failed route must not keep a carrier permanently reserved.");
                Assert.IsTrue(PopulationPathingDirector.ShouldDeferRouteForFreightReceiving(7, 3),
                    "Any direct route request must respect a live receiving claim, even when it bypasses the scheduled-route selector.");
                Assert.IsFalse(PopulationPathingDirector.ShouldDeferRouteForFreightReceiving(7, 7),
                    "The receiving owner may continue or resume its own route.");
                Assert.IsFalse(PopulationPathingDirector.ShouldDeferRouteForFreightReceiving(-1, 3),
                    "A released receiving claim must not block ordinary routing.");
                Assert.IsTrue(PopulationPathingDirector.ShouldDeferRouteForFreightMovement(true, -1, 3),
                    "Direct task and shopping routes must honor an assigned wagon-approach owner.");
                Assert.IsTrue(PopulationPathingDirector.ShouldDeferRouteForFreightMovement(false, 7, 3),
                    "Direct routes must honor an active physical receiving owner.");
                Assert.IsFalse(PopulationPathingDirector.ShouldDeferRouteForFreightMovement(false, 7, 7),
                    "The freight receiver may continue routing within its claimed destination.");
                Assert.IsTrue(PopulationPathingDirector.ShouldDeferDomesticActivityForFreightMovement(true, -1),
                    "Domestic activity starts routes directly and must respect an assigned wagon approach.");
                Assert.IsTrue(PopulationPathingDirector.ShouldDeferDomesticActivityForFreightMovement(false, 7),
                    "Domestic activity must not replace a physical cargo receiving route.");
                Assert.IsFalse(PopulationPathingDirector.ShouldDeferDomesticActivityForFreightMovement(false, -1),
                    "Ordinary domestic activity remains available after freight movement releases the person.");
                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("PopulationPathingDirector blocked a scheduled population route"));
                send.Invoke(director, new object[] { PopulationScheduleState.ReturningHome, null,
                    new System.Func<PersonState, int>(_ => { targetQueries++; return -1; }) });
                Assert.AreEqual(1, targetQueries,
                    "After FreightShipmentService releases the claim, normal schedule routing may resume.");
            }
            finally { Object.DestroyImmediate(actor); Object.DestroyImmediate(owner); }
        }

        [Test]
        public void FreightDoorFallbackRetainsProgressReasonAndClearsItOnlyOnArrival()
        {
            Assert.AreEqual("Carrying: route to B3/receiving; remaining=12m.",
                PopulationPathingDirector.ResolveFreightDoorRouteReason(false,
                    "Carrying: route to B3/receiving; remaining=12m."));
            Assert.AreEqual("Carrying: handler is physically travelling to the receiving point.",
                PopulationPathingDirector.ResolveFreightDoorRouteReason(false, string.Empty));
            Assert.AreEqual(string.Empty,
                PopulationPathingDirector.ResolveFreightDoorRouteReason(true,
                    "Carrying: route to B3/receiving; remaining=12m."));
        }

        [Test]
        public void FreightReceivingRefusalDiagnosticCapturesPhysicalOriginClassification()
        {
            string diagnostic = PopulationPathingDirector.FormatFreightHandlerOriginDiagnostic(
                7, new Vector3(4.25f, 0.83f, 12.5f), 14, 42, 1,
                insideFootprint: true,
                insideFloorZero: false,
                authorizedOutdoorOrigin: false,
                currentDestinationBuildingId: 6,
                homeBuildingId: 7,
                workplaceBuildingId: 6,
                scheduleState: "AtWork",
                routeMode: "TaskDirected",
                moverVisible: true,
                routeActive: false);

            StringAssert.Contains("P7 position=(4.25,0.83,12.50)", diagnostic);
            StringAssert.Contains("grid=(14,42) cellBuilding=1", diagnostic);
            StringAssert.Contains("insideFootprint=True insideFloor0=False authorizedOutdoorOrigin=False", diagnostic);
            StringAssert.Contains("currentDestination=6 home=7 workplace=6", diagnostic);
            StringAssert.Contains("schedule=AtWork routeMode=TaskDirected visible=True routeActive=False", diagnostic);
        }

        [Test]
        public void FreightExteriorStationInsideBuildingFootprintUsesDoorwayFallback()
        {
            GameObject worldObject = new("Freight receiving world");
            GameObject pathingObject = new("Freight receiving pathing");
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            try
            {
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownGrid grid = new(5, 5, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(world, grid);
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                Vector3 station = new(3f, 0f, 3f);
                GridCoord stationCell = grid.WorldToCoord(station);
                TownCell walkable = TownCell.CreateDefault();
                walkable.terrainZone = TerrainZone.Buildable;
                grid.SetCell(stationCell, walkable);
                Assert.IsFalse(PopulationPathingDirector.ShouldUseAuthoredDoorForBlockedExteriorActivity(
                    grid, pathing, station), "A valid exterior station should keep its direct physical route.");

                TownCell footprint = walkable;
                footprint.occupancy = CellOccupancy.Building;
                footprint.buildingId = 1;
                grid.SetCell(stationCell, footprint);
                Assert.IsTrue(PopulationPathingDirector.ShouldUseAuthoredDoorForBlockedExteriorActivity(
                    grid, pathing, station), "An exterior station inside a blocked building footprint must use the authored doorway.");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(pathingObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void PopulationPathingWaitsForTheMatchingGeneratedWorldGrid()
        {
            GameObject worldObject = new("Population readiness world");
            GameObject otherWorldObject = new("Other world");
            GameObject pathingObject = new("Population readiness pathing");
            PathingSettings settings = ScriptableObject.CreateInstance<PathingSettings>();
            try
            {
                TownWorldController world = worldObject.AddComponent<TownWorldController>();
                TownWorldController otherWorld = otherWorldObject.AddComponent<TownWorldController>();
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(world, settings);

                Assert.IsFalse(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(world, pathing),
                    "An early tick before town generation must wait instead of attempting agent creation without door anchors.");

                TownGrid generatedGrid = new(8, 8, 2f, Vector3.zero);
                typeof(TownWorldController).GetField("grid", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(world, generatedGrid);

                Assert.IsTrue(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(world, pathing));
                Assert.IsFalse(PopulationPathingDirector.IsGeneratedWorldReadyForPopulation(otherWorld, pathing),
                    "A pathing manager bound to a different world must not be treated as ready for this population.");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(pathingObject);
                Object.DestroyImmediate(otherWorldObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        [Test]
        public void ProductionAndFarmWorkersLeaveBeforeRetailWorkers()
        {
            PersonState rancher = new() { professionId = "ranch_hand" };
            PersonState clerk = new() { professionId = "general_store_clerk" };

            float ranchDeparture = InvokeScheduleTime("GetWorkDepartureTime01", rancher);
            float clerkDeparture = InvokeScheduleTime("GetWorkDepartureTime01", clerk);

            Assert.Less(ranchDeparture, clerkDeparture);
        }

        [Test]
        public void WorkDepartureWindowClosesAfterScheduledReturn()
        {
            Assert.IsTrue(PopulationPathingDirector.IsWithinWorkDepartureWindow(0.40f, 0.30f, 0.74f));
            Assert.IsFalse(PopulationPathingDirector.IsWithinWorkDepartureWindow(0.75f, 0.30f, 0.74f),
                "A worker who returned home after the shift must not be sent back to work by a stale departure threshold.");
        }

        [Test]
        public void DomesticWindowRejectsOverlapWithScheduledPaidWork()
        {
            Assert.IsTrue(PopulationPathingDirector.OverlapsScheduledWorkWindow(660, 41, 450, 1065));
            Assert.IsFalse(PopulationPathingDirector.OverlapsScheduledWorkWindow(1080, 41, 450, 1065));
        }

        [Test]
        public void RouteAwareDepartureLeavesBeforeScheduledArrivalForTravelPreparationAndBuffer()
        {
            float departure = PopulationPathingDirector.CalculateRouteAwareDepartureTime01(
                465f / 1440f, routeMinutes: 15f, preparationMinutes: 10f, arrivalBufferMinutes: 5f);

            Assert.AreEqual(435f / 1440f, departure, 0.00001f,
                "a 7:45 start with a 15-minute route leaves by 7:15 after preparation and arrival buffer");
        }

        [Test]
        public void WorkCommuteTimingHonorsAnOwnersManagingReservation()
        {
            const int personId = 17;
            const int dayIndex = 23;
            PersonScheduleTracker schedules = new();
            Assert.IsNull(schedules.TryReserve(personId, PersonActivityKind.Managing, dayIndex,
                540, 60, "management duties"));

            Assert.AreEqual(540, PopulationPathingDirector.FindFirstScheduledWorkAttendanceMinute(
                schedules, personId, dayIndex),
                "a Managing-only schedule must control the existing workplace route's departure, not the generic occupation rhythm");
            Assert.IsNull(schedules.TryReserve(personId, PersonActivityKind.Work, dayIndex,
                600, 480, "work shift"));
            Assert.AreEqual(540, PopulationPathingDirector.FindFirstScheduledWorkAttendanceMinute(
                schedules, personId, dayIndex),
                "a later Work reservation must not displace the earlier Managing reservation");
            Assert.IsNull(schedules.TryReserve(personId, PersonActivityKind.Work, dayIndex,
                510, 30, "earlier work shift"));
            Assert.AreEqual(510, PopulationPathingDirector.FindFirstScheduledWorkAttendanceMinute(
                schedules, personId, dayIndex),
                "an earlier legitimate Work reservation still takes precedence over a later Managing block");
            Assert.AreEqual(int.MaxValue, PopulationPathingDirector.FindFirstScheduledWorkAttendanceMinute(
                schedules, personId, dayIndex + 1),
                "a reservation on another day must not affect this day's commute");
            Assert.AreEqual(int.MaxValue, PopulationPathingDirector.FindFirstScheduledWorkAttendanceMinute(
                schedules, personId, dayIndex - 1));
        }

        [Test]
        public void WorkRouteWaitsUntilNonInterruptibleSleepReservationEnds()
        {
            PersonScheduleTracker schedules = new();
            Assert.IsNull(schedules.TryReserve(3, PersonActivityKind.Sleeping, 12,
                0, 420, "sleep (morning)"));

            Assert.IsTrue(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 12, 419));
            Assert.IsFalse(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 12, 420));
            Assert.IsFalse(PopulationPathingDirector.IsReservedForSleepAt(schedules, 3, 13, 100));
        }

        [Test]
        public void LodgingAndSaloonWorkersReturnLaterThanProductionWorkers()
        {
            PersonState sawyer = new() { professionId = "sawmill_sawyer" };
            PersonState lodging = new() { professionId = "boarding_house_keeper" };

            float sawyerReturn = InvokeScheduleTime("GetWorkReturnTime01", sawyer);
            float lodgingReturn = InvokeScheduleTime("GetWorkReturnTime01", lodging);

            Assert.Greater(lodgingReturn, sawyerReturn);
        }

        [TestCase(PopulationScheduleState.AtWork, true)]
        [TestCase(PopulationScheduleState.GoingToWork, false)]
        [TestCase(PopulationScheduleState.GoingShopping, false)]
        [TestCase(PopulationScheduleState.AtHome, false)]
        [TestCase(PopulationScheduleState.ReturningHome, false)]
        public void ScheduledWorkReturnOnlyRetargetsWorkersWhoReachedWork(PopulationScheduleState state, bool expected)
        {
            Assert.AreEqual(expected, PopulationPathingDirector.IsEligibleForScheduledWorkReturn(state));
        }

        [Test]
        public void LocalVisualEligibilityExcludesOffMapDeadAndUnhousedRecords()
        {
            PopulationState population = new();
            population.households.Add(new HouseholdState { id = 4 });
            PersonState resident = new() { id = 19, householdId = 4, deathDayIndex = -1 };
            PersonState departed = new() { id = 20, householdId = 4, deathDayIndex = -1, isOffMap = true };
            PersonState deceased = new() { id = 21, householdId = 4, deathDayIndex = 12 };
            PersonState unhoused = new() { id = 22, householdId = -1, deathDayIndex = -1 };

            Assert.IsTrue(PopulationPathingDirector.CanRepresentOnMap(resident, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(departed, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(deceased, population));
            Assert.IsFalse(PopulationPathingDirector.CanRepresentOnMap(unhoused, population));
        }

        [Test]
        public void VisualIdentityBindingResolvesTheSameAuthoritativePerson()
        {
            PopulationState population = new();
            PersonState resident = new() { id = 73, firstName = "Ada", lastName = "Miller" };
            population.people.Add(resident);
            GameObject actor = new GameObject("Person actor binding test");
            try
            {
                PersonVisualIdentityBinding binding = actor.AddComponent<PersonVisualIdentityBinding>();
                Assert.IsTrue(binding.Bind(resident.id));
                Assert.IsTrue(binding.TryResolve(population, out PersonState resolved));
                Assert.AreSame(resident, resolved);
                Assert.AreEqual(resident.id, binding.PersonId);
                Assert.IsTrue(binding.Bind(resident.id), "Binding the same stable identity is idempotent.");
                Assert.IsFalse(binding.Bind(74), "A visual actor cannot silently become a different Person.");
                Assert.AreEqual(resident.id, binding.PersonId);
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        [TestCase(2, true)]
        [TestCase(3, false)]
        public void VisualPrefabSelectionUsesPersonAppearanceInsteadOfIdentityParity(int personId, bool isFemale)
        {
            GameObject malePrefab = new("Male appearance test prefab");
            GameObject femalePrefab = new("Female appearance test prefab");
            try
            {
                PersonState person = new() { id = personId, isFemale = isFemale };
                MethodInfo method = typeof(PopulationPathingDirector).GetMethod(
                    "ResolveAgentVisualPrefab",
                    BindingFlags.NonPublic | BindingFlags.Static);

                Assert.NotNull(method);
                GameObject selected = (GameObject)method.Invoke(
                    null,
                    new object[] { person, malePrefab, femalePrefab });

                Assert.AreSame(isFemale ? femalePrefab : malePrefab, selected);
            }
            finally
            {
                Object.DestroyImmediate(malePrefab);
                Object.DestroyImmediate(femalePrefab);
            }
        }

        private static float InvokeScheduleTime(string methodName, PersonState person)
        {
            MethodInfo method = typeof(PopulationPathingDirector).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (float)method.Invoke(null, new object[] { person });
        }
    }
}
