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
        private GeneralStoreRuntimeManager storeRuntime;
        private GeneralStorePanelController managementController;
        private ManagementPanelView view;
        private Button createButton;
        private GameObject overlay;
        private TMP_Dropdown typeDropdown;
        private TMP_Dropdown templateDropdown;
        private Button clearButton;
        private BusinessType[] businessTypes = Array.Empty<BusinessType>();
        private int selectedTypeIndex;
        private TMP_InputField nameInput;
        private TMP_Text statusText;
        private bool configured;
        private string draftName = string.Empty;

        public bool IsOpen => overlay != null && overlay.activeSelf;

        public void Configure(
            SharedBusinessRuntimeManager newSharedBusinessRuntime,
            GeneralStorePanelController newManagementController,
            ManagementPanelView newView)
        {
            sharedBusinessRuntime = newSharedBusinessRuntime;
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            managementController = newManagementController;
            view = newView;
            configured = sharedBusinessRuntime != null && view != null;
            if (configured)
            {
                EnsureControls();
                SetBusinessesTabActive(false);
            }
        }

        public void SetBusinessesTabActive(bool active)
        {
            if (createButton != null)
            {
                createButton.gameObject.SetActive(active);
            }

            if (!active && overlay != null)
            {
                CaptureDraft();
                overlay.SetActive(false);
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
                rect.anchoredPosition = new Vector2(-18f, -16f);
                rect.sizeDelta = new Vector2(164f, 32f);
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
            overlayRect.sizeDelta = new Vector2(460f, 330f);
            overlay.GetComponent<Image>().color = new Color(0.075f, 0.08f, 0.075f, 0.98f);

            VerticalLayoutGroup layout = overlay.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 7f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;

            CreateText(overlay.transform, "Create a business", 19f, FontStyles.Bold);
            CreateText(overlay.transform,
                "Formation creates the legal/economic business first. It does not buy land or make the business operational.",
                12f, FontStyles.Normal);

            // Formation is intentionally classless. Legacy BusinessType values remain
            // descriptive/migration metadata, while this player-facing flow creates a
            // generic entity first and configures its physical activities afterward.
            businessTypes = new[] { BusinessType.Generic };
            selectedTypeIndex = 0;
            CreateText(overlay.transform,
                "Trade class: none. Configure premises, equipment, people, inventory, and policies after formation.",
                12f, FontStyles.Italic);
            typeDropdown = CreateDropdown(overlay.transform, "BusinessTypeDropdown");
            typeDropdown.onValueChanged.AddListener(SelectBusinessType);
            typeDropdown.ClearOptions();
            var options = new List<TMP_Dropdown.OptionData>(businessTypes.Length);
            for (int i = 0; i < businessTypes.Length; i++)
            {
                options.Add(new TMP_Dropdown.OptionData(BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessTypes[i])));
            }
            typeDropdown.AddOptions(options);
            typeDropdown.value = selectedTypeIndex;
            typeDropdown.RefreshShownValue();
            typeDropdown.gameObject.SetActive(false);

            CreateText(overlay.transform, "Setup template (optional)", 12f, FontStyles.Normal);
            templateDropdown = CreateDropdown(overlay.transform, "BusinessSetupTemplateDropdown");
            templateDropdown.AddOptions(new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("Blank Business"),
                new TMP_Dropdown.OptionData("Merchant assortment"),
                new TMP_Dropdown.OptionData("Bakery policy"),
                new TMP_Dropdown.OptionData("Prairie Fork smithing"),
            });
            templateDropdown.value = 0;
            templateDropdown.RefreshShownValue();

            nameInput = CreateInput(overlay.transform, "Business name (optional)");
            statusText = CreateText(overlay.transform, string.Empty, 13f, FontStyles.Normal);

            HorizontalLayoutGroup buttons = new GameObject("CreateBusiness_Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<HorizontalLayoutGroup>();
            buttons.transform.SetParent(overlay.transform, false);
            buttons.spacing = 10f;
            buttons.childControlWidth = true;
            buttons.childForceExpandWidth = true;
            Button confirm = CreateButton(buttons.transform, "CreateBusiness_Confirm", "FORM BUSINESS");
            Button cancel = CreateButton(buttons.transform, "CreateBusiness_Cancel", "CANCEL");
            clearButton = CreateButton(buttons.transform, "CreateBusiness_Clear", "CLEAR");
            confirm.onClick.AddListener(Submit);
            cancel.onClick.AddListener(CloseForm);
            clearButton.onClick.AddListener(ClearDraft);
            overlay.SetActive(false);
        }

        private void OpenForm()
        {
            if (!configured)
            {
                return;
            }

            statusText.text = "Formation creates ownership first. Premises, funding, labor, and commerce come afterward.";
            if (nameInput != null)
            {
                nameInput.text = draftName;
            }
            typeDropdown.value = Mathf.Clamp(selectedTypeIndex, 0, Mathf.Max(0, businessTypes.Length - 1));
            typeDropdown.RefreshShownValue();
            overlay.SetActive(true);
            overlay.transform.SetAsLastSibling();
        }

        private void CloseForm()
        {
            CaptureDraft();
            if (overlay != null)
            {
                overlay.SetActive(false);
            }
        }

        private void Submit()
        {
            if (sharedBusinessRuntime == null || typeDropdown == null)
            {
                return;
            }

            if (selectedTypeIndex < 0 || selectedTypeIndex >= businessTypes.Length)
            {
                statusText.text = "Choose a supported business type.";
                return;
            }

            if (!sharedBusinessRuntime.TryCreatePlayerBusiness(
                    businessTypes[typeDropdown.value],
                    nameInput != null ? nameInput.text : string.Empty,
                    out BusinessInstanceState business,
                    out string message))
            {
                statusText.text = message;
                return;
            }

            ApplySetupTemplate(business, templateDropdown != null ? templateDropdown.value : 0);

            statusText.text = $"{business.RuntimeDisplayName} formed. Configure the operation in Businesses.";
            if ((business.BusinessType == BusinessType.GeneralStore || business.BusinessType == BusinessType.Generic)
                && storeRuntime != null
                && storeRuntime.TryBindFormedPlayerBusiness(business, out string bindingMessage))
            {
                statusText.text = bindingMessage;
            }
            CloseForm();
            managementController?.Refresh();
        }

        private void SelectBusinessType(int index)
        {
            selectedTypeIndex = Mathf.Clamp(index, 0, Mathf.Max(0, businessTypes.Length - 1));
        }

        private void ClearDraft()
        {
            draftName = string.Empty;
            selectedTypeIndex = 0;
            if (nameInput != null)
            {
                nameInput.text = string.Empty;
            }
            if (typeDropdown != null && businessTypes.Length > 0)
            {
                typeDropdown.value = selectedTypeIndex;
                typeDropdown.RefreshShownValue();
            }
            if (templateDropdown != null)
            {
                templateDropdown.value = 0;
                templateDropdown.RefreshShownValue();
            }
            if (statusText != null)
            {
                statusText.text = "Draft cleared.";
            }
        }

        private void CaptureDraft()
        {
            if (nameInput != null)
            {
                draftName = nameInput.text ?? string.Empty;
            }
        }

        private static void ApplySetupTemplate(BusinessInstanceState business, int templateIndex)
        {
            if (business == null || business.BusinessType != BusinessType.Generic) return;
            switch (templateIndex)
            {
                case 1:
                    GenericBusinessVerticalService.AddMerchantProductLine(business, "flour", 20f, 40f, 8f, 35);
                    GenericBusinessVerticalService.AddMerchantProductLine(business, "coffee", 10f, 20f, 4f, 85);
                    break;
                case 2:
                    GenericBusinessVerticalService.ConfigureBakery(business);
                    break;
                case 3:
                    GenericBusinessVerticalService.ConfigurePrairieForkSmithy(business);
                    break;
            }
        }

        private static TMP_Dropdown CreateDropdown(Transform parent, string objectName)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.14f, 0.15f, 0.14f, 1f);
            TMP_Dropdown dropdown = go.GetComponent<TMP_Dropdown>();
            TextMeshProUGUI caption = CreateText(go.transform, string.Empty, 14f, FontStyles.Normal) as TextMeshProUGUI;
            caption.alignment = TextAlignmentOptions.MidlineLeft;
            dropdown.captionText = caption;
            dropdown.template = CreateDropdownTemplate(go.transform);
            dropdown.itemText = dropdown.template.Find("Viewport/Content/Item/Item Label")?.GetComponent<TMP_Text>();
            LayoutElement element = go.AddComponent<LayoutElement>();
            element.minHeight = 34f;
            element.preferredHeight = 34f;
            return dropdown;
        }

        private static RectTransform CreateDropdownTemplate(Transform parent)
        {
            GameObject template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(parent, false);
            RectTransform rect = template.transform as RectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, 2f);
            rect.sizeDelta = new Vector2(0f, 150f);
            template.GetComponent<Image>().color = new Color(0.10f, 0.11f, 0.10f, 1f);
            ScrollRect scroll = template.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            RectTransform viewportRect = viewport.transform as RectTransform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewport.GetComponent<Image>().color = Color.clear;
            viewport.GetComponent<Mask>().showMaskGraphic = false;
            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ToggleGroup));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.transform as RectTransform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            GameObject item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            RectTransform itemRect = item.transform as RectTransform;
            itemRect.anchorMin = new Vector2(0f, 1f);
            itemRect.anchorMax = new Vector2(1f, 1f);
            itemRect.sizeDelta = new Vector2(0f, 30f);
            Toggle toggle = item.GetComponent<Toggle>();
            GameObject itemLabel = new GameObject("Item Label", typeof(RectTransform));
            itemLabel.transform.SetParent(item.transform, false);
            RectTransform labelRect = itemLabel.transform as RectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);
            toggle.targetGraphic = item.AddComponent<Image>();
            toggle.graphic = itemLabel.AddComponent<TextMeshProUGUI>();
            ((TMP_Text)toggle.graphic).fontSize = 13f;
            ((TMP_Text)toggle.graphic).color = Color.white;
            ((TMP_Text)toggle.graphic).text = "Option";
            template.gameObject.SetActive(false);
            return rect;
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
