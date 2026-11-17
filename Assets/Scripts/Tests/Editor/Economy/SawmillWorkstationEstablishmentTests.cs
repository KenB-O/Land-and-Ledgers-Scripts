using EntityId = LandLedgers.Primitives.EntityId;

using System.Globalization;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-3 (expanded campaign, Phase 1 forestry): the saw-line workstation
    /// establishment (hub gate wiring). Before this slice the production hub
    /// gate never set WorkstationsFor / FindWorkstationDefinition /
    /// FindComponent / SupportSupplier — only the ThreeBusinessSlice fixture
    /// set them, on its own local gate — so every sawing attempt refused
    /// "no workstation registry supplied". These tests drive the gate the
    /// way the hub wires it (WorkstationRegisterFor semantics, the catalog
    /// lookup, component resolution from the mill register, the merged
    /// support context) through the same public API the bootstrapper calls
    /// (SawmillWorkstationEstablishment), and prove RunSawing saws through
    /// it and refuses before any deduction when the workstation is not
    /// established. The hub and bootstrapper call sites are
    /// harness-excluded, so they are source-reviewed only and Unity
    /// compilation proof is owed from Codex; the behaviour they wire is
    /// proven here. Unity compilation, Test Runner, PlayMode, and scene
    /// wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class SawmillWorkstationEstablishmentTests
    {
        private const string MillInstanceId = "test-sawmill-1";

        [Test]
        public void HubEquipmentGate_ResolvesWorkstationByHashedBusinessTaskOwner()
        {
            var gameObject = new GameObject("workstation-owner-resolution-test");
            try
            {
                SimulationSystemsHub hub = gameObject.AddComponent<SimulationSystemsHub>();
                hub.InstallEquipmentTaskGate();
                BusinessWorkstations registered = hub.WorkstationRegisterFor(MillInstanceId);
                string runtimeOwnerId = SawmillProductionDriver.BusinessEntityIdFor(MillInstanceId).Id
                    .ToString(CultureInfo.InvariantCulture);

                Assert.AreSame(registered,
                    hub.InstalledEquipmentTaskGate.WorkstationsFor("business", runtimeOwnerId),
                    "TaskAuthority passes the numeric business EntityId; the hub must resolve it to the same instance-keyed workstation registry.");

                string unknownOwnerId = SawmillProductionDriver.BusinessEntityIdFor("unregistered-mill").Id
                    .ToString(CultureInfo.InvariantCulture);
                Assert.IsNull(hub.InstalledEquipmentTaskGate.WorkstationsFor("business", unknownOwnerId),
                    "Unknown numeric business owners must fail closed instead of creating a misleading empty registry.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

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

        private static BusinessEquipmentRegister MillRegisterWithComponents(
            bool includeSawLine = true, bool includeCarriage = true, bool includeDriveSystem = true)
        {
            var register = new BusinessEquipmentRegister { BusinessInstanceId = MillInstanceId };
            if (includeSawLine) register.RegisterAsset(ComponentAsset("saw-line-1", "saw-line"));
            if (includeCarriage) register.RegisterAsset(ComponentAsset("log-carriage-1", "log-carriage"));
            if (includeDriveSystem) register.RegisterAsset(ComponentAsset("drive-system-1", "drive-system"));
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

        /// <summary>
        /// The gate the way the hub wires it: the mill's id returns its
        /// registry, any other id a fresh empty one (the hub's
        /// WorkstationRegisterFor creates on first use); definitions come
        /// from the catalog; components resolve from the mill register;
        /// support is the registered per-business context.
        /// </summary>
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

        private static string CheckSawLine(EquipmentTaskGate gate, List<string> diag) =>
            gate.CheckCodes(
                new List<string> { EquipmentRequirementCodes.Workstation("sawmill-saw-line") },
                "business", MillInstanceId, 0, diag);

        private static (BusinessWorkstations stations, SupportContext support) Establish(
            BusinessEquipmentRegister register, MotivePowerKind power = MotivePowerKind.Steam)
        {
            var diag = new List<string>();
            var stations = new BusinessWorkstations(MillInstanceId);
            Assert.IsTrue(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, power, diag, out string report, out string error), error);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            return (stations, support);
        }

        private static SupportContext SupportWith(bool includeLubrication, bool includeFiling, int lubricationUnits = 30)
        {
            var support = new SupportContext();
            if (includeLubrication)
                support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = lubricationUnits;
            if (includeFiling)
                support.AvailableRepairCapabilities.Add("saw-filing");
            return support;
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

        [Test]
        public void Catalog_Find_ReturnsSawmillSawLine()
        {
            WorkstationDefinition def = WorkstationCatalog.Find("sawmill-saw-line");
            Assert.NotNull(def, "the catalog carries the saw-line definition");
            Assert.AreEqual("sawmill-saw-line", def.WorkstationId);
            Assert.AreEqual(3, def.Components.Count);
            Assert.AreEqual("saw-line", def.Components[0].EquipmentKind);
            Assert.AreEqual("log-carriage", def.Components[1].EquipmentKind);
            Assert.AreEqual("drive-system", def.Components[2].EquipmentKind);
            Assert.IsTrue(def.AcceptableMotivePowerKinds.Contains("Steam"), "steam satisfies the drive (Canon §8.5A)");
            Assert.IsTrue(def.AcceptableMotivePowerKinds.Contains("Water"), "water stays available if Kennedy corrects OD-C6");
            Assert.IsTrue(def.SupportRequirements.Exists(r => r.Kind == "consumable" && r.Detail == "mill-lubrication" && r.UnitsPerDay == 1));
            Assert.IsTrue(def.SupportRequirements.Exists(r => r.Kind == "repair-capability" && r.Detail == "saw-filing"));

            Assert.IsNull(WorkstationCatalog.Find("no-such-workstation"), "unknown id finds nothing");
            Assert.IsNull(WorkstationCatalog.Find(null));
            Assert.IsNull(WorkstationCatalog.Find(""));
        }

        [Test]
        public void Establishment_GatePasses_WhenEstablished()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, SupportContext support) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);

            Assert.IsNull(CheckSawLine(gate, diag), "the established saw-line passes the production-shaped gate; diag: " + string.Join(" | ", diag));

            WorkstationInstance station = stations.Get("sawmill-saw-line");
            Assert.NotNull(station);
            Assert.AreEqual(MillInstanceId + "-saw-floor", station.SpaceId, "the saw floor inside the mill's premises");
            Assert.AreEqual(3, station.ComponentAssetIds.Count, "one register asset per required kind");
            Assert.IsTrue(station.ComponentAssetIds.Contains("saw-line-1"));
            Assert.IsTrue(station.ComponentAssetIds.Contains("log-carriage-1"));
            Assert.IsTrue(station.ComponentAssetIds.Contains("drive-system-1"));
            Assert.AreEqual(1, station.MotivePowerSources.Count, "one recorded steam source");
            Assert.AreEqual(MotivePowerKind.Steam, station.MotivePowerSources[0].Kind);
            Assert.IsTrue(station.MotivePowerSources[0].Available);
            Assert.IsTrue(support.AvailableRepairCapabilities.Contains("saw-filing"), "the sawyer files his own saw");
            Assert.AreEqual(SawmillWorkstationEstablishment.OpeningMillLubricationUnits,
                support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")],
                "the opening lubrication store (one month of sawing days)");
        }

        [Test]
        public void GateWithoutWorkstationSuppliers_RefusesNoRegistrySupplied()
        {
            // The pre-C-3 production shape: a gate with no workstation
            // suppliers at all. This is the refusal C-3 exists to remove.
            var diag = new List<string>();
            var gate = new EquipmentTaskGate();

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "an unwired gate refuses every sawing attempt");
            StringAssert.Contains("no workstation registry supplied", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_GateRefuses_WhenNotEstablished()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            var stations = new BusinessWorkstations(MillInstanceId);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "components in the register alone grant nothing — the station must be established");
            StringAssert.Contains("not established", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_MissingComponent_RefusesNamingIt()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents(includeDriveSystem: false);
            var stations = new BusinessWorkstations(MillInstanceId);

            Assert.IsFalse(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, MotivePowerKind.Steam, diag, out _, out string error),
                "a missing required component refuses the establishment");
            StringAssert.Contains("drive-system", error, "error: " + error);

            // The station is still established with what the register holds,
            // so the gate refuses naming the gap instead of claiming the
            // workstation was never set up.
            WorkstationInstance station = stations.Get("sawmill-saw-line");
            Assert.NotNull(station, "the station record exists even when a component is missing");
            Assert.AreEqual(2, station.ComponentAssetIds.Count);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);

            string refusal = CheckSawLine(gate, diag);
            Assert.NotNull(refusal);
            StringAssert.Contains("drive-system", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_MissingMotivePower_Refuses()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, SupportContext support) = Establish(register, MotivePowerKind.Human);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "human power alone does not drive the saw-line");
            StringAssert.Contains("motive power", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_MissingLubrication_Refuses()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, _) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, SupportWith(includeLubrication: false, includeFiling: true));

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "a saw-line with no lubrication is not supportable (Canon 5.2)");
            StringAssert.Contains("mill-lubrication", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_MissingSawFiling_Refuses()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, _) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, SupportWith(includeLubrication: true, includeFiling: false));

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "a saw-line nobody can file is not supportable (Canon 5.2)");
            StringAssert.Contains("saw-filing", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_NoSupportContext_Refuses()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, _) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, null);

            string refusal = CheckSawLine(gate, diag);

            Assert.NotNull(refusal, "nothing is supportable without a support context (Canon 5.2)");
            StringAssert.Contains("no support context", refusal, "refusal: " + refusal);
        }

        [Test]
        public void Establishment_Idempotent_ReapplyKeepsState()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            var stations = new BusinessWorkstations(MillInstanceId);
            Assert.IsTrue(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, MotivePowerKind.Steam, diag, out _, out string firstError), firstError);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);

            Assert.IsTrue(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, MotivePowerKind.Steam, diag, out _, out string secondError), secondError);
            SawmillWorkstationEstablishment.RegisterMillSupport(support);

            WorkstationInstance station = stations.Get("sawmill-saw-line");
            Assert.AreEqual(3, station.ComponentAssetIds.Count, "no duplicate components on re-establishment");
            Assert.AreEqual(1, station.MotivePowerSources.Count, "no duplicate steam source on re-establishment");
            Assert.AreEqual(30, support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")],
                "re-registering support never tops up the store");

            // A drawn-down store stays drawn down.
            support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 12;
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            Assert.AreEqual(12, support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")]);

            // A component that left the register leaves the station too, and
            // the establishment refuses naming it.
            register.Assets.RemoveAll(a => a != null && a.AssetId == "drive-system-1");
            Assert.IsFalse(SawmillWorkstationEstablishment.TryEstablishStation(
                stations, register, MotivePowerKind.Steam, diag, out _, out string thirdError), "re-establishment refuses the missing drive-system");
            StringAssert.Contains("drive-system", thirdError, "error: " + thirdError);
            Assert.AreEqual(2, station.ComponentAssetIds.Count, "the station is re-derived without the removed component");
            Assert.IsFalse(station.ComponentAssetIds.Contains("drive-system-1"));
        }

        [Test]
        public void RunSawing_WithProductionShapedGate_SawsAndCreditsCounters()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, SupportContext support) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);
            SawmillShopRuntime mill = MillWithLogs(10);

            SawmillSawingResult result = RunSawing(mill, 6, EntityId.For(EntityKind.Person, 5002), 210, diag, gate);

            Assert.NotNull(result, "the established saw-line saws through the production-shaped gate; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(6, result.LogsSawn);
            Assert.AreEqual(24, result.LumberLot.LumberUnits, "6 logs x 4 lumber/log (data).");
            Assert.AreEqual(4, mill.LogStock.TotalLogUnits, "Log lot consumed by the sawn logs.");
            Assert.AreEqual(24, mill.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void RunSawing_WithUnestablishedWorkstation_RefusesBeforeDeduction()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            var stations = new BusinessWorkstations(MillInstanceId);
            var support = new SupportContext();
            SawmillWorkstationEstablishment.RegisterMillSupport(support);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);
            SawmillShopRuntime mill = MillWithLogs(10);
            float conditionBefore = mill.Blade.Condition01;

            SawmillSawingResult result = RunSawing(mill, 6, EntityId.For(EntityKind.Person, 5002), 210, diag, gate);

            Assert.IsNull(result, "an unestablished workstation refuses sawing");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "a refused run consumes nothing");
            Assert.AreEqual(0, mill.LumberStock.TotalLumberUnits, "a refused run produces nothing");
            Assert.AreEqual(conditionBefore, mill.Blade.Condition01, "a refused run wears nothing");
            Assert.IsTrue(string.Join(" ", diag).Contains("not established"), "the refusal names the missing workstation; diag: " + string.Join(" | ", diag));
        }

        [Test]
        public void FilerLoop_DullBlade_Filed_SawingResumes()
        {
            var diag = new List<string>();
            BusinessEquipmentRegister register = MillRegisterWithComponents();
            (BusinessWorkstations stations, SupportContext support) = Establish(register);
            EquipmentTaskGate gate = ProductionShapedGate(stations, register, support);
            SawmillShopRuntime mill = MillWithLogs(10);
            mill.Blade.ApplyWear(0.70f); // condition 0.30 — dull, not broken
            Assert.IsTrue(mill.Blade.IsDull);

            SawmillSawingResult refused = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 210, diag, gate);
            Assert.IsNull(refused, "a dull saw gates ALL output (Tech X §3.9)");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "gated runs consume nothing");
            Assert.IsTrue(string.Join(" ", diag).Contains("DULL"));

            // The filer loop: the sawyer files his own saw (the mill's
            // support context carries the saw-filing capability, so the
            // file-saw task gates on this same workstation).
            string filingRefusal = mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), 211, diag);
            Assert.IsNull(filingRefusal, "filing succeeds; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(1f, mill.Blade.Condition01);
            Assert.IsFalse(mill.Blade.IsDull);

            SawmillSawingResult result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 211, diag, gate);
            Assert.NotNull(result, "output resumes after the filer works; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(2, result.LogsSawn);
            Assert.AreEqual(8, mill.LogStock.TotalLogUnits);
        }
    }
}
