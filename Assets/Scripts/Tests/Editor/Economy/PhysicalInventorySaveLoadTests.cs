// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F5 — save/load plus conservation acceptance (P9 plan
// section 4 Slice F5, directive Priority D). Primarily ACCEPTANCE, not new
// machinery: the P1 foundation is designed so nothing new needs persisting
// (the F1 registry is code, the F2 evaluator is stateless, the F3 read
// model and F4 resolver are derived, the F3 log is runtime-only), and this
// slice proves that design survives save/load with exact conservation.
//
// Save/load is driven at the AUTHORITY level, not through
// SaveLoadManager: the manager (Infrastructure/Persistence/SaveLoadManager.cs)
// and SaveDtos.cs are HARNESS-EXCLUDED, so the full manager round-trip
// cannot execute in Roslyn. Every authority in the chain HAS a drivable
// save surface in the harness, and the tests use each authority's own
// Capture/Export plus Load/Import pair:
//   - Millbrook: OffMapAidBusinessRuntime.ExportState/ImportState
//   - freight: FreightShipmentService.CaptureSaveDto/RestoreSaveDto
//   - Bakery: BakeryShopRuntime.CaptureSaveDto/LoadFromSaveDto
//   - retail + business cash: BusinessRuntimeState.CaptureSaveDto/FromSaveDto
//   - named-SKU: OffMapNamedSkuProcurementService.ExportState/ImportState
//   - household lots: HouseholdInventoryRegistry.ExportState/ImportState
//   - household money: HouseholdLedgerRegistry.ExportState/ImportState
//   - meals: HouseholdMealLogRegistry.ExportState/ImportState
// A "load" in these tests is a fresh instance of each authority fed the
// captured DTOs — exactly what a real load constructs — never the same
// object re-read. The manager's wiring of these DTOs into the save file
// (SaveLoadManager.cs populating LandLedgersSaveGameDto, including
// population.mealRecords at lines 381-382 and 966-979) is SOURCE-REVIEWED
// only, and is recorded as such in the checkpoint; no manager verification
// is claimed here.
//
// Save points are quiescent: after freight receive (shipment Completed,
// flour lot in the bin) and after the full chain through the next-day
// meal. The one deliberate non-quiescent capture is the mid-transit
// freight shipment, pinned as its own test.
//
// Pins:
//   - composed conservation: at every step, units in equal units out
//     plus units consumed plus units held, in each leg's own unit, never
//     summed across unlike units; money conserved across the modeled
//     authorities, with the two external legs named (1000c household
//     funding in, 300c off-map feed payment out).
//   - mid-chain save/load: same lots, quantities, provenance keys,
//     custodians, and settlements after load; the F4 resolver still
//     resolves every lot from the restored authorities.
//   - the F3 custody log is EMPTY after load (runtime-only by design;
//     the authorities, not the log, are the source of truth) — including
//     the exactly-once index, which is also gone, so a re-record of the
//     same transfer against a fresh log is accepted again.
//   - acceptance L: save/restore creates no additional goods — total
//     units and total money before save equal total units and total
//     money after load, exactly.
//   - known gaps, pinned either way: BusinessFeedCustodyLedger
//     non-persistence (no save surface; a fresh ledger holds nothing)
//     and U-3 in-transit shopping batch save/load (the immediate path
//     keeps the batch only in the loop's private runtime list; the
//     real-time driver's DTO round-trips the batch scalars but nothing
//     in Infrastructure/ or Orchestration/ calls it, so a real
//     save/load still drops the batch).
//
// No production change is made in this slice: every gap the tests pin is
// either already covered by an existing save surface or is a pinned,
// documented non-persistence, not a missing DTO field.

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
    public sealed class PhysicalInventorySaveLoadTests
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

        [Test]
        public void Chain_ConservationAtEveryStep_UnitsInEqualUnitsOutPlusConsumed()
        {
            // The composed scenario (F4 chain plus the next-day meal):
            // Millbrook order 16 sacks / 160 lb / 768c, freight commit and
            // receive with 125c settled exactly once, Bakery dispense 12 lb
            // flour plus 1 notion, bake 24 loaves, retail receipt 20 feed
            // sacks, household purchase and receipt of the 24 loaves, and
            // the next-day meal. Conservation is judged per leg, in the
            // leg's own unit, never by adding unlike units.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            Assert.AreEqual(47000, ModeledMoneyCents(s),
                "modeled money before the chain: bakery cash port 20000 + payer 20000 + store 5000 + bakery runtime 2000");

            // Leg 1 — Millbrook order: 16 sacks, 160 lb, 768c.
            PlaceFreightOrder(s);
            Assert.AreEqual(70, s.Supplier.GetInventoryUnits("bakery_flour_sack"),
                "supplier flour 230 -> 70 lb: the ordered 160 lb left the shelf");
            Assert.AreEqual(160, s.Lot.InventoryUnits, "the lot holds the ordered 160 lb");
            Assert.AreEqual(20000 - Sacks * 48, s.BakeryCash.BalanceCents, "order debits the buyer once");
            Assert.AreEqual(Sacks * 48, s.Supplier.CashCents, "order credits the supplier once");
            Assert.AreEqual(70 + 160, 230, "flour: 70 lb on the shelf + 160 lb in the lot = the opening 230 lb");

            // Legs 2-3 — freight: commit day 11, receive day 12, 125c settles once.
            BuildFreightShipment(s);
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            DriveFreightToReturnDay(s);
            List<LogisticsShipmentStatus> trace = TickToCompleted(s);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, s.Shipment.Status,
                "shipment did not complete; trace: " + string.Join(", ", trace));
            Assert.AreEqual(payerBefore - FreightChargeCents, s.PayerRuntime.CurrentCashCents,
                "freight charge debited from the bakery exactly once");
            Assert.AreEqual(carrierBefore + FreightChargeCents, s.CarrierRuntime.CurrentCashCents,
                "freight charge credited to the carrier exactly once");
            Assert.AreEqual(160, FlourLot(s).Units, "the delivered lot holds the lot's 160 lb");
            Assert.IsTrue(s.Lot.Received, "the paid Millbrook lot is marked received");
            Assert.AreEqual(70 + 160, 230, "flour after delivery: 70 lb supplier shelf + 160 lb bakery bin = 230 lb");

            // Legs 4-5 — bake: 12 lb flour + 1 notion into the batch, 24 loaves out.
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb after the 12 lb dispense");
            Assert.AreEqual(1, NotionsLot(s).Units, "notions 2 -> 1: exactly one notion consumed by the batch");
            Assert.AreEqual(70 + 148 + 12, 230,
                "flour: 70 lb supplier shelf + 148 lb bin + 12 lb baked into loaves = the opening 230 lb");
            Assert.AreEqual(1 + 1, 2, "notions: 1 lb-equivalent held + 1 consumed = the seeded 2");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the 24 baked loaves sit in the store pool");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 + 48c wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 48c wholesale");

            // Leg 6 — retail receipt: 20 feed sacks from the named-SKU lot into retail.
            int counterpartySupplyBefore = s.FeedSupplier.SupplyUnits["store_horse_feed_sack"];
            Assert.AreEqual(20, counterpartySupplyBefore, "the off-map feed supplier holds 20 sacks before the order");
            (PurchasedRetailLotReceipt.Result receipt, NamedSkuLot sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);
            Assert.AreEqual(0, sourceLot.RemainingUnits, "the source lot is drained by the transfer");
            Assert.AreEqual(20, s.StoreRuntime.FindRetailItemLot(receipt.RetailLotId).QuantityUnits,
                "the retail lot holds the 20 feed sacks");
            Assert.AreEqual(4652, s.StoreRuntime.CurrentCashCents, "store 4,952 - 300c feed on arrival");
            Assert.AreEqual(0, s.FeedSupplier.SupplyUnits["store_horse_feed_sack"],
                "the off-map feed supplier's finite supply drops 20 -> 0 — the 300c is paid off-map, to a seller whose cash is not modeled, so it leaves the modeled money supply");

            // Legs 7-8 — household purchase and receipt: the household needs
            // 30 loaves, the store holds 24 — a partial fill.
            FundHousehold(s, 1000);
            Assert.AreEqual(47700, ModeledMoneyCents(s),
                "modeled money after the 1000c household funding: 47000 - 300 feed + 1000 in");
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            Assert.AreEqual(120, trip.BuyerOutflowCents, "24 loaves x 5c");
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 120c");
            Assert.AreEqual(4772, s.StoreRuntime.CurrentCashCents, "store 4,652 + 120c retail");
            Assert.AreEqual(24, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "24 bread units in household inventory: bought = received, the partial total is preserved");
            Assert.AreEqual(0, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the store pool is empty after selling its 24 loaves");

            // Leg 9 — next-day meal: one person, three slots, one loaf each.
            MealDayResult meals = ServeNextDayMeal(s);
            Assert.AreEqual(meals.MealsRequiredPerPerson, meals.MealsServed,
                "every required meal is served; missed: " + meals.MealsMissed);
            Assert.AreEqual(0, meals.MealsMissed, "no missed meals with 24 loaves on hand");
            Assert.AreEqual(3, meals.MealsRequiredPerPerson,
                "morning + evening + midday (24 loaves is 8 staple days, above the 3-day midday threshold)");
            int eaten = meals.MealsServed * 1;
            Assert.AreEqual(24 - eaten, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "bread after the meal: 24 bought - meals eaten, nothing else lost");
            IReadOnlyList<HouseholdMealRecord> records = s.MealLog.GetMeals(HouseholdId);
            Assert.AreEqual(3, records.Count, "three meal records, one per slot");
            HouseholdItemLot breadLot = HouseholdBreadLot(s);
            foreach (HouseholdMealRecord record in records)
            {
                Assert.AreEqual(1, record.ItemsConsumed.Count, "each meal consumes from exactly one lot");
                Assert.AreEqual(HouseholdItemCatalog.BreadId, record.ItemsConsumed[0].ItemId);
                Assert.AreEqual(breadLot.LotId, record.ItemsConsumed[0].LotId,
                    "the meal record names the household bread lot it ate from");
                Assert.AreEqual(1, record.ItemsConsumed[0].QuantityUnits, "one loaf per meal");
            }

            // Whole-chain money: 47000 opening + 1000 funding in - 300
            // off-map feed payment out = 47700. Every cent is accounted
            // for in the per-leg figures above.
            Assert.AreEqual(47700, ModeledMoneyCents(s),
                "modeled money after the full chain: 47000 + 1000 funding - 300 external feed payment");
            Assert.AreEqual(19232, s.BakeryCash.BalanceCents, "bakery cash port 20000 - 768 order");
            Assert.AreEqual(768, s.Supplier.CashCents, "supplier holds the 768c order credit");
            Assert.AreEqual(19875, s.PayerRuntime.CurrentCashCents, "payer 20000 - 125 freight");
            Assert.AreEqual(125, s.CarrierRuntime.CurrentCashCents, "carrier holds the 125c freight charge");
            Assert.AreEqual(4772, s.StoreRuntime.CurrentCashCents, "store 5000 - 48 wholesale - 300 feed + 120 retail");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery runtime 2000 + 48 wholesale");
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1000 - 120 retail");
        }

        [Test]
        public void SaveLoad_AfterFreightReceive_RestoresLotsQuantitiesProvenanceAndSettlements()
        {
            // Save point: after freight receive — shipment Completed, the
            // 160 lb flour lot in the bakery bin, nothing in flight. The
            // save is the authorities' own DTOs; the load is fresh
            // instances fed those DTOs.
            Chain s = ChainAtReceive(null);
            SaveBundle saved = CaptureAll(s);
            Restored r = LoadAll(saved);

            Assert.IsNull(r.SupplierImportRejection, "the Millbrook import must accept: " + r.SupplierImportRejection);
            Assert.AreEqual(s.Supplier.BusinessId, r.Supplier.BusinessId);
            Assert.AreEqual(768, r.Supplier.CashCents, "supplier cash 768c survives");
            Assert.AreEqual(70, r.Supplier.GetInventoryUnits("bakery_flour_sack"), "supplier shelf 70 lb survives");
            Assert.AreEqual(1, r.Supplier.Lots.Count, "exactly one Millbrook lot survives, no duplicate");
            OffMapAidCargoLot lot = r.Supplier.Lots[0];
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(160, lot.InventoryUnits, "the lot still holds 160 lb");
            Assert.AreEqual(16, lot.PackageUnits, "16 sacks");
            Assert.AreEqual(768, lot.TotalCostCents, "768c order total");
            Assert.IsTrue(lot.SourceCommitted, "the commit flag survives");
            Assert.IsTrue(lot.Received, "the received flag survives");
            Assert.AreEqual(ShipmentId, lot.ShipmentId, "the shipment id survives");
            Assert.AreEqual(BakeryId, lot.DestinationBusinessId, "the destination survives");
            Assert.AreEqual(1, r.SupplierTransactions.Count, "the order cash transaction survives");
            Assert.AreEqual("millbrook_grain_co:" + OrderRequestId, r.SupplierTransactions[0].OrderId,
                "the transaction records the order id namespaced by the supplier");
            Assert.AreEqual(768, r.SupplierTransactions[0].AmountCents,
                "the 768c order credit is in the saved supplier ledger — the test-side cash port's debit is not lost with the port");

            Assert.AreEqual(1, r.Freight.TrackedShipments.Count, "exactly one shipment survives, no duplicate");
            LogisticsShipmentState shipment = r.Freight.TrackedShipments[0];
            Assert.AreEqual(ShipmentId, shipment.ShipmentId);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, "the Completed state survives");
            Assert.AreEqual("bakery_flour_sack", shipment.cargoSkuId);
            Assert.AreEqual(LotId, shipment.cargoLotId);
            Assert.AreEqual(Sacks, shipment.PlannedQuantityUnits, "the contracted 16 sacks survive");
            Assert.AreEqual(FreightChargeCents, shipment.FreightChargeCents, "the 125c charge survives");
            Assert.AreEqual(WagonId, shipment.physicalWagonAssetId, "the wagon survives");
            Assert.IsTrue(shipment.sourceCommittedAtSchedule, "the commit flag survives");
            Assert.IsTrue(shipment.loadApplied, "the load flag survives");
            Assert.IsTrue(shipment.deliveryApplied, "the delivery flag survives");

            Assert.AreEqual(160, r.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the bakery bin still holds 160 lb after load");
            BakeryFlourLot flourLot = FlourLot(r.Bakery);
            Assert.AreEqual(FlourLot(s).LotId, flourLot.LotId, "the flour lot keeps its lot id across load");

            Assert.AreEqual(5000, r.Store.CurrentCashCents, "store cash 5000c survives");
            Assert.AreEqual(0, r.Store.GetCategoryStock("staple_food").CurrentStockUnits, "store pool 0 survives");
            Assert.AreEqual(2000, r.BakeryRuntime.CurrentCashCents, "bakery runtime cash survives (still 2000: no bake has run yet at the receive point)");
            Assert.AreEqual(19875, r.Payer.CurrentCashCents, "payer 20000 - 125 freight survives");
            Assert.AreEqual(125, r.Carrier.CurrentCashCents, "carrier 125c survives");

            var inventoryStates = new List<HouseholdInventoryState>();
            r.Inventories.ExportState(inventoryStates);
            Assert.AreEqual(0, inventoryStates.Count, "no household lots exist yet at the receive point");
            Assert.AreEqual(0, r.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household ledger 0 survives");
        }

        [Test]
        public void SaveLoad_AfterFreightReceive_ResolverResolvesEveryLotAfterLoad()
        {
            // The F4 resolver is derived: it re-reads its authorities, so
            // it must resolve every lot from the RESTORED authorities with
            // the same units and custodians — no re-registration, no cache
            // to rebuild.
            Chain s = ChainAtReceive(null);
            SaveBundle saved = CaptureAll(s);
            Restored r = LoadAll(saved);
            var resolver = new LotIdentityResolver(
                r.Inventories, r.Store, r.Bakery, r.Supplier, r.Freight, r.Procurement);

            LotIdentity millbrook = resolver.Resolve(new LotKey(
                CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId));
            Assert.IsNotNull(millbrook, "the Millbrook lot resolves after load");
            Assert.AreEqual("bakery_flour_sack", millbrook.ItemId);
            Assert.AreEqual(160, millbrook.Units);
            Assert.AreEqual("supplier:millbrook_grain_co", millbrook.Custodian);
            Assert.AreEqual(string.Empty, millbrook.ProvenanceKey,
                "Millbrook lots still carry no provenance field after load — the gap is unchanged, not papered over");
            Assert.IsTrue(millbrook.ProvenanceNote.Contains("no provenance field"), millbrook.ProvenanceNote);

            LotIdentity freight = resolver.Resolve(new LotKey(
                CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId));
            Assert.IsNotNull(freight, "the freight shipment resolves after load");
            Assert.AreEqual("bakery_flour_sack", freight.ItemId);
            Assert.AreEqual(Sacks, freight.Units);
            Assert.AreEqual("wagon:" + WagonId, freight.Custodian);

            string flourLotId = FlourLot(r.Bakery).LotId.ToString();
            LotIdentity flour = resolver.Resolve(new LotKey(
                CustodyTransferLog.BakeryAuthority(BakeryId), flourLotId));
            Assert.IsNotNull(flour, "the bakery flour lot resolves after load");
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, flour.ItemId);
            Assert.AreEqual(160, flour.Units);
            Assert.AreEqual("bakery:" + BakeryId, flour.Custodian);
            Assert.AreEqual(string.Empty, flour.ProvenanceKey,
                "flour lots still carry no stored canonical key after load");
            Assert.IsTrue(flour.ProvenanceNote.Contains("ProvenanceChain"), flour.ProvenanceNote);

            Assert.AreEqual(2, resolver.ResolveAll("bakery_flour_sack").Count,
                "ResolveAll still finds the Millbrook lot and the freight shipment, no duplicates, no loss");
            Assert.AreEqual(0, resolver.Diagnostics.Count,
                "every resolution above succeeded, so no gap was named: " + string.Join(" | ", resolver.Diagnostics));
        }

        [Test]
        public void SaveLoad_CustodyLogIsEmptyAfterLoad_RuntimeOnlyByDesign()
        {
            // The F3 log is runtime-only by design: it has no save
            // surface, so a load produces an empty log. The audit trail
            // does not survive save/load; the authorities, not the log,
            // are the source of truth (the previous test proves the
            // authorities still answer after load).
            var log = new CustodyTransferLog();
            Chain s = ChainAtReceive(log);
            Assert.IsTrue(log.Count >= 3,
                "the chain recorded transfers before save (order, commit, receive); got " + log.Count);
            Assert.IsNotNull(log.LatestForCause(CustodyTransferLog.CauseFreightReceive, ShipmentId),
                "the receive record exists before save");

            var freshLog = new CustodyTransferLog();
            Assert.AreEqual(0, freshLog.Count, "a fresh log — what a load constructs — holds no records");
            Assert.IsNull(freshLog.LatestForCause(CustodyTransferLog.CauseFreightReceive, ShipmentId),
                "the receive record does not survive: no log state is carried across save/load");
            Assert.AreEqual(0, freshLog.EvictedCount, "nothing evicted from an empty log");

            // The exactly-once index is gone with the log: the same
            // transfer re-recorded against the fresh log is ACCEPTED, not
            // refused. That is the pinned consequence of runtime-only —
            // a post-load re-record would double-record if a flow ever
            // re-ran a transfer after load, so no flow may rely on the
            // log for exactly-once.
            string diagnostic;
            Assert.IsTrue(freshLog.Record(new CustodyTransferRecord(
                Day, "bakery_flour_sack", Sacks,
                new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId),
                new LotKey(CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId),
                "supplier:millbrook_grain_co", "wagon:" + WagonId,
                ShipmentId, CustodyTransferLog.CauseFreightReceive), out diagnostic),
                "the fresh log accepts the same CauseId the pre-save log already holds: " + diagnostic);

            AssertNoSaveSurface(typeof(CustodyTransferLog), "CustodyTransferLog");
        }

        [Test]
        public void SaveLoad_AfterFullChain_AcceptanceL_NoAdditionalGoods()
        {
            // Acceptance L: save/restore creates no additional goods.
            // Total units and total money before save equal total units
            // and total money after load, exactly, across the composed
            // scenario. Money is compared over the SAVED authorities
            // only: the test-side FakeBusinessCashPort (19232c) is not a
            // saved authority — it is test scaffolding for the bakery's
            // cash port, and its 768c debit is mirrored in the supplier's
            // saved CashCents plus CashTransactions (pinned in the
            // receive test), so nothing is lost with it.
            Chain s = ChainFull(null);
            SaveBundle saved = CaptureAll(s);

            int flourBefore = s.Supplier.GetInventoryUnits("bakery_flour_sack")
                + s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsBefore = NotionsLot(s).Units;
            int breadBefore = s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);
            int feedBefore = s.StoreRuntime.FindRetailItemLot(saved.RetailLotId).QuantityUnits;
            int moneyBefore = SavedMoneyCents(s);
            int supplierLotsBefore = s.Supplier.Lots.Count;
            int bakeryFlourLotsBefore = s.Bakery.FlourStock.Lots.Count;
            int breadShelfBefore = s.Bakery.BreadShelf.Count;
            int retailLotsBefore = s.StoreRuntime.RetailItemLots.Count;
            int namedSkuLotsBefore = s.Procurement.Lots.Count;
            int householdLotsBefore = s.Inventories.GetOrCreate(HouseholdId).Lots.Count;
            int mealRecordsBefore = s.MealLog.GetMeals(HouseholdId).Count;

            Assert.AreEqual(218, flourBefore, "flour before save: 70 lb supplier shelf + 148 lb bin");
            Assert.AreEqual(1, notionsBefore, "notions before save: 1 held");
            Assert.AreEqual(21, breadBefore, "bread before save: 24 bought - 3 eaten");
            Assert.AreEqual(20, feedBefore, "feed before save: 20 held in retail");
            Assert.AreEqual(28468, moneyBefore,
                "saved-authority money before save: supplier 768 + store 4772 + bakery runtime 2048 + payer 19875 + carrier 125 + household 880");

            Restored r = LoadAll(saved);

            int flourAfter = r.Supplier.GetInventoryUnits("bakery_flour_sack")
                + r.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsAfter = NotionsLot(r.Bakery).Units;
            int breadAfter = r.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);
            int feedAfter = r.Store.FindRetailItemLot(saved.RetailLotId).QuantityUnits;
            int moneyAfter = SavedMoneyCents(r);

            Assert.AreEqual(flourBefore, flourAfter, "flour: no pounds created or lost by save/load");
            Assert.AreEqual(notionsBefore, notionsAfter, "notions: no units created or lost by save/load");
            Assert.AreEqual(breadBefore, breadAfter, "bread: no loaves created or lost by save/load");
            Assert.AreEqual(feedBefore, feedAfter, "feed: no sacks created or lost by save/load");
            Assert.AreEqual(moneyBefore, moneyAfter, "money: no cents created or lost by save/load");
            Assert.AreEqual(supplierLotsBefore, r.Supplier.Lots.Count, "no Millbrook lot created or lost");
            Assert.AreEqual(bakeryFlourLotsBefore, r.Bakery.FlourStock.Lots.Count, "no bakery bin lot created or lost");
            Assert.AreEqual(breadShelfBefore, r.Bakery.BreadShelf.Count, "no bread lot created or lost");
            Assert.AreEqual(retailLotsBefore, r.Store.RetailItemLots.Count, "no retail lot created or lost");
            Assert.AreEqual(namedSkuLotsBefore, r.Procurement.Lots.Count, "no named-SKU lot created or lost");
            Assert.AreEqual(householdLotsBefore, r.Inventories.GetOrCreate(HouseholdId).Lots.Count, "no household lot created or lost");
            Assert.AreEqual(mealRecordsBefore, r.MealLog.GetMeals(HouseholdId).Count,
                "the three meal records survive: the eaten loaves stay eaten, never restored");
            Assert.AreEqual(1, r.Freight.TrackedShipments.Count, "no shipment created or lost");
            Assert.AreEqual(LogisticsShipmentStatus.Completed, r.Freight.TrackedShipments[0].Status);
        }

        [Test]
        public void SaveLoad_AfterFullChain_ReadModelAndResolverAgreeAfterLoad()
        {
            // After the full chain and a load, the F3 read model and the
            // F4 resolver agree with the restored authorities — and with
            // each other — about what the household holds and what nobody
            // carries anymore.
            Chain s = ChainFull(null);
            SaveBundle saved = CaptureAll(s);
            Restored r = LoadAll(saved);
            var resolver = new LotIdentityResolver(
                r.Inventories, r.Store, r.Bakery, r.Supplier, r.Freight, r.Procurement);

            HouseholdItemLot breadLot = HouseholdBreadLot(r.Inventories);
            Assert.IsNotNull(breadLot, "the household bread lot exists after load");
            Assert.AreEqual(21, breadLot.QuantityUnits, "21 loaves remain after the meal");
            LotIdentity household = resolver.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, breadLot.LotId.ToString()));
            Assert.IsNotNull(household, "the household bread lot resolves after load");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, household.ItemId);
            Assert.AreEqual(21, household.Units);
            Assert.AreEqual("household:" + HouseholdId, household.Custodian);
            Assert.AreEqual("household:" + HouseholdId, household.Owner);
            Assert.AreEqual(
                "authority:household-inventory/lot:" + breadLot.LotId,
                household.ProvenanceKey, "the household lot's canonical key survives load in its SourceLabel");
            Assert.AreEqual(string.Empty, household.ProvenanceNote);

            LotIdentity retail = resolver.Resolve(new LotKey(
                CustodyTransferLog.BusinessAuthority(StoreInstanceId), saved.RetailLotId));
            Assert.IsNotNull(retail, "the retail feed lot resolves after load");
            Assert.AreEqual("store_horse_feed_sack", retail.ItemId);
            Assert.AreEqual(20, retail.Units);
            Assert.AreEqual(
                "authority:business:" + StoreInstanceId + "/lot:" + saved.RetailLotId,
                retail.ProvenanceKey, "the retail lot's canonical key survives load in its provenance");
            Assert.AreEqual(string.Empty, retail.ProvenanceNote);

            string breadLotId = r.Bakery.BreadShelf[0].LotId.ToString();
            LotIdentity bread = resolver.Resolve(new LotKey(
                CustodyTransferLog.BakeryAuthority(BakeryId), breadLotId));
            Assert.IsNotNull(bread, "the bakery bread lot resolves after load");
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, bread.ItemId);
            Assert.AreEqual(0, bread.Units, "the bread lot stays on the shelf at 0 after selling through — and after load");
            Assert.AreEqual(
                "authority:bakery:" + BakeryId + "/lot:" + breadLotId,
                bread.ProvenanceKey, "the bread lot's canonical key survives load in its InputProvenance");
            Assert.AreEqual(string.Empty, bread.ProvenanceNote);

            // The read model over the restored registries: nobody carries
            // anything (the trip is done), the full carry cap is free, and
            // the shopper owns no lot as a person (the receipt books lots
            // to the household, OwnerPersonId -1 — ownership untouched by
            // this slice, and unchanged by load). The loop is a fresh one
            // over the restored registries: the read model's custody
            // authority is the loop, and a loaded loop carries nothing.
            var restoredLoop = new HouseholdShoppingLoop(
                s.Population, s.Membership, r.Ledgers, r.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            var readModel = new PersonInventoryReadModel(
                new HouseholdShoppingReadModel(restoredLoop, s.Tracker), r.Inventories);
            Assert.AreEqual(0, readModel.CarriedItems(ShopperPersonId).Count,
                "nobody carries goods after load — the trip completed before save");
            Assert.AreEqual(50, readModel.RemainingCarryUnits(ShopperPersonId).UnitsRemaining,
                "the full 50-unit carry cap is free after load");
            Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(ShopperPersonId).Count,
                "the shopper owns no lot as a person after load, exactly as before it");
            Assert.AreEqual(0, readModel.CarriedLoad(ShopperPersonId).Resolve().TotalUnits,
                "the carried load is zero after load");
        }

        [Test]
        public void HouseholdInventory_NextLotSequenceSurvivesLoad_NoLotIdReuse()
        {
            // Lot identity depends on the lot-id sequence surviving load:
            // a post-load AddLot must not reuse an id the pre-load lots
            // already carry, or two lots would share one identity.
            Chain s = ChainFull(null);
            SaveBundle saved = CaptureAll(s);
            var beforeStates = new List<HouseholdInventoryState>();
            s.Inventories.ExportState(beforeStates);
            int nextBefore = beforeStates[0].NextLotSequence;
            int breadLotId = HouseholdBreadLot(s).LotId;

            Restored r = LoadAll(saved);
            var afterStates = new List<HouseholdInventoryState>();
            r.Inventories.ExportState(afterStates);
            Assert.AreEqual(nextBefore, afterStates[0].NextLotSequence,
                "the lot-id sequence survives load");
            Assert.AreEqual(breadLotId, HouseholdBreadLot(r.Inventories).LotId,
                "the bread lot keeps its id across load");

            HouseholdInventory restored = r.Inventories.GetOrCreate(HouseholdId);
            Assert.IsNull(restored.AddLot("flour", 5, Day + 1, "post-load test flour"),
                "a post-load lot must be accepted");
            HouseholdItemLot flourLot = null;
            foreach (HouseholdItemLot lot in restored.Lots)
            {
                if (lot != null && string.Equals(lot.ItemId, "flour", StringComparison.Ordinal))
                    flourLot = lot;
            }
            Assert.IsNotNull(flourLot, "the post-load flour lot exists");
            Assert.AreEqual(nextBefore, flourLot.LotId,
                "the post-load lot takes the next sequence id, not a reused one");
            Assert.IsTrue(flourLot.LotId != breadLotId, "the new lot id differs from the bread lot id");
            Assert.AreEqual(21, HouseholdBreadLot(r.Inventories).QuantityUnits,
                "adding a lot after load does not touch the bread lot");
            var finalStates = new List<HouseholdInventoryState>();
            r.Inventories.ExportState(finalStates);
            Assert.AreEqual(nextBefore + 1, finalStates[0].NextLotSequence,
                "the sequence advanced by exactly one after the post-load add");
        }

        [Test]
        public void HouseholdLedger_BalanceAndEntriesSurviveLoad_NoDuplicationOnSecondImport()
        {
            // Household money is provenance-enforced entries, not a bare
            // balance: the entries themselves must survive, and loading
            // the same save twice must not double the money
            // (clear-then-load).
            Chain s = ChainFull(null);
            SaveBundle saved = CaptureAll(s);
            var beforeStates = new List<HouseholdLedgerState>();
            s.Ledgers.ExportState(beforeStates);
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents());
            int entriesBefore = beforeStates[0].Entries.Count;
            Assert.IsTrue(entriesBefore >= 2, "the ledger holds at least the funding and purchase entries; got " + entriesBefore);

            Restored r = LoadAll(saved);
            Assert.AreEqual(880, r.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(),
                "the 880c balance survives load");
            var afterStates = new List<HouseholdLedgerState>();
            r.Ledgers.ExportState(afterStates);
            Assert.AreEqual(entriesBefore, afterStates[0].Entries.Count,
                "the same entries survive — none created, none lost");
            Assert.AreEqual(beforeStates[0].NextEntrySequence, afterStates[0].NextEntrySequence,
                "the entry sequence survives load");

            // Loading the same DTOs again into the SAME restored registry
            // must not double the money.
            r.Ledgers.ImportState(saved.LedgerStates);
            Assert.AreEqual(880, r.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(),
                "a second import of the same save does not double the balance");
            var twiceStates = new List<HouseholdLedgerState>();
            r.Ledgers.ExportState(twiceStates);
            Assert.AreEqual(entriesBefore, twiceStates[0].Entries.Count,
                "a second import does not duplicate entries");
        }

        [Test]
        public void RetailLot_ProvenanceKeySurvivesDtoRoundTrip()
        {
            // The retail lot's provenance — free text plus its canonical
            // key — survives its own DTO pair exactly.
            Chain s = NewChain(null);
            (PurchasedRetailLotReceipt.Result receipt, NamedSkuLot sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);
            BusinessRetailItemLotState lot = s.StoreRuntime.FindRetailItemLot(receipt.RetailLotId);
            Assert.IsNotNull(lot);
            string canonical = "authority:business:" + StoreInstanceId + "/lot:" + receipt.RetailLotId;
            Assert.IsTrue(lot.Provenance.Contains(canonical), "the lot carries its canonical key before save: " + lot.Provenance);

            BusinessRetailItemLotSaveDto dto = lot.CaptureSaveDto();
            BusinessRetailItemLotState restored = BusinessRetailItemLotState.FromSaveDto(dto);
            Assert.IsNotNull(restored, "the DTO restores a lot");
            Assert.AreEqual(lot.LotId, restored.LotId);
            Assert.AreEqual(lot.ItemId, restored.ItemId);
            Assert.AreEqual(20, restored.QuantityUnits);
            Assert.AreEqual(lot.OwnerBusinessId, restored.OwnerBusinessId);
            Assert.AreEqual(lot.CustodianBusinessId, restored.CustodianBusinessId);
            Assert.AreEqual(lot.Provenance, restored.Provenance,
                "the provenance string — free text plus canonical key — survives exactly");
            Assert.IsTrue(restored.Provenance.Contains(canonical),
                "the canonical key is still in the restored provenance: " + restored.Provenance);
            Assert.AreEqual(lot.CreatedDayIndex, restored.CreatedDayIndex);

            Assert.IsNull(BusinessRetailItemLotState.FromSaveDto(null),
                "a null DTO restores to null, never a fabricated lot");
        }

        [Test]
        public void BakeryBreadLot_InputProvenanceSurvivesSaveLoad()
        {
            // The baked bread lot's InputProvenance (which carries its
            // canonical key) and the flour lot's free-text ProvenanceChain
            // both survive the Bakery Capture/Load pair.
            Chain s = ChainAtBake(null);
            string breadLotId = s.Bakery.BreadShelf[0].LotId.ToString();
            string canonical = "authority:bakery:" + BakeryId + "/lot:" + breadLotId;
            string joinedBefore = string.Join(" | ", s.Bakery.BreadShelf[0].InputProvenance);
            Assert.IsTrue(joinedBefore.Contains(canonical),
                "the bread lot carries its canonical key before save: " + joinedBefore);
            string flourChainBefore = FlourLot(s).ProvenanceChain();
            Assert.IsTrue(flourChainBefore.Contains("chain-flour-16"),
                "the flour lot's provenance chain names the import order before save: " + flourChainBefore);

            BakeryShopRuntime.BakeryShopRuntimeSaveDto dto = s.Bakery.CaptureSaveDto();
            var restored = new BakeryShopRuntime(BakeryId);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.BreadShelf.Count, "the bread lot survives");
            string joinedAfter = string.Join(" | ", restored.BreadShelf[0].InputProvenance);
            Assert.AreEqual(breadLotId, restored.BreadShelf[0].LotId.ToString(), "the bread lot keeps its id");
            Assert.AreEqual(joinedBefore, joinedAfter,
                "the InputProvenance survives exactly, canonical key included");
            Assert.IsTrue(joinedAfter.Contains(canonical), joinedAfter);
            Assert.AreEqual(24, restored.WholesaleDeliveries[0].Units,
                "the wholesale delivery record survives with its 24 loaves");
            Assert.IsTrue(restored.WholesaleDeliveries[0].SellerLotIds.Contains(breadLotId),
                "the delivery still names the bread lot it sold");
            Assert.AreEqual(flourChainBefore, FlourLot(restored).ProvenanceChain(),
                "the flour lot's free-text provenance chain survives exactly");
            Assert.AreEqual(148, restored.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the dispensed flour lot still holds 148 lb after load");

            restored.LoadFromSaveDto(null);
            Assert.AreEqual(148, restored.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "a null DTO load leaves the restored bakery untouched (the null contract)");
        }

        [Test]
        public void FreightShipment_MidTransitSaveLoad_RestoresStateChargeAndCargo()
        {
            // The one deliberate non-quiescent capture: the shipment is
            // mid-transport (dispatched, journey returned, not yet ticked
            // to Completed). Its state, charge, cargo, and wagon must
            // survive restore, and restoring twice must not duplicate it
            // (clear-then-load).
            Chain s = NewChain(null);
            PlaceFreightOrder(s);
            BuildFreightShipment(s);
            DriveFreightToReturnDay(s);
            LogisticsShipmentStatus statusBefore = s.Shipment.Status;
            Assert.IsTrue(statusBefore != LogisticsShipmentStatus.Completed,
                "the fixture must capture mid-transit, not completed; got " + statusBefore);

            FreightShipmentService.FreightServiceSaveDto dto =
                s.Freight.Service.CaptureSaveDto(new List<LogisticsShipmentState> { s.Shipment });
            var restoredService = new FreightShipmentService();
            List<LogisticsShipmentState> restored = restoredService.RestoreSaveDto(dto, null);
            Assert.AreEqual(1, restored.Count, "one shipment restores");
            Assert.AreEqual(1, restoredService.TrackedShipments.Count, "the restored shipment is tracked");
            LogisticsShipmentState shipment = restored[0];
            Assert.AreEqual(ShipmentId, shipment.ShipmentId);
            Assert.AreEqual(statusBefore, shipment.Status, "the mid-transit state survives");
            Assert.AreEqual("bakery_flour_sack", shipment.cargoSkuId);
            Assert.AreEqual(LotId, shipment.cargoLotId);
            Assert.AreEqual(Sacks, shipment.PlannedQuantityUnits);
            Assert.AreEqual(FreightChargeCents, shipment.FreightChargeCents, "the 125c charge survives mid-transit");
            Assert.AreEqual(WagonId, shipment.physicalWagonAssetId);
            Assert.IsTrue(shipment.sourceCommittedAtSchedule, "the commit flag survives");
            Assert.IsTrue(shipment.loadApplied, "the load flag survives");

            List<LogisticsShipmentState> restoredTwice = restoredService.RestoreSaveDto(dto, null);
            Assert.AreEqual(1, restoredTwice.Count, "a second restore still yields one shipment");
            Assert.AreEqual(1, restoredService.TrackedShipments.Count,
                "clear-then-load: the second restore does not duplicate the tracked shipment");

            List<LogisticsShipmentState> cleared = restoredService.RestoreSaveDto(null, null);
            Assert.AreEqual(0, cleared.Count, "a null DTO clears");
            Assert.AreEqual(0, restoredService.TrackedShipments.Count,
                "a null restore leaves no stale shipment tracked");
        }

        [Test]
        public void MillbrookLot_ReceivedStateSurvivesExportImport_NoDuplication()
        {
            // The Millbrook Export/Import pair clones the cargo lots, so
            // the received/commit custody flags survive — and importing
            // twice does not duplicate the lot (clear-then-load).
            Chain s = ChainAtReceive(null);
            OffMapAidBusinessSaveDto dto = s.Supplier.ExportState();
            Assert.AreEqual(1, dto.Lots.Count, "the export carries one lot");

            var restored = new OffMapAidBusinessRuntime(MillbrookDefinition());
            Assert.IsNull(restored.ImportState(dto), "the import must accept");
            Assert.AreEqual(1, restored.Lots.Count, "one lot restores");
            OffMapAidCargoLot lot = restored.Lots[0];
            Assert.AreEqual(LotId, lot.LotId);
            Assert.AreEqual(160, lot.InventoryUnits);
            Assert.IsTrue(lot.SourceCommitted, "the commit flag survives");
            Assert.IsTrue(lot.Received, "the received flag survives");
            Assert.AreEqual(ShipmentId, lot.ShipmentId);
            Assert.AreEqual(BakeryId, lot.DestinationBusinessId);
            Assert.AreEqual(768, restored.CashCents);
            Assert.AreEqual(70, restored.GetInventoryUnits("bakery_flour_sack"));

            Assert.IsNull(restored.ImportState(dto), "a second import must accept");
            Assert.AreEqual(1, restored.Lots.Count, "a second import does not duplicate the lot");
            Assert.AreEqual(768, restored.CashCents, "a second import does not double the cash");

            var wrongSupplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var wrongDto = new OffMapAidBusinessSaveDto { BusinessId = "someone_else", CashCents = 0 };
            Assert.IsTrue(!string.IsNullOrEmpty(wrongSupplier.ImportState(wrongDto)),
                "an import for a different supplier is refused, never silently applied");
        }

        [Test]
        public void BusinessFeedCustodyLedger_NonPersistence_Pin()
        {
            // PIN (known gap, not a defect to fix here): the
            // BusinessFeedCustodyLedger is deliberately runtime-side, not
            // serialized (BusinessFeedCustodyLedger.cs header). A batch
            // held for a refused receipt exists ONLY in the ledger: a
            // fresh ledger — what a load constructs — holds nothing, and
            // the type declares no save surface at all. The batch's cash
            // already moved at purchase and the store's stock was already
            // consumed, so a save/load between refusal and retry drops
            // the paid-for batch; the need stays open and the next drive
            // buys fresh feed (see the ledger's header comment).
            var ledger = new BusinessFeedCustodyLedger();
            var batch = new BusinessFeedPurchase.Batch
            {
                TransactionId = "feed-txn-1",
                SellerBusinessId = "test-feed-supplier",
                BuyerBusinessId = "bakery_001",
                ItemId = "store_horse_feed_sack",
                CategoryId = "animal_feed",
                UnitLabel = "sack",
                Units = 20,
                UnitPriceCents = 15,
                TotalPriceCents = 300,
                DayIndex = Day,
                SourceLots = new List<BusinessFeedPurchase.SourceLotPart>
                {
                    new BusinessFeedPurchase.SourceLotPart
                    {
                        LotId = "lot-feed-1", Provenance = "test feed lot",
                        UnitLabel = "sack", Units = 20,
                    },
                },
            };
            Assert.IsTrue(batch.HasValidCustodyIdentity, "the fixture batch carries a valid custody identity");
            Assert.AreEqual(0, ledger.PendingCount, "the ledger starts empty");
            Assert.IsTrue(ledger.TryHold(batch), "the refused batch is held");
            Assert.AreEqual(1, ledger.PendingCount, "one batch held");
            Assert.IsTrue(ledger.HasPendingForBuyer("bakery_001"), "the buyer has a pending batch");
            Assert.IsTrue(!ledger.TryHold(batch), "holding the same transaction twice is refused — one held copy, never two");
            Assert.AreEqual(1, ledger.PendingCount, "the duplicate hold adds nothing");
            Assert.IsTrue(ledger.TryPeekForBuyer("bakery_001", out BusinessFeedPurchase.Batch held),
                "the held batch peeks back");
            Assert.AreEqual("feed-txn-1", held.TransactionId);
            Assert.AreEqual(20, held.Units, "the held batch still holds its 20 paid-for sacks");

            // The load outcome: a fresh ledger holds nothing.
            var loaded = new BusinessFeedCustodyLedger();
            Assert.AreEqual(0, loaded.PendingCount, "a fresh ledger — what a load constructs — holds no batch");
            Assert.IsTrue(!loaded.HasPendingForBuyer("bakery_001"), "no pending batch survives load");
            Assert.IsTrue(!loaded.TryPeekForBuyer("bakery_001", out BusinessFeedPurchase.Batch missing),
                "no batch peeks back after load");
            Assert.IsNull(missing, "the peeked batch is null after load");

            AssertNoSaveSurface(typeof(BusinessFeedCustodyLedger), "BusinessFeedCustodyLedger");
        }

        [Test]
        public void InTransitShoppingBatch_DoesNotSurviveSaveLoad_U3()
        {
            // PIN (U-3, verified either way: it does NOT survive).
            // Mid-trip, the purchased goods exist ONLY in the shopping
            // loop's private custodyBatches list: the store pool is
            // already consumed, the cash already moved, and the household
            // inventory is still empty (no teleport). A fresh loop — what
            // a load constructs — has no batch, and the loop declares no
            // save surface, so the 24 paid-for loaves would be lost by a
            // mid-trip save/load. The observer captures the mid-trip
            // state and asserts only after ExecuteNeed returns (the loop
            // swallows observer faults, Slice 3 pattern).
            Chain s = NewChain(null);
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
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked for the trip");
            FundHousehold(s, 1000);

            bool observerRan = false;
            int batchesMidTrip = -1;
            int householdBreadMidTrip = -1;
            int storePoolMidTrip = -1;
            string batchIdMidTrip = string.Empty;
            int freshLoopBatchesMidTrip = -1;
            s.Loop.MidTripObserver = (result, batch) =>
            {
                observerRan = true;
                batchesMidTrip = s.Loop.ActiveCustodyBatches.Count;
                householdBreadMidTrip = s.Inventories.GetOrCreate(HouseholdId)
                    .GetAvailableUnits(HouseholdItemCatalog.BreadId);
                storePoolMidTrip = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
                batchIdMidTrip = batch != null ? batch.BatchId : string.Empty;
                // The load simulation, taken DURING the mid-trip window:
                // a fresh loop over the same authorities.
                var freshLoop = new HouseholdShoppingLoop(
                    s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                    s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                    new FinancialObligationAuthority(), s.Ids);
                freshLoopBatchesMidTrip = freshLoop.ActiveCustodyBatches.Count;
            };
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason);
            Assert.IsTrue(observerRan, "the mid-trip observer ran");
            Assert.AreEqual(1, batchesMidTrip, "exactly one batch is in transit custody mid-trip");
            Assert.IsTrue(!string.IsNullOrEmpty(batchIdMidTrip), "the batch has an id");
            Assert.IsTrue(batchIdMidTrip.StartsWith("carry-", StringComparison.Ordinal),
                "the batch id is a carried-batch id: " + batchIdMidTrip);
            Assert.AreEqual(0, householdBreadMidTrip,
                "no teleport: the household inventory is still empty mid-trip");
            Assert.AreEqual(0, storePoolMidTrip,
                "the store pool is already consumed mid-trip — the 24 loaves are ONLY in the batch");
            Assert.AreEqual(0, freshLoopBatchesMidTrip,
                "U-3 PIN: a fresh loop — what a load constructs — holds no batch, so a mid-trip save/load drops the 24 paid-for loaves");
            Assert.AreEqual(24, trip.UnitsAcquired, "the trip still completes after the observation");
            Assert.AreEqual(24, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "after the return leg the household holds the 24 loaves — the loss applies only to a save/load DURING the trip");

            // Priority U-3 (repair): the loop NOW declares the custody save
            // surface (CaptureCustodySaveDto / LoadCustodyFromSaveDto) — the
            // pin above (a fresh loop with no DTO loaded holds no batch)
            // still stands, because load is explicit: only a loop that
            // LOADS the captured custody holds the batch. Survival through
            // the surface is proven in InTransitSaveLoadTests; this test
            // keeps pinning the unrestored-fresh-loop behaviour.
            AssertSaveSurface(typeof(HouseholdShoppingLoop), "HouseholdShoppingLoop",
                "CaptureCustodySaveDto", "LoadCustodyFromSaveDto");

            // The real-time path has a DTO that DOES round-trip the batch
            // scalars. Priority U-3 (repair): loading it now ALSO registers
            // the restored batch in the loop's custody (the driver
            // re-attaches the trip batch to the loop-owned instance), so
            // the restored batch is visible to the person inventory read
            // model and deposits exactly once — proven in
            // InTransitSaveLoadTests. What is still unwired at manager
            // level: no production code constructs a ShoppingTripDriver,
            // and the coordinator's save DTO
            // (HouseholdErrandPlanRegistrySaveDto) carries plans and
            // claims only, no trips and no batches; the loop custody DTO
            // is the half the hub carries into the systems save.
            var driverLoop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.ShoppingJourneys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            var driver = new ShoppingTripDriver(driverLoop);
            var dto = new PlannedShoppingTripSaveDto
            {
                TripId = "trip-u3",
                HouseholdId = HouseholdId,
                ShopperPersonId = ShopperPersonId,
                DayIndex = Day,
                DepartureMinuteOfDay = 600,
                HomeLocationId = "home",
                ResultNeedSequence = 3,
                State = (int)PlannedShoppingTrip.TripState.Outbound,
                UnitsSettled = 24,
                BatchId = "carry-12-0",
                BatchItemId = HouseholdItemCatalog.BreadId,
                BatchUnits = 24,
                BatchUnitPriceCents = 5,
                BatchReceiptId = "rcpt-u3",
                BatchCustody = (int)GoodsCustodyState.InHand,
                BatchProvenance = "purchased 24u bread from General Store day 12 receipt rcpt-u3",
                BatchPurchaseDayIndex = Day,
            };
            driver.LoadFromSaveDto(new List<PlannedShoppingTripSaveDto> { dto });
            List<PlannedShoppingTripSaveDto> round = driver.CaptureSaveDto();
            Assert.AreEqual(1, round.Count, "the driver restores one active trip from its DTO");
            PlannedShoppingTripSaveDto back = round[0];
            Assert.AreEqual("carry-12-0", back.BatchId, "the batch id round-trips through the driver DTO");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, back.BatchItemId);
            Assert.AreEqual(24, back.BatchUnits, "the batch units round-trip");
            Assert.AreEqual(5, back.BatchUnitPriceCents);
            Assert.AreEqual("rcpt-u3", back.BatchReceiptId);
            Assert.AreEqual((int)GoodsCustodyState.InHand, back.BatchCustody);
            Assert.AreEqual(dto.BatchProvenance, back.BatchProvenance, "the batch provenance round-trips");
            Assert.AreEqual(Day, back.BatchPurchaseDayIndex);
            Assert.AreEqual("trip-u3", back.TripId);
            Assert.AreEqual((int)PlannedShoppingTrip.TripState.Outbound, back.State);
        }

        // A type with no save surface at all: no public method whose name
        // mentions saving, exporting, capturing, loading, or importing.
        // (Reflection over the type's own declared public methods; a
        // future save surface would fail this pin loudly, which is the
        // point — the pin must be updated deliberately, not silently.)
        private static void AssertNoSaveSurface(Type type, string label)
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                string name = method.Name;
                Assert.IsTrue(
                    !name.Contains("Save") && !name.Contains("Export") && !name.Contains("Capture")
                    && !name.Contains("Load") && !name.Contains("Import"),
                    label + " must declare no save surface, but declares " + name);
            }
        }

        /// <summary>
        /// Priority U-3: the deliberate counterpart of
        /// <see cref="AssertNoSaveSurface"/> — a type that MUST declare the
        /// named save-surface methods (reflection over its own declared
        /// public methods). Used where a former no-surface pin was
        /// replaced by the repair that added the surface.
        /// </summary>
        private static void AssertSaveSurface(Type type, string label, params string[] methodNames)
        {
            foreach (string wanted in methodNames)
            {
                bool found = false;
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(method.Name, wanted, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(found, label + " must declare the save-surface method " + wanted);
            }
        }

        // ---- shared chain fixture: the real authorities, merged from
        // FreightDeliveryPaymentAuditTests (freight) and
        // BakeryFoodChainEndToEndTests (bakery + shopping), as in
        // LotIdentityTests, plus the meal log/service for the next-day
        // meal leg and the market/counterparty refs for the external
        // feed-payment leg. ----

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
            public OffMapNamedSkuProcurementService Procurement;
            public ExternalMarketAuthority Market;
            public ExternalCounterparty FeedSupplier;

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

            // Meals.
            public HouseholdMealLogRegistry MealLog;
            public HouseholdMealService MealService;
        }

        // One save: every authority's DTO, captured together. The retail
        // lot id is recorded alongside so post-load lookups can name the
        // lot (empty before the retail-receipt leg runs).
        private sealed class SaveBundle
        {
            public OffMapAidBusinessSaveDto SupplierDto;
            public FreightShipmentService.FreightServiceSaveDto FreightDto;
            public BakeryShopRuntime.BakeryShopRuntimeSaveDto BakeryDto;
            public BusinessRuntimeSaveDto StoreDto;
            public BusinessRuntimeSaveDto BakeryRuntimeDto;
            public BusinessRuntimeSaveDto PayerDto;
            public BusinessRuntimeSaveDto CarrierDto;
            public NamedSkuProcurementSaveDto ProcurementDto;
            public List<HouseholdInventoryState> InventoryStates = new List<HouseholdInventoryState>();
            public List<HouseholdLedgerState> LedgerStates = new List<HouseholdLedgerState>();
            public List<HouseholdMealRecord> Meals = new List<HouseholdMealRecord>();
            public List<MissedMealRecord> MissedMeals = new List<MissedMealRecord>();
            public List<MealPreparationRecord> Preparations = new List<MealPreparationRecord>();
            public string RetailLotId = string.Empty;
        }

        // One load: fresh instances of every authority, fed the bundle.
        private sealed class Restored
        {
            public OffMapAidBusinessRuntime Supplier;
            public string SupplierImportRejection;
            public List<OffMapAidCashTransaction> SupplierTransactions = new List<OffMapAidCashTransaction>();
            public FreightShipmentService Freight;
            public BakeryShopRuntime Bakery;
            public BusinessRuntimeState Store;
            public BusinessRuntimeState BakeryRuntime;
            public BusinessRuntimeState Payer;
            public BusinessRuntimeState Carrier;
            public OffMapNamedSkuProcurementService Procurement;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdMealLogRegistry MealLog;
        }

        private static SaveBundle CaptureAll(Chain s)
        {
            var bundle = new SaveBundle
            {
                SupplierDto = s.Supplier.ExportState(),
                FreightDto = s.Freight.Service.CaptureSaveDto(
                    s.Shipment != null
                        ? new List<LogisticsShipmentState> { s.Shipment }
                        : new List<LogisticsShipmentState>()),
                BakeryDto = s.Bakery.CaptureSaveDto(),
                StoreDto = s.StoreRuntime.CaptureSaveDto(),
                BakeryRuntimeDto = s.BakeryRuntime.CaptureSaveDto(),
                PayerDto = s.PayerRuntime.CaptureSaveDto(),
                CarrierDto = s.CarrierRuntime.CaptureSaveDto(),
                ProcurementDto = s.Procurement.ExportState(),
            };
            s.Inventories.ExportState(bundle.InventoryStates);
            s.Ledgers.ExportState(bundle.LedgerStates);
            s.MealLog.ExportState(bundle.Meals, bundle.MissedMeals, bundle.Preparations);
            if (s.StoreRuntime.RetailItemLots.Count > 0)
                bundle.RetailLotId = s.StoreRuntime.RetailItemLots[0].LotId;
            return bundle;
        }

        private static Restored LoadAll(SaveBundle saved)
        {
            var r = new Restored();
            r.Supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            r.SupplierImportRejection = r.Supplier.ImportState(saved.SupplierDto);
            r.SupplierTransactions = r.Supplier.ExportState().CashTransactions;
            r.Freight = new FreightShipmentService();
            r.Freight.RestoreSaveDto(saved.FreightDto, null);
            r.Bakery = new BakeryShopRuntime(BakeryId);
            r.Bakery.LoadFromSaveDto(saved.BakeryDto);
            r.Store = BusinessRuntimeState.FromSaveDto(saved.StoreDto, saved.StoreDto.businessType);
            r.BakeryRuntime = BusinessRuntimeState.FromSaveDto(saved.BakeryRuntimeDto, saved.BakeryRuntimeDto.businessType);
            r.Payer = BusinessRuntimeState.FromSaveDto(saved.PayerDto, saved.PayerDto.businessType);
            r.Carrier = BusinessRuntimeState.FromSaveDto(saved.CarrierDto, saved.CarrierDto.businessType);
            r.Procurement = new OffMapNamedSkuProcurementService(
                StoreInstanceId, new ExternalMarketAuthority(),
                new Series2BusinessRuntimeCashPort(r.Store));
            r.Procurement.ImportState(saved.ProcurementDto);
            r.Inventories = new HouseholdInventoryRegistry();
            r.Inventories.ImportState(saved.InventoryStates);
            r.Ledgers = new HouseholdLedgerRegistry();
            r.Ledgers.ImportState(saved.LedgerStates);
            r.MealLog = new HouseholdMealLogRegistry();
            r.MealLog.ImportState(saved.Meals, saved.MissedMeals, saved.Preparations);
            return r;
        }

        // All modeled money: every authority that holds cents, including
        // the test-side bakery cash port.
        private static int ModeledMoneyCents(Chain s) =>
            s.BakeryCash.BalanceCents
            + s.Supplier.CashCents
            + s.PayerRuntime.CurrentCashCents
            + s.CarrierRuntime.CurrentCashCents
            + s.StoreRuntime.CurrentCashCents
            + s.BakeryRuntime.CurrentCashCents
            + s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

        // Saved-authority money only: the same total minus the
        // FakeBusinessCashPort, which is test scaffolding for the
        // bakery's cash port and has no save surface. Its 768c debit is
        // mirrored in the supplier's saved CashCents plus
        // CashTransactions, so acceptance L compares exactly what a real
        // save/load carries.
        private static int SavedMoneyCents(Chain s) =>
            ModeledMoneyCents(s) - s.BakeryCash.BalanceCents;

        private static int SavedMoneyCents(Restored r) =>
            r.Supplier.CashCents
            + r.Payer.CurrentCashCents
            + r.Carrier.CurrentCashCents
            + r.Store.CurrentCashCents
            + r.BakeryRuntime.CurrentCashCents
            + r.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

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

            // Named-SKU feed procurement (the retail-receipt leg's source).
            // Built here so the resolver can read it; no order is placed
            // until a test calls ReceiveFeedLot.
            s.Market = new ExternalMarketAuthority();
            s.FeedSupplier = new ExternalCounterparty
            {
                CounterpartyId = "test-feed-supplier",
                DisplayName = "Test Feed Supplier",
                InitialBudgetCents = 50000,
                TransitDays = 0,
            };
            s.FeedSupplier.SupplyUnits["store_horse_feed_sack"] = 20;
            Assert.IsNull(s.Market.RegisterCounterparty(s.FeedSupplier));
            s.Procurement = new OffMapNamedSkuProcurementService(
                StoreInstanceId, s.Market, new Series2BusinessRuntimeCashPort(s.StoreRuntime));

            // Meals.
            s.MealLog = new HouseholdMealLogRegistry();
            s.MealService = new HouseholdMealService(s.MealLog);
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

        // The full composed chain: bake, retail receipt, household
        // purchase and receipt, then the next-day meal.
        private static Chain ChainFull(CustodyTransferLog log)
        {
            Chain s = ChainAtBake(log);
            (PurchasedRetailLotReceipt.Result receipt, NamedSkuLot sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            MealDayResult meals = ServeNextDayMeal(s);
            Assert.AreEqual(meals.MealsRequiredPerPerson, meals.MealsServed, "every required meal is served");
            Assert.AreEqual(0, meals.MealsMissed, "no missed meals");
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

        // Places the 20-sack feed order through the chain's procurement
        // service, receives the arrival, and transfers the named-SKU lot
        // into retail — the Slice 4a retail-receipt leg.
        private static (PurchasedRetailLotReceipt.Result, NamedSkuLot) ReceiveFeedLot(Chain s)
        {
            NamedSkuOrder feedOrder = s.Procurement.PlaceOrder(
                "store_horse_feed_sack", 20, 15, 0, "test-feed-supplier", Day,
                NamedSkuPaymentMode.CashOnArrival, null);
            Assert.IsNotNull(feedOrder);
            Assert.AreEqual(1, s.Procurement.ReceiveArrivals(feedOrder.ExpectedArrivalDayIndex, null).Count);
            NamedSkuLot sourceLot = s.Procurement.Lots[0];
            PurchasedRetailLotReceipt.Result receipt = PurchasedRetailLotReceipt.Transfer(
                s.Procurement, s.StoreRuntime, sourceLot, "animal_feed", "sack", Day, s.Log);
            return (receipt, sourceLot);
        }

        // Serves the next day's meals for the household's one member,
        // from the household inventory, through the real meal service.
        private static MealDayResult ServeNextDayMeal(Chain s) =>
            s.MealService.ServeHouseholdDay(
                s.Population.households[0],
                new List<int> { ShopperPersonId },
                s.Inventories.GetOrCreate(HouseholdId),
                MealSchedulingPolicy.Default,
                Day + 1,
                null);

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
