using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M0 (Obj 42): characterization tests for
    /// the weekly converter, pinning current behaviour before M1–M6 change
    /// it. SharedBusinessRuntimeManager itself is harness-excluded, so
    /// these tests run against the existing harness-visible seams:
    ///   - the converter's arithmetic, executed with the RECORDED ratio
    ///     table (the ratios are private consts in SBRM; the table below
    ///     transcribes them as data — pinned as CURRENT values, not
    ///     endorsed — and M6 moves these same expectations onto the
    ///     extracted SharedBusinessWeeklyConverter helper, where the
    ///     helper executes them instead of the local formulas);
    ///   - the BusinessRuntimeState primitives the converter reads and
    ///     writes (stock credit/consume/cap, pending receipt, service
    ///     revenue, procurement bookkeeping).
    /// The seam STRINGS (Bakery/FuelDealer exclusion reason, no-mint
    /// guard, local-market outlet note) are recorded as transcription in
    /// RecordedSeamStrings — they do NOT execute SBRM; M3 makes the seam
    /// executable and M6 the converter.
    ///
    /// M1-OWNED ASSERTIONS ARE NOT IN THIS FILE (coordinator amendment):
    /// the conjure tests (Procurement_NoPendingNoSupplier_RefusesWith
    /// StockAndCashUnchanged, Procurement_PendingCovered_ReceivesPending
    /// Only, Procurement_NeverRemovesMissingSupplierReason) land in M1,
    /// written fail-first in the working tree and flipped by the fix
    /// (the F2 precedent). M0 commits only passing characterization.
    /// Unity compilation, Test Runner, PlayMode, and SBRM itself are NOT
    /// claimed; SBRM edits are source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class SharedBusinessWeeklyConverterTests
    {
        /// <summary>Transcription of the SBRM converter ratio table
        /// (SharedBusinessRuntimeManager.cs:78-88 private consts +
        /// GetOutputUnitsPerInput :5508-5522). Current values, not
        /// endorsed. M6 moves this table into SharedBusinessWeeklyConverter
        /// as data and these tests onto the helper.</summary>
        private static readonly Dictionary<BusinessType, int> RecordedOutputUnitsPerInput =
            new Dictionary<BusinessType, int>
            {
                { BusinessType.CropFarm, 8 },
                { BusinessType.Ranch, 2 },
                { BusinessType.Butcher, 8 },
                { BusinessType.Blacksmith, 4 },
                { BusinessType.FuelDealer, 5 },
                { BusinessType.GrainMill, 4 },
                { BusinessType.Bakery, 3 },
                { BusinessType.Tailor, 2 },
                { BusinessType.Wheelwright, 2 },
            };
        private const int RecordedDefaultOutputUnitsPerInput = 1;

        /// <summary>Transcription of the SBRM local-outlet caps
        /// (SharedBusinessRuntimeManager.cs:78-88). Current values.</summary>
        private static readonly Dictionary<BusinessType, int> RecordedLocalOutletMaxUnits =
            new Dictionary<BusinessType, int>
            {
                { BusinessType.CropFarm, 110 },
                { BusinessType.Ranch, 4 },
                { BusinessType.Butcher, 68 },
                { BusinessType.Blacksmith, 34 },
                { BusinessType.FuelDealer, 58 },
                { BusinessType.GrainMill, 48 },
                { BusinessType.Bakery, 62 },
                { BusinessType.Wheelwright, 24 },
                { BusinessType.Tailor, 28 },
                { BusinessType.Mine, 8 },
                { BusinessType.Sawmill, 12 },
                { BusinessType.LumberYard, 80 },
            };

        /// <summary>Transcription of the converter's recorded seam strings
        /// (SBRM source strings, verified by read at HEAD 1360425).</summary>
        private const string RecordedBakeryFuelExclusionReason =
            "Task-backed production driver is not connected; weekly category conversion is disabled";
        private const string RecordedNoMintGuardReason =
            "no input category: weekly conversion cannot mint output from nothing";
        private const string RecordedLocalOutletNoteFragment =
            "no funded named buyer/order is registered; stock retained and no revenue posted";

        private static int RecordedRatio(BusinessType type) =>
            RecordedOutputUnitsPerInput.TryGetValue(type, out int ratio) ? ratio : RecordedDefaultOutputUnitsPerInput;

        // Local transcriptions of the converter's arithmetic
        // (ResolveBusinessOperation :5003-5055). M6 replaces these with
        // the extracted helper's methods; the expectations do not change.
        private static int PlannedUnits(int throughput, float efficiency01) =>
            (int)Math.Round(Math.Max(0, throughput) * efficiency01);
        private static int InputLimitedOutput(int availableInputs, int ratio) => availableInputs * ratio;
        private static int InputUnitsNeeded(int operableUnits, int ratio) =>
            (int)Math.Ceiling(operableUnits / (float)Math.Max(1, ratio));

        [Test]
        public void RatioTable_CoversAllConverterTypes()
        {
            Assert.AreEqual(8, RecordedRatio(BusinessType.CropFarm), "CropFarm 8 (SBRM const)");
            Assert.AreEqual(2, RecordedRatio(BusinessType.Ranch), "Ranch 2");
            Assert.AreEqual(8, RecordedRatio(BusinessType.Butcher), "Butcher 8");
            Assert.AreEqual(4, RecordedRatio(BusinessType.Blacksmith), "Blacksmith 4");
            Assert.AreEqual(5, RecordedRatio(BusinessType.FuelDealer), "FuelDealer 5");
            Assert.AreEqual(4, RecordedRatio(BusinessType.GrainMill), "GrainMill 4");
            Assert.AreEqual(3, RecordedRatio(BusinessType.Bakery), "Bakery 3");
            Assert.AreEqual(2, RecordedRatio(BusinessType.Tailor), "Tailor 2 (inline in GetOutputUnitsPerInput)");
            Assert.AreEqual(2, RecordedRatio(BusinessType.Wheelwright), "Wheelwright 2 (inline)");
            Assert.AreEqual(1, RecordedRatio(BusinessType.Mine), "default 1 for unlisted types");
            Assert.AreEqual(9, RecordedOutputUnitsPerInput.Count, "7 consts + Tailor + Wheelwright");
        }

        [Test]
        public void OutletCaps_RecordedCurrentValues()
        {
            Assert.AreEqual(110, RecordedLocalOutletMaxUnits[BusinessType.CropFarm]);
            Assert.AreEqual(4, RecordedLocalOutletMaxUnits[BusinessType.Ranch]);
            Assert.AreEqual(68, RecordedLocalOutletMaxUnits[BusinessType.Butcher]);
            Assert.AreEqual(34, RecordedLocalOutletMaxUnits[BusinessType.Blacksmith]);
            Assert.AreEqual(58, RecordedLocalOutletMaxUnits[BusinessType.FuelDealer]);
            Assert.AreEqual(48, RecordedLocalOutletMaxUnits[BusinessType.GrainMill]);
            Assert.AreEqual(62, RecordedLocalOutletMaxUnits[BusinessType.Bakery]);
            Assert.AreEqual(12, RecordedLocalOutletMaxUnits.Count, "12 outlet caps recorded (incl. Mine 8, Sawmill 12, LumberYard 80)");
            foreach (KeyValuePair<BusinessType, int> entry in RecordedLocalOutletMaxUnits)
                Assert.Greater(entry.Value, 0, "every recorded cap is positive");
        }

        [Test]
        public void PlannedUnits_ScalesWithEfficiency()
        {
            Assert.AreEqual(80, PlannedUnits(100, 0.8f), "round(100 x 0.8)");
            Assert.AreEqual(0, PlannedUnits(100, 0f), "zero efficiency plans nothing");
            Assert.AreEqual(100, PlannedUnits(100, 1f), "full efficiency plans the whole throughput");
            Assert.AreEqual(0, PlannedUnits(-5, 1f), "negative throughput clamps to 0");
            Assert.AreEqual(50, PlannedUnits(100, 0.5f), "round(100 x 0.5)");
        }

        [Test]
        public void InputLimitedOutput_RatioMultiplication()
        {
            Assert.AreEqual(80, InputLimitedOutput(10, RecordedRatio(BusinessType.CropFarm)), "CropFarm: 10 inputs x 8");
            Assert.AreEqual(20, InputLimitedOutput(10, RecordedRatio(BusinessType.Ranch)), "Ranch: 10 x 2");
            Assert.AreEqual(30, InputLimitedOutput(10, RecordedRatio(BusinessType.Bakery)), "Bakery: 10 x 3");
            Assert.AreEqual(0, InputLimitedOutput(0, RecordedRatio(BusinessType.Butcher)), "no inputs, no output");
        }

        [Test]
        public void InputUnitsNeeded_RoundsUp()
        {
            Assert.AreEqual(4, InputUnitsNeeded(25, 8), "ceil(25/8) = 4");
            Assert.AreEqual(3, InputUnitsNeeded(24, 8), "ceil(24/8) = 3");
            Assert.AreEqual(1, InputUnitsNeeded(1, 8), "ceil(1/8) = 1");
            Assert.AreEqual(0, InputUnitsNeeded(0, 8), "no operable units need no input");
        }

        [Test]
        public void StorageShortfall_StoredLessThanOperable_IsRecordedNotOvercounted()
        {
            // The converter's PASS7-007 shape: produced = accepted, and the
            // shortfall is recorded as a blocked reason, never over-counted.
            // The primitive under it: AddCategoryStockUnits caps at target.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.EnsureCategoryStock("staple_food", 96, 100);

            int stored = runtime.AddCategoryStockUnits("staple_food", 10);
            Assert.AreEqual(4, stored, "only 4 units fit (96 -> 100 target)");
            Assert.AreEqual(100, runtime.GetCategoryStock("staple_food").CurrentStockUnits);

            int operableUnits = 10;
            int producedUnits = stored;
            Assert.Less(producedUnits, operableUnits, "produced < operable is the shortfall, not an error");
            Assert.AreEqual("output storage shortfall: stored 4/10 staple_food",
                $"output storage shortfall: stored {producedUnits}/{operableUnits} staple_food",
                "the shortfall reason names stored/operable exactly (converter wording, transcribed)");
        }

        [Test]
        public void ServiceRevenue_ExactCredit_BusinessCreditEqualsHouseholdDebit()
        {
            // Canon XIII §13.2 (converter service path, Pass IX WS1): the
            // former type multiplier credited 5-20% more than households
            // were debited. The current rule is exact: revenue = paid
            // visits x price, business credit == household debit.
            Assert.AreEqual(900, BusinessRuntimeState.ComputeServiceRevenueCents(3, 300));
            Assert.AreEqual(0, BusinessRuntimeState.ComputeServiceRevenueCents(0, 300), "no paid visits, no revenue");
            Assert.AreEqual(0, BusinessRuntimeState.ComputeServiceRevenueCents(3, 0), "no price, no revenue");

            var business = new BusinessRuntimeState();
            business.BindBusinessIdentity("doctor-1");
            business.SetCurrentCashCents(2000);
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger household = ledgers.GetOrCreate(7);
            Assert.IsNull(household.RecordInflow(0, 10000, HouseholdIncomeSource.OwnerContribution, "test-capital", "test funding", "test"));
            int combinedBefore = business.CurrentCashCents + household.GetBalanceCents();

            int paidVisits = 3;
            int visitPrice = 300;
            int revenue = BusinessRuntimeState.ComputeServiceRevenueCents(paidVisits, visitPrice);
            Assert.IsNull(household.RecordOutflow(3, revenue, "doctor visit", "doctor-1"));
            business.RecordDailyServiceRevenue("medical_service", paidVisits, revenue);

            Assert.AreEqual(2900, business.CurrentCashCents, "business credited exactly 900");
            Assert.AreEqual(9100, household.GetBalanceCents(), "household debited exactly 900");
            Assert.AreEqual(combinedBefore, business.CurrentCashCents + household.GetBalanceCents(),
                "cash is conserved across the service exchange — nothing minted");
        }

        [Test]
        public void CategoryStock_AddCreditsConsumeDebitsCapped()
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.EnsureCategoryStock("crop_food", 10, 100);

            Assert.AreEqual(5, runtime.AddCategoryStockUnits("crop_food", 5), "credit adds 5");
            Assert.AreEqual(15, runtime.GetCategoryStock("crop_food").CurrentStockUnits);
            Assert.IsTrue(runtime.TryConsumeCategoryStockUnits("crop_food", 6, out int consumed), "consume succeeds");
            Assert.AreEqual(6, consumed);
            Assert.AreEqual(9, runtime.GetCategoryStock("crop_food").CurrentStockUnits);
            Assert.IsTrue(!runtime.TryConsumeCategoryStockUnits("crop_food", 10, out consumed), "over-consumption refuses");
            Assert.AreEqual(0, consumed, "a refused consumption consumes nothing");
            Assert.AreEqual(9, runtime.GetCategoryStock("crop_food").CurrentStockUnits, "stock unchanged by the refusal");
            Assert.AreEqual(0, runtime.AddCategoryStockUnits("no_such_category", 5), "a missing category credits nothing");
        }

        [Test]
        public void PendingReceipt_MovesStockWithoutCash()
        {
            // M0 trace pin for M1: the RECEIVE primitive costs nothing —
            // it moves pending units into current stock and returns the
            // received count. The weekly method's spend line (received
            // units x unit cost, covering pending-received units too) is
            // what charges pending receipt, and it is the single payment:
            // pending is set by the weekly cadence refresh with no cash
            // movement, and no production path turns SBRM pending into a
            // real supplier order (see the M0 checkpoint trace). M1 keeps
            // that spend for pending-received units and deletes only the
            // direct-conjure credit + spend.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("crop_food", 10, 100);
            CategoryStockState stock = runtime.GetCategoryStock("crop_food");
            stock.SetPendingReorderUnits(20);
            Assert.AreEqual(20, stock.PendingReorderUnits);

            int received = runtime.ReceivePendingReorderUnits("crop_food", 8);
            Assert.AreEqual(8, received, "receives the requested units, clamped to pending");
            Assert.AreEqual(18, stock.CurrentStockUnits, "10 + 8 received");
            Assert.AreEqual(12, stock.PendingReorderUnits, "20 - 8 remain pending");
            Assert.AreEqual(5000, runtime.CurrentCashCents, "the receive primitive moves NO cash");

            int overReceived = runtime.ReceivePendingReorderUnits("crop_food", 99);
            Assert.AreEqual(12, overReceived, "receipt clamps to the remaining pending units");
            Assert.AreEqual(0, stock.PendingReorderUnits);
            Assert.AreEqual(5000, runtime.CurrentCashCents, "still no cash moved");
            Assert.AreEqual(0, runtime.ReceivePendingReorderUnits("no_such_category", 5), "a missing category receives nothing");
        }

        [Test]
        public void ProcurementBookkeeping_SpendIsReadable()
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("crop_food", 0, 100);

            runtime.RecordWeeklyInputProcurement(6, 120);
            Assert.AreEqual(120, runtime.LastWeeklyInputProcurementSpendCents,
                "the spend the converter's summary line reads is recorded");
            runtime.RecordWeeklyInputProcurement(4, 80);
            Assert.AreEqual(200, runtime.LastWeeklyInputProcurementSpendCents, "spend accumulates within the week");
        }

        [Test]
        public void RecordedSeamStrings_BakeryFuelExclusion_NoMintGuard_LocalOutlet()
        {
            // Transcription pins, NOT executable SBRM behaviour (SBRM is
            // harness-excluded). M3 makes the Bakery/FuelDealer seam
            // executable; M6 makes the converter executable. The strings
            // below are the exact source strings at HEAD 1360425; if a
            // later slice changes the wording, this test must be updated
            // with the reason recorded.
            Assert.IsTrue(RecordedBakeryFuelExclusionReason.Contains("Task-backed production driver"),
                "the exclusion names the owning driver");
            Assert.IsTrue(RecordedBakeryFuelExclusionReason.Contains("weekly category conversion is disabled"),
                "the exclusion states the 0-output rule");
            Assert.IsTrue(RecordedNoMintGuardReason.Contains("cannot mint output from nothing"),
                "the no-mint guard states its rule");
            Assert.IsTrue(RecordedLocalOutletNoteFragment.Contains("stock retained and no revenue posted"),
                "the local-market outlet retains stock and posts 0 revenue");
        }
    }
}
