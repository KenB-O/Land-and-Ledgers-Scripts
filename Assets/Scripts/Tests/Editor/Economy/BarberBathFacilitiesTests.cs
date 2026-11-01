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
    /// D1B: the barber's bath facilities — the bath-tub workstation (Canon Part
    /// V scale column: bath tubs/hot-water capability), the hot-water gate, and
    /// tub-based bath scheduling. A shop with no tubs keeps the W1B chair-slot
    /// behavior; installing tubs moves baths onto dedicated tubs.
    /// </summary>
    [TestFixture]
    public sealed class BarberBathFacilitiesTests
    {
        private static Func<string, WorkstationComponentView?> AssetFinder(Dictionary<string, string> assetKinds)
        {
            return assetId =>
            {
                if (assetKinds.TryGetValue(assetId, out string kind))
                {
                    return (WorkstationComponentView?)new WorkstationComponentView
                    {
                        AssetId = assetId,
                        Kind = kind,
                        Condition01 = 0.9f,
                        IsUsable = true,
                    };
                }

                return null;
            };
        }

        private static Dictionary<string, string> ChairAndTubAssets()
        {
            return new Dictionary<string, string>
            {
                { "chair-asset-1", "barber-chair" },
                { "tub-asset-1", BarberServiceCatalog.BathTubEquipmentKind },
            };
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

        private static BarberShopRuntime TubShop(List<string> diag)
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            Assert.Null(runtime.AddTub("bath-room", new List<string> { "tub-asset-1" }, diag));
            Assert.Null(runtime.DeclareHotWaterSource("laundry stove and boiler in the back room", diag));
            return runtime;
        }

        [Test]
        public void BathStationDefinition_ExistsInCatalog_WithTubAndHotWater()
        {
            WorkstationDefinition def = null;
            foreach (var candidate in WorkstationCatalog.All)
            {
                if (candidate.WorkstationId == BarberServiceCatalog.BarberBathStationId) def = candidate;
            }

            Assert.NotNull(def, "The barber bath station must be a catalogued workstation.");
            Assert.Contains("bathing", def.CapabilitiesGranted);
            Assert.AreEqual(1, def.Components.Count);
            Assert.AreEqual(BarberServiceCatalog.BathTubEquipmentKind, def.Components[0].EquipmentKind);

            bool needsHotWater = false;
            bool needsBarbering = false;
            foreach (var req in def.SupportRequirements)
            {
                if (req.Kind == "infrastructure" && req.Detail == BarberServiceCatalog.HotWaterInfrastructureId)
                    needsHotWater = true;
                if (req.Kind == "operator-skill" && req.Detail == BarberServiceCatalog.BarberingSkillId)
                    needsBarbering = true;
            }

            Assert.True(needsHotWater, "The bath station needs hot-water infrastructure (Canon Part V scale column).");
            Assert.True(needsBarbering, "The bath station needs a barbering operator.");
        }

        [Test]
        public void AddTub_RequiresFunctionalSpace()
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var diag = new List<string>();

            Assert.NotNull(runtime.AddTub(null, new List<string> { "tub-asset-1" }, diag),
                "A tub with no functional space is refused.");
            Assert.AreEqual(0, runtime.Tubs.Count);
        }

        [Test]
        public void DeclareHotWaterSource_RequiresName()
        {
            var runtime = new BarberShopRuntime("barb-biz-1");
            var diag = new List<string>();

            Assert.NotNull(runtime.DeclareHotWaterSource("  ", diag), "An unnamed hot-water source heats nothing.");
            Assert.False(runtime.HasHotWater);

            Assert.Null(runtime.DeclareHotWaterSource("laundry stove and boiler", diag));
            Assert.True(runtime.HasHotWater);
            Assert.AreEqual("laundry stove and boiler", runtime.HotWaterSource);
        }

        [Test]
        public void CountReadyTubs_NoHotWater_NotReady_Loud()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            Assert.Null(runtime.AddTub("bath-room", new List<string> { "tub-asset-1" }, diag));

            // The basin is sound, but there is no hot water — a tub without hot
            // water is not a bath (Canon Part V scale column).
            int ready = runtime.CountReadyTubs(
                WorkstationCatalog.BarberBathStation, AssetFinder(ChairAndTubAssets()), diag);

            Assert.AreEqual(0, ready);
            StringAssert.Contains("hot-water", string.Join("\n", diag),
                "The refusal must name the missing hot-water capability.");
        }

        [Test]
        public void CountReadyTubs_HotWaterAndSoundTub_Ready()
        {
            var diag = new List<string>();
            var runtime = TubShop(diag);

            int ready = runtime.CountReadyTubs(
                WorkstationCatalog.BarberBathStation, AssetFinder(ChairAndTubAssets()), diag);

            Assert.AreEqual(1, ready, "A sound tub with hot water is a ready bath station.");
        }

        [Test]
        public void ServeDay_BathUsesTub_ChairStaysFreeForShaves()
        {
            var diag = new List<string>();
            var runtime = TubShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));
            Assert.Null(runtime.QueueCustomer(Customer(102, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, AssetFinder(ChairAndTubAssets()), diag);

            Assert.AreEqual(2, completed);

            BarberServiceRecord bath = runtime.CompletedServices[0];
            Assert.AreEqual(BarberServiceCatalog.BathId, bath.ServiceId);
            Assert.True(bath.ServedAtTub, "With tubs installed, the bath runs on a tub — not a chair.");
            Assert.AreEqual(0, bath.TubIndex);
            Assert.AreEqual(-1, bath.ChairIndex);
            Assert.AreEqual(0, bath.StartMinute);
            Assert.AreEqual(45, bath.EndMinute);

            BarberServiceRecord shave = runtime.CompletedServices[1];
            Assert.AreEqual(BarberServiceCatalog.ShaveId, shave.ServiceId);
            Assert.False(shave.ServedAtTub);
            Assert.AreEqual(0, shave.ChairIndex, "The chair is free for shaves — the tub took the bath.");
            Assert.AreEqual(15, shave.StartMinute,
                "The shave starts when the barber's 15 bath-labor minutes are done — hands run sequentially across chair and tub.");
            Assert.AreEqual(35, shave.EndMinute);
        }

        [Test]
        public void ServeDay_BathStaysQueued_WhenTubBroken()
        {
            var diag = new List<string>();
            var runtime = TubShop(diag);
            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));
            Assert.Null(runtime.QueueCustomer(Customer(102, BarberServiceCatalog.ShaveId), diag));

            // The finder knows the chair but not the tub asset — the tub is down.
            var finder = AssetFinder(new Dictionary<string, string> { { "chair-asset-1", "barber-chair" } });
            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, finder, diag);

            Assert.AreEqual(1, completed, "The shave still runs; the bath waits for its tub.");
            Assert.AreEqual(1, runtime.WaitingQueue.Count);
            Assert.AreEqual(BarberServiceCatalog.BathId, runtime.WaitingQueue[0].ServiceId,
                "The bath stays queued — never served without a ready tub, never dropped.");
            StringAssert.Contains("no ready tub", string.Join("\n", diag).ToLowerInvariant(),
                "The refusal must name the missing tub.");
        }

        [Test]
        public void ServeDay_BathStaysQueued_WhenNoHotWater()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            Assert.Null(runtime.AddTub("bath-room", new List<string> { "tub-asset-1" }, diag));
            // No hot water declared.

            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));
            Assert.Null(runtime.QueueCustomer(Customer(102, BarberServiceCatalog.ShaveId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation, AssetFinder(ChairAndTubAssets()), diag);

            Assert.AreEqual(1, completed, "The shave still runs; the cold bath waits.");
            Assert.AreEqual(1, runtime.WaitingQueue.Count);
            Assert.AreEqual(BarberServiceCatalog.BathId, runtime.WaitingQueue[0].ServiceId);
        }

        [Test]
        public void ServeDay_NoTubs_BathUsesChair_WidthBehaviorPreserved()
        {
            var diag = new List<string>();
            var runtime = new BarberShopRuntime("barb-biz-1");
            var registry = new EntityIdRegistry();
            BarberConsumableBootstrap.ApplyBootstrapEndowment(runtime.ConsumableStock, registry, 290, diag);
            Assert.Null(runtime.AddChair("shop-floor", new List<string> { "chair-asset-1" }, diag));
            // No tubs installed: the W1B chair-slot behavior stands.

            Assert.Null(runtime.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));

            int completed = runtime.ServeDay(300, true,
                WorkstationCatalog.BarberChairStation,
                AssetFinder(new Dictionary<string, string> { { "chair-asset-1", "barber-chair" } }), diag);

            Assert.AreEqual(1, completed);
            BarberServiceRecord bath = runtime.CompletedServices[0];
            Assert.False(bath.ServedAtTub, "No tubs installed — the bath runs on the chair slot (W1B).");
            Assert.AreEqual(0, bath.ChairIndex);
        }

        [Test]
        public void PopulateWorkstations_ExposesTubsToEquipmentGate()
        {
            var diag = new List<string>();
            var runtime = TubShop(diag);
            var registry = new BusinessWorkstations("barb-biz-1");

            runtime.PopulateWorkstations(registry,
                WorkstationCatalog.BarberChairStation, AssetFinder(ChairAndTubAssets()), diag);

            Assert.NotNull(registry.Get($"{BarberServiceCatalog.BarberBathStationId}-0"),
                "Each tub is exposed under barber-bath-station-{n}.");
            Assert.NotNull(registry.Get(BarberServiceCatalog.BarberBathStationId),
                "The bath-station alias resolves to the first ready tub with hot water.");
        }

        [Test]
        public void SaveLoad_RoundTripsTubsHotWaterAndLeases()
        {
            var diag = new List<string>();
            var runtime = TubShop(diag);
            Assert.Null(runtime.LeaseChair(new BarberChairLease
            {
                ChairIndex = 0,
                OperatorPersonId = EntityId.For(EntityKind.Person, 501),
                RentCentsPerDay = 50,
                StartDayIndex = 300,
                EndDayIndexExclusive = 310,
            }, diag));

            var restored = new BarberShopRuntime("barb-biz-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(1, restored.Tubs.Count, "Tubs must survive.");
            Assert.AreEqual("bath-room", restored.Tubs[0].Station.SpaceId);
            Assert.Contains("tub-asset-1", restored.Tubs[0].Station.ComponentAssetIds);
            Assert.True(restored.HasHotWater, "The hot-water source must survive.");
            Assert.AreEqual("laundry stove and boiler in the back room", restored.HotWaterSource);
            Assert.AreEqual(1, restored.Leases.Count, "Leases must survive.");
            Assert.AreEqual(50, restored.Leases[0].RentCentsPerDay);

            // The restored shop still serves baths on its tub — even though its
            // only chair is leased, because a bath needs a tub, not a chair.
            Assert.Null(restored.QueueCustomer(Customer(101, BarberServiceCatalog.BathId), diag));
            int completed = restored.ServeDay(301, true,
                WorkstationCatalog.BarberChairStation, AssetFinder(ChairAndTubAssets()), diag);
            Assert.AreEqual(1, completed);
            Assert.True(restored.CompletedServices[0].ServedAtTub,
                "The bath runs on the tub — the leased chair is irrelevant to it.");
        }
    }
}
