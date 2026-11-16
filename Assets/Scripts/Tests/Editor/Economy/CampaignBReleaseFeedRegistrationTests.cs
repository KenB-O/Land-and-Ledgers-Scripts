using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Population;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Campaign B (DEF-008), 2026-11-15: horse-feed replenishment works
    /// only in dev/editor builds. The feed counterparty (Millbrook) was
    /// registered only inside the dev-only ConfigurePlaytestHorseFeed, so
    /// in release builds the standing reorder refused loudly instead of
    /// ordering from nobody and the freight horse went unfed. The fix
    /// registers the same three things in the release path
    /// (RegisterNotionsProcurement calls RegisterReleaseHorseFeed): the
    /// Millbrook counterparty, the household catalog item, and the store
    /// feed registry item — all built by StoreHorseFeedReorder builders
    /// on the same opening terms, so the terms cannot drift between
    /// build flavors.
    ///
    /// These tests pin the builders' terms and the release-market shape:
    /// a market that already carries another counterparty (the shape of
    /// the release market, which registers notions first) but no feed
    /// counterparty refuses, and orders on opening terms once the
    /// builder's counterparty is added. The bootstrapper itself is a
    /// MonoBehaviour and is verified by Roslyn syntax-parse plus source
    /// review only, as with the earlier campaign packages.
    /// </summary>
    [TestFixture]
    public sealed class CampaignBReleaseFeedRegistrationTests
    {
        private const string StoreInstanceId = "general-store-1";

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }
            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;
            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents)
                    return "test cash port refused debit";
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

        [Test]
        public void MillbrookCounterpartyCarriesTheAuthoredOpeningTerms()
        {
            ExternalCounterparty counterparty = StoreHorseFeedReorder.BuildMillbrookCounterparty();
            Assert.IsNotNull(counterparty);
            Assert.AreEqual(StoreHorseFeedReorder.MillbrookSupplierId, counterparty.CounterpartyId);
            Assert.AreEqual("Millbrook Grain Co.", counterparty.DisplayName);
            Assert.AreEqual(1, counterparty.TransitDays);
            Assert.AreEqual(0, counterparty.InitialBudgetCents);
            Assert.AreEqual(1, counterparty.SupplyUnits.Count);
            Assert.AreEqual(StoreHorseFeedReorder.OpeningLotSacks,
                counterparty.SupplyUnits[StoreHorseFeedReorder.FeedSkuId],
                "the opening lot is 20 sacks, exactly as the dev registration seeds it");
        }

        [Test]
        public void FeedRegistryItemCarriesTheAuthoredOpeningTerms()
        {
            Review2StoreFeedItem item = StoreHorseFeedReorder.BuildFeedRegistryItem(StoreInstanceId);
            Assert.IsNotNull(item);
            Assert.AreEqual(StoreInstanceId, item.BusinessInstanceId);
            Assert.AreEqual(StoreHorseFeedReorder.FeedSkuId, item.FeedItemId);
            Assert.AreEqual(StoreHorseFeedReorder.FeedCategoryId, item.FeedCategoryId);
            Assert.AreEqual("Horse Feed", item.DisplayName);
            Assert.AreEqual("sack", item.UnitLabel);
            Assert.AreEqual(StoreHorseFeedReorder.FeedPriceCentsPerSack, item.RetailPriceCents,
                "the retail price is the opening 15c/sack");
            Assert.AreEqual(2, item.FeedUnitsPerStoreUnitNumerator);
            Assert.AreEqual(1, item.FeedUnitsPerStoreUnitDenominator,
                "one sack converts to two feed units");
        }

        [Test]
        public void FeedCatalogItemMatchesTheDevRegistration()
        {
            HouseholdItemDefinition item = StoreHorseFeedReorder.BuildFeedCatalogItem();
            Assert.IsNotNull(item);
            Assert.AreEqual(StoreHorseFeedReorder.FeedSkuId, item.ItemId);
            Assert.AreEqual("Horse Feed", item.DisplayName);
            Assert.AreEqual("sack", item.UnitLabel);
            Assert.AreEqual(StoreHorseFeedReorder.FeedCategoryId, item.ReserveCategoryId);
            Assert.AreEqual(1, item.MealServingUnits);
            Assert.IsFalse(item.IsFood, "feed is not a meal item");
        }

        [Test]
        public void HelperCatalogItemRegistersIdempotently()
        {
            // Identical re-registration returns null; a conflicting
            // definition would refuse. The static catalog is process-wide,
            // so this also guards the release registration against a
            // second call in the same session.
            Assert.IsNull(HouseholdItemCatalog.RegisterRuntimeItem(
                StoreHorseFeedReorder.BuildFeedCatalogItem()));
            Assert.IsNull(HouseholdItemCatalog.RegisterRuntimeItem(
                StoreHorseFeedReorder.BuildFeedCatalogItem()));
            Assert.IsNotNull(HouseholdItemCatalog.Get(StoreHorseFeedReorder.FeedSkuId));
        }

        [Test]
        public void HelperRegistryItemRegistersAndResolves()
        {
            // The runtime here is the harness stub (category stock
            // untracked: GetCategoryStock returns null, EnsureCategoryStock
            // is a no-op). What this pins is real: AddFeedItem's
            // validation accepts the builder's item and the registry
            // resolves it. The "creates no stock" property against a real
            // runtime is covered by Unity tests (Review2VerticalD2Tests).
            var storeRuntime = new BusinessRuntimeState();
            var registry = new Review2StoreFeedRegistry();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(storeRuntime, registry,
                StoreHorseFeedReorder.BuildFeedRegistryItem(StoreInstanceId), new List<string>()));
            Review2StoreFeedItem resolved = registry.Find(StoreInstanceId);
            Assert.IsNotNull(resolved, "the trading port offers feed only from a registered item");
            Assert.AreEqual(StoreHorseFeedReorder.FeedSkuId, resolved.FeedItemId);
            Assert.AreEqual(StoreHorseFeedReorder.FeedPriceCentsPerSack, resolved.RetailPriceCents);
        }

        private static ExternalMarketAuthority ReleaseShapedMarket()
        {
            // The release market shape: another counterparty is already
            // registered (the notions counterparty in production; the
            // values here are a shape stand-in, not its terms) and no
            // feed counterparty exists yet.
            var market = new ExternalMarketAuthority();
            Assert.IsNull(market.RegisterCounterparty(new ExternalCounterparty
            {
                CounterpartyId = "release_market_existing_supplier",
                DisplayName = "Existing Supplier",
                TransitDays = 14,
                InitialBudgetCents = 0,
                SupplyUnits = new Dictionary<string, int> { ["existing_supply_sku"] = 30 },
            }));
            return market;
        }

        private static StoreHorseFeedReorderInputs ReorderInputs(
            OffMapNamedSkuProcurementService service, int storeSacksOnHand, int dayIndex)
        {
            return new StoreHorseFeedReorderInputs
            {
                Service = service,
                SupplierId = StoreHorseFeedReorder.MillbrookSupplierId,
                SkuId = StoreHorseFeedReorder.FeedSkuId,
                UnitPriceCents = StoreHorseFeedReorder.FeedPriceCentsPerSack,
                LotSacks = StoreHorseFeedReorder.OpeningLotSacks,
                StoreSacksOnHand = storeSacksOnHand,
                DayIndex = dayIndex,
            };
        }

        [Test]
        public void ReleaseMarketShapeRefusesWithoutTheFeedCounterparty()
        {
            // The release symptom, pinned: the market has a counterparty
            // but not the feed one, so the planner refuses loudly instead
            // of ordering from nobody.
            var market = ReleaseShapedMarket();
            var service = new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, new FakeBusinessCashPort(50000));

            StoreHorseFeedReorderResult result =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), new List<string>());
            Assert.IsFalse(result.Ordered);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Refusal));
            Assert.AreEqual(0, service.Orders.Count);
        }

        [Test]
        public void ReleaseMarketShapeOrdersOnOpeningTermsOnceCounterpartyIsRegistered()
        {
            // The fix, pinned end to end at the planner level: adding the
            // builder's counterparty to the release-shaped market lets the
            // standing reorder place one lot on the opening terms.
            var market = ReleaseShapedMarket();
            var service = new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, new FakeBusinessCashPort(50000));
            Assert.IsNull(market.RegisterCounterparty(
                StoreHorseFeedReorder.BuildMillbrookCounterparty()),
                "the feed counterparty registers on the market that already carries another supplier");

            StoreHorseFeedReorderResult result =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), new List<string>());
            Assert.IsTrue(result.Ordered);
            Assert.IsNotNull(result.Order);
            Assert.AreEqual(StoreHorseFeedReorder.FeedSkuId, result.Order.SkuId);
            Assert.AreEqual(20, result.Order.UnitsOrdered);
            Assert.AreEqual(15, result.Order.UnitGoodsCostCents);
            Assert.AreEqual(300, result.Order.TotalCostCents, "20 sacks at 15c, no freight surcharge");
            Assert.AreEqual(6, result.Order.ExpectedArrivalDayIndex, "1-day transit from day 5");
            Assert.AreEqual(20, market.GetCounterparty(
                StoreHorseFeedReorder.MillbrookSupplierId).SupplyUnits[StoreHorseFeedReorder.FeedSkuId],
                "release shape: the builder seeds 20 sacks, the reorder restocks 20 more, the order " +
                "consumes 20 — the initial 20 stays at the supplier. The dev path differs only here: " +
                "its opening order consumes the seed first, so its steady state is 0. Supplier-side " +
                "supply is not local stock and moves no cash, so the leftover 20 is bookkeeping, " +
                "not a gameplay difference.");

            StoreHorseFeedReorderResult duplicate =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), new List<string>());
            Assert.IsFalse(duplicate.Ordered, "an outstanding feed order blocks a duplicate");
            Assert.AreEqual(1, service.Orders.Count);
        }
    }
}
