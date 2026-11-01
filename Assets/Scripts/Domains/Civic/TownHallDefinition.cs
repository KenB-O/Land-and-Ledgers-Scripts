using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Civic
{
    [CreateAssetMenu(
        fileName = "TownHallDefinition",
        menuName = "Land & Ledgers/Civic/Town Hall Definition",
        order = 180)]
    public sealed class TownHallDefinition : ScriptableObject
    {
        [Header("Civic Identity")]
        [SerializeField]
        private string holdingId = TownHallState.TownHallBuildingId;

        [SerializeField]
        private string displayName = "Town Hall";

        [SerializeField]
        private CivicOwnerKind ownerKind = CivicOwnerKind.Town;

        [SerializeField]
        private string ownerDisplayName = "Town Council";

        [SerializeField]
        private string civicStatusLabel = "Civic";

        [Header("Physical Shell")]
        [SerializeField]
        private BuildingDefinition physicalBuildingDefinition;

        [Header("Civic Growth Roadmap")]
        [SerializeField]
        private CivicGrowthMilestone[] civicGrowthMilestones;

        [SerializeField]
        [Min(-1)]
        // Current-scope fallback until the Town Hall can read live town population directly.
        private int planningPopulationHint = 100;

        public string HoldingId => string.IsNullOrWhiteSpace(holdingId) ? TownHallState.TownHallBuildingId : holdingId.Trim();
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? "Town Hall" : displayName.Trim();
        public CivicOwnerKind OwnerKind => ownerKind;
        public string OwnerDisplayName => string.IsNullOrWhiteSpace(ownerDisplayName) ? "Town Council" : ownerDisplayName.Trim();
        public string CivicStatusLabel => string.IsNullOrWhiteSpace(civicStatusLabel) ? "Civic" : civicStatusLabel.Trim();
        public BuildingDefinition PhysicalBuildingDefinition => physicalBuildingDefinition;
        public bool HasPhysicalBuildingDefinition => physicalBuildingDefinition != null;
        public bool HasValidPhysicalBuildingDefinition => TownHallState.IsTownHallDefinition(physicalBuildingDefinition);
        public TownHallDiagnosticFlags ConfigurationDiagnosticFlags => GetConfigurationDiagnosticFlags();
        public bool HasConfigurationIssues => ConfigurationDiagnosticFlags != TownHallDiagnosticFlags.None;
        public int ConfigurationIssueCount => TownHallDiagnosticsUtility.CountIssues(ConfigurationDiagnosticFlags);
        public int PlanningPopulationHint => Mathf.Max(-1, planningPopulationHint);
        public bool HasPlanningPopulationHint => PlanningPopulationHint >= 0;
        public CivicGrowthSnapshot PlanningGrowthSnapshot => CaptureGrowthSnapshot(PlanningPopulationHint);
        public bool HasGrowthRoadmap => CivicGrowthPlanUtility.SanitizePlan(civicGrowthMilestones).Length > 0;

        public void ApplyIdentity(TownHallState state)
        {
            if (state == null)
            {
                return;
            }

            state.ApplyIdentity(
                HoldingId,
                DisplayName,
                OwnerKind,
                OwnerDisplayName,
                CivicStatusLabel);
        }

        public bool MatchesPhysicalDefinition(BuildingDefinition definition)
        {
            return physicalBuildingDefinition != null && physicalBuildingDefinition == definition;
        }

        public TownHallDiagnosticFlags GetConfigurationDiagnosticFlags()
        {
            if (!HasPhysicalBuildingDefinition)
            {
                return TownHallDiagnosticFlags.MissingPhysicalDefinition;
            }

            return HasValidPhysicalBuildingDefinition
                ? TownHallDiagnosticFlags.None
                : TownHallDiagnosticFlags.InvalidPhysicalDefinition;
        }

        public CivicGrowthMilestone[] GetGrowthRoadmap()
        {
            return CivicGrowthPlanUtility.SanitizePlan(civicGrowthMilestones);
        }

        public bool TryGetNextGrowthMilestone(int currentPopulation, out CivicGrowthMilestone milestone)
        {
            return CivicGrowthPlanUtility.TryGetNextMilestone(civicGrowthMilestones, currentPopulation, out milestone);
        }

        public CivicGrowthSnapshot CaptureGrowthSnapshot(int currentPopulation)
        {
            return CivicGrowthPlanUtility.CaptureGrowthSnapshot(civicGrowthMilestones, currentPopulation);
        }

        public string BuildConfigurationHealthLine()
        {
            return TownHallPresentationUtility.BuildDefinitionHealthLine(GetConfigurationDiagnosticFlags());
        }

        public string BuildConfigurationSummary()
        {
            string shellSummary = !HasPhysicalBuildingDefinition
                ? "Shell: missing"
                : HasValidPhysicalBuildingDefinition
                    ? "Shell: civic-compatible"
                    : "Shell: non-civic";
            return $"{BuildConfigurationHealthLine()} | {shellSummary}";
        }

        public string BuildConfigurationActionText()
        {
            TownHallDiagnosticFlags flags = ConfigurationDiagnosticFlags;
            if (flags == TownHallDiagnosticFlags.None)
            {
                return string.Empty;
            }

            if (flags.HasFlag(TownHallDiagnosticFlags.MissingPhysicalDefinition))
            {
                return "Assign the intended physical Town Hall building definition on this asset.";
            }

            return "Replace the physical building definition with a civic-compatible Town Hall shell.";
        }

        public string BuildAuthoringActionText()
        {
            string configurationAction = BuildConfigurationActionText();
            if (!string.IsNullOrWhiteSpace(configurationAction))
            {
                return configurationAction;
            }

            if (!HasGrowthRoadmap)
            {
                return "Add at least one civic growth milestone so the Town Hall can explain future institutional pressure.";
            }

            if (!HasPlanningPopulationHint)
            {
                return "Set a planning population hint to preview the next civic pressure point in editor/readout surfaces.";
            }

            string upcomingNeed = BuildGrowthUpcomingNeed(PlanningPopulationHint);
            return string.IsNullOrWhiteSpace(upcomingNeed)
                ? "Definition ready for runtime civic foundation reads."
                : upcomingNeed;
        }

        public string BuildConfigurationNoticeText()
        {
            TownHallDiagnosticFlags flags = ConfigurationDiagnosticFlags;
            return flags == TownHallDiagnosticFlags.None
                ? "Definition ready."
                : $"Definition needs review. {BuildConfigurationActionText()}";
        }

        public string BuildIdentitySummary()
        {
            return TownHallPresentationUtility.BuildIdentitySummary(DisplayName, OwnerDisplayName, CivicStatusLabel);
        }

        public string BuildGrowthRoadmapSummary()
        {
            return CivicGrowthPlanUtility.BuildRoadmapSummary(civicGrowthMilestones);
        }

        public string BuildGrowthRoadmapSummary(int currentPopulation)
        {
            return currentPopulation >= 0
                ? CivicGrowthPlanUtility.BuildNextMilestoneSummary(civicGrowthMilestones, currentPopulation)
                : BuildGrowthRoadmapSummary();
        }

        public string BuildGrowthRoadmapLine(int currentPopulation = -1)
        {
            string summary = BuildGrowthRoadmapSummary(currentPopulation);
            return string.IsNullOrWhiteSpace(summary)
                ? string.Empty
                : $"Growth roadmap: {summary}";
        }

        public string BuildGrowthProgressSummary(int currentPopulation)
        {
            return currentPopulation < 0
                ? BuildGrowthRoadmapSummary()
                : CivicGrowthPlanUtility.BuildProgressSummary(civicGrowthMilestones, currentPopulation);
        }

        public string BuildGrowthProgressLine(int currentPopulation)
        {
            if (currentPopulation < 0)
            {
                return BuildGrowthRoadmapLine();
            }

            string summary = BuildGrowthProgressSummary(currentPopulation);
            return string.IsNullOrWhiteSpace(summary)
                ? string.Empty
                : $"Growth progress: {summary}";
        }

        public string BuildGrowthUpcomingNeed(int currentPopulation)
        {
            return currentPopulation < 0
                ? string.Empty
                : CivicGrowthPlanUtility.BuildNextMilestoneNeed(civicGrowthMilestones, currentPopulation);
        }

        public string BuildGrowthPressureNeed(int currentPopulation)
        {
            return BuildGrowthUpcomingNeed(currentPopulation);
        }

        public string BuildPlanningProgressLine()
        {
            return HasPlanningPopulationHint
                ? BuildGrowthProgressLine(PlanningPopulationHint)
                : BuildGrowthRoadmapLine();
        }

        public string BuildAuthoringSummary()
        {
            string growthSummary = BuildGrowthRoadmapSummary();
            string planningSummary = HasPlanningPopulationHint
                ? $"Planning pop hint: {PlanningPopulationHint}"
                : "Planning pop hint: none";
            return $"{BuildIdentitySummary()} | {BuildConfigurationSummary()} | {planningSummary} | Roadmap: {growthSummary}";
        }

        public string BuildAuthoringNoticeText()
        {
            return $"{BuildIdentitySummary()} | {BuildConfigurationNoticeText()} | Next action: {BuildAuthoringActionText()}";
        }

        public string BuildAuthoringDetailText()
        {
            string planningLine = BuildPlanningProgressLine();
            string actionLine = BuildAuthoringActionText();
            return string.IsNullOrWhiteSpace(planningLine)
                ? $"{BuildAuthoringSummary()} | Next action: {actionLine}"
                : $"{BuildAuthoringSummary()} | {planningLine} | Next action: {actionLine}";
        }

        public string BuildReadoutSummary()
        {
            string roadmapSummary = HasGrowthRoadmap
                ? $"Roadmap: {GetGrowthRoadmap().Length} milestones"
                : "Roadmap: none";
            string planningSummary = HasPlanningPopulationHint
                ? BuildGrowthProgressSummary(PlanningPopulationHint)
                : "Planning preview: no population hint";
            return $"Definition | {BuildConfigurationSummary()} | {roadmapSummary} | {planningSummary}";
        }

        public void ConfigureRuntimeFallback(BuildingDefinition physicalDefinition)
        {
            holdingId = TownHallState.TownHallBuildingId;
            displayName = "Town Hall";
            ownerKind = CivicOwnerKind.Town;
            ownerDisplayName = "Town Council";
            civicStatusLabel = "Civic";
            physicalBuildingDefinition = physicalDefinition;
            if (civicGrowthMilestones == null || civicGrowthMilestones.Length == 0)
            {
                // Seed a grounded baseline roadmap so new Town Hall assets immediately surface
                // plausible civic growth targets instead of reading like empty placeholder data.
                civicGrowthMilestones = CivicGrowthPlanUtility.BuildDefaultRoadmap();
            }
        }

        private void OnValidate()
        {
            holdingId = string.IsNullOrWhiteSpace(holdingId) ? TownHallState.TownHallBuildingId : holdingId.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? "Town Hall" : displayName.Trim();
            ownerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "Town Council" : ownerDisplayName.Trim();
            civicStatusLabel = string.IsNullOrWhiteSpace(civicStatusLabel) ? "Civic" : civicStatusLabel.Trim();
            planningPopulationHint = Mathf.Max(-1, planningPopulationHint);

            if (civicGrowthMilestones == null || civicGrowthMilestones.Length == 0)
            {
                // Keep empty assets from silently dropping civic planning reads in inspector/debug surfaces.
                civicGrowthMilestones = CivicGrowthPlanUtility.BuildDefaultRoadmap();
            }
            else
            {
                civicGrowthMilestones = CivicGrowthPlanUtility.SanitizePlan(civicGrowthMilestones);
            }

            if (physicalBuildingDefinition != null && !TownHallState.IsTownHallDefinition(physicalBuildingDefinition))
            {
                Debug.LogWarning($"{nameof(TownHallDefinition)} '{name}' is bound to a non-civic physical building definition.", this);
            }
        }
    }
}
