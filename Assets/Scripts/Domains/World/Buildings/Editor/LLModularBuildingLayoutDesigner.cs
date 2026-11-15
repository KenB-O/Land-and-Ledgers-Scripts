#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandAndLedgers.Buildings.Editor
{
    public sealed partial class LLModularBuildingAssemblyEditor
    {
        [Serializable]
        private sealed class LayoutEdgeSetting
        {
            public string key;
            public LLBuildingPerimeterType type = LLBuildingPerimeterType.SolidWall;
            public LLModularBuildingPiece module;
        }

        [SerializeField] private int layoutWidth = 6;
        [SerializeField] private int layoutDepth = 4;
        [SerializeField] private List<Vector2Int> layoutCells = new();
        [SerializeField] private List<LayoutEdgeSetting> layoutEdgeSettings = new();
        [SerializeField] private bool showManualPlacements;
        [SerializeField] private float layoutGroundElevation = 0.4f;
        [SerializeField] private LLModularBuildingPiece layoutFloorModule;
        [SerializeField] private string layoutDesignName = "New Modular Building";

        private void DrawFootprintDesigner()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Grid footprint designer", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Each cell is one catalog grid unit. Cells must touch by an edge. Floors are placed only in occupied cells; exterior walls are generated only where a neighboring cell is empty.", MessageType.Info);

            int width = Mathf.Clamp(EditorGUILayout.IntField("Width (cells)", layoutWidth), 1, 20);
            int depth = Mathf.Clamp(EditorGUILayout.IntField("Depth (cells)", layoutDepth), 1, 20);
            if (width != layoutWidth || depth != layoutDepth)
            {
                layoutWidth = width;
                layoutDepth = depth;
                layoutCells = layoutCells.Where(cell => cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < depth).Distinct().ToList();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fill rectangle")) layoutCells = LLModularBuildingAssembly.CreateRectangleCells(layoutWidth, layoutDepth);
                if (GUILayout.Button("Clear")) layoutCells.Clear();
            }
            if (GUILayout.Button("Example L (7 cells)"))
            {
                layoutWidth = 5;
                layoutDepth = 3;
                layoutCells = new List<Vector2Int>
                {
                    new(0,0), new(1,0), new(2,0), new(3,0), new(4,0),
                    new(4,1), new(4,2)
                };
            }

            DrawFootprintGrid();
            bool connected = LLModularBuildingAssembly.AreCellsConnected(layoutCells);
            int perimeterCount = LLModularBuildingAssembly.GetPerimeter(layoutCells).Count;
            float displayCellSize = catalog.Modules.FirstOrDefault(module => module != null)?.cellSizeMeters ?? assemblyCellSize;
            EditorGUILayout.LabelField($"Occupied cells: {layoutCells.Count} · Footprint envelope: {layoutWidth * displayCellSize:0.#} × {layoutDepth * displayCellSize:0.#} m · Exterior wall segments: {perimeterCount}");
            if (!connected)
                EditorGUILayout.HelpBox("Footprint is empty, contains duplicate cells, or has disconnected orthogonal regions. Connect the cells before generating a building.", MessageType.Error);

            List<string> families = catalog.Modules.Where(module => module != null && !string.IsNullOrWhiteSpace(module.extractedFromBuildingName))
                .Select(module => module.extractedFromBuildingName).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(family => family, StringComparer.OrdinalIgnoreCase).ToList();
            if (families.Count > 0)
            {
                int selectedFamily = Mathf.Max(0, families.FindIndex(family => string.Equals(family, familyFilter, StringComparison.OrdinalIgnoreCase)));
                selectedFamily = EditorGUILayout.Popup("Architectural family / style", selectedFamily, families.ToArray());
                familyFilter = families[selectedFamily];
            }

            layoutDesignName = EditorGUILayout.TextField("Design name", layoutDesignName);
            layoutGroundElevation = EditorGUILayout.FloatField("Ground walkable elevation (m)", layoutGroundElevation);
            layoutFloorModule = (LLModularBuildingPiece)EditorGUILayout.ObjectField("Floor module (auto when empty)", layoutFloorModule, typeof(LLModularBuildingPiece), false);

            List<LLBuildingPerimeterSegment> perimeter = LLModularBuildingAssembly.GetPerimeter(layoutCells);
            SyncEdgeSettings(perimeter);
            if (perimeter.Count > 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("Exterior perimeter", EditorStyles.boldLabel);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    foreach (LLBuildingPerimeterSegment segment in perimeter)
                    {
                        LayoutEdgeSetting setting = FindEdgeSetting(segment.Key);
                        EditorGUILayout.LabelField($"Cell {segment.cell.x},{segment.cell.y} · {segment.side}", EditorStyles.boldLabel);
                        setting.type = (LLBuildingPerimeterType)EditorGUILayout.EnumPopup("Perimeter", setting.type);
                        if (setting.type != LLBuildingPerimeterType.OpenPassage)
                            setting.module = (LLModularBuildingPiece)EditorGUILayout.ObjectField("Module override", setting.module, typeof(LLModularBuildingPiece), false);
                        string limitation = GetEdgeLimitation(segment, setting);
                        if (!string.IsNullOrEmpty(limitation))
                            EditorGUILayout.HelpBox(limitation, setting.type == LLBuildingPerimeterType.SolidWall || setting.type == LLBuildingPerimeterType.SingleWindow
                                ? MessageType.Info : MessageType.Warning);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!connected || layoutCells.Count == 0))
            {
                if (GUILayout.Button("Save current footprint as prefab", GUILayout.Height(30f)))
                    CreateFootprintPrefab(layoutDesignName, layoutCells, BuildAuthoredPerimeter(perimeter), layoutFloorModule, familyFilter, layoutWidth, layoutDepth, layoutGroundElevation);
                if (GUILayout.Button("Generate rectangle + L examples", GUILayout.Height(26f)))
                    GeneratePass2Examples();
            }
            EditorGUILayout.HelpBox("Door and double-door choices are never silently replaced by solid walls. They can be saved only when a same-family module has measured cell fit and a collider-clear opening. If the catalog has no such asset, select the explicitly named Open Passage option; it creates a real empty doorway gap with an authored entrance pair and no door leaf.", MessageType.Warning);
        }

        private void DrawFootprintGrid()
        {
            EditorGUILayout.LabelField("Footprint grid (north is at the top; click a cell to occupy/remove it)");
            var occupied = new HashSet<Vector2Int>(layoutCells);
            // Let the grid shrink inside narrow docked tabs instead of forcing a
            // horizontal overflow that can hide its edge cells.
            float cellButtonSize = Mathf.Clamp((EditorGUIUtility.currentViewWidth - 44f) / Mathf.Max(1, layoutWidth), 6f, 26f);
            for (int z = layoutDepth - 1; z >= 0; z--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < layoutWidth; x++)
                {
                    Vector2Int cell = new(x, z);
                    bool isOccupied = occupied.Contains(cell);
                    string label = isOccupied ? "■" : "·";
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = isOccupied ? new Color(0.55f, 0.75f, 0.55f) : old;
                    if (GUILayout.Button(label, GUILayout.Width(cellButtonSize), GUILayout.Height(cellButtonSize)))
                    {
                        if (isOccupied) layoutCells.RemoveAll(value => value == cell);
                        else layoutCells.Add(cell);
                    }
                    GUI.backgroundColor = old;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void SyncEdgeSettings(IReadOnlyList<LLBuildingPerimeterSegment> perimeter)
        {
            var existing = layoutEdgeSettings.Where(setting => setting != null)
                .GroupBy(setting => setting.key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            layoutEdgeSettings = new List<LayoutEdgeSetting>(perimeter.Count);
            foreach (LLBuildingPerimeterSegment edge in perimeter)
            {
                if (existing.TryGetValue(edge.Key, out LayoutEdgeSetting setting)) layoutEdgeSettings.Add(setting);
                else layoutEdgeSettings.Add(new LayoutEdgeSetting { key = edge.Key });
            }
        }

        private LayoutEdgeSetting FindEdgeSetting(string key) => layoutEdgeSettings.FirstOrDefault(setting => setting != null && setting.key == key);

        private List<LLBuildingPerimeterSegment> BuildAuthoredPerimeter(IReadOnlyList<LLBuildingPerimeterSegment> perimeter)
        {
            var result = new List<LLBuildingPerimeterSegment>(perimeter.Count);
            foreach (LLBuildingPerimeterSegment edge in perimeter)
            {
                LayoutEdgeSetting setting = FindEdgeSetting(edge.Key);
                LLBuildingModuleCatalogEntry entry = setting?.module != null && catalog.TryGetCatalogId(setting.module, out string id)
                    ? catalog.Entries.FirstOrDefault(candidate => candidate.catalogId == id)
                    : null;
                result.Add(new LLBuildingPerimeterSegment
                {
                    cell = edge.cell,
                    side = edge.side,
                    type = setting != null ? setting.type : LLBuildingPerimeterType.SolidWall,
                    moduleCatalogId = entry != null ? entry.catalogId : string.Empty,
                    floorIndex = 0
                });
            }
            return result;
        }

        private string GetEdgeLimitation(LLBuildingPerimeterSegment edge, LayoutEdgeSetting setting)
        {
            if (setting == null) return "Perimeter setting is missing.";
            if (setting.type == LLBuildingPerimeterType.OpenPassage) return "Explicit open passage: no wall or door leaf is placed in this cell edge.";
            LLModularBuildingPiece module = ResolveEdgeModule(edge, setting, out Vector3Int turns, out string issue);
            if (module == null) return issue;
            if (setting.type == LLBuildingPerimeterType.SingleDoor || setting.type == LLBuildingPerimeterType.DoubleDoor)
                return IsColliderClearOpening(module.gameObject, turns, edge.side, setting.type == LLBuildingPerimeterType.DoubleDoor ? 1.4f : 0.9f, 1.9f, out string reason)
                    ? $"Measured {module.name} opening is collider-clear for the requested envelope; full route validation remains necessary."
                    : reason;
            return $"Module fit: {LLBuildingModuleCatalog.ResolveSourceFamily(module)} · {module.name} · {turns} axis/yaw adapter.";
        }

        private LLModularBuildingPiece ResolveEdgeModule(LLBuildingPerimeterSegment edge, LayoutEdgeSetting setting,
            out Vector3Int turns, out string issue)
        {
            turns = Vector3Int.zero;
            issue = string.Empty;
            if (setting.type == LLBuildingPerimeterType.OpenPassage) return null;
            if (setting.module != null)
            {
                if (!TryFitWallPanel(setting.module, edge.side, out turns))
                {
                    issue = $"Selected module {setting.module.name} does not fit a {catalog.Modules.FirstOrDefault(module => module != null)?.cellSizeMeters:0.##} m perimeter segment using its measured geometry.";
                    return null;
                }
                if (!ModuleMatchesPerimeterType(setting.module, setting.type))
                {
                    issue = $"Selected module {setting.module.name} does not match the requested {setting.type} type.";
                    return null;
                }
                return setting.module;
            }

            LLModularBuildingPiece candidate = FindCompatibleWall(setting.type, edge.side, out turns);
            if (candidate == null)
            {
                issue = setting.type == LLBuildingPerimeterType.SingleDoor || setting.type == LLBuildingPerimeterType.DoubleDoor
                    ? $"No same-family {setting.type} module has a measured fit and collider-clear aperture. The requested door is unavailable; it will not be replaced by a closed wall."
                    : $"No compatible same-family {setting.type} panel fits this edge. Choose another family or explicitly select an available module.";
                return null;
            }
            return candidate;
        }

        private LLModularBuildingPiece FindCompatibleWall(LLBuildingPerimeterType type, LLBuildingPerimeterSide side, out Vector3Int turns)
        {
            turns = Vector3Int.zero;
            foreach (LLModularBuildingPiece candidate in catalog.Modules.Where(module => module != null
                         && string.Equals(module.extractedFromBuildingName, familyFilter, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(module => module.name, StringComparer.OrdinalIgnoreCase))
            {
                if (!ModuleMatchesPerimeterType(candidate, type) || !TryFitWallPanel(candidate, side, out Vector3Int candidateTurns)) continue;
                if ((type == LLBuildingPerimeterType.SingleDoor || type == LLBuildingPerimeterType.DoubleDoor)
                    && !IsColliderClearOpening(candidate.gameObject, candidateTurns, side, type == LLBuildingPerimeterType.DoubleDoor ? 1.4f : 0.9f, 1.9f, out _))
                    continue;
                turns = candidateTurns;
                return candidate;
            }
            return null;
        }

        private static bool ModuleMatchesPerimeterType(LLModularBuildingPiece module, LLBuildingPerimeterType type)
        {
            string identity = (module.name + " " + module.sourceMeshName + " " + module.sourceFamilyKey).ToLowerInvariant();
            switch (type)
            {
                case LLBuildingPerimeterType.SolidWall:
                    return module.category == LLModuleCategory.Wall && !identity.Contains("door") && !identity.Contains("window") && !identity.Contains("wind");
                case LLBuildingPerimeterType.SingleWindow:
                    return module.category == LLModuleCategory.Wall && (identity.Contains("window") || identity.Contains("wind"))
                        && !identity.Contains("multi") && !identity.Contains("2 window");
                case LLBuildingPerimeterType.MultiWindow:
                    return module.category == LLModuleCategory.Wall && (identity.Contains("multi window") || identity.Contains("2 window") || identity.Contains("double window"));
                case LLBuildingPerimeterType.SingleDoor:
                    return (module.category == LLModuleCategory.Door || module.category == LLModuleCategory.Wall)
                        && identity.Contains("door");
                case LLBuildingPerimeterType.DoubleDoor:
                    return (module.category == LLModuleCategory.Door || module.category == LLModuleCategory.Wall)
                        && identity.Contains("door") && (identity.Contains("double") || identity.Contains("2x2") || identity.Contains("2 door"));
                default:
                    return false;
            }
        }

        private bool TryFitFloor(LLModularBuildingPiece module, out Vector3Int turns, out Vector3 oriented)
        {
            turns = Vector3Int.zero;
            oriented = Vector3.zero;
            if (module == null || module.category != LLModuleCategory.Floor
                || !string.Equals(module.extractedFromBuildingName, familyFilter, StringComparison.OrdinalIgnoreCase)
                || !Mathf.Approximately(module.cellSizeMeters, assemblyCellSize) || module.gridWidth != 1 || module.gridDepth != 1)
                return false;
            Vector3 size = GetRootLocalRendererBounds(module.gameObject).size;
            if (catalog.TryGetCatalogId(module, out string id))
            {
                LLBuildingModuleCatalogEntry entry = catalog.Entries.FirstOrDefault(candidate => candidate.catalogId == id);
                if (entry != null) turns = entry.recommendedAxisQuarterTurns;
            }
            oriented = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(size, turns);
            return oriented.y <= 0.5f
                && Mathf.Abs(oriented.x - assemblyCellSize) <= 0.12f
                && Mathf.Abs(oriented.z - assemblyCellSize) <= 0.12f;
        }

        private bool TryFitWallPanel(LLModularBuildingPiece module, LLBuildingPerimeterSide side, out Vector3Int bestTurns)
        {
            bestTurns = Vector3Int.zero;
            if (module == null || module.category != LLModuleCategory.Wall
                && module.category != LLModuleCategory.Door
                || !string.Equals(module.extractedFromBuildingName, familyFilter, StringComparison.OrdinalIgnoreCase)
                || !Mathf.Approximately(module.cellSizeMeters, assemblyCellSize)
                || Mathf.Abs(module.transform.localScale.x - 1f) > 0.001f
                || Mathf.Abs(module.transform.localScale.y - 1f) > 0.001f
                || Mathf.Abs(module.transform.localScale.z - 1f) > 0.001f)
                return false;

            Vector3 source = GetRootLocalRendererBounds(module.gameObject).size;
            float bestScore = float.MaxValue;
            bool found = false;
            for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
            for (int z = 0; z < 4; z++)
            {
                Vector3Int turns = new(x, y, z);
                Vector3 size = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(source, turns);
                float span = side == LLBuildingPerimeterSide.North || side == LLBuildingPerimeterSide.South ? size.x : size.z;
                float thickness = side == LLBuildingPerimeterSide.North || side == LLBuildingPerimeterSide.South ? size.z : size.x;
                if (size.y < 1.5f || size.y > 3.1f || thickness > 0.55f || Mathf.Abs(span - assemblyCellSize) > 0.12f) continue;
                float score = Mathf.Abs(span - assemblyCellSize) + thickness * 0.1f + (x + y + z) * 0.0001f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestTurns = turns;
                found = true;
            }
            return found;
        }

        private bool IsColliderClearOpening(GameObject module, Vector3Int turns, LLBuildingPerimeterSide side,
            float clearWidth, float clearHeight, out string reason)
        {
            reason = "Opening could not be tested.";
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(module, preview);
                Quaternion rotation = Quaternion.Euler(new Vector3(turns.x * 90f, turns.y * 90f, turns.z * 90f));
                instance.transform.localRotation = rotation;
                Bounds bounds = GetRootLocalRendererBounds(instance);
                instance.transform.localPosition = -(rotation * bounds.center);
                Physics.SyncTransforms();
                Transform movableLeaf = FindMovableDoorLeaf(instance.transform);
                Collider[] colliders = instance.GetComponentsInChildren<Collider>(true)
                    .Where(collider => collider != null && collider.enabled && !collider.isTrigger
                        && (movableLeaf == null || !collider.transform.IsChildOf(movableLeaf))).ToArray();
                const float bodyBottom = 0.12f;
                float upper = Mathf.Min(clearHeight - 0.08f, 2.1f);
                if (upper <= bodyBottom) { reason = "Requested clear opening is shorter than the minimum body clearance."; return false; }
                bool horizontalEdge = side == LLBuildingPerimeterSide.North || side == LLBuildingPerimeterSide.South;
                Vector3 direction = horizontalEdge ? Vector3.forward : Vector3.right;
                int acrossSteps = Mathf.Max(2, Mathf.CeilToInt(clearWidth / 0.12f));
                int heightSteps = Mathf.Max(3, Mathf.CeilToInt((upper - bodyBottom) / 0.15f));
                for (int across = 0; across <= acrossSteps; across++)
                {
                    float offset = Mathf.Lerp(-clearWidth * 0.5f + 0.04f, clearWidth * 0.5f - 0.04f, across / (float)acrossSteps);
                    for (int vertical = 0; vertical <= heightSteps; vertical++)
                    {
                        float height = Mathf.Lerp(bodyBottom, upper, vertical / (float)heightSteps);
                        Vector3 acrossOffset = horizontalEdge ? Vector3.right * offset : Vector3.forward * offset;
                        Ray ray = new Ray(acrossOffset + Vector3.up * height - direction * 2f, direction);
                        foreach (Collider collider in colliders)
                        {
                            if (collider.Raycast(ray, out _, 4f))
                            {
                                reason = $"{module.name} has collider geometry across the requested {clearWidth:F2}m × {clearHeight:F2}m opening.";
                                return false;
                            }
                        }
                    }
                }
                reason = "Collider-ray checks found a clear opening envelope; building-scale route testing is still required.";
                return true;
            }
            finally
            {
                if (instance != null) DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private void CreateFootprintPrefab(string name, List<Vector2Int> cells,
            List<LLBuildingPerimeterSegment> authoredPerimeter, LLModularBuildingPiece requestedFloor,
            string family, int width, int depth, float groundElevation)
        {
            if (catalog == null) { Debug.LogError("Build or load the canonical module catalog first."); return; }
            if (cells == null || cells.Count == 0 || !LLModularBuildingAssembly.AreCellsConnected(cells))
            { Debug.LogError("Cannot generate a building from an empty, duplicate, or disconnected footprint."); return; }

            familyFilter = family;
            LLModularBuildingPiece floorModule = requestedFloor;
            if (!TryFitFloor(floorModule, out Vector3Int floorTurns, out Vector3 floorSize))
            {
                floorModule = catalog.Modules.FirstOrDefault(module => TryFitFloor(module, out _, out _));
                if (!TryFitFloor(floorModule, out floorTurns, out floorSize))
                { Debug.LogError($"No one-cell floor module in {family} measures {assemblyCellSize:F2}m × {assemblyCellSize:F2}m after its catalog axis adapter."); return; }
            }

            var placements = new List<(LLModularBuildingPiece module, Vector3 center, Vector3Int turns, Vector2Int cell, string slot, string catalogId, bool entrance, LLBuildingPerimeterSide side)>();
            foreach (Vector2Int cell in cells.Distinct())
            {
                Vector3 center = GridCellCenter(cell, width, depth, assemblyCellSize);
                center.y = LLModularBuildingAssembly.GetFloorModuleCenterY(groundElevation, floorSize.y);
                catalog.TryGetCatalogId(floorModule, out string floorId);
                placements.Add((floorModule, center, floorTurns, cell, $"floor:0:cell:{cell.x},{cell.y}", floorId, false, default));
            }

            foreach (LLBuildingPerimeterSegment edge in authoredPerimeter)
            {
                if (edge.type == LLBuildingPerimeterType.OpenPassage)
                {
                    continue;
                }
                LayoutEdgeSetting setting = FindEdgeSetting(edge.Key);
                LLModularBuildingPiece module = setting != null ? setting.module : null;
                if (module == null) module = FindCompatibleWall(edge.type, edge.side, out _);
                if (module == null || !TryFitWallPanel(module, edge.side, out Vector3Int turns))
                {
                    string reason = setting != null ? GetEdgeLimitation(edge, setting) : $"No compatible {edge.type} module exists for {edge.Key}.";
                    Debug.LogError($"Assembly generation stopped at perimeter {edge.Key}: {reason}");
                    return;
                }
                if ((edge.type == LLBuildingPerimeterType.SingleDoor || edge.type == LLBuildingPerimeterType.DoubleDoor)
                    && !IsColliderClearOpening(module.gameObject, turns, edge.side, edge.type == LLBuildingPerimeterType.DoubleDoor ? 1.4f : 0.9f, 1.9f, out string openingReason))
                { Debug.LogError($"Assembly generation stopped at perimeter {edge.Key}: {openingReason}"); return; }
                Vector3 center = GridEdgeCenter(edge.cell, edge.side, width, depth, assemblyCellSize);
                Vector3 panelSize = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(GetRootLocalRendererBounds(module.gameObject).size, turns);
                center.y = groundElevation + panelSize.y * 0.5f;
                catalog.TryGetCatalogId(module, out string moduleId);
                placements.Add((module, center, turns, edge.cell, $"edge:{edge.cell.x},{edge.cell.y}:{edge.side}", moduleId,
                    edge.type == LLBuildingPerimeterType.SingleDoor || edge.type == LLBuildingPerimeterType.DoubleDoor, edge.side));
                edge.moduleCatalogId = moduleId;
            }

            // Empty passages are explicit perimeter records with entrance pairs, not closed wall substitutes.
            foreach (LLBuildingPerimeterSegment edge in authoredPerimeter.Where(edge => edge.type == LLBuildingPerimeterType.OpenPassage))
                edge.moduleCatalogId = string.Empty;

            if (!LLModularBuildingAssembly.TryValidateModules(placements.Select(item => item.module).ToList(),
                    assemblyCellSize, family, true, true, null, out List<string> compatibilityIssues))
            { Debug.LogError("Assembly module compatibility failed: " + string.Join(" | ", compatibilityIssues)); return; }

            string safeName = string.IsNullOrWhiteSpace(name) ? "Modular Building" : name.Trim();
            const string examplesFolder = "Assets/Environment/Building Materials/Examples";
            EnsureFolders(examplesFolder);
            string outputPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/Environment/Building Materials/Examples/{safeName}.prefab");
            if (string.IsNullOrWhiteSpace(outputPath))
            { Debug.LogError($"Unity could not allocate a prefab path for '{safeName}' under {examplesFolder}."); return; }
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = new GameObject(safeName);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var floors = new List<LLModularBuildingFloor>
                    { new() { floorIndex = 0, displayName = "Ground Floor", elevationLocal = groundElevation } };
                BuildingFootprintAuthority authority = root.AddComponent<BuildingFootprintAuthority>();
                ConfigureAuthority(authority, floors, width, depth, assemblyCellSize, depth * assemblyCellSize * 0.5f,
                    width * assemblyCellSize, depth * assemblyCellSize);
                LLModularBuildingAssembly assembly = root.AddComponent<LLModularBuildingAssembly>();
                string designId = $"design.{Slug(safeName)}.{family.ToLowerInvariant()}";
                assembly.ConfigureLayout(designId, family, assemblyCellSize, storeyHeight, cells, floors, authoredPerimeter);

                foreach (var item in placements)
                {
                    LLModularBuildingPiece placed = PlaceModule(item.module, root.transform, item.center, item.turns);
                    LLModularBuildingPlacement placement = placed.GetComponent<LLModularBuildingPlacement>();
                    placement.ConfigureStable(placed, 0, item.cell, item.turns, item.catalogId, $"{designId}|{item.slot}");
                    if (item.entrance)
                    {
                        Transform leaf = FindMovableDoorLeaf(placed.transform);
                        if (leaf != null)
                        {
                            BuildingDoorAnimator door = placed.GetComponent<BuildingDoorAnimator>()
                                ?? placed.gameObject.AddComponent<BuildingDoorAnimator>();
                            if (!door.ConfigurePhysicalLeaf(leaf, new Vector3(0f, 90f, 0f)))
                            {
                                Debug.LogError($"Door module '{placed.name}' has a separate leaf but it could not be bound to its physical door state.");
                                return;
                            }
                        }
                        ConfigureEntrance(authority, placed.transform, item.center,
                            ResolveOutwardYaw(item.side), 0, item.module.category == LLModuleCategory.Door ? 0.9f : assemblyCellSize,
                            assemblyCellSize, groundElevation);
                    }
                }

                foreach (LLBuildingPerimeterSegment edge in authoredPerimeter.Where(item => item.type == LLBuildingPerimeterType.OpenPassage))
                {
                    GameObject anchor = new GameObject($"Open Passage Entrance {edge.Key}");
                    anchor.transform.SetParent(root.transform, false);
                    anchor.transform.localPosition = GridEdgeCenter(edge.cell, edge.side, width, depth, assemblyCellSize);
                    ConfigureEntrance(authority, anchor.transform, anchor.transform.localPosition,
                        ResolveOutwardYaw(edge.side), 0, 0.9f, assemblyCellSize, groundElevation);
                }

                if (!assembly.TryValidate(out List<string> issues))
                { Debug.LogError("Assembly was not saved because validation failed: " + string.Join(" | ", issues)); return; }
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                if (saved == null) { Debug.LogError($"Could not save modular design prefab at {outputPath}."); return; }
                AssetDatabase.SaveAssets();
                Debug.Log($"[Modular Buildings] Saved design {designId} to {outputPath}: {cells.Count} occupied cells, {authoredPerimeter.Count} exterior edges, {placements.Count} physical modules, one geometry-derived floor. Open passages remain explicit and door modules were not fabricated.", saved);
                EditorGUIUtility.PingObject(saved);
            }
            finally
            {
                if (root != null) DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static Transform FindMovableDoorLeaf(Transform root)
        {
            if (root == null) return null;
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(candidate.name, "Door", StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate.GetComponentInChildren<Renderer>(true) == null
                    || candidate.GetComponentInChildren<Collider>(true) == null) continue;
                return candidate;
            }
            return null;
        }

        public static void GeneratePass2Examples()
        {
            LLModularBuildingAssemblyEditor window = GetWindow<LLModularBuildingAssemblyEditor>("Modular Assembly");
            window.catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(CatalogPath);
            if (window.catalog == null) { Debug.LogError("Canonical modular catalog is missing. Build it before generating examples."); return; }
            window.familyFilter = "House01";
            window.assemblyCellSize = 2f;
            window.storeyHeight = 2f;
            window.layoutGroundElevation = 0.4f;
            window.layoutEdgeSettings.Clear();
            List<LLBuildingPerimeterSegment> rectanglePerimeter;
            List<Vector2Int> rectangle = LLModularBuildingAssembly.CreateRectangleCells(6, 4);
            rectanglePerimeter = LLModularBuildingAssembly.GetPerimeter(rectangle);
            SetExampleEdge(rectanglePerimeter, new Vector2Int(2, 0), LLBuildingPerimeterSide.South, LLBuildingPerimeterType.OpenPassage);
            SetExampleEdge(rectanglePerimeter, new Vector2Int(2, 3), LLBuildingPerimeterSide.North, LLBuildingPerimeterType.SingleWindow);
            window.CreateFootprintPrefab("Pass 2 Rectangle 12x8", rectangle, rectanglePerimeter, null, "House01", 6, 4, 0.4f);

            List<Vector2Int> lCells = new()
            {
                new(0,0), new(1,0), new(2,0), new(3,0), new(4,0),
                new(4,1), new(4,2)
            };
            List<LLBuildingPerimeterSegment> lPerimeter = LLModularBuildingAssembly.GetPerimeter(lCells);
            SetExampleEdge(lPerimeter, new Vector2Int(2, 0), LLBuildingPerimeterSide.South, LLBuildingPerimeterType.OpenPassage);
            SetExampleEdge(lPerimeter, new Vector2Int(4, 2), LLBuildingPerimeterSide.North, LLBuildingPerimeterType.SingleWindow);
            window.CreateFootprintPrefab("Pass 2 L 7 Cell Example", lCells, lPerimeter, null, "House01", 5, 3, 0.4f);
        }

        private static void SetExampleEdge(List<LLBuildingPerimeterSegment> perimeter, Vector2Int cell,
            LLBuildingPerimeterSide side, LLBuildingPerimeterType type)
        {
            LLBuildingPerimeterSegment edge = perimeter.FirstOrDefault(item => item.cell == cell && item.side == side);
            if (edge != null) edge.type = type;
        }

        private static Vector3 GridCellCenter(Vector2Int cell, int width, int depth, float cellSize)
            => new((cell.x + 0.5f - width * 0.5f) * cellSize, 0f, (cell.y + 0.5f - depth * 0.5f) * cellSize);

        private static Vector3 GridEdgeCenter(Vector2Int cell, LLBuildingPerimeterSide side, int width, int depth, float cellSize)
        {
            Vector3 center = GridCellCenter(cell, width, depth, cellSize);
            switch (side)
            {
                case LLBuildingPerimeterSide.North: center.z += cellSize * 0.5f; break;
                case LLBuildingPerimeterSide.East: center.x += cellSize * 0.5f; break;
                case LLBuildingPerimeterSide.South: center.z -= cellSize * 0.5f; break;
                case LLBuildingPerimeterSide.West: center.x -= cellSize * 0.5f; break;
            }
            return center;
        }

        private static int ResolveOutwardYaw(LLBuildingPerimeterSide side)
        {
            switch (side)
            {
                case LLBuildingPerimeterSide.East: return 1;
                case LLBuildingPerimeterSide.South: return 2;
                case LLBuildingPerimeterSide.West: return 3;
                default: return 0;
            }
        }

        private static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "building";
            return new string(value.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray()).Trim('-');
        }
    }
}
#endif
