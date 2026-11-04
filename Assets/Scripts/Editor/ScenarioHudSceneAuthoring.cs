#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.EditorTools
{
    /// <summary>
    /// Idempotent scene-authoring command for the persistent scenario objective HUD.
    /// It creates inspectable uGUI objects and serializes their bindings on the
    /// existing ScenarioStartPanelController; it does not own scenario state.
    /// </summary>
    public static class ScenarioHudSceneAuthoring
    {
        private const string CanvasName = "UI Canvas";
        private const string PanelName = "Scenario Objective HUD (Authored)";

        [MenuItem("Land & Ledgers/Development/Bind Direct Town Configuration to Main Scene")]
        public static void BindDirectTownConfigurationToMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Development configuration binding is available only in Edit Mode.");
                return;
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Main Scene" || scene.path != "Assets/Main Scene.unity")
            {
                Debug.LogError("Open Assets/Main Scene.unity before binding the development town configuration.");
                return;
            }
            if (scene.isDirty)
            {
                Debug.LogError("Main Scene has pre-existing unsaved edits; configuration binding refused to avoid saving unrelated scene changes.");
                return;
            }
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            FirstLedgerSliceBootstrapper bootstrapper = FindUnique<FirstLedgerSliceBootstrapper>("Game Systems");
            if (configuration == null || bootstrapper == null)
            {
                Debug.LogError("Could not bind development configuration. " + (configuration == null ? "Asset missing." : "Bootstrapper missing."));
                return;
            }
            if (!configuration.Validate(out string validationError))
            {
                Debug.LogError("Could not bind invalid development configuration: " + validationError);
                return;
            }
            var serialized = new SerializedObject(bootstrapper);
            SerializedProperty field = serialized.FindProperty("developmentTownConfiguration");
            if (field == null)
            {
                Debug.LogError("Bootstrapper has no serialized developmentTownConfiguration field.");
                return;
            }
            field.objectReferenceValue = configuration;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrapper);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("Main Scene save failed after binding the validated town configuration.");
                return;
            }
            Debug.Log("[SceneAuthoring] Bound the validated DevelopmentTownConfiguration asset to the Main Scene bootstrapper and saved this isolated scene change.", bootstrapper);
        }

        [MenuItem("Land & Ledgers/Development/Author Runtime Entity Containers")]
        public static void AuthorRuntimeEntityContainers()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Runtime entity containers are authored in Edit Mode only.");
                return;
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/Main Scene.unity" || scene.isDirty)
            {
                Debug.LogError("Open a clean Main Scene before authoring runtime entity containers; the command refuses to save unrelated edits.");
                return;
            }
            GameObject townWorld = GameObject.Find("Town World");
            if (townWorld == null)
            {
                Debug.LogError("Runtime entity containers were not created: the authored Town World root is missing.");
                return;
            }
            Transform entities = FindOrCreate(townWorld.transform, "Runtime Entities");
            FindOrCreate(entities, "Animals");
            FindOrCreate(entities, "Transport");
            FindOrCreate(entities, "World Fixtures");
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("Runtime entity containers were authored but the isolated Main Scene save failed.");
                return;
            }
            Debug.Log("[SceneAuthoring] Main Scene now has stable Town World/Runtime Entities containers for Animals, Transport, and World Fixtures.", townWorld);
        }

        private static Transform FindOrCreate(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null) return child;
            var childObject = new GameObject(childName);
            UnityEditor.SceneManagement.EditorSceneManager.MoveGameObjectToScene(childObject, parent.gameObject.scene);
            childObject.transform.SetParent(parent, false);
            return childObject.transform;
        }

        [MenuItem("Land & Ledgers/Author Main Scene Scenario HUD")]
        public static void AuthorMainSceneScenarioHud()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Scenario HUD authoring is available only in Edit Mode.");
                return;
            }

            if (EditorSceneManager.GetActiveScene().name != "Main Scene")
            {
                Debug.LogError("Open Assets/Main Scene.unity before authoring the scenario HUD.");
                return;
            }

            Canvas canvas = FindUnique<Canvas>(CanvasName);
            ScenarioStartPanelController controller = FindUnique<ScenarioStartPanelController>("Game Systems");
            if (canvas == null || controller == null)
            {
                Debug.LogError("Scenario HUD authoring requires exactly one active UI Canvas and ScenarioStartPanelController.");
                return;
            }

            Transform existing = canvas.transform.Find(PanelName);
            GameObject panel = existing != null ? existing.gameObject : CreatePanel(canvas.transform);
            RectTransform viewport = panel.transform.Find("Viewport") as RectTransform;
            TMP_Text body = viewport != null ? viewport.Find("Content/Objectives")?.GetComponent<TMP_Text>() : null;
            Button toggle = panel.transform.Find("Header/Toggle")?.GetComponent<Button>();
            if (viewport == null || body == null || toggle == null)
            {
                Debug.LogError("Existing authored scenario HUD is incomplete; no scene changes were made.", panel);
                return;
            }

            RectTransform panelRect = panel.transform as RectTransform;
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -140f);
            panelRect.sizeDelta = new Vector2(460f, 520f);
            panelRect.SetAsLastSibling();
            panel.SetActive(false);
            SerializedObject serialized = new SerializedObject(controller);
            serialized.FindProperty("canvas").objectReferenceValue = canvas;
            serialized.FindProperty("authoredObjectivePanel").objectReferenceValue = panel;
            serialized.FindProperty("authoredObjectiveLabel").objectReferenceValue = body;
            serialized.FindProperty("authoredObjectiveViewport").objectReferenceValue = viewport;
            serialized.FindProperty("authoredObjectiveToggle").objectReferenceValue = toggle;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[ScenarioHUD] Authored objective hierarchy and controller bindings saved to Main Scene.", panel);
        }

        [MenuItem("Land & Ledgers/Author Compact Playtest Settlement")]
        public static void AuthorCompactPlaytestSettlement()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode
                || EditorSceneManager.GetActiveScene().name != "Main Scene")
            {
                Debug.LogError("Open Main Scene in Edit Mode before authoring the compact playtest settlement.");
                return;
            }

            const string settingsPath = "Assets/Core/Population/CompactPlaytestPopulationSettings.asset";
            PopulationGenerationSettings settings = AssetDatabase.LoadAssetAtPath<PopulationGenerationSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            settings.seedOffset = 511;
            settings.householdOccupancyRate = 0.92f;
            settings.minHouseholdSize = 1;
            settings.maxHouseholdSize = 6;
            settings.minimumStartingHouseholds = 8;
            settings.maximumStartingHouseholds = 10;
            settings.minimumStartingResidents = 22;
            settings.targetStartingResidents = 26;
            settings.maximumStartingResidents = 30;
            settings.newcomerArrivalRate = 0.05f;
            settings.minimumNewcomerArrivalHouseholds = 0;
            settings.Sanitize();
            EditorUtility.SetDirty(settings);

            FirstLedgerSliceBootstrapper bootstrapper = FindUnique<FirstLedgerSliceBootstrapper>("Game Systems");
            if (bootstrapper == null)
            {
                Debug.LogError("Compact playtest profile was saved, but Main Scene needs exactly one Game Systems bootstrapper.");
                return;
            }

            SerializedObject serialized = new SerializedObject(bootstrapper);
            serialized.FindProperty("populationSettings").objectReferenceValue = settings;
            serialized.FindProperty("useCompactPlaytestOpeningRoster").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[CompactPlaytest] Main Scene now uses an 8–10 household, 22–30 resident profile and three legitimate opening Businesses. Canonical defaults remain unchanged.", bootstrapper);
        }

        private static T FindUnique<T>(string objectName) where T : Component
        {
            T[] matches = UnityEngine.Object.FindObjectsByType<T>();
            T found = null;
            for (int i = 0; i < matches.Length; i++)
            {
                if (matches[i] == null || matches[i].gameObject.name != objectName) continue;
                if (found != null)
                {
                    Debug.LogError($"More than one {typeof(T).Name} named '{objectName}' exists.");
                    return null;
                }
                found = matches[i];
            }
            return found;
        }

        private static GameObject CreatePanel(Transform canvas)
        {
            GameObject root = new GameObject(PanelName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            Undo.RegisterCreatedObjectUndo(root, "Author Scenario Objective HUD");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(canvas, false);
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(24f, -140f);
            rootRect.sizeDelta = new Vector2(460f, 520f);
            root.GetComponent<Image>().color = new Color(0.055f, 0.06f, 0.055f, 0.95f);

            GameObject header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(root.transform, false);
            RectTransform headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = Vector2.one;
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(10f, -48f);
            headerRect.offsetMax = new Vector2(-10f, -6f);

            GameObject toggleObject = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Button));
            toggleObject.transform.SetParent(header.transform, false);
            RectTransform toggleRect = toggleObject.GetComponent<RectTransform>();
            toggleRect.anchorMin = Vector2.zero;
            toggleRect.anchorMax = Vector2.one;
            toggleRect.offsetMin = Vector2.zero;
            toggleRect.offsetMax = Vector2.zero;
            Image toggleImage = toggleObject.GetComponent<Image>();
            toggleImage.color = new Color(0.25f, 0.20f, 0.12f, 1f);
            Button button = toggleObject.GetComponent<Button>();
            button.targetGraphic = toggleImage;
            CreateText(toggleObject.transform, "SCENARIO OBJECTIVES  −", 18f, FontStyles.Bold);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(root.transform, false);
            RectTransform viewport = viewportObject.GetComponent<RectTransform>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(12f, 12f);
            viewport.offsetMax = new Vector2(-12f, -58f);

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform content = contentObject.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            TMP_Text body = CreateText(contentObject.transform, "Scenario objectives populate from the active scenario.", 16f, FontStyles.Normal);
            body.name = "Objectives";
            body.alignment = TextAlignmentOptions.TopLeft;
            body.overflowMode = TextOverflowModes.Overflow;

            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            return root;
        }

        private static TMP_Text CreateText(Transform parent, string value, float size, FontStyles style)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 4f);
            rect.offsetMax = new Vector2(-12f, -4f);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.92f, 0.9f, 0.8f, 1f);
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
    }
}
#endif
