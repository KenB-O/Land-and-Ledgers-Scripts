using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.World
{
    public enum ExteriorPropMatchKind
    {
        None = 0,
        BusinessExact = 1,
        AgriculturalExact = 2,
        AgriculturalGeneric = 3,
        MixedUse = 4,
        Residential = 5,
        Civic = 6,
        CommercialGeneric = 7
    }

    public readonly struct ExteriorPropSelectionPreview
    {
        public ExteriorPropSelectionPreview(
            BuildingExteriorPropGroup group,
            ExteriorPropMatchKind matchKind,
            int score,
            bool usesUpperFloorResidentialBoost)
        {
            Group = group;
            MatchKind = matchKind;
            Score = score;
            UsesUpperFloorResidentialBoost = usesUpperFloorResidentialBoost;
        }

        public BuildingExteriorPropGroup Group { get; }
        public ExteriorPropMatchKind MatchKind { get; }
        public int Score { get; }
        public bool UsesUpperFloorResidentialBoost { get; }
        public bool HasMatch => Group != null && MatchKind != ExteriorPropMatchKind.None;
        public string GroupName => Group != null ? Group.GetDisplayName() : "No Match";
    }

    public sealed class ExteriorPropRuntimeDiagnostics
    {
        public bool HasAuthoring { get; set; }
        public bool HasSelection { get; set; }
        public string SelectedGroupName { get; set; } = string.Empty;
        public string SelectionReason { get; set; } = string.Empty;
        public int EligibleGroupCount { get; set; }
        public int ManagedPropCount { get; set; }
        public int ActivePropCount { get; set; }
        public int NestedGroupRootSkips { get; set; }
        public int AncestorGroupRootsActivated { get; set; }
        public bool UsedArrayOrderTieBreak { get; set; }
        public int TopScoreTieCount { get; set; }
        public string TopScoreTieGroupSummary { get; set; } = string.Empty;
        public string NoSelectionCategory { get; set; } = string.Empty;
        public string NoSelectionDetail { get; set; } = string.Empty;
        public string SelectionRouteCategory { get; set; } = string.Empty;
        public string SelectionRouteDetail { get; set; } = string.Empty;
        public string SelectionCompetitionCategory { get; set; } = string.Empty;
        public string SelectionCompetitionDetail { get; set; } = string.Empty;
        public string SelectionCompetitionSeverity { get; set; } = string.Empty;
        public int BlockedMatchingGroupCount { get; set; }
        public int BlockedMissingRootCount { get; set; }
        public int BlockedPrefabRootCount { get; set; }
        public int BlockedOutsideHierarchyRootCount { get; set; }
        public int CollapsedSameRootMatchingGroupCount { get; set; }
        public bool UsedFallback { get; set; }
        public bool CollidersDisabled { get; set; }
    }

    [Serializable]
    public sealed class BuildingExteriorBusinessToggle
    {
        [Tooltip("Auto-synced to the BusinessType enum. Turn Enabled on for any business that should be allowed to use this group.")]
        public BusinessType businessType = BusinessType.GeneralStore;

        [Tooltip("When true, this group is eligible for the selected business type.")]
        public bool enabled;
    }

    [Serializable]
    public sealed class BuildingExteriorPropGroup
    {
        [Tooltip("Optional author-facing label used for debugging and deterministic salt. Leave blank to use the root object name.")]
        public string debugName = string.Empty;

        [Tooltip("The authored root for this prop group. Direct children of this root are treated as optional props by default.")]
        public Transform groupRoot;

        [Tooltip("Higher values win when more than one group matches. Keep most groups at 0 and only raise this when you need to force a preferred fallback.")]
        public int selectionPriority = 0;

        [Tooltip("When true, this group can act as a generic fallback for any commercial/business use when no exact business match exists.")]
        public bool matchesAnyCommercialBusiness;

        [Tooltip("When true, this group can act as a generic fallback for agricultural work sites when no exact yard-role group exists.")]
        public bool matchesAnyAgriculturalSite;

        [Tooltip("Dedicated agricultural site role for this prop group. Use this for crop yards, livestock yards, or sawmill yards.")]
        public AgriculturalSiteRole agriculturalSiteRole = AgriculturalSiteRole.None;

        [Tooltip("When true, this group is preferred for mixed-use storefront shells that also support household space.")]
        public bool matchesMixedUse;

        [Tooltip("When true, this group can be used for residential buildings or residential plots.")]
        public bool matchesResidential;

        [Tooltip("When true, this group gets a small boost when the shell carries upper-floor household space above a workplace.")]
        public bool matchesUpperFloorResidential;

        [Tooltip("When true, this group can be used for civic buildings.")]
        public bool matchesCivic;

        [Tooltip("Full business list for this group. Turn on the businesses this root group supports.")]
        public BuildingExteriorBusinessToggle[] businessTypeToggles = Array.Empty<BuildingExteriorBusinessToggle>();

        [Tooltip("When true, all descendants under Group Root are considered optional props. When false, only direct children are considered.")]
        public bool includeNestedChildren;

        [Tooltip("Fraction of authored props enabled when this group is selected. Runtime selection remains deterministic.")]
        [Range(0f, 1f)]
        public float activeRatio = 0.65f;

        [Tooltip("Minimum props to enable when this group is selected.")]
        [Min(0)]
        public int minimumActiveProps = 0;

        [Tooltip("When true, Maximum Active Props is used as a hard cap. When false, there is no explicit max override.")]
        public bool overrideMaximumActiveProps;

        [Tooltip("Hard cap used only when Override Maximum Active Props is enabled.")]
        [Min(0)]
        public int maximumActiveProps = 0;

        public bool HasDedicatedAgriculturalRole => agriculturalSiteRole != AgriculturalSiteRole.None;

        public bool MatchesBusiness(BusinessType businessType)
        {
            if (businessTypeToggles == null)
            {
                return false;
            }

            for (int i = 0; i < businessTypeToggles.Length; i++)
            {
                BuildingExteriorBusinessToggle toggle = businessTypeToggles[i];
                if (toggle != null && toggle.businessType == businessType)
                {
                    return toggle.enabled;
                }
            }

            return false;
        }

        public bool MatchesAgriculturalSiteRole(AgriculturalSiteRole siteRole)
        {
            return siteRole != AgriculturalSiteRole.None && agriculturalSiteRole == siteRole;
        }

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(debugName))
            {
                return debugName;
            }

            return groupRoot != null ? groupRoot.name : "Exterior Prop Group";
        }
    }

    [DisallowMultipleComponent]
    public sealed class BuildingExteriorPropAuthoring : MonoBehaviour, ISerializationCallbackReceiver
    {
        // These scores express authored fallback order only. They should stay stable so inspection reads and runtime dressing stay predictable.
        private const int BusinessExactScore = 5000;
        private const int AgriculturalExactScore = 4200;
        private const int AgriculturalGenericScore = 3200;
        private const int MixedUseScore = 2400;
        private const int ResidentialScore = 2000;
        private const int CivicScore = 1800;
        private const int CommercialGenericScore = 1200;
        private const int UpperFloorResidentialBonus = 120;

        [SerializeField]
        [Tooltip("Business/use-specific exterior prop groups authored as children on this building prefab.")]
        private BuildingExteriorPropGroup[] groups = Array.Empty<BuildingExteriorPropGroup>();

        [SerializeField]
        [Tooltip("When true, managed exterior prop colliders are disabled at runtime so presentation dressing does not affect movement or selection.")]
        private bool disableManagedPropColliders = true;

        public IReadOnlyList<BuildingExteriorPropGroup> Groups => groups;
        public int GroupCount => groups != null ? groups.Length : 0;

        public void ConfigureGroups(params BuildingExteriorPropGroup[] newGroups)
        {
            groups = newGroups ?? Array.Empty<BuildingExteriorPropGroup>();
            NormalizeGroups();
        }

        public ExteriorPropRuntimeDiagnostics ActivateForRuntime(
            BusinessType? businessType,
            BuildingDefinition definition,
            TownPlot plot,
            int worldSeed,
            int buildingId)
        {
            SelectionResolution resolution = ResolveRuntimeSelection(businessType, definition, plot);
            ExteriorPropRuntimeDiagnostics diagnostics = resolution.Diagnostics;
            SetAllManagedGroupsInactive();

            BuildingExteriorPropGroup group = resolution.SelectedGroup;
            if (group == null || group.groupRoot == null)
            {
                return diagnostics;
            }

            HashSet<Transform> managedGroupRoots = BuildManagedGroupRootSet();
            ActivateRequiredAncestorGroupRoots(group.groupRoot, managedGroupRoots);
            group.groupRoot.gameObject.SetActive(true);

            List<GameObject> props = CollectProps(group, managedGroupRoots, out _);
            if (props.Count == 0)
            {
                return diagnostics;
            }

            int activeCount = ResolveActiveCount(group, props.Count);
            List<ScoredProp> scored = new(props.Count);
            int plotId = plot != null ? plot.id : -1;
            int businessSalt = businessType.HasValue ? (int)businessType.Value : -1;
            int groupSalt = ResolveGroupSalt(group);
            for (int i = 0; i < props.Count; i++)
            {
                scored.Add(new ScoredProp(
                    props[i],
                    DeterministicUnit01(worldSeed, buildingId, plotId, businessSalt, groupSalt, i)));
            }

            scored.Sort((left, right) => left.Score.CompareTo(right.Score));
            for (int i = 0; i < scored.Count; i++)
            {
                GameObject prop = scored[i].Prop;
                if (prop == null)
                {
                    continue;
                }

                bool active = i < activeCount;
                prop.SetActive(active);
                if (active && disableManagedPropColliders)
                {
                    DisableColliders(prop);
                }
            }

            return diagnostics;
        }

        public ExteriorPropRuntimeDiagnostics BuildRuntimeDiagnostics(
            BusinessType? businessType,
            BuildingDefinition definition,
            TownPlot plot)
        {
            return ResolveRuntimeSelection(businessType, definition, plot).Diagnostics;
        }

        public bool TryBuildSelectionPreview(
            BusinessType? businessType,
            BuildingDefinition definition,
            TownPlot plot,
            out ExteriorPropSelectionPreview preview)
        {
            preview = default;
            SelectionContext context = BuildSelectionContext(businessType, definition, plot);
            return TryResolveBestGroup(context, out preview);
        }

        public string BuildSelectionSummary(BusinessType? businessType, BuildingDefinition definition, TownPlot plot)
        {
            ExteriorPropRuntimeDiagnostics diagnostics = BuildRuntimeDiagnostics(businessType, definition, plot);
            if (!diagnostics.HasSelection)
            {
                return string.IsNullOrWhiteSpace(diagnostics.NoSelectionDetail)
                    ? "Exterior dressing: authored groups present, but no matching group"
                    : $"Exterior dressing: {diagnostics.NoSelectionDetail}";
            }

            SelectionContext context = BuildSelectionContext(businessType, definition, plot);
            if (!diagnostics.HasSelection)
            {
                return !string.IsNullOrWhiteSpace(diagnostics.NoSelectionDetail)
                    ? $"Exterior dressing: {diagnostics.NoSelectionDetail}"
                    : $"Exterior dressing: authored groups present, but none match {BuildContextCoverageRead(context)}";
            }

            StringBuilder summary = new();
            summary.Append($"Exterior dressing: {diagnostics.SelectedGroupName}");
            if (!string.IsNullOrWhiteSpace(diagnostics.SelectionReason))
            {
                summary.Append(" | ");
                summary.Append(diagnostics.SelectionReason);
            }

            if (diagnostics.ManagedPropCount > 0)
            {
                summary.Append($" | {diagnostics.ActivePropCount}/{diagnostics.ManagedPropCount} props active");
            }
            else
            {
                summary.Append(" | authored root has no child props");
            }

            if (disableManagedPropColliders && diagnostics.ManagedPropCount > 0)
            {
                summary.Append(" | colliders off");
            }

            if (diagnostics.UsedArrayOrderTieBreak)
            {
                summary.Append($" | array-order tie break across {diagnostics.TopScoreTieCount} group(s)");
            }

            if (diagnostics.CollapsedSameRootMatchingGroupCount > 0)
            {
                summary.Append(diagnostics.CollapsedSameRootMatchingGroupCount == 1
                    ? " | collapsed same-root variant: 1"
                    : $" | collapsed same-root variants: {diagnostics.CollapsedSameRootMatchingGroupCount}");
            }

            return summary.ToString();
        }

        public void GetAuthoringIssues(List<BuildingExteriorIssue> results)
        {
            if (results == null || groups == null || groups.Length == 0)
            {
                return;
            }

            Dictionary<Transform, int> firstIndexByRoot = new();
            HashSet<Transform> managedGroupRoots = BuildManagedGroupRootSet();
            for (int i = 0; i < groups.Length; i++)
            {
                BuildingExteriorPropGroup group = groups[i];
                string label = GetGroupLabel(group, i);
                if (group == null)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.MissingGroupSlot,
                        detailKey = label,
                        groupName = label,
                        message = $"{label} is empty.",
                        isError = false
                    });
                    continue;
                }

                Transform root = group.groupRoot;
                if (root == null)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.MissingRoot,
                        detailKey = label,
                        groupName = label,
                        message = $"{label} has no group root assigned.",
                        isError = false
                    });
                    continue;
                }

                if (root == transform)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.PrefabRoot,
                        detailKey = label,
                        groupName = label,
                        message = $"{label} uses the prefab root as its group root, which would deactivate the whole building visual.",
                        isError = true
                    });
                    continue;
                }

                if (!root.IsChildOf(transform))
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.OutsideHierarchy,
                        detailKey = root.name,
                        groupName = label,
                        message = $"{label} points outside this building prefab hierarchy: '{root.name}'.",
                        isError = true
                    });
                    continue;
                }

                if (firstIndexByRoot.TryGetValue(root, out int firstIndex))
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.DuplicateRootReuse,
                        detailKey = BuildOverlapDetailKey(root),
                        groupName = label,
                        message = $"{label} reuses group root '{root.name}' already claimed by group slot {firstIndex + 1}.",
                        isError = false
                    });
                }
                else
                {
                    firstIndexByRoot.Add(root, i);
                }

                if (!GroupHasAnySelectionRoute(group))
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.NoSelectionRoute,
                        detailKey = label,
                        groupName = label,
                        message = $"{label} has no business or shell match route enabled, so it can never be selected.",
                        isError = false
                    });
                }

                List<GameObject> props = CollectProps(group, managedGroupRoots, out int nestedGroupRootSkips);
                if (props.Count == 0)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.EmptyManagedPropSet,
                        detailKey = BuildOverlapDetailKey(root),
                        groupName = label,
                        message = $"{label} does not manage any child props.",
                        isError = false
                    });
                }

                int ancestorManagedRoots = CountAncestorManagedGroupRoots(root, managedGroupRoots);
                if (ancestorManagedRoots > 0)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.NestedUnderManagedRoot,
                        detailKey = BuildOverlapDetailKey(root),
                        groupName = label,
                        message = $"{label} is nested under {ancestorManagedRoots} other managed group root(s); runtime will activate ancestor roots when this group wins.",
                        isError = false
                    });
                }

                if (nestedGroupRootSkips > 0)
                {
                    results.Add(new BuildingExteriorIssue
                    {
                        category = BuildingExteriorIssueCategory.ContainsNestedManagedRoots,
                        detailKey = BuildOverlapDetailKey(root),
                        groupName = label,
                        message = $"{label} contains {nestedGroupRootSkips} nested managed group root(s) that are skipped from its own prop list.",
                        isError = false
                    });
                }
            }
        }

        public void OnBeforeSerialize()
        {
            SyncBusinessToggleLists();
        }

        public void OnAfterDeserialize()
        {
        }

        private void OnValidate()
        {
            NormalizeGroups();
        }

        private void NormalizeGroups()
        {
            if (groups == null)
            {
                groups = Array.Empty<BuildingExteriorPropGroup>();
                return;
            }

            SyncBusinessToggleLists();
            for (int i = 0; i < groups.Length; i++)
            {
                BuildingExteriorPropGroup group = groups[i];
                if (group == null)
                {
                    continue;
                }

                group.selectionPriority = Mathf.Clamp(group.selectionPriority, -1000, 1000);
                group.activeRatio = Mathf.Clamp01(group.activeRatio);
                group.minimumActiveProps = Mathf.Max(0, group.minimumActiveProps);
                group.maximumActiveProps = Mathf.Max(0, group.maximumActiveProps);
                if (group.overrideMaximumActiveProps && group.maximumActiveProps < group.minimumActiveProps)
                {
                    group.maximumActiveProps = group.minimumActiveProps;
                }

                if (group.HasDedicatedAgriculturalRole)
                {
                    group.matchesAnyAgriculturalSite = false;
                }
            }
        }

        private bool TryResolveBestGroup(SelectionContext context, out ExteriorPropSelectionPreview preview)
        {
            preview = default;
            CandidateCollection collection = CollectCandidateMatches(context);
            if (collection.DistinctRootMatches == null || collection.DistinctRootMatches.Count == 0)
            {
                return false;
            }

            BuildingExteriorPropGroup bestGroup = null;
            ExteriorPropMatchKind bestKind = ExteriorPropMatchKind.None;
            int bestScore = int.MinValue;
            bool bestUpperFloorBoost = false;

            for (int i = 0; i < collection.DistinctRootMatches.Count; i++)
            {
                CandidateMatch candidate = collection.DistinctRootMatches[i];
                if (candidate.Score > bestScore)
                {
                    bestGroup = candidate.Group;
                    bestKind = candidate.MatchKind;
                    bestScore = candidate.Score;
                    bestUpperFloorBoost = candidate.UsesUpperFloorResidentialBoost;
                }
            }

            if (bestGroup == null || bestKind == ExteriorPropMatchKind.None)
            {
                return false;
            }

            preview = new ExteriorPropSelectionPreview(bestGroup, bestKind, bestScore, bestUpperFloorBoost);
            return true;
        }

        private SelectionResolution ResolveRuntimeSelection(
            BusinessType? businessType,
            BuildingDefinition definition,
            TownPlot plot)
        {
            SelectionContext context = BuildSelectionContext(businessType, definition, plot);
            ExteriorPropRuntimeDiagnostics diagnostics = new()
            {
                HasAuthoring = true
            };

            if (groups == null || groups.Length == 0)
            {
                diagnostics.NoSelectionCategory = "No authored groups";
                diagnostics.NoSelectionDetail = "no authored prop groups";
                return new SelectionResolution(null, diagnostics);
            }

            HashSet<Transform> managedGroupRoots = BuildManagedGroupRootSet();
            CandidateCollection collection = CollectCandidateMatches(context);
            List<CandidateMatch> matches = collection.DistinctRootMatches;
            diagnostics.EligibleGroupCount = matches.Count;
            diagnostics.BlockedMatchingGroupCount = collection.BlockedMatchingGroupCount;
            diagnostics.BlockedMissingRootCount = collection.BlockedMissingRootCount;
            diagnostics.BlockedPrefabRootCount = collection.BlockedPrefabRootCount;
            diagnostics.BlockedOutsideHierarchyRootCount = collection.BlockedOutsideHierarchyRootCount;
            diagnostics.CollapsedSameRootMatchingGroupCount = collection.CollapsedSameRootMatchingGroupCount;

            BuildingExteriorPropGroup bestGroup = null;
            ExteriorPropMatchKind bestKind = ExteriorPropMatchKind.None;
            int bestScore = int.MinValue;
            bool bestUpperFloorBoost = false;
            for (int i = 0; i < matches.Count; i++)
            {
                CandidateMatch candidate = matches[i];
                if (candidate.Score > bestScore)
                {
                    bestGroup = candidate.Group;
                    bestKind = candidate.MatchKind;
                    bestScore = candidate.Score;
                    bestUpperFloorBoost = candidate.UsesUpperFloorResidentialBoost;
                }
            }

            if (bestGroup == null || bestKind == ExteriorPropMatchKind.None)
            {
                if (collection.BlockedMatchingGroupCount > 0)
                {
                    diagnostics.NoSelectionCategory = BuildBlockedMatchNoSelectionCategory(context);
                    diagnostics.NoSelectionDetail = BuildBlockedMatchNoSelectionDetail(
                        collection.BlockedMatchingGroupCount,
                        collection.BlockedMissingRootCount,
                        collection.BlockedPrefabRootCount,
                        collection.BlockedOutsideHierarchyRootCount);
                }
                else
                {
                    diagnostics.NoSelectionCategory = BuildNoSelectionCategory(context);
                    diagnostics.NoSelectionDetail = BuildNoSelectionDetail(context);
                }

                return new SelectionResolution(null, diagnostics);
            }

            List<CandidateMatch> topScoreMatches = new();
            for (int i = 0; i < matches.Count; i++)
            {
                if (matches[i].Score == bestScore)
                {
                    topScoreMatches.Add(matches[i]);
                }
            }

            diagnostics.HasSelection = true;
            diagnostics.SelectedGroupName = bestGroup.GetDisplayName();
            diagnostics.SelectionRouteCategory = ClassifySelectionRoute(bestKind);
            diagnostics.SelectionRouteDetail = FormatMatchKind(bestKind, businessType, plot);
            diagnostics.SelectionReason = diagnostics.SelectionRouteDetail;
            if (bestUpperFloorBoost)
            {
                diagnostics.SelectionReason += " | upper-floor household accent";
            }

            diagnostics.TopScoreTieCount = topScoreMatches.Count;
            diagnostics.TopScoreTieGroupSummary = FormatCandidateGroupSummary(topScoreMatches);
            diagnostics.UsedArrayOrderTieBreak = topScoreMatches.Count > 1;
            diagnostics.SelectionCompetitionCategory = BuildCompetitionCategory(matches.Count, topScoreMatches.Count);
            diagnostics.SelectionCompetitionDetail = BuildCompetitionDetail(diagnostics.SelectedGroupName, matches.Count, topScoreMatches.Count, diagnostics.TopScoreTieGroupSummary);
            diagnostics.SelectionCompetitionSeverity = BuildCompetitionSeverity(matches.Count, topScoreMatches.Count);
            diagnostics.UsedFallback = bestKind == ExteriorPropMatchKind.AgriculturalGeneric || bestKind == ExteriorPropMatchKind.CommercialGeneric;

            List<GameObject> managedProps = CollectProps(bestGroup, managedGroupRoots, out int nestedGroupRootSkips);
            diagnostics.ManagedPropCount = managedProps.Count;
            diagnostics.ActivePropCount = ResolveActiveCount(bestGroup, managedProps.Count);
            diagnostics.NestedGroupRootSkips = nestedGroupRootSkips;
            diagnostics.AncestorGroupRootsActivated = CountAncestorManagedGroupRoots(bestGroup.groupRoot, managedGroupRoots);
            diagnostics.CollidersDisabled = disableManagedPropColliders && diagnostics.ActivePropCount > 0;

            return new SelectionResolution(bestGroup, diagnostics);
        }

        private CandidateCollection CollectCandidateMatches(SelectionContext context)
        {
            if (groups == null || groups.Length == 0)
            {
                return new CandidateCollection(new List<CandidateMatch>(), 0, 0, 0, 0, 0);
            }

            Dictionary<Transform, CandidateMatch> bestMatchByRoot = new();
            int blockedMatchCount = 0;
            int blockedMissingRootCount = 0;
            int blockedPrefabRootCount = 0;
            int blockedOutsideHierarchyCount = 0;
            int collapsedSameRootMatchingGroupCount = 0;

            for (int i = 0; i < groups.Length; i++)
            {
                BuildingExteriorPropGroup candidate = groups[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!TryGetMatchScore(candidate, context, out int score, out ExteriorPropMatchKind matchKind, out bool usesUpperFloorResidentialBoost))
                {
                    continue;
                }

                if (!IsUsableGroupRoot(candidate.groupRoot))
                {
                    blockedMatchCount++;
                    switch (ClassifyUnusableGroupRoot(candidate.groupRoot))
                    {
                        case UnusableGroupRootKind.Missing:
                            blockedMissingRootCount++;
                            break;
                        case UnusableGroupRootKind.PrefabRoot:
                            blockedPrefabRootCount++;
                            break;
                        case UnusableGroupRootKind.OutsideHierarchy:
                            blockedOutsideHierarchyCount++;
                            break;
                    }

                    continue;
                }

                CandidateMatch candidateMatch = new(candidate, score, matchKind, usesUpperFloorResidentialBoost, i);
                if (bestMatchByRoot.TryGetValue(candidate.groupRoot, out CandidateMatch existingMatch))
                {
                    collapsedSameRootMatchingGroupCount++;
                    // Only one distinct visual root can win at runtime, so same-root variants compete locally first.
                    if (candidateMatch.Score > existingMatch.Score)
                    {
                        bestMatchByRoot[candidate.groupRoot] = candidateMatch;
                    }

                    continue;
                }

                bestMatchByRoot.Add(candidate.groupRoot, candidateMatch);
            }

            List<CandidateMatch> distinctRootMatches = new(bestMatchByRoot.Values);
            distinctRootMatches.Sort((left, right) => left.SourceIndex.CompareTo(right.SourceIndex));
            return new CandidateCollection(
                distinctRootMatches,
                blockedMatchCount,
                blockedMissingRootCount,
                blockedPrefabRootCount,
                blockedOutsideHierarchyCount,
                collapsedSameRootMatchingGroupCount);
        }

        private static bool TryGetMatchScore(
            BuildingExteriorPropGroup group,
            SelectionContext context,
            out int score,
            out ExteriorPropMatchKind matchKind,
            out bool usesUpperFloorResidentialBoost)
        {
            score = int.MinValue;
            matchKind = ExteriorPropMatchKind.None;
            usesUpperFloorResidentialBoost = false;
            if (group == null)
            {
                return false;
            }

            if (context.AgriculturalSiteRole != AgriculturalSiteRole.None
                && group.HasDedicatedAgriculturalRole
                && !group.MatchesAgriculturalSiteRole(context.AgriculturalSiteRole))
            {
                // Dedicated agricultural art should never silently bleed across yard roles.
                return false;
            }

            int priority = Mathf.Clamp(group.selectionPriority, -1000, 1000);
            int bestBaseScore = int.MinValue;
            ExteriorPropMatchKind bestKind = ExteriorPropMatchKind.None;

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, BusinessExactScore, ExteriorPropMatchKind.BusinessExact,
                context.BusinessType.HasValue && group.MatchesBusiness(context.BusinessType.Value));

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, AgriculturalExactScore, ExteriorPropMatchKind.AgriculturalExact,
                context.AgriculturalSiteRole != AgriculturalSiteRole.None && group.MatchesAgriculturalSiteRole(context.AgriculturalSiteRole));

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, AgriculturalGenericScore, ExteriorPropMatchKind.AgriculturalGeneric,
                context.AgriculturalSiteRole != AgriculturalSiteRole.None && !group.HasDedicatedAgriculturalRole && group.matchesAnyAgriculturalSite);

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, MixedUseScore, ExteriorPropMatchKind.MixedUse,
                context.IsMixedUse && group.matchesMixedUse);

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, ResidentialScore, ExteriorPropMatchKind.Residential,
                context.IsResidential && !context.IsCommercial && group.matchesResidential);

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, CivicScore, ExteriorPropMatchKind.Civic,
                context.IsCivic && group.matchesCivic);

            PromoteBestMatch(ref bestBaseScore, ref bestKind, group, context, CommercialGenericScore, ExteriorPropMatchKind.CommercialGeneric,
                context.IsCommercial && group.matchesAnyCommercialBusiness);

            if (bestKind == ExteriorPropMatchKind.None)
            {
                return false;
            }

            score = bestBaseScore + priority;
            if (context.UsesUpperFloorResidential && group.matchesUpperFloorResidential)
            {
                score += UpperFloorResidentialBonus;
                usesUpperFloorResidentialBoost = true;
            }

            matchKind = bestKind;
            return true;
        }

        private static void PromoteBestMatch(
            ref int bestBaseScore,
            ref ExteriorPropMatchKind bestKind,
            BuildingExteriorPropGroup group,
            SelectionContext context,
            int score,
            ExteriorPropMatchKind kind,
            bool condition)
        {
            if (!condition)
            {
                return;
            }

            if (score > bestBaseScore)
            {
                bestBaseScore = score;
                bestKind = kind;
            }
        }

        private static SelectionContext BuildSelectionContext(BusinessType? businessType, BuildingDefinition definition, TownPlot plot)
        {
            bool commercial = businessType.HasValue
                || (definition != null && (definition.CanHostWorkplace || definition.PrimaryUse == BuildingUseType.Commercial))
                || (plot != null && (plot.zone == PlotZone.Business || plot.zone == PlotZone.MixedUse || plot.zone == PlotZone.Agricultural));
            bool residential = IsResidentialUse(definition, plot);
            bool civic = (definition != null && definition.PrimaryUse == BuildingUseType.Civic)
                || (plot != null && plot.publicSiteRole != PublicSiteRole.None);
            bool mixedUse = (definition != null && definition.IsMixedUse)
                || (plot != null && plot.zone == PlotZone.MixedUse && commercial && residential);
            AgriculturalSiteRole agriculturalSiteRole = plot != null && plot.agriculturalSiteRole != AgriculturalSiteRole.None
                ? plot.agriculturalSiteRole
                : definition != null ? definition.AgriculturalSiteRole : AgriculturalSiteRole.None;
            bool usesUpperFloorResidential = definition != null && definition.UsesUpperFloorResidential;

            return new SelectionContext(
                businessType,
                agriculturalSiteRole,
                commercial,
                residential,
                civic,
                mixedUse,
                usesUpperFloorResidential);
        }

        private static string BuildContextCoverageRead(SelectionContext context)
        {
            if (context.AgriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return $"the current {FormatAgriculturalRole(context.AgriculturalSiteRole)}";
            }

            if (context.BusinessType.HasValue)
            {
                return $"{context.BusinessType.Value} use";
            }

            if (context.IsCivic)
            {
                return "civic use";
            }

            if (context.IsMixedUse)
            {
                return "mixed-use frontage";
            }

            if (context.IsResidential)
            {
                return "residential frontage";
            }

            if (context.IsCommercial)
            {
                return "commercial frontage";
            }

            return "the current shell context";
        }

        private void SetAllManagedGroupsInactive()
        {
            if (groups == null)
            {
                return;
            }

            HashSet<Transform> managedGroupRoots = BuildManagedGroupRootSet();
            for (int i = 0; i < groups.Length; i++)
            {
                BuildingExteriorPropGroup group = groups[i];
                if (group == null || !IsUsableGroupRoot(group.groupRoot))
                {
                    continue;
                }

                group.groupRoot.gameObject.SetActive(false);

                List<GameObject> props = CollectProps(group, managedGroupRoots, out _);
                for (int propIndex = 0; propIndex < props.Count; propIndex++)
                {
                    GameObject prop = props[propIndex];
                    if (prop != null)
                    {
                        prop.SetActive(false);
                    }
                }
            }
        }

        private static List<GameObject> CollectProps(BuildingExteriorPropGroup group, HashSet<Transform> managedGroupRoots, out int nestedGroupRootSkips)
        {
            List<GameObject> props = new();
            nestedGroupRootSkips = 0;
            if (group?.groupRoot == null)
            {
                return props;
            }

            if (group.includeNestedChildren)
            {
                CollectChildPropsRecursive(group.groupRoot, group.groupRoot, managedGroupRoots, props, includeRoot: false, ref nestedGroupRootSkips);
            }
            else
            {
                for (int i = 0; i < group.groupRoot.childCount; i++)
                {
                    Transform child = group.groupRoot.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    if (managedGroupRoots != null && managedGroupRoots.Contains(child))
                    {
                        nestedGroupRootSkips++;
                        continue;
                    }

                    props.Add(child.gameObject);
                }
            }

            return props;
        }

        private static void CollectChildPropsRecursive(Transform current, Transform selectedRoot, HashSet<Transform> managedGroupRoots, List<GameObject> props, bool includeRoot, ref int nestedGroupRootSkips)
        {
            if (current == null)
            {
                return;
            }

            if (includeRoot)
            {
                if (current != selectedRoot && managedGroupRoots != null && managedGroupRoots.Contains(current))
                {
                    nestedGroupRootSkips++;
                    return;
                }

                props.Add(current.gameObject);
            }

            for (int i = 0; i < current.childCount; i++)
            {
                Transform child = current.GetChild(i);
                CollectChildPropsRecursive(child, selectedRoot, managedGroupRoots, props, includeRoot: true, ref nestedGroupRootSkips);
            }
        }

        private HashSet<Transform> BuildManagedGroupRootSet()
        {
            HashSet<Transform> roots = new();
            if (groups == null)
            {
                return roots;
            }

            for (int i = 0; i < groups.Length; i++)
            {
                BuildingExteriorPropGroup group = groups[i];
                if (group != null && IsUsableGroupRoot(group.groupRoot))
                {
                    roots.Add(group.groupRoot);
                }
            }

            return roots;
        }

        private bool IsUsableGroupRoot(Transform groupRoot)
        {
            if (groupRoot == null || groupRoot == transform)
            {
                return false;
            }

            return groupRoot.IsChildOf(transform);
        }

        private UnusableGroupRootKind ClassifyUnusableGroupRoot(Transform groupRoot)
        {
            if (groupRoot == null)
            {
                return UnusableGroupRootKind.Missing;
            }

            if (groupRoot == transform)
            {
                return UnusableGroupRootKind.PrefabRoot;
            }

            return groupRoot.IsChildOf(transform)
                ? UnusableGroupRootKind.None
                : UnusableGroupRootKind.OutsideHierarchy;
        }


        private static int CountAncestorManagedGroupRoots(Transform groupRoot, HashSet<Transform> managedGroupRoots)
        {
            if (groupRoot == null || managedGroupRoots == null || managedGroupRoots.Count == 0)
            {
                return 0;
            }

            int count = 0;
            Transform current = groupRoot.parent;
            while (current != null)
            {
                if (managedGroupRoots.Contains(current))
                {
                    count++;
                }

                current = current.parent;
            }

            return count;
        }

        private static int ActivateRequiredAncestorGroupRoots(Transform groupRoot, HashSet<Transform> managedGroupRoots)
        {
            if (groupRoot == null || managedGroupRoots == null || managedGroupRoots.Count == 0)
            {
                return 0;
            }

            int count = 0;
            Transform current = groupRoot.parent;
            while (current != null)
            {
                if (managedGroupRoots.Contains(current))
                {
                    current.gameObject.SetActive(true);
                    count++;
                }

                current = current.parent;
            }

            return count;
        }

        private static int ResolveActiveCount(BuildingExteriorPropGroup group, int propCount)
        {
            if (group == null || propCount <= 0)
            {
                return 0;
            }

            int activeCount = Mathf.RoundToInt(propCount * Mathf.Clamp01(group.activeRatio));
            activeCount = Mathf.Max(activeCount, Mathf.Min(propCount, Mathf.Max(0, group.minimumActiveProps)));
            if (group.overrideMaximumActiveProps)
            {
                activeCount = Mathf.Min(activeCount, Mathf.Clamp(group.maximumActiveProps, 0, propCount));
            }

            return Mathf.Clamp(activeCount, 0, propCount);
        }

        private void SyncBusinessToggleLists()
        {
            if (groups == null)
            {
                return;
            }

            for (int i = 0; i < groups.Length; i++)
            {
                SyncBusinessToggleList(groups[i]);
            }
        }

        private static void SyncBusinessToggleList(BuildingExteriorPropGroup group)
        {
            if (group == null)
            {
                return;
            }

            BusinessType[] values = (BusinessType[])Enum.GetValues(typeof(BusinessType));
            Dictionary<BusinessType, bool> existing = new();
            if (group.businessTypeToggles != null)
            {
                for (int i = 0; i < group.businessTypeToggles.Length; i++)
                {
                    BuildingExteriorBusinessToggle toggle = group.businessTypeToggles[i];
                    if (toggle != null)
                    {
                        existing[toggle.businessType] = toggle.enabled;
                    }
                }
            }

            BuildingExteriorBusinessToggle[] synced = new BuildingExteriorBusinessToggle[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                BusinessType value = values[i];
                synced[i] = new BuildingExteriorBusinessToggle
                {
                    businessType = value,
                    enabled = existing.TryGetValue(value, out bool isEnabled) && isEnabled
                };
            }

            group.businessTypeToggles = synced;
        }

        private string BuildOverlapDetailKey(Transform groupRoot)
        {
            if (groupRoot == null)
            {
                return string.Empty;
            }

            if (groupRoot == transform)
            {
                return "__prefab_root__";
            }

            if (!groupRoot.IsChildOf(transform))
            {
                return $"__external__:{groupRoot.name}";
            }

            List<string> segments = new();
            Transform current = groupRoot;
            while (current != null && current != transform)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }


        private static string GetGroupLabel(BuildingExteriorPropGroup group, int index)
        {
            return group != null
                ? $"Exterior group {index + 1} ('{group.GetDisplayName()}')"
                : $"Exterior group slot {index + 1}";
        }

        private static bool GroupHasAnySelectionRoute(BuildingExteriorPropGroup group)
        {
            if (group == null)
            {
                return false;
            }

            if (group.HasDedicatedAgriculturalRole
                || group.matchesAnyCommercialBusiness
                || group.matchesAnyAgriculturalSite
                || group.matchesMixedUse
                || group.matchesResidential
                || group.matchesCivic)
            {
                return true;
            }

            if (group.businessTypeToggles == null)
            {
                return false;
            }

            for (int i = 0; i < group.businessTypeToggles.Length; i++)
            {
                BuildingExteriorBusinessToggle toggle = group.businessTypeToggles[i];
                if (toggle != null && toggle.enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsResidentialUse(BuildingDefinition definition, TownPlot plot)
        {
            return (definition != null && (definition.PrimaryUse == BuildingUseType.Residential || definition.CanHostHouseholds))
                || (plot != null && plot.zone == PlotZone.Residential);
        }

        private static string BuildNoSelectionCategory(SelectionContext context)
        {
            if (context.AgriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return "No agricultural exterior match";
            }

            if (context.BusinessType.HasValue)
            {
                return $"No {context.BusinessType.Value} exterior match";
            }

            if (context.IsMixedUse)
            {
                return "No mixed-use exterior match";
            }

            if (context.IsResidential)
            {
                return "No residential exterior match";
            }

            if (context.IsCivic)
            {
                return "No civic exterior match";
            }

            if (context.IsCommercial)
            {
                return "No commercial exterior match";
            }

            return "No exterior match";
        }

        private static string BuildNoSelectionDetail(SelectionContext context)
        {
            if (context.AgriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return "authored groups present, but no yard-role or generic agricultural group matches this site";
            }

            if (context.BusinessType.HasValue)
            {
                return $"authored groups present, but nothing matches {context.BusinessType.Value} or a broader shell fallback";
            }

            if (context.IsMixedUse)
            {
                return "authored groups present, but nothing matches this mixed-use shell read";
            }

            if (context.IsResidential)
            {
                return "authored groups present, but nothing matches this residential read";
            }

            if (context.IsCivic)
            {
                return "authored groups present, but nothing matches this civic read";
            }

            if (context.IsCommercial)
            {
                return "authored groups present, but nothing matches this commercial read";
            }

            return "authored groups present, but no matching group qualifies";
        }

        private static string BuildBlockedMatchNoSelectionCategory(SelectionContext context)
        {
            if (context.AgriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return "No usable agricultural exterior match";
            }

            if (context.BusinessType.HasValue)
            {
                return $"No usable {context.BusinessType.Value} exterior match";
            }

            if (context.IsMixedUse)
            {
                return "No usable mixed-use exterior match";
            }

            if (context.IsResidential)
            {
                return "No usable residential exterior match";
            }

            if (context.IsCivic)
            {
                return "No usable civic exterior match";
            }

            if (context.IsCommercial)
            {
                return "No usable commercial exterior match";
            }

            return "No usable exterior match";
        }

        private static string BuildBlockedMatchNoSelectionDetail(
            int blockedMatchCount,
            int missingRootCount,
            int prefabRootCount,
            int outsideHierarchyCount)
        {
            if (blockedMatchCount <= 0)
            {
                return "authored groups present, but no usable matching group qualifies";
            }

            string reasonSummary = BuildBlockedMatchReasonSummary(missingRootCount, prefabRootCount, outsideHierarchyCount);
            return blockedMatchCount == 1
                ? $"authored groups present, but no usable match remains; 1 matching group was skipped because {reasonSummary}"
                : $"authored groups present, but no usable match remains; {blockedMatchCount} matching groups were skipped because {reasonSummary}";
        }

        private static string BuildBlockedMatchReasonSummary(
            int missingRootCount,
            int prefabRootCount,
            int outsideHierarchyCount)
        {
            List<string> reasons = new();
            if (missingRootCount > 0)
            {
                reasons.Add(missingRootCount == 1 ? "its group root is missing" : $"{missingRootCount} group roots are missing");
            }

            if (prefabRootCount > 0)
            {
                reasons.Add(prefabRootCount == 1 ? "its group root uses the prefab root" : $"{prefabRootCount} group roots use the prefab root");
            }

            if (outsideHierarchyCount > 0)
            {
                reasons.Add(outsideHierarchyCount == 1 ? "its group root points outside this prefab hierarchy" : $"{outsideHierarchyCount} group roots point outside this prefab hierarchy");
            }

            if (reasons.Count == 0)
            {
                return "their group roots are unusable";
            }

            if (reasons.Count == 1)
            {
                return reasons[0];
            }

            return string.Join(", ", reasons);
        }


        private static string ClassifySelectionRoute(ExteriorPropMatchKind matchKind)
        {
            return matchKind switch
            {
                ExteriorPropMatchKind.BusinessExact => "Exact business route",
                ExteriorPropMatchKind.AgriculturalExact => "Dedicated agricultural route",
                ExteriorPropMatchKind.AgriculturalGeneric => "Agricultural fallback route",
                ExteriorPropMatchKind.MixedUse => "Mixed-use shell route",
                ExteriorPropMatchKind.Residential => "Residential shell route",
                ExteriorPropMatchKind.Civic => "Civic shell route",
                ExteriorPropMatchKind.CommercialGeneric => "Commercial fallback route",
                _ => "No route"
            };
        }

        private static string BuildCompetitionCategory(int eligibleGroupCount, int topScoreTieCount)
        {
            if (topScoreTieCount > 1)
            {
                return "Tie";
            }

            if (eligibleGroupCount > 1)
            {
                return "Competitive";
            }

            return eligibleGroupCount == 1 ? "Uncontested" : string.Empty;
        }

        private static string BuildCompetitionDetail(string selectedGroupName, int eligibleGroupCount, int topScoreTieCount, string topScoreTieGroupSummary)
        {
            if (topScoreTieCount > 1)
            {
                return $"{topScoreTieCount} groups tied on score ({topScoreTieGroupSummary}); array order picks {selectedGroupName}";
            }

            if (eligibleGroupCount > 1)
            {
                return $"{selectedGroupName} beat {eligibleGroupCount - 1} other eligible group(s)";
            }

            if (eligibleGroupCount == 1)
            {
                return $"{selectedGroupName} is the only eligible group";
            }

            return string.Empty;
        }

        private static string BuildCompetitionSeverity(int eligibleGroupCount, int topScoreTieCount)
        {
            if (topScoreTieCount >= 3)
            {
                return "high";
            }

            if (topScoreTieCount == 2)
            {
                return "moderate";
            }

            if (eligibleGroupCount > 1)
            {
                return "low";
            }

            return "none";
        }

        private static string FormatCandidateGroupSummary(List<CandidateMatch> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            int visibleCount = Mathf.Min(matches.Count, 4);
            for (int i = 0; i < visibleCount; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(matches[i].Group != null ? matches[i].Group.GetDisplayName() : "Unnamed Group");
            }

            if (matches.Count > visibleCount)
            {
                builder.Append($", +{matches.Count - visibleCount} more");
            }

            return builder.ToString();
        }

        private static string FormatMatchKind(ExteriorPropMatchKind matchKind, BusinessType? businessType, TownPlot plot)
        {
            return matchKind switch
            {
                ExteriorPropMatchKind.BusinessExact => businessType.HasValue
                    ? $"business-specific {businessType.Value} group"
                    : "business-specific group",
                ExteriorPropMatchKind.AgriculturalExact => plot != null && plot.agriculturalSiteRole != AgriculturalSiteRole.None
                    ? $"dedicated {FormatAgriculturalRole(plot.agriculturalSiteRole)} group"
                    : "dedicated agricultural yard group",
                ExteriorPropMatchKind.AgriculturalGeneric => "generic agricultural yard fallback",
                ExteriorPropMatchKind.MixedUse => "mixed-use storefront group",
                ExteriorPropMatchKind.Residential => "residential group",
                ExteriorPropMatchKind.Civic => "civic group",
                ExteriorPropMatchKind.CommercialGeneric => "generic commercial fallback",
                _ => "no matching group"
            };
        }

        private static string FormatAgriculturalRole(AgriculturalSiteRole role)
        {
            return role switch
            {
                AgriculturalSiteRole.CropProductionYard => "crop-yard",
                AgriculturalSiteRole.LivestockYard => "livestock-yard",
                AgriculturalSiteRole.SawmillYard => "sawmill-yard",
                _ => "working-yard"
            };
        }

        private static int ResolveGroupSalt(BuildingExteriorPropGroup group)
        {
            if (group == null)
            {
                return 0;
            }

            unchecked
            {
                int salt = 17;
                string name = group.GetDisplayName();
                salt = (salt * 31) + (name != null ? name.GetHashCode() : 0);
                salt = (salt * 31) + group.selectionPriority;
                salt = (salt * 31) + (group.matchesAnyCommercialBusiness ? 1 : 0);
                salt = (salt * 31) + (group.matchesAnyAgriculturalSite ? 1 : 0);
                salt = (salt * 31) + (int)group.agriculturalSiteRole;
                salt = (salt * 31) + (group.matchesMixedUse ? 1 : 0);
                salt = (salt * 31) + (group.matchesResidential ? 1 : 0);
                salt = (salt * 31) + (group.matchesUpperFloorResidential ? 1 : 0);
                salt = (salt * 31) + (group.matchesCivic ? 1 : 0);
                if (group.businessTypeToggles != null)
                {
                    for (int i = 0; i < group.businessTypeToggles.Length; i++)
                    {
                        BuildingExteriorBusinessToggle toggle = group.businessTypeToggles[i];
                        if (toggle == null || !toggle.enabled)
                        {
                            continue;
                        }

                        salt = (salt * 31) + ((int)toggle.businessType + 1) * 977;
                    }
                }

                return salt;
            }
        }

        private static float DeterministicUnit01(int worldSeed, int buildingId, int plotId, int businessSalt, int groupSalt, int propIndex)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)worldSeed) * 16777619u;
                hash = (hash ^ (uint)buildingId) * 16777619u;
                hash = (hash ^ (uint)plotId) * 16777619u;
                hash = (hash ^ (uint)businessSalt) * 16777619u;
                hash = (hash ^ (uint)groupSalt) * 16777619u;
                hash = (hash ^ (uint)propIndex) * 16777619u;
                return (hash & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static void DisableColliders(GameObject prop)
        {
            Collider[] colliders = prop.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private readonly struct SelectionResolution
        {
            public SelectionResolution(BuildingExteriorPropGroup selectedGroup, ExteriorPropRuntimeDiagnostics diagnostics)
            {
                SelectedGroup = selectedGroup;
                Diagnostics = diagnostics;
            }

            public BuildingExteriorPropGroup SelectedGroup { get; }
            public ExteriorPropRuntimeDiagnostics Diagnostics { get; }
        }

        private readonly struct CandidateMatch
        {
            public CandidateMatch(
                BuildingExteriorPropGroup group,
                int score,
                ExteriorPropMatchKind matchKind,
                bool usesUpperFloorResidentialBoost,
                int sourceIndex)
            {
                Group = group;
                Score = score;
                MatchKind = matchKind;
                UsesUpperFloorResidentialBoost = usesUpperFloorResidentialBoost;
                SourceIndex = sourceIndex;
            }

            public BuildingExteriorPropGroup Group { get; }
            public int Score { get; }
            public ExteriorPropMatchKind MatchKind { get; }
            public bool UsesUpperFloorResidentialBoost { get; }
            public int SourceIndex { get; }
        }

        private readonly struct CandidateCollection
        {
            public CandidateCollection(
                List<CandidateMatch> distinctRootMatches,
                int blockedMatchingGroupCount,
                int blockedMissingRootCount,
                int blockedPrefabRootCount,
                int blockedOutsideHierarchyRootCount,
                int collapsedSameRootMatchingGroupCount)
            {
                DistinctRootMatches = distinctRootMatches;
                BlockedMatchingGroupCount = blockedMatchingGroupCount;
                BlockedMissingRootCount = blockedMissingRootCount;
                BlockedPrefabRootCount = blockedPrefabRootCount;
                BlockedOutsideHierarchyRootCount = blockedOutsideHierarchyRootCount;
                CollapsedSameRootMatchingGroupCount = collapsedSameRootMatchingGroupCount;
            }

            public List<CandidateMatch> DistinctRootMatches { get; }
            public int BlockedMatchingGroupCount { get; }
            public int BlockedMissingRootCount { get; }
            public int BlockedPrefabRootCount { get; }
            public int BlockedOutsideHierarchyRootCount { get; }
            public int CollapsedSameRootMatchingGroupCount { get; }
        }

        private enum UnusableGroupRootKind
        {
            None = 0,
            Missing = 1,
            PrefabRoot = 2,
            OutsideHierarchy = 3
        }

        private readonly struct SelectionContext
        {
            public SelectionContext(
                BusinessType? businessType,
                AgriculturalSiteRole agriculturalSiteRole,
                bool isCommercial,
                bool isResidential,
                bool isCivic,
                bool isMixedUse,
                bool usesUpperFloorResidential)
            {
                BusinessType = businessType;
                AgriculturalSiteRole = agriculturalSiteRole;
                IsCommercial = isCommercial;
                IsResidential = isResidential;
                IsCivic = isCivic;
                IsMixedUse = isMixedUse;
                UsesUpperFloorResidential = usesUpperFloorResidential;
            }

            public BusinessType? BusinessType { get; }
            public AgriculturalSiteRole AgriculturalSiteRole { get; }
            public bool IsCommercial { get; }
            public bool IsResidential { get; }
            public bool IsCivic { get; }
            public bool IsMixedUse { get; }
            public bool UsesUpperFloorResidential { get; }
        }

        private readonly struct ScoredProp
        {
            public ScoredProp(GameObject prop, float score)
            {
                Prop = prop;
                Score = score;
            }

            public readonly GameObject Prop;
            public readonly float Score;
        }
    }
}
