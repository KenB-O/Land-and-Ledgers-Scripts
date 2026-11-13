using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
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
        public void StepSize_Day207RestockStartsIdentically_60sAnd6000s()
        {
            // QA-21: the pinned collision was day 207, 09:00 — P5's
            // 40-minute domestic.animal-care vs the 09:00 store restock
            // window. The collision is start-time quantization, NOT an
            // equal-due tie: the 6000s grid is anchored at day 200, so on
            // day 207 the step ends are 00:20, 02:00, 03:40, 05:20, 07:00,
            // 08:40, 10:20; the care wanted at 08:00 is first seen at 08:40
            // and completes at 09:20, after the 09:00 window opens. At 60s
            // the care completes 08:40, before the window. The restock must
            // start identically at both step sizes. End of day 207 is
            // (StartDay + 8) * 86400 (StartDay 200, day 207 is the 8th sim
            // day); under (a)+(d1) restock-shelves has started twice by then
            // at both step sizes (days 203 and 206 — there is no day-207
            // restock in this trajectory).
            float endDay207 = (LivingWorldSim.StartDay + 8) * 86400f;

            var s60 = new LivingWorldSim();
            s60.Build();
            s60.AdvanceTo(endDay207, 60f);

            var s6000 = new LivingWorldSim();
            s6000.Build();
            s6000.AdvanceTo(endDay207, 6000f);

            int started60 = CountDiag(s60, "('work.restock-shelves') started");
            int started6000 = CountDiag(s6000, "('work.restock-shelves') started");
            Assert.AreEqual(2, started60, "60s run should have started 2 restocks by end of day 207\n" + string.Join("\n", s60.Diag));
            Assert.AreEqual(started60, started6000,
                "6000s run started " + started6000 + " restocks vs " + started60 + " at 60s — restock starts diverge across step sizes\n" + string.Join("\n", s6000.Diag));
            // Assertion 3 (reworked, Plan 10b): NOT "zero refusals" —
            // refusals are legitimate at 6000s (start-time quantization:
            // a domestic task wanted at 08:00 is first seen at the 08:40
            // step end, so its 40-minute completion lands at 09:20, after
            // the 09:00 window opens). What the fix guarantees is that no
            // refusal is terminal: every refused window start is followed
            // by a retry, a disposal, or a later start of the same task.
            // Observed under (a)+(d1): 9 refusals through day 207 at
            // 6000s, all P4-occupied, each followed by a retry and a later
            // start. No exact count is asserted.
            List<string> refusedIds = RefusedWindowTaskIds(s6000);
            Assert.Greater(refusedIds.Count, 0,
                "expected at least one refused window start at 6000s — otherwise this assertion is vacuous\n" + string.Join("\n", s6000.Diag));
            foreach (string id in refusedIds)
            {
                Assert.IsTrue(HasFollowUpAfterRefusal(s6000, id),
                    "refused window task " + id + " has no later retry, disposal, or start — the refusal was terminal\n" + string.Join("\n", s6000.Diag));
            }
            Assert.AreEqual(s60.ComputeHash(), s6000.ComputeHash(),
                "day-207 hashes still diverge across step sizes");
        }

        private static int CountDiag(LivingWorldSim sim, string needle)
        {
            int n = 0;
            foreach (string line in sim.Diag)
            {
                if (line != null && line.Contains(needle)) n++;
            }

            return n;
        }

        // Plan 10b: task ids whose window start was refused, in
        // first-refusal order. Refusal line format
        // (LivingWorldSim.cs:745):
        // "LivingWorldSim: StartTask refused for <id>: <rejection>".
        private static List<string> RefusedWindowTaskIds(LivingWorldSim sim)
        {
            var ids = new List<string>();
            const string marker = "StartTask refused for ";
            foreach (string line in sim.Diag)
            {
                if (line == null) continue;
                int at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                string rest = line.Substring(at + marker.Length);
                int end = rest.IndexOf(':');
                string id = end >= 0 ? rest.Substring(0, end) : rest;
                if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
            }

            return ids;
        }

        // Plan 10b: true when the Diag contains, AFTER the first
        // refusal of taskId, a retry, a disposal, or a start line for
        // the same task. Retry (LivingWorldSim.cs:758-759): "window
        // start for <id> deferred — retry scheduled at +60s: ...".
        // Disposal (:770-771 / :775-776): "refused window task <id>
        // interrupted and re-queued ..." / "... could not be
        // interrupted (...) — cancelled: ...". Start (:798): "<id>
        // ('<definition>') started, completes at +<min> min."
        // NOTE: the retry needle contains an em-dash (U+2014) — copy
        // it verbatim from LivingWorldSim.cs, do not retype it as a
        // hyphen.
        private static bool HasFollowUpAfterRefusal(LivingWorldSim sim, string taskId)
        {
            int refusalIndex = -1;
            const string marker = "StartTask refused for ";
            for (int i = 0; i < sim.Diag.Count; i++)
            {
                string line = sim.Diag[i];
                if (line != null && line.Contains(marker + taskId + ":")) { refusalIndex = i; break; }
            }

            if (refusalIndex < 0) return false;
            for (int i = refusalIndex + 1; i < sim.Diag.Count; i++)
            {
                string line = sim.Diag[i];
                if (line == null || !line.Contains(taskId)) continue;
                if (line.Contains("deferred — retry scheduled at +60s")
                    || line.Contains("interrupted and re-queued for re-allocation")
                    || line.Contains("could not be interrupted")
                    || line.Contains("started, completes at +")) return true;
            }

            return false;
        }

        [Test]
        public void RefusedWindowStart_DoesNotLeaveAssignedTaskBlockingNextDayRestock()
        {
            // QA-21, second half (Plan 10b re-derivation): a refused
            // window start must not leave the task Assigned forever.
            // The Plan 10 target day (207) has NO restock in the
            // current (a)+(d1) trajectory — restocks start on days 203
            // (T50) and 206 (T115) only, through day 208 — so the
            // forced refusal is re-targeted to the day-203 restock
            // (T50, allocated "T50 → P5 [540-585]"). Force a refusal
            // the retry cannot heal: occupy P5 with a long task of our
            // own (manual StartTask schedules NO completion event, so
            // it stays InProgress past 09:00 and no TaskCompletionDue
            // competes with the window event). Then assert the refused
            // restock is refused (scoped to its own id), retried,
            // disposed (no longer Assigned), and a restock starts
            // again the next day (day 204, same task re-allocated
            // after interrupt+requeue, or a fresh task after cancel).
            var sim = new LivingWorldSim();
            sim.Build();

            float day203 = LivingWorldSim.StartDay + 3;
            // Day-cycle at 00:05 generates the restock and allocates it to
            // P5 in window [540, 585]; window event due 09:00.
            sim.AdvanceTo(day203 * 86400f + 400f, 60f);

            // Let the natural 08:00 animal-care finish first (starts 08:00,
            // completes 08:40 at 60s steps) so the only occupier is ours.
            sim.AdvanceTo(day203 * 86400f + 8 * 3600f + 41 * 60f, 60f);

            // The forced task: the day-203 restock, found among the
            // store's open tasks (owner 104) by definition +
            // customerRef. It must exist and be Assigned — if it is
            // absent, this day has no restock and the test's premise
            // fails (STOP, do not retarget silently).
            string forcedId = null;
            foreach (WorkTask t in sim.Tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 104)))
            {
                if (t != null && string.Equals(t.DefinitionId, "work.restock-shelves", StringComparison.Ordinal)
                    && string.Equals(t.CustomerRef, "restock:bread", StringComparison.Ordinal))
                {
                    forcedId = t.TaskId.ToString();
                }
            }

            Assert.IsTrue(forcedId != null, "no day-203 restock task exists to force-refuse — the re-derived target day has no restock");

            // Occupy P5 08:41–12:41 with a task that is NOT a restock (so it
            // cannot trip the restock dedup itself). Farm-1 owner id 102,
            // store owner id 104 (LivingWorldSim.BusinessOwnerEntity).
            EntityId occupierOwner = EntityId.For(EntityKind.Business, 102);
            WorkTask occupier = sim.Tasks.CreateTaskWithPlannedMinutes(
                "work.field-work", occupierOwner, (int)day203, 240, "qa21-occupier");
            Assert.IsTrue(occupier != null, "could not create occupier task");
            string assignRejection;
            // P5's person EntityId: the same int->EntityId conversion the
            // allocator uses (EntityId.For(EntityKind.Person, pid)).
            Assert.IsTrue(sim.Tasks.AssignTask(occupier.TaskId, EntityId.For(EntityKind.Person, LivingWorldSim.P5), sim.Budgets, out assignRejection),
                "could not assign occupier to P5: " + assignRejection);
            string startRejection;
            Assert.IsTrue(sim.Tasks.StartTask(occupier.TaskId, (int)day203, out startRejection),
                "could not start occupier: " + startRejection);

            int restockStartedBefore = CountDiag(sim, "('work.restock-shelves') started");
            // Advance past the 09:00 window event; P5 is occupied, so the
            // window start must refuse.
            sim.AdvanceTo(day203 * 86400f + 10 * 3600f, 60f);

            // Scoped to the forced window only (Plan 10b): the old
            // cumulative count passed vacuously on earlier days' P4
            // refusals. The refusal line for THIS task id must exist.
            // NOTE: the retry needle below contains an em-dash (U+2014)
            // — copy it verbatim from LivingWorldSim.cs:759, do not
            // retype it as a hyphen.
            Assert.Greater(CountDiag(sim, "StartTask refused for " + forcedId + ":"), 0,
                "expected a refused window start for the forced restock " + forcedId + " once P5 was occupied\n" + string.Join("\n", sim.Diag));
            // The (a) retry must have fired for this task (first refusal
            // → retry at due+60s), and the (d1) disposal must have
            // followed (second refusal → interrupt+requeue, or the cancel
            // fallback).
            Assert.Greater(CountDiag(sim, "window start for " + forcedId + " deferred — retry scheduled at +60s"), 0,
                "expected a retry line for the forced restock " + forcedId + "\n" + string.Join("\n", sim.Diag));
            Assert.Greater(
                CountDiag(sim, "refused window task " + forcedId + " interrupted and re-queued for re-allocation")
                + CountDiag(sim, "refused window task " + forcedId + " could not be interrupted"), 0,
                "expected a disposal line for the forced restock " + forcedId + "\n" + string.Join("\n", sim.Diag));

            // The refused restock must not be left Assigned: re-scan the
            // store's open tasks for the forced id. (Absent = cancelled
            // by the fallback, terminal — also disposed.)
            WorkTask refused = null;
            foreach (WorkTask t in sim.Tasks.GetOpenTasks(EntityId.For(EntityKind.Business, 104)))
            {
                if (t != null && string.Equals(t.TaskId.ToString(), forcedId, StringComparison.Ordinal))
                {
                    refused = t;
                }
            }

            if (refused != null)
            {
                // Interrupt+requeue leaves it Queued (still open, but
                // re-allocatable — NOT Assigned).
                Assert.IsTrue(refused.Status != TaskStatus.Assigned,
                    "refused restock task " + refused.TaskId + " is still Assigned — it will block every future restock");
            }

            // Free P5 (the manual occupier has no completion event), then run
            // the next day-cycle and assert a restock starts again on day 204
            // — the need is not blocked forever. With interrupt+requeue the
            // day-204 start is the SAME task id re-allocated; with cancel it
            // is a FRESH id. The test asserts a start happens either way.
            Assert.IsTrue(sim.Tasks.CancelTask(occupier.TaskId, sim.Budgets), "could not cancel occupier");
            sim.AdvanceTo((day203 + 1) * 86400f + 400f, 60f);
            sim.AdvanceTo((day203 + 1) * 86400f + 10 * 3600f, 60f);
            Assert.Greater(CountDiag(sim, "('work.restock-shelves') started"), restockStartedBefore,
                "no restock started on day 204 after the day-203 refusal — the refused task is still blocking generation\n" + string.Join("\n", sim.Diag));
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
