using System;
using System.Text;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    [DisallowMultipleComponent]
    public sealed class TownPathingDemoController : MonoBehaviour
    {
        private const string AgentRootName = "PathingAgentsRoot";

        [Header("References")]
        [SerializeField]
        private PathingManager pathingManager;

        [SerializeField]
        private TownWorldController worldController;

        [SerializeField]
        private Transform agentRoot;

        [SerializeField]
        [Tooltip("Optional AgentMover prefab fallback. Used only when a route-specific visual prefab is not assigned.")]
        private AgentMover agentPrefab;

        [SerializeField]
        [Tooltip("Visual prefab for the house-to-work demo route. AgentMover is added to the spawned instance if needed.")]
        private GameObject houseToWorkAgentVisualPrefab;

        [SerializeField]
        [Tooltip("Visual prefab for the house-to-store demo route. AgentMover is added to the spawned instance if needed.")]
        private GameObject houseToStoreAgentVisualPrefab;

        [Header("Demo Routes")]
        [SerializeField]
        private bool spawnDemoOnStart = false;

        [SerializeField]
        private bool spawnHouseToWorkRoute = true;

        [SerializeField]
        private bool spawnHouseToStoreRoute = true;

        [SerializeField]
        private bool hideAgentsAtDestination = true;

        [Header("Logistics Smoke Tests")]
        [SerializeField]
        [Tooltip("When enabled, context-menu logistics diagnostics are also written to the Unity console.")]
        private bool logLogisticsRouteDiagnostics = true;

        [SerializeField]
        [Min(0)]
        [Tooltip("Additional abstract off-map cells added to edge-to-town and town-to-edge logistics smoke routes.")]
        private int logisticsSmokeExtraOffMapCells = 24;

        [SerializeField]
        [TextArea(6, 18)]
        private string lastLogisticsRouteDiagnostics = string.Empty;

        [Header("Greybox Agent Fallback")]
        [SerializeField]
        private Color houseToWorkColor = new(0.2f, 0.7f, 1f, 1f);

        [SerializeField]
        private Color houseToStoreColor = new(1f, 0.75f, 0.22f, 1f);

        private void Start()
        {
            if (spawnDemoOnStart)
            {
                SpawnDemoRoutes();
            }
        }

        [ContextMenu("Spawn Demo Routes")]
        public void SpawnDemoRoutes()
        {
            if (spawnHouseToWorkRoute)
            {
                SpawnHouseToWorkRoute();
            }

            if (spawnHouseToStoreRoute)
            {
                SpawnHouseToStoreRoute();
            }
        }

        [ContextMenu("Spawn House To Work Route")]
        public void SpawnHouseToWorkRoute()
        {
            if (!EnsureReady())
            {
                return;
            }

            if (!pathingManager.TryFindDoorAnchorByPlotZone(PlotZone.Residential, out PlacedBuilding homeBuilding, out BuildingAnchor homeDoor))
            {
                Debug.LogWarning("Cannot spawn house-to-work route because no residential door anchor was found.", this);
                return;
            }

            if (!TryFindReachableWorkDoorFrom(homeDoor.coord, out PlacedBuilding workBuilding, out BuildingAnchor workDoor, out _))
            {
                Debug.LogWarning($"Cannot spawn house-to-work route because no reachable non-store business door anchor was found from {homeDoor.coord}.", this);
                return;
            }

            AgentMover agent = CreateAgent("Pedestrian House To Work", houseToWorkAgentVisualPrefab, houseToWorkColor);
            agent.BeginDoorToDoor(homeDoor, workDoor, hideAgentsAtDestination, homeBuilding?.doorAnimator, workBuilding?.doorAnimator);
        }

        [ContextMenu("Spawn House To Store Route")]
        public void SpawnHouseToStoreRoute()
        {
            if (!EnsureReady())
            {
                return;
            }

            if (!pathingManager.TryFindDoorAnchorByPlotZone(PlotZone.Residential, out PlacedBuilding homeBuilding, out BuildingAnchor homeDoor))
            {
                Debug.LogWarning("Cannot spawn house-to-store route because no residential door anchor was found.", this);
                return;
            }

            if (!TryFindStoreOrWorkDoorReachableFrom(homeDoor.coord, out PlacedBuilding storeBuilding, out BuildingAnchor storeDoor, out _))
            {
                Debug.LogWarning($"Cannot spawn house-to-store route because no reachable general store or fallback business door anchor was found from {homeDoor.coord}.", this);
                return;
            }

            AgentMover agent = CreateAgent("Pedestrian House To Store", houseToStoreAgentVisualPrefab, houseToStoreColor);
            agent.BeginDoorToDoor(homeDoor, storeDoor, hideAgentsAtDestination, homeBuilding?.doorAnimator, storeBuilding?.doorAnimator);
        }

        [ContextMenu("Log Logistics Route Diagnostics")]
        public void LogLogisticsRouteDiagnostics()
        {
            if (!EnsureReady())
            {
                return;
            }

            StringBuilder builder = new();
            builder.AppendLine("Land & Ledgers pathing/logistics smoke test");

            if (!pathingManager.TryFindDoorAnchorByPlotZone(PlotZone.Residential, out PlacedBuilding homeBuilding, out BuildingAnchor homeDoor))
            {
                lastLogisticsRouteDiagnostics = "No residential door anchor was found for logistics smoke testing.";
                Debug.LogWarning(lastLogisticsRouteDiagnostics, this);
                return;
            }

            if (!TryFindStoreOrWorkDoorReachableFrom(homeDoor.coord, out PlacedBuilding destinationBuilding, out BuildingAnchor destinationDoor, out PathingResult pedestrianPath))
            {
                lastLogisticsRouteDiagnostics = $"No reachable store/work destination was found from residential door {homeDoor.coord}.";
                Debug.LogWarning(lastLogisticsRouteDiagnostics, this);
                return;
            }

            builder.AppendLine($"Origin: {DescribeBuilding(homeBuilding)} at {homeDoor.coord}");
            builder.AppendLine($"Destination: {DescribeBuilding(destinationBuilding)} at {destinationDoor.coord}");
            builder.AppendLine($"Pedestrian reachability: {(pedestrianPath != null && pedestrianPath.Success ? $"ok, cost {pedestrianPath.TotalCost}, cells {pedestrianPath.Path.Count}" : "not confirmed")}");

            LogisticsRoutePlanner planner = new(worldController, pathingManager);
            AppendPlannedRoute(
                builder,
                planner,
                new LogisticsRouteRequest(homeDoor.coord, destinationDoor.coord, LogisticsPathEndpointKind.GridCoord, LogisticsPathEndpointKind.GridCoord, 0, "local household/store freight"));

            AppendPlannedRoute(
                builder,
                planner,
                new LogisticsRouteRequest(destinationDoor.coord, destinationDoor.coord, LogisticsPathEndpointKind.MapEdgeRoad, LogisticsPathEndpointKind.GridCoord, logisticsSmokeExtraOffMapCells, "inbound edge freight"));

            AppendPlannedRoute(
                builder,
                planner,
                new LogisticsRouteRequest(destinationDoor.coord, destinationDoor.coord, LogisticsPathEndpointKind.GridCoord, LogisticsPathEndpointKind.MapEdgeRoad, logisticsSmokeExtraOffMapCells, "outbound edge freight"));

            lastLogisticsRouteDiagnostics = builder.ToString();
            if (logLogisticsRouteDiagnostics)
            {
                Debug.Log(lastLogisticsRouteDiagnostics, this);
            }
        }

        [ContextMenu("Clear Demo Agents")]
        public void ClearDemoAgents()
        {
            Transform root = EnsureAgentRoot();
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                DestroyUnityObject(root.GetChild(i).gameObject);
            }
        }

        private bool TryFindReachableWorkDoorFrom(GridCoord origin, out PlacedBuilding workBuilding, out BuildingAnchor workDoor, out PathingResult pathResult)
        {
            if (pathingManager.TryFindDoorAnchorReachableFrom(
                    origin,
                    building => building.definition != null
                        && building.definition.CanUsePlot(PlotZone.Business)
                        && !string.Equals(building.definition.BuildingId, "general_store", StringComparison.OrdinalIgnoreCase),
                    out workBuilding,
                    out workDoor,
                    out pathResult))
            {
                return true;
            }

            return pathingManager.TryFindDoorAnchorByPlotZoneReachableFrom(origin, PlotZone.Business, out workBuilding, out workDoor, out pathResult);
        }

        private bool TryFindStoreOrWorkDoorReachableFrom(GridCoord origin, out PlacedBuilding building, out BuildingAnchor door, out PathingResult pathResult)
        {
            if (pathingManager.TryFindDoorAnchorByDefinitionIdReachableFrom(origin, "general_store", out building, out door, out pathResult))
            {
                return true;
            }

            return TryFindReachableWorkDoorFrom(origin, out building, out door, out pathResult);
        }

        private static void AppendPlannedRoute(StringBuilder builder, LogisticsRoutePlanner planner, LogisticsRouteRequest request)
        {
            builder.AppendLine();
            LogisticsRoutePlan plan = null;
            if (planner == null || !planner.TryPlanRoute(request, out plan) || plan == null)
            {
                builder.AppendLine($"{request.Label}: FAILED");
                if (plan != null)
                {
                    builder.AppendLine($"  failure: {plan.FailureCategory} / {plan.PathingFailureCategory} — {plan.FailureReason}");
                    builder.AppendLine($"  edge candidates: start {plan.StartEdgeRoadCandidateCount}, target {plan.TargetEdgeRoadCandidateCount}, tested {plan.EdgeRouteCandidatePairsTested}");
                }

                return;
            }

            builder.AppendLine($"{plan.Label}: {plan.Quality}");
            builder.AppendLine($"  visible {plan.VisibleStart} -> {plan.VisibleEnd}, cells {plan.VisibleTravelCells} visible + {plan.OffMapTravelCells} off-map = {plan.TotalTravelCells}");
            builder.AppendLine($"  travel {plan.VisibleTravelGameSeconds:0.0}s visible + {plan.OffMapTravelGameSeconds:0.0}s off-map = {plan.TotalTravelGameSeconds:0.0}s");
            builder.AppendLine($"  road share {plan.RoadShare * 100f:0}%, penalty {plan.AverageSurfacePenalty:0.00}; {plan.QualityReason}");
            builder.AppendLine($"  edge candidates start {plan.StartEdgeRoadCandidateCount}, target {plan.TargetEdgeRoadCandidateCount}, tested {plan.EdgeRouteCandidatePairsTested}; {plan.EdgeRouteSelectionReason}");
        }

        private static string DescribeBuilding(PlacedBuilding building)
        {
            if (building == null)
            {
                return "<none>";
            }

            string id = building.definition != null ? building.definition.BuildingId : string.Empty;
            return string.IsNullOrWhiteSpace(id) ? $"building {building.id}" : $"{id} #{building.id}";
        }

        private AgentMover CreateAgent(string agentName, GameObject visualPrefab, Color color)
        {
            if (visualPrefab != null)
            {
                GameObject agentObject = TryInstantiateGameObject(visualPrefab, EnsureAgentRoot(), agentName);
                if (agentObject != null)
                {
                    agentObject.name = agentName;
                    agentObject.transform.localPosition = Vector3.zero;
                    agentObject.transform.localRotation = Quaternion.identity;

                    AgentMover visualAgent = agentObject.GetComponent<AgentMover>();
                    if (visualAgent == null)
                    {
                        visualAgent = agentObject.AddComponent<AgentMover>();
                    }

                    visualAgent.Configure(pathingManager);
                    return visualAgent;
                }
            }

            AgentMover agent;
            if (agentPrefab != null)
            {
                agent = Instantiate(agentPrefab, EnsureAgentRoot());
                agent.name = agentName;
            }
            else
            {
                GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                agentObject.name = agentName;
                agentObject.transform.SetParent(EnsureAgentRoot(), false);
                agentObject.transform.localScale = new Vector3(0.65f, 0.9f, 0.65f);
                agent = agentObject.AddComponent<AgentMover>();

                Renderer renderer = agentObject.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = CreateAgentMaterial(color);
                }
            }

            agent.Configure(pathingManager);
            return agent;
        }

        private static GameObject TryInstantiateGameObject(GameObject prefab, Transform parent, string label)
        {
            if (prefab == null)
            {
                return null;
            }

            try
            {
                UnityEngine.Object instance = parent != null
                    ? Instantiate((UnityEngine.Object)prefab, parent)
                    : Instantiate((UnityEngine.Object)prefab);

                if (instance is GameObject gameObject)
                {
                    return gameObject;
                }

                if (instance is Component component)
                {
                    return component.gameObject;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"Failed to instantiate {label} prefab '{prefab.name}': {exception.Message}", prefab);
            }

            return null;
        }

        private bool EnsureReady()
        {
            if (pathingManager == null)
            {
                pathingManager = FindAnyObjectByType<PathingManager>();
            }

            if (worldController == null)
            {
                worldController = FindAnyObjectByType<TownWorldController>();
            }

            if (pathingManager == null)
            {
                Debug.LogWarning("No PathingManager found in the scene.", this);
                return false;
            }

            if (worldController == null)
            {
                Debug.LogWarning("No TownWorldController found in the scene.", this);
                return false;
            }

            if (pathingManager.WorldController == null)
            {
                pathingManager.Configure(worldController, pathingManager.Settings);
            }

            if (worldController.Grid == null)
            {
                worldController.GenerateTownShell();
            }

            return worldController.Grid != null;
        }

        private Transform EnsureAgentRoot()
        {
            if (agentRoot != null)
            {
                return agentRoot;
            }

            Transform existing = transform.Find(AgentRootName);
            if (existing != null)
            {
                agentRoot = existing;
                return agentRoot;
            }

            GameObject root = new(AgentRootName);
            root.transform.SetParent(transform, false);
            agentRoot = root.transform;
            return agentRoot;
        }

        private static Material CreateAgentMaterial(Color color)
        {
            Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader);
            material.name = "Generated Pathing Agent Material";
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
