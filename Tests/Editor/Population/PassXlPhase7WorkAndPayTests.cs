using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Labor;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass XL phase 7: work and pay inside the slice. Roslyn only, never
    /// Unity. All wages, draws, prices, and freight are caller-supplied
    /// test parameters, never canon rates. The employee wage tests use
    /// a clearly labelled NON-SLICE test employee and business: the
    /// six-person slice has no non-founder employees, and its founders
    /// are operators, not wage employees (TECH 4.1).
    /// </summary>
    [TestFixture]
    public sealed class PassXlPhase7WorkAndPayTests
    {
        private static SlicePayrollSetup EmployeeSetup(int openingCashCents)
        {
            var personHousehold = new Dictionary<int, int> { { 99, 99 }, { 98, 98 } };
            return SliceWorkAndPay.CreateBusiness("general-store-test", openingCashCents, personHousehold);
        }

        private static EmploymentRelationship RegisterTestEmployee(SlicePayrollSetup setup, string employmentId, int personId, int weeklyWageCents)
        {
            string problem = SliceWorkAndPay.RegisterEmployee(
                setup, employmentId, personId, "general-store-test", weeklyWageCents, 0, false);
            Assert.IsNull(problem);
            Assert.IsTrue(setup.Employments.TryGetById(employmentId, out EmploymentRelationship relationship));
            return relationship;
        }

        [Test]
        public void WorkAndPay_EmployeeWeeklyPayroll_PaysOnce_AndSecondSettleMovesNothing()
        {
            var setup = EmployeeSetup(5000);
            RegisterTestEmployee(setup, "emp-1", 99, 320);
            SliceWorkAndPay.SetAttendance(setup, "emp-1", 0, true);
            var diag = new List<string>();

            string result = SliceWorkAndPay.SettleWeek(setup, 0, 6, diag);
            Assert.AreEqual("settled", result);
            Assert.AreEqual(4680, setup.Business.CurrentCashCents);
            Assert.AreEqual(320, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
            Assert.AreEqual(1, setup.Settlement.Events.Count);
            Assert.AreEqual(1, setup.Settlement.Events[0].Entries.Count);
            Assert.IsTrue(setup.Settlement.Events[0].Entries[0].Paid);
            Assert.AreEqual(320, setup.Settlement.Events[0].TotalPaidCents);

            string repeat = SliceWorkAndPay.SettleWeek(setup, 0, 6, diag);
            Assert.AreEqual("already-settled", repeat);
            Assert.AreEqual(4680, setup.Business.CurrentCashCents);
            Assert.AreEqual(320, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
            Assert.AreEqual(1, setup.Settlement.Events.Count);
        }

        [Test]
        public void WorkAndPay_NoAttendance_EarnsNothing_AndAccruesNothing()
        {
            var setup = EmployeeSetup(5000);
            RegisterTestEmployee(setup, "emp-1", 99, 320);
            SliceWorkAndPay.SetAttendance(setup, "emp-1", 0, true);
            var diag = new List<string>();
            Assert.AreEqual("settled", SliceWorkAndPay.SettleWeek(setup, 0, 6, diag));

            SliceWorkAndPay.SetAttendance(setup, "emp-1", 1, false);
            Assert.AreEqual("settled", SliceWorkAndPay.SettleWeek(setup, 1, 13, diag));
            Assert.AreEqual(4680, setup.Business.CurrentCashCents);
            Assert.AreEqual(320, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
            Assert.AreEqual(0, setup.Business.AccruedLiabilityCents);
            Assert.IsTrue(setup.Employments.TryGetById("emp-1", out EmploymentRelationship relationship));
            Assert.AreEqual(EmploymentLifecycleState.Active, relationship.LifecycleState);
        }

        [Test]
        public void WorkAndPay_InsufficientCash_AccruesLiability_Suspends_AndPaysNothing()
        {
            var setup = EmployeeSetup(200);
            RegisterTestEmployee(setup, "emp-1", 99, 320);
            SliceWorkAndPay.SetAttendance(setup, "emp-1", 0, true);
            var diag = new List<string>();

            string result = SliceWorkAndPay.SettleWeek(setup, 0, 6, diag);
            Assert.AreEqual("settled-arrears", result);
            Assert.AreEqual(200, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, setup.Ledgers.GetOrCreate(99).GetBalanceCents());
            Assert.AreEqual(320, setup.Business.AccruedLiabilityCents);
            Assert.IsTrue(setup.Employments.TryGetById("emp-1", out EmploymentRelationship relationship));
            Assert.AreEqual(EmploymentLifecycleState.Suspended, relationship.LifecycleState);
            Assert.AreEqual(EmploymentSuspensionReason.EmployerPaymentDefault, relationship.SuspensionReason);
        }

        [Test]
        public void WorkAndPay_PartialPeriod_PaysProratedOnce_AndRepeatIsRefused()
        {
            var setup = EmployeeSetup(1000);
            EmploymentRelationship relationship = RegisterTestEmployee(setup, "emp-1", 98, 400);
            int prorated = WageSettlementService.ProrateWeeklyWage(400, 2, 7);
            Assert.AreEqual(115, prorated);
            var diag = new List<string>();

            string result = SliceWorkAndPay.SettlePartial(setup, relationship, prorated, "week-0-partial", 2, diag);
            Assert.AreEqual("paid", result);
            Assert.AreEqual(885, setup.Business.CurrentCashCents);
            Assert.AreEqual(115, setup.Ledgers.GetOrCreate(98).GetBalanceCents());

            string repeat = SliceWorkAndPay.SettlePartial(setup, relationship, prorated, "week-0-partial", 2, diag);
            Assert.AreEqual("refused", repeat);
            Assert.AreEqual(885, setup.Business.CurrentCashCents);
            Assert.AreEqual(115, setup.Ledgers.GetOrCreate(98).GetBalanceCents());
        }

        [Test]
        public void WorkAndPay_PartialPeriod_UnknownOrWrongBusiness_IsRefused_AndMovesNothing()
        {
            var setup = EmployeeSetup(1000);
            var diag = new List<string>();

            Assert.AreEqual("refused-no-employment",
                SliceWorkAndPay.SettlePartial(setup, null, 100, "week-0-partial", 2, diag));

            var wrongBusiness = new EmploymentRelationship
            {
                Id = "emp-other",
                EmployeePersonId = 98,
                EmployerBusinessId = "some-other-business",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(400, "test"),
            };
            Assert.AreEqual("refused-wrong-business",
                SliceWorkAndPay.SettlePartial(setup, wrongBusiness, 100, "week-0-partial", 2, diag));
            Assert.AreEqual(1000, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, setup.Ledgers.GetOrCreate(98).GetBalanceCents());
        }

        [Test]
        public void WorkAndPay_OffMapOrDeadWorker_IsRefused_AndMovesNothing()
        {
            var setup = EmployeeSetup(1000);
            EmploymentRelationship relationship = RegisterTestEmployee(setup, "emp-1", 98, 400);
            setup.Business.PersonUnavailableForPayrollLookup = pid => pid == 98;
            var diag = new List<string>();

            string result = SliceWorkAndPay.SettlePartial(setup, relationship, 100, "week-0-partial", 2, diag);
            Assert.AreEqual("refused", result);
            Assert.AreEqual(1000, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, setup.Ledgers.GetOrCreate(98).GetBalanceCents());
        }

        [Test]
        public void WorkAndPay_OwnerCannotBeRegisteredAsWageEmployee_AndOwnerLabourCarriesNoWage()
        {
            var setup = EmployeeSetup(5000);
            string problem = SliceWorkAndPay.RegisterEmployee(
                setup, "emp-owner", 1, "general-store-1", 320, 0, true);
            Assert.IsNotNull(problem);
            Assert.IsTrue(problem.Contains("never payroll"));
            Assert.IsFalse(setup.Employments.TryGetById("emp-owner", out _));
            Assert.AreEqual(0, setup.Employments.Count);

            SliceOwnerLabourRecord labour = SliceWorkAndPay.RecordOwnerLabour(1, "general-store-1", 0, 7);
            Assert.AreEqual(0, labour.WageCents);
            Assert.AreEqual(7, labour.DaysWorked);
            Assert.AreEqual(5000, setup.Business.CurrentCashCents);
        }

        [Test]
        public void WorkAndPay_OwnerDraw_MovesCashOnce_AndReserveRefusalMovesNothing()
        {
            var setup = EmployeeSetup(5000);
            HouseholdLedger ledger = setup.Ledgers.GetOrCreate(1);

            SliceOwnerDrawResult refused = SliceWorkAndPay.DrawOwnerEarnings(setup.Business, ledger, 0, 4500, 1000);
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(5000, setup.Business.CurrentCashCents);
            Assert.AreEqual(0, ledger.GetBalanceCents());

            SliceOwnerDrawResult zero = SliceWorkAndPay.DrawOwnerEarnings(setup.Business, ledger, 0, 0, 1000);
            Assert.IsFalse(zero.Success);

            SliceOwnerDrawResult draw = SliceWorkAndPay.DrawOwnerEarnings(setup.Business, ledger, 0, 1000, 1000);
            Assert.IsTrue(draw.Success);
            Assert.AreEqual(1000, draw.AmountCents);
            Assert.AreEqual(4000, setup.Business.CurrentCashCents);
            Assert.AreEqual(1000, ledger.GetBalanceCents());
            Assert.AreEqual(1000, setup.Business.LastWeeklyOwnerDistributionCents);
        }

        [Test]
        public void WorkAndPay_BusinessRuntimeCashPort_IsTheSameCash_AsPayroll()
        {
            var setup = EmployeeSetup(5000);
            var port = new BusinessRuntimeCashPort(setup.Business);
            Assert.AreEqual(5000, port.BalanceCents);
            Assert.IsNotNull(port.TryDebit(6000, "too much"));
            Assert.AreEqual(5000, port.BalanceCents);
            Assert.IsNull(port.TryDebit(1250, "procurement"));
            Assert.AreEqual(3750, setup.Business.CurrentCashCents);
            Assert.IsNull(port.Credit(800, "sale"));
            Assert.AreEqual(4550, setup.Business.CurrentCashCents);
        }

        [Test]
        public void WorkAndPay_ComposedRun_HouseholdStartsAtZero_EarnsByDraw_ThenSpendsTwice()
        {
            // Seed 5000 (owner equity, labelled), reserve 1000, draw 1000,
            // order 100u flour at 10c + 250c freight = 1250c, transit 3,
            // sale 1: 40u at 20c = 800c, draw 2: 500c, sale 2: 20u at 20c = 400c.
            // Business: 5000 -1000 -1250 +800 -500 +400 = 3450.
            // Household: 0 +1000 -800 +500 -400 = 300.
            // Lot: 100 - 40 - 20 = 40 remaining.
            SliceWorkAndPayRunReport report = SliceWorkAndPayRun.Execute(
                0, 5000, 1000, 1000, 500,
                100, 10, 250, 200, 3,
                40, 20, 20, 20);
            Assert.IsNotNull(report);
            Assert.AreEqual(0, report.KennedyLabour.WageCents);
            Assert.IsTrue(report.Draw1.Success);
            Assert.IsTrue(report.Draw2.Success);
            Assert.AreEqual(4000, report.BusinessCashAfterDraw1Cents);
            Assert.AreEqual(1000, report.HouseholdCashAfterDraw1Cents);
            Assert.AreEqual(2750, report.BusinessCashAfterProcurementCents);
            Assert.AreEqual(3550, report.BusinessCashAfterSale1Cents);
            Assert.AreEqual(200, report.HouseholdCashAfterSale1Cents);
            Assert.AreEqual(3050, report.BusinessCashAfterDraw2Cents);
            Assert.AreEqual(700, report.HouseholdCashAfterDraw2Cents);
            Assert.AreEqual(3450, report.BusinessCashFinalCents);
            Assert.AreEqual(300, report.HouseholdCashFinalCents);
            Assert.AreEqual(40, report.StoreLotRemainingUnits);
            Assert.AreEqual(0, report.HouseholdCashStartCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.OrderId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.Sale1Id));
            Assert.IsFalse(string.IsNullOrWhiteSpace(report.Sale2Id));
            Assert.IsTrue(report.MealsEaten > 0);
            Assert.IsTrue(report.BaldwinOomsInventory.GetAvailableUnits("flour") < 60);
            Assert.IsTrue(report.SeamsStopped.Count >= 5);
        }

        [Test]
        public void WorkAndPay_ComposedRun_SecondPurchaseNeedsDraw2_NotAFreshHouseholdInput()
        {
            // Same run, but draw 2 is 0: after sale 1 the household holds
            // only 200c, so it cannot fund a 400c second sale, and the
            // run returns null with a diagnostic instead of inventing
            // cash (the 0 draw is refused at the draw step, before any
            // second sale is attempted).
            SliceWorkAndPayRunReport report = SliceWorkAndPayRun.Execute(
                0, 5000, 1000, 1000, 0,
                100, 10, 250, 200, 3,
                40, 20, 20, 20);
            Assert.IsNull(report);
        }
    }
}
