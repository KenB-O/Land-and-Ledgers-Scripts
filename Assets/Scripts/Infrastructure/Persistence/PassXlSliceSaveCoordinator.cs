using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;

namespace LandLedgers.Persistence
{
    /// <summary>Testable sink for the Pass XL no-immigration flag.</summary>
    public interface IPassXlMigrationFlagHolder
    {
        bool SuppressMigrantArrivals { get; set; }
    }

    /// <summary>Testable sink for the Pass XL player-as-NPC flag.</summary>
    public interface IPassXlPlayerFlagHolder
    {
        bool TreatPlayerAsNpc { get; set; }
    }

    /// <summary>
    /// Phase 2: plain, Unity-free coordinator for Pass XL slice save
    /// state. SaveLoadManager calls this; Roslyn tests call this
    /// directly with fake holders and a real procurement service, so
    /// the save contract is verified without a MonoBehaviour scene.
    ///
    /// CONTRACT
    /// - Capture: null holders and a null service all read as defaults
    ///   (flags false, procurement null). A service with no state
    ///   exports an empty DTO, never null, so a new save always carries
    ///   an explicit empty procurement section.
    /// - Restore: clear-then-load. The procurement service is cleared
    ///   first, then the DTO is imported, so loading a second save
    ///   cannot leak the first save's orders, lots, sales, or delivered
    ///   marks. Restoring the same DTO twice therefore yields the same
    ///   state (Clear + Import is idempotent), and duplicate imports
    ///   through ImportState alone also create nothing twice.
    /// - Restore never regenerates population. It only sets the two
    ///   flags on the holders supplied. If a holder is null, its flag
    ///   is skipped with a diagnostic, never crashed on.
    /// - Old saves: a null dto, or a dto with a null procurement field,
    ///   restores as flags false and an empty procurement service.
    ///   That is the documented default, not a data loss: an old save
    ///   never had off-map procurement state to lose.
    ///
    /// ORDER IN SaveLoadManager: capture in BuildSaveDto; restore in
    /// LoadDefaultSlot AFTER population load and AFTER the drivers
    /// domestic watermark reset, and BEFORE the post-restore
    /// consistency snapshot, so that snapshot sees the restored flags.
    /// On Continue, the bootstrapper's playtest bool is an OR override
    /// (see FirstLedgerSliceBootstrapper): it can turn NPC mode on for
    /// an old save, but it never clears a saved true on its own.
    /// </summary>
    public static class PassXlSliceSaveCoordinator
    {
        public static PassXlSliceSaveDto Capture(
            IPassXlMigrationFlagHolder migrationHolder,
            IPassXlPlayerFlagHolder playerHolder,
            OffMapNamedSkuProcurementService procurementService)
        {
            return new PassXlSliceSaveDto
            {
                treatPlayerAsNpc = playerHolder != null && playerHolder.TreatPlayerAsNpc,
                suppressMigrantArrivals = migrationHolder != null && migrationHolder.SuppressMigrantArrivals,
                procurement = procurementService != null ? procurementService.ExportState() : null,
            };
        }

        public static List<string> Restore(
            PassXlSliceSaveDto dto,
            IPassXlMigrationFlagHolder migrationHolder,
            IPassXlPlayerFlagHolder playerHolder,
            OffMapNamedSkuProcurementService procurementService)
        {
            var diagnostics = new List<string>();
            bool treatPlayerAsNpc = dto != null && dto.treatPlayerAsNpc;
            bool suppressMigrantArrivals = dto != null && dto.suppressMigrantArrivals;

            if (migrationHolder != null)
            {
                migrationHolder.SuppressMigrantArrivals = suppressMigrantArrivals;
            }
            else
            {
                diagnostics.Add("PassXlSliceSaveCoordinator: no migration flag holder supplied; SuppressMigrantArrivals not restored.");
            }

            if (playerHolder != null)
            {
                playerHolder.TreatPlayerAsNpc = treatPlayerAsNpc;
            }
            else
            {
                diagnostics.Add("PassXlSliceSaveCoordinator: no player flag holder supplied; TreatPlayerAsNpc not restored.");
            }

            if (procurementService != null)
            {
                procurementService.ClearState();
                if (dto != null && dto.procurement != null)
                {
                    procurementService.ImportState(dto.procurement);
                    diagnostics.Add($"PassXlSliceSaveCoordinator: restored off-map procurement for business '{procurementService.BusinessId}' ({procurementService.Orders.Count} orders, {procurementService.Lots.Count} lots, {procurementService.Sales.Count} sales, {procurementService.DeliveredSaleIds.Count} delivered).");
                }
                else
                {
                    diagnostics.Add("PassXlSliceSaveCoordinator: no procurement section in save; off-map procurement restored as empty (old-save default).");
                }
            }
            else
            {
                diagnostics.Add("PassXlSliceSaveCoordinator: no procurement service supplied; procurement state not restored. Codex must register the production procurement service with SaveLoadManager before saving, or procurement will not persist.");
            }

            diagnostics.Add($"PassXlSliceSaveCoordinator: flags restored (TreatPlayerAsNpc={treatPlayerAsNpc}, SuppressMigrantArrivals={suppressMigrantArrivals}); population was NOT regenerated.");
            return diagnostics;
        }
    }
}
