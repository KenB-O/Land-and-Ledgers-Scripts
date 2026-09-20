using LandLedgers.MVP;
using LandLedgers.UI;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    public sealed class FirstSessionGuidanceProgressTests
    {
        [Test]
        public void GuidanceStartsByPointingAtManagement()
        {
            FirstSessionGuidanceProgress progress = new();

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts());

            Assert.AreEqual(FirstSessionObjectiveStep.OpenManagement, objective.Step);
            StringAssert.Contains("Open Management", objective.Objective);
            StringAssert.Contains("Press C", objective.Action);
        }

        [Test]
        public void GuidanceAdvancesThroughStoreAndMoneyModelFromRuntimeFacts()
        {
            FirstSessionGuidanceProgress progress = new();
            progress.Evaluate(new FirstSessionGuidanceFacts());

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties
            });
            Assert.AreEqual(FirstSessionObjectiveStep.InspectGeneralStore, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.HireFirstWorker, objective.Step);
            StringAssert.Contains("Hire", objective.Objective);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                FirstSalesObserved = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.HireFirstWorker, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.ObserveFirstSales, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.ReviewMoneyModel, objective.Step);
            StringAssert.Contains("Owner Cash", objective.Why);
            StringAssert.Contains("Store Cash", objective.Why);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Finances,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.OpenAcquisitions, objective.Step);
        }

        [Test]
        public void GuidanceUsesLandShellAndBusinessRuntimeStateForExpansion()
        {
            FirstSessionGuidanceProgress progress = new();
            progress.Evaluate(new FirstSessionGuidanceFacts());
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Finances,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Acquisitions,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.CompleteFirstLandPurchase, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Acquisitions,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1
            });
            Assert.AreEqual(FirstSessionObjectiveStep.ReturnToProperties, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1
            });
            Assert.AreEqual(FirstSessionObjectiveStep.BuildFirstBusinessShell, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1,
                StartableOwnedShellExists = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.StartFirstBusiness, objective.Step);

            objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1,
                OwnedBusinessCount = 1,
                StartableOwnedShellExists = true,
                PlayerOwnedExpansionBusinessExists = true
            });
            Assert.AreEqual(FirstSessionObjectiveStep.Complete, objective.Step);
        }

        [Test]
        public void GuidanceDoesNotSkipStoreInspectionWhenLaterStoreSignalsAppearWithoutSelection()
        {
            FirstSessionGuidanceProgress progress = new();
            progress.Evaluate(new FirstSessionGuidanceFacts());

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });

            Assert.AreEqual(FirstSessionObjectiveStep.InspectGeneralStore, objective.Step,
                "Later staffing or sales facts should not skip the explicit store-inspection step when no store is selected.");
        }

        [Test]
        public void GuidanceDoesNotSkipMoneyModelWhenAcquisitionSignalsAppearEarly()
        {
            FirstSessionGuidanceProgress progress = new();
            progress.Evaluate(new FirstSessionGuidanceFacts());
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true
            });
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true
            });

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Acquisitions,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1
            });

            Assert.AreEqual(FirstSessionObjectiveStep.ReviewMoneyModel, objective.Step,
                "The first-session flow should still teach Owner Cash versus Store Cash before jumping into acquisition progress.");
            StringAssert.Contains("Owner Cash", objective.Why);
            StringAssert.Contains("Store Cash", objective.Why);
        }

        [Test]
        public void GuidanceDoesNotSkipReturnToPropertiesWhenShellSignalsAppearWhileStillInAcquisitions()
        {
            FirstSessionGuidanceProgress progress = AdvanceToOpenAcquisitions();

            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Acquisitions,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });

            FirstSessionObjectiveState objective = progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Acquisitions,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true,
                OwnedLandCount = 1,
                StartableOwnedShellExists = true
            });

            Assert.AreEqual(FirstSessionObjectiveStep.ReturnToProperties, objective.Step,
                "Owning land and even having shell-ready signals should not skip the explicit return-to-properties handoff.");
        }


        private static FirstSessionGuidanceProgress AdvanceToOpenAcquisitions()
        {
            FirstSessionGuidanceProgress progress = new();
            progress.Evaluate(new FirstSessionGuidanceFacts());
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Properties,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            progress.Evaluate(new FirstSessionGuidanceFacts
            {
                ManagementOpen = true,
                CurrentTab = ManagementPanelTab.Finances,
                GeneralStoreSelected = true,
                GeneralStoreStaffed = true,
                FirstSalesObserved = true
            });
            return progress;
        }
    }
}
