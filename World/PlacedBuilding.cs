using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World
{
    [Serializable]
    public struct BuildingAnchor
    {
        public AnchorType type;
        public GridCoord coord;
        public bool fromAuthoredMarker;
        public bool fromFallbackRule;
        public string source;
        [NonSerialized]
        public bool hasAuthoredWorldPosition;
        [NonSerialized]
        public Vector3 authoredWorldPosition;
        [NonSerialized]
        public bool wasReconciledToExteriorAccess;
        [NonSerialized]
        public GridCoord authoredCoordBeforeReconciliation;
        [NonSerialized]
        public string reconciliationNote;
        [NonSerialized]
        public bool hasSupplementalRoadAccessCoord;
        [NonSerialized]
        public GridCoord supplementalRoadAccessCoord;
        [NonSerialized]
        public string supplementalRoadAccessSource;
    }

    [Serializable]
    public struct BuildingAnchorIssue
    {
        public AnchorType type;
        public string message;
        public bool isError;
    }

    public enum BuildingExteriorIssueCategory
    {
        None = 0,
        MissingGroupSlot = 1,
        MissingRoot = 2,
        PrefabRoot = 3,
        OutsideHierarchy = 4,
        DuplicateRootReuse = 5,
        NoSelectionRoute = 6,
        EmptyManagedPropSet = 7,
        NestedUnderManagedRoot = 8,
        ContainsNestedManagedRoots = 9
    }

    [Serializable]
    public struct BuildingExteriorIssue
    {
        public BuildingExteriorIssueCategory category;
        public string detailKey;
        public string groupName;
        public string message;
        public bool isError;
    }

    [Serializable]
    public sealed class PlacedBuilding
    {
        public int id;
        public int plotId;
        public BuildingDefinition definition;
        public GridRect footprint;
        public Vector2Int siteSizeCells;
        public Vector2Int intendedFootprintSizeCells;
        [NonSerialized]
        public Vector2Int definitionFootprintSizeCells;
        [NonSerialized]
        public Vector2Int footprintAuthorityMinimumSizeCells;
        [NonSerialized]
        public Vector2Int resolvedPlacementFootprintSizeCells;
        [NonSerialized]
        public bool hasPrefabFootprintAuthority;
        [NonSerialized]
        public bool footprintExpandedByAuthority;
        [NonSerialized]
        public bool loadedFromSave;
        [NonSerialized]
        public GridRect savedFootprintBeforeReconciliation;
        [NonSerialized]
        public Vector2Int savedIntendedFootprintSizeCells;
        [NonSerialized]
        public int savedAnchorCount;
        [NonSerialized]
        public bool footprintMatchedCurrentAuthorityOnLoad;
        [NonSerialized]
        public bool footprintRebuiltFromCurrentAuthorityOnLoad;
        [NonSerialized]
        public bool savedFootprintPreservedAfterAuthorityMismatch;
        [NonSerialized]
        public bool savedAnchorsRegeneratedOnLoad;
        [NonSerialized]
        public bool savedAnchorsPreservedOnLoad;
        [NonSerialized]
        public string footprintReconciliationSource;
        [NonSerialized]
        public string footprintReconciliationNote;
        [NonSerialized]
        public string anchorReconciliationNote;
        public GridDirection frontageDirection;
        public PublicSiteRole publicSiteRole = PublicSiteRole.None;
        public bool playerOwned;
        public BuildingDoorAnimator doorAnimator;
        // Exterior authoring diagnostics are runtime inspection fields, not authored source data. They are filled by
        // TownWorldController after authoring activation so world inspection can explain what really happened.
        public bool hasExteriorPropAuthoring;
        public string exteriorPropSelectedGroupName;
        public string exteriorPropSelectionReason;
        public int exteriorPropEligibleGroupCount;
        public int exteriorPropManagedPropCount;
        public int exteriorPropActivePropCount;
        public int exteriorPropNestedGroupRootSkips;
        public int exteriorPropAncestorGroupRootsActivated;
        public bool exteriorPropSelectionUsedArrayOrderTieBreak;
        public int exteriorPropTopScoreTieCount;
        public string exteriorPropTopScoreTieGroupSummary;
        public string exteriorPropNoSelectionCategory;
        public string exteriorPropNoSelectionDetail;
        public string exteriorPropSelectionRouteCategory;
        public string exteriorPropSelectionRouteDetail;
        public string exteriorPropSelectionCompetitionCategory;
        public string exteriorPropSelectionCompetitionDetail;
        public string exteriorPropSelectionCompetitionSeverity;
        public int exteriorPropBlockedMatchingGroupCount;
        public int exteriorPropBlockedMissingRootCount;
        public int exteriorPropBlockedPrefabRootCount;
        public int exteriorPropBlockedOutsideHierarchyRootCount;
        public int exteriorPropCollapsedSameRootMatchingGroupCount;
        public bool exteriorPropUsedFallback;
        public bool exteriorPropCollidersDisabled;
        public readonly List<BuildingAnchor> anchors = new();
        public readonly List<BuildingAnchorIssue> anchorIssues = new();
        public readonly List<BuildingExteriorIssue> exteriorPropIssues = new();

        public bool TryGetAnchor(AnchorType type, out BuildingAnchor anchor)
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (anchors[i].type == type)
                {
                    anchor = anchors[i];
                    return true;
                }
            }

            anchor = default;
            return false;
        }

        public bool TryGetRoadAccessAnchor(out BuildingAnchor anchor)
        {
            if (TryGetAnchor(AnchorType.FrontDoor, out anchor))
            {
                return true;
            }

            if (TryGetAnchor(AnchorType.DropOff, out anchor))
            {
                return true;
            }

            if (TryGetAnchor(AnchorType.Service, out anchor))
            {
                return true;
            }

            anchor = default;
            return false;
        }

        public bool TryGetRoadAccessCoord(out GridCoord coord)
        {
            if (TryGetRoadAccessAnchor(out BuildingAnchor anchor))
            {
                coord = anchor.hasSupplementalRoadAccessCoord ? anchor.supplementalRoadAccessCoord : anchor.coord;
                return true;
            }

            coord = default;
            return false;
        }
    }
}
