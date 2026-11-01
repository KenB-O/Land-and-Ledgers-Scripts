using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass XI WS2 (audit finding A-25): the freight Invalid-handler path.
    /// AdvanceTick with an Invalid handler must stall handler-gated
    /// shipments loudly (not silently, not by inventing a handler), must
    /// still advance in-transit shipments, and must still collect revenue
    /// only for accepted deliveries.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws2FreightHandlerTests
    {
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId handler;
        private EntityId driverA;
        private List<EntityId> teamA;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(42);
            service = new FreightShipmentService();
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = 42,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    return quantityUnits;
                },
            };
            handler = EntityId.For(EntityKind.Person, 701);
            driverA = EntityId.For(EntityKind.Person, 702);
            teamA = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            service.RegisterWagon(new EquipmentAsset
            {
                AssetId = "wagon-1", Kind = "farm-wagon", DisplayName = "wagon-1",
                Condition01 = 0.9f, PayloadMassCapacityKg = 1000f, CargoVolumeCapacityM3 = 4f,
            });
        }

        private static FreightCargoSpec GrainSpec() =>
            new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        private FreightProductionDriver NewDriver()
        {
            var driver = new FreightProductionDriver(service);
            var carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("biz-freight-1");
            driver.EnsureCompany("biz-freight-1", carrierRuntime);
            var payerRuntime = new BusinessRuntimeState();
            payerRuntime.BindBusinessIdentity("biz-store-1");
            payerRuntime.SetCurrentCashCents(100000);
            driver.RegisterBusinessRuntime("biz-store-1", payerRuntime);
            return driver;
        }

        private LogisticsShipmentState CreateViaDriver(FreightProductionDriver driver, string id)
        {
            LogisticsShipmentState shipment = driver.CreateShipment(
                "biz-freight-1", id, "mill-1", "store-1", 50, GrainSpec(), "wagon-1", hooks,
                driverA, teamA, 500, "biz-store-1", new List<string>(), out string reason);
            Assert.IsNotNull(shipment, reason);
            return shipment;
        }

        [Test]
        public void InvalidHandler_PlannedShipmentStalls_WithDiagnostic_NoPhantomDispatch()
        {
            FreightProductionDriver driver = NewDriver();
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);

            var diag = new List<string>();
            int advanced = driver.AdvanceTick(3600f, hooks, EntityId.Invalid, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status,
                "no handler means no loading — the shipment waits, it is not dispatched by fiction");
            Assert.AreEqual(0, advanced);
            bool named = false;
            foreach (string line in diag)
                if (line.Contains("wait for a handler")) named = true;
            Assert.IsTrue(named, "the wait must be named loudly: " + string.Join("; ", diag));
        }

        [Test]
        public void InvalidHandler_InTransitShipmentStillAdvances()
        {
            FreightProductionDriver driver = NewDriver();
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1");
            var diag = new List<string>();
            int guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.InTransit && guard++ < 60)
                driver.AdvanceTick(3600f, hooks, handler, diag);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, string.Join("; ", diag));

            shipment.transitDurationGameSeconds = 10f;
            driver.AdvanceTick(3600f, hooks, EntityId.Invalid, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status,
                "transit needs no handler — only loading and unloading do");
            // And the arrived shipment now waits loudly for a handler.
            driver.AdvanceTick(3600f, hooks, EntityId.Invalid, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Arrived, shipment.Status);
            bool named = false;
            foreach (string line in diag)
                if (line.Contains("wait for a handler")) named = true;
            Assert.IsTrue(named, string.Join("; ", diag));
        }

        [Test]
        public void InvalidHandler_NoDuplicateCompany_OnRepeatedEnsure()
        {
            FreightProductionDriver driver = NewDriver();
            FreightCompanyRuntime first = driver.FindCompany("biz-freight-1");
            Assert.IsNotNull(first);
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("biz-freight-1");
            driver.EnsureCompany("biz-freight-1", runtime);
            Assert.AreSame(first, driver.FindCompany("biz-freight-1"),
                "a second EnsureCompany for the same business must reuse the instance");
            Assert.AreEqual(1, driver.Companies.Count,
                "EnsureCompany keyed by instance id must reuse, never duplicate, a company");
        }
    }
}
