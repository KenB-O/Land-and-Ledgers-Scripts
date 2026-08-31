using LandLedgers.Persistence;
using LandLedgers.Progression;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Progression
{
    public sealed class OwnershipAptitudeFoundationTests
    {
        [Test]
        public void SupportedSourcesAddExpectedExperienceAndCounters()
        {
            OwnershipAptitudeState state = new();

            state.RecordSource(OwnershipAptitudeSource.LandPurchased);
            Assert.AreEqual(10, state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.LandPurchased));

            state.RecordSource(OwnershipAptitudeSource.BusinessPurchased);
            Assert.AreEqual(26, state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.BusinessPurchased));

            state.RecordSource(OwnershipAptitudeSource.ParcelDeveloped);
            Assert.AreEqual(12, state.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.ParcelDeveloped));

            state.RecordSource(OwnershipAptitudeSource.BusinessOpened);
            Assert.AreEqual(26, state.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(4, state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.BusinessOpened));

            state.RecordSource(OwnershipAptitudeSource.TurnaroundCompleted);
            Assert.AreEqual(44, state.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(14, state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.TurnaroundCompleted));

            state.RecordSource(OwnershipAptitudeSource.OwnershipMilestone);
            Assert.AreEqual(30, state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(48, state.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(22, state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(1, state.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone));
        }

        [Test]
        public void CategorySeparationKeepsAcquisitionAndDevelopmentDistinct()
        {
            OwnershipAptitudeState acquisition = new();
            acquisition.RecordSource(OwnershipAptitudeSource.LandPurchased);

            Assert.Greater(acquisition.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition), 0);
            Assert.AreEqual(0, acquisition.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(0, acquisition.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));

            OwnershipAptitudeState operations = new();
            operations.RecordSource(OwnershipAptitudeSource.ParcelDeveloped);

            Assert.AreEqual(0, operations.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.Greater(operations.GetExperiencePoints(OwnershipAptitudeCategory.Operations), 0);
            Assert.AreEqual(0, operations.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
        }

        [Test]
        public void SaveDtoRoundTripsAptitudeState()
        {
            OwnershipAptitudeState state = new();
            state.RecordSource(OwnershipAptitudeSource.BusinessPurchased);
            state.RecordSource(OwnershipAptitudeSource.BusinessOpened);
            state.RecordOwnershipMilestones(5, 0);

            OwnershipAptitudeSaveDto dto = state.CaptureSaveDto();
            OwnershipAptitudeState restored = OwnershipAptitudeState.FromSaveDto(dto);

            Assert.AreEqual(state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition), restored.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(state.GetExperiencePoints(OwnershipAptitudeCategory.Operations), restored.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation), restored.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(state.GetSourceCount(OwnershipAptitudeSource.BusinessPurchased), restored.GetSourceCount(OwnershipAptitudeSource.BusinessPurchased));
            Assert.AreEqual(state.GetSourceCount(OwnershipAptitudeSource.BusinessOpened), restored.GetSourceCount(OwnershipAptitudeSource.BusinessOpened));
            Assert.AreEqual(state.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone), restored.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone));
            Assert.AreEqual(state.highestOwnershipMilestoneCount, restored.highestOwnershipMilestoneCount);
        }

        [Test]
        public void OldSaveWithoutAptitudeDataRestoresEmptyCompatibleState()
        {
            AcquisitionSaveDto oldStyleDto = JsonUtility.FromJson<AcquisitionSaveDto>(
                "{\"marketSeed\":1902,\"maxLandListings\":4,\"maxBusinessListings\":2}");

            OwnershipAptitudeState restored = OwnershipAptitudeState.FromSaveDto(oldStyleDto.ownershipAptitude);

            Assert.AreEqual(0, restored.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(0, restored.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(0, restored.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(0, restored.GetSourceCount(OwnershipAptitudeSource.LandPurchased));
            Assert.AreEqual(0, restored.highestOwnershipMilestoneCount);
        }

        [Test]
        public void GrowthAndEvaluatedEffectsStayBounded()
        {
            OwnershipAptitudeState state = new();
            for (int i = 0; i < 1000; i++)
            {
                state.RecordSource(OwnershipAptitudeSource.LandPurchased);
                state.RecordSource(OwnershipAptitudeSource.ParcelDeveloped);
                state.RecordSource(OwnershipAptitudeSource.TurnaroundCompleted);
                state.RecordSource(OwnershipAptitudeSource.OwnershipMilestone);
            }

            Assert.AreEqual(OwnershipAptitudeState.MaxCategoryExperiencePoints, state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(OwnershipAptitudeState.MaxCategoryExperiencePoints, state.GetExperiencePoints(OwnershipAptitudeCategory.Operations));
            Assert.AreEqual(OwnershipAptitudeState.MaxCategoryExperiencePoints, state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));

            OwnershipAptitudeResult result = new OwnershipAptitudeEvaluator().Evaluate(state);

            Assert.Greater(result.OfferEstimateModifier01, 0f);
            Assert.LessOrEqual(result.OfferEstimateModifier01, OwnershipAptitudeEvaluator.MaxOfferEstimateModifier01);
            Assert.LessOrEqual(result.DueDiligenceReadModifier01, OwnershipAptitudeEvaluator.MaxDueDiligenceReadModifier01);
            Assert.LessOrEqual(result.HiringReadModifier01, OwnershipAptitudeEvaluator.MaxHiringReadModifier01);
            Assert.LessOrEqual(result.ManagerOversightModifier01, OwnershipAptitudeEvaluator.MaxManagerOversightModifier01);
        }

        [Test]
        public void OwnershipMilestonesAwardOncePerThreshold()
        {
            OwnershipAptitudeState state = new();

            Assert.AreEqual(2, state.RecordOwnershipMilestones(3, 0));
            Assert.AreEqual(2, state.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone));
            Assert.AreEqual(3, state.highestOwnershipMilestoneCount);

            int acquisitionXpAfterFirstPass = state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition);
            int peopleXpAfterFirstPass = state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation);

            Assert.AreEqual(0, state.RecordOwnershipMilestones(2, 1));
            Assert.AreEqual(acquisitionXpAfterFirstPass, state.GetExperiencePoints(OwnershipAptitudeCategory.Acquisition));
            Assert.AreEqual(peopleXpAfterFirstPass, state.GetExperiencePoints(OwnershipAptitudeCategory.PeopleReputation));
            Assert.AreEqual(2, state.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone));

            Assert.AreEqual(1, state.RecordOwnershipMilestones(5, 0));
            Assert.AreEqual(3, state.GetSourceCount(OwnershipAptitudeSource.OwnershipMilestone));
            Assert.AreEqual(5, state.highestOwnershipMilestoneCount);
        }
    }
}
