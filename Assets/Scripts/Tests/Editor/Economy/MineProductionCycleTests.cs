using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass VIII (production networks): the mine extraction production
    /// function. Completed break-ore work becomes ore lots with
    /// shaft/level/vein provenance in the stockpile, the deposit depletes,
    /// and the hoist rope wears. Gates are loud: InOre level, rostered miner,
    /// producing posture, serviceable hoist. The end-to-end test runs the
    /// full detailed chain: extraction → stockpile → shipment → settlement.
    /// </summary>
    [TestFixture]
    public sealed class MineProductionCycleTests
    {
        private sealed class WorkingMine
        {
            public MineRuntimeState Mine;
            public MineLevel Level;
            public EntityId Miner;
            public MineHoist Hoist;
        }

        private static WorkingMine NewWorkingMine(EntityIdRegistry registry, List<string> diag)
        {
            var mine = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            MineShaft shaft = mine.ShaftPlan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, null);
            MineLevel level = mine.ShaftPlan.PlanLevel(shaft.ShaftId, 1, 100, null);
            level.SetWorkingStatus(MineWorkingStatus.InOre);

            EntityId miner = registry.Allocate(EntityKind.Person);
            MineCrewAssignment assignment = mine.LaborRegister.AssignMiner(
                registry.Allocate(EntityKind.EmploymentRelationship), miner,
                MineRole.HardRockMiner, MineShift.Day, 200, 400, diag);
            Assert.IsNotNull(assignment);

            MineHoist hoist = mine.HoistRegister.InstallHoist(HoistKind.SteamHoist, shaft.ShaftId, 400, diag);
            Assert.IsNotNull(hoist);

            return new WorkingMine { Mine = mine, Level = level, Miner = miner, Hoist = hoist };
        }

        [Test]
        public void BreakOreShift_ProducesLot_DepletesDeposit_WearsHoist()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            WorkingMine w = NewWorkingMine(registry, diag);

            float richnessBefore = w.Mine.RemainingRichness01;
            float ropeBefore = w.Hoist.RopeCondition01;

            // 240 worked break-ore minutes at 120 min/ton = 2 tons of ore.
            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                w.Mine, w.Level.LevelId, w.Miner, 240, 3.0f, registry, 400, diag);

            Assert.IsNotNull(lot, string.Join("; ", diag));
            Assert.AreEqual(2, lot.Tons);
            Assert.AreEqual(MineralResourceKind.Silver, lot.MineralKind);
            Assert.AreEqual(w.Level.ShaftId, lot.ShaftId);
            Assert.AreEqual(w.Level.LevelId, lot.LevelId);
            Assert.AreEqual(2, w.Mine.OreStock.TotalTons, "broken tons sit in the stockpile");
            Assert.AreEqual(richnessBefore - 2 * MineProductionCycle.ExtractionDepletionPerTon01,
                w.Mine.RemainingRichness01, 1e-6f, "deposit depleted by real tons");
            Assert.Less(w.Hoist.RopeCondition01, ropeBefore, "hoist rope wore with tons hoisted");
        }

        [Test]
        public void BreakOreShift_Refused_WhenLevelNotInOre()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            WorkingMine w = NewWorkingMine(registry, diag);
            w.Level.SetWorkingStatus(MineWorkingStatus.Driving);
            float richnessBefore = w.Mine.RemainingRichness01;

            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                w.Mine, w.Level.LevelId, w.Miner, 240, 3.0f, registry, 400, diag);

            Assert.IsNull(lot, "a driving level is not a producing front");
            Assert.AreEqual(0, w.Mine.OreStock.TotalTons);
            Assert.AreEqual(richnessBefore, w.Mine.RemainingRichness01, 1e-6f, "no depletion without production");
        }

        [Test]
        public void BreakOreShift_Refused_WhenMinerNotRostered()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            WorkingMine w = NewWorkingMine(registry, diag);
            EntityId stranger = registry.Allocate(EntityKind.Person);

            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                w.Mine, w.Level.LevelId, stranger, 240, 3.0f, registry, 400, diag);

            Assert.IsNull(lot, "no crew assignment, no broken ore");
            Assert.AreEqual(0, w.Mine.OreStock.TotalTons);
        }

        [Test]
        public void BreakOreShift_Refused_WhenMineClosed()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            WorkingMine w = NewWorkingMine(registry, diag);
            w.Mine.SetOperatingPosture(MineOperatingPosture.Closed);

            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                w.Mine, w.Level.LevelId, w.Miner, 240, 3.0f, registry, 400, diag);

            Assert.IsNull(lot, "a closed mine produces nothing");
            Assert.AreEqual(0, w.Mine.OreStock.TotalTons);
        }

        [Test]
        public void BreakOreShift_Refused_WithoutHoist()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            var mine = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            MineShaft shaft = mine.ShaftPlan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, null);
            MineLevel level = mine.ShaftPlan.PlanLevel(shaft.ShaftId, 1, 100, null);
            level.SetWorkingStatus(MineWorkingStatus.InOre);
            EntityId miner = registry.Allocate(EntityKind.Person);
            mine.LaborRegister.AssignMiner(registry.Allocate(EntityKind.EmploymentRelationship), miner,
                MineRole.HardRockMiner, MineShift.Day, 200, 400, diag);

            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                mine, level.LevelId, miner, 240, 3.0f, registry, 400, diag);

            Assert.IsNull(lot, "broken ore cannot leave the workings without hoisting");
            Assert.AreEqual(0, mine.OreStock.TotalTons);
        }

        [Test]
        public void BreakOreShift_ChainEndToEnd_ShipAndSettleConserved()
        {
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            WorkingMine w = NewWorkingMine(registry, diag);

            MineOreLot lot = MineProductionCycle.RecordBreakOreShift(
                w.Mine, w.Level.LevelId, w.Miner, 600, 2.5f, registry, 400, diag);
            Assert.IsNotNull(lot, string.Join("; ", diag));
            Assert.AreEqual(5, lot.Tons);

            // The detailed chain, end to end: stockpile → declared smelter
            // shipment → on-delivery settlement (flat path, no grade terms).
            var service = w.Mine.CreateShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(
                w.Mine.OreStock, "biz-mine-1", "Silver King", "smelter-omaha-ag", 5,
                w.Level.ShaftId, 400);
            Assert.IsNotNull(order, string.Join("; ", diag));
            Assert.AreEqual(0, w.Mine.OreStock.TotalTons, "shipped tons left the stockpile");

            var cash = new HouseholdLedger(70);
            Assert.IsNull(service.SettleDelivery(order.OrderId, cash, 421));
            Assert.AreEqual(5 * 1900, cash.GetBalanceCents(), "smelter paid the flat per-ton price");
            Assert.IsTrue(order.IsDelivered);

            // Conservation: 5 tons broken == 5 tons shipped == 5 tons paid.
            Assert.AreEqual(5, order.Tons);
        }
    }
}
