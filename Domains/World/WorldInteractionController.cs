using LandLedgers.CameraSystem;
using LandLedgers.MVP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(330)]
    public sealed class WorldInteractionController : MonoBehaviour
    {
        [SerializeField] private StrategyCameraController strategyCamera;
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private GeneralStorePanelController managementPanel;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(1f)] private float raycastDistance = 2000f;
        [SerializeField] private bool logTargetDiagnostics = false;

        [Header("Runtime Diagnostics")]
        [SerializeField] private int lastRaycastHitCount;
        [SerializeField] private int lastInspectableCandidateCount;
        [SerializeField] private int lastSkippedInactiveTargetCount;
        [SerializeField] private int lastSelectedTargetPriority;
        [SerializeField] private float lastSelectedTargetDistance;
        [SerializeField] private string lastSelectedTargetSummary;
        [SerializeField] private string lastRouteSummary;

        private const float TargetDistanceTieTolerance = 0.08f;
        private readonly RaycastHit[] hits = new RaycastHit[32];

        public int LastRaycastHitCount => lastRaycastHitCount;
        public int LastInspectableCandidateCount => lastInspectableCandidateCount;
        public int LastSkippedInactiveTargetCount => lastSkippedInactiveTargetCount;
        public int LastSelectedTargetPriority => lastSelectedTargetPriority;
        public float LastSelectedTargetDistance => lastSelectedTargetDistance;
        public string LastSelectedTargetSummary => lastSelectedTargetSummary;
        public string LastRouteSummary => lastRouteSummary;

        private void Awake()
        {
            AutoWire();
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Camera camera = ResolveCamera();
            if (camera == null)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (TryResolveTarget(ray, out WorldInspectableTarget target))
            {
                RouteTarget(target);
            }
        }

        public void Configure(
            StrategyCameraController newStrategyCamera,
            TownWorldController newTownWorld,
            GeneralStorePanelController newManagementPanel)
        {
            strategyCamera = newStrategyCamera != null ? newStrategyCamera : strategyCamera;
            townWorld = newTownWorld != null ? newTownWorld : townWorld;
            managementPanel = newManagementPanel != null ? newManagementPanel : managementPanel;
        }

        public bool TryResolveTarget(Ray ray, out WorldInspectableTarget target)
        {
            target = null;
            lastRaycastHitCount = Physics.RaycastNonAlloc(
                ray,
                hits,
                raycastDistance,
                raycastMask,
                QueryTriggerInteraction.Collide);
            lastInspectableCandidateCount = 0;
            lastSkippedInactiveTargetCount = 0;
            lastSelectedTargetPriority = int.MinValue;
            lastSelectedTargetDistance = 0f;
            lastSelectedTargetSummary = string.Empty;
            lastRouteSummary = "No target resolved.";

            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MinValue;
            for (int i = 0; i < lastRaycastHitCount; i++)
            {
                Collider collider = hits[i].collider;
                WorldInspectableTarget candidate = collider != null ? collider.GetComponentInParent<WorldInspectableTarget>() : null;
                if (candidate == null)
                {
                    continue;
                }

                if (!candidate.isActiveAndEnabled || !candidate.gameObject.activeInHierarchy)
                {
                    lastSkippedInactiveTargetCount++;
                    continue;
                }

                lastInspectableCandidateCount++;
                float distance = hits[i].distance;
                int priority = GetTargetPriority(candidate);
                bool closer = distance + TargetDistanceTieTolerance < bestDistance;
                bool nearTie = Mathf.Abs(distance - bestDistance) <= TargetDistanceTieTolerance;
                if (target == null || closer || (nearTie && priority > bestPriority))
                {
                    bestDistance = distance;
                    bestPriority = priority;
                    target = candidate;
                }
            }

            if (target != null)
            {
                lastSelectedTargetPriority = bestPriority;
                lastSelectedTargetDistance = bestDistance;
                lastSelectedTargetSummary = BuildTargetSummary(target);
            }

            if (logTargetDiagnostics)
            {
                Debug.Log(
                    $"World interaction target: hits={lastRaycastHitCount}, candidates={lastInspectableCandidateCount}, skipped inactive={lastSkippedInactiveTargetCount}, selected={lastSelectedTargetSummary}, priority={lastSelectedTargetPriority}, distance={lastSelectedTargetDistance:0.00}.",
                    this);
            }

            return target != null;
        }

        private void RouteTarget(WorldInspectableTarget target)
        {
            AutoWire();
            lastRouteSummary = string.Empty;
            if (target == null)
            {
                lastRouteSummary = "No target.";
                return;
            }

            if (managementPanel == null)
            {
                lastRouteSummary = $"No management panel available for {BuildTargetSummary(target)}.";
                return;
            }

            int buildingId = target.BuildingId;
            int plotId = target.PlotId;
            bool inspectionOnlyTarget = IsInspectionOnlyTarget(target);

            if (buildingId >= 0 && townWorld != null && townWorld.TryGetBuildingById(buildingId, out PlacedBuilding building) && building != null)
            {
                int inspectionPlotId = plotId >= 0 ? plotId : building.plotId;
                if (building.playerOwned)
                {
                    lastRouteSummary = $"Owned building management for building {buildingId:000}.";
                    managementPanel.OpenOwnedBuildingManagement(buildingId);
                    return;
                }

                if (inspectionOnlyTarget || building.publicSiteRole != PublicSiteRole.None)
                {
                    lastRouteSummary = $"Inspection-only improved property view for plot {inspectionPlotId:000}, building {buildingId:000}.";
                    managementPanel.OpenPropertyInspection(inspectionPlotId, buildingId);
                    return;
                }

                lastRouteSummary = $"Acquisition lead for improved property building {buildingId:000}.";
                managementPanel.OpenAcquisitionLeadForBuilding(buildingId);
                return;
            }

            if (plotId >= 0 && townWorld != null && townWorld.TryGetPlotById(plotId, out TownPlot plot) && plot != null)
            {
                if (plot.playerOwned)
                {
                    lastRouteSummary = $"Owned plot management for plot {plotId:000}.";
                    managementPanel.OpenOwnedPlotManagement(plotId);
                    return;
                }

                if (plot.buildingId >= 0 && townWorld.TryGetBuildingById(plot.buildingId, out PlacedBuilding linkedBuilding) && linkedBuilding != null)
                {
                    // Ground/parcels under real improvements should follow the improved-property lane rather than
                    // incorrectly dropping the player into the vacant-land acquisition flow.
                    if (linkedBuilding.playerOwned)
                    {
                        lastRouteSummary = $"Plot proxy redirected to owned building management for building {linkedBuilding.id:000}.";
                        managementPanel.OpenOwnedBuildingManagement(linkedBuilding.id);
                        return;
                    }

                    if (inspectionOnlyTarget || linkedBuilding.publicSiteRole != PublicSiteRole.None || plot.publicSiteRole != PublicSiteRole.None)
                    {
                        lastRouteSummary = $"Plot proxy redirected to inspection for linked building {linkedBuilding.id:000}.";
                        managementPanel.OpenPropertyInspection(plotId, linkedBuilding.id);
                        return;
                    }

                    lastRouteSummary = $"Plot proxy redirected to improved-property acquisition lead for building {linkedBuilding.id:000}.";
                    managementPanel.OpenAcquisitionLeadForBuilding(linkedBuilding.id);
                    return;
                }

                if (inspectionOnlyTarget || plot.publicSiteRole != PublicSiteRole.None)
                {
                    lastRouteSummary = $"Inspection-only property view for plot {plotId:000}.";
                    managementPanel.OpenPropertyInspection(plotId, buildingId);
                    return;
                }

                lastRouteSummary = $"Vacant land acquisition lead for plot {plotId:000}.";
                managementPanel.OpenAcquisitionLeadForPlot(plotId);
                return;
            }

            lastRouteSummary = $"Fallback property inspection for {BuildTargetSummary(target)}.";
            managementPanel.OpenPropertyInspection(plotId, buildingId);
        }

        private static int GetTargetPriority(WorldInspectableTarget target)
        {
            if (target == null)
            {
                return int.MinValue;
            }

            int priority = 0;
            if (target.BuildingId >= 0)
            {
                priority += 300;
            }

            if (target.PlotId >= 0)
            {
                priority += 100;
            }

            if (IsInspectionOnlyTarget(target))
            {
                priority += 40;
            }

            return priority;
        }

        private static string BuildTargetSummary(WorldInspectableTarget target)
        {
            if (target == null)
            {
                return "none";
            }

            return $"{target.Kind} target | plot {target.PlotId:000} | building {target.BuildingId:000}";
        }

        private static bool IsInspectionOnlyTarget(WorldInspectableTarget target)
        {
            if (target == null)
            {
                return false;
            }

            return target.Kind == WorldInspectableTargetKind.Civic
                || target.Kind == WorldInspectableTargetKind.Service;
        }

        private Camera ResolveCamera()
        {
            if (strategyCamera != null && strategyCamera.ControlledCamera != null)
            {
                return strategyCamera.ControlledCamera;
            }

            return Camera.main;
        }

        private void AutoWire()
        {
            strategyCamera ??= FindAnyObjectByType<StrategyCameraController>();
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            managementPanel ??= FindAnyObjectByType<GeneralStorePanelController>();
        }
    }
}
