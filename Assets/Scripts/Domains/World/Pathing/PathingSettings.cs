using UnityEngine;

namespace LandLedgers.Pathing
{
    [CreateAssetMenu(
        fileName = "PathingSettings",
        menuName = "Land & Ledgers/Pathing/Pathing Settings",
        order = 130)]
    public sealed class PathingSettings : ScriptableObject
    {
        [Header("Grid Costs")]
        [Tooltip("Preferred movement cost for road cells.")]
        [Min(1)]
        public int roadCost = 10;

        [Tooltip("Movement cost for door, service, and drop-off anchor cells.")]
        [Min(1)]
        public int anchorCost = 12;

        [Tooltip("Movement cost for open plot cells around buildings.")]
        [Min(1)]
        public int yardCost = 12;

        [Tooltip("Movement cost for normal buildable walkable cells.")]
        [Min(1)]
        public int generalWalkableCost = 14;

        [Tooltip("Movement cost for rough walkable cells if rough terrain is enabled.")]
        [Min(1)]
        public int roughWalkableCost = 18;

        [Header("Search")]
        [Tooltip("Allow steep non-blocking terrain to be treated as high-cost rough walkable ground.")]
        public bool allowRoughTerrain = false;

        [Tooltip("Use 8-way movement. Keep off for first-pass town readability.")]
        public bool allowDiagonalMovement = false;

        [Tooltip("When diagonal movement is enabled, require both adjacent cardinal cells to be traversable so agents cannot squeeze through blocked building corners.")]
        public bool preventDiagonalCornerCutting = true;

        [Tooltip("Hard cap to prevent bad requests from searching the whole map forever.")]
        [Min(64)]
        public int maxVisitedCells = 12000;

        [Tooltip("Maximum allowed height change between adjacent traversable grid cells on uneven terrain.")]
        [Min(0.05f)]
        public float maximumTraversableStepHeightMeters = 1.25f;

        [Header("Pedestrian Movement")]
        [Tooltip("Default world-space walking speed for test pedestrian agents.")]
        [Min(0.1f)]
        public float pedestrianSpeedMetersPerSecond = 3.2f;

        [Tooltip("How quickly agents turn toward their next waypoint.")]
        [Min(1f)]
        public float turnSpeedDegreesPerSecond = 720f;

        [Tooltip("How close an agent must be to a cell center before advancing to the next waypoint.")]
        [Range(0.01f, 1f)]
        public float waypointArrivalDistance = 0.18f;

        [Tooltip("Small vertical lift above sampled cell height for pedestrian visuals.")]
        [Range(0f, 1f)]
        public float agentGroundOffsetMeters = 0.08f;

        [Tooltip("Pause duration at the target door before the pedestrian is hidden as inside the building.")]
        [Min(0f)]
        public float doorArrivalPauseSeconds = 1.1f;

        [Header("Logistics Route Planning")]
        [Tooltip("Default visible-region speed for loaded wagons/carts used by logistics route estimates.")]
        [Min(0.1f)]
        public float logisticsLoadedWagonSpeedMetersPerSecond = 2.2f;

        [Tooltip("Abstract off-map road speed used for route segments outside the active town grid.")]
        [Min(0.1f)]
        public float logisticsOffMapRoadSpeedMetersPerSecond = 3.6f;

        [Tooltip("Maximum number of edge-road candidates tested per map-edge endpoint before logistics route planning gives up.")]
        [Min(1)]
        public int logisticsEdgeRoadCandidateLimit = 8;

        [Tooltip("Average surface penalty at or below this value is reported as road-linked.")]
        [Min(1f)]
        public float logisticsRoadLinkedPenaltyThreshold = 1.08f;

        [Tooltip("Average surface penalty at or below this value is reported as mixed-surface. Higher values become rough-link routes.")]
        [Min(1f)]
        public float logisticsMixedSurfacePenaltyThreshold = 1.35f;

        [Header("Logistics Route Profile")]
        [Tooltip("Road cost multiplier for wagon/logistics pathfinding. Keep low to bias freight toward roads.")]
        [Min(0.1f)]
        public float logisticsWagonRoadCostMultiplier = 0.85f;

