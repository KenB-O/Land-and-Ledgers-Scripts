using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Phase D (Real People, §26): housing observability at the source
    /// level — search decisions + rejected alternatives, property decisions,
    /// occupancy changes, construction progress, rent flows, and failed
    /// housing decisions with their reasons. The log is read-only: it never
    /// moves money, goods, or people.
    /// </summary>
    [TestFixture]
    public sealed class HousingDiagnosticsTests
    {
        [Test]
        public void SearchDecisionRecordsChosenPathAndRejectedAlternatives()
        {
            var log = new HousingDecisionLog();
            var decision = new HousingSearchDecision
            {
                DecisionId = "hsd-0", NeedId = "hsn-0", HouseholdId = 1, DayIndex = 10, Resolved = true,
                ChosenOption = new HousingSearchOption
                {
                    OptionId = "r-1", PathKind = HousingSearchPathKind.Rental,
                    ProviderName = "Landlord Ames", Description = "upper rooms",
                },
            };
            decision.RejectedAlternatives.Add(new HousingRejectedAlternative
            {
                OptionId = "p-1", PathKind = HousingSearchPathKind.Purchase,
                ProviderName = "Seller Brandt", Reason = "ownership not affordable",
            });

            HousingDecisionRecorder.RecordSearchDecision(log, decision);

            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.SearchDecision).Count);
            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.RejectedAlternative).Count);
            StringAssert.Contains("Rental", log.EventsOfKind(HousingDecisionEventKind.SearchDecision)[0].Summary);
            StringAssert.Contains("not affordable",
                log.EventsOfKind(HousingDecisionEventKind.RejectedAlternative)[0].Summary);
        }

        [Test]
        public void FailedSearchRecordsFailureWithReasons()
        {
            var log = new HousingDecisionLog();
            var decision = new HousingSearchDecision
            {
                DecisionId = "hsd-0", HouseholdId = 1, DayIndex = 10, Resolved = false,
                FailureReason = "no affordable adequate option",
            };
            decision.RejectedAlternatives.Add(new HousingRejectedAlternative
            {
                OptionId = "r-1", PathKind = HousingSearchPathKind.Rental,
                ProviderName = "Landlord Ames", Reason = "unaffordable: 2000c/month",
            });

            HousingDecisionRecorder.RecordSearchDecision(log, decision);

            List<HousingDecisionEvent> failed = log.FailedDecisions();
            Assert.AreEqual(1, failed.Count);
            Assert.AreEqual(1, failed[0].HouseholdId);
            StringAssert.Contains("no affordable", failed[0].Summary);
            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.RejectedAlternative).Count);
        }

        [Test]
        public void AllEventKindsAreQueryablePerHousehold()
        {
            var log = new HousingDecisionLog();
            HousingDecisionRecorder.RecordOccupancyChange(log, 5, 1, "space-1",
                AccommodationArrangement.Rental, 10, "moved in");
            HousingDecisionRecorder.RecordPropertyDecision(log, 1, "signed rental agreement pag-0", 10);
            HousingDecisionRecorder.RecordConstructionProgress(log, 2, "cproj-0", "foundation complete", 11);
            HousingDecisionRecorder.RecordRentFlow(log, 1, 5, "H1 paid 800c rent", 12);
            HousingDecisionRecorder.RecordFailedHousingDecision(log, 3, "search failed",
                new List<string> { "no options" }, 12);

            Assert.AreEqual(3, log.EventsForHousehold(1).Count);
            Assert.AreEqual(1, log.EventsForHousehold(2).Count);
            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.OccupancyChange).Count);
            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.ConstructionProgress).Count);
            Assert.AreEqual(1, log.EventsOfKind(HousingDecisionEventKind.RentFlow).Count);
            Assert.AreEqual(5, ((IReadOnlyList<HousingDecisionEvent>)log.Events).Count);
        }

        [Test]
        public void SaveLoadRoundTripHasNoDuplicatedEvents()
        {
            var log = new HousingDecisionLog();
            HousingDecisionRecorder.RecordRentFlow(log, 1, 5, "H1 paid 800c rent", 12);

            HousingDecisionLog.HousingDecisionLogSaveDto dto = log.CaptureSaveDto();
            var reloaded = new HousingDecisionLog();
            reloaded.LoadFromSaveDto(dto);
            reloaded.LoadFromSaveDto(dto);

            Assert.AreEqual(1, ((IReadOnlyList<HousingDecisionEvent>)reloaded.Events).Count);
        }
    }
}
