using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Tasks;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8A: shafts and levels as property improvements; sinking as tasks with
    /// labor + timber + equipment inputs; levels/drives as tracked workings;
    /// the mine plan carries named veins. No synthetic progress — depths are
    /// recorded only from completed work.
    /// </summary>
    [TestFixture]
    public sealed class MineShaftPlanTests
    {
        [Test]
        public void PlanShaft_RequiresUniqueName()
        {
            var plan = new MineShaftPlan();
            MineShaft first = plan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, "collar on the ridge");
            Assert.IsNotNull(first);
            Assert.AreEqual("No. 1 Shaft", first.ShaftName);
            Assert.AreEqual(MineShaftStatus.Planned, first.Status);

            MineShaft dup = plan.PlanShaft("no. 1 shaft", MineralResourceKind.Silver, 120, null);
            Assert.IsNull(dup, "duplicate shaft name must be refused");
            Assert.AreEqual(1, plan.Shafts.Count);
            Assert.IsNotEmpty(plan.Diagnostics);
        }

        [Test]
        public void RecordSinking_AdvancesDepthAndCapitalizesCost()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("No. 1 Shaft", MineralResourceKind.Coal, 100, null);
            var diag = new List<string>();

            Assert.IsNull(shaft.RecordSinking(20, 20, 15000, 300, diag));
            Assert.AreEqual(20, shaft.SunkDepthFeet);
            Assert.AreEqual(20, shaft.TimberedDepthFeet);
            Assert.AreEqual(15000, shaft.CapitalizedCostCents);
            Assert.AreEqual(MineShaftStatus.Sinking, shaft.Status);
            Assert.AreEqual(15000, plan.TotalCapitalizedCostCents);

            Assert.IsNull(shaft.RecordSinking(80, 80, 60000, 340, diag));
            Assert.AreEqual(100, shaft.SunkDepthFeet);
            Assert.AreEqual(MineShaftStatus.Timbered, shaft.Status);
        }

        [Test]
        public void RecordSinking_CapsDepthAtTargetAndTimberAtSunk()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("No. 2 Shaft", MineralResourceKind.Gold, 50, null);
            var diag = new List<string>();

            Assert.IsNull(shaft.RecordSinking(80, 80, 1000, 301, diag));
            Assert.AreEqual(50, shaft.SunkDepthFeet, "sunk depth caps at target depth");
            Assert.AreEqual(50, shaft.TimberedDepthFeet, "timbering caps at sunk depth");
        }

        [Test]
        public void RecordSinking_RefusesWorkOnAbandonedShaft()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("Dead Shaft", MineralResourceKind.Iron, 60, null);
            var diag = new List<string>();
            Assert.IsNull(shaft.RecordSinking(10, 10, 5000, 300, diag));
            shaft.Abandon();
            string rejection = shaft.RecordSinking(5, 5, 1000, 305, diag);
            Assert.IsNotNull(rejection, "no work may be recorded on an abandoned shaft");
            Assert.AreEqual(10, shaft.SunkDepthFeet);
        }

        [Test]
        public void ShaftLifecycle_RequiresHoistBeforeProduction()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("No. 3 Shaft", MineralResourceKind.Silver, 40, null);
            var diag = new List<string>();
            Assert.IsNull(shaft.RecordSinking(40, 40, 30000, 320, diag));

            string noHoist = shaft.SetInProduction(321);
            Assert.IsNotNull(noHoist, "production requires hoisting equipment first");

            Assert.IsNull(shaft.MarkHoistingEquipped(322));
            Assert.IsTrue(shaft.HasHoist);
            Assert.IsNull(shaft.SetInProduction(323));
            Assert.AreEqual(MineShaftStatus.InProduction, shaft.Status);
        }

        [Test]
        public void DeclareVein_RequiresUniqueName()
        {
            var plan = new MineShaftPlan();
            MineVein vein = plan.DeclareVein("Big Bonanza", MineralResourceKind.Silver, "N40E", "70 SE");
            Assert.IsNotNull(vein);
            Assert.AreEqual("Big Bonanza", vein.VeinName);
            Assert.IsNull(plan.DeclareVein("big bonanza", MineralResourceKind.Silver, null, null));
            Assert.AreEqual(1, plan.Veins.Count);
        }

        [Test]
        public void PlanLevel_ValidatesShaftAndVein()
        {
            var plan = new MineShaftPlan();
            MineShaft shaft = plan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, null);
            MineVein vein = plan.DeclareVein("Big Bonanza", MineralResourceKind.Silver, null, null);

            Assert.IsNull(plan.PlanLevel("shaft-999", 1, 100, null), "unknown shaft refused");

            MineLevel tooDeep = plan.PlanLevel(shaft.ShaftId, 1, 150, null);
            Assert.IsNull(tooDeep, "level deeper than shaft target refused");

            MineLevel badVein = plan.PlanLevel(shaft.ShaftId, 1, 100, "vein-999");
            Assert.IsNull(badVein, "undeclared vein refused");

            MineLevel level = plan.PlanLevel(shaft.ShaftId, 1, 100, vein.VeinId);
            Assert.IsNotNull(level);
            Assert.AreEqual(vein.VeinId, level.OnVeinId);
            Assert.AreEqual(1, level.LevelNumber);

            var diag = new List<string>();
            Assert.IsNull(level.RecordDrive(120, diag));
            Assert.AreEqual(120, level.DriveFeet);
            Assert.IsNotNull(level.RecordDrive(0, diag), "non-positive footage refused");

            level.SetWorkingStatus(MineWorkingStatus.InOre);
            Assert.AreEqual(MineWorkingStatus.InOre, level.WorkingStatus);
        }

        [Test]
        public void LevelsOnShaft_ReturnsOnlyThatShaftsLevels()
        {
            var plan = new MineShaftPlan();
            MineShaft a = plan.PlanShaft("A", MineralResourceKind.Coal, 100, null);
            MineShaft b = plan.PlanShaft("B", MineralResourceKind.Coal, 100, null);
            plan.PlanLevel(a.ShaftId, 1, 60, null);
            plan.PlanLevel(b.ShaftId, 1, 60, null);
            plan.PlanLevel(b.ShaftId, 2, 90, null);
            Assert.AreEqual(1, plan.LevelsOnShaft(a.ShaftId).Count);
            Assert.AreEqual(2, plan.LevelsOnShaft(b.ShaftId).Count);
        }

        [Test]
        public void TaskCatalog_RegistersShaftWorkWithInputsAndEquipment()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();
            MineShaftTaskCatalog.Register(authority, diag);

            TaskDefinition sink = authority.GetDefinition(MineShaftTaskCatalog.SinkShaftTaskId);
            Assert.IsNotNull(sink);
            Assert.AreEqual(1, sink.Inputs.Count);
            Assert.AreEqual(MineShaftTaskCatalog.TimberSetItemId, sink.Inputs[0].ItemId);
            Assert.IsTrue(sink.EquipmentClasses.Contains(
                LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Kit(MineShaftTaskCatalog.MinerHandKitId)),
                "sinking requires the miner hand kit (Canon Part V)");
            Assert.IsTrue(sink.EquipmentClasses.Contains(
                LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Asset(MineShaftTaskCatalog.WindlassAssetKind)),
                "sinking requires hoisting equipment at the windlass stage");

            Assert.IsNotNull(authority.GetDefinition(MineShaftTaskCatalog.TimberShaftTaskId));
            Assert.IsNotNull(authority.GetDefinition(MineShaftTaskCatalog.DriveLevelTaskId));
            Assert.IsNotNull(authority.GetDefinition(MineShaftTaskCatalog.EquipHoistTaskId));
        }

        [Test]
        public void SaveRoundTrip_PreservesShaftPlan()
        {
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            MineShaftPlan plan = state.ShaftPlan;
            MineShaft shaft = plan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, "ridge collar");
            var diag = new List<string>();
            shaft.RecordSinking(30, 30, 22000, 310, diag);
            MineVein vein = plan.DeclareVein("Big Bonanza", MineralResourceKind.Silver, "N40E", "70 SE");
            MineLevel level = plan.PlanLevel(shaft.ShaftId, 1, 100, vein.VeinId);
            level.RecordDrive(90, diag);

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(1, restored.ShaftPlan.Shafts.Count);
            MineShaft restoredShaft = restored.ShaftPlan.Shafts[0];
            Assert.AreEqual("No. 1 Shaft", restoredShaft.ShaftName);
            Assert.AreEqual(30, restoredShaft.SunkDepthFeet);
            Assert.AreEqual(22000, restoredShaft.CapitalizedCostCents);
            Assert.AreEqual(1, restored.ShaftPlan.Veins.Count);
            Assert.AreEqual("Big Bonanza", restored.ShaftPlan.Veins[0].VeinName);
            Assert.AreEqual(1, restored.ShaftPlan.Levels.Count);
            Assert.AreEqual(90, restored.ShaftPlan.Levels[0].DriveFeet);
        }

        [Test]
        public void LegacySave_LoadsWithEmptyShaftPlan()
        {
            var dto = new LandLedgers.Persistence.MineRuntimeSaveDto
            {
                mineralKind = MineralResourceKind.Coal,
            };
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);
            Assert.IsNotNull(restored.ShaftPlan, "legacy saves get an empty plan, never null");
            Assert.AreEqual(0, restored.ShaftPlan.Shafts.Count);
        }
    }
}
