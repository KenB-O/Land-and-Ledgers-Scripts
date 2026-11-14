// R17 (note 111 §2, plan reviewed in note 114): the shared firing core.
//
// Both fire paths (SharedBusinessRuntimeManager.TryFireWorkerFromSlot and
// GeneralStoreRuntimeManager.TryFireWorkerFromSlot) are harness-excluded,
// so the four ordered steps — ensure, settle, end, report — live here in
// one harness-compiled helper both managers call, instead of duplicated
// in two files that would drift. The helper changes no ruled behavior:
// both paths still end the record and settle the wage through the same
// calls the ruling names.
//
// Order (note 114 Q2/Q3, concretizing the ruling's literal step order):
// the end is attempted BEFORE slot.Unassign(), because ruling step 3
// requires a failed end to leave the slot filled and there is no rollback
// API (WorkerSlotState.Assign exists but resets paid state and cannot
// undo ResetPersonEmployment — do NOT implement failure recovery via
// re-Assign). The settlement runs BEFORE the end, because PayAgreedWage's
// shortfall branch unconditionally suspends the record: end-first would
// un-end the record on a cash shortfall and lose the dismissal history.
// On shortfall the record stays Suspended with the liability preserved,
// the worker is still hireable (Suspended is not Active), and the firing
// still proceeds — that corner is accepted and disclosed, not hidden.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Ends the employment record and settles the final wage when a worker
    /// is fired. Returns "ended" when at least one record was ended,
    /// "ended-with-shortfall" when the settlement shortfalled (record left
    /// Suspended with the liability preserved), "no-record" when no Active
    /// record exists for the person at this business (the caller proceeds
    /// with the slot and person reset, no wage invented), or a refusal
    /// string when the firing must not proceed (registry or state
    /// unavailable — the caller refuses loudly and the slot stays filled).
    /// A settlement refusal never blocks the firing, only the registry
    /// failure does.
    /// </summary>
    public static class EmploymentFiringService
    {
        public static string EndEmploymentForFiring(
            BusinessRuntimeState state,
            EmploymentRelationshipRegistry registry,
            int personId,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null)
            {
                return "EmploymentFiringService: employment registry unavailable — firing refused, worker stays employed.";
            }

            if (state == null)
            {
                return "EmploymentFiringService: business runtime state unavailable — firing refused, worker stays employed.";
            }

            // Install the caller's registry on the state BEFORE the ensure:
            // EnsureEmploymentRecordsFromSlots reads state.EmploymentRegistry,
            // so installing only before the settlement (plan §3 step 5) would
            // leave the ensure a no-op in the store-fallback case, where the
            // state's registry was never wired and the passed registry is the
            // shared manager's. The install is idempotent (??=): in the
            // shared path the state is already wired and this is a no-op.
            // After this line the passed registry IS the state's registry
            // (note 114 F1 invariant).
            state.EmploymentRegistry ??= registry;

            // Ensure: project a record from the filled slot when the legacy
            // hire path never registered one. The ensure skips any
            // person/business pair that already has a relationship,
            // including Ended or Suspended ones, so it can never resurrect
            // a fired worker. The slot must still be filled when this runs,
            // which is why the helper runs before slot.Unassign().
            state.EnsureEmploymentRecordsFromSlots();

            // Find the Active record for this person at this business.
            EmploymentRelationship employment = null;
            foreach (EmploymentRelationship candidate in registry.GetActiveByEmployee(personId))
            {
                if (candidate != null
                    && string.Equals(candidate.EmployerBusinessId, state.BusinessId, StringComparison.Ordinal))
                {
                    employment = candidate;
                    break;
                }
            }

            if (employment == null)
            {
                return "no-record";
            }

            // Amount: full-or-nothing per week (note 114 Q1). The weekly run
            // pays the full agreed weekly wage when attendance is recorded
            // for the week and nothing otherwise; no per-minute rate exists
            // anywhere, so pro-rata would invent one.
            int weekIndex = dayIndex >= 0 ? dayIndex / 7 : -1;
            bool attended = false;
            if (weekIndex >= 0 && state.WeeklyAttendanceProvider != null)
            {
                attended = SafeAttendanceWorked(state, employment.Id, weekIndex, diagnostics);
            }

            int amount = attended ? Math.Max(0, employment.Compensation.AgreedWeeklyWageCents) : 0;
            string periodLabel = weekIndex >= 0
                ? $"final settlement week {weekIndex}"
                : "final settlement (unstamped day)";

            // Settle first, while the record is Active. A null return means
            // paid (or 0c owed — "owes nothing", nothing paid). A non-null
            // return is a refusal or a shortfall: record it and continue to
            // the end step. The shortfall is detected from the record state
            // (only PayAgreedWage's shortfall branch changes lifecycle
            // state, to Suspended); other refusals leave it Active.
            string settleResult = state.PayAgreedWage(employment.Id, amount, periodLabel, dayIndex, diagnostics);
            bool shortfalled = employment.LifecycleState == EmploymentLifecycleState.Suspended;
            if (settleResult != null)
            {
                diagnostics.Add(
                    $"EmploymentFiringService: final-wage settlement for P{personId} did not pay in full — {settleResult}");
            }

            // End: ends every Active employment the person holds (note 114
            // Q5, end-all). In all reachable states the hire gates enforce
            // at most one Active record per person, so end-all ==
            // end-this-business; the degenerate case is pinned by T9.
            registry.EndEmploymentByReason(personId, EmploymentEndReason.Dismissed, dayIndex);
            if (shortfalled)
            {
                return "ended-with-shortfall";
            }

            return "ended";
        }

        private static bool SafeAttendanceWorked(
            BusinessRuntimeState state, string employmentId, int weekIndex, List<string> diagnostics)
        {
            try
            {
                return state.WeeklyAttendanceProvider(employmentId, weekIndex);
            }
            catch (Exception ex)
            {
                diagnostics.Add(
                    $"EmploymentFiringService: attendance check for employment {employmentId} week {weekIndex} failed " +
                    $"({ex.GetType().Name}) — treated as absent, final wage not paid.");
                return false;
            }
        }
    }
}
