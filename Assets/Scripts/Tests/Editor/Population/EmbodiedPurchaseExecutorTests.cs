using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// T1A: the real embodied-purchase executor (Canon 13.4). ProcurementNeed ->
    /// acting Person -> Journey -> supplier availability -> Transaction ->
    /// possession. No anonymous buyers; no faked sales.
    /// </summary>
    [TestFixture]
    public sealed class EmbodiedPurchaseExecutorTests
    {
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
            var ledger = ledgers.GetOrCreate(householdId);
            string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");
            Assert.IsNull(problem, problem);
        }

        [Test]
        public void Execute_RealPersonJourneySupplier_CompletesPurchaseWithProvenance()
        {
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
            PurchaseExecutionResult result = executor.Execute(need, personId, 3);

            Assert.IsTrue(result.Success, result.Notes);
            Assert.AreEqual(10, result.UnitsAcquired);
            Assert.AreEqual(1200, result.AmountPaidCents);
            Assert.AreEqual("General Store", result.Counterparty);
            // Possession: reserves credited (2 + 10).
            Assert.AreEqual(12, population.GetHousehold(householdId).reserves[0].currentUnits);
            // Ledger paid out.
            Assert.AreEqual(8800, ledgers.GetOrCreate(householdId).GetBalanceCents());
            // Stock decremented.
            Assert.AreEqual(90, supplier.StockUnits("staple_food"));
        }

        [Test]
        public void Execute_UnknownPerson_Rejects_NoAnonymousBuyers()
        {
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            directory.Register(new TestSupplier());

            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home");

            var need = new ProcurementNeed { NeedSequence = 0, HouseholdId = householdId, CategoryId = "staple_food", UnitsNeeded = 5 };
            PurchaseExecutionResult result = executor.Execute(need, 999, 3);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.UnitsAcquired);
            Assert.AreEqual(10000, ledgers.GetOrCreate(householdId).GetBalanceCents());
        }

        [Test]
        public void Execute_NoStockedSupplier_FailsWithoutFakingSale()
        {
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            var supplier = new TestSupplier();
            supplier.Stock["staple_food"] = 0;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);

            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home");

            var need = new ProcurementNeed { NeedSequence = 0, HouseholdId = householdId, CategoryId = "staple_food", UnitsNeeded = 5 };
            PurchaseExecutionResult result = executor.Execute(need, personId, 3);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(10000, ledgers.GetOrCreate(householdId).GetBalanceCents());
        }

        [Test]
        public void Execute_NoRouteToSupplier_FailsWithoutFakingSale()
        {
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);
            var directory = new SupplierDirectory();
            var supplier = new TestSupplier { LocationId = "far-store" };
            supplier.Stock["staple_food"] = 100;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("far-store", JourneyLocationKind.Store, "Far Store", 9f, 0f));
            // No edge: unreachable.

            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, journeys, pid => "home");

            var need = new ProcurementNeed { NeedSequence = 0, HouseholdId = householdId, CategoryId = "staple_food", UnitsNeeded = 5 };
            PurchaseExecutionResult result = executor.Execute(need, personId, 3);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(10000, ledgers.GetOrCreate(householdId).GetBalanceCents());
        }

        [Test]
        public void Execute_InsufficientFunds_FailsAtomically_StockUntouched()
        {
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 100); // only 100c; need costs 600c

            var directory = new SupplierDirectory();
            var supplier = new TestSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Prices["staple_food"] = 120;
            directory.Register(supplier);

            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, NewJourney(), pid => "home");

            var need = new ProcurementNeed { NeedSequence = 0, HouseholdId = householdId, CategoryId = "staple_food", UnitsNeeded = 5 };
            PurchaseExecutionResult result = executor.Execute(need, personId, 3);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(100, supplier.StockUnits("staple_food"));
            Assert.AreEqual(100, ledgers.GetOrCreate(householdId).GetBalanceCents());
        }

        [Test]
        public void Execute_PicksNearestStockedSupplier()
        {
            PopulationState population = NewPopulation(out int personId, out int householdId);
            var ledgers = new HouseholdLedgerRegistry();
            Fund(ledgers, householdId, 10000);

            var journeys = NewJourney();
            journeys.RegisterLocation(new JourneyLocation("store-far", JourneyLocationKind.Store, "Far Store", 9f, 0f));
            journeys.AddEdge("home", "store-far", 9.0f, "long road");

            var directory = new SupplierDirectory();
            var near = new TestSupplier { SupplierName = "Near Store", LocationId = "store" };
            near.Stock["staple_food"] = 100;
            near.Prices["staple_food"] = 150;
            var far = new TestSupplier { SupplierName = "Far Store", LocationId = "store-far" };
            far.Stock["staple_food"] = 100;
            far.Prices["staple_food"] = 50; // cheaper but farther — nearest wins (T1C owns price-aware choice)
            directory.Register(far);
            directory.Register(near);

            var executor = new EmbodiedPurchaseExecutor(
                population, ledgers, directory, journeys, pid => "home");

            var need = new ProcurementNeed { NeedSequence = 0, HouseholdId = householdId, CategoryId = "staple_food", UnitsNeeded = 5 };
            PurchaseExecutionResult result = executor.Execute(need, personId, 3);

            Assert.IsTrue(result.Success, result.Notes);
            Assert.AreEqual("Near Store", result.Counterparty);
        }
    }
}
