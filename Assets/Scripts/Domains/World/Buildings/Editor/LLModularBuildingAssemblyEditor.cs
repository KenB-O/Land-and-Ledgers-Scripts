#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LandAndLedgers.Buildings.Editor
{
    /// <summary>Builds a reference-only catalog and creates modular prefab assemblies in an isolated preview scene.</summary>
    public sealed partial class LLModularBuildingAssemblyEditor : EditorWindow
    {
        private const string ModuleRoot = "Assets/Environment/Building Materials/Auto Extracted";
        private const string CatalogPath = "Assets/Environment/Building Materials/Modules/BuildingMaterialsModuleCatalog.asset";
        private const string ExamplePath = "Assets/Environment/Building Materials/Examples/House01 Modular 4x4 Example.prefab";
        private LLBuildingModuleCatalog catalog;
        private string familyFilter = "House01";
        private int categoryFilterIndex;
        [Serializable]
        private sealed class ModuleRow
        {
            public LLModularBuildingPiece module;
            public Vector3 boundsCenter;
            public Vector3Int quarterTurns;
            public int floorIndex;
            public bool entrance;
            public float clearWidth = 1.07f;
        }
        [SerializeField] private string assemblyName = "New Modular Building";
        [SerializeField] private int widthCells = 2;
        [SerializeField] private int depthCells = 2;
        [SerializeField] private float assemblyCellSize = 2f;
        [SerializeField] private float storeyHeight = 2f;
        [SerializeField] private List<LLModularBuildingFloor> floorDefinitions = new() { new LLModularBuildingFloor() };
        [SerializeField] private List<ModuleRow> moduleRows = new();
        private Vector2 windowScroll;
        [SerializeField] private bool showFamilyModules;

        [MenuItem("Land & Ledgers/Buildings/Modular Assembly Authoring")]
        private static void Open() => GetWindow<LLModularBuildingAssemblyEditor>("Modular Assembly");

        [MenuItem("Land & Ledgers/Buildings/Build Building Materials Catalog")]
        public static void BuildCatalog()
        {
            EnsureFolder("Assets/Environment/Building Materials/Modules");
            const string legacyCatalogPath = "Assets/Environment/Building Materials/BuildingMaterialsModuleCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(CatalogPath) == null
                && AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(legacyCatalogPath) != null)
            {
                string moveError = AssetDatabase.MoveAsset(legacyCatalogPath, CatalogPath);
                if (!string.IsNullOrEmpty(moveError))
                {
                    Debug.LogError("[Modular Buildings] Could not move the existing catalog to the central Modules folder: " + moveError);
                    return;
                }
            }
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { ModuleRoot });
            Array.Sort(guids, StringComparer.Ordinal);
            List<LLModularBuildingPiece> modules = new();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Sources are paired source-LOD references, not the finished module prefabs for assembly.
                if (!path.Replace('\\', '/').Contains("/Prefabs/", StringComparison.OrdinalIgnoreCase)) continue;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                LLModularBuildingPiece piece = prefab != null ? prefab.GetComponent<LLModularBuildingPiece>() : null;
                if (piece != null) modules.Add(piece);
            }

            LLBuildingModuleCatalog index = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(CatalogPath);
            if (index == null)
            {
                index = CreateInstance<LLBuildingModuleCatalog>();
                AssetDatabase.CreateAsset(index, CatalogPath);
            }
            index.SetIndex(ModuleRoot, modules);
            EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            int ambiguousLegacy = index.Entries.GroupBy(entry => entry.legacyPieceId, StringComparer.OrdinalIgnoreCase)
                .Count(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1);
            int blockedDoorways = index.Entries.Count(entry => entry.categoryPath == "Walls/Doorways"
                && entry.openingClearance.StartsWith("blocked", StringComparison.OrdinalIgnoreCase));
            Debug.Log($"[Modular Buildings] Central catalog indexed {modules.Count} ready modules across {index.GetCategoryPaths().Count} virtual categories; paired source LOD prefabs were excluded. Legacy pieceId aliases with collisions={ambiguousLegacy}; door-named wall modules with blocked nominal clearance={blockedDoorways}. Prefab paths and source assets were retained.", index);
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            string name = Path.GetFileName(assetPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name)) AssetDatabase.CreateFolder(parent, name);
        }

        private void OnGUI()
        {
            windowScroll = EditorGUILayout.BeginScrollView(windowScroll, false, true,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawWindowContents();
            EditorGUILayout.EndScrollView();
        }

        private void DrawWindowContents()
        {
            EditorGUILayout.HelpBox("The central Modules catalog groups reusable LLModularBuildingPiece prefabs by architectural function and source family. It retains source-family asset paths because the generated modules depend on paired LOD prefabs and the extractor regenerates those paths.", MessageType.Info);
            catalog = (LLBuildingModuleCatalog)EditorGUILayout.ObjectField("Module Catalog", catalog, typeof(LLBuildingModuleCatalog), false);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build / Refresh Catalog")) BuildCatalog();
                if (GUILayout.Button("Load Catalog")) catalog = AssetDatabase.LoadAssetAtPath<LLBuildingModuleCatalog>(CatalogPath);
            }

            if (catalog == null)
            {
                EditorGUILayout.HelpBox("Build or load the module catalog to configure a footprint and its perimeter. The full window remains scrollable at any size.", MessageType.Info);
                return;
            }

            familyFilter = EditorGUILayout.TextField("Source Family", familyFilter);
            int familyCount = catalog.Modules.Count(m => m != null && string.Equals(m.extractedFromBuildingName, familyFilter, StringComparison.OrdinalIgnoreCase));
            EditorGUILayout.LabelField($"Indexed modules: {catalog.Modules.Count}; in this source family: {familyCount}");
            List<string> categories = catalog.GetCategoryPaths();
            List<string> categoryOptions = new List<string> { "All categories" };
            categoryOptions.AddRange(categories);
            categoryFilterIndex = Mathf.Clamp(categoryFilterIndex, 0, categoryOptions.Count - 1);
            categoryFilterIndex = EditorGUILayout.Popup("Architectural category", categoryFilterIndex, categoryOptions.ToArray());
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Primary workflow", EditorStyles.boldLabel);
            DrawFootprintDesigner();

            EditorGUILayout.Space(8f);
            showManualPlacements = EditorGUILayout.Foldout(showManualPlacements, "Manual module placements (advanced)", true);
            if (showManualPlacements)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Assemble a new building prefab", EditorStyles.boldLabel);
                assemblyName = EditorGUILayout.TextField("Assembly Name", assemblyName);
                widthCells = Mathf.Max(1, EditorGUILayout.IntField("Footprint Width (cells)", widthCells));
                depthCells = Mathf.Max(1, EditorGUILayout.IntField("Footprint Depth (cells)", depthCells));
                assemblyCellSize = Mathf.Max(0.1f, EditorGUILayout.FloatField("Grid Cell (m)", assemblyCellSize));
                storeyHeight = Mathf.Max(0.1f, EditorGUILayout.FloatField("Storey Height (m)", storeyHeight));
                for (int i = 0; i < floorDefinitions.Count; i++)
                {
                    LLModularBuildingFloor floor = floorDefinitions[i];
                    if (floor == null) { floorDefinitions[i] = floor = new LLModularBuildingFloor(); }
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        floor.floorIndex = EditorGUILayout.IntField("Floor Index", floor.floorIndex);
                        floor.displayName = EditorGUILayout.TextField("Display Name", floor.displayName);
                        floor.elevationLocal = EditorGUILayout.FloatField("Elevation Y (m)", floor.elevationLocal);
                        floor.requiresVerticalConnectionFromFloorBelow = EditorGUILayout.ToggleLeft("Requires a vertical route from floor below", floor.requiresVerticalConnectionFromFloorBelow);
                    }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add Floor")) floorDefinitions.Add(new LLModularBuildingFloor { floorIndex = floorDefinitions.Count, displayName = "Upper Floor", elevationLocal = floorDefinitions.Count * storeyHeight, requiresVerticalConnectionFromFloorBelow = true });
                    if (floorDefinitions.Count > 1 && GUILayout.Button("Remove Last Floor")) floorDefinitions.RemoveAt(floorDefinitions.Count - 1);
                }
                for (int i = 0; i < moduleRows.Count; i++)
                {
                    ModuleRow row = moduleRows[i];
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"Module placement {i + 1}", EditorStyles.boldLabel);
                            if (GUILayout.Button("Remove", GUILayout.Width(70f))) { moduleRows.RemoveAt(i); i--; continue; }
                        }
                        LLModularBuildingPiece selectedModule = (LLModularBuildingPiece)EditorGUILayout.ObjectField("Prefab Module", row.module, typeof(LLModularBuildingPiece), false);
                        if (selectedModule != row.module)
                        {
                            row.module = selectedModule;
                            if (selectedModule != null)
                            {
                                Bounds measured = GetRendererBounds(selectedModule.gameObject);
                                row.quarterTurns = LLBuildingModuleCatalog.GetRecommendedAxisQuarterTurns(selectedModule.category, measured.size);
                            }
                        }
                        row.boundsCenter = EditorGUILayout.Vector3Field("Target Bounds Center (m)", row.boundsCenter);
                        row.quarterTurns.x = EditorGUILayout.IntSlider("Quarter-turn X (axis adapter)", row.quarterTurns.x, 0, 3);
                        row.quarterTurns.y = EditorGUILayout.IntSlider("Quarter-turn Y (yaw)", row.quarterTurns.y, 0, 3);
                        row.quarterTurns.z = EditorGUILayout.IntSlider("Quarter-turn Z", row.quarterTurns.z, 0, 3);
                        row.floorIndex = EditorGUILayout.IntField("Floor Index", row.floorIndex);
                        row.entrance = EditorGUILayout.Toggle("This placement is an entrance", row.entrance);
                        if (row.entrance) row.clearWidth = Mathf.Max(0.2f, EditorGUILayout.FloatField("Clear Opening Width (m)", row.clearWidth));
                    }
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    if (GUILayout.Button("Add Module Placement")) moduleRows.Add(new ModuleRow());
                    if (GUILayout.Button("Snap Centers to Grid"))
                        foreach (ModuleRow row in moduleRows)
                            if (row != null) { row.boundsCenter.x = LLModularBuildingAssembly.SnapToCell(row.boundsCenter.x, assemblyCellSize, assemblyCellSize * 0.5f); row.boundsCenter.z = LLModularBuildingAssembly.SnapToCell(row.boundsCenter.z, assemblyCellSize, assemblyCellSize * 0.5f); }
                }
                if (GUILayout.Button("Create Assembly Prefab From Placements", GUILayout.Height(28f)))
                    CreateAssemblyPrefab(assemblyName, widthCells, depthCells, assemblyCellSize, storeyHeight, floorDefinitions, moduleRows);
            }

            EditorGUILayout.Space(6f);
            showFamilyModules = EditorGUILayout.Foldout(showFamilyModules, "Catalog module browser", true);
            if (showFamilyModules)
            {
                if (GUILayout.Button("Create House01 4×4 Example Prefab", GUILayout.Height(30f)))
                    CreateHouse01Example(catalog);

                EditorGUILayout.LabelField("Selected family modules", EditorStyles.boldLabel);
                foreach (LLBuildingModuleCatalogEntry entry in catalog.GetEntries(
                             categoryFilterIndex <= 0 ? null : categoryOptions[categoryFilterIndex], familyFilter))
                {
                    LLModularBuildingPiece piece = catalog.ResolveModule(entry);
                    if (piece == null) continue;
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(entry.categoryPath, EditorStyles.boldLabel);
                        EditorGUILayout.ObjectField(piece, typeof(LLModularBuildingPiece), false);
                        EditorGUILayout.LabelField($"{entry.sourceFamily} · {piece.gridWidth * piece.cellSizeMeters:F1}×{piece.gridDepth * piece.cellSizeMeters:F1}m", EditorStyles.miniLabel);
                        if (!string.IsNullOrEmpty(entry.openingClearance)
                            && entry.openingClearance.StartsWith("blocked", StringComparison.OrdinalIgnoreCase))
                            EditorGUILayout.HelpBox(entry.displayName + " — " + entry.openingClearance, MessageType.Warning);
                    }
                }
            }
        }

        private static Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) if (renderers[i] != null) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static void CreateAssemblyPrefab(string name, int widthCells, int depthCells, float cellSize,
            float storeyHeight, List<LLModularBuildingFloor> floors, List<ModuleRow> rows)
        {
            if (rows == null || rows.Count == 0 || rows.Any(r => r == null || r.module == null))
            { Debug.LogError("Assembly requires at least one assigned module placement."); return; }
            List<LLModularBuildingPiece> modules = rows.Select(r => r.module).ToList();
            if (!LLModularBuildingAssembly.TryValidateModules(modules, cellSize, string.Empty, true, true, null, out List<string> compatibilityIssues))
            { Debug.LogError("Assembly stopped: " + string.Join(" | ", compatibilityIssues)); return; }
            if (floors == null || floors.Count == 0 || floors.Any(f => f == null))
            { Debug.LogError("Assembly requires at least one floor elevation."); return; }
            foreach (ModuleRow row in rows)
                if (!floors.Any(f => f.floorIndex == row.floorIndex))
                { Debug.LogError($"Module {row.module.name} references missing floor {row.floorIndex}."); return; }

            string safeName = string.IsNullOrWhiteSpace(name) ? "Modular Building" : name.Trim();
            string outputPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/Environment/Building Materials/Examples/{safeName}.prefab");
            EnsureFolders(Path.GetDirectoryName(outputPath).Replace('\\', '/'));
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = new(safeName);
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                LLModularBuildingAssembly assembly = root.AddComponent<LLModularBuildingAssembly>();
                BuildingFootprintAuthority authority = root.AddComponent<BuildingFootprintAuthority>();
                float width = widthCells * cellSize, depth = depthCells * cellSize;
                ConfigureAuthority(authority, floors, widthCells, depthCells, cellSize, depth * 0.5f, width, depth);
                assembly.Configure($"modular.{safeName.ToLowerInvariant().Replace(' ', '.')}", modules[0].extractedFromBuildingName,
                    cellSize, storeyHeight, new Vector2Int(widthCells, depthCells), floors);

                int entranceCount = rows.Count(r => r.entrance);
                if (entranceCount > 1) { Debug.LogError("A prefab can have multiple doors; author each entrance pair after generation. The assembly builder creates one entrance pair per assembly only."); return; }
                foreach (ModuleRow row in rows)
                {
                    LLModularBuildingFloor assignedFloor = floors.First(f => f.floorIndex == row.floorIndex);
                    if (row.module.category == LLModuleCategory.Floor)
                    {
                        Vector3 localBounds = GetRootLocalRendererBounds(row.module.gameObject).size;
                        Vector3 oriented = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(localBounds, row.quarterTurns);
                        if (oriented.y > 0.5f || oriented.x < cellSize * 0.5f || oriented.z < cellSize * 0.5f)
                        { Debug.LogError($"Floor module {row.module.name} is not a horizontal surface after its axis adapter ({oriented})."); return; }
                        float expectedCenterY = LLModularBuildingAssembly.GetFloorModuleCenterY(assignedFloor.elevationLocal, oriented.y);
                        if (Mathf.Abs(row.boundsCenter.y - expectedCenterY) > 0.05f)
                        { Debug.LogError($"Floor module {row.module.name} center Y {row.boundsCenter.y:F3} does not place its measured top at floor {row.floorIndex} elevation {assignedFloor.elevationLocal:F3} (expected center {expectedCenterY:F3})."); return; }
                    }
                    LLModularBuildingPiece piece = PlaceModule(row.module, root.transform, row.boundsCenter, row.quarterTurns);
                    LLModularBuildingPlacement placement = piece.GetComponent<LLModularBuildingPlacement>();
                    placement.Configure(piece, row.floorIndex,
                        new Vector2Int(Mathf.RoundToInt(row.boundsCenter.x / cellSize), Mathf.RoundToInt(row.boundsCenter.z / cellSize)), row.quarterTurns);
                    if (row.entrance) ConfigureEntrance(authority, piece.transform, row.boundsCenter, row.quarterTurns.y, row.floorIndex, row.clearWidth, cellSize, floors.First(f => f.floorIndex == row.floorIndex).elevationLocal);
                }

                if (!assembly.TryValidate(out List<string> assemblyIssues))
                {
                    Debug.LogError("Assembly was not saved because validation failed: " + string.Join(" | ", assemblyIssues));
                    return;
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                if (saved == null) { Debug.LogError("Could not save modular assembly prefab."); return; }
                AssetDatabase.SaveAssets();
                Debug.Log($"[Modular Buildings] Created assembly prefab {outputPath} with {rows.Count} module placements, {floors.Count} authored floors and {entranceCount} entrance pair(s). Confirm floor triangles, door openings and physical paths on this specific assembly before gameplay use.", saved);
                EditorGUIUtility.PingObject(saved);
            }
            finally
            {
                if (root != null) DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [MenuItem("Land & Ledgers/Buildings/Validate Selected Modular Assembly")]
        private static void ValidateSelectedAssembly()
        {
            LLModularBuildingAssembly assembly = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<LLModularBuildingAssembly>() : null;
            if (assembly == null) { Debug.LogWarning("Select the root of an LLModularBuildingAssembly."); return; }
            bool valid = assembly.TryValidate(out List<string> issues);
            string message = valid ? $"'{assembly.name}' passes its authored module, footprint and interior-floor checks." : string.Join("\n", issues);
            if (valid) Debug.Log("[Modular Buildings] " + message, assembly);
            else Debug.LogWarning("[Modular Buildings] " + message, assembly);
        }

        private static void CreateHouse01Example(LLBuildingModuleCatalog catalog)
        {
            if (catalog == null) { Debug.LogError("Build the Building Materials catalog first."); return; }
            string family = "House01";
            LLModularBuildingPiece Find(LLModuleCategory category, Func<LLModularBuildingPiece, bool> predicate)
                => catalog.Modules.FirstOrDefault(p => p != null && p.category == category
                    && string.Equals(p.extractedFromBuildingName, family, StringComparison.OrdinalIgnoreCase) && predicate(p));

            LLModularBuildingPiece floor = Find(LLModuleCategory.Floor, p => p.sourceMeshName.Contains("Floor", StringComparison.OrdinalIgnoreCase)
                && p.gridWidth == 1 && p.gridDepth == 1 && IsHorizontalWalkableSurface(p.gameObject, Quaternion.Euler(-90f, 0f, 0f)));
            LLModularBuildingPiece solid = Find(LLModuleCategory.Wall, p => p.name.Contains("Wall 2 A 008 Wood Wall", StringComparison.OrdinalIgnoreCase));
            LLModularBuildingPiece door = Find(LLModuleCategory.Wall, p => p.name.Contains("Door C 001", StringComparison.OrdinalIgnoreCase) && !p.name.EndsWith("Wood Wall", StringComparison.OrdinalIgnoreCase));
            LLModularBuildingPiece window = Find(LLModuleCategory.Wall, p => p.name.Contains("Wind A 001 Window", StringComparison.OrdinalIgnoreCase));
            LLModularBuildingPiece roof = Find(LLModuleCategory.Roof, p => p.sourceMeshName.Contains("Roof", StringComparison.OrdinalIgnoreCase));
            GameObject shelfPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Environment/Props/Shelf.prefab");
            if (floor == null || solid == null || door == null || window == null || roof == null || shelfPrefab == null)
            {
                Debug.LogError($"House01 example was not generated. Required found? floor={floor != null}, solid wall={solid != null}, door wall={door != null}, window wall={window != null}, roof={roof != null}, installed shelf asset={shelfPrefab != null}. No fallback assets were fabricated.");
                return;
            }

            string doorLimitation = GetDoorAccessLimitation(door.gameObject, 0.8f, 1.9f);
            if (!string.IsNullOrEmpty(doorLimitation))
            {
                Debug.LogError($"House01 example was not generated: {doorLimitation}");
                return;
            }

            string directory = Path.GetDirectoryName(ExamplePath).Replace('\\', '/');
            EnsureFolders(directory);
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = new("House01 Modular 4x4 Example");
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one;
                LLModularBuildingAssembly assembly = root.AddComponent<LLModularBuildingAssembly>();
                BuildingFootprintAuthority authority = root.AddComponent<BuildingFootprintAuthority>();
                List<LLModularBuildingFloor> exampleFloors = new() { new() { floorIndex = 0, displayName = "Ground Floor", elevationLocal = 0.4f } };
                ConfigureAuthority(authority, exampleFloors, 2, 2, 2f, 2f, 4f, 4f);
                assembly.Configure("example.house01.modular.4x4", family, 2f, 2f, new Vector2Int(2, 2),
                    exampleFloors);

                // Align by measured renderer bounds, not guessed prefab pivots. Coordinates are in meters.
                Vector3 floorSize = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(GetRootLocalRendererBounds(floor.gameObject).size, new Vector3Int(3, 0, 0));
                float floorCenterY = LLModularBuildingAssembly.GetFloorModuleCenterY(exampleFloors[0].elevationLocal, floorSize.y);
                PlaceModule(floor, root.transform, new Vector3(-1f, floorCenterY, -1f), new Vector3Int(3, 0, 0));
                PlaceModule(floor, root.transform, new Vector3( 1f, floorCenterY, -1f), new Vector3Int(3, 0, 0));
                PlaceModule(floor, root.transform, new Vector3(-1f, floorCenterY,  1f), new Vector3Int(3, 0, 0));
                PlaceModule(floor, root.transform, new Vector3( 1f, floorCenterY,  1f), new Vector3Int(3, 0, 0));

                PlaceModule(door, root.transform, new Vector3(-1f, 1.4f, 2f), Vector3Int.zero);
                PlaceModule(solid, root.transform, new Vector3( 1f, 1.4f, 2f), Vector3Int.zero);
                PlaceModule(solid, root.transform, new Vector3(-1f, 1.4f,-2f), Vector3Int.zero);
                PlaceModule(solid, root.transform, new Vector3( 1f, 1.4f,-2f), Vector3Int.zero);
                PlaceModule(window, root.transform, new Vector3(2f, 1.4f, 1f), new Vector3Int(0, 1, 0));
                PlaceModule(solid, root.transform, new Vector3(2f, 1.4f,-1f), new Vector3Int(0, 1, 0));
                PlaceModule(solid, root.transform, new Vector3(-2f,1.4f, 1f), new Vector3Int(0, 1, 0));
                PlaceModule(solid, root.transform, new Vector3(-2f,1.4f,-1f), new Vector3Int(0, 1, 0));
                // This is a reusable roof section, not a claim that one section closes the roof.
                PlaceModule(roof, root.transform, new Vector3(0f,2.65f,0f), Vector3Int.zero);
                LLModularBuildingPiece doorInstance = root.GetComponentsInChildren<LLModularBuildingPiece>(true)
                    .FirstOrDefault(p => p.name.Contains("Door C 001", StringComparison.OrdinalIgnoreCase));
                if (doorInstance == null) { Debug.LogError("Example door placement was not instantiated."); return; }
                ConfigureEntrance(authority, doorInstance.transform, new Vector3(-1f, 1.4f, 2f), 0, 0, 0.8f, 2f, 0.4f);

                GameObject shelf = (GameObject)PrefabUtility.InstantiatePrefab(shelfPrefab, preview);
                shelf.name = "Installed Shelf Example";
                shelf.transform.SetParent(root.transform, false);
                Bounds shelfBounds = GetRootLocalRendererBounds(shelf);
                shelf.transform.localPosition = new Vector3(1.15f - shelfBounds.center.x, 0.4f - shelfBounds.min.y, -0.95f - shelfBounds.center.z);
                BuildingEquipmentPlacement equipment = shelf.GetComponent<BuildingEquipmentPlacement>() ?? shelf.AddComponent<BuildingEquipmentPlacement>();
                equipment.Configure("example.house01.shelf.01", "furniture.shelf", "Example Shelf", 0);

                string outputPath = AssetDatabase.GenerateUniqueAssetPath(ExamplePath);
                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                if (saved == null) { Debug.LogError("Prefab save failed for modular example."); return; }
                AssetDatabase.SaveAssets();
                Debug.Log($"[Modular Buildings] Created {outputPath} from existing {family} modules. Floor geometry, 2m module grid, door/window wall panels, roof section and shelf obstruction are included. Validate door collider passage and pathing before production use.", saved);
                EditorGUIUtility.PingObject(saved);
            }
            finally
            {
                if (root != null) DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        private static LLModularBuildingPiece PlaceModule(LLModularBuildingPiece source, Transform parent, Vector3 targetBoundsCenter, Vector3Int axisQuarterTurns)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject, parent.gameObject.scene);
            instance.transform.SetParent(parent, false);
            axisQuarterTurns = new Vector3Int((axisQuarterTurns.x % 4 + 4) % 4, (axisQuarterTurns.y % 4 + 4) % 4, (axisQuarterTurns.z % 4 + 4) % 4);
            Quaternion rotation = Quaternion.Euler(new Vector3(axisQuarterTurns.x * 90f, axisQuarterTurns.y * 90f, axisQuarterTurns.z * 90f));
            instance.transform.localRotation = rotation;
            Bounds bounds = GetRootLocalRendererBounds(instance);
            instance.transform.localPosition = targetBoundsCenter - rotation * bounds.center;
            LLModularBuildingPiece piece = instance.GetComponent<LLModularBuildingPiece>();
            LLModularBuildingPlacement placement = instance.GetComponent<LLModularBuildingPlacement>() ?? instance.AddComponent<LLModularBuildingPlacement>();
            placement.Configure(piece, 0,
                new Vector2Int(Mathf.RoundToInt(targetBoundsCenter.x / source.cellSizeMeters), Mathf.RoundToInt(targetBoundsCenter.z / source.cellSizeMeters)),
                axisQuarterTurns);
            return piece;
        }

        private static bool IsHorizontalWalkableSurface(GameObject root, Quaternion adapterRotation)
        {
            Bounds bounds = GetRootLocalRendererBounds(root);
            Vector3 rotatedSize = LLModularBuildingAssembly.RotateAxisAlignedBoundsSize(bounds.size, new Vector3Int(
                Mathf.RoundToInt(adapterRotation.eulerAngles.x / 90f), Mathf.RoundToInt(adapterRotation.eulerAngles.y / 90f), Mathf.RoundToInt(adapterRotation.eulerAngles.z / 90f)));
            return rotatedSize.y <= 0.5f && rotatedSize.x >= 1f && rotatedSize.z >= 1f;
        }

        private static string GetDoorAccessLimitation(GameObject root, float minimumWidth, float minimumHeight)
        {
            Bounds wall = GetRootLocalRendererBounds(root);
            Renderer leaf = root.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r =>
                r.name.Contains("Wall45", StringComparison.OrdinalIgnoreCase) || r.name.Equals("Door", StringComparison.OrdinalIgnoreCase));
            if (leaf == null) return $"{root.name} has no separable door-leaf geometry to measure against the requested {minimumWidth:F2}m × {minimumHeight:F2}m aperture.";
            Bounds leafBounds = leaf.bounds;
            Vector3 leafLocalSize = leafBounds.size;
            if (leafLocalSize.x < minimumWidth || leafLocalSize.y < minimumHeight)
                return $"{root.name} door leaf measures approximately {leafLocalSize.x:F2}m wide × {leafLocalSize.y:F2}m high (requested clear aperture at least {minimumWidth:F2}m × {minimumHeight:F2}m).";

            // A box collider spanning the wall panel blocks the opening even when the mesh looks like a door.
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 1 && colliders[0] is BoxCollider box)
            {
                Vector3 colliderSize = Vector3.Scale(box.size, box.transform.lossyScale);
                if (colliderSize.y >= wall.size.y * 0.8f && colliderSize.x >= wall.size.x * 0.8f)
                    return $"{root.name} has a full-panel BoxCollider ({colliderSize.x:F2}m × {colliderSize.y:F2}m); it closes the visual opening.";
            }
            // Current metadata does not encode aperture bounds, so do not infer clear space from the asset name.
            return $"{root.name} lacks authored clear-aperture metadata and a verified collider-free opening. No route is inferred from the prefab name.";
        }

        private static void ConfigureEntrance(BuildingFootprintAuthority authority, Transform source,
            Vector3 boundsCenter, int quarterTurns, int floorIndex, float clearWidth, float cellSize, float floorElevation)
        {
            Vector3 outward = Quaternion.Euler(0f, quarterTurns * 90f, 0f) * Vector3.forward;
            Vector3 center = new(boundsCenter.x, floorElevation, boundsCenter.z);
            SerializedObject serialized = new(authority);
            SerializedProperty entrances = serialized.FindProperty("entrancePairs");
            int index = entrances.arraySize++;
            SerializedProperty entrance = entrances.GetArrayElementAtIndex(index);
            entrance.FindPropertyRelative("id").stringValue = $"entrance-{index + 1}";
            entrance.FindPropertyRelative("sourceTransform").objectReferenceValue = source;
            entrance.FindPropertyRelative("indoorPositionLocal").vector3Value = center - outward * Mathf.Max(0.2f, cellSize * 0.25f);
            entrance.FindPropertyRelative("outdoorApproachLocal").vector3Value = center + outward * Mathf.Max(0.5f, cellSize * 0.425f);
            entrance.FindPropertyRelative("clearWidthMeters").floatValue = Mathf.Max(0.2f, clearWidth);
            entrance.FindPropertyRelative("floorIndex").intValue = floorIndex;
            entrance.FindPropertyRelative("functionalSpaceId").stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(authority);
        }

        private static void ConfigureAuthority(BuildingFootprintAuthority authority, List<LLModularBuildingFloor> floors,
            int widthCells, int depthCells, float cellSize, float frontZ, float width, float depth)
        {
            SerializedObject serialized = new(authority);
            serialized.FindProperty("authoringCellSizeMeters").floatValue = cellSize;
            serialized.FindProperty("frontLeftCornerLocal").vector3Value = new Vector3(-width * 0.5f, 0f, frontZ);
            serialized.FindProperty("frontRightCornerLocal").vector3Value = new Vector3(width * 0.5f, 0f, frontZ);
            serialized.FindProperty("backRightCornerLocal").vector3Value = new Vector3(width * 0.5f, 0f, -depth * 0.5f);
            serialized.FindProperty("backLeftCornerLocal").vector3Value = new Vector3(-width * 0.5f, 0f, -depth * 0.5f);
            serialized.FindProperty("frontDoorLocalPoint").vector3Value = new Vector3(0f, 0f, frontZ + cellSize * 0.175f);
            SerializedProperty floorsProperty = serialized.FindProperty("interiorFloors");
            floorsProperty.arraySize = floors.Count;
            for (int i = 0; i < floors.Count; i++)
            {
                SerializedProperty floor = floorsProperty.GetArrayElementAtIndex(i);
                floor.FindPropertyRelative("displayName").stringValue = floors[i].displayName;
                floor.FindPropertyRelative("floorIndex").intValue = floors[i].floorIndex;
                floor.FindPropertyRelative("floorHeightLocal").floatValue = floors[i].elevationLocal;
                floor.FindPropertyRelative("deriveWalkableFromFloorGeometry").boolValue = true;
                floor.FindPropertyRelative("floorGeometryHeightTolerance").floatValue = 0.08f;
                floor.FindPropertyRelative("walkableAreas").arraySize = 0;
                floor.FindPropertyRelative("exclusionAreas").arraySize = 0;
            }
            serialized.FindProperty("entrancePairs").arraySize = 0;
            serialized.FindProperty("activityLocations").arraySize = 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(authority);
        }

        private static Bounds GetRootLocalRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            Bounds combined = default;
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            foreach (Renderer renderer in renderers)
            {
                Bounds localBounds = renderer.localBounds;
                Matrix4x4 matrix = toRoot * renderer.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = localBounds.center + Vector3.Scale(localBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    point = matrix.MultiplyPoint3x4(point);
                    if (!hasBounds) { combined = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else combined.Encapsulate(point);
                }
            }
            return combined;
        }

        private static void EnsureFolders(string path)
        {
            string current = "Assets";
            foreach (string segment in path.Split('/').Skip(1))
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }
    }

    [CustomEditor(typeof(LLModularBuildingAssembly))]
    public sealed class LLModularBuildingAssemblyInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Validate Assembly"))
            {
                LLModularBuildingAssembly assembly = (LLModularBuildingAssembly)target;
                bool valid = assembly.TryValidate(out List<string> issues);
                if (valid) EditorGUILayout.HelpBox("Module family, scale, root footprint and geometry-derived floor checks passed.", MessageType.Info);
                else foreach (string issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
            }
        }
    }
}
#endif
