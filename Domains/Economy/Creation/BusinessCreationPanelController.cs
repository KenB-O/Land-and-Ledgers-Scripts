using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.FirstLedger
{
    /// <summary>
    /// Player-facing entry point for the permanent business-formation authority.
    /// The control is created beside the authored management shell so an older scene
    /// can receive the workflow without duplicating business logic in a prefab.
    /// Formation creates identity and ownership only; premises and operation remain
    /// separate decisions in the existing management/acquisition flows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BusinessCreationPanelController : MonoBehaviour
    {
        private SharedBusinessRuntimeManager sharedBusinessRuntime;
        private GeneralStorePanelController managementController;
        private ManagementPanelView view;
        private Button createButton;
        private GameObject overlay;
        private Button typeButton;
        private BusinessType[] businessTypes = Array.Empty<BusinessType>();
        private int selectedTypeIndex;
        private TMP_InputField nameInput;
        private TMP_Text statusText;
        private bool configured;

        public bool IsOpen => overlay != null && overlay.activeSelf;

        public void Configure(
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            GeneralStorePanelController newManagementController,
            ManagementPanelView newView)
        {
            sharedBusinessRuntime = newSharedBusinessRuntime;
            managementController = newManagementController;
            view = newView;
            configured = sharedBusinessRuntime != null && view != null;
            if (configured)
            {
                EnsureControls();
            }
        }

        private void EnsureControls()
        {
            if (view == null || view.PanelRoot == null)
            {
                return;
            }

            if (createButton == null)
            {
                createButton = CreateButton(view.PanelRoot, "Management_CreateBusinessButton", "CREATE BUSINESS");
                RectTransform rect = createButton.transform as RectTransform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-24f, -20f);
                rect.sizeDelta = new Vector2(190f, 38f);
                createButton.onClick.AddListener(OpenForm);
            }

            if (overlay == null)
            {
                BuildOverlay(view.PanelRoot);
            }
        }

        private void BuildOverlay(RectTransform parent)
        {
            overlay = new GameObject("Management_CreateBusinessOverlay", typeof(RectTransform), typeof(Image));
            RectTransform overlayRect = overlay.transform as RectTransform;
            overlayRect.SetParent(parent, false);
            overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            overlayRect.pivot = new Vector2(0.5f, 0.5f);
            overlayRect.anchoredPosition = Vector2.zero;
            overlayRect.sizeDelta = new Vector2(560f, 330f);
            overlay.GetComponent<Image>().color = new Color(0.075f, 0.08f, 0.075f, 0.98f);

            VerticalLayoutGroup layout = overlay.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;

            CreateText(overlay.transform, "Create a business", 22f, FontStyles.Bold);
            CreateText(overlay.transform,
                "Formation creates the legal/economic business first. It does not buy land or make the business operational.",
                14f, FontStyles.Normal);

            businessTypes = (BusinessType[])Enum.GetValues(typeof(BusinessType));
            selectedTypeIndex = Math.Max(0, Array.IndexOf(businessTypes, BusinessType.GeneralStore));
            typeButton = CreateButton(overlay.transform, "BusinessTypeButton", string.Empty);
            typeButton.onClick.AddListener(CycleBusinessType);
            UpdateTypeButton();

            nameInput = CreateInput(overlay.transform, "Business name (optional)");
            statusText = CreateText(overlay.transform, string.Empty, 13f, FontStyles.Normal);

            HorizontalLayoutGroup buttons = new GameObject("CreateBusiness_Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            buttons.transform.SetParent(overlay.transform, false);
            buttons.spacing = 10f;
            buttons.childControlWidth = true;
            buttons.childForceExpandWidth = true;
            Button confirm = CreateButton(buttons.transform, "CreateBusiness_Confirm", "FORM BUSINESS");
            Button cancel = CreateButton(buttons.transform, "CreateBusiness_Cancel", "CANCEL");
            confirm.onClick.AddListener(Submit);
            cancel.onClick.AddListener(CloseForm);
            overlay.SetActive(false);
        }

        private void OpenForm()
        {
            if (!configured)
            {
                return;
            }

            statusText.text = "Choose a business type. Premises, funding, labor, and commerce come afterward.";
            nameInput.text = string.Empty;
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
        }

        private void CloseForm()
        {
            if (overlay != null)
            {
                overlay.SetActive(false);
            }
        }

        private void Submit()
        {
            if (sharedBusinessRuntime == null || typeButton == null)
            {
                return;
            }

            if (selectedTypeIndex < 0 || selectedTypeIndex >= businessTypes.Length)
            {
                statusText.text = "Choose a supported business type.";
                return;
            }

            if (!sharedBusinessRuntime.TryCreatePlayerBusiness(
                    businessTypes[selectedTypeIndex],
                    nameInput != null ? nameInput.text : string.Empty,
                    out BusinessInstanceState business,
                    out string message))
            {
                statusText.text = message;
                return;
            }

            statusText.text = $"{business.RuntimeDisplayName} formed. Open it in Businesses to establish the operation.";
            CloseForm();
            managementController?.Refresh();
        }

        private void CycleBusinessType()
        {
            if (businessTypes == null || businessTypes.Length == 0)
            {
                return;
            }

            selectedTypeIndex = (selectedTypeIndex + 1) % businessTypes.Length;
            UpdateTypeButton();
        }

        private void UpdateTypeButton()
        {
            if (typeButton == null || businessTypes == null || businessTypes.Length == 0)
            {
                return;
            }

            TMP_Text label = typeButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = $"BUSINESS TYPE: {BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessTypes[selectedTypeIndex])} (click to change)";
            }
        }

        private static TMP_Text CreateText(Transform parent, string text, float size, FontStyles style)
        {
            GameObject go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TMP_Text label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = new Color(0.9f, 0.88f, 0.78f, 1f);
            label.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = size + 12f;
            element.preferredHeight = size + 18f;
            return label;
        }

        private static TMP_InputField CreateInput(Transform parent, string placeholder)
        {
            GameObject go = new GameObject("BusinessNameInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.14f, 1f);
            TMP_InputField input = go.GetComponent<TMP_InputField>();
            TextMeshProUGUI text = CreateText(go.transform, string.Empty, 16f, FontStyles.Normal) as TextMeshProUGUI;
            input.textComponent = text;
            input.placeholder = CreateText(go.transform, placeholder, 14f, FontStyles.Italic);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 38f;
            element.preferredHeight = 38f;
            return input;
        }

        private static Button CreateButton(Transform parent, string objectName, string label)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.color = new Color(0.32f, 0.25f, 0.13f, 1f);
            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            CreateText(go.transform, label, 14f, FontStyles.Bold);
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 38f;
            element.preferredHeight = 38f;
            return button;
        }
    }
}
