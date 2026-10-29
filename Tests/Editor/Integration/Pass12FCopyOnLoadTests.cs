using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
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

        // ---- Round 2 (delegated-review reconciliation): the first sweep
        // cloned occupancies only, but buildings, spaces, assignments
        // and agreements are also mutated in place; TaskAuthority,
        // the construction executor, the credit workflow and the
        // condition registry were missed entirely (TaskAuthority was
        // wrongly reported as nonexistent — it lives in Core/Tasks).

        [Test]
        public void Housing_AllCollections_DoubleRestore()
        {
            var diag = new List<string>();
            var snapshot = new HousingAuthority.HousingSaveDto
            {
                Buildings = new List<Building>
                {
                    new Building { BuildingId = "b1", ParcelId = "plot-1", Kind = "house", Condition01 = 1f, BuiltDayIndex = 0 },
                },
                Spaces = new List<AccommodationSpace>
                {
                    new AccommodationSpace { SpaceId = "s1", BuildingId = "b1", SleepingCapacity = 4 },
                },
                Assignments = new List<FunctionalSpaceAssignment>
                {
                    new FunctionalSpaceAssignment { AssignmentId = "a1", SpaceId = "s1", BusinessInstanceId = "biz-1", OperationDescription = "shop", SinceDayIndex = 0, UntilDayIndex = -1 },
                },
                Agreements = new List<PropertyAgreement>
                {
                    new PropertyAgreement
                    {
                        AgreementId = "g1", Kind = "lease", GrantorName = "Town", GranteeName = "Player",
                        SpaceOrParcelId = "s1", Terms = "terms", StartDayIndex = 0, EndDayIndex = -1,
                        PaymentTerms = new TenancyPaymentTerms { AmountCents = 5000, PeriodDays = 30, DueDayOfPeriod = 1, StartDay = 0, PayeeHolderId = "town", TenantHouseholdId = 11 },
                    },
                },
            };

            var run1 = new HousingAuthority();
            run1.LoadFromSaveDto(snapshot);
            Assert.IsNull(run1.ChangeSpaceCapacity("s1", 2, 5, diag), "setup: capacity change must succeed.");
            Assert.IsNull(run1.EndAgreement("g1", 6, false, "test", diag), "setup: agreement end must succeed.");
            run1.AllBuildings()[0].Condition01 = 0.5f;
            HousingAuthority.BuildingClosureResult closed = run1.CloseBuilding("b1", 7, "test", diag);
            Assert.AreEqual(1, closed.AssignmentsEnded, "setup: run-1 close must end the live assignment.");
            Assert.AreEqual(2, run1.SpacesForBuilding("b1")[0].SleepingCapacity, "setup: run-1 space shrunk.");
            Assert.AreEqual(0.5f, run1.AllBuildings()[0].Condition01, "setup: run-1 building worn.");

            // The snapshot itself must be untouched by every run-1 edit.
            Assert.AreEqual(4, snapshot.Spaces[0].SleepingCapacity);
            Assert.AreEqual(-1, snapshot.Agreements[0].EndDayIndex);
            Assert.AreEqual("town", snapshot.Agreements[0].PaymentTerms.PayeeHolderId);
            Assert.AreEqual(-1, snapshot.Assignments[0].UntilDayIndex);
            Assert.AreEqual(1f, snapshot.Buildings[0].Condition01);

            var run2 = new HousingAuthority();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(4, run2.SpacesForBuilding("b1")[0].SleepingCapacity,
                "A second restore must load the pristine space capacity, not run 1's shrink.");
            Assert.AreEqual(-1, run2.FindAgreement("g1").EndDayIndex,
                "A second restore must load the still-open agreement, not run 1's ended one.");
            Assert.AreEqual(1f, run2.AllBuildings()[0].Condition01,
                "A second restore must load the pristine building condition, not run 1's wear.");
            HousingAuthority.BuildingClosureResult closed2 = run2.CloseBuilding("b1", 8, "test", diag);
            Assert.AreEqual(1, closed2.AssignmentsEnded,
                "A second restore must load the still-active assignment, not run 1's ended one.");
        }

        [Test]
        public void TaskAuthority_DoubleRestore()
        {
            var definition = new TaskDefinition("def-1", "Haul", 60);
            var task = new WorkTask();
            typeof(WorkTask).GetField("taskId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(task, EntityId.For(EntityKind.WorkTask, 1));
            typeof(WorkTask).GetField("definitionId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(task, "def-1");
            typeof(WorkTask).GetField("ownerId", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(task, EntityId.For(EntityKind.Person, 7));
            var snapshot = new TaskAuthoritySaveDto
            {
                definitions = new List<TaskDefinition> { definition },
                tasks = new List<WorkTask> { task },
            };

            var run1 = new TaskAuthority();
            run1.LoadFromSaveDto(snapshot);
            WorkTask live = run1.FindTask(EntityId.For(EntityKind.WorkTask, 1));
            Assert.IsNotNull(live, "setup: loaded task must be findable.");
            // StartTask requires Assigned/ResourcesReserved, so drive the
            // lifecycle directly (internal methods are visible inside
            // the single harness assembly): start + work the run-1 copy.
            typeof(WorkTask).GetMethod("MarkStarted", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(live, new object[] { 3 });
            typeof(WorkTask).GetMethod("AddWorkedMinutes", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(live, new object[] { 30 });
            Assert.AreEqual(TaskStatus.InProgress, live.Status, "setup: run-1 task started.");
            Assert.AreEqual(30, live.MinutesWorked, "setup: run-1 task worked.");

            // The snapshot task itself must still be queued and unworked.
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.AreEqual(0, task.MinutesWorked);

            var run2 = new TaskAuthority();
            run2.LoadFromSaveDto(snapshot);
            WorkTask reloaded = run2.FindTask(EntityId.For(EntityKind.WorkTask, 1));
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(TaskStatus.Queued, reloaded.Status,
                "A second restore must load the still-queued task, not run 1's started, half-worked task.");
            Assert.AreEqual(0, reloaded.MinutesWorked);
        }

        [Test]
        public void Executor_DoubleRestore_AndDedupRunsOnCopy()
        {
            var diag = new List<string>();
            var project = new ConstructionProject
            {
                ProjectId = "proj-1", DesignId = "d1", HouseholdId = 11, ParcelId = "plot-1",
                WorkPackages = new List<WorkPackage>
                {
                    new WorkPackage { PackageId = "pkg-1", ProjectId = "proj-1", PhaseName = "frame", Sequence = 0, LaborMinutesRequired = 100, LaborMinutesCommitted = 0, Status = WorkPackageStatus.Draft },
                    new WorkPackage { PackageId = "pkg-1", ProjectId = "proj-1", PhaseName = "frame-duplicate", Sequence = 0, LaborMinutesRequired = 100, LaborMinutesCommitted = 0, Status = WorkPackageStatus.Draft },
                    new WorkPackage { PackageId = "pkg-2", ProjectId = "proj-1", PhaseName = "roof", Sequence = 1, LaborMinutesRequired = 50, LaborMinutesCommitted = 0, Status = WorkPackageStatus.Draft },
                },
            };
            var snapshot = new ConstructionProjectExecutor.ConstructionProjectSaveDto
            {
                Projects = new List<ConstructionProject> { project },
                ExecutorSequence = 0,
            };

            var executor1 = new ConstructionProjectExecutor();
            List<ConstructionProject> live = executor1.LoadFromSaveDto(snapshot, diag);
            Assert.AreEqual(1, live.Count);
            Assert.AreEqual(2, live[0].WorkPackages.Count, "setup: duplicate package deduped on load.");
            WorkPackage livePkg = live[0].WorkPackages[0];
            livePkg.Status = WorkPackageStatus.Complete;
            livePkg.LaborMinutesCommitted = 120;

            // Dedup must have run on the copy: the snapshot still holds
            // all three packages, and its first package is still Draft.
            Assert.AreEqual(3, snapshot.Projects[0].WorkPackages.Count);
            Assert.AreEqual(WorkPackageStatus.Draft, snapshot.Projects[0].WorkPackages[0].Status);
            Assert.AreEqual(0, snapshot.Projects[0].WorkPackages[0].LaborMinutesCommitted);

            var executor2 = new ConstructionProjectExecutor();
            List<ConstructionProject> reloaded = executor2.LoadFromSaveDto(snapshot, diag);
            Assert.AreEqual(WorkPackageStatus.Draft, reloaded[0].WorkPackages[0].Status,
                "A second restore must load the pristine package, not run 1's completed one.");
            Assert.AreEqual(0, reloaded[0].WorkPackages[0].LaborMinutesCommitted);
        }

        [Test]
        public void CreditWorkflow_DoubleRestore()
        {
            var offer = new CreditOffer
            {
                OfferId = "off-1", RequestId = "req-1", Borrower = "store-1", Lender = "bank",
                RequestedAmountCents = 50000, OfferedAmountCents = 50000, Status = CreditOfferStatus.Proposed,
            };
            var snapshot = new CreditOfferWorkflowSaveDto
            {
                Offers = new List<CreditOffer> { offer },
            };

            var run1 = new CreditOfferWorkflow();
            run1.LoadFromSaveDto(snapshot);
            Assert.IsTrue(run1.CounterOffer("off-1", 45000, 500, 365, "lower", out string message),
                "setup: countering the run-1 offer must succeed: " + message);
            Assert.AreEqual(CreditOfferStatus.Countered, run1.FindOffer("off-1").Status, "setup: run-1 offer countered.");
            Assert.AreEqual(45000, run1.FindOffer("off-1").OfferedAmountCents, "setup: run-1 amount moved.");

            Assert.AreEqual(CreditOfferStatus.Proposed, offer.Status, "The snapshot offer must stay Proposed.");
            Assert.AreEqual(50000, offer.OfferedAmountCents);
            Assert.AreEqual(0, offer.NegotiationHistory.Count);

            var run2 = new CreditOfferWorkflow();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual(CreditOfferStatus.Proposed, run2.FindOffer("off-1").Status,
                "A second restore must load the still-proposed offer, not run 1's countered one.");
            Assert.AreEqual(50000, run2.FindOffer("off-1").OfferedAmountCents);
            Assert.AreEqual(0, run2.FindOffer("off-1").NegotiationHistory.Count);
        }

        [Test]
        public void ResidenceCondition_DoubleRestore_QuarantineRunsOnCopy()
        {
            var snapshot = new ResidentialConditionRegistry.ResidentialConditionRegistrySaveDto
            {
                History = new List<ResidentialConditionRecord>
                {
                    new ResidentialConditionRecord { RecordId = "r1", PersonId = 7, HouseholdId = 11, Condition = ResidentialConditionKind.SuitablyHoused, SinceDayIndex = 0, UntilDayIndex = -1, LastEvaluatedDayIndex = 5 },
                    new ResidentialConditionRecord { RecordId = "r2", PersonId = 7, HouseholdId = 11, Condition = ResidentialConditionKind.SuitablyHoused, SinceDayIndex = 0, UntilDayIndex = -1, LastEvaluatedDayIndex = 5 },
                },
            };

            var run1 = new ResidentialConditionRegistry();
            run1.LoadFromSaveDto(snapshot);
            Assert.AreEqual("r1", run1.CurrentConditionFor(7).RecordId, "setup: first open record stays current.");
            Assert.GreaterOrEqual(run1.History[1].UntilDayIndex, 0, "setup: duplicate open record quarantined on load.");

            // The quarantine must have run on the copy: the snapshot's
            // second record is still open.
            Assert.AreEqual(-1, snapshot.History[1].UntilDayIndex);

            var run2 = new ResidentialConditionRegistry();
            run2.LoadFromSaveDto(snapshot);
            Assert.AreEqual("r1", run2.CurrentConditionFor(7).RecordId);
            Assert.GreaterOrEqual(run2.History[1].UntilDayIndex, 0,
                "A second restore must quarantine afresh, not reload an already-quarantined record.");
        }

        [Test]
        public void LedgerImport_SmallerLoad_DropsStaleLedgers()
        {
            var registry = new HouseholdLedgerRegistry();
            registry.ImportState(new[]
            {
                new HouseholdLedgerState { HouseholdId = 1, NextEntrySequence = 1, Entries = new List<HouseholdLedgerEntry>() },
                new HouseholdLedgerState { HouseholdId = 2, NextEntrySequence = 1, Entries = new List<HouseholdLedgerEntry>() },
            });
            Assert.IsNotNull(registry.Get(1), "setup: household 1 loaded.");
            Assert.IsNotNull(registry.Get(2), "setup: household 2 loaded.");

            registry.ImportState(new[]
            {
                new HouseholdLedgerState { HouseholdId = 1, NextEntrySequence = 2, Entries = new List<HouseholdLedgerEntry>() },
            });
            Assert.IsNotNull(registry.Get(1), "The kept household survives the smaller load.");
            Assert.IsNull(registry.Get(2),
                "A smaller save must not leave the dropped household's ledger behind.");
        }
    }
}
