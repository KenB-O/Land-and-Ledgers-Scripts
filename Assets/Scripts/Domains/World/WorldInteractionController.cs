using System.Collections.Generic;
using LandLedgers.CameraSystem;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Reporting;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Text;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(330)]
    public sealed class WorldInteractionController : MonoBehaviour
    {
        [SerializeField] private StrategyCameraController strategyCamera;
        [SerializeField] private TownWorldController townWorld;
        [SerializeField] private GeneralStorePanelController managementPanel;
        [SerializeField] private PopulationManager populationManager;
        [SerializeField] private SharedBusinessRuntimeManager sharedBusinessRuntime;
        [SerializeField] private LayerMask raycastMask = ~0;
        [SerializeField, Min(1f)] private float raycastDistance = 2000f;
        [SerializeField] private bool logTargetDiagnostics = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private bool showDevelopmentPersonDetails = true;
#else
        [SerializeField] private bool showDevelopmentPersonDetails;
#endif

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
        [SerializeField] private RectTransform personInspectionRoot;
        [SerializeField] private TMP_Text personInspectionTitle;
        [SerializeField] private TMP_Text personInspectionBody;
        [SerializeField] private Button personInspectionCloseButton;

        private void Awake()
        {
            AutoWire();
            if (personInspectionCloseButton != null)
            {
                personInspectionCloseButton.onClick.AddListener(ClosePersonInspection);
            }
        }

        private void OnDestroy()
        {
            if (personInspectionCloseButton != null)
            {
                personInspectionCloseButton.onClick.RemoveListener(ClosePersonInspection);
            }
        }

        public int LastRaycastHitCount => lastRaycastHitCount;
        public int LastInspectableCandidateCount => lastInspectableCandidateCount;
        public int LastSkippedInactiveTargetCount => lastSkippedInactiveTargetCount;
        public int LastSelectedTargetPriority => lastSelectedTargetPriority;
        public float LastSelectedTargetDistance => lastSelectedTargetDistance;
        public string LastSelectedTargetSummary => lastSelectedTargetSummary;
        public string LastRouteSummary => lastRouteSummary;

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

            // NonAlloc results are unordered and may omit a person when the buffer fills.
            RaycastHit[] resolvedHits = lastRaycastHitCount == hits.Length
                ? Physics.RaycastAll(ray, raycastDistance, raycastMask, QueryTriggerInteraction.Collide)
                : hits;
            if (resolvedHits != hits) lastRaycastHitCount = resolvedHits.Length;
            float nearestSolidDistance = float.PositiveInfinity;
            for (int i = 0; i < lastRaycastHitCount; i++)
            {
                Collider blocker = resolvedHits[i].collider;
                if (blocker == null || blocker.isTrigger) continue;
                WorldInspectableTarget owner = blocker.GetComponentInParent<WorldInspectableTarget>();
                if (owner != null && owner.Kind == WorldInspectableTargetKind.Person) continue;
                nearestSolidDistance = Mathf.Min(nearestSolidDistance, resolvedHits[i].distance);
            }

            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MinValue;
            for (int i = 0; i < lastRaycastHitCount; i++)
            {
                Collider collider = resolvedHits[i].collider;
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
                float distance = resolvedHits[i].distance;
                bool person = candidate.Kind == WorldInspectableTargetKind.Person;
                if (person && distance > nearestSolidDistance + TargetDistanceTieTolerance) continue;
                bool currentPerson = target != null && target.Kind == WorldInspectableTargetKind.Person;
                int priority = GetTargetPriority(candidate);
                bool closer = distance + TargetDistanceTieTolerance < bestDistance;
                bool nearTie = Mathf.Abs(distance - bestDistance) <= TargetDistanceTieTolerance;
                // Invisible footprint triggers must not steal clicks from visible people.
                if (target == null || (person && !currentPerson)
                    || (person == currentPerson && (closer || (nearTie && priority > bestPriority))))
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

            if (target.Kind == WorldInspectableTargetKind.Person && target.PersonId >= 0)
            {
                ShowPersonInspection(target.PersonId);
                lastRouteSummary = $"Person inspection for P{target.PersonId:000}.";
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

            if (target.Kind == WorldInspectableTargetKind.Person)
            {
                priority += 500;
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

            string person = target.PersonId >= 0 ? $" | person {target.PersonId:000}" : string.Empty;
            return $"{target.Kind} target | plot {target.PlotId:000} | building {target.BuildingId:000}{person}";
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
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
        }

        private void ShowPersonInspection(int personId)
        {
            AutoWire();
            PersonState person = populationManager != null && populationManager.State != null
                ? populationManager.State.GetPerson(personId)
                : null;
            if (person == null)
            {
                lastRouteSummary = $"Person P{personId:000} is not present in the authoritative population registry.";
                return;
            }

            managementPanel?.CloseManagementPanel();

            if (personInspectionRoot == null || personInspectionTitle == null || personInspectionBody == null)
            {
                lastRouteSummary = "Person inspection panel is not authored or its serialized references are incomplete.";
                return;
            }

            personInspectionTitle.text = string.IsNullOrWhiteSpace(person.DisplayName) ? "Resident" : person.DisplayName.Trim();
            personInspectionBody.text = BuildPersonInspectionText(person);
            personInspectionRoot.gameObject.SetActive(true);
            personInspectionRoot.SetAsLastSibling();
        }

        private string BuildPersonInspectionText(PersonState person)
        {
            PopulationState state = populationManager.State;
            HouseholdState household = person.householdId >= 0 ? state.GetHousehold(person.householdId) : null;
            var text = new StringBuilder(512);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (showDevelopmentPersonDetails)
                AppendPersonField(text, "Person ID", $"P{person.id:000}", true);
#endif
            AppendPersonField(text, "Age", person.age >= 0 ? $"{person.age} years" : null, true);
            AppendPersonField(text, "Household", household != null ? household.householdName : null, showDevelopmentPersonDetails || household != null);
            AppendPersonField(text, "Occupation", !string.IsNullOrWhiteSpace(person.professionName) ? person.professionName : person.professionId, showDevelopmentPersonDetails || !string.IsNullOrWhiteSpace(person.professionName));
            AppendPersonField(text, "Schedule", person.scheduleState.ToString(), showDevelopmentPersonDetails);
            AppendPersonField(text, "Home", DescribeBuilding(person.homeBuildingId), person.homeBuildingId >= 0);
            AppendPersonField(text, "Workplace", DescribeWorkplace(person.workplaceBuildingId), person.workplaceBuildingId >= 0);
            AppendPersonField(text, "Businesses owned", DescribeOwnedBusinesses(person.id), true);
            string skillSummary = person.visibleWorkerProfile != null && person.visibleWorkerProfile.IsInitialized
                ? $"General skill estimate {person.visibleWorkerProfile.generalSkill}/100"
                : "No skill estimate recorded";
            AppendPersonField(text, "Skills", skillSummary, true);
            AppendPersonField(text, "Destination", person.currentDestinationBuildingId >= 0 ? person.currentDestinationBuildingId.ToString("000") : null, showDevelopmentPersonDetails);

            AppendRelations(text, person);
            AppendCapabilityEvidence(text, person);

            if (showDevelopmentPersonDetails)
            {
                text.AppendLine();
                text.AppendLine("DEVELOPMENT DETAILS");
                AppendPersonField(text, "Status", person.deathDayIndex >= 0 ? $"Deceased on day {person.deathDayIndex}" : person.isOffMap ? "Off map" : "Living, on map", true);
                AppendPersonField(text, "Wage snapshot", person.wage.weeklyWage > 0 ? $"{person.wage.weeklyWage} cents per week" : null, person.wage.weeklyWage > 0);
            }

            return text.ToString().TrimEnd();
        }

        private string DescribeBuilding(int buildingId)
        {
            if (buildingId < 0) return "Unknown";
            if (townWorld != null && townWorld.TryGetBuildingById(buildingId, out PlacedBuilding building)
                && building?.definition != null)
            {
                return $"{building.definition.DisplayName} ({buildingId:000})";
            }
            return $"Building {buildingId:000}";
        }

        private string DescribeWorkplace(int buildingId)
        {
            if (buildingId < 0) return "No current workplace recorded";
            BusinessInstanceState business = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.FindByBuildingId(buildingId)
                : null;
            return business != null
                ? $"{business.RuntimeDisplayName} at {DescribeBuilding(buildingId)}"
                : DescribeBuilding(buildingId);
        }

        private string DescribeOwnedBusinesses(int personId)
        {
            IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime?.Businesses;
            if (businesses == null) return "Business records unavailable";
            var names = new StringBuilder();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business?.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Npc
                    || business.Owner.PersonId != personId) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(business.RuntimeDisplayName);
            }
            return names.Length > 0 ? names.ToString() : "None recorded";
        }

        private static void AppendPersonField(StringBuilder text, string label, string value, bool isKnown)
        {
            text.Append(label).Append(": ").AppendLine(isKnown && !string.IsNullOrWhiteSpace(value) ? value : "Unknown");
        }

        private void AppendRelations(StringBuilder text, PersonState person)
        {
            text.AppendLine();
            text.AppendLine("RELATIONS");
            KinshipRegistry kinship = populationManager != null ? populationManager.Kinship : null;
            if (kinship == null)
            {
                AppendPersonField(text, "Family connections", null, false);
                return;
            }

            AppendRelationGroup(text, "Spouse", kinship.GetSpouses(person.id));
            AppendRelationGroup(text, "Parents", kinship.GetParents(person.id));
            AppendRelationGroup(text, "Children", kinship.GetChildren(person.id));
            AppendRelationGroup(text, "Siblings", kinship.GetRelated(person.id, KinshipRelation.Sibling));
        }

        private void AppendRelationGroup(StringBuilder text, string label, System.Collections.Generic.List<int> relatedIds)
        {
            if (relatedIds == null || relatedIds.Count == 0)
            {
                AppendPersonField(text, label, showDevelopmentPersonDetails ? "No recorded relation" : null, showDevelopmentPersonDetails);
                return;
            }

            var names = new StringBuilder();
            for (int i = 0; i < relatedIds.Count; i++)
            {
                PersonState relative = populationManager.State.GetPerson(relatedIds[i]);
                if (i > 0) names.Append(", ");
                names.Append(relative != null ? relative.DisplayName.Trim() : $"P{relatedIds[i]:000}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (showDevelopmentPersonDetails) names.Append($" [P{relatedIds[i]:000}]");
#endif
            }

                AppendPersonField(text, label, names.ToString(), true);
        }

        private static void AppendCapabilityEvidence(StringBuilder text, PersonState person)
        {
            LandLedgers.Skills.CapabilityEvidenceReading reading = CapabilityPresentation.FromPerson(person);
            string value = CapabilityPresentation.FormatReading(reading);
            bool isKnown = reading != null && reading.Confidence != LandLedgers.Skills.CapabilityConfidence.Insufficient;
            AppendPersonField(text, "Work capability estimate", value, isKnown);
            if (isKnown && !string.IsNullOrWhiteSpace(reading.Basis))
            {
                AppendPersonField(text, "Evidence", reading.Basis, true);
            }
        }

        private void ClosePersonInspection()
        {
            if (personInspectionRoot != null)
            {
                personInspectionRoot.gameObject.SetActive(false);
            }
        }
    }
}
