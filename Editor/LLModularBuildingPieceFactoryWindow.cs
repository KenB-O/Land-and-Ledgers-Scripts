
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandAndLedgers.Buildings.Editor
{
    public sealed class LLModularBuildingPieceFactoryWindow : EditorWindow
    {
        private const string DefaultKitRootFolder = "Assets/Environment/Building Materials";
        private const string DefaultWesternBuildingsFolder = "Assets/Asset Packs/Western/Models/Buildings";
        private const string DefaultWesternMaterialsFolder = "Assets/Asset Packs/Western/Models/Mats & Textures/Materials";
        private const string DefaultWesternTexturesFolder = "Assets/Asset Packs/Western/Models/Textures";
        private const string SourceFolderName = "Sources";
        private const string PrefabFolderName = "Prefabs";
        private const string AutoExtractedFolderName = "Auto Extracted";

        private static readonly Regex LODRegex = new Regex(@"(?:^|[ _-])LOD[ _-]?(0|1)(?:$|[ _-])|(?:^|[ _-])(0|1)(?:$|[ _-])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SizeRegex = new Regex(@"(?<!\d)(\d+)\s*[xX]\s*(\d+)(?!\d)", RegexOptions.Compiled);
        private static readonly Regex NumberTokenRegex = new Regex(@"(?:^|[ _-])\d{1,3}(?:$|[ _-])", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new Regex(@"[A-Z]?[a-z]+|[A-Z]+(?![a-z])|\d+", RegexOptions.Compiled);

        [Serializable]
        private sealed class PairState
        {
            public string familyKey;
            public string displayName;
            public string sourceFolder;
            public string lod0Path;
            public string lod1Path;
            public string targetPrefabPath;
            public bool isValid;
            public bool hasExistingPrefab;
            public string note;
        }

        [Serializable]
        private sealed class PieceOverrideState
        {
            public bool hasOverride;
            public LLSizeOverrideMode sizeOverrideMode;
            public float overrideTargetHorizontalSpanMeters = 2f;
            public int overrideGridWidth = 1;
            public int overrideGridDepth = 1;
            public bool overrideCategoryAndCollider;
            public LLModuleCategory overrideCategory = LLModuleCategory.Other;
            public LLColliderAuthoringMode overrideColliderMode = LLColliderAuthoringMode.Auto;
        }

        private sealed class ExtractEntry
        {
            public Transform transform;
            public string cleanObjectName;
            public string cleanMeshName;
            public string cleanMaterialName;
            public string familyKey;
            public string prettyName;
            public Bounds bounds;
            public Mesh sharedMesh;
        }

        private enum BatchMode
        {
            CreateMissingOnly,
            CreateOrUpdateAll
        }

        private Vector2 scroll;
        private readonly List<PairState> scanResults = new List<PairState>();
        private readonly List<string> batchNotes = new List<string>();

        private string kitRootFolder = DefaultKitRootFolder;
        private string westernBuildingsFolder = DefaultWesternBuildingsFolder;
        private string westernMaterialsFolder = DefaultWesternMaterialsFolder;
        private string westernTexturesFolder = DefaultWesternTexturesFolder;
        private string manualSaveFolder = DefaultKitRootFolder + "/Manual";

        private BatchMode batchMode = BatchMode.CreateOrUpdateAll;
        private bool cleanGeneratedOutputFirst = true;
        private bool relinkMaterialsFromWesternFolder = true;
        private bool allowLod0FallbackForMissingLod1 = true;
        private bool keepGeneratedSourcePrefabs = true;
        private bool buildFinishedPiecePrefabs = true;
        private bool normalizeSourcesToGrid = true;
        private bool useGeometryDrivenSizing = true;
        private bool centerOnBounds = true;
        private bool groundBottomOnYZero = true;
        private bool createCollisionRoot = true;

        private float cellSizeMeters = 2f;
        private float lod0ScreenRelativeHeight = 0.28f;
        private float lod1ScreenRelativeHeight = 0f;
        private float lodFadeWidth = 0.03f;
        private bool neverCullWhileVisible = true;

        private GameObject manualBuildingSource;
        private GameObject manualLod0Source;
        private GameObject manualLod1Source;

        private Dictionary<string, Material> materialLookup;

        [MenuItem("Land & Ledgers/Buildings/Modular Piece Factory")]
        private static void OpenWindow()
        {
            LLModularBuildingPieceFactoryWindow window = GetWindow<LLModularBuildingPieceFactoryWindow>();
            window.titleContent = new GUIContent("LL Piece Factory");
            window.minSize = new Vector2(820f, 700f);
            window.Show();
        }

        [MenuItem("Land & Ledgers/Buildings/Apply Overrides To Selected Prefabs")]
        private static void ApplyOverridesToSelectedPrefabsMenu()
        {
            string[] selectedPrefabPaths = Selection.assetGUIDs
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            int updatedCount = 0;
            for (int i = 0; i < selectedPrefabPaths.Length; i++)
            {
                if (ApplyOverridesToExistingPrefabAssetStatic(selectedPrefabPaths[i]))
                {
                    updatedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Land & Ledgers] Applied modular prefab overrides to " + updatedCount + " prefab(s).");
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawPaths();
            DrawSettings();
            DrawWesternBatch();
            DrawSourceSync();
            DrawManualSection();
            DrawOverrideTools();
            DrawResults();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPaths()
        {
            EditorGUILayout.LabelField("Folder Paths", EditorStyles.boldLabel);
            kitRootFolder = EditorGUILayout.TextField("Kit Root Folder", kitRootFolder);
            westernBuildingsFolder = EditorGUILayout.TextField("Western Buildings Folder", westernBuildingsFolder);
            westernMaterialsFolder = EditorGUILayout.TextField("Western Materials Folder", westernMaterialsFolder);
            westernTexturesFolder = EditorGUILayout.TextField("Western Textures Folder", westernTexturesFolder);
            manualSaveFolder = EditorGUILayout.TextField("Manual Save Folder", manualSaveFolder);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset To Requested Paths"))
            {
                kitRootFolder = DefaultKitRootFolder;
                westernBuildingsFolder = DefaultWesternBuildingsFolder;
                westernMaterialsFolder = DefaultWesternMaterialsFolder;
                westernTexturesFolder = DefaultWesternTexturesFolder;
                manualSaveFolder = DefaultKitRootFolder + "/Manual";
            }

            if (GUILayout.Button("Ping Kit Root"))
            {
                PingAssetPath(kitRootFolder);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Automation Settings", EditorStyles.boldLabel);
            cleanGeneratedOutputFirst = EditorGUILayout.Toggle("Clean Auto Extracted First", cleanGeneratedOutputFirst);
            relinkMaterialsFromWesternFolder = EditorGUILayout.Toggle("Relink Materials From Western Folder", relinkMaterialsFromWesternFolder);
            allowLod0FallbackForMissingLod1 = EditorGUILayout.Toggle("Allow LOD0 Fallback For Missing LOD1", allowLod0FallbackForMissingLod1);
            keepGeneratedSourcePrefabs = EditorGUILayout.Toggle("Keep Generated Source Prefabs", keepGeneratedSourcePrefabs);
            buildFinishedPiecePrefabs = EditorGUILayout.Toggle("Build Finished Piece Prefabs", buildFinishedPiecePrefabs);
            batchMode = (BatchMode)EditorGUILayout.EnumPopup("Batch Mode", batchMode);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Normalization / LOD", EditorStyles.miniBoldLabel);
            cellSizeMeters = EditorGUILayout.FloatField("Cell Size Meters", cellSizeMeters);
            normalizeSourcesToGrid = EditorGUILayout.Toggle("Normalize Sources To Grid", normalizeSourcesToGrid);
            useGeometryDrivenSizing = EditorGUILayout.Toggle("Use Geometry-Driven Sizing (legacy)", useGeometryDrivenSizing);
            centerOnBounds = EditorGUILayout.Toggle("Center On Bounds", centerOnBounds);
            groundBottomOnYZero = EditorGUILayout.Toggle("Ground Bottom On Y=0", groundBottomOnYZero);
            createCollisionRoot = EditorGUILayout.Toggle("Create Collision Root", createCollisionRoot);
            lod0ScreenRelativeHeight = EditorGUILayout.Slider("LOD0 Height", lod0ScreenRelativeHeight, 0.01f, 1f);
            lod1ScreenRelativeHeight = EditorGUILayout.Slider("LOD1 Height", lod1ScreenRelativeHeight, 0f, 1f);
            lodFadeWidth = EditorGUILayout.Slider("LOD Fade Width", lodFadeWidth, 0f, 1f);
            neverCullWhileVisible = EditorGUILayout.Toggle("Never Cull While Visible", neverCullWhileVisible);
        }

        private void DrawWesternBatch()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Western Building Batch", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This is the main clean-slate batch. It scans every building FBX in the Western Buildings folder, extracts repeated child families from the imported model, relinks materials from the Western Materials folder when possible, writes LOD0 / LOD1 source prefabs, and builds finished Land & Ledgers modular prefabs into Auto Extracted.",
                MessageType.Info);

            manualBuildingSource = (GameObject)EditorGUILayout.ObjectField("Single Building Preview", manualBuildingSource, typeof(GameObject), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Process All Western Buildings"))
            {
                ProcessAllWesternBuildings();
            }

            using (new EditorGUI.DisabledScope(manualBuildingSource == null))
            {
                if (GUILayout.Button("Process Selected Building"))
                {
                    ProcessSingleBuilding(AssetDatabase.GetAssetPath(manualBuildingSource), true);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSourceSync()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Source Sync", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This keeps the older Sources -> Prefabs flow. It scans every Sources folder under the kit root, pairs LOD0 and LOD1 prefabs by name, then builds finished modular prefabs in sibling Prefabs folders.",
                MessageType.None);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan Sources"))
            {
                ScanSources();
            }

            using (new EditorGUI.DisabledScope(scanResults.Count == 0))
            {
                if (GUILayout.Button("Build From Scanned Sources"))
                {
                    BuildFromScannedSources();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawManualSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Manual Piece Builder", EditorStyles.boldLabel);
            manualLod0Source = (GameObject)EditorGUILayout.ObjectField("LOD0 Source", manualLod0Source, typeof(GameObject), false);
            manualLod1Source = (GameObject)EditorGUILayout.ObjectField("LOD1 Source", manualLod1Source, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(manualLod0Source == null || manualLod1Source == null))
            {
                if (GUILayout.Button("Create Manual Piece Prefab"))
                {
                    EnsureFolderChain(manualSaveFolder);
                    string prettyName = SanitizePrefabName(StripLodSuffix(manualLod0Source.name));
                    string prefabPath = CombineAssetPath(manualSaveFolder, prettyName + ".prefab");
                    BuildPiecePrefabFromSourcePaths(
                        prettyName,
                        string.Empty,
                        AssetDatabase.GetAssetPath(manualLod0Source),
                        AssetDatabase.GetAssetPath(manualLod1Source),
                        prefabPath,
                        prettyName,
                        prettyName,
                        string.Empty);
                }
            }
        }

        private void DrawOverrideTools()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Override / Repair Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Conservative mode: pieces now default to a 2m target span. For exceptions, edit the LLModularBuildingPiece component on the generated prefab and set Size Override Mode. Then use the button below or the top menu action to reapply the override without rerunning the full extraction batch.",
                MessageType.Info);

            if (GUILayout.Button("Apply Overrides To Selected Prefabs"))
            {
                ApplyOverridesToSelectedPrefabsMenu();
            }
        }

        private void DrawResults()
        {
            if (scanResults.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Scanned Source Pairs", EditorStyles.boldLabel);
                int drawCount = Mathf.Min(scanResults.Count, 20);
                for (int i = 0; i < drawCount; i++)
                {
                    PairState state = scanResults[i];
                    EditorGUILayout.HelpBox(
                        state.displayName + "\n" +
                        "LOD0: " + ShortAsset(state.lod0Path) + "\n" +
                        "LOD1: " + ShortAsset(state.lod1Path) + "\n" +
                        "Prefab: " + ShortAsset(state.targetPrefabPath) + "\n" +
                        "Note: " + state.note,
                        state.isValid ? MessageType.None : MessageType.Warning);
                }
            }

            if (batchNotes.Count > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Last Batch Notes", EditorStyles.boldLabel);
                int drawCount = Mathf.Min(batchNotes.Count, 16);
                for (int i = 0; i < drawCount; i++)
                {
                    EditorGUILayout.HelpBox(batchNotes[i], MessageType.None);
                }
            }
        }

        private void ProcessAllWesternBuildings()
        {
            batchNotes.Clear();
            string[] buildingPaths = FindBuildingFbxPaths();
            if (buildingPaths.Length == 0)
            {
                batchNotes.Add("No FBX files were found under " + westernBuildingsFolder + ".");
                return;
            }

            try
            {
                if (cleanGeneratedOutputFirst)
                {
                    string generatedRoot = CombineAssetPath(kitRootFolder, AutoExtractedFolderName);
                    if (AssetDatabase.IsValidFolder(generatedRoot))
                    {
                        AssetDatabase.DeleteAsset(generatedRoot);
                    }
                }

                materialLookup = null;
                for (int i = 0; i < buildingPaths.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("Land & Ledgers", "Processing " + Path.GetFileNameWithoutExtension(buildingPaths[i]), (i + 1f) / buildingPaths.Length);
                    ProcessSingleBuilding(buildingPaths[i], false);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            batchNotes.Add("Processed " + buildingPaths.Length + " building FBX assets from " + westernBuildingsFolder + ".");
            ScanSources();
        }

        private void ProcessSingleBuilding(string buildingPath, bool rescanAfter)
        {
            if (string.IsNullOrEmpty(buildingPath))
            {
                return;
            }

            GameObject buildingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(buildingPath);
            if (buildingAsset == null)
            {
                batchNotes.Add("Skipped invalid building asset path: " + buildingPath);
                return;
            }

            GameObject previewRoot = InstantiateForPreview(buildingAsset);
            try
            {
                if (relinkMaterialsFromWesternFolder)
                {
                    RelinkMaterialsRecursive(previewRoot);
                }

                Transform lod0Root = FindImportedLodRoot(previewRoot.transform, 0);
                Transform lod1Root = FindImportedLodRoot(previewRoot.transform, 1);
                if (lod0Root == null)
                {
                    lod0Root = previewRoot.transform;
                }

                Dictionary<string, List<ExtractEntry>> lod0Families = CollectExtractEntries(lod0Root);
                Dictionary<string, List<ExtractEntry>> lod1Families = lod1Root != null
                    ? CollectExtractEntries(lod1Root)
                    : new Dictionary<string, List<ExtractEntry>>(StringComparer.OrdinalIgnoreCase);

                string buildingName = SanitizePrefabName(buildingAsset.name);
                string buildingRoot = CombineAssetPath(CombineAssetPath(kitRootFolder, AutoExtractedFolderName), buildingName);
                string sourceFolder = CombineAssetPath(buildingRoot, SourceFolderName);
                string prefabFolder = CombineAssetPath(buildingRoot, PrefabFolderName);

                EnsureFolderChain(buildingRoot);
                if (keepGeneratedSourcePrefabs)
                {
                    EnsureFolderChain(sourceFolder);
                }
                if (buildFinishedPiecePrefabs)
                {
                    EnsureFolderChain(prefabFolder);
                }

                int extractedCount = 0;
                foreach (KeyValuePair<string, List<ExtractEntry>> family in lod0Families.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    ExtractEntry lod0Entry = SelectRepresentative(family.Value);
                    ExtractEntry lod1Entry = null;
                    if (lod1Families.TryGetValue(family.Key, out List<ExtractEntry> candidates))
                    {
                        lod1Entry = FindBestMatchingEntry(lod0Entry, candidates);
                    }

                    if (lod1Entry == null && !allowLod0FallbackForMissingLod1)
                    {
                        continue;
                    }

                    string prettyName = lod0Entry.prettyName;
                    string lod0SourcePath = keepGeneratedSourcePrefabs ? CombineAssetPath(sourceFolder, prettyName + " LOD 0.prefab") : CreateTemporarySourcePrefab(lod0Entry, 0);
                    string lod1SourcePath = keepGeneratedSourcePrefabs ? CombineAssetPath(sourceFolder, prettyName + " LOD 1.prefab") : CreateTemporarySourcePrefab(lod1Entry ?? lod0Entry, lod1Entry == null ? 0 : 1);

                    if (keepGeneratedSourcePrefabs)
                    {
                        CreateSourcePrefabFromEntry(lod0Entry, lod0SourcePath, 0);
                        CreateSourcePrefabFromEntry(lod1Entry ?? lod0Entry, lod1SourcePath, lod1Entry == null ? 0 : 1);
                    }

                    if (buildFinishedPiecePrefabs)
                    {
                        string targetPrefabPath = CombineAssetPath(prefabFolder, prettyName + ".prefab");
                        BuildPiecePrefabFromSourcePaths(
                            prettyName,
                            buildingPath,
                            lod0SourcePath,
                            lod1SourcePath,
                            targetPrefabPath,
                            lod0Entry.cleanObjectName,
                            lod0Entry.cleanMaterialName,
                            lod0Entry.cleanMeshName);
                    }

                    extractedCount++;
                }

                batchNotes.Add("Processed " + buildingAsset.name + ": extracted " + extractedCount + " piece families.");
            }
            finally
            {
                DestroyImmediate(previewRoot);
            }

            AssetDatabase.SaveAssets();
            if (rescanAfter)
            {
                ScanSources();
            }
        }

        private void ScanSources()
        {
            scanResults.Clear();
            List<string> sourceFolders = FindSourceFolders();
            Dictionary<string, PairState> pairs = new Dictionary<string, PairState>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sourceFolders.Count; i++)
            {
                string folder = sourceFolders[i];
                string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { folder });
                for (int j = 0; j < guids.Length; j++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[j]);
                    if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(path);
                    string familyKey = BuildPairFamilyKey(fileName, out int lodIndex);
                    if (string.IsNullOrEmpty(familyKey))
                    {
                        continue;
                    }

                    string pairMapKey = folder + "|" + familyKey;
                    if (!pairs.TryGetValue(pairMapKey, out PairState state))
                    {
                        string siblingPrefabFolder = folder.Replace("/" + SourceFolderName, "/" + PrefabFolderName);
                        state = new PairState
                        {
                            familyKey = familyKey,
                            displayName = SanitizePrefabName(familyKey),
                            sourceFolder = folder,
                            targetPrefabPath = CombineAssetPath(siblingPrefabFolder, SanitizePrefabName(familyKey) + ".prefab")
                        };
                        pairs.Add(pairMapKey, state);
                    }

                    if (lodIndex == 0)
                    {
                        state.lod0Path = path;
                    }
                    else if (lodIndex == 1)
                    {
                        state.lod1Path = path;
                    }
                }
            }

            foreach (PairState state in pairs.Values.OrderBy(x => x.displayName, StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(state.lod0Path))
                {
                    state.isValid = false;
                    state.note = "Missing LOD0 source.";
                }
                else if (string.IsNullOrEmpty(state.lod1Path))
                {
                    state.isValid = allowLod0FallbackForMissingLod1;
                    state.note = allowLod0FallbackForMissingLod1 ? "LOD1 missing; LOD0 fallback will be used." : "Missing LOD1 source.";
                }
                else
                {
                    state.isValid = true;
                    state.note = "Ready.";
                }

                state.hasExistingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(state.targetPrefabPath) != null;
                scanResults.Add(state);
            }
        }

        private void BuildFromScannedSources()
        {
            int builtCount = 0;
            for (int i = 0; i < scanResults.Count; i++)
            {
                PairState state = scanResults[i];
                if (!state.isValid)
                {
                    continue;
                }
                if (batchMode == BatchMode.CreateMissingOnly && state.hasExistingPrefab)
                {
                    continue;
                }

                EnsureFolderChain(Path.GetDirectoryName(state.targetPrefabPath).Replace("\\", "/"));
                string lod1Path = string.IsNullOrEmpty(state.lod1Path) ? state.lod0Path : state.lod1Path;
                BuildPiecePrefabFromSourcePaths(
                    state.displayName,
                    string.Empty,
                    state.lod0Path,
                    lod1Path,
                    state.targetPrefabPath,
                    state.displayName,
                    state.displayName,
                    string.Empty);
                builtCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            batchNotes.Add("Built or updated " + builtCount + " finished prefabs from scanned sources.");
            ScanSources();
        }

        private void CreateSourcePrefabFromEntry(ExtractEntry entry, string targetPath, int lodIndex)
        {
            if (entry == null || string.IsNullOrEmpty(targetPath))
            {
                return;
            }

            EnsureFolderChain(Path.GetDirectoryName(targetPath).Replace("\\", "/"));
            if (batchMode == BatchMode.CreateMissingOnly && AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) != null)
            {
                return;
            }

            GameObject sourceRoot = new GameObject(entry.prettyName + (lodIndex == 0 ? " LOD 0" : " LOD 1"));
            try
            {
                GameObject copy = Instantiate(entry.transform.gameObject, sourceRoot.transform);
                copy.name = "Visual";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one;
                StripToVisualOnly(copy);

                if (relinkMaterialsFromWesternFolder)
                {
                    RelinkMaterialsRecursive(sourceRoot);
                }

                float appliedScale;
                NormalizeVisualRoot(copy, entry.prettyName, out appliedScale);

                LLModularBuildingPiece piece = sourceRoot.AddComponent<LLModularBuildingPiece>();
                piece.pieceId = MakePieceId(entry.prettyName);
                piece.sourceKey = SanitizeKey(entry.prettyName);
                piece.sourceFamilyKey = entry.familyKey;
                piece.sourceMeshName = entry.cleanMeshName;
                piece.primaryMaterialName = entry.cleanMaterialName;
                piece.category = LLModularBuildingPiece.InferCategoryFromName(entry.prettyName);
                piece.colliderMode = LLModularBuildingPiece.InferColliderModeFromCategory(piece.category);
                piece.cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
                Vector2Int footprint = InferGridFootprint(entry.prettyName, GetRendererBounds(sourceRoot).size, piece.category);
                piece.gridWidth = Mathf.Max(1, footprint.x);
                piece.gridDepth = Mathf.Max(1, footprint.y);
                piece.gridHeight = 1;
                piece.normalizedToGrid = normalizeSourcesToGrid;
                piece.sourceHorizontalSpanMeters = Mathf.Max(entry.bounds.size.x, entry.bounds.size.z);
                piece.targetHorizontalSpanMeters = Mathf.Max(0.1f, cellSizeMeters);
                piece.appliedUniformScale = appliedScale;

                PrefabUtility.SaveAsPrefabAsset(sourceRoot, targetPath);
            }
            finally
            {
                DestroyImmediate(sourceRoot);
            }
        }

        private string CreateTemporarySourcePrefab(ExtractEntry entry, int lodIndex)
        {
            string tempFolder = CombineAssetPath(CombineAssetPath(kitRootFolder, AutoExtractedFolderName), "_Temp Sources");
            EnsureFolderChain(tempFolder);
            string targetPath = CombineAssetPath(tempFolder, entry.prettyName + (lodIndex == 0 ? " LOD 0.prefab" : " LOD 1.prefab"));
            CreateSourcePrefabFromEntry(entry, targetPath, lodIndex);
            return targetPath;
        }

        private void BuildPiecePrefabFromSourcePaths(
            string prettyName,
            string sourceBuildingPath,
            string lod0SourcePath,
            string lod1SourcePath,
            string targetPrefabPath,
            string sourceObjectName,
            string materialName,
            string meshName)
        {
            GameObject lod0Asset = AssetDatabase.LoadAssetAtPath<GameObject>(lod0SourcePath);
            GameObject lod1Asset = AssetDatabase.LoadAssetAtPath<GameObject>(lod1SourcePath);
            if (lod0Asset == null)
            {
                return;
            }
            if (lod1Asset == null)
            {
                lod1Asset = lod0Asset;
            }

            EnsureFolderChain(Path.GetDirectoryName(targetPrefabPath).Replace("\\", "/"));
            if (batchMode == BatchMode.CreateMissingOnly && AssetDatabase.LoadAssetAtPath<GameObject>(targetPrefabPath) != null)
            {
                return;
            }

            GameObject root = new GameObject(prettyName);
            try
            {
                Transform lod0Root = CreateChild(root.transform, "LOD0");
                Transform lod1Root = CreateChild(root.transform, "LOD1");
                Transform collisionRoot = createCollisionRoot ? CreateChild(root.transform, "Collision") : null;

                GameObject lod0Instance = (GameObject)PrefabUtility.InstantiatePrefab(lod0Asset);
                GameObject lod1Instance = (GameObject)PrefabUtility.InstantiatePrefab(lod1Asset);
                lod0Instance.transform.SetParent(lod0Root, false);
                lod1Instance.transform.SetParent(lod1Root, false);
                RemovePieceComponentRecursively(lod0Instance);
                RemovePieceComponentRecursively(lod1Instance);

                if (relinkMaterialsFromWesternFolder)
                {
                    RelinkMaterialsRecursive(root);
                }

                Bounds lod0Bounds = GetRendererBounds(lod0Root.gameObject);
                PieceOverrideState overrideState = LoadExistingOverrideState(targetPrefabPath);
                LLModuleCategory category = LLModularBuildingPiece.InferCategoryFromName(prettyName);
                Vector2Int footprint = InferGridFootprint(prettyName, lod0Bounds.size, category);
                LLColliderAuthoringMode colliderMode = LLModularBuildingPiece.InferColliderModeFromCategory(category);

                LLModularBuildingPiece piece = root.AddComponent<LLModularBuildingPiece>();
                piece.pieceId = MakePieceId(prettyName);
                piece.sourceKey = SanitizeKey(prettyName);
                piece.sourceFamilyKey = BuildPairFamilyKey(prettyName, out _);
                piece.sourceBuildingAssetPath = sourceBuildingPath;
                piece.extractedFromBuildingName = string.IsNullOrEmpty(sourceBuildingPath) ? string.Empty : Path.GetFileNameWithoutExtension(sourceBuildingPath);
                piece.sourceMeshName = meshName;
                piece.primaryMaterialName = materialName;
                piece.lod0AssetPath = lod0SourcePath;
                piece.lod1AssetPath = lod1SourcePath;
                piece.outputPrefabPath = targetPrefabPath;
                piece.category = category;
                piece.colliderMode = colliderMode;
                piece.cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
                piece.gridWidth = Mathf.Max(1, footprint.x);
                piece.gridDepth = Mathf.Max(1, footprint.y);
                piece.gridHeight = 1;
                piece.normalizedToGrid = normalizeSourcesToGrid;
                piece.sourceHorizontalSpanMeters = Mathf.Max(lod0Bounds.size.x, lod0Bounds.size.z);
                piece.targetHorizontalSpanMeters = Mathf.Max(0.1f, cellSizeMeters);
                piece.appliedUniformScale = 1f;
                piece.lod0Root = lod0Root;
                piece.lod1Root = lod1Root;
                piece.collisionRoot = collisionRoot;
                piece.lod0ScreenRelativeHeight = lod0ScreenRelativeHeight;
                piece.lod1ScreenRelativeHeight = lod1ScreenRelativeHeight;
                piece.lodFadeWidth = lodFadeWidth;
                piece.neverCullWhileVisible = neverCullWhileVisible;

                ApplyOverridesToPieceComponent(piece, overrideState);
                ApplySizeOverrideToPieceRoots(piece);

                if (piece.overrideCategoryAndCollider)
                {
                    piece.category = piece.overrideCategory;
                    piece.colliderMode = piece.overrideColliderMode == LLColliderAuthoringMode.Auto
                        ? LLModularBuildingPiece.InferColliderModeFromCategory(piece.overrideCategory)
                        : piece.overrideColliderMode;
                }

                if (collisionRoot != null)
                {
                    BuildCollisionRoot(lod0Root.gameObject, collisionRoot.gameObject, piece.colliderMode);
                }

                piece.RebuildLODGroup();

                PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath);
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private void BuildCollisionRoot(GameObject lod0Object, GameObject collisionRoot, LLColliderAuthoringMode mode)
        {
            if (collisionRoot == null || lod0Object == null)
            {
                return;
            }

            if (mode == LLColliderAuthoringMode.SingleBox)
            {
                Bounds bounds = GetRendererBounds(lod0Object);
                BoxCollider box = collisionRoot.AddComponent<BoxCollider>();
                box.center = collisionRoot.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size;
                return;
            }

            GameObject collisionCopy = Instantiate(lod0Object, collisionRoot.transform);
            collisionCopy.name = "Collision Meshes";
            Renderer[] renderers = collisionCopy.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                DestroyImmediate(renderers[i]);
            }

            MeshFilter[] filters = collisionCopy.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh != null)
                {
                    MeshCollider meshCollider = filters[i].gameObject.GetComponent<MeshCollider>();
                    if (meshCollider == null)
                    {
                        meshCollider = filters[i].gameObject.AddComponent<MeshCollider>();
                    }
                    meshCollider.sharedMesh = filters[i].sharedMesh;
                    meshCollider.convex = false;
                }
            }
        }

        private void NormalizeVisualRoot(GameObject visualRoot, string nameForSizing, out float appliedScale)
        {
            appliedScale = 1f;
            if (visualRoot == null)
            {
                return;
            }

            if (normalizeSourcesToGrid)
            {
                Bounds bounds = GetRendererBounds(visualRoot);
                float sourceMajorSpan = Mathf.Max(bounds.size.x, bounds.size.z);
                float targetMajorSpan = Mathf.Max(0.1f, cellSizeMeters);
                if (sourceMajorSpan > 0.0001f)
                {
                    appliedScale = targetMajorSpan / sourceMajorSpan;
                    visualRoot.transform.localScale *= appliedScale;
                }
            }

            OffsetToBounds(visualRoot, centerOnBounds, groundBottomOnYZero);
        }

        private void OffsetToBounds(GameObject root, bool center, bool ground)
        {
            Bounds bounds = GetRendererBounds(root);
            Vector3 offset = Vector3.zero;
            if (center)
            {
                offset.x = -bounds.center.x;
                offset.z = -bounds.center.z;
            }
            if (ground)
            {
                offset.y = -bounds.min.y;
            }
            else if (center)
            {
                offset.y = -bounds.center.y;
            }

            root.transform.localPosition += offset;
        }

        private Dictionary<string, List<ExtractEntry>> CollectExtractEntries(Transform lodRoot)
        {
            Dictionary<string, List<ExtractEntry>> results = new Dictionary<string, List<ExtractEntry>>(StringComparer.OrdinalIgnoreCase);
            if (lodRoot == null)
            {
                return results;
            }

            Renderer[] renderers = lodRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Mesh sharedMesh = GetSharedMesh(renderer);
                if (sharedMesh == null)
                {
                    continue;
                }

                Transform pieceRoot = DeterminePieceRoot(renderer.transform, lodRoot);
                Bounds bounds = GetRendererBounds(pieceRoot.gameObject);
                string cleanObjectName = CleanupName(pieceRoot.name);
                string cleanMeshName = CleanupName(sharedMesh.name);
                string cleanMaterialName = CleanupName(GetPrimaryMaterialName(renderer));
                string familyKey = cleanObjectName + "|" + cleanMeshName + "|" + cleanMaterialName + "|" +
                                   Mathf.Round(bounds.size.x * 20f) / 20f + "|" +
                                   Mathf.Round(bounds.size.y * 20f) / 20f + "|" +
                                   Mathf.Round(bounds.size.z * 20f) / 20f;
                string prettyName = BuildPrettyExtractName(cleanObjectName, cleanMaterialName, bounds.size);

                if (!results.TryGetValue(familyKey, out List<ExtractEntry> list))
                {
                    list = new List<ExtractEntry>();
                    results.Add(familyKey, list);
                }

                if (list.Any(existing => existing.transform == pieceRoot))
                {
                    continue;
                }

                list.Add(new ExtractEntry
                {
                    transform = pieceRoot,
                    cleanObjectName = cleanObjectName,
                    cleanMeshName = cleanMeshName,
                    cleanMaterialName = cleanMaterialName,
                    familyKey = familyKey,
                    prettyName = prettyName,
                    bounds = bounds,
                    sharedMesh = sharedMesh
                });
            }

            return results;
        }

        private ExtractEntry SelectRepresentative(List<ExtractEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            ExtractEntry best = entries[0];
            float bestScore = ScoreEntry(best);
            for (int i = 1; i < entries.Count; i++)
            {
                float score = ScoreEntry(entries[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = entries[i];
                }
            }

            return best;
        }

        private ExtractEntry FindBestMatchingEntry(ExtractEntry reference, List<ExtractEntry> candidates)
        {
            if (reference == null || candidates == null || candidates.Count == 0)
            {
                return null;
            }

            ExtractEntry best = candidates[0];
            float bestDiff = BoundsDifference(reference.bounds.size, best.bounds.size);
            for (int i = 1; i < candidates.Count; i++)
            {
                float diff = BoundsDifference(reference.bounds.size, candidates[i].bounds.size);
                if (diff < bestDiff)
                {
                    best = candidates[i];
                    bestDiff = diff;
                }
            }
            return best;
        }

        private float ScoreEntry(ExtractEntry entry)
        {
            Vector3 size = entry.bounds.size;
            return size.x * size.y * size.z;
        }

        private float BoundsDifference(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
        }

        private Transform DeterminePieceRoot(Transform rendererTransform, Transform lodRoot)
        {
            Transform current = rendererTransform;
            while (current.parent != null && current.parent != lodRoot)
            {
                if (current.parent.GetComponentsInChildren<Renderer>(true).Length > 4)
                {
                    break;
                }
                current = current.parent;
            }
            return current;
        }

        private Transform FindImportedLodRoot(Transform root, int lodIndex)
        {
            Queue<Transform> queue = new Queue<Transform>();
            queue.Enqueue(root);
            string target = "lod" + lodIndex;
            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                string lower = current.name.ToLowerInvariant();
                if (lower == target || lower.EndsWith("_" + target) || lower.Contains(target))
                {
                    return current;
                }

                for (int i = 0; i < current.childCount; i++)
                {
                    queue.Enqueue(current.GetChild(i));
                }
            }

            return null;
        }

        private GameObject InstantiateForPreview(GameObject asset)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            if (instance == null)
            {
                instance = Instantiate(asset);
            }
            instance.hideFlags = HideFlags.HideAndDontSave;
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private void StripToVisualOnly(GameObject root)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (component is Transform || component is MeshFilter || component is MeshRenderer || component is SkinnedMeshRenderer)
                {
                    continue;
                }

                DestroyImmediate(component);
            }
        }

        private void RemovePieceComponentRecursively(GameObject root)
        {
            LLModularBuildingPiece[] pieces = root.GetComponentsInChildren<LLModularBuildingPiece>(true);
            for (int i = 0; i < pieces.Length; i++)
            {
                DestroyImmediate(pieces[i]);
            }
        }

        private void RelinkMaterialsRecursive(GameObject root)
        {
            Dictionary<string, Material> lookup = GetMaterialLookup();
            if (lookup.Count == 0)
            {
                return;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] materials = renderers[i].sharedMaterials;
                bool changed = false;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material source = materials[j];
                    string key = CleanupName(source != null ? source.name : string.Empty);
                    if (!string.IsNullOrEmpty(key) && lookup.TryGetValue(key, out Material resolved) && resolved != null && resolved != source)
                    {
                        materials[j] = resolved;
                        changed = true;
                    }
                }
                if (changed)
                {
                    renderers[i].sharedMaterials = materials;
                }
            }
        }

        private Dictionary<string, Material> GetMaterialLookup()
        {
            if (materialLookup != null)
            {
                return materialLookup;
            }

            materialLookup = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(westernMaterialsFolder))
            {
                return materialLookup;
            }

            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { westernMaterialsFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    continue;
                }

                string key = CleanupName(material.name);
                if (!materialLookup.ContainsKey(key))
                {
                    materialLookup.Add(key, material);
                }
            }

            return materialLookup;
        }

        private string[] FindBuildingFbxPaths()
        {
            if (!AssetDatabase.IsValidFolder(westernBuildingsFolder))
            {
                return Array.Empty<string>();
            }

            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { westernBuildingsFolder });
            return guids.Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private List<string> FindSourceFolders()
        {
            List<string> folders = new List<string>();
            if (!AssetDatabase.IsValidFolder(kitRootFolder))
            {
                return folders;
            }

            string[] folderGuids = AssetDatabase.FindAssets("t:DefaultAsset", new[] { kitRootFolder });
            for (int i = 0; i < folderGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(folderGuids[i]);
                if (AssetDatabase.IsValidFolder(path) && Path.GetFileName(path).Equals(SourceFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    folders.Add(path.Replace("\\", "/"));
                }
            }
            folders.Sort(StringComparer.OrdinalIgnoreCase);
            return folders;
        }

        private void EnsureFolderChain(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return;
            }

            folderPath = folderPath.Replace("\\", "/");
            string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private string BuildPairFamilyKey(string fileName, out int lodIndex)
        {
            lodIndex = -1;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string working = fileName;
            Match match = LODRegex.Match(fileName);
            if (match.Success)
            {
                string token = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : match.Groups[2].Value;
                int.TryParse(token, out lodIndex);
                working = working.Remove(match.Index, match.Length);
            }

            return CleanupName(working);
        }

        private string StripLodSuffix(string raw)
        {
            return BuildPairFamilyKey(raw, out _);
        }

        private static bool ApplyOverridesToExistingPrefabAssetStatic(string prefabPath)
        {
            if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                LLModularBuildingPiece piece = root.GetComponent<LLModularBuildingPiece>();
                if (piece == null)
                {
                    return false;
                }

                ApplySizeOverrideToPieceRootsStatic(piece);
                if (piece.overrideCategoryAndCollider)
                {
                    piece.category = piece.overrideCategory;
                    piece.colliderMode = piece.overrideColliderMode == LLColliderAuthoringMode.Auto
                        ? LLModularBuildingPiece.InferColliderModeFromCategory(piece.overrideCategory)
                        : piece.overrideColliderMode;
                }

                RebuildCollisionForLoadedPrefabStatic(piece);
                piece.RebuildLODGroup();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private PieceOverrideState LoadExistingOverrideState(string prefabPath)
        {
            PieceOverrideState state = new PieceOverrideState();
            if (string.IsNullOrEmpty(prefabPath))
            {
                return state;
            }

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing == null)
            {
                return state;
            }

            LLModularBuildingPiece piece = existing.GetComponent<LLModularBuildingPiece>();
            if (piece == null)
            {
                return state;
            }

            state.hasOverride = piece.sizeOverrideMode != LLSizeOverrideMode.None || piece.overrideCategoryAndCollider;
            state.sizeOverrideMode = piece.sizeOverrideMode;
            state.overrideTargetHorizontalSpanMeters = piece.overrideTargetHorizontalSpanMeters;
            state.overrideGridWidth = piece.overrideGridWidth;
            state.overrideGridDepth = piece.overrideGridDepth;
            state.overrideCategoryAndCollider = piece.overrideCategoryAndCollider;
            state.overrideCategory = piece.overrideCategory;
            state.overrideColliderMode = piece.overrideColliderMode;
            return state;
        }

        private void ApplyOverridesToPieceComponent(LLModularBuildingPiece piece, PieceOverrideState state)
        {
            if (piece == null || state == null)
            {
                return;
            }

            piece.sizeOverrideMode = state.sizeOverrideMode;
            piece.overrideTargetHorizontalSpanMeters = Mathf.Max(0.1f, state.overrideTargetHorizontalSpanMeters);
            piece.overrideGridWidth = Mathf.Max(1, state.overrideGridWidth);
            piece.overrideGridDepth = Mathf.Max(1, state.overrideGridDepth);
            piece.overrideCategoryAndCollider = state.overrideCategoryAndCollider;
            piece.overrideCategory = state.overrideCategory;
            piece.overrideColliderMode = state.overrideColliderMode;

            if (piece.sizeOverrideMode == LLSizeOverrideMode.TargetSpanMeters)
            {
                piece.targetHorizontalSpanMeters = piece.overrideTargetHorizontalSpanMeters;
            }
            else if (piece.sizeOverrideMode == LLSizeOverrideMode.GridFootprint)
            {
                piece.gridWidth = piece.overrideGridWidth;
                piece.gridDepth = piece.overrideGridDepth;
                piece.targetHorizontalSpanMeters = Mathf.Max(piece.overrideGridWidth, piece.overrideGridDepth) * Mathf.Max(0.1f, piece.cellSizeMeters);
            }
        }

        private void ApplySizeOverrideToPieceRoots(LLModularBuildingPiece piece)
        {
            ApplySizeOverrideToPieceRootsStatic(piece);
        }

        private static void ApplySizeOverrideToPieceRootsStatic(LLModularBuildingPiece piece)
        {
            if (piece == null || piece.lod0Root == null)
            {
                return;
            }

            float desiredTarget = piece.targetHorizontalSpanMeters;
            if (piece.sizeOverrideMode == LLSizeOverrideMode.TargetSpanMeters)
            {
                desiredTarget = Mathf.Max(0.1f, piece.overrideTargetHorizontalSpanMeters);
                piece.targetHorizontalSpanMeters = desiredTarget;
            }
            else if (piece.sizeOverrideMode == LLSizeOverrideMode.GridFootprint)
            {
                piece.gridWidth = Mathf.Max(1, piece.overrideGridWidth);
                piece.gridDepth = Mathf.Max(1, piece.overrideGridDepth);
                desiredTarget = Mathf.Max(piece.gridWidth, piece.gridDepth) * Mathf.Max(0.1f, piece.cellSizeMeters);
                piece.targetHorizontalSpanMeters = desiredTarget;
            }
            else
            {
                desiredTarget = Mathf.Max(0.1f, piece.cellSizeMeters);
                piece.targetHorizontalSpanMeters = desiredTarget;
            }

            Bounds currentBounds = GetRendererBoundsStatic(piece.lod0Root.gameObject);
            float currentSpan = Mathf.Max(currentBounds.size.x, currentBounds.size.z);
            if (currentSpan <= 0.0001f)
            {
                return;
            }

            float ratio = desiredTarget / currentSpan;
            piece.lod0Root.localScale *= ratio;
            if (piece.lod1Root != null)
            {
                piece.lod1Root.localScale *= ratio;
            }

            piece.sourceHorizontalSpanMeters = currentSpan;
            piece.appliedUniformScale *= ratio;
        }

        private static void RebuildCollisionForLoadedPrefabStatic(LLModularBuildingPiece piece)
        {
            if (piece == null || piece.collisionRoot == null || piece.lod0Root == null)
            {
                return;
            }

            for (int i = piece.collisionRoot.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(piece.collisionRoot.GetChild(i).gameObject);
            }

            Collider[] colliders = piece.collisionRoot.GetComponents<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                Object.DestroyImmediate(colliders[i]);
            }

            if (piece.colliderMode == LLColliderAuthoringMode.SingleBox)
            {
                Bounds bounds = GetRendererBoundsStatic(piece.lod0Root.gameObject);
                BoxCollider box = piece.collisionRoot.gameObject.AddComponent<BoxCollider>();
                box.center = piece.collisionRoot.transform.InverseTransformPoint(bounds.center);
                box.size = bounds.size;
                return;
            }

            GameObject collisionCopy = Object.Instantiate(piece.lod0Root.gameObject, piece.collisionRoot);
            collisionCopy.name = "Collision Meshes";
            Renderer[] renderers = collisionCopy.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Object.DestroyImmediate(renderers[i]);
            }

            MeshFilter[] filters = collisionCopy.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null)
                {
                    continue;
                }

                MeshCollider meshCollider = filters[i].gameObject.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = filters[i].gameObject.AddComponent<MeshCollider>();
                }
                meshCollider.sharedMesh = filters[i].sharedMesh;
                meshCollider.convex = false;
            }
        }

        private static Bounds GetRendererBoundsStatic(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private float DetermineTargetHorizontalSpanMeters(string nameForSizing, Vector3 boundsSize, LLModuleCategory category)
        {
            return Mathf.Max(0.1f, cellSizeMeters);
        }

        private Vector2Int InferGridFootprint(string nameForSizing, Vector3 boundsSize, LLModuleCategory category)
        {
            string sanitizedName = SanitizeNameForSizing(nameForSizing);
            if (!useGeometryDrivenSizing && TryResolveNamedSize(sanitizedName, out int a, out int b))
            {
                return new Vector2Int(
                    Mathf.Max(1, Mathf.RoundToInt(a / Mathf.Max(0.1f, cellSizeMeters))),
                    Mathf.Max(1, Mathf.RoundToInt(b / Mathf.Max(0.1f, cellSizeMeters))));
            }

            switch (category)
            {
                case LLModuleCategory.Floor:
                case LLModuleCategory.Ceiling:
                case LLModuleCategory.Foundation:
                case LLModuleCategory.Roof:
                case LLModuleCategory.Porch:
                    return new Vector2Int(
                        Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(boundsSize.x, 0.1f) / Mathf.Max(0.1f, cellSizeMeters))),
                        Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(boundsSize.z, 0.1f) / Mathf.Max(0.1f, cellSizeMeters))));

                case LLModuleCategory.Wall:
                case LLModuleCategory.Door:
                case LLModuleCategory.Window:
                    return new Vector2Int(
                        Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(Mathf.Max(boundsSize.x, boundsSize.z), 0.1f) / Mathf.Max(0.1f, cellSizeMeters))),
                        1);

                default:
                    return new Vector2Int(1, 1);
            }
        }

        private float SnapToCellMultiple(float value)
        {
            float cell = Mathf.Max(0.1f, cellSizeMeters);
            return Mathf.Max(cell, Mathf.Round(value / cell) * cell);
        }

        private string SanitizeNameForSizing(string raw)
        {
            string text = raw ?? string.Empty;
            text = Regex.Replace(text, @"\bwood\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\bplaster\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\bbrick\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\bmetal\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\bstone\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim();
            return text;
        }

        private bool TryResolveNamedSize(string nameForSizing, out int a, out int b)
        {
            a = 0;
            b = 0;

            string text = nameForSizing ?? string.Empty;
            MatchCollection matches = SizeRegex.Matches(text);
            if (matches == null || matches.Count == 0)
            {
                return false;
            }

            Match best = null;
            int bestScore = int.MinValue;

            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                string aText = match.Groups[1].Value;
                string bText = match.Groups[2].Value;
                if (!int.TryParse(aText, out int candidateA) || !int.TryParse(bText, out int candidateB))
                {
                    continue;
                }

                if (HasLeadingZero(aText) || HasLeadingZero(bText))
                {
                    continue;
                }

                int score = 0;

                // Prefer later explicit size tokens like "2x6" over earlier indexed chunks like "4 X 011".
                score += match.Index;

                // Prefer more believable modular piece dimensions and penalize obviously huge spans.
                if (candidateA <= 8) score += 12; else if (candidateA <= 12) score += 4; else score -= 12;
                if (candidateB <= 8) score += 12; else if (candidateB <= 12) score += 4; else score -= 12;

                // Prefer near-grid-friendly values when possible, but still allow odd sizes like 3x4.
                int roundedCell = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0.1f, cellSizeMeters)));
                if (candidateA % roundedCell == 0) score += 3;
                if (candidateB % roundedCell == 0) score += 3;

                // Penalize suspicious aspect ratios which are usually IDs rather than dimensions.
                int major = Math.Max(candidateA, candidateB);
                int minor = Math.Max(1, Math.Min(candidateA, candidateB));
                if ((float)major / minor > 4.5f) score -= 8;

                if (best == null || score >= bestScore)
                {
                    best = match;
                    bestScore = score;
                    a = candidateA;
                    b = candidateB;
                }
            }

            return best != null;
        }

        private static bool HasLeadingZero(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length > 1 && value[0] == '0';
        }

        private string BuildPrettyExtractName(string cleanObjectName, string cleanMaterialName, Vector3 size)
        {
            string objectPart = SanitizePrefabName(cleanObjectName);
            string materialPart = SanitizePrefabName(cleanMaterialName);
            string sizePart = BuildSizeLabel(size);

            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(objectPart)) parts.Add(objectPart);
            if (!string.IsNullOrEmpty(sizePart) && !objectPart.ToLowerInvariant().Contains(sizePart.ToLowerInvariant())) parts.Add(sizePart);
            if (!string.IsNullOrEmpty(materialPart) && !objectPart.ToLowerInvariant().Contains(materialPart.ToLowerInvariant())) parts.Add(materialPart);
            if (parts.Count == 0) parts.Add("Extracted Piece");
            return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray());
        }

        private string BuildSizeLabel(Vector3 size)
        {
            int a = Mathf.RoundToInt(Mathf.Max(size.x, size.z));
            int b = Mathf.RoundToInt(Mathf.Min(size.x, size.z));
            if (a <= 0 || b <= 0)
            {
                return string.Empty;
            }
            return b + "x" + a;
        }

        private string CleanupName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            string working = raw.Replace("(Clone)", string.Empty).Replace("_", " ").Replace("-", " ");
            working = Regex.Replace(working, @"LOD\s*[01]", " ", RegexOptions.IgnoreCase);
            working = NumberTokenRegex.Replace(working, " ");
            working = Regex.Replace(working, @"\s+", " ").Trim();
            return SanitizePrefabName(working);
        }

        private string SanitizePrefabName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            List<string> words = new List<string>();
            MatchCollection matches = WordRegex.Matches(raw);
            for (int i = 0; i < matches.Count; i++)
            {
                string value = matches[i].Value;
                if (int.TryParse(value, out _))
                {
                    words.Add(value);
                }
                else
                {
                    words.Add(char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant());
                }
            }

            return string.Join(" ", words.ToArray()).Trim();
        }

        private string SanitizeKey(string raw)
        {
            return Regex.Replace(CleanupName(raw).ToLowerInvariant(), @"\s+", "_").Trim('_');
        }

        private string MakePieceId(string raw)
        {
            return SanitizeKey(raw);
        }

        private string GetPrimaryMaterialName(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
            {
                return string.Empty;
            }

            return renderer.sharedMaterials[0] != null ? renderer.sharedMaterials[0].name : string.Empty;
        }

        private Mesh GetSharedMesh(Renderer renderer)
        {
            MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
            if (filter != null)
            {
                return filter.sharedMesh;
            }

            SkinnedMeshRenderer skinned = renderer as SkinnedMeshRenderer;
            return skinned != null ? skinned.sharedMesh : null;
        }

        private Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Bounds(root.transform.position, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child.transform;
        }

        private string CombineAssetPath(string left, string right)
        {
            return (left.TrimEnd('/', '\\') + "/" + right.TrimStart('/', '\\')).Replace("\\", "/");
        }

        private string ShortAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "—";
            }
            return path.Length <= 90 ? path : "..." + path.Substring(path.Length - 87);
        }

        private void PingAssetPath(string path)
        {
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                Selection.activeObject = obj;
            }
        }
    }
}
#endif
