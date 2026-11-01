using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass IV (Worker 4, Phase P): off-screen rules never manufacture labor
    /// time, goods, money, capacity, travel, or customers — and the return to
    /// rendered areas reconciles loudly against the authorities.
    /// </summary>
    [TestFixture]
    public sealed class OffScreenSimulationTests
    {
        private static OffScreenBatchStep ValidStep()
        {
            return new OffScreenBatchStep
            {
                Kind = OffScreenActionKind.Sleep,
                PersonId = 1,
                HouseholdId = 7,
                Minutes = 60,
                BudgetMinutesRemaining = 300,
                WindowFree = true,
                Label = "night sleep chunk",
            };
        }

        [Test]
        public void MayBatch_AllowsLowConsequence_Only()
        {
            Assert.IsTrue(OffScreenSimulationRules.MayBatch(OffScreenActionKind.Sleep));
            Assert.IsTrue(OffScreenSimulationRules.MayBatch(OffScreenActionKind.Rest));
            Assert.IsTrue(OffScreenSimulationRules.MayBatch(OffScreenActionKind.Meal));
            Assert.IsTrue(OffScreenSimulationRules.MayBatch(OffScreenActionKind.UncontendedDomesticWork));
            Assert.IsTrue(OffScreenSimulationRules.MayBatch(OffScreenActionKind.CompletedJourneyLeg));
            Assert.IsFalse(OffScreenSimulationRules.MayBatch(OffScreenActionKind.EquipmentWork));
            Assert.IsFalse(OffScreenSimulationRules.MayBatch(OffScreenActionKind.CashTransaction));
            Assert.IsFalse(OffScreenSimulationRules.MayBatch(OffScreenActionKind.SharedTask));
            Assert.IsFalse(OffScreenSimulationRules.MayBatch(OffScreenActionKind.ShoppingSettlement));
        }

        [Test]
        public void ValidateBatchStep_ValidStep_Passes()
        {
            Assert.IsNull(OffScreenSimulationRules.ValidateBatchStep(ValidStep()));
        }

        [Test]
        public void ValidateBatchStep_RefusesManufacturedLaborTime()
        {
            OffScreenBatchStep overBudget = ValidStep();
            overBudget.BudgetMinutesRemaining = 30;
            Assert.IsNotNull(OffScreenSimulationRules.ValidateBatchStep(overBudget));

            OffScreenBatchStep noWindow = ValidStep();
            noWindow.WindowFree = false;
            Assert.IsNotNull(OffScreenSimulationRules.ValidateBatchStep(noWindow));
        }

        [Test]
        public void ValidateBatchStep_RefusesManufacturedGoods()
        {
            OffScreenBatchStep meal = ValidStep();
            meal.Kind = OffScreenActionKind.Meal;
            meal.GoodsConsumed = new Dictionary<string, int> { { "bread", 4 } };
            meal.InventoryUnits = new Dictionary<string, int> { { "bread", 2 } };

            string refusal = OffScreenSimulationRules.ValidateBatchStep(meal);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("no manufactured goods"));
        }

        [Test]
        public void ValidateBatchStep_RefusesCashMovement()
        {
            OffScreenBatchStep step = ValidStep();
            step.CashDeltaCents = 500;

            string refusal = OffScreenSimulationRules.ValidateBatchStep(step);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("HouseholdLedger"));
        }

        [Test]
        public void ValidateBatchStep_RefusesUnreservedEquipment()
        {
            OffScreenBatchStep step = ValidStep();
            step.Kind = OffScreenActionKind.UncontendedDomesticWork;
            step.RequiresEquipment = true;
            step.EquipmentReserved = false;

            Assert.IsNotNull(OffScreenSimulationRules.ValidateBatchStep(step));
        }

        [Test]
        public void ValidateBatchStep_RefusesEarlyJourneyCompletion()
        {
            OffScreenBatchStep step = ValidStep();
            step.Kind = OffScreenActionKind.CompletedJourneyLeg;
            step.JourneyLegDue = false;

            string refusal = OffScreenSimulationRules.ValidateBatchStep(step);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("no teleporting"));
        }

        [Test]
        public void ValidateBatchStep_RefusesFullResolutionKinds()
        {
            OffScreenBatchStep step = ValidStep();
            step.Kind = OffScreenActionKind.EquipmentWork;

            string refusal = OffScreenSimulationRules.ValidateBatchStep(step);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("full resolution"));
        }

        private static OffScreenHouseholdRecord MatchingRecord()
        {
            return new OffScreenHouseholdRecord
            {
                HouseholdId = 7,
                FromDayIndex = 100,
                ToDayIndex = 101,
                EndingCashCents = 5000,
                EndingInventoryUnits = new Dictionary<string, int> { { "bread", 3 } },
                EndingPersonLocations = new Dictionary<int, string> { { 1, "home" } },
                BatchedStepsApplied = 4,
                FullResolutionEventsKept = 2,
            };
        }

        private static OffScreenReconciler.ReconciliationReport Reconcile(OffScreenHouseholdRecord record, List<string> diag)
        {
            var reconciler = new OffScreenReconciler();
            return reconciler.ReconcileHousehold(
                7, record,
                ledgerBalanceCentsOf: _ => 5000,
                inventoryUnitsOf: _ => new Dictionary<string, int> { { "bread", 3 } },
                personLocationOf: pid => pid == 1 ? "home" : null,
                diag: diag);
        }

        [Test]
        public void Reconcile_MatchingRecord_IsClean()
        {
            OffScreenReconciler.ReconciliationReport report = Reconcile(MatchingRecord(), null);
            Assert.IsTrue(report.Clean);
            Assert.AreEqual(0, report.Divergences.Count);
        }

        [Test]
        public void Reconcile_CashDivergence_LedgerWins()
        {
            OffScreenHouseholdRecord record = MatchingRecord();
            record.EndingCashCents = 9999; // off-screen claim disagrees

            OffScreenReconciler.ReconciliationReport report = Reconcile(record, null);
            Assert.IsFalse(report.Clean);
            Assert.IsTrue(report.Divergences[0].Contains("ledger wins"));
        }

        [Test]
        public void Reconcile_InventoryDivergence_RegistryWins()
        {
            OffScreenHouseholdRecord record = MatchingRecord();
            record.EndingInventoryUnits["bread"] = 10;

            OffScreenReconciler.ReconciliationReport report = Reconcile(record, null);
            Assert.IsFalse(report.Clean);
            Assert.IsTrue(report.Divergences[0].Contains("registry wins"));
        }

        [Test]
        public void Reconcile_LocationMismatch_NoTeleporting()
        {
            OffScreenHouseholdRecord record = MatchingRecord();
            record.EndingPersonLocations[1] = "market-square"; // claims a move that never happened

            OffScreenReconciler.ReconciliationReport report = Reconcile(record, null);
            Assert.IsFalse(report.Clean);
            Assert.IsTrue(report.Divergences[0].Contains("no teleporting"));
        }

        [Test]
        public void Reconcile_MissingRecord_IsLoud()
        {
            OffScreenReconciler.ReconciliationReport report = Reconcile(null, null);
            Assert.IsFalse(report.Clean);
            Assert.AreEqual(1, report.Divergences.Count);
        }
    }
}
