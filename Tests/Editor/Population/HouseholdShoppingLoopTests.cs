using System.Collections.Generic;
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
    /// Phase C: the complete autonomous shopping loop (the end-to-end proof).
    /// Every test runs the real 17-step chain — real Person, real Journey/Task
    /// legs, real store stock, real ledger — and asserts the anti-cheat
    /// invariants: no teleporting goods, no faked sales, conservation of
    /// every cent, loud double-booking refusal.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdShoppingLoopTests
    {
        private sealed class FakeStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;
            public int RecordedCashRevenue;
            public int RecordedReceivable;
            public List<string> ReceivableObligationIds = new List<string>();
            public bool OffersDeliveryValue;
            public int DeliveryFeeValue;
            public bool OffersTradeCreditValue;
            public int TradeCreditLimitValue;

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
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
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

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                RecordedReceivable += System.Math.Max(0, amountCents);
                ReceivableObligationIds.Add(obligationId ?? string.Empty);
            }

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
        }

        private sealed class FakeUpstreamSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "up-1";
            public string SupplierName { get; set; } = "Wholesaler";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int LeadDays = 2;

            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => LeadDays;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                if (shipped > 0) Stock[categoryId] = have - shipped;
                return shipped;
            }
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
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (DayOfWeekIndex 0)

            public static Fixture Create(bool withEdge = true, bool includeChild = true, bool wipeKnowledge = false)
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                var adult = new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus };
                f.Population.people.Add(adult);
                var household = new HouseholdState { id = 7 };
                f.Population.households.Add(household);
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0 });
                Assert.IsNull(regProblem, regProblem);
                if (includeChild)
                {
                    var child = new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 };
                    f.Population.people.Add(child);
                    regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = 7, StartDayIndex = 0 });
                    Assert.IsNull(regProblem, regProblem);
                }

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                if (withEdge)
                {
                    f.Journeys.AddEdge("home", "store", 2.0f, "river road");
                }

                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();

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
                if (!wipeKnowledge)
                {
                    f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                }

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);

                f.PersonLocations[1] = "home";
                f.PersonLocations[2] = "home";
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

        // ---------- the full 17-step loop ----------

        [Test]
        public void ExecuteNeed_FullLoop_CompletesAllSeventeenStepsWithConservation()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            int storeCashBefore = f.StoreCash();
            int storeStockBefore = f.StoreStock();

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(1, result.ShopperPersonId);
            Assert.AreEqual(10, result.UnitsAcquired);
            Assert.AreEqual(HouseholdItemCatalog.FlourId, result.ItemId);
            Assert.AreEqual("General Store", result.Counterparty);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ReceiptId));

            // Step 11/12/13 conservation: buyer out == store cash in, every cent.
            Assert.AreEqual(1200, result.BuyerOutflowCents);
            Assert.AreEqual(1200, result.StoreCashInCents);
            Assert.AreEqual(0, result.StoreReceivableCents);
            Assert.AreEqual(8800, f.Balance());
            Assert.AreEqual(storeCashBefore + 1200, f.StoreCash());

            // Step 13: real store stock decreased.
            Assert.AreEqual(storeStockBefore - 10, f.StoreStock());

            // Step 16: real lots with provenance, only after physical receipt.
            HouseholdInventory inventory = f.Inventories.GetOrCreate(7);
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(1, inventory.Lots.Count);
            StringAssert.Contains("General Store", inventory.Lots[0].SourceLabel);
            StringAssert.Contains(result.ReceiptId, inventory.Lots[0].SourceLabel);

            // Need fulfilled.
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(0, result.UnitsStillNeeded);

            // §26: the event trail covers the loop.
            var kinds = new HashSet<ShoppingEventKind>();
            foreach (ShoppingDiagnosticEvent e in result.Events) kinds.Add(e.Kind);
            foreach (ShoppingEventKind required in new[]
            {
                ShoppingEventKind.ShopperAssignment, ShoppingEventKind.Decision,
                ShoppingEventKind.JourneyLeg, ShoppingEventKind.Settlement,
                ShoppingEventKind.RetailPurchase, ShoppingEventKind.Custody,
                ShoppingEventKind.Receipt,
            })
            {
                Assert.IsTrue(kinds.Contains(required), $"missing event kind {required}");
            }

            // Event sequences are ordered.
            for (int i = 1; i < result.Events.Count; i++)
            {
                Assert.Greater(result.Events[i].EventSequence, result.Events[i - 1].EventSequence);
            }

            // Person time: the shopper is home, schedule released, budget spent.
            Assert.AreEqual(PopulationScheduleState.AtHome, f.Population.GetPerson(1).scheduleState);
            Assert.AreEqual(0, f.Tracker.ActiveFor(1, f.Day).Count);
            Assert.AreEqual("home", f.PersonLocations[1]);
            Assert.IsTrue(f.Loop.TripHistory.Count >= 1);
        }

        [Test]
        public void ExecuteNeed_MidTrip_HouseholdInventoryUnchangedUntilPhysicalReceipt()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            int unitsAtCustody = -1;
            f.Loop.MidTripObserver = (trip, batch) =>
            {
                // Goods are in the shopper's custody — the household must NOT
                // have them yet. This is the no-teleport invariant.
                unitsAtCustody = f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId);
                Assert.AreEqual(GoodsCustodyState.InHand, batch.Custody);
                Assert.AreEqual(10, batch.Units);
            };

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(0, unitsAtCustody, "household inventory changed before physical receipt — teleport!");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
        }

        [Test]
        public void ExecuteNeed_AfterReceipt_HouseholdKeepsConsumingPurchasedGoods()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsTrue(result.Success, result.FailureReason);

            // Step 17: the Phase B meal loop consumes the purchased lots.
            var mealLog = new HouseholdMealLogRegistry();
            var mealService = new HouseholdMealService(mealLog);
            MealDayResult meals = mealService.ServeHouseholdDay(
                f.Population.GetHousehold(7),
                new List<int> { 1, 2 },
                f.Inventories.GetOrCreate(7),
                MealSchedulingPolicy.Default,
                f.Day,
                null);

            Assert.Greater(meals.MealsServed, 0, "no meals served from purchased goods");
            Assert.Less(f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId), 10);
        }

        // ---------- C4 failure modes ----------

        [Test]
        public void ExecuteNeed_InsufficientFunds_NoSaleNeedStaysOpen()
        {
            Fixture f = Fixture.Create();
            f.Fund(100); // 10u flour @ 120c = 1200c
            f.Port.OffersTradeCreditValue = false;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            int storeCashBefore = f.StoreCash();
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            // Insufficient funds rejects the candidate at evaluation: the
            // household cannot afford the trip, so no sale is attempted.
            Assert.IsTrue(result.RejectedAlternatives.Count > 0);
            StringAssert.Contains("unaffordable", result.RejectedAlternatives[0].Reason);
            StringAssert.Contains("100c", result.RejectedAlternatives[0].Reason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(100, f.Balance(), "ledger moved on a failed purchase");
            Assert.AreEqual(storeCashBefore, f.StoreCash(), "store cash moved without a sale");
            Assert.AreEqual(100, f.StoreStock(), "store stock moved without a sale");
            Assert.AreEqual(0, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        [Test]
        public void ExecuteNeed_PartialStock_PartialFillNeedStaysOpen()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            f.Port.Stock["staple_food"] = 3; // only 3u available
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(3, result.UnitsAcquired);
            Assert.AreEqual(7, result.UnitsStillNeeded);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "need must stay open while unmet");
            Assert.AreEqual(360, result.BuyerOutflowCents);
            Assert.AreEqual(360, result.StoreCashInCents);
            Assert.AreEqual(0, f.StoreStock());
            Assert.AreEqual(3, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        [Test]
        public void ExecuteNeed_NoKnownSeller_UnavailableSellerFailure()
        {
            // Nobody knows the store: knowledge wiped at fixture build.
            Fixture f = Fixture.Create(wipeKnowledge: true);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            StringAssert.Contains("known seller", result.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(10000, f.Balance());
        }

        [Test]
        public void ExecuteNeed_StoreClosed_ReschedulesWithoutSale()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            // Sunday-only hours; day 0 is Monday.
            f.Port.Capability.Hours.OpenDayOfWeekIndices = new List<int> { 6 };
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            int storeCashBefore = f.StoreCash();
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RejectedAlternatives.Count > 0);
            StringAssert.Contains("closed", result.RejectedAlternatives[0].Reason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(storeCashBefore, f.StoreCash());
            Assert.AreEqual(100, f.StoreStock());
            // The shopper walked home; schedule released.
            Assert.AreEqual(PopulationScheduleState.AtHome, f.Population.GetPerson(1).scheduleState);
            Assert.AreEqual(0, f.Tracker.ActiveFor(1, f.Day).Count);
        }

        [Test]
        public void ExecuteNeed_UnstaffedStore_CannotTrade()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            f.Port.Capability.Staffed = false;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RejectedAlternatives.Count > 0);
            StringAssert.Contains("unstaffed", result.RejectedAlternatives[0].Reason);
            Assert.AreEqual(100, f.StoreStock(), "unstaffed store must not sell");
        }

        [Test]
        public void ExecuteNeed_OnlyChildMember_UnavailableShopper()
        {
            Fixture f = Fixture.Create(includeChild: true);
            f.Fund(10000);
            // The adult leaves: only the child remains an active member.
            // The child knows the store (same town) but is too young to shop.
            f.Membership.EndMembership(1, 0, "test: adult departs");
            f.Knowledge.LearnSupplier(2, "store-1", "lives in the same town", 0);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            StringAssert.Contains("shopper", result.FailureReason.ToLower());
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(0, f.Tracker.ActiveFor(2, f.Day).Count);
        }

        [Test]
        public void ExecuteNeed_BlockedJourney_NoViableSeller()
        {
            Fixture f = Fixture.Create(withEdge: false); // no road home→store
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RejectedAlternatives.Count > 0);
            StringAssert.Contains("journey", result.RejectedAlternatives[0].Reason.ToLower());
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(100, f.StoreStock());
        }

        [Test]
        public void ExecuteNeed_UnaffordableFreight_CandidateRejected()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            f.Port.OffersDeliveryValue = true;
            f.Port.DeliveryFeeValue = 9000; // freight alone breaks the budget
            f.Port.OffersTradeCreditValue = false;
            // 60u exceeds the 50u carry capacity → delivery is the only way.
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RejectedAlternatives.Count > 0);
            StringAssert.Contains("freight", result.RejectedAlternatives[0].Reason.ToLower());
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(100, f.StoreStock());
        }

        [Test]
        public void ExecuteNeed_DeliveryAffordable_ArrangedDeliveryExecutes()
        {
            Fixture f = Fixture.Create();
            f.Fund(20000);
            f.Port.OffersDeliveryValue = true;
            f.Port.DeliveryFeeValue = 200;
            f.Port.Stock["staple_food"] = 200;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 60);

            GoodsCustodyState custodyAtTrip = GoodsCustodyState.InHand;
            f.Loop.MidTripObserver = (trip, batch) => { custodyAtTrip = batch.Custody; };

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(GoodsCustodyState.ArrangedDelivery, custodyAtTrip);
            // 60u * 120c + 200c freight.
            Assert.AreEqual(7400, result.BuyerOutflowCents);
            Assert.AreEqual(7400, result.StoreCashInCents);
            Assert.AreEqual(60, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            bool sawDelivery = false;
            foreach (ShoppingDiagnosticEvent e in result.Events)
            {
                if (e.Kind == ShoppingEventKind.Delivery) sawDelivery = true;
            }

            Assert.IsTrue(sawDelivery, "no delivery event recorded");
        }

        [Test]
        public void ExecuteNeed_TradeCredit_CreatesRealObligationNoInformalDebt()
        {
            Fixture f = Fixture.Create();
            // No cash at all — but the store offers documented trade credit.
            f.Port.OffersTradeCreditValue = true;
            f.Port.TradeCreditLimitValue = 5000;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            int storeCashBefore = f.StoreCash();
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(0, result.BuyerOutflowCents);
            Assert.AreEqual(0, result.StoreCashInCents);
            Assert.AreEqual(1200, result.StoreReceivableCents);
            Assert.AreEqual(storeCashBefore, f.StoreCash(), "credit sale must not add store cash");
            Assert.AreEqual(1200, f.Port.RecordedReceivable);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.ObligationId));

            // The obligation is REAL: it lives in the shared authority.
            FinancialObligation obligation = f.Obligations.Find(result.ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(FinancialObligationKind.TradeCredit, obligation.Kind);
            Assert.AreEqual("household:7", obligation.Debtor);
            Assert.AreEqual("store-1", obligation.Creditor);
            Assert.AreEqual(1200, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(90, f.StoreStock(), "store stock still decreases on a credit sale");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        // ---------- C2: person time is authoritative ----------

        [Test]
        public void ExecuteNeed_DoubleBookedShopper_RefusedLoudly()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            // P1 is already working the mill during the trip window.
            string existing = f.Tracker.TryReserve(1, PersonActivityKind.Work, f.Day, 600, 180, "mill work");
            Assert.IsNull(existing);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            var options = f.Options();
            options.ShopperPersonIdOverride = 1;
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, options);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("already", result.FailureReason);
            // The work reservation survives; no shopping reservation was added.
            Assert.AreEqual(1, f.Tracker.ActiveFor(1, f.Day).Count);
            Assert.AreEqual(PersonActivityKind.Work, f.Tracker.ActiveFor(1, f.Day)[0].Activity);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(100, f.StoreStock());
        }

        [Test]
        public void PersonScheduleTracker_OverlappingWindows_Refused()
        {
            var tracker = new PersonScheduleTracker();
            Assert.IsNull(tracker.TryReserve(1, PersonActivityKind.Work, 0, 480, 240, "morning work"));
            string refusal = tracker.TryReserve(1, PersonActivityKind.Shopping, 0, 600, 120, "shopping");
            Assert.IsNotNull(refusal);
            StringAssert.Contains("already", refusal);
            // Sequential windows are fine.
            Assert.IsNull(tracker.TryReserve(1, PersonActivityKind.Shopping, 0, 720, 120, "afternoon shopping"));
            // Different person, same window: fine.
            Assert.IsNull(tracker.TryReserve(2, PersonActivityKind.Shopping, 0, 600, 120, "shopping"));
        }

        [Test]
        public void PersonScheduleTracker_DescribeActivity_AnswersWhatPersonIsDoing()
        {
            var tracker = new PersonScheduleTracker();
            tracker.TryReserve(1, PersonActivityKind.Shopping, 3, 600, 120, "flour run");
            StringAssert.Contains("Shopping", tracker.DescribeActivity(1, 3, 630));
            StringAssert.Contains("no scheduled activity", tracker.DescribeActivity(1, 3, 900));
        }

        // ---------- C5: restocking ----------

        [Test]
        public void StoreRestock_LowStock_OrdersShipsReceivesWithProvenance()
        {
            var port = new FakeStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
            });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.CashBalance = 50000;

            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 1000;
            upstream.Quotes["staple_food"] = 80;

            var restock = new StoreRestockService();
            restock.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.35f);

            Assert.AreEqual(1, restock.Orders.Count);
            StoreProcurementOrder order = restock.Orders[0];
            Assert.AreEqual(90, order.UnitsOrdered);
            Assert.AreEqual(80, order.UnitCostCents);
            Assert.AreEqual(StoreProcurementStage.Ordered, order.Stage);
            StringAssert.Contains("Wholesaler", order.Provenance);

            // No stock appears before the lead time elapses.
            var diag = new List<string>();
            restock.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 1, diag);
            Assert.AreEqual(10, port.CategoryStockUnits("staple_food"));

            // Day 2: the order ships from REAL upstream stock and is received.
            restock.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, diag);
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(1000 - 90, upstream.AvailableUnits("staple_food"), "upstream stock must decrease");
            Assert.AreEqual(50000 - 90 * 80, port.CashBalance, "store pays real cash on receipt");
            Assert.AreEqual(1, restock.Deliveries.Count);
            StringAssert.Contains(order.OrderId, restock.Deliveries[0].Provenance);
            StringAssert.Contains(restock.Deliveries[0].DeliveryId, restock.Deliveries[0].Provenance);
        }

        [Test]
        public void StoreRestock_UnaffordableDelivery_WaitsWithoutInvisibleStock()
        {
            var port = new FakeStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
            });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.CashBalance = 100; // cannot afford 90u @ 80c

            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 1000;
            upstream.Quotes["staple_food"] = 80;

            var restock = new StoreRestockService();
            restock.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.35f);
            var diag = new List<string>();
            restock.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, diag);

            Assert.AreEqual(10, port.CategoryStockUnits("staple_food"), "unaffordable delivery must not book stock");
            Assert.AreEqual(100, port.CashBalance);
            Assert.AreEqual(StoreProcurementStage.InTransit, restock.Orders[0].Stage);
        }

        // ---------- demand-to-revenue shortcut audit ----------

        [Test]
        public void ShortageMonitor_CreatingNeeds_PostsNoMoneyAnywhere()
        {
            Fixture f = Fixture.Create();
            f.Fund(5000);
            int storeCashBefore = f.StoreCash();
            int ledgerEntriesBefore = f.Ledgers.GetOrCreate(7).Entries.Count;

            var monitor = new HouseholdShortageMonitor(f.Needs);
            var inventory = f.Inventories.GetOrCreate(7); // empty
            monitor.Evaluate(
                f.Population.GetHousehold(7), inventory,
                new Dictionary<string, int> { { HouseholdItemCatalog.FlourId, 2 } },
                false,
                new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true },
                0, 1000, f.Day);

            Assert.AreEqual(1, f.Needs.GetOpenNeeds(7).Count, "need should exist");
            Assert.AreEqual(ledgerEntriesBefore, f.Ledgers.GetOrCreate(7).Entries.Count, "demand created a ledger entry");
            Assert.AreEqual(storeCashBefore, f.StoreCash(), "demand added store revenue");
            Assert.AreEqual(100, f.StoreStock(), "demand moved store stock");
        }

        [Test]
        public void ExecuteNeed_FailedAttempts_NeverMoveMoneyOrGoods()
        {
            // Blocked journey: every attempt must be sterile.
            Fixture blocked = Fixture.Create(withEdge: false);
            blocked.Fund(10000);
            int entriesBefore = blocked.Ledgers.GetOrCreate(7).Entries.Count;
            int storeCashBefore = blocked.StoreCash();
            HouseholdPurchasingNeed need = blocked.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            ShoppingTripResult result = blocked.Loop.ExecuteNeed(need, blocked.Day, blocked.Options());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(entriesBefore, blocked.Ledgers.GetOrCreate(7).Entries.Count);
            Assert.AreEqual(storeCashBefore, blocked.StoreCash());
            Assert.AreEqual(100, blocked.StoreStock());
        }

        [Test]
        public void ShoppingReadModel_AnswersWhoWhatWhereWhy()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsTrue(result.Success, result.FailureReason);

            var readModel = new HouseholdShoppingReadModel(f.Loop, f.Tracker);
            StringAssert.Contains("no scheduled activity", readModel.DescribePersonActivity(1, f.Day, 900));
            Assert.AreEqual(0, readModel.PersonCustody(1).Count);
            StringAssert.Contains("General Store", readModel.TripDecisionSummary(need.NeedSequence));
            StringAssert.Contains("fulfilled", readModel.NeedStatusSummary(need).ToLower());
            Assert.Greater(readModel.PersonJourney(1).Count, 0);
        }

    }
}
