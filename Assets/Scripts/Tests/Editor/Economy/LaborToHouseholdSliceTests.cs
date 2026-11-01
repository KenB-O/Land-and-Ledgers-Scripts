// Vertical slice: labor -> household economy (Autonomous Labor pass).
// Deterministic integration fixture using production authorities.
// World: 3-5 households, employable persons, 2 businesses, tight labor
// supply, one owner-delegated manager. Day indices 400+.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class LaborToHouseholdSliceTests
    {
        private List<string> Diag() => new List<string>();

        private sealed class World
        {
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
            public ContactGraphService Contacts = new ContactGraphService();
            public EmploymentNegotiationService Negotiation =
                new EmploymentNegotiationService((biz, person, act) => true); // owner P3 offers directly
            public LaborDemandAssessor Demand = new LaborDemandAssessor();
            public WorkScheduler Scheduler = new WorkScheduler();
            public WageSettlementService Settlement = new WageSettlementService();
            public ManagerBrain Brain = new ManagerBrain();
            public BusinessRuntimeState Store = new BusinessRuntimeState();
            public BusinessRuntimeState Smith = new BusinessRuntimeState();
            public Dictionary<int, int> PersonHousehold = new Dictionary<int, int>();
        }

        private World BuildWorld()
        {
            var w = new World();
            // Persons: P1 unemployed (H1), P5 spouse (H1), P6 child (H1),
            // P2 employed at farm (H2), P3 store manager (H3), P4 distant (H4).
            w.PersonHousehold[1] = 1; w.PersonHousehold[5] = 1; w.PersonHousehold[6] = 1;
            w.PersonHousehold[2] = 2; w.PersonHousehold[3] = 3; w.PersonHousehold[4] = 4;

            w.Ledgers.GetOrCreate(1).RecordInflow(400, 400, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "H1 opening reserves", "self");
            w.Ledgers.GetOrCreate(2).RecordInflow(400, 900, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "H2 opening reserves", "self");
            w.Ledgers.GetOrCreate(3).RecordInflow(400, 600, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "H3 opening reserves", "self");

            // Contact graph: households + one documented P3<->P1 witness link.
            w.Contacts.SeedHouseholdLinks(1, 1, new[] { 5, 6 });
            w.Contacts.SeedHouseholdLinks(2, 2, new int[0]);
            w.Contacts.SeedHouseholdLinks(3, 3, new int[0]);
            w.Contacts.AddLink(3, 1, ContactLinkKind.Witnessed, "P3 saw P1 seeking work at the market");
            w.Contacts.AddLink(1, 3, ContactLinkKind.Witnessed, "P1 spoke with P3 at the market");

            w.Store.BindBusinessIdentity("store-1");
            w.Store.AddCashCents(20000);
            w.Store.EmploymentRegistry = w.Employments;
            w.Store.HouseholdLedgers = w.Ledgers;
            w.Store.PersonHouseholdIdLookup = pid => w.PersonHousehold.TryGetValue(pid, out int h) ? h : -1;

            w.Smith.BindBusinessIdentity("smith-1");
            w.Smith.AddCashCents(8000);
            w.Smith.EmploymentRegistry = w.Employments;
            w.Smith.HouseholdLedgers = w.Ledgers;
            w.Smith.PersonHouseholdIdLookup = pid => w.PersonHousehold.TryGetValue(pid, out int h) ? h : -1;
            return w;
        }

        [Test]
        public void LaborToHousehold_FullTrace()
        {
            var w = BuildWorld();
            var diag = Diag();
            var h1 = w.Ledgers.GetOrCreate(1);

            // 1. Household reserves decline.
            Assert.IsNull(h1.RecordOutflow(400, 150, "flour and meal", "general store"));
            Assert.AreEqual(250, h1.GetBalanceCents());
            Assert.IsNull(h1.RecordOutflow(401, 130, "flour and meal", "general store"));
            Assert.AreEqual(120, h1.GetBalanceCents());
            Assert.IsNull(h1.RecordOutflow(402, 80, "flour and meal", "general store"));
            Assert.AreEqual(40, h1.GetBalanceCents());

            // 2. P1 needs income: urgent evaluation accepts a fair offer.
            var need = new JobDecisionInput
            {
                PersonId = 1, HouseholdId = 1, ReservesCents = 40, Dependents = 2,
                CurrentlyEmployed = false, CurrentWageCentsPerWeek = 0,
                OfferWageCentsPerWeek = 320, CommuteMinutesOneWay = 20,
                ConditionsScore01 = 0.7f, EmployerReliability01 = 0.6f,
                KnownAlternativeCount = 0, ReservesTrendCentsPerDay = -110, DayIndex = 402
            };
            Assert.AreEqual(JobDecisionKind.Accept, PersonJobDecisions.EvaluateOffer(need, diag).Kind);

            // 3. The store has real work pressure (queues, balks, open tasks).
            var demandInputs = new LaborDemandInputs
            {
                BusinessInstanceId = "store-1", DayIndex = 402,
                WaitingCustomers = 6, RecentBalkCount = 2,
                RequiresPersonPhaseMinutes = 60,
                ActiveEmployeeCount = 1, AvailableEmployeeMinutes = 240
            };
            demandInputs.OpenTaskMinutesByFamily["TendCustomer"] = 360;
            demandInputs.OpenTaskMinutesByFamily["StockShelves"] = 120;
            LaborDemandReport report = w.Demand.Assess(demandInputs, diag);
            Assert.AreEqual(540, report.TotalUnmetMinutes);
            Assert.IsTrue(report.CoverageGap);
            Assert.IsTrue(report.HiringPressure);

            // 4. The delegated manager seeks candidates (hire intent recorded).
            var brainInputs = new ManagerCycleInputs
            {
                BusinessInstanceId = "store-1", ManagerPersonId = 3,
                CashCents = w.Store.CurrentCashCents, DemandReport = report,
                DayIndex = 402, JudgmentSeed = 7, JudgmentQuality01 = 0.8f,
                PurchaseLimitCents = 5000, MaxBorrowingCents = 0
            };
            brainInputs.DelegatedScopes.Add("HireStaff");
            brainInputs.DelegatedScopes.Add("AssignTasks");
            ManagerDecisionResult hireCall = w.Brain.DailyCycle(brainInputs, diag);
            Assert.IsTrue(w.Brain.ActivityLog.Records.Count > 0);
            // Handbill effort costs real money, booked through the single writer.
            w.Store.AdjustCashCents(-150, true);
            Assert.AreEqual(19850, w.Store.CurrentCashCents);

            // 5. P1 legitimately learns of the position (documented link only).
            List<int> reachable = w.Contacts.ReachableCandidates("store-1", new[] { 3 });
            Assert.IsTrue(reachable.Contains(1));
            Assert.IsFalse(reachable.Contains(4)); // P4: no link, no awareness

            // 6. Employer evaluates known evidence: the reachable id is enough
            // to open an offer; hidden traits are never consulted (no API takes them).

            // 7. Terms negotiated: 300 offered -> 340 countered -> 320 accepted.
            EmploymentOffer offer = w.Negotiation.MakeOffer(
                "store-1", 1, 3, 300, "clerk: serve customers, stock shelves",
                "dawn to dusk, six days", 404, 14, "store-1 counter", 0,
                "none", "clerk duties", "good conduct", 7,
                "InPerson", "witnessed-by:P3", 402, diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(OfferState.Offered, offer.State);
            Assert.IsNull(w.Negotiation.RecordCounter(offer.OfferId, 340, 403, diag));
            Assert.AreEqual(OfferState.Countered, offer.State);
            Assert.IsNull(w.Negotiation.RecordCounter(offer.OfferId, 320, 403, diag));

            // 8. Agreement accepted: PendingStart until day 404, full terms kept.
            Assert.IsNull(w.Negotiation.AcceptCounterOffer(offer.OfferId, 3, 403, diag));
            Assert.IsNull(w.Negotiation.AcceptOffer(offer.OfferId, 403, w.Employments, diag));
            Assert.AreEqual(OfferState.Accepted, offer.State);
            Assert.IsTrue(w.Employments.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship rel));
            Assert.AreEqual(EmploymentLifecycleState.PendingStart, rel.LifecycleState);
            Assert.AreEqual(404, rel.StartDayIndex);
            Assert.AreEqual(320, rel.Compensation.AgreedWeeklyWageCents);

            // 9. P1 begins working on day 404; scheduler assigns real work.
            w.Negotiation.AdvanceDay(404, w.Employments, diag);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);
            var schedInputs = new SchedulerInputs { BusinessInstanceId = "store-1", DayIndex = 404 };
            schedInputs.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-tend-1", Family = "TendCustomer", MinutesRequired = 240, LocationId = "store-1" });
            schedInputs.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-stock-1", Family = "StockShelves", MinutesRequired = 120, LocationId = "store-1" });
            schedInputs.Workers.Add(new SchedulerAvailableWorker
                { PersonId = 1, AvailableMinutes = 480 });
            schedInputs.Workers[0].SkillEvidence["TendCustomer"] = 0.4f; // novice: descriptive only
            WorkAssignmentReport assignment = w.Scheduler.AssignDailyWork(schedInputs, diag);
            Assert.AreEqual(0, assignment.UnassignedTaskIds.Count);
            Assert.AreEqual(2, assignment.Assignments.Count);
            int committed = 0;
            foreach (WorkAssignment a in assignment.Assignments)
            {
                Assert.AreEqual(1, a.PersonId);
                committed += a.MinutesCommitted;
            }

            // 10. Person-time consumed: 360 of 480 minutes committed, none double-booked.
            Assert.AreEqual(360, committed);
            Assert.IsTrue(committed <= 480);

            // 11. Work contributes to a real productive outcome: a served sale.
            w.Store.AddCashCents(450); // till revenue from served customers
            Assert.AreEqual(20300, w.Store.CurrentCashCents);

            // 12. Employer pays actual wages: cash out, once, via the single path.
            int cashBefore = w.Store.CurrentCashCents;
            string settleResult = w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 57, 406, diag);
            Assert.AreEqual("settled", settleResult);
            Assert.AreEqual(cashBefore - 320, w.Store.CurrentCashCents);
            // Idempotency: the same week boundary again posts nothing.
            string repeat = w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 57, 406, diag);
            Assert.AreEqual("already-settled", repeat);
            Assert.AreEqual(cashBefore - 320, w.Store.CurrentCashCents);

            // 13-14. Employee-controlled funds increase; household gains access.
            Assert.AreEqual(360, h1.GetBalanceCents());

            // 15. Household purchases needed goods with the wage income.
            Assert.IsNull(h1.RecordOutflow(407, 90, "flour", "general store"));
            Assert.AreEqual(270, h1.GetBalanceCents());

            // 16. The manager reacts to operating conditions (post-payroll cash).
            var reactInputs = new ManagerCycleInputs
            {
                BusinessInstanceId = "store-1", ManagerPersonId = 3,
                CashCents = w.Store.CurrentCashCents, DemandReport = report,
                DayIndex = 407, JudgmentSeed = 11, JudgmentQuality01 = 0.8f,
                PurchaseLimitCents = 5000, MaxBorrowingCents = 0,
                ShortageObserved = true, ProposedProcurementCents = 5000
            };
            reactInputs.DelegatedScopes.Add("HireStaff");
            ManagerDecisionResult react = w.Brain.DailyCycle(reactInputs, diag);
            Assert.IsTrue(w.Brain.ActivityLog.Records.Count >= 2);
            ManagerActivityRecord last = w.Brain.ActivityLog.Records[w.Brain.ActivityLog.Records.Count - 1];
            Assert.AreEqual(3, last.ManagerPersonId);
            Assert.AreEqual("store-1", last.BusinessInstanceId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(last.Evidence));

            // 17. The economy continues without owner orders: second week runs.
            for (int d = 408; d <= 413; d++) w.Negotiation.AdvanceDay(d, w.Employments, diag);
            var week2Inputs = new SchedulerInputs { BusinessInstanceId = "store-1", DayIndex = 410 };
            week2Inputs.OpenWork.Add(new SchedulerOpenWorkItem
                { TaskId = "t-tend-2", Family = "TendCustomer", MinutesRequired = 200, LocationId = "store-1" });
            week2Inputs.Workers.Add(new SchedulerAvailableWorker { PersonId = 1, AvailableMinutes = 480 });
            WorkAssignmentReport week2 = w.Scheduler.AssignDailyWork(week2Inputs, diag);
            Assert.AreEqual(0, week2.UnassignedTaskIds.Count);
            int cashBefore2 = w.Store.CurrentCashCents;
            Assert.AreEqual("settled", w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 58, 413, diag));
            Assert.AreEqual(cashBefore2 - 320, w.Store.CurrentCashCents);
            Assert.AreEqual(EmploymentLifecycleState.Active, rel.LifecycleState);

            // Cash conservation over the whole slice:
            // 20000 - 150 (handbill) + 450 (sales) - 320 - 320 (two payrolls) = 19660.
            Assert.AreEqual(19660, w.Store.CurrentCashCents);
            // H1: 400 - 150 - 130 - 80 + 320 (week 57) - 90 + 320 (week 58) = 590.
            Assert.AreEqual(590, h1.GetBalanceCents());
        }

        [Test]
        public void Slice_RejectedOffer_Proved()
        {
            var w = BuildWorld();
            var diag = Diag();

            // P2 is employed at the farm on adequate pay with healthy reserves:
            // a low offer with a long commute is refused, not auto-accepted.
            var poor = new JobDecisionInput
            {
                PersonId = 2, HouseholdId = 2, ReservesCents = 900, Dependents = 1,
                CurrentlyEmployed = true, CurrentWageCentsPerWeek = 400,
                OfferWageCentsPerWeek = 250, CommuteMinutesOneWay = 120,
                ConditionsScore01 = 0.3f, EmployerReliability01 = 0.2f,
                KnownAlternativeCount = 1, ReservesTrendCentsPerDay = 5, DayIndex = 402
            };
            JobDecision decision = PersonJobDecisions.EvaluateOffer(poor, diag);
            Assert.AreEqual(JobDecisionKind.Refuse, decision.Kind);

            // The refusal is honored end-to-end: no relationship is created.
            w.Contacts.AddLink(3, 2, ContactLinkKind.Witnessed, "P3 knows P2 from the farm");
            w.Contacts.AddLink(2, 3, ContactLinkKind.Witnessed, "P2 knows P3");
            EmploymentOffer offer = w.Negotiation.MakeOffer(
                "store-1", 2, 3, 250, "clerk", "dawn to dusk", 404, 0, "store-1", 0,
                "none", "clerk duties", "good conduct", 7,
                "InPerson", "witnessed-by:P3", 402, diag);
            Assert.IsNull(w.Negotiation.RecordRefusal(offer.OfferId, "commute too long for the wage", 403, diag));
            Assert.AreEqual(OfferState.Refused, offer.State);
            Assert.IsFalse(w.Employments.TryGetById("emp-" + offer.OfferId, out _));
        }

        [Test]
        public void Slice_LegitimateStaffingShortage_Proved()
        {
            var w = BuildWorld();
            var diag = Diag();

            // Day 410: 8 customers arrive; only P1 (480 effective minutes) is on hand.
            var demandInputs = new LaborDemandInputs
            {
                BusinessInstanceId = "store-1", DayIndex = 410,
                WaitingCustomers = 8, RecentBalkCount = 3,
                ActiveEmployeeCount = 1, AvailableEmployeeMinutes = 480
            };
            demandInputs.OpenTaskMinutesByFamily["TendCustomer"] = 480; // 8 x 60
            demandInputs.OpenTaskMinutesByFamily["StockShelves"] = 240;
            LaborDemandReport report = w.Demand.Assess(demandInputs, diag);
            Assert.IsTrue(report.CoverageGap);
            Assert.IsTrue(report.HiringPressure);

            var schedInputs = new SchedulerInputs { BusinessInstanceId = "store-1", DayIndex = 410 };
            for (int i = 0; i < 8; i++)
                schedInputs.OpenWork.Add(new SchedulerOpenWorkItem
                    { TaskId = "t-cust-" + i, Family = "TendCustomer", MinutesRequired = 90, LocationId = "store-1" });
            schedInputs.Workers.Add(new SchedulerAvailableWorker { PersonId = 1, AvailableMinutes = 480 });
            WorkAssignmentReport result = w.Scheduler.AssignDailyWork(schedInputs, diag);

            // Demand (720 min) exceeds P1's budget (480 min): work stays queued,
            // NO worker is invented — every assignment names P1 or is marked
            // unassigned (PersonId = -1).
            Assert.IsTrue(result.UnassignedTaskIds.Count > 0);
            foreach (WorkAssignment a in result.Assignments)
                Assert.IsTrue(a.PersonId == 1 || a.PersonId == -1,
                    "no invented worker: assignment for " + a.TaskId + " names P" + a.PersonId);
        }

        [Test]
        public void Slice_SaveLoad_Idempotency()
        {
            var w = BuildWorld();
            var diag = Diag();

            // Mid-slice state: offer accepted, employment pending start.
            EmploymentOffer offer = w.Negotiation.MakeOffer(
                "store-1", 1, 3, 320, "clerk", "dawn to dusk", 404, 0, "store-1", 0,
                "none", "clerk duties", "good conduct", 7,
                "InPerson", "witnessed-by:P3", 402, diag);
            Assert.IsNull(w.Negotiation.AcceptOffer(offer.OfferId, 403, w.Employments, diag));

            var instructions = new StandingInstructionRegister();
            var locked = new StandingInstruction
            {
                InstructionId = "own-max-borrow", BusinessInstanceId = "store-1",
                Kind = InstructionKind.OwnerLocked, Objective = "never borrow",
                MaxBorrowingCents = 0, CreatedDayIndex = 400
            };
            locked.ProhibitedActs.Add("Borrow");
            instructions.Register(locked, diag);

            // Settle one payroll week so the marker exists.
            w.Negotiation.AdvanceDay(404, w.Employments, diag);
            Assert.AreEqual("settled", w.Settlement.SettleWeeklyPayroll(w.Store, w.Employments, 57, 406, diag));
            int cashAfterPayroll = w.Store.CurrentCashCents;

            // Capture and reload into fresh services.
            var contacts2 = new ContactGraphService();
            contacts2.LoadFromSaveDto(w.Contacts.CaptureSaveDto());
            var negotiation2 = new EmploymentNegotiationService((biz, person, act) => true);
            negotiation2.LoadFromSaveDto(w.Negotiation.CaptureSaveDto());
            var settlement2 = new WageSettlementService();
            settlement2.LoadFromSaveDto(w.Settlement.CaptureSaveDto());
            var instructions2 = new StandingInstructionRegister();
            instructions2.LoadFromSaveDto(instructions.CaptureSaveDto());

            // No duplicate hire: the accepted offer stays terminal on reload —
            // re-accepting is refused and the registry keeps exactly one record.
            string reaccept = negotiation2.AcceptOffer(offer.OfferId, 406, w.Employments, diag);
            Assert.IsNotNull(reaccept);
            Assert.IsTrue(w.Employments.TryGetById("emp-" + offer.OfferId, out EmploymentRelationship reloaded));
            Assert.AreEqual(EmploymentLifecycleState.Active, reloaded.LifecycleState); // Active at capture time
            // No wage double-post: the settled-week marker survives the load.
            string repeat = settlement2.SettleWeeklyPayroll(w.Store, w.Employments, 57, 406, diag);
            Assert.AreEqual("already-settled", repeat);
            Assert.AreEqual(cashAfterPayroll, w.Store.CurrentCashCents);
            // Owner restrictions survive: the OwnerLocked instruction is intact.
            Assert.IsTrue(instructions2.IsActProhibited("store-1", "Borrow"));
            // No person or business is recreated: registries keep their counts.
            Assert.IsTrue(w.Employments.TryGetById("emp-" + offer.OfferId, out _));
        }
    }
}
