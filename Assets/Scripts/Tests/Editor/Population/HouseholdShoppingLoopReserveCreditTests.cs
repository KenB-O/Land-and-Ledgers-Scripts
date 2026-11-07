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
    /// Plan 08 Slice 0 (Q03, AUD-335 + AUD-266): discriminator.
    /// Ports the HouseholdShoppingLoopTests Fixture shape (same loop
    /// construction, same FakeStorePort offering flour for staple_food)
    /// into a harness-included file, because the original file is
    /// exclamation-excluded from the harness. Proves the loop receipt
    /// lands lots while the reserve ledger stays unchanged (AUD-335),
    /// and that an off-map adult is still picked to shop (AUD-266).
    /// The Slice 0 loop-test reserve assertion and deficit assertion
    /// are expected to change in Slice 1 (the repair credits the
    /// reserve, so the deficit closes); the AUD-266 assertions are
    /// expected to change in Slice 2 (the repair skips off-map
    /// members). Those rewrites are named here in advance and are not
    /// regressions.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdShoppingLoopReserveCreditTests
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

        private static bool HasStapleFoodNeed(List<HouseholdReserveNeed> needs)
        {
            foreach (HouseholdReserveNeed need in needs)
            {
                if (string.Equals(need.CategoryId, "staple_food", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static OffMapJourneyAuthority DepartFirstAdultOffMap(Fixture f, int personId)
        {
            f.Journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            f.Journeys.AddEdge("home", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(f.Journeys);
            OffMapJourneyRecord record = authority.DepartPerson(
                personId, "home", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, f.Diagnostics);
            Assert.IsNotNull(record, "DepartPerson must create the journey record: " + string.Join("; ", f.Diagnostics));
            Assert.IsTrue(authority.IsOffMapPerson(personId), $"P{personId} must read off-map while the journey is active.");
            return authority;
        }

        // ---------- Slice 0: AUD-335 discriminator (loop half) ----------

        [Test]
        public void LoopReceipt_LeavesReserveUnchangedAndDeficitOpen()
        {
            // Plan 08 Slice 0: a loop receipt lands real flour lots while
            // the staple_food reserve still reads 0, so the reserve deficit
            // stays open and the GSRM path would re-buy on the next gated
            // day. Expected to change in Slice 1: after the repair the
            // reserve reads the credited units and the deficit is zero.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            Assert.NotNull(household, "H7 must exist in the fixture population.");
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            List<HouseholdReserveNeed> needsBefore = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            Assert.Greater(target, 0, "The staple_food target must be computed from the catalog, not assumed.");
            Assert.IsTrue(HasStapleFoodNeed(needsBefore), "A 0-unit reserve must raise a staple_food need before the receipt.");

            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, result.UnitsAcquired);
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "The receipt succeeds: 5u of flour lots land in the household.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(0, reserve.currentUnits,
                "AUD-335: the loop receipt never credits the reserve, so it still reads 0.");
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(HasStapleFoodNeed(needsAfter),
                "AUD-335: the deficit stays open after the loop receipt, so the GSRM path would re-buy.");
            Assert.AreEqual(target, reserve.targetUnits - reserve.currentUnits,
                "AUD-335: the deficit still equals the full target.");
        }

        [Test]
        public void LoopReceipt_SecondReceiptStillLeavesReserveUnchanged()
        {
            // Plan 08 Slice 0: the defect repeats — a second loop receipt
            // for the same category also lands lots while the reserve stays
            // at 0, so the deficit never closes no matter how often the
            // household shops through the loop.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            f.Fund(10000);

            HouseholdPurchasingNeed first = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            ShoppingTripResult firstResult = f.Loop.ExecuteNeed(first, f.Day, f.Options());
            Assert.IsTrue(firstResult.Success, firstResult.FailureReason);

            HouseholdPurchasingNeed second = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            ShoppingTripResult result = f.Loop.ExecuteNeed(second, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "Two receipts land 10u of flour lots in total.");
            Assert.AreEqual(0, reserve.currentUnits,
                "AUD-335: even a second loop receipt never credits the reserve.");
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(HasStapleFoodNeed(needsAfter),
                "AUD-335: the deficit stays open after two loop receipts.");
        }

        // ---------- Slice 0: AUD-266 discriminator (shopper half) ----------

        [Test]
        public void ChooseShopper_ReturnsOffMapMember()
        {
            // Plan 08 Slice 0: ShopperAssignmentPolicy picks the off-map
            // adult first in the member list, because it never consults
            // the OffMapJourneyAuthority. P1 is the preferred shopper
            // (adult woman) and is off-map on a freight journey; P2 is an
            // on-map adult. Expected to change in Slice 2: the policy
            // skips P1 and returns P2.
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Emma", ageBand = AgeBand.Adult18Plus, isFemale = true
            });
            population.people.Add(new PersonState
            {
                id = 2, firstName = "Tomas", ageBand = AgeBand.Adult18Plus, isFemale = false
            });
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            journeys.AddEdge("home", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(journeys);
            var diag = new List<string>();
            OffMapJourneyRecord record = authority.DepartPerson(
                1, "home", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, diag);
            Assert.IsNotNull(record, "DepartPerson must create the journey record: " + string.Join("; ", diag));
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must read off-map while the journey is active.");

            int chosen = ShopperAssignmentPolicy.ChooseShopper(
                population, new List<int> { 1, 2 }, 0, 480, 60, null, null, null, out string reason);

            Assert.AreEqual(1, chosen,
                "AUD-266: ChooseShopper returns the off-map adult (reason: " + (reason ?? "none") + ").");
        }

        [Test]
        public void ReassignIfUnavailable_IgnoresOffMapExecutor()
        {
            // Plan 08 Slice 0: a plan assigned to P1, whose reservation is
            // still live and whose schedule is clear, is never reassigned
            // when P1 departs off-map, because CheckUnavailable never
            // checks off-map state. Expected to change in Slice 2: the
            // check reports P1 off-map and the plan reassigns to P2.
            Fixture f = Fixture.Create();
            OffMapJourneyAuthority authority = DepartFirstAdultOffMap(f, 1);

            var coordinator = new HouseholdErrandCoordinator();
            var ctx = new HouseholdErrandCoordinator.CoordinatorContext
            {
                Population = f.Population,
                Memberships = f.Membership,
                Schedules = f.Tracker,
                Budgets = f.Budgets,
                PersonEntityId = pid => EntityId.For(EntityKind.Person, pid),
                Needs = f.Needs,
            };
            // The authority is held only to establish P1's off-map state;
            // Slice 0's CoordinatorContext has no OffMapJourneys field yet
            // (Slice 2 adds it), which is exactly the coordinator gap.
            HouseholdErrandPlan plan = coordinator.Plans.Create(7, 0);
            string refusal = f.Tracker.TryReserve(1, PersonActivityKind.Shopping, 0, 0, 30, "test plan window");
            Assert.IsNull(refusal, refusal);
            List<PersonActivityReservation> active = f.Tracker.ActiveFor(1, 0);
            Assert.AreEqual(1, active.Count, "The plan window reservation must exist before reassignment is tested.");
            plan.Assign(1, new List<string>(), active[0].ReservationId);
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must still read off-map when reassignment is tested.");

            string result = coordinator.ReassignIfUnavailable(plan.PlanId, 0, ctx, f.Diagnostics);

            Assert.IsNull(result,
                "AUD-266: ReassignIfUnavailable is a no-op for an off-map executor (returned: " + (result ?? "null") + ").");
            Assert.AreEqual(1, plan.ResponsiblePersonId,
                "AUD-266: the plan still names the off-map P1 as responsible.");
        }
    }
}
