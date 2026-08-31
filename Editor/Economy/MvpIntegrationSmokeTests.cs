using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace LandLedgers.Editor.Economy
{
    public sealed class MvpIntegrationSmokeTests
    {
        [Test]
        public void MainSceneManagementPanelBindsRuntimeControlsAndTabs()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Main Scene.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid(), "Main Scene should open for the Management smoke test.");

            ManagementPanelView view = UnityEngine.Object.FindAnyObjectByType<ManagementPanelView>(FindObjectsInactive.Include);
            Assert.NotNull(view, "Main Scene should contain a ManagementPanelView.");
            Assert.IsTrue(view.BindFromChildren(), "ManagementPanelView should bind authored scene references.");

            view.EnsureBusinessCashTransferControls(true);
            view.EnsureBankLoanControls(true);
            view.EnsureCloseButton(true);

            Assert.IsTrue(view.HasRequiredReferences(), "ManagementPanelView should keep all required authored references after dynamic controls are created.");
            Assert.NotNull(view.CashTransferDepositOneButton);
            Assert.NotNull(view.CashTransferWithdrawOneButton);
            Assert.NotNull(view.CashTransferAutoToggleButton);
            AssertBusinessCashTransferPropertyScrollPlacement(view);
            AssertBusinessCashTransferPolish(view);
            AssertBusinessCashTransferInputsAreNumeric(view);
            AssertPropertyDetailSectionsAutoSizeAndCollapse(view);
            AssertManagementCloseButtonExists(view);
            Assert.NotNull(view.LoanSubmitButton);
            Assert.NotNull(view.AcquisitionFocusButton);
            Assert.NotNull(view.GovernmentFocusButton);
            Assert.NotNull(view.ResourcesTabButton);
            Assert.NotNull(view.ResourcesContentRoot);
            Assert.NotNull(view.ResourcesSummaryText);
            Assert.NotNull(view.ResourcesDistrictListText);
            Assert.NotNull(view.ResourcesDetailText);

            AssertSecondPassSectionBindings(view);
            AssertCashTransferButtonPathReportsRejection(view);

            AssertTabSwitch(view, ManagementPanelTab.Properties, view.PropertiesContentRoot);
            AssertTabSwitch(view, ManagementPanelTab.Finances, view.FinancesContentRoot);
            AssertTabSwitch(view, ManagementPanelTab.Acquisitions, view.AcquisitionsContentRoot);
            AssertTabSwitch(view, ManagementPanelTab.Government, view.GovernmentContentRoot);
            AssertTabSwitch(view, ManagementPanelTab.Resources, view.ResourcesContentRoot);
        }

        private static void AssertBusinessCashTransferPropertyScrollPlacement(ManagementPanelView view)
        {
            RectTransform root = view.CashTransferRoot;
            Assert.NotNull(root, "Business cash transfer root should be created or rebound.");

            Assert.AreSame(view.PropertyDetailBodyContent, root.parent, "Business cash transfer controls should live in the property detail scroll content.");
            Assert.IsTrue(IsDescendantOf(root, view.PropertyDetailBodyContent), "Business cash transfer controls should scroll with property detail content.");
            Transform storeSections = view.PropertyDetailBodyContent != null ? view.PropertyDetailBodyContent.Find("PropertyDetail_StoreSections") : null;
            Assert.IsFalse(IsDescendantOf(root, storeSections), "Business cash transfer controls should not be embedded in the store section stack.");

            Assert.AreEqual(new Vector2(0f, 1f), root.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), root.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 1f), root.pivot);
            Assert.AreEqual(Vector2.zero, root.sizeDelta);
            Component layout = root.GetComponent("LayoutElement");
            Assert.NotNull(layout);
            Assert.IsFalse((bool)layout.GetType().GetProperty("ignoreLayout").GetValue(layout), "Cash transfer card should participate in the property detail layout.");
            Assert.GreaterOrEqual((float)layout.GetType().GetProperty("preferredHeight").GetValue(layout), 244f);
        }

        private static void AssertBusinessCashTransferPolish(ManagementPanelView view)
        {
            Assert.NotNull(view.CashTransferTitleText);
            Assert.NotNull(view.CashTransferHelpText);
            Assert.NotNull(view.CashTransferThresholdText);
            Assert.NotNull(view.CashTransferDepositInput);
            Assert.NotNull(view.CashTransferWithdrawInput);

            StringAssert.Contains("Refill Below", view.CashTransferLowerThresholdInput.placeholder.GetComponent<TMP_Text>().text);
            StringAssert.Contains("Sweep Above", view.CashTransferUpperThresholdInput.placeholder.GetComponent<TMP_Text>().text);
            Assert.AreEqual("Bitter SemiBold SDF", view.CashTransferTitleText.font.name);
            Assert.AreEqual("Bitter SemiBold SDF", view.CashTransferDepositExactButton.GetComponentInChildren<TMP_Text>(true).font.name);
            AssertInputTextBoundsMatch(view.CashTransferDepositInput);
            AssertInputTextBoundsMatch(view.CashTransferWithdrawInput);
        }

        private static void AssertBusinessCashTransferInputsAreNumeric(ManagementPanelView view)
        {
            Assert.AreEqual(TMP_InputField.ContentType.DecimalNumber, view.CashTransferDepositInput.contentType);
            Assert.AreEqual(TMP_InputField.ContentType.DecimalNumber, view.CashTransferWithdrawInput.contentType);
            Assert.AreEqual(TMP_InputField.ContentType.DecimalNumber, view.CashTransferLowerThresholdInput.contentType);
            Assert.AreEqual(TMP_InputField.ContentType.DecimalNumber, view.CashTransferUpperThresholdInput.contentType);
        }

        private static void AssertManagementCloseButtonExists(ManagementPanelView view)
        {
            Assert.NotNull(view.CloseButton, "Management tab row should expose a Close button.");
            Assert.IsTrue(view.CloseButton.gameObject.activeSelf, "Close button should be visible on the tab row.");
            Assert.AreEqual("Close", view.CloseButton.GetComponentInChildren<TMP_Text>(true).text);
        }

        private static void AssertInputTextBoundsMatch(TMP_InputField input)
        {
            Assert.NotNull(input);
            Assert.NotNull(input.textComponent);
            Assert.NotNull(input.placeholder);
            RectTransform textRect = input.textComponent.rectTransform;
            RectTransform placeholderRect = input.placeholder.rectTransform;
            Assert.AreEqual(new Vector2(0f, 0f), textRect.anchorMin);
            Assert.AreEqual(new Vector2(1f, 1f), textRect.anchorMax);
            Assert.AreEqual(textRect.offsetMin, placeholderRect.offsetMin);
            Assert.AreEqual(textRect.offsetMax, placeholderRect.offsetMax);
            Assert.AreEqual(new Vector2(10f, 4f), textRect.offsetMin);
            Assert.AreEqual(new Vector2(-10f, -4f), textRect.offsetMax);
        }

        private static void AssertPropertyDetailSectionsAutoSizeAndCollapse(ManagementPanelView view)
        {
            Assert.NotNull(view.StoreOverviewText);
            Assert.NotNull(view.StoreFinanceText);
            Assert.NotNull(view.StoreStaffingText);
            Assert.NotNull(view.StoreStockText);

            view.RentingOverviewText.text = string.Empty;
            view.StoreOverviewText.text = "Owned Land\nSelected start: Blacksmith\nSelected shell: Deep shell\nCurrent read: planning text should grow without clipping.\nNext move: keep reading the full body.";
            view.StoreFinanceText.text = string.Empty;
            view.StoreStaffingText.text = "Development: Business\nBusiness 1/3: Blacksmith\nShell 1/2: Main Street shop shell\nBlocked: Inputs are thin but readable.\nNext move: resolve materials and crew.";
            view.StoreStockText.text = "Construction Inputs\nLumber: available\nNails: thin\nBuilder lane: waiting\nThis section should grow with the text instead of clipping or leaving a giant fixed hole.";
            view.RefreshPropertyDetailLayout();

            RectTransform upperRow = view.PropertyDetailBodyContent.Find("PropertyDetail_StoreSections/PropertyDetail_StoreUpperRow") as RectTransform;
            Assert.NotNull(upperRow);
            Assert.IsTrue(view.StoreOverviewText.gameObject.activeSelf);
            Assert.IsFalse(view.StoreFinanceText.gameObject.activeSelf, "Empty finance text should collapse instead of reserving space.");

            LayoutElement upperLayout = upperRow.GetComponent<LayoutElement>();
            Assert.NotNull(upperLayout);
            Assert.AreEqual(-1f, upperLayout.preferredHeight);

            LayoutElement overviewLayout = view.StoreOverviewText.GetComponent<LayoutElement>();
            LayoutElement staffingLayout = view.StoreStaffingText.GetComponent<LayoutElement>();
            LayoutElement stockLayout = view.StoreStockText.GetComponent<LayoutElement>();
            Assert.NotNull(overviewLayout);
            Assert.NotNull(staffingLayout);
            Assert.NotNull(stockLayout);
            Assert.AreEqual(-1f, overviewLayout.preferredHeight);
            Assert.AreEqual(-1f, staffingLayout.preferredHeight);
            Assert.AreEqual(-1f, stockLayout.preferredHeight);
            Assert.GreaterOrEqual(overviewLayout.minHeight, 100f);
            Assert.GreaterOrEqual(staffingLayout.minHeight, 80f);
            Assert.GreaterOrEqual(stockLayout.minHeight, 100f);
        }

        private static void AssertCashTransferButtonPathReportsRejection(ManagementPanelView view)
        {
            List<UnityEngine.Object> cleanup = new();
            try
            {
                GameObject portfolioObject = new("MVP Management Cash Transfer Smoke Portfolio");
                cleanup.Add(portfolioObject);
                PlayerPortfolioManager portfolio = portfolioObject.AddComponent<PlayerPortfolioManager>();
                portfolio.LoadFromSaveDto(new PlayerPortfolioSaveDto
                {
                    initialized = true,
                    ownerCashCents = 0
                });

                BusinessInstanceState business = BusinessInstanceState.Create(
                    "mvp_management_cash_transfer_smoke",
                    LoadProfile(BusinessType.GeneralStore),
                    0,
                    BusinessOwnerIdentity.Player());
                int ownerCashBefore = portfolio.OwnerCashCents;
                bool transferResult = true;

                view.CashTransferDepositOneButton.onClick.RemoveAllListeners();
                view.CashTransferDepositOneButton.onClick.AddListener(() =>
                {
                    transferResult = portfolio.TryTransferOwnerBusinessCash(
                        business,
                        100,
                        2000,
                        out string message);
                    view.CashTransferStatusText.text = message;
                });

                view.CashTransferDepositOneButton.onClick.Invoke();

                Assert.IsFalse(transferResult);
                Assert.AreEqual(ownerCashBefore, portfolio.OwnerCashCents);
                StringAssert.Contains("owner cash", view.CashTransferStatusText.text);
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                    }
                }
            }
        }

        private static void AssertSecondPassSectionBindings(ManagementPanelView view)
        {
            RectTransform acquisitionQuietSection = ReadProperty<RectTransform>(view, "AcquisitionOffMarketSectionRoot");
            RectTransform civicImplicationsSection = ReadProperty<RectTransform>(view, "GovernmentOwnerImplicationsSectionRoot");
            TMP_Text civicImplicationsBody = ReadProperty<TMP_Text>(view, "GovernmentOwnerImplicationsText");

            Assert.NotNull(acquisitionQuietSection, "Acquisitions should bind a dedicated quiet/off-market section root.");
            Assert.NotNull(civicImplicationsSection, "Civic should bind a dedicated owner-implications section root.");
            Assert.NotNull(civicImplicationsBody, "Civic should bind a dedicated owner-implications body text.");
            Assert.AreSame(civicImplicationsSection, civicImplicationsBody.transform.parent, "Owner implications body should live inside its dedicated section.");

            Assert.IsTrue(IsDescendantOf(view.BankLoanRoot, view.FinancesContentRoot), "Bank loan desk should remain inside the finances surface.");
            LayoutElement bankLoanLayout = view.BankLoanRoot != null ? view.BankLoanRoot.GetComponent<LayoutElement>() : null;
            Assert.NotNull(bankLoanLayout, "Bank loan desk should keep a layout element for fit checks.");
            Assert.GreaterOrEqual(bankLoanLayout.preferredHeight, 240f);
            Assert.LessOrEqual(bankLoanLayout.preferredHeight, 340f);

            Assert.AreNotSame(view.AcquisitionOffMarketText.transform.parent, view.AcquisitionForSaleText.transform.parent, "Quiet leads should no longer reuse the active-leads section.");
            Assert.AreNotSame(view.AcquisitionOffMarketText.transform.parent, view.AcquisitionHistoryText.transform.parent, "Quiet leads should no longer reuse the process section.");
        }

        private static void AssertTabSwitch(ManagementPanelView view, ManagementPanelTab tab, RectTransform expectedActiveRoot)
        {
            view.ShowTab(tab);

            Assert.IsTrue(expectedActiveRoot.gameObject.activeSelf, $"{tab} content should be active after selecting the tab.");
            Assert.AreEqual(tab == ManagementPanelTab.Properties, view.PropertiesContentRoot.gameObject.activeSelf);
            Assert.AreEqual(tab == ManagementPanelTab.Finances, view.FinancesContentRoot.gameObject.activeSelf);
            Assert.AreEqual(tab == ManagementPanelTab.Acquisitions, view.AcquisitionsContentRoot.gameObject.activeSelf);
            Assert.AreEqual(tab == ManagementPanelTab.Government, view.GovernmentContentRoot.gameObject.activeSelf);
            Assert.AreEqual(tab == ManagementPanelTab.Resources, view.ResourcesContentRoot.gameObject.activeSelf);
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Business.BusinessType == businessType)
                {
                    return profiles[i];
                }
            }

            Assert.Fail($"{businessType} profile should be present in Resources.");
            return null;
        }

        private static bool IsDescendantOf(Transform candidate, Transform possibleAncestor)
        {
            if (candidate == null || possibleAncestor == null)
            {
                return false;
            }

            Transform current = candidate.parent;
            while (current != null)
            {
                if (current == possibleAncestor)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static T ReadProperty<T>(object target, string propertyName) where T : class
        {
            System.Reflection.PropertyInfo property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            Assert.NotNull(property, $"{propertyName} should exist for the management second pass.");
            return property.GetValue(target) as T;
        }
    }
}
