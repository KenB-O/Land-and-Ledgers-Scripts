using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Slice S2 (2026-11-05, plan 02 §4): scenario-owned bakery opening
    /// equipment, end to end — kit definition, config schema, applier,
    /// hub register, Pass 9 save seam, and the two S1 call-site fixes
    /// (read here through the register-backed finder and kit check the
    /// driver itself uses). Pins that the equipment is authorable per
    /// business key, zero by default, idempotent, validated before any
    /// mutation, and survives a save round-trip without duplication or
    /// resurrection. Unity compilation, Test Runner, PlayMode, and scene
    /// wiring are NOT claimed; SimulationDrivers and the hub are
    /// harness-excluded, so the driver call sites are source-reviewed
    /// only and exercised here through the same public APIs they call.
    /// </summary>
    [TestFixture]
    public sealed class BakeryOpeningEquipmentTests
    {
        private const string BakeryKey = "bakery";
        private const string BakeryInstanceId = "test-bakery-1";

        private static readonly string[] OvenComponentIds =
        {
            "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
        };

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BusinessOpeningEquipment[] openingEquipment)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            SetPrivateField(config, "configurationId", "test-town");
            SetPrivateField(config, "seedOffset", 0);
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-0", firstName = "Test", lastName = "Baker", age = 30, homeBuildingOrdinal = 0 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-0", surname = "Baker", displayName = "Baker Household",
                    memberPersonKeys = new[] { "person-0" }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = BakeryKey, operationId = "bakery", ownerPersonKey = "person-0",
                    businessId = BakeryInstanceId, premisesRequired = false,
                },
            });
            SetPrivateField(config, "freightBusinessKey", BakeryKey);
            SetPrivateField(config, "openingHorseId", "A0");
            SetPrivateField(config, "openingWagonId", "test-wagon");
            SetPrivateField(config, "openingEquipment", openingEquipment ?? Array.Empty<DevelopmentTownConfiguration.BusinessOpeningEquipment>());
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessOpeningEquipment BakeryEquipmentEntry(
            string[] kitIds, params string[][] ovenComponentIds) =>
            new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = BakeryKey,
                kitIds = kitIds ?? Array.Empty<string>(),
                ovens = BuildOvens(ovenComponentIds),
            };

        private static DevelopmentTownConfiguration.OvenEquipment[] BuildOvens(string[][] ovenComponentIds)
        {
            var ovens = new DevelopmentTownConfiguration.OvenEquipment[ovenComponentIds.Length];
            for (int i = 0; i < ovenComponentIds.Length; i++)
                ovens[i] = new DevelopmentTownConfiguration.OvenEquipment { componentAssetIds = ovenComponentIds[i] };
            return ovens;
        }

        private static (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register)
            NewBakeryWithRegister()
        {
            var bakery = new BakeryShopRuntime(BakeryInstanceId);
            var state = new BusinessRuntimeState();
            var register = new BusinessEquipmentRegister { BusinessInstanceId = BakeryInstanceId };
            return (bakery, state, register);
        }

        private static bool Apply(
            DevelopmentTownConfiguration config, BakeryShopRuntime bakery, BusinessRuntimeState state,
            BusinessEquipmentRegister register, List<string> diag, out string report, out string error) =>
            ScenarioOpeningEquipmentEndowment.TryApply(
                config, BakeryKey, bakery, state, register, 0, diag, out report, out error);

        /// <summary>Register-backed component finder — the same shape the S2 driver call site uses.</summary>
        private static Func<string, WorkstationComponentView?> RegisterFinder(BusinessEquipmentRegister register) =>
            assetId =>
            {
                if (register?.Assets == null) return null;
                foreach (EquipmentAsset asset in register.Assets)
                    if (asset != null && string.Equals(asset.AssetId, assetId, StringComparison.Ordinal))
                        return new WorkstationComponentView
                        {
                            AssetId = asset.AssetId,
                            Kind = asset.Kind,
                            Condition01 = asset.Condition01,
                            IsUsable = asset.IsUsable,
                        };
                return null;
            };

        /// <summary>Kit gate exactly as the driver runs it: ForHolder over the register + kit code check.</summary>
        private static string KitRefusal(BakeryShopRuntime bakery, BusinessEquipmentRegister register, List<string> diag)
        {
            var availability = EquipmentRequirements.ForHolder(
                "business", bakery.BusinessInstanceId, 0, register?.Assets, register?.Kits, null);
            var stub = new TaskDefinition("bakery-daily-bake", "daily bakery bake", 1);
            stub.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(BakeryBreadCatalog.BakerHandKitId));
            return EquipmentRequirements.CheckTaskRequirements(stub, availability, diag);
        }

        private static void SeedFlourNotionsAndFuel(BakeryShopRuntime bakery, EntityIdRegistry ids, List<string> diag)
        {
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.FlourItemId, 100,
                "test-flour-import", "Test flour import", 0, ids, diag), "flour seeding should succeed");
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.NotionsItemId, 10,
                "test-notions-import", "Test notions import", 0, ids, diag), "notions seeding should succeed");
            Assert.IsNull(BakeryFuelSupply.ReceiveImportArrival(
                bakery.FuelStock, 10, "test-fuel-import", "Test fuel import", 0, ids, diag),
                "fuel seeding should succeed");
        }

        [Test]
        public void BakerHandKitDefinition_ExistsInCatalog()
        {
            ToolKitDefinition def = ToolKitCatalog.Get(BakeryBreadCatalog.BakerHandKitId);
            Assert.IsNotNull(def, "baker-hand-kit must resolve in the catalog");
            Assert.AreEqual("baker-hand-kit", def.KitId);
            Assert.AreEqual("Baker's Hand Tool Kit", def.DisplayName);
            Assert.IsTrue(def.CanonicalContents.Contains("peels"), "kit contents come from BakeryBreadCatalog.cs:96 (peels, troughs, scales, knives)");
            Assert.IsTrue(def.CanonicalContents.Contains("troughs"));
            Assert.IsTrue(def.CanonicalContents.Contains("scales"));
            Assert.IsTrue(def.CanonicalContents.Contains("knives"));
            Assert.AreEqual(4, def.CanonicalContents.Count, "no contents invented beyond the documented four");
            Assert.IsNotNull(ToolKitCatalog.Get("carpenter-hand-tool-kit"), "existing kits are untouched by the addition");
        }

        [Test]
        public void OpeningEquipment_AppliesOvenAndKit_FromConfig()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(new[] { BakeryBreadCatalog.BakerHandKitId }, OvenComponentIds),
            });
            Assert.IsTrue(config.Validate(out string configError), configError);
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();

            Assert.IsTrue(Apply(config, bakery, state, register, diag, out string report, out string error), error);
            StringAssert.Contains("oven 0", report, "report names the installed oven; report: " + report);

            Assert.AreEqual(1, bakery.Ovens.Count, "exactly one oven is installed");
            Assert.AreEqual(4, register.Assets.Count, "one component asset per BakeOven kind");
            Assert.AreEqual("oven-chamber", register.Assets[0].Kind, "component kinds come from the BakeOven definition, positionally");
            Assert.AreEqual("kneading-table", register.Assets[1].Kind);
            Assert.AreEqual("proofing-rack", register.Assets[2].Kind);
            Assert.AreEqual("bake-peels", register.Assets[3].Kind);
            Assert.AreEqual(1, bakery.CountReadyOvens(WorkstationCatalog.BakeOven, RegisterFinder(register), diag),
                "the oven is ready against the register-backed finder; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(1, register.Kits.Count, "exactly one kit is installed");
            Assert.AreEqual(BakeryBreadCatalog.BakerHandKitId, register.Kits[0].KitId);
            Assert.AreEqual(BakeryInstanceId, register.Kits[0].OwnerId, "the kit is owned by the bakery business");
            Assert.IsNull(KitRefusal(bakery, register, diag),
                "the kit gate passes for the bakery holder; diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void OpeningEquipment_Twice_IsIdempotent()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(new[] { BakeryBreadCatalog.BakerHandKitId }, OvenComponentIds),
            });
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();

            Assert.IsTrue(Apply(config, bakery, state, register, diag, out _, out string firstError), firstError);
            Assert.IsTrue(Apply(config, bakery, state, register, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the second apply is refused as already applied; report: " + secondReport);

            Assert.AreEqual(1, bakery.Ovens.Count, "no second oven on re-apply");
            Assert.AreEqual(4, register.Assets.Count, "no duplicate component assets on re-apply");
            Assert.AreEqual(1, register.Kits.Count, "no duplicate kit on re-apply");
        }

        [Test]
        public void OpeningEquipment_EmptyConfig_InstallsNothing()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(null);
            Assert.IsTrue(config.Validate(out string configError), configError);
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();

            Assert.IsTrue(Apply(config, bakery, state, register, diag, out string report, out string error), error);
            StringAssert.Contains("No opening equipment is configured", report, "report: " + report);

            Assert.AreEqual(0, bakery.Ovens.Count, "the live default installs no oven");
            Assert.AreEqual(0, register.Assets.Count, "the live default registers no assets");
            Assert.AreEqual(0, register.Kits.Count, "the live default registers no kit");
            Assert.IsTrue(!string.IsNullOrEmpty(KitRefusal(bakery, register, diag)),
                "with no kit the gate still refuses — nothing was defaulted true");
        }

        [Test]
        public void OpeningEquipment_UnknownKitId_RefusesWithoutInstalling()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(new[] { "no-such-kit" }, OvenComponentIds),
            });
            Assert.IsTrue(!config.Validate(out _), "an unknown kit id is an invalid config");
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();

            Assert.IsTrue(!Apply(config, bakery, state, register, diag, out _, out string error),
                "an unknown kit id refuses the apply");
            StringAssert.Contains("no-such-kit", error, "refusal names the unknown kit; error: " + error);

            Assert.AreEqual(0, bakery.Ovens.Count, "validate-before-mutate: the valid oven entry installs nothing either");
            Assert.AreEqual(0, register.Assets.Count, "no component assets are registered on refusal");
            Assert.AreEqual(0, register.Kits.Count, "no kit is registered on refusal");
        }

        [Test]
        public void OpeningEquipment_WrongComponentCount_Refuses()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(
                    new[] { BakeryBreadCatalog.BakerHandKitId },
                    new[] { "oven-chamber-1", "kneading-table-1", "proofing-rack-1" }),
            });
            Assert.IsTrue(!config.Validate(out _), "an oven with 3 components (BakeOven needs 4) is an invalid config");
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();

            Assert.IsTrue(!Apply(config, bakery, state, register, diag, out _, out string error),
                "a wrong component count refuses the apply");
            StringAssert.Contains("BakeOven definition (4)", error, "refusal names the expected count; error: " + error);

            Assert.AreEqual(0, bakery.Ovens.Count, "no oven is installed on refusal");
            Assert.AreEqual(0, register.Assets.Count, "no component assets are registered on refusal");
            Assert.AreEqual(0, register.Kits.Count, "no kit is registered on refusal");
        }

        [Test]
        public void OpeningEquipment_SaveRoundTrip_PreservesKitAndOven()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(new[] { BakeryBreadCatalog.BakerHandKitId }, OvenComponentIds),
            });
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();
            Assert.IsTrue(Apply(config, bakery, state, register, diag, out _, out string applyError), applyError);

            var pass9 = new Series2Pass9SaveState { BakeryRuntime = bakery };
            pass9.EquipmentRegisters.Add(register);
            Series2Pass9SaveDto dto = pass9.Capture(diag);
            Assert.AreEqual(1, dto.EquipmentRegisters.Count, "the register is captured into the Pass 9 DTO");

            // Fresh live state, as after a load: the bakery runtime is
            // restored from its own DTO and the registers from the seam.
            var freshBakery = new BakeryShopRuntime(BakeryInstanceId);
            var restored = new Series2Pass9SaveState { BakeryRuntime = freshBakery };
            restored.Restore(dto, null, diag);
            // Restoring the same save twice must not duplicate or resurrect.
            restored.Restore(dto, null, diag);

            Assert.AreEqual(1, restored.EquipmentRegisters.Count, "restore is idempotent: one register, not two");
            BusinessEquipmentRegister restoredRegister = restored.EquipmentRegisters[0];
            Assert.AreEqual(BakeryInstanceId, restoredRegister.BusinessInstanceId);
            Assert.AreEqual(4, restoredRegister.Assets.Count, "all four component assets survive the round-trip");
            Assert.AreEqual("oven-chamber", restoredRegister.Assets[0].Kind);
            Assert.AreEqual(1, restoredRegister.Kits.Count, "the kit survives the round-trip");
            Assert.AreEqual(BakeryBreadCatalog.BakerHandKitId, restoredRegister.Kits[0].KitId);
            Assert.AreEqual($"{BakeryInstanceId}-{BakeryBreadCatalog.BakerHandKitId}-0", restoredRegister.Kits[0].InstanceId);
            Assert.AreEqual(1, freshBakery.Ovens.Count, "the oven survives through the bakery runtime DTO");
            Assert.AreEqual(1, freshBakery.CountReadyOvens(WorkstationCatalog.BakeOven, RegisterFinder(restoredRegister), diag),
                "the restored oven is still ready against the restored register; diag: " + string.Join(" | ", diag));
            Assert.IsNull(KitRefusal(freshBakery, restoredRegister, diag),
                "the kit gate still passes after restore; diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void Precheck_WithOpeningEquipment_PassesKitAndOvenGates()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                BakeryEquipmentEntry(new[] { BakeryBreadCatalog.BakerHandKitId }, OvenComponentIds),
            });
            (BakeryShopRuntime bakery, BusinessRuntimeState state, BusinessEquipmentRegister register) = NewBakeryWithRegister();
            Assert.IsTrue(Apply(config, bakery, state, register, diag, out _, out string applyError), applyError);

            // Test fixtures only: flour/notions/fuel placed in stock so the
            // run reaches the kit and oven gates. No production stock is
            // invented, and no bread is baked by this test's setup alone.
            var ids = new EntityIdRegistry();
            SeedFlourNotionsAndFuel(bakery, ids, diag);

            List<string> planned = bakery.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 2, diag);
            Assert.IsNotNull(planned);
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            driver.AdoptPlannedBatches(2, planned, diag);

            Assert.IsNull(KitRefusal(bakery, register, diag), "kit gate passes before Precheck");
            bool kitUsable = KitRefusal(bakery, register, diag) == null;
            string refusal = driver.Precheck(
                2, kitUsable, WorkstationCatalog.BakeOven, RegisterFinder(register), null, diag);
            Assert.IsNull(refusal, "Precheck passes the kit and oven gates on scenario-owned equipment; diag: "
                + string.Join(" | ", diag));
            StringAssert.Contains("pre-check passed", string.Join(" | ", diag));
            Assert.AreEqual(0, bakery.BreadShelf.Count, "Precheck alone bakes nothing");
        }
    }
}
