using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass IX WS3 (PASS8-001 / A4-06, A4-07, A4-10, A4-19): the production
    /// mine cycle driver. Ore is produced ONLY from completed break-ore
    /// tasks (worked minutes → lots with provenance → stockpile), the
    /// reconciled weekly path can never double-produce or double-deplete,
    /// and ore reaches its buyer through a declared smelter shipment with
    /// a counterparty record — paid on delivery, never instantly.
    /// </summary>
    [TestFixture]
    public sealed class MineProductionDriverTests
    {
        private const string MineBusinessId = "biz-mine-1";
        private const int Day = 400;

        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public MineRuntimeState Mine;
            public MineLevel Level;
            public EntityId Miner;
            public BusinessInstanceState Business;
            public List<string> Diag = new List<string>();
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "field " + name);
            field.SetValue(target, value);
        }

        private static Fixture NewFixture()
        {
            var f = new Fixture();
            f.Budgets.EnsureDay(Day);
            MineLaborRegister.MineLaborTaskCatalog.Register(f.Tasks, f.Diag);

            f.Mine = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            MineShaft shaft = f.Mine.ShaftPlan.PlanShaft("No. 1 Shaft", MineralResourceKind.Silver, 120, null);
            f.Level = f.Mine.ShaftPlan.PlanLevel(shaft.ShaftId, 1, 100, null);
            f.Level.SetWorkingStatus(MineWorkingStatus.InOre);
            f.Miner = f.Ids.Allocate(EntityKind.Person);
            Assert.IsNotNull(f.Mine.LaborRegister.AssignMiner(
                f.Ids.Allocate(EntityKind.EmploymentRelationship), f.Miner,
                MineRole.HardRockMiner, MineShift.Day, 200, Day, f.Diag));
            Assert.IsNotNull(f.Mine.HoistRegister.InstallHoist(HoistKind.SteamHoist, shaft.ShaftId, Day, f.Diag));

            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(MineBusinessId);
            runtime.EnsureCategoryStock(f.Mine.StockpileCategoryId, 0, 36);
            f.Business = new BusinessInstanceState();
            SetField(f.Business, "instanceId", MineBusinessId);
            SetField(f.Business, "businessType", BusinessType.Mine);
            SetField(f.Business, "runtimeState", runtime);
            f.Business.SetMineState(f.Mine);
            return f;
        }

        /// <summary>Creates and completes a break-ore task for the fixture's miner.</summary>
        private static WorkTask CompleteBreakOreTask(Fixture f, int plannedMinutes, TaskOutcome outcome = TaskOutcome.Success)
        {
            EntityId owner = MineProductionDriver.BusinessEntityIdFor(MineBusinessId);
            WorkTask task = f.Tasks.CreateTaskWithPlannedMinutes(
                MineLaborRegister.MineLaborTaskCatalog.BreakOreTaskId, owner, Day,
                plannedMinutes, f.Level.LevelId, "grade:2.5");
            Assert.IsTrue(f.Tasks.AssignTask(task.TaskId, f.Miner, f.Budgets, out string assignReason), assignReason);
            Assert.IsTrue(f.Tasks.StartTask(task.TaskId, Day, out string startReason), startReason);
            int guard = 0;
            while (task.MinutesRemaining > 0 && guard++ < 50)
            {
                Assert.IsTrue(f.Tasks.RecordWorkProgress(task.TaskId, 60, f.Budgets, Day,
                    out bool done, out string workReason), workReason);
                if (done) break;
            }
            Assert.IsTrue(f.Tasks.CompleteTask(task.TaskId, outcome, Day, f.Budgets));
            return task;
        }

        [Test]
        public void EndToEnd_RealShift_OreLotsWithProvenance_ThenCounterpartySaleOnDelivery()
        {
            Fixture f = NewFixture();
            var driver = new MineProductionDriver();
            float richnessBefore = f.Mine.RemainingRichness01;

            CompleteBreakOreTask(f, 240); // 240 worked minutes = 2 tons
            MineProductionDriver.MineDriveResult drive = driver.DriveCompletedBreakOreTasks(
                f.Mine, MineBusinessId, f.Tasks, f.Ids, Day, f.Diag);

            Assert.AreEqual(1, drive.TasksConsumed, string.Join("; ", f.Diag));
            Assert.AreEqual(2, drive.TonsBroken);
            Assert.AreEqual(1, drive.Lots.Count);
            MineOreLot lot = drive.Lots[0];
            Assert.AreEqual(f.Level.ShaftId, lot.ShaftId, "lot provenance: shaft");
            Assert.AreEqual(f.Level.LevelId, lot.LevelId, "lot provenance: level");
            Assert.AreEqual(f.Level.OnVeinId, lot.VeinId, "lot provenance: vein");
            Assert.AreEqual(2, f.Mine.OreStock.TotalTons, "ore sits in mine inventory as lots");
            Assert.AreEqual(
                richnessBefore - 2 * MineProductionCycle.ExtractionDepletionPerTon01,
                f.Mine.RemainingRichness01, 1e-6f, "depleted exactly per real ton broken");
            Assert.AreEqual(0, f.Business.RuntimeState.GetCategoryStock(f.Mine.StockpileCategoryId).CurrentStockUnits,
                "no bare category units are conjured beside the lots");

            // Sale: through the declared smelter link, with a counterparty
            // record — and NO cash until the transit elapses (A4-10).
            var cash = new HouseholdLedger(70);
            MineOreShipmentOrder order = driver.DispatchStockpileToSmelter(
                f.Mine, MineBusinessId, "Silver King", Day, f.Diag);
            Assert.IsNotNull(order, string.Join("; ", f.Diag));
            Assert.AreEqual("Omaha Smelting Works", order.SmelterName, "named counterparty on the order");
            Assert.AreEqual(2, order.Tons);
            Assert.AreEqual(0, f.Mine.OreStock.TotalTons, "shipped tons left the stockpile into the order");
            Assert.AreEqual(0, cash.GetBalanceCents(), "no instant buyerless sale");

            Assert.AreEqual(0, driver.SettleDueShipments(f.Mine, cash, Day + 1, f.Diag),
                "in transit: the smelter has not paid yet");
            Assert.IsFalse(order.IsDelivered);

            int paid = driver.SettleDueShipments(f.Mine, cash, order.ExpectedArrivalDayIndex, f.Diag);
            Assert.AreEqual(2 * 1900, paid, "smelter pays the declared per-ton price on delivery");
            Assert.AreEqual(2 * 1900, cash.GetBalanceCents());
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(0, driver.SettleDueShipments(f.Mine, cash, order.ExpectedArrivalDayIndex + 30, f.Diag),
                "settlement is exactly-once");
        }

        [Test]
        public void DoubleDepletionRegression_WeeklyDriveTwice_DepletesOnce_ProducesOnce()
        {
            Fixture f = NewFixture();
            var driver = new MineProductionDriver();
            float richnessBefore = f.Mine.RemainingRichness01;
            int cashBefore = f.Business.RuntimeState.CurrentCashCents;

            CompleteBreakOreTask(f, 240);
            MineProductionDriver.MineWeeklyResult week1 = driver.DriveWeekly(
                f.Business, f.Tasks, f.Ids, Day, f.Diag);
            Assert.AreEqual(2, week1.TonsBroken, string.Join("; ", f.Diag));
            float richnessAfterWeek1 = f.Mine.RemainingRichness01;
            Assert.AreEqual(
                richnessBefore - 2 * MineProductionCycle.ExtractionDepletionPerTon01,
                richnessAfterWeek1, 1e-6f, "one drive depletes once");
            Assert.AreEqual(0, week1.SettledCents, "the week's ore is in transit, not sold instantly");
            Assert.AreEqual(cashBefore, f.Business.RuntimeState.CurrentCashCents,
                "no revenue is credited at production time");

            // The reconciled weekly path runs again (the old abstract path
            // would have produced and depleted AGAIN from crew counts).
            MineProductionDriver.MineWeeklyResult week2 = driver.DriveWeekly(
                f.Business, f.Tasks, f.Ids, Day, f.Diag);
            Assert.AreEqual(0, week2.TonsBroken, "the completed task was consumed exactly once");
            Assert.AreEqual(richnessAfterWeek1, f.Mine.RemainingRichness01, 1e-6f,
                "no double depletion from a second weekly resolution");
            Assert.AreEqual(0, f.Business.RuntimeState.GetCategoryStock(f.Mine.StockpileCategoryId).CurrentStockUnits,
                "the abstract path adds no category-stock ore");

            // After transit, the delivery settles exactly once through the runtime.
            MineOreShipmentOrder order = f.Mine.ShipmentOrders[0];
            MineProductionDriver.MineWeeklyResult week3 = driver.DriveWeekly(
                f.Business, f.Tasks, f.Ids, order.ExpectedArrivalDayIndex, f.Diag);
            Assert.AreEqual(2 * 1900, week3.SettledCents);
            Assert.AreEqual(cashBefore + 2 * 1900, f.Business.RuntimeState.CurrentCashCents);
            MineProductionDriver.MineWeeklyResult week4 = driver.DriveWeekly(
                f.Business, f.Tasks, f.Ids, order.ExpectedArrivalDayIndex + 7, f.Diag);
            Assert.AreEqual(0, week4.SettledCents, "delivered orders never settle twice");
            Assert.AreEqual(richnessAfterWeek1, f.Mine.RemainingRichness01, 1e-6f,
                "richness is untouched by settlement weeks");
        }

        [Test]
        public void NoCompletedTasks_WeeklyDrive_ProducesNothing_DepletesNothing()
        {
            Fixture f = NewFixture();
            var driver = new MineProductionDriver();
            float richnessBefore = f.Mine.RemainingRichness01;

            MineProductionDriver.MineWeeklyResult result = driver.DriveWeekly(
                f.Business, f.Tasks, f.Ids, Day, f.Diag);
            Assert.AreEqual(0, result.TonsBroken);
            Assert.AreEqual(0, f.Mine.OreStock.TotalTons);
            Assert.AreEqual(richnessBefore, f.Mine.RemainingRichness01, 1e-6f,
                "staffing counts alone no longer produce or deplete (A4-08/A4-19)");

            // Even with no TaskAuthority at all, the drive is a pure report.
            MineProductionDriver.MineWeeklyResult noTasks = driver.DriveWeekly(
                f.Business, null, f.Ids, Day, f.Diag);
            Assert.AreEqual(0, noTasks.TonsBroken);
            Assert.AreEqual(richnessBefore, f.Mine.RemainingRichness01, 1e-6f);
        }

        [Test]
        public void FailedBreakOreTask_CreditsNoOre()
        {
            Fixture f = NewFixture();
            var driver = new MineProductionDriver();
            CompleteBreakOreTask(f, 240, TaskOutcome.SeriousFailure);

            MineProductionDriver.MineDriveResult drive = driver.DriveCompletedBreakOreTasks(
                f.Mine, MineBusinessId, f.Tasks, f.Ids, Day, f.Diag);
            Assert.AreEqual(0, drive.TonsBroken, "failed work broke no ore");
            Assert.AreEqual(0, f.Mine.OreStock.TotalTons);
        }

        [Test]
        public void ConsumedSet_SaveLoad_DoesNotReproduce()
        {
            Fixture f = NewFixture();
            var driver = new MineProductionDriver();
            CompleteBreakOreTask(f, 240);
            Assert.AreEqual(2, driver.DriveCompletedBreakOreTasks(
                f.Mine, MineBusinessId, f.Tasks, f.Ids, Day, f.Diag).TonsBroken);

            var restored = new MineProductionDriver();
            restored.LoadFromSaveDto(driver.CaptureSaveDto());
            MineProductionDriver.MineDriveResult again = restored.DriveCompletedBreakOreTasks(
                f.Mine, MineBusinessId, f.Tasks, f.Ids, Day, f.Diag);
            Assert.AreEqual(0, again.TonsBroken, "a restored driver never re-credits a consumed shift");
            Assert.AreEqual(2, f.Mine.OreStock.TotalTons, "stockpile unchanged by the restore drive");
        }
    }
}
