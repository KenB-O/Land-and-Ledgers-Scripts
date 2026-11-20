using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LandLedgers.PlayMode
{
    public sealed class LLModularBuildingPhysicalTraversalPlayModeTests
    {
#if UNITY_EDITOR
        [UnityTest, Timeout(120000)]
        public IEnumerator SmallTownResidentEntersGeneratedBuildingRoutesAroundShelfAndExits()
        {
            GameObject building = null;
            GameObject shelf = null;
            GameObject resident = null;
            Component mover = null;
            var errors = new List<string>();
            Application.LogCallback observeLog = (condition, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add(condition);
            };
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += observeLog;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Main Scene", LoadSceneMode.Single);
                Assert.NotNull(load);
                while (!load.isDone) yield return null;
                yield return null;

                Type pathingType = RuntimeType("LandLedgers.Pathing.PathingManager");
                Type authorityType = RuntimeType("LandLedgers.World.BuildingFootprintAuthority");
                Type activityType = RuntimeType("LandLedgers.World.BuildingActivityLocationAuthoring");
                Type placementType = RuntimeType("LandLedgers.World.BuildingEquipmentPlacement");
                Type moverType = RuntimeType("LandLedgers.Pathing.AgentMover");
                Component pathing = null;
                float startupDeadline = UnityEngine.Time.realtimeSinceStartup + 60f;
                while (UnityEngine.Time.realtimeSinceStartup < startupDeadline)
                {
                    pathing = FindComponent(pathingType);
                    if (pathing != null && pathingType.GetProperty("Grid")?.GetValue(pathing) != null) break;
                    yield return null;
                }
                Assert.NotNull(pathing, "Small-town Main Scene must initialize its live PathingManager.");
                Assert.NotNull(pathingType.GetProperty("Grid")?.GetValue(pathing), "Small-town Main Scene must have a live navigation grid.");

                GameObject buildingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Environment/Building Materials/Examples/Pass 2 Rectangle 12x8.prefab");
                GameObject shelfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Props/Shelf.prefab");
                GameObject manPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Man.prefab");
                Assert.NotNull(buildingPrefab);
                Assert.NotNull(shelfPrefab);
                Assert.NotNull(manPrefab);

                building = Object.Instantiate(buildingPrefab, new Vector3(10000f, 0f, 10000f), Quaternion.identity);
                building.name = "PlayMode modular traversal test building";
                Component authority = building.GetComponent(authorityType);
                Assert.NotNull(authority);
                IList entrances = (IList)authorityType.GetProperty("EntrancePairs").GetValue(authority);
                object entrance = entrances[0];
                Type entranceType = entrance.GetType();
                string entranceId = (string)entranceType.GetField("id").GetValue(entrance);
                Vector3 outdoorLocal = (Vector3)entranceType.GetField("outdoorApproachLocal").GetValue(entrance);

                shelf = Object.Instantiate(shelfPrefab, building.transform, false);
                shelf.name = "Installed playtest shelf";
                shelf.transform.localPosition = new Vector3(0f, 0.4f, 0f);
                shelf.transform.localRotation = Quaternion.identity;
                Component placement = shelf.GetComponent(placementType) ?? shelf.AddComponent(placementType);
                placementType.GetMethod("Configure").Invoke(placement,
                    new object[] { "playmode-shelf-01", "furniture.shelf", "Shelf", 0 });

                object activity = System.Activator.CreateInstance(activityType);
                activityType.GetField("id").SetValue(activity, "playmode.destination");
                activityType.GetField("displayName").SetValue(activity, "PlayMode Interior Destination");
                activityType.GetField("type").SetValue(activity, System.Enum.Parse(RuntimeType("LandLedgers.World.BuildingActivityLocationType"), "Workshop"));
                activityType.GetField("floorIndex").SetValue(activity, 0);
                Vector3 activityLocal = new(4f, 0.4f, 0f);
                activityType.GetField("positionLocal").SetValue(activity, activityLocal);
                activityType.GetField("requiresWalkableInterior").SetValue(activity, true);
                var activities = (IList)authorityType
                    .GetField("activityLocations", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(authority);
                activities.Add(activity);
                authorityType.GetMethod("RefreshDerivedInteriorGeometry").Invoke(authority, null);

                object[] routeArgs = { entranceId, "playmode.destination", null, null };
                Assert.IsTrue((bool)authorityType.GetMethod("TryBuildInteriorActivityRoute").Invoke(authority, routeArgs),
                    routeArgs[3]?.ToString());
                List<Vector3> planned = (List<Vector3>)routeArgs[2];
                Assert.That(planned.Count, Is.GreaterThan(3), "The route should bend around the installed shelf.");
                foreach (Vector3 point in planned)
                {
                    object[] pointArgs = { point, 0, null, null };
                    Assert.IsTrue((bool)authorityType.GetMethod("TryEvaluateInteriorWorldPoint").Invoke(authority, pointArgs),
                        pointArgs[3]?.ToString());
                }

                resident = Object.Instantiate(manPrefab, authority.transform.TransformPoint(outdoorLocal), Quaternion.identity);
                resident.name = "PlayMode resident using authored Man prefab";
                mover = resident.GetComponent(moverType) ?? resident.AddComponent(moverType);
                moverType.GetMethod("Configure").Invoke(mover, new object[] { pathing });
                moverType.GetMethod("ShowAtCurrentLocation").Invoke(mover, null);
                yield return null;

                Assert.IsTrue((bool)moverType.GetMethod("BeginBuildingActivityRoute").Invoke(mover,
                    new object[] { authority, entranceId, "playmode.destination" }),
                    "The resident should start through the authored entrance using the live pathing authority.");
                Type timeType = RuntimeType("LandLedgers.Time.TimeManager");
                object time = timeType.GetProperty("Instance")?.GetValue(null);
                double simSeconds = time != null ? (double)timeType.GetProperty("PreciseAbsoluteGameSeconds").GetValue(time) : 0d;
                Bounds shelfBounds = shelf.GetComponentInChildren<Renderer>(true).bounds;
                PropertyInfo activeRoute = moverType.GetProperty("IsRouteActive");
                PropertyInfo arrived = moverType.GetProperty("IsArrivedVisible");
                MethodInfo advance = moverType.GetMethod("AdvanceAuthoritativeRouteTo");
                for (int i = 0; i < 400 && (bool)activeRoute.GetValue(mover); i++)
                {
                    simSeconds += 0.25d;
                    advance.Invoke(mover, new object[] { simSeconds });
                    Vector3 bodyPoint = resident.transform.position + Vector3.up;
                    Assert.IsFalse(shelfBounds.Contains(bodyPoint), "The resident must physically route around the shelf footprint.");
                    yield return null;
                }
                Assert.IsTrue((bool)arrived.GetValue(mover), "The resident should reach the authorized interior activity point.");
                Assert.That(Vector3.Distance(resident.transform.position, authority.transform.TransformPoint(activityLocal)), Is.LessThan(0.1f));

                object[] exitArgs = { authority, entranceId, null };
                Assert.IsTrue((bool)moverType.GetMethod("BeginBuildingInteriorExitRoute").Invoke(mover, exitArgs), exitArgs[2]?.ToString());
                for (int i = 0; i < 400 && (bool)activeRoute.GetValue(mover); i++)
                {
                    simSeconds += 0.25d;
                    advance.Invoke(mover, new object[] { simSeconds });
                    yield return null;
                }
                Vector3 expectedExterior = authority.transform.TransformPoint(outdoorLocal);
                Assert.IsTrue((bool)arrived.GetValue(mover), "The resident should complete the authorized exterior doorway route.");
                Assert.That(Vector3.Distance(resident.transform.position, expectedExterior), Is.LessThan(0.1f));
                TestContext.WriteLine($"Verified Man prefab route: entrance={entranceId}; planned interior waypoints={planned.Count}; shelf placement={placementType.GetProperty("PlacementId").GetValue(placement)}; entered, routed around shelf and exited.");
                string[] expectedBaselineErrors = errors.FindAll(message =>
                    (message.Contains("Failed to load '")
                        && message.Contains("Single Story Business Building White Plaster Animation 1.anim"))
                    || (message.Contains("[OpeningTransportPresentation] Refused to place opening transport")
                        && message.Contains("no feasible horse-and-wagon hitch side"))).ToArray();
                string[] unexpectedErrors = errors.FindAll(message => !expectedBaselineErrors.Contains(message)).ToArray();
                TestContext.WriteLine($"Captured startup/runtime errors={errors.Count}; known missing-asset/opening-wagon errors={expectedBaselineErrors.Length}; other errors={unexpectedErrors.Length}.");
                Assert.IsEmpty(unexpectedErrors, string.Join("\n", unexpectedErrors));
            }
            finally
            {
                Application.logMessageReceived -= observeLog;
                LogAssert.ignoreFailingMessages = previousIgnore;
                if (resident != null) Object.Destroy(resident);
                if (building != null) Object.Destroy(building);
            }
        }

        private static Type RuntimeType(string fullName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            Assert.Fail($"Runtime type '{fullName}' was not loaded.");
            return null;
        }

        private static Component FindComponent(Type type)
        {
            if (type == null) return null;
            foreach (GameObject gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (gameObject == null || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded) continue;
                Component component = gameObject.GetComponent(type);
                if (component != null && component.gameObject.activeInHierarchy) return component;
            }
            return null;
        }
#endif
    }
}
