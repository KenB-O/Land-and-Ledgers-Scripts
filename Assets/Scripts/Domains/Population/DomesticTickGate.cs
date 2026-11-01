namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XI WS1 (PASS9-006): the domestic tick watermark, extracted
    /// from SimulationDrivers into a plain rule so it is testable and so
    /// its save/load reset is a single explicit operation.
    ///
    /// Rule, unchanged from the drivers field it replaces: only forward
    /// motion advances domestic labor, so a repeated or paused tick can
    /// never re-start a pending task. The gate holds nothing but the
    /// watermark float — resetting it preserves all completed work, all
    /// pending tasks, and all person-time reservations, which live in
    /// DomesticLaborDriver and WorkTimeBudgets, not here.
    ///
    /// Load contract: the save/load hook calls Reset with the loaded
    /// clock's absolute game seconds. Loading an earlier-time save in
    /// the same session then advances domestic labor on the next tick
    /// instead of stalling until game time passes the old watermark.
    /// </summary>
    public sealed class DomesticTickGate
    {
        private float previousTickGameSeconds = -1f;

        public float PreviousTickGameSeconds => previousTickGameSeconds;

        public bool ShouldAdvance(float currentGameSeconds)
        {
            return currentGameSeconds > previousTickGameSeconds;
        }

        public void MarkAdvanced(float currentGameSeconds)
        {
            previousTickGameSeconds = currentGameSeconds;
        }

        public void Reset(float loadedGameSeconds)
        {
            previousTickGameSeconds = loadedGameSeconds;
        }
    }
}