        [Tooltip("Anchor cost multiplier for wagon/logistics pathfinding.")]
        [Min(0.1f)]
        public float logisticsWagonAnchorCostMultiplier = 1.05f;

        [Tooltip("Yard/plot cost multiplier for wagon/logistics pathfinding.")]
        [Min(0.1f)]
        public float logisticsWagonYardCostMultiplier = 1.45f;

        [Tooltip("General walkable cost multiplier for wagon/logistics pathfinding.")]
        [Min(0.1f)]
        public float logisticsWagonGeneralWalkableCostMultiplier = 1.7f;

        [Tooltip("Rough walkable cost multiplier for wagon/logistics pathfinding.")]
        [Min(0.1f)]
        public float logisticsWagonRoughWalkableCostMultiplier = 2.25f;

        [Header("Debug")]
        public bool drawManagerLastPath = true;
        public bool drawSelectedAgentPath = true;
        public bool logFailedRequests = true;
        [Range(0f, 3f)]
        public float debugLineHeight = 0.35f;
        public Color lastPathColor = new(0.2f, 0.85f, 1f, 1f);
        public Color selectedAgentPathColor = new(1f, 0.72f, 0.18f, 1f);
        public Color failedPathColor = new(1f, 0.15f, 0.08f, 1f);

        public void Sanitize()
        {
            roadCost = Mathf.Max(1, roadCost);
            anchorCost = Mathf.Max(1, anchorCost);
            yardCost = Mathf.Max(1, yardCost);
            generalWalkableCost = Mathf.Max(1, generalWalkableCost);
            roughWalkableCost = Mathf.Max(1, roughWalkableCost);
            maxVisitedCells = Mathf.Max(64, maxVisitedCells);
            maximumTraversableStepHeightMeters = Mathf.Max(0.05f, maximumTraversableStepHeightMeters);
            pedestrianSpeedMetersPerSecond = Mathf.Max(0.1f, pedestrianSpeedMetersPerSecond);
            turnSpeedDegreesPerSecond = Mathf.Max(1f, turnSpeedDegreesPerSecond);
            waypointArrivalDistance = Mathf.Clamp(waypointArrivalDistance, 0.01f, 1f);
            agentGroundOffsetMeters = Mathf.Clamp(agentGroundOffsetMeters, 0f, 1f);
            doorArrivalPauseSeconds = Mathf.Max(0f, doorArrivalPauseSeconds);
            logisticsLoadedWagonSpeedMetersPerSecond = Mathf.Max(0.1f, logisticsLoadedWagonSpeedMetersPerSecond);
            logisticsOffMapRoadSpeedMetersPerSecond = Mathf.Max(0.1f, logisticsOffMapRoadSpeedMetersPerSecond);
            logisticsEdgeRoadCandidateLimit = Mathf.Max(1, logisticsEdgeRoadCandidateLimit);
            logisticsRoadLinkedPenaltyThreshold = Mathf.Max(1f, logisticsRoadLinkedPenaltyThreshold);
            logisticsMixedSurfacePenaltyThreshold = Mathf.Max(logisticsRoadLinkedPenaltyThreshold, logisticsMixedSurfacePenaltyThreshold);
            logisticsWagonRoadCostMultiplier = Mathf.Max(0.1f, logisticsWagonRoadCostMultiplier);
            logisticsWagonAnchorCostMultiplier = Mathf.Max(0.1f, logisticsWagonAnchorCostMultiplier);
            logisticsWagonYardCostMultiplier = Mathf.Max(0.1f, logisticsWagonYardCostMultiplier);
            logisticsWagonGeneralWalkableCostMultiplier = Mathf.Max(0.1f, logisticsWagonGeneralWalkableCostMultiplier);
            logisticsWagonRoughWalkableCostMultiplier = Mathf.Max(0.1f, logisticsWagonRoughWalkableCostMultiplier);
            debugLineHeight = Mathf.Clamp(debugLineHeight, 0f, 3f);
        }

        private void OnValidate()
        {
            Sanitize();
        }
    }
}
