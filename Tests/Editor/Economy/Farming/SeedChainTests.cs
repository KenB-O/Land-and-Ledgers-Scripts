using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// W5C: the seed chain below the store — variety as data, structured
    /// upstream provenance (breeder/grower → merchant → farm), the seed
    /// merchant supply (EQU-1 import path + grower restock), and farm seed
    /// purchase/consumption with FIFO and loud shortfall refusal. Farms never
    /// plant conjured seed.
    /// </summary>
    [TestFixture]
    public sealed class SeedChainTests
    {
        private static SeedMerchant StockedMerchant(EntityIdRegistry ids, List<string> diagnostics)
        {
            var merchant = new SeedMerchant("store-1", "General Store");
            merchant.SetPrice(CropKind.Wheat, 60);
            var importLot = new ImportLot
            {
                LotId = "IMP-ORD-0001-1",
                MaterialId = SeedMerchantSupply.SeedMaterialIdFor(CropKind.Wheat),
                MaterialName = "Seed wheat (Red Fife)",
                Units = 200,
                OriginName = SeedMerchantSupply.DefaultSeedHouseOrigin,
                OrderId = "IMP-ORD-0001",
                ArrivalDayIndex = 300,
                Imported = true,
            };
            SeedLot stocked = merchant.RestockFromImport(ids, importLot, CropKind.Wheat, "red-fife", 300, diagnostics);
            Assert.NotNull(stocked);
            return merchant;
        }

        private static FarmSeedStore StoreWithSeed(EntityIdRegistry ids, List<string> diagnostics)
        {
            var merchant = StockedMerchant(ids, diagnostics);
            var store = new FarmSeedStore("farm-1");
            SeedPurchase purchase = store.PurchaseSeed(merchant, CropKind.Wheat, "red-fife", 100, 305, ids, diagnostics);
            Assert.NotNull(purchase);
            return store;
        }

        private static (CropFieldAuthority, CropChain) PreparedWheatField(float acres)
        {
            var authority = new CropFieldAuthority();
            var field = new CropFieldState("wheat-40", "farm-1", acres)
            {
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(authority.RegisterField(field));
            return (authority, new CropChain(authority));
        }

        private static CropLot GrainHarvest(int units)
        {
            return new CropLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9101),
                Crop = CropKind.Wheat,
                ProductKind = "grain",
                QuantityUnits = units,
                FieldId = "wheat-40",
                FarmId = "farm-1",
                HarvestDayIndex = 200,
                SeedSource = "general store",
            };
        }

        [Test]
        public void VarietyCatalog_HasHistoricalStarters()
        {
            CropVariety redFife = CropVarietyCatalog.Get("red-fife");
            Assert.NotNull(redFife);
            Assert.AreEqual(CropKind.Wheat, redFife.Crop);
            Assert.AreEqual("David Fife", redFife.OriginatorName);
            Assert.AreEqual(1842, redFife.FirstGrownYear);

            Assert.NotNull(CropVarietyCatalog.Get("timothy"));
            Assert.AreEqual(CropKind.Hay, CropVarietyCatalog.Get("timothy").Crop);
            Assert.IsNull(CropVarietyCatalog.Get("no-such-variety"));
            Assert.AreEqual("variety unrecorded",
                CropVarietyCatalog.DisplayNameOf(CropVarietyCatalog.UnknownVarietyId));
        }

        [Test]
        public void ProvenanceChain_RendersBreederToFarm()
        {
            var chain = new SeedProvenanceChain()
                .AppendHop(SeedProvenanceRoles.Breeder, "David Fife", string.Empty, -1)
                .AppendHop(SeedProvenanceRoles.Merchant, "Steele, Briggs Seed Co., Toronto", string.Empty, 300)
                .AppendHop(SeedProvenanceRoles.Merchant, "General Store", "store-1", 305);

            string rendered = chain.Render();
            Assert.IsTrue(rendered.Contains("breeder: David Fife"));
            Assert.IsTrue(rendered.Contains("merchant: General Store"));
            Assert.AreEqual("David Fife", chain.Originator.DisplayName);
        }

        [Test]
        public void SeedMerchant_RestockFromImport_NamesSeedHouse()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = new SeedMerchant("store-1", "General Store");

            var importLot = new ImportLot
            {
                LotId = "IMP-ORD-0001-1",
                MaterialId = "seed-wheat",
                MaterialName = "Seed wheat (Red Fife)",
                Units = 200,
                OriginName = SeedMerchantSupply.DefaultSeedHouseOrigin,
                OrderId = "IMP-ORD-0001",
                ArrivalDayIndex = 300,
                Imported = true,
            };

            SeedLot lot = merchant.RestockFromImport(ids, importLot, CropKind.Wheat, "red-fife", 300, diagnostics);
            Assert.NotNull(lot);
            Assert.AreEqual(200, lot.Units);
            Assert.AreEqual("red-fife", lot.VarietyId);
            Assert.IsTrue(lot.LotId.IsValid);
            Assert.IsTrue(lot.RenderSource().Contains("Steele, Briggs Seed Co., Toronto"));
            Assert.IsTrue(lot.RenderSource().Contains("IMP-ORD-0001"));
            Assert.AreEqual(200, merchant.SeedStockUnits(CropKind.Wheat));
        }

        [Test]
        public void SeedMerchant_RestockFromGrower_PreservesGrowerChain()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = new SeedMerchant("store-1", "General Store");

            var growerLot = new SeedLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                Crop = CropKind.Oats,
                VarietyId = "scotch-oats",
                Units = 60,
                AcquiredDayIndex = 290,
            };
            growerLot.Provenance.AppendHop(SeedProvenanceRoles.Grower, "farm seed-farm-2", "farm:seed-farm-2", 290);
            growerLot.SourceDescription = growerLot.RenderSource();

            SeedLot stocked = merchant.RestockFromGrower(ids, growerLot, 295, diagnostics);
            Assert.NotNull(stocked);
            Assert.IsTrue(stocked.RenderSource().Contains("farm seed-farm-2"),
                "The grower's provenance survives the merchant's shelf.");
            Assert.AreEqual(60, merchant.SeedStockUnits(CropKind.Oats, "scotch-oats"));
        }

        [Test]
        public void SeedMerchant_SellSeedLots_FIFO_OldestFirst()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = new SeedMerchant("store-1", "General Store");

            string problem = merchant.RestockSeed(CropKind.Wheat, 40, "seed farm: Miller's Select", 290);
            Assert.IsNull(problem);
            problem = merchant.RestockSeed(CropKind.Wheat, 40, "seed farm: Baker's Acres", 295);
            Assert.IsNull(problem);

            List<SeedLot> sold = merchant.SellSeedLots(CropKind.Wheat, null, 50, 300, ids, diagnostics);
            Assert.NotNull(sold);
            Assert.AreEqual(50, sold[0].Units + (sold.Count > 1 ? sold[1].Units : 0));
            Assert.IsTrue(sold[0].RenderSource().Contains("Miller's Select"),
                "Oldest lot (day 290) sells first.");
            Assert.AreEqual(30, merchant.SeedStockUnits(CropKind.Wheat));
            Assert.IsTrue(sold[0].RenderSource().Contains("General Store"),
                "The merchant hop is appended at sale.");
        }

        [Test]
        public void SeedMerchant_Shortfall_RefusesLoudly_NothingSold()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = StockedMerchant(ids, diagnostics);
            diagnostics.Clear();

            List<SeedLot> sold = merchant.SellSeedLots(CropKind.Wheat, "red-fife", 500, 310, ids, diagnostics);
            Assert.IsNull(sold, "Short sale refused — no partial fill, no conjured seed.");
            Assert.AreEqual(200, merchant.SeedStockUnits(CropKind.Wheat), "Failed sale touches nothing.");
            Assert.IsTrue(diagnostics.Count > 0);

            Assert.AreEqual(-1, merchant.SellSeed(CropKind.Oats, 5, 310, diagnostics),
                "T1E unit path: no stock, no sale.");
        }

        [Test]
        public void FarmSeedStore_RefusesOrphanLots_AcceptsEndowment()
        {
            var diagnostics = new List<string>();
            var store = new FarmSeedStore("farm-1");

            var orphan = new SeedLot { Crop = CropKind.Wheat, VarietyId = "red-fife", Units = 50, AcquiredDayIndex = 300 };
            Assert.IsNotNull(store.ReceiveSeedLot(orphan, diagnostics), "Orphan seed refused.");
            Assert.AreEqual(0, store.SeedUnitsOnHand(CropKind.Wheat, null));

            var endowment = new SeedLot
            {
                Crop = CropKind.Wheat,
                VarietyId = "red-fife",
                Units = 80,
                AcquiredDayIndex = 1,
                IsBootstrapEndowment = true,
            };
            Assert.IsNull(store.ReceiveSeedLot(endowment, diagnostics),
                "Explicit bootstrap endowment is the honest fallback.");
            Assert.AreEqual(80, store.SeedUnitsOnHand(CropKind.Wheat, null));
        }

        [Test]
        public void FarmSeedStore_PurchaseSeed_CarriesFullChain()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = StockedMerchant(ids, diagnostics);
            var store = new FarmSeedStore("farm-1");

            SeedPurchase purchase = store.PurchaseSeed(merchant, CropKind.Wheat, "red-fife", 100, 305, ids, diagnostics);
            Assert.NotNull(purchase);
            Assert.AreEqual(100, purchase.Units);
            Assert.AreEqual(100 * 60, purchase.TotalCents, "Price recorded for the caller to settle.");
            Assert.AreEqual(100, store.SeedUnitsOnHand(CropKind.Wheat, "red-fife"));
            Assert.AreEqual(100, merchant.SeedStockUnits(CropKind.Wheat));

            string rendered = store.Lots[0].RenderSource();
            Assert.IsTrue(rendered.Contains("Steele, Briggs Seed Co., Toronto"), "Import origin survives.");
            Assert.IsTrue(rendered.Contains("General Store"), "Merchant hop appended at sale.");
            Assert.IsTrue(rendered.Contains("Red Fife"), "Variety rides the lot.");
        }

        [Test]
        public void FarmSeedStore_HoldBackOwnSeed_NamesFarmAsGrower()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var store = new FarmSeedStore("farm-1");
            CropLot harvest = GrainHarvest(100);

            SeedLot seed = store.HoldBackOwnSeed(ids, harvest, "red-fife", 12, 210, diagnostics);
            Assert.NotNull(seed);
            Assert.AreEqual(12, seed.Units);
            Assert.AreEqual(88, harvest.QuantityUnits, "Held-back seed never leaves the farm's grain.");
            Assert.AreEqual("red-fife", seed.VarietyId);
            string rendered = seed.RenderSource();
            Assert.IsTrue(rendered.Contains("breeder: David Fife"), "The variety's originator opens the chain.");
            Assert.IsTrue(rendered.Contains("farm farm-1 (own harvest)"), "The farm is the grower hop.");
            Assert.AreEqual(12, store.SeedUnitsOnHand(CropKind.Wheat, "red-fife"));
        }

        [Test]
        public void FarmSeedStore_Consume_FIFO_AndLoudShortfall()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var store = StoreWithSeed(ids, diagnostics); // 100u acquired day 305
            // A second, newer lot of the same variety (acquired day 330).
            var merchant = StockedMerchant(ids, diagnostics);
            SeedPurchase second = store.PurchaseSeed(merchant, CropKind.Wheat, "red-fife", 40, 330, ids, diagnostics);
            Assert.NotNull(second);

            SeedConsumption consumption = store.TryConsumeForPlanting(CropKind.Wheat, null, 120, 340, diagnostics);
            Assert.NotNull(consumption);
            Assert.AreEqual(120, consumption.Units);
            Assert.IsTrue(consumption.Lines[0].ProvenanceRendered.Contains("day 305"),
                "Oldest lot (acquired day 305) consumed first.");
            Assert.AreEqual(20, store.SeedUnitsOnHand(CropKind.Wheat, null));

            diagnostics.Clear();
            SeedConsumption shortfall = store.TryConsumeForPlanting(CropKind.Wheat, null, 500, 340, diagnostics);
            Assert.IsNull(shortfall, "Shortfall refused loudly.");
            Assert.AreEqual(20, store.SeedUnitsOnHand(CropKind.Wheat, null), "Failed consumption touches nothing.");
            Assert.IsTrue(diagnostics.Count > 0);
        }

        [Test]
        public void PlantingLink_ConsumesExactNeed_RecordsProvenance()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var store = StoreWithSeed(ids, diagnostics); // 100u red-fife wheat on hand
            var (authority, chain) = PreparedWheatField(40f); // 40 acres × 2u/acre = 80u

            // Day 10 = spring (CropChainTests convention).
            string problem = store.PlantFieldFromStore(chain, "wheat-40", CropKind.Wheat, "red-fife",
                EntityId.Invalid, 10, diagnostics);
            Assert.IsNull(problem, problem);

            CropFieldState field = authority.GetField("wheat-40");
            Assert.AreEqual(CropGrowthState.Planted, field.GrowthState);
            Assert.AreEqual(20, store.SeedUnitsOnHand(CropKind.Wheat, null), "Exactly the 80u need was consumed.");
            Assert.IsTrue(field.SeedSource.Contains("Red Fife"), "Planting records the variety.");
            Assert.IsTrue(field.SeedSource.Contains("Steele, Briggs Seed Co., Toronto"), "Planting records the origin.");
            Assert.AreEqual("red-fife", field.SeedVarietyId);
        }

        [Test]
        public void PlantingLink_Shortfall_NoPlantingNoConsumption()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var merchant = StockedMerchant(ids, diagnostics);
            var store = new FarmSeedStore("farm-1");
            Assert.NotNull(store.PurchaseSeed(merchant, CropKind.Wheat, "red-fife", 10, 305, ids, diagnostics));
            var (authority, chain) = PreparedWheatField(40f); // needs 80u, holds 10u

            string problem = store.PlantFieldFromStore(chain, "wheat-40", CropKind.Wheat, "red-fife",
                EntityId.Invalid, 10, diagnostics);
            Assert.IsNotNull(problem, "Shortfall refuses the planting loudly.");
            Assert.AreEqual(CropGrowthState.Prepared, authority.GetField("wheat-40").GrowthState,
                "The field stays unplanted.");
            Assert.AreEqual(10, store.SeedUnitsOnHand(CropKind.Wheat, null), "The store is untouched.");
        }

        [Test]
        public void PlantingLink_PlantFieldRefusal_SeedNotConsumed()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var store = StoreWithSeed(ids, diagnostics);
            var (authority, chain) = PreparedWheatField(40f);

            // Day 100 = summer: PlantField itself refuses (season gate stays its authority).
            string problem = store.PlantFieldFromStore(chain, "wheat-40", CropKind.Wheat, "red-fife",
                EntityId.Invalid, 100, diagnostics);
            Assert.IsNotNull(problem);
            Assert.AreEqual(CropGrowthState.Prepared, authority.GetField("wheat-40").GrowthState);
            Assert.AreEqual(100, store.SeedUnitsOnHand(CropKind.Wheat, null),
                "Peeked seed is not consumed when planting refuses.");
        }

        [Test]
        public void FarmSeedStore_SaveLoad_RoundTrip()
        {
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();
            var store = StoreWithSeed(ids, diagnostics);

            FarmSeedStoreSaveDto dto = store.CaptureSaveDto();
            var restored = new FarmSeedStore();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual("farm-1", restored.FarmId);
            Assert.AreEqual(100, restored.SeedUnitsOnHand(CropKind.Wheat, "red-fife"));
            Assert.IsTrue(restored.Lots[0].RenderSource().Contains("Steele, Briggs Seed Co., Toronto"),
                "Provenance survives save/load.");
            Assert.AreEqual("red-fife", restored.Lots[0].VarietyId);
        }

        [Test]
        public void SeedMerchantSupply_RegistersImportables()
        {
            var diagnostics = new List<string>();
            SeedMerchantSupply.EnsureSeedImportables(diagnostics);

            ImportMaterial wheat = ImportCatalog.Get(SeedMerchantSupply.SeedMaterialIdFor(CropKind.Wheat));
            Assert.NotNull(wheat);
            Assert.IsTrue(wheat.OriginName.Contains("Steele, Briggs Seed Co., Toronto"),
                "Named off-map origin per EQU-1 — never a vague 'imported'.");
            Assert.IsTrue(wheat.TransitDays >= 1);
            Assert.NotNull(ImportCatalog.Get(SeedMerchantSupply.SeedMaterialIdFor(CropKind.Hay)));
        }
    }
}
