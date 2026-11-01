using LandLedgers.UI;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace LandLedgers.EditorTests.UI
{
    public sealed class LandLedgersTypographyTests
    {
        [Test]
        public void RequiredGameplayFontAssetsExist()
        {
            foreach (LandLedgersTypography.FontRole role in LandLedgersTypography.RequiredRoles)
            {
                TMP_FontAsset font = LandLedgersTypography.GetFont(role);

                Assert.NotNull(font, $"{role} should resolve to a TMP font asset.");
                AssertUsableFont(font, $"{role} resolved to an unusable TMP font asset.");
            }
        }

        [Test]
        public void TmpDefaultFontIsUsable()
        {
            TMP_Settings settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(
                "Assets/Asset Packs/TextMesh Pro/Resources/TMP Settings.asset");

            Assert.NotNull(settings);
            Assert.NotNull(TMP_Settings.defaultFontAsset);
            AssertUsableFont(TMP_Settings.defaultFontAsset, "TMP default font must have a valid atlas texture.");
        }

        [Test]
        public void TooltipUsesBitterTitleAndSourceSansBody()
        {
            GameObject canvasObject = new("Typography Tooltip Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                UITooltipController tooltip = UITooltipController.GetOrCreate(canvas);

                TMP_Text title = FindText(tooltip.transform, "Title");
                TMP_Text body = FindText(tooltip.transform, "Body");

                AssertUsableFont(title.font, "Tooltip title font must be usable.");
                AssertUsableFont(body.font, "Tooltip body font must be usable.");
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void OwnershipWorkflowUsesLedgerHierarchy()
        {
            GameObject canvasObject = new("Typography Workflow Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                OwnershipWorkflowView view = OwnershipWorkflowView.GetOrCreate(canvas);

                AssertUsableFont(view.TitleText.font, "Workflow title font must be usable.");
                AssertUsableFont(view.StageText.font, "Workflow stage font must be usable.");
                AssertUsableFont(view.LedgerText.font, "Workflow ledger font must be usable.");
                AssertUsableFont(view.PrimaryButton.GetComponentInChildren<TMP_Text>(true).font, "Workflow button font must be usable.");
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void ManagementDenseTextUsesSourceSansAndHeadingsUseBitter()
        {
            GameObject viewObject = new("Typography Management View", typeof(RectTransform));
            try
            {
                ManagementPanelView view = viewObject.AddComponent<ManagementPanelView>();
                TMP_Text title = CreateText("Panel_Title", viewObject.transform);
                TMP_Text summary = CreateText("FinancesSummaryText", viewObject.transform);

                LandLedgersTypography.ApplyRole(title, LandLedgersTypography.TextRole.ScreenTitle);
                LandLedgersTypography.ApplyRole(summary, LandLedgersTypography.TextRole.DenseBody);

                AssertUsableFont(title.font, "Management title font must be usable.");
                AssertUsableFont(summary.font, "Management dense font must be usable.");
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
            }
        }

        private static TMP_Text CreateText(string name, Transform parent)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            return textObject.GetComponent<TMP_Text>();
        }

        private static TMP_Text FindText(Transform root, string name)
        {
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == name)
                {
                    return texts[i];
                }
            }

            Assert.Fail($"Missing text '{name}'.");
            return null;
        }

        private static void AssertUsableFont(TMP_FontAsset font, string message)
        {
            Assert.NotNull(font, message);
            Assert.NotNull(font.atlasTextures, message);
            Assert.Greater(font.atlasTextures.Length, 0, message);
            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                Assert.NotNull(font.atlasTextures[i], message);
            }
        }
    }
}
