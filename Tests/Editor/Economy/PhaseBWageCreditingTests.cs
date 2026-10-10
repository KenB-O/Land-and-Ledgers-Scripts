using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Phase B (Real People): household cash unification. The household ledger
    /// is the single cash truth (Canon 13.2/13.4): payroll credits worker
    /// households through the employment registry (business cash out ==
    /// household ledger in), fiat top-ups are rejected by the ledger, and
    /// legacy spendingMoneyCents balances migrate deterministically (OQ-8).
    /// </summary>
    [TestFixture]
    public sealed class PhaseBWageCreditingTests
    {
        private static EmploymentRelationship NewEmployment(string businessId, int personId, int weeklyWageCents)
        {
            return new EmploymentRelationship
            {
                Id = $"phaseb-emp-{personId}",
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                RoleDisplayName = "Hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            };
        }

        private static BusinessRuntimeState NewBusinessWithPayroll(
            string businessId,
            EmploymentRelationshipRegistry employments,
            HouseholdLedgerRegistry ledgers,
            System.Func<int, int> householdLookup,
            int startingCashCents)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(businessId);
            runtime.EmploymentRegistry = employments;
            runtime.HouseholdLedgers = ledgers;
            runtime.PersonHouseholdIdLookup = householdLookup;
            runtime.PayrollDayIndex = 42;
            runtime.SetCurrentCashCents(startingCashCents);
            return runtime;
        }

        [Test]
        public void Payroll_CreditsWorkerHouseholdLedger_ConservationHolds()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-1", 5, 800));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusinessWithPayroll(
                "biz-1", employments, ledgers, personId => personId == 5 ? 11 : -1, 10000);

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, runtime.CurrentCashCents, "Business cash is debited the agreed wage.");
            Assert.AreEqual(800, runtime.LastWeeklyPayrollCents);
            HouseholdLedger householdLedger = ledgers.Get(11);
            Assert.NotNull(householdLedger, "The worker's household ledger receives the wage.");
            Assert.AreEqual(800, householdLedger.GetBalanceCents(),
                "Conservation: business cash out (800c) == household ledger in (800c).");
            Assert.AreEqual(1, householdLedger.Entries.Count);
            HouseholdLedgerEntry entry = householdLedger.Entries[0];
            Assert.AreEqual(HouseholdIncomeSource.WageEmployment, entry.Source);
            Assert.AreEqual("phaseb-emp-5", entry.SourceReference,
                "The employment id is the provenance reference.");
            Assert.AreEqual(42, entry.DayIndex);
            Assert.IsEmpty(runtime.LastWeeklyPayrollDiagnostics);
        }

        [Test]
        public void Payroll_MultipleWorkers_EachHouseholdCreditedSeparately()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-2", 5, 800));
            employments.Register(NewEmployment("biz-2", 6, 1200));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusinessWithPayroll(
                "biz-2", employments, ledgers,
                personId => personId == 5 ? 11 : personId == 6 ? 12 : -1,
                10000);

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(8000, runtime.CurrentCashCents);
            Assert.AreEqual(800, ledgers.Get(11).GetBalanceCents());
            Assert.AreEqual(1200, ledgers.Get(12).GetBalanceCents());
            // No shared wallet: each worker's household holds only its own wage.
            Assert.AreEqual(1, ledgers.Get(11).Entries.Count);
            Assert.AreEqual(1, ledgers.Get(12).Entries.Count);
        }

        [Test]
        public void Payroll_WorkerWithoutHousehold_LogsGap_DeductionStands()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-3", 7, 800));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusinessWithPayroll(
                "biz-3", employments, ledgers, _ => -1, 10000);

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(9200, runtime.CurrentCashCents,
                "The business deduction stands even when the worker household is unknown.");
            Assert.IsNotEmpty(runtime.LastWeeklyPayrollDiagnostics,
                "The crediting gap is logged, never silently kept.");
            Assert.IsNull(ledgers.Get(99), "No phantom household ledger is invented.");
        }

        [Test]
        public void Payroll_InsufficientBusinessCash_SuspendsAndCreditsNobody()
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(NewEmployment("biz-4", 5, 800));
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewBusinessWithPayroll(
                "biz-4", employments, ledgers, personId => 11, 100);

            runtime.ResolveWeeklyPayroll();

            Assert.AreEqual(100, runtime.CurrentCashCents, "Unpaid wages are not deducted.");
            Assert.IsNull(ledgers.Get(11), "No wage is credited when the business cannot pay.");
            Assert.IsTrue(employments.TryGetById("phaseb-emp-5", out EmploymentRelationship employment));
            Assert.AreEqual(EmploymentLifecycleState.Suspended, employment.LifecycleState,
                "The employment suspends on employer payment default.");
            Assert.AreEqual(EmploymentSuspensionReason.EmployerPaymentDefault, employment.SuspensionReason);
        }

        [Test]
        public void Ledger_RejectsFiatTopUp_WithoutProvenance()
        {
            var ledger = new HouseholdLedger(21);

            string unspecified = ledger.RecordInflow(1, 500, HouseholdIncomeSource.Unspecified, "ref", "reason", "cp");
            string noReason = ledger.RecordInflow(1, 500, HouseholdIncomeSource.OtherDocumented, "ref", "", "cp");

            Assert.NotNull(unspecified, "Sourceless inflows are rejected (Canon 13.2).");
            Assert.NotNull(noReason, "Reasonless inflows are rejected (Canon 13.2).");
            Assert.AreEqual(0, ledger.GetBalanceCents(), "Rejected inflows book nothing.");
            Assert.IsEmpty(ledger.Entries);
        }

        [Test]
        public void LegacyMigration_MovesBalanceOnce_Idempotent()
        {
            var households = new List<HouseholdState>
            {
                new HouseholdState { id = 31, spendingMoneyCents = 1500 },
                new HouseholdState { id = 32, spendingMoneyCents = 0 },
            };
            var ledgers = new HouseholdLedgerRegistry();
            var diagnostics = new List<string>();

            HouseholdLegacyCashMigrator.MigrateAll(households, ledgers, 10, diagnostics);

            Assert.AreEqual(0, households[0].spendingMoneyCents, "The legacy field is zeroed after migration.");
            Assert.AreEqual(1500, ledgers.Get(31).GetBalanceCents(),
                "The exact legacy balance lands in the ledger exactly once.");
            HouseholdLedgerEntry entry = ledgers.Get(31).Entries[0];
            Assert.AreEqual(HouseholdLegacyCashMigrator.MigrationSourceReference, entry.SourceReference);
            Assert.AreEqual(10, entry.DayIndex);

            // Second pass (or a reloaded migrated save) books nothing.
            HouseholdLegacyCashMigrator.MigrateAll(households, ledgers, 11, diagnostics);
            Assert.AreEqual(1500, ledgers.Get(31).GetBalanceCents());
            Assert.AreEqual(1, ledgers.Get(31).Entries.Count, "No duplicate migration entry.");
        }

        [Test]
        public void LegacyMigration_NegativeBalance_QuarantinedNotBooked()
        {
            var households = new List<HouseholdState>
            {
                new HouseholdState { id = 33, spendingMoneyCents = -200 },
            };
            var ledgers = new HouseholdLedgerRegistry();

            HouseholdLegacyCashMigrator.MigrateAll(households, ledgers, 10, new List<string>());

            Assert.AreEqual(0, households[0].spendingMoneyCents, "Negative balance reset, not carried.");
            Assert.IsNull(ledgers.Get(33), "Ambiguous balances are quarantined, never booked.");
        }

        [Test]
        public void UpgradeIncome_FlowsThroughLedger_WithProvenance()
        {
            var household = new HouseholdState { id = 41 };
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.ChickenCoop, 0));
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger ledger = ledgers.GetOrCreate(41);

            // ChickenCoop: 50c weekly income, 25c weekly upkeep.
            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household, ledger, 7);

            Assert.AreEqual(25, ledger.GetBalanceCents(),
                "Upgrade net (50c income - 25c upkeep) lands in the ledger, not the retired wallet.");
            Assert.AreEqual(0, household.spendingMoneyCents, "The retired wallet is untouched.");
            Assert.AreEqual(2, ledger.Entries.Count, "Income and upkeep are separate auditable entries.");
            Assert.AreEqual(HouseholdIncomeSource.OtherDocumented, ledger.Entries[0].Source);
            Assert.IsFalse(string.IsNullOrWhiteSpace(ledger.Entries[0].Reason),
                "Upgrade income carries its economic reason (Canon 13.2).");
            Assert.AreEqual(50, household.lastWeeklyUpgradeIncomeCents);
            Assert.AreEqual(25, household.lastWeeklyUpgradeUpkeepCents);
        }

        [Test]
        public void UpgradeUpkeep_UnpayableWithEmptyLedger_LeavesNeedUnmet()
        {
            var household = new HouseholdState { id = 42 };
            household.upgrades.Add(HouseholdUpgradeState.Built(HouseholdUpgradeKind.KitchenGarden, 0));
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger ledger = ledgers.GetOrCreate(42);

            // KitchenGarden: 0c income, 15c upkeep, empty ledger.
            HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household, ledger, 7);

            Assert.AreEqual(0, ledger.GetBalanceCents(), "No negative balance, no fiat.");
            Assert.AreEqual(0, household.lastWeeklyUpgradeUpkeepCents,
                "Unpaid upkeep is reported unpaid, not booked.");
            Assert.IsNotEmpty(ledger.Diagnostics, "The rejected upkeep is logged.");
        }
    }
}
