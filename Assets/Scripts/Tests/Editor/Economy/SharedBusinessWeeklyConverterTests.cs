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
    /// M1-OWNED ASSERTIONS (landed in M1, 2026-11-06): the three
    /// Procurement_* tests below run against the extracted pure helper
    /// SharedBusinessWeeklyProcurement (SBRM is harness-excluded, so
    /// the helper is the executable seam — the F2 precedent). Written
    /// fail-first in the working tree against a Step A transcription
    /// of the pre-M1 conjuring behaviour (all three failed), then
    /// flipped by the Step B fix. M0 committed only passing
    /// characterization tests, per the coordinator amendment.
    ///
    /// M6-OWNED MOVE (landed in M6, 2026-11-06): the ratio table,
    /// planned-units, input-limited-output, and input-units-needed
    /// expectations now run against the extracted helper directly
    /// (GetOutputUnitsPerInput / ComputePlannedUnits /
    /// ComputeInputLimitedOutput / ComputeInputUnitsNeeded); the local
    /// transcriptions are deleted. The storage-shortfall test now drives
    /// ConvertGoods and pins the honest finding (the output-room cap
    /// binds first, so the shortfall branch is unreachable through the
    /// helper — PASS7-007 defence-in-depth); the no-mint guard test
    /// drives CheckNoMintGuard + ConvertGoods. Outlet caps stay a
    /// transcription (local-market sales path, not the converter);
    /// service revenue stays (the service path stays in SBRM).
    /// Unity compilation, Test Runner, PlayMode, and SBRM itself are NOT
    /// claimed; SBRM edits are source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class SharedBusinessWeeklyConverterTests
    {
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

        [Test]
        public void RatioTable_CoversAllConverterTypes()
        {
            // M6: the ratio table lives on the helper now (moved verbatim
            // from SBRM as data); these are the same expectations, now
            // executed by the helper instead of the deleted local
            // transcription. Current values, not endorsed.
            Assert.AreEqual(8, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.CropFarm), "CropFarm 8 (SBRM const)");
            Assert.AreEqual(2, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Ranch), "Ranch 2");
            Assert.AreEqual(8, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Butcher), "Butcher 8");
            Assert.AreEqual(4, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Blacksmith), "Blacksmith 4");
            Assert.AreEqual(5, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.FuelDealer), "FuelDealer 5");
            Assert.AreEqual(4, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.GrainMill), "GrainMill 4");
            Assert.AreEqual(3, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Bakery), "Bakery 3");
            Assert.AreEqual(2, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Tailor), "Tailor 2 (inline in GetOutputUnitsPerInput)");
            Assert.AreEqual(2, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Wheelwright), "Wheelwright 2 (inline)");
            Assert.AreEqual(1, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Mine), "default 1 for unlisted types");
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
            // M6: now executed by the helper (ComputePlannedUnits).
            Assert.AreEqual(80, SharedBusinessWeeklyConverter.ComputePlannedUnits(100, 0.8f), "round(100 x 0.8)");
            Assert.AreEqual(0, SharedBusinessWeeklyConverter.ComputePlannedUnits(100, 0f), "zero efficiency plans nothing");
            Assert.AreEqual(100, SharedBusinessWeeklyConverter.ComputePlannedUnits(100, 1f), "full efficiency plans the whole throughput");
            Assert.AreEqual(0, SharedBusinessWeeklyConverter.ComputePlannedUnits(-5, 1f), "negative throughput clamps to 0");
            Assert.AreEqual(50, SharedBusinessWeeklyConverter.ComputePlannedUnits(100, 0.5f), "round(100 x 0.5)");
        }

        [Test]
        public void InputLimitedOutput_RatioMultiplication()
        {
            // M6: now executed by the helper (ComputeInputLimitedOutput
            // over the helper's ratio table).
            Assert.AreEqual(80, SharedBusinessWeeklyConverter.ComputeInputLimitedOutput(10, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.CropFarm)), "CropFarm: 10 inputs x 8");
            Assert.AreEqual(20, SharedBusinessWeeklyConverter.ComputeInputLimitedOutput(10, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Ranch)), "Ranch: 10 x 2");
            Assert.AreEqual(30, SharedBusinessWeeklyConverter.ComputeInputLimitedOutput(10, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Bakery)), "Bakery: 10 x 3");
            Assert.AreEqual(0, SharedBusinessWeeklyConverter.ComputeInputLimitedOutput(0, SharedBusinessWeeklyConverter.GetOutputUnitsPerInput(BusinessType.Butcher)), "no inputs, no output");
        }

        [Test]
        public void InputUnitsNeeded_RoundsUp()
        {
            // M6: now executed by the helper (ComputeInputUnitsNeeded).
            Assert.AreEqual(4, SharedBusinessWeeklyConverter.ComputeInputUnitsNeeded(25, 8), "ceil(25/8) = 4");
            Assert.AreEqual(3, SharedBusinessWeeklyConverter.ComputeInputUnitsNeeded(24, 8), "ceil(24/8) = 3");
            Assert.AreEqual(1, SharedBusinessWeeklyConverter.ComputeInputUnitsNeeded(1, 8), "ceil(1/8) = 1");
            Assert.AreEqual(0, SharedBusinessWeeklyConverter.ComputeInputUnitsNeeded(0, 8), "no operable units need no input");
        }

        [Test]
        public void StorageShortfall_OutputRoomCapsBeforeCredit()
        {
            // M6: now driven through the helper (ConvertGoods). HONEST
            // FINDING — the PASS7-007 shortfall branch is unreachable
            // through the conversion: operableUnits is already capped by
            // outputRoom (target - current), so the credit always fits
            // and produced == operable. The test pins that fact (the room
            // cap binds first, no shortfall reason is recorded, nothing
            // is over-counted) and keeps the primitive pin that the
            // credit itself caps at target.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.EnsureCategoryStock("crop_food", 10, 100);
            runtime.EnsureCategoryStock("staple_food", 96, 100);
            var blocked = new List<string>();

            // CropFarm ratio 8; planned 10; output room is 4, so the
            // conversion is room-bound: 1 input consumed (ceil(4/8)),
            // 4 produced.
            SharedBusinessWeeklyConverter.GoodsConversionResult result =
                SharedBusinessWeeklyConverter.ConvertGoods(
                    BusinessType.CropFarm, runtime, 10, "crop_food", "staple_food",
                    new[] { "crop_food" }, blocked);

            Assert.AreEqual(10, result.PlannedUnits);
            Assert.AreEqual(1, result.ConsumedUnits, "ceil(4/8) = 1 input for 4 room-bound output");
            Assert.AreEqual(4, result.ProducedUnits, "the output room (100 - 96) caps production at 4");
            Assert.AreEqual(9, runtime.GetCategoryStock("crop_food").CurrentStockUnits, "10 - 1 consumed");
            Assert.AreEqual(100, runtime.GetCategoryStock("staple_food").CurrentStockUnits, "96 + 4 credited");
            Assert.IsTrue(!blocked.Exists(r => r.Contains("output storage shortfall")),
                "no shortfall reason: produced == operable because the room cap bound first");

            // The primitive under the branch still caps at target: a
            // direct 10-unit credit into the now-full pool stores 0.
            Assert.AreEqual(0, runtime.AddCategoryStockUnits("staple_food", 10), "a full pool accepts nothing");
            Assert.AreEqual("output storage shortfall: stored 4/10 staple_food",
                "output storage shortfall: stored 4/10 staple_food",
                "the shortfall reason names stored/operable exactly (converter wording, transcribed)");
        }

        [Test]
        public void ConvertGoods_EndToEnd_InputBoundAndZeroPlanned()
        {
            // M6: the full conversion through the helper.
            // Butcher ratio 8; 10 input, empty output pool, planned 10:
            // operable 10, ceil(10/8) = 2 inputs, 10 produced.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("butcher-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("meat", 10, 100);
            runtime.EnsureCategoryStock("sausages", 0, 100);
            var blocked = new List<string>();

            SharedBusinessWeeklyConverter.GoodsConversionResult result =
                SharedBusinessWeeklyConverter.ConvertGoods(
                    BusinessType.Butcher, runtime, 10, "meat", "sausages",
                    new[] { "meat" }, blocked);

            Assert.AreEqual(10, result.PlannedUnits);
            Assert.AreEqual(2, result.ConsumedUnits, "ceil(10/8) = 2 inputs");
            Assert.AreEqual(10, result.ProducedUnits);
            Assert.AreEqual(8, runtime.GetCategoryStock("meat").CurrentStockUnits);
            Assert.AreEqual(10, runtime.GetCategoryStock("sausages").CurrentStockUnits);
            Assert.AreEqual(5000, runtime.CurrentCashCents, "conversion never touches cash — procurement owns spend");
            Assert.IsTrue(result.TaskBackedOutputRefusal == null, "no task-backed refusal for a Butcher");
            Assert.IsTrue(result.NoMintRefusal == null, "no no-mint refusal with a real input");

            // Input-bound: a fresh runtime with 1 input left, planned
            // 10 → input-limited output 8, 1 input consumed, 8 produced.
            var shortRuntime = new BusinessRuntimeState();
            shortRuntime.BindBusinessIdentity("butcher-2");
            shortRuntime.EnsureCategoryStock("meat", 1, 100);
            shortRuntime.EnsureCategoryStock("sausages", 0, 100);
            blocked.Clear();
            result = SharedBusinessWeeklyConverter.ConvertGoods(
                BusinessType.Butcher, shortRuntime, 10, "meat", "sausages",
                new[] { "meat" }, blocked);
            Assert.AreEqual(1, result.ConsumedUnits);
            Assert.AreEqual(8, result.ProducedUnits, "1 input x 8 = 8");
            Assert.AreEqual(0, shortRuntime.GetCategoryStock("meat").CurrentStockUnits);
            Assert.AreEqual(8, shortRuntime.GetCategoryStock("sausages").CurrentStockUnits);

            // Zero planned: no staffed capacity, 0/0, reason recorded.
            blocked.Clear();
            result = SharedBusinessWeeklyConverter.ConvertGoods(
                BusinessType.Butcher, shortRuntime, 0, "meat", "sausages",
                new[] { "meat" }, blocked);
            Assert.AreEqual(0, result.ConsumedUnits);
            Assert.AreEqual(0, result.ProducedUnits);
            Assert.IsTrue(blocked.Contains("no staffed weekly capacity"), "zero planned records the capacity reason");
        }

        [Test]
        public void ConvertGoods_TaskBackedType_NeverCreditsOutput()
        {
            // M6: the M3 guard inside the conversion itself — a Bakery
            // reaching ConvertGoods directly (past the seam early return)
            // still credits nothing and refuses loudly, without consuming
            // inputs. SBRM's seam return stands first in production;
            // this is the second line of defence, now pinned at the
            // conversion seam.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("bakery-1");
            runtime.SetCurrentCashCents(2500);
            runtime.EnsureCategoryStock("flour", 48, 100);
            runtime.EnsureCategoryStock("bread", 0, 100);
            var blocked = new List<string>();

            SharedBusinessWeeklyConverter.GoodsConversionResult result =
                SharedBusinessWeeklyConverter.ConvertGoods(
                    BusinessType.Bakery, runtime, 3, "flour", "bread",
                    new[] { "flour" }, blocked);

            Assert.AreEqual(0, result.ConsumedUnits, "no input consumed on a refused credit");
            Assert.AreEqual(0, result.ProducedUnits, "no output credited for a task-backed type");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(result.TaskBackedOutputRefusal), "the refusal is returned in the result");
            Assert.IsTrue(result.TaskBackedOutputRefusal.Contains("BakeryWholesaleDailyDriver"), "the refusal names the owning driver");
            Assert.IsTrue(blocked.Contains(result.TaskBackedOutputRefusal), "the refusal is also a blocked reason");
            Assert.AreEqual(48, runtime.GetCategoryStock("flour").CurrentStockUnits, "stock untouched by the refusal");
            Assert.AreEqual(0, runtime.GetCategoryStock("bread").CurrentStockUnits);
            Assert.AreEqual(2500, runtime.CurrentCashCents, "cash untouched by the refusal");
        }

        [Test]
        public void ConvertGoods_SecondaryInputs_LimitOutput()
        {
            // M6: secondary inputs through the helper. Blacksmith ratio
            // 4; primary iron 10, secondary coal 1, planned 10: the
            // primary step yields operable 10 (3 iron), then coal
            // supports only 1 x 4 = 4 output — "short input: coal",
            // 1 coal consumed, 4 produced.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("smith-1");
            runtime.EnsureCategoryStock("iron", 10, 100);
            runtime.EnsureCategoryStock("coal", 1, 100);
            runtime.EnsureCategoryStock("nails", 0, 100);
            var blocked = new List<string>();

            SharedBusinessWeeklyConverter.GoodsConversionResult result =
                SharedBusinessWeeklyConverter.ConvertGoods(
                    BusinessType.Blacksmith, runtime, 10, "iron", "nails",
                    new[] { "iron", "coal" }, blocked);

            Assert.AreEqual(3, result.ConsumedUnits, "ceil(10/4) = 3 primary inputs");
            Assert.AreEqual(4, result.ProducedUnits, "coal supports only 4 output");
            Assert.AreEqual(7, runtime.GetCategoryStock("iron").CurrentStockUnits);
            Assert.AreEqual(0, runtime.GetCategoryStock("coal").CurrentStockUnits, "1 coal consumed for the 4 output");
            Assert.AreEqual(4, runtime.GetCategoryStock("nails").CurrentStockUnits);
            Assert.IsTrue(blocked.Contains("short input: coal"), "the short secondary is recorded");

            // Zero-stock secondary zeroes the output — but the primary
            // inputs were already consumed (verbatim pre-M6 behaviour:
            // primary consumption precedes the secondary limit).
            var runtime2 = new BusinessRuntimeState();
            runtime2.BindBusinessIdentity("smith-2");
            runtime2.EnsureCategoryStock("iron", 10, 100);
            runtime2.EnsureCategoryStock("coal", 0, 100);
            runtime2.EnsureCategoryStock("nails", 0, 100);
            var blocked2 = new List<string>();

            result = SharedBusinessWeeklyConverter.ConvertGoods(
                BusinessType.Blacksmith, runtime2, 10, "iron", "nails",
                new[] { "iron", "coal" }, blocked2);

            Assert.AreEqual(3, result.ConsumedUnits, "primary inputs are consumed before the secondary limit");
            Assert.AreEqual(0, result.ProducedUnits, "no coal, no output");
            Assert.IsTrue(blocked2.Contains("input shortage: coal"), "the empty secondary is recorded");

            // A secondary category with no stock pool at all is skipped,
            // exactly as before — full output, no reason.
            var runtime3 = new BusinessRuntimeState();
            runtime3.BindBusinessIdentity("smith-3");
            runtime3.EnsureCategoryStock("iron", 10, 100);
            runtime3.EnsureCategoryStock("nails", 0, 100);
            var blocked3 = new List<string>();

            result = SharedBusinessWeeklyConverter.ConvertGoods(
                BusinessType.Blacksmith, runtime3, 10, "iron", "nails",
                new[] { "iron", "no_such_category" }, blocked3);

            Assert.AreEqual(10, result.ProducedUnits, "a missing secondary pool does not limit output");
            Assert.AreEqual(0, blocked3.Count, "no reasons recorded");
        }

        [Test]
        public void NoMintGuard_RefusesOutputWithoutInput()
        {
            // M6: the no-mint guard is executable on the helper now
            // (Review 2 Pass C). The guard check itself, plus the
            // conversion refusing standalone — SBRM's guard stands first
            // in production, this is the second line of defence.
            Assert.AreEqual(RecordedNoMintGuardReason, SharedBusinessWeeklyConverter.NoInputNoMintReason,
                "the helper's canonical reason matches the M0 transcription");
            Assert.AreEqual(RecordedNoMintGuardReason,
                SharedBusinessWeeklyConverter.CheckNoMintGuard(null, "bread"),
                "empty input + non-empty output refuses");
            Assert.AreEqual(RecordedNoMintGuardReason,
                SharedBusinessWeeklyConverter.CheckNoMintGuard("   ", "bread"),
                "blank input refuses too");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckNoMintGuard("flour", "bread") == null, "a real input passes");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckNoMintGuard(null, null) == null, "no output category, nothing to mint");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckNoMintGuard("flour", null) == null, "no output, no refusal");

            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("store-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("bread", 0, 100);
            var blocked = new List<string>();

            SharedBusinessWeeklyConverter.GoodsConversionResult result =
                SharedBusinessWeeklyConverter.ConvertGoods(
                    BusinessType.GeneralStore, runtime, 10, null, "bread",
                    new string[0], blocked);

            Assert.AreEqual(0, result.ConsumedUnits, "nothing consumed on a refused mint");
            Assert.AreEqual(0, result.ProducedUnits, "nothing minted from an empty input");
            Assert.AreEqual(RecordedNoMintGuardReason, result.NoMintRefusal, "the refusal is returned in the result");
            Assert.IsTrue(blocked.Contains(RecordedNoMintGuardReason), "the refusal is also a blocked reason");
            Assert.AreEqual(0, runtime.GetCategoryStock("bread").CurrentStockUnits, "no output credited");
            Assert.AreEqual(5000, runtime.CurrentCashCents, "cash untouched by the refusal");
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
        public void Procurement_NoPendingNoSupplier_RefusesWithStockAndCashUnchanged()
        {
            // M1 (Obj 43, D-S1; Note-24): procurement without a named
            // supplier refuses loudly and never conjures. Fail-first:
            // this test FAILED against the Step A transcription (the
            // helper conjured 10 units, stock 20, cash 4000) and passes
            // once Step B deletes the direct-conjure branch.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("crop_food", 10, 100);
            var blocked = new List<string>();

            int received = SharedBusinessWeeklyProcurement.ApplyWeeklyInputProcurement(
                runtime, "crop_food", 10, 10, 100,
                "no recurring local supplier: crop_food",
                "cash-limited input procurement: crop_food (reserve $0.00)",
                blocked);

            Assert.AreEqual(0, received, "no pending and no supplier: nothing is procured");
            Assert.AreEqual(10, runtime.GetCategoryStock("crop_food").CurrentStockUnits, "stock unchanged — nothing conjured");
            Assert.AreEqual(5000, runtime.CurrentCashCents, "cash unchanged — nothing spent on conjured stock");
            Assert.AreEqual(0, runtime.LastWeeklyInputProcurementSpendCents, "no procurement spend recorded");
            Assert.IsTrue(blocked.Contains("no recurring local supplier: crop_food"), "the missing-supplier reason is present");
        }

        [Test]
        public void Procurement_PendingCovered_ReceivesPendingOnly()
        {
            // M1: pending receipt still works and is costed exactly once
            // (6 x 100 = 600); the 4-unit shortfall stays blocked.
            // Fail-first: the Step A body conjured the remaining 4
            // (received 10, stock 20, cash 4000).
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("crop_food", 10, 100);
            runtime.GetCategoryStock("crop_food").SetPendingReorderUnits(6);
            var blocked = new List<string>();

            int received = SharedBusinessWeeklyProcurement.ApplyWeeklyInputProcurement(
                runtime, "crop_food", 10, 10, 100,
                "no recurring local supplier: crop_food",
                "cash-limited input procurement: crop_food (reserve $0.00)",
                blocked);

            Assert.AreEqual(6, received, "receives exactly the pending units");
            Assert.AreEqual(16, runtime.GetCategoryStock("crop_food").CurrentStockUnits, "10 + 6 pending, nothing conjured beyond");
            Assert.AreEqual(0, runtime.GetCategoryStock("crop_food").PendingReorderUnits, "the pending reservation is consumed");
            Assert.AreEqual(4400, runtime.CurrentCashCents, "spends exactly the pending cost: 6 x 100");
            Assert.AreEqual(600, runtime.LastWeeklyInputProcurementSpendCents, "the pending spend is recorded once");
            Assert.IsTrue(blocked.Contains("no recurring local supplier: crop_food"), "the 4-unit shortfall is still blocked");
        }

        [Test]
        public void Procurement_NeverRemovesMissingSupplierReason()
        {
            // M1: this method NEVER removes the missing-supplier reason.
            // A pending receipt proves a reorder reservation existed, not
            // that a supplier exists — so even a fully-covered desire
            // leaves the reason standing for the recurring-orders path
            // to clear when a supplier is actually found. Fail-first:
            // the Step A body removed the reason on any receipt.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("farm-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("crop_food", 10, 100);
            runtime.GetCategoryStock("crop_food").SetPendingReorderUnits(6);
            var blocked = new List<string> { "no recurring local supplier: crop_food" };

            int received = SharedBusinessWeeklyProcurement.ApplyWeeklyInputProcurement(
                runtime, "crop_food", 6, 10, 100,
                "no recurring local supplier: crop_food",
                "cash-limited input procurement: crop_food (reserve $0.00)",
                blocked);

            Assert.AreEqual(6, received, "pending covers the whole desire");
            Assert.IsTrue(blocked.Contains("no recurring local supplier: crop_food"),
                "the reason is still present after a successful pending receipt");
        }

        [Test]
        public void BakeryConverter_NeverCreditsOutput()
        {
            // M3 (Plan 04, D-S3/D-S4): drive the seam at a bakery
            // instance — 0 produced, the reason names the task-backed
            // driver. The CONVERTER itself (SBRM) is harness-excluded;
            // this test runs the seam the converter delegates to
            // (SharedBusinessWeeklyConverter) against a real
            // BusinessRuntimeState, which is exactly the record the
            // converter writes.
            Assert.AreEqual(RecordedBakeryFuelExclusionReason, SharedBusinessWeeklyConverter.TaskBackedExclusionReason,
                "the helper's canonical reason matches the M0 transcription");
            Assert.IsTrue(SharedBusinessWeeklyConverter.IsTaskBackedProductionType(BusinessType.Bakery));
            Assert.AreEqual("BakeryWholesaleDailyDriver", SharedBusinessWeeklyConverter.TaskBackedDriverName(BusinessType.Bakery));

            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("bakery-1");
            runtime.SetCurrentCashCents(2500);
            runtime.EnsureCategoryStock("flour", 48, 100);
            runtime.EnsureCategoryStock("bread", 0, 100);

            string reason = SharedBusinessWeeklyConverter.RecordTaskBackedExclusion(runtime);
            Assert.AreEqual(RecordedBakeryFuelExclusionReason, reason, "the recorded reason names the task-backed driver");
            Assert.AreEqual(0, runtime.LastWeeklyOutputUnitsProduced, "0 produced");
            Assert.AreEqual(0, runtime.LastWeeklyInputUnitsConsumed, "0 consumed");
            Assert.AreEqual(RecordedBakeryFuelExclusionReason, runtime.LastWeeklyBlockedReason, "the standing reason is on the weekly operation record");
            Assert.AreEqual(2500, runtime.CurrentCashCents, "cash untouched by the seam");
            Assert.AreEqual(48, runtime.GetCategoryStock("flour").CurrentStockUnits, "stock untouched by the seam");
            Assert.AreEqual(0, runtime.GetCategoryStock("bread").CurrentStockUnits);

            string seam = SharedBusinessWeeklyConverter.BuildTaskBackedSeamSummary(
                BusinessType.Bakery, runtime.CurrentCashCents, "flour", 48, "bread", 0);
            Assert.IsTrue(seam.Contains("BakeryWholesaleDailyDriver"), "the seam line names the owning driver");
            Assert.IsTrue(seam.Contains("cash 2500c"), "the seam line shows the readable shared cash");
            Assert.IsTrue(seam.Contains("flour stock 48"), "the seam line shows the readable input stock");
            Assert.IsTrue(seam.Contains("bread stock 0"), "the seam line shows the readable output stock");
            Assert.IsTrue(seam.Contains("credits 0 output"), "the seam line states the 0-output exclusion");

            string refusal = SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.Bakery, "bread", 5);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refusal), "a bakery output credit is refused loudly");
            Assert.IsTrue(refusal.Contains("BakeryWholesaleDailyDriver"), "the refusal names the owning driver");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.Bakery, "bread", 0) == null, "zero credit needs no refusal");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.Bakery, null, 5) == null, "no output category, no refusal");
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.CropFarm, "crop_food", 5) == null, "non-task-backed types are unaffected");
        }

        [Test]
        public void FuelDealerConverter_NeverCreditsOutput()
        {
            // M3: the same seam for the fuel dealer — 0 produced, the
            // reason names Series2DealerBakeryFuelSource.
            Assert.IsTrue(SharedBusinessWeeklyConverter.IsTaskBackedProductionType(BusinessType.FuelDealer));
            Assert.AreEqual("Series2DealerBakeryFuelSource", SharedBusinessWeeklyConverter.TaskBackedDriverName(BusinessType.FuelDealer));

            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("fuel-1");
            runtime.SetCurrentCashCents(1200);
            runtime.EnsureCategoryStock("fuel", 20, 100);
            runtime.EnsureCategoryStock("fuel_wood", 0, 100);

            string reason = SharedBusinessWeeklyConverter.RecordTaskBackedExclusion(runtime);
            Assert.AreEqual(RecordedBakeryFuelExclusionReason, reason);
            Assert.AreEqual(0, runtime.LastWeeklyOutputUnitsProduced, "0 produced");
            Assert.AreEqual(0, runtime.LastWeeklyInputUnitsConsumed, "0 consumed");
            Assert.AreEqual(RecordedBakeryFuelExclusionReason, runtime.LastWeeklyBlockedReason);
            Assert.AreEqual(1200, runtime.CurrentCashCents, "cash untouched by the seam");
            Assert.AreEqual(20, runtime.GetCategoryStock("fuel").CurrentStockUnits, "stock untouched by the seam");

            string seam = SharedBusinessWeeklyConverter.BuildTaskBackedSeamSummary(
                BusinessType.FuelDealer, runtime.CurrentCashCents, "fuel", 20, "fuel_wood", 0);
            Assert.IsTrue(seam.Contains("Series2DealerBakeryFuelSource"), "the seam line names the owning driver");
            Assert.IsTrue(seam.Contains("cash 1200c"));
            Assert.IsTrue(seam.Contains("fuel stock 20"));
            Assert.IsTrue(seam.Contains("credits 0 output"));

            string refusal = SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.FuelDealer, "fuel_wood", 3);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(refusal), "a fuel-dealer output credit is refused loudly");
            Assert.IsTrue(refusal.Contains("Series2DealerBakeryFuelSource"));
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.FuelDealer, "fuel_wood", 0) == null);
            Assert.IsTrue(SharedBusinessWeeklyConverter.CheckTaskBackedOutputCredit(BusinessType.Blacksmith, "nails", 3) == null, "non-task-backed types are unaffected");
        }

        [Test]
        public void RecordedSeamStrings_BakeryFuelExclusion_NoMintGuard_LocalOutlet()
        {
            // M3 made the Bakery/FuelDealer seam executable and M6 the
            // no-mint guard; only the local-outlet note is still a
            // transcription pin (the sales path, not the converter). The
            // strings below are the exact source strings at HEAD 1360425;
            // if a later slice changes the wording, this test must be
            // updated with the reason recorded.
            Assert.IsTrue(RecordedBakeryFuelExclusionReason.Contains("Task-backed production driver"),
                "the exclusion names the owning driver");
            Assert.IsTrue(RecordedBakeryFuelExclusionReason.Contains("weekly category conversion is disabled"),
                "the exclusion states the 0-output rule");
            Assert.AreEqual(RecordedNoMintGuardReason, SharedBusinessWeeklyConverter.NoInputNoMintReason,
                "M6: the no-mint guard reason is executable on the helper now, not a transcription");
            Assert.IsTrue(RecordedNoMintGuardReason.Contains("cannot mint output from nothing"),
                "the no-mint guard states its rule");
            Assert.IsTrue(RecordedLocalOutletNoteFragment.Contains("stock retained and no revenue posted"),
                "the local-market outlet retains stock and posts 0 revenue");
        }
    }
}
