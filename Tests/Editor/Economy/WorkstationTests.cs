using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// EQP-2: Group A workstations instantiated from the canon equipment survey.
    /// Capabilities derive from actual components + space (Tech X §3.5) — never
    /// from the room alone. Damage blocks work; it never silently debuffs (§3.9).
    /// </summary>
    [TestFixture]
    public sealed class WorkstationTests
    {
        private static EquipmentAsset Asset(string id, string kind, float condition = 0.9f) =>
            new EquipmentAsset { AssetId = id, Kind = kind, Condition01 = condition };

        private static WorkstationComponentView? FindIn(Dictionary<string, EquipmentAsset> byId, string assetId)
        {
            if (byId.TryGetValue(assetId, out var asset) && asset != null)
                return new WorkstationComponentView
                {
                    AssetId = asset.AssetId, Kind = asset.Kind,
                    Condition01 = asset.Condition01, IsUsable = asset.IsUsable,
                };
            return null;
        }

        private static WorkstationInstance StationFor(string workstationId, string spaceId,
            Dictionary<string, EquipmentAsset> parts, out System.Func<string, WorkstationComponentView?> finder)
        {
            var station = new WorkstationInstance
            {
                InstanceId = workstationId + "-test",
                WorkstationId = workstationId,
                BusinessInstanceId = "biz-test",
                SpaceId = spaceId,
            };
            foreach (string id in parts.Keys) station.InstallComponent(id);
            var local = parts;
            finder = id => FindIn(local, id);
            return station;
        }

        [Test]
        public void Catalog_AllSevenWorkstations_DefinedAsData()
        {
            var all = WorkstationCatalog.All;
            Assert.AreEqual(7, all.Count, "Group A: forge, oven, bar, mill, butcher block, saw line, assay bench.");
            foreach (var def in all)
            {
                Assert.IsTrue(def.Components.Count > 0, $"{def.WorkstationId} must name components.");
                Assert.IsTrue(def.CapabilitiesGranted.Count > 0, $"{def.WorkstationId} must grant capabilities.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(def.SourceNote), $"{def.WorkstationId} must cite its source.");
            }
            Assert.IsTrue(WorkstationCatalog.ForgeStation.CapabilitiesGranted.Contains("smith-equipment"));
            Assert.IsTrue(WorkstationCatalog.GrainMillStation.CapabilitiesGranted.Contains("mill-grain"));
        }

        [Test]
        public void ForgeStation_MigratesFromFlagToDerivedReadiness()
        {
            var smithy = new BlacksmithRuntime("smith-1", "Test Smithy", forgeStationReady: false);
            Assert.IsFalse(smithy.ForgeStationReady);

            var assets = new List<EquipmentAsset>
            {
                Asset("forge-1", "forge"), Asset("anvil-1", "anvil"), Asset("tools-1", "smith-hand-tools"),
            };
            var diagnostics = new List<string>();
            string reason = smithy.EstablishForgeStationFromComponents(assets, "smithy-main", diagnostics);
            Assert.IsNull(reason, "All components present — the station is ready (Tech X §3.5).");
            Assert.IsTrue(smithy.ForgeStationReady);
            Assert.IsTrue(smithy.ForgeStationDerivedFromComponents);

            // Wreck the anvil: readiness must collapse, not silently degrade.
            assets[1].ApplyWear(0.99f);
            reason = smithy.EstablishForgeStationFromComponents(assets, "smithy-main", new List<string>());
            Assert.IsNotNull(reason, "A wrecked anvil blocks the station (Tech X §3.9).");
            StringAssert.Contains("anvil", reason);
            Assert.IsFalse(smithy.ForgeStationReady);
        }

        [Test]
        public void ButcherBlock_GatesSlaughterOnceEstablished()
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState animal = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Male,
                AnimalOwnerKind.Business, "biz_butcher_001", 10, "test purchase");

            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");

            // No station established: legacy behavior — slaughter proceeds.
            var diagnostics = new List<string>();
            Assert.IsNull(butcher.CheckButcherBlock(diagnostics));

            // Station established but missing the block-table: slaughter refuses.
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["saw-1"] = Asset("saw-1", "meat-saw"),
                ["rail-1"] = Asset("rail-1", "rail-hooks"),
                ["scale-1"] = Asset("scale-1", "scale-set"),
            };
            var station = StationFor("butcher-block", "butcher-shop", parts, out var finder);
            butcher.EstablishButcherBlock(station, finder);
            diagnostics = new List<string>();
            CarcassYield blocked = butcher.Slaughter(registry, animal.AnimalId, 1000, 12, diagnostics);
            Assert.IsNull(blocked, "Missing block-table must refuse slaughter (Tech X §3.5, §3.9).");

            // Complete the station: slaughter proceeds.
            parts["block-1"] = Asset("block-1", "block-table");
            station = StationFor("butcher-block", "butcher-shop", parts, out finder);
            butcher.EstablishButcherBlock(station, finder);
            diagnostics = new List<string>();
            CarcassYield yield = butcher.Slaughter(registry, animal.AnimalId, 1000, 12, diagnostics);
            Assert.IsNotNull(yield, "Complete block — slaughter proceeds with carcass balance intact.");
        }

        [Test]
        public void MillStation_GatesMillingOnceEstablished()
        {
            var ids = new EntityIdRegistry();
            var miller = new Miller("mill-1");
            var diagnostics = new List<string>();

            CropLot GrainLot() => new CropLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9001),
                Crop = CropKind.Wheat, ProductKind = "grain", QuantityUnits = 100,
                FieldId = "wheat-1", FarmId = "farm-1", HarvestDayIndex = 200, SeedSource = "saved seed",
            };

            // No station: legacy behavior — milling proceeds.
            Assert.IsNotNull(miller.MillGrain(ids, GrainLot(), EntityId.Invalid, 220, diagnostics));

            // Station established without millstones: milling refuses.
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["hopper-1"] = Asset("hopper-1", "hopper-feed"),
                ["sifter-1"] = Asset("sifter-1", "sifter-bolter"),
            };
            var station = StationFor("grain-mill-station", "mill", parts, out var finder);
            miller.EstablishMillStation(station, finder);
            Assert.IsNull(miller.MillGrain(ids, GrainLot(), EntityId.Invalid, 220, new List<string>()),
                "Missing millstones must refuse milling (Tech X §3.5, §3.9).");

            // Complete the station: milling proceeds.
            parts["stones-1"] = Asset("stones-1", "millstones");
            station = StationFor("grain-mill-station", "mill", parts, out finder);
            miller.EstablishMillStation(station, finder);
            Assert.IsNotNull(miller.MillGrain(ids, GrainLot(), EntityId.Invalid, 220, new List<string>()));
        }

        [Test]
        public void MillStation_MotivePower_IsASeparateGenuineConstraint()
        {
            var def = WorkstationCatalog.GrainMillStation;
            var noPower = new List<MotivePowerSource>();
            Assert.IsFalse(def.MotivePowerSatisfied(noPower, new List<string>()),
                "Tech X §3.6: stones without water or steam do not mill.");
            var withWater = new List<MotivePowerSource>
            {
                new MotivePowerSource(MotivePowerKind.Water, "mill race"),
            };
            Assert.IsTrue(def.MotivePowerSatisfied(withWater, new List<string>()));
        }

        [Test]
        public void BusinessWorkstations_Registry_TracksReadiness()
        {
            var registry = new BusinessWorkstations("biz-saloon-1");
            var station = registry.GetOrCreate("saloon-bar");
            station.SpaceId = "saloon-main";
            station.InstallComponent("counter-1");

            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["counter-1"] = Asset("counter-1", "bar-counter"),
            };
            var diagnostics = new List<string>();
            string reason = registry.CheckReady("saloon-bar", WorkstationCatalog.SaloonBar,
                id => FindIn(parts, id), diagnostics);
            Assert.IsNotNull(reason, "Glassware and faro layout still missing.");
            Assert.AreSame(station, registry.Get("saloon-bar"));
            Assert.IsNull(registry.Get("nope"));
        }

        [Test]
        public void AssayBench_StandaloneService_NoMineRequired()
        {
            var def = WorkstationCatalog.AssayBench;
            Assert.IsTrue(def.CapabilitiesGranted.Contains("assay-ore"));
            var station = new WorkstationInstance
            {
                InstanceId = "assay-1", WorkstationId = "assay-bench",
                BusinessInstanceId = "biz-assay-1", SpaceId = "assay-office",
            };
            var parts = new Dictionary<string, EquipmentAsset>
            {
                ["furnace-1"] = Asset("furnace-1", "assay-furnace"),
                ["balance-1"] = Asset("balance-1", "assay-balance"),
                ["crucible-1"] = Asset("crucible-1", "crucible-set"),
            };
            foreach (string id in parts.Keys) station.InstallComponent(id);
            Assert.IsNull(station.EvaluateReady(def, id => FindIn(parts, id), new List<string>()),
                "Assaying needs the bench + furnace, not a mine (survey §2.19).");
        }
    }
}
