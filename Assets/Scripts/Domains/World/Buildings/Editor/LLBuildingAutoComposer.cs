#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LandAndLedgers.Buildings.Editor
{
    public sealed class LLBuildingAutoComposer : EditorWindow
    {
        private const float CellSizeMeters = 2f;
        private const float StoryHeightMeters = 2f;
        private const string DefaultKitFolder = "Assets/Environment/Building Kits/1897 Frontier";
        private const string DefaultOutputFolder = "Assets/Environment/Generated Buildings";
        private const string DefaultStyle = "NaturalWood";

        private static readonly string[] NamingExamples =
        {
            "LL_NaturalWood_Wall_Solid_W1_H1_A",
            "LL_NaturalWood_Wall_Window_W1_H1_A",
            "LL_NaturalWood_Wall_Door_W1_H1_A",
            "LL_NaturalWood_Wall_WindowDoor_W1_H1_A",
            "LL_NaturalWood_Floor_Base_W1_D1_A",
            "LL_NaturalWood_Foundation_Base_W1_D1_A",
            "LL_NaturalWood_Roof_GableLeft_W1_D1_A",
            "LL_NaturalWood_Roof_GableRight_W1_D1_A",
            "LL_NaturalWood_Roof_Ridge_W1_D1_A",
            "LL_NaturalWood_Porch_Post_H1_A",
            "LL_NaturalWood_Porch_Beam_W1_A",
            "LL_NaturalWood_Trim_Cornice_W1_A"
        };

        private string kitFolder = DefaultKitFolder;
        private string outputFolder = DefaultOutputFolder;
        private string styleFilter = DefaultStyle;
        private bool includeSubfolders = true;
        private bool validateBoundsOnRefresh = true;
        private bool generateCollision = true;
        private bool addBoxColliders = true;
        private bool createFloors = true;
        private bool createCeilings = false;
        private bool createFoundations = true;
        private bool createRoofs = true;
        private bool createPorches = true;
        private bool createTrim = true;
        private bool useNestedPrefabs = true;
        private bool overwriteExisting = false;
        private bool focusOutputAfterCreate = true;

        private BuildingProfile selectedProfile = BuildingProfile.FrontierHouseSmall;
        private string buildingName = "House";
        private int widthCells = 4;
        private int depthCells = 4;
        private int stories = 1;
        private int seed = 1897;
        private int batchCount = 5;
        private bool includeBackDoor = false;
        private bool includeBalcony = false;
        private bool includeFalseFront = false;
        private bool includePorch = true;
        private float porchDepthCells = 1f;

        private Vector2 scroll;
        private readonly List<KitPiece> kitPieces = new List<KitPiece>();
        private readonly Dictionary<string, List<KitPiece>> registry = new Dictionary<string, List<KitPiece>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();
        private string lastActionMessage = string.Empty;

        private enum BuildingProfile
        {
            FrontierHouseSmall,
            FrontierHouseMedium,
            GeneralStoreNarrow,
            GeneralStoreWide,
            DoctorOffice,
            SheriffOffice,
            SaloonTwoStory,
            ChurchSimple
        }

        private enum PieceCategory
        {
            Wall,
            Floor,
            Ceiling,
            Foundation,
            Roof,
            Porch,
            Trim,
            Detail,
            Stair,
            Corner,
            Unknown
        }

        private enum RoofMode
        {
            Gable,
            Flat,
            Shed
        }

        private enum FrontageMode
        {
            House,
            Storefront,
            Office,
            Civic,
            FalseFront
        }

        [Serializable]
        private sealed class KitPiece
        {
            public string assetPath;
            public GameObject prefab;
            public string style;
            public PieceCategory category;
            public string subtype;
            public int widthCells = 1;
            public int depthCells = 1;
            public int heightUnits = 1;
            public string variant = "A";
            public Bounds bounds;
            public bool boundsValid;
            public string Key => ComposeRegistryKey(style, category, subtype, widthCells, depthCells, heightUnits);
        }

        [Serializable]
        private sealed class ValidationIssue
        {
            public MessageType messageType;
            public string message;
            public string assetPath;
        }

        [Serializable]
        private sealed class BuildingRecipe
        {
            public string displayName;
            public string style;
            public BuildingProfile profile;
            public int widthCells;
            public int depthCells;
            public int stories;
            public int seed;
            public bool includeBackDoor;
            public bool includePorch;
            public bool includeBalcony;
            public bool includeFalseFront;
            public int porchDepthCells;
            public RoofMode roofMode;
            public FrontageMode frontageMode;
            public string subfolder;
        }

        private sealed class StoryLayout
        {
            public WallSlot[,] front;
            public WallSlot[,] back;
            public WallSlot[,] left;
            public WallSlot[,] right;
        }

        private struct WallSlot
        {
            public string subtype;
            public bool allowFallbackToSolid;

            public static WallSlot Solid() => new WallSlot { subtype = "Solid", allowFallbackToSolid = true };
            public static WallSlot Window() => new WallSlot { subtype = "Window", allowFallbackToSolid = true };
            public static WallSlot Door() => new WallSlot { subtype = "Door", allowFallbackToSolid = true };
            public static WallSlot WindowDoor() => new WallSlot { subtype = "WindowDoor", allowFallbackToSolid = true };
            public static WallSlot Empty() => new WallSlot { subtype = "", allowFallbackToSolid = false };
        }

        [MenuItem("Land & Ledgers/Buildings/Auto Compose Buildings")]
        public static void ShowWindow()
        {
            LLBuildingAutoComposer window = GetWindow<LLBuildingAutoComposer>();
            window.titleContent = new GUIContent("LL Auto Composer");
            window.minSize = new Vector2(560f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            ApplyProfileDefaults(selectedProfile);
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space(4f);
            DrawKitSection();
            EditorGUILayout.Space(8f);
            DrawNamingGuideSection();
            EditorGUILayout.Space(8f);
            DrawGenerationSection();
            EditorGUILayout.Space(8f);
            DrawIssuesSection();
            EditorGUILayout.Space(8f);
            DrawLastActionSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawKitSection()
        {
            EditorGUILayout.LabelField("Kit Source", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Put cleanly named modular prefabs in a kit folder. This editor script scans the folder, validates the prefabs against the declared 2-meter grid, and composes finished building prefabs without silently stretching the source art.",
                MessageType.Info);

            kitFolder = EditorGUILayout.TextField("Kit Folder", kitFolder);
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            styleFilter = EditorGUILayout.TextField("Style", styleFilter);
            includeSubfolders = EditorGUILayout.Toggle("Include Subfolders", includeSubfolders);
            validateBoundsOnRefresh = EditorGUILayout.Toggle("Validate Bounds", validateBoundsOnRefresh);
            useNestedPrefabs = EditorGUILayout.Toggle("Use Nested Prefabs", useNestedPrefabs);
            overwriteExisting = EditorGUILayout.Toggle("Overwrite Existing", overwriteExisting);
            focusOutputAfterCreate = EditorGUILayout.Toggle("Ping Output", focusOutputAfterCreate);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Kit", GUILayout.Height(28f)))
                {
                    RefreshKit();
                }

                if (GUILayout.Button("Validate Kit", GUILayout.Height(28f)))
                {
                    ValidateKitOnly();
                }

                if (GUILayout.Button("Open Output Folder", GUILayout.Height(28f)))
                {
                    RevealOutputFolder();
                }
            }

            EditorGUILayout.LabelField($"Pieces Found: {kitPieces.Count}");
            if (kitPieces.Count > 0)
            {
                int styleMatchCount = kitPieces.Count(piece => string.Equals(piece.style, styleFilter, StringComparison.OrdinalIgnoreCase));
                EditorGUILayout.LabelField($"Pieces Matching Style '{styleFilter}': {styleMatchCount}");
            }
        }

        private void DrawNamingGuideSection()
        {
            EditorGUILayout.LabelField("Naming Guide", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Expected format: LL_[Style]_[Category]_[Subtype]_[Dims]_[Variant]. Dims can be multiple underscore-separated tokens like W1_D1_H1. Categories are strict. Examples below are the names you should build toward.",
                MessageType.None);

            foreach (string example in NamingExamples)
            {
                EditorGUILayout.SelectableLabel(example, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private void DrawGenerationSection()
        {
            EditorGUILayout.LabelField("Generation", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            selectedProfile = (BuildingProfile)EditorGUILayout.EnumPopup("Profile", selectedProfile);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyProfileDefaults(selectedProfile);
            }

            buildingName = EditorGUILayout.TextField("Base Name", buildingName);
            widthCells = Mathf.Max(2, EditorGUILayout.IntField("Width Cells", widthCells));
            depthCells = Mathf.Max(2, EditorGUILayout.IntField("Depth Cells", depthCells));
            stories = Mathf.Max(1, EditorGUILayout.IntField("Stories", stories));
            seed = EditorGUILayout.IntField("Seed", seed);
            batchCount = Mathf.Max(1, EditorGUILayout.IntField("Batch Count", batchCount));

            generateCollision = EditorGUILayout.Toggle("Create Collision Root", generateCollision);
            addBoxColliders = EditorGUILayout.Toggle("Add Box Colliders", addBoxColliders);
            createFoundations = EditorGUILayout.Toggle("Create Foundations", createFoundations);
            createFloors = EditorGUILayout.Toggle("Create Floors", createFloors);
            createCeilings = EditorGUILayout.Toggle("Create Ceilings", createCeilings);
            createRoofs = EditorGUILayout.Toggle("Create Roofs", createRoofs);
            createPorches = EditorGUILayout.Toggle("Create Porch", createPorches);
            createTrim = EditorGUILayout.Toggle("Create Trim", createTrim);
            includeBackDoor = EditorGUILayout.Toggle("Back Door", includeBackDoor);
            includeBalcony = EditorGUILayout.Toggle("Balcony", includeBalcony);
            includeFalseFront = EditorGUILayout.Toggle("False Front", includeFalseFront);
            includePorch = EditorGUILayout.Toggle("Porch Enabled", includePorch);
            porchDepthCells = Mathf.Clamp(EditorGUILayout.FloatField("Porch Depth Cells", porchDepthCells), 1f, 3f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate One", GUILayout.Height(30f)))
                {
                    GenerateOne();
                }

                if (GUILayout.Button("Generate Variants", GUILayout.Height(30f)))
                {
                    GenerateVariants();
                }
            }

            if (GUILayout.Button("Generate Small Frontier Batch", GUILayout.Height(30f)))
            {
                GenerateSuggestedBatch();
            }
        }

        private void DrawIssuesSection()
        {
            EditorGUILayout.LabelField("Validation / Notes", EditorStyles.boldLabel);
            if (issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No validation issues currently recorded.", MessageType.None);
                return;
            }

            for (int i = 0; i < issues.Count; i++)
            {
                ValidationIssue issue = issues[i];
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(issue.assetPath)
                    ? issue.message
                    : issue.message + "\n" + issue.assetPath, issue.messageType);
            }
        }

        private void DrawLastActionSection()
        {
            if (string.IsNullOrWhiteSpace(lastActionMessage))
            {
                return;
            }

            EditorGUILayout.LabelField("Last Action", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lastActionMessage, MessageType.Info);
        }

        private void RefreshKit()
        {
            kitPieces.Clear();
            registry.Clear();
            issues.Clear();

            if (string.IsNullOrWhiteSpace(kitFolder) || !AssetDatabase.IsValidFolder(kitFolder))
            {
                issues.Add(new ValidationIssue
                {
                    messageType = MessageType.Error,
                    message = "Kit folder does not exist.",
                    assetPath = kitFolder
                });
                return;
            }

            string[] searchFolders = { kitFolder };
            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
            Array.Sort(guids, StringComparer.Ordinal);

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!includeSubfolders && !IsDirectChildOfFolder(assetPath, kitFolder))
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    continue;
                }

                if (!prefab.name.StartsWith("LL_", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                KitPiece piece = ParsePiece(prefab, assetPath);
                if (piece == null)
                {
                    continue;
                }

                kitPieces.Add(piece);
                RegisterPiece(piece);

                if (validateBoundsOnRefresh)
                {
                    ValidatePieceBounds(piece);
                }
            }

            issues.Add(new ValidationIssue
            {
                messageType = MessageType.Info,
                message = $"Refresh complete. Parsed {kitPieces.Count} kit prefabs.",
                assetPath = kitFolder
            });
        }

        private void ValidateKitOnly()
        {
            if (kitPieces.Count == 0)
            {
                RefreshKit();
                if (kitPieces.Count == 0)
                {
                    return;
                }
            }

            issues.Clear();
            for (int i = 0; i < kitPieces.Count; i++)
            {
                ValidatePieceBounds(kitPieces[i]);
            }

            ValidateRequiredPieces(styleFilter);
        }

        private void ValidateRequiredPieces(string style)
        {
            RequirePiece(style, PieceCategory.Wall, "Solid", 1, 1, 1);
            RequirePiece(style, PieceCategory.Wall, "Window", 1, 1, 1);
            RequirePiece(style, PieceCategory.Wall, "Door", 1, 1, 1);
            RequirePiece(style, PieceCategory.Floor, "Base", 1, 1, 1);
            RequirePiece(style, PieceCategory.Foundation, "Base", 1, 1, 1, false);
            RequirePiece(style, PieceCategory.Roof, "GableLeft", 1, 1, 1, false);
            RequirePiece(style, PieceCategory.Roof, "GableRight", 1, 1, 1, false);
            RequirePiece(style, PieceCategory.Roof, "Ridge", 1, 1, 1, false);
        }

        private void RequirePiece(string style, PieceCategory category, string subtype, int width, int depth, int height, bool required = true)
        {
            if (TryGetPiece(style, category, subtype, width, depth, height, out _))
            {
                return;
            }

            issues.Add(new ValidationIssue
            {
                messageType = required ? MessageType.Warning : MessageType.Info,
                message = required
                    ? $"Missing required piece: {style} / {category} / {subtype} / W{width}_D{depth}_H{height}."
                    : $"Optional piece not found: {style} / {category} / {subtype} / W{width}_D{depth}_H{height}.",
                assetPath = kitFolder
            });
        }

        private KitPiece ParsePiece(GameObject prefab, string assetPath)
        {
            string[] tokens = prefab.name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 5)
            {
                issues.Add(new ValidationIssue
                {
                    messageType = MessageType.Warning,
                    message = "Skipped prefab because the name does not match the LL naming convention.",
                    assetPath = assetPath
                });
                return null;
            }

            if (!string.Equals(tokens[0], "LL", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (!TryParseCategory(tokens[2], out PieceCategory category))
            {
                issues.Add(new ValidationIssue
                {
                    messageType = MessageType.Warning,
                    message = "Skipped prefab because the category token is unknown.",
                    assetPath = assetPath
                });
                return null;
            }

            KitPiece piece = new KitPiece
            {
                assetPath = assetPath,
                prefab = prefab,
                style = tokens[1],
                category = category,
                subtype = tokens[3]
            };

            for (int i = 4; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token.Length <= 3 && char.IsLetter(token[0]) && token.All(char.IsLetterOrDigit) && i == tokens.Length - 1)
                {
                    piece.variant = token;
                    continue;
                }

                if (TryParseDimToken(token, out char dimCode, out int dimValue))
                {
                    switch (char.ToUpperInvariant(dimCode))
                    {
                        case 'W':
                            piece.widthCells = Mathf.Max(1, dimValue);
                            break;
                        case 'D':
                            piece.depthCells = Mathf.Max(1, dimValue);
                            break;
                        case 'H':
                            piece.heightUnits = Mathf.Max(1, dimValue);
                            break;
                    }
                }
            }

            piece.bounds = CalculatePrefabBounds(prefab, out piece.boundsValid);
            return piece;
        }

        private static bool TryParseCategory(string token, out PieceCategory category)
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "wall": category = PieceCategory.Wall; return true;
                case "floor": category = PieceCategory.Floor; return true;
                case "ceiling": category = PieceCategory.Ceiling; return true;
                case "foundation": category = PieceCategory.Foundation; return true;
                case "roof": category = PieceCategory.Roof; return true;
                case "porch": category = PieceCategory.Porch; return true;
                case "trim": category = PieceCategory.Trim; return true;
                case "detail": category = PieceCategory.Detail; return true;
                case "stair": category = PieceCategory.Stair; return true;
                case "corner": category = PieceCategory.Corner; return true;
                default:
                    category = PieceCategory.Unknown;
                    return false;
            }
        }

        private static bool TryParseDimToken(string token, out char dimCode, out int dimValue)
        {
            dimCode = '\0';
            dimValue = 0;
            if (string.IsNullOrWhiteSpace(token) || token.Length < 2)
            {
                return false;
            }

            dimCode = token[0];
            if (!char.IsLetter(dimCode))
            {
                return false;
            }

            string numeric = token.Substring(1);
            return int.TryParse(numeric, NumberStyles.Integer, CultureInfo.InvariantCulture, out dimValue);
        }

        private void RegisterPiece(KitPiece piece)
        {
            if (!registry.TryGetValue(piece.Key, out List<KitPiece> list))
            {
                list = new List<KitPiece>();
                registry.Add(piece.Key, list);
            }

            list.Add(piece);
        }

        private void ValidatePieceBounds(KitPiece piece)
        {
            if (!piece.boundsValid)
            {
                issues.Add(new ValidationIssue
                {
                    messageType = MessageType.Warning,
                    message = "Prefab has no renderers, so footprint validation could not run.",
                    assetPath = piece.assetPath
                });
                return;
            }

            float tolerance = 0.40f;
            Vector3 size = piece.bounds.size;
            float horizontalA = size.x;
            float horizontalB = size.z;
            float vertical = size.y;

            switch (piece.category)
            {
                case PieceCategory.Wall:
                case PieceCategory.Porch:
                case PieceCategory.Trim:
                case PieceCategory.Corner:
                case PieceCategory.Stair:
                {
                    float expectedWidth = piece.widthCells * CellSizeMeters;
                    float expectedHeight = piece.heightUnits * StoryHeightMeters;
                    float measuredWidth = Mathf.Max(horizontalA, horizontalB);
                    if (Mathf.Abs(measuredWidth - expectedWidth) > tolerance)
                    {
                        issues.Add(new ValidationIssue
                        {
                            messageType = MessageType.Warning,
                            message = $"Width mismatch. Declared {expectedWidth:0.##}m but measured about {measuredWidth:0.##}m.",
                            assetPath = piece.assetPath
                        });
                    }

                    if (Mathf.Abs(vertical - expectedHeight) > tolerance)
                    {
                        issues.Add(new ValidationIssue
                        {
                            messageType = MessageType.Warning,
                            message = $"Height mismatch. Declared {expectedHeight:0.##}m but measured about {vertical:0.##}m.",
                            assetPath = piece.assetPath
                        });
                    }
                    break;
                }

                case PieceCategory.Floor:
                case PieceCategory.Ceiling:
                case PieceCategory.Foundation:
                case PieceCategory.Roof:
                {
                    float expectedWidth = piece.widthCells * CellSizeMeters;
                    float expectedDepth = piece.depthCells * CellSizeMeters;
                    float[] actual = { horizontalA, horizontalB };
                    Array.Sort(actual);
                    float[] expected = { Mathf.Min(expectedWidth, expectedDepth), Mathf.Max(expectedWidth, expectedDepth) };
                    if (Mathf.Abs(actual[0] - expected[0]) > tolerance || Mathf.Abs(actual[1] - expected[1]) > tolerance)
                    {
                        issues.Add(new ValidationIssue
                        {
                            messageType = MessageType.Warning,
                            message = $"Footprint mismatch. Declared {expectedWidth:0.##}m x {expectedDepth:0.##}m but measured about {horizontalA:0.##}m x {horizontalB:0.##}m.",
                            assetPath = piece.assetPath
                        });
                    }
                    break;
                }
            }
        }

        private void GenerateOne()
        {
            EnsureKitReady();
            if (kitPieces.Count == 0)
            {
                return;
            }

            BuildingRecipe recipe = CreateRecipeFromUi(selectedProfile, seed, 0);
            GameObject prefab = ComposeAndSave(recipe);
            if (prefab != null)
            {
                lastActionMessage = $"Generated building prefab: {AssetDatabase.GetAssetPath(prefab)}";
                if (focusOutputAfterCreate)
                {
                    EditorGUIUtility.PingObject(prefab);
                    Selection.activeObject = prefab;
                }
            }
        }

        private void GenerateVariants()
        {
            EnsureKitReady();
            if (kitPieces.Count == 0)
            {
                return;
            }

            List<string> generatedPaths = new List<string>();
            for (int i = 0; i < batchCount; i++)
            {
                BuildingRecipe recipe = CreateRecipeFromUi(selectedProfile, seed + i * 31, i);
                GameObject prefab = ComposeAndSave(recipe);
                if (prefab != null)
                {
                    generatedPaths.Add(AssetDatabase.GetAssetPath(prefab));
                }
            }

            lastActionMessage = generatedPaths.Count == 0
                ? "No variant prefabs were generated."
                : "Generated variants:\n" + string.Join("\n", generatedPaths);
        }

        private void GenerateSuggestedBatch()
        {
            EnsureKitReady();
            if (kitPieces.Count == 0)
            {
                return;
            }

            List<BuildingRecipe> batch = new List<BuildingRecipe>
            {
                new BuildingRecipe { displayName = "Frontier House Small", style = styleFilter, profile = BuildingProfile.FrontierHouseSmall, widthCells = 4, depthCells = 4, stories = 1, seed = seed + 11, includeBackDoor = false, includePorch = true, includeBalcony = false, includeFalseFront = false, porchDepthCells = 1, roofMode = RoofMode.Gable, frontageMode = FrontageMode.House, subfolder = "Houses" },
                new BuildingRecipe { displayName = "Frontier House Medium", style = styleFilter, profile = BuildingProfile.FrontierHouseMedium, widthCells = 5, depthCells = 4, stories = 1, seed = seed + 23, includeBackDoor = true, includePorch = true, includeBalcony = false, includeFalseFront = false, porchDepthCells = 1, roofMode = RoofMode.Gable, frontageMode = FrontageMode.House, subfolder = "Houses" },
                new BuildingRecipe { displayName = "General Store Narrow", style = styleFilter, profile = BuildingProfile.GeneralStoreNarrow, widthCells = 5, depthCells = 4, stories = 2, seed = seed + 37, includeBackDoor = true, includePorch = true, includeBalcony = false, includeFalseFront = true, porchDepthCells = 1, roofMode = RoofMode.Gable, frontageMode = FrontageMode.FalseFront, subfolder = "Businesses" },
                new BuildingRecipe { displayName = "Doctor Office", style = styleFilter, profile = BuildingProfile.DoctorOffice, widthCells = 4, depthCells = 4, stories = 1, seed = seed + 41, includeBackDoor = true, includePorch = true, includeBalcony = false, includeFalseFront = false, porchDepthCells = 1, roofMode = RoofMode.Gable, frontageMode = FrontageMode.Office, subfolder = "Services" },
                new BuildingRecipe { displayName = "Sheriff Office", style = styleFilter, profile = BuildingProfile.SheriffOffice, widthCells = 4, depthCells = 4, stories = 1, seed = seed + 47, includeBackDoor = true, includePorch = true, includeBalcony = false, includeFalseFront = false, porchDepthCells = 1, roofMode = RoofMode.Gable, frontageMode = FrontageMode.Office, subfolder = "Civic" },
                new BuildingRecipe { displayName = "Church Simple", style = styleFilter, profile = BuildingProfile.ChurchSimple, widthCells = 5, depthCells = 6, stories = 1, seed = seed + 53, includeBackDoor = false, includePorch = false, includeBalcony = false, includeFalseFront = false, porchDepthCells = 0, roofMode = RoofMode.Gable, frontageMode = FrontageMode.Civic, subfolder = "Civic" },
            };

            List<string> generated = new List<string>();
            foreach (BuildingRecipe recipe in batch)
            {
                GameObject prefab = ComposeAndSave(recipe);
                if (prefab != null)
                {
                    generated.Add(AssetDatabase.GetAssetPath(prefab));
                }
            }

            lastActionMessage = generated.Count == 0
                ? "No prefabs were generated in the batch run."
                : "Generated suggested batch:\n" + string.Join("\n", generated);
        }

        private BuildingRecipe CreateRecipeFromUi(BuildingProfile profile, int recipeSeed, int index)
        {
            BuildingRecipe recipe = new BuildingRecipe
            {
                displayName = string.IsNullOrWhiteSpace(buildingName) ? profile.ToString() : buildingName,
                style = styleFilter,
                profile = profile,
                widthCells = widthCells,
                depthCells = depthCells,
                stories = stories,
                seed = recipeSeed,
                includeBackDoor = includeBackDoor,
                includePorch = includePorch && createPorches,
                includeBalcony = includeBalcony,
                includeFalseFront = includeFalseFront,
                porchDepthCells = Mathf.RoundToInt(porchDepthCells),
                roofMode = RoofMode.Gable,
                frontageMode = FrontageMode.House,
                subfolder = "Generated"
            };

            switch (profile)
            {
                case BuildingProfile.FrontierHouseSmall:
                case BuildingProfile.FrontierHouseMedium:
                    recipe.frontageMode = FrontageMode.House;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Houses";
                    break;
                case BuildingProfile.GeneralStoreNarrow:
                case BuildingProfile.GeneralStoreWide:
                    recipe.frontageMode = includeFalseFront ? FrontageMode.FalseFront : FrontageMode.Storefront;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Businesses";
                    break;
                case BuildingProfile.DoctorOffice:
                    recipe.frontageMode = FrontageMode.Office;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Services";
                    break;
                case BuildingProfile.SheriffOffice:
                    recipe.frontageMode = FrontageMode.Office;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Civic";
                    break;
                case BuildingProfile.SaloonTwoStory:
                    recipe.frontageMode = includeFalseFront ? FrontageMode.FalseFront : FrontageMode.Storefront;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Businesses";
                    break;
                case BuildingProfile.ChurchSimple:
                    recipe.frontageMode = FrontageMode.Civic;
                    recipe.roofMode = RoofMode.Gable;
                    recipe.subfolder = "Civic";
                    break;
            }

            if (index > 0)
            {
                recipe.displayName += $" {index + 1:00}";
            }

            return recipe;
        }

        private GameObject ComposeAndSave(BuildingRecipe recipe)
        {
            ValidateRequiredPieces(recipe.style);
            if (issues.Any(issue => issue.messageType == MessageType.Error))
            {
                lastActionMessage = "Aborted generation because there are blocking validation errors.";
                return null;
            }

            string outputDirectory = EnsureOutputFolder(recipe.subfolder);
            string prefabName = SanitizeFileName($"{recipe.displayName} {recipe.style}");
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(outputDirectory, prefabName + ".prefab").Replace('\\', '/'));
            if (overwriteExisting)
            {
                prefabPath = Path.Combine(outputDirectory, prefabName + ".prefab").Replace('\\', '/');
            }

            GameObject root = new GameObject(prefabName);
            try
            {
                Transform shellRoot = CreateChild(root.transform, "Shell");
                Transform foundationRoot = CreateChild(shellRoot, "Foundation");
                Transform floorsRoot = CreateChild(shellRoot, "Floors");
                Transform ceilingsRoot = CreateChild(shellRoot, "Ceilings");
                Transform wallsRoot = CreateChild(shellRoot, "Walls");
                Transform roofRoot = CreateChild(shellRoot, "Roof");
                Transform porchRoot = CreateChild(root.transform, "Porch");
                Transform trimRoot = CreateChild(root.transform, "Trim");
                Transform detailsRoot = CreateChild(root.transform, "Details");
                Transform collisionRoot = generateCollision ? CreateChild(root.transform, "Collision") : null;

                System.Random rng = new System.Random(recipe.seed);
                StoryLayout[] layoutByStory = BuildStoryLayouts(recipe, rng);

                if (createFoundations)
                {
                    FillSurface(recipe, foundationRoot, PieceCategory.Foundation, "Base", 0f);
                }

                if (createFloors)
                {
                    for (int story = 0; story < recipe.stories; story++)
                    {
                        FillSurface(recipe, floorsRoot, PieceCategory.Floor, "Base", story * StoryHeightMeters);
                    }
                }

                if (createCeilings)
                {
                    for (int story = 0; story < recipe.stories; story++)
                    {
                        FillSurface(recipe, ceilingsRoot, PieceCategory.Ceiling, "Base", story * StoryHeightMeters + StoryHeightMeters - 0.05f);
                    }
                }

                for (int story = 0; story < recipe.stories; story++)
                {
                    PlacePerimeterWalls(recipe, wallsRoot, collisionRoot, layoutByStory[story], story);
                }

                if (createRoofs)
                {
                    BuildRoof(recipe, roofRoot);
                }

                if (recipe.includePorch && createPorches)
                {
                    BuildPorch(recipe, porchRoot, collisionRoot);
                }

                if (createTrim)
                {
                    BuildTrim(recipe, trimRoot, detailsRoot);
                }

                if (generateCollision && addBoxColliders)
                {
                    BuildCollision(recipe, collisionRoot);
                }

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return saved;
            }
            finally
            {
                DestroyImmediate(root);
            }
        }

        private StoryLayout[] BuildStoryLayouts(BuildingRecipe recipe, System.Random rng)
        {
            StoryLayout[] layouts = new StoryLayout[recipe.stories];
            for (int story = 0; story < recipe.stories; story++)
            {
                StoryLayout layout = new StoryLayout
                {
                    front = new WallSlot[recipe.widthCells, 1],
                    back = new WallSlot[recipe.widthCells, 1],
                    left = new WallSlot[recipe.depthCells, 1],
                    right = new WallSlot[recipe.depthCells, 1]
                };

                for (int x = 0; x < recipe.widthCells; x++)
                {
                    layout.front[x, 0] = WallSlot.Solid();
                    layout.back[x, 0] = WallSlot.Solid();
                }

                for (int z = 0; z < recipe.depthCells; z++)
                {
                    layout.left[z, 0] = WallSlot.Solid();
                    layout.right[z, 0] = WallSlot.Solid();
                }

                ApplyFacade(recipe, layout, story, rng);
                layouts[story] = layout;
            }

            return layouts;
        }

        private void ApplyFacade(BuildingRecipe recipe, StoryLayout layout, int story, System.Random rng)
        {
            int center = recipe.widthCells / 2;
            bool evenFront = recipe.widthCells % 2 == 0;
            int leftCenter = evenFront ? center - 1 : center;
            int rightCenter = center;

            switch (recipe.profile)
            {
                case BuildingProfile.FrontierHouseSmall:
                case BuildingProfile.FrontierHouseMedium:
                {
                    if (story == 0)
                    {
                        int doorCell = rng.NextDouble() < 0.5 ? leftCenter : rightCenter;
                        layout.front[doorCell, 0] = WallSlot.Door();
                        for (int x = 0; x < recipe.widthCells; x++)
                        {
                            if (x != doorCell && x > 0 && x < recipe.widthCells - 1)
                            {
                                layout.front[x, 0] = WallSlot.Window();
                            }
                        }

                        if (recipe.includeBackDoor)
                        {
                            layout.back[rightCenter, 0] = WallSlot.Door();
                        }
                    }
                    else
                    {
                        for (int x = 1; x < recipe.widthCells - 1; x++)
                        {
                            if (x % 2 == 0 || recipe.widthCells <= 4)
                            {
                                layout.front[x, 0] = WallSlot.Window();
                            }
                        }
                    }

                    for (int z = 1; z < recipe.depthCells - 1; z++)
                    {
                        if (z % 2 == 1)
                        {
                            layout.left[z, 0] = WallSlot.Window();
                            layout.right[z, 0] = WallSlot.Window();
                        }
                    }
                    break;
                }

                case BuildingProfile.GeneralStoreNarrow:
                case BuildingProfile.GeneralStoreWide:
                {
                    if (story == 0)
                    {
                        layout.front[leftCenter, 0] = WallSlot.Door();
                        for (int x = 0; x < recipe.widthCells; x++)
                        {
                            if (x != leftCenter && x != 0 && x != recipe.widthCells - 1)
                            {
                                layout.front[x, 0] = WallSlot.Window();
                            }
                        }
                    }
                    else
                    {
                        for (int x = 1; x < recipe.widthCells - 1; x++)
                        {
                            layout.front[x, 0] = WallSlot.Window();
                        }
                    }

                    if (recipe.includeBackDoor && story == 0)
                    {
                        layout.back[rightCenter, 0] = WallSlot.Door();
                    }
                    break;
                }

                case BuildingProfile.DoctorOffice:
                case BuildingProfile.SheriffOffice:
                {
                    if (story == 0)
                    {
                        layout.front[rightCenter, 0] = WallSlot.Door();
                        for (int x = 0; x < recipe.widthCells; x++)
                        {
                            if (x != rightCenter && x > 0 && x < recipe.widthCells - 1)
                            {
                                layout.front[x, 0] = WallSlot.Window();
                            }
                        }

                        if (recipe.includeBackDoor)
                        {
                            layout.back[leftCenter, 0] = WallSlot.Door();
                        }
                    }
                    break;
                }

                case BuildingProfile.SaloonTwoStory:
                {
                    if (story == 0)
                    {
                        layout.front[leftCenter, 0] = WallSlot.Door();
                        layout.front[rightCenter, 0] = WallSlot.Door();
                        for (int x = 0; x < recipe.widthCells; x++)
                        {
                            if (x != leftCenter && x != rightCenter && x > 0 && x < recipe.widthCells - 1)
                            {
                                layout.front[x, 0] = WallSlot.Window();
                            }
                        }
                    }
                    else
                    {
                        for (int x = 1; x < recipe.widthCells - 1; x++)
                        {
                            layout.front[x, 0] = WallSlot.Window();
                        }
                    }
                    break;
                }

                case BuildingProfile.ChurchSimple:
                {
                    if (story == 0)
                    {
                        layout.front[rightCenter, 0] = WallSlot.Door();
                        if (recipe.widthCells >= 5)
                        {
                            layout.front[1, 0] = WallSlot.Window();
                            layout.front[recipe.widthCells - 2, 0] = WallSlot.Window();
                        }
                    }

                    for (int z = 1; z < recipe.depthCells - 1; z++)
                    {
                        if (z % 2 == 1)
                        {
                            layout.left[z, 0] = WallSlot.Window();
                            layout.right[z, 0] = WallSlot.Window();
                        }
                    }
                    break;
                }
            }
        }

        private void PlacePerimeterWalls(BuildingRecipe recipe, Transform wallsRoot, Transform collisionRoot, StoryLayout layout, int story)
        {
            float y = story * StoryHeightMeters;
            float wallCenterY = y + StoryHeightMeters * 0.5f;

            for (int x = 0; x < recipe.widthCells; x++)
            {
                Vector3 frontPos = GetFrontWallPosition(recipe, x, wallCenterY);
                PlaceWallFromSlot(recipe, wallsRoot, collisionRoot, layout.front[x, 0], frontPos, Quaternion.identity);

                Vector3 backPos = GetBackWallPosition(recipe, x, wallCenterY);
                PlaceWallFromSlot(recipe, wallsRoot, collisionRoot, layout.back[x, 0], backPos, Quaternion.Euler(0f, 180f, 0f));
            }

            for (int z = 0; z < recipe.depthCells; z++)
            {
                Vector3 leftPos = GetLeftWallPosition(recipe, z, wallCenterY);
                PlaceWallFromSlot(recipe, wallsRoot, collisionRoot, layout.left[z, 0], leftPos, Quaternion.Euler(0f, -90f, 0f));

                Vector3 rightPos = GetRightWallPosition(recipe, z, wallCenterY);
                PlaceWallFromSlot(recipe, wallsRoot, collisionRoot, layout.right[z, 0], rightPos, Quaternion.Euler(0f, 90f, 0f));
            }
        }

        private void PlaceWallFromSlot(BuildingRecipe recipe, Transform parent, Transform collisionRoot, WallSlot slot, Vector3 localPosition, Quaternion localRotation)
        {
            if (string.IsNullOrEmpty(slot.subtype))
            {
                return;
            }

            if (!TryGetPiece(recipe.style, PieceCategory.Wall, slot.subtype, 1, 1, 1, out KitPiece piece) && slot.allowFallbackToSolid)
            {
                TryGetPiece(recipe.style, PieceCategory.Wall, "Solid", 1, 1, 1, out piece);
            }

            if (piece == null)
            {
                return;
            }

            PlacePrefabPiece(piece, parent, localPosition, localRotation);
        }

        private void FillSurface(BuildingRecipe recipe, Transform parent, PieceCategory category, string subtype, float y)
        {
            if (!TryGetPiece(recipe.style, category, subtype, 1, 1, 1, out KitPiece tile))
            {
                return;
            }

            for (int x = 0; x < recipe.widthCells; x++)
            {
                for (int z = 0; z < recipe.depthCells; z++)
                {
                    PlacePrefabPiece(tile, parent, GetCellCenter(recipe, x, z, y), Quaternion.identity);
                }
            }
        }

        private void BuildRoof(BuildingRecipe recipe, Transform parent)
        {
            float roofBaseY = recipe.stories * StoryHeightMeters;
            if (recipe.roofMode == RoofMode.Flat)
            {
                if (TryGetPiece(recipe.style, PieceCategory.Roof, "Flat", 1, 1, 1, out KitPiece flat))
                {
                    for (int x = 0; x < recipe.widthCells; x++)
                    {
                        for (int z = 0; z < recipe.depthCells; z++)
                        {
                            PlacePrefabPiece(flat, parent, GetCellCenter(recipe, x, z, roofBaseY), Quaternion.identity);
                        }
                    }
                }
                return;
            }

            bool hasLeft = TryGetPiece(recipe.style, PieceCategory.Roof, "GableLeft", 1, 1, 1, out KitPiece gableLeft);
            bool hasRight = TryGetPiece(recipe.style, PieceCategory.Roof, "GableRight", 1, 1, 1, out KitPiece gableRight);
            bool hasRidge = TryGetPiece(recipe.style, PieceCategory.Roof, "Ridge", 1, 1, 1, out KitPiece ridge);
            if (!hasLeft && !hasRight && !hasRidge)
            {
                return;
            }

            float centerLine = (recipe.depthCells - 1) * 0.5f;
            for (int x = 0; x < recipe.widthCells; x++)
            {
                for (int z = 0; z < recipe.depthCells; z++)
                {
                    float distanceToCenter = Mathf.Abs(z - centerLine);
                    float y = roofBaseY + Mathf.Max(0f, (recipe.depthCells * 0.5f - distanceToCenter - 0.5f) * 0.15f);
                    Vector3 pos = GetCellCenter(recipe, x, z, y);

                    if (Mathf.Abs(z - centerLine) < 0.25f && ridge != null)
                    {
                        PlacePrefabPiece(ridge, parent, pos, Quaternion.identity);
                    }
                    else if (z < centerLine)
                    {
                        PlacePrefabPiece(gableLeft != null ? gableLeft : gableRight, parent, pos, Quaternion.identity);
                    }
                    else
                    {
                        PlacePrefabPiece(gableRight != null ? gableRight : gableLeft, parent, pos, Quaternion.Euler(0f, 180f, 0f));
                    }
                }
            }
        }

        private void BuildPorch(BuildingRecipe recipe, Transform porchRoot, Transform collisionRoot)
        {
            int porchDepth = Mathf.Clamp(recipe.porchDepthCells, 1, 3);
            if (TryGetPiece(recipe.style, PieceCategory.Porch, "Floor", 1, 1, 1, out KitPiece porchFloor)
                || TryGetPiece(recipe.style, PieceCategory.Floor, "Base", 1, 1, 1, out porchFloor))
            {
                for (int x = 0; x < recipe.widthCells; x++)
                {
                    for (int d = 0; d < porchDepth; d++)
                    {
                        Vector3 pos = new Vector3(
                            GetLocalMinX(recipe) + x * CellSizeMeters + CellSizeMeters * 0.5f,
                            0f,
                            GetLocalMinZ(recipe) - (d + 0.5f) * CellSizeMeters);
                        PlacePrefabPiece(porchFloor, porchRoot, pos, Quaternion.identity);
                    }
                }
            }

            if (TryGetPiece(recipe.style, PieceCategory.Porch, "Post", 1, 1, 1, out KitPiece post))
            {
                for (int x = 0; x <= recipe.widthCells; x += Mathf.Max(1, recipe.widthCells - 1))
                {
                    float localX = GetLocalMinX(recipe) + x * CellSizeMeters;
                    float localZ = GetLocalMinZ(recipe) - porchDepth * CellSizeMeters;
                    PlacePrefabPiece(post, porchRoot, new Vector3(localX, StoryHeightMeters * 0.5f, localZ), Quaternion.identity);
                }
            }

            if (TryGetPiece(recipe.style, PieceCategory.Porch, "Beam", 1, 1, 1, out KitPiece beam))
            {
                for (int x = 0; x < recipe.widthCells; x++)
                {
                    float localX = GetLocalMinX(recipe) + x * CellSizeMeters + CellSizeMeters * 0.5f;
                    float localZ = GetLocalMinZ(recipe) - porchDepth * CellSizeMeters;
                    PlacePrefabPiece(beam, porchRoot, new Vector3(localX, StoryHeightMeters, localZ), Quaternion.identity);
                }
            }
        }

        private void BuildTrim(BuildingRecipe recipe, Transform trimRoot, Transform detailsRoot)
        {
            if (TryGetPiece(recipe.style, PieceCategory.Trim, "Cornice", 1, 1, 1, out KitPiece cornice))
            {
                float y = recipe.stories * StoryHeightMeters;
                for (int x = 0; x < recipe.widthCells; x++)
                {
                    PlacePrefabPiece(cornice, trimRoot, GetFrontWallPosition(recipe, x, y), Quaternion.identity);
                    PlacePrefabPiece(cornice, trimRoot, GetBackWallPosition(recipe, x, y), Quaternion.Euler(0f, 180f, 0f));
                }
            }

            if (recipe.includeFalseFront)
            {
                if (TryGetPiece(recipe.style, PieceCategory.Detail, "FalseFrontTop", 1, 1, 1, out KitPiece falseFrontTop))
                {
                    for (int x = 0; x < recipe.widthCells; x++)
                    {
                        PlacePrefabPiece(falseFrontTop, detailsRoot, GetFrontWallPosition(recipe, x, recipe.stories * StoryHeightMeters + 0.8f), Quaternion.identity);
                    }
                }
            }
        }

        private void BuildCollision(BuildingRecipe recipe, Transform collisionRoot)
        {
            if (collisionRoot == null)
            {
                return;
            }

            float widthMeters = recipe.widthCells * CellSizeMeters;
            float depthMeters = recipe.depthCells * CellSizeMeters;
            float heightMeters = recipe.stories * StoryHeightMeters;

            BoxCollider shell = collisionRoot.gameObject.AddComponent<BoxCollider>();
            shell.center = new Vector3(0f, heightMeters * 0.5f, 0f);
            shell.size = new Vector3(widthMeters, heightMeters, depthMeters);

            if (recipe.includePorch && createPorches)
            {
                BoxCollider porch = collisionRoot.gameObject.AddComponent<BoxCollider>();
                porch.center = new Vector3(0f, 0.5f, -depthMeters * 0.5f - recipe.porchDepthCells * CellSizeMeters * 0.5f);
                porch.size = new Vector3(widthMeters, 1f, recipe.porchDepthCells * CellSizeMeters);
            }
        }

        private GameObject PlacePrefabPiece(KitPiece piece, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            if (piece == null || piece.prefab == null)
            {
                return null;
            }

            GameObject instance;
            if (useNestedPrefabs)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(piece.prefab);
                if (instance == null)
                {
                    instance = Instantiate(piece.prefab);
                }
            }
            else
            {
                instance = Instantiate(piece.prefab);
            }

            instance.name = piece.prefab.name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        private static Transform CreateChild(Transform parent, string childName)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Bounds CalculatePrefabBounds(GameObject prefab, out bool hasBounds)
        {
            hasBounds = false;
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private static string ComposeRegistryKey(string style, PieceCategory category, string subtype, int width, int depth, int height)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|W{3}|D{4}|H{5}",
                style?.Trim() ?? string.Empty,
                category,
                subtype?.Trim() ?? string.Empty,
                width,
                depth,
                height);
        }

        private bool TryGetPiece(string style, PieceCategory category, string subtype, int width, int depth, int height, out KitPiece piece)
        {
            piece = null;
            string exactKey = ComposeRegistryKey(style, category, subtype, width, depth, height);
            if (registry.TryGetValue(exactKey, out List<KitPiece> exactList) && exactList.Count > 0)
            {
                piece = exactList[0];
                return true;
            }

            string fallbackVariantKey = ComposeRegistryKey(style, category, subtype, width, depth, 1);
            if (height != 1 && registry.TryGetValue(fallbackVariantKey, out List<KitPiece> fallbackHeightList) && fallbackHeightList.Count > 0)
            {
                piece = fallbackHeightList[0];
                return true;
            }

            if (string.Equals(subtype, "Floor", StringComparison.OrdinalIgnoreCase) && TryGetPiece(style, category, "Base", width, depth, height, out piece))
            {
                return true;
            }

            if (string.Equals(subtype, "WindowDoor", StringComparison.OrdinalIgnoreCase) && TryGetPiece(style, PieceCategory.Wall, "Door", width, depth, height, out piece))
            {
                return true;
            }

            if (string.Equals(subtype, "Post", StringComparison.OrdinalIgnoreCase) && TryGetPiece(style, PieceCategory.Detail, "Post", width, depth, height, out piece))
            {
                return true;
            }

            return false;
        }

        private void EnsureKitReady()
        {
            if (kitPieces.Count == 0)
            {
                RefreshKit();
            }
        }

        private void RevealOutputFolder()
        {
            string folder = EnsureOutputFolder(string.Empty);
            UnityEngine.Object folderObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folder);
            if (folderObject != null)
            {
                EditorGUIUtility.PingObject(folderObject);
                Selection.activeObject = folderObject;
            }
        }

        private string EnsureOutputFolder(string subfolder)
        {
            string root = EnsureFolderPath(outputFolder);
            if (string.IsNullOrWhiteSpace(subfolder))
            {
                return root;
            }

            string child = Path.Combine(root, subfolder).Replace('\\', '/');
            return EnsureFolderPath(child);
        }

        private static string EnsureFolderPath(string folderPath)
        {
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return folderPath;
            }

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                throw new InvalidOperationException("Folder path is empty.");
            }

            string current = parts[0];
            if (current != "Assets")
            {
                throw new InvalidOperationException("Folder path must live under Assets.");
            }

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }

            return current;
        }

        private static bool IsDirectChildOfFolder(string assetPath, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            string normalizedAssetPath = assetPath.Replace('\\', '/').TrimEnd('/');
            string normalizedFolderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            string assetDirectory = Path.GetDirectoryName(normalizedAssetPath)?.Replace('\\', '/') ?? string.Empty;
            return string.Equals(assetDirectory, normalizedFolderPath, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyProfileDefaults(BuildingProfile profile)
        {
            switch (profile)
            {
                case BuildingProfile.FrontierHouseSmall:
                    buildingName = "Frontier House Small";
                    widthCells = 4;
                    depthCells = 4;
                    stories = 1;
                    includePorch = true;
                    includeBackDoor = false;
                    includeBalcony = false;
                    includeFalseFront = false;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.FrontierHouseMedium:
                    buildingName = "Frontier House Medium";
                    widthCells = 5;
                    depthCells = 4;
                    stories = 1;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = false;
                    includeFalseFront = false;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.GeneralStoreNarrow:
                    buildingName = "General Store Narrow";
                    widthCells = 5;
                    depthCells = 4;
                    stories = 2;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = false;
                    includeFalseFront = true;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.GeneralStoreWide:
                    buildingName = "General Store Wide";
                    widthCells = 6;
                    depthCells = 4;
                    stories = 2;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = false;
                    includeFalseFront = true;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.DoctorOffice:
                    buildingName = "Doctor Office";
                    widthCells = 4;
                    depthCells = 4;
                    stories = 1;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = false;
                    includeFalseFront = false;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.SheriffOffice:
                    buildingName = "Sheriff Office";
                    widthCells = 4;
                    depthCells = 4;
                    stories = 1;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = false;
                    includeFalseFront = false;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.SaloonTwoStory:
                    buildingName = "Saloon";
                    widthCells = 6;
                    depthCells = 4;
                    stories = 2;
                    includePorch = true;
                    includeBackDoor = true;
                    includeBalcony = true;
                    includeFalseFront = true;
                    porchDepthCells = 1f;
                    break;
                case BuildingProfile.ChurchSimple:
                    buildingName = "Church";
                    widthCells = 5;
                    depthCells = 6;
                    stories = 1;
                    includePorch = false;
                    includeBackDoor = false;
                    includeBalcony = false;
                    includeFalseFront = false;
                    porchDepthCells = 0f;
                    break;
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value.Trim();
        }

        private static float GetLocalMinX(BuildingRecipe recipe)
        {
            return -recipe.widthCells * CellSizeMeters * 0.5f;
        }

        private static float GetLocalMinZ(BuildingRecipe recipe)
        {
            return -recipe.depthCells * CellSizeMeters * 0.5f;
        }

        private static Vector3 GetCellCenter(BuildingRecipe recipe, int x, int z, float y)
        {
            return new Vector3(
                GetLocalMinX(recipe) + x * CellSizeMeters + CellSizeMeters * 0.5f,
                y,
                GetLocalMinZ(recipe) + z * CellSizeMeters + CellSizeMeters * 0.5f);
        }

        private static Vector3 GetFrontWallPosition(BuildingRecipe recipe, int x, float centerY)
        {
            return new Vector3(
                GetLocalMinX(recipe) + x * CellSizeMeters + CellSizeMeters * 0.5f,
                centerY,
                GetLocalMinZ(recipe));
        }

        private static Vector3 GetBackWallPosition(BuildingRecipe recipe, int x, float centerY)
        {
            return new Vector3(
                GetLocalMinX(recipe) + x * CellSizeMeters + CellSizeMeters * 0.5f,
                centerY,
                GetLocalMinZ(recipe) + recipe.depthCells * CellSizeMeters);
        }

        private static Vector3 GetLeftWallPosition(BuildingRecipe recipe, int z, float centerY)
        {
            return new Vector3(
                GetLocalMinX(recipe),
                centerY,
                GetLocalMinZ(recipe) + z * CellSizeMeters + CellSizeMeters * 0.5f);
        }

        private static Vector3 GetRightWallPosition(BuildingRecipe recipe, int z, float centerY)
        {
            return new Vector3(
                GetLocalMinX(recipe) + recipe.widthCells * CellSizeMeters,
                centerY,
                GetLocalMinZ(recipe) + z * CellSizeMeters + CellSizeMeters * 0.5f);
        }
    }
}
#endif
