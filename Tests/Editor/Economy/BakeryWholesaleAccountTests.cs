using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1D: wholesale bread supply — standing accounts with boarding houses,
    /// hotels and restaurants (Canon §7.3E), account-driven batch planning,
    /// fresh-first fulfillment, and the owner's scarcity policy.
    /// </summary>
    [TestFixture]
    public sealed class BakeryWholesaleAccountTests
    {
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

        private static BakeryShopRuntime StockedShop(List<string> diag, int day = 290)
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        private static BakeryWholesaleAccount BoardingHouseAccount(int units = 24, int dueDay = 300)
        {
            return new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                BuyerType = BusinessType.BoardingHouse,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = units,
                DeliveryCadenceDays = 1,
                NextDeliveryDayIndex = dueDay,
                PricePerUnitCents = 6, // wholesale under the 8¢ retail price
            };
        }

        [Test]
        public void AddWholesaleAccount_ValidatesTerms_Loudly()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            Assert.IsNotNull(runtime.AddWholesaleAccount(null, diag), "null refused");
            Assert.IsNotNull(runtime.AddWholesaleAccount(new BakeryWholesaleAccount
            {
                ProductId = BakeryBreadCatalog.BreadLoafId, UnitsPerDelivery = 10,
            }, diag), "unnamed buyer refused");
            Assert.IsNotNull(runtime.AddWholesaleAccount(new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                ProductId = "bake.croissant-1870", UnitsPerDelivery = 10,
            }, diag), "unknown product refused");
            Assert.IsNotNull(runtime.AddWholesaleAccount(new BakeryWholesaleAccount
            {
                BuyerBusinessId = "boardinghouse-biz-1",
                ProductId = BakeryBreadCatalog.BreadLoafId, UnitsPerDelivery = 0,
            }, diag), "zero units refused");

            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(), diag));
            Assert.AreEqual(1, runtime.WholesaleAccounts.Count);
            Assert.AreEqual(BakeryWholesaleFreshness.FreshOrDayOld,
                runtime.WholesaleAccounts[0].Freshness, "freshness term defaults explicitly");
        }

        [Test]
        public void PlanWholesaleBatches_CoversDueAccounts_FromPipelineFirst()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));

            var planned = runtime.PlanWholesaleBatches(300, diag);
            Assert.AreEqual(1, planned.Count, "24 loaves = 1 bread batch");

            // Already covered: planning again adds nothing.
            var replanned = runtime.PlanWholesaleBatches(300, diag);
            Assert.AreEqual(0, replanned.Count, "the pipeline already covers the account");

            // The planned batch bakes and the account fills.
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            var record = runtime.FulfillWholesale(runtime.WholesaleAccounts[0].AccountId, 300, diag,
                new EntityIdRegistry());
            Assert.IsNotNull(record);
            Assert.IsTrue(record.FullyCovered);
            Assert.AreEqual(24, record.Units);
            Assert.AreEqual("boardinghouse-biz-1", record.BuyerBusinessId);
            Assert.AreEqual(6, record.PricePerUnitCents);
            Assert.Greater(record.ProvenanceLines.Count, 0, "flour provenance rides to the buyer");
            Assert.AreEqual(301, runtime.WholesaleAccounts[0].NextDeliveryDayIndex,
                "the account advances to its next delivery day");
        }

        [Test]
        public void FulfillWholesale_ShipsFreshFirst_DayOldOnlyIfAllowed()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 301, diag));
            Assert.AreEqual(1, WorkDay(runtime, 301, diag));
            // Shelf: 24 day-old (baked 300) + 24 fresh (baked 301).

            var account = BoardingHouseAccount(units: 24, dueDay: 301);
            Assert.IsNull(runtime.AddWholesaleAccount(account, diag));
            var record = runtime.FulfillWholesale(account.AccountId, 301, diag, new EntityIdRegistry());

            Assert.IsNotNull(record);
            Assert.IsTrue(record.FullyCovered);
            Assert.AreEqual(runtime.BreadShelf[1].LotId.ToString(), record.SellerLotIds[0],
                "wholesale takes the fresh lot; the retail counter sells day-old first (W2A)");

            // The day-old lot remains for the counter.
            var retail = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 301, diag);
            Assert.AreEqual(1, retail.Count);
            Assert.AreEqual(BakeryGoodsCondition.DayOld, retail[0].ConditionSoldAt);
        }

        [Test]
        public void FulfillWholesale_FreshOnlyAccount_RefusesDayOld()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            // Day 301: the only lot is day-old.

            var account = BoardingHouseAccount(units: 24, dueDay: 301);
            account.Freshness = BakeryWholesaleFreshness.FreshOnly;
            Assert.IsNull(runtime.AddWholesaleAccount(account, diag));

            var record = runtime.FulfillWholesale(account.AccountId, 301, diag, new EntityIdRegistry());
            Assert.IsNotNull(record);
            Assert.IsFalse(record.FullyCovered, "day-old bread must not ship to a fresh-only account");
            Assert.AreEqual(0, record.Units);
            StringAssert.Contains("short", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void SupplyProtection_ContractsBeatCounter_ByDefault()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            // 24 fresh loaves on the shelf; the account holds all 24.

            var held = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 300, diag);
            Assert.AreEqual(0, held.Count, "contracts are protected: the counter cannot sell committed bread");
            StringAssert.Contains("supply protection", string.Join("\n", diag).ToLower());

            // The account still fills in full.
            var record = runtime.FulfillWholesale(runtime.WholesaleAccounts[0].AccountId, 300, diag,
                new EntityIdRegistry());
            Assert.IsTrue(record.FullyCovered);
        }

        [Test]
        public void SupplyProtection_RetailCounterPolicy_SellsFreely()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SupplyProtection = BakerySupplyProtectionPolicy.ProtectRetailCounter;
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var sold = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 300, diag);
            Assert.AreEqual(24, sold[0].Units, "the counter sells freely under this policy");

            var record = runtime.FulfillWholesale(runtime.WholesaleAccounts[0].AccountId, 300, diag,
                new EntityIdRegistry());
            Assert.IsFalse(record.FullyCovered, "the account shorts — loudly, never faked");
        }

        [Test]
        public void SupplyProtection_ProportionalShare_SplitsShelf()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SupplyProtection = BakerySupplyProtectionPolicy.ProportionalShare;
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 24, dueDay: 300), diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            // 24 sellable, 24 committed, reference demand 24 → counter gets half.
            var sold = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 300, diag);
            Assert.AreEqual(12, sold[0].Units);
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesAccountsAndPolicy()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SupplyProtection = BakerySupplyProtectionPolicy.ProtectRetailCounter;
            Assert.IsNull(runtime.AddWholesaleAccount(BoardingHouseAccount(units: 48, dueDay: 305), diag));

            var dto = runtime.CaptureSaveDto();
            var revived = new BakeryShopRuntime("bakery-biz-1");
            revived.LoadFromSaveDto(dto);

            Assert.AreEqual(BakerySupplyProtectionPolicy.ProtectRetailCounter, revived.SupplyProtection);
            Assert.AreEqual(1, revived.WholesaleAccounts.Count);
            Assert.AreEqual(48, revived.WholesaleAccounts[0].UnitsPerDelivery);
            Assert.AreEqual(305, revived.WholesaleAccounts[0].NextDeliveryDayIndex);
            Assert.AreEqual("boardinghouse-biz-1", revived.WholesaleAccounts[0].BuyerBusinessId);
        }
    }
}
