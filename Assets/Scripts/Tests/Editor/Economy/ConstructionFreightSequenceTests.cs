using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    /// <summary>
    /// QA-20 (freight half): the ConstructionMaterialFreightCoordinator
    /// mints shipment ids as cship-{sequence++} and holds no save slot,
    /// so a coordinator rebuilt after a load restarts its sequence at 0.
    /// The ids must still not collide with pre-save ones, because the
    /// freight service persists its shipments and refuses duplicates.
    ///
    /// This file exists separately from ConstructionFullChainTests.cs
    /// because that file is excluded from the Roslyn harness recipe
    /// (sources.txt, `!Tests/Editor/Economy/ConstructionFullChainTests.cs`)
    /// — it depends on production files the harness excludes
    /// (NpcHomeConstruction and friends) — so a regression test placed
    /// there would never run in the harness. The plan builder here is a
    /// minimal copy of that file's Chain.FreightPlan helper, using only
    /// types the harness recipe includes. Roslyn harness only. No Unity
    /// compilation, Test Runner, PlayMode, or scene wiring is claimed
    /// by this file.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionFreightSequenceTests
    {
        [Test]
        public void SequenceRestartsAfterLoad_DoesNotReissuePreSaveShipmentIds()
        {
            var diag = new List<string>();
            var stockpile = new ConstructionSiteStockpile { ProjectId = "proj-qa20" };
            var coordinator = new ConstructionMaterialFreightCoordinator();
            var plan = MakePlan(MakeWagon("wagon-1", 100000f, 100f), "site:proj-qa20", 41, 42, 43);

            Assert.IsNull(coordinator.FreightSaleToSite(ManualSale("sale-qa20-a"), plan, stockpile, 11, diag),
                string.Join("; ", diag));
            Assert.IsNull(coordinator.FreightSaleToSite(ManualSale("sale-qa20-b"), plan, stockpile, 11, diag),
                string.Join("; ", diag));
            Assert.AreEqual(2, plan.Freight.TrackedShipments.Count);
            Assert.AreEqual("cship-0", plan.Freight.TrackedShipments[0].ShipmentId);
            Assert.AreEqual("cship-1", plan.Freight.TrackedShipments[1].ShipmentId);

            // Save the service and restore into a fresh one, as a load does.
            var dto = plan.Freight.CaptureSaveDto(new List<LogisticsShipmentState>(plan.Freight.TrackedShipments));
            var restored = new FreightShipmentService();
            restored.RestoreSaveDto(dto, null);
            Assert.AreEqual(2, restored.TrackedShipments.Count);
            Assert.AreEqual("cship-0", restored.TrackedShipments[0].ShipmentId);
            Assert.AreEqual("cship-1", restored.TrackedShipments[1].ShipmentId);

            // A fresh coordinator restarts its sequence at 0 (the bug
            // condition). The next freights must still not re-issue cship-0
            // or cship-1.
            var fresh = new ConstructionMaterialFreightCoordinator();
            var plan2 = MakePlan(MakeWagon("wagon-1", 100000f, 100f), "site:proj-qa20", 41, 42, 43);
            plan2.Freight = restored;

            Assert.IsNull(fresh.FreightSaleToSite(ManualSale("sale-qa20-c"), plan2, stockpile, 11, diag),
                string.Join("; ", diag));
            Assert.AreEqual(3, restored.TrackedShipments.Count);
            Assert.AreEqual("cship-2", restored.TrackedShipments[2].ShipmentId);

            Assert.IsNull(fresh.FreightSaleToSite(ManualSale("sale-qa20-d"), plan2, stockpile, 11, diag),
                string.Join("; ", diag));
            Assert.AreEqual(4, restored.TrackedShipments.Count);
            Assert.AreEqual("cship-3", restored.TrackedShipments[3].ShipmentId);
        }

        private static ConstructionMaterialSale ManualSale(string saleId)
        {
            var sale = new ConstructionMaterialSale
            {
                SaleId = saleId, SupplierBusinessId = "lumber-yard",
                ProjectId = "proj-qa20", PackageId = "pkg-qa20", DayIndex = 11,
            };
            sale.Lines.Add(new ConstructionMaterialSaleLine
            {
                DisplayName = "lumber", Units = 600, UnitLabel = "board feet",
                UnitPriceCents = 40, Provenance = "yard lot L-9",
            });
            return sale;
        }

        private static EquipmentAsset MakeWagon(string assetId, float massKg, float volM3)
        {
            return new EquipmentAsset
            {
                AssetId = assetId,
                Kind = "wagon",
                DisplayName = "farm wagon",
                PayloadMassCapacityKg = massKg,
                CargoVolumeCapacityM3 = volM3,
                Condition01 = 1f,
            };
        }

        private static ConstructionFreightPlan MakePlan(EquipmentAsset wagon, string siteLabel,
            int driverPersonId, int loaderPersonId, int unloaderPersonId)
        {
            return new ConstructionFreightPlan
            {
                Freight = new FreightShipmentService(),
                Wagon = wagon,
                DriverId = EntityId.For(EntityKind.Person, driverPersonId),
                LoaderId = EntityId.For(EntityKind.Person, loaderPersonId),
                UnloaderId = EntityId.For(EntityKind.Person, unloaderPersonId),
                Tasks = new TaskAuthority(),
                Budgets = new WorkTimeBudgetStore(),
                Tracker = new PersonScheduleTracker(),
                SiteLabel = siteLabel,
                FreightChargeCents = 0,
                CargoSpec = new FreightCargoSpec { MassKgPerUnit = 1f, VolumeM3PerUnit = 0.01f },
            };
        }
    }
}
