using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Civic
{
    public enum CivicOwnerKind
    {
        Town = 0
    }

    public enum TownHallResolutionSource
    {
        None = 0,
        SavedReference = 1,
        TaggedWorldBuilding = 2,
        RecoveredSavedReference = 3
    }

    public enum TownHallReadinessState
    {
        Ready = 0,
        NeedsReview = 1,
        Unbuilt = 2,
        Blocked = 3
    }

    public enum TownHallRuntimeCondition
    {
        Ready = 0,
        NeedsReview = 1,
        AwaitingPlacement = 2,
        RecoverableDrift = 3,
        Blocked = 4
    }

    public enum TownHallIssueSeverity
    {
        None = 0,
        Warning = 1,
        Error = 2
    }

    public enum TownHallNoticeLevel
    {
        None = 0,
        Good = 1,
        Warning = 2,
        Error = 3
    }

    public enum CivicGrowthPressureLevel
    {
        None = 0,
        Distant = 1,
        NearTerm = 2,
        DueNow = 3,
        Complete = 4
    }

    [Flags]
    public enum TownHallDiagnosticFlags
    {
        None = 0,
        MissingTownWorld = 1 << 0,
        MissingTownHallDefinition = 1 << 1,
        MissingPhysicalDefinition = 1 << 2,
        InvalidPhysicalDefinition = 1 << 3,
        MultipleTaggedBuildings = 1 << 4,
        SavedReferenceMissing = 1 << 5,
        SavedReferencePlotMismatch = 1 << 6,
        SavedReferenceNotTagged = 1 << 7,
        SavedReferenceRecoveryBlockedByConflict = 1 << 8
    }

    public static class TownHallDiagnosticsUtility
    {
        public static int CountIssues(TownHallDiagnosticFlags flags)
        {
            int issueCount = 0;
            for (int bit = 1; bit <= (int)TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict; bit <<= 1)
            {
                if ((((int)flags) & bit) != 0)
                {
                    issueCount++;
                }
            }

            return issueCount;
        }

        public static bool HasBlockingIssues(TownHallDiagnosticFlags flags)
        {
            return flags.HasFlag(TownHallDiagnosticFlags.MissingTownWorld)
                || flags.HasFlag(TownHallDiagnosticFlags.MissingTownHallDefinition)
                || flags.HasFlag(TownHallDiagnosticFlags.MissingPhysicalDefinition)
                || flags.HasFlag(TownHallDiagnosticFlags.InvalidPhysicalDefinition)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict);
        }

        public static bool HasRecoverableDrift(TownHallDiagnosticFlags flags)
        {
            return flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceMissing)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferencePlotMismatch)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceNotTagged);
        }

        public static bool HasDefinitionIssues(TownHallDiagnosticFlags flags)
        {
            return flags.HasFlag(TownHallDiagnosticFlags.MissingTownHallDefinition)
                || flags.HasFlag(TownHallDiagnosticFlags.MissingPhysicalDefinition)
                || flags.HasFlag(TownHallDiagnosticFlags.InvalidPhysicalDefinition);
        }

        public static bool HasConflictIssues(TownHallDiagnosticFlags flags)
        {
            return flags.HasFlag(TownHallDiagnosticFlags.MultipleTaggedBuildings)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceRecoveryBlockedByConflict);
        }

        public static bool HasSavedReferenceIssues(TownHallDiagnosticFlags flags)
        {
            return flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceMissing)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferencePlotMismatch)
                || flags.HasFlag(TownHallDiagnosticFlags.SavedReferenceNotTagged);
        }

        public static TownHallIssueSeverity GetSeverity(TownHallDiagnosticFlags flags)
        {
            if (flags == TownHallDiagnosticFlags.None)
            {
                return TownHallIssueSeverity.None;
            }

            return HasBlockingIssues(flags)
                ? TownHallIssueSeverity.Error
                : TownHallIssueSeverity.Warning;
        }

        public static TownHallReadinessState GetReadiness(bool built, TownHallDiagnosticFlags flags)
        {
            if (built)
            {
                return flags == TownHallDiagnosticFlags.None
                    ? TownHallReadinessState.Ready
                    : TownHallReadinessState.NeedsReview;
            }

            return HasBlockingIssues(flags)
                ? TownHallReadinessState.Blocked
                : TownHallReadinessState.Unbuilt;
        }

        public static TownHallRuntimeCondition GetRuntimeCondition(bool built, TownHallDiagnosticFlags flags)
        {
            if (built)
            {
                return flags == TownHallDiagnosticFlags.None
                    ? TownHallRuntimeCondition.Ready
                    : TownHallRuntimeCondition.NeedsReview;
            }

            if (HasBlockingIssues(flags))
            {
                return TownHallRuntimeCondition.Blocked;
            }

            if (HasRecoverableDrift(flags))
            {
                return TownHallRuntimeCondition.RecoverableDrift;
            }

            return TownHallRuntimeCondition.AwaitingPlacement;
        }

        public static TownHallNoticeLevel GetNoticeLevel(TownHallRuntimeCondition condition, TownHallIssueSeverity severity)
        {
            if (severity == TownHallIssueSeverity.Error || condition == TownHallRuntimeCondition.Blocked)
            {
                return TownHallNoticeLevel.Error;
            }

            if (condition == TownHallRuntimeCondition.Ready)
            {
                return TownHallNoticeLevel.Good;
            }

            return condition == TownHallRuntimeCondition.AwaitingPlacement && severity == TownHallIssueSeverity.None
                ? TownHallNoticeLevel.None
                : TownHallNoticeLevel.Warning;
        }

        public static bool RequiresAction(TownHallRuntimeCondition condition, TownHallDiagnosticFlags flags)
        {
            return condition != TownHallRuntimeCondition.Ready
                && (flags != TownHallDiagnosticFlags.None || condition == TownHallRuntimeCondition.AwaitingPlacement);
        }
    }


    public readonly struct CivicGrowthSnapshot
    {
        public CivicGrowthSnapshot(
            int currentPopulation,
            int totalMilestones,
            int unlockedMilestones,
            bool hasNextMilestone,
            CivicGrowthMilestone nextMilestone,
            int residentsToNext,
            CivicGrowthPressureLevel pressureLevel)
        {
            CurrentPopulation = Mathf.Max(0, currentPopulation);
            TotalMilestones = Mathf.Max(0, totalMilestones);
            UnlockedMilestones = Mathf.Clamp(unlockedMilestones, 0, TotalMilestones);
            HasNextMilestone = hasNextMilestone;
            NextMilestone = nextMilestone;
            ResidentsToNext = Mathf.Max(0, residentsToNext);
            PressureLevel = pressureLevel;
        }

        public int CurrentPopulation { get; }
        public int TotalMilestones { get; }
        public int UnlockedMilestones { get; }
        public bool HasNextMilestone { get; }
        public CivicGrowthMilestone NextMilestone { get; }
        public int ResidentsToNext { get; }
        public CivicGrowthPressureLevel PressureLevel { get; }
        public bool HasRoadmap => TotalMilestones > 0;
        public bool IsComplete => PressureLevel == CivicGrowthPressureLevel.Complete;
    }

    [Serializable]
    public struct CivicGrowthMilestone
    {
        [Min(0)]
        public int minimumPopulation;

        public string buildingName;

        [TextArea(2, 4)]
        public string rationale;

        public int MinimumPopulation => Mathf.Max(0, minimumPopulation);
        public string BuildingName => string.IsNullOrWhiteSpace(buildingName) ? string.Empty : buildingName.Trim();
        public string Rationale => string.IsNullOrWhiteSpace(rationale) ? string.Empty : rationale.Trim();
        public bool IsConfigured => !string.IsNullOrWhiteSpace(BuildingName);

        public CivicGrowthMilestone Sanitized()
        {
            return new CivicGrowthMilestone
            {
                minimumPopulation = MinimumPopulation,
                buildingName = BuildingName,
                rationale = Rationale
            };
        }
    }

    public static class CivicGrowthPlanUtility
    {
        public static CivicGrowthMilestone[] BuildDefaultRoadmap()
        {
            return new[]
            {
                new CivicGrowthMilestone
                {
                    minimumPopulation = 120,
                    buildingName = "Dedicated Schoolhouse",
                    rationale = "A more settled family town justifies a dedicated school room instead of borrowed civic space."
                },
                new CivicGrowthMilestone
                {
                    minimumPopulation = 160,
                    buildingName = "Fire Equipment Shed",
                    rationale = "Denser main-street frontage benefits from pumps, ladders, and bucket storage kept close at hand."
                },
                new CivicGrowthMilestone
                {
                    minimumPopulation = 220,
                    buildingName = "Marshal's Office & Lockup",
                    rationale = "Heavier commercial traffic and transient pressure make formal civic order more worthwhile."
                },
                new CivicGrowthMilestone
                {
                    minimumPopulation = 300,
                    buildingName = "Records Office & Civic Annex",
                    rationale = "Deeds, permits, and town books start to outgrow a single Town Hall room as the settlement matures."
                },
                new CivicGrowthMilestone
                {
                    minimumPopulation = 380,
                    buildingName = "Public Pump Yard",
                    rationale = "A larger town benefits from more formal public water handling and basic street-service infrastructure."
                }
            };
        }

        public static CivicGrowthMilestone[] SanitizePlan(CivicGrowthMilestone[] milestones)
        {
            if (milestones == null || milestones.Length == 0)
            {
                return Array.Empty<CivicGrowthMilestone>();
            }

            List<CivicGrowthMilestone> sanitized = new List<CivicGrowthMilestone>(milestones.Length);
            for (int i = 0; i < milestones.Length; i++)
            {
                CivicGrowthMilestone milestone = milestones[i].Sanitized();
                if (milestone.IsConfigured)
                {
                    sanitized.Add(milestone);
                }
            }

            sanitized.Sort((left, right) =>
            {
                int populationCompare = left.MinimumPopulation.CompareTo(right.MinimumPopulation);
                return populationCompare != 0
                    ? populationCompare
                    : string.Compare(left.BuildingName, right.BuildingName, StringComparison.Ordinal);
            });

            return sanitized.ToArray();
        }

        public static bool TryGetNextMilestone(CivicGrowthMilestone[] milestones, int currentPopulation, out CivicGrowthMilestone nextMilestone)
        {
            CivicGrowthMilestone[] sanitized = SanitizePlan(milestones);
            int normalizedPopulation = Mathf.Max(0, currentPopulation);
            for (int i = 0; i < sanitized.Length; i++)
            {
                if (sanitized[i].MinimumPopulation > normalizedPopulation)
                {
                    nextMilestone = sanitized[i];
                    return true;
                }
            }

            nextMilestone = default;
            return false;
        }

        public static int CountUnlockedMilestones(CivicGrowthMilestone[] milestones, int currentPopulation)
        {
            CivicGrowthMilestone[] sanitized = SanitizePlan(milestones);
            int normalizedPopulation = Mathf.Max(0, currentPopulation);
            int unlocked = 0;
            for (int i = 0; i < sanitized.Length; i++)
            {
                if (sanitized[i].MinimumPopulation <= normalizedPopulation)
                {
                    unlocked++;
                }
            }

            return unlocked;
        }

        public static CivicGrowthSnapshot CaptureGrowthSnapshot(CivicGrowthMilestone[] milestones, int currentPopulation)
        {
            CivicGrowthMilestone[] sanitized = SanitizePlan(milestones);
            int normalizedPopulation = Mathf.Max(0, currentPopulation);
            int unlockedMilestones = CountUnlockedMilestones(sanitized, normalizedPopulation);
            bool hasNextMilestone = TryGetNextMilestone(sanitized, normalizedPopulation, out CivicGrowthMilestone nextMilestone);
            int residentsToNext = hasNextMilestone
                ? Mathf.Max(0, nextMilestone.MinimumPopulation - normalizedPopulation)
                : 0;

            // These thresholds are intentionally lightweight planning heuristics.
            // They keep the roadmap useful in current scope without pretending we already
            // have a fully integrated civic-population expansion system.
            CivicGrowthPressureLevel pressureLevel = sanitized.Length == 0
                ? CivicGrowthPressureLevel.None
                : !hasNextMilestone
                    ? CivicGrowthPressureLevel.Complete
                    : residentsToNext <= 5
                        ? CivicGrowthPressureLevel.DueNow
                        : residentsToNext <= 25
                            ? CivicGrowthPressureLevel.NearTerm
                            : CivicGrowthPressureLevel.Distant;

            return new CivicGrowthSnapshot(
                normalizedPopulation,
                sanitized.Length,
                unlockedMilestones,
                hasNextMilestone,
                nextMilestone,
                residentsToNext,
                pressureLevel);
        }

        public static string BuildRoadmapSummary(CivicGrowthMilestone[] milestones)
        {
            CivicGrowthMilestone[] sanitized = SanitizePlan(milestones);
            if (sanitized.Length == 0)
            {
                return "No civic growth roadmap configured.";
            }

            List<string> labels = new List<string>(sanitized.Length);
            for (int i = 0; i < sanitized.Length; i++)
            {
                labels.Add($"{sanitized[i].BuildingName} ({sanitized[i].MinimumPopulation})");
            }

            return string.Join(" | ", labels);
        }

        public static string BuildProgressSummary(CivicGrowthMilestone[] milestones, int currentPopulation)
        {
            CivicGrowthSnapshot snapshot = CaptureGrowthSnapshot(milestones, currentPopulation);
            if (!snapshot.HasRoadmap)
            {
                return "No civic growth roadmap configured.";
            }

            string progressLabel = $"Population hint {snapshot.CurrentPopulation} | {snapshot.UnlockedMilestones}/{snapshot.TotalMilestones} civic milestones covered";
            if (!snapshot.HasNextMilestone)
            {
                return $"{progressLabel} | Current roadmap covered";
            }

            return $"{progressLabel} | Next: {snapshot.NextMilestone.BuildingName} in {snapshot.ResidentsToNext} residents ({TownHallPresentationUtility.BuildGrowthPressureLabel(snapshot.PressureLevel)})";
        }

        public static string BuildNextMilestoneSummary(CivicGrowthMilestone[] milestones, int currentPopulation)
        {
            return TryGetNextMilestone(milestones, currentPopulation, out CivicGrowthMilestone milestone)
                ? $"Next civic milestone: {milestone.BuildingName} at {milestone.MinimumPopulation} population"
                : "Civic roadmap milestones currently covered.";
        }

        public static string BuildNextMilestoneNeed(CivicGrowthMilestone[] milestones, int currentPopulation)
        {
            CivicGrowthSnapshot snapshot = CaptureGrowthSnapshot(milestones, currentPopulation);
            if (!snapshot.HasNextMilestone)
            {
                return string.Empty;
            }

            string rationaleSuffix = string.IsNullOrEmpty(snapshot.NextMilestone.Rationale)
                ? string.Empty
                : $" {snapshot.NextMilestone.Rationale}";

            return snapshot.PressureLevel switch
            {
                CivicGrowthPressureLevel.DueNow => $"Next civic investment is pressing now: {snapshot.NextMilestone.BuildingName} sits about {snapshot.ResidentsToNext} residents away.{rationaleSuffix}",
                CivicGrowthPressureLevel.NearTerm => $"Start preparing for {snapshot.NextMilestone.BuildingName}; the town is about {snapshot.ResidentsToNext} residents from that threshold.{rationaleSuffix}",
                CivicGrowthPressureLevel.Distant => $"Longer-term civic target: {snapshot.NextMilestone.BuildingName} at {snapshot.NextMilestone.MinimumPopulation} population.{rationaleSuffix}",
                _ => string.Empty
            };
        }
    }

    public static class TownHallPresentationUtility
    {
        public static string BuildConditionLabel(TownHallRuntimeCondition condition)
        {
            return condition switch
            {
                TownHallRuntimeCondition.Ready => "Ready",
                TownHallRuntimeCondition.NeedsReview => "Needs review",
                TownHallRuntimeCondition.AwaitingPlacement => "Awaiting placement",
                TownHallRuntimeCondition.RecoverableDrift => "Recoverable drift",
                TownHallRuntimeCondition.Blocked => "Blocked",
                _ => "Unknown"
            };
        }

        public static string BuildSeverityLabel(TownHallIssueSeverity severity)
        {
            return severity switch
            {
                TownHallIssueSeverity.Warning => "Warning",
                TownHallIssueSeverity.Error => "Error",
                _ => "None"
            };
        }

        public static string BuildResolutionLabel(TownHallResolutionSource source)
        {
            return source switch
            {
                TownHallResolutionSource.SavedReference => "saved Town Hall reference",
                TownHallResolutionSource.TaggedWorldBuilding => "first tagged Town Hall in world",
                TownHallResolutionSource.RecoveredSavedReference => "recovered saved Town Hall reference",
                _ => "no Town Hall resolved"
            };
        }

        public static string BuildIssueCountLabel(int issueCount)
        {
            return issueCount <= 0
                ? "No issues"
                : issueCount == 1
                    ? "1 issue"
                    : $"{issueCount} issues";
        }

        public static string BuildNoticeLevelLabel(TownHallNoticeLevel level)
        {
            return level switch
            {
                TownHallNoticeLevel.Good => "Good",
                TownHallNoticeLevel.Warning => "Warning",
                TownHallNoticeLevel.Error => "Blocked",
                _ => "Quiet"
            };
        }

        public static string BuildGrowthPressureLabel(CivicGrowthPressureLevel level)
        {
            return level switch
            {
                CivicGrowthPressureLevel.DueNow => "Due now",
                CivicGrowthPressureLevel.NearTerm => "Near-term",
                CivicGrowthPressureLevel.Distant => "Later",
                CivicGrowthPressureLevel.Complete => "Covered",
                _ => "Unplanned"
            };
        }

        public static string BuildPrimaryContributionSummary(TownHallContribution contribution)
        {
            return $"Civic confidence +{FormatPercent(contribution.CivicConfidence01)} | Maturity +{FormatPercent(contribution.TownMaturity01)}";
        }

        public static string BuildSecondaryContributionSummary(TownHallContribution contribution)
        {
            return $"Immigration pull hook +{FormatPercent(contribution.ImmigrationPull01)} | Land confidence +{FormatPercent(contribution.LandConfidence01)}";
        }

        public static string BuildRuntimeHealthLine(TownHallRuntimeSnapshot snapshot)
        {
            return $"{BuildConditionLabel(snapshot.Condition)} | {BuildIssueCountLabel(snapshot.IssueCount)} | {BuildPrimaryContributionSummary(snapshot.Contribution)}";
        }

        public static string BuildManagementHealthLine(TownHallRuntimeSnapshot snapshot)
        {
            return snapshot.Condition switch
            {
                TownHallRuntimeCondition.Ready => $"Primary effects: {BuildPrimaryContributionSummary(snapshot.Contribution)}",
                TownHallRuntimeCondition.NeedsReview => $"Built with {BuildIssueCountLabel(snapshot.IssueCount).ToLowerInvariant()} | Primary effects: {BuildPrimaryContributionSummary(snapshot.Contribution)}",
                TownHallRuntimeCondition.AwaitingPlacement => "Primary effects inactive until the Town Hall is placed.",
                TownHallRuntimeCondition.RecoverableDrift => "Saved civic reference drifted; primary effects should not be trusted until repaired.",
                TownHallRuntimeCondition.Blocked => "Primary effects blocked by Town Hall configuration or world-state issues.",
                _ => BuildRuntimeHealthLine(snapshot)
            };
        }

        public static string BuildRuntimeStateLine(TownHallRuntimeSnapshot snapshot)
        {
            return $"Resolution: {BuildResolutionLabel(snapshot.ResolutionSource)} | Severity: {BuildSeverityLabel(snapshot.Severity)} | Notice: {BuildNoticeLevelLabel(snapshot.NoticeLevel)}";
        }

        public static string BuildManagementStatusLine(TownHallRuntimeSnapshot snapshot)
        {
            return snapshot.Condition switch
            {
                TownHallRuntimeCondition.Ready => "Ready for civic use.",
                TownHallRuntimeCondition.NeedsReview => "Built and active, but needs review.",
                TownHallRuntimeCondition.AwaitingPlacement => "Planned civic site awaiting placement.",
                TownHallRuntimeCondition.RecoverableDrift => "Saved Town Hall reference drifted from current world state.",
                TownHallRuntimeCondition.Blocked => "Blocked by Town Hall configuration or world-state issues.",
                _ => "Town Hall state unknown."
            };
        }

        public static string BuildNoticeHeadline(TownHallRuntimeSnapshot snapshot)
        {
            return snapshot.Condition switch
            {
                TownHallRuntimeCondition.Ready => "Town Hall operating normally.",
                TownHallRuntimeCondition.NeedsReview => "Town Hall needs review.",
                TownHallRuntimeCondition.AwaitingPlacement => "Town Hall not yet placed.",
                TownHallRuntimeCondition.RecoverableDrift => "Town Hall reference drift can be repaired.",
                TownHallRuntimeCondition.Blocked => "Town Hall is blocked by configuration or world-state issues.",
                _ => "Town Hall state unknown."
            };
        }

        public static string BuildNoticeSummary(TownHallRuntimeSnapshot snapshot)
        {
            string headline = BuildNoticeHeadline(snapshot);
            if (snapshot.NoticeLevel == TownHallNoticeLevel.None)
            {
                return headline;
            }

            return $"{BuildNoticeLevelLabel(snapshot.NoticeLevel)} | {headline}";
        }

        public static string BuildUpcomingNeedLabel(TownHallRuntimeSnapshot snapshot)
        {
            return snapshot.RequiresAction
                ? BuildConditionLabel(snapshot.Condition)
                : string.Empty;
        }

        public static string BuildContributionStateLine(TownHallRuntimeSnapshot snapshot)
        {
            return snapshot.Built
                ? BuildSecondaryContributionSummary(snapshot.Contribution)
                : "Civic benefits inactive until the Town Hall is built.";
        }

        public static string BuildGrowthLine(string growthSummary)
        {
            return string.IsNullOrWhiteSpace(growthSummary)
                ? string.Empty
                : growthSummary.Trim();
        }

        public static string BuildPlanningNeedLine(string planningNeed)
        {
            return string.IsNullOrWhiteSpace(planningNeed)
                ? string.Empty
                : planningNeed.Trim();
        }

        public static string BuildCompactReadout(TownHallRuntimeSnapshot snapshot, TownHallNoticeSnapshot notice)
        {
            if (snapshot.Condition == TownHallRuntimeCondition.Ready && !snapshot.RequiresAction)
            {
                return $"Ready | {BuildPrimaryContributionSummary(snapshot.Contribution)}";
            }

            if (snapshot.Condition == TownHallRuntimeCondition.AwaitingPlacement && !snapshot.RequiresAction)
            {
                return "Awaiting placement | Civic benefits inactive";
            }

            return notice.HasVisibleNotice
                ? notice.Summary
                : BuildManagementHealthLine(snapshot);
        }

        public static string BuildDefinitionHealthLine(TownHallDiagnosticFlags flags)
        {
            if (flags == TownHallDiagnosticFlags.None)
            {
                return "Configured | No issues";
            }

            string stateLabel = TownHallDiagnosticsUtility.HasBlockingIssues(flags)
                ? "Blocked"
                : "Needs review";
            return $"{stateLabel} | {BuildIssueCountLabel(TownHallDiagnosticsUtility.CountIssues(flags))}";
        }

        public static string BuildIdentitySummary(string displayName, string ownerDisplayName, string civicStatusLabel)
        {
            string normalizedDisplayName = NormalizeText(displayName, "Town Hall");
            string normalizedOwnerDisplayName = NormalizeText(ownerDisplayName, "Town Council");
            string normalizedStatusLabel = NormalizeText(civicStatusLabel, "Civic");
            return $"{normalizedDisplayName} | Owner: {normalizedOwnerDisplayName} | Status: {normalizedStatusLabel}";
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f)}%";
        }
    }

    public readonly struct TownHallReadoutSnapshot
    {
        public TownHallReadoutSnapshot(
            TownHallRuntimeSnapshot runtimeSnapshot,
            TownHallNoticeSnapshot noticeSnapshot,
            string definitionSummary,
            string diagnosticsText,
            string growthLine,
            string planningNeedLine)
        {
            RuntimeSnapshot = runtimeSnapshot;
            NoticeSnapshot = noticeSnapshot;
            TitleLine = runtimeSnapshot.Built
                ? $"{runtimeSnapshot.DisplayName} | {runtimeSnapshot.CivicStatusLabel}"
                : "Town Hall not built.";
            OwnerLine = runtimeSnapshot.Built
                ? $"Owner: {runtimeSnapshot.OwnerDisplayName}"
                : string.Empty;
            LocationLine = runtimeSnapshot.Built
                ? $"Plot {runtimeSnapshot.PlotId:000} | Building {runtimeSnapshot.BuildingId:000}"
                : string.Empty;
            StatusLine = TownHallPresentationUtility.BuildManagementStatusLine(runtimeSnapshot);
            HealthLine = TownHallPresentationUtility.BuildManagementHealthLine(runtimeSnapshot);
            RuntimeStateLine = runtimeSnapshot.HasDiagnostics || runtimeSnapshot.Condition != TownHallRuntimeCondition.Ready
                ? TownHallPresentationUtility.BuildRuntimeStateLine(runtimeSnapshot)
                : string.Empty;
            NoticeLine = noticeSnapshot.HasVisibleNotice
                ? noticeSnapshot.Summary
                : string.Empty;
            ContributionLine = TownHallPresentationUtility.BuildContributionStateLine(runtimeSnapshot);
            DefinitionLine = string.IsNullOrWhiteSpace(definitionSummary)
                ? "Town Hall definition missing."
                : definitionSummary.Trim();
            DiagnosticsText = string.IsNullOrWhiteSpace(diagnosticsText)
                ? string.Empty
                : diagnosticsText.Trim();
            GrowthLine = TownHallPresentationUtility.BuildGrowthLine(growthLine);
            NextStepLine = noticeSnapshot.RequiresAction && !string.IsNullOrWhiteSpace(noticeSnapshot.ActionText)
                ? noticeSnapshot.ActionText.Trim()
                : string.Empty;
            // Runtime repair work always outranks longer-term civic growth planning in the
            // current readout. Planning pressure only surfaces when the Town Hall itself is stable.
            UpcomingNeedLine = noticeSnapshot.RequiresAction
                ? (string.IsNullOrWhiteSpace(noticeSnapshot.UpcomingNeed) ? string.Empty : noticeSnapshot.UpcomingNeed.Trim())
                : TownHallPresentationUtility.BuildPlanningNeedLine(planningNeedLine);
            CompactSummary = TownHallPresentationUtility.BuildCompactReadout(runtimeSnapshot, noticeSnapshot);
        }

        public TownHallRuntimeSnapshot RuntimeSnapshot { get; }
        public TownHallNoticeSnapshot NoticeSnapshot { get; }
        public string TitleLine { get; }
        public string OwnerLine { get; }
        public string LocationLine { get; }
        public string StatusLine { get; }
        public string HealthLine { get; }
        public string RuntimeStateLine { get; }
        public string NoticeLine { get; }
        public string ContributionLine { get; }
        public string DefinitionLine { get; }
        public string DiagnosticsText { get; }
        public string GrowthLine { get; }
        public string NextStepLine { get; }
        public string UpcomingNeedLine { get; }
        public string CompactSummary { get; }
        public bool HasOwnerLine => !string.IsNullOrEmpty(OwnerLine);
        public bool HasLocationLine => !string.IsNullOrEmpty(LocationLine);
        public bool HasRuntimeStateLine => !string.IsNullOrEmpty(RuntimeStateLine);
        public bool HasNoticeLine => !string.IsNullOrEmpty(NoticeLine);
        public bool HasDiagnosticsText => !string.IsNullOrEmpty(DiagnosticsText);
        public bool HasGrowthLine => !string.IsNullOrEmpty(GrowthLine);
        public bool HasNextStepLine => !string.IsNullOrEmpty(NextStepLine);
        public bool HasUpcomingNeedLine => !string.IsNullOrEmpty(UpcomingNeedLine);
    }

    public readonly struct TownHallNoticeSnapshot
    {
        public TownHallNoticeSnapshot(TownHallRuntimeSnapshot runtimeSnapshot, string actionText)
        {
            RuntimeSnapshot = runtimeSnapshot;
            ActionText = actionText ?? string.Empty;
            Level = runtimeSnapshot.NoticeLevel;
            Summary = TownHallPresentationUtility.BuildNoticeSummary(runtimeSnapshot);
            UpcomingNeed = runtimeSnapshot.RequiresAction
                ? string.IsNullOrWhiteSpace(ActionText)
                    ? TownHallPresentationUtility.BuildUpcomingNeedLabel(runtimeSnapshot)
                    : ActionText
                : string.Empty;
        }

        public TownHallRuntimeSnapshot RuntimeSnapshot { get; }
        public TownHallNoticeLevel Level { get; }
        public string Summary { get; }
        public string ActionText { get; }
        public string UpcomingNeed { get; }
        public bool HasVisibleNotice => Level == TownHallNoticeLevel.Warning
            || Level == TownHallNoticeLevel.Error
            || RuntimeSnapshot.RequiresAction;
        public bool RequiresAction => RuntimeSnapshot.RequiresAction;
    }

    public readonly struct TownHallRuntimeSnapshot
    {
        public TownHallRuntimeSnapshot(
            bool built,
            int buildingId,
            int plotId,
            string displayName,
            string ownerDisplayName,
            string civicStatusLabel,
            TownHallContribution contribution,
            TownHallResolutionSource resolutionSource,
            TownHallDiagnosticFlags diagnosticFlags,
            int taggedTownHallCount)
        {
            Built = built;
            BuildingId = buildingId;
            PlotId = plotId;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Town Hall" : displayName.Trim();
            OwnerDisplayName = string.IsNullOrWhiteSpace(ownerDisplayName) ? "Town Council" : ownerDisplayName.Trim();
            CivicStatusLabel = string.IsNullOrWhiteSpace(civicStatusLabel) ? "Civic" : civicStatusLabel.Trim();
            Contribution = contribution;
            ResolutionSource = resolutionSource;
            DiagnosticFlags = diagnosticFlags;
            TaggedTownHallCount = Mathf.Max(0, taggedTownHallCount);
            IssueCount = TownHallDiagnosticsUtility.CountIssues(diagnosticFlags);
            Severity = TownHallDiagnosticsUtility.GetSeverity(diagnosticFlags);
            Readiness = TownHallDiagnosticsUtility.GetReadiness(built, diagnosticFlags);
            Condition = TownHallDiagnosticsUtility.GetRuntimeCondition(built, diagnosticFlags);
            NoticeLevel = TownHallDiagnosticsUtility.GetNoticeLevel(Condition, Severity);
            RequiresAction = TownHallDiagnosticsUtility.RequiresAction(Condition, diagnosticFlags);
        }

        public bool Built { get; }
        public int BuildingId { get; }
        public int PlotId { get; }
        public string DisplayName { get; }
        public string OwnerDisplayName { get; }
        public string CivicStatusLabel { get; }
        public TownHallContribution Contribution { get; }
        public TownHallResolutionSource ResolutionSource { get; }
        public TownHallDiagnosticFlags DiagnosticFlags { get; }
        public int TaggedTownHallCount { get; }
        public int IssueCount { get; }
        public TownHallIssueSeverity Severity { get; }
        public TownHallReadinessState Readiness { get; }
        public TownHallRuntimeCondition Condition { get; }
        public TownHallNoticeLevel NoticeLevel { get; }
        public bool RequiresAction { get; }
        public bool HasDiagnostics => DiagnosticFlags != TownHallDiagnosticFlags.None;
    }

    [Serializable]
    public sealed class TownHallState
    {
        public const string TownHallBuildingId = "town_hall";

        public bool built;
        public int buildingId = -1;
        public int plotId = -1;
        public string holdingId = TownHallBuildingId;
        public string displayName = "Town Hall";
        public CivicOwnerKind ownerKind = CivicOwnerKind.Town;
        public string ownerDisplayName = "Town Council";
        public string civicStatusLabel = "Civic";

        public bool HasBuildingReference => buildingId >= 0 && plotId >= 0;
        public bool Built => built && HasBuildingReference;

        public static bool IsTownHallDefinition(BuildingDefinition definition)
        {
            return definition != null && definition.PrimaryUse == BuildingUseType.Civic;
        }

        public TownHallContribution CalculateContribution(TownHallContributionWeights weights)
        {
            return Built ? TownHallContribution.FromWeights(weights) : TownHallContribution.Zero;
        }

        public void ApplyIdentity(
            string newHoldingId,
            string newDisplayName,
            CivicOwnerKind newOwnerKind,
            string newOwnerDisplayName,
            string newCivicStatusLabel)
        {
            holdingId = newHoldingId;
            displayName = newDisplayName;
            ownerKind = newOwnerKind;
            ownerDisplayName = newOwnerDisplayName;
            civicStatusLabel = newCivicStatusLabel;
            NormalizeIdentity();
        }

        public void MarkBuilt(int newBuildingId, int newPlotId)
        {
            built = newBuildingId >= 0 && newPlotId >= 0;
            buildingId = built ? newBuildingId : -1;
            plotId = built ? newPlotId : -1;
            NormalizeIdentity();
        }

        public void MarkBuilt(PlacedBuilding building)
        {
            if (building == null)
            {
                MarkUnbuilt();
                return;
            }

            MarkBuilt(building.id, building.plotId);
        }

        public bool MatchesBuildingReference(int candidateBuildingId, int candidatePlotId)
        {
            return HasBuildingReference
                && buildingId == candidateBuildingId
                && plotId == candidatePlotId;
        }

        public void MarkUnbuilt()
        {
            built = false;
            buildingId = -1;
            plotId = -1;
            NormalizeIdentity();
        }

        public TownHallSaveDto CaptureSaveDto()
        {
            NormalizeIdentity();
            return new TownHallSaveDto
            {
                built = Built,
                buildingId = Built ? buildingId : -1,
                plotId = Built ? plotId : -1,
                holdingId = holdingId,
                displayName = displayName,
                ownerKind = ownerKind,
                ownerDisplayName = ownerDisplayName,
                civicStatusLabel = civicStatusLabel
            };
        }

        public void LoadFromSaveDto(TownHallSaveDto dto)
        {
            if (dto == null)
            {
                MarkUnbuilt();
                return;
            }

            built = dto.built;
            buildingId = dto.buildingId;
            plotId = dto.plotId;
            holdingId = dto.holdingId;
            displayName = dto.displayName;
            ownerKind = dto.ownerKind;
            ownerDisplayName = dto.ownerDisplayName;
            civicStatusLabel = dto.civicStatusLabel;

            if (!HasBuildingReference)
            {
                built = false;
                buildingId = -1;
                plotId = -1;
            }

            NormalizeIdentity();
        }

        public void NormalizeIdentity()
        {
            holdingId = NormalizeText(holdingId, TownHallBuildingId);
            displayName = NormalizeText(displayName, "Town Hall");
            ownerDisplayName = NormalizeText(ownerDisplayName, "Town Council");
            civicStatusLabel = NormalizeText(civicStatusLabel, "Civic");
        }

        private static string NormalizeText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }

    [Serializable]
    public struct TownHallContributionWeights
    {
        public const float MaxContribution01 = 0.2f;

        public float civicConfidence;
        public float townMaturity;
        public float immigrationPull;
        public float landConfidence;

        public static TownHallContributionWeights Default => new()
        {
            civicConfidence = 0.06f,
            townMaturity = 0.08f,
            immigrationPull = 0.03f,
            landConfidence = 0.04f
        };

        public TownHallContributionWeights Sanitized()
        {
            return new TownHallContributionWeights
            {
                civicConfidence = ClampContribution(civicConfidence),
                townMaturity = ClampContribution(townMaturity),
                immigrationPull = ClampContribution(immigrationPull),
                landConfidence = ClampContribution(landConfidence)
            };
        }

        private static float ClampContribution(float value)
        {
            return Mathf.Clamp(value, 0f, MaxContribution01);
        }
    }

    public readonly struct TownHallContribution
    {
        public TownHallContribution(
            float civicConfidence01,
            float townMaturity01,
            float immigrationPull01,
            float landConfidence01)
        {
            CivicConfidence01 = Mathf.Clamp(civicConfidence01, 0f, TownHallContributionWeights.MaxContribution01);
            TownMaturity01 = Mathf.Clamp(townMaturity01, 0f, TownHallContributionWeights.MaxContribution01);
            ImmigrationPull01 = Mathf.Clamp(immigrationPull01, 0f, TownHallContributionWeights.MaxContribution01);
            LandConfidence01 = Mathf.Clamp(landConfidence01, 0f, TownHallContributionWeights.MaxContribution01);
        }

        public float CivicConfidence01 { get; }
        public float TownMaturity01 { get; }
        public float ImmigrationPull01 { get; }
        public float LandConfidence01 { get; }

        public static TownHallContribution Zero => new(0f, 0f, 0f, 0f);

        public static TownHallContribution FromWeights(TownHallContributionWeights weights)
        {
            TownHallContributionWeights sanitized = weights.Sanitized();
            return new TownHallContribution(
                sanitized.civicConfidence,
                sanitized.townMaturity,
                sanitized.immigrationPull,
                sanitized.landConfidence);
        }
    }
}
