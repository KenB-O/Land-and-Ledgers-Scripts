using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Worker D: autonomous management (Phase G). The manager acts only within
    /// delegated scopes and standing-instruction bounds; OwnerLocked refuses;
    /// DoOnce fires exactly once; cash constraints delay; every significant
    /// act carries decision provenance. The 14-day unattended run proves a
    /// business can operate with minimal owner involvement - survival OR
    /// honest failure, never passive income.
    /// </summary>
    [TestFixture]
    public sealed class ManagerBrainTests
    {
        private static ManagerCycleInputs BaseInputs()
        {
            return new ManagerCycleInputs
            {
                BusinessInstanceId = "store-1",
                ManagerPersonId = 3,
                CashCents = 10000,
                DemandReport = new LaborDemandReport
                {
                    BusinessInstanceId = "store-1",
                    DayIndex = 410,
                    TotalUnmetMinutes = 900,
                    CoverageGap = true,
                    HiringPressure = true,
                    EvidenceSummary = "queues growing, 2 balks"
                },
                DelegatedScopes = new List<string>
                {
                    ManagerScopeNames.Staffing,
                    ManagerScopeNames.Scheduling,
                    ManagerScopeNames.Purchasing
                },
                DayIndex = 410,
                JudgmentSeed = 42,
                JudgmentQuality01 = 1f,
                PurchaseLimitCents = 2000,
                MaxBorrowingCents = 10000
            };
        }

        /// <summary>Inputs with no hiring pressure / coverage gap, so a single
        /// candidate act can be exercised in isolation.</summary>
        private static ManagerCycleInputs QuietInputs()
        {
            ManagerCycleInputs inputs = BaseInputs();
            inputs.DemandReport.HiringPressure = false;
            inputs.DemandReport.CoverageGap = false;
            inputs.DemandReport.TotalUnmetMinutes = 0;
            inputs.DemandReport.EvidenceSummary = "steady trade";
            return inputs;
        }

        private static StandingInstruction DoOnceProcure()
        {
            return new StandingInstruction
            {
                InstructionId = "do-1",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.DoOnce,
                TargetActKind = ManagerActKinds.Procure,
                Objective = "place one flour order from the usual supplier",
                MaxSpendCentsPerAct = 500,
                CreatedByPersonId = 7,
                CreatedDayIndex = 409
            };
        }

        [Test]
        public void DoOnce_ConsumedAfterOneAction()
        {
            var brain = new ManagerBrain();
            StandingInstruction instruction = DoOnceProcure();
            var inputs = QuietInputs();
            inputs.ShortageObserved = true;
            inputs.ProposedProcurementCents = 400;
            inputs.Instructions = new List<StandingInstruction> { instruction };

            ManagerDecisionResult first = brain.DailyCycle(inputs, new List<string>());
            Assert.IsTrue(first.Acted, "DoOnce act executes");
            Assert.AreEqual(ManagerActKinds.Procure, first.ActionKind);
            Assert.IsTrue(instruction.Consumed, "DoOnce consumed at execution");
            Assert.IsTrue(first.Outcome.Contains("DoOnce"), "consumption recorded in outcome");

            // A second identical cycle may procure again under routine scope,
            // but it must NOT claim the DoOnce a second time.
            ManagerDecisionResult second = brain.DailyCycle(inputs, new List<string>());
            Assert.IsTrue(second.Outcome.IndexOf("DoOnce") < 0, "DoOnce fires exactly once");

            Assert.IsNotNull(brain.Instructions.ConsumeDoOnce("do-1", 411, new List<string>()),
                "consuming twice is refused");
        }

        [Test]
        public void ManagerJudgment_ActsWithinBounds()
        {
            var brain = new ManagerBrain();
            var inputs = BaseInputs();
            inputs.Instructions = new List<StandingInstruction>
            {
                new StandingInstruction
                {
                    InstructionId = "mj-1",
                    BusinessInstanceId = "store-1",
                    Kind = InstructionKind.ManagerJudgment,
                    TargetActKind = ManagerActKinds.OpenRecruitment,
                    Objective = "keep the store staffed as queues grow",
                    MaxSpendCentsPerAct = -1,
                    CreatedByPersonId = 7,
                    CreatedDayIndex = 405
                }
            };

            ManagerDecisionResult result = brain.DailyCycle(inputs, new List<string>());

            Assert.IsTrue(result.Acted);
            Assert.AreEqual(ManagerActKinds.OpenRecruitment, result.ActionKind);
            Assert.IsTrue(result.AuthorityUsed.Contains(ManagerScopeNames.Staffing), "scope named in authority");
            Assert.IsTrue(result.ResultingReference.StartsWith("intent:recruitment-effort:"),
                "cash moves stay with the integrator - intent only");
            Assert.Greater(result.TimeCostMinutes, 0, "manager time cost noted");
        }

        [Test]
        public void OwnerLocked_RefusesAndEscalates_NeverExecutes()
        {
            var brain = new ManagerBrain();
            var inputs = QuietInputs();
            inputs.ShortageObserved = true;
            inputs.ProposedProcurementCents = 400;
            inputs.Instructions = new List<StandingInstruction>
            {
                new StandingInstruction
                {
                    InstructionId = "ol-1",
                    BusinessInstanceId = "store-1",
                    Kind = InstructionKind.OwnerLocked,
                    TargetActKind = ManagerActKinds.Procure,
                    Objective = "no procurement without the owner's sign-off this month",
                    CreatedByPersonId = 7,
                    CreatedDayIndex = 408
                }
            };

            ManagerDecisionResult result = brain.DailyCycle(inputs, new List<string>());

            Assert.IsTrue(result.Escalated, "prohibition escalates");
            bool procured = false;
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                if (record.ResultingReference.StartsWith("intent:procurement:"))
                {
                    procured = true;
                }
            }

            Assert.IsFalse(procured, "OwnerLocked act NEVER executes");
            Assert.AreEqual(10000, inputs.CashCents, "no cash touched");
            bool refused = false;
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                if (record.Decision.Contains("Refuse") && record.Decision.Contains(ManagerActKinds.Procure))
                {
                    refused = true;
                }
            }

            Assert.IsTrue(refused, "refusal recorded with provenance");
        }

        [Test]
        public void BorrowingCap_Respected()
        {
            var brain = new ManagerBrain();

            // Above the owner's max borrowing permission -> escalate.
            var over = BaseInputs();
            over.ProposedBorrowingCents = 8000; // owner max 10000? set lower:
            over.MaxBorrowingCents = 5000;
            over.DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling };
            ManagerDecisionResult r1 = brain.DailyCycle(over, new List<string>());
            Assert.IsTrue(r1.Escalated, "over owner max borrowing escalates");
            Assert.IsTrue(r1.EscalationReason.Contains("max borrowing"), "reason names the owner's permission");

            // Below the owner max but at/above the major threshold -> still escalates.
            var major = BaseInputs();
            major.ProposedBorrowingCents = 60000;
            major.MaxBorrowingCents = 100000;
            major.DelegatedScopes = new List<string> { ManagerScopeNames.CashHandling };
            ManagerDecisionResult r2 = brain.DailyCycle(major, new List<string>());
            Assert.IsTrue(r2.Escalated, "major borrowing is owner-reserved");
            Assert.IsTrue(r2.EscalationReason.Contains("major borrowing"));

            // Nothing was ever executed: every borrow-related record is an
            // escalation, never an execution.
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                bool mentionsBorrow = record.Decision.Contains(ManagerActKinds.Borrow)
                    || (record.ResultingReference != null && record.ResultingReference.Contains("borrow"));
                if (mentionsBorrow)
                {
                    Assert.IsTrue(record.Outcome.Contains("escalated"),
                        "borrowing is never executed by the manager, only escalated: " + record.Outcome);
                }
            }
        }

        [Test]
        public void ProhibitedSale_Refused()
        {
            var brain = new ManagerBrain();
            var inputs = QuietInputs();
            inputs.ProposedAssetSaleCents = 20000;
            inputs.DelegatedScopes = new List<string>
            {
                ManagerScopeNames.Staffing, ManagerScopeNames.Scheduling, ManagerScopeNames.Purchasing,
                ManagerScopeNames.Maintenance, ManagerScopeNames.CashHandling, ManagerScopeNames.Inventory
            };

            ManagerDecisionResult result = brain.DailyCycle(inputs, new List<string>());

            Assert.IsTrue(result.Escalated, "asset sale escalates no matter how broad the grant");
            Assert.IsTrue(result.EscalationReason.Contains("owner-reserved"));
            Assert.IsFalse(result.Acted, "never executed");
        }

        [Test]
        public void DecisionProvenance_Logged()
        {
            var brain = new ManagerBrain();
            ManagerDecisionResult result = brain.DailyCycle(BaseInputs(), new List<string>());

            Assert.IsTrue(result.Acted);
            Assert.GreaterOrEqual(brain.ActivityLog.Count, 1);
            ManagerActivityRecord record = brain.ActivityLog.Records[brain.ActivityLog.Count - 1];
            Assert.AreEqual(3, record.ManagerPersonId, "manager identity");
            Assert.AreEqual("store-1", record.BusinessInstanceId, "business");
            Assert.AreEqual(410, record.DayIndex);
            Assert.IsNotEmpty(record.ObservedConditions, "observed conditions");
            Assert.IsNotEmpty(record.Decision, "decision");
            Assert.IsNotEmpty(record.Evidence, "evidence");
            Assert.IsNotEmpty(record.AuthorityUsed, "authority used");
            Assert.IsNotEmpty(record.ResultingReference, "resulting reference");
            Assert.IsNotEmpty(record.Outcome, "outcome");
            Assert.Greater(record.TimeCostMinutes, 0, "time cost");
        }

        [Test]
        public void CashConstraints_DelayProcurement()
        {
            var brain = new ManagerBrain();
            var inputs = QuietInputs();
            inputs.CashCents = 100;
            inputs.ShortageObserved = true;
            inputs.ProposedProcurementCents = 1500; // within the 2000c cap, but cash is short

            ManagerDecisionResult result = brain.DailyCycle(inputs, new List<string>());

            Assert.IsFalse(result.Acted, "cannot spend cash the business lacks");
            bool delayed = false;
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                if (record.Outcome.Contains("insufficient cash"))
                {
                    delayed = true;
                }
            }

            Assert.IsTrue(delayed, "delay recorded with reason");
        }

        [Test]
        public void UnattendedRun_14Days_StaffsRespondsProcuresPaysEscalates()
        {
            // A store with one clerk, a delegated manager, and standing
            // instructions runs 14 days with no owner orders.
            var registry = new EmploymentRelationshipRegistry();
            var ledgers = new HouseholdLedgerRegistry();
            var business = new BusinessRuntimeState();
            business.BindBusinessIdentity("store-1");
            business.SetCurrentCashCents(12000);
            business.EmploymentRegistry = registry;
            business.HouseholdLedgers = ledgers;
            business.PersonHouseholdIdLookup = pid => pid == 1 ? 101 : -1;

            var employment = new EmploymentRelationship
            {
                Id = "emp-1",
                EmployeePersonId = 1,
                EmployerBusinessId = "store-1",
                RoleDisplayName = "Clerk",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(3000, "agreed"),
                StartDayIndex = 400
            };
            Assert.IsNull(registry.Register(employment));

            var wages = new WageSettlementService();
            var brain = new ManagerBrain();
            var flourOrder = new StandingInstruction
            {
                InstructionId = "do-1",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.DoOnce,
                TargetActKind = ManagerActKinds.Procure,
                Objective = "place one flour order from the usual supplier",
                MaxSpendCentsPerAct = 1500,
                CreatedByPersonId = 7,
                CreatedDayIndex = 409
            };
            var keepOperating = new StandingInstruction
            {
                InstructionId = "mj-keep",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.ManagerJudgment,
                Objective = "keep the store operating: staff, stock, and serve",
                MaxSpendCentsPerAct = -1,
                CreatedByPersonId = 7,
                CreatedDayIndex = 400
            };
            var neverSell = new StandingInstruction
            {
                InstructionId = "ol-sell",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.OwnerLocked,
                TargetActKind = ManagerActKinds.SellAsset,
                Objective = "never sell the store or its fixtures",
                CreatedByPersonId = 7,
                CreatedDayIndex = 400
            };

            int recruitmentIntents = 0;
            int procureIntents = 0;
            int escalations = 0;
            int payrolls = 0;

            for (int day = 410; day <= 423; day++)
            {
                var inputs = BaseInputs();
                inputs.DayIndex = day;
                inputs.CashCents = business.CurrentCashCents;
                inputs.Instructions = new List<StandingInstruction> { flourOrder, keepOperating, neverSell };
                inputs.DemandReport.DayIndex = day;

                // Days 410-412: real hiring pressure.
                inputs.DemandReport.HiringPressure = day <= 412;
                inputs.DemandReport.CoverageGap = day <= 412;
                inputs.DemandReport.TotalUnmetMinutes = day <= 412 ? 900 : 120;

                // Days 413 and 415: shortages the manager may procure against.
                inputs.ShortageObserved = day == 413 || day == 415;
                inputs.ProposedProcurementCents = 1200;

                // Day 418: borrowing over the owner's max -> must escalate.
                inputs.ProposedBorrowingCents = day == 418 ? 8000 : 0;

                // Day 420: asset sale proposed -> OwnerLocked refuses.
                inputs.ProposedAssetSaleCents = day == 420 ? 20000 : 0;

                var diag = new List<string>();
                ManagerDecisionResult result = brain.DailyCycle(inputs, diag);

                if (result.Acted && result.ActionKind == ManagerActKinds.OpenRecruitment) recruitmentIntents++;
                if (result.Acted && result.ActionKind == ManagerActKinds.Procure) procureIntents++;
                if (result.Escalated) escalations++;

                // Real sales revenue every day - the only cash inflow (no passive income).
                business.AddWeeklyLocalTransferRevenue(800, $"day {day} sales");

                // Weekly payroll on days 416 and 423.
                if (day == 416 || day == 423)
                {
                    business.PayrollDayIndex = day;
                    string outcome = wages.SettleWeeklyPayroll(business, registry, day == 416 ? 59 : 60, day, diag);
                    Assert.IsTrue(outcome == "settled" || outcome == "settled-arrears", "payroll settles: " + outcome);
                    payrolls++;
                }
            }

            Assert.GreaterOrEqual(recruitmentIntents, 1, "manager staffs: recruitment intent opened");
            Assert.AreEqual(2, procureIntents, "manager procures: two procurement intents (DoOnce + routine)");
            Assert.IsTrue(flourOrder.Consumed, "DoOnce consumed exactly once across 14 days");
            Assert.GreaterOrEqual(escalations, 2, "manager escalates: borrowing over max + prohibited sale");
            Assert.AreEqual(2, payrolls, "manager pays: two weekly payrolls");

            // Cash conservation: 12000 + 14*800 sales - 2*3000 payroll = 17200.
            // Procurement intents move no cash (integrator-owned) - none leaked.
            Assert.AreEqual(17200, business.CurrentCashCents, "cash conserved - no passive income, no leaks");
            Assert.AreEqual(6000, ledgers.GetOrCreate(101).GetBalanceCents(), "worker household received both payrolls");

            // Provenance: every record names manager, evidence, authority, outcome.
            Assert.GreaterOrEqual(brain.ActivityLog.Count, 6, "significant actions logged, no chatter");
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                Assert.IsNotEmpty(record.Evidence, "evidence on every record");
                Assert.IsNotEmpty(record.AuthorityUsed, "authority on every record");
                Assert.Greater(record.TimeCostMinutes, 0, "time cost on every record");
            }

            // OwnerLocked survived the whole run: the sale never executed.
            foreach (ManagerActivityRecord record in brain.ActivityLog.Records)
            {
                Assert.IsFalse(record.ResultingReference.StartsWith("intent:sell"),
                    "prohibited sale never executed");
            }
        }

        [Test]
        public void BrainSaveLoad_RoundTripsLogAndInstructions()
        {
            var brain = new ManagerBrain();
            brain.DailyCycle(BaseInputs(), new List<string>());

            var restored = new ManagerBrain();
            restored.LoadFromSaveDto(brain.CaptureSaveDto());

            Assert.AreEqual(brain.ActivityLog.Count, restored.ActivityLog.Count, "log survives");
            // Owner restrictions are never erased on load.
            var inputs = BaseInputs();
            inputs.ShortageObserved = true;
            inputs.ProposedProcurementCents = 400;
            var locked = new StandingInstruction
            {
                InstructionId = "ol-9",
                BusinessInstanceId = "store-1",
                Kind = InstructionKind.OwnerLocked,
                TargetActKind = ManagerActKinds.Procure,
                Objective = "locked",
                CreatedByPersonId = 7,
                CreatedDayIndex = 400
            };
            brain.Instructions.Register(locked, new List<string>());
            var restored2 = new ManagerBrain();
            restored2.LoadFromSaveDto(brain.CaptureSaveDto());
            Assert.IsTrue(restored2.Instructions.IsActProhibited("store-1", ManagerActKinds.Procure),
                "OwnerLocked survives save/load");
        }
    }
}
