using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Phase B (Real People): real household inventory (B2), real food and
    /// meals (B3), shortage detection (B4), person/household distinctions (B5).
    /// </summary>
    [TestFixture]
    public sealed class PhaseBHouseholdConsumptionTests
    {
        // ---------- B2: real household inventory ----------

        [Test]
        public void Inventory_AddConsume_ConservationHolds()
        {
            var inventory = new HouseholdInventory(51);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 6, 1, "general store purchase"));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 4, 2, "general store purchase"));

            var records = new List<HouseholdConsumptionRecord>();
            Assert.IsNull(inventory.Consume(HouseholdItemCatalog.FlourId, 4, 3, "meal", "P1", records));

            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "Consumption removes real quantities: 10 - 4 = 6.");
            int totalConsumed = 0;
            foreach (HouseholdConsumptionRecord r in records) totalConsumed += r.QuantityUnits;
            Assert.AreEqual(4, totalConsumed, "Consumption records trace the exact quantities.");
            // FIFO: oldest lot (lot 0, 6 units) consumed first.
            Assert.AreEqual(0, records[0].LotId);
        }

        [Test]
        public void Inventory_RejectsSourcelessUnknownAndNonPositive()
        {
            var inventory = new HouseholdInventory(52);

            Assert.NotNull(inventory.AddLot("no_such_item", 5, 1, "store"), "Unknown items rejected.");
            Assert.NotNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 5, 1, ""), "Sourceless lots rejected (no synthetic stock).");
            Assert.NotNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 0, 1, "store"), "Non-positive quantities rejected.");
            Assert.AreEqual(0, inventory.Lots.Count, "Rejections book nothing.");
        }

        [Test]
        public void Inventory_Hold_PreventsDoubleAvailability()
        {
            var inventory = new HouseholdInventory(53);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, 1, "mill purchase"));

            Assert.IsNull(inventory.HoldForCommitment(HouseholdItemCatalog.FlourId, 6, "shipment-1"));
            Assert.AreEqual(4, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "Held units are not available for consumption.");
            Assert.AreEqual(10, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId),
                "Held units still exist in the household.");

            var records = new List<HouseholdConsumptionRecord>();
            Assert.NotNull(inventory.Consume(HouseholdItemCatalog.FlourId, 5, 2, "meal", "P1", records),
                "Cannot consume held units.");
            Assert.IsNull(inventory.Consume(HouseholdItemCatalog.FlourId, 4, 2, "meal", "P1", records));

            inventory.ReleaseHold(HouseholdItemCatalog.FlourId, 6);
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "Released holds return to the consumable pool.");

            Assert.IsNull(inventory.HoldForCommitment(HouseholdItemCatalog.FlourId, 6, "shipment-1"));
            Assert.IsNull(inventory.FulfillCommitment(HouseholdItemCatalog.FlourId, 6, "shipment-1"));
            Assert.AreEqual(0, inventory.GetTotalUnits(HouseholdItemCatalog.FlourId),
                "Fulfilled commitments leave the household permanently.");
        }

        [Test]
        public void Inventory_DaysOfSupply_IsDerivedReportOnly()
        {
            var inventory = new HouseholdInventory(54);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 20, 1, "mill purchase"));

            float before = inventory.EstimateDaysOfSupply(HouseholdItemCatalog.FlourId, 4);
            Assert.AreEqual(5f, before, 0.001f);
            inventory.Consume(HouseholdItemCatalog.FlourId, 4, 2, "meal", "P1", new List<HouseholdConsumptionRecord>());
            float after = inventory.EstimateDaysOfSupply(HouseholdItemCatalog.FlourId, 4);
            Assert.AreEqual(4f, after, 0.001f, "The estimate derives from lots; it is never a stored balance.");
        }

        [Test]
        public void Bridge_SeedLotsFromReserves_IsOnceAndHonest()
        {
            var household = new HouseholdState { id = 55 };
            household.EnsureHouseholdReservesInitialized();
            HouseholdReserveState staple = household.GetReserve("staple_food");
            staple.currentUnits = 8;
            var inventory = new HouseholdInventory(55);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 9, new List<string>());

            Assert.AreEqual(8, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "Legacy staple units seed honestly as unspecified, never invented as flour/bread.");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.IsTrue(inventory.State.SeededFromReserves);

            HouseholdInventoryReserveBridge.SeedLotsFromReserves(household, inventory, 10, new List<string>());
            Assert.AreEqual(8, inventory.GetAvailableUnits(HouseholdItemCatalog.UnspecifiedStapleFoodId),
                "Seeding is idempotent: a second pass adds nothing.");
            Assert.AreEqual(1, inventory.Lots.Count, "No duplicate lots.");
        }

        // ---------- B3: real food and meals ----------

        [Test]
        public void Meals_ServedFromRealLots_WithTraceableRecords()
        {
            var household = new HouseholdState { id = 61 };
            var inventories = new HouseholdInventoryRegistry();
            var mealLog = new HouseholdMealLogRegistry();
            HouseholdInventory inventory = inventories.GetOrCreate(61);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "general store purchase"));

            var policy = new MealSchedulingPolicy { MiddayMealEnabled = false };
            var service = new HouseholdMealService(mealLog);
            MealDayResult result = service.ServeHouseholdDay(
                household, new List<int> { 1, 2 }, inventory, policy, 5, null);

            Assert.AreEqual(4, result.MealsServed, "2 members x 2 substantial meals.");
            Assert.AreEqual(0, result.MealsMissed);
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "4 loaves consumed from real lots.");

            IReadOnlyList<HouseholdMealRecord> records = mealLog.GetMeals(61);
            Assert.AreEqual(4, records.Count);
            HouseholdMealRecord first = records[0];
            Assert.AreEqual(1, first.PersonId);
            Assert.AreEqual(61, first.HouseholdId);
            Assert.AreEqual(5, first.DayIndex);
            Assert.AreEqual(MealSourceKind.HouseholdInventory, first.SourceKind);
            Assert.AreEqual(1, first.ItemsConsumed.Count);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, first.ItemsConsumed[0].ItemId);
            Assert.AreEqual(1, first.ItemsConsumed[0].QuantityUnits);
            Assert.AreEqual(MealAdequacy.Substantial, first.Adequacy);
            Assert.IsNotEmpty(first.ResultingInventory, "Each meal snapshots resulting inventory.");
        }

        [Test]
        public void Meals_NoFood_NoMealRecord_HardshipInstead()
        {
            var household = new HouseholdState { id = 62 };
            var mealLog = new HouseholdMealLogRegistry();
            var inventory = new HouseholdInventory(62);
            var service = new HouseholdMealService(mealLog);

            MealDayResult result = service.ServeHouseholdDay(
                household, new List<int> { 3 }, inventory, MealSchedulingPolicy.Default, 6, null);

            Assert.AreEqual(0, result.MealsServed);
            Assert.IsEmpty(mealLog.GetMeals(62), "No food => NO successful meal is recorded.");
            IReadOnlyList<MissedMealRecord> missed = mealLog.GetMissedMeals(62);
            Assert.IsNotEmpty(missed, "Shortage produces hardship records, never invisible replenishment.");
            Assert.AreEqual("no food available in household inventory", missed[0].Reason);
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void Meals_OutOfHomeMeals_RecordedWithoutConsumingInventory()
        {
            var household = new HouseholdState { id = 63 };
            var mealLog = new HouseholdMealLogRegistry();
            var inventory = new HouseholdInventory(63);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "general store purchase"));
            var service = new HouseholdMealService(mealLog);

            var outOfHome = new Dictionary<int, OutOfHomeMeals>
            {
                { 4, new OutOfHomeMeals { MealsEaten = 2, SourceKind = MealSourceKind.BoardingArrangement } },
            };
            MealDayResult result = service.ServeHouseholdDay(
                household, new List<int> { 4 }, inventory,
                new MealSchedulingPolicy { MiddayMealEnabled = false }, 7, outOfHome);

            Assert.AreEqual(2, result.MealsServed);
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "Boarding-house meals consume nothing from the household larder.");
            IReadOnlyList<HouseholdMealRecord> records = mealLog.GetMeals(63);
            Assert.AreEqual(2, records.Count);
            Assert.AreEqual(MealSourceKind.BoardingArrangement, records[0].SourceKind,
                "Out-of-home meals are recorded with their true source.");
            Assert.IsEmpty(records[0].ItemsConsumed, "No lots consumed for meals eaten elsewhere.");
        }

        [Test]
        public void PrepareFromRaw_RequiresRealIngredientsEquipmentAndLabor()
        {
            var mealLog = new HouseholdMealLogRegistry();
            var service = new HouseholdMealService(mealLog);
            var inventory = new HouseholdInventory(64);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 10, 1, "mill purchase"));

            // Missing equipment: rejected, flour untouched.
            Assert.NotNull(service.PrepareFromRaw(
                inventory, HouseholdItemCatalog.FlourId, HouseholdItemCatalog.BreadId,
                2, 3, "", 30, 8, "P1"));
            Assert.AreEqual(10, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));

            // Insufficient flour: rejected.
            Assert.NotNull(service.PrepareFromRaw(
                inventory, HouseholdItemCatalog.FlourId, HouseholdItemCatalog.BreadId,
                2, 10, "bake oven", 60, 8, "P1"));

            // Real conversion: 3 loaves from 6 lb flour, named equipment + labor.
            Assert.IsNull(service.PrepareFromRaw(
                inventory, HouseholdItemCatalog.FlourId, HouseholdItemCatalog.BreadId,
                2, 3, "bake oven", 60, 8, "P1"));
            Assert.AreEqual(4, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(3, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));

            IReadOnlyList<MealPreparationRecord> preps = mealLog.GetPreparations(64);
            Assert.AreEqual(1, preps.Count);
            Assert.AreEqual(6, preps[0].RawUnitsConsumed);
            Assert.AreEqual(3, preps[0].PreparedUnitsProduced);
            Assert.AreEqual("bake oven", preps[0].EquipmentLabel);
            Assert.AreEqual(60, preps[0].LaborMinutes);
            HouseholdItemLot breadLot = null;
            foreach (HouseholdItemLot lot in inventory.Lots)
            {
                if (lot != null && lot.ItemId == HouseholdItemCatalog.BreadId) breadLot = lot;
            }
            Assert.NotNull(breadLot, "The prepared bread lot exists with preparation provenance.");
            Assert.AreEqual("meal preparation from flour (bake oven, 60 min labor)", breadLot.SourceLabel);
        }

        [Test]
        public void MealPolicy_IsConfigurable_NotUniversalConstant()
        {
            var household = new HouseholdState { id = 65 };
            var mealLog = new HouseholdMealLogRegistry();
            var inventory = new HouseholdInventory(65);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 1, "store"));

            var policy = new MealSchedulingPolicy
            {
                SubstantialMealSlots = new List<MealSlot> { MealSlot.Evening },
                MiddayMealEnabled = false,
            };
            var service = new HouseholdMealService(mealLog);
            MealDayResult result = service.ServeHouseholdDay(
                household, new List<int> { 5 }, inventory, policy, 9, null);

            Assert.AreEqual(1, result.MealsServed, "Policy configured to one substantial meal serves one.");
            Assert.AreEqual(9, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void MealPolicy_MiddayMeal_SkippedWhenStoresLow()
        {
            var household = new HouseholdState { id = 66 };
            var mealLog = new HouseholdMealLogRegistry();
            var inventory = new HouseholdInventory(66);
            // 2 loaves: plenty for 2 substantial meals, not enough for midday per the threshold.
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 2, 1, "store"));

            var policy = new MealSchedulingPolicy { MiddayMealSupplyThresholdDays = 3f };
            var service = new HouseholdMealService(mealLog);
            MealDayResult result = service.ServeHouseholdDay(
                household, new List<int> { 6 }, inventory, policy, 10, null);

            Assert.AreEqual(2, result.MealsServed, "Midday skipped when staple supply is below threshold.");
            Assert.AreEqual(0, result.MealsMissed,
                "The policy-skipped midday is a deliberate conservation decision, not a failed serving.");
            Assert.AreEqual(0, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId));
        }

        // ---------- B4: shortage detection ----------

        [Test]
        public void Shortage_LowSupply_CreatesPurchasingNeed_NotASale()
        {
            var needs = new HouseholdNeedRegistry();
            var monitor = new HouseholdShortageMonitor(needs);
            var household = new HouseholdState { id = 71 };
            var inventory = new HouseholdInventory(71);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 4, 1, "mill purchase"));

            var expectedUse = new Dictionary<string, int> { { HouseholdItemCatalog.FlourId, 8 } };
            monitor.Evaluate(household, inventory, expectedUse, false,
                new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true }, 0, 0, 11);

            HouseholdPurchasingNeed need = needs.GetOpenNeed(71, HouseholdItemCatalog.FlourId);
            Assert.NotNull(need, "Inadequate reserves create a REAL purchasing need.");
            Assert.Greater(need.UnitsNeeded, 0);
            Assert.IsTrue(need.Urgency01 > 0f, "Urgency is positive for a real shortfall.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(need.ReasonSummary));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            // Need is not a sale: it names no store, carries no money, posts no revenue.
            Assert.IsFalse(need.ReasonSummary.Contains("general store"),
                "The need is demand; no counterparty sale is implied.");
        }

        [Test]
        public void Shortage_AdequateSupply_NoNeed()
        {
            var needs = new HouseholdNeedRegistry();
            var monitor = new HouseholdShortageMonitor(needs);
            var household = new HouseholdState { id = 72 };
            var inventory = new HouseholdInventory(72);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 200, 1, "mill purchase"));

            monitor.Evaluate(household, inventory,
                new Dictionary<string, int> { { HouseholdItemCatalog.FlourId, 8 } },
                false, new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true }, 0, 0, 12);

            Assert.IsNull(needs.GetOpenNeed(72, HouseholdItemCatalog.FlourId));
            Assert.IsEmpty(needs.GetOpenNeeds(72));
        }

        [Test]
        public void Shortage_ReEvaluation_UpdatesSingleNeed_NoDuplicates()
        {
            var needs = new HouseholdNeedRegistry();
            var monitor = new HouseholdShortageMonitor(needs);
            var household = new HouseholdState { id = 73 };
            var inventory = new HouseholdInventory(73);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 4, 1, "mill purchase"));
            var expectedUse = new Dictionary<string, int> { { HouseholdItemCatalog.FlourId, 8 } };
            var access = new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true };

            monitor.Evaluate(household, inventory, expectedUse, false, access, 0, 0, 13);
            int firstNeedUnits = needs.GetOpenNeed(73, HouseholdItemCatalog.FlourId).UnitsNeeded;

            inventory.Consume(HouseholdItemCatalog.FlourId, 2, 14, "meal", "P1", new List<HouseholdConsumptionRecord>());
            monitor.Evaluate(household, inventory, expectedUse, false, access, 0, 0, 14);
            HouseholdPurchasingNeed updated = needs.GetOpenNeed(73, HouseholdItemCatalog.FlourId);

            Assert.AreEqual(1, needs.GetOpenNeeds(73).Count, "Re-evaluation updates the open need; never duplicates.");
            Assert.Greater(updated.UnitsNeeded, firstNeedUnits, "Deepening shortage grows the need.");
            Assert.IsTrue(updated.Urgency01 > 0f, "Urgency stays positive.");
        }

        [Test]
        public void Shortage_Restocked_FulfillsNeed()
        {
            var needs = new HouseholdNeedRegistry();
            var monitor = new HouseholdShortageMonitor(needs);
            var household = new HouseholdState { id = 74 };
            var inventory = new HouseholdInventory(74);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 4, 1, "mill purchase"));
            var expectedUse = new Dictionary<string, int> { { HouseholdItemCatalog.FlourId, 8 } };
            var access = new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true };

            monitor.Evaluate(household, inventory, expectedUse, false, access, 0, 0, 15);
            Assert.NotNull(needs.GetOpenNeed(74, HouseholdItemCatalog.FlourId));

            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 200, 16, "mill purchase"));
            monitor.Evaluate(household, inventory, expectedUse, false, access, 0, 0, 16);

            Assert.IsNull(needs.GetOpenNeed(74, HouseholdItemCatalog.FlourId), "Restocked reserves fulfill the need.");
            Assert.IsEmpty(needs.GetOpenNeeds(74));
        }

        [Test]
        public void Shortage_SuggestedResponses_MatchActualOptions()
        {
            var needs = new HouseholdNeedRegistry();
            var monitor = new HouseholdShortageMonitor(needs);
            var household = new HouseholdState { id = 75 };
            var inventory = new HouseholdInventory(75);
            var expectedUse = new Dictionary<string, int> { { HouseholdItemCatalog.BreadId, 8 } };

            // No cash, no store, but can produce: purchase must not be suggested.
            monitor.Evaluate(household, inventory, expectedUse, false,
                new HouseholdSupplyAccess { HasGeneralStore = false, HasCash = false, CanProduce = true },
                0, 0, 17);
            HouseholdPurchasingNeed need = needs.GetOpenNeed(75, HouseholdItemCatalog.BreadId);
            Assert.NotNull(need);
            Assert.IsFalse(need.SuggestedResponses.Contains("purchase"), "No cash/store => no purchase response.");
            Assert.IsTrue(need.SuggestedResponses.Contains("produce"));
            Assert.IsTrue(need.SuggestedResponses.Contains("go without"));

            // Cash + store, strained by obligations: credit is legitimate, purchase is conditional.
            var needs2 = new HouseholdNeedRegistry();
            var monitor2 = new HouseholdShortageMonitor(needs2);
            var household2 = new HouseholdState { id = 76 };
            monitor2.Evaluate(household2, new HouseholdInventory(76), expectedUse, false,
                new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true },
                upcomingObligationCents: 5000, expectedIncomeCents: 1000, dayIndex: 17);
            HouseholdPurchasingNeed need2 = needs2.GetOpenNeed(76, HouseholdItemCatalog.BreadId);
            Assert.IsTrue(need2.SuggestedResponses.Contains("legitimate credit (documented obligation)"));
        }

        // ---------- B5: person/household distinctions ----------

        [Test]
        public void Person_ChangesEmployerAndLocation_IdentityPreserved()
        {
            var memberships = new HouseholdMembershipRegistry();
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = 81, HouseholdId = 91, StartDayIndex = 0,
            }));

            var employments = new EmploymentRelationshipRegistry();
            var first = new EmploymentRelationship
            {
                Id = "emp-first", EmployeePersonId = 81, EmployerBusinessId = "mill-1",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(900, "test"),
                Kind = EmploymentKind.Permanent, Source = EmploymentSource.Authored,
            };
            Assert.IsNull(employments.Register(first));

            // The person changes employer: end the old relationship, register the new one.
            first.LifecycleState = EmploymentLifecycleState.Ended;
            first.EndDayIndex = 20;
            var second = new EmploymentRelationship
            {
                Id = "emp-second", EmployeePersonId = 81, EmployerBusinessId = "smithy-2",
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(1100, "test"),
                Kind = EmploymentKind.Permanent, Source = EmploymentSource.Authored,
                StartDayIndex = 21,
            };
            Assert.IsNull(employments.Register(second));

            Assert.AreEqual(91, memberships.GetActiveHouseholdId(81),
                "Changing employer never recreates the person or moves their household.");
            Assert.AreEqual(1, memberships.GetActiveMembers(91).Count);
            List<EmploymentRelationship> activeEmployments = employments.GetActiveByEmployee(81);
            Assert.AreEqual(1, activeEmployments.Count);
            Assert.AreEqual("smithy-2", activeEmployments[0].EmployerBusinessId,
                "The person now works for the new employer under the same identity.");
        }

        [Test]
        public void Membership_Transfer_EndsOld_StartsNew_PersonUnchanged()
        {
            var memberships = new HouseholdMembershipRegistry();
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 10, PersonId = 82, HouseholdId = 92, StartDayIndex = 0,
            }));

            Assert.IsNull(memberships.TransferMembership(82, 93, 30, 11), "Transfer succeeds (null = no diagnostic).");

            Assert.AreEqual(93, memberships.GetActiveHouseholdId(82));
            Assert.IsEmpty(memberships.GetActiveMembers(92), "The old household no longer lists the person.");
            Assert.AreEqual(1, memberships.GetActiveMembers(93).Count);
        }

        [Test]
        public void Household_NeedNotBeMarriedCouple_BoardersAndSinglesSupported()
        {
            var memberships = new HouseholdMembershipRegistry();

            // Single-person household.
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 20, PersonId = 83, HouseholdId = 94, StartDayIndex = 0,
            }));

            // Host household with a boarder (non-kin): membership is in the
            // host household; the boarding-house lodging itself is a separate
            // occupancy fact on the household, not a second membership.
            var hostHousehold = new HouseholdState
            {
                id = 95,
                hostsBoarders = true,
                isBoardingHouseLodging = true,
                boardingBusinessInstanceId = "boarding-house-1",
            };
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 21, PersonId = 84, HouseholdId = 95, StartDayIndex = 0,
            }));

            Assert.AreEqual(1, memberships.GetActiveMembers(94).Count, "Single-person household is valid.");
            Assert.AreEqual(95, memberships.GetActiveHouseholdId(84), "Boarder is a member of the host household.");
            Assert.AreEqual("boarding-house-1", hostHousehold.boardingBusinessInstanceId,
                "Accommodation (boarding-house lodging) is separate from membership.");
        }

        [Test]
        public void OffMapWorker_PreservesIdentityAndEconomicState()
        {
            // A worker with an off-map employer has no EmploymentRelationship,
            // but keeps person identity, household membership, and ledger.
            var memberships = new HouseholdMembershipRegistry();
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 30, PersonId = 85, HouseholdId = 96, StartDayIndex = 0,
            }));

            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger ledger = ledgers.GetOrCreate(96);
            Assert.IsNull(ledger.RecordInflow(21, 1500,
                HouseholdIncomeSource.OutsideEmployerPayment, "off-map-mill",
                "wages from off-map employer for P85", "off-map employer"));

            Assert.AreEqual(96, memberships.GetActiveHouseholdId(85),
                "Off-map simulation preserves identity and household.");
            Assert.AreEqual(1500, ledger.GetBalanceCents(),
                "Off-map wages land in the household ledger with provenance.");
            Assert.AreEqual(HouseholdIncomeSource.OutsideEmployerPayment, ledger.Entries[0].Source);
        }
    }
}
