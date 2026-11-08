// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice 6 — demand observation tests (directive P3, P9 plan
// Slice 6, Priority F). Pins: the observation book's record shape and
// duplicate refusal, completed sales vs observed demand vs stockout unmet
// demand recorded separately (driven through the real authorities on the
// S6-1 chain fixture), partial fills with requested vs supplied, the
// day-of-week pattern and the trend from recorded history only, the same
// unresolved need counted once across retries (not once per retry), empty
// shelves with demand recording unmet demand and never zero demand, the
// F3-log derivation and the gaps it cannot supply, and acceptance legs Q
// (observation plus reserve change nothing in any live authority) and R
// (16-sack haul, 125c settlement, 12 lb to 24 loaves, all unchanged).
//
// The book is fed by explicit Record calls (the tests are the caller) and
// by derivation from the F3 custody log. No live flow is instrumented in
// this slice: the requested-vs-supplied split and the stockout flag are
// not in the F3 log, so they cannot be derived from it — the gap is named
// in DemandObservation and in test FromCustodyLog_DerivesCompletedSales_
// NamesGaps instead of restructuring a site.

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class DemandObservationTests
    {
        private const int Day = 12;
        private const int EthanId = 7;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string StoreInstanceId = "general-store-1";
        private const string WagonId = "opening-freight-wagon";
        private const string ShipmentId = "freight-shipment:request-1";
        private const string AgreementId = "freight-agreement:request-1";
        private const string LotId = "aidlot-millbrook_grain_co:chain-flour-16";
        private const string OrderRequestId = "chain-flour-16";
        private const int Sacks = 16;
        private const int FreightChargeCents = 125;
        private const int HouseholdId = 7;
        private const int ShopperPersonId = 1;

        private static DemandAttemptRecord SampleAttempt(
            int dayIndex = 12,
            string needId = "household:7/need:0",
            string attemptId = "",
            int requested = 10,
            int supplied = 10,
            bool stockWasEmpty = false)
        {
            return new DemandAttemptRecord(
                dayIndex, StoreInstanceId, HouseholdItemCatalog.BreadId, HouseholdId,
                needId, attemptId, requested, supplied, stockWasEmpty, string.Empty);
        }

        private static bool RecordAttempt(DemandObservation book, HouseholdPurchasingNeed need,
            int dayIndex, int requested, int supplied, bool stockWasEmpty)
        {
            return book.Record(new DemandAttemptRecord(
                dayIndex, StoreInstanceId, HouseholdItemCatalog.BreadId, HouseholdId,
                DemandObservation.NeedKey(need.HouseholdId, need.NeedSequence),
                string.Empty, requested, supplied, stockWasEmpty, string.Empty),
                out string diagnostic);
        }

        // Drives the real authorities up to a stocked store: Millbrook
        // order, freight, bake — the same legs as the F3 chain test.
        private static Chain DriveToStockedStore(CustodyTransferLog log)
        {
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            return s;
        }

        [Test]
        public void RecordShape_FieldsAndRecordId()
        {
            var book = new DemandObservation();
            Assert.AreEqual(0, book.Count);
            Assert.IsTrue(book.Record(SampleAttempt(), out string diag), diag);
            Assert.AreEqual(1, book.Count);
            DemandAttemptRecord attempt = book.Records[0];
            Assert.AreEqual("demand-1", attempt.RecordId);
            Assert.AreEqual(12, attempt.DayIndex);
            Assert.AreEqual(StoreInstanceId, attempt.BusinessId);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, attempt.ItemId);
            Assert.AreEqual(HouseholdId, attempt.HouseholdId);
            Assert.AreEqual("household:7/need:0", attempt.NeedId);
            Assert.AreEqual(10, attempt.RequestedUnits);
            Assert.AreEqual(10, attempt.SuppliedUnits);
            Assert.AreEqual(0, attempt.UnmetUnits, "a full fill leaves no unmet demand");
            Assert.IsTrue(!attempt.IsPartialFill, "a full fill is not a partial fill");
            Assert.IsTrue(!attempt.IsStockoutAttempt, "a supplied attempt is not a stockout");
        }

        [Test]
        public void DuplicateRefusal_SameNeedSameDay_SecondRefused()
        {
            var book = new DemandObservation();
            Assert.IsTrue(book.Record(SampleAttempt(), out _));
            Assert.IsTrue(!book.Record(SampleAttempt(), out string diagnostic),
                "the same attempt recorded twice must be refused");
            Assert.IsTrue(diagnostic.Contains("duplicate"), "the refusal must name itself as a duplicate: " + diagnostic);
            Assert.AreEqual(1, book.Count, "the book never double-records an attempt");
            Assert.AreEqual("demand-1", book.Records[0].RecordId);
        }

        [Test]
        public void SameNeedDifferentDay_Accepted()
        {
            var book = new DemandObservation();
            Assert.IsTrue(book.Record(SampleAttempt(dayIndex: 12), out _));
            Assert.IsTrue(book.Record(SampleAttempt(dayIndex: 13), out string diag),
                "a genuine retry on a later day is a new attempt in the audit trail: " + diag);
            Assert.AreEqual(2, book.Count);
            Assert.AreEqual("demand-2", book.Records[1].RecordId);
        }

        [Test]
        public void Validation_BadRecords_Refused()
        {
            var book = new DemandObservation();
            Assert.IsTrue(!book.Record(null, out _), "a null record must be refused");
            Assert.IsTrue(!book.Record(SampleAttempt(requested: 0), out _),
                "an attempt requesting zero units is not an attempt");
            Assert.IsTrue(!book.Record(SampleAttempt(requested: -3), out _),
                "an attempt requesting negative units must be refused");
            Assert.IsTrue(!book.Record(SampleAttempt(requested: 10, supplied: 11), out _),
                "a sale cannot supply more than the attempt requested");
            Assert.IsTrue(!book.Record(SampleAttempt(supplied: -1), out _),
                "a negative supply must be refused");
            Assert.IsTrue(!book.Record(new DemandAttemptRecord(12, string.Empty,
                HouseholdItemCatalog.BreadId, HouseholdId, "household:7/need:0", string.Empty,
                10, 10, false, string.Empty), out _),
                "an attempt with no business is not attributable");
            Assert.IsTrue(!book.Record(new DemandAttemptRecord(12, StoreInstanceId,
                string.Empty, HouseholdId, "household:7/need:0", string.Empty,
                10, 10, false, string.Empty), out _),
                "an attempt with no item is not attributable");
            Assert.IsTrue(!book.Record(SampleAttempt(dayIndex: -1), out string dayDiag),
                "an attempt with no day cannot be placed in history: " + dayDiag);
            Assert.AreEqual(0, book.Count);
            Assert.AreEqual(8, book.Diagnostics.Count, "every refusal is named in diagnostics");
        }

        [Test]
        public void DedupFallback_NoNeedId_UsesHouseholdItemDay()
        {
            var book = new DemandObservation();
            Assert.IsTrue(book.Record(SampleAttempt(needId: string.Empty), out _));
            Assert.IsTrue(!book.Record(SampleAttempt(needId: string.Empty), out string diag),
                "with no need id, household plus item plus day is the dedup key: " + diag);
            Assert.IsTrue(book.Record(SampleAttempt(needId: string.Empty, dayIndex: 13), out _),
                "a later day is a new attempt");
            Assert.IsTrue(book.Record(new DemandAttemptRecord(12, StoreInstanceId,
                HouseholdItemCatalog.BreadId, 8, string.Empty, string.Empty,
                10, 10, false, string.Empty), out _),
                "another household on the same day is a different attempt");
            Assert.AreEqual(3, book.Count);
        }

        [Test]
        public void Chain_S61_PartialFill_ObservedDemandVsSales()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "24 loaves on the shelves before the purchase");

            FundHousehold(s, 1000);
            int stockBefore = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(trip.PartialFillReason),
                "the partial fill names its reason: " + trip.PartialFillReason);
            HouseholdPurchasingNeed need = s.Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need, "the need stays open after a partial fill");
            Assert.AreEqual(6, need.UnitsNeeded, "30 wanted, 24 bought, 6 still needed");

            var book = new DemandObservation();
            Assert.IsTrue(RecordAttempt(book, need, Day, 30, trip.UnitsAcquired, stockBefore <= 0),
                "the attempt records");
            Assert.AreEqual(24, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "completed sales are the 24 loaves actually sold");
            Assert.AreEqual(30, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "observed demand is the 30 loaves attempted, including the partial fill");
            Assert.AreEqual(0, book.StockoutUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the shelves held 24 at the attempt, so this is not an empty-shelf stockout");
            Assert.AreEqual(6, book.OtherUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "6 loaves wanted but not supplied, stock-limited");
            IReadOnlyList<DemandAttemptRecord> partials =
                book.PartialFills(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(1, partials.Count, "one partial fill");
            Assert.AreEqual(30, partials[0].RequestedUnits, "the partial fill records what was requested");
            Assert.AreEqual(24, partials[0].SuppliedUnits, "the partial fill records what was supplied");
            IReadOnlyList<DailyDemandPoint> history =
                book.DailyHistory(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(Day, history[0].DayIndex);
            Assert.AreEqual(30, history[0].RequestedUnits);
            Assert.AreEqual(24, history[0].SuppliedUnits);
            Assert.AreEqual(6, history[0].UnmetUnits);
            Assert.AreEqual(0, history[0].StockoutUnmetUnits);
        }

        [Test]
        public void Chain_StockoutRetry_UnmetDemandRecordedOnce()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            FundHousehold(s, 1000);

            // Day 12: the household wants 30, the store holds 24 — a
            // stock-limited partial fill, shelves not empty at the attempt.
            int stockBefore = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            ShoppingTripResult first = BuyBread(s, 30, Day);
            Assert.IsTrue(first.Success, "first purchase succeeds; failure: " + first.FailureReason);
            Assert.AreEqual(24, first.UnitsAcquired);
            HouseholdPurchasingNeed need = s.Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need, "the need stays open after the partial fill");
            var book = new DemandObservation();
            Assert.IsTrue(RecordAttempt(book, need, Day, 30, first.UnitsAcquired, stockBefore <= 0));

            // Day 13: the same need retries for its remaining 6 loaves;
            // the shelves are now empty, so nothing is supplied.
            Assert.AreEqual(0, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the shelves are empty on the retry");
            Assert.AreEqual(6, need.UnitsNeeded, "6 loaves still needed");
            ShoppingTripResult retry = s.Loop.ExecuteNeed(need, Day + 1, ShoppingOptions(s));
            Assert.AreEqual(0, retry.UnitsAcquired, "empty shelves supply nothing");
            Assert.IsTrue(RecordAttempt(book, need, Day + 1, need.UnitsNeeded, retry.UnitsAcquired,
                s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits <= 0));

            Assert.AreEqual(24, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "only the 24 loaves actually sold count as sales");
            Assert.AreEqual(30, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the need is observed once (30), not once per retry (30 + 6)");
            Assert.AreEqual(6, book.StockoutUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the 6 outstanding loaves are unmet because the shelves were empty on the retry");
            Assert.AreEqual(0, book.OtherUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the whole shortfall is the stockout, not a second bucket");
            IReadOnlyList<DailyDemandPoint> history =
                book.DailyHistory(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(2, history.Count, "one point per day with an attempt");
            Assert.AreEqual(30, history[0].RequestedUnits, "the demand is attributed to day 12, its first attempt");
            Assert.AreEqual(24, history[0].SuppliedUnits);
            Assert.AreEqual(6, history[0].UnmetUnits);
            Assert.AreEqual(6, history[0].StockoutUnmetUnits);
            Assert.AreEqual(0, history[1].RequestedUnits, "the retry adds no new demand");
            Assert.AreEqual(0, history[1].SuppliedUnits);
            Assert.AreEqual(0, history[1].UnmetUnits, "the unmet demand is not counted twice");
        }

        [Test]
        public void StockoutSequence_EmptyShelves_RecordUnmetNeverZeroDemand()
        {
            // No freight, no bakery: the store starts with empty shelves
            // and the real shopping loop is driven against it.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            FundHousehold(s, 1000);
            Assert.AreEqual(0, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the shelves start empty");

            ShoppingTripResult first = BuyBread(s, 14, Day);
            Assert.AreEqual(0, first.UnitsAcquired, "empty shelves supply nothing");
            HouseholdPurchasingNeed need = s.Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need, "the need stays open after a stockout");
            Assert.AreEqual(14, need.UnitsNeeded, "nothing was bought, so all 14 are still needed");

            var book = new DemandObservation();
            Assert.IsTrue(RecordAttempt(book, need, Day, 14, first.UnitsAcquired, true));
            Assert.AreEqual(0, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "zero sales — the shelves were empty");
            Assert.AreEqual(14, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "zero sales with a stockout must NOT infer zero demand: the 14 wanted loaves are observed");
            Assert.AreEqual(14, book.StockoutUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the 14 wanted loaves are stockout unmet demand");

            // Day 13: the same unresolved need retries and still gets
            // nothing. It must not be counted as a new purchase attempt.
            ShoppingTripResult retry = s.Loop.ExecuteNeed(need, Day + 1, ShoppingOptions(s));
            Assert.AreEqual(0, retry.UnitsAcquired, "the shelves are still empty");
            Assert.IsTrue(RecordAttempt(book, need, Day + 1, need.UnitsNeeded, retry.UnitsAcquired, true));
            Assert.AreEqual(14, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the same unresolved need counts once, not once per retry");
            Assert.AreEqual(14, book.StockoutUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "one unmet-demand entry of 14, not two of 14");
            Assert.AreEqual(2, book.Count, "both attempts stay in the audit trail; only the aggregates dedup");
        }

        [Test]
        public void PartialFills_RecordRequestedVsSupplied()
        {
            var book = new DemandObservation();
            Assert.IsTrue(book.Record(SampleAttempt(needId: "household:7/need:1", requested: 10, supplied: 10), out _));
            Assert.IsTrue(book.Record(SampleAttempt(needId: "household:7/need:2", requested: 30, supplied: 24), out _));
            Assert.IsTrue(book.Record(SampleAttempt(needId: "household:7/need:3", requested: 14, supplied: 0, stockWasEmpty: true), out _));
            Assert.IsTrue(book.Record(SampleAttempt(needId: "household:7/need:4", requested: 8, supplied: 5), out _));
            IReadOnlyList<DemandAttemptRecord> partials =
                book.PartialFills(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(2, partials.Count, "the full fill and the zero-supply stockout are not partial fills");
            Assert.AreEqual(30, partials[0].RequestedUnits);
            Assert.AreEqual(24, partials[0].SuppliedUnits);
            Assert.AreEqual(8, partials[1].RequestedUnits);
            Assert.AreEqual(5, partials[1].SuppliedUnits);
            Assert.AreEqual(10 + 24 + 5, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(10 + 30 + 14 + 8, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void DayOfWeekPattern_FromRecordedHistory()
        {
            var book = new DemandObservation();
            // Days 0-13, requested = day + 1, one distinct need per day.
            // Day-of-week index is ((day % 7) + 7) % 7, the same indexing
            // the shopping loop uses (HouseholdShopping.cs:208).
            for (int day = 0; day < 14; day++)
            {
                Assert.IsTrue(book.Record(SampleAttempt(dayIndex: day,
                    needId: "household:7/need:" + day, requested: day + 1, supplied: day + 1), out string diag),
                    "day " + day + " must record: " + diag);
            }
            double[] pattern = book.DayOfWeekPattern(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(7, pattern.Length, "one average per day-of-week index");
            Assert.AreEqual(4.5, pattern[0], 0.001, "days 0 and 7 requested 1 and 8: average 4.5");
            Assert.AreEqual(5.5, pattern[1], 0.001, "days 1 and 8 requested 2 and 9");
            Assert.AreEqual(6.5, pattern[2], 0.001, "days 2 and 9 requested 3 and 10");
            Assert.AreEqual(7.5, pattern[3], 0.001, "days 3 and 10 requested 4 and 11");
            Assert.AreEqual(8.5, pattern[4], 0.001, "days 4 and 11 requested 5 and 12");
            Assert.AreEqual(9.5, pattern[5], 0.001, "days 5 and 12 requested 6 and 13");
            Assert.AreEqual(10.5, pattern[6], 0.001, "days 6 and 13 requested 7 and 14");
        }

        [Test]
        public void Trend_RisingFromRecordedHistory()
        {
            var book = new DemandObservation();
            // Six days: 10/day for the first half, 20/day for the second.
            for (int day = 0; day < 6; day++)
            {
                int requested = day < 3 ? 10 : 20;
                Assert.IsTrue(book.Record(SampleAttempt(dayIndex: day,
                    needId: "household:7/need:" + day, requested: requested, supplied: requested), out _));
            }
            DemandTrend trend = book.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 6, 5);
            Assert.AreEqual(DemandTrendDirection.Rising, trend.Direction);
            Assert.AreEqual(10.0, trend.MagnitudeUnitsPerDay, 0.001, "second half 20/day minus first half 10/day");
            Assert.AreEqual(10.0, trend.FirstHalfAverageUnitsPerDay, 0.001);
            Assert.AreEqual(20.0, trend.SecondHalfAverageUnitsPerDay, 0.001);
            Assert.AreEqual(6, trend.DaysObserved);
            Assert.AreEqual(0, trend.WindowStartDay);
            Assert.AreEqual(5, trend.WindowEndDay);
        }

        [Test]
        public void Trend_FallingFlatAndUnknown()
        {
            var falling = new DemandObservation();
            for (int day = 0; day < 6; day++)
            {
                int requested = day < 3 ? 20 : 10;
                Assert.IsTrue(falling.Record(SampleAttempt(dayIndex: day,
                    needId: "household:7/need:" + day, requested: requested, supplied: requested), out _));
            }
            DemandTrend fall = falling.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 6, 5);
            Assert.AreEqual(DemandTrendDirection.Falling, fall.Direction);
            Assert.AreEqual(-10.0, fall.MagnitudeUnitsPerDay, 0.001);

            var flat = new DemandObservation();
            for (int day = 0; day < 4; day++)
            {
                Assert.IsTrue(flat.Record(SampleAttempt(dayIndex: day,
                    needId: "household:7/need:" + day, requested: 12, supplied: 12), out _));
            }
            DemandTrend even = flat.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 4, 3);
            Assert.AreEqual(DemandTrendDirection.Flat, even.Direction);
            Assert.AreEqual(0.0, even.MagnitudeUnitsPerDay, 0.001);

            var sparse = new DemandObservation();
            Assert.IsTrue(sparse.Record(SampleAttempt(dayIndex: 0), out _));
            DemandTrend unknown = sparse.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 6, 5);
            Assert.AreEqual(DemandTrendDirection.Unknown, unknown.Direction,
                "one observed day cannot show a direction");
            Assert.IsTrue(unknown.Reason.Contains("history"), "the Unknown names its cause: " + unknown.Reason);

            var empty = new DemandObservation();
            DemandTrend none = empty.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 6, 5);
            Assert.AreEqual(DemandTrendDirection.Unknown, none.Direction,
                "no history means no trend — history is recorded fact, never forecast");
            DemandTrend shortWindow = flat.Trend(StoreInstanceId, HouseholdItemCatalog.BreadId, 1, 3);
            Assert.AreEqual(DemandTrendDirection.Unknown, shortWindow.Direction,
                "a one-day window cannot split into halves");
        }

        [Test]
        public void History_DailyWeeklyMonthly_Sums()
        {
            var book = new DemandObservation();
            // Days 0-34: 10 requested and 10 supplied every day except
            // day 5 (30 requested, 20 supplied) and day 31 (stockout 10/0).
            for (int day = 0; day < 35; day++)
            {
                int requested = 10;
                int supplied = 10;
                bool stockout = false;
                if (day == 5) { requested = 30; supplied = 20; }
                if (day == 31) { supplied = 0; stockout = true; }
                Assert.IsTrue(book.Record(SampleAttempt(dayIndex: day,
                    needId: "household:7/need:" + day, requested: requested, supplied: supplied,
                    stockWasEmpty: stockout), out string diag), "day " + day + ": " + diag);
            }
            IReadOnlyList<DailyDemandPoint> daily =
                book.DailyHistory(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(35, daily.Count);
            Assert.AreEqual(30, daily[5].RequestedUnits);
            Assert.AreEqual(20, daily[5].SuppliedUnits);
            Assert.AreEqual(10, daily[5].UnmetUnits);
            Assert.AreEqual(10, daily[31].UnmetUnits);
            Assert.AreEqual(10, daily[31].StockoutUnmetUnits);
            Assert.AreEqual(0, daily[31].SuppliedUnits);

            IReadOnlyList<DemandHistoryPoint> weekly =
                book.WeeklyHistory(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(5, weekly.Count, "days 0-34 span 5 seven-day blocks");
            Assert.AreEqual(0, weekly[0].PeriodIndex);
            Assert.AreEqual(0, weekly[0].StartDayIndex);
            Assert.AreEqual(90, weekly[0].RequestedUnits, "week 0: six days at 10 plus day 5 at 30");
            Assert.AreEqual(80, weekly[0].SuppliedUnits, "week 0: six days at 10 plus day 5 at 20");
            Assert.AreEqual(10, weekly[0].UnmetUnits);
            Assert.AreEqual(4, weekly[4].PeriodIndex);
            Assert.AreEqual(28, weekly[4].StartDayIndex);
            Assert.AreEqual(70, weekly[4].RequestedUnits, "week 4: seven days at 10");
            Assert.AreEqual(60, weekly[4].SuppliedUnits, "week 4: six days at 10 plus day 31 at 0");
            Assert.AreEqual(10, weekly[4].StockoutUnmetUnits);

            IReadOnlyList<DemandHistoryPoint> monthly =
                book.MonthlyHistory(StoreInstanceId, HouseholdItemCatalog.BreadId);
            Assert.AreEqual(2, monthly.Count, "days 0-34 span 2 thirty-day blocks");
            Assert.AreEqual(0, monthly[0].PeriodIndex);
            Assert.AreEqual(320, monthly[0].RequestedUnits, "month 0: twenty-nine days at 10 plus day 5 at 30");
            Assert.AreEqual(310, monthly[0].SuppliedUnits, "month 0: twenty-nine days at 10 plus day 5 at 20");
            Assert.AreEqual(1, monthly[1].PeriodIndex);
            Assert.AreEqual(30, monthly[1].StartDayIndex);
            Assert.AreEqual(50, monthly[1].RequestedUnits, "month 1: five days at 10");
            Assert.AreEqual(40, monthly[1].SuppliedUnits, "month 1: four days at 10 plus day 31 at 0");
        }

        [Test]
        public void FromCustodyLog_DerivesCompletedSales_NamesGaps()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(new CustodyTransferRecord(12, HouseholdItemCatalog.BreadId, 24,
                LotKey.Empty, LotKey.Empty,
                CustodyTransferLog.BusinessCustodian(StoreInstanceId),
                CustodyTransferLog.PersonCustodian(ShopperPersonId),
                "receipt-1", CustodyTransferLog.CauseRetailPurchase), out _));
            Assert.IsTrue(log.Record(new CustodyTransferRecord(12, HouseholdItemCatalog.BreadId, 6,
                LotKey.Empty, LotKey.Empty,
                CustodyTransferLog.BusinessCustodian(StoreInstanceId),
                CustodyTransferLog.PersonCustodian(ShopperPersonId),
                "receipt-2", CustodyTransferLog.CauseRetailPurchase), out _));
            Assert.IsTrue(log.Record(new CustodyTransferRecord(13, HouseholdItemCatalog.BreadId, 10,
                LotKey.Empty, LotKey.Empty,
                CustodyTransferLog.BusinessCustodian(StoreInstanceId),
                CustodyTransferLog.PersonCustodian(ShopperPersonId),
                "receipt-3", CustodyTransferLog.CauseRetailPurchase), out _));
            // A freight commit is not a sale and must not enter the book.
            Assert.IsTrue(log.Record(new CustodyTransferRecord(12, "bakery_flour_sack", 16,
                LotKey.Empty, LotKey.Empty,
                CustodyTransferLog.SupplierCustodian("millbrook_grain_co"),
                CustodyTransferLog.WagonCustodian(WagonId),
                ShipmentId, CustodyTransferLog.CauseFreightCommit), out _));
            // A purchase with no day cannot be placed in history.
            Assert.IsTrue(log.Record(new CustodyTransferRecord(CustodyTransferLog.UnknownDayIndex,
                HouseholdItemCatalog.BreadId, 4,
                LotKey.Empty, LotKey.Empty,
                CustodyTransferLog.BusinessCustodian(StoreInstanceId),
                CustodyTransferLog.PersonCustodian(ShopperPersonId),
                "receipt-4", CustodyTransferLog.CauseRetailPurchase), out _));

            DemandObservation book = DemandObservation.FromCustodyLog(log);
            Assert.AreEqual(30, book.CompletedSales(StoreInstanceId, HouseholdItemCatalog.BreadId, 12),
                "day 12 sold 24 + 6");
            Assert.AreEqual(10, book.CompletedSales(StoreInstanceId, HouseholdItemCatalog.BreadId, 13));
            Assert.AreEqual(40, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId));
            Assert.AreEqual(40, book.ObservedDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the log carries no requested-vs-supplied split, so observed demand is the sales floor");
            Assert.AreEqual(0, book.StockoutUnmetDemandTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the log carries no shelf state, so no stockout can be derived from it");
            Assert.AreEqual(3, book.Count, "three dated purchases; the freight commit and the dateless purchase are excluded");
            string diagnostics = string.Join(" | ", book.Diagnostics);
            Assert.IsTrue(diagnostics.Contains("requested"), "the missing requested-vs-supplied split is named: " + diagnostics);
            Assert.IsTrue(diagnostics.Contains("household"), "the missing household id is named: " + diagnostics);
            Assert.IsTrue(diagnostics.Contains("stockout"), "the missing shelf state is named: " + diagnostics);
            Assert.IsTrue(diagnostics.Contains("day"), "the skipped dateless purchase is named: " + diagnostics);
        }

        [Test]
        public void Chain_Q_ObservationAndReserveChangeNothing()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.AreEqual(24, trip.UnitsAcquired);

            int storeCashBefore = s.StoreRuntime.CurrentCashCents;
            int householdCashBefore = s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();
            int flourBefore = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int storeStockBefore = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            int householdBreadBefore = s.Inventories.GetOrCreate(HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.BreadId);
            int payerCashBefore = s.PayerRuntime.CurrentCashCents;
            int carrierCashBefore = s.CarrierRuntime.CurrentCashCents;
            int supplierCashBefore = s.Supplier.CashCents;
            int logCountBefore = log.Count;

            // Both halves, exercised in full: derive observation from the
            // log, record the purchase attempt explicitly, bridge to reserve
            // inputs, and compute a projection. The two feeds live in
            // separate books: the log-derived attempt is keyed by its
            // receipt id and the explicit attempt by its need id, and no
            // mapping between a need id and a receipt id exists in this
            // slice — mixing both feeds for the same purchase on one book
            // would count its 24 loaves twice.
            DemandObservation logBook = DemandObservation.FromCustodyLog(log);
            Assert.AreEqual(24, logBook.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId),
                "the log-derived book holds the one 24-loaf purchase");
            var book = new DemandObservation();
            HouseholdPurchasingNeed need = s.Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need);
            Assert.IsTrue(RecordAttempt(book, need, Day, 30, trip.UnitsAcquired, false));
            Assert.AreEqual(24, book.CompletedSalesTotal(StoreInstanceId, HouseholdItemCatalog.BreadId));
            ReserveInputs inputs = ReserveInputs.FromObservation(book, StoreInstanceId,
                HouseholdItemCatalog.BreadId, 1, Day, out string bridgeDiag);
            Assert.IsTrue(inputs.RecentDemandPerDay.IsKnown, "one day of demand bridges: " + bridgeDiag);
            ReserveProjection projection = ReserveProjection.Compute(inputs);
            Assert.IsTrue(!projection.RecommendedReserveUnits.IsKnown,
                "no lead time was supplied, so no reserve can be computed — the projection says Unknown instead of inventing one");
            Assert.IsTrue(string.Join(" | ", projection.Reasoning).Contains("lead time"),
                "the projection names the missing lead time: " + string.Join(" | ", projection.Reasoning));

            Assert.AreEqual(storeCashBefore, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(householdCashBefore, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(),
                "household cash unchanged");
            Assert.AreEqual(flourBefore, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bakery flour unchanged");
            Assert.AreEqual(storeStockBefore, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "store stock unchanged");
            Assert.AreEqual(householdBreadBefore, s.Inventories.GetOrCreate(HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.BreadId), "household bread unchanged");
            Assert.AreEqual(payerCashBefore, s.PayerRuntime.CurrentCashCents, "payer cash unchanged");
            Assert.AreEqual(carrierCashBefore, s.CarrierRuntime.CurrentCashCents, "carrier cash unchanged");
            Assert.AreEqual(supplierCashBefore, s.Supplier.CashCents, "supplier cash unchanged");
            Assert.AreEqual(logCountBefore, log.Count, "the custody log itself is untouched by the read");
        }

        [Test]
        public void Chain_R_FiguresUnchanged()
        {
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);

            PlaceFreightOrder(s);
            Assert.AreEqual(Sacks, s.Lot.PackageUnits, "16-sack haul");
            Assert.AreEqual(Sacks * 10, s.Lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, s.Lot.TotalCostCents, "16 sacks x 48c");

            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "the 125c freight charge settles exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "the 125c freight charge is credited exactly once");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds 160 lb");

            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "12 lb of flour bakes 24 loaves");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb: 12 dispensed, 148 remaining");
        }

        // ---- shared chain fixture: the real authorities, merged from
        // FreightDeliveryPaymentAuditTests (freight) and
        // BakeryFoodChainEndToEndTests (bakery + shopping). ----

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }

            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;

            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents) return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }

            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        // Production port over a real BusinessRuntimeState, copied from
        // BakeryFoodChainEndToEndTests: the production GeneralStoreTradingPort
        // wraps a MonoBehaviour manager and cannot be constructed in Roslyn.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Chain
        {
            public List<string> Diag = new List<string>();
            public CustodyTransferLog Log;
            public EntityIdRegistry Ids = new EntityIdRegistry();

            // Millbrook + freight.
            public OffMapAidBusinessRuntime Supplier;
            public FakeBusinessCashPort BakeryCash;
            public OffMapAidCargoLot Lot;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentReservationService Reservations;
            public FreightProductionDriver Freight;
            public FreightShipmentHooks Hooks;
            public AnimalState Horse;
            public EquipmentAsset Wagon;
            public LogisticsShipmentState Shipment;
            public FreightServiceAgreement Agreement;
            public BusinessRuntimeState CarrierRuntime;
            public BusinessRuntimeState PayerRuntime;
            public JourneyModel JourneyModel;
            public OffMapJourneyAuthority Journeys;

            // Bakery production + store.
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public Series2BakeryProductionDriver ProductionDriver;

            // Shopping.
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel ShoppingJourneys;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored Millbrook asset
            // (10 lb / 18 bakery units / 48c / 230 starting), as in
            // MillbrookFlourFreightCustodyTests.
            var definition = ScriptableObject.CreateInstance<OffMapAidBusinessDefinition>();
            definition.BusinessId = "millbrook_grain_co";
            definition.DisplayName = "Millbrook Grain Co.";
            definition.PickupRouteMilesFromTown = 50f;
            definition.Inventory = new List<OffMapAidInventoryRow>
            {
                new OffMapAidInventoryRow
                {
                    ItemId = "bakery_flour_sack", DisplayName = "Flour",
                    InventoryUnit = "lb", StartingUnits = 230,
                    OrderUnit = "10 lb sack", InventoryUnitsPerOrderUnit = 10,
                    BakeryUnitsPerOrderUnit = 18, OrderPriceCents = 48,
                },
            };
            return definition;
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BusinessRuntimeState Runtime(string id, int cash)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(id);
            runtime.SetCurrentCashCents(cash);
            return runtime;
        }

        private static Chain NewChain(CustodyTransferLog log)
        {
            var s = new Chain { Log = log };
            s.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            s.Supplier.CustodyLog = log;
            s.BakeryCash = new FakeBusinessCashPort(20000);
            s.Bakery.CustodyLog = log;
            s.Adapter = new MillbrookFlourFreightCustodyAdapter(s.Supplier, s.Bakery, s.Ids, log);

            // Freight (mirrors FreightDeliveryPaymentAuditTests.SetUp).
            s.Tasks = new TaskAuthority();
            s.Budgets = new WorkTimeBudgetStore();
            s.Budgets.EnsureDay(Day);
            s.Budgets.EnsureDay(Day + 1);
            s.Budgets.EnsureDay(Day + 2);
            s.Reservations = new EquipmentReservationService();
            s.Horse = new AnimalRegistry(s.Ids).RegisterAnimal(AnimalSpecies.Horse, "Draft", AnimalSex.Male,
                AnimalOwnerKind.Business, CarrierId, Day, "Custody chain fixture");
            Assert.IsNotNull(s.Horse);
            Assert.IsNull(HorseAuthority.Configure(s.Horse, HorseUseCapabilities.GeneralPurpose,
                "ethan-staging", "freight-company-stable", Day));
            s.Horse.TransportState = HorseTransportState.AttachedToWagon;
            s.Horse.AttachedWagonAssetId = WagonId;
            s.Wagon = new EquipmentAsset
            {
                AssetId = WagonId, Kind = "freight-wagon", DisplayName = "Opening Freight Wagon",
                Condition01 = 1f, OwnerKind = "business", OwnerId = CarrierId,
                LocationId = "ethan-staging", HitchPreparationShipmentId = ShipmentId,
                HitchingAnimalIds = new List<string> { s.Horse.AnimalId.ToString() }
            };
            Assert.IsNull(WagonAuthority.Configure(s.Wagon, WagonSubtype.GeneralFarmFreight.ToString(),
                450f, 4f, "ethan-staging"));
            s.CarrierRuntime = Runtime(CarrierId, 0);
            s.PayerRuntime = Runtime(BakeryId, 20000);
            s.Freight = new FreightProductionDriver();
            s.Freight.EnsureCompany(CarrierId, s.CarrierRuntime);
            s.Freight.RegisterBusinessRuntime(BakeryId, s.PayerRuntime);
            s.Freight.Service.RegisterWagon(s.Wagon);
            s.Hooks = new FreightShipmentHooks
            {
                Tasks = s.Tasks,
                Budgets = s.Budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                Reservations = s.Reservations,
                NowGameSeconds = Day * 86400f + 8 * 3600f,
                CommitSourceLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.CommitSourceLot(shipment, qty, out reason),
                ReceiveCargoLot = (LogisticsShipmentState shipment, int qty, out string reason) =>
                    s.Adapter.ReceiveCargoLot(shipment, qty, Day, out reason),
            };
            s.JourneyModel = new JourneyModel();
            Assert.IsNull(s.JourneyModel.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f)));
            RegionalTownRegistry.ApplyAll(s.JourneyModel, new List<string>());
            s.Journeys = new OffMapJourneyAuthority(s.JourneyModel);

            // Bakery production + store (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.StoreRuntime.EnsureCategoryStock("animal_feed", 0, 0);
            s.BakeryRuntime.BindBusinessIdentity(BakeryId);
            s.BakeryRuntime.AddCashCents(2000);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));

            // Shopping (mirrors BakeryFoodChainEndToEndTests.NewSlice).
            s.Population = new PopulationState();
            s.Population.people.Add(new PersonState
            {
                id = ShopperPersonId, firstName = "Glen", lastName = "Test", age = 34,
                ageBand = AgeBand.Adult18Plus, householdId = HouseholdId,
            });
            s.Population.households.Add(new HouseholdState { id = HouseholdId });
            s.Membership = new HouseholdMembershipRegistry();
            Assert.IsNull(s.Membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = ShopperPersonId, HouseholdId = HouseholdId, StartDayIndex = 0,
            }));
            s.Ledgers = new HouseholdLedgerRegistry();
            s.Inventories = new HouseholdInventoryRegistry();
            s.Needs = new HouseholdNeedRegistry();
            s.ShoppingJourneys = new JourneyModel();
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            s.ShoppingJourneys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            Assert.IsNull(s.ShoppingJourneys.AddEdge("home", "general-store", 2.0f, "river road"));
            // One shared TaskAuthority serves the freight service and the
            // shopping loop, as in production.
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(s.Directory, s.StorePort);
            s.Knowledge.LearnSupplier(ShopperPersonId, StoreInstanceId, "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.Loop.CustodyLog = log;
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static void PlaceFreightOrder(Chain s)
        {
            Assert.IsNull(s.Supplier.TryPlaceOrder(
                OrderRequestId, BakeryId, "Glen Pierce", Sacks, s.BakeryCash, 0, out OffMapAidCargoLot lot),
                "fixture order must place");
            s.Lot = lot;
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(Sacks, lot.PackageUnits);
            Assert.AreEqual(Sacks * 10, lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(Sacks * 48, lot.TotalCostCents, "16 sacks x 48c");
        }

        private static void BuildFreightShipment(Chain s)
        {
            FreightServiceAgreementBook book = s.Freight.ServiceAgreements;
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = BakeryId, CarrierBusinessId = CarrierId,
                PickupBusinessId = "millbrook_grain_co", DestinationBusinessId = BakeryId,
                CargoLotId = LotId, Quantity = Sacks, QuantityUnit = "lb", RequestingPersonId = 2,
                CarrierOperatorPersonId = EthanId, PickupLocationId = "millbrook-pickup",
                DestinationLocationId = "bakery-door",
                CargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = FreightChargeCents, PayerBusinessId = BakeryId, PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept(request.RequestId, EthanId, Day, 40000f, out s.Agreement));
            s.Shipment = s.Freight.CreateShipment(CarrierId, ShipmentId, "millbrook_grain_co", BakeryId, Sacks,
                new FreightCargoSpec { MassKgPerUnit = 4.54f, VolumeM3PerUnit = 0.02f }, WagonId,
                s.Hooks, EntityId.For(EntityKind.Person, EthanId), new List<EntityId> { s.Horse.AnimalId },
                FreightChargeCents, BakeryId, s.Diag, out string shipmentRefusal,
                AgreementId, LotId, MillbrookFlourFreightCustodyAdapter.FlourSkuId, true);
            Assert.IsNotNull(s.Shipment, shipmentRefusal);
            Assert.IsNull(book.LinkShipment(s.Agreement.AgreementId, ShipmentId));
            Assert.IsNull(book.MarkPreparedForDispatch(s.Agreement.AgreementId, ShipmentId, 50000f));
            s.Freight.SetDispatchPlan(ShipmentId, EntityId.For(EntityKind.Person, EthanId),
                new List<EntityId> { s.Horse.AnimalId });
        }

        private static void BindAndAuthorize(Chain s)
        {
            string bind = WagonAuthority.TryAssignDraftTeam(s.Wagon,
                new List<AnimalState> { s.Horse },
                EntityId.For(EntityKind.Person, EthanId).ToString(), ShipmentId,
                new AnimalAvailabilityAuthority(s.Reservations), Day * 86400f + 8 * 3600f, float.MaxValue);
            Assert.IsNull(bind, "fixture bind must succeed");
            Assert.IsTrue(s.Freight.AuthorizeShipmentDispatch(ShipmentId, out _), "fixture authorization must succeed");
        }

        private static List<LogisticsShipmentStatus> TickToCompleted(Chain s)
        {
            var trace = new List<LogisticsShipmentStatus>();
            for (int tick = 0; tick < 8 && s.Shipment.Status != LogisticsShipmentStatus.Completed; tick++)
            {
                s.Freight.AdvanceTick(86400f, s.Hooks, EntityId.For(EntityKind.Person, EthanId), s.Diag);
                trace.Add(s.Shipment.Status);
            }
            return trace;
        }

        // The normal production path up to the return-day tick loop: journey
        // departed day 10, lot committed at the supplier on D+1 (day 11)
        // through the adapter — the same call the pickup drive makes —
        // team bound and authorized, journey returned on its due day, the
        // off-map hold lifted.
        private static void DriveFreightToReturnDay(Chain s)
        {
            OffMapJourneyRecord record = s.Journeys.DepartVehicle(
                WagonId, "town-center", "Millbrook Grain Co.", 0,
                "Millbrook flour pickup", null,
                new List<string> { AgreementId },
                10, null, null, s.Diag);
            Assert.IsNotNull(record, "Millbrook departure should succeed: " + string.Join("; ", s.Diag));
            record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = Sacks;

            // The D+1 pickup's first half, as the drive performs it: commit
            // the lot through the adapter (day 11), then apply the loading
            // writes — exactly WorkLoading's post-commit writes, no task.
            Assert.IsTrue(s.Adapter.CommitSourceLot(s.Shipment, s.Shipment.PlannedQuantityUnits, out string reason, 11),
                reason);
            s.Shipment.sourceLotDebitApplied = true;
            s.Shipment.loadApplied = true;
            s.Shipment.sourceCommittedAtSchedule = true;
            s.Shipment.remainingQuantityUnits = s.Shipment.PlannedQuantityUnits;
            s.Shipment.physicalLocationId = "wagon:" + WagonId;

            BindAndAuthorize(s);
            List<OffMapJourneyRecord> returned = s.Journeys.CompleteDueJourneys(Day, null, s.Diag);
            Assert.AreEqual(1, returned.Count, "the journey must complete exactly once on its due day");
            Assert.AreEqual(OffMapJourneyState.Returned, record.State);
            s.Hooks.IsVehicleOffMap = _ => false;
        }

        private static void InstallOven(Chain s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
        }

        private static void SeedNotionsAndFuel(Chain s)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
        }

        private static void BookAccount(Chain s, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
        }

        private static BakeryWholesaleDailyInputs InputsFor(Chain s, int dayIndex) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = new MillbrookFlourQuote
                {
                    SacksAvailable = 0,
                    GoodsPricePerSackCents = null,
                    OneWayMiles = 50f,
                    FreightQuoteCents = 125,
                    FreightAvailable = false,
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
            };

        private static ShoppingLoopOptions ShoppingOptions(Chain s) =>
            new ShoppingLoopOptions
            {
                PersonLocationId = pid => s.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => s.PersonLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = s.Diag,
            };

        private static void FundHousehold(Chain s, int cents)
        {
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(HouseholdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
        }

        private static ShoppingTripResult BuyBread(Chain s, int units, int dayIndex)
        {
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, dayIndex);
            need.UnitsNeeded = units;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            return s.Loop.ExecuteNeed(need, dayIndex, ShoppingOptions(s));
        }

        private static void AssertKindsInOrder(CustodyTransferLog log, params string[] expectedKinds)
        {
            Assert.AreEqual(expectedKinds.Length, log.Count,
                "one record per transfer, in order; got: " + string.Join(", ", LogKinds(log)));
            for (int i = 0; i < expectedKinds.Length; i++)
            {
                Assert.AreEqual(expectedKinds[i], log.Records[i].CauseKind,
                    $"record {i} must be {expectedKinds[i]}; got: " + string.Join(", ", LogKinds(log)));
            }
        }

        private static IEnumerable<string> LogKinds(CustodyTransferLog log)
        {
            foreach (CustodyTransferRecord record in log.Records) yield return record.CauseKind;
        }

        private static CustodyTransferRecord RecordAt(CustodyTransferLog log, int index) => log.Records[index];

        // The flour bin holds flour AND notions lots: find the flour lot by
        // name instead of indexing Lots[0].
        private static BakeryFlourLot FlourLot(Chain s)
        {
            BakeryFlourLot found = null;
            int flourCount = 0;
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                {
                    flourCount++;
                    found = lot;
                }
            }
            Assert.AreEqual(1, flourCount, "exactly one flour lot is expected in the bin");
            Assert.IsNotNull(found);
            return found;
        }

    }
}
