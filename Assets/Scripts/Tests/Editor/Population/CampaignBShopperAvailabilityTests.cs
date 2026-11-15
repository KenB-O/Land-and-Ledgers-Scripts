// Campaign B, Package 16 (QA-15/OD-2, Nov 15, 2026): availability-first
// household shopping regression tests.
//
// Ports the HouseholdShoppingLoopReserveCreditTests fixture shape (same
// loop construction, same FakeStorePort offering flour for staple_food)
// into a harness-included file, because SinglePersonShoppingTests.cs is
// harness-excluded (its fixture needs ManagerWorkSchedulerDrive and real
// BusinessRuntimeState lot behavior, neither of which compiles in the
// harness). Two adults instead of one adult plus a child: Tomas (P1)
// and Anna (P2), both Adult18Plus, plus a remote forest-camp location
// 18 mi from the store (360 min one-way at the fixture's 20 min/mi).
// Flour is the need throughout: the shopper rule is item-generic, and
// flour keeps the fixture minimal (the bread vertical slice exercises
// the same chooser through the same loop paths).
//
// The owner decision under test (Kennedy, Nov 15, 2026): choose an
// eligible adult using actual work schedules, existing tasks,
// reservations, current location, travel time, and availability; prefer
// someone home or otherwise free over someone working a shift; no
// woman-first, no gender-based preference anywhere; trips begin from
// the selected NPC's actual location, never an assumed home position;
// if nobody is available, defer and retry rather than interrupting
// work or creating artificial travel. The reservation-count score is
// one input among several (kept, not deleted); travel only breaks
// ties between equally available adults; on a further tie the first
// member in member order wins, deterministically.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class CampaignBShopperAvailabilityTests
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
                if (!Stock.TryGetValue(categoryId, out int stock) || stock <= 0 || units <= 0) return false;
                consumedUnits = Math.Min(stock, units);
                Stock[categoryId] = stock - consumedUnits;
                return consumedUnits > 0;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += revenueCents;
                RecordedCashRevenue += revenueCents;
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                RecordedReceivable += amountCents;
                ReceivableObligationIds.Add(obligationId);
            }

            public string SpendCash(int amountCents, string purpose)
            {
                if (amountCents <= 0) return null;
                if (CashBalance < amountCents) return "insufficient store cash";
                CashBalance -= amountCents;
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return null;
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                return 0;
            }

            public int DeliveryFeeCents(string itemId, int units) => Math.Max(0, DeliveryFeeValue);
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
            public HouseholdErrandCoordinator Coordinator;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Transitions = new List<string>();
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (DayOfWeekIndex 0)

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                var tomas = new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus, isFemale = false };
                var anna = new PersonState { id = 2, firstName = "Anna", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus, isFemale = true };
                f.Population.people.Add(tomas);
                f.Population.people.Add(anna);
                var household = new HouseholdState { id = 7 };
                f.Population.households.Add(household);
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0 });
                Assert.IsNull(regProblem, regProblem);
                regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = 7, StartDayIndex = 0 });
                Assert.IsNull(regProblem, regProblem);

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("forest-camp", JourneyLocationKind.Farmstead, "Forest Camp", 20f, 0f));
                f.Journeys.AddEdge("home", "store", 2.0f, "river road");
                f.Journeys.AddEdge("home", "forest-camp", 20.0f, "forest trail");
                f.Journeys.AddEdge("forest-camp", "store", 18.0f, "forest road");

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
                f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                f.Knowledge.LearnSupplier(2, "store-1", "lives in the same town", 0);

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);
                f.Coordinator = new HouseholdErrandCoordinator();

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
                    SetPersonLocation = (pid, loc) =>
                    {
                        string old = fixture.PersonLocations.TryGetValue(pid, out string cur) ? cur : "?";
                        fixture.Transitions.Add($"{pid}:{old}->{loc}");
                        fixture.PersonLocations[pid] = loc;
                    },
                    DepartureMinuteOfDay = 480,
                    InStoreMinutes = 15,
                    Diagnostics = fixture.Diagnostics,
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
                    OffMapJourneys = null,
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

            public void ReserveWork(int personId, int startMinute, int minutes)
            {
                string problem = Tracker.TryReserve(personId, PersonActivityKind.Work, Day, startMinute, minutes, "shift");
                Assert.IsNull(problem, problem);
            }

            public void ReserveRest(int personId, int startMinute, int minutes)
            {
                string problem = Tracker.TryReserve(personId, PersonActivityKind.Resting, Day, startMinute, minutes, "rest");
                Assert.IsNull(problem, problem);
            }

            public void ReserveJobSeeking(int personId, int startMinute, int minutes)
            {
                string problem = Tracker.TryReserve(personId, PersonActivityKind.JobSeeking, Day, startMinute, minutes, "job search");
                Assert.IsNull(problem, problem);
            }

            public List<int> MemberIds() => new List<int> { 1, 2 };

            public Func<int, EntityId> PersonEntityId() => pid => EntityId.For(EntityKind.Person, pid);

            public List<PersonActivityReservation> ShoppingWindows(int personId)
            {
                // The reservation record persists after the trip releases
                // it (SinglePersonShoppingTests.FindReservation precedent:
                // no Released filter). A completed trip's window is a
                // released record; a never-planned trip leaves no record.
                var found = new List<PersonActivityReservation>();
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == Day
                        && r.Activity == PersonActivityKind.Shopping)
                    {
                        found.Add(r);
                    }
                }
                return found;
            }

            public bool HasAnyReservation(int personId)
            {
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && !r.Released && r.PersonId == personId && r.DayIndex == Day) return true;
                }
                return false;
            }
        }

        // ---------- Chooser level: the availability-first rule ----------

        [Test]
        public void SingleAdultHousehold_SoleAdultShops()
        {
            Fixture f = Fixture.Create();
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                f.Population, new List<int> { 1 }, f.Day, 480, 95,
                f.Tracker, f.Budgets, f.PersonEntityId(), out reason);
            Assert.AreEqual(1, shopper, "The sole adult in a single-adult household shops. Reason: " + reason);
        }

        [Test]
        public void EmployedWomanWithAvailableHusband_FreeHusbandShops()
        {
            Fixture f = Fixture.Create();
            f.ReserveWork(2, 480, 240); // Anna works 08:00-12:00; Tomas is free.
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                f.Population, f.MemberIds(), f.Day, 480, 95,
                f.Tracker, f.Budgets, f.PersonEntityId(), out reason);
            Assert.AreEqual(1, shopper,
                "Availability beats sex: the employed woman is on shift and her free husband shops. Reason: " + reason);
        }

        [Test]
        public void SexSwappedHousehold_ChoosesSamePerson()
        {
            // The same schedules as EmployedWomanWithAvailableHusband, with
            // the sexes swapped: Tomas is the woman, Anna the man. The
            // choice must not move — no gender-based preference anywhere.
            Fixture f = Fixture.Create();
            f.Population.people[0].isFemale = true;
            f.Population.people[1].isFemale = false;
            f.ReserveWork(2, 480, 240); // Anna (now the man) works; Tomas (now the woman) is free.
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                f.Population, f.MemberIds(), f.Day, 480, 95,
                f.Tracker, f.Budgets, f.PersonEntityId(), out reason);
            Assert.AreEqual(1, shopper,
                "Sex-swapped household chooses the same person id. Reason: " + reason);
        }

        [Test]
        public void DualEmployed_IdenticalShifts_PostShiftDepartureFirstInMemberOrder()
        {
            Fixture f = Fixture.Create();
            f.ReserveWork(1, 480, 240);
            f.ReserveWork(2, 480, 240);
            int departure;
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                f.Population, f.MemberIds(), f.Day, 480, 95, 40, 15, arrival => null,
                f.Tracker, f.Budgets, f.PersonEntityId(), out departure, out reason);
            Assert.AreEqual(1, shopper,
                "Dual-employed, identical shifts: the search steps forward to the post-shift departure and the " +
                "first member in member order shops. Reason: " + reason);
            Assert.AreEqual(720, departure, "Both shifts end at 720, so the first feasible departure is 720.");
        }

        [Test]
        public void TravelTieBreak_NearerFreeAdultWins()
        {
            // Two equally free adults: the nearer one-way wins. Both
            // directions, so the tie-break is travel, not member order.
            Fixture homeNear = Fixture.Create();
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                homeNear.Population, homeNear.MemberIds(), homeNear.Day, 480, 95,
                homeNear.Tracker, homeNear.Budgets, homeNear.PersonEntityId(), out reason,
                null, pid => pid == 1 ? 40 : 360);
            Assert.AreEqual(1, shopper, "Nearer adult (home, 40 min) wins over the remote one. Reason: " + reason);

            Fixture campNear = Fixture.Create();
            shopper = ShopperAssignmentPolicy.ChooseShopper(
                campNear.Population, campNear.MemberIds(), campNear.Day, 480, 95,
                campNear.Tracker, campNear.Budgets, campNear.PersonEntityId(), out reason,
                null, pid => pid == 1 ? 360 : 40);
            Assert.AreEqual(2, shopper, "Nearer adult (camp, 40 min) wins over the far one. Reason: " + reason);
        }

        [Test]
        public void WindowSearch_TravelTieBreak_NearerFreeAdultWinsAtEarliestDeparture()
        {
            Fixture f = Fixture.Create();
            int departure;
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                f.Population, f.MemberIds(), f.Day, 480, 95, 40, 15, arrival => null,
                f.Tracker, f.Budgets, f.PersonEntityId(), out departure, out reason,
                null, pid => pid == 1 ? 40 : 360);
            Assert.AreEqual(1, shopper, "Nearer free adult wins at the earliest departure. Reason: " + reason);
            Assert.AreEqual(480, departure, "Both are free at 08:00, so the earliest departure stands.");
        }

        [Test]
        public void NoFreeAdult_ReturnsMinusOneWithReason()
        {
            Fixture f = Fixture.Create();
            f.ReserveWork(1, 480, 600);
            f.ReserveWork(2, 480, 600);
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                f.Population, f.MemberIds(), f.Day, 480, 95,
                f.Tracker, f.Budgets, f.PersonEntityId(), out reason);
            Assert.AreEqual(-1, shopper, "Nobody free means no shopper is chosen (defer, never interrupt work).");
            Assert.IsNotNull(reason, "The deferral names the rejections.");
            StringAssert.Contains("P1", reason);
            StringAssert.Contains("P2", reason);
        }

        [Test]
        public void WindowSearch_NoWindow_ReturnsMinusOne()
        {
            Fixture f = Fixture.Create();
            f.ReserveWork(1, 480, 600);
            f.ReserveWork(2, 480, 600);
            int departure;
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                f.Population, f.MemberIds(), f.Day, 480, 95, 40, 15, arrival => null,
                f.Tracker, f.Budgets, f.PersonEntityId(), out departure, out reason);
            Assert.AreEqual(-1, shopper, "No window works when both adults work the whole day.");
            Assert.AreEqual(-1, departure);
            StringAssert.Contains("No shopper in any searched window", reason);
            StringAssert.Contains("trip needs 95 min", reason);
        }

        [Test]
        public void WindowSearch_TripTooLongForStoreHours_NamesTripAndLastDeparture()
        {
            Fixture f = Fixture.Create();
            int departure;
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                f.Population, f.MemberIds(), f.Day, 480, 735, 360, 15, arrival => null,
                f.Tracker, f.Budgets, f.PersonEntityId(), out departure, out reason);
            Assert.AreEqual(-1, shopper, "A 735-min trip cannot fit in store hours from an 08:00 departure.");
            Assert.AreEqual(-1, departure);
            StringAssert.Contains("735-min trip", reason);
            StringAssert.Contains("last feasible departure 345", reason);
        }

        [Test]
        public void ChooseShopper_PostShift_FreeAllDayMemberBeatsShiftWorker()
        {
            // The kept reservation-count score, isolated: at 720 both
            // adults are free for the trip window, but Tomas carries a
            // Work reservation from his 480-720 shift (score 20) while
            // Anna carries none (score 0). The score is one input among
            // several — it is kept, not deleted — and it prefers the adult
            // with the lighter day. (Mutation A inverts this comparison.)
            Fixture f = Fixture.Create();
            f.ReserveWork(1, 480, 240);
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopper(
                f.Population, f.MemberIds(), f.Day, 720, 95,
                f.Tracker, f.Budgets, f.PersonEntityId(), out reason);
            Assert.AreEqual(2, shopper,
                "At the post-shift departure the free-all-day adult shops. Reason: " + reason);
        }

        [Test]
        public void WindowSearch_PostShift_ShiftWorkerBeatsJobSeeker()
        {
            // The kept score, isolated at the window search: both adults
            // are blocked until 720 — Tomas by a Work shift (score 20),
            // Anna by job-seeking (score 100) — and both are free from
            // 720. The search steps to 720 and the lower score wins, so
            // the shift worker shops over the job-seeker. (Mutation A
            // inverts this comparison.)
            Fixture f = Fixture.Create();
            f.ReserveWork(1, 480, 240);
            f.ReserveJobSeeking(2, 480, 240);
            int departure;
            string reason;
            int shopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                f.Population, f.MemberIds(), f.Day, 480, 95, 40, 15, arrival => null,
                f.Tracker, f.Budgets, f.PersonEntityId(), out departure, out reason);
            Assert.AreEqual(1, shopper,
                "At the post-shift departure the shift worker (score 20) shops over the job-seeker (score 100). Reason: " + reason);
            Assert.AreEqual(720, departure, "Both are blocked until their 480-720 commitments end.");
        }

        // ---------- Loop level: single-need path (ExecuteNeed) ----------

        [Test]
        public void ExecuteNeed_EmployedWomanWithAvailableHusband_HusbandShopsAndBuys()
        {
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.ReserveWork(2, 480, 240); // Anna works 08:00-12:00; Tomas is free.
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsTrue(result.Success, "Trip succeeds. Reason: " + result.FailureReason);
            Assert.AreEqual(1, result.ShopperPersonId, "The free husband shops, not the employed woman.");
            Assert.AreEqual(2, result.UnitsAcquired);
            Assert.AreEqual(0, f.ShoppingWindows(2).Count, "Anna holds no shopping reservation.");
        }

        [Test]
        public void ExecuteNeed_RemoteOnlyShopper_FailsHonestlyOnCorrectedTrip()
        {
            // Tomas (home) works all day; Anna is the only candidate and
            // she is at the forest camp. The trip is priced from HER
            // actual location (735 min), not from home (95 min): it does
            // not fit her 600-min budget, so the need stays open and no
            // reservation is held. This is the QA-24 defer-and-retry case
            // at the single-need path.
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.ReserveWork(1, 480, 600);
            f.PersonLocations[2] = "forest-camp";
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsFalse(result.Success, "The 735-min workplace trip cannot be planned.");
            StringAssert.Contains("735", result.FailureReason,
                "The failure names the corrected trip length. Reason: " + result.FailureReason);
            Assert.AreEqual(0, f.ShoppingWindows(1).Count, "Tomas holds no shopping reservation.");
            Assert.AreEqual(0, f.ShoppingWindows(2).Count, "Anna holds no shopping reservation.");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The need stays open for a later day.");
        }

        [Test]
        public void ExecuteNeed_NearerFreeAdultShopsFromActualLocation()
        {
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.PersonLocations[2] = "forest-camp"; // Anna is remote; Tomas is home and free.
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsTrue(result.Success, "Trip succeeds. Reason: " + result.FailureReason);
            Assert.AreEqual(1, result.ShopperPersonId, "The nearer free adult (Tomas, home) shops.");
            Assert.AreEqual(2, result.UnitsAcquired);
            Assert.AreEqual(0, f.ShoppingWindows(2).Count, "Anna holds no shopping reservation.");
            Assert.AreEqual("forest-camp", f.PersonLocations[2], "Anna never moved from the camp.");
            Assert.IsTrue(f.Transitions.Contains("1:home->store"), "Tomas departs from home, his actual location.");
            Assert.IsTrue(f.Transitions.Contains("1:store->home"), "Tomas returns home.");
            foreach (string t in f.Transitions)
            {
                Assert.IsFalse(t.StartsWith("2:"), "No leg ever departs from Anna's location: " + t);
            }
        }

        // ---------- Loop level: live path (PlanTrip) ----------

        [Test]
        public void PlanTrip_RemoteOnlyShopper_FailsHonestlyOnCorrectedTrip()
        {
            // Same setup as the ExecuteNeed remote case, through PlanTrip
            // (the live path: DriveEmbodiedShopping -> ShoppingTripDriver
            // -> PlanTrip). The corrected 735-min trip cannot fit between
            // the 08:00 departure and close/sleep, so the trip fails with
            // the window wording and the need stays open.
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.ReserveWork(1, 480, 600);
            f.PersonLocations[2] = "forest-camp";
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, f.Day, f.Options());
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State,
                "The trip fails instead of planning a leg from home. Reason: " + trip.FailureReason);
            StringAssert.Contains("735-min trip", trip.FailureReason);
            StringAssert.Contains("last feasible departure 345", trip.FailureReason);
            Assert.AreEqual(0, f.ShoppingWindows(1).Count, "Tomas holds no shopping reservation.");
            Assert.AreEqual(0, f.ShoppingWindows(2).Count, "Anna holds no shopping reservation.");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The need stays open for a later day.");
        }

        [Test]
        public void PlanTrip_NearerFreeAdultShopsFromActualLocation()
        {
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.PersonLocations[2] = "forest-camp"; // Anna is remote; Tomas is home and free.
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, f.Day, f.Options());
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State,
                "The trip plans. Reason: " + trip.FailureReason);
            Assert.AreEqual(1, trip.ShopperPersonId, "The nearer free adult (Tomas, home) shops.");
            Assert.AreEqual(480, trip.DepartureMinuteOfDay, "Both are free at 08:00.");
            Assert.AreEqual("home", trip.HomeLocationId, "The trip departs from the shopper's actual location.");
            Assert.IsNotNull(trip.Legs, "Legs are planned for the chosen shopper.");
            Assert.IsFalse(f.HasAnyReservation(2), "Anna holds no reservation of any kind.");
        }

        // ---------- Loop level: batch path (coordinator + ExecuteBatch) ----------

        [Test]
        public void ExecuteBatch_DualEmployedIdenticalShifts_PostShiftShopperIsFirstInMemberOrder()
        {
            // Batch-path replication of the SameShift scenario: both adults
            // work 08:00-12:00, so the window search steps to the 720
            // departure and the first member in member order shops. This
            // pins the expectation the SinglePersonShoppingTests SameShift
            // rewrite carries (shopper 1, departure 720, end 815).
            Fixture f = Fixture.Create();
            f.Fund(5000);
            f.ReserveWork(1, 480, 240);
            f.ReserveWork(2, 480, 240);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 2);
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count, "One plan claims the one open need.");
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);
            Assert.IsTrue(batch.TripExecuted, "The batch trip executes. Reason: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ShopperPersonId, "First member in member order shops after both shifts end.");
            List<PersonActivityReservation> windows = f.ShoppingWindows(1);
            Assert.AreEqual(1, windows.Count, "Tomas holds exactly one shopping window.");
            Assert.AreEqual(720, windows[0].StartMinuteOfDay, "The trip departs at 720, after both shifts.");
            Assert.AreEqual(815, windows[0].EndMinuteOfDay, "The 95-min trip ends at 815.");
            Assert.AreEqual(0, f.ShoppingWindows(2).Count, "Anna holds no shopping reservation.");
        }
    }
}
