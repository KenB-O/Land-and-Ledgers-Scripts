using LandLedgers.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandLedgers.EditorTools.World
{
    public static class PersonInspectionPanelAuthoring
    {
        private const string MainScenePath = "Assets/Main Scene.unity";
        private const string RootProperty = "personInspectionRoot";
        private const string TitleProperty = "personInspectionTitle";
        private const string BodyProperty = "personInspectionBody";
        private const string CloseProperty = "personInspectionCloseButton";
        private const string FollowProperty = "personInspectionFollowButton";
        private const string ObserveNextProperty = "personInspectionObserveNextButton";

        [MenuItem("Land Ledgers/Author Person Inspection Panel in Main Scene")]
        private static void AuthorMainScenePanel()
        {
            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.path != MainScenePath)
            {
                Debug.LogError($"Open {MainScenePath} before authoring the Person inspection panel.");
                return;
            }

            WorldInteractionController controller = Object.FindAnyObjectByType<WorldInteractionController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogError("Main Scene has no WorldInteractionController to bind to the Person panel.");
                return;
            }

            SerializedObject serializedController = new(controller);
            serializedController.Update();
            SerializedProperty rootProperty = serializedController.FindProperty(RootProperty);
            SerializedProperty titleProperty = serializedController.FindProperty(TitleProperty);
            SerializedProperty bodyProperty = serializedController.FindProperty(BodyProperty);
            SerializedProperty closeProperty = serializedController.FindProperty(CloseProperty);
            SerializedProperty followProperty = serializedController.FindProperty(FollowProperty);
            SerializedProperty observeNextProperty = serializedController.FindProperty(ObserveNextProperty);
            if (rootProperty == null || titleProperty == null || bodyProperty == null || closeProperty == null || followProperty == null || observeNextProperty == null)
            {
                Debug.LogError("WorldInteractionController is missing serialized Person panel fields.");
                return;
            }

            RectTransform root = rootProperty.objectReferenceValue as RectTransform;
            if (root == null)
            {
                Canvas canvas = FindGameplayCanvas();
                if (canvas == null)
                {
                    Debug.LogError("Main Scene has no authored Canvas for the Person inspection panel.");
                    return;
                }

                Transform existing = canvas.transform.Find("Person Inspection Popup");
                root = existing as RectTransform;
                if (root == null)
                {
                    GameObject panel = new("Person Inspection Popup", typeof(RectTransform), typeof(Image));
                    Undo.RegisterCreatedObjectUndo(panel, "Create Person Inspection Popup");
                    panel.transform.SetParent(canvas.transform, false);
                    root = (RectTransform)panel.transform;
                }
            }

            ConfigurePanel(root);
            TMP_FontAsset font = ResolveFont(root);
            TMP_Text title = EnsureText("Person Name", root, font, 24f, FontStyles.Bold);
            ConfigureTitle(title);
            TMP_Text body = EnsureText("Person Details", root, font, 17f, FontStyles.Normal);
            ConfigureBody(body);
            Button close = EnsureCloseButton(root, font);
            close.onClick.RemoveAllListeners();
            Button follow = EnsureFollowButton(root, font);
            Button observeNext = EnsureObserveNextButton(root, font);

            rootProperty.objectReferenceValue = root;
            titleProperty.objectReferenceValue = title;
            bodyProperty.objectReferenceValue = body;
            closeProperty.objectReferenceValue = close;
            followProperty.objectReferenceValue = follow;
            observeNextProperty.objectReferenceValue = observeNext;
            serializedController.ApplyModifiedProperties();
            root.gameObject.SetActive(false);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Authored and bound the persistent Person inspection panel in Main Scene.", controller);
        }

        private static Canvas FindGameplayCanvas()
        {
            try
            {
                GameObject taggedCanvas = GameObject.FindWithTag("UI Canvas");
                Canvas tagged = taggedCanvas != null ? taggedCanvas.GetComponent<Canvas>() : null;
                if (tagged != null) return tagged;
            }
            catch (UnityException)
            {
                // Fall back to the existing authored Canvas in scenes without the optional tag.
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return canvases.Length > 0 ? canvases[0] : null;
        }

        private static void ConfigurePanel(RectTransform root)
        {
            root.name = "Person Inspection Popup";
            root.anchorMin = new Vector2(1f, 0.5f);
            root.anchorMax = new Vector2(1f, 0.5f);
            root.pivot = new Vector2(1f, 0.5f);
            root.sizeDelta = new Vector2(410f, 560f);
            root.anchoredPosition = new Vector2(-24f, 0f);
            Image background = root.GetComponent<Image>();
            if (background == null) background = Undo.AddComponent<Image>(root.gameObject);
            background.color = new Color(0.14f, 0.105f, 0.075f, 0.96f);
        }

        private static TMP_FontAsset ResolveFont(RectTransform root)
        {
            if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;
            TMP_Text existing = root.GetComponentInChildren<TMP_Text>(true);
            return existing != null ? existing.font : null;
        }

        private static TMP_Text EnsureText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style)
        {
            Transform existing = parent.Find(name);
            GameObject child = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
                child.transform.SetParent(parent, false);
            }

            TMP_Text text = child.GetComponent<TMP_Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.95f, 0.9f, 0.8f, 1f);
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }

        private static void ConfigureTitle(TMP_Text title)
        {
            RectTransform rect = (RectTransform)title.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(22f, -60f);
            rect.offsetMax = new Vector2(-112f, -18f);
            title.alignment = TextAlignmentOptions.MidlineLeft;
        }

        private static void ConfigureBody(TMP_Text body)
        {
            RectTransform rect = (RectTransform)body.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(22f, 22f);
            rect.offsetMax = new Vector2(-22f, -76f);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.enableAutoSizing = true;
            body.fontSizeMin = 13f;
            body.fontSizeMax = 17f;
        }

        private static Button EnsureCloseButton(RectTransform root, TMP_FontAsset font)
        {
            Transform existing = root.Find("Close");
            GameObject closeObject = existing != null
                ? existing.gameObject
                : new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(closeObject, "Create Person Inspection Close Button");
                closeObject.transform.SetParent(root, false);
            }

            RectTransform rect = (RectTransform)closeObject.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(82f, 34f);
            rect.anchoredPosition = new Vector2(-16f, -16f);
            closeObject.GetComponent<Image>().color = new Color(0.37f, 0.26f, 0.17f, 1f);
            TMP_Text label = EnsureText("Close Label", closeObject.transform, font, 16f, FontStyles.Bold);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);
            label.alignment = TextAlignmentOptions.Center;
            label.text = "CLOSE";
            return closeObject.GetComponent<Button>();
        }

        private static Button EnsureFollowButton(RectTransform root, TMP_FontAsset font)
        {
            Transform existing = root.Find("Follow");
            GameObject followObject = existing != null
                ? existing.gameObject
                : new GameObject("Follow", typeof(RectTransform), typeof(Image), typeof(Button));
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(followObject, "Create Person Inspection Follow Button");
                followObject.transform.SetParent(root, false);
            }
            RectTransform rect = (RectTransform)followObject.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(90f, 34f);
            rect.anchoredPosition = new Vector2(-108f, -16f);
            followObject.GetComponent<Image>().color = new Color(0.37f, 0.26f, 0.17f, 1f);
            TMP_Text label = EnsureText("Follow Label", followObject.transform, font, 16f, FontStyles.Bold);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 4f);
            labelRect.offsetMax = new Vector2(-4f, -4f);
            label.alignment = TextAlignmentOptions.Center;
            label.text = "FOLLOW";
            return followObject.GetComponent<Button>();
        }

        private static Button EnsureObserveNextButton(RectTransform root, TMP_FontAsset font)
        {
            Transform existing = root.Find("Observe Next");
            GameObject buttonObject = existing != null
                ? existing.gameObject
                : new GameObject("Observe Next", typeof(RectTransform), typeof(Image), typeof(Button));
            if (existing == null)
            {
                Undo.RegisterCreatedObjectUndo(buttonObject, "Create Observe Next Activity Button");
                buttonObject.transform.SetParent(root, false);
            }
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.sizeDelta = new Vector2(94f, 34f);
            rect.anchoredPosition = new Vector2(-204f, -16f);
            buttonObject.GetComponent<Image>().color = new Color(0.31f, 0.34f, 0.25f, 1f);
            TMP_Text label = EnsureText("Observe Next Label", buttonObject.transform, font, 13f, FontStyles.Bold);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(3f, 4f);
            labelRect.offsetMax = new Vector2(-3f, -4f);
            label.alignment = TextAlignmentOptions.Center;
            label.text = "WATCH NEXT";
            return buttonObject.GetComponent<Button>();
        }
    }
}
