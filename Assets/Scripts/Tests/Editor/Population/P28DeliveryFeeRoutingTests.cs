using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// P28 (A1 plan 33, Agent 3 note 131 SOUND WITH CORRECTIONS C1-C5):
    /// the delivery fee belongs to the CARRIER, never the store. When a fee
    /// is charged, the settlement posts goodsCost to the store and
    /// freightCost to the carrier's till in one conserved settlement
    /// (household-out == store-in + carrier-in). A nonzero fee may only be
    /// charged when a real carrier leg performs the delivery; until then the
    /// port's 0 stands. The fee VALUE is 0 in production (no rate invented);
    /// these tests pin the contract at a quoted fee of 250c through the fake
    /// port, the same way the FoodEconomy chain pins the cash contract.
    ///
    /// Fixture shape (60u flour at 120c over the 50u carry cap, fee quote
    /// 250c, fund 10,000c) is the FoodEconomyShoppingChainTests fixture,
    /// extended with a carrier till and the two loop seams (R47 pattern).
    ///
    /// Deviations from the plan, for the landing review (note 131's
    /// classification is corrected where the source proves otherwise):
    /// D1 — T10's "goods obligation 7200" is unreachable. SettleAtCounter
    /// caps units by live stock BEFORE creating the obligation, so with
    /// stock 55 the goods obligation is for the 55u actually sold (6600),
    /// not the 60u planned. T10 pins the reachable credit partial-fill
    /// numbers (goods obligation 6600, freight obligation 250,
    /// StoreReceivableCents 6600, CarrierReceivableCents 250); the C1
    /// truing itself (sold &lt; units in credit mode) cannot trigger through
    /// ExecuteBatch with a faithful port — SettleAtCounter re-caps units
    /// by stockNow, so sold == units always — and is verified by source
    /// review, mirroring the cash branch line for line.
    /// D2 — T2/T7 are compile-red at the base, not green-at-base pins:
    /// both assert the new result.CarrierCashInCents field, and the
    /// fixture constructs the loop through the new ctor seams. Red-first
    /// is nine compile-red (T1/T2/T4/T5/T6/T7/T8/T9/T10) plus T3
    /// runtime-red; there are no green-at-base pins in this slice.
    /// D3 — T4's "TripExecuted false" is wrong for the batch path. When
    /// the backstop refuses, ExecuteBatch keeps the need in
    /// UnbatchedNeeds and still sets TripExecuted once the trip legs ran
    /// (the shopper did travel; the need simply did not settle). T4 pins
    /// ServedNeedCount 0, UnbatchedNeeds 1, no money moved, need Open,
    /// and the backstop text in diagnostics.
    /// D4 — T8's second need cannot be flour. The need registry dedups
    /// by (household, item): CreateNeed returns the open need for the
    /// same item (HouseholdShortage.cs), so opening a second flour need
    /// returns the SAME need object and the batch holds one need, not
    /// two (ServedNeedCount 1 in the first full run). T8's second leg
    /// is salt 10u instead (same 120c price, carried, no fee); all
    /// money assertions are unchanged. Test-side only, production
    /// untouched.
    /// T9 uses the standard InTransit restore shape (task, budget,
    /// schedule and due-event DTOs restored into the fresh fixture
    /// before RehydrateAfterLoad) — the plan's re-shape clause was not
    /// needed.
    /// </summary>
    [TestFixture]
    public sealed class P28DeliveryFeeRoutingTests
    {
        // Verbatim copy of the FoodEconomyShoppingChainTests fake store port.
        private sealed class FakeStorePort : IItemLevelGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public Dictionary<string, int> ItemStock = new Dictionary<string, int>();
            public Dictionary<string, int> ItemPrices = new Dictionary<string, int>();
            public HashSet<string> ItemLevelItems = new HashSet<string>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;
            public int RecordedCashRevenue;
            public int RecordedItemCashRevenue;
            public bool OffersDeliveryValue;
            public int DeliveryFeeValue;
            public bool OffersTradeCreditValue;
            public int TradeCreditLimitValue;
            // P28 credit void: one-shot consume controls (C2, C5).
            public bool FailNextConsume;
            public int? NextConsumeLimit;
            public int ConsumeAttempts;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => OffersDeliveryValue;
            public bool OffersTradeCredit => OffersTradeCreditValue;
            public int TradeCreditLimitCents => TradeCreditLimitValue;

            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                ConsumeAttempts++;
                // P28 credit void (C5): one-shot failure — disarms after
                // firing and short-circuits BEFORE any stock mutation.
                if (FailNextConsume)
                {
                    FailNextConsume = false;
                    return false;
                }
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                // P28 credit void (C2): one-shot consume limit — caps the
                // next consume at min(requested, have, limit), then clears
                // itself, so the C1 branch can be driven with stock 100.
                if (NextConsumeLimit.HasValue)
                {
                    take = System.Math.Min(take, System.Math.Max(0, NextConsumeLimit.Value));
                    NextConsumeLimit = null;
                }
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
                RecordedCashRevenue += System.Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "nothing to receive";
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                int target = CategoryTargetStockUnits(categoryId);
                return target > 0 ? System.Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => System.Math.Max(0, DeliveryFeeValue);

            public bool UsesItemLevelInventory(string itemId) => ItemLevelItems.Contains(itemId);

            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int s) ? s : 0;

            public int ItemPricePerUnitCents(string itemId) => ItemPrices.TryGetValue(itemId, out int p) ? p : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = ItemStockUnits(itemId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                ItemStock[itemId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
                RecordedItemCashRevenue += System.Math.Max(0, revenueCents);
            }
        }

        /// <summary>How the carrier seams are bound for one fixture.</summary>
        private enum CarrierBinding
        {
            /// <summary>No seams at all — the production shape.</summary>
            None,
            /// <summary>Lookup returns "carrier-1"; the resolver returns the live till.</summary>
            Bound,
            /// <summary>Lookup returns "carrier-1"; the resolver returns null (no till).</summary>
            Unresolvable,
        }

        private sealed class Fixture
        {
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
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry EntityIds;
            public HouseholdShoppingLoop Loop;
            public ShoppingTripDriver Driver;
            public SimpleDueEventScheduler DueEvents;
            public FakeStorePort Port;
            public BusinessRuntimeState Carrier;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (day-of-week index 0)

            public static Fixture Create(bool withKnowledge = true, CarrierBinding carrier = CarrierBinding.None)
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                {
                    id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus
                });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0
                });
                Assert.IsNull(regProblem, regProblem);

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                f.Journeys.AddEdge("home", "store", 2.0f, "river road");

                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();
                f.DueEvents = new SimpleDueEventScheduler();

                f.Port = new FakeStorePort();
                f.Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                    StoreUnitsPerItemUnit = 1,
                });
                f.Port.Stock["staple_food"] = 100;
                f.Port.Prices["staple_food"] = 120;
                f.Port.Targets["staple_food"] = 100;

                f.Directory = new SupplierDirectory();
                GeneralStoreSupplierAdapter.RegisterIn(f.Directory, f.Port);
                if (withKnowledge)
                {
                    f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                }

                // C3: the carrier till is built the FreightCompanyTests way —
                // BindBusinessIdentity, never a BusinessId setter (the id is
                // getter-only). The till starts empty so every cent in it
                // after a test is a fee this slice posted.
                f.Carrier = new BusinessRuntimeState();
                f.Carrier.BindBusinessIdentity("carrier-1");
                f.Carrier.SetCurrentCashCents(0);

                if (carrier == CarrierBinding.None)
                {
                    f.Loop = new HouseholdShoppingLoop(
                        f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                        f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                        f.Obligations, f.EntityIds);
                }
                else
                {
                    f.Loop = new HouseholdShoppingLoop(
                        f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                        f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                        f.Obligations, f.EntityIds,
                        carrierBusinessIdForSupplier: supplier =>
                            string.Equals(supplier.SupplierBusinessId, "store-1", StringComparison.Ordinal)
                                ? "carrier-1"
                                : string.Empty,
                        carrierRuntimeResolver: id => carrier == CarrierBinding.Bound
                            && string.Equals(id, "carrier-1", StringComparison.Ordinal)
                                ? f.Carrier
                                : null);
                }
                f.Driver = new ShoppingTripDriver(f.Loop);

                f.PersonLocations[1] = "home";
                return f;
            }

            public ShoppingLoopOptions Options()
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                    SetPersonLocation = (pid, loc) => fixture.PersonLocations[pid] = loc,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    Diagnostics = fixture.Diagnostics,
                };
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.ReasonSummary = "test need";
                return need;
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();
            public int StoreStock() => Port.CategoryStockUnits("staple_food");
            public int StoreCash() => Port.CashBalance;
        }

        private static IGoodsSupplier FindSupplierByBusinessId(Fixture f, string businessId)
        {
            foreach (IGoodsSupplier supplier in f.Directory.All)
            {
                if (supplier != null && string.Equals(supplier.SupplierBusinessId, businessId, StringComparison.Ordinal))
                {
                    return supplier;
                }
            }

            return null;
        }

        private static Fixture DeliveryFixture(CarrierBinding carrier, int feeCents)
        {
            Fixture f = Fixture.Create(carrier: carrier);
            f.Port.OffersDeliveryValue = true;
            f.Port.DeliveryFeeValue = feeCents;
            return f;
        }

        private static ShoppingTripResult SingleResult(HouseholdShoppingLoop.BatchShoppingResult batch)
        {
            Assert.AreEqual(1, batch.NeedResults.Count, "one need must produce one result");
            return batch.NeedResults[0];
        }

        [Test]
        public void P28_T1_CashConservedAcrossThreeTills()
        {
            // The ruling's pin: one settlement, three tills, every cent kept.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            int storeCashBefore = f.StoreCash();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t1");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(60, batch.UnitsAcquiredTotal);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(10000 - 7450, f.Balance(), "Household debits goods + freight in one debit.");
            Assert.AreEqual(storeCashBefore + 7200, f.StoreCash(), "Store posts goods-only, never the fee.");
            Assert.AreEqual(7200, f.Port.RecordedCashRevenue);
            Assert.AreEqual(250, f.Carrier.CurrentCashCents, "Carrier till gains exactly the fee.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(7450, result.BuyerOutflowCents);
            Assert.AreEqual(7200, result.StoreCashInCents);
            Assert.AreEqual(0, result.StoreReceivableCents);
            Assert.AreEqual(250, result.CarrierCashInCents);
            Assert.AreEqual(0, result.CarrierReceivableCents);
            Assert.AreEqual(result.BuyerOutflowCents,
                result.StoreCashInCents + result.StoreReceivableCents + result.CarrierCashInCents + result.CarrierReceivableCents,
                "Conservation: buyer out == store in + carrier in.");
        }

        [Test]
        public void P28_T2_FeeZeroPin()
        {
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 0);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            int storeCashBefore = f.StoreCash();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t2");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10000 - 7200, f.Balance(), "A fee of 0 changes nothing: the household pays goods-only.");
            Assert.AreEqual(storeCashBefore + 7200, f.StoreCash());
            Assert.AreEqual(7200, f.Port.RecordedCashRevenue);
            Assert.AreEqual(0, f.Carrier.CurrentCashCents, "No fee, no carrier credit.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(7200, result.StoreCashInCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(0, result.CarrierReceivableCents);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void P28_T3_NoCarrierNoFee()
        {
            // The leak, demonstrated: with no carrier bound, delivery is
            // offered but the fee is forced to 0, so no fee may be charged
            // — the household pays goods-only even though the port quotes
            // 250c. At the base this test FAILS (household -7450, store
            // +7450): the fee leaked to the store. Runtime red; T1/T4/T5/
            // T6/T7/T8/T9/T10 are compile-red at the base instead (D2).
            Fixture f = DeliveryFixture(CarrierBinding.None, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            int storeCashBefore = f.StoreCash();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t3");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10000 - 7200, f.Balance(), "With no carrier bound, no fee may be charged.");
            Assert.AreEqual(storeCashBefore + 7200, f.StoreCash(), "The store must not receive the fee.");
            Assert.AreEqual(7200, f.Port.RecordedCashRevenue);
            Assert.AreEqual(0, f.Carrier.CurrentCashCents, "No carrier is bound, so no carrier is paid.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(7200, result.StoreCashInCents);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void P28_T4_UnresolvableCarrierRefusesBeforeDebit()
        {
            // The backstop: a quoted fee with no resolvable carrier till
            // refuses the WHOLE settlement before any debit. The need stays
            // open; no money moves; no stock moves.
            Fixture f = DeliveryFixture(CarrierBinding.Unresolvable, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t4");

            // D3: TripExecuted is true — the shopper did travel; the need
            // simply did not settle. The refusal is visible as
            // ServedNeedCount 0 with the need in UnbatchedNeeds.
            Assert.IsTrue(batch.TripExecuted, "The batch trip ran; the need simply did not settle.");
            Assert.AreEqual(0, batch.ServedNeedCount, "A refused settlement serves no need.");
            Assert.AreEqual(1, batch.UnbatchedNeeds.Count, "The refused need stays unbatched, not settled.");
            Assert.AreEqual(10000, f.Balance(), "A refused settlement must not move buyer money.");
            Assert.AreEqual(100, f.StoreStock(), "A refused settlement must not move seller stock.");
            Assert.AreEqual(0, f.Port.RecordedCashRevenue);
            Assert.AreEqual(0, f.Carrier.CurrentCashCents);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The unmet need stays open for a later day.");
            string diag = string.Join("\n", f.Diagnostics);
            Assert.IsTrue(diag.IndexOf("no carrier till is bound", StringComparison.Ordinal) >= 0,
                "The refusal must name the missing carrier till in diagnostics: " + diag);
        }

        [Test]
        public void P28_T5_CreditSplitsIntoTwoObligations()
        {
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(100);
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 10000;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t5");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(60, batch.UnitsAcquiredTotal);
            Assert.AreEqual(100, f.Balance(), "Credit mode moves no cash.");
            Assert.AreEqual(0, f.Port.RecordedCashRevenue, "Credit mode posts no store cash.");
            Assert.AreEqual(0, f.Carrier.CurrentCashCents, "Credit mode posts a carrier receivable, not carrier cash.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(7200, result.StoreReceivableCents, "The store is owed goods-only.");
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(250, result.CarrierReceivableCents, "The carrier is owed the fee.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FreightObligationId));

            // Both obligations are REAL: they live in the shared authority.
            FinancialObligation goods = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(goods);
            Assert.AreEqual(FinancialObligationKind.TradeCredit, goods.Kind);
            Assert.AreEqual("household:7", goods.Debtor);
            Assert.AreEqual("store-1", goods.Creditor);
            Assert.AreEqual(7200, goods.OutstandingPrincipalCents);
            FinancialObligation freight = f.Obligations.Find(result.FreightObligationId);
            Assert.IsNotNull(freight);
            Assert.AreEqual(FinancialObligationKind.TradeCredit, freight.Kind);
            Assert.AreEqual("household:7", freight.Debtor);
            Assert.AreEqual("carrier-1", freight.Creditor);
            Assert.AreEqual(250, freight.OutstandingPrincipalCents);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void P28_T6_PartialFillKeepsFreight()
        {
            // Stock 55u of 60u needed: the store sells 55u, the household
            // is refunded the 5u goods shortfall, and the freight stands —
            // the carrier still performed the delivery.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            f.Port.Stock["staple_food"] = 55;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            int storeCashBefore = f.StoreCash();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t6");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(55, batch.UnitsAcquiredTotal);
            Assert.AreEqual(10000 - 6850, f.Balance(), "Household pays 55u goods + the full fee.");
            Assert.AreEqual(storeCashBefore + 6600, f.StoreCash(), "Store posts the 55u actually sold.");
            Assert.AreEqual(6600, f.Port.RecordedCashRevenue);
            Assert.AreEqual(250, f.Carrier.CurrentCashCents, "Freight stands on a partial fill.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(6850, result.BuyerOutflowCents);
            Assert.AreEqual(6600, result.StoreCashInCents);
            Assert.AreEqual(250, result.CarrierCashInCents);
            Assert.AreEqual(5, result.UnitsStillNeeded);
        }

        [Test]
        public void P28_T7_CarryFitsNoDelivery()
        {
            // 10u fits the 50u carry cap, so no delivery is arranged even
            // though the port offers it and a carrier is bound.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            int storeCashBefore = f.StoreCash();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t7");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10000 - 1200, f.Balance());
            Assert.AreEqual(storeCashBefore + 1200, f.StoreCash());
            Assert.AreEqual(1200, f.Port.RecordedCashRevenue);
            Assert.AreEqual(0, f.Carrier.CurrentCashCents, "No delivery, no fee, no carrier credit.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(1200, result.StoreCashInCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void P28_T8_BatchConservation()
        {
            // Two needs in one batch: 60u flour delivered (fee 250) +
            // 10u salt carried (no fee). Conservation holds across the
            // whole batch. The second need MUST be a different item: the
            // need registry dedups by (household, item), so a second
            // flour need returns the same need object and the batch
            // would hold one need, not two (D4).
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "household_goods",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.SaltId },
                StoreUnitsPerItemUnit = 1,
            });
            f.Port.Stock["household_goods"] = 100;
            f.Port.Prices["household_goods"] = 120;
            f.Port.Targets["household_goods"] = 100;
            HouseholdPurchasingNeed need60 = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            HouseholdPurchasingNeed needSalt = f.OpenNeed(HouseholdItemCatalog.SaltId, 10);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need60, needSalt }, f.Day, f.Options(), "p28-t8");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(2, batch.ServedNeedCount);
            Assert.AreEqual(70, batch.UnitsAcquiredTotal);
            int buyerOut = 0, storeIn = 0, carrierIn = 0;
            foreach (ShoppingTripResult r in batch.NeedResults)
            {
                buyerOut += r.BuyerOutflowCents;
                storeIn += r.StoreCashInCents + r.StoreReceivableCents;
                carrierIn += r.CarrierCashInCents + r.CarrierReceivableCents;
            }

            Assert.AreEqual(8650, buyerOut, "60u + fee, then 10u carried.");
            Assert.AreEqual(8400, storeIn, "Store posts 7200 + 1200 goods-only.");
            Assert.AreEqual(250, carrierIn, "Carrier posts the one fee.");
            Assert.AreEqual(buyerOut, storeIn + carrierIn, "Batch conservation: buyer out == store in + carrier in.");
            Assert.AreEqual(10000 - 8650, f.Balance());
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need60.Status);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, needSalt.Status);
        }

        [Test]
        public void P28_T9_DtoRoundTripKeepsCarrierBinding()
        {
            // The trip DTO carries the carrier binding through save/load, so
            // a restored trip can still refuse a fee with no carrier
            // instead of charging it to the store.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            f.Driver.PlanForHousehold(f.HouseholdId, f.Needs, f.Day, f.Options(), f.Diagnostics);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count, "one trip must be planned");
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            Assert.IsNotNull(trip.Candidate, "the planned trip must carry its candidate");
            Assert.AreEqual("carrier-1", trip.Candidate.CarrierBusinessInstanceId);
            Assert.IsTrue(trip.Candidate.UseDelivery);
            Assert.AreEqual(250, trip.Candidate.DeliveryFeeCents);

            // Save: the driver DTO plus the task, budget, schedule and
            // due-event DTOs. RehydrateAfterLoad drops the trip as dead
            // when its leg tasks cannot be found in the fresh authority,
            // so the tasks must be restored first (the
            // InTransitSaveLoadTests
            // DriverPath_MidTripSaveLoad_RestoresCarriedBatch precedent).
            List<PlannedShoppingTripSaveDto> dto = f.Driver.CaptureSaveDto();
            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            Assert.AreEqual(1, dto.Count);
            Assert.AreEqual("carrier-1", dto[0].CarrierBusinessInstanceId, "the DTO must carry the carrier binding");
            Assert.AreEqual(250, dto[0].DeliveryFeeCents);
            Assert.IsTrue(dto[0].UseDelivery);

            Fixture g = DeliveryFixture(CarrierBinding.Bound, 250);
            g.Fund(10000);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, g.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(dto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Driver.RehydrateAfterLoad(g.Tasks,
                id => FindSupplierByBusinessId(g, id),
                g.Day, g.Day * 86400f + 610 * 60f, g.DueEvents, g.Diagnostics);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count, "one trip must restore");
            PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
            Assert.IsNotNull(restored.Candidate, "the restored trip must carry its candidate");
            Assert.AreEqual("carrier-1", restored.Candidate.CarrierBusinessInstanceId,
                "the carrier binding must survive the round trip");
            Assert.IsTrue(restored.Candidate.UseDelivery);
            Assert.AreEqual(250, restored.Candidate.DeliveryFeeCents);
        }

        [Test]
        public void P28_T10_CreditPartialFill()
        {
            // C1: credit partial fill — stock 55u of 60u, fee 250. The
            // store's receivable trues to the goods actually sold (6600)
            // and the carrier's receivable stands (250). D1: the goods
            // obligation is 6600, not the plan's 7200 — SettleAtCounter
            // caps units by live stock BEFORE creating the obligation, so
            // with stock 55 the obligation is for the 55u actually sold.
            // The C1 truing (sold &lt; units) cannot trigger through
            // ExecuteBatch with a faithful port and is verified by source
            // review; this test pins the reachable credit partial-fill
            // conservation numbers.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(100);
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 10000;
            f.Port.Stock["staple_food"] = 55;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t10");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(55, batch.UnitsAcquiredTotal);
            Assert.AreEqual(100, f.Balance(), "Credit mode moves no cash.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(6600, result.StoreReceivableCents, "C1: the store receivable is the goods actually sold.");
            Assert.AreEqual(250, result.CarrierReceivableCents, "C1: the carrier receivable stands on a partial fill.");
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FreightObligationId));

            FinancialObligation goods = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(goods);
            Assert.AreEqual("store-1", goods.Creditor);
            Assert.AreEqual(6600, goods.OutstandingPrincipalCents, "D1: the goods obligation is for the units actually sold.");
            FinancialObligation freight = f.Obligations.Find(result.FreightObligationId);
            Assert.IsNotNull(freight);
            Assert.AreEqual("carrier-1", freight.Creditor);
            Assert.AreEqual(250, freight.OutstandingPrincipalCents);
        }

        [Test]
        public void P28_T11_CreditVoidCancelsBothObligations()
        {
            // The P28 credit-void wart (note 136 item 2, X3): SettleAtCounter
            // creates the two trade-credit obligations BEFORE the sale, and
            // the void branch left them standing. The void now cancels both
            // in full, so no live debt survives a zero sale.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(100);
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 10000;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            f.Port.FailNextConsume = true;

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t11");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(0, batch.ServedNeedCount);
            Assert.AreEqual(0, batch.UnitsAcquiredTotal);
            Assert.AreEqual(1, batch.UnbatchedNeeds.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The voided need stays open for a later day.");
            Assert.AreEqual(100, f.Balance(), "Credit mode moves no cash.");
            Assert.AreEqual(100, f.StoreStock(), "A voided sale moves no stock.");
            Assert.AreEqual(0, f.Port.RecordedCashRevenue);
            Assert.AreEqual(0, f.Carrier.CurrentCashCents);
            Assert.AreEqual(1, f.Port.ConsumeAttempts, "The void fires exactly one consume, on the SellItem call.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(0, result.StoreReceivableCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(0, result.CarrierReceivableCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId),
                "The void branch zeroes only the five money fields; the ids survive on the failed result.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FreightObligationId));

            FinancialObligation goods = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(goods);
            Assert.AreEqual(FinancialObligationStatus.Cancelled, goods.Status);
            Assert.AreEqual(0, goods.OutstandingPrincipalCents);
            Assert.AreEqual(0, goods.TotalOutstandingCents);
            Assert.AreEqual(7200, goods.OriginalPrincipalCents, "Cancel zeroes the remainder; the created principal stays for audit.");
            FinancialObligation freight = f.Obligations.Find(result.FreightObligationId);
            Assert.IsNotNull(freight);
            Assert.AreEqual(FinancialObligationStatus.Cancelled, freight.Status);
            Assert.AreEqual(0, freight.OutstandingPrincipalCents);
            Assert.AreEqual(0, freight.TotalOutstandingCents);
            Assert.AreEqual(250, freight.OriginalPrincipalCents);
            string diag = string.Join("\n", f.Diagnostics);
            Assert.IsTrue(diag.IndexOf("cancelled", StringComparison.OrdinalIgnoreCase) >= 0,
                "The void diagnostic must name the cancellation: " + diag);
        }

        [Test]
        public void P28_T12_CreditVoidWithoutFeeCancelsGoodsOnly()
        {
            // No fee was charged, so no freight obligation exists: the void
            // cancels the goods obligation only, and no refusal may
            // pollute the diagnostic.
            Fixture f = DeliveryFixture(CarrierBinding.None, 0);
            f.Fund(100);
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 10000;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            f.Port.FailNextConsume = true;

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t12");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(0, batch.ServedNeedCount);
            Assert.AreEqual(1, batch.UnbatchedNeeds.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(100, f.Balance(), "Credit mode moves no cash.");
            Assert.AreEqual(100, f.StoreStock(), "A voided sale moves no stock.");
            Assert.AreEqual(1, f.Port.ConsumeAttempts);
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(0, result.StoreReceivableCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(0, result.CarrierReceivableCents);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId));
            Assert.IsTrue(string.IsNullOrWhiteSpace(result.FreightObligationId),
                "No fee was charged, so no freight obligation exists and no freight cancel runs.");
            FinancialObligation goods = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(goods);
            Assert.AreEqual(FinancialObligationStatus.Cancelled, goods.Status);
            Assert.AreEqual(0, goods.OutstandingPrincipalCents);
            Assert.AreEqual(0, goods.TotalOutstandingCents);
            Assert.AreEqual(7200, goods.OriginalPrincipalCents);
            string diag = string.Join("\n", f.Diagnostics);
            Assert.IsTrue(diag.IndexOf("cancelled", StringComparison.OrdinalIgnoreCase) >= 0,
                "The void diagnostic must name the cancellation: " + diag);
            Assert.IsTrue(diag.IndexOf("Unknown financial obligation", StringComparison.Ordinal) < 0,
                "No freight cancel runs, so no unknown-id refusal may appear: " + diag);
        }

        [Test]
        public void P28_T13_CashVoidUnchanged()
        {
            // Cash mode has no obligations: the void refunds the household
            // in full and the diagnostic keeps the existing text verbatim.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            f.Port.FailNextConsume = true;

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t13");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(0, batch.ServedNeedCount);
            Assert.AreEqual(1, batch.UnbatchedNeeds.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(10000, f.Balance(), "The cash void refunds the whole purchase.");
            Assert.AreEqual(100, f.StoreStock(), "A voided sale moves no stock.");
            Assert.AreEqual(0, f.Obligations.Obligations.Count, "Cash mode creates no obligations.");
            Assert.AreEqual(1, f.Port.ConsumeAttempts);
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(0, result.StoreReceivableCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(0, result.CarrierReceivableCents);
            string diag = string.Join("\n", f.Diagnostics);
            Assert.IsTrue(diag.IndexOf("refunded, manual reconciliation required.", StringComparison.Ordinal) >= 0,
                "Cash mode keeps the existing void text verbatim: " + diag);
        }

        [Test]
        public void P28_T14_CreditPartialFillStillTruesOnlyResult()
        {
            // C1 regression pin, corrected per note 138 C2: the C1 branch
            // (sold < units in credit mode) cannot trigger through
            // ExecuteBatch with a faithful port, because the counter check
            // re-caps units by stock. The one-shot consume limit drives
            // it: stock 100 keeps units at 60 and the obligations at
            // 7200/250, then SellItem consumes only 55.
            Fixture f = DeliveryFixture(CarrierBinding.Bound, 250);
            f.Fund(100);
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 10000;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);
            f.Port.NextConsumeLimit = 55;

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "p28-t14");

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(55, batch.UnitsAcquiredTotal);
            Assert.AreEqual(100, f.Balance(), "Credit mode moves no cash.");
            ShoppingTripResult result = SingleResult(batch);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(6600, result.StoreReceivableCents, "C1: the store receivable is the goods actually sold.");
            Assert.AreEqual(250, result.CarrierReceivableCents, "C1: the carrier receivable stands on a partial fill.");
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(0, result.CarrierCashInCents);
            Assert.AreEqual(5, result.UnitsStillNeeded);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.FreightObligationId));

            FinancialObligation goods = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(goods);
            Assert.AreEqual(7200, goods.OutstandingPrincipalCents, "C1: the obligation principal stays as created, only the result is trued.");
            FinancialObligation freight = f.Obligations.Find(result.FreightObligationId);
            Assert.IsNotNull(freight);
            Assert.AreEqual(250, freight.OutstandingPrincipalCents);
            string diag = string.Join("\n", f.Diagnostics);
            Assert.IsTrue(diag.IndexOf("INCONSISTENCY", StringComparison.Ordinal) >= 0,
                "The C1 truing must still be loud in diagnostics: " + diag);
        }

        [Test]
        public void P28_T15_CancelUnknownIdRefuses()
        {
            // Authority level, no loop: an unknown id refuses with the
            // AccrueInterest wording and throws nothing.
            var authority = new FinancialObligationAuthority();
            string refusal = authority.CancelObligation("no-such-id", "test");
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.IndexOf("Unknown financial obligation", StringComparison.Ordinal) >= 0,
                "The refusal must name the unknown id: " + refusal);
        }

        [Test]
        public void P28_T16_CancelSatisfiedRefuses()
        {
            // Authority level: a paid obligation is a settled fact and
            // must never be voided away.
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = authority.Create(
                ids, FinancialObligationKind.TradeCredit, "household:7", "store-1", 7200, 0,
                "trade credit, net 30", "test obligation");
            Assert.IsNotNull(obligation);
            FinancialPaymentRecord payment = authority.ApplyPayment(
                obligation.ObligationId, 7200, 0, "household:7", "store-1");
            Assert.IsNotNull(payment, "The obligation must settle in full before the cancel is attempted.");
            Assert.AreEqual(FinancialObligationStatus.Satisfied, obligation.Status);
            string refusal = authority.CancelObligation(obligation.ObligationId, "test");
            Assert.IsNotNull(refusal, "Cancelling a paid obligation must refuse — a settled fact is never voided away.");
            Assert.AreEqual(FinancialObligationStatus.Satisfied, obligation.Status);
            Assert.AreEqual(7200, obligation.OriginalPrincipalCents);
        }

        [Test]
        public void P28_T17_CancelIsIdempotentAndInert()
        {
            // Authority level: cancel is an idempotent no-op the second
            // time (first reason wins) and the cancelled obligation is
            // inert to the exactly-once paths. C6: the exactly-once calls
            // use positive amounts, so any refusal is the Settled refusal,
            // not the positivity refusal.
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = authority.Create(
                ids, FinancialObligationKind.TradeCredit, "household:7", "store-1", 7200, 0,
                "trade credit, net 30", "test obligation");
            Assert.IsNotNull(obligation);
            Assert.IsNull(authority.CancelObligation(obligation.ObligationId, "first reason"));
            Assert.AreEqual(FinancialObligationStatus.Cancelled, obligation.Status);
            Assert.AreEqual(0, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(0, obligation.TotalOutstandingCents);
            Assert.AreEqual(7200, obligation.OriginalPrincipalCents);
            Assert.AreEqual("first reason", obligation.CancellationReason);
            Assert.IsNull(authority.CancelObligation(obligation.ObligationId, "second reason"));
            Assert.AreEqual("first reason", obligation.CancellationReason, "A second cancel keeps the first reason.");
            Assert.IsNull(authority.ApplyPayment(obligation.ObligationId, 100, 0, "household:7", "store-1"),
                "A cancelled obligation is inert to payment.");
            Assert.IsNotNull(authority.AccrueInterest(obligation.ObligationId, 100),
                "A cancelled obligation is inert to accrual.");
        }
    }
}
