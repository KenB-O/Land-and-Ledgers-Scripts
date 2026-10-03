using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Orchestration.Scenarios.StartingChoice;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests
{
    /// <summary>
    /// T2H: the departed are archived with references intact (PL-27), and the
    /// campaign starts with a player-chosen opportunity after investigation
    /// (PL-45, GHOST-CAN-008/009).
    /// </summary>
    public sealed class ArchivalAndStartingChoiceTests
    {
        private PopulationState population;
        private EmploymentRelationshipRegistry employment;
        private PersonArchive archive;
        private List<string> diag;

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Elder", lastName = "Em", age = 70,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 1, deathDayIndex = 100,
            });
            population.people.Add(new PersonState
            {
                id = 2, firstName = "Young", lastName = "Em", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 1, deathDayIndex = -1,
            });
            employment = new EmploymentRelationshipRegistry();
            employment.Register(new EmploymentRelationship
            {
                Id = "emp-1", EmployeePersonId = 1, EmployerBusinessId = "biz-1",
                RoleDisplayName = "hand", LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(500, "test"),
                Source = EmploymentSource.Manual,
            });
            archive = new PersonArchive();
            diag = new List<string>();
        }

        [Test]
        public void DepartedAreArchivedNotDeleted()
        {
            int count = archive.ArchiveDeparted(population, employment, 100, diag);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1, population.people.Count, "The living remain; the departed move.");
            Assert.IsNull(population.GetPerson(1));

            HistoricalPersonRecord record = archive.GetHistorical(1);
            Assert.IsNotNull(record);
            Assert.AreEqual("Elder Em", record.FullName);
            Assert.AreEqual(100, record.DeathDayIndex);
            CollectionAssert.Contains(record.EmploymentReferenceIds, "emp-1",
                "Employment references survive archival — nothing is orphaned.");
        }

        [Test]
        public void TheLivingAreNeverArchived()
        {
            int count = archive.ArchiveDeparted(population, employment, 100, diag);
            Assert.AreEqual(1, count);
            Assert.IsNull(archive.GetHistorical(2));
            Assert.IsNotNull(population.GetPerson(2));
        }

        [Test]
        public void ArchiveRoundTrips()
        {
            archive.ArchiveDeparted(population, employment, 100, diag);
            var dto = archive.CaptureSaveDto();
            var restored = new PersonArchive();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.HistoricalCount);
            Assert.AreEqual("Elder Em", restored.GetHistorical(1).FullName);
        }

        [Test]
        public void PlayerChoosesFromAConstrainedPoolBeforeTheClockStarts()
        {
            var service = new StartingChoiceService();
            service.OfferOpportunity("opp-buy", StartingOpportunityKind.BuyBusiness,
                "Buy the bakery", "A going concern.", 800000,
                new List<string> { "review the books", "talk to suppliers" }, diag);
            service.OfferOpportunity("opp-work", StartingOpportunityKind.TakeEmployment,
                "Clerk at the store", "A wage.", 0,
                new List<string> { "meet the proprietor" }, diag);

            Assert.IsFalse(service.ClockStarted, "The clock starts on choice — never before (GHOST-CAN-008).");

            string problem = service.Choose("opp-work", diag);
            Assert.IsNull(problem);
            Assert.IsTrue(service.ClockStarted);

            // One start per campaign.
            Assert.IsNotNull(service.Choose("opp-buy", diag));
            // The pool closes after the clock starts.
            Assert.IsNull(service.OfferOpportunity("opp-late", StartingOpportunityKind.BuyProperty,
                "Late", "Too late.", 0, null, diag));
        }

        [Test]
        public void InvestigationCanKillAnIdea()
        {
            var service = new StartingChoiceService();
            service.OfferOpportunity("opp-buy", StartingOpportunityKind.BuyBusiness,
                "Buy the bakery", "A going concern.", 800000,
                new List<string> { "review the books" }, diag);

            service.Investigate("opp-buy", "the books show three years of losses", killsIdea: true, diag);

            string problem = service.Choose("opp-buy", diag);
            Assert.IsNotNull(problem, "A killed idea cannot be chosen.");
            Assert.IsFalse(service.ClockStarted);
        }

        [Test]
        public void InvestigationFindingsAccumulate()
        {
            var service = new StartingChoiceService();
            var opp = service.OfferOpportunity("opp-buy", StartingOpportunityKind.BuyBusiness,
                "Buy the bakery", "A going concern.", 800000,
                new List<string> { "review the books", "talk to suppliers" }, diag);

            service.Investigate("opp-buy", "books are clean", killsIdea: false, diag);
            Assert.IsFalse(opp.InvestigationComplete);
            service.Investigate("opp-buy", "suppliers speak well of it", killsIdea: false, diag);
            Assert.IsTrue(opp.InvestigationComplete);
        }
    }
}
