using System;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Persistence
{
    /// <summary>
    /// Phase 2: Pass XL vertical slice save section. This is the ONLY
    /// place Phase 1 slice flags and procurement state are persisted:
    /// TreatPlayerAsNpc (SimulationDrivers), SuppressMigrantArrivals
    /// (PopulationManager), and the General Store off-map procurement
    /// service state (orders, lots, sales, delivered marks, sequences).
    /// A null procurement field means an old save with no off-map
    /// procurement; restore treats it as an empty service state.
    ///
    /// This class lives in its own file (moved from SaveDtos.cs with
    /// no change to namespace, name, or field names) so the slice
    /// coordinator and its Roslyn tests can compile it without
    /// dragging the whole SaveDtos closure into the harness. Serialized
    /// shape is unchanged: older readers ignore the passXlSlice field
    /// on LandLedgersSaveGameDto, newer readers read it as before.
    /// </summary>
    [Serializable]
    public sealed class PassXlSliceSaveDto
    {
        public bool treatPlayerAsNpc;
        public bool suppressMigrantArrivals;
        public NamedSkuProcurementSaveDto procurement;
    }
}
