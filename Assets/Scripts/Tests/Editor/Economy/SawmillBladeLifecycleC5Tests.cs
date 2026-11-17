using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-5 (expanded campaign, Phase 1 forestry): the blade lifecycle —
    /// the 17-log dull threshold through the production-shaped gate, the
    /// broken-blade re-set path, the retired-blade spare path, the opening
    /// spare-blade endowment, and the lubrication store's consumption +
    /// persistence. Fixtures copy the C-3 harness-safe RunSawing pattern
    /// (SawmillWorkstationEstablishmentTests) and the C-1 config-building
    /// pattern (OpeningEmploymentEndowmentTests) — never the compacted
    /// summary's description of them. What C-3 already proves (dull ->
    /// filing -> sawing resumes) is NOT duplicated here. What the D2B tests
    /// prove at runtime level (SawmillDepthTests, harness-excluded) is
    /// re-pinned here only where the production-shaped gate is part of the
    /// behaviour. Plan adjudication, recorded: the plan said "broken blade
    /// refuses filing and requires a spare" — the code's D2B design gives
    /// broken blades the re-set path (unlimited), so a broken blade does
    /// NOT require a spare; the spare is required for a RETIRED blade (or a
    /// missing blade id). This file proves the retired -> spare -> resume
    /// path and does NOT invent a re-set limit. Unity compilation, Test
    /// Runner, PlayMode, and scene wiring are NOT claimed; the hub and
    /// bootstrapper call sites are harness-excluded, so they are
    /// source-reviewed only and exercised here through the same public APIs
    /// they call.
    /// </summary>
    [TestFixture]
    public sealed class SawmillBladeLifecycleC5Tests
    {
        private const string MillInstanceId = "test-sawmill-1";
        private const string MillKey = "sawmill";
        private const string SpareBladeId = "sawmill-spare-blade-1";
        private const string OwnerKey = "mill-owner";

        // ---- C-3 fixture pattern (copied from SawmillWorkstationEstablishmentTests) ----

        private static EquipmentAsset ComponentAsset(string assetId, string kind) =>
            new EquipmentAsset
            {
                AssetId = assetId,
                Kind = kind,
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = MillInstanceId,
                LocationId = MillInstanceId,
            };

        private static BusinessEquipmentRegister MillRegisterWithComponents()
        {
            var register = new BusinessEquipmentRegister { BusinessInstanceId = MillInstanceId };
            register.RegisterAsset(ComponentAsset("saw-line-1", "saw-line"));
            register.RegisterAsset(ComponentAsset("log-carriage-1", "log-carriage"));
            register.RegisterAsset(ComponentAsset("drive-system-1", "drive-system"));
            return register;
        }

        private static WorkstationComponentView? FindInRegister(BusinessEquipmentRegister register, string assetId)
        {
            if (register?.Assets == null) return null;
            foreach (var asset in register.Assets)
                if (asset != null && asset.AssetId == assetId)
                    return new WorkstationComponentView
                    {
                        AssetId = asset.AssetId,
                        Kind = asset.Kind,
                        Condition01 = asset.Condition01,
                        IsUsable = asset.IsUsable,
                    };
            return null;
        }

        private static EquipmentTaskGate ProductionShapedGate(
            BusinessWorkstations stations, BusinessEquipmentRegister register, SupportContext support) =>
            new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => string.Equals(id, stations.BusinessInstanceId, System.StringComparison.Ordinal)
                    ? stations
                    : new BusinessWorkstations(id),
                FindWorkstationDefinition = WorkstationCatalog.Find,
                FindComponent = assetId => FindInRegister(register, assetId),
                SupportSupplier = () => support,
            };

        private static (BusinessWorkstations stations, SupportContext support) Establish(
            BusinessEquipmentRegister register)
        {
            var diag = new List<string>();
            var stations = new BusinessWorkstations(MillInstanceId);
            Assert.IsTrue(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, MotivePowerKind.Steam, diag, out _, out string error), error);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            return (stations, support);
        }

        private static (BusinessWorkstations stations, SupportContext support, EquipmentTaskGate gate) EstablishedGate()
        {
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, SupportContext support) = Establish(register);
            return (stations, support, ProductionShapedGate(stations, register, support));
        }

        private static SawmillShopRuntime MillWithLogs(int logs)
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime(MillInstanceId, ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9101),
                LogUnits = logs,
                MassKg = logs * 300f,
                OwnerId = MillInstanceId,
                StandId = "stand-1",
                Species = "white pine",
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = 200,
            }, diag));
            return mill;
        }

        private static SawmillSawingResult RunSawing(
            SawmillShopRuntime mill, int logs, EntityId worker, int day, List<string> diag,
            EquipmentTaskGate gate, string species = "white pine")
        {
            int minutes = logs * SawmillConversionData.DefaultForSpecies(species).SawMinutesPerLog;
            var tasks = new TaskAuthority();
            Sawmill.RegisterTaskDefinitions(tasks);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(day);
            var task = tasks.CreateTaskWithPlannedMinutes(Sawmill.SawLogsTaskId,
                EntityId.For(EntityKind.Business, 1), day, minutes);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, worker, budgets, day, out var assignReason), assignReason);
            Assert.IsTrue(tasks.StartTask(task.TaskId, day, out var startReason), startReason);
            Assert.IsTrue(tasks.RecordWorkProgress(task.TaskId, minutes, budgets, day,
                out bool reached, out var workReason), workReason);
            Assert.IsTrue(reached, "The simulated saw-line labor duration must elapse before conversion.");
            return mill.RunSawing(logs, worker, day, diag, tasks, task.TaskId, budgets, gate);
        }

        // ---- C-1 config pattern (copied from OpeningEmploymentEndowmentTests) ----

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig(
            DevelopmentTownConfiguration.BusinessSpareBladeDefinition[] openingSpareBlades)
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            // Schema 2 (foundation pass): configurationId, seedOffset and
            // freightBusinessKey live on the nested GeneralSettings; the
            // horse, wagon, employees, spare blades and equipment live
            // on the mill's opening record. The flat top-level fields no
            // longer exist.
            config.Settings.configurationId = "test-town";
            config.Settings.seedOffset = 0;
            config.Settings.freightBusinessKey = MillKey;
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = OwnerKey, firstName = "Test", lastName = "Miller", age = 30, homeBuildingOrdinal = 0 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-owner", surname = "Miller", displayName = "Miller Household",
                    memberPersonKeys = new[] { OwnerKey }, homeBuildingOrdinal = 0, openingCashCents = 0,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = MillKey, operationId = "sawmill", ownerPersonKey = OwnerKey,
                    businessId = MillInstanceId, premisesRequired = false,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        openingHorseId = "A0",
                        openingWagonId = "test-wagon",
                        employees = Array.Empty<DevelopmentTownConfiguration.BusinessEmployeeDefinition>(),
                        spareBlades = openingSpareBlades ?? Array.Empty<DevelopmentTownConfiguration.BusinessSpareBladeDefinition>(),
                        equipment = new DevelopmentTownConfiguration.BusinessOpeningEquipment(),
                    },
                },
            });
            return config;
        }

        private static DevelopmentTownConfiguration.BusinessSpareBladeDefinition SpareEntry(string bladeId = SpareBladeId) =>
            new DevelopmentTownConfiguration.BusinessSpareBladeDefinition { businessKey = MillKey, bladeId = bladeId };

        private static bool ApplySpareBlades(
            DevelopmentTownConfiguration config, BusinessRuntimeState state, SawmillShopRuntime mill,
            List<string> diag, out string report, out string error) =>
            ScenarioMillOpeningSpareBladeEndowment.TryApply(
                config, MillKey, MillInstanceId, state, mill, 0, diag, out report, out error);

        private static SawmillShopRuntime MillWithEndowedSpare(out BusinessRuntimeState state, List<string> diag)
        {
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry() });
            Assert.IsTrue(config.Validate(out string configError), configError);
            state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out _, out string error), error);
            return mill;
        }

        // ---- The 17-log dull threshold ----

        [Test]
        public void DullThreshold_SixteenWhitePineLogs_NotDull_Seventeenth_Dulls()
        {
            var diag = new List<string>();
            (_, _, EquipmentTaskGate gate) = EstablishedGate();
            SawmillShopRuntime mill = MillWithLogs(20);

            // The threshold, not the count, is the pin: white pine wears
            // 0.04/log and dulls at 0.35 — 16 logs leave 0.36 (not dull),
            // the 17th leaves 0.32 (dull).
            Assert.AreEqual(0.35f, SawmillBladeState.DullThreshold01);
            Assert.AreEqual(0.04f, SawmillConversionData.DefaultForSpecies("white pine").BladeWearPerLog01);

            SawmillSawingResult first = RunSawing(mill, 16, EntityId.For(EntityKind.Person, 5002), 210, diag, gate);
            Assert.NotNull(first, "16 logs saw cleanly; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(0.36f, mill.Blade.Condition01, 0.001f, "1.0 - 16 x 0.04");
            Assert.IsFalse(mill.Blade.IsDull, "0.36 is above the 0.35 dull threshold");

            SawmillSawingResult second = RunSawing(mill, 1, EntityId.For(EntityKind.Person, 5002), 211, diag, gate);
            Assert.NotNull(second, "the 17th log still saws — the blade dulls DURING it, not before; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(0.32f, mill.Blade.Condition01, 0.001f, "1.0 - 17 x 0.04");
            Assert.IsTrue(mill.Blade.IsDull, "0.32 is at/below the 0.35 dull threshold");

            SawmillSawingResult refused = RunSawing(mill, 1, EntityId.For(EntityKind.Person, 5002), 212, diag, gate);
            Assert.IsNull(refused, "a dull saw gates ALL further output");
            Assert.AreEqual(3, mill.LogStock.TotalLogUnits, "the gated run consumes nothing");
            Assert.IsTrue(string.Join(" ", diag).Contains("DULL"), "the refusal names the dull blade; diag: " + string.Join(" | ", diag));
        }

        // ---- Broken blade: filing refuses, re-set restores (through the gate) ----

        [Test]
        public void BrokenBlade_RefusesFiling_ReSetRestores_ThroughProductionGate()
        {
            var diag = new List<string>();
            (_, _, EquipmentTaskGate gate) = EstablishedGate();
            SawmillShopRuntime mill = MillWithLogs(10);
            mill.Blade.ApplyWear(0.95f); // condition 0.05 — broken, not merely dull
            Assert.IsTrue(mill.Blade.IsBroken);
            Assert.IsTrue(mill.Blade.IsDull);

            SawmillSawingResult refused = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 210, diag, gate);
            Assert.IsNull(refused, "a broken saw gates ALL output");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "a gated run consumes nothing");
            Assert.IsTrue(string.Join(" ", diag).Contains("BROKEN"), "the refusal names the broken blade; diag: " + string.Join(" | ", diag));

            // Filing cannot fix a broken saw — the D2B design's re-set path
            // (unlimited) is the repair; a broken blade does NOT require a
            // spare (plan adjudication, see the file header).
            string filingRefusal = mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), 211, diag);
            Assert.NotNull(filingRefusal, "filing a broken blade refuses");
            Assert.AreEqual(0.05f, mill.Blade.Condition01, 0.001f, "the refused filing wears nothing");

            string resetRefusal = mill.RecordSawReset(EntityId.For(EntityKind.Person, 5003), 211, diag);
            Assert.IsNull(resetRefusal, "re-setting restores the broken blade; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(1f, mill.Blade.Condition01);
            Assert.AreEqual(1, mill.Blade.ResetCount, "the re-set is factual history");
            Assert.IsFalse(mill.Blade.IsBroken);

            SawmillSawingResult result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 211, diag, gate);
            Assert.NotNull(result, "output resumes after the re-set; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(2, result.LogsSawn);
            Assert.AreEqual(8, mill.LogStock.TotalLogUnits);
        }

        // ---- Retired blade: no spare stays gated; endowed spare resumes ----

        [Test]
        public void RetiredBlade_NoSpare_RefusesNamingSpare_StaysGated()
        {
            var diag = new List<string>();
            (_, _, EquipmentTaskGate gate) = EstablishedGate();
            SawmillShopRuntime mill = MillWithLogs(10);

            Assert.IsNull(mill.RetireFittedBlade("cracked plate", 210, diag), "retiring the fitted blade succeeds");
            Assert.IsTrue(mill.Blade.IsRetired);

            SawmillSawingResult refused = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 211, diag, gate);
            Assert.IsNull(refused, "a retired blade gates ALL output");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "a gated run consumes nothing");
            Assert.IsTrue(string.Join(" ", diag).Contains("retired scrap"), "the refusal names the retired blade; diag: " + string.Join(" | ", diag));
            Assert.IsTrue(string.Join(" ", diag).Contains("fit a spare blade"), "the refusal names the remedy; diag: " + string.Join(" | ", diag));

            string fitRefusal = mill.FitSpareBlade(SpareBladeId, 211, diag);
            Assert.NotNull(fitRefusal, "no spare on hand — nothing is conjured");
            Assert.IsTrue(fitRefusal.Contains("no spare blade"), "refusal: " + fitRefusal);

            SawmillSawingResult stillRefused = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 212, diag, gate);
            Assert.IsNull(stillRefused, "sawing stays gated with no spare");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void RetiredBlade_WithEndowedSpare_FitSpare_ResumesSawing()
        {
            var diag = new List<string>();
            (_, _, EquipmentTaskGate gate) = EstablishedGate();
            SawmillShopRuntime mill = MillWithEndowedSpare(out _, diag);
            Assert.AreEqual(1, mill.BladeInventory.Spares.Count, "the endowed spare is on hand");
            // MillWithEndowedSpare builds a mill without logs; give it some.
            Assert.IsNull(mill.ReceiveLogLot(new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9102),
                LogUnits = 10,
                MassKg = 3000f,
                OwnerId = MillInstanceId,
                StandId = "stand-1",
                Species = "white pine",
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = 200,
            }, diag));

            Assert.IsNull(mill.RetireFittedBlade("cracked plate", 210, diag));
            Assert.IsNull(mill.FitSpareBlade(SpareBladeId, 211, diag), "the endowed spare fits; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(SpareBladeId, mill.Blade.BladeId, "the spare is now the fitted blade");
            Assert.AreEqual(1f, mill.Blade.Condition01, "the spare is fresh");
            Assert.AreEqual("BOOTSTRAP endowment", mill.Blade.ProcurementSource, "the fitted spare keeps its endowment provenance");
            Assert.IsFalse(mill.Blade.IsRetired);
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count, "the spare left the inventory");
            Assert.AreEqual(1, mill.BladeInventory.Scrap.Count, "the retired blade is scrap, not a spare");

            SawmillSawingResult result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 211, diag, gate);
            Assert.NotNull(result, "output resumes with the fitted spare; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(2, result.LogsSawn);
            Assert.AreEqual(8, mill.LogStock.TotalLogUnits);
        }

        // ---- The spare-blade endowment ----

        [Test]
        public void SpareBladeEndowment_InstallsOneSpareWithBootstrapProvenance()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry() });
            Assert.IsTrue(config.Validate(out string configError), configError);
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out string report, out string error), error);
            StringAssert.Contains(SpareBladeId, report, "report names the blade; report: " + report);

            Assert.AreEqual(1, mill.BladeInventory.Spares.Count, "exactly one spare stocked");
            SawmillBladeState spare = mill.BladeInventory.Spares[0];
            Assert.AreEqual(SpareBladeId, spare.BladeId);
            Assert.AreEqual("BOOTSTRAP endowment", spare.ProcurementSource, "the spare is an opening endowment, never an anonymous blade");
            Assert.IsTrue(spare.IsBootstrapEndowment);
            Assert.AreEqual(0, spare.ProcuredDayIndex);
            Assert.AreEqual("BOOTSTRAP endowment", mill.Blade.ProcurementSource, "the fitted blade is stamped too, when blank");
            Assert.IsTrue(mill.Blade.IsBootstrapEndowment);
        }

        [Test]
        public void SpareBladeEndowment_Twice_IsIdempotent()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry() });
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out _, out string firstError), firstError);
            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out string secondReport, out string secondError), secondError);
            StringAssert.Contains("already recorded", secondReport, "the second apply is a no-op; report: " + secondReport);

            Assert.AreEqual(1, mill.BladeInventory.Spares.Count, "no duplicate spare on re-apply");
        }

        [Test]
        public void SpareBladeEndowment_EmptyConfig_InstallsNothing_StillStampsFittedBlade()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(null);
            Assert.IsTrue(config.Validate(out string configError), configError);
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out string report, out string error), error);
            StringAssert.Contains("No opening spare blades are configured", report, "report: " + report);
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count, "no spare is conjured from an empty config");
            Assert.AreEqual("BOOTSTRAP endowment", mill.Blade.ProcurementSource, "the fitted blade is still stamped — it IS the opening blade");
        }

        [Test]
        public void SpareBladeEndowment_DuplicateBladeId_RefusesNothingReceived()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry(), SpareEntry() });
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsFalse(ApplySpareBlades(config, state, mill, diag, out _, out string error), "a duplicate blade id refuses");
            StringAssert.Contains("twice", error, "error: " + error);
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count, "validate-before-mutate: nothing was received");
            Assert.AreEqual(string.Empty, mill.Blade.ProcurementSource, "the fitted blade is not stamped on a refused apply");
        }

        [Test]
        public void SpareBladeEndowment_BlankBladeId_RefusesNothingReceived()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry(bladeId: "  ") });
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsFalse(ApplySpareBlades(config, state, mill, diag, out _, out string error), "a blank blade id refuses");
            StringAssert.Contains("blank blade id", error, "error: " + error);
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count);
        }

        [Test]
        public void SpareBladeEndowment_FittedBladeStampedOnlyWhenBlank()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry() });
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            // A fitted blade with a recorded source keeps it — the stamp
            // never overwrites provenance.
            Assert.IsNull(mill.RecordBladeProcurement("General Store test-supplier", 0, isBootstrapEndowment: false, diag));

            Assert.IsTrue(ApplySpareBlades(config, state, mill, diag, out _, out string error), error);
            Assert.AreEqual("General Store test-supplier", mill.Blade.ProcurementSource, "the recorded source survives");
            Assert.AreEqual(1, mill.BladeInventory.Spares.Count, "the spare is still received");
        }

        [Test]
        public void SpareBladeEndowment_UnconfiguredBusiness_Refuses()
        {
            var diag = new List<string>();
            DevelopmentTownConfiguration config = BuildConfig(new[] { SpareEntry() });
            var state = new BusinessRuntimeState();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());

            Assert.IsFalse(ScenarioMillOpeningSpareBladeEndowment.TryApply(
                config, "no-such-business", MillInstanceId, state, mill, 0, diag, out _, out string error),
                "an unconfigured business refuses");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(error), "the refusal is named");
            Assert.AreEqual(0, mill.BladeInventory.Spares.Count);
        }

        [Test]
        public void SpareBladeConfigValidation_DuplicateAndBlank_Refused()
        {
            DevelopmentTownConfiguration valid = BuildConfig(new[] { SpareEntry() });
            Assert.IsTrue(valid.Validate(out string validError), validError);
            // Schema 2 nests spare blades under their owning business, so
            // the blade surfaces attributed to the mill; there is no flat
            // entry that could name an unconfigured business any more.
            // K2: the OpeningSpareBlades derived view is deleted. The
            // blade is attributed to its owning business by direct
            // owner-record iteration instead of the view's stamp.
            var attributedBlades = new List<(string businessKey, DevelopmentTownConfiguration.BusinessSpareBladeDefinition spare)>();
            foreach (DevelopmentTownConfiguration.BusinessDefinition business in valid.Businesses)
            {
                if (business?.opening?.spareBlades == null) continue;
                foreach (DevelopmentTownConfiguration.BusinessSpareBladeDefinition spare in business.opening.spareBlades)
                    attributedBlades.Add((business.key, spare));
            }
            Assert.AreEqual(1, attributedBlades.Count);
            Assert.AreEqual(MillKey, attributedBlades[0].businessKey,
                "direct iteration attributes the blade to its owning business");

            DevelopmentTownConfiguration duplicate = BuildConfig(new[] { SpareEntry(), SpareEntry() });
            Assert.IsFalse(duplicate.Validate(out string duplicateError), "a duplicate blade id fails validation");
            Assert.IsTrue(duplicateError.Contains("spare blade"), "error: " + duplicateError);

            DevelopmentTownConfiguration blank = BuildConfig(new[] { SpareEntry(bladeId: "") });
            Assert.IsFalse(blank.Validate(out string blankError), "a blank blade id fails validation");
        }

        // ---- Lubrication consumption + persistence ----

        [Test]
        public void LubricationConsumption_DrawsOnePerCompletedSawingDay_FromBothStores()
        {
            var diag = new List<string>();
            (_, SupportContext support) = Establish(MillRegisterWithComponents());
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            Assert.AreEqual(30, mill.MillLubricationUnits, "a fresh mill opens at the C-3 store");
            Assert.AreEqual(30, support.ConsumableUnits[SawmillLubrication.LubricationKey]);

            Assert.IsNull(SawmillLubrication.TryConsumeDay(mill, support, 210, diag), "one sawing day consumes one unit");
            Assert.AreEqual(29, mill.MillLubricationUnits, "the runtime field draws down");
            Assert.AreEqual(29, support.ConsumableUnits[SawmillLubrication.LubricationKey], "the gate's key draws down with it");

            // The helper is called ONCE per completed sawing day by the
            // driver — a day with 3 runs still burns 1, because the call
            // site, not the helper, counts days. Two calls are two days.
            Assert.IsNull(SawmillLubrication.TryConsumeDay(mill, support, 211, diag));
            Assert.AreEqual(28, mill.MillLubricationUnits);
            Assert.AreEqual(28, support.ConsumableUnits[SawmillLubrication.LubricationKey]);
        }

        [Test]
        public void LubricationConsumption_EmptyStore_RefusesNeverBelowZero()
        {
            var diag = new List<string>();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            var support = new SupportContext();
            mill.MillLubricationUnits = 1;
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(mill, support, diag));

            Assert.IsNull(SawmillLubrication.TryConsumeDay(mill, support, 210, diag), "the last unit consumes");
            Assert.AreEqual(0, mill.MillLubricationUnits);
            Assert.AreEqual(0, support.ConsumableUnits[SawmillLubrication.LubricationKey]);

            string refusal = SawmillLubrication.TryConsumeDay(mill, support, 211, diag);
            Assert.NotNull(refusal, "an empty store refuses instead of going negative");
            Assert.AreEqual(0, mill.MillLubricationUnits, "nothing consumed");
            Assert.AreEqual(0, support.ConsumableUnits[SawmillLubrication.LubricationKey]);
        }

        [Test]
        public void LubricationConsumption_Disagreement_RefusesLoudly()
        {
            var diag = new List<string>();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            var support = new SupportContext();
            mill.MillLubricationUnits = 12;
            support.ConsumableUnits[SawmillLubrication.LubricationKey] = 30;

            string refusal = SawmillLubrication.TryConsumeDay(mill, support, 210, diag);
            Assert.NotNull(refusal, "two stores that disagree refuse instead of silently resyncing");
            Assert.IsTrue(refusal.Contains("disagree"), "refusal: " + refusal);
            Assert.AreEqual(12, mill.MillLubricationUnits, "nothing consumed");
            Assert.AreEqual(30, support.ConsumableUnits[SawmillLubrication.LubricationKey]);
        }

        [Test]
        public void LubricationSync_SetsSupportFromRuntime_EveryBoot()
        {
            var diag = new List<string>();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            var support = new SupportContext();

            // A drawn-down runtime (12) restores the key to 12 — a Continue
            // no longer resets the store to the opening 30.
            mill.MillLubricationUnits = 12;
            support.ConsumableUnits[SawmillLubrication.LubricationKey] = 30;
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(mill, support, diag));
            Assert.AreEqual(12, support.ConsumableUnits[SawmillLubrication.LubricationKey]);
            Assert.AreEqual(12, mill.MillLubricationUnits, "the runtime is the source of truth — it never moves");

            // A missing key is created from the runtime, not from the opening default.
            var freshSupport = new SupportContext();
            mill.MillLubricationUnits = 7;
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(mill, freshSupport, diag));
            Assert.AreEqual(7, freshSupport.ConsumableUnits[SawmillLubrication.LubricationKey]);
        }

        [Test]
        public void LubricationSaveRoundTrip_PreservesDrawnDownStore_AndPreC5SavesLoadAsOpening()
        {
            var diag = new List<string>();
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            var support = new SupportContext();
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(mill, support, diag));
            // Draw the store down to 12 through the real helper (18 days).
            for (int day = 210; day < 228; day++)
                Assert.IsNull(SawmillLubrication.TryConsumeDay(mill, support, day, diag));
            Assert.AreEqual(12, mill.MillLubricationUnits);
            mill.RecordLubricationPurchaseTransaction("mill-lubrication-purchase:test-sawmill-1:210:1");

            SawmillShopRuntime.SawmillShopRuntimeSaveDto dto = mill.CaptureSaveDto();
            Assert.AreEqual(12, dto.MillLubricationUnits, "the DTO carries the drawn-down store");
            Assert.AreEqual(1, dto.LubricationPurchaseTransactionIds.Count);

            var restored = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(12, restored.MillLubricationUnits, "a Continue restores 12, not the opening 30");
            Assert.AreEqual(1, restored.LubricationPurchaseTransactionIds.Count);
            Assert.IsTrue(restored.HasAppliedLubricationPurchase("mill-lubrication-purchase:test-sawmill-1:210:1"));
            var restoredSupport = new SupportContext();
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(restored, restoredSupport, diag));
            Assert.AreEqual(12, restoredSupport.ConsumableUnits[SawmillLubrication.LubricationKey], "the re-synced key is 12 too");

            // A pre-C-5 save has no such field: the DTO default (-1) loads
            // as the opening store, since nothing ever consumed it there.
            var preC5 = new SawmillShopRuntime.SawmillShopRuntimeSaveDto();
            Assert.AreEqual(-1, preC5.MillLubricationUnits, "the unset sentinel");
            var oldMill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            oldMill.LoadFromSaveDto(preC5);
            Assert.AreEqual(30, oldMill.MillLubricationUnits, "a pre-C-5 save opens at the opening store");
        }

        [Test]
        public void LubricationPurchaseTransactionIds_RecordedOnce()
        {
            var mill = new SawmillShopRuntime(MillInstanceId, new EntityIdRegistry());
            Assert.IsTrue(mill.RecordLubricationPurchaseTransaction("tx-1"), "first record succeeds");
            Assert.IsFalse(mill.RecordLubricationPurchaseTransaction("tx-1"), "the same id records nothing twice");
            Assert.IsTrue(mill.RecordLubricationPurchaseTransaction("tx-2"));
            Assert.AreEqual(2, mill.LubricationPurchaseTransactionIds.Count);
            Assert.IsFalse(mill.RecordLubricationPurchaseTransaction(""), "a blank id records nothing");
            Assert.IsFalse(mill.RecordLubricationPurchaseTransaction(null), "a null id records nothing");
        }
    }
}
