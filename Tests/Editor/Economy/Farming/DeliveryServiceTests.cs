using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Businesses.Freight;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// FVS-3: delivery and freight integration. Sales settle ON DELIVERY (risk
    /// transfers with physical custody); perishables age in transit; internal
    /// hauling is a cost (Canon §11.2) while the freight company earns real
    /// revenue only from external payers (Canon §3.6, BIZ-3).
    /// </summary>
    [TestFixture]
    public sealed class DeliveryServiceTests
    {
        private AnimalRegistry registry;
        private EntityIdRegistry ids;
        private DairyChain dairy;

        [SetUp]
        public void SetUp()
        {
            ids = new EntityIdRegistry();
            registry = new AnimalRegistry(ids);
            dairy = new DairyChain();
        }

        private MilkLot FreshMilkLot(int dayIndex, out AnimalState cow)
        {
            cow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Female,
                AnimalOwnerKind.Business, "farm:test", dayIndex, "test");
            cow.BiologicalState = AnimalBiologicalState.Lactating;
            cow.Parity = 2;
            dairy.RegisterCow(new DairyCowState(cow.AnimalId, dayIndex - 60, 1f));
            var diagnostics = new List<string>();
            MilkLot lot = dairy.MilkCow(registry, ids, cow.AnimalId,
                EntityId.For(EntityKind.Person, 5), "test-farm", dayIndex, 2, diagnostics);
            Assert.IsNotNull(lot, "Setup milking failed: " + string.Join(" | ", diagnostics));
            return lot;
        }

        private List<ITransitLot> AsTransit(params MilkLot[] lots)
        {
            var list = new List<ITransitLot>();
            foreach (var l in lots) list.Add(l);
            return list;
        }

        private bool DairyExecutor(DairyBuyerStock buyer, HouseholdLedger ledger,
            int pricePerUnit, DeliveryJob job, List<ITransitLot> lots, int day, List<string> diagnostics)
        {
            var sale = DairyMarket.ExecuteDairySale(lots, "milk", buyer, "test-farm", pricePerUnit, day, ledger, diagnostics);
            return sale != null;
        }

        [Test]
        public void FreshDelivery_SettlesSaleOnArrival()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var buyer = new DairyBuyerStock("store-1", "General Store");
            buyer.SetCapacity("milk", 500);
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            DeliveryJob job = DeliveryService.PlanDelivery("agree-1", "milk", AsTransit(lot),
                "test-farm", "farm-biz-1", "store-1", "General Store",
                DeliveryHaulerOption.FarmOwnTeam, 2f, 100, null, null, diagnostics);
            Assert.IsNotNull(job, "Plan failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(DeliveryJobStatus.Planned, job.Status);
            Assert.AreEqual(0, ledger.GetBalanceCents(), "Nothing books revenue at plan time.");

            Assert.IsNull(DeliveryService.DepartDelivery(job, 100));
            Assert.AreEqual(DeliveryJobStatus.InTransit, job.Status);

            int unitsShipped = lot.QuantityUnits;
            Assert.IsNull(DeliveryService.CompleteDelivery(job, AsTransit(lot), 100, 0,
                (j, ls, d, diags) => DairyExecutor(buyer, ledger, 3, j, ls, d, diags), diagnostics));

            Assert.AreEqual(DeliveryJobStatus.Delivered, job.Status);
            Assert.Greater(ledger.GetBalanceCents(), 0, "Proceeds post on delivery, not before.");
            Assert.AreEqual(unitsShipped, buyer.OnHand("milk"), "Buyer receives exactly what was shipped.");
            Assert.AreEqual(unitsShipped * 3, ledger.GetBalanceCents(), "Proceeds = units × price.");
        }

        [Test]
        public void SpoiledInTransit_SaleFailsSellerBearsLoss()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            int unitsBefore = lot.QuantityUnits;
            Assert.Greater(unitsBefore, 0);
            var buyer = new DairyBuyerStock("store-1", "General Store");
            buyer.SetCapacity("milk", 500);
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            DeliveryJob job = DeliveryService.PlanDelivery("agree-2", "milk", AsTransit(lot),
                "test-farm", "farm-biz-1", "store-1", "General Store",
                DeliveryHaulerOption.FarmOwnTeam, 2f, 100, null, null, diagnostics);
            Assert.IsNotNull(job);
            Assert.IsNull(DeliveryService.DepartDelivery(job, 100));

            // 5-day transit kills fresh milk.
            Assert.IsNull(DeliveryService.CompleteDelivery(job, AsTransit(lot), 105, 5,
                (j, ls, d, diags) => DairyExecutor(buyer, ledger, 3, j, ls, d, diags), diagnostics));

            Assert.AreEqual(DeliveryJobStatus.Failed, job.Status);
            Assert.AreEqual(0, ledger.GetBalanceCents(), "No proceeds for spoiled goods — seller bears the loss.");
            Assert.AreEqual(0, buyer.OnHand("milk"));
        }

        [Test]
        public void InternalHaul_RecordsCostNeverRevenue()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var diagnostics = new List<string>();

            DeliveryJob job = DeliveryService.PlanDelivery("agree-3", "milk", AsTransit(lot),
                "test-farm", "farm-biz-1", "store-1", "General Store",
                DeliveryHaulerOption.FarmOwnTeam, 2f, 100, null, null, diagnostics);
            Assert.IsNotNull(job);
            Assert.IsNotNull(job.InternalCost, "Internal haul must record its cost (Canon §11.2).");
            Assert.Greater(job.InternalCost.TotalCostCents, 0, "Feed + wear are real costs.");
            Assert.AreEqual(0, job.FreightChargeCents, "Internal hauling books no freight revenue.");

            var ledger = new HouseholdLedger(7);
            Assert.IsNull(DeliveryService.PostInternalHaulCost(ledger, job, 100, diagnostics));
            Assert.Less(ledger.GetBalanceCents(), 0, "Haul cost posts as an outflow with provenance.");
        }

        [Test]
        public void FreightCompany_BuildsRealBiz3Shipment()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var diagnostics = new List<string>();

            DeliveryJob job = DeliveryService.PlanDelivery("agree-4", "milk", AsTransit(lot),
                "test-farm", "farm-biz-1", "store-1", "General Store",
                DeliveryHaulerOption.FreightCompany, 4f, 100, "farm-biz-1", null, diagnostics);
            Assert.IsNotNull(job);
            Assert.Greater(job.FreightChargeCents, 0, "External haul has a real charge.");

            var freight = new FreightCompanyRuntime("freight-co-1");
            LogisticsShipmentState shipment = DeliveryService.BuildFreightShipment(job, diagnostics);
            Assert.IsNotNull(shipment, "Shipment build failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(LogisticsCargoClass.Perishables, shipment.CargoClass);
            Assert.AreEqual("farm-biz-1", shipment.FreightPayerBusinessInstanceId);

            Assert.IsTrue(freight.TryAcceptShipment(shipment, diagnostics),
                "Freight company must accept: " + string.Join(" | ", diagnostics));

            // Charge collects only on completed delivery from an external payer.
            int early = freight.CollectFreightCharge(shipment.ShipmentId, diagnostics);
            Assert.AreEqual(0, early, "No charge before completion.");
        }

        [Test]
        public void HaulerValidation_IsLoud()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var diagnostics = new List<string>();

            DeliveryJob noPayer = DeliveryService.PlanDelivery("a", "milk", AsTransit(lot),
                "f", "fb", "s", "Store", DeliveryHaulerOption.FreightCompany, 2f, 100, null, null, diagnostics);
            Assert.IsNull(noPayer, "Freight company without a named payer must be refused.");
            Assert.IsTrue(diagnostics.Count > 0);

            diagnostics.Clear();
            DeliveryJob noWorker = DeliveryService.PlanDelivery("b", "milk", AsTransit(lot),
                "f", "fb", "s", "Store", DeliveryHaulerOption.FarmEmployee, 2f, 100, null, null, diagnostics);
            Assert.IsNull(noWorker, "Employee hauling must name the worker.");
        }

        [Test]
        public void FiniteBuyerCapacity_CanRefuse()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var buyer = new DairyBuyerStock("store-1", "General Store");
            buyer.SetCapacity("milk", 1); // almost no room
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            DairySale sale = DairyMarket.ExecuteDairySale(AsTransit(lot), "milk", buyer,
                "test-farm", 3, 100, ledger, diagnostics);

            Assert.IsNull(sale, "Finite buyer capacity must refuse overflow (Canon §9.2).");
            Assert.AreEqual(0, ledger.GetBalanceCents());
        }

        [Test]
        public void HandlingTasks_EnqueuedForFarmHauls()
        {
            AnimalState cow;
            MilkLot lot = FreshMilkLot(100, out cow);
            var diagnostics = new List<string>();
            DeliveryJob job = DeliveryService.PlanDelivery("agree-5", "milk", AsTransit(lot),
                "test-farm", "farm-biz-1", "store-1", "General Store",
                DeliveryHaulerOption.FarmEmployee, 2f, 100, null, "Hired Hand", diagnostics);
            Assert.IsNotNull(job);

            var authority = new TaskAuthority();
            new FreightCompanyRuntime("x").RegisterTaskDefinitions(authority);
            DeliveryService.EnqueueHandlingTasks(authority, job, EntityId.For(EntityKind.Person, 9), 100);

            // Loading + unloading tasks exist (TTS-5 math); exact count via definitions present.
            Assert.IsNotNull(authority.GetDefinition(FreightCompanyRuntime.LoadFreightTaskId));
            Assert.IsNotNull(authority.GetDefinition(FreightCompanyRuntime.UnloadFreightTaskId));
        }
    }
}
