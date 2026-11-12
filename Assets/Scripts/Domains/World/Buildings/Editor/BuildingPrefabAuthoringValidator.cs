using System.Collections.Generic;
using LandLedgers.World;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.World.Editor
{
    /// <summary>
    /// Read-only validation for building definitions and the prefabs they actually reference.
    /// It deliberately reports authoring issues without changing scenes or prefab assets.
    /// </summary>
    public static class BuildingPrefabAuthoringValidator
    {
        private const string DefinitionsFolder = "Assets/Core/World/Buildings";
        private const string PrefabsFolder = "Assets/Environment/Buildings";

        [MenuItem("Land & Ledgers/World/Validate Building Prefabs")]
        public static void ValidateAll()
        {
            string[] guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { DefinitionsFolder });
            HashSet<GameObject> referencedPrefabs = new();
            int errors = 0;
            int warnings = 0;
            int definitions = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                BuildingDefinition definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(assetPath);
                if (definition == null)
                {
                    continue;
                }

                definitions++;
                GameObject prefab = definition.VisualPrefab;
                if (prefab == null)
                {
                    Debug.LogError($"[Building Authoring] {assetPath}: visual prefab reference is missing.", definition);
                    errors++;
                    continue;
                }

                referencedPrefabs.Add(prefab);
                ValidateDefinition(assetPath, definition, prefab, ref errors, ref warnings);
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder });
            int unreferenced = 0;
            foreach (string guid in prefabGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || referencedPrefabs.Contains(prefab))
                {
                    continue;
                }

                unreferenced++;
                Debug.Log($"[Building Authoring] Available prefab is not referenced by a BuildingDefinition: {path}. This is informational; special shelter and modular assets may be intentional.", prefab);
            }

            Debug.Log($"[Building Authoring] Validation complete: {definitions} definitions, {referencedPrefabs.Count} referenced prefabs, {errors} errors, {warnings} warnings, {unreferenced} available unreferenced prefabs.");
        }

        private static void ValidateDefinition(string definitionPath, BuildingDefinition definition, GameObject prefab, ref int errors, ref int warnings)
        {
            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrWhiteSpace(prefabPath) || AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                ReportError($"{definitionPath}: visual prefab reference does not resolve to a prefab asset.", definition, ref errors);
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                ReportError($"{definitionPath}: visual prefab could not be opened for validation ({prefabPath}).", definition, ref errors);
                return;
            }

            try
            {
                BuildingFootprintAuthority footprint = root.GetComponent<BuildingFootprintAuthority>();
                if (footprint == null)
                {
                    ReportError($"{definitionPath} -> {prefabPath}: root BuildingFootprintAuthority is missing.", definition, ref errors);
                }
                else if (footprint.TryBuildAuthoringWarningSummary(out string summary))
                {
                    ReportWarning($"{definitionPath} -> {prefabPath}: footprint authoring: {summary}.", definition, ref warnings);
                }

                if (footprint != null)
                {
                    ValidateDeclaredUses(definitionPath, prefabPath, definition, footprint, ref errors, ref warnings);
                }

                List<BuildingAnchorMarker> markers = new(root.GetComponentsInChildren<BuildingAnchorMarker>(true));
                ValidateAnchor(definition, prefabPath, root.transform, markers, AnchorType.FrontDoor, ref errors, ref warnings);
                ValidateAnchor(definition, prefabPath, root.transform, markers, AnchorType.Service, ref errors, ref warnings);
                ValidateAnchor(definition, prefabPath, root.transform, markers, AnchorType.DropOff, ref errors, ref warnings);

                if (footprint != null)
                {
                    foreach (BuildingAnchorMarker marker in markers)
                    {
                        if (marker.Type == AnchorType.FrontDoor
                            && !footprint.ContainsFootprintLocalPoint(root.transform.InverseTransformPoint(marker.transform.position), 0.75f))
                        {
                            ReportWarning($"{prefabPath}: FrontDoor marker '{GetRelativePath(root.transform, marker.transform)}' lies outside the footprint boundary.", definition, ref warnings);
                        }
                    }
                }

                int doorVisualCount = CountDoorVisuals(root.transform);
                if (doorVisualCount == 0)
                {
                    ReportWarning($"{prefabPath}: no door/entrance-named visual was found; confirm the authored front-door marker meets the physical entrance.", definition, ref warnings);
                }

                int hitchAnchors = CountExplicitHitchAnchors(root.transform);
                if (hitchAnchors > 1)
                {
                    ReportWarning($"{prefabPath}: {hitchAnchors} explicitly named hitch anchors found; review for redundant vehicle staging points.", definition, ref warnings);
                }

                ValidateLods(prefabPath, root, definition, ref errors, ref warnings);

                BuildingExteriorPropAuthoring exteriorProps = root.GetComponent<BuildingExteriorPropAuthoring>();
                if (exteriorProps != null)
                {
                    List<BuildingExteriorIssue> issues = new();
                    exteriorProps.GetAuthoringIssues(issues);
                    foreach (BuildingExteriorIssue issue in issues)
                    {
                        string message = $"{prefabPath}: exterior props {issue.groupName}: {issue.message}";
                        if (issue.isError)
                        {
                            ReportError(message, definition, ref errors);
                        }
                        else
                        {
                            ReportWarning(message, definition, ref warnings);
                        }
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ValidateDeclaredUses(
            string definitionPath,
            string prefabPath,
            BuildingDefinition definition,
            BuildingFootprintAuthority footprint,
            ref int errors,
            ref int warnings)
        {
            bool hasBed = false;
            bool hasBusinessActivity = false;
            bool hasWalkableBedFloor = false;
            bool hasGroundFloorEntrance = false;

            foreach (BuildingActivityLocationAuthoring activity in footprint.ActivityLocations)
            {
                if (activity == null) continue;
                if (activity.type == BuildingActivityLocationType.Bed)
                {
                    hasBed = true;
                    hasWalkableBedFloor |= activity.requiresWalkableInterior
                        && footprint.HasInteriorWalkableArea(activity.floorIndex, activity.functionalSpaceId);
                }

                hasBusinessActivity |= activity.type == BuildingActivityLocationType.Counter
                    || activity.type == BuildingActivityLocationType.Workshop
                    || activity.type == BuildingActivityLocationType.Oven;
            }

            foreach (BuildingEntrancePairAuthoring entrance in footprint.EntrancePairs)
            {
                if (entrance != null && entrance.floorIndex == 0) hasGroundFloorEntrance = true;
            }

            if (definition.CanHostWorkplace && !hasBusinessActivity)
            {
                ReportWarning($"{definitionPath} -> {prefabPath}: workplace-capable definition has no typed Counter, Workshop, or Oven activity point.", definition, ref warnings);
            }

            if (!definition.CanHostHouseholds) return;

            if (!hasBed || !hasWalkableBedFloor)
            {
                ReportWarning($"{definitionPath} -> {prefabPath}: residential capacity is declared, but no Bed activity on an authored walkable floor is available; the shell is not yet an activity-ready dwelling.", definition, ref warnings);
            }

            if (definition.UsesUpperFloorResidential)
            {
                bool hasUpperWalkableFloor = false;
                foreach (InteriorFloorAuthoring floor in footprint.InteriorFloors)
                {
                    if (floor != null && floor.floorIndex > 0 && floor.walkableAreas != null && floor.walkableAreas.Count > 0)
                    {
                        hasUpperWalkableFloor = true;
                        break;
                    }
                }

                bool hasUpperBed = false;
                foreach (BuildingActivityLocationAuthoring activity in footprint.ActivityLocations)
                {
                    if (activity != null && activity.type == BuildingActivityLocationType.Bed
                        && activity.floorIndex > 0 && activity.requiresWalkableInterior)
                    {
                        hasUpperBed = true;
                        break;
                    }
                }

                if (!hasUpperWalkableFloor || !hasUpperBed)
                {
                    ReportWarning($"{definitionPath} -> {prefabPath}: definition advertises an upper-floor household, but the prefab lacks a walkable upper-floor residence and/or a Bed activity there. This use is not physically authored yet.", definition, ref warnings);
                }
                else
                {
                    ReportWarning($"{definitionPath} -> {prefabPath}: upper-floor residence is authored, but current AgentMover interior activity routes are floor-0-only; verify a supported stair transition before assigning residents.", definition, ref warnings);
                }
            }
            else if (definition.IsMixedUse && !hasGroundFloorEntrance)
            {
                ReportWarning($"{definitionPath} -> {prefabPath}: mixed-use shell has no ground-floor entrance pair for shared customer and household access.", definition, ref warnings);
            }
        }

        private static void ValidateAnchor(BuildingDefinition definition, string prefabPath, Transform root, List<BuildingAnchorMarker> markers, AnchorType type, ref int errors, ref int warnings)
        {
            int markerCount = 0;
            foreach (BuildingAnchorMarker marker in markers)
            {
                if (marker.Type == type)
                {
                    markerCount++;
                }
            }

            string namedAnchor = type switch
            {
                AnchorType.FrontDoor => "Anchor_FrontDoor",
                AnchorType.Service => "Anchor_ServicePoint",
                AnchorType.DropOff => "Anchor_DropOffPoint",
                _ => string.Empty
            };
            Transform namedTransform = FindNamedChild(root, namedAnchor);
            int authoredCount = markerCount + (namedTransform != null && !HasMarker(namedTransform, type) ? 1 : 0);
            BuildingAnchorExpectation expectation = definition.GetAnchorExpectation(type);

            if (expectation == BuildingAnchorExpectation.Required && authoredCount == 0)
            {
                ReportError($"{prefabPath}: required {type} anchor is missing.", definition, ref errors);
            }
            else if (authoredCount > 1)
            {
                ReportWarning($"{prefabPath}: {authoredCount} authored {type} anchor candidates may compete; TownWorldController uses its established first-valid-marker rules.", definition, ref warnings);
            }

            if (namedTransform != null && markerCount > 0 && !HasMarker(namedTransform, type))
            {
                ReportWarning($"{prefabPath}: {namedAnchor} coexists with a {type} marker; confirm both are intentional.", definition, ref warnings);
            }
        }

        private static void ValidateLods(string prefabPath, GameObject root, Object context, ref int errors, ref int warnings)
        {
            LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(true);
            if (groups.Length == 0)
            {
                ReportWarning($"{prefabPath}: no LODGroup exists on the prefab or its modular pieces.", context, ref warnings);
                return;
            }

            foreach (LODGroup group in groups)
            {
                LOD[] lods = group.GetLODs();
                if (lods.Length == 0)
                {
                    ReportError($"{prefabPath}: LODGroup '{GetRelativePath(root.transform, group.transform)}' has no levels.", context, ref errors);
                    continue;
                }

                float previous = 1.01f;
                for (int i = 0; i < lods.Length; i++)
                {
                    float threshold = lods[i].screenRelativeTransitionHeight;
                    if (threshold < 0f || threshold > 1f || threshold > previous)
                    {
                        ReportError($"{prefabPath}: LODGroup '{GetRelativePath(root.transform, group.transform)}' has invalid transition ordering at LOD {i} ({threshold:0.###}).", context, ref errors);
                        break;
                    }

                    if (lods[i].renderers == null || lods[i].renderers.Length == 0)
                    {
                        ReportWarning($"{prefabPath}: LODGroup '{GetRelativePath(root.transform, group.transform)}' has no renderers at LOD {i}.", context, ref warnings);
                    }
                    else
                    {
                        bool hasMissingRenderer = false;
                        foreach (Renderer renderer in lods[i].renderers)
                        {
                            if (renderer == null)
                            {
                                hasMissingRenderer = true;
                                continue;
                            }
                        }

                        if (hasMissingRenderer)
                        {
                            ReportWarning($"{prefabPath}: LODGroup '{GetRelativePath(root.transform, group.transform)}' has a null renderer slot at LOD {i}; confirm the level still has the intended visible renderers.", context, ref warnings);
                        }
                    }

                    previous = threshold;
                }
            }
        }

        private static int CountDoorVisuals(Transform root)
        {
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name.Contains("door") || name.Contains("entrance"))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountExplicitHitchAnchors(Transform root)
        {
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name.ToLowerInvariant();
                if (name.Contains("hitch") && (name.StartsWith("anchor_") || name.Contains("hitchpoint")))
                {
                    count++;
                }
            }

            return count;
        }

        private static Transform FindNamedChild(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasMarker(Transform transform, AnchorType type)
        {
            BuildingAnchorMarker marker = transform.GetComponent<BuildingAnchorMarker>();
            return marker != null && marker.Type == type;
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            List<string> parts = new();
            for (Transform current = child; current != null && current != root; current = current.parent)
            {
                parts.Insert(0, current.name);
            }

            return string.Join("/", parts);
        }

        private static void ReportError(string message, Object context, ref int errors)
        {
            Debug.LogError("[Building Authoring] " + message, context);
            errors++;
        }

        private static void ReportWarning(string message, Object context, ref int warnings)
        {
            Debug.LogWarning("[Building Authoring] " + message, context);
            warnings++;
        }
    }
}
