using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass 2 (task system): Roslyn tests for the new task-system surface
    /// only. Existing systems are not re-tested here; their own suites
    /// cover them. All numbers are tuning relationships, not canon.
    /// </summary>
    [TestFixture]
    public sealed class Pass2TaskSystemTests
    {
        [Test]
        public void Catalogs_RegisterStoreFuelAndLifeDefinitions_WithSkillHooks()
        {
            var authority = new TaskAuthority();
            Pass2TaskCatalogs.RegisterAll(authority);

            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.OrderFromSupplierId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.MoveShelfToBackId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.HandlePaymentId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.BookkeepingId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.FellTreeId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.DispenseFuelId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.SellFuelId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.EatMealId));
            Assert.IsNotNull(authority.GetDefinition(Pass2TaskCatalogs.SleepId));

            Assert.AreEqual(Pass2TaskCatalogs.Pass2SkillHooks.Forestry,
                authority.GetDefinition(Pass2TaskCatalogs.FellTreeId).RequiredSkillId);
            // Ordering has an explicit unskilled marker, not a silent gap.
            Assert.IsTrue(string.IsNullOrEmpty(
                authority.GetDefinition(Pass2TaskCatalogs.OrderFromSupplierId).RequiredSkillId));

            var decisions = Pass2TaskCatalogs.StoreActionDecisions();
            Pass2StoreActionDecision choose = null;
            Pass2StoreActionDecision price = null;
            foreach (var d in decisions)
            {
                if (d.ActionId == "gs.choose-supplier") choose = d;
                if (d.ActionId == "gs.set-price-margin") price = d;
            }
            Assert.IsNotNull(choose);
            Assert.AreEqual(Pass2ActionKind.InstantAction, choose.Kind);
            Assert.IsNotNull(price);
            Assert.AreEqual(Pass2ActionKind.InstantAction, price.Kind);
        }

        [Test]
        public void Crew_SoloIsSlow_NeverRefused_MoreHandsFaster()
        {
            Assert.AreEqual(90, Pass2CrewDuration.CrewAdjustedMinutes(90, 1));
            Assert.AreEqual(53, Pass2CrewDuration.CrewAdjustedMinutes(90, 2));
            Assert.AreEqual(30, Pass2CrewDuration.CrewAdjustedMinutes(90, 4));
            Assert.AreEqual(1, Pass2CrewDuration.CrewAdjustedMinutes(1, 10));
        }

        [Test]
        public void FuelDispense_Fifo_ShortfallRefusesWhole_EmptiedLotsNeverResold()
        {
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();
            intake.ReceiveByproductLot("fuel-dealer", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 1),
                FuelWoodUnits = 10,
                SourceMillBusinessId = "sawmill",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            }, diag);
            intake.ReceiveByproductLot("fuel-dealer", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 2),
                FuelWoodUnits = 10,
                SourceMillBusinessId = "sawmill",
                SourceLogLotId = "log-2",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            }, diag);

            // Shortfall dispenses nothing.
            Assert.IsNull(intake.TryDispenseUnits("fuel-dealer", 25, 1, diag));
            Assert.AreEqual(20, intake.TotalFuelWoodUnitsFor("fuel-dealer"));

            var lines = intake.TryDispenseUnits("fuel-dealer", 12, 1, diag);
            Assert.IsNotNull(lines);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(10, lines[0].UnitsTaken);
            Assert.AreEqual(2, lines[1].UnitsTaken);
            Assert.AreEqual(8, intake.TotalFuelWoodUnitsFor("fuel-dealer"));
            Assert.AreEqual(1, intake.LotsFor("fuel-dealer").Count);
        }

        [Test]
        public void DemandChains_AreOrdered_AndSecondsWorkIsListedAsBlocked()
        {
            var hotel = Pass2DemandChains.HotelGuestChain();
            Assert.AreEqual(4, hotel.Count);
            Assert.AreEqual("ht.deal-with-customer", hotel[0].TaskDefinitionId);
            Assert.AreEqual("ht.refurbish-room", hotel[3].TaskDefinitionId);

            var saloon = Pass2DemandChains.SaloonGuestChain();
            Assert.AreEqual(5, saloon.Count);
            Assert.AreEqual("sa.serve-at-bar", saloon[3].TaskDefinitionId);
            Assert.AreEqual("sa.serve-at-table", saloon[4].TaskDefinitionId);

            Assert.Greater(Pass2DemandChains.Pass2SecondsBlocklist().Count, 0);
            Assert.Greater(Pass2DemandChains.RoutineTriggerGaps().Count, 0);
        }

        [Test]
        public void SequenceDay_SumsAndReportsMissed_WithoutAFixedCap()
        {
            var day = new Pass2TaskSequenceDay();
            day.AddTask("gs.order-from-supplier", 30);
            day.AddTask("fd.split-fuel", 30);
            day.AddTask("lf.eat-meal", 30);
            day.MarkCompleted("gs.order-from-supplier");
            day.MarkMissed("fd.split-fuel", "No worker available; task left in queue.");
            Assert.AreEqual(90, day.TotalPlannedMinutes());
            Assert.AreEqual(30, day.CompletedMinutes());
            Assert.AreEqual(2, day.MissedEntries().Count);

            var report = new Pass2MissedWorkReport();
            report.AddFromSequenceDay(day, "Jacob, Fuel Dealer");
            report.RecordIdleMinutes(45);
            string rendered = report.RenderConsoleReport("Day report");
            Assert.IsTrue(rendered.Contains("fd.split-fuel"));
            Assert.IsTrue(rendered.Contains("lf.eat-meal"));
            Assert.IsTrue(rendered.Contains("Idle (standing by, not missed work): 45 minutes."));
        }
    }
}
