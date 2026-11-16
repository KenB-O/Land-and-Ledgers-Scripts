using System;
using System.Collections.Generic;
using System.Linq;
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
    /// DEF-067 (Priority One): the Unity run was inconclusive because it
    /// provided no corresponding legacy and Phase C household
    /// purchase/receipt records for a day-by-day charge comparison.
    /// These tests pin the instrumentation that makes that comparison
    /// possible: an append-only, in-memory reconciliation that records
    /// every posted legacy charge (EmbodiedPurchaseExecutor) and every
    /// posted Phase C settlement (HouseholdShoppingLoop.SettleAtCounter),
    /// and flags same-want overlaps and Phase C multi-item same-want
    /// charges. Diagnostics only: no guard, no quantity, price or
    /// settlement change, no save/load persistence.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdFoodChargeReconciliationTests
    {
        // ---------- T1: legacy success records one Legacy charge ----------

        [Test]
        public void LegacyExecutor_Success_RecordsOneLegacyCharge()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            var supplier = new TestSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);
            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home",
                null, null, null, recorder);

            var need = new ProcurementNeed
            {
                NeedSequence = 0,
                HouseholdId = householdId,
                CategoryId = "staple_food",
                UnitsNeeded = 10,
            };

            PurchaseExecutionResult result = executor.Execute(need, personId, 0);

            Assert.IsTrue(result.Success, result.Notes);
            Assert.AreEqual(10, result.UnitsAcquired);
            Assert.AreEqual(1200, result.AmountPaidCents);
            List<HouseholdFoodChargeRecord> records = recorder.GetRecords(householdId, 0);
            Assert.AreEqual(1, records.Count);
            HouseholdFoodChargeRecord record = records[0];
            Assert.AreEqual(HouseholdFoodChargePath.Legacy, record.Path);
            Assert.AreEqual(householdId, record.HouseholdId);
            Assert.AreEqual(0, record.DayIndex);
            Assert.AreEqual("staple_food", record.WantKey);
            Assert.AreEqual(10, record.Units);
            Assert.AreEqual(1200, record.GoodsCents);
            Assert.AreEqual(0, record.FreightCents);
            Assert.AreEqual(1200, record.AmountCents);
            Assert.AreEqual("General Store", record.Counterparty);
            Assert.AreEqual("cash", record.SettlementMode);
            Assert.AreEqual(need.NeedSequence, record.NeedSequence);
        }

        // ---------- T2: legacy rejection records nothing ----------

        [Test]
        public void LegacyExecutor_Rejected_RecordsNothing()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            var supplier = new TestSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);
            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home",
                null, null, null, recorder);

            // Unknown acting person: the executor refuses before any
            // ledger or supplier touch.
            var need = new ProcurementNeed
            {
                NeedSequence = 0,
                HouseholdId = householdId,
                CategoryId = "staple_food",
                UnitsNeeded = 10,
            };

            PurchaseExecutionResult result = executor.Execute(need, 999, 0);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, recorder.GetRecords(householdId, 0).Count);
        }

        // ---------- T3: loop settlement records one Phase C charge ----------

        [Test]
        public void LoopSettlement_Success_RecordsOnePhaseCCharge()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            LoopFixture f = LoopFixture.Create(recorder);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsTrue(result.Success, result.FailureReason);
            Assert.AreEqual(10, result.UnitsAcquired);
            Assert.AreEqual(1200, result.BuyerOutflowCents);
            List<HouseholdFoodChargeRecord> records = recorder.GetRecords(7, 0);
            Assert.AreEqual(1, records.Count);
            HouseholdFoodChargeRecord record = records[0];
            Assert.AreEqual(HouseholdFoodChargePath.PhaseC, record.Path);
            Assert.AreEqual(7, record.HouseholdId);
            Assert.AreEqual(0, record.DayIndex);
            Assert.AreEqual(HouseholdItemCatalog.FlourId, record.WantKey);
            Assert.AreEqual(10, record.Units);
            Assert.AreEqual(1200, record.GoodsCents);
            Assert.AreEqual(0, record.FreightCents);
            Assert.AreEqual(1200, record.AmountCents);
            Assert.AreEqual("General Store", record.Counterparty);
            Assert.AreEqual("cash", record.SettlementMode);
            Assert.AreEqual(need.NeedSequence, record.NeedSequence);
        }

        // ---------- T4: loop failed settlement records nothing ----------

        [Test]
        public void LoopSettlement_Failed_RecordsNothing()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            LoopFixture f = LoopFixture.Create(recorder);
            f.Fund(10000);
            // Soap is a catalog item, but this store offers flour only:
            // no known seller, so the need fails before settlement.
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.SoapId, 10);

            ShoppingTripResult result = f.Loop.ExecuteNeed(need, f.Day, f.Options());

            Assert.IsFalse(result.Success, "Expected the soap need to fail with no known seller.");
            Assert.AreEqual(0, recorder.GetRecords(7, 0).Count);
        }

        // ---------- T5: reconciler flags both-paths-same-want ----------

        [Test]
        public void Reconciler_BothPathsSameWantSameDay_FlagsOverlap()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            recorder.RecordLegacy(7, 3, "staple_food", 10, 1200, 0, "General Store");
            recorder.RecordPhaseC(7, 3, HouseholdItemCatalog.BreadId, 5, 600, 0, 600, 4, "General Store", "cash");

            DayFoodChargeSummary summary = recorder.SummarizeDay(7, 3);

            Assert.IsTrue(summary.BothPathsChargedSameWant);
            Assert.IsFalse(summary.PhaseCChargedMultipleItemsSameWant);
            Assert.AreEqual(1, summary.OverlappedWants.Count);
            Assert.AreEqual("staple_food", summary.OverlappedWants[0]);
            Assert.AreEqual(10, summary.LegacyUnits);
            Assert.AreEqual(1200, summary.LegacyCents);
            Assert.AreEqual(5, summary.PhaseCUnits);
            Assert.AreEqual(600, summary.PhaseCCents);
            Assert.AreEqual(1, summary.PerWant.Count);
            WantChargeTotals want = summary.PerWant[0];
            Assert.AreEqual("staple_food", want.WantCategory);
            Assert.AreEqual(10, want.LegacyUnits);
            Assert.AreEqual(5, want.PhaseCUnits);
            Assert.AreEqual(1, want.PhaseCItems.Count);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, want.PhaseCItems[0]);
            string line = recorder.SummarizeDayLine(3);
            Assert.IsTrue(line.Contains("OVERLAP staple_food"), line);
        }

        // ---------- T6: reconciler flags Phase C multi-item same want ----------

        [Test]
        public void Reconciler_PhaseCMultipleItemsSameWant_FlagsMultiItem()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            recorder.RecordPhaseC(7, 3, HouseholdItemCatalog.BreadId, 5, 600, 0, 600, 4, "General Store", "cash");
            recorder.RecordPhaseC(7, 3, HouseholdItemCatalog.FlourId, 10, 1200, 0, 1200, 5, "General Store", "cash");

            DayFoodChargeSummary summary = recorder.SummarizeDay(7, 3);

            Assert.IsFalse(summary.BothPathsChargedSameWant);
            Assert.IsTrue(summary.PhaseCChargedMultipleItemsSameWant);
            Assert.AreEqual(1, summary.MultiItemWants.Count);
            Assert.AreEqual("staple_food", summary.MultiItemWants[0]);
            Assert.AreEqual(15, summary.PhaseCUnits);
            Assert.AreEqual(1800, summary.PhaseCCents);
            Assert.AreEqual(1, summary.PerWant.Count);
            WantChargeTotals want = summary.PerWant[0];
            Assert.AreEqual(2, want.PhaseCItems.Count);
            Assert.IsTrue(want.PhaseCItems.Contains(HouseholdItemCatalog.BreadId));
            Assert.IsTrue(want.PhaseCItems.Contains(HouseholdItemCatalog.FlourId));
            string line = recorder.SummarizeDayLine(3);
            Assert.IsTrue(line.Contains("MULTI-ITEM staple_food"), line);
        }

        // ---------- T7: no flags for one path, other days, other wants ----------

        [Test]
        public void Reconciler_NoFlags_WhenSinglePathDifferentDayOrDifferentWant()
        {
            // (a) One path only: Phase C bread alone is not an overlap.
            var onlyPhaseC = new HouseholdFoodChargeReconciliation();
            onlyPhaseC.RecordPhaseC(7, 3, HouseholdItemCatalog.BreadId, 5, 600, 0, 600, 4, "General Store", "cash");
            DayFoodChargeSummary a = onlyPhaseC.SummarizeDay(7, 3);
            Assert.IsFalse(a.BothPathsChargedSameWant);
            Assert.IsFalse(a.PhaseCChargedMultipleItemsSameWant);

            // (b) Same want, different days: not an overlap.
            var otherDays = new HouseholdFoodChargeReconciliation();
            otherDays.RecordLegacy(7, 3, "staple_food", 10, 1200, 0, "General Store");
            otherDays.RecordPhaseC(7, 4, HouseholdItemCatalog.BreadId, 5, 600, 0, 600, 4, "General Store", "cash");
            DayFoodChargeSummary b3 = otherDays.SummarizeDay(7, 3);
            DayFoodChargeSummary b4 = otherDays.SummarizeDay(7, 4);
            Assert.IsFalse(b3.BothPathsChargedSameWant);
            Assert.IsFalse(b4.BothPathsChargedSameWant);

            // (c) Same day, different wants: legacy staple_food vs Phase C
            // fuel_wood is not an overlap.
            var otherWants = new HouseholdFoodChargeReconciliation();
            otherWants.RecordLegacy(7, 3, "staple_food", 10, 1200, 0, "General Store");
            otherWants.RecordPhaseC(7, 3, HouseholdItemCatalog.FuelWoodId, 2, 56, 0, 56, 9, "General Store", "cash");
            DayFoodChargeSummary c = otherWants.SummarizeDay(7, 3);
            Assert.IsFalse(c.BothPathsChargedSameWant);
            Assert.IsFalse(c.PhaseCChargedMultipleItemsSameWant);
            Assert.AreEqual(2, c.PerWant.Count);
        }

        [Test]
        public void Reporter_PublishesCompletedDayAfterSettlements_AndIncludesZeroDaysOnce()
        {
            var recorder = new HouseholdFoodChargeReconciliation();
            var reporter = new HouseholdFoodChargeReconciliationReporter();

            // A day is summarized at the next day boundary, after its
            // physical shopping settlements have been recorded.
            Assert.IsTrue(reporter.TryCreateCompletedDayLine(recorder, 2, out string emptyDay));
            StringAssert.Contains("day 2: no food charges recorded", emptyDay);
            Assert.IsFalse(reporter.TryCreateCompletedDayLine(recorder, 2, out _),
                "One completed day should not be published repeatedly.");

            recorder.RecordPhaseC(7, 3, HouseholdItemCatalog.BreadId,
                6, 30, 0, 30, 12, "General Store", "cash");
            Assert.IsTrue(reporter.TryCreateCompletedDayLine(recorder, 3, out string settledDay));
            StringAssert.Contains("H7 legacy=0u/0c phaseC=6u/30c", settledDay);
            Assert.IsFalse(settledDay.Contains("OVERLAP"), settledDay);
            Assert.IsFalse(settledDay.Contains("MULTI-ITEM"), settledDay);
        }

        // ---------- T8: null recorder changes nothing ----------

        [Test]
        public void NullRecorder_ExecutorAndLoopBehaveUnchanged()
        {
            // Legacy executor without a recorder: the purchase posts
            // exactly as before (10u staple_food for 1200c).
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            var supplier = new TestSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);
            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home");
            var need = new ProcurementNeed
            {
                NeedSequence = 0,
                HouseholdId = householdId,
                CategoryId = "staple_food",
                UnitsNeeded = 10,
            };
            PurchaseExecutionResult legacyResult = executor.Execute(need, personId, 0);
            Assert.IsTrue(legacyResult.Success, legacyResult.Notes);
            Assert.AreEqual(10, legacyResult.UnitsAcquired);
            Assert.AreEqual(1200, legacyResult.AmountPaidCents);
            Assert.AreEqual(8800, ledgers.GetOrCreate(householdId).GetBalanceCents());

            // Phase C loop without a recorder: the settlement posts
            // exactly as before (10u flour for 1200c).
            LoopFixture f = LoopFixture.Create(null);
            f.Fund(10000);
            HouseholdPurchasingNeed loopNeed = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            ShoppingTripResult loopResult = f.Loop.ExecuteNeed(loopNeed, f.Day, f.Options());
            Assert.IsTrue(loopResult.Success, loopResult.FailureReason);
            Assert.AreEqual(10, loopResult.UnitsAcquired);
            Assert.AreEqual(1200, loopResult.BuyerOutflowCents);
            Assert.AreEqual(8800, f.Balance());
        }

        // ---------- legacy-executor fixture helpers (EmbodiedPurchaseExecutorTests pattern) ----------

        private static PopulationState NewPopulation(out int personId, out int householdId)
        {
            var population = new PopulationState();
            var person = new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34 };
            population.people.Add(person);
            var household = new HouseholdState { id = 7 };
            household.memberIds.Add(1);
            household.reserves.Add(new HouseholdReserveState
            {
                categoryId = "staple_food",
                displayName = "Staple food",
                currentUnits = 2,
                lowThresholdUnits = 14,
                targetUnits = 20,
            });
            population.households.Add(household);
            personId = 1;
            householdId = 7;
            return population;
        }

        private static JourneyModel NewJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "river road");
            return journeys;
        }

        private static void Fund(HouseholdLedgerRegistry ledgers, int householdId, int cents)
        {
            HouseholdLedger ledger = ledgers.GetOrCreate(householdId);
            string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");
            Assert.IsNull(problem, problem);
        }

        private sealed class TestSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();

            public bool HasCategory(string categoryId) => Stock.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int PricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) Stock[categoryId] -= sold;
                return sold;
            }
        }

        // ---------- Phase C loop fixture (HouseholdShoppingLoopTests pattern) ----------

        private sealed class FakeStorePort : IGeneralStoreTradingPort, IPaidStoreReceivingPort
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

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

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
                throw new System.NotImplementedException("Credit settlement is not used by the DEF-067 loop fixture.");
            }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
            }

            public string ReceivePaidStock(string lotId, string itemId, string categoryId, string unitLabel,
                int units, int costCents, string provenance, int dayIndex)
            {
                if (CurrentCashCents < costCents) return "cannot afford";
                string problem = ReceiveStock(categoryId, units, provenance);
                return problem ?? SpendCash(costCents, provenance);
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

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class LoopFixture
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

            public static LoopFixture Create(HouseholdFoodChargeReconciliation recorder)
            {
                var f = new LoopFixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                string regProblem = f.Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0 });
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

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds, chargeRecorder: recorder);

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
        }
    }
}
