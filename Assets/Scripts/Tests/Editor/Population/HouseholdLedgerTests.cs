using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// HF-4: provenance-enforced household income ledger (Canon XIII 13.2), family-labor
    /// accounting without payroll (TECH §4.5, PL-04), and consumption planning that never
    /// posts revenue by itself (Canon 13.4).
    /// </summary>
    [TestFixture]
    public sealed class HouseholdLedgerTests
    {
        [Test]
        public void Inflow_RequiresProvenance_RejectsSyntheticTopUp()
        {
            var ledger = new HouseholdLedger(1);

            string noSource = ledger.RecordInflow(100, 5000, HouseholdIncomeSource.Unspecified, "", "weekly money", "");
            string noReason = ledger.RecordInflow(100, 5000, HouseholdIncomeSource.OtherDocumented, "", "", "");

            Assert.IsNotNull(noSource);
            Assert.IsNotNull(noReason);
            Assert.AreEqual(0, ledger.GetBalanceCents());
            Assert.AreEqual(0, ledger.Entries.Count);
        }

        [Test]
        public void DocumentedInflow_And_Outflow_DeriveBalance()
        {
            var ledger = new HouseholdLedger(1);

            Assert.IsNull(ledger.RecordInflow(100, 12000, HouseholdIncomeSource.Remittance, "kin:uncle", "monthly remittance", "uncle"));
            Assert.IsNull(ledger.RecordOutflow(101, 3000, "staple food purchase", "general store"));

            Assert.AreEqual(9000, ledger.GetBalanceCents());
            Assert.AreEqual(2, ledger.Entries.Count);
            Assert.AreEqual(12000, ledger.Entries[0].BalanceAfterCents);
            Assert.AreEqual(9000, ledger.Entries[1].BalanceAfterCents);
        }

        [Test]
        public void WagePayment_RecordsEmploymentRelationshipReference()
        {
            var ledger = new HouseholdLedger(1);

            string rejection = ledger.RecordWagePayment(100, "E5", 12, 8500, "week 12");

            Assert.IsNull(rejection);
            Assert.AreEqual(8500, ledger.GetBalanceCents());
            HouseholdLedgerEntry entry = ledger.Entries[0];
            Assert.AreEqual(HouseholdIncomeSource.WageEmployment, entry.Source);
            Assert.AreEqual("E5", entry.SourceReference);
            Assert.IsTrue(entry.Reason.Contains("E5"));
        }

        [Test]
        public void WagePayment_RejectsMissingEmploymentReference()
        {
            var ledger = new HouseholdLedger(1);

            string rejection = ledger.RecordWagePayment(100, "", 12, 8500, "week 12");

            Assert.IsNotNull(rejection);
            Assert.AreEqual(0, ledger.GetBalanceCents());
        }

        [Test]
        public void FamilyLabor_RecordsTimeAndOutput_WithoutPayroll()
        {
            var labor = new FamilyLaborLedger();

            string rejection = labor.RecordContribution(new FamilyLaborContribution
            {
                PersonId = 7,
                HouseholdId = 1,
                BusinessId = "B3",
                TaskCategory = "poultry",
                DayIndex = 100,
                MinutesWorked = 240,
                OutputUnits = 30,
                OutputUnitLabel = "eggs collected",
            }, agreedWageCents: 0);

            Assert.IsNull(rejection);
            labor.GetTotals(7, "B3", out int minutes, out int output);
            Assert.AreEqual(240, minutes);
            Assert.AreEqual(30, output);
        }

        [Test]
        public void FamilyLabor_RejectsWageTerms()
        {
            var labor = new FamilyLaborLedger();

            string rejection = labor.RecordContribution(new FamilyLaborContribution
            {
                PersonId = 7,
                HouseholdId = 1,
                BusinessId = "B3",
                TaskCategory = "poultry",
                DayIndex = 100,
                MinutesWorked = 240,
            }, agreedWageCents: 1200);

            Assert.IsNotNull(rejection);
            Assert.AreEqual(0, labor.ContributionCount);
        }

        [Test]
        public void ConsumptionPlan_GeneratesNeeds_ButMovesNoMoney()
        {
            var ledger = new HouseholdLedger(1);
            var household = new HouseholdState
            {
                id = 1,
                reserves = new List<HouseholdReserveState>
                {
                    new HouseholdReserveState { categoryId = "staple_food", currentUnits = 2, lowThresholdUnits = 10 },
                },
                demandSnapshot = new HouseholdDemandSnapshot { medicineNeed = 3 },
            };

            var planner = new HouseholdConsumptionPlanner();
            List<ProcurementNeed> needs = planner.BuildPlan(household, requestingPersonId: 7, dayIndex: 100);

            Assert.AreEqual(2, needs.Count); // 8u staple shortfall + 3u medicine_remedies
            Assert.AreEqual("staple_food", needs[0].CategoryId);
            Assert.AreEqual(8, needs[0].UnitsNeeded);
            Assert.AreEqual("medicine_remedies", needs[1].CategoryId);
            // Planning alone moves no money and books no revenue: Canon 13.4.
            Assert.AreEqual(0, ledger.GetBalanceCents());
            Assert.AreEqual(0, ledger.Entries.Count);
        }

        [Test]
        public void LedgerRegistry_ExportsAndImports_State()
        {
            var registry = new HouseholdLedgerRegistry();
            registry.GetOrCreate(1).RecordInflow(100, 5000, HouseholdIncomeSource.Remittance, "kin", "gift", "aunt");

            var states = new List<HouseholdLedgerState>();
            registry.ExportState(states);

            var restored = new HouseholdLedgerRegistry();
            restored.ImportState(states);

            Assert.AreEqual(5000, restored.Get(1).GetBalanceCents());
        }
    }
}
