using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Market;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class OccupationProfileCatalogTests
    {
        [Test]
        public void TierOneContainsTheLockedTwelveProfilesWithoutDuplicateTitles()
        {
            Assert.AreEqual(12, OccupationProfileCatalog.TierOne.Count);
            var ids = new HashSet<string>();
            foreach (OccupationProfile profile in OccupationProfileCatalog.TierOne)
            {
                Assert.IsTrue(ids.Add(profile.Id));
                Assert.AreEqual(1, profile.Tier);
                Assert.IsNotEmpty(profile.TaskFamilies);
                Assert.IsNotEmpty(profile.CapabilityFamilies);
            }
        }

        [Test]
        public void HistoricalTitlesResolveToExistingProfiles()
        {
            Assert.AreEqual("farmer", OccupationProfileCatalog.Find("Dairy Farmer").Id);
            Assert.AreEqual("teamster", OccupationProfileCatalog.Find("Drayman").Id);
            Assert.AreEqual("carpenter", OccupationProfileCatalog.Find("Railroad Carpenter").Id);
            Assert.AreEqual("merchant", OccupationProfileCatalog.Find("Feed Merchant").Id);
        }

        [Test]
        public void CalibrationReportDerivesFromPersonsRatherThanPopulationQuotas()
        {
            PopulationState state = new PopulationState();
            state.people.Add(new PersonState { id = 1, ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket, professionId = "teamster" });
            state.people.Add(new PersonState { id = 2, ageBand = AgeBand.Child0To9, laborAccessLevel = LaborAccessLevel.None });

            OccupationCalibrationReport report = OccupationCalibrationReport.Build(state);
            Assert.AreEqual(2, report.PersonCount);
            Assert.AreEqual(1, report.EconomicallyActivePersonCount);
            Assert.AreEqual(1, report.ResolvedOccupationCount);
            Assert.AreEqual(1, report.UnresolvedOccupationCount);
            Assert.AreEqual(1, report.PeopleByOccupation["teamster"]);
        }

        [Test]
        public void FreightFormationNeedsARealTeamsterButNotAStorefront()
        {
            PersonState founder = new PersonState
            {
                firstName = "Martha", lastName = "Collins", professionId = "teamster", startingCashCents = 5000
            };
            BusinessOpportunity opportunity = new BusinessOpportunity
            {
                BusinessType = BusinessType.LiveryFreight, Score01 = 0.8f
            };

            BusinessFormationAssessment assessment = BusinessFormationAssessment.Evaluate(
                opportunity, founder, 0, true, false, true);

            Assert.AreEqual(BusinessFormationDecision.Plausible, assessment.Decision);
            Assert.IsTrue(assessment.HasPremisesAccess,
                "Freight formation treats mobile operation as satisfying the premises requirement without a storefront.");
            Assert.AreEqual("teamster", assessment.FounderProfile.Id);
        }
    }
}
