using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M0 (Obj 42): characterization tests for
    /// the real-demand pipeline, pinning current behaviour before M2/M4
    /// wire it into the weekly pass. The pipeline lives in the
    /// harness-visible RealDemandSource (SBRM's CollectRealDemand /
    /// RegisterBusinessInputNeed wrappers are harness-excluded and have
    /// ZERO callers anywhere — see the M0 inventory — so these tests run
    /// against the source directly, which is exactly what M2 wires in
    /// and M4 completes on the producer side).
    ///
    /// What RealDemandTests already pins is NOT repeated here: the
    /// single-need business intent, self-supply refusal, household
    /// reserve/registry channels, funds/knowledge/stock/price gates.
    /// What this file adds: the bakery's multi-input needs (flour +
    /// notions + fuel — the M4 producer examples), the null-registry
    /// state, weekly clear/re-register hygiene, invalid-registration
    /// guards, and the duplicate-registration accumulation hazard M2
    /// must not introduce into the weekly pass.
    /// Unity compilation, Test Runner, PlayMode, and SBRM itself are NOT
    /// claimed; SBRM edits are source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class SharedBusinessDemandWiringTests
    {
        private sealed class TestSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();

            public bool HasCategory(string categoryId) => Stock.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int PricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) Stock[categoryId] -= sold;
                return sold;
            }
        }

        private sealed class Fixture
        {
            public PopulationState Population = new PopulationState();
            public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public SupplierDirectory Directory = new SupplierDirectory();
            public List<string> Diag = new List<string>();
            public int HouseholdId = 7;

            public Fixture()
            {
                var household = new HouseholdState { id = HouseholdId };
                household.reserves.Add(new HouseholdReserveState
                {
                    categoryId = "staple_food",
                    displayName = "Staple food",
                    currentUnits = 2,
                    lowThresholdUnits = 14,
                    targetUnits = 20,
                });
                Population.households.Add(household);
            }

            public void FundHousehold(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public TestSupplier AddSupplier(string id, string category, int stock, int priceCents)
            {
                var supplier = new TestSupplier { SupplierBusinessId = id };
                supplier.Stock[category] = stock;
                supplier.Prices[category] = priceCents;
                Directory.Register(supplier);
                return supplier;
            }

            public RealDemandSource NewSource(
                HouseholdNeedRegistry needs, PopulationState population) =>
                new RealDemandSource(
                    population, Ledgers, needs, Directory,
                    householdKnowsSupplier: null,
                    businessCashCents: id => 5000,
                    generalStoreSupplierId: "store-1");
        }

        [Test]
        public void CollectRealDemand_ReturnsIntentsFromRegisteredNeeds()
        {
            // The M4 producer examples: the bakery registers its flour,
            // notions, and fuel needs; each becomes one intent naming the
            // real buyer, a real stocked supplier, and a valid price.
            // (Single-need collection is already pinned by RealDemandTests;
            // this pins the multi-input bakery case M4 wires.)
            var f = new Fixture();
            f.AddSupplier("mill-2", "flour", 50, 200);
            f.AddSupplier("store-1", "notions", 30, 10);
            f.AddSupplier("store-1", "fuel", 40, 100);

            RealDemandSource source = f.NewSource(f.Needs, null);
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 10,
                MaxPriceCentsPerUnit = 250,
                ReasonSummary = "weekly bread production",
            });
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "notions",
                UnitsNeeded = 5,
                MaxPriceCentsPerUnit = 15,
                ReasonSummary = "weekly bread production",
            });
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "fuel",
                UnitsNeeded = 2,
                MaxPriceCentsPerUnit = 150,
                ReasonSummary = "weekly bread production",
            });

            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);

            Assert.AreEqual(3, intents.Count, "three registered needs produce three intents; diag: " + string.Join("; ", f.Diag));
            foreach (PurchaseIntent intent in intents)
            {
                Assert.AreEqual("business", intent.BuyerKind);
                Assert.AreEqual("bakery-1", intent.BuyerId);
                Assert.AreEqual("cash", intent.SettlementKind);
                Assert.IsTrue(!string.IsNullOrWhiteSpace(intent.Provenance), "provenance documents need->supplier->funds");
            }
            Assert.AreEqual("flour", intents[0].GoodsOrServiceId);
            Assert.AreEqual("mill-2", intents[0].SellerBusinessInstanceId);
            Assert.AreEqual(10, intents[0].QuantityUnits);
            Assert.AreEqual(2000, intents[0].AgreedPriceCents);
            Assert.AreEqual("notions", intents[1].GoodsOrServiceId);
            Assert.AreEqual(5, intents[1].QuantityUnits);
            Assert.AreEqual(50, intents[1].AgreedPriceCents);
            Assert.AreEqual("fuel", intents[2].GoodsOrServiceId);
            Assert.AreEqual(2, intents[2].QuantityUnits);
            Assert.AreEqual(200, intents[2].AgreedPriceCents);
        }

        [Test]
        public void HouseholdNeedRegistry_Unassigned_CollectsNoHouseholdNeeds()
        {
            // SBRM's HouseholdNeedRegistry is never assigned in production
            // (no production assignment exists; SimulationSystemsHub
            // .PurchasingNeeds is never wired to it). With the registry
            // null AND reserves topped up (no live shortfall), collection
            // returns NO household intents: the open-need channel is dead.
            var f = new Fixture();
            f.Population.GetHousehold(f.HouseholdId).reserves[0].currentUnits = 20;
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 100, 120);

            RealDemandSource noRegistry = f.NewSource(null, f.Population);
            List<PurchaseIntent> intents = noRegistry.CollectDemand(3, f.Diag);
            Assert.AreEqual(0, intents.Count, "null registry + no reserve shortfall = no household demand");

            // Precision, not overclaim: the LIVE reserve channel still
            // works without a registry — only the open-need channel is
            // dead. A reserve shortfall still produces an intent.
            f.Population.GetHousehold(f.HouseholdId).reserves[0].currentUnits = 2;
            List<PurchaseIntent> shortfall = noRegistry.CollectDemand(3, f.Diag);
            Assert.AreEqual(1, shortfall.Count, "the reserve shortfall channel survives a null registry");
            Assert.AreEqual("household", shortfall[0].BuyerKind);
            Assert.AreEqual(12, shortfall[0].QuantityUnits, "shortfall 14-2");
        }

        [Test]
        public void BusinessInputNeeds_ClearedBetweenWeeks_NoStaleIntents()
        {
            // Weekly hygiene for M2: the weekly pass must re-register
            // each week's needs. Clearing between weeks must leave no
            // stale business intents behind.
            var f = new Fixture();
            f.AddSupplier("mill-2", "flour", 50, 200);
            RealDemandSource source = f.NewSource(f.Needs, null);
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 10,
                ReasonSummary = "weekly bread production",
            });

            Assert.AreEqual(1, source.CollectDemand(3, f.Diag).Count, "the registered need collects once");
            source.ClearBusinessInputNeeds();
            Assert.AreEqual(0, source.CollectDemand(4, f.Diag).Count, "after clearing, the stale need collects nothing");
        }

        [Test]
        public void BusinessInputNeed_InvalidRegistration_Ignored()
        {
            var f = new Fixture();
            f.AddSupplier("mill-2", "flour", 50, 200);
            RealDemandSource source = f.NewSource(f.Needs, null);

            source.RegisterBusinessInputNeed(null);
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = string.Empty,
                InputCategoryId = "flour",
                UnitsNeeded = 10,
            });
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = string.Empty,
                UnitsNeeded = 10,
            });
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 0,
            });

            Assert.AreEqual(0, source.CollectDemand(3, f.Diag).Count,
                "null, buyerless, category-less, and zero-unit needs are all ignored, never collected");
        }

        [Test]
        public void WeeklyPass_CollectsDemandBeforeProcurement()
        {
            // M2 (Plan 04, D-S5): the demand signal the weekly pass
            // reads. The COLLECTION ORDER itself — CollectRealDemand once
            // at the top of ResolveWeeklySharedOperations, before the
            // converter — is MonoBehaviour-level and source-reviewed in
            // SBRM; this test pins the seam it feeds: the covered-vs-
            // formula desired-units selection in
            // SharedBusinessWeeklyProcurement.ResolveDesiredUnits.
            var intents = new List<PurchaseIntent>
            {
                new PurchaseIntent { BuyerKind = "business", BuyerId = "bakery-1", GoodsOrServiceId = "flour", QuantityUnits = 10, AgreedPriceCents = 2000 },
                new PurchaseIntent { BuyerKind = "business", BuyerId = "bakery-1", GoodsOrServiceId = "flour", QuantityUnits = 5, AgreedPriceCents = 1000 },
                new PurchaseIntent { BuyerKind = "business", BuyerId = "bakery-1", GoodsOrServiceId = "notions", QuantityUnits = 5, AgreedPriceCents = 50 },
                // A household intent for the same category string must
                // NEVER read as a business need.
                new PurchaseIntent { BuyerKind = "household", BuyerId = "7", GoodsOrServiceId = "flour", QuantityUnits = 12, AgreedPriceCents = 1440 },
            };

            Assert.IsTrue(SharedBusinessWeeklyProcurement.TryGetDemandUnits(intents, "bakery-1", "flour", out int flourUnits),
                "the bakery's flour need is covered");
            Assert.AreEqual(15, flourUnits, "duplicate registrations sum: 10 + 5 (the M0 hazard — M4 must clear + re-register weekly)");
            Assert.IsTrue(SharedBusinessWeeklyProcurement.TryGetDemandUnits(intents, "bakery-1", "notions", out int notionsUnits));
            Assert.AreEqual(5, notionsUnits);
            Assert.IsTrue(!SharedBusinessWeeklyProcurement.TryGetDemandUnits(intents, "bakery-1", "fuel", out _),
                "fuel is uncovered — no intent, no demand signal");
            Assert.IsTrue(!SharedBusinessWeeklyProcurement.TryGetDemandUnits(intents, "mill-2", "flour", out _),
                "another buyer's flour need does not cover the bakery");
            Assert.IsTrue(!SharedBusinessWeeklyProcurement.TryGetDemandUnits(
                new List<PurchaseIntent> { intents[3] }, "bakery-1", "flour", out _),
                "a household-only flour intent does not cover the bakery");

            // Covered: the intent quantity REPLACES the formula (which
            // would give shortfall 10 + pending 0 = 10 for stock 2/12).
            Assert.AreEqual(15, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(2, 0, 12, intents, "bakery-1", "flour"));
            // Uncovered: the existing computation is unchanged.
            Assert.AreEqual(10, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(2, 0, 12, intents, "bakery-1", "fuel"),
                "shortfall 12-2 = 10, no pending buffer");
            Assert.AreEqual(12, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(2, 2, 12, intents, "bakery-1", "fuel"),
                "shortfall 10 + min(pending 2, planned 12) = 12");
            Assert.AreEqual(10, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(2, 0, 12, null, "bakery-1", "flour"),
                "a null intent list is uncovered — the formula applies");
        }

        [Test]
        public void WeeklyPass_RecurringTradeSuppliesConverterInput()
        {
            // M2 (Plan 04, D-S1 sequencing): the RESOLVER ORDER itself —
            // recurring orders + transfer agreements + crop transfers
            // before the converter — is MonoBehaviour-level and
            // source-reviewed in ResolveWeeklySharedOperations; this test
            // pins the seam that order enables: once a recurring-order
            // delivery has landed, the converter's desired units drop to
            // what the delivery did not cover, so nothing is procured a
            // second time.
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("bakery-1");
            runtime.SetCurrentCashCents(5000);
            runtime.EnsureCategoryStock("flour", 2, 100);

            // Before the delivery: shortfall 12 - 2 = 10, the converter
            // would procure 10.
            Assert.AreEqual(10, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(
                runtime.GetCategoryStock("flour").CurrentStockUnits, 0, 12, null, "bakery-1", "flour"));

            // The recurring-order delivery lands (10 units into the pool).
            Assert.AreEqual(10, runtime.AddCategoryStockUnits("flour", 10), "the delivery lands before the converter runs");
            Assert.AreEqual(12, runtime.GetCategoryStock("flour").CurrentStockUnits);

            // After the delivery: desired 0 — the converter procures
            // nothing new (Apply with desired 0 receives 0, spends 0).
            Assert.AreEqual(0, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(
                runtime.GetCategoryStock("flour").CurrentStockUnits, 0, 12, null, "bakery-1", "flour"));
            int received = SharedBusinessWeeklyProcurement.ApplyWeeklyInputProcurement(
                runtime, "flour", 0, 0, 100,
                "no recurring local supplier: flour",
                "cash-limited input procurement: flour (reserve $0.00)",
                new List<string>());
            Assert.AreEqual(0, received, "nothing double-procured");
            Assert.AreEqual(5000, runtime.CurrentCashCents, "no cash spent on units trade already supplied");
            Assert.AreEqual(12, runtime.GetCategoryStock("flour").CurrentStockUnits);

            // Pending still receipts (a reservation, not a new purchase):
            // post-trade stock is full but 4 pending units remain.
            runtime.GetCategoryStock("flour").SetPendingReorderUnits(4);
            Assert.AreEqual(4, SharedBusinessWeeklyProcurement.ResolveDesiredUnits(
                runtime.GetCategoryStock("flour").CurrentStockUnits, 4, 12, null, "bakery-1", "flour"),
                "shortfall 0 + min(pending 4, planned 12) = 4");
            int pendingReceived = SharedBusinessWeeklyProcurement.ApplyWeeklyInputProcurement(
                runtime, "flour", 4, 10, 100,
                "no recurring local supplier: flour",
                "cash-limited input procurement: flour (reserve $0.00)",
                new List<string>());
            Assert.AreEqual(4, pendingReceived, "the pending reservation is received");
            Assert.AreEqual(16, runtime.GetCategoryStock("flour").CurrentStockUnits, "12 + 4 pending, nothing conjured");
            Assert.AreEqual(4600, runtime.CurrentCashCents, "exactly the pending cost: 4 x 100");
        }

        [Test]
        public void DuplicateRegistration_CollectsTwice_WeeklyPassMustNotDoubleRegister()
        {
            // Characterization of a hazard, not a fix: registering the
            // same need twice collects two identical intents. M2's weekly
            // wiring must therefore clear + re-register (or dedupe) each
            // week, or the weekly pass will double-count demand.
            var f = new Fixture();
            f.AddSupplier("mill-2", "flour", 50, 200);
            RealDemandSource source = f.NewSource(f.Needs, null);
            var need = new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 10,
                MaxPriceCentsPerUnit = 250,
                ReasonSummary = "weekly bread production",
            };
            source.RegisterBusinessInputNeed(need);
            source.RegisterBusinessInputNeed(need);

            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);
            Assert.AreEqual(2, intents.Count, "the same need registered twice collects twice — M2 must clear + re-register weekly");
            Assert.AreEqual(intents[0].AgreedPriceCents, intents[1].AgreedPriceCents, "the duplicate is identical");
        }
    }
}
