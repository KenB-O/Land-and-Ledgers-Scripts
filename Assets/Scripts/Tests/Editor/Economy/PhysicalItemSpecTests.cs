// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F1 — physical item specification registry tests.
// Pins: append-only registration, conflicting re-registration refused,
// identical re-registration idempotent, household-id agreement with the
// catalog, non-household ids allowed, every seeded spec Unknown on all
// physical axes, measure guards, and the registry creating or moving no
// stock. Acceptance cases I (Unknown is not silently zero) and R (no change
// to existing economic transactions).

using System;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class PhysicalItemSpecTests
    {
        private static PhysicalItemSpec UnknownSpec(
            string itemId, string displayName, string unitLabel) =>
            new PhysicalItemSpec(
                itemId, displayName, unitLabel,
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalDimensions.Unknown,
                containerCompatible: false, perishable: false, handlingNote: string.Empty);

        private static void AssertAllPhysicalUnknown(PhysicalItemSpec spec)
        {
            Assert.IsTrue(!spec.UnitMassKg.IsKnown, spec.ItemId + ": mass must be Unknown.");
            Assert.IsTrue(!spec.UnitVolumeLiters.IsKnown, spec.ItemId + ": volume must be Unknown.");
            Assert.IsTrue(!spec.DimensionsMeters.IsKnown, spec.ItemId + ": dimensions must be Unknown.");
            Assert.IsTrue(!spec.DimensionsMeters.Length.IsKnown, spec.ItemId + ": length must be Unknown.");
            Assert.IsTrue(!spec.DimensionsMeters.Width.IsKnown, spec.ItemId + ": width must be Unknown.");
            Assert.IsTrue(!spec.DimensionsMeters.Height.IsKnown, spec.ItemId + ": height must be Unknown.");
        }

        [Test]
        public void SeededHouseholdSpecs_AgreeWithCatalogAndAreAllUnknown()
        {
            foreach (HouseholdItemDefinition definition in HouseholdItemCatalog.All)
            {
                Assert.IsTrue(
                    PhysicalItemSpecRegistry.TryGet(definition.ItemId, out PhysicalItemSpec spec),
                    "Seeded spec missing for household item '" + definition.ItemId + "'.");
                Assert.AreEqual(definition.DisplayName, spec.DisplayName);
                Assert.AreEqual(definition.UnitLabel, spec.UnitLabel);
                AssertAllPhysicalUnknown(spec);
            }
        }

        [Test]
        public void SeededNonHouseholdSpecs_AreAllUnknown()
        {
            string[] ids = { "bakery-flour", "bakery-notions", "bakery_flour_sack", "bakery-fuelwood" };
            foreach (string id in ids)
            {
                Assert.IsTrue(
                    PhysicalItemSpecRegistry.TryGet(id, out PhysicalItemSpec spec),
                    "Seeded spec missing for non-household item '" + id + "'.");
                AssertAllPhysicalUnknown(spec);
            }
        }

        [Test]
        public void SeededFlourSack_IsUnknown_NotTenPounds()
        {
            // U-1 (F1 decision): the flour-sack spec stays Unknown in this
            // slice; no caller or fixture mass is adopted as item truth.
            Assert.IsTrue(
                PhysicalItemSpecRegistry.TryGet("bakery_flour_sack", out PhysicalItemSpec spec),
                "Seeded spec missing for 'bakery_flour_sack'.");
            Assert.IsTrue(!spec.UnitMassKg.IsKnown, "Flour-sack mass must be Unknown, not a 10 lb adoption.");
            Assert.IsTrue(!spec.UnitMassKg.TryGetValue(out float massKg), "Unknown mass must not yield a value.");
            Assert.AreEqual(0f, massKg, 0.0001f);
        }

        [Test]
        public void Register_NewNonHouseholdId_Succeeds()
        {
            PhysicalItemSpec spec = UnknownSpec("f1-test-widget-a", "Test Widget A", "each");
            Assert.IsNull(PhysicalItemSpecRegistry.Register(spec));
            Assert.IsTrue(PhysicalItemSpecRegistry.TryGet("f1-test-widget-a", out PhysicalItemSpec found));
            Assert.AreEqual("Test Widget A", found.DisplayName);
        }

        [Test]
        public void Register_IdenticalReRegistration_IsIdempotent()
        {
            PhysicalItemSpec spec = UnknownSpec("f1-test-widget-b", "Test Widget B", "each");
            int before = PhysicalItemSpecRegistry.Count;
            Assert.IsNull(PhysicalItemSpecRegistry.Register(spec));
            Assert.AreEqual(before + 1, PhysicalItemSpecRegistry.Count);
            Assert.IsNull(PhysicalItemSpecRegistry.Register(UnknownSpec("f1-test-widget-b", "Test Widget B", "each")));
            Assert.AreEqual(before + 1, PhysicalItemSpecRegistry.Count);
        }

        [Test]
        public void Register_SeedIdenticalReRegistration_IsIdempotent()
        {
            int before = PhysicalItemSpecRegistry.Count;
            Assert.IsNull(PhysicalItemSpecRegistry.Register(UnknownSpec("flour", "Flour", "lb")));
            Assert.AreEqual(before, PhysicalItemSpecRegistry.Count);
        }

        [Test]
        public void Register_ConflictingReRegistration_RefusedAndOriginalKept()
        {
            PhysicalItemSpec original = UnknownSpec("f1-test-widget-c", "Test Widget C", "each");
            Assert.IsNull(PhysicalItemSpecRegistry.Register(original));
            PhysicalItemSpec conflicting = new PhysicalItemSpec(
                "f1-test-widget-c", "Test Widget C", "box",
                PhysicalMeasure.Unknown, PhysicalMeasure.Unknown, PhysicalDimensions.Unknown,
                containerCompatible: false, perishable: false, handlingNote: string.Empty);
            string refusal = PhysicalItemSpecRegistry.Register(conflicting);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "Conflicting re-registration must be refused.");
            Assert.IsTrue(PhysicalItemSpecRegistry.TryGet("f1-test-widget-c", out PhysicalItemSpec kept));
            Assert.AreEqual("each", kept.UnitLabel);
        }

        [Test]
        public void Register_HouseholdIdUnitLabelMismatch_Refused()
        {
            PhysicalItemSpec mismatched = UnknownSpec("flour", "Flour", "sack");
            string refusal = PhysicalItemSpecRegistry.Register(mismatched);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "Household-id UnitLabel mismatch must be refused.");
            Assert.IsTrue(PhysicalItemSpecRegistry.TryGet("flour", out PhysicalItemSpec kept));
            Assert.AreEqual("lb", kept.UnitLabel);
        }

        [Test]
        public void Register_HouseholdIdDisplayNameMismatch_Refused()
        {
            PhysicalItemSpec mismatched = UnknownSpec("flour", "Not Flour", "lb");
            string refusal = PhysicalItemSpecRegistry.Register(mismatched);
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "Household-id DisplayName mismatch must be refused.");
            Assert.IsTrue(PhysicalItemSpecRegistry.TryGet("flour", out PhysicalItemSpec kept));
            Assert.AreEqual("Flour", kept.DisplayName);
        }

        [Test]
        public void Register_NullSpecAndEmptyId_Refused()
        {
            Assert.IsTrue(!string.IsNullOrEmpty(PhysicalItemSpecRegistry.Register(null)));
            Assert.IsTrue(!string.IsNullOrEmpty(
                PhysicalItemSpecRegistry.Register(UnknownSpec(string.Empty, "No Id", "each"))));
            Assert.IsTrue(!string.IsNullOrEmpty(
                PhysicalItemSpecRegistry.Register(UnknownSpec("   ", "Blank Id", "each"))));
        }

        [Test]
        public void Register_NonHouseholdId_Allowed()
        {
            // A spec may exist for an id the household catalog never names.
            Assert.IsNull(PhysicalItemSpecRegistry.Register(
                UnknownSpec("f1-test-gadget", "Test Gadget", "crate")));
            Assert.IsTrue(PhysicalItemSpecRegistry.TryGet("f1-test-gadget", out PhysicalItemSpec found));
            Assert.IsTrue(!found.UnitMassKg.IsKnown);
        }

        [Test]
        public void PhysicalMeasure_GuardsRejectInvalidKnownValues()
        {
            Assert.IsTrue(!PhysicalMeasure.Known(-1f).IsKnown, "Negative mass must become Unknown.");
            Assert.IsTrue(!PhysicalMeasure.Known(float.NaN).IsKnown, "NaN must become Unknown.");
            Assert.IsTrue(!PhysicalMeasure.Known(float.PositiveInfinity).IsKnown, "Infinity must become Unknown.");
            Assert.IsTrue(!PhysicalMeasure.Known(float.NegativeInfinity).IsKnown, "-Infinity must become Unknown.");
            Assert.IsTrue(!new PhysicalMeasure(true, -2f).IsKnown, "Guarded constructor must reject negatives.");
            Assert.AreEqual(0f, PhysicalMeasure.Known(-1f).Value, 0.0001f);
        }

        [Test]
        public void PhysicalMeasure_KnownRoundTripsAndUnknownIsNotZero()
        {
            // Acceptance case I: Unknown mass/volume is not silently treated
            // as zero. Value reads 0 for an Unknown measure, but IsKnown is
            // false and TryGetValue refuses — the zero is an accessor default,
            // never a stored quantity.
            PhysicalMeasure known = PhysicalMeasure.Known(2.5f);
            Assert.IsTrue(known.IsKnown);
            Assert.AreEqual(2.5f, known.Value, 0.0001f);
            Assert.IsTrue(known.TryGetValue(out float knownValue));
            Assert.AreEqual(2.5f, knownValue, 0.0001f);

            PhysicalMeasure unknown = PhysicalMeasure.Unknown;
            Assert.IsTrue(!unknown.IsKnown);
            Assert.AreEqual(0f, unknown.Value, 0.0001f);
            Assert.IsTrue(!unknown.TryGetValue(out float unknownValue));
            Assert.AreEqual(0f, unknownValue, 0.0001f);
        }

        [Test]
        public void PhysicalDimensions_UnknownWhenAnyAxisUnknown()
        {
            Assert.IsTrue(!PhysicalDimensions.Unknown.IsKnown);
            Assert.IsTrue(PhysicalDimensions.Known(1f, 2f, 3f).IsKnown);
            Assert.AreEqual(1f, PhysicalDimensions.Known(1f, 2f, 3f).Length.Value, 0.0001f);
            PhysicalDimensions partial = new PhysicalDimensions(
                PhysicalMeasure.Known(1f), PhysicalMeasure.Unknown, PhysicalMeasure.Known(1f));
            Assert.IsTrue(!partial.IsKnown, "A partially known box is not a known box.");
            PhysicalDimensions guarded = PhysicalDimensions.Known(-1f, 2f, 3f);
            Assert.IsTrue(!guarded.IsKnown, "A negative axis must make the whole dimensions Unknown.");
        }

        [Test]
        public void TryGet_UnknownIdReturnsFalse()
        {
            Assert.IsTrue(!PhysicalItemSpecRegistry.TryGet("f1-no-such-item", out PhysicalItemSpec spec));
            Assert.IsNull(spec);
            Assert.IsTrue(!PhysicalItemSpecRegistry.TryGet(null, out spec));
            Assert.IsTrue(!PhysicalItemSpecRegistry.TryGet("   ", out spec));
        }

        [Test]
        public void RegistryCreatesOrMovesNoStock()
        {
            // Acceptance case R: registering specs changes no existing
            // economic state. The household inventory authority and the
            // household catalog are untouched by the spec registry, and the
            // spec id never leaks into the catalog as an item.
            var inventory = new HouseholdInventory(householdId: 9001);
            int lotsBefore = inventory.Lots.Count;
            int catalogBefore = HouseholdItemCatalog.All.Count;
            int registryBefore = PhysicalItemSpecRegistry.Count;

            Assert.IsNull(PhysicalItemSpecRegistry.Register(
                UnknownSpec("f1-test-no-stock", "Test No-Stock Item", "each")));

            Assert.AreEqual(lotsBefore, inventory.Lots.Count);
            Assert.AreEqual(0, inventory.Lots.Count);
            Assert.AreEqual(catalogBefore, HouseholdItemCatalog.All.Count);
            Assert.AreEqual(registryBefore + 1, PhysicalItemSpecRegistry.Count);
            Assert.IsNull(HouseholdItemCatalog.Get("f1-test-no-stock"));

            // The inventory authority still works normally afterwards: one
            // real lot added through it is exactly one lot, no more.
            Assert.IsNull(inventory.AddLot("flour", 5, 0, "f1-test-source"));
            Assert.AreEqual(1, inventory.Lots.Count);
            Assert.AreEqual("flour", inventory.Lots[0].ItemId);
            Assert.AreEqual(5, inventory.Lots[0].QuantityUnits);
        }
    }
}
