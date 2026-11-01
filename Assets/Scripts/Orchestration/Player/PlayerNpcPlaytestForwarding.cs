using LandLedgers.Persistence;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Phase 2, plain testable seam for the bootstrapper playtest bool.
    ///
    /// RULE, STATED PLAINLY: the bootstrapper bool is an OR override,
    /// not a source of truth that clears a saved value.
    /// - Bootstrapper true  -> drivers.TreatPlayerAsNpc becomes true.
    /// - Bootstrapper false -> drivers are left exactly as they are.
    ///   That matters on Continue: SaveLoadManager restores
    ///   TreatPlayerAsNpc from PassXlSliceSaveDto, and a scene default
    ///   of false must never silently clear a saved true. To turn NPC
    ///   mode off for a continued game, save with the flag off; do not
    ///   rely on toggling the scene bool off after a save.
    /// - A null drivers object is a no-op, never a crash.
    ///
    /// FirstLedgerSliceBootstrapper calls this on New Game (after
    /// ConfigureScenarioAuthorities creates/finds the drivers) and on
    /// Continue (after LoadDefaultSlot restores the save). Codex must
    /// still set the bootstrapper bool in Main Scene Inspector; this
    /// file is code only and claims no scene wiring.
    /// </summary>
    public static class PlayerNpcPlaytestForwarding
    {
        public static bool ApplyBootstrapperFlag(
            IPassXlPlayerFlagHolder drivers, bool bootstrapperTreatPlayerAsNpc)
        {
            if (drivers == null)
            {
                return false;
            }

            if (bootstrapperTreatPlayerAsNpc)
            {
                drivers.TreatPlayerAsNpc = true;
                return true;
            }

            return drivers.TreatPlayerAsNpc;
        }
    }
}
