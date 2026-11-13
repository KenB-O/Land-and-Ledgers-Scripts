using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Tasks;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W4A: Sawmill runtime — log intake with provenance validation, data-driven
    /// log-to-lumber conversion, lumber lots with full provenance chains, blade
    /// wear and the filer loop (Tech X §3.9), and slab/offcut byproduct lots
    /// transferred by custody to a named FuelDealer (Canon §8.4).
    /// </summary>
    [TestFixture]
    public sealed class SawmillRuntimeTests
    {
        private static LogLot DocumentedLogLot(int lotNumber, int logs, string standId)
        {
            return new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LogUnits = logs,
                MassKg = logs * 300f,
                OwnerId = "mill-1",
                StandId = standId,
                Species = "white pine",
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = 200,
            };
        }

        private static SawmillShopRuntime MillWithLogs(int logs)
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, logs, "stand-1"), diag));
            return mill;
        }

        private static SawmillSawingResult RunSawing(SawmillShopRuntime mill, int logs,
            EntityId worker, int day, List<string> diag, string species = "white pine")
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

            var registry = new BusinessWorkstations("mill-1");
            var station = registry.GetOrCreate("sawmill-saw-line");
            station.SpaceId = "mill-saw-floor";
            station.InstallComponent("saw-line-1");
            station.InstallComponent("log-carriage-1");
            station.InstallComponent("drive-system-1");
            station.MotivePowerSources.Add(new MotivePowerSource(MotivePowerKind.Steam, "recorded steam engine"));
            var gate = new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => registry,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => id == "saw-line-1"
                    ? new WorkstationComponentView { AssetId = id, Kind = "saw-line", Condition01 = 1f, IsUsable = true }
                    : id == "log-carriage-1"
                        ? new WorkstationComponentView { AssetId = id, Kind = "log-carriage", Condition01 = 1f, IsUsable = true }
                        : id == "drive-system-1"
                            ? new WorkstationComponentView { AssetId = id, Kind = "drive-system", Condition01 = 1f, IsUsable = true }
                            : (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
            return mill.RunSawing(logs, worker, day, diag, tasks, task.TaskId, budgets, gate);
        }

        // ---- Log intake: provenance validation ----

        [Test]
        public void LogIntake_Refuses_AnonymousLogs_Loudly()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();

            var anonymous = DocumentedLogLot(9101, 10, string.Empty);
            string refusal = mill.ReceiveLogLot(anonymous, diag);

            Assert.NotNull(refusal, "Anonymous logs must be refused.");
            Assert.AreEqual(0, mill.LogStock.TotalLogUnits, "Refused logs touch nothing.");
            Assert.IsTrue(string.Join(" ", diag).Contains("Anonymous logs are refused loudly"),
                "The refusal must be loud about provenance.");
        }

        [Test]
        public void LogIntake_Refuses_LotWithoutEntityId()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();

            var lot = DocumentedLogLot(9101, 10, "stand-1");
            lot.LotId = EntityId.Invalid;
            Assert.NotNull(mill.ReceiveLogLot(lot, diag));
            Assert.AreEqual(0, mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void LogIntake_Accepts_DocumentedLogLot()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();

            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, 10, "stand-1"), diag));
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits);
        }

        // ---- Conversion ratios as data ----

        [Test]
        public void ConversionData_WhitePine_MatchesT1ECalibration()
        {
            var profile = SawmillConversionData.DefaultForSpecies("white pine");
            Assert.AreEqual("softwood-standard", profile.ProfileId);
            Assert.AreEqual(4, profile.LumberPerLog, "T1E calibration: 4 lumber units per log.");
            Assert.AreEqual(30, profile.SawMinutesPerLog, "T1E calibration: 30 saw-minutes per log.");
            Assert.Greater(profile.FuelWoodUnitsPerLog, 0, "Slabs/offcuts are the fuel byproduct.");
        }

        [Test]
        public void ConversionData_Hardwood_WearsBladeFaster()
        {
            var soft = SawmillConversionData.DefaultForSpecies("white pine");
            var hard = SawmillConversionData.DefaultForSpecies("oak");
            Assert.AreNotEqual(soft.ProfileId, hard.ProfileId);
            Assert.Greater(hard.BladeWearPerLog01, soft.BladeWearPerLog01);
            Assert.Greater(hard.SawMinutesPerLog, soft.SawMinutesPerLog);
        }

        [Test]
        public void ConversionData_UnknownSpecies_FallsBack_Loudly()
        {
            var profile = SawmillConversionData.DefaultForSpecies("mysterywood");
            Assert.NotNull(profile);
            Assert.AreEqual("softwood-standard", profile.ProfileId, "The fallback is a named profile, never an invented yield.");
        }

        // ---- Sawing: lumber lots with full provenance ----

        [Test]
        public void Sawing_Produces_LumberLot_WithFullProvenanceChain()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, 10, "stand-1"), diag));

            var worker = EntityId.For(EntityKind.Person, 5002);
            var result = RunSawing(mill, 6, worker, 210, diag);

            Assert.NotNull(result);
            Assert.AreEqual(6, result.LogsSawn);
            Assert.AreEqual(24, result.LumberLot.LumberUnits, "6 logs x 4 lumber/log (data).");
            Assert.AreEqual("stand-1", result.LumberLot.StandId);
            Assert.AreEqual("white pine", result.LumberLot.Species);
            Assert.AreEqual("mill-1", result.LumberLot.MillBusinessId);
            Assert.AreEqual("mill-1", result.LumberLot.OwnerId, "Input ownership carries through the conversion.");
            Assert.AreEqual(worker, result.LumberLot.SawedBy);
            Assert.AreEqual(210, result.LumberLot.SawedDayIndex);
            Assert.AreEqual("softwood-standard", result.LumberLot.ConversionProfileId);
            Assert.AreEqual(EntityId.For(EntityKind.Lot, 9101).ToString(), result.LumberLot.SourceLogLotId);
            Assert.AreEqual(4, mill.LogStock.TotalLogUnits, "Log lot consumed by the sawn logs.");
            Assert.AreEqual(24, mill.LumberStock.TotalLumberUnits);
            Assert.IsTrue(result.LumberLot.ProvenanceChain().Contains("stand-1"));
        }

        [Test]
        public void Sawing_Produces_ByproductLot_ForFuelDealer()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();

            var result = RunSawing(mill, 6, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.NotNull(result.ByproductLot);
            Assert.AreEqual(12, result.ByproductLot.FuelWoodUnits, "6 logs x 2 fuel-wood/log (data).");
            Assert.AreEqual("mill-1", result.ByproductLot.SourceMillBusinessId);
            Assert.AreEqual("stand-1", result.ByproductLot.StandId);
            Assert.AreEqual("mill-1", result.ByproductLot.OwnerId, "Byproduct ownership follows the source log lot.");
            Assert.AreEqual(EntityId.For(EntityKind.Lot, 9101).ToString(), result.ByproductLot.SourceLogLotId);
            Assert.AreEqual(1, mill.ByproductStock.Count, "Byproduct held as a real lot in the mill yard.");
        }

        [Test]
        public void Sawing_WearsBlade_PerLog_FromData()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            var profile = SawmillConversionData.DefaultForSpecies("white pine");

            RunSawing(mill, 5, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.AreEqual(1f - 5 * profile.BladeWearPerLog01, mill.Blade.Condition01, 0.0001f,
                "Blade wear is data-driven, per log sawn.");
        }

        [Test]
        public void Sawing_Refuses_Loudly_WhenLogLotShort()
        {
            var mill = MillWithLogs(3);
            var diag = new List<string>();
            float conditionBefore = mill.Blade.Condition01;

            var result = RunSawing(mill, 6, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.IsNull(result, "Short-supply runs are refused, not blended across lots.");
            Assert.AreEqual(3, mill.LogStock.TotalLogUnits, "Refused runs consume nothing.");
            Assert.AreEqual(conditionBefore, mill.Blade.Condition01, "Refused runs wear nothing.");
            Assert.IsTrue(string.Join(" ", diag).Contains("REFUSED"));
        }

        // ---- Blade wear / filer loop ----

        [Test]
        public void Sawing_Refuses_WhenBladeDull()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.70f); // condition 0.30 — dull, not broken

            var result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.IsNull(result, "A dull saw gates ALL output (Tech X §3.9).");
            Assert.AreEqual(10, mill.LogStock.TotalLogUnits, "Gated runs consume nothing.");
            Assert.IsTrue(string.Join(" ", diag).Contains("DULL"));
        }

        [Test]
        public void Sawing_Refuses_WhenBladeBroken_DistinctMessage()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.95f); // condition 0.05 — broken

            var result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 210, diag);

            Assert.IsNull(result);
            Assert.IsTrue(string.Join(" ", diag).Contains("BROKEN"), "Broken is reported distinctly from dull.");
        }

        [Test]
        public void SawFiling_RestoresBlade_AndRecordsHistory()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            mill.Blade.ApplyWear(0.70f);
            Assert.IsTrue(mill.Blade.IsDull);

            string refusal = mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), 211, diag);

            Assert.IsNull(refusal);
            Assert.AreEqual(1f, mill.Blade.Condition01);
            Assert.IsFalse(mill.Blade.IsDull);
            Assert.AreEqual(1, mill.Blade.MaintenanceLog.Count, "Filing records diligence history.");
            Assert.IsTrue(mill.Blade.MaintenanceLog[0].Contains("211"));

            var result = RunSawing(mill, 2, EntityId.For(EntityKind.Person, 5002), 211, diag);
            Assert.NotNull(result, "Output resumes after the filer works.");
        }

        [Test]
        public void MaintenanceTask_FileSaw_Registers_WithWorkstationGate()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();

            SawmillMaintenanceTasks.RegisterTaskDefinitions(authority, diag);

            var def = authority.GetDefinition(SawmillMaintenanceTasks.FileSawTaskId);
            Assert.NotNull(def, "file-saw is registered.");
            Assert.AreEqual(45, def.BaseMinutes, "Authored filing calibration.");
            Assert.IsTrue(def.EquipmentClasses.Contains("workstation:sawmill-saw-line"),
                "Filing executes against the mill's saw line.");
        }

        // ---- Byproducts to the FuelDealer ----

        [Test]
        public void ByproductTransfer_MovesRealLots_ToFuelDealerIntake()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            RunSawing(mill, 6, EntityId.For(EntityKind.Person, 5002), 210, diag);
            var intake = new FuelYardByproductIntake();

            int transferred = mill.TransferByproductsToFuelDealer("fuel-dealer-1", intake, diag);

            Assert.AreEqual(1, transferred);
            Assert.AreEqual(0, mill.ByproductStock.Count, "Custody moved — the mill no longer holds the lot.");
            var held = intake.LotsFor("fuel-dealer-1");
            Assert.AreEqual(1, held.Count);
            Assert.AreEqual(12, held[0].FuelWoodUnits);
            Assert.AreEqual("mill-1", held[0].SourceMillBusinessId);
            Assert.AreEqual("stand-1", held[0].StandId, "Provenance survives custody transfer.");
            Assert.AreEqual(12, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
        }

        [Test]
        public void FuelYardIntake_RejectsDuplicateLotIdentityAcrossOwners()
        {
            var intake = new FuelYardByproductIntake();
            var diagnostics = new List<string>();
            var firstReceipt = new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9202),
                FuelWoodUnits = 12,
                OwnerId = "mill-1",
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "white pine",
                ProducedDayIndex = 210,
            };

            Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", firstReceipt, diagnostics));
            var duplicateReceipt = new SlabOffcutFuelLot
            {
                LotId = firstReceipt.LotId,
                FuelWoodUnits = 99,
                OwnerId = "mill-1",
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "white pine",
                ProducedDayIndex = 210,
            };

            string sameDealerRefusal = intake.ReceiveByproductLot("fuel-dealer-1", duplicateReceipt, diagnostics);
            StringAssert.Contains("already held by FuelDealer 'fuel-dealer-1'", sameDealerRefusal);
            string otherDealerRefusal = intake.ReceiveByproductLot("fuel-dealer-2", duplicateReceipt, diagnostics);
            StringAssert.Contains("already held by FuelDealer 'fuel-dealer-1'", otherDealerRefusal);

            Assert.AreEqual(12, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"), "the original physical quantity remains authoritative");
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor("fuel-dealer-2"), "a second yard cannot claim the same lot");
            Assert.AreEqual(1, intake.LotsFor("fuel-dealer-1").Count);
            Assert.AreEqual(0, intake.LotsFor("fuel-dealer-2").Count);
        }

        [Test]
        public void FuelYardIntake_Refuses_OrphanLots()
        {
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();

            var orphan = new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9201),
                FuelWoodUnits = 12,
                OwnerId = "mill-1",
                SourceMillBusinessId = string.Empty, // no mill named
                SourceLogLotId = "9101",
                StandId = "stand-1",
                ProducedDayIndex = 210,
            };
            Assert.NotNull(intake.ReceiveByproductLot("fuel-dealer-1", orphan, diag));
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));

            Assert.NotNull(intake.ReceiveByproductLot(string.Empty, orphan, diag),
                "Slabs go to a NAMED FuelDealer, never the void.");
        }

        [Test]
        public void LumberStock_Refuses_ForeignMillLumber()
        {
            var ids = new EntityIdRegistry();
            var stock = new SawmillLumberStock("mill-1");
            var diag = new List<string>();

            var foreign = new SawmillLumberLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                LumberUnits = 20,
                SourceLogLotId = "9101",
                StandId = "stand-1",
                MillBusinessId = "mill-2",
                SawedDayIndex = 210,
            };
            Assert.NotNull(stock.ReceiveLot(foreign, diag), "Another mill's lumber is a purchase, not production.");
            Assert.AreEqual(0, stock.TotalLumberUnits);
        }

        // ---- Save / load ----

        [Test]
        public void SaveLoad_RoundTrip_PreservesEverything()
        {
            var mill = MillWithLogs(10);
            var diag = new List<string>();
            RunSawing(mill, 4, EntityId.For(EntityKind.Person, 5002), 210, diag);
            mill.Blade.ApplyWear(0.10f);
            mill.Blade.RecordFiling(211, diag); // resets to 1.0, adds a log entry

            var dto = mill.CaptureSaveDto();

            var restored = new SawmillShopRuntime("mill-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(6, restored.LogStock.TotalLogUnits);
            Assert.AreEqual(16, restored.LumberStock.TotalLumberUnits);
            Assert.AreEqual(1, restored.ByproductStock.Count);
            Assert.AreEqual(8, restored.ByproductStock[0].FuelWoodUnits);
            Assert.AreEqual("stand-1", restored.ByproductStock[0].StandId);
            Assert.AreEqual(1f, restored.Blade.Condition01);
            Assert.AreEqual(1, restored.Blade.MaintenanceLog.Count);
            Assert.AreEqual(16, restored.LumberStock.Lots[0].LumberUnits);
            Assert.AreEqual("softwood-standard", restored.LumberStock.Lots[0].ConversionProfileId);
        }

        [Test]
        public void SaveLoad_ByproductIntake_RoundTrip()
        {
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();
            intake.ReceiveByproductLot("fuel-dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 9201),
                FuelWoodUnits = 12,
                OwnerId = "mill-1",
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "9101",
                StandId = "stand-1",
                ProducedDayIndex = 210,
            }, diag);

            var restored = new FuelYardByproductIntake();
            restored.LoadFromSaveDto(intake.CaptureSaveDto());

            Assert.AreEqual(12, restored.TotalFuelWoodUnitsFor("fuel-dealer-1"));
            Assert.AreEqual("stand-1", restored.LotsFor("fuel-dealer-1")[0].StandId);
        }
    }
}
