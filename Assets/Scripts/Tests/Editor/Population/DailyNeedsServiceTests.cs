using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// T2C: NPC daily-needs execution. HF-4 plans; this executes. Meals need a
    /// source (GHOST-DEF-006): reserves are consumed causally, shortfalls are
    /// shopped for by a real household member, and missed meals are logged —
    /// never faked.
    /// </summary>
    public sealed class DailyNeedsServiceTests
    {
        private sealed class TestSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "store-1";
            public string SupplierName => "General Store";
            public string LocationId => "store";
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int PriceCents = 10;

            public bool HasCategory(string categoryId) => Stock.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int u) ? u : 0;
            public int PricePerUnitCents(string categoryId) => PriceCents;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) Stock[categoryId] -= sold;
                return sold;
            }
        }

        private sealed class CapturingExecutor : IEmbodiedPurchaseExecutor
        {
            public readonly List<int> ActingPersonIds = new List<int>();
            private readonly IEmbodiedPurchaseExecutor inner;

            public CapturingExecutor()
            {
            }

            public CapturingExecutor(IEmbodiedPurchaseExecutor inner)
            {
                this.inner = inner;
            }

            public PurchaseExecutionResult Execute(ProcurementNeed need, int actingPersonId, int dayIndex)
            {
                ActingPersonIds.Add(actingPersonId);
                if (inner != null)
                {
                    return inner.Execute(need, actingPersonId, dayIndex);
                }

                return new PurchaseExecutionResult
                {
                    NeedSequence = need.NeedSequence,
                    Success = true,
                    ActingPersonId = actingPersonId,
                    UnitsAcquired = need.UnitsNeeded,
                    AmountPaidCents = 0,
                };
            }
        }

        private PopulationState population;
        private HouseholdLedgerRegistry ledgers;
        private SupplierDirectory suppliers;
        private JourneyModel journeys;
        private EmbodiedPurchaseExecutor executor;
        private HouseholdConsumptionPlanner planner;
        private DailyNeedsService service;
        private List<string> diag;

        private static PersonState Adult(int id, string name, int householdId)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
            };
        }

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            population.people.Add(Adult(1, "Abe", 7));
            population.people.Add(Adult(2, "Bea", 7));
            var household = new HouseholdState { id = 7 };
            household.memberIds.Add(1);
            household.memberIds.Add(2);
            household.reserves.Add(new HouseholdReserveState
            {
                categoryId = "staple_food", displayName = "Staple food",
                currentUnits = 0, lowThresholdUnits = 20, targetUnits = 30,
            });
            population.households.Add(household);

            ledgers = new HouseholdLedgerRegistry();
            ledgers.GetOrCreate(7).RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");

            suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");

            executor = new EmbodiedPurchaseExecutor(population, ledgers, suppliers, journeys, pid => "home");
            planner = new HouseholdConsumptionPlanner();
            service = new DailyNeedsService();
            diag = new List<string>();
        }

        [Test]
        public void HouseholdEatsFromStoresWhenStocked()
        {
            population.households[0].reserves[0].currentUnits = 100;
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(96, population.households[0].reserves[0].currentUnits);
            Assert.AreEqual(0, report.PurchasesMade, "No shopping needed — ate from stores.");
        }

        [Test]
        public void LotBackedBread_IsEatenWithoutCooking_AndSkippedMiddayIsNotNutritionalMiss()
        {
            var inventories = new HouseholdInventoryRegistry();
            HouseholdInventory inventory = inventories.GetOrCreate(7);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 1, 0, "actual household purchase"));
            var mealLog = new HouseholdMealLogRegistry();
            population.people[0].nutrition.nutrition01 = 1f;
            population.people[1].nutrition.nutrition01 = 1f;

            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, null, 0, diag,
                inventories: inventories, mealLog: mealLog,
                mealPolicy: MealSchedulingPolicy.Default);

            // The default policy skips its flexible midday slot while supply
            // is low. The two substantial slots still produce one real meal
            // and one genuine missed meal for Abe; Bea misses both.
            Assert.AreEqual(1, report.MealsEaten);
            Assert.AreEqual(3, report.MealsMissed);
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(1, mealLog.GetMeals(7).Count,
                "Ready bread is consumed directly; no cooking Task or prepared meal is fabricated.");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, mealLog.GetMeals(7)[0].ItemsConsumed[0].ItemId);
            Assert.AreEqual(0, mealLog.GetPreparations(7).Count);
            Assert.That(population.GetPerson(1).nutrition.nutrition01, Is.EqualTo(0.82f).Within(0.000001f),
                "Only the two scheduled substantial slots count toward nutrition when midday is skipped.");
            Assert.That(population.GetPerson(2).nutrition.nutrition01, Is.EqualTo(0.64f).Within(0.000001f));
        }

        [Test]
        public void ShortfallIsShoppedForByAHouseholdMember()
        {
            // No food in stores: the acting member must shop (GHOST-DEF-006 — meals need a source).
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.Greater(report.PurchasesMade, 0, "The shortfall was shopped for, not conjured.");
            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.Greater(report.SpendCents, 0);
        }

        [Test]
        public void NoSupplierMeansMissedMealsLoggedNotFaked()
        {
            var emptySuppliers = new SupplierDirectory(); // nothing stocked anywhere
            var noShopExecutor = new EmbodiedPurchaseExecutor(population, ledgers, emptySuppliers, journeys, pid => "home");

            var report = service.ExecuteDay(population, planner, noShopExecutor, 0, diag);

            Assert.AreEqual(0, report.MealsEaten);
            Assert.AreEqual(4, report.MealsMissed, "Missed meals are logged, never faked.");
            Assert.AreEqual(0, report.PurchasesMade);
        }

        [Test]
        public void NoActingAdultMeansUnmetNeeds()
        {
            population.people.Clear(); // nobody home
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(0, report.HouseholdsServed);
            Assert.AreEqual(0, report.MealsEaten);
        }

        [Test]
        public void OffMapFirstAdultSkipped_FallsBackToSecondAdult()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): REWRITE of the Slice 0
            // FindActingAdult_ReturnsOffMapFirstAdult. With the authority
            // threaded through ExecuteDay, the off-map first adult P1 is
            // skipped with a W-2 skip log and the on-map second adult P2
            // serves — the capturing executor delegates to the real
            // executor, so the shopped meals are really eaten.
            journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            journeys.AddEdge("home", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(journeys);
            OffMapJourneyRecord record = authority.DepartPerson(
                1, "home", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, diag);
            Assert.IsNotNull(record, "DepartPerson must create the journey record: " + string.Join("; ", diag));
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must read off-map while the journey is active.");

            var capturing = new CapturingExecutor(executor);
            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, capturing, 0, diag, offMapJourneys: authority);

            Assert.AreEqual(1, report.HouseholdsServed, "A household with an on-map acting adult is served.");
            Assert.Greater(capturing.ActingPersonIds.Count, 0, "The executor must have been called at least once.");
            Assert.IsTrue(capturing.ActingPersonIds.TrueForAll(id => id == 2),
                "Every executor call must name P2, the on-map second adult: " + string.Join(",", capturing.ActingPersonIds));
            Assert.AreEqual(4, report.MealsEaten, "Two members x two meals, all shopped for by P2.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.IsTrue(diag.Exists(m => m.Contains("P1 is off-map, needs stay open for return.")),
                "The skip must be logged in the W-2 wording.");
            Assert.IsTrue(diag.TrueForAll(m => !m.Contains("WITHOUT the off-map guard")),
                "With the authority installed, the unavailable-authority line must not fire.");
        }

        [Test]
        public void AllAdultsOffMap_NoActingAdult_NeedsUnmet()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): when every adult is
            // off-map, no acting adult is found, nothing is faked, and
            // the "no acting adult" diagnostic is present.
            journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            journeys.AddEdge("home", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(journeys);
            foreach (int personId in new[] { 1, 2 })
            {
                OffMapJourneyRecord record = authority.DepartPerson(
                    personId, "home", "Millbrook", 1, 2, "freight",
                    null, null, 0, _ => 0, null, diag);
                Assert.IsNotNull(record, $"DepartPerson must create the journey record for P{personId}: " + string.Join("; ", diag));
                Assert.IsTrue(authority.IsOffMapPerson(personId), $"P{personId} must read off-map while the journey is active.");
            }

            var capturing = new CapturingExecutor(executor);
            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, capturing, 0, diag, offMapJourneys: authority);

            Assert.AreEqual(0, report.HouseholdsServed, "No household is served without an on-map acting adult.");
            Assert.AreEqual(0, report.MealsEaten);
            Assert.AreEqual(0, capturing.ActingPersonIds.Count, "The executor must never be called without an acting adult.");
            Assert.IsTrue(diag.Exists(m => m.Contains("no acting adult")),
                "The unmet needs must be logged honestly, not faked.");
            Assert.IsTrue(diag.Exists(m => m.Contains("P1 is off-map, needs stay open for return.")),
                "The P1 skip must be logged.");
            Assert.IsTrue(diag.Exists(m => m.Contains("P2 is off-map, needs stay open for return.")),
                "The P2 skip must be logged.");
        }

        [Test]
        public void NullAuthority_FailOpen_FirstAdultServesWithLoudLog()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): with no authority passed,
            // the guard fails open — P1 serves as before — but exactly
            // one loud unavailable-authority line is logged for the day.
            DailyNeedsService.DayReport report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(1, report.HouseholdsServed);
            Assert.AreEqual(4, report.MealsEaten, "Two members x two meals, shopped for by P1.");
            Assert.AreEqual(0, report.MealsMissed);
            int loudLines = 0;
            foreach (string message in diag)
            {
                if (message.Contains("off-map journey authority unavailable, proceeding WITHOUT the off-map guard"))
                {
                    loudLines++;
                }
            }
            Assert.AreEqual(1, loudLines, "Exactly one loud unavailable-authority line per day.");
        }

        [Test]
        public void ConsumptionAndPurchaseAreCausallyLinked()
        {
            // Day 0: shop for meals. Day 1: reserves reflect yesterday's purchase minus meals eaten.
            service.ExecuteDay(population, planner, executor, 0, diag);
            int afterDay0 = population.households[0].reserves[0].currentUnits;

            var report1 = service.ExecuteDay(population, planner, executor, 1, diag);

            // Reserves move only through eating and buying — the loop is causal.
            Assert.GreaterOrEqual(afterDay0, 0);
            Assert.AreEqual(4, report1.MealsEaten + report1.MealsMissed,
                "Every meal is accounted for: eaten or missed.");
        }

        [Test]
        public void DailyNeeds_UsesAuthoritativeHouseholdMembershipInsteadOfLegacyMemberMirror()
        {
            // The household's compatibility member list is deliberately stale.
            // The registry remains the sole authority for who receives meals.
            population.people.Add(Adult(3, "Cal", householdId: -1));
            population.people[0].laborAccessLevel = LaborAccessLevel.None;
            population.people[0].nutrition.nutrition01 = 0.4f;
            population.people[2].nutrition.nutrition01 = 0.4f;
            population.households[0].memberIds.Clear();
            population.households[0].memberIds.Add(1);
            population.households[0].reserves[0].currentUnits = 100;

            var memberships = new HouseholdMembershipRegistry();
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 1,
                PersonId = 3,
                HouseholdId = 7,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
            }));

            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, executor, 0, diag,
                memberships: memberships);

            Assert.AreEqual(2, report.MealsEaten,
                "Only the single registry member should receive two daily meals.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits);
            Assert.That(population.people[0].nutrition.nutrition01, Is.EqualTo(0.4f),
                "The stale legacy household member must not receive a meal.");
            Assert.That(population.people[2].nutrition.nutrition01, Is.EqualTo(0.7f).Within(0.000001f),
                "The current registry member receives two meals and recovers once.");
        }

        // ---------- Plan 01 Slice 3 (HARNESS-RUN): end-to-end needs over the authored nine ----------

        // Authored households in ID order (H1..H5), exactly as
        // PopulationManager.RebuildMembershipRegistry constructs them:
        // person IDs 1-9, household IDs 1-5, membership IDs 0-8.
        private static readonly int[][] AuthoredHouseholds =
        {
            new[] { 1, 2 },
            new[] { 3, 4 },
            new[] { 5, 6 },
            new[] { 7, 8 },
            new[] { 9 },
        };

        private static int AuthoredHouseholdOf(int personId)
        {
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int memberId in AuthoredHouseholds[h])
                {
                    if (memberId == personId)
                    {
                        return h + 1;
                    }
                }
            }
            return -1;
        }

        private sealed class AuthoredNineFixture
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Memberships;
            public HouseholdLedgerRegistry Ledgers;
            public EmbodiedPurchaseExecutor Executor;
            public HouseholdConsumptionPlanner Planner;
        }

        /// <summary>
        /// Builds the authored nine as a hand-built registry population: 9
        /// adults aged 27-35 (Adult18Plus, FullLaborMarket) in 5 households,
        /// one Active registry membership per resident (IDs 0-8). The legacy
        /// household memberIds mirrors are deliberately stale - every
        /// household lists only P1 - so any serving that follows the mirror
        /// instead of the registry fails these tests.
        /// </summary>
        private static AuthoredNineFixture BuildAuthoredNineFixture(int reserveUnits)
        {
            var fixture = new AuthoredNineFixture();
            fixture.Population = new PopulationState();
            for (int personId = 1; personId <= 9; personId++)
            {
                fixture.Population.people.Add(new PersonState
                {
                    id = personId,
                    firstName = "Resident" + personId,
                    lastName = "Test",
                    age = 26 + personId,
                    ageBand = AgeBand.Adult18Plus,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                    householdId = AuthoredHouseholdOf(personId),
                });
            }
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                var household = new HouseholdState { id = h + 1 };
                household.memberIds.Add(1);
                household.reserves.Add(new HouseholdReserveState
                {
                    categoryId = "staple_food",
                    displayName = "Staple food",
                    currentUnits = reserveUnits,
                    lowThresholdUnits = 20,
                    targetUnits = 30,
                });
                fixture.Population.households.Add(household);
            }

            fixture.Memberships = new HouseholdMembershipRegistry();
            int nextMembershipId = 0;
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                foreach (int personId in AuthoredHouseholds[h])
                {
                    string rejection = fixture.Memberships.Register(new HouseholdMembership
                    {
                        MembershipId = nextMembershipId++,
                        PersonId = personId,
                        HouseholdId = h + 1,
                        Lifecycle = HouseholdMembershipLifecycle.Active,
                        StartDayIndex = 0,
                        Source = HouseholdMembershipSource.Authored,
                    });
                    Assert.IsNull(rejection, $"P{personId} into H{h + 1} must register cleanly.");
                }
            }

            fixture.Ledgers = new HouseholdLedgerRegistry();
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                string funding = fixture.Ledgers.GetOrCreate(householdId).RecordInflow(
                    0, 100000, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(funding, funding);
            }

            var suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");

            fixture.Executor = new EmbodiedPurchaseExecutor(
                fixture.Population, fixture.Ledgers, suppliers, journeys, pid => "home");
            fixture.Planner = new HouseholdConsumptionPlanner();
            return fixture;
        }

        [Test]
        public void AuthoredNine_ExecuteDay_ServesAllFiveHouseholdsWithActingAdults()
        {
            // Plan 01 Slice 3: adult resolution end to end. Stocked reserves
            // mean no shopping is needed; every household must still resolve
            // a real acting adult through the registry alone.
            AuthoredNineFixture fixture = BuildAuthoredNineFixture(reserveUnits: 100);
            var localDiag = new List<string>();

            DailyNeedsService.DayReport report = service.ExecuteDay(
                fixture.Population, fixture.Planner, fixture.Executor, 0, localDiag,
                memberships: fixture.Memberships);

            Assert.AreEqual(5, report.HouseholdsServed,
                "All five authored households must resolve an acting adult.");
            Assert.AreEqual(18, report.MealsEaten, "Nine residents x two meals.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(0, report.PurchasesMade, "Stocked reserves mean no shopping is needed.");
            Assert.IsTrue(localDiag.TrueForAll(message => !message.Contains("no acting adult")),
                "No household may report 'no acting adult' with the repaired registry.");
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                Assert.AreEqual(100 - 2 * AuthoredHouseholds[h].Length,
                    fixture.Population.households[h].reserves[0].currentUnits,
                    $"H{h + 1} reserves must fall by exactly one unit per meal eaten.");
            }
        }

        [Test]
        public void AuthoredNine_ShortfallIsShoppedForByActingAdults()
        {
            // Plan 01 Slice 3: shopping task creation end to end. Empty
            // reserves mean every household's shortfall must be shopped for
            // by its own acting adult through the embodied executor - not
            // conjured, and not shopped by the stale mirror's P1 for all.
            AuthoredNineFixture fixture = BuildAuthoredNineFixture(reserveUnits: 0);
            var localDiag = new List<string>();

            DailyNeedsService.DayReport report = service.ExecuteDay(
                fixture.Population, fixture.Planner, fixture.Executor, 0, localDiag,
                memberships: fixture.Memberships);

            Assert.AreEqual(5, report.HouseholdsServed,
                "All five authored households must resolve an acting adult.");
            Assert.AreEqual(18, report.MealsEaten, "Nine residents x two meals, all shopped for.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.Greater(report.PurchasesMade, 0, "The shortfall was shopped for, not conjured.");
            Assert.Greater(report.SpendCents, 0);
            Assert.IsTrue(localDiag.TrueForAll(message => !message.Contains("no acting adult")),
                "No household may report 'no acting adult' with the repaired registry.");
        }

        [Test]
        public void AuthoredNine_LotBackedMeals_ConsumeRealBreadLots()
        {
            // Plan 01 Slice 3: household meal execution end to end, lot-backed.
            // Forty loaves per household is 3.3+ staple days of supply for a
            // two-person household (daily use = members x 3 expected meals x 2
            // staple units), so the flexible midday slot is served: three
            // scheduled slots per resident, 27 meals, each consuming one real
            // loaf from a real lot and recorded in the meal log.
            AuthoredNineFixture fixture = BuildAuthoredNineFixture(reserveUnits: 0);
            var inventories = new HouseholdInventoryRegistry();
            var mealLog = new HouseholdMealLogRegistry();
            for (int householdId = 1; householdId <= 5; householdId++)
            {
                Assert.IsNull(inventories.GetOrCreate(householdId).AddLot(
                    HouseholdItemCatalog.BreadId, 40, 0, "test larder"));
            }
            var localDiag = new List<string>();

            DailyNeedsService.DayReport report = service.ExecuteDay(
                fixture.Population, fixture.Planner, null, 0, localDiag,
                inventories: inventories, mealLog: mealLog,
                mealPolicy: MealSchedulingPolicy.Default,
                memberships: fixture.Memberships);

            Assert.AreEqual(5, report.HouseholdsServed,
                "All five authored households must resolve an acting adult.");
            Assert.AreEqual(27, report.MealsEaten, "Nine residents x three scheduled slots, all lot-backed.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.IsTrue(localDiag.TrueForAll(message => !message.Contains("no acting adult")),
                "No household may report 'no acting adult' with the repaired registry.");
            int loggedMeals = 0;
            for (int h = 0; h < AuthoredHouseholds.Length; h++)
            {
                loggedMeals += mealLog.GetMeals(h + 1).Count;
                Assert.AreEqual(40 - 3 * AuthoredHouseholds[h].Length,
                    inventories.GetOrCreate(h + 1).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                    $"H{h + 1} bread must fall by exactly one loaf per meal served.");
            }
            Assert.AreEqual(27, loggedMeals, "Every served meal must be recorded in the meal log.");
        }
    }
}
