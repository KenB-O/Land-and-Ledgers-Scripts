// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F4 — lot identity and provenance key tests (P9 plan
// section 2.4, section 4 Slice F4, directive Priority D). Pins: the
// canonical provenance-key shape and append-once rule; Resolve for each
// of the five lot-id spaces plus freight through the real authorities
// (the same end-to-end chain Slice 4a used: Millbrook order, freight
// commit and receive, Bakery dispense and bake, retail receipt,
// household purchase and receipt); Resolve returns null for unknown keys
// and for keys in a space whose authority was not supplied; ResolveAll
// returns every identity for an item across authorities with no
// duplicates; the double-count pin on the F2 BusinessStorage leg
// (acceptance K); Resolve/ResolveAll change nothing (acceptance Q); and
// the chain quantities and settlements match the existing figures
// (acceptance R: 16-sack haul, 125c settlement, 12 lb to 24 loaves).
// Acceptance E (transfers preserve lot identity and provenance) is
// pinned by the chain test resolving every lot created along the chain.

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
    public sealed class LotIdentityTests
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
        public void CanonicalKey_FormatMatchesDirectiveExamples()
        {
            Assert.AreEqual("authority:household-inventory/lot:7",
                LotIdentity.CanonicalProvenanceKey("household-inventory", "7"));
            Assert.AreEqual("authority:business:general-store/lot:feed-42",
                LotIdentity.CanonicalProvenanceKey("business:general-store", "feed-42"));
            Assert.AreEqual("authority:bakery:bakery_001/lot:Lot:9",
                LotIdentity.CanonicalProvenanceKey("bakery:bakery_001", "Lot:9"));
        }

        [Test]
        public void AppendProvenanceKey_PreservesFreeTextAndAppendsExactlyOnce()
        {
            const string freeText = "purchased 24u bread from General Store";
            string once = LotIdentity.AppendProvenanceKey(
                freeText, "household-inventory", "0");
            Assert.IsTrue(once.StartsWith(freeText, StringComparison.Ordinal),
                "free text is kept verbatim at the front: " + once);
            Assert.IsTrue(once.Contains("authority:household-inventory/lot:0"),
                "the canonical key is appended: " + once);
            string twice = LotIdentity.AppendProvenanceKey(
                once, "household-inventory", "0");
            Assert.AreEqual(once, twice, "a second append of the same lot's key changes nothing");
            Assert.AreEqual("authority:household-inventory/lot:3",
                LotIdentity.AppendProvenanceKey(string.Empty, "household-inventory", "3"),
                "an empty field becomes just the key");
            Assert.AreEqual("authority:household-inventory/lot:3",
                LotIdentity.AppendProvenanceKey(null, "household-inventory", "3"));
            // A different lot's key appends alongside, never overwriting.
            string other = LotIdentity.AppendProvenanceKey(once, "household-inventory", "1");
            Assert.IsTrue(other.Contains("authority:household-inventory/lot:0"), other);
            Assert.IsTrue(other.Contains("authority:household-inventory/lot:1"), other);
        }

        [Test]
        public void Resolve_HouseholdLot_ReportsItemUnitsCustodianOwnerAndProvenanceKey()
        {
            var registries = new HouseholdInventoryRegistry();
            HouseholdInventory inventory = registries.GetOrCreate(HouseholdId);
            Assert.IsNull(inventory.AddLot("bread", 24, Day, "purchased 24u bread from General Store"),
                "the lot must be accepted");
            Assert.IsNull(inventory.AddLot("flour", 10, Day, "purchased 10u flour from General Store"),
                "a second lot must be accepted");

            var resolver = new LotIdentityResolver(households: registries);
            LotIdentity bread = resolver.Resolve(
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "0"));
            Assert.IsNotNull(bread, "the first household lot resolves");
            Assert.AreEqual("bread", bread.ItemId);
            Assert.AreEqual(24, bread.Units);
            Assert.AreEqual("household:7", bread.Custodian);
            Assert.AreEqual("household:7", bread.Owner,
                "OwnerPersonId -1 means the household itself owns the lot (Slice 4a: receipt books to the household, never the shopper)");
            Assert.AreEqual("authority:household-inventory/lot:0", bread.ProvenanceKey,
                "the AddLot append rule puts this lot's canonical key in its SourceLabel");
            Assert.AreEqual(string.Empty, bread.ProvenanceNote);
            Assert.AreEqual("household-inventory", bread.SourceAuthority);
            Assert.IsTrue(inventory.Lots[0].SourceLabel.Contains("purchased 24u bread from General Store"),
                "free text is preserved in the SourceLabel: " + inventory.Lots[0].SourceLabel);

            LotIdentity flour = resolver.Resolve(
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "1"));
            Assert.IsNotNull(flour);
            Assert.AreEqual("flour", flour.ItemId);
            Assert.AreEqual("authority:household-inventory/lot:1", flour.ProvenanceKey,
                "each lot carries its own key, not its neighbour's");
            Assert.IsTrue(bread.Key != flour.Key, "the two lots are different keys");
        }

        [Test]
        public void Resolve_HouseholdSplitChild_CarriesItsOwnKeyParentKeepsItsOwn()
        {
            var registries = new HouseholdInventoryRegistry();
            HouseholdInventory inventory = registries.GetOrCreate(HouseholdId);
            Assert.IsNull(inventory.AddLot("flour", 10, 0, "test flour"));
            int childId = inventory.SplitLot(0, 4, 1);
            Assert.AreEqual(1, childId, "the split child is lot 1");

            var resolver = new LotIdentityResolver(households: registries);
            LotIdentity parent = resolver.Resolve(
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "0"));
            LotIdentity child = resolver.Resolve(
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, "1"));
            Assert.IsNotNull(parent);
            Assert.IsNotNull(child);
            Assert.AreEqual(6, parent.Units, "the parent keeps 10 - 4");
            Assert.AreEqual(4, child.Units);
            Assert.AreEqual("authority:household-inventory/lot:0", parent.ProvenanceKey);
            Assert.AreEqual("authority:household-inventory/lot:1", child.ProvenanceKey,
                "the child carries its own canonical key, not only its parent's");
            Assert.IsTrue(inventory.Lots[1].SourceLabel.Contains("authority:household-inventory/lot:0"),
                "the child's SourceLabel still carries its parent's key as lineage: " + inventory.Lots[1].SourceLabel);
        }

        [Test]
        public void Resolve_RetailLot_ReportsItemUnitsCustodianOwnerAndProvenanceKey()
        {
            Chain s = NewChain(null);
            PurchasedRetailLotReceipt.Result receipt;
            NamedSkuLot sourceLot;
            (receipt, sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);

            var resolver = new LotIdentityResolver(business: s.StoreRuntime);
            LotIdentity retail = resolver.Resolve(new LotKey(
                CustodyTransferLog.BusinessAuthority(StoreInstanceId), receipt.RetailLotId));
            Assert.IsNotNull(retail, "the retail lot created by the receipt resolves");
            Assert.AreEqual("store_horse_feed_sack", retail.ItemId);
            Assert.AreEqual(20, retail.Units);
            Assert.AreEqual("business:" + StoreInstanceId, retail.Custodian);
            Assert.AreEqual("business:" + StoreInstanceId, retail.Owner);
            Assert.AreEqual(
                "authority:business:" + StoreInstanceId + "/lot:" + receipt.RetailLotId,
                retail.ProvenanceKey,
                "the receipt append rule puts the retail lot's canonical key in its provenance");
            Assert.AreEqual(string.Empty, retail.ProvenanceNote);
        }

        [Test]
        public void Resolve_NamedSkuLot_ReportsRemainingUnitsAndNamesMissingProvenanceKey()
        {
            Chain s = NewChain(null);
            (PurchasedRetailLotReceipt.Result receipt, NamedSkuLot sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);

            // Resolve the source lot BEFORE the transfer drained it, via a
            // second, identical procurement: the chain's source lot now has
            // RemainingUnits 0, which the resolver reports honestly.
            var resolver = new LotIdentityResolver(namedSku: s.Procurement);
            LotIdentity source = resolver.Resolve(new LotKey(
                CustodyTransferLog.NamedSkuAuthority(StoreInstanceId), sourceLot.LotId));
            Assert.IsNotNull(source, "the named-SKU source lot resolves even after transfer");
            Assert.AreEqual("store_horse_feed_sack", source.ItemId);
            Assert.AreEqual(0, source.Units,
                "RemainingUnits is 0 once the receipt transferred the lot's units — the resolver reports the live remaining quantity");
            Assert.AreEqual("business:" + StoreInstanceId, source.Custodian);
            Assert.AreEqual(string.Empty, source.ProvenanceKey,
                "no canonical key was appended at named-SKU creation (the directive's append list covers only the four named sites)");
            Assert.IsTrue(source.ProvenanceNote.Contains("no canonical provenance key"),
                "the gap is named in the record: " + source.ProvenanceNote);
        }

        [Test]
        public void Resolve_MillbrookLot_ReportsSacksAndNamesMissingProvenanceField()
        {
            Chain s = NewChain(null);
            PlaceFreightOrder(s);

            var resolver = new LotIdentityResolver(millbrook: s.Supplier);
            LotIdentity millbrook = resolver.Resolve(new LotKey(
                CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId));
            Assert.IsNotNull(millbrook, "the Millbrook cargo lot resolves");
            Assert.AreEqual("bakery_flour_sack", millbrook.ItemId);
            Assert.AreEqual(160, millbrook.Units, "InventoryUnits: 16 sacks x 10 lb");
            Assert.AreEqual("supplier:millbrook_grain_co", millbrook.Custodian);
            Assert.AreEqual("supplier:millbrook_grain_co", millbrook.Owner);
            Assert.AreEqual(string.Empty, millbrook.ProvenanceKey,
                "OffMapAidCargoLot carries no provenance field, so no canonical key can be appended or reported");
            Assert.IsTrue(millbrook.ProvenanceNote.Contains("no provenance field"),
                "the gap is named in the record: " + millbrook.ProvenanceNote);
        }

        [Test]
        public void Resolve_FreightShipment_ReportsCargoSkuPlannedUnitsAndWagon()
        {
            Chain s = ChainAtReceive(null);

            var resolver = new LotIdentityResolver(freight: s.Freight.Service);
            LotIdentity freight = resolver.Resolve(new LotKey(
                CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId));
            Assert.IsNotNull(freight, "the freight shipment resolves by its shipment id");
            Assert.AreEqual("bakery_flour_sack", freight.ItemId);
            Assert.AreEqual(Sacks, freight.Units, "PlannedQuantityUnits: the contracted 16 sacks");
            Assert.AreEqual("wagon:" + WagonId, freight.Custodian);
            Assert.AreEqual(string.Empty, freight.Owner,
                "a shipment carries no owner field — Owner stays empty, never invented");
            Assert.AreEqual(string.Empty, freight.ProvenanceKey);
            Assert.IsTrue(freight.ProvenanceNote.Contains(LotId),
                "the note names the cargo lot the shipment carries: " + freight.ProvenanceNote);
        }

        [Test]
        public void Resolve_BakeryFlourLot_ReportsPoundsAndNamesMissingProvenanceKey()
        {
            Chain s = ChainAtReceive(null);

            var resolver = new LotIdentityResolver(bakery: s.Bakery);
            string flourLotId = FlourLot(s).LotId.ToString();
            LotIdentity flour = resolver.Resolve(new LotKey(
                CustodyTransferLog.BakeryAuthority(BakeryId), flourLotId));
            Assert.IsNotNull(flour, "the bakery flour lot resolves");
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, flour.ItemId);
            Assert.AreEqual(160, flour.Units, "the delivered lot holds the lot's 160 lb");
            Assert.AreEqual("bakery:" + BakeryId, flour.Custodian);
            Assert.AreEqual(string.Empty, flour.Owner,
                "BakeryFlourLot carries no owner field — Owner stays empty, never invented");
            Assert.AreEqual(string.Empty, flour.ProvenanceKey,
                "flour lots carry free-text provenance (ProvenanceChain), not a stored canonical key");
            Assert.IsTrue(flour.ProvenanceNote.Contains("ProvenanceChain"),
                "the gap is named in the record: " + flour.ProvenanceNote);
        }

        [Test]
        public void Resolve_UnknownKeyAndMissingAuthority_ReturnNullWithGapNamed()
        {
            var registries = new HouseholdInventoryRegistry();
            registries.GetOrCreate(HouseholdId).AddLot("bread", 6, Day, "test bread");

            var empty = new LotIdentityResolver();
            Assert.IsNull(empty.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, "0")),
                "with no authority supplied, even an existing household lot is unresolvable");
            Assert.IsTrue(string.Join(" | ", empty.Diagnostics).Contains("no household inventory authority"),
                "the missing authority is named: " + string.Join(" | ", empty.Diagnostics));

            var householdOnly = new LotIdentityResolver(households: registries);
            Assert.IsNotNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, "0")),
                "the supplied household space resolves");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.BakeryAuthority(BakeryId), "Lot:9")),
                "a bakery key is unresolvable without the Bakery authority");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.BusinessAuthority(StoreInstanceId), "retail-feed-x")),
                "a retail key is unresolvable without the business authority");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId)),
                "a Millbrook key is unresolvable without the Millbrook authority");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.NamedSkuAuthority(StoreInstanceId), "lot-x")),
                "a named-SKU key is unresolvable without the procurement authority");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId)),
                "a freight key is unresolvable without the freight authority");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, "99")),
                "an unknown household lot id resolves to null");
            Assert.IsNull(householdOnly.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, "carry-12-0")),
                "a carried batch id has no registry record and resolves to null, never an exception");
            Assert.IsNull(householdOnly.Resolve(new LotKey("bogus:thing", "x")),
                "an unknown authority space resolves to null");
            Assert.IsNull(householdOnly.Resolve(LotKey.Empty),
                "an empty key matches nothing");
            Assert.IsTrue(householdOnly.Diagnostics.Count >= 9,
                "every null names its gap in diagnostics: " + string.Join(" | ", householdOnly.Diagnostics));
        }

        [Test]
        public void DoubleCountPin_BusinessStorageReportsPoolAndIdentifiedLotsSeparately()
        {
            // Acceptance K: a business item cannot be duplicated by summing
            // the category-pool and identified-lot representations. The
            // store holds 30 pool-only units plus a 20-unit identified
            // retail lot for the same item; the F2 BusinessStorage leg
            // must report Occupied 50 (the pool, once) and IdentifiedLot
            // 20 (the lots, once) — never an Occupied of 70.
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity(StoreInstanceId);
            state.AddCashCents(5000);
            state.EnsureCategoryStock("animal_feed", 0, 0);
            Assert.AreEqual(30, state.AddCategoryStockUnits("animal_feed", 30),
                "30 pool-only units enter the category");
            Assert.IsNull(state.TryAddRetailItemLot(
                "retail-feed-pin", "store_horse_feed_sack", "animal_feed", "sack", 20,
                StoreInstanceId, StoreInstanceId, "test provenance", Day),
                "the 20-unit identified lot must be accepted");

            var evaluator = new PhysicalCapacityEvaluator(
                null, id => state, null, null);
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                new PhysicalCapacityQuery(
                    PhysicalContainerKind.BusinessStorage, StoreInstanceId,
                    new PhysicalLoad(new (string, int)[] { ("store_horse_feed_sack", 1) }), Day),
                new PhysicalCapacityEvaluationContext { BusinessCategoryId = "animal_feed" });

            Assert.AreEqual(50, verdict.StorageAxes.OccupiedUnits,
                "Occupied is the category pool (30 pool-only + 20 from the lot's receipt), counted exactly once");
            Assert.AreEqual(20, verdict.StorageAxes.IdentifiedLotUnits,
                "IdentifiedLotUnits is the identified-lot view for the load's item, reported separately");
            Assert.IsTrue(verdict.StorageAxes.OccupiedUnits != 70,
                "the evaluator never sums the pool and the identified lots into one occupied figure");
            Assert.IsTrue(verdict.StorageAxes.OccupiedUnits + verdict.StorageAxes.IdentifiedLotUnits == 70,
                "the two views may be added by a caller only if the caller knows they overlap — the evaluator itself provides no combined figure");
        }

        [Test]
        public void Resolve_BakeryBreadLot_ReportsLoavesAndProvenanceKey()
        {
            Chain s = ChainAtBake(null);
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count,
                "the baked lot stays on the shelf after the wholesale transfer");
            string breadLotId = s.Bakery.BreadShelf[0].LotId.ToString();

            var resolver = new LotIdentityResolver(bakery: s.Bakery);
            LotIdentity bread = resolver.Resolve(new LotKey(
                CustodyTransferLog.BakeryAuthority(BakeryId), breadLotId));
            Assert.IsNotNull(bread, "the bakery bread lot resolves");
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, bread.ItemId);
            Assert.AreEqual(0, bread.Units,
                "the wholesale transfer sold all 24 loaves through TakeUnits, so the lot stays on the shelf at 0 — the identity survives the sale");
            Assert.AreEqual("bakery:" + BakeryId, bread.Custodian);
            Assert.AreEqual(string.Empty, bread.Owner,
                "BakeryBreadLot carries no owner field — Owner stays empty, never invented");
            Assert.AreEqual(
                "authority:bakery:" + BakeryId + "/lot:" + breadLotId,
                bread.ProvenanceKey,
                "the bake append rule puts the bread lot's canonical key in its InputProvenance");
            Assert.AreEqual(string.Empty, bread.ProvenanceNote);
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "one wholesale delivery went out");
            Assert.AreEqual(24, s.Bakery.WholesaleDeliveries[0].Units,
                "the 24 loaves went through this lot (case R: 12 lb to 24 loaves)");
            Assert.IsTrue(s.Bakery.WholesaleDeliveries[0].SellerLotIds.Contains(breadLotId),
                "the delivery names the bread lot it sold");
        }

        [Test]
        public void Chain_ResolveEveryLot_PreservesIdentityAndProvenance()
        {
            // Acceptance E (transfers preserve lot identity and
            // provenance) and R (no change to existing economic
            // transactions: 16-sack haul, 125c settlement, 12 lb to
            // 24 loaves) — the Slice 4a chain, resolved lot by lot.
            var log = new CustodyTransferLog();
            Chain s = NewChain(log);
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);

            // Leg 1 — Millbrook order: 16 sacks, 160 lb, 768c (case R).
            PlaceFreightOrder(s);
            Assert.AreEqual(20000 - Sacks * 48, s.BakeryCash.BalanceCents, "order debits the buyer once");
            Assert.AreEqual(Sacks * 48, s.Supplier.CashCents, "order credits the supplier once");
            var resolver = new LotIdentityResolver(
                s.Inventories, s.StoreRuntime, s.Bakery, s.Supplier, s.Freight.Service, s.Procurement);
            var millbrookKey = new LotKey(CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId);
            LotIdentity millbrook = resolver.Resolve(millbrookKey);
            Assert.IsNotNull(millbrook);
            Assert.AreEqual("bakery_flour_sack", millbrook.ItemId);
            Assert.AreEqual(160, millbrook.Units);
            Assert.AreEqual(string.Empty, millbrook.ProvenanceKey, "Millbrook lots carry no provenance field");

            // Legs 2-3 — freight: commit day 11, receive day 12, 125c settles once (case R).
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

            string flourLotId = FlourLot(s).LotId.ToString();
            var bakeryFlourKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), flourLotId);
            LotIdentity flour = resolver.Resolve(bakeryFlourKey);
            Assert.IsNotNull(flour);
            Assert.AreEqual(BakeryBreadCatalog.FlourItemId, flour.ItemId);
            Assert.AreEqual(160, flour.Units);
            Assert.AreEqual(string.Empty, flour.ProvenanceKey, "flour lots carry no stored canonical key");
            LotIdentity freight = resolver.Resolve(new LotKey(
                CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId));
            Assert.IsNotNull(freight);
            Assert.AreEqual("bakery_flour_sack", freight.ItemId);
            Assert.AreEqual(Sacks, freight.Units);
            Assert.AreEqual("wagon:" + WagonId, freight.Custodian);

            // Legs 4-5 — bake: 12 lb flour + 1 notion into the batch, 24 loaves out (case R).
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "flour 160 -> 148 lb: the partial leg preserves the total (12 dispensed + 148 remaining, case M)");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 + 48c wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 48c wholesale");
            Assert.AreEqual(148, resolver.Resolve(bakeryFlourKey).Units,
                "a fresh read after the bake sees the dispensed flour lot at 148 lb — the resolver holds no stale copy");
            string breadLotId = s.Bakery.BreadShelf.Count > 0
                ? s.Bakery.BreadShelf[0].LotId.ToString()
                : s.Bakery.WholesaleDeliveries[0].SellerLotIds[0];
            var bakeryBreadKey = new LotKey(CustodyTransferLog.BakeryAuthority(BakeryId), breadLotId);
            LotIdentity bread = resolver.Resolve(bakeryBreadKey);
            Assert.IsNotNull(bread);
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, bread.ItemId);
            Assert.AreEqual("authority:bakery:" + BakeryId + "/lot:" + breadLotId, bread.ProvenanceKey,
                "the baked bread lot carries its canonical key");

            // Leg 6 — retail receipt: 20 feed sacks from the named-SKU lot into retail.
            (PurchasedRetailLotReceipt.Result receipt, NamedSkuLot sourceLot) = ReceiveFeedLot(s);
            Assert.IsTrue(receipt.Received, receipt.Refusal);
            Assert.AreEqual(4652, s.StoreRuntime.CurrentCashCents, "store 4,952 - 300c feed on arrival");
            var namedSkuKey = new LotKey(CustodyTransferLog.NamedSkuAuthority(StoreInstanceId), sourceLot.LotId);
            LotIdentity namedSku = resolver.Resolve(namedSkuKey);
            Assert.IsNotNull(namedSku);
            Assert.AreEqual("store_horse_feed_sack", namedSku.ItemId);
            Assert.AreEqual(0, namedSku.Units, "the source lot's remaining units are 0 after transfer");
            var retailKey = new LotKey(CustodyTransferLog.BusinessAuthority(StoreInstanceId), receipt.RetailLotId);
            LotIdentity retail = resolver.Resolve(retailKey);
            Assert.IsNotNull(retail);
            Assert.AreEqual("store_horse_feed_sack", retail.ItemId);
            Assert.AreEqual(20, retail.Units);
            Assert.AreEqual("authority:business:" + StoreInstanceId + "/lot:" + receipt.RetailLotId,
                retail.ProvenanceKey, "the retail lot carries its canonical key");

            // Legs 7-8 — household purchase and receipt: the household needs
            // 30 loaves, the store holds 24 — a partial fill (case M).
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 30, Day);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, trip.UnitsAcquired, "the partial fill sells the 24 loaves on hand");
            Assert.AreEqual(120, trip.BuyerOutflowCents, "24 loaves x 5c");
            Assert.AreEqual(880, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 120c");
            Assert.AreEqual(4772, s.StoreRuntime.CurrentCashCents, "store 4,652 + 120c retail");
            Assert.AreEqual(24, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "24 bread units in household inventory: bought = received, the partial total is preserved");
            HouseholdItemLot householdLot = null;
            foreach (HouseholdItemLot lot in s.Inventories.GetOrCreate(HouseholdId).Lots)
            {
                if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    householdLot = lot;
            }
            Assert.IsNotNull(householdLot, "a bread lot exists in the household inventory");
            Assert.AreEqual(-1, householdLot.OwnerPersonId,
                "the receipt books the lot to the household (OwnerPersonId -1), never to the shopper — ownership untouched by this slice");
            var householdKey = new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, householdLot.LotId.ToString());
            LotIdentity household = resolver.Resolve(householdKey);
            Assert.IsNotNull(household, "the household receipt lot resolves — AddLot now returns its id through the registry, not through the call site");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, household.ItemId);
            Assert.AreEqual(24, household.Units);
            Assert.AreEqual("household:" + HouseholdId, household.Custodian);
            Assert.AreEqual("household:" + HouseholdId, household.Owner);
            Assert.AreEqual(
                "authority:household-inventory/lot:" + householdLot.LotId,
                household.ProvenanceKey, "the household lot carries its canonical key");

            // ResolveAll across the chain: every identity for an item, no duplicates.
            IReadOnlyList<LotIdentity> flourSacks = resolver.ResolveAll("bakery_flour_sack");
            Assert.AreEqual(2, flourSacks.Count,
                "bakery_flour_sack is the Millbrook lot and the freight shipment carrying it — the bakery flour lot is 'bakery-flour', a different item id");
            var flourSackKeys = new HashSet<LotKey>();
            foreach (LotIdentity identity in flourSacks)
                Assert.IsTrue(flourSackKeys.Add(identity.Key), "duplicate key " + identity.Key + " in ResolveAll");
            Assert.IsTrue(flourSackKeys.Contains(millbrookKey), "ResolveAll includes the Millbrook lot");
            Assert.IsTrue(flourSackKeys.Contains(new LotKey(CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId)),
                "ResolveAll includes the freight shipment");
            IReadOnlyList<LotIdentity> breads = resolver.ResolveAll(HouseholdItemCatalog.BreadId);
            Assert.AreEqual(1, breads.Count, "household 'bread' is one lot; the bakery's product id is 'bake.bread-loaf'");
            Assert.AreEqual(householdKey, breads[0].Key);
            IReadOnlyList<LotIdentity> feeds = resolver.ResolveAll("store_horse_feed_sack");
            Assert.AreEqual(2, feeds.Count, "the named-SKU source lot and the retail lot");
            var feedKeys = new HashSet<LotKey>();
            foreach (LotIdentity identity in feeds)
                Assert.IsTrue(feedKeys.Add(identity.Key), "duplicate key " + identity.Key + " in ResolveAll");
            Assert.IsTrue(feedKeys.Contains(namedSkuKey), "ResolveAll includes the named-SKU lot");
            Assert.IsTrue(feedKeys.Contains(retailKey), "ResolveAll includes the retail lot");
            Assert.AreEqual(0, resolver.ResolveAll("no-such-item").Count, "an unknown item resolves to nothing");
            Assert.AreEqual(0, resolver.ResolveAll(null).Count, "a null item id resolves to nothing, never an error");
        }

        [Test]
        public void ResolveAndResolveAll_ChangeNothing()
        {
            // Acceptance Q: an inspection query cannot mutate inventory
            // or create stock. Resolver calls before and after a sequence
            // of transfers leave every quantity and settlement identical.
            Chain s = ChainAtReceive(null);
            var resolver = new LotIdentityResolver(
                s.Inventories, s.StoreRuntime, s.Bakery, s.Supplier, s.Freight.Service, s.Procurement);

            int supplierInventoryBefore = s.Supplier.GetInventoryUnits("bakery_flour_sack");
            int supplierCashBefore = s.Supplier.CashCents;
            int flourBefore = FlourLot(s).Units;
            int payerBefore = s.PayerRuntime.CurrentCashCents;
            int carrierBefore = s.CarrierRuntime.CurrentCashCents;
            int storeCashBefore = s.StoreRuntime.CurrentCashCents;

            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(resolver.Resolve(new LotKey(
                    CustodyTransferLog.MillbrookAuthority("millbrook_grain_co"), LotId)));
                Assert.IsNotNull(resolver.Resolve(new LotKey(
                    CustodyTransferLog.BakeryAuthority(BakeryId), FlourLot(s).LotId.ToString())));
                Assert.IsNotNull(resolver.Resolve(new LotKey(
                    CustodyTransferLog.FreightAuthority(ShipmentId), ShipmentId)));
                Assert.IsNull(resolver.Resolve(new LotKey(
                    CustodyTransferLog.HouseholdInventoryAuthority, "99")));
                Assert.AreEqual(2, resolver.ResolveAll("bakery_flour_sack").Count);
            }

            Assert.AreEqual(supplierInventoryBefore, s.Supplier.GetInventoryUnits("bakery_flour_sack"),
                "resolving changes no supplier inventory");
            Assert.AreEqual(supplierCashBefore, s.Supplier.CashCents, "resolving moves no supplier cash");
            Assert.AreEqual(flourBefore, FlourLot(s).Units, "resolving changes no flour stock");
            Assert.AreEqual(payerBefore, s.PayerRuntime.CurrentCashCents, "resolving moves no payer cash");
            Assert.AreEqual(carrierBefore, s.CarrierRuntime.CurrentCashCents, "resolving moves no carrier cash");
            Assert.AreEqual(storeCashBefore, s.StoreRuntime.CurrentCashCents, "resolving moves no store cash");
            Assert.AreEqual(160, FlourLot(s).Units, "the flour lot still holds its 160 lb after inspection");

            // Transfers still run identically after the inspection calls.
            InstallOven(s);
            SeedNotionsAndFuel(s);
            BookAccount(s, Day);
            BakeryWholesaleDailyResult baked = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, Day), s.Diag);
            Assert.AreEqual(1, baked.BatchesBaked, "one batch bakes after inspection; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(24, baked.UnitsStocked, "12 lb still bakes 24 loaves after inspection");
            Assert.AreEqual(148, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 + 48c wholesale after inspection");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 48c wholesale after inspection");
        }

        // ---- shared chain fixture: the real authorities, merged from
        // FreightDeliveryPaymentAuditTests (freight) and
        // BakeryFoodChainEndToEndTests (bakery + shopping), as in
        // CustodyTransferLogTests. ----

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
                AnimalOwnerKind.Business, CarrierId, Day, "Lot identity fixture");
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
            var market = new ExternalMarketAuthority();
            var feedSupplier = new ExternalCounterparty
            {
                CounterpartyId = "test-feed-supplier",
                DisplayName = "Test Feed Supplier",
                InitialBudgetCents = 50000,
                TransitDays = 0,
            };
            feedSupplier.SupplyUnits["store_horse_feed_sack"] = 20;
            Assert.IsNull(market.RegisterCounterparty(feedSupplier));
            s.Procurement = new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, new Series2BusinessRuntimeCashPort(s.StoreRuntime));
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
