// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority spoilage integration (corrected order step 4, 2026-11-08).
// Connects the Slice 5 perishable condition model (PerishableLotState) to
// actual lot transfers and storage transitions through real production
// authorities. The state travels with the lot: created with the lot at its
// creation site (Bakery bake for bread, with the authored AgeToDay rule as
// its deterioration source; Millbrook order and freight receipt for flour,
// Unknown where the source carries no condition information), carried
// through purchase (the carried batch keeps the lot's condition; purchase
// never resets it), carried through deposit (the household lot keeps the
// condition it arrived with), and carried through save/load (the condition
// round-trips with the lot's own DTO pair, like F5).
//
// Deterioration applies ONLY where authored: the Bakery AgeToDay rule for
// bread, applied at the same simulation-time steps the existing drivers
// already advance (AgeShelf). No invented decay for any other item, no
// global spoilage rule, no automatic discards, no economic policy change.
// Spoilage accounting stays accounting-only: recording a loss removes
// nothing from any live authority in this slice. No spoilage is activated
// in the shopping loop, the Bakery drivers, or any live flow beyond the
// state travelling with the lot.
//
// BLOCKED SEAMS, recorded not papered over (see the checkpoint entry):
// (a) The General Store category pool is anonymous: the wholesale
// delivery drains the bakery bread lot into pool units, and a household
// purchase draws from the pool with no lot id (the F3 retail-purchase
// source-lot gap). A pool purchase therefore carries NO lot condition —
// the batch and the household lot honestly report none. The lot-backed
// path that DOES carry a condition through purchase is the bakery-direct
// sale (Series2BakerySupplierAdapter over SellBread), whose
// condition-homogeneous cap (oldest baked day only) makes the sold
// condition well-defined.
// (b) The Review2 wholesale driver takes shelf units directly
// (lot.TakeUnits) and its result carries only the BakeryGoodsCondition,
// not a PerishableLotState, so the delivery record written by
// RecordWholesaleTransfer carries provenance lines but no attached
// states. The bakery lot's remaining state stays unit-synced through
// TakeUnits. FulfillWholesale (the runtime's own fulfillment) does
// capture the taken parts' states on its delivery record.
// (c) The retail lot (BusinessRetailItemLotState) carries no condition:
// its save DTO (BusinessRetailItemLotSaveDto, SaveDtos.cs) lives in the
// harness-excluded persistence file whose harness copy is a stale
// verbatim extraction (commit a38c6aa), so attaching a state in memory
// without DTO coverage would lose it on load. Attachment there is
// deferred to the slice that regenerates the shim; the gap is pinned
// by reflection below.
// (d) The freight shipment (LogisticsShipmentState) carries no
// condition field: the condition travels Millbrook lot -> bakery flour
// lot via the adapter re-reading the source lot at receive, not via
// the shipment. A mid-transit shipment save alone carries no
// condition; the supplier record (which IS saved) carries it.
// (e) Directly seeded bin lots (flour/notions/fuel received straight
// into BakeryFlourStock without a creation site that knows the bakery
// authority) carry no state: the bin is a dumb store and must not
// invent authority ids. Only the freight-received flour lot (attached
// by the adapter, which knows the bakery id) carries one.

