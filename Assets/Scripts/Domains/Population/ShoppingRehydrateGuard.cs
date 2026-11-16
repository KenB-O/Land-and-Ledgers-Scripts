// Campaign C (DEF-023) — sticky record of a shopping-trip rehydration that
// was skipped, so a load that would silently drop in-flight trips fails loudly
// instead. ApplyRestoredShoppingTrips (SimulationDrivers) is the only production
// caller of ShoppingTripDriver.RehydrateAfterLoad, and it silently returns when
// the driver is null, no trips are pending, or the hub/TimeManager is missing.
// The first two cases are legitimate no-ops; the third leaves
// NeedsRehydrateAfterLoad set forever with the driver's leg/service maps empty,
// and the per-tick backstop (SyncTaskCompletionDueEvents) then re-schedules the
// leg dues while the MAP-ONLY service due is never re-registered (Slice 19) —
// the customer's wait is pushed later with no signal. This guard records the
// skip (with the pending trip count), arms a one-time first-tick backstop
// shield, and is cleared the moment rehydration runs or no trips are pending.
// Pure state, no Unity dependency, so the Roslyn harness can test it directly
// without compiling SimulationDrivers.
using System;

namespace LandLedgers.Population
{
    public sealed class ShoppingRehydrateGuard
    {
        public bool SkippedAfterLoad { get; private set; }
        public int SkippedTripCount { get; private set; }
        public bool GuardedFirstTick { get; private set; }

        /// <summary>
        /// Records a skipped rehydration. Returns true when this call newly arms
        /// the skip (the caller logs the loud error only then); later calls while
        /// the skip is still pending only refresh the count, so a late retry on
        /// every tick cannot spam the Console. The one-time first-tick shield is
        /// re-armed only on a fresh skip.
        /// </summary>
        public bool RecordSkipped(int tripCount)
        {
            bool newlyArmed = !SkippedAfterLoad;
            if (newlyArmed)
            {
                SkippedAfterLoad = true;
                GuardedFirstTick = false;
            }
            SkippedTripCount = Math.Max(0, tripCount);
            return newlyArmed;
        }

        /// <summary>
        /// Clears the record: rehydration ran, or no trips are pending (New Game
        /// rebind, second rebind, or a load with no trips). Any earlier skip is
        /// no longer pending.
        /// </summary>
        public void RecordCleared()
        {
            SkippedAfterLoad = false;
            SkippedTripCount = 0;
            GuardedFirstTick = false;
        }

        /// <summary>
        /// True exactly once per recorded skip: the first post-load tick while
        /// rehydration is still pending. Consumes the shield — later ticks run
        /// the backstop normally (the skip is already logged loudly, and holding
        /// the backstop off forever would stall every other task's dues).
        /// </summary>
        public bool TakeFirstTickGuard()
        {
            if (!SkippedAfterLoad || GuardedFirstTick) return false;
            GuardedFirstTick = true;
            return true;
        }
    }
}
