using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Lane 9e: the fuel dealer has seeded seller knowledge and can serve a
    /// retail customer. The dealer was fully wired as a supplier but had no
    /// seeding, so no household ever learned of it. Seeding mirrors the
    /// bakery's (live residents only, building-backed supplier, idempotent).
    /// </summary>
    [TestFixture]
    public sealed class FuelDealerKnowledgeSeedingTests
    {
        private sealed class FakeDealer : IItemResolvingSupplier, IFuelDealerSupplier
        {
            public string SupplierBusinessId { get; set; } = "fuel-dealer-1";
            public string SupplierName { get; set; } = "Fuel Dealer";
            public string LocationId { get; set; } = "building:3";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "fuel_wood";
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

        private sealed class FakeStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "building:1";

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;
            public bool OffersItem(string itemId) => false;
            public string CategoryForItem(string itemId) => string.Empty;
            public int ItemStockUnits(string itemId) => 0;
            public int ItemPricePerUnitCents(string itemId) => 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => string.Empty;
            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics) => 0;
        }

        [Test]
        public void SeedsOnlyLiveResidents()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus },
                new PersonState { id = 2, firstName = "Old", lastName = "Morrow", age = 80, ageBand = AgeBand.Adult18Plus, deathDayIndex = 5 },
                new PersonState { id = 3, firstName = "Away", lastName = "Morrow", age = 30, ageBand = AgeBand.Adult18Plus, isOffMap = true },
                new PersonState { id = -1, firstName = "Ghost", lastName = "Nobody", age = 40, ageBand = AgeBand.Adult18Plus },
            };
            var dealer = new FakeDealer();
            var suppliers = new List<IGoodsSupplier> { dealer };
            var knowledge = new ShopperSellerKnowledge();

            FuelDealerKnowledgeSeeding.SeedLocalFuelDealerKnowledge(people, suppliers, knowledge, 100);

            Assert.IsTrue(knowledge.Knows(1, "fuel-dealer-1"), "a live resident learns the dealer");
            Assert.IsFalse(knowledge.Knows(2, "fuel-dealer-1"), "the dead do not learn suppliers");
            Assert.IsFalse(knowledge.Knows(3, "fuel-dealer-1"), "off-map people do not learn suppliers");
            Assert.IsFalse(knowledge.Knows(-1, "fuel-dealer-1"), "a negative person id learns nothing");
            Assert.AreEqual(1, knowledge.KnownSuppliers(1, suppliers).Count, "the live resident knows exactly the dealer");
        }

        [Test]
        public void SeedingIsIdempotent()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus },
            };
            var dealer = new FakeDealer();
            var suppliers = new List<IGoodsSupplier> { dealer };
            var knowledge = new ShopperSellerKnowledge();

            FuelDealerKnowledgeSeeding.SeedLocalFuelDealerKnowledge(people, suppliers, knowledge, 100);
            FuelDealerKnowledgeSeeding.SeedLocalFuelDealerKnowledge(people, suppliers, knowledge, 101);

            Assert.IsTrue(knowledge.Knows(1, "fuel-dealer-1"));
            Assert.AreEqual(1, knowledge.KnownSuppliers(1, suppliers).Count, "repeated seeding must not duplicate knowledge");
        }

        [Test]
        public void DealerWithoutBuildingLocationOrIdIsNotEligible()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus },
            };
            var knowledge = new ShopperSellerKnowledge();

            var noBuilding = new FakeDealer { LocationId = "fuel-dealer-1" };
            FuelDealerKnowledgeSeeding.SeedLocalFuelDealerKnowledge(people, new List<IGoodsSupplier> { noBuilding }, knowledge, 100);
            Assert.IsFalse(knowledge.Knows(1, "fuel-dealer-1"), "a dealer with no building location stays undiscovered");
            Assert.IsFalse(FuelDealerKnowledgeSeeding.IsEligibleLocalFuelDealerSupplier(noBuilding));

            var noId = new FakeDealer { SupplierBusinessId = string.Empty };
            Assert.IsFalse(FuelDealerKnowledgeSeeding.IsEligibleLocalFuelDealerSupplier(noId), "a dealer with no business id is not eligible");

            var store = new FakeStore();
            Assert.IsFalse(FuelDealerKnowledgeSeeding.IsEligibleLocalFuelDealerSupplier(store),
                "a non-dealer supplier is never seeded as a fuel dealer");
        }

        [Test]
        public void SeededDealerCanServeRetailCustomer()
        {
            var f = new Fixture();
            f.Dealer.Stock[HouseholdItemCatalog.FuelWoodId] = 50;
            f.Dealer.Prices[HouseholdItemCatalog.FuelWoodId] = 200;
            f.Directory.Register(f.Dealer);

            FuelDealerKnowledgeSeeding.SeedLocalFuelDealerKnowledge(
                f.Population.people, f.Directory.All, f.Knowledge, Fixture.Day);

            Assert.IsTrue(f.Knowledge.Knows(1, "fuel-dealer-1"), "seeding must make the dealer known to the shopper");

            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 10);
            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, Fixture.Day, f.LoopOptions);

            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason + "\n" + string.Join("\n", f.Diag));
            Assert.IsNotNull(trip.Candidate, "the seeded dealer must be chosen as a candidate");
            Assert.AreEqual("fuel-dealer-1", trip.Candidate.Supplier.SupplierBusinessId, "the trip must go to the dealer");
            Assert.AreEqual(10, trip.Candidate.UnitsPlanned, "ten bundles planned");
            Assert.IsFalse(string.IsNullOrWhiteSpace(trip.StockReservationId),
                "the planned trip holds its stock on the unified authority");
            Assert.AreEqual(50, f.Dealer.Stock[HouseholdItemCatalog.FuelWoodId], "planning alone sells nothing");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "planning alone does not fulfill the need");
        }

        [Test]
        public void UnseededDealerIsNotKnown()
        {
            var people = new List<PersonState>
            {
                new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus },
            };
            var knowledge = new ShopperSellerKnowledge();
            Assert.IsFalse(knowledge.Knows(1, "fuel-dealer-1"), "without seeding, no household knows the dealer");
        }

        private sealed class Fixture
        {
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
            public readonly EquipmentReservationService Authority = new EquipmentReservationService();
            public readonly FakeDealer Dealer = new FakeDealer();
            public readonly HouseholdShoppingLoop Loop;
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();

            public const int HouseholdId = 7;
            public const int Day = 100;

            public Fixture()
            {
                Population.people.Add(new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                Population.people.Add(new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = HouseholdId, StartDayIndex = 0 });

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("building:3", JourneyLocationKind.TownBuilding, "Fuel Dealer", 2f, 0f));
                Journeys.AddEdge("home", "building:3", 2.0f, "river road");

                Locations[1] = "home";
                Locations[2] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                LoopOptions = new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null,
                    reservationAuthority: Authority);

                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            }

            public ShoppingLoopOptions LoopOptions;

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }
        }
    }
}
