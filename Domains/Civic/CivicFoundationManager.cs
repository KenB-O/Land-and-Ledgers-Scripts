using System;
using System.Reflection;
using System.Text;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Civic
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(245)]
    public sealed class CivicFoundationManager : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationManager populationManager;

        [Header("Town Hall")]
        [SerializeField]
        private TownHallState townHall = new();

        [SerializeField]
        private TownHallContributionWeights townHallWeights = TownHallContributionWeights.Default;

        [SerializeField]
        private bool warnOnTownHallConflicts = true;

        [Header("Schoolhouse")]
        [SerializeField]
        private SchoolhouseState schoolhouse = new();

        [SerializeField]
        private SchoolhouseThresholds schoolhouseThresholds = SchoolhouseThresholds.Default;

        [SerializeField]
        private SchoolhouseContributionWeights schoolhouseWeights = SchoolhouseContributionWeights.Default;

        [SerializeField]
        private bool autoEstablishSchoolhouseWhenEligible = true;

        [SerializeField]
        [Min(-1)]
        // Readout-only fallback used until this manager can pull a live population signal.
        private int planningPopulationHint = -1;

        private const string SchoolhousePublicSiteRoleName = "Schoolhouse";

        private int taggedTownHallCount;
        private TownHallResolutionSource resolutionSource;
        private TownHallDiagnosticFlags diagnosticFlags;
        private int lastLoggedConflictCount = -1;
        private bool recoveredSavedReferencePending;
        private bool refreshInProgress;
        private bool hasCachedSnapshot;
        private int lastSnapshotFrame = -1;
        private TownHallRuntimeSnapshot cachedSnapshot;

        public TownHallState TownHall
        {
            get
            {
                townHall ??= new TownHallState();
                townHall.NormalizeIdentity();
                return townHall;
            }
        }

        public SchoolhouseState Schoolhouse
        {
            get
            {
                schoolhouse ??= new SchoolhouseState();
                schoolhouse.NormalizeIdentity();
                return schoolhouse;
            }
        }

        public TownHallContribution CurrentTownHallContribution => EnsureRuntimeSnapshotCurrent().Contribution;
        public SchoolhouseRuntimeSnapshot CurrentSchoolhouseSnapshot => CaptureSchoolhouseRuntimeSnapshot();
        public SchoolhouseContribution CurrentSchoolhouseContribution => CurrentSchoolhouseSnapshot.Contribution;
        public SchoolhouseDiagnosticFlags CurrentSchoolhouseDiagnosticFlags => CaptureSchoolhouseDiagnosticFlags(CurrentSchoolhouseSnapshot);
        public int SchoolhouseDiagnosticIssueCount => SchoolhouseDiagnosticsUtility.CountIssues(CurrentSchoolhouseDiagnosticFlags);
        public bool HasSchoolhouseDiagnostics => CurrentSchoolhouseDiagnosticFlags != SchoolhouseDiagnosticFlags.None;
        public CivicFoundationContribution CurrentCivicFoundationContribution => CivicFoundationContribution.From(CurrentTownHallContribution, CurrentSchoolhouseContribution);
        public TownHallContribution CurrentCombinedCivicContribution => CurrentCivicFoundationContribution.CoreContribution;
        public float CivicConfidenceContribution01 => CurrentCivicFoundationContribution.CivicConfidence01;
        public float TownMaturityContribution01 => CurrentCivicFoundationContribution.TownMaturity01;
        public float ImmigrationPullContribution01 => CurrentCivicFoundationContribution.ImmigrationPull01;
        public float LandConfidenceContribution01 => CurrentCivicFoundationContribution.LandConfidence01;
        public float FamilyAttractivenessContribution01 => CurrentCivicFoundationContribution.FamilyAttractiveness01;
        public float LaborStabilityContribution01 => CurrentCivicFoundationContribution.LaborStability01;
        public int TaggedTownHallCount => EnsureRuntimeSnapshotCurrent().TaggedTownHallCount;
        public TownHallResolutionSource ResolutionSource => EnsureRuntimeSnapshotCurrent().ResolutionSource;
        public TownHallDiagnosticFlags DiagnosticFlags => EnsureRuntimeSnapshotCurrent().DiagnosticFlags;
        public bool HasTownHallTagConflict => EnsureRuntimeSnapshotCurrent().TaggedTownHallCount > 1;
        public bool HasDiagnostics => EnsureRuntimeSnapshotCurrent().HasDiagnostics;
        public TownHallReadinessState ReadinessState => EnsureRuntimeSnapshotCurrent().Readiness;
        public TownHallRuntimeCondition RuntimeCondition => EnsureRuntimeSnapshotCurrent().Condition;
        public TownHallIssueSeverity IssueSeverity => EnsureRuntimeSnapshotCurrent().Severity;
        public TownHallNoticeLevel NoticeLevel => EnsureRuntimeSnapshotCurrent().NoticeLevel;
        public int DiagnosticIssueCount => EnsureRuntimeSnapshotCurrent().IssueCount;
        public int PlanningPopulationHint => GetPlanningPopulationHint();

        public void Configure(TownWorldController newTownWorld)
        {
            townWorld = newTownWorld;
            RefreshFromWorld();
        }

        public void Configure(TownWorldController newTownWorld, PopulationManager newPopulationManager)
        {
            townWorld = newTownWorld;
            populationManager = newPopulationManager;
            RefreshFromWorld();
        }

        public CivicSaveDto CaptureSaveDto()
        {
            RefreshFromWorld();
            return new CivicSaveDto
            {
                townHall = TownHall.CaptureSaveDto(),
                schoolhouse = Schoolhouse.CaptureSaveDto()
            };
        }

        public void LoadFromSaveDto(CivicSaveDto dto)
        {
            AutoWire();
            TownHall.LoadFromSaveDto(dto != null ? dto.townHall : null);
            Schoolhouse.LoadFromSaveDto(dto != null ? dto.schoolhouse : null);

            BeginDiagnosticPass();
            EvaluateSavedReferenceDiagnostics();

            if (!SavedTownHallStillExists() && TryRetagSavedTownHallBuilding())
            {
                recoveredSavedReferencePending = true;
            }

            RefreshFromWorld();
        }

        public void RefreshFromWorld()
        {
            InvalidateRuntimeSnapshot();
            RefreshFromWorldInternal();
        }

        public bool TryGetTownHallBuilding(out PlacedBuilding building)
        {
            TownHallRuntimeSnapshot snapshot = EnsureRuntimeSnapshotCurrent();
            if (!snapshot.Built || townWorld == null)
            {
                building = null;
                return false;
            }

            building = GetBuilding(snapshot.BuildingId);
            return IsTownHallBuilding(building);
        }

        public TownHallRuntimeSnapshot CaptureRuntimeSnapshot()
        {
            return EnsureRuntimeSnapshotCurrent();
        }

        public TownHallNoticeSnapshot CaptureNoticeSnapshot()
        {
            TownHallRuntimeSnapshot snapshot = EnsureRuntimeSnapshotCurrent();
            return new TownHallNoticeSnapshot(snapshot, BuildActionTextCore(snapshot));
        }

        public TownHallReadoutSnapshot CaptureReadoutSnapshot()
        {
            TownHallRuntimeSnapshot snapshot = EnsureRuntimeSnapshotCurrent();
            TownHallNoticeSnapshot notice = new TownHallNoticeSnapshot(snapshot, BuildActionTextCore(snapshot));
            return new TownHallReadoutSnapshot(
                snapshot,
                notice,
                BuildDefinitionSummaryText(),
                BuildDiagnosticsTextCore(snapshot),
                BuildGrowthPlanText(),
                BuildGrowthNeedText());
        }

        public SchoolhouseRuntimeSnapshot CaptureSchoolhouseRuntimeSnapshot()
        {
            return Schoolhouse.CaptureRuntimeSnapshot(GetPopulationState(), schoolhouseThresholds, schoolhouseWeights);
        }

        public string BuildHealthLine()
        {
            return TownHallPresentationUtility.BuildManagementHealthLine(EnsureRuntimeSnapshotCurrent());
        }

        public string BuildCivicContributionText()
        {
            return CurrentCivicFoundationContribution.BuildSummaryLine();
        }

        public string BuildDefinitionSummaryText()
        {
            TownHallDefinition definition = GetTownHallDefinition();
            return definition != null
                ? definition.BuildReadoutSummary()
                : "Town Hall definition missing.";
        }

        public string BuildGrowthPlanText()
        {
            TownHallDefinition definition = GetTownHallDefinition();
            if (definition == null || !definition.HasGrowthRoadmap)
            {
                return string.Empty;
            }

            int populationHint = GetPlanningPopulationHint();
            return populationHint < 0
                ? definition.BuildGrowthRoadmapLine()
                : definition.BuildGrowthProgressLine(populationHint);
        }

        public string BuildGrowthNeedText()
        {
            TownHallDefinition definition = GetTownHallDefinition();
            if (definition == null)
            {
                return string.Empty;
            }

            int populationHint = GetPlanningPopulationHint();
            return populationHint < 0
                ? string.Empty
                : definition.BuildGrowthPressureNeed(populationHint);
        }

        public string BuildCompactSummaryText()
        {
            TownHallReadoutSnapshot readout = CaptureReadoutSnapshot();
            SchoolhouseRuntimeSnapshot schoolhouseSnapshot = CaptureSchoolhouseRuntimeSnapshot();
            return $"Town Hall: {readout.CompactSummary} | {SchoolhousePresentationUtility.BuildCompactSummary(schoolhouseSnapshot)}";
        }

        public string BuildDetailText()
        {
            TownHallReadoutSnapshot readout = CaptureReadoutSnapshot();
            StringBuilder detail = new StringBuilder();
            detail.Append(readout.TitleLine);

            if (readout.HasOwnerLine)
            {
                detail.Append("\n").Append(readout.OwnerLine);
            }

            if (readout.HasLocationLine)
            {
                detail.Append("\n").Append(readout.LocationLine);
            }

            detail.Append("\nStatus: ").Append(readout.StatusLine);
            detail.Append("\nHealth: ").Append(readout.HealthLine);
            detail.Append("\nContributions: ").Append(readout.ContributionLine);
            detail.Append("\nCombined civic effects: ").Append(BuildCivicContributionText());
            detail.Append("\nDefinition: ").Append(readout.DefinitionLine);

            if (readout.HasGrowthLine)
            {
                detail.Append("\nGrowth: ").Append(readout.GrowthLine);
            }

            if (readout.HasNoticeLine)
            {
                detail.Append("\nNotice: ").Append(readout.NoticeLine);
            }

            if (readout.HasRuntimeStateLine)
            {
                detail.Append("\nRuntime: ").Append(readout.RuntimeStateLine);
            }

            if (readout.HasDiagnosticsText)
            {
                detail.Append("\n").Append(readout.DiagnosticsText);
            }

            if (readout.HasNextStepLine)
            {
                detail.Append("\nNext step: ").Append(readout.NextStepLine);
            }

            SchoolhouseRuntimeSnapshot schoolhouseSnapshot = CaptureSchoolhouseRuntimeSnapshot();
            SchoolhouseDiagnosticFlags schoolhouseDiagnostics = CaptureSchoolhouseDiagnosticFlags(schoolhouseSnapshot);
            detail.Append("\nSchoolhouse: ").Append(SchoolhousePresentationUtility.BuildStatusLine(schoolhouseSnapshot));
            detail.Append("\nSchoolhouse readiness: ").Append(SchoolhousePresentationUtility.BuildReadinessLine(schoolhouseSnapshot));
            detail.Append("\nSchoolhouse need: ").Append(schoolhouseSnapshot.Eligibility.BuildNeedSummary());
            detail.Append("\nSchoolhouse effects: ").Append(SchoolhousePresentationUtility.BuildContributionLine(schoolhouseSnapshot));

            if (schoolhouseDiagnostics != SchoolhouseDiagnosticFlags.None)
            {
                detail.Append("\nSchoolhouse diagnostics: ").Append(SchoolhousePresentationUtility.BuildDiagnosticSummaryLine(schoolhouseDiagnostics));
                detail.Append("\n").Append(SchoolhousePresentationUtility.BuildDiagnosticsText(schoolhouseSnapshot, schoolhouseDiagnostics));
            }

            string schoolhouseAction = SchoolhousePresentationUtility.BuildActionLine(schoolhouseSnapshot);
            if (!string.IsNullOrWhiteSpace(schoolhouseAction))
            {
                detail.Append("\nSchoolhouse next step: ").Append(schoolhouseAction);
            }

            string schoolhousePlacementBridge = BuildSchoolhousePlacementBridgeText(schoolhouseSnapshot);
            if (!string.IsNullOrWhiteSpace(schoolhousePlacementBridge))
            {
                detail.Append("\nSchoolhouse placement: ").Append(schoolhousePlacementBridge);
            }

            return detail.ToString();
        }

        public string BuildDiagnosticsText()
        {
            string townHallDiagnostics = CaptureReadoutSnapshot().DiagnosticsText;
            SchoolhouseRuntimeSnapshot schoolhouseSnapshot = CaptureSchoolhouseRuntimeSnapshot();
            string schoolhouseDiagnostics = SchoolhousePresentationUtility.BuildDiagnosticsText(
                schoolhouseSnapshot,
                CaptureSchoolhouseDiagnosticFlags(schoolhouseSnapshot));

            if (string.IsNullOrWhiteSpace(townHallDiagnostics))
            {
                return schoolhouseDiagnostics;
            }

            if (string.IsNullOrWhiteSpace(schoolhouseDiagnostics))
            {
                return townHallDiagnostics;
            }

            return $"{townHallDiagnostics}\n{schoolhouseDiagnostics}";
        }

        public string BuildActionText()
        {
            TownHallNoticeSnapshot notice = CaptureNoticeSnapshot();
            if (notice.RequiresAction && !string.IsNullOrWhiteSpace(notice.ActionText))
            {
                return notice.ActionText;
            }

            SchoolhouseRuntimeSnapshot schoolhouseSnapshot = CaptureSchoolhouseRuntimeSnapshot();
            return BuildSchoolhouseActionTextCore(
                schoolhouseSnapshot,
                CaptureSchoolhouseDiagnosticFlags(schoolhouseSnapshot));
        }

        public string BuildNoticeText()
        {
            TownHallReadoutSnapshot readout = CaptureReadoutSnapshot();
            SchoolhouseRuntimeSnapshot schoolhouseSnapshot = CaptureSchoolhouseRuntimeSnapshot();
            string townHallNotice = readout.HasNoticeLine
                ? readout.NoticeLine
                : readout.StatusLine;
            string schoolhouseNotice = BuildSchoolhouseNoticeTextCore(
                schoolhouseSnapshot,
                CaptureSchoolhouseDiagnosticFlags(schoolhouseSnapshot));

            if (string.IsNullOrWhiteSpace(schoolhouseNotice))
            {
                return townHallNotice;
            }

            return string.IsNullOrWhiteSpace(townHallNotice)
                ? schoolhouseNotice
                : $"{townHallNotice} | {schoolhouseNotice}";
        }

        public string BuildUpcomingNeedText()
        {
            TownHallReadoutSnapshot readout = CaptureReadoutSnapshot();
            return readout.HasUpcomingNeedLine
                ? readout.UpcomingNeedLine
                : string.Empty;
        }

        private int GetPlanningPopulationHint()
        {
            if (planningPopulationHint >= 0)
            {
                return planningPopulationHint;
            }

            TownHallDefinition definition = GetTownHallDefinition();
            return definition != null
                ? definition.PlanningPopulationHint
                : -1;
        }

        private string BuildDiagnosticsTextCore(TownHallRuntimeSnapshot snapshot)
        {
            if (!snapshot.HasDiagnostics)
            {
                return string.Empty;
            }

            StringBuilder diagnostics = new StringBuilder();

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingTownWorld))
            {
                diagnostics.AppendLine("Issue: Town world controller is not wired.");
            }

            if (TownHallDiagnosticsUtility.HasDefinitionIssues(snapshot.DiagnosticFlags))
            {
                diagnostics.AppendLine(BuildDefinitionIssueText(snapshot));
            }

            if (TownHallDiagnosticsUtility.HasConflictIssues(snapshot.DiagnosticFlags))
            {
                diagnostics.AppendLine(BuildConflictIssueText(snapshot));
            }

            if (TownHallDiagnosticsUtility.HasSavedReferenceIssues(snapshot.DiagnosticFlags))
            {
                diagnostics.AppendLine(BuildSavedReferenceIssueText(snapshot));
            }

            return diagnostics.ToString().TrimEnd();
        }

        private string BuildDefinitionIssueText(TownHallRuntimeSnapshot snapshot)
        {
            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingTownHallDefinition))
            {
                return "Issue: No Town Hall definition is assigned in town settings.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingPhysicalDefinition)
                && snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.InvalidPhysicalDefinition))
            {
                return "Issue: Town Hall definition has conflicting shell diagnostics; review the assigned physical building definition.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingPhysicalDefinition))
            {
                return "Issue: Town Hall definition is missing its physical building definition.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.InvalidPhysicalDefinition))
            {
                return "Issue: Town Hall definition points to a non-civic physical building definition.";
            }

            return string.Empty;
        }

        private string BuildConflictIssueText(TownHallRuntimeSnapshot snapshot)
        {
            bool hasMultipleTaggedBuildings = snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MultipleTaggedBuildings);
            bool recoveryBlocked = snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict);

            if (hasMultipleTaggedBuildings && recoveryBlocked)
            {
                return $"Issue: {snapshot.TaggedTownHallCount} buildings are tagged as Town Hall, which also blocked saved-reference recovery.";
            }

            if (hasMultipleTaggedBuildings)
            {
                return $"Issue: {snapshot.TaggedTownHallCount} buildings are tagged as Town Hall.";
            }

            return "Issue: Saved Town Hall could not be re-tagged because another Town Hall tag already exists.";
        }

        private string BuildSavedReferenceIssueText(TownHallRuntimeSnapshot snapshot)
        {
            StringBuilder detail = new StringBuilder("Issue: Saved Town Hall reference drift detected");
            bool appendedReason = false;

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceMissing))
            {
                detail.Append(appendedReason ? "; " : " (");
                detail.Append("building no longer exists in the world list");
                appendedReason = true;
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferencePlotMismatch))
            {
                detail.Append(appendedReason ? "; " : " (");
                detail.Append("saved plot and building no longer match");
                appendedReason = true;
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceNotTagged))
            {
                detail.Append(appendedReason ? "; " : " (");
                detail.Append("building is no longer tagged as Town Hall");
                appendedReason = true;
            }

            if (appendedReason)
            {
                detail.Append(").");
            }
            else
            {
                detail.Append('.');
            }

            return detail.ToString();
        }

        private string BuildActionTextCore(TownHallRuntimeSnapshot snapshot)
        {
            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingTownWorld))
            {
                return "Wire the Town World Controller on the Civic Foundation Manager.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MissingTownHallDefinition))
            {
                return "Assign a Town Hall definition in the town world settings.";
            }

            TownHallDefinition definition = GetTownHallDefinition();
            if (definition != null && definition.HasConfigurationIssues)
            {
                return definition.BuildConfigurationActionText();
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.MultipleTaggedBuildings)
                || snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict))
            {
                return "Remove duplicate Town Hall tags so one intended civic building remains authoritative.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferencePlotMismatch))
            {
                return "Review the Town Hall plot and building pairing; the saved civic reference no longer matches the current plot.";
            }

            if (snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceMissing)
                || snapshot.DiagnosticFlags.HasFlag(TownHallDiagnosticFlags.SavedReferenceNotTagged))
            {
                return snapshot.Condition == TownHallRuntimeCondition.RecoverableDrift
                    ? "Re-tag or rebuild the intended Town Hall so the saved civic reference and world state match again."
                    : "Review the current Town Hall binding and resolve the stale saved reference.";
            }

            if (snapshot.Condition == TownHallRuntimeCondition.AwaitingPlacement)
            {
                return "Place or tag the intended Town Hall building when the civic site is ready.";
            }

            if (snapshot.Condition == TownHallRuntimeCondition.Blocked)
            {
                return "Resolve the blocking Town Hall configuration issue before expecting civic benefits.";
            }

            return string.Empty;
        }


        private string BuildSchoolhouseActionTextCore(SchoolhouseRuntimeSnapshot snapshot, SchoolhouseDiagnosticFlags flags)
        {
            if (flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPopulationData))
            {
                return "Wire the Population Manager so Schoolhouse pressure, attendance, and teacher assignment can be trusted.";
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPublicSiteRole))
            {
                return "Add a Schoolhouse public-site role before the civic lane can bind or place the Schoolhouse cleanly.";
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPlacementBridge))
            {
                return "Expose the town-world civic-site placement bridge, or manually tag the intended Schoolhouse site.";
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.MissingPhysicalDefinition))
            {
                return "Assign a civic-compatible Schoolhouse physical definition before auto-establishment can place the site.";
            }

            if (SchoolhouseDiagnosticsUtility.HasSavedReferenceIssues(flags))
            {
                return "Review the Schoolhouse building and plot binding; the saved schoolhouse reference no longer matches the world state.";
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.TeacherVacant))
            {
                return flags.HasFlag(SchoolhouseDiagnosticFlags.NoTeacherCandidate)
                    ? "Recruit or free an adult teacher candidate so the built Schoolhouse can open."
                    : "Assign an available adult teacher so the built Schoolhouse can open.";
            }

            if (flags.HasFlag(SchoolhouseDiagnosticFlags.CapacityStrained))
            {
                return "Plan added schoolroom capacity before schooling demand outgrows the one-room site.";
            }

            string placementBridge = BuildSchoolhousePlacementBridgeText(snapshot);
            if (!string.IsNullOrWhiteSpace(placementBridge))
            {
                return $"Resolve Schoolhouse placement: {placementBridge}";
            }

            return SchoolhousePresentationUtility.BuildActionLine(snapshot);
        }

        private string BuildSchoolhouseNoticeTextCore(SchoolhouseRuntimeSnapshot snapshot, SchoolhouseDiagnosticFlags flags)
        {
            string action = BuildSchoolhouseActionTextCore(snapshot, flags);
            if (!string.IsNullOrWhiteSpace(action) && snapshot.RequiresAction)
            {
                return $"Schoolhouse: {action}";
            }

            if (flags != SchoolhouseDiagnosticFlags.None)
            {
                return $"Schoolhouse: {SchoolhousePresentationUtility.BuildDiagnosticSummaryLine(flags)}";
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.PressureBuilding)
            {
                return "Schoolhouse: family schooling pressure is becoming visible.";
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.UnbuiltReady)
            {
                return "Schoolhouse: dedicated site is justified by household and child counts.";
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.Underused)
            {
                return "Schoolhouse: open but still underused by the current family base.";
            }

            if (snapshot.IsOperating && snapshot.IsCapacityStrained)
            {
                return $"Schoolhouse: operating over one-room capacity with {snapshot.UnservedSchoolAgeChildCount} children unserved.";
            }

            return string.Empty;
        }

        private void Awake()
        {
            AutoWire();
            RefreshFromWorld();
        }

        private void OnValidate()
        {
            townHall ??= new TownHallState();
            townHall.NormalizeIdentity();
            townHallWeights = townHallWeights.Sanitized();
            schoolhouse ??= new SchoolhouseState();
            schoolhouse.NormalizeIdentity();
            schoolhouseThresholds = schoolhouseThresholds.Sanitized();
            schoolhouseWeights = schoolhouseWeights.Sanitized();
            planningPopulationHint = Mathf.Max(-1, planningPopulationHint);
            InvalidateRuntimeSnapshot();
        }

        private TownHallRuntimeSnapshot EnsureRuntimeSnapshotCurrent()
        {
            if (!hasCachedSnapshot || ShouldRefreshRuntimeSnapshot())
            {
                RefreshFromWorldInternal();
            }

            return cachedSnapshot;
        }

        private bool ShouldRefreshRuntimeSnapshot()
        {
            // Most public readouts ask for several strings in the same frame. Cache once per
            // frame in play mode so those surfaces do not keep rescanning the world list.
            return !Application.isPlaying || lastSnapshotFrame != UnityEngine.Time.frameCount;
        }

        private void RefreshFromWorldInternal()
        {
            if (refreshInProgress)
            {
                return;
            }

            refreshInProgress = true;
            try
            {
                AutoWire();
                BeginDiagnosticPass();
                EvaluateSavedReferenceDiagnostics();

                if (TryResolveTownHallBuilding(out PlacedBuilding building))
                {
                    TownHall.MarkBuilt(building);
                }
                else
                {
                    TownHall.MarkUnbuilt();
                }

                ApplyTownHallDefinitionIdentity();
                RefreshSchoolhouseFromWorldAndPopulation();
                CacheRuntimeSnapshot();
                recoveredSavedReferencePending = false;
            }
            finally
            {
                refreshInProgress = false;
            }
        }

        private void CacheRuntimeSnapshot()
        {
            cachedSnapshot = new TownHallRuntimeSnapshot(
                TownHall.Built,
                TownHall.buildingId,
                TownHall.plotId,
                TownHall.displayName,
                TownHall.ownerDisplayName,
                TownHall.civicStatusLabel,
                TownHall.CalculateContribution(townHallWeights),
                resolutionSource,
                diagnosticFlags,
                taggedTownHallCount);
            hasCachedSnapshot = true;
            lastSnapshotFrame = Application.isPlaying ? UnityEngine.Time.frameCount : -1;
        }

        private void InvalidateRuntimeSnapshot()
        {
            hasCachedSnapshot = false;
            lastSnapshotFrame = -1;
        }

        private void BeginDiagnosticPass()
        {
            taggedTownHallCount = 0;
            resolutionSource = TownHallResolutionSource.None;
            diagnosticFlags = TownHallDiagnosticFlags.None;

            if (townWorld == null)
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.MissingTownWorld);
                ResetConflictLogging();
                return;
            }

            TownHallDefinition definition = GetTownHallDefinition();
            if (definition == null)
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.MissingTownHallDefinition);
            }
            else
            {
                AddDiagnosticFlag(definition.GetConfigurationDiagnosticFlags());
            }
        }

        private void EvaluateSavedReferenceDiagnostics()
        {
            if (!TownHall.HasBuildingReference || townWorld == null)
            {
                return;
            }

            PlacedBuilding building = GetBuilding(TownHall.buildingId);
            if (building == null)
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.SavedReferenceMissing);
                return;
            }

            if (building.plotId != TownHall.plotId)
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.SavedReferencePlotMismatch);
            }

            if (!IsTownHallBuilding(building))
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.SavedReferenceNotTagged);
            }
        }

        private bool SavedTownHallStillExists()
        {
            if (!TownHall.HasBuildingReference)
            {
                return false;
            }

            PlacedBuilding building = GetBuilding(TownHall.buildingId);
            return building != null
                && TownHall.MatchesBuildingReference(building.id, building.plotId)
                && IsTownHallBuilding(building);
        }

        private bool TryResolveTownHallBuilding(out PlacedBuilding building)
        {
            building = null;
            if (townWorld == null)
            {
                ResetConflictLogging();
                return false;
            }

            PlacedBuilding firstTaggedCandidate = null;
            PlacedBuilding savedTaggedCandidate = null;

            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding candidate = townWorld.Buildings[i];
                if (!IsTownHallBuilding(candidate))
                {
                    continue;
                }

                taggedTownHallCount++;
                firstTaggedCandidate ??= candidate;

                if (TownHall.MatchesBuildingReference(candidate.id, candidate.plotId))
                {
                    savedTaggedCandidate = candidate;
                }
            }

            if (taggedTownHallCount > 1)
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.MultipleTaggedBuildings);
            }

            if (savedTaggedCandidate != null)
            {
                building = savedTaggedCandidate;
                resolutionSource = recoveredSavedReferencePending
                    ? TownHallResolutionSource.RecoveredSavedReference
                    : TownHallResolutionSource.SavedReference;
                LogTownHallConflictIfNeeded();
                return true;
            }

            if (firstTaggedCandidate != null)
            {
                building = firstTaggedCandidate;
                resolutionSource = TownHallResolutionSource.TaggedWorldBuilding;
                LogTownHallConflictIfNeeded();
                return true;
            }

            ResetConflictLogging();
            return false;
        }

        private bool TryRetagSavedTownHallBuilding()
        {
            if (!TownHall.HasBuildingReference)
            {
                return false;
            }

            PlacedBuilding building = GetBuilding(TownHall.buildingId);
            if (building == null || building.plotId != TownHall.plotId)
            {
                return false;
            }

            if (IsTownHallBuilding(building))
            {
                return true;
            }

            if (HasDifferentTaggedTownHall(building.id, building.plotId))
            {
                AddDiagnosticFlag(TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict);
                return false;
            }

            building.publicSiteRole = PublicSiteRole.TownHall;
            TownPlot plot = GetPlot(TownHall.plotId);
            if (plot != null)
            {
                plot.publicSiteRole = PublicSiteRole.TownHall;
            }

            return true;
        }

        private bool HasDifferentTaggedTownHall(int buildingId, int plotId)
        {
            if (townWorld == null)
            {
                return false;
            }

            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding candidate = townWorld.Buildings[i];
                if (!IsTownHallBuilding(candidate))
                {
                    continue;
                }

                if (candidate.id != buildingId || candidate.plotId != plotId)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyTownHallDefinitionIdentity()
        {
            GetTownHallDefinition()?.ApplyIdentity(TownHall);
        }

        private TownHallDefinition GetTownHallDefinition()
        {
            return townWorld != null && townWorld.Settings != null
                ? townWorld.Settings.townHallDefinition
                : null;
        }

        private void RefreshSchoolhouseFromWorldAndPopulation()
        {
            if (TryResolveSchoolhouseBuilding(out PlacedBuilding building))
            {
                Schoolhouse.MarkBuilt(building);
            }
            else if (Schoolhouse.HasBuildingReference && !SavedSchoolhouseStillExists())
            {
                Schoolhouse.MarkUnbuilt();
            }

            PopulationState population = GetPopulationState();
            SchoolhouseEligibilitySnapshot eligibility = SchoolhouseEvaluator.CaptureEligibility(population, schoolhouseThresholds);
            if (!Schoolhouse.Built
                && autoEstablishSchoolhouseWhenEligible
                && eligibility.IsEligible
                && townWorld != null)
            {
                BuildingDefinition definition = ResolveSchoolhousePhysicalDefinition();
                if (TryEstablishSchoolhouseSite(definition, out PlacedBuilding established))
                {
                    Schoolhouse.MarkBuilt(established);
                }
            }

            if (Schoolhouse.Built)
            {
                SchoolhouseEvaluator.TryAssignTeacher(Schoolhouse, population, schoolhouseThresholds, out _, out _);
            }
        }

        private bool TryResolveSchoolhouseBuilding(out PlacedBuilding building)
        {
            building = null;
            if (townWorld == null)
            {
                return false;
            }

            PlacedBuilding firstTaggedCandidate = null;
            PlacedBuilding savedTaggedCandidate = null;
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding candidate = townWorld.Buildings[i];
                if (!HasPublicSiteRole(candidate, SchoolhousePublicSiteRoleName))
                {
                    continue;
                }

                firstTaggedCandidate ??= candidate;
                if (Schoolhouse.MatchesBuildingReference(candidate.id, candidate.plotId))
                {
                    savedTaggedCandidate = candidate;
                }
            }

            building = savedTaggedCandidate ?? firstTaggedCandidate;
            return building != null;
        }

        private bool SavedSchoolhouseStillExists()
        {
            if (!Schoolhouse.HasBuildingReference)
            {
                return false;
            }

            PlacedBuilding building = GetBuilding(Schoolhouse.buildingId);
            return building != null
                && Schoolhouse.MatchesBuildingReference(building.id, building.plotId)
                && HasPublicSiteRole(building, SchoolhousePublicSiteRoleName);
        }

        private BuildingDefinition ResolveSchoolhousePhysicalDefinition()
        {
            if (townWorld == null || townWorld.Settings == null)
            {
                return null;
            }

            if (TryGetSchoolhousePhysicalDefinitionFromSettings(out BuildingDefinition schoolhouseDefinition)
                && TownHallState.IsTownHallDefinition(schoolhouseDefinition))
            {
                return schoolhouseDefinition;
            }

            TownHallDefinition definition = GetTownHallDefinition();
            return definition != null && definition.HasValidPhysicalBuildingDefinition
                ? definition.PhysicalBuildingDefinition
                : null;
        }

        private bool TryEstablishSchoolhouseSite(BuildingDefinition definition, out PlacedBuilding established)
        {
            established = null;
            if (townWorld == null || definition == null || !TryGetPublicSiteRole(SchoolhousePublicSiteRoleName, out PublicSiteRole schoolhouseRole))
            {
                return false;
            }

            if (!TryGetSchoolhouseEstablishmentMethod(out MethodInfo method))
            {
                return false;
            }

            // The civic lane can detect and use the wider world placement bridge when present,
            // but it must not require that newer world API before the rest of the project catches up.
            object[] args = { schoolhouseRole, definition, null, null };
            bool placed = method.Invoke(townWorld, args) is bool result && result;
            established = placed ? args[2] as PlacedBuilding : null;
            return established != null;
        }

        private SchoolhouseDiagnosticFlags CaptureSchoolhouseDiagnosticFlags(SchoolhouseRuntimeSnapshot snapshot)
        {
            SchoolhouseDiagnosticFlags flags = SchoolhouseDiagnosticFlags.None;
            PopulationState population = GetPopulationState();

            if (townWorld == null)
            {
                flags |= SchoolhouseDiagnosticFlags.MissingTownWorld;
            }

            if (population == null)
            {
                flags |= SchoolhouseDiagnosticFlags.MissingPopulationData;
            }

            bool roleAvailable = TryGetPublicSiteRole(SchoolhousePublicSiteRoleName, out _);
            if (!roleAvailable && (snapshot.Built || snapshot.Eligibility.IsEligible))
            {
                flags |= SchoolhouseDiagnosticFlags.MissingPublicSiteRole;
            }

            if (snapshot.Built && townWorld != null)
            {
                PlacedBuilding building = GetBuilding(snapshot.BuildingId);
                if (building == null)
                {
                    flags |= SchoolhouseDiagnosticFlags.SavedReferenceMissing;
                }
                else
                {
                    if (building.plotId != snapshot.PlotId)
                    {
                        flags |= SchoolhouseDiagnosticFlags.SavedReferencePlotMismatch;
                    }

                    if (!HasPublicSiteRole(building, SchoolhousePublicSiteRoleName))
                    {
                        flags |= SchoolhouseDiagnosticFlags.SavedReferenceNotTagged;
                    }
                }
            }
            else if (!snapshot.Built && snapshot.Eligibility.IsEligible && autoEstablishSchoolhouseWhenEligible)
            {
                if (!TryGetSchoolhouseEstablishmentMethod(out _))
                {
                    flags |= SchoolhouseDiagnosticFlags.MissingPlacementBridge;
                }

                if (ResolveSchoolhousePhysicalDefinition() == null)
                {
                    flags |= SchoolhouseDiagnosticFlags.MissingPhysicalDefinition;
                }
            }

            if (snapshot.OperationalState == SchoolhouseOperationalState.TeacherVacant)
            {
                flags |= SchoolhouseDiagnosticFlags.TeacherVacant;
                if (!SchoolhouseEvaluator.HasAvailableTeacherCandidate(population, schoolhouseThresholds))
                {
                    flags |= SchoolhouseDiagnosticFlags.NoTeacherCandidate;
                }
            }

            if (snapshot.IsCapacityStrained)
            {
                flags |= SchoolhouseDiagnosticFlags.CapacityStrained;
            }

            return flags;
        }

        private string BuildSchoolhousePlacementBridgeText(SchoolhouseRuntimeSnapshot snapshot)
        {
            if (snapshot.Built || !autoEstablishSchoolhouseWhenEligible || !snapshot.Eligibility.IsEligible)
            {
                return string.Empty;
            }

            if (!TryGetPublicSiteRole(SchoolhousePublicSiteRoleName, out _))
            {
                return "waiting on a Schoolhouse public-site role in the world model.";
            }

            if (!TryGetSchoolhouseEstablishmentMethod(out _))
            {
                return "waiting on the town-world civic-site placement bridge.";
            }

            if (ResolveSchoolhousePhysicalDefinition() == null)
            {
                return "waiting on a civic-compatible Schoolhouse physical definition.";
            }

            return string.Empty;
        }

        private bool TryGetSchoolhouseEstablishmentMethod(out MethodInfo method)
        {
            method = null;
            if (townWorld == null)
            {
                return false;
            }

            method = townWorld.GetType().GetMethod(
                "TryEstablishCivicSite",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(PublicSiteRole),
                    typeof(BuildingDefinition),
                    typeof(PlacedBuilding).MakeByRefType(),
                    typeof(TownPlot).MakeByRefType()
                },
                null);

            return method != null && method.ReturnType == typeof(bool);
        }

        private bool TryGetSchoolhousePhysicalDefinitionFromSettings(out BuildingDefinition definition)
        {
            definition = null;
            object settings = townWorld != null ? townWorld.Settings : null;
            if (settings == null)
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type settingsType = settings.GetType();

            string[] memberNames = { "schoolhousePhysicalDefinition", "SchoolhousePhysicalDefinition" };
            for (int i = 0; i < memberNames.Length; i++)
            {
                FieldInfo field = settingsType.GetField(memberNames[i], flags);
                if (field != null)
                {
                    definition = field.GetValue(settings) as BuildingDefinition;
                    return definition != null;
                }

                PropertyInfo property = settingsType.GetProperty(memberNames[i], flags);
                if (property != null && typeof(BuildingDefinition).IsAssignableFrom(property.PropertyType))
                {
                    definition = property.GetValue(settings) as BuildingDefinition;
                    return definition != null;
                }
            }

            return false;
        }

        private static bool HasPublicSiteRole(PlacedBuilding building, string roleName)
        {
            return building != null
                && !string.IsNullOrWhiteSpace(roleName)
                && string.Equals(building.publicSiteRole.ToString(), roleName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPublicSiteRole(string roleName, out PublicSiteRole role)
        {
            return Enum.TryParse(roleName, true, out role);
        }

        private PopulationState GetPopulationState()
        {
            AutoWire();
            return populationManager != null ? populationManager.State : null;
        }

        private void LogTownHallConflictIfNeeded()
        {
            if (taggedTownHallCount <= 1)
            {
                ResetConflictLogging();
                return;
            }

            if (!warnOnTownHallConflicts || lastLoggedConflictCount == taggedTownHallCount)
            {
                return;
            }

            Debug.LogWarning(
                $"{nameof(CivicFoundationManager)} detected {taggedTownHallCount} Town Hall-tagged buildings. Using {TownHallPresentationUtility.BuildResolutionLabel(resolutionSource).ToLowerInvariant()}.",
                this);
            lastLoggedConflictCount = taggedTownHallCount;
        }

        private void ResetConflictLogging()
        {
            lastLoggedConflictCount = -1;
        }

        private void AddDiagnosticFlag(TownHallDiagnosticFlags flags)
        {
            diagnosticFlags |= flags;
        }

        private static bool IsTownHallBuilding(PlacedBuilding building)
        {
            return building != null && building.publicSiteRole == PublicSiteRole.TownHall;
        }

        private PlacedBuilding GetBuilding(int buildingId)
        {
            if (townWorld == null || buildingId < 0 || buildingId >= townWorld.Buildings.Count)
            {
                return null;
            }

            return townWorld.Buildings[buildingId];
        }

        private TownPlot GetPlot(int plotId)
        {
            if (townWorld == null || plotId < 0 || plotId >= townWorld.Plots.Count)
            {
                return null;
            }

            return townWorld.Plots[plotId];
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
        }
    }

    public readonly struct CivicFoundationContribution
    {
        private const float CombinedContributionMax01 = 1f;

        public CivicFoundationContribution(TownHallContribution townHall, SchoolhouseContribution schoolhouse)
        {
            TownHall = townHall;
            Schoolhouse = schoolhouse;
            CoreContribution = new TownHallContribution(
                townHall.CivicConfidence01 + schoolhouse.CivicConfidence01,
                townHall.TownMaturity01 + schoolhouse.TownMaturity01,
                townHall.ImmigrationPull01 + schoolhouse.ImmigrationPull01,
                townHall.LandConfidence01 + schoolhouse.LandConfidence01);
        }

        public TownHallContribution TownHall { get; }
        public SchoolhouseContribution Schoolhouse { get; }
        public TownHallContribution CoreContribution { get; }
        public float CivicConfidence01 => CoreContribution.CivicConfidence01;
        public float TownMaturity01 => CoreContribution.TownMaturity01;
        public float ImmigrationPull01 => CoreContribution.ImmigrationPull01;
        public float LandConfidence01 => CoreContribution.LandConfidence01;
        public float FamilyAttractiveness01 => ClampCombined(Schoolhouse.FamilyAttractiveness01);
        public float LaborStability01 => ClampCombined(Schoolhouse.LaborStability01);
        public bool HasSchoolhouseSpecificEffects => FamilyAttractiveness01 > 0f || LaborStability01 > 0f;

        public static CivicFoundationContribution From(TownHallContribution townHall, SchoolhouseContribution schoolhouse)
        {
            return new CivicFoundationContribution(townHall, schoolhouse);
        }

        public string BuildSummaryLine()
        {
            string core = $"Civic confidence +{FormatPercent(CivicConfidence01)} | Town maturity +{FormatPercent(TownMaturity01)} | Immigration pull +{FormatPercent(ImmigrationPull01)} | Land confidence +{FormatPercent(LandConfidence01)}";
            if (!HasSchoolhouseSpecificEffects)
            {
                return core;
            }

            // Schoolhouse-specific settlement effects do not fit the older TownHallContribution shape,
            // so keep them visible here instead of losing them in the combined civic bridge.
            return $"{core} | Family attractiveness +{FormatPercent(FamilyAttractiveness01)} | Labor stability +{FormatPercent(LaborStability01)}";
        }

        private static float ClampCombined(float value)
        {
            return Mathf.Clamp(value, 0f, CombinedContributionMax01);
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(ClampCombined(value01) * 100f)}%";
        }
    }
}
