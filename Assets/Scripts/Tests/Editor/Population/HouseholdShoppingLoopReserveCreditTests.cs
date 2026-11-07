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
            // Plan 08 Slice 0 discriminator, REPAIRED FORM in Slice 1:
            // the loop receipt now credits the reserve by the units
            // received, so the reserve reads 5 and the deficit shrinks to
            // 3 (target 8 less 5 received). The plan's "deficit zero"
            // wording does not hold for this 5u receipt against the
            // catalog target of 8; the zero-deficit case is the new
            // LoopReceipt_ClosesDeficitSoBuildShoppingNeedsReportsNone
            // below. Note the credited 5u sits exactly at the staple
            // buying threshold (ceil(8 x 0.6) = 5), so BuildShoppingNeeds
            // raises no need even though a 3u deficit remains — no re-buy
            // fires. Only the two named assertions changed here.
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
            Assert.AreEqual(5, reserve.currentUnits,
                "Slice 1 repaired form: the loop receipt credits the reserve by the units received.");
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(!HasStapleFoodNeed(needsAfter),
                "Slice 1 repaired form: the credited reserve sits at the buying threshold, so no staple_food need remains and no re-buy fires.");
            Assert.AreEqual(3, reserve.targetUnits - reserve.currentUnits,
                "Slice 1 repaired form: the deficit is the target less the credited receipt (8 - 5 = 3).");
        }

        [Test]
        public void LoopReceipt_SecondReceiptStillLeavesReserveUnchanged()
        {
            // Plan 08 Slice 0 discriminator, REPAIRED FORM in Slice 1:
            // the first receipt credits 5u and the second receipt credits
            // the remaining 3u up to the target, clamped at 8, so the
            // deficit closes and no staple_food need remains. Only the two
            // named assertions changed here.
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
            Assert.AreEqual(8, reserve.currentUnits,
                "Slice 1 repaired form: the second receipt credits the reserve to the target (clamped at 8).");
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(!HasStapleFoodNeed(needsAfter),
                "Slice 1 repaired form: two receipts close the deficit, so no staple_food need remains.");
        }

        // ---------- Slice 1: AUD-335 repair (loop receipt credits reserves) ----------

        [Test]
        public void LoopReceipt_CreditsReserveByUnitsReceived()
        {
            // Plan 08 Slice 1: a 5u flour receipt credits the mapped
            // staple_food reserve by exactly the units received.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, result.UnitsAcquired);
            Assert.AreEqual(5, reserve.currentUnits, "The reserve rises by exactly the units received.");
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(target - 5, reserve.targetUnits - reserve.currentUnits,
                "The deficit shrinks by exactly the credited receipt.");
        }

        [Test]
        public void LoopReceipt_PartialFillCreditsOnlyReceived()
        {
            // Plan 08 Slice 1: when the store holds fewer units than the
            // need, the reserve rises by the received count only and the
            // need stays open with the remainder.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            f.Fund(10000);
            f.Port.Stock["staple_food"] = 3; // only 3u available
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(3, result.UnitsAcquired);
            Assert.AreEqual(2, result.UnitsStillNeeded);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "A partial purchase must leave the unmet need open.");
            Assert.AreEqual(3, reserve.currentUnits, "The reserve rises by the received count only.");
            Assert.AreEqual(3, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        [Test]
        public void LoopReceipt_ClampsReserveAtTargetWhileLotsHoldFullAmount()
        {
            // Plan 08 Slice 1: a receipt larger than the remaining deficit
            // stops the reserve at target while the lots hold the full
            // receipt, exactly as ApplyReservePurchase behaves — the excess
            // lives only in lots, and no re-buy fires while the reserve
            // reads at target.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            Assert.Greater(target, 0, "The staple_food target must be computed from the catalog, not assumed.");
            reserve.currentUnits = target - 2; // 2u of deficit remain
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, result.UnitsAcquired);
            Assert.AreEqual(target, reserve.currentUnits, "The reserve stops at target.");
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "The lots hold the full receipt above the reserve.");
        }

        [Test]
        public void LoopReceipt_ItemWithoutReserveCategoryCreditsNothing()
        {
            // Plan 08 Slice 1: an item whose category has no household
            // reserve credits nothing and crashes nothing. No catalogued
            // item has an empty ReserveCategoryId (verified in source:
            // every HouseholdItemCatalog definition carries one), so the
            // loop-reachable form of this guard is a catalogued item
            // whose category has no household reserve: "book" maps to
            // "luxury_goods", which the reserve catalog does not define.
            // The null-definition guard is pinned by the direct helper
            // call at the end of this test.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "luxury_goods",
                OfferedItemIds = new List<string> { "book" },
                StoreUnitsPerItemUnit = 1,
            });
            f.Port.Stock["luxury_goods"] = 100;
            f.Port.Prices["luxury_goods"] = 50;
            f.Port.Targets["luxury_goods"] = 100;
            var reservesBefore = new Dictionary<string, int>();
            foreach (string categoryId in new[]
            {
                "staple_food", "meat", "household_goods", "fuel_wood",
                "clothing", "tools_hardware", "medicine_remedies",
            })
            {
                HouseholdReserveState reserve = household.GetReserve(categoryId);
                reservesBefore[categoryId] = reserve != null ? reserve.currentUnits : -1;
            }
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed("book", 5);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, result.UnitsAcquired);
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits("book"),
                "The book lots land.");
            foreach (KeyValuePair<string, int> entry in reservesBefore)
            {
                HouseholdReserveState reserve = household.GetReserve(entry.Key);
                int after = reserve != null ? reserve.currentUnits : -1;
                Assert.AreEqual(entry.Value, after, $"Reserve {entry.Key} must be untouched by a book receipt.");
            }

            HouseholdInventoryReserveBridge.MirrorLotReceiptToReserve(household, "not_a_real_item", 5);
            foreach (KeyValuePair<string, int> entry in reservesBefore)
            {
                HouseholdReserveState reserve = household.GetReserve(entry.Key);
                int after = reserve != null ? reserve.currentUnits : -1;
                Assert.AreEqual(entry.Value, after, $"Reserve {entry.Key} must be untouched by an unknown item id.");
            }
        }

        [Test]
        public void LoopReceipt_BatchCreditsEachNeed()
        {
            // Plan 08 Slice 1: ExecuteBatch with two needs credits each
            // mapped reserve by its own receipt through the same shared
            // settlement unit — one trip, one shopper, two credits.
            Fixture f = Fixture.Create();
            f.Port.OfferList[0].OfferedItemIds.Add(HouseholdItemCatalog.BreadId);
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            f.Fund(10000);
            HouseholdPurchasingNeed flourNeed = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            HouseholdPurchasingNeed breadNeed = f.OpenNeed(HouseholdItemCatalog.BreadId, 3);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { flourNeed, breadNeed }, f.Day, f.Options());

            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            Assert.AreEqual(2, batch.ServedNeedCount);
            Assert.AreEqual(8, batch.UnitsAcquiredTotal);
            Assert.AreEqual(target, reserve.currentUnits, "Both receipts credit the shared staple_food reserve.");
            Assert.AreEqual(5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(3, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, flourNeed.Status);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, breadNeed.Status);
        }

        [Test]
        public void LoopReceipt_ClosesDeficitSoBuildShoppingNeedsReportsNone()
        {
            // Plan 08 Slice 1: after a full receipt the deficit is zero,
            // so BuildShoppingNeeds returns no need for the category —
            // the reserve-side proof that no re-buy fires.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            Assert.Greater(target, 0, "The staple_food target must be computed from the catalog, not assumed.");
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, target);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(target, result.UnitsAcquired);
            Assert.AreEqual(target, reserve.currentUnits);
            Assert.AreEqual(target, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(!HasStapleFoodNeed(needsAfter),
                "After a full receipt BuildShoppingNeeds reports no staple_food need, so no re-buy fires.");
        }

        // ---------- Slice 0: AUD-266 discriminator (shopper half) ----------

        [Test]
        public void ChooseShopper_SkipsOffMapMember()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): REWRITE of the Slice 0
            // ChooseShopper_ReturnsOffMapMember. With the authority
            // passed, the policy skips the off-map preferred shopper P1
            // (adult woman) and falls back to the on-map adult P2.
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
                population, new List<int> { 1, 2 }, 0, 480, 60, null, null, null, out string reason, authority);

            Assert.AreEqual(2, chosen,
                "The off-map adult must be skipped for the on-map adult (reason: " + (reason ?? "none") + ").");
            Assert.IsNull(reason, "A found shopper leaves no rejection reason.");
        }

        [Test]
        public void ChooseShopper_AllOffMap_ReturnsMinusOneWithReason()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): when every member is
            // off-map, no shopper is chosen and the reason names the
            // off-map rejections.
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
            foreach (int personId in new[] { 1, 2 })
            {
                OffMapJourneyRecord record = authority.DepartPerson(
                    personId, "home", "Millbrook", 1, 2, "freight",
                    null, null, 0, _ => 0, null, diag);
                Assert.IsNotNull(record, $"DepartPerson must create the journey record for P{personId}: " + string.Join("; ", diag));
            }

            int chosen = ShopperAssignmentPolicy.ChooseShopper(
                population, new List<int> { 1, 2 }, 0, 480, 60, null, null, null, out string reason, authority);

            Assert.AreEqual(-1, chosen, "Nobody can shop when every member is off-map.");
            Assert.IsTrue(reason != null && reason.Contains("P1: off-map (freight journey)."),
                "The reason must name the P1 off-map rejection, got: " + (reason ?? "null"));
            Assert.IsTrue(reason.Contains("P2: off-map (freight journey)."),
                "The reason must name the P2 off-map rejection, got: " + reason);
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
            // this Slice 0 test deliberately leaves the context's
            // OffMapJourneys field unset, so it pins the fail-open default
            // (Slice 2 adds the field and the authority-carrying test
            // below). The Slice 0 form stays green by design.
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

        // ---------- Slice 2: AUD-266 repair (off-map shopper skip) ----------

        [Test]
        public void ReassignIfUnavailable_OffMapExecutor_ReassignsToNextAdult()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): a plan assigned to P1, who
            // then departs off-map, reassigns to the next on-map adult P3.
            // The old reservation is released (no duplicated person-time)
            // and the already-fulfilled flour need is dropped from the
            // plan, so only the open bread need carries over.
            Fixture f = Fixture.Create();
            f.Port.OfferList[0].OfferedItemIds.Add(HouseholdItemCatalog.BreadId);
            var secondAdult = new PersonState { id = 3, firstName = "Ana", lastName = "Morrow", age = 29, ageBand = AgeBand.Adult18Plus };
            f.Population.people.Add(secondAdult);
            string regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 3, PersonId = 3, HouseholdId = 7, StartDayIndex = 0 });
            Assert.IsNull(regProblem, regProblem);
            f.PersonLocations[3] = "home";
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
                OffMapJourneys = authority,
            };
            HouseholdPurchasingNeed flourNeed = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            HouseholdPurchasingNeed breadNeed = f.OpenNeed(HouseholdItemCatalog.BreadId, 3);
            HouseholdErrandPlan plan = coordinator.Plans.Create(7, 0);
            Assert.IsTrue(coordinator.Plans.Claim(plan.PlanId, 7, flourNeed.NeedSequence), "The flour need must claim cleanly.");
            Assert.IsTrue(coordinator.Plans.Claim(plan.PlanId, 7, breadNeed.NeedSequence), "The bread need must claim cleanly.");
            string refusal = f.Tracker.TryReserve(1, PersonActivityKind.Shopping, 0, 0, 30, "test plan window");
            Assert.IsNull(refusal, refusal);
            List<PersonActivityReservation> active = f.Tracker.ActiveFor(1, 0);
            Assert.AreEqual(1, active.Count, "The plan window reservation must exist before reassignment is tested.");
            plan.Assign(1, new List<string> { flourNeed.NeedSequence.ToString(), breadNeed.NeedSequence.ToString() }, active[0].ReservationId);
            flourNeed.Status = PurchasingNeedStatus.Fulfilled;
            flourNeed.UnitsNeeded = 0;
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must still read off-map when reassignment is tested.");

            string result = coordinator.ReassignIfUnavailable(plan.PlanId, 0, ctx, f.Diagnostics);

            Assert.IsTrue(result != null, "Reassignment must report the move, not return a no-op null.");
            Assert.AreEqual(3, plan.ResponsiblePersonId, "The plan must move to the on-map adult P3.");
            Assert.AreEqual(1, plan.NeedIds.Count, "The fulfilled need must be dropped from the plan.");
            Assert.AreEqual(breadNeed.NeedSequence.ToString(), plan.NeedIds[0], "Only the open bread need carries over.");
            Assert.AreEqual(0, f.Tracker.ActiveFor(1, 0).Count, "The old P1 reservation must be released.");
            Assert.AreEqual(1, f.Tracker.ActiveFor(3, 0).Count, "The replacement shopper must hold a new reservation.");
        }

        [Test]
        public void ShopperOverride_OffMapPerson_Refused()
        {
            // Plan 08 Slice 2 (Q03, AUD-266): an explicit shopper
            // override naming an off-map person is refused loudly — the
            // trip fails, the need stays open, and no sale and no charge
            // happen. The override path bypasses ChooseShopper, so
            // ValidateShopper is its only guard.
            Fixture f = Fixture.Create();
            OffMapJourneyAuthority authority = DepartFirstAdultOffMap(f, 1);
            f.Loop.OffMapJourneys = authority;
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            ShoppingLoopOptions options = f.Options();
            options.ShopperPersonIdOverride = 1;

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, options);

            Assert.IsTrue(!result.Success, "An off-map override must fail the trip.");
            Assert.IsTrue(result.FailureReason != null && result.FailureReason.Contains("P1 is off-map."),
                "The trip must fail loudly naming the off-map override, got: " + (result.FailureReason ?? "null"));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "The need stays open.");
            Assert.AreEqual(5, need.UnitsNeeded, "No units were bought.");
            Assert.AreEqual(100, f.StoreStock(), "No store stock moved.");
            Assert.AreEqual(10000, f.Balance(), "No charge was made.");
            Assert.AreEqual(0, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "No lots landed.");
        }

        // ---------- Slice 2: AUD-335 repeat and save/load ----------

        [Test]
        public void LoopReceipt_SecondReceiptDoesNotDoubleCredit()
        {
            // Plan 08 Slice 2 (Q03, AUD-335 repeat): a second receipt
            // for the same category after the deficit closed credits
            // nothing further to the reserve (clamped at target), while
            // the lots still grow by the second receipt and no need
            // returns.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            f.Fund(10000);
            HouseholdPurchasingNeed first = f.OpenNeed(HouseholdItemCatalog.FlourId, target);
            ShoppingTripResult firstResult = f.Loop.ExecuteNeed(first, f.Day, f.Options());
            Assert.IsTrue(firstResult.Success, firstResult.FailureReason);
            Assert.AreEqual(target, reserve.currentUnits, "The first receipt fills the reserve to target.");

            HouseholdPurchasingNeed second = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            ShoppingTripResult result = f.Loop.ExecuteNeed(second, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, result.UnitsAcquired);
            Assert.AreEqual(target, reserve.currentUnits, "The second receipt credits nothing further to the reserve.");
            Assert.AreEqual(target + 5, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "The lots still grow by the second receipt.");
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            Assert.IsTrue(!HasStapleFoodNeed(needsAfter), "No staple_food need returns after the second receipt.");
        }

        [Test]
        public void ReserveCredit_SurvivesSaveLoadRoundTrip()
        {
            // Plan 08 Slice 2 (Q03, AUD-335 save/load, DTO level the
            // harness can drive): a credited reserve exports through the
            // HouseholdReserveSaveDto shape and imports into a fresh
            // household with its credited value intact, and no need for
            // the category arises after the load. The PopulationManager
            // capture/restore itself is verified by source review (fresh
            // PopulationState on load, reserves built from the DTO only),
            // not by a harness test, because PopulationManager.cs is
            // exclamation-excluded.
            Fixture f = Fixture.Create();
            HouseholdState household = f.Population.GetHousehold(7);
            HouseholdReserveState reserve = household.GetReserve("staple_food");
            Assert.NotNull(reserve, "H7 must hold a staple_food reserve.");
            reserve.currentUnits = 0;
            HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            int target = reserve.targetUnits;
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 5);
            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());
            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(5, reserve.currentUnits, "The receipt credits the reserve before the save.");

            var exported = new List<HouseholdReserveSaveDto>();
            foreach (HouseholdReserveState saved in household.reserves)
            {
                exported.Add(new HouseholdReserveSaveDto
                {
                    categoryId = saved.categoryId,
                    displayName = saved.displayName,
                    currentUnits = saved.currentUnits,
                    targetUnits = saved.targetUnits,
                    lowThresholdUnits = saved.lowThresholdUnits,
                    lastDailyUseUnits = saved.lastDailyUseUnits,
                    lastRequestedPurchaseUnits = saved.lastRequestedPurchaseUnits,
                    lastLocalPurchaseUnits = saved.lastLocalPurchaseUnits,
                    lastOffMapPurchaseUnits = saved.lastOffMapPurchaseUnits,
                    lastUrgency01 = saved.lastUrgency01,
                    lastShoppingAttemptDayIndex = saved.lastShoppingAttemptDayIndex,
                    consecutiveLocalShortfallDays = saved.consecutiveLocalShortfallDays,
                    rememberedStockoutUnits = saved.rememberedStockoutUnits,
                    stockoutFrustration01 = saved.stockoutFrustration01,
                });
            }

            var restored = new HouseholdState { id = 7 };
            foreach (HouseholdReserveSaveDto dto in exported)
            {
                restored.reserves.Add(new HouseholdReserveState
                {
                    categoryId = dto.categoryId,
                    displayName = dto.displayName,
                    currentUnits = System.Math.Max(0, dto.currentUnits),
                    targetUnits = System.Math.Max(0, dto.targetUnits),
                    lowThresholdUnits = System.Math.Max(0, dto.lowThresholdUnits),
                    lastDailyUseUnits = System.Math.Max(0, dto.lastDailyUseUnits),
                    lastRequestedPurchaseUnits = System.Math.Max(0, dto.lastRequestedPurchaseUnits),
                    lastLocalPurchaseUnits = System.Math.Max(0, dto.lastLocalPurchaseUnits),
                    lastOffMapPurchaseUnits = System.Math.Max(0, dto.lastOffMapPurchaseUnits),
                    lastUrgency01 = UnityEngine.Mathf.Clamp01(dto.lastUrgency01),
                    lastShoppingAttemptDayIndex = System.Math.Max(-1, dto.lastShoppingAttemptDayIndex),
                    consecutiveLocalShortfallDays = System.Math.Max(0, dto.consecutiveLocalShortfallDays),
                    rememberedStockoutUnits = System.Math.Max(0, dto.rememberedStockoutUnits),
                    stockoutFrustration01 = UnityEngine.Mathf.Clamp01(dto.stockoutFrustration01),
                });
            }
            restored.EnsureHouseholdReservesInitialized(true, false);

            HouseholdReserveState restoredReserve = restored.GetReserve("staple_food");
            Assert.NotNull(restoredReserve, "The restored household must hold a staple_food reserve.");
            Assert.AreEqual(5, restoredReserve.currentUnits, "The credited reserve survives the round trip with its credited value.");
            Assert.AreEqual(target, restoredReserve.targetUnits);
            List<HouseholdReserveNeed> needsAfter = HouseholdReserveEvaluator.BuildShoppingNeeds(restored);
            Assert.IsTrue(!HasStapleFoodNeed(needsAfter),
                "No staple_food need arises after the load — no double credit, no re-buy.");
        }
    }
}
