// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F0 (expanded campaign 2026-11-16): furniture item-spec
// and cargo-spec tests. Pins: the eight first-wave furniture specs are
// registered with the plan's proposed Known values; every cargo spec
// agrees with its registry spec and validates through
// FreightCargoCapacity; a bed is 8 per production wagon with volume
// binding first; the brief's mixed load (bed + table + four chairs,
// 93 kg / 1.45 m3) fits one wagon trip; an Unknown registry measure
// refuses a freight spec loudly instead of silently becoming zero;
// conflicting re-registration is refused; furniture ids never leak into
// the household catalog.

using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Economy.Transport;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class FurnitureSpecsF0Tests
    {
        // The plan's proposed numbers (plan sections 4.1 and 6.1), pinned
        // here so any change — by F1, by another lane, or by the owner —
        // fails loudly instead of drifting silently.
        private static readonly (string, string, float, float, float, float, float, bool)[] Expected =
        {
            ("furniture.bed", "Bed", 40f, 500f, 2.00f, 0.60f, 0.42f, false),
            ("furniture.table", "Table", 25f, 350f, 1.50f, 0.90f, 0.26f, false),
            ("furniture.chair", "Chair", 7f, 150f, 0.50f, 0.50f, 0.60f, false),
            ("furniture.cabinet-small", "Small Cabinet", 30f, 400f, 1.00f, 0.50f, 0.80f, true),
            ("furniture.cabinet-large", "Large Cabinet", 45f, 600f, 1.50f, 0.50f, 0.80f, true),
            ("furniture.shelf", "Shelf", 10f, 120f, 1.00f, 0.30f, 0.40f, true),
            ("furniture.crate", "Crate", 12f, 150f, 0.60f, 0.50f, 0.50f, true),
            ("furniture.bedroll", "Bedroll", 8f, 100f, 2.00f, 0.25f, 0.20f, false),
        };

        private static EquipmentAsset ProductionWagon() =>
            new EquipmentAsset
            {
                AssetId = "f0-test-wagon",
                Kind = "wagon",
                DisplayName = "freight wagon",
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = "f0-test-freight",
                // The production opening wagon (OpeningTransportStock via
                // WagonAuthority.Configure): 450 kg / 4 m3.
                PayloadMassCapacityKg = WagonAuthority.DefaultMassCapacityKg,
                CargoVolumeCapacityM3 = WagonAuthority.DefaultVolumeCapacityM3,
            };

        [Test]
        public void AllEightFurnitureSpecs_AreRegisteredWithKnownAuthoredValues()
        {
            Assert.AreEqual(8, FurnitureItemSpecs.AllIds.Length);
            foreach ((string id, string name, float massKg, float volumeLiters,
                float lengthM, float widthM, float heightM, bool container) in Expected)
            {
                Assert.IsTrue(
                    PhysicalItemSpecRegistry.TryGet(id, out PhysicalItemSpec spec),
                    "Furniture spec missing for '" + id + "'.");
                Assert.AreEqual(name, spec.DisplayName);
                Assert.AreEqual("each", spec.UnitLabel);
                Assert.IsTrue(spec.UnitMassKg.IsKnown, id + ": mass must be Known.");
                Assert.IsTrue(spec.UnitVolumeLiters.IsKnown, id + ": volume must be Known.");
                Assert.IsTrue(spec.DimensionsMeters.IsKnown, id + ": dimensions must be Known.");
                Assert.AreEqual(massKg, spec.UnitMassKg.Value, 0.0001f);
                Assert.AreEqual(volumeLiters, spec.UnitVolumeLiters.Value, 0.0001f);
                Assert.AreEqual(lengthM, spec.DimensionsMeters.Length.Value, 0.0001f);
                Assert.AreEqual(widthM, spec.DimensionsMeters.Width.Value, 0.0001f);
                Assert.AreEqual(heightM, spec.DimensionsMeters.Height.Value, 0.0001f);
                Assert.AreEqual(container, spec.ContainerCompatible);
                Assert.IsFalse(spec.Perishable, id + ": furniture is not perishable.");
            }
        }

        [Test]
        public void FurnitureCargoSpecs_MatchRegistrySpecsAndValidate()
        {
            foreach (string id in FurnitureItemSpecs.AllIds)
            {
                FreightCargoSpec cargo = FurnitureCargoSpecs.SpecFor(id);
                Assert.IsTrue(cargo != null, "No cargo spec for '" + id + "'.");
                Assert.IsNull(
                    FreightCargoCapacity.ValidateCargoSpec(cargo),
                    "Cargo spec for '" + id + "' must validate.");
                Assert.IsTrue(
                    PhysicalItemSpecRegistry.TryGet(id, out PhysicalItemSpec registered),
                    "Registry spec missing for '" + id + "'.");
                Assert.AreEqual(registered.UnitMassKg.Value, cargo.MassKgPerUnit, 0.0001f);
                Assert.AreEqual(
                    registered.UnitVolumeLiters.Value / 1000f, cargo.VolumeM3PerUnit, 0.0001f);
            }
        }

        [Test]
        public void Bed_MaxUnitsPerTrip_OnProductionWagon_IsEightVolumeBinds()
        {
            EquipmentAsset wagon = ProductionWagon();
            FreightCargoSpec bed = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.BedId);
            Assert.AreEqual(8, FurnitureCargoSpecs.MaxUnitsPerTrip(bed, wagon));
            // Volume binds first: by mass alone the wagon takes 11 beds
            // (450 / 40), by volume alone it takes 8 (4 / 0.50).
            Assert.AreEqual(11, FreightCargoCapacity.MaxUnitsPerTrip(bed, 450f, 1000f));
            Assert.AreEqual(8, FreightCargoCapacity.MaxUnitsPerTrip(bed, 100000f, 4f));
            // A table is volume-bound at 11 (4 / 0.35); by mass it takes 18.
            FreightCargoSpec table = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.TableId);
            Assert.AreEqual(11, FurnitureCargoSpecs.MaxUnitsPerTrip(table, wagon));
        }

        [Test]
        public void MixedLoad_BedTableFourChairs_FitsOneWagonTrip()
        {
            // The brief's consolidation example, checked (plan section 6.2):
            // bed + table + four chairs = 93 kg / 1.45 m3, one wagon trip.
            FreightCargoSpec bed = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.BedId);
            FreightCargoSpec table = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.TableId);
            FreightCargoSpec chair = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.ChairId);
            float massKg = bed.MassKgPerUnit + table.MassKgPerUnit + 4f * chair.MassKgPerUnit;
            float volumeM3 = bed.VolumeM3PerUnit + table.VolumeM3PerUnit + 4f * chair.VolumeM3PerUnit;
            Assert.AreEqual(93f, massKg, 0.0001f);
            Assert.AreEqual(1.45f, volumeM3, 0.0001f);
            Assert.IsTrue(massKg <= WagonAuthority.DefaultMassCapacityKg, "Mixed load must fit the wagon by mass.");
            Assert.IsTrue(volumeM3 <= WagonAuthority.DefaultVolumeCapacityM3, "Mixed load must fit the wagon by volume.");
        }

        [Test]
        public void NineBeds_NeedTwoTrips_EightBedsNeedOne()
        {
            EquipmentAsset wagon = ProductionWagon();
            FreightCargoSpec bed = FurnitureCargoSpecs.SpecFor(FurnitureItemSpecs.BedId);
            Assert.AreEqual(1, FreightCargoCapacity.TripsNeeded(8, bed, wagon));
            Assert.AreEqual(2, FreightCargoCapacity.TripsNeeded(9, bed, wagon));
            Assert.AreEqual(0, FreightCargoCapacity.TripsNeeded(0, bed, wagon));
        }

        [Test]
        public void TryBuildFreightSpec_UnknownRegistrySpec_RefusesLoudly()
        {
            // "flour" is a seeded household spec with every physical field
            // Unknown. Its accessor default reads 0 kg — which must never
            // become a cargo spec, because a 0 per-unit measure does not
            // bind in FreightCargoCapacity and would price an unbounded load.
            string refusal = FurnitureCargoSpecs.TryBuildFreightSpec("flour", out FreightCargoSpec spec);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "Unknown mass must refuse a freight spec.");
            Assert.IsTrue(refusal.Contains("Unknown"), "Refusal must name the Unknown measure: " + refusal);
            Assert.IsTrue(spec == null, "A refused build must return no spec.");
            Assert.IsTrue(
                FurnitureCargoSpecs.TryBuildFreightSpec("bread", out _) != null,
                "Unknown volume must refuse a freight spec too.");
        }

        [Test]
        public void TryBuildFreightSpec_UnregisteredId_Refuses()
        {
            string refusal = FurnitureCargoSpecs.TryBuildFreightSpec("f0-no-such-item", out FreightCargoSpec spec);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "An unregistered id must refuse a freight spec.");
            Assert.IsTrue(spec == null, "A refused build must return no spec.");
            Assert.IsTrue(
                FurnitureCargoSpecs.TryBuildFreightSpec(null, out _) != null,
                "An empty id must refuse a freight spec.");
        }

        [Test]
        public void TryBuildFreightSpec_KnownFurnitureSpec_Succeeds()
        {
            Assert.IsNull(
                FurnitureCargoSpecs.TryBuildFreightSpec(FurnitureItemSpecs.BedId, out FreightCargoSpec spec));
            Assert.IsTrue(spec != null, "A Known furniture spec must build a freight spec.");
            Assert.AreEqual(40f, spec.MassKgPerUnit, 0.0001f);
            Assert.AreEqual(0.50f, spec.VolumeM3PerUnit, 0.0001f);
        }

        [Test]
        public void Register_ConflictingFurnitureReRegistration_RefusedAndOriginalKept()
        {
            var conflicting = new PhysicalItemSpec(
                FurnitureItemSpecs.BedId, "Bed", "each",
                PhysicalMeasure.Known(41f), PhysicalMeasure.Known(500f),
                PhysicalDimensions.Known(2.00f, 0.60f, 0.42f),
                containerCompatible: false, perishable: false, handlingNote: string.Empty);
            string refusal = PhysicalItemSpecRegistry.Register(conflicting);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "Conflicting re-registration must be refused.");
            Assert.IsTrue(
                PhysicalItemSpecRegistry.TryGet(FurnitureItemSpecs.BedId, out PhysicalItemSpec kept));
            Assert.AreEqual(40f, kept.UnitMassKg.Value, 0.0001f);
        }

        [Test]
        public void FurnitureSpecIds_AreNotHouseholdCatalogItems()
        {
            // Spec ids never leak into the household catalog as items.
            foreach (string id in FurnitureItemSpecs.AllIds)
            {
                Assert.IsTrue(HouseholdItemCatalog.Get(id) == null, "'" + id + "' must not be a catalog item.");
            }
        }

        [Test]
        public void Register_SeedIdenticalFurnitureReRegistration_IsIdempotent()
        {
            Assert.IsTrue(
                PhysicalItemSpecRegistry.TryGet(FurnitureItemSpecs.BedId, out PhysicalItemSpec bed));
            int before = PhysicalItemSpecRegistry.Count;
            Assert.IsNull(PhysicalItemSpecRegistry.Register(bed));
            Assert.AreEqual(before, PhysicalItemSpecRegistry.Count);
        }
    }
}
