// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority P7 — test and harness improvements (last slice, corrected order
// step 6). Hardening tests around the completed physical-inventory systems.
// Every test below drives a real production authority — never a copy of a
// production algorithm in a fixture:
//
//   (a) tick sizes: PerishableLotState.ApplyElapsedTime, the deterministic
//       simulation-time deterioration function the bakery chain ages
//       through (AgeShelf), driven with the same elapsed time split into
//       different subdivisions. All subdivisions use exact binary
//       fractions (0.5 / 0.25 / 0.125 days) so the comparison is exact,
//       not a floating-point tolerance argument.
//   (b) repeated transfers: HouseholdInventory.AddLot / SplitLot /
//       MergeCompatibleLots, the real household-storage lot authorities,
//       driven through split/merge round-trips on the same lot.
//   (c) stockouts: DemandObservation, the real unmet-demand book —
//       empty shelves record unmet demand (never zero demand) and the
//       same unresolved need is counted once across retries.
//   (d) perishable condition: PerishableLotState.TransferredTo / Split
//       plus the HouseholdInventory condition attachment — condition
//       survives each transfer, is never reset, and is never improved
//       by mixing.
//   (e) forecast correctness: ReserveProjection computed from a recorded
//       DemandObservation history matches that history, and a
//       SupplierComparison ranking matches the figures the real catalog
//       quote authority holds.
//
// Deferred, named not papered over:
//   - A person-custody-to-household-storage RETURN leg (taking a stored
//     lot back into a person's custody) has no production authority to
//     drive: HouseholdShoppingLoop has no take-from-household operation
//     and HouseholdInventory has no transfer-out-to-custody operation,
//     so no fixture-only test pretends one exists. The forward leg
//     (purchase, carry, deposit exactly once) is pinned by the V1 and
//     U-3 slices; the return leg needs an owner decision, not a test.
//   - A loop-level tick-size test on the V1 workflow: the shopping loop
//     takes no tick-size parameter, so there is no subdivision seam in
//     the loop itself. Tick invariance is pinned at the deterioration
//     authority the workflow ages through (ApplyElapsedTime).
//   - A stockout driven through the live shopping loop: no live flow
//     records demand observations (Slice 6: fed by explicit Record
//     calls and by derivation from the F3 custody log), so loop-level
//     stockout recording would need a production change, which this
//     slice forbids. The book-level behaviour is pinned here.

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class PhysicalInventoryHardeningTests
    {
        private const string StoreId = "general-store-1";
        private const string FlourItem = "bakery_flour_sack";
        private const int RevenuePerSackCents = 100;

        private static PerishableLotState BreadState(string lotId, int units, int producedDay,
            PerishablePhysicalCondition condition)
        {
            var state = new PerishableLotState(
                new LotKey("bakery:bakery_001", lotId), BakeryBreadCatalog.BreadLoafId, units, producedDay);
            state = state.WithConditions(condition, condition);
            state = state.WithSafety(PerishableSafetyState.Safe, string.Empty);
            return state;
        }

        private static PerishableLotState Advance(PerishableLotState state, double days)
        {
            return PerishableLotState.ApplyElapsedTime(state, days, string.Empty);
        }

        private static PerishableLotState AdvanceInTicks(PerishableLotState state, double totalDays, double tickDays)
        {
            PerishableLotState current = state;
            double remaining = totalDays;
            while (remaining > 0.0)
            {
                double step = Math.Min(tickDays, remaining);
                current = Advance(current, step);
                remaining -= step;
            }
            return current;
        }

        private static bool Record(DemandObservation book, int day, string needId,
            int requested, int supplied, bool stockWasEmpty)
        {
            return book.Record(new DemandAttemptRecord(day, StoreId,
                HouseholdItemCatalog.BreadId, 7, needId, string.Empty,
                requested, supplied, stockWasEmpty, string.Empty), out string diag);
        }

        private static HouseholdItemLot LotById(HouseholdInventory inventory, int lotId)
        {
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot != null && lot.LotId == lotId) return lot;
            }
            return null;
        }

        private static int TotalUnits(HouseholdInventory inventory)
        {
            int total = 0;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot != null) total += lot.QuantityUnits;
            }
            return total;
        }

        // (a) Tick sizes on the deterioration authority.

        [Test]
        public void TickSubdivision_SameElapsedTime_SameConditionAndUnits()
        {
            PerishableLotState oneShot = Advance(BreadState("lot-a", 14, 0, PerishablePhysicalCondition.Fresh), 2.0);
            PerishableLotState halves = AdvanceInTicks(BreadState("lot-a", 14, 0, PerishablePhysicalCondition.Fresh), 2.0, 0.5);
            PerishableLotState quarters = AdvanceInTicks(BreadState("lot-a", 14, 0, PerishablePhysicalCondition.Fresh), 2.0, 0.25);
            PerishableLotState eighths = AdvanceInTicks(BreadState("lot-a", 14, 0, PerishablePhysicalCondition.Fresh), 2.0, 0.125);

            foreach (PerishableLotState state in new[] { oneShot, halves, quarters, eighths })
            {
                Assert.AreEqual(PerishablePhysicalCondition.Stale, state.PhysicalCondition,
                    "2 days old is Stale under the authored AgeToDay rule, however the time was subdivided");
                Assert.AreEqual(14, state.Units, "elapsed time alone never changes the quantity");
                Assert.AreEqual(2.0, state.AsOfDay, "the as-of day is the same total elapsed time");
                Assert.IsTrue(!state.RemainingUsefulLife.IsKnown, "no authored remaining life is invented by ticking");
                Assert.AreEqual(PerishableSafetyState.Safe, state.SafetyState,
                    "elapsed time alone never changes the safety state (constructed Safe, stays Safe)");
            }
            Assert.IsTrue(oneShot.EqualsState(halves), "one 2-day tick equals four half-day ticks, field for field");
            Assert.IsTrue(oneShot.EqualsState(quarters), "one 2-day tick equals eight quarter-day ticks, field for field");
            Assert.IsTrue(oneShot.EqualsState(eighths), "one 2-day tick equals sixteen eighth-day ticks, field for field");
        }

        [Test]
        public void TickSubdivision_DayBoundary_Exact()
        {
            PerishableLotState oneShot = Advance(BreadState("lot-b", 10, 0, PerishablePhysicalCondition.Fresh), 1.0);
            PerishableLotState halves = AdvanceInTicks(BreadState("lot-b", 10, 0, PerishablePhysicalCondition.Fresh), 1.0, 0.5);
            PerishableLotState quarters = AdvanceInTicks(BreadState("lot-b", 10, 0, PerishablePhysicalCondition.Fresh), 1.0, 0.25);
            foreach (PerishableLotState state in new[] { oneShot, halves, quarters })
            {
                Assert.AreEqual(PerishablePhysicalCondition.DayOld, state.PhysicalCondition,
                    "exactly 1 day old is DayOld, however the day was subdivided");
            }
            Assert.IsTrue(oneShot.EqualsState(halves));
            Assert.IsTrue(oneShot.EqualsState(quarters));

            PerishableLotState threeQuarters = AdvanceInTicks(
                BreadState("lot-b", 10, 0, PerishablePhysicalCondition.Fresh), 0.75, 0.25);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, threeQuarters.PhysicalCondition,
                "0.75 days is still Fresh — the boundary is the whole day, not the tick count");
            Assert.AreEqual(0.75, threeQuarters.AsOfDay);
        }

        [Test]
        public void TickSubdivision_NonBreadItem_NeverAges()
        {
            var flour = new PerishableLotState(
                new LotKey("millbrook:millbrook_grain_co", "lot-flour"), HouseholdItemCatalog.FlourId, 160, 0);
            PerishableLotState oneShot = Advance(flour, 2.0);
            PerishableLotState quarters = AdvanceInTicks(flour, 2.0, 0.25);
            foreach (PerishableLotState state in new[] { oneShot, quarters })
            {
                Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                    "flour has no authored aging rule, so elapsed time changes nothing");
                Assert.AreEqual(160, state.Units);
                Assert.AreEqual(2.0, state.AsOfDay);
                Assert.AreEqual(PerishableSafetyState.Unknown, state.SafetyState);
            }
            Assert.IsTrue(oneShot.EqualsState(quarters), "the non-bread result is tick-invariant too");
        }

        // (b) Repeated transfers of the same lot through household storage.

        [Test]
        public void SplitMerge_RoundTrip_ConservesUnitsIdentityAndCondition()
        {
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 14, 1, "store",
                string.Empty, string.Empty, -1, BreadState("lot-0", 14, 1, PerishablePhysicalCondition.Fresh)));
            Assert.AreEqual(1, inventory.Lots.Count);
            Assert.AreEqual(14, TotalUnits(inventory));

            int childId = inventory.SplitLot(0, 6, 2);
            Assert.AreEqual(1, childId, "the split child takes the next lot id");
            Assert.AreEqual(2, inventory.Lots.Count);
            Assert.AreEqual(14, TotalUnits(inventory), "a split moves units, it never creates or destroys them");
            HouseholdItemLot parent = LotById(inventory, 0);
            HouseholdItemLot child = LotById(inventory, 1);
            Assert.IsNotNull(parent);
            Assert.IsNotNull(child);
            Assert.AreEqual(8, parent.QuantityUnits);
            Assert.AreEqual(6, child.QuantityUnits);
            Assert.IsTrue(parent.SourceLabel.Contains("authority:household-inventory/lot:0"),
                "the parent keeps its own canonical key: " + parent.SourceLabel);
            Assert.IsTrue(child.SourceLabel.Contains("authority:household-inventory/lot:1"),
                "the child carries its own canonical key: " + child.SourceLabel);
            Assert.IsTrue(!parent.SourceLabel.Contains("authority:household-inventory/lot:1"),
                "the parent never gains the child's key");
            Assert.IsTrue(child.AncestryLotIds.Contains(0), "the child records the parent as lineage");
            PerishableLotState parentState = PerishableLotState.FromSaveDto(parent.PerishableCondition);
            PerishableLotState childState = PerishableLotState.FromSaveDto(child.PerishableCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, parentState.PhysicalCondition);
            Assert.AreEqual(8, parentState.Units, "the split divides the condition state with the lot");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, childState.PhysicalCondition);
            Assert.AreEqual(6, childState.Units);

            Assert.AreEqual(1, inventory.MergeCompatibleLots(3), "the two same-condition parts merge back into one lot");
            Assert.AreEqual(1, inventory.Lots.Count);
            Assert.AreEqual(14, TotalUnits(inventory));
            HouseholdItemLot survivor = LotById(inventory, 0);
            Assert.IsNotNull(survivor, "the oldest lot survives the merge — lot identity is stable");
            Assert.AreEqual(14, survivor.QuantityUnits);
            Assert.IsTrue(survivor.AncestryLotIds.Contains(1), "the merged-away child's id stays in the lineage");
            PerishableLotState survivorState = PerishableLotState.FromSaveDto(survivor.PerishableCondition);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, survivorState.PhysicalCondition,
                "merging back never resets the condition");
            Assert.AreEqual(14, survivorState.Units, "the survivor's attached state tracks the merged quantity");
        }

        [Test]
        public void SplitTwice_ThenMerge_AllLineageKept()
        {
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 14, 1, "store",
                string.Empty, string.Empty, -1, BreadState("lot-0", 14, 1, PerishablePhysicalCondition.Fresh)));
            Assert.AreEqual(1, inventory.SplitLot(0, 6, 2));
            Assert.AreEqual(2, inventory.SplitLot(0, 5, 2));
            Assert.AreEqual(3, inventory.Lots.Count);
            Assert.AreEqual(14, TotalUnits(inventory), "3 + 6 + 5 is still 14 after two splits");
            Assert.AreEqual(3, LotById(inventory, 0).QuantityUnits);
            Assert.AreEqual(6, LotById(inventory, 1).QuantityUnits);
            Assert.AreEqual(5, LotById(inventory, 2).QuantityUnits);

            Assert.AreEqual(2, inventory.MergeCompatibleLots(3), "both split parts merge back");
            Assert.AreEqual(1, inventory.Lots.Count);
            Assert.AreEqual(14, TotalUnits(inventory));
            HouseholdItemLot survivor = LotById(inventory, 0);
            Assert.IsNotNull(survivor);
            Assert.IsTrue(survivor.AncestryLotIds.Contains(1), "first split child kept in lineage");
            Assert.IsTrue(survivor.AncestryLotIds.Contains(2), "second split child kept in lineage");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh,
                PerishableLotState.FromSaveDto(survivor.PerishableCondition).PhysicalCondition);
        }

        [Test]
        public void TwoLots_SameItem_SeparateIdentities_SplitIsolates()
        {
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "store"));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "store",
                string.Empty, string.Empty, -1, BreadState("lot-1", 10, 1, PerishablePhysicalCondition.Fresh)));
            Assert.AreEqual(2, inventory.Lots.Count);
            Assert.AreEqual(20, TotalUnits(inventory));

            int childId = inventory.SplitLot(1, 4, 2);
            Assert.AreEqual(2, childId);
            Assert.AreEqual(10, LotById(inventory, 0).QuantityUnits, "splitting lot 1 never touches lot 0");
            Assert.IsTrue(!LotById(inventory, 0).HasPerishableCondition,
                "the conditionless lot stays conditionless");
            Assert.AreEqual(6, LotById(inventory, 1).QuantityUnits);
            Assert.AreEqual(4, LotById(inventory, 2).QuantityUnits);
            Assert.AreEqual(20, TotalUnits(inventory), "no duplication, no loss across the split");
            Assert.IsTrue(LotById(inventory, 1).SourceLabel.Contains("authority:household-inventory/lot:1"));
            Assert.IsTrue(LotById(inventory, 2).SourceLabel.Contains("authority:household-inventory/lot:2"));
            Assert.IsTrue(LotById(inventory, 2).SourceLabel.Contains("authority:household-inventory/lot:1"),
                "the child keeps the parent key as lineage: " + LotById(inventory, 2).SourceLabel);
        }

        // (c) Stockout behaviour through the real unmet-demand book.

        [Test]
        public void Stockout_EmptyShelves_RecordsUnmetDemandNeverZero()
        {
            var book = new DemandObservation();
            Assert.IsTrue(Record(book, 5, "household:7/need:1", 14, 0, true),
                "the empty-shelf attempt records");
            Assert.AreEqual(14, book.ObservedDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "empty shelves record the demand that was there — never zero demand");
            Assert.AreEqual(14, book.ObservedDemand(StoreId, HouseholdItemCatalog.BreadId, 5));
            Assert.AreEqual(14, book.StockoutUnmetDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "all 14 wanted loaves are unmet because the shelves were empty");
            Assert.AreEqual(14, book.StockoutUnmetDemand(StoreId, HouseholdItemCatalog.BreadId, 5));
            Assert.AreEqual(0, book.CompletedSalesTotal(StoreId, HouseholdItemCatalog.BreadId),
                "nothing was sold");
            Assert.AreEqual(0, book.OtherUnmetDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "the shortfall is a stockout, not another cause");
            IReadOnlyList<DailyDemandPoint> history = book.DailyHistory(StoreId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(14, history[0].RequestedUnits);
            Assert.AreEqual(0, history[0].SuppliedUnits);
            Assert.AreEqual(14, history[0].UnmetUnits);
        }

        [Test]
        public void SameUnresolvedNeed_Retried_CountsOnce()
        {
            var book = new DemandObservation();
            Assert.IsTrue(Record(book, 5, "household:7/need:1", 14, 0, true), "first attempt records");
            Assert.IsTrue(!Record(book, 5, "household:7/need:1", 14, 0, true),
                "the identical attempt on the same day is refused as a duplicate");
            Assert.IsTrue(Record(book, 6, "household:7/need:1", 14, 0, true),
                "the retry on the next day is a new attempt in the log");
            Assert.AreEqual(14, book.ObservedDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "the same unresolved need is counted once, not once per retry");
            Assert.AreEqual(14, book.StockoutUnmetDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "the stockout shortfall is the one need's 14, not 28");
            Assert.AreEqual(0, book.CompletedSalesTotal(StoreId, HouseholdItemCatalog.BreadId));
            IReadOnlyList<DailyDemandPoint> history = book.DailyHistory(StoreId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual(14, history[0].RequestedUnits, "demand is attributed to the first day");
            Assert.AreEqual(0, history[1].RequestedUnits,
                "the retry-only day shows zero requested — the demand was already counted");
            Assert.AreEqual(0, history[1].UnmetUnits, "the retry-only day shows zero unmet for the same reason");
        }

        [Test]
        public void PartialFill_SplitsStockoutFromOtherUnmet()
        {
            var book = new DemandObservation();
            Assert.IsTrue(Record(book, 5, "household:7/need:1", 30, 24, false),
                "a partial fill records");
            Assert.AreEqual(1, book.PartialFills(StoreId, HouseholdItemCatalog.BreadId).Count);
            Assert.AreEqual(30, book.ObservedDemandTotal(StoreId, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(24, book.CompletedSalesTotal(StoreId, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(0, book.StockoutUnmetDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "shelves were not empty, so none of the shortfall is a stockout");
            Assert.AreEqual(6, book.OtherUnmetDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "the 6 unsupplied loaves are other-cause unmet demand");
        }

        // (d) Perishable condition through repeated transfers.

        [Test]
        public void ConditionSurvives_RepeatedTransfers_NeverReset()
        {
            PerishableLotState state = BreadState("lot-c", 14, 0, PerishablePhysicalCondition.Fresh);
            state = state.TransferredTo("person:101", 0, "carried home by the shopper");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, state.PhysicalCondition,
                "the first transfer never resets the condition");
            state = state.TransferredTo("household:7", 0, "deposited to household storage");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, state.PhysicalCondition,
                "the second transfer never resets it either");
            Assert.AreEqual(14, state.Units, "transfers move units, they never change the count");
            Assert.AreEqual(2, state.HandlingHistory.Count, "each transfer is recorded in the handling history");

            state = Advance(state, 1.0);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, state.PhysicalCondition,
                "one elapsed day ages the bread to DayOld");
            state = state.TransferredTo("person:101", 1, "taken out again");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, state.PhysicalCondition,
                "a transfer never rejuvenates DayOld back to Fresh");
            Assert.AreEqual(3, state.HandlingHistory.Count);

            (PerishableLotState remaining, PerishableLotState transferred) = state.Split(6, 1);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, remaining.PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, transferred.PhysicalCondition,
                "a split divides the condition, it never averages or freshens either part");
            Assert.AreEqual(8, remaining.Units);
            Assert.AreEqual(6, transferred.Units);
        }

        [Test]
        public void Mixing_NeverImprovesOrAverages()
        {
            var inventory = new HouseholdInventory(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "store",
                string.Empty, string.Empty, -1, BreadState("lot-f", 10, 1, PerishablePhysicalCondition.Fresh)));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "store",
                string.Empty, string.Empty, -1, BreadState("lot-d", 10, 1, PerishablePhysicalCondition.DayOld)));
            Assert.AreEqual(2, inventory.Lots.Count);

            Assert.AreEqual(0, inventory.MergeCompatibleLots(2),
                "Fresh and DayOld lots never merge — mixing must not average the condition");
            Assert.AreEqual(2, inventory.Lots.Count);
            Assert.AreEqual(20, TotalUnits(inventory));
            HouseholdItemLot fresh = LotById(inventory, 0);
            HouseholdItemLot dayOld = LotById(inventory, 1);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh,
                PerishableLotState.FromSaveDto(fresh.PerishableCondition).PhysicalCondition);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld,
                PerishableLotState.FromSaveDto(dayOld.PerishableCondition).PhysicalCondition,
                "the DayOld lot is not improved by sitting next to a Fresh lot");
        }

        [Test]
        public void ConditionSurvives_RepeatedSaveDtoRoundTrips()
        {
            PerishableLotState aged = Advance(
                BreadState("lot-e", 12, 0, PerishablePhysicalCondition.Fresh), 1.0);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, aged.PhysicalCondition);
            PerishableLotState restored = aged;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                restored = PerishableLotState.FromSaveDto(restored.CaptureSaveDto());
            }
            Assert.IsTrue(aged.EqualsState(restored),
                "three save/load round-trips preserve the condition field for field");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, restored.PhysicalCondition);
            Assert.AreEqual(12, restored.Units);
            Assert.AreEqual(1.0, restored.AsOfDay);
        }

        // (e) Forecast correctness against recorded history and quoted figures.

        [Test]
        public void ReserveProjection_MatchesRecordedHistory()
        {
            var book = new DemandObservation();
            int[] dailyRequested = { 8, 10, 12, 10, 8, 12, 10 };
            for (int day = 0; day < dailyRequested.Length; day++)
            {
                Assert.IsTrue(Record(book, day, "household:7/need:" + day,
                    dailyRequested[day], dailyRequested[day], false), "day " + day + " records");
            }
            Assert.AreEqual(70, book.ObservedDemandTotal(StoreId, HouseholdItemCatalog.BreadId),
                "the history the projection reads is 8+10+12+10+8+12+10 = 70");
            Assert.IsTrue(book.TryAverageDailyDemand(StoreId, HouseholdItemCatalog.BreadId, 7, 6, out double average));
            Assert.AreEqual(10.0, average, "the average daily demand in the history is exactly 10");
            Assert.IsTrue(book.TryDailyDemandStdDev(StoreId, HouseholdItemCatalog.BreadId, 7, 6, out double stdDev));
            Assert.AreEqual(Math.Sqrt(16.0 / 7.0), stdDev, "population spread of the recorded days: sqrt(16/7)");

            ReserveInputs inputs = ReserveInputs.FromObservation(book, StoreId,
                HouseholdItemCatalog.BreadId, 7, 6, out string diagnostic);
            Assert.IsTrue(inputs.RecentDemandPerDay.IsKnown, "the bridge derives the demand rate: " + diagnostic);
            Assert.AreEqual(10, inputs.RecentDemandPerDay.Units, "the bridged rate is the history's average");
            Assert.IsTrue(inputs.DemandUncertaintyUnits.IsKnown);
            Assert.AreEqual(2, inputs.DemandUncertaintyUnits.Units,
                "the bridged uncertainty is the ceiling of the history's spread (1.51.. -> 2)");

            inputs.ReplenishmentLeadTimeDays = LeadTimeDays.Known(2.0);
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(projection.RecommendedReserveUnits.IsKnown);
            Assert.AreEqual(22, projection.RecommendedReserveUnits.Units,
                "10/day over a 2-day lead time covers 20, plus the measured uncertainty buffer of 2");
            Assert.AreEqual(10, inputs.RecentDemandPerDay.Units,
                "computing the projection never mutates its inputs");
            Assert.AreEqual(2, inputs.DemandUncertaintyUnits.Units);
        }

        [Test]
        public void SupplierRanking_MatchesAuthorityQuoteFigures()
        {
            var catalog = new OffMapSupplierCatalog();
            Assert.IsTrue(catalog.RegisterSupplier(new OffMapSupplier
            {
                SupplierId = "near-mill-supplier",
                SupplierName = "Near Mill Supplier",
                DistanceMiles = 100,
                OfferedSkuIds = new List<string> { FlourItem },
                GoodsUnitPriceCents = 40,
            }));
            Assert.IsTrue(catalog.RegisterSupplier(new OffMapSupplier
            {
                SupplierId = "far-mill-supplier",
                SupplierName = "Far Mill Supplier",
                DistanceMiles = 300,
                OfferedSkuIds = new List<string> { FlourItem },
                GoodsUnitPriceCents = 30,
            }));
            List<OffMapFreightQuote> quotes = catalog.QuoteAll(FlourItem, 16, 5, 0);
            OffMapFreightQuote nearQuote = null;
            OffMapFreightQuote farQuote = null;
            foreach (OffMapFreightQuote quote in quotes)
            {
                if (quote.FreightOption != FreightOptionKind.SellerFreight) continue;
                if (quote.SupplierId == "near-mill-supplier") nearQuote = quote;
                if (quote.SupplierId == "far-mill-supplier") farQuote = quote;
            }
            Assert.IsNotNull(nearQuote, "the near supplier quotes seller freight");
            Assert.IsNotNull(farQuote, "the far supplier quotes seller freight");

            SupplierOption near = CatalogOption(nearQuote);
            SupplierOption far = CatalogOption(farQuote);
            foreach (SupplierOption option in new[] { near, far })
            {
                OffMapFreightQuote quote = option.SupplierId == "near-mill-supplier" ? nearQuote : farQuote;
                int handGoods = quote.GoodsCents;
                int handFreight = quote.FreightCents;
                int handRevenue = 16 * RevenuePerSackCents;
                int handProfit = handRevenue - handGoods - handFreight;
                Assert.AreEqual(handGoods, option.ExpectedGoodsCostCents.Cents,
                    "the option's goods cost is the quote's own goods figure");
                Assert.AreEqual(handFreight, option.FreightCostCents.Cents,
                    "the option's freight is the quote's own freight figure (distance x caller rate)");
                Assert.AreEqual(handGoods + handFreight, option.ExpectedTotalCostCents.Cents);
                Assert.AreEqual(handProfit, option.ExpectedProfitCents.Cents,
                    "the option's profit is revenue minus the quote's own goods and freight");
            }
            Assert.AreEqual(460, near.ExpectedProfitCents.Cents,
                "near: 1600 revenue - 640 goods - 500 freight = 460");
            Assert.AreEqual(-380, far.ExpectedProfitCents.Cents,
                "far: 1600 revenue - 480 goods - 1500 freight = -380");

            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { far, near }, 0);
            Assert.AreEqual(2, result.Ranked.Count);
            Assert.AreEqual("near-mill-supplier", result.Ranked[0].Option.SupplierId,
                "the ranking follows expected profit, not unit price — the dearer near supplier wins");
            Assert.AreEqual(1, result.Ranked[0].Rank);
            Assert.AreEqual("far-mill-supplier", result.Ranked[1].Option.SupplierId);
            Assert.AreEqual(2, result.Ranked[1].Rank);
            Assert.IsTrue(result.Ranked[0].ExpectedProfitCents.Cents > result.Ranked[1].ExpectedProfitCents.Cents,
                "rank 1 out-profits rank 2 by the authorities' own figures");
        }

        private static SupplierOption CatalogOption(OffMapFreightQuote quote)
        {
            return SupplierOption.FromCatalogQuote(
                quote,
                LeadTimeDays.Known(2.0),
                SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("hardening test: no storage check supplied"),
                ReserveQuantity.Unknown,
                CentsAmount.Known(RevenuePerSackCents),
                "100c per sack = 20 loaves x 5c: the recorded bake converts 12 lb flour to 24 loaves, " +
                "so a 10 lb sack bakes 20 loaves, and the S6-1 sequence sells bread at 5c per loaf.",
                CentsAmount.Known(20000));
        }
    }
}
