using LandLedgers.FirstLedger;
using LandLedgers.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.UI
{
    public sealed class FirstSessionGuidanceTests
    {
        [Test]
        public void ProgressionFollowsEarlyCoreLoopRuntimeFacts()
        {
            FirstSessionGuidanceProgress progress = new();
            FirstSessionGuidanceFacts facts = new()
            {
                CurrentTab = ManagementPanelTab.Properties
            };

            Assert.AreEqual(FirstSessionObjectiveStep.OpenManagement, progress.Evaluate(facts).Step);

            facts.ManagementOpen = true;
            Assert.AreEqual(FirstSessionObjectiveStep.InspectGeneralStore, progress.Evaluate(facts).Step);

            facts.GeneralStoreSelected = true;
            Assert.AreEqual(FirstSessionObjectiveStep.HireFirstWorker, progress.Evaluate(facts).Step);

            facts.FirstSalesObserved = true;
            Assert.AreEqual(FirstSessionObjectiveStep.HireFirstWorker, progress.Evaluate(facts).Step);

            facts.FirstSalesObserved = false;
            facts.GeneralStoreStaffed = true;
            Assert.AreEqual(FirstSessionObjectiveStep.ObserveFirstSales, progress.Evaluate(facts).Step);

            facts.FirstSalesObserved = true;
            Assert.AreEqual(FirstSessionObjectiveStep.ReviewMoneyModel, progress.Evaluate(facts).Step);

            facts.CurrentTab = ManagementPanelTab.Finances;
            Assert.AreEqual(FirstSessionObjectiveStep.OpenAcquisitions, progress.Evaluate(facts).Step);

            facts.CurrentTab = ManagementPanelTab.Acquisitions;
            Assert.AreEqual(FirstSessionObjectiveStep.CompleteFirstLandPurchase, progress.Evaluate(facts).Step);

            facts.OwnedLandCount = 1;
            Assert.AreEqual(FirstSessionObjectiveStep.ReturnToProperties, progress.Evaluate(facts).Step);

            facts.CurrentTab = ManagementPanelTab.Properties;
            Assert.AreEqual(FirstSessionObjectiveStep.BuildFirstBusinessShell, progress.Evaluate(facts).Step);

            facts.StartableOwnedShellExists = true;
            Assert.AreEqual(FirstSessionObjectiveStep.StartFirstBusiness, progress.Evaluate(facts).Step);

            facts.PlayerOwnedExpansionBusinessExists = true;
            Assert.AreEqual(FirstSessionObjectiveStep.Complete, progress.Evaluate(facts).Step);
        }

        [Test]
        public void MoneyModelCopyNamesOwnerCashAndBusinessCash()
        {
            StringAssert.Contains("Owner Cash", FirstSessionGuidanceText.MoneyModelSummary);
            StringAssert.Contains("Store Cash", FirstSessionGuidanceText.MoneyModelSummary);
            StringAssert.Contains("Profits", FirstSessionGuidanceText.MoneyModelSummary);
        }

        [Test]
        public void ConstructionBlockerCopyGivesActionableNextSteps()
        {
            string message = FirstSessionGuidanceText.BuildConstructionBlockerNextStep(
                "cash short $120.00; Lumber short 5; Nails / simple hardware short 2; Labor short 1");

            StringAssert.Contains("Owner Cash", message);
            StringAssert.Contains("sawmill/lumber yard", message);
            StringAssert.Contains("blacksmith hardware", message);
            StringAssert.Contains("labor", message);
        }

        [Test]
        public void HudGuidanceStripDisplaysAndClears()
        {
            GameObject hudObject = new("First Session HUD Test", typeof(RectTransform));
            try
            {
                LandLedgersHUDController hud = hudObject.AddComponent<LandLedgersHUDController>();

                hud.SetFirstSessionGuidance(
                    "Objective: Open Management",
                    "Why: Management holds the core loop.",
                    "Action: Press C.",
                    "Next: keep going.");

                Assert.IsTrue(hud.IsFirstSessionGuidanceVisible);
                StringAssert.Contains("Open Management", hud.CurrentFirstSessionObjectiveText);
                StringAssert.Contains("Press C", hud.CurrentFirstSessionActionText);
                StringAssert.Contains("keep going", hud.CurrentFirstSessionBlockerText);
                Assert.AreEqual(new Vector2(20f, -138f), hud.CurrentFirstSessionGuidanceAnchoredPosition);
                Assert.AreEqual(new Vector2(620f, 164f), hud.CurrentFirstSessionGuidanceSizeDelta);

                RectTransform strip = FindChild<RectTransform>(hudObject.transform, "HUD_FirstSessionObjectiveStrip");
                RectTransform content = FindChild<RectTransform>(strip, "HUD_FirstSessionObjectiveContent");
                RectTransform blocker = FindChild<RectTransform>(strip, "HUD_FirstSessionBlockerText");
                Assert.IsNotNull(content);
                Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>());
                Assert.IsNull(strip.GetComponent<HorizontalLayoutGroup>());
                Assert.AreEqual(content, FindChild<RectTransform>(strip, "HUD_FirstSessionObjectiveText").parent);
                Assert.AreEqual(content, FindChild<RectTransform>(strip, "HUD_FirstSessionWhyText").parent);
                Assert.AreEqual(content, FindChild<RectTransform>(strip, "HUD_FirstSessionActionText").parent);
                Assert.Less(
                    FindChild<RectTransform>(strip, "HUD_FirstSessionObjectiveText").GetSiblingIndex(),
                    FindChild<RectTransform>(strip, "HUD_FirstSessionWhyText").GetSiblingIndex());
                Assert.Less(
                    FindChild<RectTransform>(strip, "HUD_FirstSessionWhyText").GetSiblingIndex(),
                    FindChild<RectTransform>(strip, "HUD_FirstSessionActionText").GetSiblingIndex());
                Assert.AreEqual(strip, blocker.parent);
                Assert.AreEqual(new Vector2(0f, 0f), blocker.anchorMin);
                Assert.AreEqual(new Vector2(1f, 0f), blocker.anchorMax);
                Assert.AreEqual(new Vector2(0.5f, 0f), blocker.pivot);

                hud.ClearFirstSessionGuidance();

                Assert.IsFalse(hud.IsFirstSessionGuidanceVisible);
                Assert.IsEmpty(hud.CurrentFirstSessionObjectiveText);
                Assert.IsFalse(blocker.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void HudNoticeStripAndGuidanceOccupySeparateTopLeftRegions()
        {
            GameObject hudObject = new("HUD Layout Test", typeof(RectTransform));
            GameObject alertObject = new("HUD_AlertStrip", typeof(RectTransform));
            try
            {
                alertObject.transform.SetParent(hudObject.transform, false);
                LandLedgersHUDController hud = hudObject.AddComponent<LandLedgersHUDController>();

                hud.SetTownPulseAlert("Repair Rush");
                hud.SetOpportunityNoticeAlert("Retail: Restock thin categories");
                hud.SetFirstSessionGuidance(
                    "Open Management",
                    "Management holds the core loop.",
                    "Press C.",
                    "Keep going.");

                RectTransform alert = hud.AlertStrip;
                float alertBottom = alert.anchoredPosition.y - alert.sizeDelta.y;
                float guidanceTop = hud.CurrentFirstSessionGuidanceAnchoredPosition.y;

                Assert.AreEqual(new Vector2(0f, -72f), alert.anchoredPosition);
                Assert.AreEqual(new Vector2(0f, 52f), alert.sizeDelta);
                Assert.LessOrEqual(guidanceTop, alertBottom - 14f);
                Assert.AreEqual(new Vector2(20f, -138f), hud.CurrentFirstSessionGuidanceAnchoredPosition);
                Assert.AreEqual(new Vector2(620f, 164f), hud.CurrentFirstSessionGuidanceSizeDelta);
                Assert.IsTrue(hud.IsTownPulseAlertVisible);
                Assert.IsTrue(hud.IsOpportunityNoticeAlertVisible);
                Assert.IsTrue(hud.IsFirstSessionGuidanceVisible);
            }
            finally
            {
                Object.DestroyImmediate(alertObject);
                Object.DestroyImmediate(hudObject);
            }
        }

        [Test]
        public void HudPrefabAuthorsFirstSessionGuidanceStrip()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Core/UI/UI Canvas.prefab");
            Assert.IsNotNull(prefab);

            RectTransform strip = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionObjectiveStrip");
            RectTransform content = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionObjectiveContent");
            RectTransform objective = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionObjectiveText");
            RectTransform why = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionWhyText");
            RectTransform action = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionActionText");
            RectTransform blocker = FindChild<RectTransform>(prefab.transform, "HUD_FirstSessionBlockerText");

            Assert.IsNotNull(strip);
            Assert.IsNotNull(content);
            Assert.IsNotNull(objective);
            Assert.IsNotNull(why);
            Assert.IsNotNull(action);
            Assert.IsNotNull(blocker);
            Assert.AreEqual(new Vector2(20f, -138f), strip.anchoredPosition);
            Assert.AreEqual(new Vector2(620f, 164f), strip.sizeDelta);
            Assert.IsNotNull(content.GetComponent<VerticalLayoutGroup>());
            Assert.IsNull(strip.GetComponent<HorizontalLayoutGroup>());
            Assert.AreEqual(content, objective.parent);
            Assert.AreEqual(content, why.parent);
            Assert.AreEqual(content, action.parent);
            Assert.Less(objective.GetSiblingIndex(), why.GetSiblingIndex());
            Assert.Less(why.GetSiblingIndex(), action.GetSiblingIndex());
            Assert.AreEqual(strip, blocker.parent);
            Assert.IsFalse(blocker.gameObject.activeSelf);
            Assert.AreEqual(new Vector2(0f, 0f), blocker.anchorMin);
            Assert.AreEqual(new Vector2(1f, 0f), blocker.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0f), blocker.pivot);

            LandLedgersHUDController controller = prefab.GetComponent<LandLedgersHUDController>();
            Assert.IsNotNull(controller);
        }

        private static T FindChild<T>(Transform root, string childName) where T : Component
        {
            T[] children = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i];
                }
            }

            return null;
        }
    }
}
