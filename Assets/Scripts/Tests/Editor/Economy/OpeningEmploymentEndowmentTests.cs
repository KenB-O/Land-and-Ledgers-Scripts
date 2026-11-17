using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-1 (expanded campaign, Phase 1 forestry): the sawmill opening
    /// employment endowment (F1) and the opening camp-provision cover (F2).
    /// A scenario-authored mill hand is a real EmploymentRelationship in the
    /// hub-owned registry — the mill pays the agreed wage through the normal
    /// payroll read, and the camp roster reads the hire through the same
    /// GetActiveByEmployer the production bootstrapper uses. The cover-days
    /// endowment (7 days x 2 heads x 1 ration) is pinned behaviourally: a
    /// fed two-person crew fells for exactly seven days, then refuses
    /// unfed. Pins authorability per business key, zero by default,
    /// idempotence, validate-before-mutate refusals, and the two-person
    /// team forming. Unity compilation, Test Runner, PlayMode, and scene
    /// wiring are NOT claimed; the bootstrapper call site is
    /// harness-excluded, so it is source-reviewed only and exercised here
    /// through the same public APIs it calls.
    /// </summary>
    [TestFixture]
    public sealed class OpeningEmploymentEndowmentTests
    {
        private const string MillKey = "sawmill";
        private const string MillInstanceId = "test-sawmill-1";
        private const string OwnerKey = "mill-owner";
        private const string HandKey = "mill-hand";
        private const int MillHandWageCents = 750;

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BusinessEmployeeDefinition[] openingEmployees,
            DevelopmentTownConfiguration.BusinessOpeningEquipment[] openingEquipment = null)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            SetPrivateField(config, "configurationId", "test-town");
            SetPrivateField(config, "seedOffset", 0);
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = OwnerKey, firstName = "Test", lastName = "Miller", age = 30, homeBuildingOrdinal = 0 },
                new DevelopmentTownConfiguration.PersonDefinition
                { key = HandKey, firstName = "Test", lastName = "Hand", age = 24, homeBuildingOrdinal = 1 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-owner", surname = "Miller", displayName = "Miller Household",
                    memberPersonKeys = new[] { OwnerKey }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-hand", surname = "Hand", displayName = "Hand Household",
                    memberPersonKeys = new[] { HandKey }, homeBuildingOrdinal = 1, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = MillKey, operationId = "sawmill", ownerPersonKey = OwnerKey,
                    businessId = MillInstanceId, premisesRequired = false,
                },
            });
            SetPrivateField(config, "freightBusinessKey", MillKey);
            SetPrivateField(config, "openingHorseId", "A0");
            SetPrivateField(config, "openingWagonId", "test-wagon");
            SetPrivateField(config, "openingEmployees", openingEmployees ?? Array.Empty<DevelopmentTownConfiguration.BusinessEmployeeDefinition>());
            SetPrivateField(config, "openingEquipment", openingEquipment ?? Array.Empty<DevelopmentTownConfiguration.BusinessOpeningEquipment>());
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessEmployeeDefinition MillHandEntry(
            string personKey = HandKey, string role = "logger", int wageCents = MillHandWageCents) =>
            new DevelopmentTownConfiguration.BusinessEmployeeDefinition
            {
                businessKey = MillKey, personKey = personKey, role = role, weeklyWageCents = wageCents,
            };

        private static DevelopmentTownConfiguration.BusinessOpeningEquipment MillAxesEntry() =>
            new DevelopmentTownConfiguration.BusinessOpeningEquipment
            {
                businessKey = MillKey,
                assetEntries = new[]
                {
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "felling-axe-1", kind = "felling-axe" },
                    new DevelopmentTownConfiguration.OpeningAssetEntry { assetId = "felling-axe-2", kind = "felling-axe" },
                },
            };

        private static bool ApplyEmployment(
            DevelopmentTownConfiguration config, BusinessRuntimeState state,
            EmploymentRelationshipRegistry registry, List<string> diag, out string report, out string error) =>
            ScenarioOpeningEmploymentEndowment.TryApply(
                config, MillKey, MillInstanceId, state, registry, 0, diag, out report, out error);

        [Test]
        public void OpeningEmployment_AppliesMillLogger_FromConfig()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { MillHandEntry() });
            Assert.IsTrue(config.Validate(out string configError), configError);
            Assert.AreEqual(1, config.ResolveRuntimePersonId(OwnerKey));
            Assert.AreEqual(2, config.ResolveRuntimePersonId(HandKey));
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsTrue(ApplyEmployment(config, state, registry, diag, out string report, out string error), error);
            StringAssert.Contains(HandKey, report, "report names the hire; report: " + report);
            StringAssert.Contains("logger", report, "report: " + report);

            Assert.AreSame(registry, state.EmploymentRegistry,
                "the runtime state is pointed at the hub registry the hire was registered into");
            List<EmploymentRelationship> active = registry.GetActiveByEmployer(MillInstanceId);
            Assert.AreEqual(1, active.Count, "exactly one opening employment is registered");
            EmploymentRelationship hire = active[0];
            Assert.AreEqual(2, hire.EmployeePersonId, "the hand resolves to runtime person 2");
            Assert.AreEqual(MillInstanceId, hire.EmployerBusinessId);
            Assert.AreEqual("logger", hire.RoleDisplayName);
            Assert.AreEqual(MillHandWageCents, hire.Compensation.AgreedWeeklyWageCents);
            Assert.IsTrue(hire.IsActive);
            Assert.AreEqual(EmploymentKind.Permanent, hire.Kind);
            Assert.AreEqual(EmploymentSource.Authored, hire.Source);
            Assert.AreEqual(0, hire.StartDayIndex);
            Assert.AreEqual(-1, hire.EndDayIndex, "open-ended");
            Assert.AreEqual(MillHandWageCents, registry.GetActiveWeeklyPayrollCents(MillInstanceId),
                "the mill owes the agreed wage through the normal payroll read");
        }

        [Test]
        public void OpeningEmployment_Twice_IsIdempotent()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { MillHandEntry() });
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsTrue(ApplyEmployment(config, state, registry, diag, out _, out string firstError), firstError);
            Assert.IsTrue(ApplyEmployment(config, state, registry, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the second apply is a no-op; report: " + secondReport);

            Assert.AreEqual(1, registry.GetActiveByEmployer(MillInstanceId).Count,
                "no duplicate employment on re-apply");
        }

        [Test]
        public void OpeningEmployment_EmptyConfig_RegistersNothing()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(null);
            Assert.IsTrue(config.Validate(out string configError), configError);
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsTrue(ApplyEmployment(config, state, registry, diag, out string report, out string error), error);
            StringAssert.Contains("No opening employment is configured", report, "report: " + report);
            Assert.AreEqual(0, registry.GetActiveByEmployer(MillInstanceId).Count,
                "the live default hires nobody");
        }

        [Test]
        public void OpeningEmployment_UnknownPerson_RefusesWithoutRegistering()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { MillHandEntry(personKey: "no-such-person") });
            Assert.IsFalse(config.Validate(out _), "an unknown person is an invalid config");
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsFalse(ApplyEmployment(config, state, registry, diag, out _, out string error),
                "an unknown person refuses the apply");
            StringAssert.Contains("no-such-person", error, "refusal names the person; error: " + error);
            Assert.AreEqual(0, registry.GetActiveByEmployer(MillInstanceId).Count,
                "validate-before-mutate: nothing is registered on refusal");
        }

        [Test]
        public void OpeningEmployment_NonPositiveWage_Refuses()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { MillHandEntry(wageCents: 0) });
            Assert.IsFalse(config.Validate(out _), "a zero wage is an invalid config");
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsFalse(ApplyEmployment(config, state, registry, diag, out _, out string error),
                "a zero wage refuses the apply, as in the hiring path");
            Assert.AreEqual(0, registry.GetActiveByEmployer(MillInstanceId).Count);
        }

        [Test]
        public void OpeningEmployment_OwnerAsEmployee_Refuses()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { MillHandEntry(personKey: OwnerKey) });
            Assert.IsFalse(config.Validate(out _), "the owner as their own employee is an invalid config");
            var state = new BusinessRuntimeState();
            var registry = new EmploymentRelationshipRegistry();

            Assert.IsFalse(ApplyEmployment(config, state, registry, diag, out _, out string error),
                "the owner is rostered separately, never as an employee");
            StringAssert.Contains(OwnerKey, error, "refusal names the owner; error: " + error);
            Assert.AreEqual(0, registry.GetActiveByEmployer(MillInstanceId).Count);
        }

        [Test]
        public void OpeningEmployment_TwoPersonCrewFellsSevenCoverDays_WithInstalledAxes()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(
                new[] { MillHandEntry() }, new[] { MillAxesEntry() });
            Assert.IsTrue(config.Validate(out string configError), configError);

            // The two C-1 endowments, as the bootstrapper applies them.
            var state = new BusinessRuntimeState();
            var register = new BusinessEquipmentRegister { BusinessInstanceId = MillInstanceId };
            Assert.IsTrue(ScenarioMillOpeningEquipmentEndowment.TryApply(
                config, MillKey, MillInstanceId, state, register, 0, diag, out _, out string axeError), axeError);
            var registry = new EmploymentRelationshipRegistry();
            Assert.IsTrue(ApplyEmployment(config, state, registry, diag, out _, out string employmentError), employmentError);

            // The roster, exactly as ConfigureFellingCampAndRights builds it:
            // the owner first, then the first active employee.
            var ids = new EntityIdRegistry();
            var standRegistry = new LoggingStandRegistry();
            Assert.IsNull(standRegistry.RegisterStand(new TimberStand
            {
                StandId = "stand-1",
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                Species = "white pine",
                KilogramsPerLog = 600f,
                StandingTimberUnits = 100,
            }));
            Assert.IsNull(standRegistry.GrantTimberRights("stand-1", MillInstanceId, 0, "town-land-office"));
            var operation = new LoggingOperationRuntime(MillInstanceId, ids);
            var camp = new LoggingCampSite("camp-stand-1", MillInstanceId, "stand-1", 300);
            int ownerPersonId = config.ResolveRuntimePersonId(OwnerKey);
            Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, ownerPersonId),
                LoggingCampRoles.Logger, 300, diag));
            foreach (EmploymentRelationship relationship in registry.GetActiveByEmployer(MillInstanceId))
            {
                Assert.IsNull(camp.AssignWorker(EntityId.For(EntityKind.Person, relationship.EmployeePersonId),
                    LoggingCampRoles.Logger, 300, diag));
                break;
            }
            Assert.AreEqual(2, camp.CrewHeadcount, "owner plus the hired hand");
            Assert.AreEqual(2, camp.LoggerCount, "two loggers make one felling team");

            // The F2 cover: 7 days x 2 heads x 1 ration, endowed once from
            // the bootstrap endowment supplier id.
            Assert.IsNull(camp.SupplyLedger.RecordDelivery(
                LoggingCampSupplyLedger.BootstrapEndowmentSupplierId, 14f, 300,
                "7 cover days for the logging camp crew.", diag));
            Assert.AreEqual(14f, camp.SupplyLedger.Balance);

            var gate = new EquipmentTaskGate { AssetsSupplier = () => register.Assets };
            for (int day = 0; day < 7; day++)
            {
                LogLot lot = camp.RunCampDay(standRegistry, operation, MillInstanceId, 5,
                    EntityId.For(EntityKind.Person, ownerPersonId), 300 + day, 6, diag, gate);
                Assert.IsNotNull(lot, $"day {day + 1} of the cover fells; diag: " + string.Join(" | ", diag));
                Assert.AreEqual(5, lot.LogUnits);
                Assert.AreEqual(14f - 2f * (day + 1), camp.SupplyLedger.Balance,
                    "two heads consume one ration each per day");
            }
            Assert.AreEqual(35, operation.LandingLogUnits, "seven days x five logs");
            Assert.AreEqual(65, standRegistry.GetStand("stand-1").StandingTimberUnits);
            Assert.AreEqual(7, camp.WorkDays.Count);
            Assert.AreEqual(0f, camp.SupplyLedger.Balance, "the cover is spent exactly at seven days");

            LogLot unfed = camp.RunCampDay(standRegistry, operation, MillInstanceId, 5,
                EntityId.For(EntityKind.Person, ownerPersonId), 307, 6, diag, gate);
            Assert.IsNull(unfed, "day 8 refuses: the crew goes unfed, never auto-supplied");
            Assert.AreEqual(65, standRegistry.GetStand("stand-1").StandingTimberUnits,
                "a refused day fells nothing");
            Assert.AreEqual(35, operation.LandingLogUnits);
            Assert.AreEqual(7, camp.WorkDays.Count, "a refused day is not recorded as a work day");
        }
    }
}
