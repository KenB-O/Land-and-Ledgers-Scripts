using System.Collections.Generic;
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
    /// Campaign B package 15 (DEF-019): the household fuel reserve credit
    /// link, wired into the LIVE receipt paths.
    ///
    /// Live-path verdict (verified in the B tree, see the package 15
    /// checkpoint): the Phase C HouseholdShoppingLoop is the sole purchase
    /// authority in production — DailyNeedsService.ExecuteDay returns early
    /// on a null executor with the loop bound, and SimulationDrivers only
    /// hands the legacy EmbodiedPurchaseExecutor to it when the loop is
    /// null (a degraded/bootstrap context). The legacy executor's inline
    /// 1:1 reserve credit is therefore NOT the live path. The live
    /// receipts are the real-time driver's ReceiveAtHome (carried) and
    /// ReceiveDelivery (arranged delivery), which booked lots only. This
    /// package wires Series2HouseholdFuelReserveLink.CreditReserve into
    /// both, for fuel items, at the legacy 1:1 precedent.
    ///
    /// The synchronous ExecuteNeed / ExecuteBatch path (ReceiveSettledGoods)
    /// already credits reserves through
    /// HouseholdInventoryReserveBridge.MirrorLotReceiptToReserve — but its
    /// only production caller is HouseholdErrandCoordinator.ExecutePlan,
    /// whose PlanDay / ExecutePlan have no production callers (the hub
    /// only save/loads the coordinator's plans). That path is NOT driven
    /// in production, so the mirror there does not cover live fuel
    /// receipts. These tests drive the driver paths directly, with
    /// fabricated trips and batches, and pin the 1:1 credit, the fuel-only
    /// gate, the household-units basis, and the exactly-once property.
    ///
    /// End-to-end proof (a household buys fuel and the heating reserve
    /// credits in a run) waits on DEF-003: no fuel can be bought until the
    /// dealer can sell it. Nothing here claims it.
    /// </summary>
    [TestFixture]
    public sealed class CampaignBFuelReserveCreditTests
    {
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
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0;

            public static Fixture Create()
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
                var child = new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 };
                f.Population.people.Add(child);
                regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = 7, StartDayIndex = 0 });
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

                // No store port is registered: these tests drive the receipt
                // methods directly with fabricated trips and batches, so no
                // purchase is executed and no seller is needed.
                f.Directory = new SupplierDirectory();

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

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.ReasonSummary = "test need";
                return need;
            }

            public HouseholdReserveState FuelReserve()
            {
                HouseholdState household = Population.GetHousehold(HouseholdId);
                Assert.NotNull(household, "H7 must exist in the fixture population.");
                HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
                Assert.NotNull(reserve, "H7 must hold a fuel_wood reserve.");
                return reserve;
            }

            public HouseholdReserveState StapleFoodReserve()
            {
                HouseholdState household = Population.GetHousehold(HouseholdId);
                Assert.NotNull(household, "H7 must exist in the fixture population.");
                household.EnsureHouseholdReservesInitialized(migrateLegacyFoodReserve: true);
                HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);
                Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
                return reserve;
            }
        }

        private static CarriedGoodsBatch MakeBatch(string batchId, string itemId, int units,
            GoodsCustodyState custody, int packageSize = 1)
        {
            return new CarriedGoodsBatch
            {
                BatchId = batchId,
                HouseholdId = 7,
                HolderPersonId = 1,
                ItemId = itemId,
                Units = units,
                UnitPriceCents = 10,
                ReceiptId = "rc-" + batchId,
                Provenance = "test seller",
                Custody = custody,
                PurchaseDayIndex = 0,
                HouseholdUnitsPerStoreUnit = packageSize,
                SupplierName = "General Store",
            };
        }

        private static PlannedShoppingTrip MakeTrip(Fixture f, HouseholdPurchasingNeed need,
            CarriedGoodsBatch batch, string tripId)
        {
            if (batch != null && need != null)
            {
                batch.NeedSequence = need.NeedSequence;
                batch.HasNeedIdentity = true;
            }
            return new PlannedShoppingTrip
            {
                TripId = tripId,
                Need = need,
                HouseholdId = f.HouseholdId,
                ShopperPersonId = 1,
                DayIndex = f.Day,
                HomeLocationId = "home",
                Options = f.Options(),
                Result = new ShoppingTripResult
                {
                    NeedSequence = need.NeedSequence,
                    HouseholdId = f.HouseholdId,
                    ShopperPersonId = 1,
                    ItemId = need.ItemId,
                },
                State = PlannedShoppingTrip.TripState.Returning,
                Batch = batch,
            };
        }

        private static bool DiagContains(List<string> diag, string fragment)
        {
            foreach (string line in diag)
            {
                if (!string.IsNullOrEmpty(line) && line.Contains(fragment)) return true;
            }

            return false;
        }

        [Test]
        public void ReceiveAtHome_Fuel_CreditsReserveOneToOne()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            CarriedGoodsBatch batch = MakeBatch("b-fuel-1", HouseholdItemCatalog.FuelWoodId, 6, GoodsCustodyState.InHand);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-fuel-1");

            string error = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);

            Assert.IsNull(error, error);
            Assert.IsTrue(trip.Result.Success, trip.Result.FailureReason);
            Assert.AreEqual(6, trip.Result.UnitsAcquired);
            Assert.AreEqual(6, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "The receipt books 6u of fuel lots in the household.");
            Assert.AreEqual(6, reserve.currentUnits,
                "The fuel reserve rises by exactly the units received (1:1, the legacy executor precedent).");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsTrue(DiagContains(f.Diagnostics, "credited 6 fuel_wood reserve unit(s)"),
                "The credit comes from Series2HouseholdFuelReserveLink, not from the lot mirror (which logs no such line).");
        }

        [Test]
        public void ReceiveCarriedBatch_OldPurchaseCannotFulfillLaterSameSkuNeed()
        {
            Fixture f = Fixture.Create();
            HouseholdPurchasingNeed purchasedNeed = f.OpenNeed(HouseholdItemCatalog.BreadId, 4);
            int purchasedNeedSequence = purchasedNeed.NeedSequence;
            purchasedNeed.Status = PurchasingNeedStatus.Cancelled;
            HouseholdPurchasingNeed laterNeed = f.OpenNeed(HouseholdItemCatalog.BreadId, 4);
            Assert.AreNotEqual(purchasedNeedSequence, laterNeed.NeedSequence);

            CarriedGoodsBatch batch = MakeBatch("b-old-bread", HouseholdItemCatalog.BreadId, 4,
                GoodsCustodyState.InHand);
            batch.NeedSequence = purchasedNeedSequence;
            batch.HasNeedIdentity = true;
            Assert.IsNotNull(f.Loop.AttachRestoredBatch(batch));

            string refusal = f.Loop.ReceiveCarriedBatch(batch.BatchId, f.Day, f.Diagnostics);

            Assert.IsNotNull(refusal, "a paid batch must not be assigned to a later same-SKU need");
            Assert.AreEqual(PurchasingNeedStatus.Cancelled, purchasedNeed.Status);
            Assert.AreEqual(PurchasingNeedStatus.Open, laterNeed.Status);
            Assert.AreEqual(4, laterNeed.UnitsNeeded);
            Assert.AreEqual(0, f.Inventories.GetOrCreate(f.HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count,
                "unmatched purchased goods remain in custody for explicit recovery");
        }

        [Test]
        public void ReceiveDelivery_Fuel_CreditsReserveOneToOne()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            CarriedGoodsBatch batch = MakeBatch("b-fuel-2", HouseholdItemCatalog.FuelWoodId, 6, GoodsCustodyState.ArrangedDelivery);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-fuel-2");

            string error = f.Loop.ReceiveDelivery(trip, f.Day, f.Diagnostics);

            Assert.IsNull(error, error);
            Assert.AreEqual(6, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "The delivery books 6u of fuel lots in the household.");
            Assert.AreEqual(6, reserve.currentUnits,
                "The fuel reserve rises by exactly the units received (1:1) on the arranged-delivery path too.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsTrue(DiagContains(f.Diagnostics, "credited 6 fuel_wood reserve unit(s)"),
                "The credit comes from Series2HouseholdFuelReserveLink, not from the lot mirror.");
        }

        [Test]
        public void ReceiveAtHome_NonFuel_DoesNotTouchFuelReserve()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            CarriedGoodsBatch batch = MakeBatch("b-flour-1", HouseholdItemCatalog.FlourId, 5, GoodsCustodyState.InHand);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-flour-1");

            string error = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);

            Assert.IsNull(error, error);
            Assert.IsTrue(trip.Result.Success, trip.Result.FailureReason);
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "The flour receipt still books its lots.");
            Assert.AreEqual(0, reserve.currentUnits,
                "A non-fuel receipt must not touch the fuel reserve.");
            Assert.IsTrue(!DiagContains(f.Diagnostics, "Series2HouseholdFuelReserveLink: credited"),
                "No reserve-credit diagnostic fires for a non-fuel receipt.");
        }

        [Test]
        public void ReceiveAtHome_PhysicalBreadReceipt_UpdatesReserveOnceAndKeepsItemLots()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.StapleFoodReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 8);
            CarriedGoodsBatch batch = MakeBatch("b-bread-home-1", HouseholdItemCatalog.BreadId, 4,
                GoodsCustodyState.InHand, packageSize: 2);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-bread-home-1");

            string error = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);
            Assert.IsNull(error, error);
            Assert.AreEqual(8, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "The genuine receipt creates the exact bread lot quantity.");
            Assert.AreEqual(System.Math.Min(reserve.targetUnits, 8), reserve.currentUnits,
                "The staple reserve mirrors household units received and clamps at its authored target.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);

            Assert.IsNull(f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics));
            Assert.AreEqual(8, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "A repeated callback must not create another physical lot.");
            Assert.AreEqual(System.Math.Min(reserve.targetUnits, 8), reserve.currentUnits,
                "A repeated callback must not credit the reserve twice.");
        }

        [Test]
        public void ReceiveDelivery_PhysicalBreadReceipt_UpdatesReserveOnceAndKeepsItemLots()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.StapleFoodReserve();
            reserve.currentUnits = 0;
            reserve.targetUnits = 6;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 3);
            CarriedGoodsBatch batch = MakeBatch("b-bread-delivery-1", HouseholdItemCatalog.BreadId, 3,
                GoodsCustodyState.ArrangedDelivery);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-bread-delivery-1");

            string error = f.Loop.ReceiveDelivery(trip, f.Day, f.Diagnostics);
            Assert.IsNull(error, error);
            Assert.AreEqual(3, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "The genuine delivery creates the exact bread lot quantity.");
            Assert.AreEqual(3, reserve.currentUnits, "The reserve mirrors the received household units.");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);

            Assert.IsNull(f.Loop.ReceiveDelivery(trip, f.Day, f.Diagnostics));
            Assert.AreEqual(3, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "A repeated delivery callback must not create another lot.");
            Assert.AreEqual(3, reserve.currentUnits, "A repeated delivery callback must not credit twice.");
        }

        [Test]
        public void ReceiveAtHome_Fuel_PackageSizeCreditsHouseholdUnits()
        {
            // W5: custody holds STORE units; the household is credited
            // packages x package size. The reserve credit must follow the
            // household units (the lot credit), not the store units.
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            CarriedGoodsBatch batch = MakeBatch("b-fuel-3", HouseholdItemCatalog.FuelWoodId, 3, GoodsCustodyState.InHand, packageSize: 2);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-fuel-3");

            string error = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);

            Assert.IsNull(error, error);
            Assert.AreEqual(6, trip.Result.UnitsAcquired);
            Assert.AreEqual(6, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId));
            Assert.AreEqual(6, reserve.currentUnits,
                "The reserve is credited the 6 household units received, not the 3 store units in custody.");
        }

        [Test]
        public void ReceiveAtHome_Fuel_SecondReceiptDoesNotDoubleCredit()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            CarriedGoodsBatch batch = MakeBatch("b-fuel-4", HouseholdItemCatalog.FuelWoodId, 6, GoodsCustodyState.InHand);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-fuel-4");

            string first = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);
            Assert.IsNull(first, first);
            Assert.AreEqual(6, reserve.currentUnits);

            string second = f.Loop.ReceiveAtHome(trip, f.Day, f.Diagnostics);

            Assert.IsNull(second, second);
            Assert.AreEqual(6, reserve.currentUnits,
                "The second receipt takes the already-received branch and credits nothing.");
            Assert.AreEqual(6, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId),
                "The second receipt books no second lot either.");
        }

        [Test]
        public void ReceiveDelivery_Fuel_SecondReceiptDoesNotDoubleCredit()
        {
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            CarriedGoodsBatch batch = MakeBatch("b-fuel-5", HouseholdItemCatalog.FuelWoodId, 6, GoodsCustodyState.ArrangedDelivery);
            PlannedShoppingTrip trip = MakeTrip(f, need, batch, "t-fuel-5");

            string first = f.Loop.ReceiveDelivery(trip, f.Day, f.Diagnostics);
            Assert.IsNull(first, first);
            Assert.AreEqual(6, reserve.currentUnits);

            string second = f.Loop.ReceiveDelivery(trip, f.Day, f.Diagnostics);

            Assert.IsNull(second, second);
            Assert.AreEqual(6, reserve.currentUnits,
                "The second delivery returns early (custody is no longer ArrangedDelivery) and credits nothing.");
            Assert.AreEqual(6, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId));
        }

        [Test]
        public void ReceiveAtHome_Fuel_TwoReceiptsAccumulateUnclamped()
        {
            // Unlike the AUD-335 lot mirror (clamped at target), the
            // CreditReserve link credits unclamped, exactly as the legacy
            // executor's inline reserve credit did (reserve += sold).
            // Two separate receipts accumulate: 6 + 4 = 10, whatever the
            // reserve target reads.
            Fixture f = Fixture.Create();
            HouseholdReserveState reserve = f.FuelReserve();
            reserve.currentUnits = 0;
            int target = reserve.targetUnits;

            HouseholdPurchasingNeed firstNeed = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 6);
            PlannedShoppingTrip firstTrip = MakeTrip(f, firstNeed,
                MakeBatch("b-fuel-6", HouseholdItemCatalog.FuelWoodId, 6, GoodsCustodyState.InHand), "t-fuel-6");
            string first = f.Loop.ReceiveAtHome(firstTrip, f.Day, f.Diagnostics);
            Assert.IsNull(first, first);
            Assert.AreEqual(6, reserve.currentUnits);

            HouseholdPurchasingNeed secondNeed = f.OpenNeed(HouseholdItemCatalog.FuelWoodId, 4);
            PlannedShoppingTrip secondTrip = MakeTrip(f, secondNeed,
                MakeBatch("b-fuel-7", HouseholdItemCatalog.FuelWoodId, 4, GoodsCustodyState.InHand), "t-fuel-7");
            string second = f.Loop.ReceiveAtHome(secondTrip, f.Day, f.Diagnostics);
            Assert.IsNull(second, second);

            Assert.AreEqual(10, reserve.currentUnits,
                $"Two receipts accumulate to 10u even though the fuel target reads {target} (unclamped 1:1 credit).");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FuelWoodId));
        }
    }
}
