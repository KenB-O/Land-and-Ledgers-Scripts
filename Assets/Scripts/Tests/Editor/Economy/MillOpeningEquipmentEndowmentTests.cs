using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-1 (expanded campaign, Phase 1 forestry): the sawmill opening
    /// equipment endowment (F3) — two scenario-authored felling axes, one
    /// per feller, installed once from the config into the mill's equipment
    /// register, satisfying the fell-timber gate (felling-axe OR
    /// crosscut-saw) on real assets. Pins authorability per business key,
    /// zero by default, idempotence, validate-before-mutate refusals, and
    /// the gate behaviour the axes exist to satisfy. Unity compilation,
    /// Test Runner, PlayMode, and scene wiring are NOT claimed; the
    /// bootstrapper call site is harness-excluded, so it is source-reviewed
    /// only and exercised here through the same public API it calls.
    /// </summary>
    [TestFixture]
    public sealed class MillOpeningEquipmentEndowmentTests
    {
        private const string MillKey = "sawmill";
        private const string MillInstanceId = "test-sawmill-1";

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
                { key = "person-0", firstName = "Test", lastName = "Miller", age = 30, homeBuildingOrdinal = 0 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-0", surname = "Miller", displayName = "Miller Household",
                    memberPersonKeys = new[] { "person-0" }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = MillKey, operationId = "sawmill", ownerPersonKey = "person-0",
                    businessId = MillInstanceId, premisesRequired = false,
                },
            });
            SetPrivateField(config, "freightBusinessKey", MillKey);
            SetPrivateField(config, "openingHorseId", "A0");
            SetPrivateField(config, "openingWagonId", "test-wagon");
            SetPrivateField(config, "openingEquipment", openingEquipment ?? Array.Empty<DevelopmentTownConfiguration.BusinessOpeningEquipment>());
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessOpeningEquipment MillEquipmentEntry(
            params (string assetId, string kind)[] assets)
        {
            var entries = new DevelopmentTownConfiguration.OpeningAssetEntry[assets.Length];
            for (int i = 0; i < assets.Length; i++)
                entries[i] = new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = assets[i].assetId, kind = assets[i].kind };
            return new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = MillKey,
                assetEntries = entries,
            };
        }

        private static (BusinessRuntimeState state, BusinessEquipmentRegister register) NewMillWithRegister()
        {
            var state = new BusinessRuntimeState();
            var register = new BusinessEquipmentRegister { BusinessInstanceId = MillInstanceId };
            return (state, register);
        }

        private static bool Apply(
            DevelopmentTownConfiguration config, BusinessRuntimeState state,
            BusinessEquipmentRegister register, List<string> diag, out string report, out string error) =>
            ScenarioMillOpeningEquipmentEndowment.TryApply(
                config, MillKey, MillInstanceId, state, register, 0, diag, out report, out error);

        private static LoggingStandRegistry RegistryWithStand(string standId, int units)
        {
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(new TimberStand
            {
                StandId = standId,
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                Species = "white pine",
                KilogramsPerLog = 600f,
                StandingTimberUnits = units,
            }));
            Assert.IsNull(registry.GrantTimberRights(standId, MillInstanceId, 0, "town-land-office"));
            return registry;
        }

        private static EquipmentTaskGate RegisterBackedGate(BusinessEquipmentRegister register) =>
            new EquipmentTaskGate { AssetsSupplier = () => register.Assets };

        [Test]
        public void MillOpeningEquipment_AppliesTwoFellingAxes_FromConfig()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-2", "felling-axe")),
            });
            Assert.IsTrue(config.Validate(out string configError), configError);
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();

            Assert.IsTrue(Apply(config, state, register, diag, out string report, out string error), error);
            StringAssert.Contains("felling-axe-1", report, "report names the installed axes; report: " + report);
            StringAssert.Contains("felling-axe-2", report, "report: " + report);

            Assert.AreEqual(2, register.Assets.Count, "one asset per authored entry");
            Assert.AreEqual("felling-axe-1", register.Assets[0].AssetId);
            Assert.AreEqual("felling-axe", register.Assets[0].Kind);
            Assert.AreEqual("felling-axe-2", register.Assets[1].AssetId);
            Assert.AreEqual("felling-axe", register.Assets[1].Kind);
            Assert.AreEqual(1f, register.Assets[0].Condition01, "opening assets install at full condition");
            Assert.AreEqual("business", register.Assets[0].OwnerKind, "the axes are business-owned");
            Assert.AreEqual(MillInstanceId, register.Assets[0].OwnerId);
            Assert.AreEqual(MillInstanceId, register.Assets[0].LocationId, "the axes are located at the mill");
        }

        [Test]
        public void MillOpeningEquipment_Twice_IsIdempotent()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-2", "felling-axe")),
            });
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();

            Assert.IsTrue(Apply(config, state, register, diag, out _, out string firstError), firstError);
            Assert.IsTrue(Apply(config, state, register, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the second apply is a no-op; report: " + secondReport);

            Assert.AreEqual(2, register.Assets.Count, "no duplicate axes on re-apply");
        }

        [Test]
        public void MillOpeningEquipment_Reapply_HealsMissingComponents_WithoutRepairingPresentOnes()
        {
            var diag = new List<string>();
            // First apply: the C-1/C-2 entry (axes + wagon) — the pre-C-3 save.
            DevelopmentTownConfiguration firstConfig = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-2", "felling-axe"), ("log-wagon-1", "log-wagon")),
            });
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();
            Assert.IsTrue(Apply(firstConfig, state, register, diag, out _, out string firstError), firstError);
            Assert.AreEqual(3, register.Assets.Count, "the pre-C-3 save holds axes + wagon");

            // The axes have been used since: one is worn to half condition.
            register.Assets[0].Condition01 = 0.5f;

            // Second apply: the C-3 entry (axes + wagon + saw-line
            // components), same endowment id — the first post-C-3 boot of
            // the pre-C-3 save.
            DevelopmentTownConfiguration secondConfig = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-2", "felling-axe"), ("log-wagon-1", "log-wagon"),
                    ("saw-line-1", "saw-line"), ("log-carriage-1", "log-carriage"), ("drive-system-1", "drive-system")),
            });
            Assert.IsTrue(Apply(secondConfig, state, register, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the heal reports the recorded endowment; report: " + secondReport);
            StringAssert.Contains("saw-line-1", secondReport, "the heal names the newly installed components; report: " + secondReport);

            Assert.AreEqual(6, register.Assets.Count, "the three saw-line components install; nothing duplicates");
            Assert.AreEqual(0.5f, register.Assets[0].Condition01, "a present asset is never re-installed, so a used axe is never silently repaired");
            Assert.AreEqual(1f, register.Assets[3].Condition01, "the healed components install at full condition");
            Assert.AreEqual("saw-line", register.Assets[3].Kind);
            Assert.AreEqual("log-carriage", register.Assets[4].Kind);
            Assert.AreEqual("drive-system", register.Assets[5].Kind);
        }

        [Test]
        public void MillOpeningEquipment_EmptyConfig_InstallsNothing_AndFellingGateRefuses()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(null);
            Assert.IsTrue(config.Validate(out string configError), configError);
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();

            Assert.IsTrue(Apply(config, state, register, diag, out string report, out string error), error);
            StringAssert.Contains("No opening equipment is configured", report, "report: " + report);
            Assert.AreEqual(0, register.Assets.Count, "the live default installs no axe");

            // The F3 refusal, through the real felling path: no axe in the
            // register means the fell-timber gate refuses and nothing is cut.
            var ids = new EntityIdRegistry();
            LoggingStandRegistry registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime(MillInstanceId, ids);
            LogLot lot = operation.RunFelling(registry, "stand-1", MillInstanceId, 5,
                EntityId.For(EntityKind.Person, 1), 300, diag, RegisterBackedGate(register));
            Assert.IsNull(lot, "with no axe the gate refuses felling");
            Assert.IsTrue(diag.Exists(d => d.Contains("FELLING REFUSED")),
                "the refusal is loud; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(100, registry.GetStand("stand-1").StandingTimberUnits,
                "a refused felling cuts nothing");
            Assert.AreEqual(0, operation.LandingLogUnits);
        }

        [Test]
        public void MillOpeningEquipment_FixtureId_RefusesWithoutInstalling()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(("slice-felling-axe", "felling-axe")),
            });
            Assert.IsFalse(config.Validate(out _), "the slice fixture id is an invalid config");
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();

            Assert.IsFalse(Apply(config, state, register, diag, out _, out string error),
                "the slice fixture id refuses the apply");
            StringAssert.Contains("slice-felling-axe", error, "refusal names the id; error: " + error);
            Assert.AreEqual(0, register.Assets.Count, "a refused apply installs nothing");
        }

        [Test]
        public void MillOpeningEquipment_DuplicateAssetId_RefusesWithoutInstalling()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-1", "felling-axe")),
            });
            Assert.IsFalse(config.Validate(out _), "a duplicate asset id is an invalid config");
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();

            Assert.IsFalse(Apply(config, state, register, diag, out _, out string error),
                "a duplicate asset id refuses the apply");
            StringAssert.Contains("felling-axe-1", error, "refusal names the id; error: " + error);
            Assert.AreEqual(0, register.Assets.Count, "validate-before-mutate: nothing is installed on refusal");
        }

        [Test]
        public void MillOpeningEquipment_InstalledAxes_SatisfyFellingGate()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[]
            {
                MillEquipmentEntry(("felling-axe-1", "felling-axe"), ("felling-axe-2", "felling-axe")),
            });
            (BusinessRuntimeState state, BusinessEquipmentRegister register) = NewMillWithRegister();
            Assert.IsTrue(Apply(config, state, register, diag, out _, out string applyError), applyError);

            var ids = new EntityIdRegistry();
            LoggingStandRegistry registry = RegistryWithStand("stand-1", 100);
            var operation = new LoggingOperationRuntime(MillInstanceId, ids);
            LogLot lot = operation.RunFelling(registry, "stand-1", MillInstanceId, 5,
                EntityId.For(EntityKind.Person, 1), 300, diag, RegisterBackedGate(register));

            Assert.IsNotNull(lot, "the installed axes satisfy the fell-timber gate; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(5, lot.LogUnits);
            Assert.AreEqual(MillInstanceId, lot.OwnerId, "the rights holder owns the felled logs");
            Assert.AreEqual("stand-1", lot.StandId, "stand provenance survives the gate");
            Assert.AreEqual(95, registry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(5, operation.LandingLogUnits);
        }
    }
}
