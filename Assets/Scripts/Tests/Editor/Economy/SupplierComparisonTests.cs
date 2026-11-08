// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice P5 — supplier and procurement intelligence tests
// (directive P5). Pins: the comparison ranks by expected profit, not unit
// price (a dearer option that profits outranks a cheaper option that
// loses money after freight and spoilage); freight is taken from the
// actual freight authority computation (a settled
// LogisticsShipmentState.FreightChargeCents, or an OffMapFreightQuote
// from OffMapSupplierCatalog.QuoteAll — distance times a caller-supplied
// rate, never a canon rate); consolidated shipments are considered only
// where quoted; Unknown inputs are reported as Unknown with the option
// marked incomparable on that axis rather than ranked; reliability comes
// from F3 log history (a supplier with recorded late arrivals ranks below
// an equally priced reliable one, and no history means Unknown
// reliability, never assumed reliable); and acceptance legs Q (the
// comparison plus the plan change nothing in any live authority) and R
// (16-sack haul, 125c settlement, 12 lb to 24 loaves, all unchanged).
//
// Revenue basis used throughout (named, not invented): the recorded bake
// converts 12 lb of flour into 24 loaves (2 loaves per lb), so a 10 lb
// sack bakes 20 loaves, and the S6-1 sequence sells bread at 5c per loaf
// — 100c of revenue per sack. Lead times are caller-supplied (no authored
// lead time exists anywhere in v3.1); the tests supply 2 days and name it
// as a caller-supplied figure.

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
using LandLedgers.Economy.Storage;
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
    public sealed class SupplierComparisonTests
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

        private const string FlourItem = "bakery_flour_sack";
        private const string MillbrookId = "millbrook_grain_co";

        // 100c of revenue per sack: 20 loaves (12 lb -> 24 loaves, so a
        // 10 lb sack bakes 20) at the recorded 5c loaf price.
        private const int RevenuePerSackCents = 100;
        private const string RevenueBasis =
            "100c per sack = 20 loaves x 5c: the recorded bake converts 12 lb flour to 24 loaves, " +
            "so a 10 lb sack bakes 20 loaves, and the S6-1 sequence sells bread at 5c per loaf.";

        private static CentsAmount Cash(int cents) => CentsAmount.Known(cents);
        private static CentsAmount UnknownCash() => CentsAmount.Unknown;

        private static SupplierOption SampleOption(
            string supplierId = MillbrookId,
            string itemId = FlourItem,
            int quantityUnits = Sacks,
            int unitPriceCents = 48,
            int freightCents = FreightChargeCents,
            double leadTimeDays = 2.0,
            int revenuePerUnitCents = RevenuePerSackCents,
            int spoilageUnits = -1,
            int availableCashCents = 20000,
            bool consolidatedQuoted = false,
            bool consolidated = false,
            string reliabilitySupplierAuthority = null)
        {
            return new SupplierOption(
                supplierId: supplierId,
                itemId: itemId,
                quantityUnits: ReserveQuantity.Known(quantityUnits),
                quantityAvailable: ReserveQuantity.Unknown,
                unitPriceCents: unitPriceCents >= 0 ? CentsAmount.Known(unitPriceCents) : CentsAmount.Unknown,
                unitPriceSource: "test option: caller-supplied price, source named per test",
                freightCostCents: freightCents >= 0 ? CentsAmount.Known(freightCents) : CentsAmount.Unknown,
                freightSource: "test option: caller-supplied freight, source named per test",
                consolidatedShipmentQuoted: consolidatedQuoted,
                consolidatedShipment: consolidated,
                leadTimeDays: leadTimeDays >= 0.0 ? LeadTimeDays.Known(leadTimeDays) : LeadTimeDays.Unknown,
                quotedArrivalDay: ReserveQuantity.Unknown,
                reliability: SupplierReliability.Unknown,
                storageFit: SupplierStorageFit.Unknown("test option: no storage check supplied"),
                expectedSpoilageUnits: spoilageUnits >= 0 ? ReserveQuantity.Known(spoilageUnits) : ReserveQuantity.Unknown,
                revenuePerUnitCents: revenuePerUnitCents >= 0 ? CentsAmount.Known(revenuePerUnitCents) : CentsAmount.Unknown,
                revenueBasis: RevenueBasis,
                availableCashCents: availableCashCents >= 0 ? CentsAmount.Known(availableCashCents) : CentsAmount.Unknown,
                isSelfCollect: false);
        }

        private static string Reasoning(RankedSupplierOption ranked) =>
            string.Join(" | ", ranked.Reasoning);

        private static string ResultReasoning(SupplierComparisonResult result) =>
            string.Join(" | ", result.Diagnostics);

        // Two real suppliers quoting the same SKU through the actual quote
        // authority. Prices and distances are caller-supplied to the
        // catalog (its own doc: NOT a price list, NOT canon); the freight
        // rate is likewise caller-supplied (5c per mile, named here).
        private static OffMapSupplierCatalog TwoSupplierCatalog()
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
            return catalog;
        }

        private static OffMapFreightQuote SellerFreightQuote(
            OffMapSupplierCatalog catalog, string supplierId, int quantityUnits)
        {
            List<OffMapFreightQuote> quotes = catalog.QuoteAll(FlourItem, quantityUnits, 5, 0);
            foreach (OffMapFreightQuote quote in quotes)
            {
                if (string.Equals(quote.SupplierId, supplierId, StringComparison.Ordinal)
                    && quote.FreightOption == FreightOptionKind.SellerFreight)
                {
                    return quote;
                }
            }
            Assert.Fail($"no SellerFreight quote for {supplierId}");
            return null;
        }

        private static SupplierOption CatalogOption(OffMapFreightQuote quote)
        {
            return SupplierOption.FromCatalogQuote(
                quote,
                LeadTimeDays.Known(2.0),
                SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown,
                CentsAmount.Known(RevenuePerSackCents),
                RevenueBasis,
                CentsAmount.Known(20000));
        }

        private static CustodyTransferRecord FreightRecord(
            string causeKind, string causeId, string supplierId, int dayIndex)
        {
            var lotKey = new LotKey(
                CustodyTransferLog.MillbrookAuthority(supplierId), "aidlot-" + causeId);
            return new CustodyTransferRecord(
                dayIndex, FlourItem, Sacks,
                lotKey, lotKey,
                CustodyTransferLog.SupplierCustodian(supplierId),
                CustodyTransferLog.WagonCustodian(WagonId),
                causeId, causeKind);
        }

        // F3 log history for two suppliers: A commits day 11 and receives
        // day 12 (transit 1 day, on time against a 2-day lead); B commits
        // day 11 and receives day 20 (transit 9 days, late against the
        // same 2-day lead).
        private static CustodyTransferLog LogWithLateAndReliableSuppliers()
        {
            var log = new CustodyTransferLog();
            Assert.IsTrue(log.Record(FreightRecord(
                CustodyTransferLog.CauseFreightCommit, "ship-a", "supplier-a", 11), out string d0), d0);
            Assert.IsTrue(log.Record(FreightRecord(
                CustodyTransferLog.CauseFreightReceive, "ship-a", "supplier-a", 12), out string d1), d1);
            Assert.IsTrue(log.Record(FreightRecord(
                CustodyTransferLog.CauseFreightCommit, "ship-b", "supplier-b", 11), out string d2), d2);
            Assert.IsTrue(log.Record(FreightRecord(
                CustodyTransferLog.CauseFreightReceive, "ship-b", "supplier-b", 20), out string d3), d3);
            return log;
        }

        private static PerishableLotState Lot(string lotId, string itemId, int units,
            PerishablePhysicalCondition condition = PerishablePhysicalCondition.Fresh,
            PerishableSafetyState safety = PerishableSafetyState.Safe,
            UsefulLifeEstimate remainingLife = default(UsefulLifeEstimate))
        {
            var state = new PerishableLotState(
                new LotKey("bakery:bakery_001", lotId), itemId, units, 0);
            state = state.WithConditions(condition, condition);
            state = state.WithSafety(safety,
                safety == PerishableSafetyState.Unsafe ? "test evidence: unsafe" : string.Empty);
            if (remainingLife.IsKnown)
            {
                // Remaining life is set at construction only through the
                // DTO round-trip: capture, edit the DTO field, restore.
                PerishableLotState.SaveDto dto = state.CaptureSaveDto();
                dto.RemainingLifeKnown = true;
                dto.RemainingLifeDays = remainingLife.Days;
                state = PerishableLotState.FromSaveDto(dto);
            }
            return state;
        }

        [Test]
        public void MillbrookOption_FromRuntime_Quotes48cAndSettles125cFreight()
        {
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var shipment = new LogisticsShipmentState
            {
                shipmentId = ShipmentId,
                freightChargeCents = FreightChargeCents,
            };
            CentsAmount freight = SupplierOption.FreightFromShipment(shipment, out string freightReason);
            Assert.IsTrue(freight.IsKnown, freightReason);
            Assert.AreEqual(FreightChargeCents, freight.Cents, "the settled charge on the shipment is the freight figure");
            Assert.IsTrue(freightReason.Contains("freight-shipment"), "the freight source names the shipment: " + freightReason);

            SupplierOption option = SupplierOption.FromMillbrook(
                supplier, FlourItem, ReserveQuantity.Known(Sacks), freight, freightReason,
                LeadTimeDays.Known(2.0), SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown, CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000));
            Assert.AreEqual(MillbrookId, option.SupplierId);
            Assert.AreEqual(FlourItem, option.ItemId);
            Assert.IsTrue(option.UnitPriceCents.IsKnown, "the Millbrook table quotes a price per sack");
            Assert.AreEqual(48, option.UnitPriceCents.Cents, "48c per sack, from the Millbrook table");
            Assert.AreEqual(Sacks, option.QuantityUnits.Units);
            Assert.IsTrue(!option.QuantityAvailable.IsKnown,
                "Millbrook quotes a price per sack; the fillable quantity is decided at order time by TryPlaceOrder — no pre-order quantity quote exists");
            Assert.AreEqual(768, option.ExpectedGoodsCostCents.Cents, "16 sacks x 48c");
            Assert.AreEqual(1600, option.ExpectedRevenueCents.Cents, "16 sacks x 100c of derived loaf revenue");
            Assert.AreEqual(893, option.ExpectedTotalCostCents.Cents, "768c goods + 125c settled freight");
            Assert.IsTrue(option.ExpectedProfitCents.IsKnown);
            Assert.AreEqual(707, option.ExpectedProfitCents.Cents, "1600 - 768 - 125, spoilage unmeasured and excluded");
            Assert.IsTrue(option.ProfitExcludesUnmeasuredSpoilage,
                "Unknown spoilage is excluded from the profit and flagged, never silently counted as zero");
            Assert.AreEqual(14, option.ExpectedArrivalDay(Day).Units, "day 12 + 2-day caller-supplied lead time");
        }

        [Test]
        public void CatalogComparison_TwoSuppliersSameSku_ProfitBeatsUnitPrice()
        {
            OffMapSupplierCatalog catalog = TwoSupplierCatalog();
            SupplierOption near = CatalogOption(SellerFreightQuote(catalog, "near-mill-supplier", Sacks));
            SupplierOption far = CatalogOption(SellerFreightQuote(catalog, "far-mill-supplier", Sacks));

            // The quotes themselves are the authority's computation:
            // freight is distance times the caller-supplied 5c-per-mile rate.
            Assert.AreEqual(40, near.UnitPriceCents.Cents);
            Assert.AreEqual(500, near.FreightCostCents.Cents, "100 miles x 5c per mile");
            Assert.AreEqual(30, far.UnitPriceCents.Cents);
            Assert.AreEqual(1500, far.FreightCostCents.Cents, "300 miles x 5c per mile");

            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { far, near }, Day);
            Assert.AreEqual(2, result.Ranked.Count);
            Assert.AreEqual("near-mill-supplier", result.Ranked[0].Option.SupplierId);
            Assert.AreEqual(1, result.Ranked[0].Rank);
            Assert.AreEqual(460, result.Ranked[0].ExpectedProfitCents.Cents, "1600 - 640 - 500");
            Assert.AreEqual("far-mill-supplier", result.Ranked[1].Option.SupplierId);
            Assert.AreEqual(2, result.Ranked[1].Rank);
            Assert.AreEqual(-380, result.Ranked[1].ExpectedProfitCents.Cents, "1600 - 480 - 1500: a loss after freight");
            Assert.IsTrue(result.Ranked[1].Reasoning.Count > 0, "never a bare ranking: every option carries its reasoning");
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("freight"),
                "the dearer-per-unit loser is explained by its freight: " + Reasoning(result.Ranked[1]));
        }

        [Test]
        public void ProfitRanking_DearerMillbrookOutranksCheaperFarCatalog()
        {
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            CentsAmount freight = SupplierOption.FreightFromShipment(
                new LogisticsShipmentState { freightChargeCents = FreightChargeCents }, out string freightReason);
            SupplierOption millbrook = SupplierOption.FromMillbrook(
                supplier, FlourItem, ReserveQuantity.Known(Sacks), freight, freightReason,
                LeadTimeDays.Known(2.0), SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown, CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(20000));
            OffMapSupplierCatalog catalog = TwoSupplierCatalog();
            SupplierOption far = CatalogOption(SellerFreightQuote(catalog, "far-mill-supplier", Sacks));

            Assert.IsTrue(far.UnitPriceCents.Cents < millbrook.UnitPriceCents.Cents,
                "the far catalog supplier is cheaper per sack (30c vs 48c)");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { far, millbrook }, Day);
            Assert.AreEqual(MillbrookId, result.Ranked[0].Option.SupplierId,
                "the dearer option that profits (707c) ranks above the cheaper option that loses money (-380c)");
            Assert.AreEqual(1, result.Ranked[0].Rank);
            Assert.AreEqual("far-mill-supplier", result.Ranked[1].Option.SupplierId);
        }

        [Test]
        public void ConsolidatedShipment_OnlyWhereQuoted()
        {
            SupplierOption unquoted = SampleOption(consolidatedQuoted: false, consolidated: true);
            SupplierOption quoted = SampleOption(consolidatedQuoted: true, consolidated: true);
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { unquoted, quoted }, Day);
            Assert.AreEqual(2, result.Ranked.Count);
            Assert.IsTrue(Reasoning(result.Ranked[0]).Contains("not quoted"),
                "an unquoted consolidation is never assumed: " + Reasoning(result.Ranked[0]));
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("consolidated"),
                "a quoted consolidation is reported: " + Reasoning(result.Ranked[1]));
            Assert.AreEqual(result.Ranked[0].ExpectedProfitCents.Cents,
                result.Ranked[1].ExpectedProfitCents.Cents,
                "consolidation changes no figure — no authority quotes a consolidation discount, so none is applied");
        }

        [Test]
        public void UnknownLeadTime_ArrivalUnknownButProfitStillRanked()
        {
            SupplierOption option = SampleOption(leadTimeDays: -1.0);
            Assert.IsTrue(!option.ExpectedArrivalDay(Day).IsKnown,
                "no authored lead time exists anywhere in v3.1 — without a caller-supplied lead time the arrival is Unknown, never invented");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { option }, Day);
            Assert.AreEqual(1, result.Ranked.Count);
            Assert.AreEqual(1, result.Ranked[0].Rank, "the profit axis is Known, so the option still ranks on profit");
            Assert.IsTrue(result.Ranked[0].ExpectedProfitCents.IsKnown);
            Assert.IsTrue(Reasoning(result.Ranked[0]).Contains("lead time"),
                "the Unknown arrival names the missing lead time: " + Reasoning(result.Ranked[0]));
        }

        [Test]
        public void UnknownUnitPrice_IncomparableOnProfit()
        {
            SupplierOption option = SampleOption(unitPriceCents: -1);
            Assert.IsTrue(!option.ExpectedProfitCents.IsKnown, "no quoted unit price, no profit figure");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { option, SampleOption() }, Day);
            Assert.AreEqual(2, result.Ranked.Count);
            Assert.AreEqual(1, result.Ranked[0].Rank, "the comparable option ranks first");
            Assert.AreEqual(0, result.Ranked[1].Rank, "an option with an Unknown profit input is unranked, not guessed into the ranking");
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("unit price"),
                "the incomparability names the missing input: " + Reasoning(result.Ranked[1]));
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("expected profit unknown"),
                "the incomparability names itself: " + Reasoning(result.Ranked[1]));
        }

        [Test]
        public void UnknownFreight_IncomparableOnProfit()
        {
            SupplierOption option = SampleOption(freightCents: -1);
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { option, SampleOption() }, Day);
            Assert.AreEqual(1, result.Ranked[0].Rank, "the comparable option ranks first");
            Assert.AreEqual(0, result.Ranked[1].Rank);
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("freight"),
                "the incomparability names the missing freight: " + Reasoning(result.Ranked[1]));
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("expected profit unknown"),
                "the incomparability names itself: " + Reasoning(result.Ranked[1]));
        }

        [Test]
        public void UnknownRevenue_IncomparableOnProfit()
        {
            SupplierOption option = SampleOption(revenuePerUnitCents: -1);
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { option, SampleOption() }, Day);
            Assert.AreEqual(1, result.Ranked[0].Rank, "the comparable option ranks first");
            Assert.AreEqual(0, result.Ranked[1].Rank);
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("revenue"),
                "the incomparability names the missing revenue: " + Reasoning(result.Ranked[1]));
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("expected profit unknown"),
                "the incomparability names itself: " + Reasoning(result.Ranked[1]));
        }

        [Test]
        public void Reliability_FromLogHistory_LateSupplierRanksBelowReliable()
        {
            CustodyTransferLog log = LogWithLateAndReliableSuppliers();
            SupplierReliability reliable = SupplierReliability.FromCustodyLog(
                log, CustodyTransferLog.MillbrookAuthority("supplier-a"), LeadTimeDays.Known(2.0));
            SupplierReliability late = SupplierReliability.FromCustodyLog(
                log, CustodyTransferLog.MillbrookAuthority("supplier-b"), LeadTimeDays.Known(2.0));
            Assert.IsTrue(reliable.IsKnown);
            Assert.AreEqual(1, reliable.CommittedShipments);
            Assert.AreEqual(1, reliable.ReceivedShipments);
            Assert.IsTrue(reliable.OnTimeShipments.IsKnown);
            Assert.AreEqual(1, reliable.OnTimeShipments.Units, "transit 1 day against a 2-day lead is on time");
            Assert.IsTrue(late.IsKnown);
            Assert.AreEqual(0, late.OnTimeShipments.Units, "transit 9 days against a 2-day lead is late");
            Assert.IsTrue(late.AverageTransitDays.IsKnown);
            Assert.AreEqual(9.0, late.AverageTransitDays.Days, 0.001);

            SupplierOption optionA = SampleOption(supplierId: "supplier-a");
            optionA = WithReliability(optionA, reliable);
            SupplierOption optionB = SampleOption(supplierId: "supplier-b");
            optionB = WithReliability(optionB, late);
            Assert.AreEqual(optionA.ExpectedProfitCents.Cents, optionB.ExpectedProfitCents.Cents,
                "equally priced by design, so the tie-break is reliability");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { optionB, optionA }, Day);
            Assert.AreEqual("supplier-a", result.Ranked[0].Option.SupplierId,
                "the supplier with recorded late arrivals ranks below the equally priced reliable one");
            Assert.AreEqual(1, result.Ranked[0].Rank);
            Assert.AreEqual("supplier-b", result.Ranked[1].Option.SupplierId);
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("late"),
                "the late supplier's reasoning names the late arrival: " + Reasoning(result.Ranked[1]));
        }

        private static SupplierOption WithReliability(SupplierOption option, SupplierReliability reliability)
        {
            return new SupplierOption(
                option.SupplierId, option.ItemId, option.QuantityUnits, option.QuantityAvailable,
                option.UnitPriceCents, option.UnitPriceSource, option.FreightCostCents, option.FreightSource,
                option.ConsolidatedShipmentQuoted, option.ConsolidatedShipment, option.LeadTimeDays,
                option.QuotedArrivalDay, reliability, option.StorageFit, option.ExpectedSpoilageUnits,
                option.RevenuePerUnitCents, option.RevenueBasis, option.AvailableCashCents,
                option.IsSelfCollect);
        }

        [Test]
        public void Reliability_NoHistory_UnknownNeverAssumedReliable()
        {
            var emptyLog = new CustodyTransferLog();
            SupplierReliability none = SupplierReliability.FromCustodyLog(
                emptyLog, CustodyTransferLog.MillbrookAuthority("supplier-c"), LeadTimeDays.Known(2.0));
            Assert.IsTrue(!none.IsKnown, "no history means Unknown reliability, never assumed reliable");
            Assert.AreEqual(0, none.CommittedShipments);
            Assert.IsTrue(none.Reason.Contains("no freight history"),
                "the gap is named: " + none.Reason);

            SupplierOption withHistory = WithReliability(SampleOption(supplierId: "supplier-a"),
                SupplierReliability.FromCustodyLog(LogWithLateAndReliableSuppliers(),
                    CustodyTransferLog.MillbrookAuthority("supplier-a"), LeadTimeDays.Known(2.0)));
            SupplierOption withoutHistory = SampleOption(supplierId: "supplier-c");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { withoutHistory, withHistory }, Day);
            Assert.AreEqual("supplier-a", result.Ranked[0].Option.SupplierId,
                "on an equal profit, a supplier with a recorded reliable history ranks above one with no history at all");
            Assert.IsTrue(Reasoning(result.Ranked[1]).Contains("no reliability history") || Reasoning(result.Ranked[1]).Contains("Unknown"),
                "the no-history option says so: " + Reasoning(result.Ranked[1]));
        }

        [Test]
        public void StorageFit_FromF2BusinessStorageLeg()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            var evaluator = new PhysicalCapacityEvaluator(
                null, id => string.Equals(id, StoreInstanceId, StringComparison.Ordinal) ? s.StoreRuntime : null,
                null, null);
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                new PhysicalCapacityQuery(PhysicalContainerKind.BusinessStorage, StoreInstanceId,
                    new PhysicalLoad(new[] { (HouseholdItemCatalog.BreadId, 16) }), Day),
                new PhysicalCapacityEvaluationContext { BusinessCategoryId = "staple_food" });
            Assert.IsTrue(verdict.StorageAxes.TotalUnits.HasValue, "the F2 leg answers Total");
            Assert.IsTrue(verdict.StorageAxes.OccupiedUnits.HasValue, "the F2 leg answers Occupied");
            Assert.AreEqual(24, verdict.StorageAxes.OccupiedUnits.Value,
                "24 loaves are on the shelves after the bake");
            int available = verdict.StorageAxes.TotalUnits.Value - verdict.StorageAxes.OccupiedUnits.Value;

            SupplierStorageFit fits = SupplierStorageFit.FromAvailableStorage(
                ReserveQuantity.Known(available), ReserveQuantity.Known(16));
            Assert.IsTrue(fits.IsKnown);
            Assert.IsTrue(fits.Fits, "16 units fit in the available storage");
            SupplierStorageFit tooBig = SupplierStorageFit.FromAvailableStorage(
                ReserveQuantity.Known(available), ReserveQuantity.Known(available + 1));
            Assert.IsTrue(!tooBig.Fits);
            Assert.AreEqual(1, tooBig.ShortfallUnits.Units, "the shortfall is reported, not silently capped");
            SupplierStorageFit unknown = SupplierStorageFit.FromAvailableStorage(
                ReserveQuantity.Unknown, ReserveQuantity.Known(16));
            Assert.IsTrue(!unknown.IsKnown, "Unknown availability means an Unknown fit, never an assumed fit");
        }

        [Test]
        public void ExpectedSpoilage_FromSlice5Lots()
        {
            var lots = new List<PerishableLotState>
            {
                Lot("lot-fresh", "bake.bread-loaf", 24),
                Lot("lot-aged", "bake.bread-loaf", 24,
                    PerishablePhysicalCondition.Stale, PerishableSafetyState.Safe,
                    UsefulLifeEstimate.Known(0.0)),
                Lot("lot-unsafe", "bake.bread-loaf", 10,
                    PerishablePhysicalCondition.Fresh, PerishableSafetyState.Unsafe),
                Lot("lot-suspect", "bake.bread-loaf", 6,
                    PerishablePhysicalCondition.DayOld, PerishableSafetyState.Suspect),
            };
            // The spoilage rule is the Slice 5 rule as the Slice 6 reserve
            // uses it: past useful life or Unsafe counts as spoiled;
            // Suspect is counted, with the caution named, because no
            // authored rule discounts Suspect.
            SpoilageForecast forecast = SupplierOption.ExpectedSpoilageFromLots(
                lots, CentsAmount.Known(5), out string reason);
            Assert.AreEqual(34, forecast.SpoiledUnits.Units,
                "24 past-life + 10 unsafe loaves are expected spoilage; fresh and suspect are not");
            Assert.IsTrue(forecast.LossCents.IsKnown);
            Assert.AreEqual(170, forecast.LossCents.Cents, "34 spoiled loaves x 5c of recorded loaf revenue");
            Assert.IsTrue(reason.Contains("past its useful life") && reason.Contains("unsafe"),
                "the forecast names what spoiled: " + reason);

            SpoilageForecast unknownRevenue = SupplierOption.ExpectedSpoilageFromLots(
                lots, CentsAmount.Unknown, out _);
            Assert.AreEqual(34, unknownRevenue.SpoiledUnits.Units, "the spoiled units are still counted");
            Assert.IsTrue(!unknownRevenue.LossCents.IsKnown,
                "with no recorded revenue per unit the spoilage loss in cents is Unknown, never invented");
        }

        [Test]
        public void SelfCollectQuote_RequiresJourney_ArrivalUnknown()
        {
            OffMapSupplierCatalog catalog = TwoSupplierCatalog();
            OffMapFreightQuote selfCollect = null;
            foreach (OffMapFreightQuote quote in catalog.QuoteAll(FlourItem, Sacks, 5, 0))
            {
                if (string.Equals(quote.SupplierId, "near-mill-supplier", StringComparison.Ordinal)
                    && quote.FreightOption == FreightOptionKind.SelfCollect)
                {
                    selfCollect = quote;
                }
            }
            Assert.IsNotNull(selfCollect, "the catalog quotes a SelfCollect option");
            Assert.AreEqual(0, selfCollect.FreightCents, "self-collect quotes zero freight");
            Assert.IsTrue(selfCollect.RequiresJourney, "but someone must actually travel to collect it");

            SupplierOption option = CatalogOption(selfCollect);
            Assert.IsTrue(option.IsSelfCollect, "the option records that it is self-collect");
            Assert.IsTrue(!option.ExpectedArrivalDay(Day).IsKnown,
                "no freight movement is quoted, so no arrival can be computed — the quote books no journey");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { option }, Day);
            Assert.IsTrue(Reasoning(result.Ranked[0]).Contains("journey"),
                "the reasoning names the missing journey: " + Reasoning(result.Ranked[0]));
        }

        [Test]
        public void FromNamedSkuOrder_QuotesGoodsPlusSurchargeAndArrival()
        {
            var order = new NamedSkuOrder
            {
                OrderId = "named-order-1",
                BusinessId = StoreInstanceId,
                SkuId = "store_horse_feed_sack",
                UnitsOrdered = 20,
                UnitGoodsCostCents = 15,
                FreightSurchargeCents = 60,
                TotalCostCents = 360,
                SupplierId = "offmap-feed-co",
                SupplierName = "Off-Map Feed Co.",
                OrderDayIndex = 12,
                ExpectedArrivalDayIndex = 14,
            };
            SupplierOption option = SupplierOption.FromNamedSkuOrder(
                order, LeadTimeDays.Unknown, SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Known(0), CentsAmount.Known(20),
                "20c per feed sack: test revenue basis, recorded retail price supplied by the caller",
                CentsAmount.Known(20000));
            Assert.AreEqual("offmap-feed-co", option.SupplierId);
            Assert.AreEqual("store_horse_feed_sack", option.ItemId);
            Assert.AreEqual(15, option.UnitPriceCents.Cents, "the order's own goods price");
            Assert.AreEqual(60, option.FreightCostCents.Cents, "the order's seller-paid freight surcharge");
            Assert.AreEqual(20, option.QuantityUnits.Units);
            Assert.AreEqual(20, option.QuantityAvailable.Units, "the order's own quantity is its availability");
            Assert.AreEqual(14, option.ExpectedArrivalDay(Day).Units,
                "the order's own expected arrival day, quoted by the service");
            Assert.AreEqual(360, option.ExpectedTotalCostCents.Cents, "20 x 15 + 60");
            Assert.AreEqual(40, option.ExpectedProfitCents.Cents, "20 x 20 revenue - 360 cost");
        }

        [Test]
        public void Compare_ExcludesOtherItemOptions()
        {
            SupplierOption feed = SampleOption(itemId: "store_horse_feed_sack", supplierId: "offmap-feed-co");
            SupplierComparisonResult result = SupplierComparison.Compare(
                FlourItem, new[] { feed, SampleOption() }, Day);
            Assert.AreEqual(1, result.Ranked.Count, "an option for another item never enters this item's ranking");
            Assert.AreEqual(MillbrookId, result.Ranked[0].Option.SupplierId);
            Assert.IsTrue(ResultReasoning(result).Contains("store_horse_feed_sack"),
                "the exclusion names the other item: " + ResultReasoning(result));
        }

        [Test]
        public void Chain_Q_CompareAndPlanChangeNothing()
        {
            var log = new CustodyTransferLog();
            Chain s = DriveToStockedStore(log);
            (int supplierCash, int supplierStock, int bakeryCash, int payerCash, int carrierCash,
                int flourLb, int storeStock, int lotCount) snapshot = Snapshot(s);

            SupplierOption millbrook = MillbrookOptionFromChain(s);
            SupplierComparisonResult comparison = SupplierComparison.Compare(
                FlourItem, new[] { millbrook }, Day);
            ReserveProjection projection = ReserveProjection.Compute(PlanInputs());
            int logCountBefore = log.Count;
            ReplenishmentPlan plan = ReplenishmentPlan.Plan(projection, comparison, Day);
            Assert.IsTrue(plan.HasSelection, "the plan selects the Millbrook option: " + string.Join(" | ", plan.Reasoning));

            (int supplierCash, int supplierStock, int bakeryCash, int payerCash, int carrierCash,
                int flourLb, int storeStock, int lotCount) after = Snapshot(s);
            Assert.AreEqual(snapshot, after,
                "comparison plus planning change nothing in any live authority — quantities and settlements identical before and after");
            Assert.AreEqual(logCountBefore, log.Count, "no new custody record either: the plan orders nothing");
        }

        private static (int, int, int, int, int, int, int, int) Snapshot(Chain s) =>
            (s.Supplier.CashCents, s.Supplier.GetInventoryUnits(FlourItem),
                s.BakeryCash.BalanceCents, s.PayerRuntime.CurrentCashCents, s.CarrierRuntime.CurrentCashCents,
                s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                s.Supplier.Lots.Count + s.Bakery.FlourStock.Lots.Count);

        private static ReserveInputs PlanInputs()
        {
            return new ReserveInputs
            {
                RecentDemandPerDay = ReserveQuantity.Known(10),
                DemandUncertaintyUnits = ReserveQuantity.Unknown,
                ActualUsableStockUnits = ReserveQuantity.Known(0),
                ForecastSpoilageUnits = ReserveQuantity.Known(0),
                ExistingCommitmentsUnits = ReserveQuantity.Known(0),
                ConfirmedIncomingDeliveriesUnits = ReserveQuantity.Known(0),
                ProductionCapacityPerDay = ReserveQuantity.Unknown,
                ReplenishmentLeadTimeDays = LeadTimeDays.Known(2.0),
                AvailableStorageUnits = ReserveQuantity.Unknown,
            };
        }

        private static SupplierOption MillbrookOptionFromChain(Chain s)
        {
            CentsAmount freight = SupplierOption.FreightFromShipment(s.Shipment, out string freightReason);
            return SupplierOption.FromMillbrook(
                s.Supplier, FlourItem, ReserveQuantity.Known(Sacks), freight, freightReason,
                LeadTimeDays.Known(2.0), SupplierReliability.Unknown,
                SupplierStorageFit.Unknown("test option: no storage check supplied"),
                ReserveQuantity.Unknown, CentsAmount.Known(RevenuePerSackCents), RevenueBasis,
                CentsAmount.Known(s.BakeryCash.BalanceCents));
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

            // The comparison reads the same chain figures and changes none of them.
            SupplierOption millbrook = MillbrookOptionFromChain(s);
            Assert.AreEqual(125, millbrook.FreightCostCents.Cents, "freight is the settled 125c charge");
            Assert.AreEqual(48, millbrook.UnitPriceCents.Cents);
            SupplierComparisonResult comparison = SupplierComparison.Compare(
                FlourItem, new[] { millbrook }, Day);
            Assert.AreEqual(707, comparison.Ranked[0].ExpectedProfitCents.Cents);
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the comparison moved no flour");
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "the comparison settled no freight twice");
        }

        // ---- shared chain fixture: the real authorities, copied from
        // DemandObservationTests (itself merged from
        // FreightDeliveryPaymentAuditTests and
        // BakeryFoodChainEndToEndTests). Shopping helpers omitted: this
        // slice drives the chain only to a stocked store. ----

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
