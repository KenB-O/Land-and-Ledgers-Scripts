using System.IO;
using LandLedgers.Time;
using LandLedgers.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LandLedgers.Editor
{
    public static class LandLedgersHUDSetupUtility
    {
        private const string PrefabPath = "Assets/Core/UI/UI Canvas.prefab";
        private const string ScenePath = "Assets/Main Scene.unity";

        [MenuItem("Land & Ledgers/UI/Create HUD In Scene")]
        public static void CreateHUDInScene()
        {
            GameObject hudPrefab = CreateOrUpdatePrefabAsset();
            GameObject existing = FindExistingHUDRoot();

            if (existing != null)
            {
                Selection.activeGameObject = existing;
                Debug.Log("A Land & Ledgers HUD already exists in the open scene.", existing);
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab);
            instance.name = "LandLedgers_HUDCanvas";
            EnsureTimeManager();
            EnsureEventSystem();

            Undo.RegisterCreatedObjectUndo(instance, "Create Land & Ledgers HUD");
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }

        [MenuItem("Land & Ledgers/UI/Create Or Update HUD Prefab")]
        public static void CreateOrUpdatePrefab()
        {
            Selection.activeObject = CreateOrUpdatePrefabAsset();
        }

        public static void BootstrapDefaultSceneAndAssets()
        {
            GameObject hudPrefab = CreateOrUpdatePrefabAsset();
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(ScenePath);

            EnsureTimeManager();
            EnsureEventSystem();

            GameObject existing = FindExistingHUDRoot();
            if (existing == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, scene);
                instance.name = "LandLedgers_HUDCanvas";
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public static GameObject CreateOrUpdatePrefabAsset()
        {
            LandLedgersTypography.ConfigureTmpSettings();
            GameObject hudRoot = BuildHUD();

            string directory = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(hudRoot, PrefabPath);
            Object.DestroyImmediate(hudRoot);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefab;
        }

        private static GameObject BuildHUD()
        {
            GameObject canvasObject = new("LandLedgers_HUDCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(LandLedgersHUDController));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            StretchToParent(canvasRect);

            GameObject root = CreatePanel("HUD_Root", canvasRect, new Color(0f, 0f, 0f, 0f));
            RectTransform rootRect = root.GetComponent<RectTransform>();
            StretchToParent(rootRect);

            GameObject topBar = CreatePanel("HUD_TopBar", rootRect, new Color(0.075f, 0.085f, 0.08f, 0.95f));
            RectTransform topBarRect = topBar.GetComponent<RectTransform>();
            topBarRect.anchorMin = new Vector2(0f, 1f);
            topBarRect.anchorMax = new Vector2(1f, 1f);
            topBarRect.pivot = new Vector2(0.5f, 1f);
            topBarRect.anchoredPosition = Vector2.zero;
            topBarRect.sizeDelta = new Vector2(0f, 72f);
            HorizontalLayoutGroup topLayout = topBar.AddComponent<HorizontalLayoutGroup>();
            topLayout.padding = new RectOffset(24, 24, 10, 10);
            topLayout.spacing = 18f;
            topLayout.childAlignment = TextAnchor.MiddleCenter;
            topLayout.childControlWidth = true;
            topLayout.childControlHeight = true;
            topLayout.childForceExpandWidth = true;
            topLayout.childForceExpandHeight = true;

            GameObject leftCluster = CreateTransparentObject("HUD_LeftCluster", topBarRect);
            LayoutElement leftLayout = leftCluster.AddComponent<LayoutElement>();
            leftLayout.flexibleWidth = 1f;
            leftLayout.minWidth = 440f;
            HorizontalLayoutGroup leftGroup = leftCluster.AddComponent<HorizontalLayoutGroup>();
            leftGroup.childAlignment = TextAnchor.MiddleLeft;
            leftGroup.childForceExpandWidth = false;
            leftGroup.childForceExpandHeight = true;

            TMP_Text cash = CreateText("HUD_CashText", leftCluster.transform, "Net Worth $12,500 | Cash $12,500", 24, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            cash.color = new Color(0.93f, 0.91f, 0.82f, 1f);
            cash.rectTransform.sizeDelta = new Vector2(440f, 44f);

            GameObject centerCluster = CreateTransparentObject("HUD_DateCluster", topBarRect);
            LayoutElement centerLayout = centerCluster.AddComponent<LayoutElement>();
            centerLayout.flexibleWidth = 1.5f;
            centerLayout.minWidth = 520f;
            HorizontalLayoutGroup centerGroup = centerCluster.AddComponent<HorizontalLayoutGroup>();
            centerGroup.childAlignment = TextAnchor.MiddleCenter;

            TMP_Text date = CreateText("HUD_DateText", centerCluster.transform, "Monday, April 1, Year 1 (Week 1)", 22, FontStyles.Bold, TextAlignmentOptions.Center);
            date.color = new Color(0.92f, 0.93f, 0.88f, 1f);
            date.textWrappingMode = TextWrappingModes.NoWrap;
            date.rectTransform.sizeDelta = new Vector2(620f, 44f);

            GameObject controls = CreateTransparentObject("HUD_TimeSpeedControls", topBarRect);
            LayoutElement controlsLayout = controls.AddComponent<LayoutElement>();
            controlsLayout.flexibleWidth = 1f;
            controlsLayout.minWidth = 620f;
            HorizontalLayoutGroup controlsGroup = controls.AddComponent<HorizontalLayoutGroup>();
            controlsGroup.spacing = 8f;
            controlsGroup.childAlignment = TextAnchor.MiddleRight;
            controlsGroup.childForceExpandWidth = false;
            controlsGroup.childForceExpandHeight = false;

            Button pause = CreateSpeedButton("HUD_TimeSpeed_Pause", controls.transform, "II");
            Button speed1x = CreateSpeedButton("HUD_TimeSpeed_1x", controls.transform, "1x");
            Button speed2x = CreateSpeedButton("HUD_TimeSpeed_2x", controls.transform, "2x");
            Button speed4x = CreateSpeedButton("HUD_TimeSpeed_4x", controls.transform, "4x");
            Button speed6x = CreateSpeedButton("HUD_TimeSpeed_6x", controls.transform, "6x");
            Button speed10x = CreateSpeedButton("HUD_TimeSpeed_10x", controls.transform, "10x");
            Button speed25x = CreateSpeedButton("HUD_TimeSpeed_25x", controls.transform, "25x");
            Button speed50x = CreateSpeedButton("HUD_TimeSpeed_50x", controls.transform, "50x");
            Button speed100x = CreateSpeedButton("HUD_TimeSpeed_100x", controls.transform, "100x");

            GameObject alertStrip = CreatePanel("HUD_AlertStrip", rootRect, new Color(0.095f, 0.105f, 0.1f, 0.82f));
            RectTransform alertRect = alertStrip.GetComponent<RectTransform>();
            alertRect.anchorMin = new Vector2(0f, 1f);
            alertRect.anchorMax = new Vector2(1f, 1f);
            alertRect.pivot = new Vector2(0.5f, 1f);
            alertRect.anchoredPosition = new Vector2(0f, -72f);
            alertRect.sizeDelta = new Vector2(0f, 52f);
            HorizontalLayoutGroup alertLayout = alertStrip.AddComponent<HorizontalLayoutGroup>();
            alertLayout.padding = new RectOffset(24, 24, 8, 8);
            alertLayout.spacing = 12f;
            alertLayout.childAlignment = TextAnchor.MiddleLeft;
            alertLayout.childForceExpandWidth = false;
            alertLayout.childForceExpandHeight = true;

            TMP_Text alertPlaceholder = CreateText("HUD_AlertStrip_Placeholder", alertStrip.transform, "No active alerts", 16, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            alertPlaceholder.color = new Color(0.73f, 0.76f, 0.69f, 1f);
            alertPlaceholder.rectTransform.sizeDelta = new Vector2(620f, 36f);

            CreateFirstSessionGuidanceStrip(
                rootRect,
                out RectTransform firstSessionGuidanceStrip,
                out TMP_Text firstSessionObjectiveText,
                out TMP_Text firstSessionWhyText,
                out TMP_Text firstSessionActionText,
                out TMP_Text firstSessionBlockerText);

            LandLedgersHUDController controller = canvasObject.GetComponent<LandLedgersHUDController>();
            SerializedObject serializedController = new(controller);
            serializedController.FindProperty("cashText").objectReferenceValue = cash;
            serializedController.FindProperty("dateText").objectReferenceValue = date;
            serializedController.FindProperty("pauseButton").objectReferenceValue = pause;
            serializedController.FindProperty("speed1xButton").objectReferenceValue = speed1x;
            serializedController.FindProperty("speed2xButton").objectReferenceValue = speed2x;
            serializedController.FindProperty("speed4xButton").objectReferenceValue = speed4x;
            serializedController.FindProperty("speed6xButton").objectReferenceValue = speed6x;
            serializedController.FindProperty("speed10xButton").objectReferenceValue = speed10x;
            serializedController.FindProperty("speed25xButton").objectReferenceValue = speed25x;
            serializedController.FindProperty("speed50xButton").objectReferenceValue = speed50x;
            serializedController.FindProperty("speed100xButton").objectReferenceValue = speed100x;
            serializedController.FindProperty("alertStrip").objectReferenceValue = alertRect;
            serializedController.FindProperty("townPulseAlertText").objectReferenceValue = alertPlaceholder;
            serializedController.FindProperty("firstSessionGuidanceStrip").objectReferenceValue = firstSessionGuidanceStrip;
            serializedController.FindProperty("firstSessionObjectiveText").objectReferenceValue = firstSessionObjectiveText;
            serializedController.FindProperty("firstSessionWhyText").objectReferenceValue = firstSessionWhyText;
            serializedController.FindProperty("firstSessionActionText").objectReferenceValue = firstSessionActionText;
            serializedController.FindProperty("firstSessionBlockerText").objectReferenceValue = firstSessionBlockerText;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            return canvasObject;
        }

        private static void CreateFirstSessionGuidanceStrip(
            Transform parent,
            out RectTransform stripRect,
            out TMP_Text objectiveText,
            out TMP_Text whyText,
            out TMP_Text actionText,
            out TMP_Text blockerText)
        {
            GameObject strip = CreatePanel("HUD_FirstSessionObjectiveStrip", parent, new Color(0.08f, 0.09f, 0.08f, 0.94f));
            strip.SetActive(false);
            stripRect = strip.GetComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(0f, 1f);
            stripRect.anchorMax = new Vector2(0f, 1f);
            stripRect.pivot = new Vector2(0f, 1f);
            stripRect.anchoredPosition = new Vector2(20f, -138f);
            stripRect.sizeDelta = new Vector2(620f, 164f);

            Image background = strip.GetComponent<Image>();
            background.raycastTarget = false;

            LayoutElement stripLayout = strip.AddComponent<LayoutElement>();
            stripLayout.preferredWidth = 620f;
            stripLayout.preferredHeight = 164f;

            GameObject content = CreateTransparentObject("HUD_FirstSessionObjectiveContent", stripRect);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.offsetMin = new Vector2(16f, -116f);
            contentRect.offsetMax = new Vector2(-16f, -12f);

            VerticalLayoutGroup layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            objectiveText = CreateGuidanceText("HUD_FirstSessionObjectiveText", content.transform, "Open Management", 17f, FontStyles.Bold, TextAlignmentOptions.Left);
            objectiveText.color = new Color(1f, 0.96f, 0.84f, 1f);
            ConfigureGuidanceLayoutElement(objectiveText.rectTransform, 24f);

            whyText = CreateGuidanceText("HUD_FirstSessionWhyText", content.transform, "Why: Management holds the core loop.", 14.5f, FontStyles.Normal, TextAlignmentOptions.Left);
            whyText.color = new Color(0.82f, 0.84f, 0.78f, 1f);
            ConfigureGuidanceLayoutElement(whyText.rectTransform, 22f);

            actionText = CreateGuidanceText("HUD_FirstSessionActionText", content.transform, "Do Now: Press C.", 14.5f, FontStyles.Bold, TextAlignmentOptions.Left);
            actionText.color = new Color(1f, 0.96f, 0.84f, 1f);
            ConfigureGuidanceLayoutElement(actionText.rectTransform, 22f);

            blockerText = CreateGuidanceText("HUD_FirstSessionBlockerText", stripRect, string.Empty, 13.5f, FontStyles.Normal, TextAlignmentOptions.Left);
            blockerText.color = new Color(1f, 0.74f, 0.48f, 1f);
            RectTransform blockerRect = blockerText.rectTransform;
            blockerRect.anchorMin = new Vector2(0f, 0f);
            blockerRect.anchorMax = new Vector2(1f, 0f);
            blockerRect.pivot = new Vector2(0.5f, 0f);
            blockerRect.anchoredPosition = new Vector2(0f, 12f);
            blockerRect.sizeDelta = new Vector2(-32f, 24f);
            blockerText.gameObject.SetActive(false);
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            Image image = panel.GetComponent<Image>();
            image.color = color;
            return panel;
        }

        private static GameObject CreateTransparentObject(string name, Transform parent)
        {
            GameObject obj = new(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            return obj;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text tmp = textObject.GetComponent<TMP_Text>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            LandLedgersTypography.ApplyRole(tmp, ResolveRole(name, fontStyle));
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return tmp;
        }

        private static TMP_Text CreateGuidanceText(string name, Transform parent, string text, float fontSize, FontStyles fontStyle, TextAlignmentOptions alignment)
        {
            TMP_Text tmp = CreateText(name, parent, text, fontSize, fontStyle, alignment);
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static void ConfigureGuidanceLayoutElement(RectTransform rect, float preferredHeight)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = Vector2.zero;

            LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
            layout.preferredHeight = preferredHeight;
        }

        private static Button CreateSpeedButton(string name, Transform parent, string label)
        {
            GameObject buttonObject = new(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(56f, 42f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.17f, 0.92f);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = 56f;
            layout.preferredHeight = 42f;
            layout.minWidth = 56f;
            layout.minHeight = 42f;

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.08f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.72f, 1f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            TMP_Text text = CreateText("Label", buttonObject.transform, label, 18, FontStyles.Bold, TextAlignmentOptions.Center);
            LandLedgersTypography.ApplyRole(text, LandLedgersTypography.TextRole.HudUtility);
            text.color = new Color(0.82f, 0.84f, 0.78f, 1f);
            RectTransform textRect = text.rectTransform;
            StretchToParent(textRect);

            return button;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }

        private static LandLedgersTypography.TextRole ResolveRole(string objectName, FontStyles style)
        {
            if (objectName.Contains("Cash", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.FeaturedValue;
            }

            if (objectName.Contains("Date", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.HudLabel;
            }

            if (objectName.Contains("Alert", System.StringComparison.OrdinalIgnoreCase))
            {
                return LandLedgersTypography.TextRole.Badge;
            }

            return style == FontStyles.Bold ? LandLedgersTypography.TextRole.HudLabel : LandLedgersTypography.TextRole.HudUtility;
        }

        private static void EnsureTimeManager()
        {
            TimeManager manager = Object.FindAnyObjectByType<TimeManager>();
            if (manager != null)
            {
                return;
            }

            SimulationTimeSettings settings = TimeManagerSetupUtility.GetOrCreateSettingsAsset();
            GameObject managerObject = new("Time Manager");
            manager = managerObject.AddComponent<TimeManager>();
            SerializedObject serializedManager = new(manager);
            serializedManager.FindProperty("settings").objectReferenceValue = settings;
            serializedManager.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(managerObject.scene);
        }

        private static void EnsureEventSystem()
        {
            EventSystem existing = Object.FindAnyObjectByType<EventSystem>();
            if (existing != null)
            {
                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                {
                    existing.gameObject.AddComponent<InputSystemUIInputModule>();
                }

                return;
            }

            GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EditorSceneManager.MarkSceneDirty(eventSystem.scene);
        }

        private static GameObject FindExistingHUDRoot()
        {
            LandLedgersHUDController controller = Object.FindAnyObjectByType<LandLedgersHUDController>();
            if (controller != null)
            {
                return controller.gameObject;
            }

            GameObject namedHud = GameObject.Find("LandLedgers_HUDCanvas");
            return namedHud;
        }
    }
}
