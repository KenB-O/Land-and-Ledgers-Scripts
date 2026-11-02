// Pass 4 (player depth): the player duty model, source-level only.
// Plain C# (System only), so it stays verifiable in the small Roslyn
// harness. It is a MODEL of the duty toggle, hiring offload, player
// travel, and NPC-mode interaction — not the production driver.
// Production advancement lives in SimulationDrivers (AdvancePlayerWork,
// RecordMovementProgress) and task issuance lives in PlayerDirector,
// both through the same TaskAuthority NPCs use. This model must never
// be read as a second player system: it records what the duty toggle
// MEANS, what it refuses, and what a driver must do next. Unity UI,
// avatar movement, and scene wiring are Codex/Unity AI work.
//
// Principles, from Kennedy's vision, recorded here so a later reader
// does not have to re-derive them from chat:
//   - The player does the SAME tasks NPCs do, through the same
//     TaskAuthority and the Pass 2 catalogs. No separate player logic.
//   - "Doing all daily duties" is a toggle, not micromanagement: when
//     the toggle is on, the day's duties are the player's roster and
//     he just does them, in sequence, until they are done or cannot
//     be done.
//   - Hiring offload is per-task, from unfinished-task evidence, never
//     from a headcount quota. There is no "hire N people" output here,
//     and no recommended headcount anywhere in this file.
//   - No arbitrary work cap is invented here. The roster is a
//     Pass 2 task-sequence day (a planning view). The booking
//     constraint that already exists in production is WorkTimeBudget
//     (default 600 minutes, a tuning value, NOT canon). This model
//     takes the budget as a caller-supplied number, reports planned
//     minutes against it, and never truncates the roster to fit it:
//     a task the roster cannot book is a missed/offload candidate,
//     not a task that never existed. Whether the cap survives, is
//     raised, or is replaced by pure task sequencing is still a
//     Kennedy decision (Pass 2 left the two coexisting); until it is
//     made, a driver that enforces only one of them while the other
//     still caps would be lying about one of them.
//   - Player ordering is NEVER automatic (Pass 3 guard). This model
//     delegates to Pass3OrderingSession.ValidateAutomatic() and never
//     weakens it: a player automatic order is refused, an employee
//     automatic order needs delegation permission.
//   - Seconds: Pass 2's blocklist stands. No sub-minute task is
//     authored here. Sub-minute saloon work, drink variants, and any
//     task under one minute remain blocked on Kennedy's open seconds
//     decision (small additive option vs full migration). Do NOT work
//     around that by booking them at 1 minute each.
//   - Heating: NOT implemented here. The heating audit is complete,
//     but Kennedy has not decided methods, coal unit weight/heat,
//     Dealer vs Mine retail, or device drivers. Heating is pending,
//     not guessed.
using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Ordering;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>Who owns a duty task right now. Exactly one owner, never both.</summary>
    public sealed class Pass4DutyTask
    {
        public string TaskDefinitionId = string.Empty;
        public int PlannedMinutes;
        public string Owner = "player";
        public bool Completed;
        public bool InProgress;
        public string MissedReason = string.Empty;
        public bool DutyAssigned;

        public Pass4DutyTask() { }
    }

    /// <summary>A task that stalled because it was issued while NPC mode was on.</summary>
    public sealed class Pass4StalledTaskRecord
    {
        public string TaskDefinitionId = string.Empty;
        public string Reason = string.Empty;
        public string RecommendedAction = string.Empty;

        public Pass4StalledTaskRecord() { }
    }

    /// <summary>
    /// The player duty roster: a toggle, a day's tasks, hiring offload,
    /// player travel, and NPC-mode interaction, in one place.
    ///
    /// Location and travel follow PlayerDirector's rule: travel takes
    /// real minutes, arrival changes the location, and nothing here
    /// teleports. A travel leg with no route is refused and the
    /// location does not change. A travel leg gated by NPC mode does
    /// not advance and does not arrive.
    ///
    /// Money in this model is a caller-supplied gate only (CashCents).
    /// Real money moves only through HouseholdLedger (households) and
    /// FinancialObligationAuthority (credit) in production; this model
    /// never writes a ledger, never invents cash, and never invents debt.
    /// A Codex driver must replace the CashCents gate with the real
    /// ledger check before this model drives a live game.
    /// </summary>
    public sealed class Pass4PlayerDuty
    {
        public string PlayerPersonId = string.Empty;
        public string CurrentLocationId = string.Empty;
        public int DailyBudgetMinutes;

        /// <summary>Requested duty state. Effective duty is DutyOn && !NpcMode.</summary>
        public bool DutyRequested { get; private set; }

        /// <summary>True only when the toggle is on AND NPC mode is off.</summary>
        public bool DutyOn => DutyRequested && !TreatPlayerAsNpc;

        /// <summary>Mirrors SimulationDrivers.TreatPlayerAsNpc (default false).</summary>
        public bool TreatPlayerAsNpc { get; private set; }

        public bool IsTravelling { get; private set; }
        public string TravelDestination = string.Empty;
        public int TravelMinutesTotal;
        public int TravelMinutesElapsed;
        public string CurrentTaskId = string.Empty;
        public int CashCents;

        private readonly List<Pass4DutyTask> tasks = new List<Pass4DutyTask>();
        private readonly List<Pass4DutyTask> offloaded = new List<Pass4DutyTask>();

        public IReadOnlyList<Pass4DutyTask> Tasks => tasks;
        public IReadOnlyList<Pass4DutyTask> OffloadedTasks => offloaded;

        public Pass4PlayerDuty() { }

        public Pass4PlayerDuty(string playerPersonId, string startingLocationId, int dailyBudgetMinutes)
        {
            PlayerPersonId = playerPersonId ?? string.Empty;
            CurrentLocationId = startingLocationId ?? string.Empty;
            DailyBudgetMinutes = Math.Max(1, dailyBudgetMinutes);
        }

        /// <summary>
        /// Turns the "doing all daily duties" toggle on or off. Turning it
        /// on while NPC mode is on does NOT silently enable duty work:
        /// the request is recorded, duty stays suspended, and the caller
        /// gets the reason. The roster is preserved either way.
        /// </summary>
        public bool SetDuty(bool on, List<string> diag)
        {
            DutyRequested = on;
            if (on && TreatPlayerAsNpc)
            {
                if (diag != null) diag.Add("Pass4Player: duty toggle requested, but NPC mode is on, so duty work is suspended and the roster is preserved, not advanced.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Mirrors the player-as-NPC flag. Turning NPC mode ON suspends
        /// effective duty (roster preserved, no new player advancement).
        /// The toggle itself is NOT cleared by NPC mode: turning NPC mode
        /// off makes a still-requested toggle effective again, and the
        /// console status always shows effective duty, so resumption is
        /// visible, never silent. A player who does not want duty work
        /// after NPC mode must toggle duty off deliberately.
        /// </summary>
        public void SetNpcMode(bool npcMode, List<string> diag)
        {
            bool wasOn = TreatPlayerAsNpc;
            TreatPlayerAsNpc = npcMode;
            if (npcMode && !wasOn && DutyRequested && diag != null)
                diag.Add("Pass4Player: NPC mode on suspends the duty toggle; roster preserved, player-issued travel and task advancement gated.");
            if (!npcMode && wasOn && DutyRequested && diag != null)
                diag.Add("Pass4Player: NPC mode off; the duty toggle was never turned off, so it is effective again. Toggle duty off deliberately if duty work is not wanted.");
        }

        /// <summary>Adds a duty task to today's roster. Refuses duplicates, bad minutes, and adding while duty is not effective.</summary>
        public bool AddDutyTask(string taskDefinitionId, int plannedMinutes, List<string> diag)
        {
            if (!DutyOn)
            {
                if (diag != null) diag.Add("Pass4Player: cannot add a duty task while the duty toggle is off or NPC mode is on.");
                return false;
            }
            if (string.IsNullOrEmpty(taskDefinitionId) || plannedMinutes <= 0)
            {
                if (diag != null) diag.Add("Pass4Player: a duty task needs an id and positive minutes.");
                return false;
            }
            foreach (var t in tasks)
            {
                if (string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed)
                {
                    if (diag != null) diag.Add("Pass4Player: duty task '" + taskDefinitionId + "' is already on today's roster.");
                    return false;
                }
            }
            tasks.Add(new Pass4DutyTask
            {
                TaskDefinitionId = taskDefinitionId,
                PlannedMinutes = plannedMinutes,
                Owner = "player",
                DutyAssigned = true,
            });
            return true;
        }

        public int TotalPlannedMinutes()
        {
            int total = 0;
            foreach (var t in tasks) total += Math.Max(1, t.PlannedMinutes);
            return total;
        }

        public int CompletedMinutes()
        {
            int total = 0;
            foreach (var t in tasks) if (t.Completed) total += Math.Max(1, t.PlannedMinutes);
            return total;
        }

        /// <summary>Planned minutes over the caller-supplied daily budget. Zero when within budget. Over-budget tasks are missed/offload candidates, not deleted.</summary>
        public int OverBudgetMinutes()
        {
            return Math.Max(0, TotalPlannedMinutes() - Math.Max(1, DailyBudgetMinutes));
        }

        public bool StartDutyTask(string taskDefinitionId, List<string> diag)
        {
            if (!DutyOn)
            {
                if (diag != null) diag.Add("Pass4Player: cannot start a duty task while the duty toggle is off or NPC mode is on.");
                return false;
            }
            if (IsTravelling || !string.IsNullOrEmpty(CurrentTaskId))
            {
                if (diag != null) diag.Add("Pass4Player: player is busy; finish or cancel the current activity first.");
                return false;
            }
            foreach (var t in tasks)
            {
                if (string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed)
                {
                    t.InProgress = true;
                    CurrentTaskId = taskDefinitionId;
                    return true;
                }
            }
            if (diag != null) diag.Add("Pass4Player: duty task '" + taskDefinitionId + "' is not on today's roster.");
            return false;
        }

        public bool CompleteDutyTask(string taskDefinitionId, List<string> diag)
        {
            foreach (var t in tasks)
            {
                if (string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed)
                {
                    t.Completed = true;
                    t.InProgress = false;
                    t.MissedReason = string.Empty;
                    if (string.Equals(CurrentTaskId, taskDefinitionId, StringComparison.Ordinal)) CurrentTaskId = string.Empty;
                    return true;
                }
            }
            if (diag != null) diag.Add("Pass4Player: duty task '" + taskDefinitionId + "' is not on today's roster or is already complete.");
            return false;
        }

        public bool MissDutyTask(string taskDefinitionId, string reason, List<string> diag)
        {
            foreach (var t in tasks)
            {
                if (string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed)
                {
                    t.InProgress = false;
                    t.MissedReason = reason ?? "Not completed.";
                    if (string.Equals(CurrentTaskId, taskDefinitionId, StringComparison.Ordinal)) CurrentTaskId = string.Empty;
                    return true;
                }
            }
            if (diag != null) diag.Add("Pass4Player: duty task '" + taskDefinitionId + "' is not on today's roster.");
            return false;
        }

        /// <summary>
        /// Offloads ONE unfinished player duty task to ONE employee.
        /// Evidence is per-task and unfinished-task based: the task must
        /// be on the roster, unfinished, and player-owned, and the caller
        /// must name the evidence (for example "missed yesterday" or
        /// "backlog 90 minutes"). There is no headcount argument, no
        /// quota, and no "hire N" output: offloading this task says
        /// nothing about how many employees the business needs in total.
        /// The task leaves the player roster entirely, so it is never
        /// double-counted as both player work and employee work.
        /// </summary>
        public bool OffloadTask(string taskDefinitionId, string employeeId, string evidence, List<string> diag)
        {
            if (TreatPlayerAsNpc)
            {
                if (diag != null) diag.Add("Pass4Player: cannot offload while NPC mode is on; player decisions are gated and the roster is preserved.");
                return false;
            }
            if (string.IsNullOrEmpty(employeeId))
            {
                if (diag != null) diag.Add("Pass4Player: offload needs an employee to receive the task.");
                return false;
            }
            if (string.IsNullOrEmpty(evidence))
            {
                if (diag != null) diag.Add("Pass4Player: offload needs unfinished-task evidence (for example a missed task or a backlog reason); a quota is never evidence.");
                return false;
            }
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (string.Equals(t.TaskDefinitionId, taskDefinitionId, StringComparison.Ordinal) && !t.Completed && string.Equals(t.Owner, "player", StringComparison.Ordinal))
                {
                    t.Owner = employeeId;
                    t.InProgress = false;
                    if (string.Equals(CurrentTaskId, taskDefinitionId, StringComparison.Ordinal)) CurrentTaskId = string.Empty;
                    tasks.RemoveAt(i);
                    offloaded.Add(t);
                    return true;
                }
            }
            if (diag != null) diag.Add("Pass4Player: duty task '" + taskDefinitionId + "' is not an unfinished player-owned task, so it cannot be offloaded.");
            return false;
        }

        /// <summary>
        /// Sends the player somewhere through the journey system: a route
        /// must exist, the player must not be busy, and NPC mode must be
        /// off. No route means no travel and no location change — never
        /// a teleport. Production routes come from JourneyModel.FindRoute
        /// via PlayerDirector; this model takes the route result as input.
        /// </summary>
        public bool SendTo(string destinationLocationId, bool routeFound, int routeMinutes, List<string> diag)
        {
            if (TreatPlayerAsNpc)
            {
                if (diag != null) diag.Add("Pass4Player: player travel is gated while NPC mode is on; location unchanged.");
                return false;
            }
            if (string.IsNullOrEmpty(destinationLocationId))
            {
                if (diag != null) diag.Add("Pass4Player: travel needs a destination.");
                return false;
            }
            if (IsTravelling || !string.IsNullOrEmpty(CurrentTaskId))
            {
                if (diag != null) diag.Add("Pass4Player: player is busy; finish or cancel the current activity first.");
                return false;
            }
            if (!routeFound)
            {
                if (diag != null) diag.Add("Pass4Player: no route to '" + destinationLocationId + "'; travel refused, location unchanged, no teleport.");
                return false;
            }
            if (string.Equals(destinationLocationId, CurrentLocationId, StringComparison.Ordinal) || routeMinutes <= 0)
            {
                CurrentLocationId = destinationLocationId;
                return true;
            }
            IsTravelling = true;
            TravelDestination = destinationLocationId;
            TravelMinutesTotal = routeMinutes;
            TravelMinutesElapsed = 0;
            return true;
        }

        /// <summary>Advances travel. Gated by NPC mode (no progress, no arrival). Arrival changes the location; nothing else does.</summary>
        public void AdvanceTravel(int minutes, List<string> diag)
        {
            if (!IsTravelling) return;
            if (TreatPlayerAsNpc)
            {
                if (diag != null) diag.Add("Pass4Player: travel advancement gated while NPC mode is on; player has not arrived.");
                return;
            }
            TravelMinutesElapsed += Math.Max(0, minutes);
            if (TravelMinutesElapsed >= TravelMinutesTotal)
            {
                CurrentLocationId = TravelDestination;
                IsTravelling = false;
                TravelDestination = string.Empty;
                TravelMinutesElapsed = 0;
                TravelMinutesTotal = 0;
            }
        }

        /// <summary>Cancels the current task or travel. The player stays where he is; travel does not teleport him home, and an interrupted task returns to the roster unfinished, not completed.</summary>
        public bool CancelCurrent(List<string> diag)
        {
            bool didAnything = false;
            if (!string.IsNullOrEmpty(CurrentTaskId))
            {
                foreach (var t in tasks)
                {
                    if (string.Equals(t.TaskDefinitionId, CurrentTaskId, StringComparison.Ordinal) && !t.Completed)
                    {
                        t.InProgress = false;
                        t.MissedReason = "Cancelled by player; unfinished.";
                    }
                }
                CurrentTaskId = string.Empty;
                didAnything = true;
            }
            if (IsTravelling)
            {
                IsTravelling = false;
                TravelDestination = string.Empty;
                TravelMinutesElapsed = 0;
                TravelMinutesTotal = 0;
                didAnything = true;
            }
            if (!didAnything && diag != null) diag.Add("Pass4Player: nothing to cancel.");
            return didAnything;
        }

        /// <summary>
        /// Series 2 Pass 9, additive only (all roster methods are
        /// untouched): clear-then-load restore of a saved duty
        /// snapshot. The roster, offloaded list, toggle, NPC mode,
        /// current task, travel, and model cash gate are reinstated
        /// verbatim from the Pass 9 save bundle's duty section.
        /// Two honest limits, both diagnosed, never hidden:
        /// a current task that is not an unfinished roster task is
        /// dropped (the player is not busy with a task that does
        /// not exist), and travel requested while a task is current
        /// is dropped (the model forbids being busy twice) — the
        /// player stays at the saved location, never teleported.
        /// Restoring the same snapshot twice yields the same
        /// roster (idempotent). This restores the MODEL state;
        /// TaskAuthority WorkTasks are the binding's job (see
        /// Series2PlayerDutyBinding), not this method's — a driver
        /// that restores duty without re-issuing through the
        /// binding has a roster with no authority tasks, and must
        /// say so, not pretend otherwise.
        /// </summary>
        public void RestoreSnapshot(
            string locationId, int dailyBudgetMinutes, bool dutyRequested, bool npcMode,
            string currentTaskId, int cashCents,
            string travelDestination, int travelMinutesTotal, int travelMinutesElapsed,
            List<Pass4DutyTask> restoredTasks, List<Pass4DutyTask> restoredOffloaded,
            List<string> diag)
        {
            tasks.Clear();
            offloaded.Clear();
            if (restoredTasks != null)
            {
                foreach (Pass4DutyTask t in restoredTasks)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.TaskDefinitionId)) continue;
                    tasks.Add(new Pass4DutyTask
                    {
                        TaskDefinitionId = t.TaskDefinitionId,
                        PlannedMinutes = Math.Max(1, t.PlannedMinutes),
                        Owner = string.IsNullOrWhiteSpace(t.Owner) ? "player" : t.Owner,
                        Completed = t.Completed,
                        InProgress = t.InProgress && !t.Completed,
                        MissedReason = t.MissedReason ?? string.Empty,
                        DutyAssigned = t.DutyAssigned,
                    });
                }
            }

            if (restoredOffloaded != null)
            {
                foreach (Pass4DutyTask t in restoredOffloaded)
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.TaskDefinitionId)) continue;
                    offloaded.Add(new Pass4DutyTask
                    {
                        TaskDefinitionId = t.TaskDefinitionId,
                        PlannedMinutes = Math.Max(1, t.PlannedMinutes),
                        Owner = string.IsNullOrWhiteSpace(t.Owner) ? "employee" : t.Owner,
                        Completed = t.Completed,
                        InProgress = false,
                        MissedReason = t.MissedReason ?? string.Empty,
                        DutyAssigned = t.DutyAssigned,
                    });
                }
            }

            CurrentLocationId = locationId ?? string.Empty;
            DailyBudgetMinutes = Math.Max(1, dailyBudgetMinutes);
            DutyRequested = dutyRequested;
            TreatPlayerAsNpc = npcMode;
            CashCents = cashCents;

            CurrentTaskId = string.Empty;
            if (!string.IsNullOrWhiteSpace(currentTaskId))
            {
                bool foundCurrent = false;
                foreach (Pass4DutyTask t in tasks)
                {
                    if (string.Equals(t.TaskDefinitionId, currentTaskId, System.StringComparison.Ordinal) && !t.Completed && t.InProgress)
                    {
                        foundCurrent = true;
                        break;
                    }
                }

                if (foundCurrent)
                {
                    CurrentTaskId = currentTaskId;
                }
                else if (diag != null)
                {
                    diag.Add($"Pass4Player: saved current task '{currentTaskId}' is not an unfinished in-progress roster task after restore; the player is not busy, never busy with a phantom task.");
                }
            }

            IsTravelling = false;
            TravelDestination = string.Empty;
            TravelMinutesTotal = 0;
            TravelMinutesElapsed = 0;
            if (!string.IsNullOrWhiteSpace(travelDestination) && travelMinutesTotal > 0 && travelMinutesElapsed < travelMinutesTotal)
            {
                if (!string.IsNullOrEmpty(CurrentTaskId))
                {
                    if (diag != null) diag.Add("Pass4Player: saved travel dropped on restore because a task is current; the model forbids being busy twice. The player stays at the saved location.");
                }
                else
                {
                    IsTravelling = true;
                    TravelDestination = travelDestination;
                    TravelMinutesTotal = travelMinutesTotal;
                    TravelMinutesElapsed = Math.Max(0, travelMinutesElapsed);
                }
            }
            else if (!string.IsNullOrWhiteSpace(travelDestination) && diag != null)
            {
                diag.Add("Pass4Player: saved travel was already complete or had no route minutes; the player is at the saved location, not teleported to the destination.");
            }
        }

        /// <summary>
        /// The Pass 1 stall, addressed honestly, not hidden: a task issued
        /// while NPC mode is on stalls InProgress, because autonomous
        /// drivers do not convert player tasks into NPC work (see
        /// PlayerNpcPlaytestPolicy). This model NEVER auto-converts it.
        /// The only honest outcomes are cancel it, or turn NPC mode off
        /// and re-issue it deliberately.
        /// </summary>
        public Pass4StalledTaskRecord ResolveStalledTask(string taskDefinitionId)
        {
            return new Pass4StalledTaskRecord
            {
                TaskDefinitionId = taskDefinitionId ?? string.Empty,
                Reason = "Issued while NPC mode was on: player-issued advancement is gated, and autonomous drivers do not convert a player task into NPC work, so it stalls InProgress.",
                RecommendedAction = "Cancel the player task, or turn NPC mode off and re-issue it deliberately. Do not auto-convert it to NPC work.",
            };
        }

        /// <summary>Pass 3 ordering guard, unchanged: player ordering is never automatic.</summary>
        public string ValidateOrdering(Pass3OrderingSession session)
        {
            if (session == null) return "Pass4Player: no ordering session supplied; order refused.";
            return session.ValidateAutomatic();
        }

        /// <summary>Builds a Pass 2 sequence day from this roster, for the single missed-work source of truth.</summary>
        public Pass2TaskSequenceDay BuildSequenceDay()
        {
            var day = new Pass2TaskSequenceDay();
            foreach (var t in tasks)
            {
                day.AddTask(t.TaskDefinitionId, t.PlannedMinutes);
                if (t.Completed) day.MarkCompleted(t.TaskDefinitionId);
                else if (!string.IsNullOrEmpty(t.MissedReason)) day.MarkMissed(t.TaskDefinitionId, t.MissedReason);
            }
            return day;
        }

        /// <summary>Pass 2 missed-work report for this player roster. Offloaded tasks are NOT missed: they were reassigned, not lost.</summary>
        public Pass2MissedWorkReport BuildMissedWorkReport(int idleMinutes)
        {
            var report = new Pass2MissedWorkReport();
            report.AddFromSequenceDay(BuildSequenceDay(), "player");
            report.RecordIdleMinutes(idleMinutes);
            return report;
        }

        /// <summary>Console-first status: what the duty toggle is, where the player is, what is done, missed, over budget, or offloaded. No UI is claimed.</summary>
        public string RenderConsoleStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Player duty status");
            sb.AppendLine("  Duty requested: " + (DutyRequested ? "on" : "off") + "; NPC mode: " + (TreatPlayerAsNpc ? "on" : "off") + "; duty effective: " + (DutyOn ? "on" : "off") + ".");
            sb.AppendLine("  Location: " + CurrentLocationId + (IsTravelling ? " (travelling to " + TravelDestination + ", " + TravelMinutesElapsed + "/" + TravelMinutesTotal + " minutes)" : "") + ".");
            sb.AppendLine("  Current task: " + (string.IsNullOrEmpty(CurrentTaskId) ? "none" : CurrentTaskId) + ".");
            sb.AppendLine("  Roster: " + tasks.Count + " task(s), planned " + TotalPlannedMinutes() + " minutes, completed " + CompletedMinutes() + " minutes, over budget " + OverBudgetMinutes() + " minutes (budget " + DailyBudgetMinutes + ", caller-supplied, not canon).");
            sb.AppendLine("  Offloaded to employees: " + offloaded.Count + " task(s), never double-counted as player work.");
            return sb.ToString();
        }
    }
}
