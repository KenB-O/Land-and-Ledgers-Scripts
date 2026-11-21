// Lane 9c (M-R8, Agent M §4 class 4): the presentation check for
// business tasks advanced by AdvanceAutonomousWork. Domestic tasks
// already get this check (DomesticActivityPresentationFailure on the
// pathing director: off-map / no managed actor / no mover / riding),
// but AdvanceAutonomousWork checked only null/status/assignee, so a
// business task assigned to a worker with no body accrued minutes
// normally. The decision lives here (pure static, no Unity types) so
// the harness can pin it; SimulationDrivers gathers the four facts
// and applies the verdict. A skipped task is NOT interrupted or
// cancelled — it stays InProgress and the stale sweep still owns its
// window, so a worker who gains an actor later can resume; the skip
// only stops minutes accruing to a worker who cannot physically exist.
// The check runs regardless of CustomerRef: the building-arrival gate
// may be skipped for customer tasks, but the presentation check must
// not be.
using System;

namespace LandLedgers.Tasks
{
    public static class BusinessTaskPresentationGate
    {
        /// <summary>
        /// Whether this tick's work accrual must be skipped for the
        /// assignee. True when the assignee has no managed on-map
        /// physical actor, is off-map, is dead, or is Hidden; the reason
        /// names the first failing fact for the day-keyed diagnostic.
        /// </summary>
        public static bool ShouldSkipWorkAccrual(
            bool hasManagedActor, bool isOffMap, bool isDead, bool isHidden,
            out string reason)
        {
            reason = string.Empty;
            if (!hasManagedActor)
            {
                reason = "the assignee has no managed on-map physical actor";
                return true;
            }
            if (isOffMap)
            {
                reason = "the assignee is off-map";
                return true;
            }
            if (isDead)
            {
                reason = "the assignee is dead";
                return true;
            }
            if (isHidden)
            {
                reason = "the assignee's physical actor is Hidden";
                return true;
            }
            return false;
        }
    }
}
