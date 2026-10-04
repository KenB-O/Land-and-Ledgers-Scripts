using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8D: hoisting plants as real equipment with condition — rope wears
    /// with tons hoisted, a worn plant cannot hoist (condition gate, not a
    /// silent debuff). Ventilation/drainage are task gates on real equipment
    /// (canon covers them only as capacity equipment/occupation — no
    /// invented flood/gas mechanics).
    /// </summary>
    [TestFixture]
    public sealed class MineHoistingTests
    {
        [Test]
        public void InstallHoist_OnePerShaft()
        {
            var register = new MineHoistRegister();
            var diag = new List<string>();

            MineHoist hoist = register.InstallHoist(HoistKind.SteamHoist, "shaft-1", 400, diag);
            Assert.IsNotNull(hoist);
            Assert.AreEqual(HoistKind.SteamHoist, hoist.HoistKind);
            Assert.IsTrue(hoist.CanHoist);

            Assert.IsNull(register.InstallHoist(HoistKind.HorseWhim, "shaft-1", 401, diag),
                "second hoist on the same shaft refused");
            Assert.IsNull(register.InstallHoist(HoistKind.Unspecified, "shaft-2", 401, diag),
                "unspecified hoist kind refused");
            Assert.AreEqual(1, register.Hoists.Count);
        }

        [Test]
        public void RecordHoistingWork_WearsRopeAndGatesOnCondition()
        {
            var register = new MineHoistRegister();
            var diag = new List<string>();
            MineHoist hoist = register.InstallHoist(HoistKind.Gin, "shaft-1", 400, diag);

            Assert.IsNull(hoist.RecordHoistingWork(100, diag));
            Assert.Less(hoist.RopeCondition01, 1f, "rope wears with tons hoisted");

            // Wear the rope down to the gate.
            for (int i = 0; i < 20 && hoist.CanHoist; i++)
                hoist.RecordHoistingWork(100, diag);
            Assert.IsFalse(hoist.CanHoist, "worn rope gates hoisting (Tech X §3.9)");
            Assert.IsTrue(hoist.RopeNeedsReplacement);

            string blocked = hoist.RecordHoistingWork(10, diag);
            Assert.IsNotNull(blocked, "hoisting on a failed rope is refused loudly");

            hoist.ReplaceRope(450);
            Assert.AreEqual(1f, hoist.RopeCondition01);
            Assert.IsTrue(hoist.CanHoist, "new rope restores hoisting");
        }

        [Test]
        public void Repair_RestoresWornPlant()
        {
            var register = new MineHoistRegister();
            var diag = new List<string>();
            MineHoist hoist = register.InstallHoist(HoistKind.HorseWhim, "shaft-1", 400, diag);

            hoist.RecordWear(0.8f);
            Assert.IsFalse(hoist.CanHoist, "worn plant cannot hoist");

            hoist.Repair(410);
            Assert.AreEqual(1f, hoist.Condition01);
            Assert.IsTrue(hoist.CanHoist);
        }

        [Test]
        public void HoistTaskCatalog_GatesOnRealEquipment()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();
            MineHoistingTaskCatalog.Register(authority, diag);

            TaskDefinition hoistOre = authority.GetDefinition(MineHoistingTaskCatalog.HoistOreTaskId);
            Assert.IsNotNull(hoistOre);
            Assert.IsTrue(hoistOre.EquipmentClasses.Contains(
                LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Asset(
                    MineShaftTaskCatalog.HoistAssetKind)),
                "hoisting requires installed hoisting equipment");

            TaskDefinition dewater = authority.GetDefinition(MineHoistingTaskCatalog.DewaterShaftTaskId);
            Assert.IsNotNull(dewater);
            Assert.IsTrue(dewater.EquipmentClasses.Contains(
                LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Asset(
                    MineHoistingTaskCatalog.PumpAssetKind)),
                "drainage requires a real pump (Canon pump-operator profile)");

            Assert.IsNotNull(authority.GetDefinition(MineHoistingTaskCatalog.VentilateHeadingTaskId));
            Assert.IsNotNull(authority.GetDefinition(MineHoistingTaskCatalog.InspectHoistTaskId));
        }
    }
}
