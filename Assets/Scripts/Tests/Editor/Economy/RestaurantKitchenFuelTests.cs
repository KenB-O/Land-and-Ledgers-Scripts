using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1E: kitchen stove fuel (Canon §8.1B: food service demands fuel) —
    /// provenance lots, bootstrap, the cold-stove gate, fuel burn per batch
    /// cooking, and fuel provenance cooked into the meal lot.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantKitchenFuelTests
    {
        private static Func<string, WorkstationComponentView?> KitchenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "stove-1", "stove-range" },
                { "cookware-1", "cookware-set" },
                { "pantry-1", "pantry-bins" },
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

        private static RestaurantShopRuntime FueledShop(List<string> diag, int day = 300)
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, day, diag);
            RestaurantFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
            {
                "stove-1", "cookware-1", "pantry-1",
            }, diag), "kitchen installation should succeed");
            return runtime;
        }

        private static int WorkDay(RestaurantShopRuntime runtime, int day, List<string> diag)
        {
            var registry = new EntityIdRegistry();
            return runtime.WorkDay(day, cookKitUsable: true,
                WorkstationCatalog.RestaurantKitchen, KitchenFinder(), diag, registry);
        }

        [Test]
        public void ReceiveLot_RefusesOrphanFuel()
        {
            var stock = new RestaurantFuelStock();
            var diag = new List<string>();

            string refusal = stock.ReceiveLot(new RestaurantFuelLot
            {
                FuelName = RestaurantMealCatalog.FuelItemId,
                Units = 10,
                AcquiredDayIndex = 300,
            }, diag);

            Assert.NotNull(refusal, "fuel without a supplier chain or bootstrap flag is refused");
            Assert.AreEqual(0, stock.Lots.Count);
        }

        [Test]
        public void BootstrapEndowment_IsExplicitAndOneTime()
        {
            var stock = new RestaurantFuelStock();
            var diag = new List<string>();

            RestaurantFuelBootstrap.ApplyBootstrapEndowment(stock, new EntityIdRegistry(), 300, diag);

            Assert.AreEqual(RestaurantFuelBootstrap.BootstrapFuelUnits, stock.UnitsOnHand(RestaurantMealCatalog.FuelItemId));
            Assert.IsTrue(stock.Lots[0].IsBootstrapEndowment, "the opening pile is explicitly marked");
            StringAssert.Contains("BOOTSTRAP", stock.Lots[0].ProvenanceChain());
        }

        [Test]
        public void ReceiveFuelDealerLot_NamesTheYard()
        {
            var stock = new RestaurantFuelStock();
            var diag = new List<string>();

            string refusal = RestaurantFuelSupply.ReceiveFuelDealerLot(
                stock, "fueldealer-biz-1", "dealer-lot-9", 20, 300, new EntityIdRegistry(), diag);

            Assert.Null(refusal);
            Assert.AreEqual(20, stock.UnitsOnHand(RestaurantMealCatalog.FuelItemId));
            StringAssert.Contains("fueldealer-biz-1", stock.Lots[0].ProvenanceChain());

            string orphan = RestaurantFuelSupply.ReceiveFuelDealerLot(
                stock, "", "dealer-lot-9", 20, 300, new EntityIdRegistry(), diag);
            Assert.NotNull(orphan, "an unnamed fuel dealer is refused");
        }

        [Test]
        public void WorkDay_WithoutFuel_BatchStaysPrepped_StoveCold()
        {
            var diag = new List<string>();
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, 300, diag);
            // NOTE: no fuel endowment — the woodpile is empty.
            Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
            {
                "stove-1", "cookware-1", "pantry-1",
            }, diag));
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(0, cooked, "no fuel, no cooking");
            Assert.AreEqual(RestaurantMealBatchStage.Prepped, runtime.Batches[0].Stage,
                "prep (ingredients + labor) happened; only the firing waited");
            StringAssert.Contains("stove fuel", string.Join("\n", diag));
        }

        [Test]
        public void WorkDay_BurnsOneFuelUnitPerBatch_FuelProvenanceOnLot()
        {
            var diag = new List<string>();
            var runtime = FueledShop(diag);
            int fuelBefore = runtime.FuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            Assert.AreEqual(fuelBefore - RestaurantMealCatalog.FuelUnitsPerBatchCooking,
                runtime.FuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId),
                "each batch cooking burns its fuel");
            StringAssert.Contains("stove fuel",
                string.Join(" | ", runtime.MealShelf[0].InputProvenance),
                "the firing fuel is traceable into the meal lot");
        }

        [Test]
        public void RequireKitchenFuel_False_RoutesAroundUnmodeledFuel()
        {
            var diag = new List<string>();
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, 300, diag);
            Assert.Null(runtime.AddKitchen("cookhouse", new List<string>
            {
                "stove-1", "cookware-1", "pantry-1",
            }, diag));
            runtime.RequireKitchenFuel = false; // fuel not modeled in this context
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);

            int cooked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(1, cooked, "with the gate off, cooking does not wait on fuel");
            Assert.AreEqual(0, runtime.FuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId));
        }

        [Test]
        public void FuelStock_RoundTripsSaveLoad()
        {
            var diag = new List<string>();
            var runtime = FueledShop(diag);
            runtime.PlanMealBatch(RestaurantMealCatalog.BeefStewId, 1, 300, diag);
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(runtime.FuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId),
                restored.FuelStock.UnitsOnHand(RestaurantMealCatalog.FuelItemId),
                "the woodpile survives save/load");
            Assert.IsTrue(restored.RequireKitchenFuel, "the fuel gate setting survives save/load");
        }
    }
}
