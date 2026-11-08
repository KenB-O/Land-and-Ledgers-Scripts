// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F2 — shared capacity query tests (P9 plan section 2.4,
// section 4 Slice F2). Pins: load resolution through the F1 registry
// (Known totals only where a Known spec exists; Unknown naming the first
// unknown item); per-axis behaviour (a count-bounded container answers for
// Unknown-mass items, a mass-bounded container refuses them naming the
// item); delegation fidelity on every leg (the evaluator verdict equals the
// underlying authority verdict — wagon 450 kg / 4 m3 boundary,
// category-target boundary, zone placement boundary, equipment envelope
// boundary); the person query returns Unknown with the missing inputs named
// and no demographic field exists on the inputs type; HorsePack returns
// "no authority". Acceptance cases H (known limits reject impossible
// loads), I (Unknown is not silently zero), J (wagon query agrees with its
// authoritative equipment and cargo records), K (no double-count of
// category pool and identified lots).
//
// The evaluator is constructed in tests against the real authorities (real
// EquipmentCapacityService over a real reservation service, real
// BusinessRuntimeState, real PhysicalStorageZone, real wagon asset) — the
// P1 Slice 0/6 real-authority, test-side driver pattern. No flow calls the
// evaluator in this slice.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Storage;
using LandLedgers.Economy.Transport;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class PhysicalCapacityContractsTests
    {
        private const string WidgetId = "test-known-widget";
        private const string BaleId = "test-known-bale";

        private static bool knownSpecsEnsured;

        /// <summary>
        /// Test-only Known specs (append-only registry; identical
        /// re-registration is idempotent, so this is safe to call per test).
        /// Widget: 2 kg, 10 L per unit. Bale: 1 kg, 100 L per unit. Both are
        /// non-household ids, so no catalog agreement rule applies. These are
        /// the only Known specs in the suite; every seeded spec stays Unknown.
        /// </summary>
        private static void EnsureKnownSpecs()
        {
            if (knownSpecsEnsured) return;
            knownSpecsEnsured = true;
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                WidgetId, "Test Widget", "each",
                PhysicalMeasure.Known(2f), PhysicalMeasure.Known(10f), PhysicalDimensions.Unknown,
                containerCompatible: true, perishable: false, handlingNote: string.Empty)));
            Assert.IsNull(PhysicalItemSpecRegistry.Register(new PhysicalItemSpec(
                BaleId, "Test Bale", "each",
                PhysicalMeasure.Known(1f), PhysicalMeasure.Known(100f), PhysicalDimensions.Unknown,
                containerCompatible: true, perishable: false, handlingNote: string.Empty)));
        }

        private static PhysicalLoad Load(params (string ItemId, int Units)[] lines) =>
            new PhysicalLoad(lines);

        private static PhysicalCapacityQuery Query(PhysicalContainerKind kind, string key, PhysicalLoad load) =>
            new PhysicalCapacityQuery(kind, key, load, dayIndex: 10);

        private static EquipmentCapacityService MakeEquipmentService(out EquipmentReservationService reservations)
        {
            reservations = new EquipmentReservationService();
            var service = new EquipmentCapacityService(reservations);
            EquipmentConditionProfiles.RegisterAll(service);
            return service;
        }

        private static BusinessRuntimeState MakeStore(string businessId, string categoryId, int targetStockUnits)
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity(businessId);
            state.EnsureCategoryStock(categoryId, 0, targetStockUnits);
            return state;
        }

        private static PhysicalStorageZone MakeZone(
            StorageCapacityLimit mass, StorageCapacityLimit volume, string zoneId = "zone-1")
        {
            return new PhysicalStorageZone(
                new PhysicalStorageZoneDefinition(zoneId, "site-1", StorageExposure.Indoor, mass, volume),
                (authority, lotId) => true);
        }

        private static PhysicalStorageZone CloneZoneWithSamePlacements(PhysicalStorageZone source)
        {
            var clone = new PhysicalStorageZone(
                new PhysicalStorageZoneDefinition(
                    source.Definition.StorageZoneId, source.Definition.LocationId, source.Definition.Exposure,
                    source.Definition.MassCapacityKg, source.Definition.VolumeCapacityCubicMeters),
                (authority, lotId) => true);
            foreach (StoredLotReference placed in source.Placements)
                Assert.IsTrue(clone.TryPlace(
                    new StoredLotReference(placed.InventoryAuthorityId, placed.LotId, placed.Load), out _));
            return clone;
        }

        private static EquipmentAsset MakeWagon(string assetId, float massKg, float volumeM3)
        {
            var wagon = new EquipmentAsset { AssetId = assetId, Kind = WagonAuthority.DefaultKind, Condition01 = 1f };
            Assert.IsNull(WagonAuthority.Configure(wagon, "GeneralFarmFreight", massKg, volumeM3, "farm-yard-1"));
            return wagon;
        }

        private static PhysicalCapacityEvaluator MakeEvaluator(
            IEquipmentCapacityService equipment = null,
            Func<string, BusinessRuntimeState> businessById = null,
            Func<string, PhysicalStorageZone> zoneById = null,
            Func<string, EquipmentAsset> wagonById = null) =>
            new PhysicalCapacityEvaluator(equipment, businessById, zoneById, wagonById);

        // ---------- load resolution ----------

        [Test]
        public void LoadResolution_KnownTotalsSumPerUnitMassAndVolume()
        {
            EnsureKnownSpecs();
            PhysicalLoadResolution resolved = Load((WidgetId, 3)).Resolve();
            Assert.AreEqual(3, resolved.TotalUnits);
            Assert.IsTrue(resolved.IsMassKnown);
            Assert.AreEqual(6f, resolved.TotalMassKg.Value, 0.001f);
            Assert.IsTrue(resolved.IsVolumeKnown);
            Assert.AreEqual(30f, resolved.TotalVolumeLiters.Value, 0.001f);
            Assert.AreEqual(0.03f, resolved.TotalVolumeCubicMeters.Value, 0.0001f);
            Assert.IsTrue(resolved.IsFullyKnown);
            Assert.IsNull(resolved.FirstUnknownItemId);
        }

        [Test]
        public void LoadResolution_UnknownNamesFirstUnknownItemForBreadFlourAndSack()
        {
            // Bread: Unknown on all physical axes (seeded F1).
            PhysicalLoadResolution bread = Load(("bread", 14)).Resolve();
            Assert.IsTrue(!bread.IsMassKnown, "Bread mass must be Unknown, not zero.");
            Assert.IsTrue(!bread.IsVolumeKnown);
            Assert.AreEqual("bread", bread.FirstUnknownItemId);
            Assert.AreEqual("bread", bread.FirstUnknownMassItemId);
            Assert.AreEqual("bread", bread.FirstUnknownVolumeItemId);

            // Flour by the pound: Unknown as well.
            PhysicalLoadResolution flour = Load(("flour", 5)).Resolve();
            Assert.IsTrue(!flour.IsMassKnown);
            Assert.AreEqual("flour", flour.FirstUnknownItemId);

            // Millbrook sack: Unknown (U-1 decision stands).
            PhysicalLoadResolution sack = Load(("bakery_flour_sack", 2)).Resolve();
            Assert.IsTrue(!sack.IsMassKnown);
            Assert.AreEqual("bakery_flour_sack", sack.FirstUnknownItemId);

            // First unknown in line order, not alphabetical: a Known line first.
            EnsureKnownSpecs();
            PhysicalLoadResolution mixed = Load((WidgetId, 1), ("bread", 1), ("flour", 1)).Resolve();
            Assert.AreEqual("bread", mixed.FirstUnknownItemId);
            Assert.IsTrue(!mixed.IsMassKnown);
        }

        [Test]
        public void LoadResolution_EmptyLoadIsKnownZeroAndUnregisteredItemIsUnknown()
        {
            PhysicalLoadResolution empty = PhysicalLoad.Empty.Resolve();
            Assert.AreEqual(0, empty.TotalUnits);
            Assert.IsTrue(empty.IsFullyKnown);
            Assert.AreEqual(0f, empty.TotalMassKg.Value, 0.001f);

            PhysicalLoadResolution missing = Load(("no-such-item", 1)).Resolve();
            Assert.IsTrue(!missing.IsFullyKnown);
            Assert.AreEqual("no-such-item", missing.FirstUnknownItemId);
        }

        // ---------- equipment leg ----------

        [Test]
        public void Equipment_CountBoundedContainerAnswersForUnknownMassItems()
        {
            // Shelf: Container, 40 items, no mass axis — count only.
            EquipmentCapacityService service = MakeEquipmentService(out _);
            EquipmentCapacityDescriptor shelf = EquipmentConditionProfiles.GetDescriptor("shelf");
            Assert.IsNotNull(shelf, "Shelf descriptor must be authored.");
            service.RegisterCapacity("asset:shelf-1", shelf, EquipmentConditionProfiles.GetProfile("shelf"));
            var evaluator = MakeEvaluator(equipment: service);
            var context = new PhysicalCapacityEvaluationContext { EquipmentDescriptor = shelf };

            // 14 loaves of Unknown mass: the count axis still answers.
            PhysicalCapacityQuery fits = Query(PhysicalContainerKind.Equipment, "asset:shelf-1", Load(("bread", 14)));
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(fits, context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);
            Assert.IsTrue(verdict.Outcome == PhysicalCapacityOutcome.Fits
                == service.CanAccept("asset:shelf-1", 1f, 14f, 0f),
                "Evaluator verdict must equal the equipment authority verdict.");

            // Envelope boundary: 40 fits, 41 refuses — both match the authority.
            Assert.AreEqual(PhysicalCapacityOutcome.Fits,
                evaluator.Evaluate(Query(PhysicalContainerKind.Equipment, "asset:shelf-1", Load(("bread", 40))), context).Outcome);
            Assert.IsTrue(service.CanAccept("asset:shelf-1", 1f, 40f, 0f));
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit,
                evaluator.Evaluate(Query(PhysicalContainerKind.Equipment, "asset:shelf-1", Load(("bread", 41))), context).Outcome);
            Assert.IsTrue(!service.CanAccept("asset:shelf-1", 1f, 41f, 0f));
        }

        [Test]
        public void Equipment_MassBoundedContainerRefusesUnknownMassItemsNamingTheItem()
        {
            EnsureKnownSpecs();
            EquipmentCapacityService service = MakeEquipmentService(out _);
            var massOnly = new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Container, 1,
                new CapacityEnvelope { MaxMassKg = 10f }, 0f, string.Empty);
            service.RegisterCapacity("asset:bin-mass-1", massOnly, null);
            var evaluator = MakeEvaluator(equipment: service);
            var context = new PhysicalCapacityEvaluationContext { EquipmentDescriptor = massOnly };

            // Case I: Unknown mass is not silently zero — the query refuses.
            PhysicalCapacityVerdict unknown = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:bin-mass-1", Load(("bread", 2))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, unknown.Outcome, unknown.Reason);
            StringAssert.Contains("bread", unknown.Reason);

            // Known load under the envelope agrees with the authority.
            PhysicalCapacityVerdict fits = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:bin-mass-1", Load((WidgetId, 3))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, fits.Outcome, fits.Reason);
            Assert.IsTrue(service.CanAccept("asset:bin-mass-1", 1f, 6f, 0f));

            // Known load over the envelope (12 kg > 10 kg) refuses, matching the authority.
            PhysicalCapacityVerdict over = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:bin-mass-1", Load((WidgetId, 6))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, over.Outcome, over.Reason);
            Assert.IsTrue(!service.CanAccept("asset:bin-mass-1", 1f, 12f, 0f));
        }

        [Test]
        public void Equipment_TransportLegReachesCanAcceptTransportOnMass()
        {
            EnsureKnownSpecs();
            EquipmentCapacityService service = MakeEquipmentService(out _);
            var transport = new EquipmentCapacityDescriptor(
                EquipmentCapacityModel.Transport, 1,
                new CapacityEnvelope { MaxMassKg = 100f }, 0f, string.Empty);
            service.RegisterCapacity("asset:cart-1", transport, null);
            var evaluator = MakeEvaluator(equipment: service);
            var context = new PhysicalCapacityEvaluationContext { EquipmentDescriptor = transport };

            // Boundary: 50 widgets = 100 kg fits exactly; 51 widgets = 102 kg refuses.
            PhysicalCapacityVerdict atLimit = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:cart-1", Load((WidgetId, 50))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, atLimit.Outcome, atLimit.Reason);
            Assert.IsTrue(service.CanAccept("asset:cart-1", 1f, 100f, 0f));
            PhysicalCapacityVerdict over = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:cart-1", Load((WidgetId, 51))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, over.Outcome, over.Reason);
            Assert.IsTrue(!service.CanAccept("asset:cart-1", 1f, 102f, 0f));

            // Unknown mass cannot be weighed against a mass envelope.
            PhysicalCapacityVerdict unknown = evaluator.Evaluate(
                Query(PhysicalContainerKind.Equipment, "asset:cart-1", Load(("bread", 1))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, unknown.Outcome, unknown.Reason);
            StringAssert.Contains("bread", unknown.Reason);
        }

        // ---------- wagon leg (acceptance H and J) ----------

        [Test]
        public void Wagon_Boundary450kgAnd4m3MatchesCanCarry()
        {
            EnsureKnownSpecs();
            EquipmentAsset wagon = MakeWagon("wagon-1", 450f, 4f);
            var evaluator = MakeEvaluator(wagonById: id => id == "wagon-1" ? wagon : null);
            var context = PhysicalCapacityEvaluationContext.Default;

            // Case H: a known limit rejects a physically impossible load.
            // Mass boundary: 225 widgets = 450 kg fits; 226 = 452 kg refuses.
            PhysicalCapacityVerdict atMass = evaluator.Evaluate(
                Query(PhysicalContainerKind.Wagon, "wagon-1", Load((WidgetId, 225))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, atMass.Outcome, atMass.Reason);
            Assert.IsTrue(WagonAuthority.CanCarry(wagon, 450f, 2.25f), "Case J: verdict agrees with the wagon record.");
            PhysicalCapacityVerdict overMass = evaluator.Evaluate(
                Query(PhysicalContainerKind.Wagon, "wagon-1", Load((WidgetId, 226))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, overMass.Outcome, overMass.Reason);
            Assert.IsTrue(!WagonAuthority.CanCarry(wagon, 452f, 2.26f));

            // Volume boundary: 40 bales = 4 m3 fits; 41 = 4.1 m3 refuses.
            PhysicalCapacityVerdict atVolume = evaluator.Evaluate(
                Query(PhysicalContainerKind.Wagon, "wagon-1", Load((BaleId, 40))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, atVolume.Outcome, atVolume.Reason);
            Assert.IsTrue(WagonAuthority.CanCarry(wagon, 40f, 4f));
            PhysicalCapacityVerdict overVolume = evaluator.Evaluate(
                Query(PhysicalContainerKind.Wagon, "wagon-1", Load((BaleId, 41))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, overVolume.Outcome, overVolume.Reason);
            Assert.IsTrue(!WagonAuthority.CanCarry(wagon, 41f, 4.1f));

            // Case I: Unknown mass is not zero — a bread load refuses, naming the item.
            PhysicalCapacityVerdict unknown = evaluator.Evaluate(
                Query(PhysicalContainerKind.Wagon, "wagon-1", Load(("bread", 1))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, unknown.Outcome, unknown.Reason);
            StringAssert.Contains("bread", unknown.Reason);

            // Haul only: the fits reason says so (Technical XIV section 6.2).
            StringAssert.Contains("Haul capacity only", atMass.Reason);
        }

        // ---------- business storage leg (7.1A axes, case K) ----------

        [Test]
        public void BusinessStorage_CategoryTargetBoundaryAnd71AAxesMatchAuthority()
        {
            BusinessRuntimeState store = MakeStore("general-store-1", "staple_food", 100);
            var evaluator = MakeEvaluator(businessById: id => id == "general-store-1" ? store : null);
            var context = new PhysicalCapacityEvaluationContext { BusinessCategoryId = "staple_food" };

            PhysicalCapacityVerdict fits = evaluator.Evaluate(
                Query(PhysicalContainerKind.BusinessStorage, "general-store-1", Load(("bread", 10))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, fits.Outcome, fits.Reason);
            Assert.IsTrue(store.CanReceiveShipment("staple_food", 10, out _));
            Assert.AreEqual(100, fits.StorageAxes.TotalUnits, "7.1A Total from BusinessCapacityState.");
            Assert.AreEqual(0, fits.StorageAxes.OccupiedUnits, "7.1A Occupied from BusinessCapacityState.");
            Assert.IsTrue(!fits.StorageAxes.AllocatedUnits.HasValue,
                "7.1A Allocated is Unknown: no business-level allocated figure exists.");

            // Fill the category to its target: the authority refuses, and so does the evaluator.
            Assert.AreEqual(100, store.AddCategoryStockUnits("staple_food", 100));
            PhysicalCapacityVerdict full = evaluator.Evaluate(
                Query(PhysicalContainerKind.BusinessStorage, "general-store-1", Load(("bread", 1))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, full.Outcome, full.Reason);
            Assert.IsTrue(!store.CanReceiveShipment("staple_food", 1, out string blocked));
            StringAssert.Contains("category full", full.Reason);
            Assert.AreEqual(blocked, full.Reason, "The evaluator returns the authority's own refusal text.");
            Assert.AreEqual(100, full.StorageAxes.OccupiedUnits);
        }

        [Test]
        public void BusinessStorage_CategoryPoolAndIdentifiedLotsAreNeverSummed()
        {
            // Case K: one 24-loaf identified lot lands in exactly one view each.
            BusinessRuntimeState store = MakeStore("general-store-1", "staple_food", 1000);
            Assert.IsNull(store.TryAddRetailItemLot(
                "lot-1", "bread", "staple_food", "loaf", 24,
                "general-store-1", "general-store-1", "test provenance", 0));
            var evaluator = MakeEvaluator(businessById: id => id == "general-store-1" ? store : null);
            var context = new PhysicalCapacityEvaluationContext { BusinessCategoryId = "staple_food" };

            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                Query(PhysicalContainerKind.BusinessStorage, "general-store-1", Load(("bread", 1))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, verdict.Reason);

            // The two views, reported separately.
            Assert.AreEqual(24, store.GetCategoryStock("staple_food").CurrentStockUnits, "Category pool view.");
            Assert.AreEqual(24, store.GetRetailItemStockUnits("bread"), "Identified-lot view.");
            Assert.AreEqual(24, verdict.StorageAxes.OccupiedUnits, "Verdict Occupied is the pool view.");
            Assert.AreEqual(24, verdict.StorageAxes.IdentifiedLotUnits, "Verdict identified view is the lot view.");
            // Summing both views would give 48 — the double count. No verdict
            // figure is 48, and the verdict exposes no combined stock figure.
            Assert.AreEqual(1000, verdict.StorageAxes.TotalUnits);
            Assert.IsTrue(verdict.StorageAxes.TotalUnits != 48
                && verdict.StorageAxes.OccupiedUnits != 48
                && verdict.StorageAxes.IdentifiedLotUnits != 48);
        }

        // ---------- storage zone leg ----------

        [Test]
        public void StorageZone_PerAxisBehaviourMatchesTryPlaceWithoutPlacing()
        {
            EnsureKnownSpecs();
            var context = PhysicalCapacityEvaluationContext.Default;

            // Mass-bounded zone refuses an Unknown-mass load, naming the item.
            PhysicalStorageZone massZone = MakeZone(StorageCapacityLimit.Known(10f), StorageCapacityLimit.Unknown);
            var massEvaluator = MakeEvaluator(zoneById: id => id == "zone-1" ? massZone : null);
            PhysicalCapacityVerdict unknown = massEvaluator.Evaluate(
                Query(PhysicalContainerKind.StorageZone, "zone-1", Load(("bread", 2))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, unknown.Outcome, unknown.Reason);
            StringAssert.Contains("bread", unknown.Reason);
            Assert.AreEqual(0, massZone.Placements.Count, "The evaluator places nothing.");

            // Unbounded zone answers the same load on no axis at all.
            PhysicalStorageZone openZone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown);
            var openEvaluator = MakeEvaluator(zoneById: id => id == "zone-1" ? openZone : null);
            PhysicalCapacityVerdict open = openEvaluator.Evaluate(
                Query(PhysicalContainerKind.StorageZone, "zone-1", Load(("bread", 2))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, open.Outcome, open.Reason);
            Assert.AreEqual(0, openZone.Placements.Count, "The evaluator places nothing.");

            // Placement boundary: 5 widgets (10 kg) fit a 10 kg zone, 6 (12 kg) refuse —
            // and the evaluator agrees with real TryPlace on an identical clone zone.
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Known(10f), StorageCapacityLimit.Known(2f));
            var evaluator = MakeEvaluator(zoneById: id => id == "zone-1" ? zone : null);
            PhysicalCapacityVerdict atLimit = evaluator.Evaluate(
                Query(PhysicalContainerKind.StorageZone, "zone-1", Load((WidgetId, 5))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, atLimit.Outcome, atLimit.Reason);
            PhysicalStorageZone clone = CloneZoneWithSamePlacements(zone);
            Assert.IsTrue(clone.TryPlace(
                new StoredLotReference("inventory", "lot-x", StorageLoadMeasurement.MassAndVolume(10f, 0.05f)), out _),
                "Clone TryPlace agrees: 10 kg fits the 10 kg zone.");
            PhysicalCapacityVerdict over = evaluator.Evaluate(
                Query(PhysicalContainerKind.StorageZone, "zone-1", Load((WidgetId, 6))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, over.Outcome, over.Reason);
            Assert.IsTrue(!clone.TryPlace(
                new StoredLotReference("inventory", "lot-y", StorageLoadMeasurement.MassAndVolume(12f, 0.06f)), out _),
                "Clone TryPlace agrees: 12 kg refuses the 10 kg zone.");
            Assert.AreEqual(0, zone.Placements.Count, "The evaluator places nothing.");
        }

        [Test]
        public void StorageZone_ExistingPlacementWithUnknownMassRefusesLikeTryPlace()
        {
            // Legacy/restored state: a placement whose load has no volume
            // measurement, in a zone that bounds volume. Built by reflection
            // (the public API rightly refuses to create it).
            PhysicalStorageZone volumeZone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Known(3f));
            InjectPlacement(volumeZone,
                new StoredLotReference("inventory", "lot-1", StorageLoadMeasurement.MassOnly(2f)));

            var evaluator = MakeEvaluator(zoneById: id => id == "zone-1" ? volumeZone : null);
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                Query(PhysicalContainerKind.StorageZone, "zone-1", Load(("bread", 1))),
                PhysicalCapacityEvaluationContext.Default);
            // The zone bounds volume; the existing placement cannot be measured on it.
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome, verdict.Reason);
            StringAssert.Contains("existing placed lot has unknown volume", verdict.Reason);

            // The same state refuses a real TryPlace on a clone built the same
            // way, confirming fidelity.
            PhysicalStorageZone clone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Known(3f));
            InjectPlacement(clone,
                new StoredLotReference("inventory", "lot-1", StorageLoadMeasurement.MassOnly(2f)));
            Assert.IsTrue(!clone.TryPlace(
                new StoredLotReference("inventory", "lot-2", StorageLoadMeasurement.VolumeOnly(1f)), out _));
            Assert.AreEqual(1, volumeZone.Placements.Count, "The evaluator places nothing.");
        }

        private static void InjectPlacement(PhysicalStorageZone zone, StoredLotReference lot)
        {
            FieldInfo placementsField = typeof(PhysicalStorageZone).GetField(
                "placements", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(placementsField, "The serialized placement field is part of this restore scenario.");
            var placements = (List<StoredLotReference>)placementsField.GetValue(zone);
            placements.Add(lot);
        }

        // ---------- person leg ----------

        [Test]
        public void PersonQuery_ReturnsUnknownNamingMissingInputsAndDerivesAgeFromBirthDate()
        {
            var person = new PersonState
            {
                id = 7,
                firstName = "Test",
                lastName = "Person",
                // Deliberately wrong cached age: the factory must not read it.
                age = 999,
                birthDate = new BirthDate(1800, 1, 15),
            };
            person.health.laborCapacityPercent = 80;
            person.nutrition.nutrition01 = 0.9f;

            PersonCarryInputs inputs = PersonCarryInputs.FromPersonState(person, 1830, 6, 1, new[] { "harness" });
            Assert.AreEqual(7, inputs.PersonId);
            Assert.AreEqual(30, inputs.AgeYears, "Age is completed years from the birth date, not the cached field.");
            Assert.AreEqual(80, inputs.LaborCapacityPercent);
            Assert.AreEqual(0.9f, inputs.Nutrition01, 0.001f);
            Assert.IsTrue(!inputs.BodySize.IsKnown, "No body-size field exists in source.");
            Assert.IsTrue(!inputs.Strength.IsKnown, "No strength field exists in source.");
            Assert.IsTrue(!inputs.Fatigue.IsKnown, "No fatigue authority exists in source.");
            Assert.AreEqual(1, inputs.EquipmentHeld.Count);
            Assert.AreEqual("harness", inputs.EquipmentHeld[0]);
            CollectionAssert.AreEquivalent(
                new[] { "body size", "strength", "fatigue" }, inputs.MissingInputNames);

            var evaluator = MakeEvaluator();
            var context = new PhysicalCapacityEvaluationContext { CarryInputs = inputs };
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                Query(PhysicalContainerKind.OnFootPerson, "7", Load(("bread", 14))), context);
            Assert.AreEqual(PhysicalCapacityOutcome.Unknown, verdict.Outcome, verdict.Reason);
            StringAssert.Contains("body size", verdict.Reason);
            StringAssert.Contains("strength", verdict.Reason);
            StringAssert.Contains("fatigue", verdict.Reason);
        }

        [Test]
        public void PersonCarryInputs_HasNoDemographicFieldAndResolvesHeldEquipmentViaGrants()
        {
            // Compile-level pin: no member whose name contains the forbidden
            // demographic substrings exists on the inputs type.
            foreach (MemberInfo member in typeof(PersonCarryInputs).GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                string lower = member.Name.ToLowerInvariant();
                Assert.IsTrue(!lower.Contains("female") && !lower.Contains("sex") && !lower.Contains("gender"),
                    "PersonCarryInputs must not carry a demographic member: " + member.Name);
            }

            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-1",
                EquipmentKind = "plow",
                HolderKind = "person",
                HolderId = "7",
                Custody = EquipmentCustodyKind.Borrowed,
                GranterName = "Neighbour",
                ExpiryDayIndex = -1,
            });
            IReadOnlyList<string> held = PersonCarryInputs.ResolveEquipmentHeld(
                resolver, "person", "7",
                new[] { "plow", "harness", "anvil" },
                (holderKind, holderId) => new List<string> { "harness" },
                dayIndex: 10);
            CollectionAssert.AreEquivalent(new[] { "plow", "harness" }, held,
                "Owned kinds plus unexpired grants; a kind with no grant is not listed.");
        }

        // ---------- horse pack ----------

        [Test]
        public void HorsePack_ReturnsNoAuthority()
        {
            var evaluator = MakeEvaluator();
            PhysicalCapacityVerdict verdict = evaluator.Evaluate(
                Query(PhysicalContainerKind.HorsePack, "horse-1", Load(("bread", 1))),
                PhysicalCapacityEvaluationContext.Default);
            Assert.AreEqual(PhysicalCapacityOutcome.NoAuthority, verdict.Outcome, verdict.Reason);
            StringAssert.Contains("no authority", verdict.Reason);
        }

        // ---------- evaluator discipline ----------

        [Test]
        public void Evaluator_StoresNoCapacityOfItsOwn()
        {
            // Every instance field is an authority reference (a service or a
            // lookup delegate). No capacity value, limit, or envelope is stored.
            FieldInfo[] fields = typeof(PhysicalCapacityEvaluator).GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.AreEqual(4, fields.Length, "Evaluator holds exactly the four authority references.");
            foreach (FieldInfo field in fields)
            {
                Assert.IsTrue(field.FieldType.IsInterface || typeof(Delegate).IsAssignableFrom(field.FieldType),
                    "Evaluator field '" + field.Name + "' must be an authority reference, not a capacity value.");
            }
        }

        [Test]
        public void Evaluate_DispatchesToTheSameLegVerdictOnEveryKind()
        {
            EnsureKnownSpecs();
            EquipmentCapacityService service = MakeEquipmentService(out _);
            EquipmentCapacityDescriptor shelf = EquipmentConditionProfiles.GetDescriptor("shelf");
            service.RegisterCapacity("asset:shelf-1", shelf, EquipmentConditionProfiles.GetProfile("shelf"));
            BusinessRuntimeState store = MakeStore("general-store-1", "staple_food", 100);
            PhysicalStorageZone zone = MakeZone(StorageCapacityLimit.Unknown, StorageCapacityLimit.Unknown);
            EquipmentAsset wagon = MakeWagon("wagon-1", 450f, 4f);
            var evaluator = MakeEvaluator(
                service,
                id => id == "general-store-1" ? store : null,
                id => id == "zone-1" ? zone : null,
                id => id == "wagon-1" ? wagon : null);
            PhysicalLoadResolution resolved = Load((WidgetId, 2)).Resolve();

            var equipmentContext = new PhysicalCapacityEvaluationContext { EquipmentDescriptor = shelf };
            PhysicalCapacityQuery equipmentQuery = Query(PhysicalContainerKind.Equipment, "asset:shelf-1", Load((WidgetId, 2)));
            Assert.AreEqual(
                evaluator.EvaluateEquipment(equipmentQuery, equipmentContext, resolved).Outcome,
                evaluator.Evaluate(equipmentQuery, equipmentContext).Outcome);

            var businessContext = new PhysicalCapacityEvaluationContext { BusinessCategoryId = "staple_food" };
            PhysicalCapacityQuery businessQuery = Query(PhysicalContainerKind.BusinessStorage, "general-store-1", Load((WidgetId, 2)));
            Assert.AreEqual(
                evaluator.EvaluateBusinessStorage(businessQuery, businessContext, resolved).Outcome,
                evaluator.Evaluate(businessQuery, businessContext).Outcome);

            PhysicalCapacityQuery zoneQuery = Query(PhysicalContainerKind.StorageZone, "zone-1", Load((WidgetId, 2)));
            Assert.AreEqual(
                evaluator.EvaluateStorageZone(zoneQuery, PhysicalCapacityEvaluationContext.Default, resolved).Outcome,
                evaluator.Evaluate(zoneQuery, PhysicalCapacityEvaluationContext.Default).Outcome);

            PhysicalCapacityQuery wagonQuery = Query(PhysicalContainerKind.Wagon, "wagon-1", Load((WidgetId, 2)));
            Assert.AreEqual(
                evaluator.EvaluateWagon(wagonQuery, PhysicalCapacityEvaluationContext.Default, resolved).Outcome,
                evaluator.Evaluate(wagonQuery, PhysicalCapacityEvaluationContext.Default).Outcome);
        }
    }
}
