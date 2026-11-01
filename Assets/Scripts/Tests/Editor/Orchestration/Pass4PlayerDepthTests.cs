using System.Collections.Generic;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Ordering;
using LandLedgers.Orchestration.Player;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass 4 (player depth): Roslyn tests for the new player surface
    /// only. Existing systems are not re-tested here; their own suites
    /// cover them. All prices, minutes, and cash figures are test
    /// parameters, never canon. Durations used here follow existing
    /// code (eat 30, sleep 540, budget 600 as a caller-supplied
    /// default), not Kennedy's example guesses.
    /// </summary>
    [TestFixture]
    public sealed class Pass4PlayerDepthTests
    {
        private sealed class FixedRoute : IPass4RouteProvider
        {
            public bool Found = true;
            public int Minutes = 20;
            public bool TryRoute(string from, string to, out int routeMinutes, out string reason)
            {
                routeMinutes = Found ? Minutes : 0;
                reason = Found ? null : "No route in test provider.";
                return Found;
            }
        }

        private sealed class TestSeller : IPass4FoodSeller
        {
            public string LocationId { get; set; }
            public bool IsOpen { get; set; }
            public bool HasFood { get; set; }
            public int FoodPriceCents { get; set; }
            public bool OffersTradeCredit { get; set; }
            public int TradeCreditLimitCents { get; set; }
        }

        private sealed class TestLodging : IPass4Lodging
        {
            public string LocationId { get; set; }
            public bool IsAvailable { get; set; }
            public int NightPriceCents { get; set; }
            public bool OffersTradeCredit { get; set; }
            public int TradeCreditLimitCents { get; set; }
        }

        private static Pass4PlayerDuty Duty()
        {
            var duty = new Pass4PlayerDuty("player-1", "home", 600);
            var diag = new List<string>();
            Assert.IsTrue(duty.SetDuty(true, diag));
            return duty;
        }

        [Test]
        public void Duty_Roster_Sequence_OverBudget_Is_Reported_Not_Truncated()
        {
            var duty = Duty();
            var diag = new List<string>();
            Assert.IsTrue(duty.AddDutyTask("gs.order-from-supplier", 30, diag));
            Assert.IsTrue(duty.AddDutyTask("fd.fell-tree", 90, diag));
            Assert.IsTrue(duty.AddDutyTask("lf.eat-meal", 30, diag));
            Assert.IsFalse(duty.AddDutyTask("gs.order-from-supplier", 30, diag)); // duplicate open task
            Assert.AreEqual(150, duty.TotalPlannedMinutes());
            Assert.AreEqual(0, duty.OverBudgetMinutes());
            Assert.IsTrue(duty.AddDutyTask("lf.sleep", 540, diag));
            Assert.AreEqual(690, duty.TotalPlannedMinutes());
            Assert.AreEqual(90, duty.OverBudgetMinutes()); // reported, roster intact
            Assert.AreEqual(4, duty.Tasks.Count);
            Assert.IsTrue(duty.StartDutyTask("gs.order-from-supplier", diag));
            Assert.IsTrue(duty.CompleteDutyTask("gs.order-from-supplier", diag));
            Assert.IsTrue(duty.CompleteDutyTask("lf.eat-meal", diag));
            Assert.IsTrue(duty.CompleteDutyTask("lf.sleep", diag));
            Assert.IsTrue(duty.MissDutyTask("fd.fell-tree", "Ran out of day.", diag));
            var report = duty.BuildMissedWorkReport(0);
            Assert.AreEqual(1, report.Items.Count);
            Assert.AreEqual("fd.fell-tree", report.Items[0].TaskDefinitionId);
        }

        [Test]
        public void Offload_Moves_One_Unfinished_Task_And_Never_Double_Counts()
        {
            var duty = Duty();
            var diag = new List<string>();
            Assert.IsTrue(duty.AddDutyTask("fd.split-fuel", 30, diag));
            Assert.IsFalse(duty.OffloadTask("fd.split-fuel", "emp-1", "", diag)); // no evidence
            Assert.IsFalse(duty.OffloadTask("fd.split-fuel", "", "backlog 30 minutes", diag)); // no employee
            Assert.IsTrue(duty.OffloadTask("fd.split-fuel", "emp-1", "backlog 30 minutes", diag));
            Assert.AreEqual(0, duty.Tasks.Count);
            Assert.AreEqual(1, duty.OffloadedTasks.Count);
            Assert.AreEqual("emp-1", duty.OffloadedTasks[0].Owner);
            Assert.IsFalse(duty.OffloadTask("fd.split-fuel", "emp-2", "backlog", diag)); // no longer player-owned
            var report = duty.BuildMissedWorkReport(0);
            Assert.AreEqual(0, report.Items.Count); // offloaded is reassigned, not missed
        }

        [Test]
        public void Npc_Mode_Suspends_Duty_And_Stalled_Task_Is_Never_Auto_Converted()
        {
            var duty = Duty();
            var diag = new List<string>();
            Assert.IsTrue(duty.AddDutyTask("gs.bookkeeping", 15, diag));
            duty.SetNpcMode(true, diag);
            Assert.IsFalse(duty.DutyOn);
            Assert.IsFalse(duty.AddDutyTask("gs.handle-payment", 2, diag));
            Assert.IsFalse(duty.SendTo("bakery", true, 10, diag));
            var stall = duty.ResolveStalledTask("gs.bookkeeping");
            Assert.IsTrue(stall.RecommendedAction.Contains("Cancel"));
            // Wording check, corrected: the recommendation says "Do not
            // auto-convert", so it must contain that refusal, and must
            // not promise an auto-conversion.
            Assert.IsTrue(stall.RecommendedAction.Contains("Do not auto-convert"));
            Assert.IsFalse(stall.RecommendedAction.Contains("will be auto-converted"));
            duty.SetNpcMode(false, diag);
            Assert.IsTrue(duty.DutyOn); // toggle was never turned off, resumption is visible in status
            Assert.IsTrue(duty.RenderConsoleStatus().Contains("duty effective: on"));
        }

        [Test]
        public void Travel_Requires_A_Route_And_Arrives_Only_On_Completion()
        {
            var duty = Duty();
            var diag = new List<string>();
            Assert.IsFalse(duty.SendTo("bakery", false, 0, diag));
            Assert.AreEqual("home", duty.CurrentLocationId);
            Assert.IsTrue(duty.SendTo("bakery", true, 20, diag));
            Assert.IsTrue(duty.IsTravelling);
            Assert.AreEqual("home", duty.CurrentLocationId);
            duty.AdvanceTravel(10, diag);
            Assert.IsTrue(duty.IsTravelling);
            duty.AdvanceTravel(10, diag);
            Assert.IsFalse(duty.IsTravelling);
            Assert.AreEqual("bakery", duty.CurrentLocationId);
            Assert.IsTrue(duty.SendTo("bakery", true, 0, diag)); // same place, no travel
        }

        [Test]
        public void Ordering_Guard_Is_Not_Weakened_By_The_Duty_Model()
        {
            var duty = Duty();
            var playerAuto = new Pass3OrderingSession { BusinessId = "store", Actor = Pass3ActorKind.Player, IsAutomatic = true, DelegationAllowed = false };
            Assert.IsNotNull(duty.ValidateOrdering(playerAuto));
            var employeeAuto = new Pass3OrderingSession { BusinessId = "store", Actor = Pass3ActorKind.EmployeeDelegate, IsAutomatic = true, DelegationAllowed = true };
            Assert.IsNull(duty.ValidateOrdering(employeeAuto));
            var playerManual = new Pass3OrderingSession { BusinessId = "store", Actor = Pass3ActorKind.Player, IsAutomatic = false, DelegationAllowed = false };
            Assert.IsNull(duty.ValidateOrdering(playerManual));
            Assert.IsNotNull(duty.ValidateOrdering(null));
        }

        [Test]
        public void AutoEat_Happy_Path_Pays_And_Arrives_At_Seller()
        {
            var eat = new Pass4AutoEat("home", 100);
            var diag = new List<string>();
            eat.SetToggle(true, diag);
            Assert.IsTrue(eat.Start("Hungry at midday.", diag));
            var seller = new TestSeller { LocationId = "bakery", IsOpen = true, HasFood = true, FoodPriceCents = 8, OffersTradeCredit = false, TradeCreditLimitCents = 0 };
            Assert.IsTrue(eat.StepFindSeller(seller, diag));
            Assert.IsTrue(eat.StepTravel(new FixedRoute(), diag));
            Assert.AreEqual("bakery", eat.CurrentLocationId);
            Assert.IsTrue(eat.StepEat(diag));
            Assert.AreEqual(Pass4AutoEatState.Done, eat.State);
            Assert.AreEqual(92, eat.CashCents);
            Assert.AreEqual(30, Pass4AutoEat.EatMinutes);
        }

        [Test]
        public void AutoEat_Failures_Leave_Known_State_And_Conjure_Nothing()
        {
            var diag = new List<string>();
            var noSeller = new Pass4AutoEat("home", 100);
            noSeller.SetToggle(true, diag);
            Assert.IsTrue(noSeller.Start("Hungry.", diag));
            Assert.IsFalse(noSeller.StepFindSeller(null, diag));
            Assert.AreEqual(Pass4AutoEatState.Failed, noSeller.State);
            Assert.AreEqual("home", noSeller.CurrentLocationId);

            var noRoute = new Pass4AutoEat("home", 100);
            noRoute.SetToggle(true, diag);
            Assert.IsTrue(noRoute.Start("Hungry.", diag));
            var seller = new TestSeller { LocationId = "bakery", IsOpen = true, HasFood = true, FoodPriceCents = 8 };
            Assert.IsTrue(noRoute.StepFindSeller(seller, diag));
            Assert.IsFalse(noRoute.StepTravel(new FixedRoute { Found = false }, diag));
            Assert.AreEqual("home", noRoute.CurrentLocationId); // no teleport

            var broke = new Pass4AutoEat("home", 5);
            broke.SetToggle(true, diag);
            Assert.IsTrue(broke.Start("Hungry.", diag));
            Assert.IsTrue(broke.StepFindSeller(seller, diag));
            Assert.IsTrue(broke.StepTravel(new FixedRoute(), diag));
            Assert.IsFalse(broke.StepEat(diag)); // 5c < 8c, no credit offered
            Assert.AreEqual(Pass4AutoEatState.Failed, broke.State);
            Assert.AreEqual("bakery", broke.CurrentLocationId); // stayed at seller, not home
            Assert.AreEqual(5, broke.CashCents); // nothing charged
        }

        [Test]
        public void AutoSleep_Happy_Path_And_No_Lodging_Failure()
        {
            var diag = new List<string>();
            var sleep = new Pass4AutoSleep("field", 200);
            sleep.SetToggle(true, diag);
            Assert.IsTrue(sleep.Start("Night falling.", diag));
            var lodging = new TestLodging { LocationId = "boarding-house", IsAvailable = true, NightPriceCents = 50 };
            Assert.IsTrue(sleep.StepFindLodging(lodging, diag));
            Assert.IsTrue(sleep.StepTravel(new FixedRoute(), diag));
            Assert.AreEqual("boarding-house", sleep.CurrentLocationId);
            Assert.IsTrue(sleep.StepSleep(diag));
            Assert.AreEqual(Pass4AutoSleepState.Done, sleep.State);
            Assert.AreEqual(150, sleep.CashCents);
            Assert.AreEqual(540, Pass4AutoSleep.SleepMinutes);

            var stranded = new Pass4AutoSleep("field", 200);
            stranded.SetToggle(true, diag);
            Assert.IsTrue(stranded.Start("Night falling.", diag));
            Assert.IsFalse(stranded.StepFindLodging(null, diag));
            Assert.AreEqual(Pass4AutoSleepState.Failed, stranded.State);
            Assert.AreEqual("field", stranded.CurrentLocationId); // no bed conjured, no teleport
        }

        [Test]
        public void Player_Messages_Come_From_The_Pass2_Report_Not_Invention()
        {
            var duty = Duty();
            var diag = new List<string>();
            Assert.IsTrue(duty.AddDutyTask("fd.haul-log", 60, diag));
            Assert.IsTrue(duty.MissDutyTask("fd.haul-log", "Ran out of day.", diag));
            var report = duty.BuildMissedWorkReport(30);
            var messages = Pass4PlayerMessages.BuildFromReport(report, "player");
            Assert.AreEqual(2, messages.Count); // 1 missed + 1 idle-standing-by line
            Assert.IsTrue(messages[0].Text.Contains("fd.haul-log"));
            Assert.IsTrue(messages[1].Text.Contains("standing by"));
            var offload = Pass4PlayerMessages.BuildOffloadMessage("fd.haul-log", "emp-1");
            Assert.IsTrue(offload.Text.Contains("no longer the player's task"));
            string rendered = Pass4PlayerMessages.RenderConsoleLines(messages, "Messages");
            Assert.IsTrue(rendered.Contains("fd.haul-log"));
        }

        [Test]
        public void Seconds_Blocklist_Is_Not_Worked_Around_In_Pass4()
        {
            // Pass 4 authors no sub-minute task definition. The only
            // life durations here are whole-minute existing-code values.
            Assert.AreEqual(30, Pass4AutoEat.EatMinutes);
            Assert.AreEqual(540, Pass4AutoSleep.SleepMinutes);
            Assert.IsTrue(Pass2DemandChains.Pass2SecondsBlocklist().Count > 0);
        }
    }
}
