using System;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL vertical slice: the player-as-NPC playtest policy.
    ///
    /// Normally the player IS a real person: on map, doing every task NPCs can
    /// do, eating, sleeping, working through the same TaskAuthority, budgets,
    /// and schedules as everyone else. PlayerDirector only adds an
    /// issuance/advancement path on top: player-issued travel and
    /// player-issued tasks advance on the short tick, and player owner-labour
    /// minutes are accumulated for weekly valuation.
    ///
    /// When TreatPlayerAsNpc is true, that player-only layer is switched off
    /// for playtesting, so Kennedy can watch NPCs run alone:
    ///   - player-issued task advancement stops (SimulationDrivers gates
    ///     AdvancePlayerWork on this policy);
    ///   - player-issued travel stops advancing (SimulationDrivers gates
    ///     PlayerDirector.RecordMovementProgress on this policy);
    ///   - player owner-labour accounting stops (SimulationDrivers gates the
    ///     weekly accumulation and posting on this policy).
    ///
    /// What this policy does NOT do, by design:
    ///   - it does not change player identity, household membership, business
    ///     ownership, or OwnerKind classification;
    ///   - it does not stop the autonomous drivers (daily needs, domestic
    ///     labour, daily activity plans, autonomous business work), so the
    ///     player person still eats, sleeps, does domestic chores, and works
    ///     in a business through the same drivers as any NPC;
    ///   - it does not convert a player-issued task into an autonomous task.
    ///     A task already issued while the flag is on will stall InProgress
    ///     rather than being picked up as NPC work, because autonomous
    ///     advancement only covers business-owned, schedule-gated tasks and
    ///     NPC drivers only generate their own assignments. For a pure
    ///     watch-the-NPCs playtest, issue no player tasks while the flag is on.
    ///
    /// Persistence: this class is plain C# and is NOT a save DTO. In Unity
    /// the authoritative value lives on SimulationDrivers as a serialized
    /// bool (see SimulationDrivers.TreatPlayerAsNpc), which Codex exposes in
    /// the Inspector. That scene value resets to the scene value on reload
    /// and is not currently part of any save DTO; if the setting must survive
    /// New Game / Continue, Codex must forward a bootstrapper-level
    /// serialized bool into drivers.TreatPlayerAsNpc. Default is false, so
    /// old scenes and old saves behave exactly as before.
    ///
    /// Plain C# with no Unity dependency, so the rule itself is testable in
    /// the Roslyn harness. SimulationDrivers wiring is covered by an
    /// Editor test (see Tests/Editor/Orchestration/PassXlPlayerAsNpcTests.cs)
    /// and must be run in Unity by Codex/Luna.
    /// </summary>
    public sealed class PlayerNpcPlaytestPolicy
    {
        /// <summary>False by default: ordinary player behaviour, unchanged.</summary>
        public bool TreatPlayerAsNpc { get; set; }

        /// <summary>True when player-issued tasks may advance on the short tick.</summary>
        public bool ShouldAdvancePlayerWork()
        {
            return !TreatPlayerAsNpc;
        }

        /// <summary>True when player-issued travel may advance on the short tick.</summary>
        public bool ShouldAdvancePlayerTravel()
        {
            return !TreatPlayerAsNpc;
        }

        /// <summary>True when player owner-labour minutes may be accumulated/posted.</summary>
        public bool ShouldCountPlayerOwnerLabour()
        {
            return !TreatPlayerAsNpc;
        }
    }
}
