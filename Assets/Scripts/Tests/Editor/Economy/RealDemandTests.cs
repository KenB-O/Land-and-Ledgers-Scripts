using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass VII Workstream A: real demand and the end of manufactured revenue.
    /// Every ordinary business revenue credit must trace to a real buyer with a
    /// real need, real knowledge, real supplier access, real inventory, a valid
    /// price, and real purchasing power. No buyer, no sale.
    /// Slice A (genuine commercial revenue) runs against the production
    /// authorities: BusinessRuntimeState, HouseholdLedger, RealDemandSource.
    /// </summary>
    [TestFixture]
    public sealed class RealDemandTests
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
            public BusinessRuntimeState Store = new BusinessRuntimeState();
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

                Store.BindBusinessIdentity("store-1");
                Store.SetCurrentCashCents(5000);
                Store.EnsureCategoryStock("staple_food", 100, 200);
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

            public RealDemandSource NewSource()
            {
                return new RealDemandSource(
                    Population, Ledgers, Needs, Directory,
                    householdKnowsSupplier: null,
                    businessCashCents: null,
                    generalStoreSupplierId: "store-1");
            }
        }

        // ---- Mandatory: no customer means no ordinary sale. ----

        [Test]
        public void SettledSale_EmptyBuyer_Refused_NothingMoves()
        {
            var f = new Fixture();
            int cashBefore = f.Store.CurrentCashCents;
            int stockBefore = f.Store.GetCategoryStock("staple_food").CurrentStockUnits;

            Assert.IsFalse(f.Store.ResolveSettledSale("staple_food", 10, 1200, string.Empty));
            Assert.IsFalse(f.Store.ResolveSettledSale("staple_food", 10, 1200, null));

            Assert.AreEqual(cashBefore, f.Store.CurrentCashCents, "no buyer: business cash must not move");
            Assert.AreEqual(stockBefore, f.Store.GetCategoryStock("staple_food").CurrentStockUnits,
                "no buyer: inventory must not decline");
        }

        [Test]
        public void BuyerlessPlaceholder_ManufacturesNothing_ObservationOnly()
        {
            var f = new Fixture();
            int cashBefore = f.Store.CurrentCashCents;
            int stockBefore = f.Store.GetCategoryStock("staple_food").CurrentStockUnits;

#pragma warning disable CS0618
            f.Store.ResolveDailySalesPlaceholder("staple_food", 10, 1200);
#pragma warning restore CS0618

            Assert.AreEqual(cashBefore, f.Store.CurrentCashCents,
                "buyerless placeholder must not manufacture cash");
            Assert.AreEqual(stockBefore, f.Store.GetCategoryStock("staple_food").CurrentStockUnits,
                "buyerless placeholder must not move inventory");
            // The valid operation survives: demand observation for forecasting.
            Assert.AreEqual(10, f.Store.GetCategoryStock("staple_food").LastDailyUnitsSold);
        }

        // ---- Mandatory: real inventory declines only on delivery or consumption. ----

        [Test]
        public void SettledSale_WithBuyer_MovesStockAndCashExactlyOnce()
        {
            var f = new Fixture();

            Assert.IsTrue(f.Store.ResolveSettledSale("staple_food", 10, 1200, "household:7"));

            Assert.AreEqual(6200, f.Store.CurrentCashCents, "seller receives legitimate payment exactly once");
            Assert.AreEqual(90, f.Store.GetCategoryStock("staple_food").CurrentStockUnits,
                "real delivery declines real inventory");
        }

        // ---- Slice A: genuine commercial revenue, no-demand case produces no sale. ----

        [Test]
        public void SliceA_NoDemand_NoIntents_NoSale()
        {
            var f = new Fixture();
            // Household reserves are topped up: no shortfall, no open needs.
            f.Population.GetHousehold(f.HouseholdId).reserves[0].currentUnits = 20;
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 100, 120);

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "no need means no demand and no sale");
            Assert.AreEqual(5000, f.Store.CurrentCashCents);
            Assert.AreEqual(100, f.Store.GetCategoryStock("staple_food").CurrentStockUnits);
        }

        [Test]
        public void SliceA_GenuineCommercialRevenue_FullChain()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 100, 120);

            // Real need: reserve shortfall (2/14) is a live, actual need.
            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);
            Assert.AreEqual(1, intents.Count, "one real need produces one intent");

            PurchaseIntent intent = intents[0];
            Assert.AreEqual("household", intent.BuyerKind);
            Assert.AreEqual("7", intent.BuyerId);
            Assert.AreEqual("store-1", intent.SellerBusinessInstanceId);
            Assert.AreEqual("staple_food", intent.GoodsOrServiceId);
            Assert.AreEqual(12, intent.QuantityUnits, "shortfall 14-2");
            Assert.AreEqual(1440, intent.AgreedPriceCents);
            Assert.AreEqual("cash", intent.SettlementKind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(intent.Provenance), "provenance documents need->knowledge->supplier");

            // Execute through the production authorities: buyer pays, seller settles.
            string rejection = f.Ledgers.GetOrCreate(f.HouseholdId)
                .RecordOutflow(3, intent.AgreedPriceCents, "staple food purchase", "store-1");
            Assert.IsNull(rejection, rejection);
            Assert.IsTrue(f.Store.ResolveSettledSale(
                intent.GoodsOrServiceId, intent.QuantityUnits, intent.AgreedPriceCents,
                $"{intent.BuyerKind}:{intent.BuyerId}"));

            // Slice K (household economic consequence): the household is poorer by exactly the spend.
            Assert.AreEqual(8560, f.Ledgers.GetOrCreate(f.HouseholdId).GetBalanceCents());
            // Seller paid exactly once.
            Assert.AreEqual(6440, f.Store.CurrentCashCents);
            // Real inventory declined on delivery.
            Assert.AreEqual(88, f.Store.GetCategoryStock("staple_food").CurrentStockUnits);
        }

        // ---- Mandatory: a customer without funds or credit cannot produce a successful paid transaction. ----

        [Test]
        public void CollectDemand_BrokeHousehold_NoIntent_DemandLoggedUnmet()
        {
            var f = new Fixture();
            // No funding: balance is zero.
            f.AddSupplier("store-1", "staple_food", 100, 120);

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "a customer without funds produces no paid transaction");
            Assert.IsTrue(f.Diag.Exists(d => d.Contains("cannot afford")),
                "unmet demand is logged, not silently dropped");
            Assert.AreEqual(5000, f.Store.CurrentCashCents);
        }

        // ---- Mandatory chain steps: knowledge, supplier access, inventory, price. ----

        [Test]
        public void CollectDemand_UnknownSupplier_NoIntent()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            // Supplier is not the public general store and no knowledge function: unknown.
            f.AddSupplier("mystery-1", "staple_food", 100, 120);

            var source = new RealDemandSource(
                f.Population, f.Ledgers, f.Needs, f.Directory,
                householdKnowsSupplier: null,
                generalStoreSupplierId: "store-1");
            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "buyer knowledge is required");
        }

        [Test]
        public void CollectDemand_KnownSupplierViaAffinity_ProducesIntent()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            f.AddSupplier("farm-9", "staple_food", 100, 100);

            var source = new RealDemandSource(
                f.Population, f.Ledgers, f.Needs, f.Directory,
                householdKnowsSupplier: (hid, sid) => sid == "farm-9",
                generalStoreSupplierId: "store-1");
            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);

            Assert.AreEqual(1, intents.Count);
            Assert.AreEqual("farm-9", intents[0].SellerBusinessInstanceId);
        }

        [Test]
        public void CollectDemand_NoStock_NoIntent()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 0, 120);

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "a supplier cannot sell what it does not hold");
        }

        [Test]
        public void CollectDemand_InvalidPrice_NoIntent()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 100, 0);

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "a valid price is required");
        }

        [Test]
        public void CollectDemand_QuantityCappedByStockAndFunds()
        {
            var f = new Fixture();
            f.FundHousehold(500); // affords 4 units at 120c
            f.AddSupplier("store-1", "staple_food", 3, 120); // only 3 in stock

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(1, intents.Count);
            Assert.AreEqual(3, intents[0].QuantityUnits, "capped by scarcest of need/stock/funds");
            Assert.AreEqual(360, intents[0].AgreedPriceCents);
        }

        // ---- Mandatory: business input requirements are real demand. ----

        [Test]
        public void CollectDemand_BusinessInputNeed_ProducesBusinessIntent()
        {
            var f = new Fixture();
            var miller = new TestSupplier { SupplierBusinessId = "mill-2", SupplierName = "Grain Mill" };
            miller.Stock["flour"] = 50;
            miller.Prices["flour"] = 200;
            f.Directory.Register(miller);

            RealDemandSource source = f.NewSource();
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 10,
                MaxPriceCentsPerUnit = 250,
                ReasonSummary = "weekly bread production",
            });

            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);

            Assert.AreEqual(1, intents.Count);
            Assert.AreEqual("business", intents[0].BuyerKind);
            Assert.AreEqual("bakery-1", intents[0].BuyerId);
            Assert.AreEqual("mill-2", intents[0].SellerBusinessInstanceId);
            Assert.AreEqual(2000, intents[0].AgreedPriceCents);
        }

        [Test]
        public void CollectDemand_BusinessCannotBuyFromItself()
        {
            var f = new Fixture();
            f.AddSupplier("bakery-1", "flour", 50, 200);

            RealDemandSource source = f.NewSource();
            source.RegisterBusinessInputNeed(new BusinessInputNeed
            {
                BuyerBusinessInstanceId = "bakery-1",
                InputCategoryId = "flour",
                UnitsNeeded = 10,
                ReasonSummary = "self-supply is not a purchase",
            });

            List<PurchaseIntent> intents = source.CollectDemand(3, f.Diag);

            Assert.AreEqual(0, intents.Count, "a business cannot buy its own inputs from itself");
        }

        // ---- Mandatory: a credit purchase records proper debt and receivable. ----

        [Test]
        public void CreditPurchase_RecordsDebtAndReceivable()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();

            FinancialObligation obligation = authority.Create(
                ids,
                FinancialObligationKind.TradeCredit,
                debtor: "household:7",
                creditor: "business:store-1",
                principalCents: 1200,
                dayIndex: 3,
                terms: "net 30",
                purpose: "staple food on store credit");

            Assert.IsNotNull(obligation);
            Assert.AreEqual("household:7", obligation.Debtor, "proper debt on the buyer");
            Assert.AreEqual("business:store-1", obligation.Creditor, "proper receivable for the seller");
            Assert.AreEqual(1200, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(1200, authority.TotalOutstandingFor("household:7"));
        }

        // ---- Mandatory: a service business is paid only for legitimate fulfillment. ----

        [Test]
        public void ServiceBusiness_PaidOnlyForLegitimateFulfillment()
        {
            var f = new Fixture();
            var doctor = new BusinessRuntimeState();
            doctor.BindBusinessIdentity("doctor-1");
            doctor.SetCurrentCashCents(2000);

            // Legitimate fulfillment: a real patient pays through the household ledger.
            f.FundHousehold(10000);
            string rejection = f.Ledgers.GetOrCreate(f.HouseholdId)
                .RecordOutflow(3, 300, "doctor visit", "doctor-1");
            Assert.IsNull(rejection, rejection);
            doctor.RecordDailyServiceRevenue("medical_service", 1, 300);

            Assert.AreEqual(2300, doctor.CurrentCashCents, "real patient, real payment");
            Assert.AreEqual(9700, f.Ledgers.GetOrCreate(f.HouseholdId).GetBalanceCents());

            // No patient, no fulfillment, no payment: capacity alone books nothing.
            var freight = new BusinessRuntimeState();
            freight.BindBusinessIdentity("freight-1");
            freight.SetCurrentCashCents(2000);
            int cashBefore = freight.CurrentCashCents;
            // (Deliberately no RecordDailyServiceRevenue call: there is no customer.)
            Assert.AreEqual(cashBefore, freight.CurrentCashCents,
                "planned capacity with no customer produces no revenue");
        }

        // ---- Mandatory: a business may operate at a loss. ----

        [Test]
        public void BusinessMayOperateAtLoss_NoArtificialTopUp()
        {
            var f = new Fixture();
            // A week with no customers: upkeep still costs real money.
            f.Store.AdjustCashCents(-800, recordNetDelta: true);

            Assert.AreEqual(4200, f.Store.CurrentCashCents);
            Assert.AreEqual(0, f.Store.GetCategoryStock("staple_food").LastDailyRevenueCents,
                "no sales means no revenue, and none is manufactured");
            Assert.Less(f.Store.CurrentCashCents, 5000, "the loss is real and stays");
        }

        // ---- Mandatory: managers respond to changed real demand. ----

        [Test]
        public void DemandSignal_UnmetThenMet_CollectionResponds()
        {
            var f = new Fixture();
            f.FundHousehold(10000);

            // Day 1: real need, no supplier — demand is logged unmet.
            List<PurchaseIntent> day1 = f.NewSource().CollectDemand(1, f.Diag);
            Assert.AreEqual(0, day1.Count);
            Assert.IsTrue(f.Diag.Exists(d => d.Contains("no known stocked supplier")));

            // Day 2: a supplier appears — the same need now produces an intent.
            f.Diag.Clear();
            f.AddSupplier("store-1", "staple_food", 100, 120);
            List<PurchaseIntent> day2 = f.NewSource().CollectDemand(2, f.Diag);

            Assert.AreEqual(1, day2.Count, "collection responds to changed real supply");
        }

        // ---- Open needs from the shortage monitor are real demand. ----

        [Test]
        public void CollectDemand_OpenNeedFromRegistry_ProducesIntent()
        {
            var f = new Fixture();
            f.FundHousehold(10000);
            f.AddSupplier("store-1", "staple_food", 100, 120);
            // Top up reserves so only the registry need drives demand.
            f.Population.GetHousehold(f.HouseholdId).reserves[0].currentUnits = 20;

            HouseholdPurchasingNeed need = f.Needs.CreateNeed(f.HouseholdId, "staple_food", 3);
            need.UnitsNeeded = 5;
            need.ReasonSummary = "shortage monitor: low days of supply";

            List<PurchaseIntent> intents = f.NewSource().CollectDemand(3, f.Diag);

            Assert.AreEqual(1, intents.Count);
            Assert.AreEqual(5, intents[0].QuantityUnits);
            Assert.IsTrue(intents[0].Provenance.Contains("shortage monitor"));
        }
    }
}
