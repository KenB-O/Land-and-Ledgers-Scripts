using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass IX WS3 (PASS8-002 / A4-11, A4-12, A4-13): freight production.
    /// Exclusivity first: no wagon, driver or animal is ever double-booked
    /// across live shipments (checked across the service's shipments and,
    /// when wired, through the unified reservation authority). Then the
    /// production driver: FreightCompanyRuntime instances owned per
    /// business, shipments advancing on the tick through the full
    /// lifecycle, rejected deliveries kept in legitimate custody with no
    /// revenue, and mid-transport save/load restoring exactly once.
    /// </summary>
    [TestFixture]
    public sealed class FreightProductionDriverTests
    {
        private const int Day = 42;

        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FreightShipmentService service;
        private FreightShipmentHooks hooks;
        private EntityId handler;
        private EntityId driverA;
        private EntityId driverB;
        private List<EntityId> teamA;
        private List<EntityId> teamB;
        private Dictionary<string, int> recipientStock;
        private bool recipientAccepts;

        [SetUp]
        public void SetUp()
        {
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            service = new FreightShipmentService();
            recipientStock = new Dictionary<string, int>(StringComparer.Ordinal);
            recipientAccepts = true;
            hooks = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = new List<string>(),
                ReceiveShipment = (LogisticsShipmentState shipment, int quantityUnits, out string reason) =>
                {
                    reason = string.Empty;
                    if (!recipientAccepts)
                    {
                        reason = "recipient refused the delivery";
                        return 0;
                    }
                    string key = shipment.destinationBusinessInstanceId ?? "nowhere";
                    recipientStock[key] = recipientStock.TryGetValue(key, out int have) ? have + quantityUnits : quantityUnits;
                    return quantityUnits;
                },
            };
            handler = EntityId.For(EntityKind.Person, 701);
            driverA = EntityId.For(EntityKind.Person, 702);
            driverB = EntityId.For(EntityKind.Person, 703);
            teamA = new List<EntityId> { EntityId.For(EntityKind.Animal, 11), EntityId.For(EntityKind.Animal, 12) };
            teamB = new List<EntityId> { EntityId.For(EntityKind.Animal, 13), EntityId.For(EntityKind.Animal, 14) };
            service.RegisterWagon(Wagon("wagon-1"));
            service.RegisterWagon(Wagon("wagon-2"));
        }

        private static EquipmentAsset Wagon(string id) => new EquipmentAsset
        {
            AssetId = id,
            Kind = "farm-wagon",
            DisplayName = id,
            Condition01 = 0.9f,
            PayloadMassCapacityKg = 1000f,
            CargoVolumeCapacityM3 = 4f,
        };

        private static FreightCargoSpec GrainSpec() => new FreightCargoSpec { MassKgPerUnit = 2f, VolumeM3PerUnit = 0.01f };

        private LogisticsShipmentState CreateViaService(string id, string wagonId)
        {
            return service.CreateShipment(id, "mill-1", "store-1", 50, GrainSpec(), wagonId, hooks, out _);
        }

        private void LoadAndDispatch(LogisticsShipmentState shipment, EntityId driver, List<EntityId> animals)
        {
            Assert.IsTrue(service.BeginLoading(shipment, handler, hooks, out string lreason), lreason);
            int guard = 0;
            while (!shipment.loadApplied && guard++ < 20)
                Assert.IsTrue(service.WorkLoading(shipment, 60, hooks, out string wreason), wreason);
            Assert.IsTrue(shipment.loadApplied);
            Assert.IsTrue(service.Dispatch(shipment, driver, animals, hooks, out string dreason), dreason);
        }

        // ---------- A4-13: exclusivity ----------

        [Test]
        public void Exclusivity_SecondLiveShipmentOnSameWagon_Refused()
        {
            LogisticsShipmentState first = CreateViaService("ship-1", "wagon-1");
            Assert.IsNotNull(first);

            LogisticsShipmentState second = service.CreateShipment("ship-2", "mill-1", "store-1",
                10, GrainSpec(), "wagon-1", hooks, out string reason);
            Assert.IsNull(second, "a wagon already bound to a live shipment cannot be booked twice");
            StringAssert.Contains("already bound", reason, reason);

            // A different wagon is fine.
            Assert.IsNotNull(CreateViaService("ship-3", "wagon-2"));
        }

        [Test]
        public void Exclusivity_DriverAndAnimal_DoubleBooking_RefusedAtDispatch()
        {
            LogisticsShipmentState first = CreateViaService("ship-1", "wagon-1");
            LoadAndDispatch(first, driverA, teamA);

            LogisticsShipmentState second = CreateViaService("ship-2", "wagon-2");
            Assert.IsTrue(service.BeginLoading(second, handler, hooks, out _));
            int guard = 0;
            while (!second.loadApplied && guard++ < 20)
                service.WorkLoading(second, 60, hooks, out _);

            Assert.IsFalse(service.Dispatch(second, driverA, teamB, hooks, out string driverReason),
                "the same driver cannot haul two live shipments");
            StringAssert.Contains("driver", driverReason, driverReason);

            Assert.IsFalse(service.Dispatch(second, driverB, teamA, hooks, out string animalReason),
                "the same draft animal cannot haul two live shipments");
            StringAssert.Contains("animal", animalReason, animalReason);

            Assert.IsTrue(service.Dispatch(second, driverB, teamB, hooks, out string okReason), okReason);
        }

        [Test]
        public void Exclusivity_ReservationAuthority_HoldsDuringHaul_ReleasesOnCompletion()
        {
            var authority = new EquipmentReservationService();
            hooks.Reservations = authority;
            hooks.NowGameSeconds = 1000f;

            // An external hold on wagon-2 blocks even creating a shipment on it.
            var external = new ReservationRequest("external-task", "external holder");
            external.Items.Add(new ReservationItem(
                ReservationKeyConventions.Asset("wagon-2"), 1f, 0f, float.MaxValue));
            Assert.IsNull(authority.TryReserveAll(external, out _));
            Assert.IsNull(service.CreateShipment("ship-held", "mill-1", "store-1",
                10, GrainSpec(), "wagon-2", hooks, out string heldReason));
            StringAssert.Contains("reserved", heldReason, heldReason);

            LogisticsShipmentState shipment = CreateViaService("ship-1", "wagon-1");
            LoadAndDispatch(shipment, driverA, teamA);
            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Asset("wagon-1"), 1000f),
                "dispatch reserved the wagon through the authority");
            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Person(driverA.ToString()), 1000f),
                "dispatch reserved the driver through the authority");
            Assert.IsTrue(authority.IsReserved(ReservationKeyConventions.Animal(teamA[0].ToString()), 1000f),
                "dispatch reserved the draft animals through the authority");

            shipment.transitDurationGameSeconds = 10f;
            service.AdvanceTransit(shipment, 10f, hooks);
            Assert.IsTrue(service.BeginUnloading(shipment, handler, hooks, out _));
            int guard = 0;
            while (shipment.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
                service.WorkUnloading(shipment, 60, hooks, out _);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status);
            Assert.IsFalse(authority.IsReserved(ReservationKeyConventions.Asset("wagon-1"), 1000f),
                "completion released the wagon hold");
            Assert.IsFalse(authority.IsReserved(ReservationKeyConventions.Person(driverA.ToString()), 1000f),
                "completion released the driver hold");
        }

        [Test]
        public void Exclusivity_CompletedShipmentFreesWagonDriverAndAnimals()
        {
            LogisticsShipmentState first = CreateViaService("ship-1", "wagon-1");
            LoadAndDispatch(first, driverA, teamA);
            first.transitDurationGameSeconds = 10f;
            service.AdvanceTransit(first, 10f, hooks);
            Assert.IsTrue(service.BeginUnloading(first, handler, hooks, out _));
            int guard = 0;
            while (first.Status != LogisticsShipmentStatus.Completed && guard++ < 20)
                service.WorkUnloading(first, 60, hooks, out _);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, first.Status);

            LogisticsShipmentState second = CreateViaService("ship-2", "wagon-1");
            Assert.IsNotNull(second, "a completed shipment frees its wagon");
            LoadAndDispatch(second, driverA, teamA); // same crew is free again
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, second.Status);
        }

        // ---------- A4-11: production driver lifecycle ----------

        private FreightProductionDriver NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime)
        {
            var driver = new FreightProductionDriver(service);
            carrierRuntime = new BusinessRuntimeState();
            carrierRuntime.BindBusinessIdentity("biz-freight-1");
            driver.EnsureCompany("biz-freight-1", carrierRuntime);
            return driver;
        }

        private LogisticsShipmentState CreateViaDriver(FreightProductionDriver driver, string id, string wagonId)
        {
            LogisticsShipmentState shipment = driver.CreateShipment(
                "biz-freight-1", id, "mill-1", "store-1", 50, GrainSpec(), wagonId, hooks,
                driverA, teamA, 500, "biz-store-1", new List<string>(), out string reason);
            Assert.IsNotNull(shipment, reason);
            return shipment;
        }

        private void TickUntil(FreightProductionDriver driver, Func<bool> done, List<string> diag)
        {
            int guard = 0;
            while (!done() && guard++ < 60)
                driver.AdvanceTick(3600f, hooks, handler, diag);
        }

        [Test]
        public void FullLifecycle_ThroughProductionDriver_SettlesOnlyOnAcceptance()
        {
            FreightProductionDriver driver = NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime);
            var diag = new List<string>();
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");
            Assert.AreEqual(LogisticsShipmentStatus.Planned, shipment.Status);
            Assert.AreSame(shipment, driver.FindCompany("biz-freight-1").ActiveShipments[0],
                "the carrier company's runtime owns the accepted shipment");

            int cashBefore = carrierRuntime.CurrentCashCents;
            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Completed, diag);

            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, shipment.DeliveredQuantityUnits, "acceptance recorded the delivered quantity");
            Assert.AreEqual(0, shipment.RemainingQuantityUnits);
            Assert.AreEqual(50, recipientStock["store-1"], "goods landed in recipient custody exactly once");
            Assert.AreEqual(cashBefore + 500, carrierRuntime.CurrentCashCents,
                "the freight charge settled to the carrier after acceptance");

            // Further ticks settle nothing twice.
            TickUntil(driver, () => false, diag);
            Assert.AreEqual(cashBefore + 500, carrierRuntime.CurrentCashCents, "settlement is exactly-once");
            Assert.AreEqual(50, recipientStock["store-1"], "no duplicated delivery on later ticks");
        }

        [Test]
        public void RejectedDelivery_StaysInCustody_NoRevenue_ThenRetryCompletes()
        {
            FreightProductionDriver driver = NewDriverWithCarrier(out BusinessRuntimeState carrierRuntime);
            var diag = new List<string>();
            recipientAccepts = false;
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");
            int cashBefore = carrierRuntime.CurrentCashCents;

            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Delayed, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Delayed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, shipment.RemainingQuantityUnits, "rejected cargo stays in wagon custody — it never disappears");
            Assert.IsTrue(shipment.physicalTransportBound, "the wagon/team stay bound to the held cargo");
            Assert.IsFalse(shipment.deliveryApplied, "nothing was delivered");
            Assert.AreEqual(0, shipment.DeliveredQuantityUnits);
            Assert.IsFalse(recipientStock.ContainsKey("store-1"), "nothing reached the recipient");
            Assert.AreEqual(cashBefore, carrierRuntime.CurrentCashCents,
                "no revenue settles for cargo that was not legitimately accepted");
            Assert.IsTrue(service.VerifyConservation(shipment, 0, out string cons), cons);

            // The recipient relents: resume and the same cargo completes.
            recipientAccepts = true;
            service.ResumeShipment(shipment, hooks);
            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.Completed, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, recipientStock["store-1"]);
            Assert.AreEqual(cashBefore + 500, carrierRuntime.CurrentCashCents,
                "revenue settles only after the retry is accepted");
        }

        [Test]
        public void SaveLoad_MidTransport_RestoresExactlyOnce_CargoIntact()
        {
            FreightProductionDriver driver = NewDriverWithCarrier(out _);
            var diag = new List<string>();
            LogisticsShipmentState shipment = CreateViaDriver(driver, "ship-1", "wagon-1");
            shipment.transitDurationGameSeconds = 100000f;
            TickUntil(driver, () => shipment.Status == LogisticsShipmentStatus.InTransit, diag);
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, shipment.Status, string.Join("; ", diag));
            Assert.AreEqual(50, shipment.RemainingQuantityUnits);

            FreightShipmentService.FreightServiceSaveDto dto = driver.CaptureSaveDto();

            // Restore into a fresh driver + service (the loaded game).
            var service2 = new FreightShipmentService();
            service2.RegisterWagon(Wagon("wagon-1"));
            service2.RegisterWagon(Wagon("wagon-2"));
            var driver2 = new FreightProductionDriver(service2);
            var runtime2 = new BusinessRuntimeState();
            runtime2.BindBusinessIdentity("biz-freight-1");
            driver2.EnsureCompany("biz-freight-1", runtime2);
            List<LogisticsShipmentState> restored = driver2.RestoreSaveDto(dto, hooks, diag);
            Assert.AreEqual(1, restored.Count, "one shipment restored");
            Assert.AreEqual(1, driver2.AllShipments().Count, "the company holds it exactly once");
            LogisticsShipmentState r = restored[0];
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, r.Status);
            Assert.AreEqual(50, r.RemainingQuantityUnits, "cargo intact after restore");
            Assert.IsTrue(r.loadApplied);

            // Restoring the same DTO again still yields exactly one shipment
            // (the restore replaces the held instance — re-fetch it).
            driver2.RestoreSaveDto(dto, hooks, diag);
            Assert.AreEqual(1, driver2.AllShipments().Count, "a second restore does not duplicate the shipment");
            r = driver2.AllShipments()[0];
            Assert.AreEqual(LogisticsShipmentStatus.InTransit, r.Status);
            Assert.AreEqual(50, r.RemainingQuantityUnits, "cargo still intact after the second restore");

            // Finish the restored shipment with the fresh service's hooks.
            var hooks2 = new FreightShipmentHooks
            {
                Tasks = tasks,
                Budgets = budgets,
                DayIndex = Day,
                Diagnostics = diag,
                ReceiveShipment = hooks.ReceiveShipment,
            };
            r.transitDurationGameSeconds = 10f;
            int guard = 0;
            while (r.Status != LogisticsShipmentStatus.Completed && guard++ < 60)
                driver2.AdvanceTick(3600f, hooks2, handler, diag);
            Assert.AreEqual(LogisticsShipmentStatus.Completed, r.Status, string.Join("; ", diag));
            Assert.AreEqual(50, recipientStock["store-1"], "restored cargo delivered exactly once");
            Assert.IsTrue(service2.VerifyConservation(r, 50, out string cons), cons);
        }
    }
}
