using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Integration
{
    /// <summary>
    /// Pass XII Worker F (copy-on-load sweep, E-DEFECT-02 pattern):
    /// Worker A fixed six load methods and flagged the rest as
    /// unaudited. These tests prove, for each load method Worker F
    /// fixed, that restoring the SAME in-memory snapshot twice does
    /// not carry run-1 mutations into run 2: load, mutate through
    /// the production path, load the same DTO again, and assert the
    /// pristine state. Disk loads were never at risk (deserialization
    /// mints fresh objects); the hazard is strictly in-memory
    /// snapshot reuse. Boarding-house and hotel reservation books
    /// were fixed the same way but are outside the Roslyn harness
    /// subset, so they have no harness test here — their fix is
    /// Unity-session verified, not harness verified.
    /// </summary>
    [TestFixture]
    public sealed class Pass12FCopyOnLoadTests
    {
        [Test]
        public void Tax_DoubleRestore_LoadsPristineAssessment_AndDoesNotWriteIntoDto()
        {
            var diag = new List<string>();
            var service = new PropertyTaxService();
            service.LevyAssessment(null, "plot-7", "Player", 0, 1500, 60, 100, 0, diag);
            var snapshot = service.CaptureSaveDto();
            Assert.AreEqual(1, snapshot.Assessments.Count);

            bool trackedBeforeLoad = snapshot.Assessments[0].FeesPaidTracked;
            var run1 = new PropertyTaxService();
            run1.LoadFromSaveDto(snapshot);
            for (int d = 1; d <= 100; d++) run1.AdvanceDay(d, diag);
            Assert.AreEqual(TaxAssessmentStatus.Delinquent, run1.AllAssessments[0].Status,
                "setup: advancing past the day-60 due date delinquents the run-1 copy.");
            Assert.Greater(run1.AllAssessments[0].PenaltiesAccruedCents, 0,
                "setup: run 1 accrues penalties once delinquent.");

            // The snapshot itself must be untouched: run 1's delinquency
            // and penalties must not leak into it, and the old-save fee
            // migration must not have rewritten its tracking flag (it
            // used to write FeesPaidCents into the DTO object itself).
            Assert.AreEqual(TaxAssessmentStatus.Assessed, snapshot.Assessments[0].Status);
            Assert.AreEqual(0, snapshot.Assessments[0].PenaltiesAccruedCents);
            Assert.AreEqual(trackedBeforeLoad, snapshot.Assessments[0].FeesPaidTracked);

            var run2 = new PropertyTaxService();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(TaxAssessmentStatus.Assessed, run2.AllAssessments[0].Status,
                "A second restore of the same snapshot must load the pristine assessment, not run 1's delinquency.");
            Assert.AreEqual(60, run2.AllAssessments[0].DueDayIndex);
            Assert.AreEqual(1500, run2.AllAssessments[0].AmountCents);
        }

        [Test]
        public void Housing_DoubleRestore_OccupancyStaysCurrent()
        {
            var diag = new List<string>();
            var housing = new HousingAuthority();
            Assert.IsNotNull(housing.RegisterBuilding("b1", "plot-1", "house", 0, "test", diag));
            AccommodationSpace space = housing.DefineSpace("b1", 2, diag);
            Assert.IsNotNull(space);
            Assert.IsNotNull(housing.Occupy(7, 11, space.SpaceId, AccommodationArrangement.Rental, 1, diag));
            var snapshot = housing.CaptureSaveDto();

            var run1 = new HousingAuthority();
            run1.LoadFromSaveDto(snapshot);
            run1.EndOccupanciesForPerson(7, 5, diag);
            Assert.AreEqual(0, run1.AllCurrentOccupancies().Count,
                "setup: ending the run-1 occupancy must leave nothing current.");

            var run2 = new HousingAuthority();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(1, run2.AllCurrentOccupancies().Count,
                "A second restore of the same snapshot must load the still-current occupancy.");
            Assert.AreEqual(-1, snapshot.Occupancies[0].UntilDayIndex,
                "The snapshot's own occupancy must still be current.");
        }

        [Test]
        public void Construction_DoubleRestore_RecordStaysUnbound()
        {
            var snapshot = new NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto();
            snapshot.Records.Add(new NpcConstructionRecord
            {
                RecordId = "rec-1",
                ProjectId = "proj-1",
                HouseholdId = 11,
            });

            var run1 = new NpcConstructionCommissioner();
            run1.LoadFromSaveDto(snapshot, new List<string>());
            NpcConstructionRecord loaded = run1.FindRecord("rec-1");
            Assert.IsNotNull(loaded);
            loaded.ContractId = "contract-9";
            loaded.Decisions.Add("day 3: hired builder");

            var run2 = new NpcConstructionCommissioner();
            run2.LoadFromSaveDto(snapshot, new List<string>());
            NpcConstructionRecord restored = run2.FindRecord("rec-1");
            Assert.IsNotNull(restored);
            Assert.AreEqual(string.Empty, restored.ContractId,
                "A second restore must not carry run 1's contract binding.");
            Assert.AreEqual(0, restored.Decisions.Count);
            Assert.AreEqual(string.Empty, snapshot.Records[0].ContractId);
        }

        [Test]
        public void Credit_DoubleRestore_NoteStaysActive()
        {
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            var registry = new CreditRegistry();
            PromissoryNote note = registry.IssuePromissoryNote(ids, "Abel", "Bank", 500000, "8%, due day 900", 10, null, diag);
            Assert.IsNotNull(note);
            var snapshot = registry.CaptureSaveDto();

            var run1 = new CreditRegistry();
            run1.LoadFromSaveDto(snapshot);
            Assert.IsNull(run1.SatisfyInstrument(note.InstrumentId.ToString(), diag));
            Assert.AreEqual(CreditInstrumentStatus.Satisfied, run1.CaptureSaveDto().Notes[0].Status,
                "setup: satisfying the run-1 copy must mark it satisfied.");

            var run2 = new CreditRegistry();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(CreditInstrumentStatus.Active, run2.CaptureSaveDto().Notes[0].Status,
                "A second restore of the same snapshot must load the still-active note.");
            Assert.AreEqual(CreditInstrumentStatus.Active, snapshot.Notes[0].Status);
        }

        [Test]
        public void Market_DoubleRestore_ListingStaysActive()
        {
            var snapshot = new UsedEquipmentMarketBook.UsedEquipmentMarketBookDto();
            snapshot.Listings.Add(new UsedEquipmentListing
            {
                ListingId = EntityId.For(EntityKind.Contract, 1),
                AssetId = "asset-1",
                DisplayName = "Test Plow",
                AskingPriceCents = 5000,
                Status = UsedEquipmentListingStatus.Active,
            });

            var run1 = new UsedEquipmentMarketBook();
            run1.LoadFromSaveDto(snapshot);
            string key = null;
            foreach (KeyValuePair<string, UsedEquipmentListing> entry in run1.Listings) key = entry.Key;
            Assert.IsNotNull(key);
            Assert.IsNull(run1.WithdrawListing(key, 5, "test withdrawal", new List<string>()));
            Assert.AreEqual(UsedEquipmentListingStatus.Withdrawn, run1.Listings[key].Status,
                "setup: withdrawing the run-1 copy must mark it withdrawn.");

            var run2 = new UsedEquipmentMarketBook();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(UsedEquipmentListingStatus.Active, run2.Listings[key].Status,
                "A second restore of the same snapshot must load the still-active listing.");
            Assert.AreEqual(UsedEquipmentListingStatus.Active, snapshot.Listings[0].Status);
        }

        [Test]
        public void Schedule_DoubleRestore_ReservationStaysUnreleased()
        {
            var tracker = new PersonScheduleTracker();
            Assert.IsNull(tracker.TryReserve(7, PersonActivityKind.Work, 3, 480, 60, "work"));
            var snapshot = tracker.CaptureSaveDto();
            Assert.AreEqual(1, snapshot.reservations.Count);
            string reservationId = snapshot.reservations[0].ReservationId;

            var run1 = new PersonScheduleTracker();
            run1.LoadFromSaveDto(snapshot);
            run1.Release(reservationId);
            Assert.IsTrue(run1.Reservations[0].Released, "setup: releasing the run-1 copy must mark it released.");

            var run2 = new PersonScheduleTracker();
            run2.LoadFromSaveDto(snapshot);
            Assert.IsFalse(run2.Reservations[0].Released,
                "A second restore of the same snapshot must load the unreleased reservation.");
            Assert.IsFalse(snapshot.reservations[0].Released);
        }

        [Test]
        public void Errand_DoubleRestore_PlanStaysProposed()
        {
            var registry = new HouseholdErrandPlanRegistry();
            HouseholdErrandPlan plan = registry.Create(11, 3);
            Assert.AreEqual(HouseholdErrandPlanStatus.Proposed, plan.Status);
            var snapshot = registry.CaptureSaveDto();

            var run1 = new HouseholdErrandPlanRegistry();
            run1.LoadFromSaveDto(snapshot);
            run1.All[0].MarkCompleted();
            Assert.AreEqual(HouseholdErrandPlanStatus.Completed, run1.All[0].Status,
                "setup: completing the run-1 copy must mark it completed.");

            var run2 = new HouseholdErrandPlanRegistry();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(HouseholdErrandPlanStatus.Proposed, run2.All[0].Status,
                "A second restore of the same snapshot must load the still-proposed plan.");
            Assert.AreEqual(HouseholdErrandPlanStatus.Proposed, snapshot.Plans[0].Status);
        }
    }
}
