using System.Collections.Generic;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Pass V slice H: coordinated household errands. Batches open needs into
    /// HouseholdErrandPlans (one trip serving several needs), executes through
    /// the existing shopping authorities with no duplicate purchases, and
    /// reassigns the plan when the responsible member becomes unavailable
    /// (no duplicated person-time, no duplicated purchases).
    /// </summary>
    [TestFixture]
    public sealed class Pass5SliceCoordinatedErrandsTests
    {
        private sealed class TestStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public readonly Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "general_goods";
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => LastReceiptIdValue;

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    LastReceiptIdValue = $"rcpt-{++ReceiptSequence}";
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                    {
                        CashReceived += settlement.AmountCents;
                    }
                }

                return sold;
            }
        }

        private sealed class Fixture
        {
            public const int HouseholdId = 7;
            public const int Day = 100;

            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly SupplierDirectory Directory = new SupplierDirectory();
            public readonly JourneyModel Journeys = new JourneyModel();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public readonly TestStore Store = new TestStore();
            public readonly HouseholdShoppingLoop Loop;
            public readonly HouseholdErrandCoordinator Coordinator = new HouseholdErrandCoordinator();
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();

            public Fixture()
            {
                Population.people.Add(Adult(1, "Tomas", HouseholdId));
                Population.people.Add(Adult(2, "Anna", HouseholdId));
                Population.people.Add(new PersonState
                {
                    id = 3, firstName = "Petr", lastName = "Morrow", age = 8,
                    ageBand = AgeBand.Child0To9, householdId = HouseholdId,
                });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 3, PersonId = 3, HouseholdId = HouseholdId, StartDayIndex = 0 });

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                Store.Stock[HouseholdItemCatalog.FlourId] = 100;
                Store.Prices[HouseholdItemCatalog.FlourId] = 120;
                Store.Stock[HouseholdItemCatalog.SaltId] = 100;
                Store.Prices[HouseholdItemCatalog.SaltId] = 40;
                Store.Stock[HouseholdItemCatalog.SoapId] = 100;
                Store.Prices[HouseholdItemCatalog.SoapId] = 60;
                Directory.Register(Store);
                Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                Knowledge.LearnSupplier(2, "store-1", "lives in the same town", 0);

                Locations[1] = "home";
                Locations[2] = "home";
                Locations[3] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null);

                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            }

            public static PersonState Adult(int id, string name, int householdId)
            {
                return new PersonState
                {
                    id = id, firstName = name, lastName = "Morrow", age = 34,
                    ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = householdId,
                };
            }

            public HouseholdErrandCoordinator.CoordinatorContext Context()
            {
                return new HouseholdErrandCoordinator.CoordinatorContext
                {
                    Population = Population,
                    Memberships = Membership,
                    Ledgers = Ledgers,
                    Inventories = Inventories,
                    Needs = Needs,
                    Schedules = Tracker,
                    Tasks = Tasks,
                    Budgets = Budgets,
                    PersonEntityId = pid => EntityId.For(EntityKind.Person, pid),
                };
            }

            public ShoppingLoopOptions Options()
            {
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };
            }

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                need.Urgency01 = 0.9f;
                return need;
            }
        }

        [Test]
        public void H_BatchServesMultipleNeedsInOneTrip()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed flour = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            HouseholdPurchasingNeed salt = f.OpenNeed(HouseholdItemCatalog.SaltId, 5);
            HouseholdPurchasingNeed soap = f.OpenNeed(HouseholdItemCatalog.SoapId, 4);

            int cashBefore = f.Ledgers.Get(Fixture.HouseholdId).GetBalanceCents();
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            Assert.AreEqual(1, plans.Count, "one plan batches all three open needs");
            HouseholdErrandPlan plan = plans[0];
            Assert.AreEqual(HouseholdErrandPlanStatus.Proposed, plan.Status);

            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plan, Fixture.Day, f.Context(), f.Loop, f.Options(), f.Diag);

            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.TripExecuted, "trip executed: " + batch.FailureReason);
            Assert.AreEqual(3, batch.ServedNeedCount);
            Assert.AreEqual(0, batch.UnbatchedNeeds.Count);
            Assert.Greater(batch.ShopperPersonId, 0);
            // Exactly one shopper for the whole batch.
            foreach (ShoppingTripResult r in batch.NeedResults)
            {
                Assert.AreEqual(batch.ShopperPersonId, r.ShopperPersonId);
            }

            // No duplicate purchases: each need fulfilled exactly once.
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, flour.Status);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, salt.Status);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, soap.Status);
            Assert.AreEqual(HouseholdErrandPlanStatus.Completed, plan.Status);

            // Conservation: ledger outflow == store cash in; goods received once.
            int cashAfter = f.Ledgers.Get(Fixture.HouseholdId).GetBalanceCents();
            int expectedSpend = 10 * 120 + 5 * 40 + 4 * 60;
            Assert.AreEqual(cashBefore - expectedSpend, cashAfter);
            Assert.AreEqual(expectedSpend, f.Store.CashReceived);
            HouseholdInventory inventory = f.Inventories.Get(Fixture.HouseholdId);
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.SaltId));
            Assert.AreEqual(4, inventory.GetAvailableUnits(HouseholdItemCatalog.SoapId));

            // Person-time: the trip's single window was released afterwards.
            Assert.IsTrue(f.Tracker.ActiveFor(batch.ShopperPersonId, Fixture.Day).Count == 0);
        }

        [Test]
        public void H_TwoPlansCannotClaimTheSameNeed()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.OpenNeed(HouseholdItemCatalog.SaltId, 5);

            List<HouseholdErrandPlan> first = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            Assert.AreEqual(1, first.Count);
            List<HouseholdErrandPlan> second = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            Assert.AreEqual(0, second.Count, "claimed needs are not re-planned — no duplicate purchase possible");
        }

        [Test]
        public void H_MemberBecomesUnavailable_ReassignsWithoutDuplicating()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed flour = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            HouseholdPurchasingNeed salt = f.OpenNeed(HouseholdItemCatalog.SaltId, 5);

            // Plan and execute far enough to assign the shopper and hold the window.
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            HouseholdErrandPlan plan = plans[0];
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plan, Fixture.Day, f.Context(), f.Loop, f.Options(), f.Diag);
            Assert.IsTrue(batch.TripExecuted);
            int firstShopper = batch.ShopperPersonId;
            Assert.AreEqual(HouseholdErrandPlanStatus.Completed, plan.Status);

            // New day, new needs, new plan — then the executor becomes unavailable
            // before the trip runs: their reservation is lost externally.
            HouseholdPurchasingNeed soap = f.OpenNeed(HouseholdItemCatalog.SoapId, 4);
            List<HouseholdErrandPlan> plans2 = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day + 1, f.Context(), f.Diag);
            Assert.AreEqual(1, plans2.Count);
            HouseholdErrandPlan plan2 = plans2[0];

            // Simulate assignment without full execution: assign P1 manually so
            // the plan is Assigned, then take P1 away (reservation lost).
            var ctx = f.Context();
            string reserveRefusal = ctx.Schedules.TryReserve(1, PersonActivityKind.Shopping, Fixture.Day + 1,
                600, 120, "test: pre-assign P1");
            Assert.IsNull(reserveRefusal);
            string reservationId = null;
            foreach (PersonActivityReservation r in ctx.Schedules.ActiveFor(1, Fixture.Day + 1))
            {
                reservationId = r.ReservationId;
            }

            plan2.Assign(1, new[] { soap.NeedSequence.ToString() }, reservationId);
            f.Coordinator.Plans.NoteEstimate(plan2.PlanId, 120, 600);

            // P1 becomes unavailable: the reservation is cancelled externally.
            ctx.Schedules.Release(reservationId);

            string outcome = f.Coordinator.ReassignIfUnavailable(plan2.PlanId, Fixture.Day + 1, ctx, f.Diag);
            Assert.IsNotNull(outcome, "reassignment should have happened, got: " + outcome);
            Assert.AreEqual(2, plan2.ResponsiblePersonId, "plan moved to the other adult");
            Assert.AreNotEqual(reservationId, plan2.ReservationId, "new reservation issued");
            Assert.AreEqual(HouseholdErrandPlanStatus.Assigned, plan2.Status);

            // No duplicated time: exactly one live reservation for the plan.
            int liveForP1 = ctx.Schedules.ActiveFor(1, Fixture.Day + 1).Count;
            int liveForP2 = ctx.Schedules.ActiveFor(2, Fixture.Day + 1).Count;
            Assert.AreEqual(0, liveForP1);
            Assert.AreEqual(1, liveForP2);

            // No duplicated purchases: execute the reassigned plan — soap is
            // bought exactly once.
            int soapBefore = f.Store.ItemStockUnits(HouseholdItemCatalog.SoapId);
            HouseholdShoppingLoop.BatchShoppingResult batch2 =
                f.Coordinator.ExecutePlan(plan2, Fixture.Day + 1, ctx, f.Loop, f.Options(), f.Diag);
            Assert.IsNotNull(batch2);
            Assert.IsTrue(batch2.TripExecuted, batch2.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, soap.Status);
            Assert.AreEqual(soapBefore - 4, f.Store.ItemStockUnits(HouseholdItemCatalog.SoapId));
            Assert.AreEqual(4, f.Inventories.Get(Fixture.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.SoapId));
            // Earlier needs were not re-bought.
            Assert.AreEqual(10, f.Inventories.Get(Fixture.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        [Test]
        public void H_ReassignDropsAlreadyFulfilledNeeds()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed flour = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            HouseholdPurchasingNeed salt = f.OpenNeed(HouseholdItemCatalog.SaltId, 5);

            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            HouseholdErrandPlan plan = plans[0];

            var ctx = f.Context();
            string reserveRefusal = ctx.Schedules.TryReserve(1, PersonActivityKind.Shopping, Fixture.Day,
                600, 120, "test: pre-assign P1");
            Assert.IsNull(reserveRefusal);
            string reservationId = null;
            foreach (PersonActivityReservation r in ctx.Schedules.ActiveFor(1, Fixture.Day))
            {
                reservationId = r.ReservationId;
            }

            plan.Assign(1, new[] { flour.NeedSequence.ToString(), salt.NeedSequence.ToString() }, reservationId);
            f.Coordinator.Plans.NoteEstimate(plan.PlanId, 120, 600);

            // Flour got fulfilled by another path (e.g. a neighbor's gift recorded
            // as a lot would also do); salt is still open.
            flour.Status = PurchasingNeedStatus.Fulfilled;
            flour.UnitsNeeded = 0;

            // P1 becomes unavailable (reservation lost).
            ctx.Schedules.Release(reservationId);
            string outcome = f.Coordinator.ReassignIfUnavailable(plan.PlanId, Fixture.Day, ctx, f.Diag);

            Assert.IsNotNull(outcome);
            Assert.AreEqual(2, plan.ResponsiblePersonId);
            CollectionAssert.AreEquivalent(
                new[] { salt.NeedSequence.ToString() }, plan.NeedIds,
                "fulfilled flour must not be re-bought by the reassigned plan");
        }

        [Test]
        public void H_AvailableExecutor_NeedsNoReassignment()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            HouseholdErrandPlan plan = plans[0];

            var ctx = f.Context();
            string reserveRefusal = ctx.Schedules.TryReserve(1, PersonActivityKind.Shopping, Fixture.Day,
                600, 120, "test: pre-assign P1");
            Assert.IsNull(reserveRefusal);
            string reservationId = null;
            foreach (PersonActivityReservation r in ctx.Schedules.ActiveFor(1, Fixture.Day))
            {
                reservationId = r.ReservationId;
            }

            plan.Assign(1, new[] { "0" }, reservationId);
            f.Coordinator.Plans.NoteEstimate(plan.PlanId, 120, 600);

            string outcome = f.Coordinator.ReassignIfUnavailable(plan.PlanId, Fixture.Day, ctx, f.Diag);
            Assert.IsNull(outcome, "available executor → no-op, got: " + outcome);
            Assert.AreEqual(1, plan.ResponsiblePersonId);
            Assert.AreEqual(reservationId, plan.ReservationId);
        }

        [Test]
        public void H_PlanRegistry_SaveLoad_RestoresClaimsWithoutReexecution()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            Assert.AreEqual(1, plans.Count);

            HouseholdErrandPlanRegistrySaveDto dto = f.Coordinator.Plans.CaptureSaveDto();
            var restored = new HouseholdErrandPlanRegistry();
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.IsNeedClaimed(Fixture.HouseholdId, 0));
            Assert.AreEqual(1, restored.ActiveFor(Fixture.HouseholdId).Count);

            // A fresh coordinator over the restored registry does not re-plan.
            var coordinator2 = new HouseholdErrandCoordinator();
            coordinator2.Plans.LoadFromSaveDto(dto);
            List<HouseholdErrandPlan> replanned = coordinator2.PlanDay(Fixture.HouseholdId, Fixture.Day, f.Context(), f.Diag);
            Assert.AreEqual(0, replanned.Count, "restored claim survives — no duplicate plan after load");
        }
    }
}
