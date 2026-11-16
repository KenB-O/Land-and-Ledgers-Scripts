// Campaign C (DEF-066): the real-time conversation rule (Nov 13, 2026
// design decision, Kennedy + ChatGPT) — when the player enters a direct
// NPC conversation, the simulation slows globally to real time and the
// previously selected speed restores automatically when it ends;
// NPC-to-NPC conversations do not change player speed.
//
// This gate owns the capture/restore state machine only. It never touches
// the clock itself: the conversation session owner (currently
// AcquisitionSellerMeetingController, the only live direct player-NPC
// conversation session) applies the gate's decisions through
// TimeManager.SetSpeed. Plain C#, no Unity dependency, so the Roslyn
// harness can execute it directly.
//
// Semantics (mirroring the Observe slowdown-with-restore precedent in
// WorldInteractionController, which captures previousObservationSpeed,
// sets Observe, and restores on cancel):
//   - Begin returns RealTime when the sim is running faster than real
//     time (any of Speed1x..Speed500x, or Observe, which advances 10
//     game-seconds per real second — still faster than RealTime's 1).
//     The previously selected speed is captured exactly once: a second
//     Begin while the conversation is already open is a no-op, so a
//     re-opened or refreshed meeting cannot overwrite the first capture.
//   - Begin returns null (no SetSpeed call) when the sim is already at
//     real time, or when it is paused. A paused sim is never unpaused
//     to slow it down: paused is already slower than real time.
//   - End returns the captured speed only when a slowdown was actually
//     applied AND the current speed is still RealTime. If the player
//     (or another system) changed the speed mid-conversation, that new
//     selection wins and is not clobbered by the restore.
//   - End when no conversation is open returns null: a stray close
//     can never rewrite the player's speed.
//   - The gate is per-session state. It is not saved: if the player
//     saves mid-conversation and loads, the meeting UI is closed on
//     load while the clock keeps the saved (real-time) speed, and the
//     player re-selects speed from the HUD — the same property the
//     Observe path already has (previousObservationSpeed is not saved).
namespace LandLedgers.Time
{
    /// <summary>
    /// Capture/restore state machine for the real-time conversation rule.
    /// One instance per conversation session owner.
    /// </summary>
    public sealed class ConversationSpeedGate
    {
        private bool active;
        private bool slowdownApplied;
        private SimulationSpeed previousSpeed = SimulationSpeed.Speed1x;

        /// <summary>True while a direct player-NPC conversation is open.</summary>
        public bool IsActive => active;

        /// <summary>
        /// Entering a direct player-NPC conversation. Returns the speed
        /// the caller should apply via TimeManager.SetSpeed, or null when
        /// no change is needed (already real time, already paused, or
        /// already inside a conversation).
        /// </summary>
        public SimulationSpeed? BeginConversation(SimulationSpeed currentSpeed)
        {
            if (active)
            {
                return null;
            }

            active = true;
            previousSpeed = currentSpeed;

            if (currentSpeed == SimulationSpeed.Paused || currentSpeed == SimulationSpeed.RealTime)
            {
                slowdownApplied = false;
                return null;
            }

            slowdownApplied = true;
            return SimulationSpeed.RealTime;
        }

        /// <summary>
        /// Leaving a direct player-NPC conversation. Returns the speed
        /// the caller should restore via TimeManager.SetSpeed, or null
        /// when no restore is owed (no conversation was open, no
        /// slowdown was applied, or the speed was changed
        /// mid-conversation by the player or another system).
        /// </summary>
        public SimulationSpeed? EndConversation(SimulationSpeed currentSpeed)
        {
            if (!active)
            {
                return null;
            }

            active = false;
            if (!slowdownApplied)
            {
                return null;
            }

            slowdownApplied = false;
            if (currentSpeed != SimulationSpeed.RealTime)
            {
                return null;
            }

            return previousSpeed;
        }
    }
}
