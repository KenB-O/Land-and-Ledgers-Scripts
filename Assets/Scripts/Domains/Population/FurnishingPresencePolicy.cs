using LandLedgers.Economy.Labor;

namespace LandLedgers.Population
{
    /// <summary>
    /// B7 (expanded campaign 2026-11-16): which furnishing actions require the
    /// player's physical presence and which do not. This is the source contract
    /// D's player selection API and F's purchase flow consume; G wires the
    /// production driver that enforces it (see
    /// IPlayerFurnishingPresenceDriver).
    ///
    /// Owner direction (campaign brief): important actions require presence —
    /// buying/selling a horse, attending a business meeting, inspecting
    /// property, negotiating a major transaction, meeting a supplier,
    /// personally conducting a commercial exchange, hands-on work — while
    /// routine sleeping, ordinary eating and minor housekeeping are NEVER
    /// forced on the player. The player is NOT The Sims.
    ///
    /// Presence table (O-4 proposed defaults — the owner corrects the line):
    /// Buy, Install, Move, Remove, Sell, InspectProperty and SelectContents
    /// are major transactions, hands-on work or property inspection, so they
    /// require presence (or an authorized agent, see the contract). Use —
    /// routine sleeping, eating, cooking, dining, storing — is presence-free.
    /// No furnishing action in this table is agent-proof: an authorized agent
    /// may act for the player on any presence-required action, which is the
    /// historically and contractually valid delegation the owner direction
    /// allows. Unspecified is Required, never silently free: an action whose
    /// kind is not known must not execute remotely by default.
    ///
    /// Routine tasks are ONLY the three Pass2 life tasks (lf.eat-meal,
    /// lf.drink, lf.sleep). Any other in-progress task is work, business or
    /// another directed action, and a directed furnishing action refuses
    /// while one runs ("cancel first"), matching the PlayerDirector's own
    /// IsBusy rule. The owner can widen the routine line later (O-4
    /// follow-up); the contract does not guess at it.
    /// </summary>
    public enum FurnishingActionKind
    {
        Unspecified = 0,
        Buy = 1,
        Install = 2,
        Move = 3,
        Remove = 4,
        Sell = 5,
        InspectProperty = 6,
        SelectContents = 7,
        Use = 8,
    }

    /// <summary>Whether a furnishing action needs the actor physically present.</summary>
    public enum PresenceRequirement
    {
        Required = 0,
        Free = 1,
    }

    /// <summary>
    /// Presence table for furnishing actions (B7). Pure policy, no state, no
    /// Unity dependency; the contract applies it, D and F read it.
    /// </summary>
    public static class FurnishingPresencePolicy
    {
        /// <summary>
        /// Presence requirement for one furnishing action kind. Use is free;
        /// every named major action is required; Unspecified and any unknown
        /// kind default to Required (never silently free).
        /// </summary>
        public static PresenceRequirement RequirementFor(FurnishingActionKind kind)
        {
            switch (kind)
            {
                case FurnishingActionKind.Use:
                    return PresenceRequirement.Free;
                case FurnishingActionKind.Buy:
                case FurnishingActionKind.Install:
                case FurnishingActionKind.Move:
                case FurnishingActionKind.Remove:
                case FurnishingActionKind.Sell:
                case FurnishingActionKind.InspectProperty:
                case FurnishingActionKind.SelectContents:
                    return PresenceRequirement.Required;
                default:
                    return PresenceRequirement.Required;
            }
        }

        /// <summary>True when the action kind requires presence (or an authorized agent).</summary>
        public static bool IsPresenceRequired(FurnishingActionKind kind)
        {
            return RequirementFor(kind) == PresenceRequirement.Required;
        }

        /// <summary>
        /// True when a task definition id is one of the three routine life
        /// tasks — eating, drinking, sleeping — that a directed furnishing
        /// action may interrupt. The ids are Pass2TaskCatalogs' constants,
        /// referenced directly so a rename there fails this file's build
        /// instead of silently drifting. Registering a life definition does
        /// NOT replace day-boundary meal consumption or the planner meal/sleep
        /// reservations; it only marks the task as routine for this gate.
        /// </summary>
        public static bool IsRoutineTaskDefinitionId(string definitionId)
        {
            if (string.IsNullOrEmpty(definitionId))
                return false;
            return definitionId == Pass2TaskCatalogs.EatMealId
                || definitionId == Pass2TaskCatalogs.DrinkId
                || definitionId == Pass2TaskCatalogs.SleepId;
        }
    }
}