using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Integration;
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
    public sealed class PerishableIntegrationTests
    {
        private const int Day = 12;
        private const int EthanId = 7;
        private const string CarrierId = "livery_freight_003";
        private const string BakeryId = "bakery_001";
        private const string DirectBakeryId = "pierce-bakery-1";
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

        private static PerishableLotState StateOf(BakeryBreadLot lot)
        {
            Assert.IsTrue(lot.HasPerishableCondition, "the bread lot is expected to carry a condition state");
            return PerishableLotState.FromSaveDto(lot.PerishableCondition);
        }

        private static PerishableLotState StateOf(BakeryFlourLot lot)
        {
            Assert.IsTrue(lot.HasPerishableCondition, "the flour lot is expected to carry a condition state");
            return PerishableLotState.FromSaveDto(lot.PerishableCondition);
        }

        private static PerishableLotState StateOf(HouseholdItemLot lot)
        {
            Assert.IsTrue(lot.HasPerishableCondition, "the household lot is expected to carry a condition state");
            return PerishableLotState.FromSaveDto(lot.PerishableCondition);
        }

        private static PerishableLotState StateOf(CarriedGoodsBatch batch)
        {
            Assert.IsTrue(batch.HasPerishableCondition, "the carried batch is expected to carry a condition state");
            return PerishableLotState.FromSaveDto(batch.PerishableCondition);
        }

        // Bakes one batch on the real chain WITHOUT the wholesale leg, so
        // the bread lot stays on the shelf (24 loaves) instead of draining
        // into the store pool. ChainAtBake runs the wholesale driver too.
        private static Chain ChainAtBakeNoWholesale(CustodyTransferLog log)
        {
            Chain s = ChainAtReceive(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            var driver = s.ProductionDriver;
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, Day, s.Diag),
                "one batch must plan; diag: " + string.Join("; ", s.Diag));
            int baked = driver.RunScheduledDay(
                Day, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            return s;
        }

        [Test]
        public void Bake_CreatesFreshConditionOnBreadLot_AcceptanceN()
        {
            // Acceptance N: the condition state is created with the lot
            // at bake — Fresh, anchored at the bake day, keyed to the
            // bread lot that baked it.
            var log = new CustodyTransferLog();
            Chain s = ChainAtBakeNoWholesale(log);
            BakeryBreadLot lot = s.Bakery.BreadShelf[0];
            Assert.AreEqual(24, lot.UnitCount, "one batch yields 24 loaves");
            Assert.AreEqual(BakeryGoodsCondition.Fresh, lot.Condition, "baked today: Fresh by the authored rule");
            Assert.IsTrue(lot.HasPerishableCondition, "the bake attaches a condition state to the bread lot");
            PerishableLotState state = PerishableLotState.FromSaveDto(lot.PerishableCondition);
            Assert.AreEqual(new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), lot.LotId.ToString()), state.Key,
                "the state is keyed to the bread lot that baked it");
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, state.ItemId, "the state names the baked product");
            Assert.AreEqual(24, state.Units, "the state carries the baked quantity");
            Assert.AreEqual(Day, state.ProducedOrAcquiredDay, "the age anchor is the bake day");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, state.PhysicalCondition, "baked today: Fresh");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, state.PerceivedCondition, "known and perceived agree at bake");
            Assert.AreEqual(PerishableSafetyState.Unknown, state.SafetyState, "no safety evidence exists at bake");
            Assert.IsTrue(!state.RemainingUsefulLife.IsKnown, "no authored shelf life exists, so remaining life stays Unknown");
            Assert.IsTrue(state.SourceTransactionOrBatch.StartsWith("bake:", StringComparison.Ordinal),
                "the state names its bake batch as its source, not a fresh start: " + state.SourceTransactionOrBatch);
            Assert.AreEqual(0, state.HandlingHistory.Count, "history starts empty at creation");
            // The flour that went into the loaf keeps its own honest
            // state: the bin lot is down to 148 lb and its attached
            // state tracks the same units, still Unknown.
            BakeryFlourLot flourLot = FlourLot(s);
            Assert.AreEqual(148, flourLot.Units, "160 - 12 dispensed");
            Assert.AreEqual(148, StateOf(flourLot).Units, "the flour lot's state tracks the dispensed quantity");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, StateOf(flourLot).PhysicalCondition,
                "flour carries no authored condition");
        }

        [Test]
        public void MillbrookOrder_CarriesUnknownCondition_AndSaveLoad()
        {
            // The Millbrook order is a creation site: the cargo lot
            // carries an explicit Unknown state (the source carries no
            // condition information), and it round-trips with the
            // supplier's own DTO pair.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            PlaceFreightOrder(s);
            OffMapAidCargoLot lot = s.Lot;
            Assert.IsTrue(lot.HasPerishableCondition, "the order attaches a condition state to the cargo lot");
            PerishableLotState state = PerishableLotState.FromSaveDto(lot.PerishableCondition);
            Assert.AreEqual(new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), lot.LotId), state.Key,
                "the state is keyed to the cargo lot");
            Assert.AreEqual("bakery_flour_sack", state.ItemId, "the state names the ordered package");
            Assert.AreEqual(Sacks, state.Units, "the state carries the ordered package count");
            Assert.AreEqual(0, state.ProducedOrAcquiredDay, "the age anchor is the order day (day 0 in this chain)");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                "Millbrook carries no condition information, so the state is honestly Unknown, never a default Fresh");
            Assert.IsTrue(!state.RemainingUsefulLife.IsKnown, "remaining life stays Unknown");
            OffMapAidBusinessSaveDto dto = s.Supplier.ExportState();
            var restored = new OffMapAidBusinessRuntime(MillbrookDefinition());
            Assert.IsNull(restored.ImportState(dto), "supplier import must succeed");
            Assert.AreEqual(1, restored.Lots.Count, "one cargo lot survives the round-trip");
            Assert.IsTrue(restored.Lots[0].HasPerishableCondition, "the restored cargo lot still carries its state");
            Assert.IsTrue(PerishableLotState.FromSaveDto(restored.Lots[0].PerishableCondition).EqualsState(state),
                "the cargo lot's state round-trips exactly, including its empty history");
        }

        [Test]
        public void FreightReceive_CarriesUnknownConditionIntoFlourBin()
        {
            // The freight receive is a transfer: the adapter re-reads
            // the source lot (the shipment itself carries no condition
            // field — blocked seam (d)) and attaches the same Unknown
            // condition to the new flour lot, keyed to that lot and
            // anchored at the receive day.
            var log = new CustodyTransferLog();
            Chain s = ChainAtReceive(log);
            BakeryFlourLot flourLot = FlourLot(s);
            Assert.AreEqual(160, flourLot.Units, "the delivered lot holds the lot's 160 lb");
            Assert.IsTrue(flourLot.HasPerishableCondition, "the receive attaches a condition state to the flour lot");
            PerishableLotState state = StateOf(flourLot);
            Assert.AreEqual(new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), flourLot.LotId.ToString()), state.Key,
                "the state is keyed to the flour lot, not the cargo lot");
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, state.ItemId, "the state names flour in bakery units");
            Assert.AreEqual(160, state.Units, "the state carries the received pounds");
            Assert.AreEqual(Day, state.ProducedOrAcquiredDay, "the age anchor is the receive day");
            Assert.AreEqual(PerishablePhysicalCondition.Unknown, state.PhysicalCondition,
                "flour carries no authored condition at receipt either");
            Assert.IsTrue(state.SourceTransactionOrBatch.Contains(LotId),
                "the state names its Millbrook source lot: " + state.SourceTransactionOrBatch);
            // Directly seeded bin lots carry no state: the bin is a dumb
            // store and must not invent a bakery authority id (blocked
            // seam (e)). The notions lot was seeded straight into the
            // bin by the fixture, so it honestly reports none.
            Chain b = ChainAtBake(log);
            Assert.IsTrue(!NotionsLot(b).HasPerishableCondition,
                "a bin lot seeded without a creation site carries no condition state");
        }

        [Test]
        public void WholesaleDelivery_FulfillWholesale_CarriesTakenCondition()
        {
            // The runtime's own wholesale fulfillment captures the taken
            // part of each seller lot's state on the delivery record, in
            // parallel with the existing per-lot lists.
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag), "oven installation should succeed");
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, diag), "flour lot should be received");
            Assert.IsNull(bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, diag), "notions lot should be received");
            Assert.IsNull(bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, diag), "fuel lot should be received");
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, Day, diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                Day, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids),
                "one batch should bake; diag: " + string.Join("; ", diag));
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = Day,
            };
            Assert.IsNull(bakery.AddWholesaleAccount(account, diag), "account booking should succeed");
            BakeryWholesaleDeliveryRecord record = bakery.FulfillWholesale(
                bakery.WholesaleAccounts[0].AccountId, Day, diag, ids);
            Assert.IsNotNull(record, "fulfillment must produce a delivery record; diag: " + string.Join("; ", diag));
            Assert.AreEqual(24, record.Units, "24 loaves delivered");
            Assert.IsTrue(record.FullyCovered, "the single lot covers the delivery");
            Assert.AreEqual(1, record.PerishableConditionsByLot.Count, "one taken part, one attached state");
            PerishableLotState taken = PerishableLotState.FromSaveDto(record.PerishableConditionsByLot[0]);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, taken.PhysicalCondition, "the delivered loaves are Fresh");
            Assert.AreEqual(24, taken.Units, "the taken part carries the delivered quantity");
            Assert.AreEqual(Day, taken.ProducedOrAcquiredDay, "the taken part keeps the bake-day anchor");
            Assert.AreEqual(1, taken.HandlingHistory.Count, "the taken part records its split from the shelf lot");
            Assert.AreEqual("split", taken.HandlingHistory[0].Kind, "the split is the recorded event");
            BakeryBreadLot shelfLot = bakery.BreadShelf[0];
            Assert.AreEqual(0, shelfLot.UnitCount, "the shelf lot is drained");
            Assert.AreEqual(0, StateOf(shelfLot).Units, "the remaining state tracks the drained quantity");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, StateOf(shelfLot).PhysicalCondition,
                "the remaining part keeps the same condition, never reset");
        }

        [Test]
        public void BakeryDirectPurchase_ConditionSurvivesPurchaseAndDeposit_AcceptanceN_O()
        {
            // Acceptance N and O through the lot-backed path: the
            // household buys bread directly from the bakery counter, the
            // carried batch keeps the lot's Fresh condition (never reset
            // by purchase), and the deposited household lot keeps exactly
            // the batch's state.
            var s = NewBakeryDirectChain();
            CarriedGoodsBatch midTripBatch = null;
            s.Loop.MidTripObserver = (result, batch) => { midTripBatch = batch; };
            FundHouseholdDirect(s, 1000);
            ShoppingTripResult trip = BuyBreadDirect(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(14, trip.UnitsAcquired, "14 loaves bought");
            Assert.AreEqual(112, trip.BuyerOutflowCents, "14 loaves x 8c bakery fresh price");
            Assert.AreEqual(888, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 112c");
            Assert.AreEqual(10, s.Bakery.BreadShelf[0].UnitCount, "shelf keeps the 10 unsold loaves");
            Assert.IsNotNull(midTripBatch, "the mid-trip observer must have seen the carried batch");
            Assert.IsTrue(midTripBatch.HasPerishableCondition, "the carried batch keeps the lot's condition");
            PerishableLotState batchState = StateOf(midTripBatch);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, batchState.PhysicalCondition,
                "purchase does not reset the condition — Fresh stays Fresh (acceptance O)");
            Assert.AreEqual(14, batchState.Units, "the batch carries the purchased quantity");
            Assert.AreEqual(Day, batchState.ProducedOrAcquiredDay, "the age anchor is still the bake day");
            Assert.AreEqual(new LotKey(CustodyTransferLog.BakeryAuthority(DirectBakeryId), s.Bakery.BreadShelf[0].LotId.ToString()), batchState.Key,
                "the batch state is keyed to the single bakery lot that supplied all 14 loaves");
            Assert.AreEqual(2, batchState.HandlingHistory.Count,
                "the batch records its sale split and its purchase transfer, in order");
            Assert.AreEqual("split", batchState.HandlingHistory[0].Kind, "the lot was split at the sale");
            Assert.AreEqual("transfer", batchState.HandlingHistory[1].Kind, "the taken part was then transferred to the shopper");
            HouseholdItemLot homeLot = HouseholdBreadLot(s.Inventories);
            Assert.AreEqual(14, homeLot.QuantityUnits, "the household lot holds the 14 loaves");
            Assert.IsTrue(homeLot.HasPerishableCondition, "the deposited lot keeps the condition it arrived with");
            Assert.IsTrue(PerishableLotState.FromSaveDto(homeLot.PerishableCondition).EqualsState(batchState),
                "deposit keeps the batch's state verbatim — condition survives deposit (acceptance N)");
            Assert.AreEqual(1, s.Bakery.Sales.Count, "one sale record");
            Assert.AreEqual(14, s.Bakery.Sales[0].Units, "the sale record carries the sold quantity");
            Assert.AreEqual(BakeryGoodsCondition.Fresh, s.Bakery.Sales[0].ConditionSoldAt, "sold Fresh");
            Assert.IsTrue(s.Bakery.Sales[0].HasPerishableCondition, "the sale record carries the taken part's state");
            Assert.AreEqual(14, PerishableLotState.FromSaveDto(s.Bakery.Sales[0].PerishableCondition).Units,
                "the sale record's taken part holds 14 loaves");
        }

        [Test]
        public void StorePoolPurchase_CarriesNoCondition_BlockedSeam()
        {
            // Blocked seam (a): the store pool is anonymous. The
            // wholesale delivery drains the bakery lot into pool units
            // and the household purchase draws from the pool with no lot
            // id, so the batch and the household lot honestly carry NO
            // condition state — none is invented from the pool.
            var log = new CustodyTransferLog();
            Chain s = ChainAtBake(log);
            CarriedGoodsBatch midTripBatch = null;
            s.Loop.MidTripObserver = (result, batch) => { midTripBatch = batch; };
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(14, trip.UnitsAcquired, "14 loaves bought from the pool");
            Assert.AreEqual(70, trip.BuyerOutflowCents, "14 loaves x 5c store price");
            Assert.AreEqual(930, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 70c");
            Assert.AreEqual(10, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "pool keeps the 10 unsold loaves");
            Assert.IsNotNull(midTripBatch, "the mid-trip observer must have seen the carried batch");
            Assert.IsTrue(!midTripBatch.HasPerishableCondition,
                "a pool purchase carries no lot condition — the pool holds no lot to inherit one from");
            HouseholdItemLot homeLot = HouseholdBreadLot(s);
            Assert.AreEqual(14, homeLot.QuantityUnits, "the household lot holds the 14 loaves");
            Assert.IsTrue(!homeLot.HasPerishableCondition,
                "the deposited lot carries no condition either — none was invented at the pool");
            // Blocked seam (b): the Review2 wholesale driver took the
            // shelf units directly, so its delivery record carries
            // provenance lines but no attached states.
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "one wholesale delivery was recorded");
            BakeryWholesaleDeliveryRecord delivery = s.Bakery.WholesaleDeliveries[0];
            Assert.AreEqual(24, delivery.Units, "24 loaves delivered to the pool");
            Assert.AreEqual(0, delivery.PerishableConditionsByLot.Count,
                "the Review2 path captures no taken-part states on the delivery record");
            Assert.IsTrue(delivery.ProvenanceLines.Count > 0, "the provenance lines still name the upstream chain");
            // The drained shelf lot's remaining state stays honest: 0
            // units, still Fresh — the take synced its units.
            BakeryBreadLot shelfLot = s.Bakery.BreadShelf[0];
            Assert.AreEqual(0, shelfLot.UnitCount, "the wholesale delivery drains the bread lot to 0 into the store pool");
            Assert.AreEqual(0, StateOf(shelfLot).Units, "the remaining state tracks the drained quantity");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, StateOf(shelfLot).PhysicalCondition,
                "the drain does not touch the condition");
        }

        [Test]
        public void HouseholdLot_ConditionSurvivesSaveLoad()
        {
            // The household lot's own DTO pair (registry Export/Import,
            // copy-on-import) round-trips the attached state exactly,
            // including its handling history.
            var s = NewBakeryDirectChain();
            FundHouseholdDirect(s, 1000);
            ShoppingTripResult trip = BuyBreadDirect(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            HouseholdItemLot before = HouseholdBreadLot(s.Inventories);
            PerishableLotState beforeState = StateOf(before);
            var states = new List<HouseholdInventoryState>();
            s.Inventories.ExportState(states);
            var restored = new HouseholdInventoryRegistry();
            restored.ImportState(states);
            HouseholdItemLot after = HouseholdBreadLot(restored);
            Assert.AreEqual(14, after.QuantityUnits, "the restored lot holds the 14 loaves");
            Assert.IsTrue(after.HasPerishableCondition, "the restored lot still carries its state");
            Assert.IsTrue(PerishableLotState.FromSaveDto(after.PerishableCondition).EqualsState(beforeState),
                "the household lot's state round-trips exactly, including its purchase-transfer history");
            // The round-trip is a true copy: mutating the restored lot
            // must not move the original (copy-on-import).
            restored.GetOrCreate(HouseholdId).Consume(
                HouseholdItemCatalog.BreadId, 1, Day, "meal", "test", new List<HouseholdConsumptionRecord>());
            Assert.AreEqual(14, before.QuantityUnits, "the original lot is untouched by consumption of the copy");
            Assert.AreEqual(14, StateOf(before).Units, "the original lot's state is untouched too");
        }

        [Test]
        public void BakeryLot_ConditionSurvivesSaveLoad()
        {
            // The bakery runtime's own DTO pair (Capture/Load) carries
            // the bread lot, the flour lot, and their attached states.
            // (The pair shares lot objects in memory, as the F5 pattern
            // records — the assertion is that the DTO carries the states
            // and the restored runtime answers the same figures.)
            var log = new CustodyTransferLog();
            Chain s = ChainAtBakeNoWholesale(log);
            BakeryShopRuntime.BakeryShopRuntimeSaveDto dto = s.Bakery.CaptureSaveDto();
            var restored = new BakeryShopRuntime(BakeryId);
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.BreadShelf.Count, "one bread lot survives the round-trip");
            BakeryBreadLot lot = restored.BreadShelf[0];
            Assert.AreEqual(24, lot.UnitCount, "the restored lot holds the 24 loaves");
            PerishableLotState before = StateOf(s.Bakery.BreadShelf[0]);
            Assert.IsTrue(PerishableLotState.FromSaveDto(lot.PerishableCondition).EqualsState(before),
                "the bread lot's state round-trips exactly");
            BakeryFlourLot flourBefore = FlourLot(s);
            BakeryFlourLot flourAfter = null;
            foreach (BakeryFlourLot candidate in restored.FlourStock.Lots)
            {
                if (candidate != null && string.Equals(candidate.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                    flourAfter = candidate;
            }
            Assert.IsNotNull(flourAfter, "the flour lot survives the round-trip");
            Assert.AreEqual(148, flourAfter.Units, "the restored flour lot holds 148 lb");
            Assert.IsTrue(PerishableLotState.FromSaveDto(flourAfter.PerishableCondition)
                .EqualsState(StateOf(flourBefore)), "the flour lot's state round-trips exactly");
        }

        [Test]
        public void CustodyBatch_ConditionSurvivesSaveLoad()
        {
            // The in-transit custody DTO pair (loop Capture/Load,
            // copy-on-load) round-trips the batch's condition, and a
            // stranded batch deposited through the U-3 Continue entry
            // lands exactly once with its condition intact.
            var s = NewBakeryDirectChain();
            CarriedGoodsBatch midTripBatch = null;
            HouseholdShoppingCustodySaveDto custodyDto = null;
            s.Loop.MidTripObserver = (result, batch) =>
            {
                midTripBatch = batch;
                custodyDto = s.Loop.CaptureCustodySaveDto();
            };
            FundHouseholdDirect(s, 1000);
            // The immediate trip receives at once, so the Continue leg
            // is driven on a stranded copy: the observer captured the
            // in-transit DTO before receipt, and a fresh loop restores
            // it as a batch still in custody.
            ShoppingTripResult trip = BuyBreadDirect(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.IsNotNull(midTripBatch, "the mid-trip observer must have seen the carried batch");
            Assert.IsNotNull(custodyDto, "the observer must have captured the in-transit custody DTO");
            Assert.AreEqual(1, custodyDto.Batches.Count, "one batch in transit at capture time");
            Assert.IsTrue(custodyDto.Batches[0].HasPerishableCondition, "the captured batch carries its condition");
            PerishableLotState capturedState = StateOf(custodyDto.Batches[0]);
            var strandedLoop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.Journeys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            strandedLoop.LoadCustodyFromSaveDto(custodyDto);
            Assert.AreEqual(1, strandedLoop.ActiveCustodyBatches.Count, "one batch restores into custody");
            Assert.IsTrue(PerishableLotState.FromSaveDto(strandedLoop.ActiveCustodyBatches[0].PerishableCondition)
                .EqualsState(capturedState), "the restored batch's state round-trips exactly");
            // The Continue deposit leg itself is driven by
            // StrandedBatch_ContinueDepositsOnceWithCondition below, on
            // a stranded batch built from a real baked lot.
        }

        [Test]
        public void DriverDto_ConditionRoundTrips()
        {
            // The real-time driver's own DTO pair (ToDto/FromDto via
            // the driver's public Capture/Load) carries the batch's
            // condition through a save/load, so a mid-trip restore
            // keeps the condition the loop settled.
            var s = NewBakeryDirectChain();
            var driver = new ShoppingTripDriver(s.Loop);
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, Day);
            need.UnitsNeeded = 14;
            need.Urgency01 = 0.9f;
            var state = new PerishableLotState(
                    new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), "lot:1"),
                    BakeryBreadCatalog.BreadLoafId, 14, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            var dto = new PlannedShoppingTripSaveDto
            {
                TripId = "trip-1",
                Need = need,
                HouseholdId = HouseholdId,
                ShopperPersonId = ShopperPersonId,
                DayIndex = Day,
                DepartureMinuteOfDay = 600,
                State = (int)PlannedShoppingTrip.TripState.InStore,
                BatchId = "carry-12-0",
                BatchItemId = HouseholdItemCatalog.BreadId,
                BatchUnits = 14,
                BatchUnitPriceCents = 8,
                BatchReceiptId = "rcpt-test",
                BatchCustody = (int)GoodsCustodyState.InHand,
                BatchProvenance = "test stranded batch",
                BatchPurchaseDayIndex = Day,
                BatchHouseholdUnitsPerStoreUnit = 1,
                BatchSupplierName = "Pierce Bakery",
                BatchPerishableCondition = state.CaptureSaveDto(),
                BatchHasPerishableCondition = true,
            };
            driver.LoadFromSaveDto(new List<PlannedShoppingTripSaveDto> { dto });
            Assert.AreEqual(1, driver.ActiveTrips.Count, "one trip restores");
            PlannedShoppingTrip restored = driver.ActiveTrips[0];
            Assert.IsNotNull(restored.Batch, "the restored trip carries its batch");
            Assert.IsTrue(restored.Batch.HasPerishableCondition, "the restored batch carries its condition");
            Assert.IsTrue(PerishableLotState.FromSaveDto(restored.Batch.PerishableCondition).EqualsState(state),
                "the restored batch's state matches the saved state exactly");
            List<PlannedShoppingTripSaveDto> roundTripped = driver.CaptureSaveDto();
            Assert.AreEqual(1, roundTripped.Count, "one trip captures back");
            Assert.IsTrue(roundTripped[0].BatchHasPerishableCondition, "the re-captured DTO still carries the condition");
            Assert.IsTrue(PerishableLotState.FromSaveDto(roundTripped[0].BatchPerishableCondition).EqualsState(state),
                "the driver's DTO pair round-trips the batch condition exactly");
        }

        [Test]
        public void StrandedBatch_ContinueDepositsOnceWithCondition()
        {
            // The U-3 Continue entry deposits a stranded batch exactly
            // once, and the deposited lot keeps the batch's condition —
            // the condition here comes from a real baked lot, split off
            // through the Slice 5 Split (inheriting condition exactly).
            var s = NewBakeryDirectChain();
            BakeryBreadLot shelfLot = s.Bakery.BreadShelf[0];
            PerishableLotState lotState = StateOf(shelfLot);
            (PerishableLotState remaining, PerishableLotState taken) = lotState.Split(14, Day);
            Assert.AreEqual(10, remaining.Units, "the shelf keeps 10 loaves in the state split");
            Assert.AreEqual(14, taken.Units, "the stranded part carries 14 loaves");
            var batch = new CarriedGoodsBatch
            {
                BatchId = "carry-12-0",
                HouseholdId = HouseholdId,
                HolderPersonId = ShopperPersonId,
                ItemId = HouseholdItemCatalog.BreadId,
                Units = 14,
                UnitPriceCents = 8,
                ReceiptId = "rcpt-test",
                Provenance = "test stranded batch from the bakery shelf",
                Custody = GoodsCustodyState.InHand,
                PurchaseDayIndex = Day,
                HouseholdUnitsPerStoreUnit = 1,
                SupplierName = "Pierce Bakery",
                PerishableCondition = taken.CaptureSaveDto(),
                HasPerishableCondition = true,
            };
            var loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.Journeys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            Assert.IsNotNull(loop.AttachRestoredBatch(batch), "the stranded batch must attach to loop custody");
            FundHouseholdDirect(s, 1000);
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, Day);
            need.UnitsNeeded = 14;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            string problem = loop.ReceiveCarriedBatch(batch.BatchId, Day, s.Diag);
            Assert.IsNull(problem, "Continue must deposit the stranded batch: " + problem);
            Assert.AreEqual(0, loop.ActiveCustodyBatches.Count, "the batch leaves custody on deposit");
            HouseholdItemLot deposited = HouseholdBreadLot(s.Inventories);
            Assert.AreEqual(14, deposited.QuantityUnits, "the deposited lot holds the 14 loaves");
            Assert.IsTrue(PerishableLotState.FromSaveDto(deposited.PerishableCondition).EqualsState(taken),
                "the Continued deposit keeps the batch's state verbatim");
            string second = loop.ReceiveCarriedBatch(batch.BatchId, Day, s.Diag);
            Assert.IsNotNull(second, "a second Continue of the same batch must be refused, never double-deposit");
            Assert.AreEqual(14, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "exactly 14 loaves in the household, never 28");
        }

        [Test]
        public void Aging_OnlyByAuthoredRule_AcceptanceP()
        {
            // Acceptance P, re-pinned through the integrated path: the
            // same simulated elapsed time produces the same deterioration
            // regardless of tick subdivision — one AgeShelf(18) call and
            // six daily AgeShelf calls land on identical attached states
            // (Stale, AsOfDay 18, same anchor). The flour lot's stored
            // state does not move at all: no authored parameter exists
            // for it, so its condition stays Unknown and its remaining
            // life stays Unknown.
            var logA = new CustodyTransferLog();
            Chain a = ChainAtBakeNoWholesale(logA);
            PerishableLotState flourBefore = StateOf(FlourLot(a));
            a.Bakery.AgeShelf(18, a.Diag);
            PerishableLotState agedOnce = StateOf(a.Bakery.BreadShelf[0]);
            Assert.AreEqual(PerishablePhysicalCondition.Stale, agedOnce.PhysicalCondition, "six days past baking: Stale");
            Assert.AreEqual(18.0, agedOnce.AsOfDay, "one jump lands on day 18");
            Assert.AreEqual(Day, agedOnce.ProducedOrAcquiredDay, "the anchor is still the bake day");
            Assert.AreEqual(24, agedOnce.Units, "aging never changes the quantity");
            Assert.IsTrue(StateOf(FlourLot(a)).EqualsState(flourBefore),
                "the flour lot's stored state is untouched by shelf aging");

            var logB = new CustodyTransferLog();
            Chain b = ChainAtBakeNoWholesale(logB);
            for (int day = 13; day <= 18; day++)
            {
                b.Bakery.AgeShelf(day, b.Diag);
            }

            PerishableLotState agedDaily = StateOf(b.Bakery.BreadShelf[0]);
            Assert.IsTrue(agedDaily.EqualsState(agedOnce),
                "one 6-day jump and six 1-day steps produce identical attached states (acceptance P)");
            Assert.AreEqual(BakeryGoodsCondition.Stale, b.Bakery.BreadShelf[0].Condition,
                "the authored AgeToDay rule agrees: Stale on day 18");
            // A non-bread lot aged 30 days by the pure function does
            // not change either, and its remaining life stays Unknown.
            // (EqualsState would also demand an identical AsOfDay, but
            // elapsed time honestly advances the state's clock — only
            // the condition and life must not move.)
            PerishableLotState flourAged = PerishableLotState.ApplyElapsedTime(flourBefore, 30.0, "ambient");
            Assert.AreEqual(flourBefore.PhysicalCondition, flourAged.PhysicalCondition,
                "30 simulated days move no non-bread condition");
            Assert.AreEqual(flourBefore.PerceivedCondition, flourAged.PerceivedCondition,
                "30 simulated days move no non-bread perceived condition");
            Assert.AreEqual(flourBefore.SafetyState, flourAged.SafetyState,
                "30 simulated days move no non-bread safety state");
            Assert.IsTrue(!flourAged.RemainingUsefulLife.IsKnown,
                "30 simulated days invent no shelf life for flour");
            Assert.AreEqual(flourBefore.Units, flourAged.Units, "30 simulated days move no flour units");
            Assert.AreEqual(flourBefore.AsOfDay + 30.0, flourAged.AsOfDay,
                "only the state's clock advances under elapsed time");
        }

        [Test]
        public void PreservationAndMixing_NeverImprove()
        {
            // Canon XXXI 4.6 through the attached lot: preservation
            // transforms and does not reset (a DayOld lot stays DayOld),
            // and mixing never averages conditions — lots with different
            // conditions do not merge, so no lot is rejuvenated by
            // another's freshness.
            var log = new CustodyTransferLog();
            Chain s = ChainAtBakeNoWholesale(log);
            s.Bakery.AgeShelf(13, s.Diag);
            BakeryBreadLot lot = s.Bakery.BreadShelf[0];
            Assert.AreEqual(BakeryGoodsCondition.DayOld, lot.Condition, "one day past baking: DayOld");
            PerishableLotState dayOld = StateOf(lot);
            PerishableLotState preserved = dayOld.ApplyPreservation("smoking", 13);
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, preserved.PhysicalCondition,
                "preservation never rejuvenates: DayOld stays DayOld");
            Assert.AreEqual("smoking", preserved.PreservationMethod, "the method is recorded");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, StateOf(lot).PhysicalCondition,
                "preservation returns a new state; the stored lot state is untouched until a flow writes it back");
            Assert.IsTrue(string.IsNullOrEmpty(StateOf(lot).PreservationMethod),
                "no preservation is recorded on the lot itself");

            var inventories = new HouseholdInventoryRegistry();
            HouseholdInventory inv = inventories.GetOrCreate(HouseholdId);
            var freshState = new PerishableLotState(
                    new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "1"),
                    HouseholdItemCatalog.BreadId, 10, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            var oldState = new PerishableLotState(
                    new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "2"),
                    HouseholdItemCatalog.BreadId, 10, Day - 1)
                .WithConditions(PerishablePhysicalCondition.DayOld, PerishablePhysicalCondition.DayOld);
            Assert.IsNull(inv.AddLot(HouseholdItemCatalog.BreadId, 10, Day, "src fresh", condition: freshState));
            Assert.IsNull(inv.AddLot(HouseholdItemCatalog.BreadId, 10, Day, "src day-old", condition: oldState));
            Assert.AreEqual(0, inv.MergeCompatibleLots(Day),
                "lots with different conditions never merge — mixing must not average or rejuvenate");
            Assert.AreEqual(2, inv.Lots.Count, "both lots survive unmerged");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh,
                PerishableLotState.FromSaveDto(inv.Lots[0].PerishableCondition).PhysicalCondition,
                "the fresh lot stays Fresh");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld,
                PerishableLotState.FromSaveDto(inv.Lots[1].PerishableCondition).PhysicalCondition,
                "the day-old lot stays DayOld");
            var freshState2 = new PerishableLotState(
                    new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "3"),
                    HouseholdItemCatalog.BreadId, 5, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            Assert.IsNull(inv.AddLot(HouseholdItemCatalog.BreadId, 5, Day, "src fresh 2", condition: freshState2));
            Assert.AreEqual(1, inv.MergeCompatibleLots(Day),
                "two Fresh lots from the same bake day do merge");
            Assert.AreEqual(2, inv.Lots.Count, "three lots become two");
            HouseholdItemLot survivor = null;
            foreach (HouseholdItemLot candidate in inv.Lots)
            {
                if (candidate != null && candidate.QuantityUnits == 15)
                    survivor = candidate;
            }
            Assert.IsNotNull(survivor, "the merged survivor holds 15 loaves");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh,
                PerishableLotState.FromSaveDto(survivor.PerishableCondition).PhysicalCondition,
                "the merged survivor stays Fresh, never reset");
            Assert.AreEqual(15, PerishableLotState.FromSaveDto(survivor.PerishableCondition).Units,
                "the survivor's state tracks the merged quantity");
        }

        [Test]
        public void SplitAndConsume_KeepStateUnitsHonest()
        {
            // Split inherits the condition exactly on both parts, and
            // consumption keeps the stored state's units honest — the
            // state always describes the lot as it stands.
            var inventories = new HouseholdInventoryRegistry();
            HouseholdInventory inv = inventories.GetOrCreate(HouseholdId);
            var freshState = new PerishableLotState(
                    new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "1"),
                    HouseholdItemCatalog.BreadId, 24, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            Assert.IsNull(inv.AddLot(HouseholdItemCatalog.BreadId, 24, Day, "src", condition: freshState));
            int lotId = inv.Lots[0].LotId;
            int childId = inv.SplitLot(lotId, 10, Day);
            Assert.IsTrue(childId >= 0, "split must succeed: " + string.Join("; ", inv.Diagnostics));
            HouseholdItemLot parent = null;
            HouseholdItemLot child = null;
            foreach (HouseholdItemLot candidate in inv.Lots)
            {
                if (candidate != null && candidate.LotId == lotId) parent = candidate;
                if (candidate != null && candidate.LotId == childId) child = candidate;
            }
            Assert.IsNotNull(parent, "the parent lot survives the split");
            Assert.IsNotNull(child, "the child lot exists");
            Assert.AreEqual(14, parent.QuantityUnits, "the parent keeps 14 loaves");
            Assert.AreEqual(10, child.QuantityUnits, "the child holds 10 loaves");
            Assert.AreEqual(14, StateOf(parent).Units, "the parent's state tracks 14");
            Assert.AreEqual(10, StateOf(child).Units, "the child's state tracks 10");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, StateOf(parent).PhysicalCondition,
                "the parent keeps its condition through the split");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, StateOf(child).PhysicalCondition,
                "the child inherits the condition exactly, no fresh start");
            Assert.AreEqual(Day, StateOf(child).ProducedOrAcquiredDay, "the child inherits the bake-day anchor");
            var records = new List<HouseholdConsumptionRecord>();
            Assert.IsNull(inv.Consume(HouseholdItemCatalog.BreadId, 4, Day, "meal", "test", records));
            Assert.AreEqual(1, records.Count, "one consumption record");
            Assert.AreEqual(lotId, records[0].LotId, "consumption draws the oldest lot first");
            Assert.AreEqual(10, parent.QuantityUnits, "the parent is down to 10 loaves after the meal");
            Assert.AreEqual(10, StateOf(parent).Units, "the parent's state tracks the meal");
            Assert.AreEqual(10, child.QuantityUnits, "the child is untouched by the meal");
        }

        [Test]
        public void Attachment_ChangesNothingInAnyLiveAuthority_AcceptanceQ()
        {
            // Acceptance Q: the attachment changes nothing in any live
            // authority. Drive the chain to the bake, snapshot every
            // quantity and settlement, exercise the attached states hard
            // (copies only — deterioration, preservation, split,
            // spoilage accounting, DTO round-trips), and snapshot again.
            // The no-wholesale chain: the shelf lot keeps its 24 loaves,
            // so the state copies below can split and spoil from a
            // non-empty state (ChainAtBake drains the shelf into the
            // store pool, leaving the bread state at 0 units).
            var log = new CustodyTransferLog();
            Chain s = ChainAtBakeNoWholesale(log);
            int supplierShelfBefore = s.Supplier.GetInventoryUnits("bakery_flour_sack");
            int supplierCashBefore = s.Supplier.CashCents;
            int binFlourBefore = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsBefore = NotionsLot(s).Units;
            int shelfBefore = s.Bakery.BreadShelf[0].UnitCount;
            int poolBefore = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            int storeCashBefore = s.StoreRuntime.CurrentCashCents;
            int bakeryCashBefore = s.BakeryRuntime.CurrentCashCents;

            PerishableLotState bread = StateOf(s.Bakery.BreadShelf[0]);
            PerishableLotState aged = PerishableLotState.ApplyElapsedTime(bread, 30.0, "ambient");
            Assert.AreEqual(PerishablePhysicalCondition.Stale, aged.PhysicalCondition, "the copy ages; the lot does not");
            PerishableLotState preserved = bread.ApplyPreservation("smoking", Day);
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, preserved.PhysicalCondition, "the copy preserves; the lot does not");
            (PerishableLotState left, PerishableLotState moved) = bread.Split(5, Day);
            Assert.AreEqual(19, left.Units, "the copy splits; the lot does not");
            Assert.AreEqual(5, moved.Units, "the split part carries 5");
            SpoilageRecord loss = bread.RecordSpoilage(3, "mould", Day, "test evidence", out PerishableLotState afterLoss);
            Assert.AreEqual(3, loss.UnitsLost, "the spoilage record accounts 3 lost");
            Assert.AreEqual(21, afterLoss.Units, "the copy accounts the loss; the lot does not");
            PerishableLotState roundTripped = PerishableLotState.FromSaveDto(bread.CaptureSaveDto());
            Assert.IsTrue(roundTripped.EqualsState(bread), "the DTO round-trip is exact on a copy");
            PerishableLotState flour = StateOf(FlourLot(s));
            PerishableLotState flourAged = PerishableLotState.ApplyElapsedTime(flour, 30.0, "ambient");
            Assert.AreEqual(flour.PhysicalCondition, flourAged.PhysicalCondition,
                "flour does not move under elapsed time, even on a copy");
            Assert.IsTrue(!flourAged.RemainingUsefulLife.IsKnown,
                "flour gains no invented shelf life under elapsed time, even on a copy");
            Assert.AreEqual(flour.AsOfDay + 30.0, flourAged.AsOfDay,
                "only the copy's clock advances under elapsed time");

            Assert.AreEqual(supplierShelfBefore, s.Supplier.GetInventoryUnits("bakery_flour_sack"), "supplier shelf unchanged");
            Assert.AreEqual(supplierCashBefore, s.Supplier.CashCents, "supplier cash unchanged");
            Assert.AreEqual(binFlourBefore, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin flour unchanged");
            Assert.AreEqual(notionsBefore, NotionsLot(s).Units, "notions unchanged");
            Assert.AreEqual(shelfBefore, s.Bakery.BreadShelf[0].UnitCount, "shelf unchanged");
            Assert.AreEqual(poolBefore, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "store pool unchanged");
            Assert.AreEqual(storeCashBefore, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(bakeryCashBefore, s.BakeryRuntime.CurrentCashCents, "bakery cash unchanged");
            Assert.AreEqual(PerishablePhysicalCondition.Fresh, StateOf(s.Bakery.BreadShelf[0]).PhysicalCondition,
                "the attached lot state itself is unchanged by exercising copies");
        }

        [Test]
        public void ChainFigures_Unchanged_AcceptanceR()
        {
            // Acceptance R: the chain figures are unchanged by the
            // attachment — 16-sack haul, 125c settlement, 12 lb to 24
            // loaves, 14 loaves at 5c from the store pool — plus the
            // bakery-direct figures, 14 loaves at the bakery's own 8c
            // fresh price.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            PlaceFreightOrder(s);
            Assert.AreEqual(Sacks, s.Lot.PackageUnits, "16-sack haul");
            Assert.AreEqual(160, s.Lot.InventoryUnits, "16 sacks x 10 lb");
            Assert.AreEqual(768, s.Lot.TotalCostCents, "16 sacks x 48c");
            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once (125c)");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once (125c)");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "12 lb flour -> 24 loaves");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb after the 12 lb dispense");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the 24 baked loaves sit in the store pool");
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.AreEqual(14, trip.UnitsAcquired, "14 loaves at 5c from the store pool");
            Assert.AreEqual(70, trip.BuyerOutflowCents, "14 loaves x 5c");
            Assert.AreEqual(930, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 70c");
            Assert.AreEqual(14, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "14 bread units in household inventory");

            var direct = NewBakeryDirectChain();
            FundHouseholdDirect(direct, 1000);
            ShoppingTripResult directTrip = BuyBreadDirect(direct, 14, Day);
            Assert.IsTrue(directTrip.Success, "bakery-direct purchase succeeds; failure: " + directTrip.FailureReason);
            Assert.AreEqual(14, directTrip.UnitsAcquired, "14 loaves from the bakery counter");
            Assert.AreEqual(112, directTrip.BuyerOutflowCents, "14 loaves x 8c bakery fresh price");
            Assert.AreEqual(888, direct.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 112c");
            Assert.AreEqual(10, direct.Bakery.BreadShelf[0].UnitCount, "shelf keeps the 10 unsold loaves");
        }

        [Test]
        public void ReadModel_SurfacesAttachedCondition()
        {
            // V1 point 9 through the read model: what the shopper
            // carries mid-trip reports the batch's attached Fresh
            // condition (no longer Unknown), and a household lot owned
            // by the shopper reports its attached condition too.
            var s = NewBakeryDirectChain();
            PersonItemCondition midTripCondition = PersonItemCondition.Unknown;
            int midTripCount = -1;
            s.Loop.MidTripObserver = (result, batch) =>
            {
                IReadOnlyList<PersonInventoryReadModel.CarriedItem> carried =
                    s.Loop.PersonInventory.CarriedItems(ShopperPersonId);
                midTripCount = carried.Count;
                if (carried.Count == 1)
                    midTripCondition = carried[0].Condition;
            };
            FundHouseholdDirect(s, 1000);
            ShoppingTripResult trip = BuyBreadDirect(s, 14, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.AreEqual(1, midTripCount, "the shopper carries exactly one batch mid-trip");
            Assert.AreEqual(PersonItemCondition.Fresh, midTripCondition,
                "the read model reports the carried batch's attached Fresh condition");
            Assert.AreEqual(0, s.Loop.PersonInventory.CarriedItems(ShopperPersonId).Count,
                "after receipt the shopper carries nothing");
            Assert.AreEqual(0, s.Loop.PersonInventory.HouseholdLotsOwnedBy(ShopperPersonId).Count,
                "the receipt books the lot to the household (OwnerPersonId -1), never to the shopper — the Slice 3 finding stands");
            var freshState = new PerishableLotState(
                    new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "9"),
                    HouseholdItemCatalog.BreadId, 10, Day)
                .WithConditions(PerishablePhysicalCondition.Fresh, PerishablePhysicalCondition.Fresh);
            HouseholdInventory inv = s.Inventories.GetOrCreate(HouseholdId);
            Assert.IsNull(inv.AddLot(HouseholdItemCatalog.BreadId, 10, Day, "src owned",
                ownerPersonId: ShopperPersonId, condition: freshState));
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> owned =
                s.Loop.PersonInventory.HouseholdLotsOwnedBy(ShopperPersonId);
            Assert.AreEqual(1, owned.Count, "one lot is owned by the shopper as a person");
            Assert.AreEqual(PersonItemCondition.Fresh, owned[0].Condition,
                "the read model reports the owned lot's attached Fresh condition");
        }

        [Test]
        public void StagedTransfer_PreservesBakedDayAging()
        {
            // The staged-to-sellable transfer ages from the BAKED day,
            // never the transfer day — and the attached state agrees:
            // a loaf baked yesterday is DayOld when transferred today.
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var ids = new EntityIdRegistry();
            var diag = new List<string>();
            var staged = new BakeryStagedBreadLot
            {
                LotId = "staged-1",
                BatchId = "batch-1",
                ProductId = BakeryBreadCatalog.BreadLoafId,
                BakedDayIndex = Day - 1,
                Units = 10,
                InputProvenance = new List<string> { "test provenance" },
            };
            BakeryBreadLot lot = bakery.ReceiveTransferredBreadLot(staged, Day, ids, diag);
            Assert.IsNotNull(lot, "the transfer must succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual(BakeryGoodsCondition.DayOld, lot.Condition, "baked yesterday, transferred today: DayOld");
            Assert.IsTrue(lot.HasPerishableCondition, "the transferred lot carries a condition state");
            PerishableLotState state = StateOf(lot);
            Assert.AreEqual(Day - 1, state.ProducedOrAcquiredDay, "the age anchor is the baked day, not the transfer day");
            Assert.AreEqual(PerishablePhysicalCondition.DayOld, state.PhysicalCondition, "the state agrees: DayOld");
            Assert.AreEqual(10, state.Units, "the state carries the transferred quantity");
            Assert.IsNull(bakery.ReceiveTransferredBreadLot(staged, Day, ids, diag),
                "the same staged lot must never transfer twice");
        }

        [Test]
        public void RetailLot_DtoGap_Pinned()
        {
            // Blocked seam (c), pinned by execution: the retail lot's
            // save DTO declares no perishable-condition field, so a
            // condition attached in memory could not round-trip through
            // the lot's own DTO pair. The DTO lives in the
            // harness-excluded SaveDtos.cs
            // (BusinessRetailItemLotSaveDto) whose harness copy is a
            // stale verbatim extraction (commit a38c6aa); attaching
            // without DTO coverage would lose the state on load, so the
            // attachment is deferred to the slice that regenerates the
            // shim. The retail lot record itself declares no condition
            // field either.
            foreach (FieldInfo field in typeof(BusinessRetailItemLotSaveDto).GetFields())
            {
                Assert.IsTrue(!field.Name.ToLowerInvariant().Contains("perishable"),
                    "the retail lot save DTO must not half-carry a condition field: " + field.Name);
            }
            foreach (PropertyInfo property in typeof(BusinessRetailItemLotSaveDto).GetProperties())
            {
                Assert.IsTrue(!property.Name.ToLowerInvariant().Contains("perishable"),
                    "the retail lot save DTO must not half-carry a condition property: " + property.Name);
            }
            Assert.IsNull(typeof(BusinessRetailItemLotState).GetField(
                    "perishableCondition", BindingFlags.NonPublic | BindingFlags.Instance),
                "the retail lot record carries no condition field (deferred with its DTO)");
        }

        // ---- Bakery-direct chain (bake-only + bakery-counter purchase) ----

        private sealed class BakeryDirectChain
        {
            public List<string> Diag = new List<string>();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public Series2BakerySupplierAdapter Adapter;
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel Journeys;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
        }

        private static BakeryDirectChain NewBakeryDirectChain()
        {
            var s = new BakeryDirectChain();
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(2000);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag), "flour lot should be received");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag), "notions lot should be received");
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag), "fuel lot should be received");
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, Day, s.Diag),
                "one batch must plan; diag: " + string.Join("; ", s.Diag));
            int baked = driver.RunScheduledDay(
                Day, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");

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
            s.Journeys = new JourneyModel();
            s.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            s.Journeys.RegisterLocation(new JourneyLocation("bakery", JourneyLocationKind.Store, "Pierce Bakery", 2f, 0f));
            Assert.IsNull(s.Journeys.AddEdge("home", "bakery", 2.0f, "river road"));
            s.Tasks = new TaskAuthority();
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Budgets = new WorkTimeBudgetStore();
            s.Budgets.EnsureDay(Day);
            s.Budgets.EnsureDay(Day + 1);
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            s.Adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            s.Adapter.DayIndex = Day;
            Series2BakerySupplierAdapter.RegisterIn(s.Directory, s.Adapter);
            s.Knowledge.LearnSupplier(ShopperPersonId, "pierce-bakery-1", "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.Journeys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static ShoppingLoopOptions ShoppingOptionsDirect(BakeryDirectChain s) =>
            new ShoppingLoopOptions
            {
                PersonLocationId = pid => s.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => s.PersonLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = s.Diag,
            };

        private static void FundHouseholdDirect(BakeryDirectChain s, int cents)
        {
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(HouseholdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
        }

        private static ShoppingTripResult BuyBreadDirect(BakeryDirectChain s, int units, int dayIndex)
        {
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, dayIndex);
            need.UnitsNeeded = units;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            return s.Loop.ExecuteNeed(need, dayIndex, ShoppingOptionsDirect(s));
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
                AnimalOwnerKind.Business, CarrierId, Day, "Save/load fixture");
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

        private static Chain ChainAtReceive(CustodyTransferLog log)
        {
            Chain s = NewChain(log);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");
            return s;
        }

        private static Chain ChainAtBake(CustodyTransferLog log)
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

        // The flour bin holds flour AND notions lots: find a lot by name
        // instead of indexing Lots[0].
        private static BakeryFlourLot FlourLot(Chain s) => FlourLot(s.Bakery);

        private static BakeryFlourLot FlourLot(BakeryShopRuntime bakery)
        {
            BakeryFlourLot found = null;
            int flourCount = 0;
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
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

        private static BakeryFlourLot NotionsLot(Chain s) => NotionsLot(s.Bakery);

        private static BakeryFlourLot NotionsLot(BakeryShopRuntime bakery)
        {
            BakeryFlourLot found = null;
            foreach (BakeryFlourLot lot in bakery.FlourStock.Lots)
            {
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.NotionsItemId, StringComparison.Ordinal))
                    found = lot;
            }
            Assert.IsNotNull(found, "exactly one notions lot is expected in the bin");
            return found;
        }

        private static HouseholdItemLot HouseholdBreadLot(Chain s) =>
            HouseholdBreadLot(s.Inventories);

        private static HouseholdItemLot HouseholdBreadLot(HouseholdInventoryRegistry registries)
        {
            HouseholdItemLot found = null;
            foreach (HouseholdItemLot lot in registries.GetOrCreate(HouseholdId).Lots)
            {
                if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    found = lot;
            }
            Assert.IsNotNull(found, "exactly one household bread lot is expected");
            return found;
        }
    }
}
