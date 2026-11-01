using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1B: the barbershop runtime — chair pool throughput, the waiting queue,
    /// consumable gating with loud refusal, and save/load.
    /// </summary>
    [TestFixture]
    public sealed class BarberShopRuntimeTests
    {
        private static Func<string, WorkstationComponentView?> ChairFinder(params string[] goodAssetIds)
        {
            var good = new HashSet<string>(goodAssetIds);
            return assetId => good.Contains(assetId)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = "barber-chair",
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private static BarberShopRuntime TwoChairShop(List<string> diag)
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-2" }, diag));
            return runtime;
        }

        private static BarberCustomer Customer(int personSeq, string serviceId)
        {
            return new BarberCustomer
            {
                CustomerPersonId = EntityId.For(EntityKind.Person, personSeq),
                ServiceId = serviceId,
                RequestDayIndex = 300,
            };
        }

        [Test]
        public void ChairStationDefinition_ExistsInCatalog_WithBarberingCapability()
        {
            WorkstationDefinition def = null;
            foreach (var candidate in WorkstationCatalog.All)
            {
                if (candidate.WorkstationId == BarberServiceCatalog.BarberChairStationId) def = candidate;
            }

            Assert.NotNull(def, "The barber chair station must be a catalogued workstation.");
            Assert.Contains("barbering", def.CapabilitiesGranted);
            Assert.AreEqual(1, def.Components.Count);
            Assert.AreEqual("barber-chair", def.Components[0].EquipmentKind);
        }

        [Test]
        public void QueueCustomer_ValidatesPersonAndService()
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var diag = new List<string>();

            Assert.NotNull(runtime.QueueCustomer(null, diag), "Null customer refused.");
            Assert.NotNull(runtime.QueueCustomer(new BarberCustomer
            {
                CustomerPersonId = EntityId.Invalid,
                ServiceId = BarberServiceCatalog.ShaveId,
            }, diag), "Anonymous customer refused.");
            Assert.NotNull(runtime.QueueCustomer(Customer(101, "barb.perm-wave"), diag),
                "Unknown service refused.");
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.ShaveId), diag));
            Assert.AreEqual(1, runtime.WaitingQueue.Count);
        }

        [Test]
        public void AddChair_RequiresFunctionalSpace()
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var diag = new List<string>();

            Assert.NotNull(runtime.AddChair("", new List<string> { "chair-asset-1" }, diag),
                "A chair with no space is refused — a room alone never grants the workstation.");
            Assert.AreEqual(0, runtime.Chairs.Count);
        }

        [Test]
        public void ServeDay_NoKit_NoService_QueueStaysVisible()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, barberKitUsable: false,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            Assert.AreEqual(0, completed, "No usable kit — the NX-1 teeth gate stops the whole day.");
            Assert.AreEqual(1, runtime.WaitingQueue.Count, "The queue stays visible, never silently dropped.");
        }

        [Test]
        public void ServeDay_NoReadyChair_NoService()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.ShaveId), diag));

            // Finder knows no chair assets — neither chair is ready.
            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder(), diag);

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, runtime.WaitingQueue.Count);
            Assert.AreEqual(0, runtime.CountReadyChairs(WorkstationCatalog.BarberChairStation, ChairFinder(), diag));
        }

        [Test]
        public void ServeDay_OneChair_ServesSequentially()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));

            for (int i = 0; i < 3; i++)
                Assert.Null(runtime.QueueCustomer(Customer(101 + i, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1"), diag);

            Assert.AreEqual(3, completed);
            Assert.AreEqual(0, runtime.WaitingQueue.Count);
            Assert.AreEqual(0, runtime.CompletedServices[0].StartMinute);
            Assert.AreEqual(20, runtime.CompletedServices[1].StartMinute);
            Assert.AreEqual(40, runtime.CompletedServices[2].StartMinute);
            foreach (var record in runtime.CompletedServices)
                Assert.AreEqual(0, record.ChairIndex, "One chair — everything is sequential.");
        }

        [Test]
        public void ServeDay_BathSoaksWhileBarberShaves_ConcurrencyProof()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));
            Assert.Null(runtime.QueueCustomer(Customer(102, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            Assert.AreEqual(2, completed);
            BarberServiceRecord bath = runtime.CompletedServices[0];
            BarberServiceRecord shave = runtime.CompletedServices[1];

            Assert.AreEqual(BarberServiceCatalog.BathId, bath.ServiceId);
            Assert.AreEqual(0, bath.StartMinute);
            Assert.AreEqual(45, bath.EndMinute);
            Assert.AreEqual(0, bath.ChairIndex);

            Assert.AreEqual(BarberServiceCatalog.ShaveId, shave.ServiceId);
            Assert.AreEqual(1, shave.ChairIndex, "The shave takes the second chair — concurrency, not queueing.");
            Assert.AreEqual(15, shave.StartMinute,
                "The shave starts while the bath still soaks: the barber's 15 bath-labor minutes are done.");
            Assert.AreEqual(35, shave.EndMinute);
            Assert.AreEqual(15, shave.FeeCents, "Default shave fee comes from the schedule as data.");
        }

        [Test]
        public void ServeDay_SoapShortfall_CustomerStaysQueued_OthersServed()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));

            // Exactly one soap portion and plenty of linen — no bootstrap.
            Assert.Null(runtime.ConsumableStock.ReceiveLot(new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.SoapItemId,
                Units = 1,
                AcquiredDayIndex = 300,
                ImportOrderId = "IMP-1870-044",
                OriginName = "Off-map wholesale drug and toiletries house, via railhead",
            }, diag));
            Assert.Null(runtime.ConsumableStock.ReceiveLot(new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.LinenItemId,
                Units = 10,
                AcquiredDayIndex = 300,
                ImportOrderId = "IMP-1870-044",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
            }, diag));

            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.ShaveId), diag)); // soap 1
            Assert.Null(runtime.QueueCustomer(Customer(102, BarberServiceCatalog.BathId), diag));  // soap 2 — short
            Assert.Null(runtime.QueueCustomer(Customer(103, BarberServiceCatalog.HaircutId), diag)); // soap 0

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1"), diag);

            Assert.AreEqual(2, completed, "Shave and haircut run; the bath is refused on soap.");
            Assert.AreEqual(1, runtime.WaitingQueue.Count);
            Assert.AreEqual(BarberServiceCatalog.BathId, runtime.WaitingQueue[0].ServiceId,
                "The soap-starved bath stays queued — never served on conjured stock, never dropped.");
            Assert.AreEqual(0, runtime.ConsumableStock.UnitsOnHand(BarberServiceCatalog.SoapItemId));

            // The served shave carries its soap lot's provenance.
            BarberServiceRecord shave = runtime.CompletedServices[0];
            Assert.AreEqual(2, shave.ConsumablesUsed.Count, "Shave: one soap line + one linen line.");
            StringAssert.Contains("IMP-1870-044", shave.ConsumablesUsed[0].ProvenanceChain,
                "Soap dispenses first; its line names the import order.");
        }

        [Test]
        public void ServeDay_ProfessionalDayCapsThroughput_QueueStaysVisible()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            // Bootstrap linen is 30 — top up so labor, not linen, is the cap.
            var registry = new EntityIdRegistry();
            Assert.Null(runtime.ConsumableStock.ReceiveLot(new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.LinenItemId,
                Units = 20,
                AcquiredDayIndex = 300,
                ImportOrderId = "IMP-1870-045",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
            }, diag));

            for (int i = 0; i < 40; i++)
                Assert.Null(runtime.QueueCustomer(Customer(200 + i, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            Assert.AreEqual(30, completed, "600 professional minutes / 20 per shave = 30 — the barber caps before the chairs do.");
            Assert.AreEqual(10, runtime.WaitingQueue.Count,
                "The unserved stay queued and visible — never silently dropped.");
        }

        [Test]
        public void ServeDay_ServiceRecord_CarriesFeeAndProvenance()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            runtime.SetFeeSchedule(new BarberFeeSchedule(12, 22, 33));
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            Assert.AreEqual(1, completed);
            BarberServiceRecord bath = runtime.CompletedServices[0];
            Assert.AreEqual(33, bath.FeeCents, "Fee comes from the schedule as data.");
            Assert.AreEqual(2, bath.ConsumablesUsed.Count, "Bath: soap line + linen line.");
            foreach (var line in bath.ConsumablesUsed)
                StringAssert.Contains("BOOTSTRAP", line.ProvenanceChain,
                    "Every consumed unit traces to its lot.");
        }

        [Test]
        public void PopulateWorkstations_ExposesChairsToEquipmentGate()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            var finder = ChairFinder("chair-asset-1"); // only chair 0 is ready
            var registry = new BusinessWorkstations("barb-biz-1");

            runtime.PopulateWorkstations(registry,
                WorkstationCatalog.BarberChairStation, finder, diag);

            Assert.NotNull(registry.Get("barber-chair-station-0"), "Each chair is individually addressable.");
            Assert.NotNull(registry.Get("barber-chair-station-1"));
            WorkstationInstance alias = registry.Get(BarberServiceCatalog.BarberChairStationId);
            Assert.NotNull(alias, "The declared workstation id resolves while a chair is ready.");
            StringAssert.Contains("barber-chair-0", alias.InstanceId,
                "The alias points at the first READY chair.");
        }

        [Test]
        public void PopulateWorkstations_NoReadyChair_AliasRefusedHonestly()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            var registry = new BusinessWorkstations("barb-biz-1");

            runtime.PopulateWorkstations(registry,
                WorkstationCatalog.BarberChairStation, ChairFinder(), diag);

            Assert.Null(registry.Get(BarberServiceCatalog.BarberChairStationId),
                "No ready chair — the alias stays unregistered so the gate refuses honestly.");
        }

        [Test]
        public void SaveLoad_RoundTripsChairsStockFeesAndQueue()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            runtime.SetFeeSchedule(new BarberFeeSchedule(12, 22, 33));
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.HaircutId), diag));

            var restored = new BarberShopRuntime("barb-biz-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(22, restored.FeeSchedule.HaircutFeeCents, "Custom fees must survive.");
            Assert.AreEqual(BarberConsumableBootstrap.BootstrapSoapUnits,
                restored.ConsumableStock.UnitsOnHand(BarberServiceCatalog.SoapItemId),
                "Shelf stock must survive.");
            Assert.AreEqual(2, restored.Chairs.Count, "Chairs must survive.");
            Assert.AreEqual("shop-floor", restored.Chairs[0].Station.SpaceId);
            Assert.Contains("chair-asset-2", restored.Chairs[1].Station.ComponentAssetIds);
            Assert.AreEqual(1, restored.WaitingQueue.Count, "The queue must survive.");
            Assert.AreEqual(BarberServiceCatalog.HaircutId, restored.WaitingQueue[0].ServiceId);

            // The restored shop still works: chairs evaluate ready after load.
            int completed = restored.ServeDay(301, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);
            Assert.AreEqual(1, completed);
            Assert.AreEqual(22, restored.CompletedServices[0].FeeCents);
        }

        [Test]
        public void CloseDay_SummarizesCoverage()
        {
            var diag = new List<string>();
            var runtime = TwoChairShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.ShaveId), diag));
            runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, ChairFinder("chair-asset-1", "chair-asset-2"), diag);

            runtime.CloseDay(300, diag);
            StringAssert.Contains("2 chair(s)", runtime.CoverageSummary());
            StringAssert.Contains("1 service(s) completed", runtime.CoverageSummary());
        }
    }
}
