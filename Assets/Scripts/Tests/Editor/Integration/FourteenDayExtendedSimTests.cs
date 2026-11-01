using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// Pass IV (Worker 4): the 14-day deterministic extended simulation.
    /// Several households + businesses, real employment, multiple production
    /// processes, shared equipment contention (one thresher, two farms), a
    /// delegated manager, real inventory and purchases, a deliberate mill
    /// breakdown + flour shortage, and a deliberate time conflict.
    ///
    /// Records goods in/out, waste, cash transfers, equipment occupancy,
    /// person work time, and task completions. Repeats at 1x small steps,
    /// large steps, 10x, 100x — consequential outcomes must match (same seed:
    /// there is no RNG; every decision is deterministic).
    ///
    /// Save/load at: mid-bake, mid-shipment, competing tasks, after
    /// cancellation, mid-sim — with idempotent restore.
    /// </summary>
    [TestFixture]
    public sealed class FourteenDayExtendedSimTests
    {
        private const float EndSeconds = (LivingWorldSim.StartDay + LivingWorldSim.SimDays) * 86400f;

        private static LivingWorldSim RunFull(float stepSeconds)
        {
            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceTo(EndSeconds, stepSeconds);
            return sim;
        }

        [Test]
        public void TimingStress_StepSizesAgree()
        {
            LivingWorldSim s60 = RunFull(60f);
            string h60 = s60.ComputeHash();

            Assert.AreEqual(h60, RunFull(3600f).ComputeHash(),
                "large steps diverged from 1x small steps");
            Assert.AreEqual(h60, RunFull(600f).ComputeHash(),
                "10x diverged from 1x small steps");
            Assert.AreEqual(h60, RunFull(6000f).ComputeHash(),
                "100x diverged from 1x small steps");
        }

        [Test]
        public void FullSim_Reconciles_WithExpectedStoryBeats()
        {
            LivingWorldSim sim = RunFull(60f);

            List<string> findings = sim.Reconcile();
            Assert.AreEqual(0, findings.Count,
                "reconciliation failed:\n" + string.Join("\n", findings));

            // The deliberate beats really happened.
            Assert.Greater(sim.ThresherContentionDeferrals, 0,
                "the shared thresher never contended\n" + string.Join("\n", sim.Diag));
            Assert.Greater(sim.EmploymentConflictRefusals, 0,
                "the deliberate time conflict never refused loudly");
            Assert.AreEqual(2, sim.SettledPayrollWeeks.Count,
                "expected two weekly payrolls, got " + sim.SettledPayrollWeeks.Count);
            Assert.AreEqual(0, sim.PayrollArrears.Count, "wage arrears: " + string.Join("; ", sim.PayrollArrears));
            Assert.IsFalse(sim.MillFailed, "the mill should be repaired by day 14");

            // The world stayed productive.
            Assert.Greater(sim.TotalWorkMinutes, 0, "no work happened");
            int breadSold = sim.Store1.UnitsSold.TryGetValue(LivingWorldSim.Bread, out int sb) ? sb : 0;
            Assert.Greater(breadSold, 0, "no retail bread sold in 14 days");

            Console.WriteLine("=== 14-day reconciliation figures ===");
            Console.WriteLine(sim.Summary());
            Console.WriteLine("hash: " + sim.ComputeHash());
        }

        private static LivingWorldSim RestoreFrom(LivingWorldSimSaveDto dto)
        {
            var restored = new LivingWorldSim();
            restored.BuildStatic();
            restored.LoadFromSaveDto(dto);
            return restored;
        }

        [Test]
        public void SaveLoad_MidBake_IdempotentRestore()
        {
            string baseline = RunFull(60f).ComputeHash();

            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceUntil(
                () => sim.AnyTaskInProgress(AutonomousWorkGeneratorIds.ProductionOrder),
                60f, (LivingWorldSim.StartDay + 4) * 86400f);
            Assert.IsTrue(sim.AnyTaskInProgress(AutonomousWorkGeneratorIds.ProductionOrder),
                "no bake task went InProgress");

            LivingWorldSimSaveDto dto = sim.CaptureSaveDto();

            // Idempotent restore: loading twice == loading once.
            LivingWorldSim r1 = RestoreFrom(dto);
            LivingWorldSim r2 = RestoreFrom(dto);
            Assert.AreEqual(r1.ComputeHash(), r2.ComputeHash(), "double restore diverged");

            r1.AdvanceTo(EndSeconds, 60f);
            Assert.AreEqual(baseline, r1.ComputeHash(), "mid-bake save/load changed the outcome");
        }

        [Test]
        public void SaveLoad_MidShipment_ResumesToSameOutcome()
        {
            string baseline = RunFull(60f).ComputeHash();

            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceUntil(() => sim.ShoppingDriver.ActiveTrips.Count > 0,
                60f, (LivingWorldSim.StartDay + 2) * 86400f);
            Assert.Greater(sim.ShoppingDriver.ActiveTrips.Count, 0, "no shopping trip started");

            LivingWorldSimSaveDto dto = sim.CaptureSaveDto();
            LivingWorldSim restored = RestoreFrom(dto);
            Assert.Greater(restored.ShoppingDriver.ActiveTrips.Count, 0, "trip lost across save/load");

            restored.AdvanceTo(EndSeconds, 60f);
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-shipment save/load changed the outcome");
        }

        [Test]
        public void SaveLoad_CompetingTasks_SameContentionOutcome()
        {
            string baseline = RunFull(60f).ComputeHash();

            // Day 5, 02:00: the day cycle ran at 00:05 — farm-1 holds the
            // thresher, farm-2's thresh task is deferred.
            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceTo((LivingWorldSim.StartDay + 5) * 86400f + 2 * 3600f, 60f);
            Assert.Greater(sim.ThresherContentionDeferrals, 0,
                "expected thresher contention by day 5 02:00");

            LivingWorldSimSaveDto dto = sim.CaptureSaveDto();
            LivingWorldSim restored = RestoreFrom(dto);
            restored.AdvanceTo(EndSeconds, 60f);
            Assert.AreEqual(baseline, restored.ComputeHash(), "competing-tasks save/load changed the outcome");
        }

        [Test]
        public void SaveLoad_AfterCancellation_TaskStaysCancelled()
        {
            string baseline;

            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceTo((LivingWorldSim.StartDay + 4) * 86400f + 3600f, 60f);
            Assert.IsTrue(sim.CancelFirstOpenTask(LivingWorldSim.Farm1, AutonomousWorkGeneratorIds.FieldWork),
                "no field task to cancel on day 4");

            // The uninterrupted run ALSO cancels the same task: the save/load
            // comparison must include the cancellation on both sides.
            LivingWorldSimSaveDto dto = sim.CaptureSaveDto();
            LivingWorldSim restored = RestoreFrom(dto);
            restored.AdvanceTo(EndSeconds, 60f);

            var sim2 = new LivingWorldSim();
            sim2.Build();
            sim2.AdvanceTo((LivingWorldSim.StartDay + 4) * 86400f + 3600f, 60f);
            Assert.IsTrue(sim2.CancelFirstOpenTask(LivingWorldSim.Farm1, AutonomousWorkGeneratorIds.FieldWork),
                "no field task to cancel on day 4 (second run)");
            sim2.AdvanceTo(EndSeconds, 60f);
            baseline = sim2.ComputeHash();

            Assert.AreEqual(baseline, restored.ComputeHash(),
                "after-cancellation save/load changed the outcome");
        }

        [Test]
        public void SaveLoad_MidSim_Day7_IdenticalOutcome()
        {
            string baseline = RunFull(60f).ComputeHash();

            var sim = new LivingWorldSim();
            sim.Build();
            sim.AdvanceTo((LivingWorldSim.StartDay + 7) * 86400f, 60f);

            LivingWorldSimSaveDto dto = sim.CaptureSaveDto();
            LivingWorldSim restored = RestoreFrom(dto);
            restored.AdvanceTo(EndSeconds, 60f);
            Assert.AreEqual(baseline, restored.ComputeHash(), "mid-sim save/load changed the outcome");
        }
    }

    /// <summary>Catalog ids without dragging the generator instance in.</summary>
    internal static class AutonomousWorkGeneratorIds
    {
        public const string ProductionOrder = "work.production-order";
        public const string FieldWork = "work.field-work";
    }
}
