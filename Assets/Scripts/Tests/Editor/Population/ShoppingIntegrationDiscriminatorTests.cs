// Priority 1 Slice 0 — discriminator pins for the zero-purchase
// investigation. Each test demonstrates in source the exact behaviour
// the corresponding hypothesis claims, using the real authorities
// (real GeneralStoreSupplierAdapter, real BusinessRuntimeState, real
// HouseholdShortageMonitor, real ShopperSellerKnowledge). They are
// expected to PASS: a pin that contradicts the plan's source reading
// fails the slice and rewrites the hypothesis ranking.
//
// Two seams are test-local replicas, because the production owners
// are harness-excluded and cannot be compiled by the Roslyn harness:
//  - DiscriminatorStorePort mirrors the production
//    GeneralStoreTradingPort read rules (item-level offers read
//    identified retail lots, pooled offers read the category pool;
//    TradingCapability.Staffed is read live from
//    RuntimeState.FilledWorkerCount, the production NPC-branch rule)
//    over the same real BusinessRuntimeState.
//  - SeedLocalGeneralStoreKnowledge / IsEligibleLocalGeneralStoreSupplier
//    replicate SimulationDrivers.cs:660-700 verbatim. If the production
//    rule changes, this replica must change with it; the replica pins
//    the rule as written, it cannot catch production drift.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Population;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class ShoppingIntegrationDiscriminatorTests
    {
        // ---- test port over a real BusinessRuntimeState ----------------
        // Mirrors the production GeneralStoreTradingPort read rules; see
        // the file header. Every cash/stock move lands in the shared
        // runtime; this is a test seam, not a second cash authority.
        private sealed class DiscriminatorStorePort : IGeneralStoreTradingPort, IItemLevelGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public string BusinessId = "general-store-1";
            public string Location = "building:7";
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => BusinessId;
            public string StoreName => "General Store";
            public string LocationId => Location;
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;

            // Production NPC-branch rule (GeneralStoreTradingPort.cs):
            // staffed iff the bound business runtime has a filled worker.
            // A state with no worker slots therefore reads unstaffed.
            public StoreTradingCapability TradingCapability => new StoreTradingCapability
            {
                Staffed = Runtime != null && Runtime.FilledWorkerCount > 0,
            };

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

            private GeneralStoreCategoryOffer FindOffer(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return null;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer?.OfferedItemIds == null) continue;
                    foreach (string offered in offer.OfferedItemIds)
                        if (string.Equals(offered, itemId, StringComparison.OrdinalIgnoreCase)) return offer;
                }

                return null;
            }

            public bool UsesItemLevelInventory(string itemId)
            {
                return FindOffer(itemId)?.UsesItemLevelInventory == true;
            }

            public int ItemStockUnits(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                return offer.UsesItemLevelInventory
                    ? (Runtime?.GetRetailItemStockUnits(itemId, offer.CategoryId) ?? 0) / Math.Max(1, offer.StoreUnitsPerItemUnit)
                    : CategoryStockUnits(offer.CategoryId) / Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public int ItemPricePerUnitCents(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                int storeUnitPrice = offer.UsesItemLevelInventory
                    ? offer.ItemPriceOverrideCents > 0 ? offer.ItemPriceOverrideCents : (Prices.TryGetValue(itemId, out int p) ? p : 0)
                    : CategoryPricePerUnitCents(offer.CategoryId);
                return storeUnitPrice * Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null || !offer.UsesItemLevelInventory || Runtime == null) return false;
                int factor = Math.Max(1, offer.StoreUnitsPerItemUnit);
                if (!Runtime.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units * factor, out int consumedStoreUnits))
                    return false;
                consumedUnits = consumedStoreUnits / factor;
                return consumedUnits > 0;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                Runtime?.RecordRetailItemSettlement(categoryId, unitsSold, revenueCents);
            }

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                    if (offer != null && offer.UsesItemLevelInventory
                        && string.Equals(offer.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                        return false;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                Runtime?.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "Store runtime unavailable.";
                if (Runtime.CurrentCashCents < Math.Max(0, amountCents)) return "cannot afford";
                Runtime.SpendCents(Math.Max(0, amountCents));
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "Store runtime unavailable.";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                return stock.TargetStockUnits > 0
                    ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                    : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        // ---- production seeding rule replica (SimulationDrivers.cs) ---
        // SimulationDrivers is harness-excluded; this replicates its
        // IsEligibleLocalGeneralStoreSupplier + SeedLocalGeneralStoreKnowledge
        // verbatim so the H1 mechanism is checkable in source.
        private static bool IsEligibleLocalGeneralStoreSupplier(IGoodsSupplier supplier)
        {
            if (!(supplier is GeneralStoreSupplierAdapter adapter)
                || string.IsNullOrWhiteSpace(adapter.SupplierBusinessId))
            {
                return false;
            }

            if (!string.Equals(adapter.SupplierBusinessId, "general-store", StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(adapter.LocationId)
                && adapter.LocationId.StartsWith("building:", StringComparison.Ordinal);
        }

        private static void SeedLocalGeneralStoreKnowledge(
            IEnumerable<PersonState> people,
            IEnumerable<IGoodsSupplier> suppliers,
            ShopperSellerKnowledge knowledge,
            int dayIndex)
        {
            if (people == null || suppliers == null || knowledge == null) return;
            foreach (PersonState person in people)
            {
                if (person == null || person.id < 0 || person.deathDayIndex >= 0 || person.isOffMap) continue;
                foreach (IGoodsSupplier supplier in suppliers)
                {
                    if (!IsEligibleLocalGeneralStoreSupplier(supplier)) continue;
                    knowledge.LearnSupplier(person.id, supplier.SupplierBusinessId,
                        "local public General Store", dayIndex);
                }
            }
        }

        // ---- shared fixtures ------------------------------------------
        private static List<PersonState> TwoResidents()
        {
            return new List<PersonState>
            {
                new PersonState { id = 1 },
                new PersonState { id = 2 },
            };
        }

        private static GeneralStoreCategoryOffer BreadOffer(bool itemLevel)
        {
            return new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 1,
                UsesItemLevelInventory = itemLevel,
                ItemPriceOverrideCents = 5,
            };
        }

        // One real BusinessRuntimeState seeded exactly as the opening
        // endowment does: 18 identified bread loaves (which also raise
        // the staple_food pool by 18) plus a 6-unit pool-only top-up, so
        // the pool total (24) and the identified lots (18) differ and the
        // two port flavours can be told apart by what they report.
        private static BusinessRuntimeState SeededStoreState()
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general-store-1");
            state.EnsureCategoryStock("staple_food", 0, 1000);
            Assert.IsNull(state.TryAddRetailItemLot("lot-bread-1", HouseholdItemCatalog.BreadId,
                "staple_food", "loaf", 18, "general-store-1", "general-store-1",
                "test opening endowment", 0), "identified bread lot should land");
            Assert.AreEqual(6, state.AddCategoryStockUnits("staple_food", 6),
                "pool-only top-up should land in full");
            Assert.AreEqual(24, state.GetCategoryStock("staple_food").CurrentStockUnits,
                "pool holds lots (18) + top-up (6)");
            Assert.AreEqual(18, state.GetRetailItemStockUnits(HouseholdItemCatalog.BreadId),
                "identified lots hold exactly the 18 loaves");
            return state;
        }

        private static DiscriminatorStorePort FallbackPort(BusinessRuntimeState state)
        {
            var port = new DiscriminatorStorePort
            {
                Runtime = state,
                BusinessId = "general-store",
                Location = "general-store",
            };
            port.Prices["staple_food"] = 5;
            port.OfferList.Add(BreadOffer(itemLevel: false));
            return port;
        }

        private static DiscriminatorStorePort NpcPort(BusinessRuntimeState state)
        {
            var port = new DiscriminatorStorePort
            {
                Runtime = state,
                BusinessId = "general-store-1",
                Location = "building:7",
            };
            port.Prices["staple_food"] = 5;
            port.OfferList.Add(BreadOffer(itemLevel: true));
            return port;
        }

        // ---- H1: knowledge seeding ------------------------------------
        [Test]
        public void KnowledgeSeeding_FallbackPortWithoutBuildingLocation_SeedsNobody()
        {
            var directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(directory, FallbackPort(SeededStoreState()));
            var knowledge = new ShopperSellerKnowledge();

            SeedLocalGeneralStoreKnowledge(TwoResidents(), directory.All, knowledge, dayIndex: 0);

            Assert.AreEqual(0, knowledge.Records.Count,
                "a fallback port with LocationId 'general-store' (no building) is ineligible, so nobody learns the store");
            Assert.IsTrue(!knowledge.Knows(1, "general-store"));
            Assert.IsTrue(!knowledge.Knows(2, "general-store"));
        }

        [Test]
        public void KnowledgeSeeding_NpcPort_SeedsEveryResident()
        {
            var directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(directory, NpcPort(SeededStoreState()));
            var knowledge = new ShopperSellerKnowledge();

            SeedLocalGeneralStoreKnowledge(TwoResidents(), directory.All, knowledge, dayIndex: 0);

            Assert.AreEqual(2, knowledge.Records.Count, "every resident learns the NPC store");
            Assert.IsTrue(knowledge.Knows(1, "general-store-1"));
            Assert.IsTrue(knowledge.Knows(2, "general-store-1"));
        }

        // ---- H1/H2 discriminator: the shortage monitor ----------------
        private sealed class MonitorFixture
        {
            public HouseholdState Household = new HouseholdState { id = 7 };
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public SupplierDirectory Directory = new SupplierDirectory();
            public ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public List<int> MemberIds = new List<int> { 1, 2 };

            public HouseholdShortageMonitor Monitor()
            {
                // Production value (SimulationDrivers.DriveHouseholdShortageMonitor).
                return new HouseholdShortageMonitor(Needs) { LowDaysThreshold = 1f };
            }

            public void Evaluate(HouseholdShortageMonitor monitor, int dayIndex)
            {
                monitor.Evaluate(
                    Household,
                    Inventories.GetOrCreate(7),
                    // 2-person household: 2 members x 1 loaf x 3 meals/day.
                    new Dictionary<string, int> { { HouseholdItemCatalog.BreadId, 6 } },
                    isColdSeason: false,
                    supplyAccess: new HouseholdSupplyAccess(),
                    upcomingObligationCents: 0,
                    expectedIncomeCents: 0,
                    dayIndex: dayIndex,
                    isRecognizedByKnownSeller: HouseholdShortageMonitor.KnownSellerRecognitionPredicate(
                        Directory, Knowledge, MemberIds));
            }
        }

        [Test]
        public void Monitor_WithEmptyKnowledge_KeepsFoodNeedOpen_AndLogsSellerBlocker()
        {
            var f = new MonitorFixture();
            GeneralStoreSupplierAdapter.RegisterIn(f.Directory, FallbackPort(SeededStoreState()));
            HouseholdShortageMonitor monitor = f.Monitor();

            f.Evaluate(monitor, dayIndex: 0);

            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId),
                "a real bread shortage stays visible even before the household knows a seller");
            bool blockerLogged = false;
            foreach (string line in monitor.Diagnostics)
            {
                if (line != null && line.Contains("H7 has an open 'bread' shortage, but no known seller currently offers it"))
                    blockerLogged = true;
            }

            Assert.IsTrue(blockerLogged,
                "the seller blocker must remain visible beside the open need; diagnostics: " + string.Join(" | ", monitor.Diagnostics));
        }

        [Test]
        public void Monitor_WithKnowledge_OpensBreadNeed_FromZeroStock()
        {
            var f = new MonitorFixture();
            GeneralStoreSupplierAdapter.RegisterIn(f.Directory, NpcPort(SeededStoreState()));
            f.Knowledge.LearnSupplier(1, "general-store-1", "lives in the same town", 0);
            f.Knowledge.LearnSupplier(2, "general-store-1", "lives in the same town", 0);
            HouseholdShortageMonitor monitor = f.Monitor();

            f.Evaluate(monitor, dayIndex: 0);

            HouseholdPurchasingNeed need = f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need, "a recognized seller opens the need even with zero household stock");
            Assert.AreEqual(6, need.UnitsNeeded,
                "2-person household, 1 day of supply below the threshold: 6 loaves");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            foreach (string line in monitor.Diagnostics)
            {
                Assert.IsTrue(line == null || !line.Contains("will not raise a 'bread' need"),
                    "no suppression diagnostic once the seller is known: " + line);
            }
        }

        // ---- H2: which stock does the registered port read? -----------
        [Test]
        public void FallbackPortAdapter_ReadsCategoryPool_NotIdentifiedLots()
        {
            BusinessRuntimeState state = SeededStoreState();
            var adapter = new GeneralStoreSupplierAdapter(FallbackPort(state));

            Assert.AreEqual(24, adapter.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "fallback-shaped adapter reports the category pool total (18 lots + 6 pool-only), not the identified lots");
            Assert.AreEqual(24, adapter.StockUnits("staple_food"));
            Assert.AreEqual(18, state.GetRetailItemStockUnits(HouseholdItemCatalog.BreadId),
                "the identified lots themselves are still exactly 18");
        }

        [Test]
        public void NpcPortAdapter_ReadsIdentifiedLots()
        {
            BusinessRuntimeState state = SeededStoreState();
            var adapter = new GeneralStoreSupplierAdapter(NpcPort(state));

            Assert.AreEqual(18, adapter.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "item-level adapter reports exactly the identified lot units, not the pool total");
            Assert.AreEqual(24, adapter.StockUnits("staple_food"),
                "the category surface still reads the pool");
        }

        // ---- H3: staffing ---------------------------------------------
        [Test]
        public void UnstaffedStore_CheckTradingCapabilityRefuses()
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general-store-1");
            state.EnsureCategoryStock("staple_food", 0, 1000);
            Assert.AreEqual(0, state.FilledWorkerCount,
                "fresh state carries no worker slots, so nothing is filled");
            var adapter = new GeneralStoreSupplierAdapter(NpcPort(state));

            // Day 0 is a Monday inside store hours (480-1080): hours pass,
            // so any refusal is the staffing gate, not the clock.
            string refusal = adapter.CheckTradingCapability(absoluteDayIndex: 0, minuteOfDay: 600);

            Assert.IsNotNull(refusal, "an unstaffed store must refuse to trade");
            Assert.IsTrue(refusal.Contains("unstaffed"),
                "refusal names staffing; got: " + refusal);
        }
    }
}
