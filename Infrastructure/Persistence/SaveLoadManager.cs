using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace LandLedgers.Persistence
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public sealed class SaveLoadManager : MonoBehaviour
    {
        private const int CurrentFormatVersion = SaveMigrationEnvelope.CurrentFormatVersion;
        private const int MinimumSupportedFormatVersion = SaveMigrationEnvelope.MinimumSupportedFormatVersion;
        private const string SaveDirectoryName = "Saves";
        private const string DefaultSlotFileName = "default.json";
        private const string BackupSlotExtension = ".bak";
        private const string TempSlotExtension = ".tmp";

        [Header("Scene Systems")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private CivicFoundationManager civicFoundation;

        [SerializeField]
        private GeneralStoreRuntimeManager storeRuntime;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private LogisticsRuntimeManager logisticsRuntime;

        [SerializeField]
        private TownPulseRuntimeManager townPulseRuntime;

        [SerializeField]
        private AcquisitionMarketManager acquisitionMarket;

        [SerializeField]
        private PlayerDebtManager playerDebtManager;

        [SerializeField]
        private PlayerPortfolioManager playerPortfolioManager;

        [SerializeField]
        private PopulationPathingDirector populationPathingDirector;

        [SerializeField]
        private GeneralStorePanelController managementPanel;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [SerializeField]
        private FirstSessionGuidanceManager firstSessionGuidance;

        [SerializeField]
        private FirstLedgerSliceBootstrapper bootstrapper;

        [SerializeField, Tooltip("CLN-1: owns the standalone simulation authorities (tasks, skills, animals, ledgers, valuation, freight, butcher, farm flows).")]
        private SimulationSystemsHub systemsHub;

        [SerializeField]
        private FirstLedgerBootstrap firstLedgerBootstrap;

        [Header("Runtime Controls")]
        [SerializeField]
        private bool enableDevHotkeys = true;

        [SerializeField]
        private Key saveKey = Key.F5;

        [SerializeField]
        private Key loadKey = Key.F9;

        [Header("Runtime Status")]
        [SerializeField]
        private string lastStatus = "No save/load operation yet.";

        [SerializeField]
        private int lastNormalizationRepairCount;

        [SerializeField]
        private string lastNormalizationSummary = string.Empty;

        [SerializeField]
        private string lastReferenceResolutionSummary = string.Empty;

        [SerializeField]
        private string lastSaveIntegritySummary = string.Empty;

        [SerializeField]
        private string lastRestoreDependencySummary = string.Empty;

        [SerializeField]
        private string lastPostRestoreConsistencySummary = string.Empty;

        private MigrationManifestDto activeMigrationManifest;

        public string LastStatus => lastStatus;
        public string DefaultSlotPath => Path.Combine(Application.persistentDataPath, SaveDirectoryName, DefaultSlotFileName);

        [ContextMenu("Save Default Slot")]
        public void SaveDefaultSlotFromContextMenu()
        {
            SaveDefaultSlot(out _);
        }

        [ContextMenu("Load Default Slot")]
        public void LoadDefaultSlotFromContextMenu()
        {
            LoadDefaultSlot(out _);
        }

        [ContextMenu("Inspect Default Slot")]
        public void InspectDefaultSlotFromContextMenu()
        {
            TryReadDefaultSlotManifest(out _, out _);
        }

        public bool TryReadDefaultSlotManifest(out SaveManifestDto manifest, out string message)
        {
            manifest = null;
            string path = DefaultSlotPath;
            string backupPath = BuildBackupSlotPath(path);

            if (TryInspectSaveSlot(path, "default slot", out manifest, out message))
            {
                lastStatus = message;
                return true;
            }

            string primaryFailure = message;
            if (TryInspectSaveSlot(backupPath, "backup slot", out manifest, out message))
            {
                message = $"Default slot inspection fell back to backup because the primary slot was not usable ({primaryFailure}). {message}";
                lastStatus = message;
                return true;
            }

            message = File.Exists(path) || File.Exists(backupPath)
            ? $"No readable save manifest is available. Primary failed: {primaryFailure}. Backup failed: {message}."
            : $"No save slot exists at {path} or backup slot at {backupPath}.";
            lastStatus = message;
            return false;
        }

        public bool SaveDefaultSlot(out string message)
        {
            AutoWire();
            if (!CanSave(out message))
            {
                return ReportOperationFailure(message, "Save Blocked", message, "Save blocked", false, 6f);
            }

            LandLedgersSaveGameDto save = BuildSaveDto();
            SaveIntegrityReport integrityReport = ValidateSaveDtoForWrite(save);
            RecordSaveIntegrityDiagnostics(integrityReport);
            if (save.manifest != null)
            {
                save.manifest.saveIntegritySummary = integrityReport.BuildSummary("Save capture integrity");
                save.manifest.saveIntegrityWarningCount = integrityReport.WarningCount;
                save.manifest.saveIntegrityFatalCount = integrityReport.FatalErrorCount;
            }

            if (integrityReport.HasFatalErrors)
            {
                message = BuildOperationStatusMessage(
                "Save blocked because the captured ledger state is not safe to write.",
                integrityReport.BuildSummary("Save capture integrity"));
                return ReportOperationFailure(message, "Save Blocked", "Captured save state failed integrity checks.", "Save integrity failed", true, 6.5f);
            }

            string path = DefaultSlotPath;
            try
            {
                string json = JsonUtility.ToJson(save, true);
                WriteSaveFileWithBackup(path, json);
                activeMigrationManifest = save.migrationManifest;
            }
            catch (Exception ex)
            {
                message = BuildOperationStatusMessage(
                $"Save failed while writing default slot to {path}: {ex.Message}",
                integrityReport.BuildSummary("Save capture integrity"));
                return ReportOperationFailure(message, "Save Failed", "The ledger could not be written safely; the previous backup was preserved when available.", "Save failed", true, 7f);
            }

            lastNormalizationRepairCount = 0;
            lastNormalizationSummary = "No normalization needed while saving.";
            lastReferenceResolutionSummary = "Reference resolution not used during save capture.";
            lastRestoreDependencySummary = "Restore dependency audit not used during save capture.";
            lastPostRestoreConsistencySummary = "Post-load consistency audit not used during save capture.";
            RecordSaveIntegrityDiagnostics(integrityReport);

            message = BuildOperationStatusMessage(
            $"Saved default slot to {path}.",
            integrityReport.BuildSummary("Save capture integrity"));
            return ReportOperationSuccess(message, "Ledger Saved", BuildHudSaveSummary(integrityReport), "Ledger saved", 4.75f);
        }
        public bool LoadDefaultSlot(out string message)
        {
            AutoWire();
            string path = DefaultSlotPath;
            string backupPath = BuildBackupSlotPath(path);

            if (townWorld == null)
            {
                message = "Cannot load save: TownWorldController is missing.";
                return ReportOperationFailure(message, "Load Blocked", message, "Load blocked", true, 6f);
            }

            bool loadedFromBackup = false;
            string slotSourcePath = path;
            if (!TryLoadSaveFromSlot(path, out LandLedgersSaveGameDto save, out SaveNormalizationReport normalizationReport, out int saveFormatVersion, out string primaryFailure))
            {
                if (!TryLoadSaveFromSlot(backupPath, out save, out normalizationReport, out saveFormatVersion, out string backupFailure))
                {
                    message = File.Exists(path) || File.Exists(backupPath)
                    ? $"Default save could not be loaded ({primaryFailure}). Backup also failed ({backupFailure})."
                    : $"No default save slot exists at {path} or backup slot at {backupPath}.";
                    return ReportOperationFailure(message, "No Usable Save", message, "No usable save slot", true, 7f);
                }

                loadedFromBackup = true;
                slotSourcePath = backupPath;
            }

            SaveIntegrityReport integrityReport = ValidateSaveDtoForRestore(save);
            RecordSaveIntegrityDiagnostics(integrityReport);
            if (integrityReport.HasFatalErrors)
            {
                message = integrityReport.BuildSummary("Load integrity");
                return ReportOperationFailure(message, "Load Blocked", message, "Load integrity failed", true, 7f);
            }

            if (!townWorld.TryValidateSaveTerrainCompatibility(save.world, out string terrainCompatibilityError))
            {
                message = $"Load blocked before world reconstruction: {terrainCompatibilityError}";
                return ReportOperationFailure(message, "Terrain Incompatible", message, "Terrain compatibility failed", true, 8f);
            }

            SaveRestoreDependencyReport restoreDependencyReport = BuildRestoreDependencyReport(save);
            RecordRestoreDependencyDiagnostics(restoreDependencyReport);
            PrepareManagersForRestore();
            activeMigrationManifest = save.migrationManifest;

            timeManager?.SetPaused(true);
            SaveReferenceResolver resolver = new(townWorld);

            if (!townWorld.LoadFromSaveDto(save.world, resolver, out message))
            {
                RecordPersistenceDiagnostics(saveFormatVersion, normalizationReport, resolver, integrityReport, restoreDependencyReport, null);
                return ReportOperationFailure(message, "Load Failed", BuildHudLoadFailureSummary(message, normalizationReport, resolver, integrityReport, restoreDependencyReport), "Load failed", true, 6.5f);
            }

            timeManager?.LoadFromSaveDto(save.time);
            populationManager?.LoadFromSaveDto(save.population);
            civicFoundation?.LoadFromSaveDto(save.civic);

            // CLN-1: restore the HF-1 entity-ID cursors first (animal import requires
            // restored cursors), then the HF-2/HF-4 population sections and the
            // systems hub section.
            if (systemsHub != null)
            {
                List<string> cursorDiagnostics = EntityIdSaveAdapter.ReadCursors(
                    save,
                    systemsHub.Ids,
                    save.population != null ? save.population.nextPersonId : 0,
                    save.population != null ? save.population.nextHouseholdId : 0,
                    0);
                foreach (string diagnostic in cursorDiagnostics)
                {
                    Debug.LogWarning($"[SaveLoad] {diagnostic}");
                }

                if (save.population != null)
                {
                    systemsHub.ImportAnimalState(
                        save.population.animals,
                        save.population.historicalAnimals,
                        save.population.cohorts,
                        save.population.eggBatches);
                    systemsHub.ImportHouseholdLedgers(save.population.householdLedgers);
                    // Phase B: restore the real-consumption authorities.
                    systemsHub.ImportHouseholdInventories(save.population.householdInventories);
                    systemsHub.ImportMealLog(
                        save.population.mealRecords,
                        save.population.missedMeals,
                        save.population.mealPreparations);
                    systemsHub.ImportPurchasingNeeds(save.population.purchasingNeeds);

                    // Phase B (Real People): OQ-8 deterministic repair. Legacy
                    // saves carry household cash in the retired
                    // spendingMoneyCents wallet; book each positive balance
                    // once as an explicit migration entry in the ledger so
                    // money is neither duplicated nor lost. Runs after the
                    // ledger import so saved ledgers are never overwritten.
                    int migrationDay = save.time != null ? save.time.absoluteDayIndex : 0;
                    var migrationDiagnostics = new System.Collections.Generic.List<string>();
                    HouseholdLegacyCashMigrator.MigrateAll(
                        populationManager != null && populationManager.State != null
                            ? populationManager.State.households
                            : null,
                        systemsHub.HouseholdLedgers,
                        migrationDay,
                        migrationDiagnostics);
                    foreach (string migrationDiagnostic in migrationDiagnostics)
                    {
                        Debug.Log($"[SaveLoad] {migrationDiagnostic}");
                    }
                }

                int absoluteDayIndex = save.time != null ? save.time.absoluteDayIndex : 0;
                List<string> systemsDiagnostics = systemsHub.LoadFromSaveDto(save.systems, absoluteDayIndex);
                foreach (string diagnostic in systemsDiagnostics)
                {
                    Debug.LogWarning($"[SaveLoad] {diagnostic}");
                }
            }
            storeRuntime?.LoadFromSaveDto(save.businesses != null ? save.businesses.deepGeneralStore : null);

            string deepStoreInstanceId = storeRuntime != null && storeRuntime.CurrentBusiness != null
            ? storeRuntime.CurrentBusiness.InstanceId
            : string.Empty;
            sharedBusinessRuntime?.LoadFromSaveDtos(
            save.businesses != null ? save.businesses.sharedBusinesses : null,
            deepStoreInstanceId,
            save.businesses != null ? save.businesses.localRecurringOrderRelationships : null,
            save.businesses != null ? save.businesses.lastWeeklyRecurringLocalOrderSummary : null,
            save.businesses != null ? save.businesses.transferAgreements : null,
            save.businesses != null ? save.businesses.lastWeeklyTransferAgreementSummary : null);
            logisticsRuntime?.LoadFromSaveDto(save.businesses != null ? save.businesses.logistics : null);
            populationManager?.RefreshSettlementMetrics("Population restored from save.");

            ConfigurePortfolioManager();
            if (save.portfolio != null && save.portfolio.initialized)
            {
                playerPortfolioManager?.LoadFromSaveDto(save.portfolio);
            }
            else
            {
                playerPortfolioManager?.InitializeFromLegacyBusinessCash(storeRuntime, sharedBusinessRuntime);
            }

            townPulseRuntime?.Configure(timeManager, hudController);
            townPulseRuntime?.LoadFromSaveDto(save.businesses != null ? save.businesses.townPulse : null);
            acquisitionMarket?.LoadFromSaveDto(save.acquisition);
            playerDebtManager?.Configure(timeManager, storeRuntime, acquisitionMarket, sharedBusinessRuntime, playerPortfolioManager);
            playerDebtManager?.AttachFinancialAuthority(systemsHub?.FinancialObligations, systemsHub?.Ids);
            playerDebtManager?.LoadFromSaveDto(save.debt);
            playerDebtManager?.ProcessCurrentDay();
            populationPathingDirector?.ResetForLoadedState();

            ConfigureFirstSessionGuidance();
            firstSessionGuidance?.LoadFromSaveDto(save.firstSessionGuidance);
            firstLedgerBootstrap ??= FindAnyObjectByType<FirstLedgerBootstrap>();
            firstLedgerBootstrap?.RestoreFromSaveDto(save.scenario);
            managementPanel?.Refresh();
            ConfigureFirstSessionGuidance();

            SavePostRestoreConsistencyReport postRestoreReport = TryBuildPostRestoreConsistencyReport(save);
            RecordPersistenceDiagnostics(saveFormatVersion, normalizationReport, resolver, integrityReport, restoreDependencyReport, postRestoreReport);
            bootstrapper?.MarkRestoredFromSave(BuildPersistenceStatusSummary());

            string sourceLabel = loadedFromBackup ? "backup slot" : "default slot";
            string legacyPrefix = saveFormatVersion < CurrentFormatVersion
            ? $"Loaded legacy v{saveFormatVersion} save from {sourceLabel} at {slotSourcePath}."
            : $"Loaded {sourceLabel} from {slotSourcePath}.";
            message = BuildOperationStatusMessage(legacyPrefix, BuildPersistenceStatusSummary());
            return ReportOperationSuccess(
            message,
            BuildHudLoadTitle(saveFormatVersion, normalizationReport, loadedFromBackup),
            BuildHudLoadSummary(saveFormatVersion, normalizationReport, resolver, integrityReport, restoreDependencyReport, postRestoreReport, loadedFromBackup),
            normalizationReport.HasRepairs ? "Legacy save normalized" : "Ledger loaded",
            5.75f);
        }
        private void Awake()
        {
            AutoWire();
        }

        private void Update()
        {
            if (!enableDevHotkeys || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current[saveKey].wasPressedThisFrame)
            {
                SaveDefaultSlot(out _);
            }
            else if (Keyboard.current[loadKey].wasPressedThisFrame)
            {
                LoadDefaultSlot(out _);
            }
        }

        private bool TryInspectSaveSlot(string path, string slotLabel, out SaveManifestDto manifest, out string message)
        {
            manifest = null;
            if (!TryLoadSaveFromSlot(path, out LandLedgersSaveGameDto save, out SaveNormalizationReport normalizationReport, out int saveFormatVersion, out message))
            {
                return false;
            }

            SaveIntegrityReport integrityReport = ValidateSaveDtoForRestore(save);
            RecordPersistenceDiagnostics(saveFormatVersion, normalizationReport, null, integrityReport, null, null);
            manifest = save.manifest;
            message = BuildSlotInspectionSummary(path, slotLabel, save, normalizationReport, integrityReport);
            return true;
        }

        private static bool TryLoadSaveFromSlot(
        string path,
        out LandLedgersSaveGameDto save,
        out SaveNormalizationReport normalizationReport,
        out int saveFormatVersion,
        out string message)
        {
            save = null;
            normalizationReport = new SaveNormalizationReport();
            saveFormatVersion = 0;

            if (!TryReadSaveFile(path, out string json, out message))
            {
                return false;
            }

            if (!TryDeserializeSave(json, out save, out message))
            {
                return false;
            }

            if (save == null)
            {
                message = $"Save slot at {path} could not be parsed into a ledger save.";
                return false;
            }

            int rawFormatVersion = save.manifest != null ? save.manifest.formatVersion : 0;
            if (!SaveMigrationEnvelope.IsRawFormatVersionSupported(rawFormatVersion, path, out string versionError))
            {
                message = versionError;
                return false;
            }

            // JsonUtility materialises a default MigrationManifestDto (not null) when the JSON key
            // is absent from a legacy save (format version <= 5). Detect that default-constructed
            // sentinel by its empty migrationRunId and null it out so TryInterpretAndValidateEnvelope
            // enters the correct legacy-bootstrap branch rather than the manifest-validation branch.
            if (save.migrationManifest != null && string.IsNullOrEmpty(save.migrationManifest.migrationRunId))
            {
                save.migrationManifest = null;
            }

            if (!SaveMigrationEnvelope.TryInterpretAndValidateEnvelope(save, rawFormatVersion, out string envelopeError))
            {
                message = $"Save migration envelope error in {path}: {envelopeError}";
                return false;
            }

            if (!PersistentIdAllocatorMigration.TryMigrateOrValidate(save, out string migrationError))
            {
                message = $"ID allocator migration failed in {path}: {migrationError}";
                return false;
            }

            save = NormalizeLoadedSave(save, normalizationReport);
            if (save == null)
            {
                message = $"Save slot at {path} could not be parsed into a ledger save.";
                return false;
            }

            saveFormatVersion = rawFormatVersion;
            message = $"Loaded save DTO from {path}.";
            return true;
        }

        private static bool TryReadSaveFile(string path, out string json, out string message)
        {
            json = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                message = "Save path was empty.";
                return false;
            }

            if (!File.Exists(path))
            {
                message = $"Save slot does not exist at {path}.";
                return false;
            }

            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                message = $"Save slot at {path} could not be read: {ex.Message}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                message = $"Save slot at {path} was empty.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static bool TryDeserializeSave(string json, out LandLedgersSaveGameDto save, out string message)
        {
            save = null;
            try
            {
                save = JsonUtility.FromJson<LandLedgersSaveGameDto>(json);
            }
            catch (Exception ex)
            {
                message = $"Save JSON could not be parsed: {ex.Message}";
                return false;
            }

            if (save == null)
            {
                message = "Save JSON parsed to an empty save object.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static void WriteSaveFileWithBackup(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            string tempPath = path + TempSlotExtension;
            string backupPath = BuildBackupSlotPath(path);
            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                {
                    File.Copy(path, backupPath, true);
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception cleanupException)
                    {
                        Debug.LogWarning($"Could not remove temporary save file {tempPath}: {cleanupException.Message}");
                    }
                }

                throw;
            }
        }

        private static string BuildBackupSlotPath(string path)
        {
            return path + BackupSlotExtension;
        }

        private static string FormatCents(int cents)
        {
            return (cents / 100f).ToString("C0");
        }

        private LandLedgersSaveGameDto BuildSaveDto()
        {
            WorldSaveDto world = townWorld.CaptureSaveDto();
            TimeSaveDto time = timeManager != null ? timeManager.CaptureSaveDto() : new TimeSaveDto();
            PopulationSaveDto population = populationManager != null ? populationManager.CaptureSaveDto() : new PopulationSaveDto();
            CivicSaveDto civic = civicFoundation != null ? civicFoundation.CaptureSaveDto() : new CivicSaveDto();
            GeneralStoreSaveDto store = storeRuntime != null ? storeRuntime.CaptureSaveDto() : new GeneralStoreSaveDto();
            string deepStoreInstanceId = store.currentBusiness != null ? store.currentBusiness.instanceId : string.Empty;
            AcquisitionSaveDto acquisition = acquisitionMarket != null ? acquisitionMarket.CaptureSaveDto() : new AcquisitionSaveDto();
            PlayerDebtSaveDto debt = playerDebtManager != null ? playerDebtManager.CaptureSaveDto() : new PlayerDebtSaveDto();
            PlayerPortfolioSaveDto portfolio = playerPortfolioManager != null ? playerPortfolioManager.CaptureSaveDto() : new PlayerPortfolioSaveDto();
            firstSessionGuidance ??= FindAnyObjectByType<FirstSessionGuidanceManager>();
            FirstSessionGuidanceSaveDto guidance = firstSessionGuidance != null ? firstSessionGuidance.CaptureSaveDto() : new FirstSessionGuidanceSaveDto();
            firstLedgerBootstrap ??= FindAnyObjectByType<FirstLedgerBootstrap>();

            BusinessPortfolioSaveDto businesses = new()
            {
                deepGeneralStore = store,
                townPulse = townPulseRuntime != null ? townPulseRuntime.CaptureSaveDto() : new TownPulseSaveDto(),
                sharedBusinesses = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.CaptureSaveDtos(deepStoreInstanceId)
                : new System.Collections.Generic.List<BusinessInstanceSaveDto>(),
                logistics = logisticsRuntime != null
                ? logisticsRuntime.CaptureSaveDto()
                : new LogisticsRuntimeSaveDto(),
                localRecurringOrderRelationships = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.CaptureLocalRecurringOrderRelationshipSaveDtos()
                : new System.Collections.Generic.List<LocalRecurringOrderRelationshipSaveDto>(),
                lastWeeklyRecurringLocalOrderSummary = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.LastWeeklyRecurringLocalOrderSummary
                : string.Empty,
                transferAgreements = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.CaptureTransferAgreementSaveDtos()
                : new System.Collections.Generic.List<BusinessTransferAgreementSaveDto>(),
                lastWeeklyTransferAgreementSummary = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.LastWeeklyTransferAgreementSummary
                : string.Empty
            };

            LandLedgersSaveGameDto save = new()
            {
                world = world,
                time = time,
                population = population,
                civic = civic,
                businesses = businesses,
                acquisition = acquisition,
                debt = debt,
                portfolio = portfolio,
                firstSessionGuidance = guidance,
                scenario = firstLedgerBootstrap != null ? firstLedgerBootstrap.CaptureSaveDto() : new ScenarioSaveDto(),
                systems = systemsHub != null ? systemsHub.CaptureSaveDto() : new SystemsSaveDto(),
                migrationManifest = SaveMigrationEnvelope.CaptureForSave(activeMigrationManifest),
                manifest = new SaveManifestDto
                {
                    formatVersion = CurrentFormatVersion,
                    saveId = Guid.NewGuid().ToString("N"),
                    savedAtUtc = DateTime.UtcNow.ToString("O"),
                    sceneName = SceneManager.GetActiveScene().name,
                    displayName = BuildDisplayName(time),
                    worldSeed = world.seed,
                    absoluteDayIndex = time.absoluteDayIndex,
                    cashCents = playerPortfolioManager != null ? playerPortfolioManager.OwnerCashCents : 0,
                    liquidCashCents = playerPortfolioManager != null ? playerPortfolioManager.OwnerCashCents : 0,
                    netWorthCents = playerPortfolioManager != null ? playerPortfolioManager.NetWorthCents : 0,
                    businessCashCents = playerPortfolioManager != null ? playerPortfolioManager.BusinessCashCents : 0,
                    debtLiabilityCents = playerPortfolioManager != null ? playerPortfolioManager.DebtLiabilityCents : 0,
                    watchlistedAcquisitionCount = acquisitionMarket != null && acquisitionMarket.WatchedListingIds != null ? acquisitionMarket.WatchedListingIds.Count : 0,
                    activeAcquisitionCount = acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Count : 0,
                    tentativeAcquisitionCount = acquisitionMarket != null ? CountTentativeDeals(acquisitionMarket) : 0,
                    ownedLandCount = acquisitionMarket != null ? acquisitionMarket.OwnedLandCount : 0,
                    ownedBusinessCount = acquisitionMarket != null ? acquisitionMarket.OwnedBusinessCount : 0,
                    wealthHeadline = playerPortfolioManager != null ? playerPortfolioManager.BuildWealthHeadline() : string.Empty,
                    liquidityPressureSummary = playerPortfolioManager != null ? playerPortfolioManager.BuildLiquidityPressureSummary() : string.Empty,
                    ownerCommitmentClimate = playerPortfolioManager != null
                    ? playerPortfolioManager.BuildOwnerCommitmentClimateSummary(
                    0,
                    0,
                    0,
                    acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Count : 0,
                    acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Sum(d => d != null ? Mathf.Max(0, d.stalledReviewCount) : 0) : 0)
                    : string.Empty,
                    ownerExecutionLoad = playerPortfolioManager != null
                    ? playerPortfolioManager.BuildExecutionLoadSummary(
                    acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Count : 0,
                    acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Sum(d => d != null ? Mathf.Max(0, d.stalledReviewCount) : 0) : 0)
                    : string.Empty,
                    acquisitionReadinessHeadline = acquisitionMarket != null ? acquisitionMarket.BuildAcquisitionReadinessHeadline() : string.Empty,
                    acquisitionProcessLedger = acquisitionMarket != null ? acquisitionMarket.BuildAcquisitionProcessLedgerSummary() : string.Empty,
                    acquisitionDecisionClimate = acquisitionMarket != null ? acquisitionMarket.BuildAcquisitionDecisionClimateSummary() : string.Empty,
                    acquisitionActionChecklist = acquisitionMarket != null
                    ? (!string.IsNullOrWhiteSpace(acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land))
                    ? acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land)
                    : acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Businesses))
                    : string.Empty,
                    acquisitionWeeklyReview = acquisitionMarket != null ? acquisitionMarket.BuildAcquisitionWeeklyReviewSummary() : string.Empty,
                    selectedLeadReadiness = acquisitionMarket != null
                    ? (!string.IsNullOrWhiteSpace(acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land))
                    ? acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land)
                    : acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Businesses))
                    : string.Empty,
                    selectedLeadRunway = acquisitionMarket != null
                    ? (!string.IsNullOrWhiteSpace(acquisitionMarket.BuildSelectedLeadRunwaySummary(AcquisitionMarketSection.Land))
                    ? acquisitionMarket.BuildSelectedLeadRunwaySummary(AcquisitionMarketSection.Land)
                    : acquisitionMarket.BuildSelectedLeadRunwaySummary(AcquisitionMarketSection.Businesses))
                    : string.Empty,
                    selectedLeadExecutionLoad = acquisitionMarket != null
                    ? (!string.IsNullOrWhiteSpace(acquisitionMarket.BuildSelectedLeadExecutionLoadSummary(AcquisitionMarketSection.Land))
                    ? acquisitionMarket.BuildSelectedLeadExecutionLoadSummary(AcquisitionMarketSection.Land)
                    : acquisitionMarket.BuildSelectedLeadExecutionLoadSummary(AcquisitionMarketSection.Businesses))
                    : string.Empty,
                    townSettlementHeadline = populationManager != null ? populationManager.BuildTownSettlementHeadline() : string.Empty,
                    townCommerceHeadline = sharedBusinessRuntime != null ? sharedBusinessRuntime.BuildTownCommerceHeadline() : string.Empty,
                    townCommerceLedger = sharedBusinessRuntime != null ? sharedBusinessRuntime.BuildTownCommerceLedgerSummary() : string.Empty,
                    townServicePressureHeadline = sharedBusinessRuntime != null ? sharedBusinessRuntime.BuildTownServicePressureHeadline() : string.Empty,
                    townDecisionClimate = sharedBusinessRuntime != null ? sharedBusinessRuntime.BuildTownDecisionClimateHeadline() : string.Empty
                }
            };

            // CLN-1: persist the HF-1 entity-ID cursors and the HF-2/HF-4 population
            // sections (animals, cohorts, egg batches, household ledgers) from the
            // systems hub. Legacy nextPersonId/nextHouseholdId/nextBuildingId fields
            // remain the authority for their kinds (EntityIdSaveAdapter).
            if (systemsHub != null)
            {
                EntityIdSaveAdapter.WriteCursors(
                    save,
                    systemsHub.Ids,
                    save.population != null ? save.population.nextPersonId : 0,
                    save.population != null ? save.population.nextHouseholdId : 0,
                    0);

                if (save.population != null)
                {
                    save.population.animals.Clear();
                    save.population.historicalAnimals.Clear();
                    save.population.cohorts.Clear();
                    save.population.eggBatches.Clear();
                    save.population.householdLedgers.Clear();
                    save.population.householdInventories.Clear();
                    save.population.mealRecords.Clear();
                    save.population.missedMeals.Clear();
                    save.population.mealPreparations.Clear();
                    save.population.purchasingNeeds.Clear();
                    systemsHub.ExportAnimalState(
                        save.population.animals,
                        save.population.historicalAnimals,
                        save.population.cohorts,
                        save.population.eggBatches);
                    systemsHub.ExportHouseholdLedgers(save.population.householdLedgers);
                    // Phase B: persist the real-consumption authorities.
                    systemsHub.ExportHouseholdInventories(save.population.householdInventories);
                    systemsHub.ExportMealLog(
                        save.population.mealRecords,
                        save.population.missedMeals,
                        save.population.mealPreparations);
                    systemsHub.ExportPurchasingNeeds(save.population.purchasingNeeds);
                }
            }

            return save;
        }

        private bool CanSave(out string message)
        {
            if (townWorld == null)
            {
                message = "Cannot save: TownWorldController is missing.";
                return false;
            }

            if (townWorld.Grid == null)
            {
                message = "Cannot save: town world has not been generated.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static string BuildDisplayName(TimeSaveDto time)
        {
            return time != null
            ? $"Day {time.absoluteDayIndex}, {DateTime.Now:g}"
            : DateTime.Now.ToString("g");
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            civicFoundation ??= FindAnyObjectByType<CivicFoundationManager>();
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            townPulseRuntime ??= FindAnyObjectByType<TownPulseRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
            playerPortfolioManager ??= FindAnyObjectByType<PlayerPortfolioManager>();
            populationPathingDirector ??= FindAnyObjectByType<PopulationPathingDirector>();
            managementPanel ??= FindAnyObjectByType<GeneralStorePanelController>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
            firstSessionGuidance ??= FindAnyObjectByType<FirstSessionGuidanceManager>();
            bootstrapper ??= FindAnyObjectByType<FirstLedgerSliceBootstrapper>();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();

            if (systemsHub == null)
            {
                GameObject systemsObject = new("Simulation Systems Hub");
                systemsHub = systemsObject.AddComponent<SimulationSystemsHub>();
            }

            if (sharedBusinessRuntime == null)
            {
                GameObject runtimeObject = new("Shared Business Runtime");
                sharedBusinessRuntime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
            }

            if (townPulseRuntime == null)
            {
                GameObject pulseObject = new("Town Pulse Runtime");
                townPulseRuntime = pulseObject.AddComponent<TownPulseRuntimeManager>();
            }

            if (logisticsRuntime == null)
            {
                logisticsRuntime = LogisticsRuntimeManager.FindOrCreate();
            }

            if (civicFoundation == null)
            {
                GameObject civicObject = new("Civic Foundation");
                civicFoundation = civicObject.AddComponent<CivicFoundationManager>();
            }

            if (playerDebtManager == null)
            {
                GameObject debtObject = new("Player Debt Manager");
                playerDebtManager = debtObject.AddComponent<PlayerDebtManager>();
            }

            if (playerPortfolioManager == null)
            {
                GameObject portfolioObject = new("Player Portfolio Manager");
                playerPortfolioManager = portfolioObject.AddComponent<PlayerPortfolioManager>();
            }

            townPulseRuntime.Configure(timeManager, hudController);
            ConfigurePortfolioManager();
            playerDebtManager.Configure(timeManager, storeRuntime, acquisitionMarket, sharedBusinessRuntime, playerPortfolioManager);
            playerDebtManager.AttachFinancialAuthority(systemsHub?.FinancialObligations, systemsHub?.Ids);
        }


        private string BuildOperationStatusMessage(string lead, params string[] extraDetails)
        {
            List<string> parts = new() { lead };
            if (extraDetails != null)
            {
                for (int i = 0; i < extraDetails.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(extraDetails[i]))
                    {
                        parts.Add(extraDetails[i]);
                    }
                }
            }

            if (playerPortfolioManager != null)
            {
                string wealth = playerPortfolioManager.BuildWealthHeadline();
                if (!string.IsNullOrWhiteSpace(wealth))
                {
                    parts.Add(wealth);
                }

                string progress = playerPortfolioManager.BuildWealthGoalProgressSummary();
                if (!string.IsNullOrWhiteSpace(progress))
                {
                    parts.Add(progress);
                }

                string saveRead = playerPortfolioManager.BuildWealthSaveReadSummary();
                if (!string.IsNullOrWhiteSpace(saveRead))
                {
                    parts.Add(saveRead);
                }

                string liquidity = playerPortfolioManager.BuildLiquidityPressureSummary();
                if (!string.IsNullOrWhiteSpace(liquidity))
                {
                    parts.Add(liquidity);
                }

                int activeDeals = acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Count : 0;
                int stalledDeals = acquisitionMarket != null ? acquisitionMarket.ActiveDeals.Sum(d => d != null ? Mathf.Max(0, d.stalledReviewCount) : 0) : 0;
                string ownerClimate = playerPortfolioManager.BuildOwnerCommitmentClimateSummary(0, 0, 0, activeDeals, stalledDeals);
                if (!string.IsNullOrWhiteSpace(ownerClimate))
                {
                    parts.Add(ownerClimate);
                }

                string executionLoad = playerPortfolioManager.BuildExecutionLoadSummary(activeDeals, stalledDeals);
                if (!string.IsNullOrWhiteSpace(executionLoad))
                {
                    parts.Add(executionLoad);
                }
            }

            if (populationManager != null)
            {
                string settlement = populationManager.BuildTownSettlementHeadline();
                if (!string.IsNullOrWhiteSpace(settlement))
                {
                    parts.Add(settlement);
                }
            }

            if (sharedBusinessRuntime != null)
            {
                string commerce = sharedBusinessRuntime.BuildTownCommerceHeadline();
                if (!string.IsNullOrWhiteSpace(commerce))
                {
                    parts.Add(commerce);
                }

                string commerceLedger = sharedBusinessRuntime.BuildTownCommerceLedgerSummary();
                if (!string.IsNullOrWhiteSpace(commerceLedger))
                {
                    parts.Add(commerceLedger);
                }

                string localOrders = sharedBusinessRuntime.LastWeeklyRecurringLocalOrderSummary;
                if (!string.IsNullOrWhiteSpace(localOrders))
                {
                    parts.Add(localOrders);
                }

                string processClimate = sharedBusinessRuntime.BuildTownProcessClimateSummary();
                if (!string.IsNullOrWhiteSpace(processClimate))
                {
                    parts.Add(processClimate);
                }
            }

            if (acquisitionMarket != null)
            {
                string readiness = acquisitionMarket.BuildAcquisitionReadinessHeadline();
                if (!string.IsNullOrWhiteSpace(readiness))
                {
                    parts.Add(readiness);
                }

                string selectedChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land);
                if (string.IsNullOrWhiteSpace(selectedChecklist))
                {
                    selectedChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Businesses);
                }
                if (!string.IsNullOrWhiteSpace(selectedChecklist))
                {
                    parts.Add(selectedChecklist);
                }

                string decisionClimate = acquisitionMarket.BuildAcquisitionDecisionClimateSummary();
                if (!string.IsNullOrWhiteSpace(decisionClimate))
                {
                    parts.Add(decisionClimate);
                }

                string selectedLead = acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land);
                if (string.IsNullOrWhiteSpace(selectedLead))
                {
                    selectedLead = acquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Businesses);
                }
                if (!string.IsNullOrWhiteSpace(selectedLead))
                {
                    parts.Add(selectedLead);
                }

                string watch = acquisitionMarket.BuildWatchlistSummaryText();
                if (!string.IsNullOrWhiteSpace(watch))
                {
                    parts.Add(watch);
                }

                string pipeline = acquisitionMarket.BuildDealPipelineSummary();
                if (!string.IsNullOrWhiteSpace(pipeline))
                {
                    parts.Add(pipeline);
                }
            }

            return string.Join("\n", parts);
        }

        private string BuildPersistenceStatusSummary()
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(lastNormalizationSummary))
            {
                parts.Add(lastNormalizationSummary);
            }

            if (!string.IsNullOrWhiteSpace(lastReferenceResolutionSummary))
            {
                parts.Add(lastReferenceResolutionSummary);
            }

            if (!string.IsNullOrWhiteSpace(lastSaveIntegritySummary))
            {
                parts.Add(lastSaveIntegritySummary);
            }

            if (!string.IsNullOrWhiteSpace(lastRestoreDependencySummary))
            {
                parts.Add(lastRestoreDependencySummary);
            }

            if (!string.IsNullOrWhiteSpace(lastPostRestoreConsistencySummary))
            {
                parts.Add(lastPostRestoreConsistencySummary);
            }

            return string.Join(" ", parts);
        }

        private void RecordPersistenceDiagnostics(
        int saveFormatVersion,
        SaveNormalizationReport report,
        SaveReferenceResolver resolver = null,
        SaveIntegrityReport integrityReport = null,
        SaveRestoreDependencyReport restoreDependencyReport = null,
        SavePostRestoreConsistencyReport postRestoreConsistencyReport = null)
        {
            lastNormalizationRepairCount = report != null ? report.TotalRepairs : 0;
            lastNormalizationSummary = report != null
            ? report.BuildSummary(saveFormatVersion)
            : "No save normalization report recorded.";
            lastReferenceResolutionSummary = resolver != null
            ? resolver.BuildDiagnosticsSummary()
            : "Reference resolution not used during this operation.";
            RecordSaveIntegrityDiagnostics(integrityReport);
            RecordRestoreDependencyDiagnostics(restoreDependencyReport);
            RecordPostRestoreConsistencyDiagnostics(postRestoreConsistencyReport);
        }

        private void RecordSaveIntegrityDiagnostics(SaveIntegrityReport integrityReport)
        {
            lastSaveIntegritySummary = integrityReport != null
            ? integrityReport.BuildSummary("Save integrity")
            : "Save integrity audit not run during this operation.";
        }

        private void RecordRestoreDependencyDiagnostics(SaveRestoreDependencyReport restoreDependencyReport)
        {
            lastRestoreDependencySummary = restoreDependencyReport != null
            ? restoreDependencyReport.BuildSummary()
            : "Restore dependency audit not run during this operation.";
        }

        private void RecordPostRestoreConsistencyDiagnostics(SavePostRestoreConsistencyReport postRestoreConsistencyReport)
        {
            lastPostRestoreConsistencySummary = postRestoreConsistencyReport != null
            ? postRestoreConsistencyReport.BuildSummary()
            : "Post-load consistency audit not run during this operation.";
        }

        private bool ReportOperationFailure(string operationMessage, string hudTitle, string hudMessage, string feedbackContext, bool logAsError, float hudDurationSeconds)
        {
            lastStatus = operationMessage;
            // Keep the HUD strip concise even when lastStatus stores the fuller multi-system summary for later inspection.
            PushHudOperationStatus(hudTitle, string.IsNullOrWhiteSpace(hudMessage) ? operationMessage : hudMessage, true, hudDurationSeconds);
            LLFeedbackService.Play(LLFeedbackKind.Warning, feedbackContext);
            if (logAsError)
            {
                Debug.LogError(lastStatus, this);
            }
            else
            {
                Debug.LogWarning(lastStatus, this);
            }

            return false;
        }

        private bool ReportOperationSuccess(string operationMessage, string hudTitle, string hudMessage, string feedbackContext, float hudDurationSeconds)
        {
            lastStatus = operationMessage;
            PushHudOperationStatus(hudTitle, string.IsNullOrWhiteSpace(hudMessage) ? operationMessage : hudMessage, false, hudDurationSeconds);
            LLFeedbackService.Play(LLFeedbackKind.Notice, feedbackContext);
            Debug.Log(lastStatus, this);
            return true;
        }

        private void PushHudOperationStatus(string title, string message, bool warning, float durationSeconds)
        {
            if (hudController == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            hudController.SetSystemOperationStatus(title, message, warning, durationSeconds);
        }

        private string BuildHudSaveSummary(SaveIntegrityReport integrityReport)
        {
            List<string> parts = new();
            if (timeManager != null)
            {
                parts.Add($"Default slot updated for {timeManager.GetReadableDate()} at {timeManager.GetReadableClock()}.");
            }
            else
            {
                parts.Add("Default slot updated.");
            }

            if (integrityReport != null && integrityReport.WarningCount > 0)
            {
                parts.Add($"Integrity audit recorded {integrityReport.WarningCount} warning(s).");
            }

            return string.Join(" ", parts);
        }

        private static string BuildHudLoadTitle(int saveFormatVersion, SaveNormalizationReport report, bool loadedFromBackup)
        {
            if (loadedFromBackup)
            {
                return "Backup Save Loaded";
            }

            return saveFormatVersion < CurrentFormatVersion || (report != null && report.HasRepairs)
            ? "Legacy Save Loaded"
            : "Ledger Loaded";
        }

        private string BuildHudLoadSummary(
        int saveFormatVersion,
        SaveNormalizationReport report,
        SaveReferenceResolver resolver,
        SaveIntegrityReport integrityReport,
        SaveRestoreDependencyReport restoreDependencyReport,
        SavePostRestoreConsistencyReport postRestoreConsistencyReport,
        bool loadedFromBackup)
        {
            List<string> parts = new();
            if (loadedFromBackup)
            {
                parts.Add("Default slot was not usable; restored from the preserved backup slot.");
            }
            else if (saveFormatVersion < CurrentFormatVersion || (report != null && report.HasRepairs))
            {
                parts.Add(report != null && report.HasRepairs
                ? "Default slot restored after repairing missing legacy sections."
                : $"Legacy v{saveFormatVersion} slot restored without structural repair.");
            }
            else
            {
                parts.Add("Default slot restored cleanly.");
            }

            if (resolver != null && resolver.TotalReferenceMissCount > 0)
            {
                parts.Add("Some saved references could not be matched exactly; review restore diagnostics if world state looks wrong.");
            }

            if (integrityReport != null && integrityReport.WarningCount > 0)
            {
                parts.Add($"Save integrity audit reported {integrityReport.WarningCount} warning(s).");
            }

            if (restoreDependencyReport != null && restoreDependencyReport.WarningCount > 0)
            {
                parts.Add($"Restore dependency audit reported {restoreDependencyReport.WarningCount} warning(s).");
            }

            if (postRestoreConsistencyReport != null && postRestoreConsistencyReport.WarningCount > 0)
            {
                parts.Add($"Post-load audit reported {postRestoreConsistencyReport.WarningCount} warning(s).");
            }

            return string.Join(" ", parts);
        }

        private string BuildHudLoadFailureSummary(
        string failureMessage,
        SaveNormalizationReport report,
        SaveReferenceResolver resolver,
        SaveIntegrityReport integrityReport,
        SaveRestoreDependencyReport restoreDependencyReport)
        {
            List<string> parts = new();
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                parts.Add(failureMessage.Trim());
            }

            if (report != null && report.HasRepairs)
            {
                parts.Add("The save required legacy normalization before the failure point.");
            }

            if (resolver != null && resolver.TotalReferenceMissCount > 0)
            {
                parts.Add("Some saved references also failed to match current definitions.");
            }

            if (integrityReport != null && integrityReport.WarningCount > 0)
            {
                parts.Add("Save integrity warnings were present before the failure point.");
            }

            if (restoreDependencyReport != null && restoreDependencyReport.WarningCount > 0)
            {
                parts.Add("Restore dependency warnings were present before the failure point.");
            }

            return string.Join(" ", parts);
        }

        private string BuildSlotInspectionSummary(string path, string slotLabel, LandLedgersSaveGameDto save, SaveNormalizationReport normalizationReport, SaveIntegrityReport integrityReport)
        {
            SaveManifestDto manifest = save != null ? save.manifest : null;
            if (manifest == null)
            {
                return $"{slotLabel} at {path} is readable, but it has no manifest.";
            }

            List<string> parts = new()
            {
                $"Inspected {slotLabel} at {path}.",
                $"Format v{manifest.formatVersion}; {manifest.displayName}; Day {manifest.absoluteDayIndex}; Seed {manifest.worldSeed}.",
                $"Liquid Cash {FormatCents(manifest.liquidCashCents)} | Net Worth {FormatCents(manifest.netWorthCents)} | Debt {FormatCents(manifest.debtLiabilityCents)}.",
                $"Owned land {manifest.ownedLandCount}; owned businesses {manifest.ownedBusinessCount}; active deals {manifest.activeAcquisitionCount}."
            };

            if (!string.IsNullOrWhiteSpace(manifest.wealthHeadline))
            {
                parts.Add(manifest.wealthHeadline);
            }

            if (normalizationReport != null && normalizationReport.HasRepairs)
            {
                parts.Add(normalizationReport.BuildSummary(manifest.formatVersion));
            }

            if (integrityReport != null)
            {
                parts.Add(integrityReport.BuildSummary("Save integrity"));
            }

            return string.Join(" ", parts);
        }

        private static LandLedgersSaveGameDto NormalizeLoadedSave(LandLedgersSaveGameDto save, SaveNormalizationReport report)
        {
            if (save == null)
            {
                return null;
            }

            report ??= new SaveNormalizationReport();

            EnsureTopLevel(ref save.manifest, report, "Manifest");
            EnsureTopLevel(ref save.world, report, "World");
            EnsureTopLevel(ref save.time, report, "Time");
            EnsureTopLevel(ref save.population, report, "Population");
            EnsureTopLevel(ref save.businesses, report, "Businesses");
            EnsureTopLevel(ref save.acquisition, report, "Acquisition");
            EnsureTopLevel(ref save.civic, report, "Civic");
            EnsureTopLevel(ref save.civic.townHall, report, "Town hall civic state");
            EnsureTopLevel(ref save.civic.schoolhouse, report, "Schoolhouse civic state");
            EnsureTopLevel(ref save.debt, report, "Debt");
            EnsureTopLevel(ref save.portfolio, report, "Portfolio");
            EnsureTopLevel(ref save.firstSessionGuidance, report, "First session guidance");
            EnsureTopLevel(ref save.world.regionalResources, report, "Regional resources");

            EnsureList(ref save.world.plots, report, "World plots");
            EnsureList(ref save.world.buildings, report, "World buildings");
            EnsureList(ref save.world.roadCells, report, "World road cells");
            EnsureList(ref save.world.regionalResources.districts, report, "Regional resource districts");
            EnsureList(ref save.world.regionalResources.remoteSites, report, "Regional remote resource sites");
            EnsureList(ref save.population.people, report, "Population people");
            EnsureList(ref save.population.households, report, "Population households");
            EnsureList(ref save.population.validationMessages, report, "Population validation messages");
            EnsureList(ref save.population.rentalProperties, report, "Rental properties");
            EnsureList(ref save.population.rentalApplicants, report, "Rental applicants");
            EnsureList(ref save.businesses.sharedBusinesses, report, "Shared businesses");
            EnsureList(ref save.businesses.localRecurringOrderRelationships, report, "Recurring local order relationships");
            EnsureList(ref save.businesses.transferAgreements, report, "Business transfer agreements");
            EnsureTopLevel(ref save.businesses.logistics, report, "Logistics state");
            EnsureList(ref save.businesses.logistics.shipments, report, "Logistics shipments");
            EnsureTopLevel(ref save.businesses.townPulse, report, "Town pulse state");
            EnsureList(ref save.businesses.townPulse.lastDailyCategoryResults, report, "Town pulse category results");
            EnsureTopLevel(ref save.businesses.deepGeneralStore, report, "Deep general store state");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness, report, "Deep general store business");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.owner, report, "Deep general store owner");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.cashTransferRule, report, "Deep general store cash transfer rule");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.businessReputation, report, "Deep general store reputation");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.runtime, report, "Deep general store runtime");
            EnsureList(ref save.businesses.deepGeneralStore.currentBusiness.runtime.categoryStock, report, "Deep general store category stock");
            EnsureList(ref save.businesses.deepGeneralStore.currentBusiness.runtime.workerSlots, report, "Deep general store worker slots");
            EnsureList(ref save.businesses.deepGeneralStore.currentBusiness.runtime.lastSuspendedPayrollWorkerIds, report, "Deep general store suspended payroll worker ids");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.runtime.mainFocus, report, "Deep general store focus");
            EnsureTopLevel(ref save.businesses.deepGeneralStore.currentBusiness.runtime.capacity, report, "Deep general store capacity");
            EnsureList(ref save.businesses.deepGeneralStore.currentBusiness.businessReputation.stockoutMemory, report, "Deep general store stockout memory");
            EnsureList(ref save.acquisition.opportunityWindows, report, "Acquisition opportunity windows");
            EnsureList(ref save.acquisition.ownedPlotIds, report, "Owned plot ids");
            EnsureList(ref save.acquisition.ownedBusinessBuildingIds, report, "Owned business building ids");
            EnsureList(ref save.acquisition.activeDeals, report, "Active acquisition deals");
            EnsureList(ref save.acquisition.watchedListingIds, report, "Watched acquisition listings");
            EnsureList(ref save.acquisition.landAppreciations, report, "Land appreciations");
            EnsureList(ref save.acquisition.constructionSupportNodes, report, "Construction support nodes");
            EnsureList(ref save.acquisition.constructionProjects, report, "Construction projects");
            EnsureTopLevel(ref save.acquisition.ownershipAptitude, report, "Ownership aptitude");
            EnsureTopLevel(ref save.debt.application, report, "Debt application");
            EnsureTopLevel(ref save.debt.reputation, report, "Debt reputation");
            EnsureList(ref save.debt.activeLoans, report, "Active debt loans");
            EnsureList(ref save.portfolio.distributionCheckpoints, report, "Portfolio distribution checkpoints");

            for (int i = 0; i < save.world.buildings.Count; i++)
            {
                if (save.world.buildings[i] == null)
                {
                    save.world.buildings[i] = new BuildingSaveDto();
                    report.NoteEntryRepair($"World building entry {i}");
                }

                EnsureList(ref save.world.buildings[i].anchors, report, $"World building {i} anchors");
            }

            for (int i = 0; i < save.population.households.Count; i++)
            {
                HouseholdSaveDto household = save.population.households[i];
                if (household == null)
                {
                    household = new HouseholdSaveDto();
                    report.NoteEntryRepair($"Household entry {i}");
                }

                EnsureList(ref household.memberIds, report, $"Household {i} members");
                EnsureList(ref household.upgrades, report, $"Household {i} upgrades");
                EnsureList(ref household.reserves, report, $"Household {i} reserves");
                EnsureList(ref household.boarderPersonIds, report, $"Household {i} boarders");
                save.population.households[i] = household;
            }

            for (int i = 0; i < save.businesses.logistics.shipments.Count; i++)
            {
                LogisticsShipmentSaveDto shipment = save.businesses.logistics.shipments[i];
                if (shipment == null)
                {
                    shipment = new LogisticsShipmentSaveDto();
                    report.NoteEntryRepair($"Logistics shipment entry {i}");
                }

                EnsureTopLevel(ref shipment.routePlan, report, $"Logistics shipment {i} route plan");
                EnsureTopLevel(ref shipment.routePlan.visibleStart, report, $"Logistics shipment {i} visible start");
                EnsureTopLevel(ref shipment.routePlan.visibleEnd, report, $"Logistics shipment {i} visible end");
                EnsureList(ref shipment.routePlan.visiblePath, report, $"Logistics shipment {i} visible path");
                save.businesses.logistics.shipments[i] = shipment;
            }

            for (int i = 0; i < save.businesses.localRecurringOrderRelationships.Count; i++)
            {
                LocalRecurringOrderRelationshipSaveDto relationship = save.businesses.localRecurringOrderRelationships[i];
                if (relationship == null)
                {
                    relationship = new LocalRecurringOrderRelationshipSaveDto();
                    report.NoteEntryRepair($"Recurring local order relationship entry {i}");
                }
                save.businesses.localRecurringOrderRelationships[i] = relationship;
            }

            for (int i = 0; i < save.businesses.sharedBusinesses.Count; i++)
            {
                BusinessInstanceSaveDto business = save.businesses.sharedBusinesses[i];
                if (business == null)
                {
                    business = new BusinessInstanceSaveDto();
                    report.NoteEntryRepair($"Shared business entry {i}");
                }

                EnsureTopLevel(ref business.owner, report, $"Shared business {i} owner");
                EnsureTopLevel(ref business.cashTransferRule, report, $"Shared business {i} cash transfer rule");
                EnsureTopLevel(ref business.businessReputation, report, $"Shared business {i} reputation");
                EnsureList(ref business.businessReputation.stockoutMemory, report, $"Shared business {i} stockout memory");
                EnsureTopLevel(ref business.runtime, report, $"Shared business {i} runtime");
                EnsureList(ref business.runtime.categoryStock, report, $"Shared business {i} category stock");
                EnsureList(ref business.runtime.workerSlots, report, $"Shared business {i} worker slots");
                EnsureList(ref business.runtime.lastSuspendedPayrollWorkerIds, report, $"Shared business {i} suspended payroll worker ids");
                EnsureTopLevel(ref business.runtime.mainFocus, report, $"Shared business {i} main focus");
                EnsureTopLevel(ref business.runtime.capacity, report, $"Shared business {i} capacity");
                save.businesses.sharedBusinesses[i] = business;
            }

            return save;
        }

        private static void EnsureTopLevel<T>(ref T value, SaveNormalizationReport report, string label) where T : class, new()
        {
            if (value != null)
            {
                return;
            }

            value = new T();
            report?.NoteSectionRepair(label);
        }

        private static void EnsureList<T>(ref List<T> list, SaveNormalizationReport report, string label)
        {
            if (list != null)
            {
                return;
            }

            list = new List<T>();
            report?.NoteCollectionRepair(label);
        }

        private void PrepareManagersForRestore()
        {
            townPulseRuntime?.Configure(timeManager, hudController);
            ConfigurePortfolioManager();
            playerDebtManager?.Configure(timeManager, storeRuntime, acquisitionMarket, sharedBusinessRuntime, playerPortfolioManager);
            playerDebtManager?.AttachFinancialAuthority(systemsHub?.FinancialObligations, systemsHub?.Ids);
            ConfigureFirstSessionGuidance();
        }

        private SaveRestoreDependencyReport BuildRestoreDependencyReport(LandLedgersSaveGameDto save)
        {
            SaveRestoreDependencyReport report = new();
            if (save == null)
            {
                report.AddWarning("Loaded save was null before restore.");
                return report;
            }

            if (save.world != null && townWorld == null)
            {
                report.AddWarning("World state is present but TownWorldController is missing.");
            }

            if (save.time != null && timeManager == null)
            {
                report.AddWarning("Time state is present but TimeManager is missing.");
            }

            if (save.population != null && populationManager == null)
            {
                report.AddWarning("Population state is present but PopulationManager is missing.");
            }

            if (save.civic != null && civicFoundation == null)
            {
                report.AddWarning("Civic holdings are present but CivicFoundationManager is missing.");
            }

            if (save.businesses != null)
            {
                if (save.businesses.deepGeneralStore != null && storeRuntime == null)
                {
                    report.AddWarning("Deep General Store state is present but GeneralStoreRuntimeManager is missing.");
                }

                if ((save.businesses.sharedBusinesses?.Count ?? 0) > 0 && sharedBusinessRuntime == null)
                {
                    report.AddWarning("Shared business state is present but SharedBusinessRuntimeManager is missing.");
                }

                if ((save.businesses.localRecurringOrderRelationships?.Count ?? 0) > 0 && sharedBusinessRuntime == null)
                {
                    report.AddWarning("Recurring local orders are present but SharedBusinessRuntimeManager is missing.");
                }

                if ((save.businesses.logistics?.shipments?.Count ?? 0) > 0 && logisticsRuntime == null)
                {
                    report.AddWarning("Logistics shipments are present but LogisticsRuntimeManager is missing.");
                }

                if (save.businesses.townPulse != null && townPulseRuntime == null)
                {
                    report.AddWarning("Town pulse state is present but TownPulseRuntimeManager is missing.");
                }
            }

            if (save.acquisition != null && acquisitionMarket == null)
            {
                report.AddWarning("Acquisition state is present but AcquisitionMarketManager is missing.");
            }

            if (save.portfolio != null && save.portfolio.initialized && playerPortfolioManager == null)
            {
                report.AddWarning("Portfolio state is initialized but PlayerPortfolioManager is missing.");
            }

            if (save.debt != null && playerDebtManager == null)
            {
                report.AddWarning("Debt state is present but PlayerDebtManager is missing.");
            }

            report.SetObservedOrder("World -> time/population/civic/store -> shared businesses -> logistics -> portfolio/debt -> town pulse/acquisition/guidance/UI.");
            return report;
        }

        private SavePostRestoreConsistencyReport TryBuildPostRestoreConsistencyReport(LandLedgersSaveGameDto loadedSave)
        {
            SavePostRestoreConsistencyReport report = new();
            try
            {
                LandLedgersSaveGameDto snapshot = BuildSaveDto();
                CompareLoadedToSnapshot(loadedSave, snapshot, report);
            }
            catch (Exception ex)
            {
                report.AddWarning($"Post-load consistency audit could not capture a runtime snapshot: {ex.Message}");
            }

            return report;
        }

        private static void CompareLoadedToSnapshot(LandLedgersSaveGameDto loaded, LandLedgersSaveGameDto snapshot, SavePostRestoreConsistencyReport report)
        {
            if (loaded == null || snapshot == null)
            {
                report.AddWarning("Post-load consistency audit could not compare empty save data.");
                return;
            }

            CompareValue("world seed", loaded.world?.seed ?? 0, snapshot.world?.seed ?? 0, report);
            CompareValue("absolute day index", loaded.time?.absoluteDayIndex ?? 0, snapshot.time?.absoluteDayIndex ?? 0, report);
            CompareValue("plot count", loaded.world?.plots?.Count ?? 0, snapshot.world?.plots?.Count ?? 0, report);
            CompareValue("building count", loaded.world?.buildings?.Count ?? 0, snapshot.world?.buildings?.Count ?? 0, report);
            CompareValue("person count", loaded.population?.people?.Count ?? 0, snapshot.population?.people?.Count ?? 0, report);
            CompareValue("household count", loaded.population?.households?.Count ?? 0, snapshot.population?.households?.Count ?? 0, report);
            CompareValue("shared business count", loaded.businesses?.sharedBusinesses?.Count ?? 0, snapshot.businesses?.sharedBusinesses?.Count ?? 0, report);
            CompareValue("recurring order relationship count", loaded.businesses?.localRecurringOrderRelationships?.Count ?? 0, snapshot.businesses?.localRecurringOrderRelationships?.Count ?? 0, report);
            CompareValue("logistics shipment count", loaded.businesses?.logistics?.shipments?.Count ?? 0, snapshot.businesses?.logistics?.shipments?.Count ?? 0, report);
            CompareValue("active acquisition deal count", loaded.acquisition?.activeDeals?.Count ?? 0, snapshot.acquisition?.activeDeals?.Count ?? 0, report);
            CompareValue("active loan count", loaded.debt?.activeLoans?.Count ?? 0, snapshot.debt?.activeLoans?.Count ?? 0, report);

            CompareStringSet("business instance ids", CollectBusinessIds(loaded), CollectBusinessIds(snapshot), report);
            CompareStringSet("recurring relationship ids", CollectRelationshipIds(loaded.businesses), CollectRelationshipIds(snapshot.businesses), report);
            CompareStringSet("shipment ids", CollectShipmentIds(loaded.businesses?.logistics), CollectShipmentIds(snapshot.businesses?.logistics), report);
            CompareStringSet("active acquisition listing ids", CollectDealListingIds(loaded.acquisition), CollectDealListingIds(snapshot.acquisition), report);
        }

        private static void CompareValue(string label, int expected, int actual, SavePostRestoreConsistencyReport report)
        {
            if (expected != actual)
            {
                report.AddWarning($"{label} changed during restore ({expected} -> {actual}).");
            }
        }

        private static void CompareStringSet(string label, HashSet<string> expected, HashSet<string> actual, SavePostRestoreConsistencyReport report)
        {
            if (expected == null || actual == null)
            {
                return;
            }

            if (!expected.SetEquals(actual))
            {
                report.AddWarning($"{label} changed during restore.");
            }
        }

        private static SaveIntegrityReport ValidateSaveDtoForWrite(LandLedgersSaveGameDto save)
        {
            SaveIntegrityReport report = ValidateSaveDtoCommon(save);
            if (save == null)
            {
                report.AddFatal("Save root is null.");
            }
            else
            {
                if (save.world == null)
                {
                    report.AddFatal("World state is missing.");
                }

                if (save.migrationManifest == null)
                {
                    report.AddFatal("Migration manifest is missing from save root.");
                }
                else if (!SaveMigrationEnvelope.ValidateMigrationManifest(save.migrationManifest, CurrentFormatVersion, out string manifestError))
                {
                    report.AddFatal($"Migration manifest is invalid: {manifestError}");
                }
            }

            return report;
        }

        private static SaveIntegrityReport ValidateSaveDtoForRestore(LandLedgersSaveGameDto save)
        {
            SaveIntegrityReport report = ValidateSaveDtoCommon(save);
            if (save != null && save.migrationManifest == null)
            {
                report.AddFatal("Migration manifest is missing from save root during restore.");
            }

            return report;
        }

        private static SaveIntegrityReport ValidateSaveDtoCommon(LandLedgersSaveGameDto save)
        {
            SaveIntegrityReport report = new();
            if (save == null)
            {
                report.AddFatal("Save root is null.");
                return report;
            }

            HashSet<int> plotIds = new();
            HashSet<int> buildingIds = new();
            HashSet<int> personIds = new();
            HashSet<int> householdIds = new();
            HashSet<string> businessInstanceIds = CollectBusinessIds(save);
            ValidateBusinessInstanceIds(save, report);

            ValidatePlots(save.world?.plots, plotIds, report);
            ValidateBuildings(save.world?.buildings, plotIds, buildingIds, report);
            ValidatePeople(save.population?.people, personIds, report);
            ValidateHouseholds(save.population?.households, householdIds, personIds, buildingIds, report);
            ValidateBusinessInstance(save.businesses?.deepGeneralStore?.currentBusiness, businessInstanceIds, buildingIds, report, "Deep General Store");
            if (save.businesses?.sharedBusinesses != null)
            {
                for (int i = 0; i < save.businesses.sharedBusinesses.Count; i++)
                {
                    ValidateBusinessInstance(save.businesses.sharedBusinesses[i], businessInstanceIds, buildingIds, report, $"Shared business {i}");
                }
            }

            ValidateRecurringOrderRelationships(save.businesses?.localRecurringOrderRelationships, businessInstanceIds, report);
            ValidateLogistics(save.businesses?.logistics, businessInstanceIds, report);
            ValidateAcquisition(save.acquisition, plotIds, buildingIds, report);
            ValidatePortfolio(save.portfolio, businessInstanceIds, report);
            return report;
        }

        private static void ValidatePlots(List<PlotSaveDto> plots, HashSet<int> plotIds, SaveIntegrityReport report)
        {
            if (plots == null)
            {
                report.AddWarning("Plot collection is missing.");
                return;
            }

            for (int i = 0; i < plots.Count; i++)
            {
                PlotSaveDto plot = plots[i];
                if (plot == null)
                {
                    report.AddWarning($"Plot entry {i} is null.");
                    continue;
                }

                if (!plotIds.Add(plot.id))
                {
                    report.AddWarning($"Duplicate plot id {plot.id}.");
                }
            }
        }

        private static void ValidateBuildings(List<BuildingSaveDto> buildings, HashSet<int> plotIds, HashSet<int> buildingIds, SaveIntegrityReport report)
        {
            if (buildings == null)
            {
                report.AddWarning("Building collection is missing.");
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingSaveDto building = buildings[i];
                if (building == null)
                {
                    report.AddWarning($"Building entry {i} is null.");
                    continue;
                }

                if (!buildingIds.Add(building.id))
                {
                    report.AddWarning($"Duplicate building id {building.id}.");
                }

                if (string.IsNullOrWhiteSpace(building.buildingDefinitionId))
                {
                    report.AddWarning($"Building {building.id} has no building definition id.");
                }

                if (building.plotId >= 0 && !plotIds.Contains(building.plotId))
                {
                    report.AddWarning($"Building {building.id} references missing plot {building.plotId}.");
                }
            }
        }

        private static void ValidatePeople(List<PersonSaveDto> people, HashSet<int> personIds, SaveIntegrityReport report)
        {
            if (people == null)
            {
                report.AddWarning("People collection is missing.");
                return;
            }

            for (int i = 0; i < people.Count; i++)
            {
                PersonSaveDto person = people[i];
                if (person == null)
                {
                    report.AddWarning($"Person entry {i} is null.");
                    continue;
                }

                if (!personIds.Add(person.id))
                {
                    report.AddWarning($"Duplicate person id {person.id}.");
                }
            }
        }

        private static void ValidateHouseholds(List<HouseholdSaveDto> households, HashSet<int> householdIds, HashSet<int> personIds, HashSet<int> buildingIds, SaveIntegrityReport report)
        {
            if (households == null)
            {
                report.AddWarning("Household collection is missing.");
                return;
            }

            for (int i = 0; i < households.Count; i++)
            {
                HouseholdSaveDto household = households[i];
                if (household == null)
                {
                    report.AddWarning($"Household entry {i} is null.");
                    continue;
                }

                if (!householdIds.Add(household.id))
                {
                    report.AddWarning($"Duplicate household id {household.id}.");
                }

                if (household.homeBuildingId >= 0 && !buildingIds.Contains(household.homeBuildingId))
                {
                    report.AddWarning($"Household {household.id} references missing home building {household.homeBuildingId}.");
                }

                if (household.memberIds != null)
                {
                    for (int j = 0; j < household.memberIds.Count; j++)
                    {
                        if (!personIds.Contains(household.memberIds[j]))
                        {
                            report.AddWarning($"Household {household.id} references missing member person {household.memberIds[j]}.");
                        }
                    }
                }
            }
        }

        private static void ValidateBusinessInstanceIds(LandLedgersSaveGameDto save, SaveIntegrityReport report)
        {
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            CheckBusinessInstanceId(save?.businesses?.deepGeneralStore?.currentBusiness, seen, report, "Deep General Store");
            if (save?.businesses?.sharedBusinesses == null)
            {
                return;
            }

            for (int i = 0; i < save.businesses.sharedBusinesses.Count; i++)
            {
                CheckBusinessInstanceId(save.businesses.sharedBusinesses[i], seen, report, $"Shared business {i}");
            }
        }

        private static void CheckBusinessInstanceId(BusinessInstanceSaveDto business, HashSet<string> seen, SaveIntegrityReport report, string label)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.instanceId))
            {
                return;
            }

            if (!seen.Add(business.instanceId))
            {
                report.AddWarning($"Duplicate business instance id {business.instanceId} on {label}.");
            }
        }

        private static void ValidateBusinessInstance(BusinessInstanceSaveDto business, HashSet<string> businessInstanceIds, HashSet<int> buildingIds, SaveIntegrityReport report, string label)
        {
            if (business == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(business.instanceId))
            {
                report.AddWarning($"{label} has no instance id.");
            }

            if (string.IsNullOrWhiteSpace(business.profileId))
            {
                report.AddWarning($"{label} has no profile id.");
            }

            if (business.assignedBuildingId >= 0 && !buildingIds.Contains(business.assignedBuildingId))
            {
                report.AddWarning($"{label} references missing building {business.assignedBuildingId}.");
            }

            if (business.owner == null)
            {
                report.AddWarning($"{label} is missing owner state.");
            }

            if (business.cashTransferRule == null)
            {
                report.AddWarning($"{label} is missing cash-transfer policy state.");
            }

            if (business.businessReputation == null)
            {
                report.AddWarning($"{label} is missing business reputation state.");
            }

            if (business.runtime == null)
            {
                report.AddWarning($"{label} is missing runtime state.");
            }
        }

        private static void ValidateRecurringOrderRelationships(List<LocalRecurringOrderRelationshipSaveDto> relationships, HashSet<string> businessInstanceIds, SaveIntegrityReport report)
        {
            if (relationships == null)
            {
                return;
            }

            HashSet<string> relationshipIds = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipSaveDto relationship = relationships[i];
                if (relationship == null)
                {
                    report.AddWarning($"Recurring order relationship entry {i} is null.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(relationship.relationshipId) && !relationshipIds.Add(relationship.relationshipId))
                {
                    report.AddWarning($"Duplicate recurring order relationship id {relationship.relationshipId}.");
                }

                if (!string.IsNullOrWhiteSpace(relationship.sellerInstanceId) && !businessInstanceIds.Contains(relationship.sellerInstanceId))
                {
                    report.AddWarning($"Recurring order {relationship.relationshipId} references missing seller business {relationship.sellerInstanceId}.");
                }

                if (!string.IsNullOrWhiteSpace(relationship.buyerInstanceId) && !businessInstanceIds.Contains(relationship.buyerInstanceId))
                {
                    report.AddWarning($"Recurring order {relationship.relationshipId} references missing buyer business {relationship.buyerInstanceId}.");
                }
            }
        }

        private static void ValidateLogistics(LogisticsRuntimeSaveDto logistics, HashSet<string> businessInstanceIds, SaveIntegrityReport report)
        {
            if (logistics?.shipments == null)
            {
                return;
            }

            HashSet<string> shipmentIds = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < logistics.shipments.Count; i++)
            {
                LogisticsShipmentSaveDto shipment = logistics.shipments[i];
                if (shipment == null)
                {
                    report.AddWarning($"Logistics shipment entry {i} is null.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(shipment.shipmentId))
                {
                    report.AddWarning($"Logistics shipment entry {i} has no shipment id.");
                }
                else if (!shipmentIds.Add(shipment.shipmentId))
                {
                    report.AddWarning($"Duplicate logistics shipment id {shipment.shipmentId}.");
                }

                if (!string.IsNullOrWhiteSpace(shipment.sourceBusinessInstanceId) && !businessInstanceIds.Contains(shipment.sourceBusinessInstanceId))
                {
                    report.AddWarning($"Shipment {shipment.shipmentId} references missing source business {shipment.sourceBusinessInstanceId}.");
                }

                if (!string.IsNullOrWhiteSpace(shipment.destinationBusinessInstanceId) && !businessInstanceIds.Contains(shipment.destinationBusinessInstanceId))
                {
                    report.AddWarning($"Shipment {shipment.shipmentId} references missing destination business {shipment.destinationBusinessInstanceId}.");
                }

                if (shipment.routePlan == null)
                {
                    report.AddWarning($"Shipment {shipment.shipmentId} is missing route-plan state.");
                }
            }
        }

        private static void ValidateAcquisition(AcquisitionSaveDto acquisition, HashSet<int> plotIds, HashSet<int> buildingIds, SaveIntegrityReport report)
        {
            if (acquisition == null)
            {
                return;
            }

            ValidateKnownIds(acquisition.ownedPlotIds, plotIds, report, "owned plot");
            ValidateKnownIds(acquisition.ownedBusinessBuildingIds, buildingIds, report, "owned business building");
            if (acquisition.activeDeals != null)
            {
                for (int i = 0; i < acquisition.activeDeals.Count; i++)
                {
                    AcquisitionDealSaveDto deal = acquisition.activeDeals[i];
                    if (deal == null)
                    {
                        report.AddWarning($"Active acquisition deal entry {i} is null.");
                        continue;
                    }

                    if (deal.plotId >= 0 && !plotIds.Contains(deal.plotId))
                    {
                        report.AddWarning($"Acquisition deal {deal.listingId} references missing plot {deal.plotId}.");
                    }

                    if (deal.buildingId >= 0 && !buildingIds.Contains(deal.buildingId))
                    {
                        report.AddWarning($"Acquisition deal {deal.listingId} references missing building {deal.buildingId}.");
                    }
                }
            }
        }

        private static void ValidatePortfolio(PlayerPortfolioSaveDto portfolio, HashSet<string> businessInstanceIds, SaveIntegrityReport report)
        {
            if (portfolio?.distributionCheckpoints == null)
            {
                return;
            }

            for (int i = 0; i < portfolio.distributionCheckpoints.Count; i++)
            {
                OwnerDistributionCheckpointSaveDto checkpoint = portfolio.distributionCheckpoints[i];
                if (checkpoint == null)
                {
                    report.AddWarning($"Portfolio distribution checkpoint entry {i} is null.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(checkpoint.businessInstanceId) && !businessInstanceIds.Contains(checkpoint.businessInstanceId))
                {
                    report.AddWarning($"Portfolio checkpoint references missing business {checkpoint.businessInstanceId}.");
                }
            }
        }

        private static void ValidateKnownIds(List<int> ids, HashSet<int> knownIds, SaveIntegrityReport report, string label)
        {
            if (ids == null)
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (!knownIds.Contains(ids[i]))
                {
                    report.AddWarning($"Save references missing {label} id {ids[i]}.");
                }
            }
        }

        private static HashSet<string> CollectBusinessIds(LandLedgersSaveGameDto save)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            AddBusinessId(ids, save?.businesses?.deepGeneralStore?.currentBusiness);
            if (save?.businesses?.sharedBusinesses != null)
            {
                for (int i = 0; i < save.businesses.sharedBusinesses.Count; i++)
                {
                    AddBusinessId(ids, save.businesses.sharedBusinesses[i]);
                }
            }

            return ids;
        }

        private static void AddBusinessId(HashSet<string> ids, BusinessInstanceSaveDto business)
        {
            if (ids == null || business == null || string.IsNullOrWhiteSpace(business.instanceId))
            {
                return;
            }

            ids.Add(business.instanceId);
        }

        private static HashSet<string> CollectRelationshipIds(BusinessPortfolioSaveDto businesses)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (businesses?.localRecurringOrderRelationships == null)
            {
                return ids;
            }

            for (int i = 0; i < businesses.localRecurringOrderRelationships.Count; i++)
            {
                string id = businesses.localRecurringOrderRelationships[i]?.relationshipId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static HashSet<string> CollectShipmentIds(LogisticsRuntimeSaveDto logistics)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (logistics?.shipments == null)
            {
                return ids;
            }

            for (int i = 0; i < logistics.shipments.Count; i++)
            {
                string id = logistics.shipments[i]?.shipmentId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static HashSet<string> CollectDealListingIds(AcquisitionSaveDto acquisition)
        {
            HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
            if (acquisition?.activeDeals == null)
            {
                return ids;
            }

            for (int i = 0; i < acquisition.activeDeals.Count; i++)
            {
                string id = acquisition.activeDeals[i]?.listingId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private sealed class SaveIntegrityReport
        {
            private const int MaxTrackedMessages = 8;
            private readonly List<string> warnings = new();
            private readonly List<string> fatalErrors = new();

            public int WarningCount => warnings.Count;
            public int FatalErrorCount => fatalErrors.Count;
            public bool HasFatalErrors => FatalErrorCount > 0;

            public void AddWarning(string message)
            {
                Track(warnings, message);
            }

            public void AddFatal(string message)
            {
                Track(fatalErrors, message);
            }

            public string BuildSummary(string label)
            {
                string prefix = $"{label}: {WarningCount} warning(s), {FatalErrorCount} fatal issue(s).";
                string tracked = BuildTrackedMessageSummary();
                return string.IsNullOrWhiteSpace(tracked) ? prefix : prefix + " " + tracked;
            }

            private string BuildTrackedMessageSummary()
            {
                List<string> messages = new();
                if (fatalErrors.Count > 0)
                {
                    messages.Add("Fatal: " + string.Join("; ", fatalErrors));
                }

                if (warnings.Count > 0)
                {
                    messages.Add("Warnings: " + string.Join("; ", warnings));
                }

                return string.Join(" ", messages);
            }

            private static void Track(List<string> list, string message)
            {
                if (string.IsNullOrWhiteSpace(message) || list.Count >= MaxTrackedMessages || list.Contains(message))
                {
                    return;
                }

                list.Add(message);
            }
        }

        private sealed class SaveRestoreDependencyReport
        {
            private const int MaxTrackedWarnings = 8;
            private readonly List<string> warnings = new();
            private string observedOrder = string.Empty;

            public int WarningCount => warnings.Count;

            public void AddWarning(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || warnings.Count >= MaxTrackedWarnings || warnings.Contains(message))
                {
                    return;
                }

                warnings.Add(message);
            }

            public void SetObservedOrder(string order)
            {
                observedOrder = order ?? string.Empty;
            }

            public string BuildSummary()
            {
                string summary = $"Restore dependency audit: {WarningCount} warning(s).";
                if (!string.IsNullOrWhiteSpace(observedOrder))
                {
                    summary += " Order: " + observedOrder;
                }

                if (warnings.Count > 0)
                {
                    summary += " Warnings: " + string.Join("; ", warnings) + ".";
                }

                return summary;
            }
        }

        private sealed class SavePostRestoreConsistencyReport
        {
            private const int MaxTrackedWarnings = 8;
            private readonly List<string> warnings = new();

            public int WarningCount => warnings.Count;

            public void AddWarning(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || warnings.Count >= MaxTrackedWarnings || warnings.Contains(message))
                {
                    return;
                }

                warnings.Add(message);
            }

            public string BuildSummary()
            {
                string summary = $"Post-load consistency audit: {WarningCount} warning(s).";
                if (warnings.Count > 0)
                {
                    summary += " Warnings: " + string.Join("; ", warnings) + ".";
                }

                return summary;
            }
        }

        private sealed class SaveNormalizationReport
        {
            private const int MaxTrackedLabels = 8;
            private readonly List<string> repairedLabels = new();

            public int SectionRepairs { get; private set; }
            public int CollectionRepairs { get; private set; }
            public int EntryRepairs { get; private set; }
            public int TotalRepairs => SectionRepairs + CollectionRepairs + EntryRepairs;
            public bool HasRepairs => TotalRepairs > 0;

            public void NoteSectionRepair(string label)
            {
                SectionRepairs++;
                TrackLabel(label);
            }

            public void NoteCollectionRepair(string label)
            {
                CollectionRepairs++;
                TrackLabel(label);
            }

            public void NoteEntryRepair(string label)
            {
                EntryRepairs++;
                TrackLabel(label);
            }

            public string BuildSummary(int saveFormatVersion)
            {
                string prefix = saveFormatVersion < CurrentFormatVersion
                ? $"Loaded legacy v{saveFormatVersion} save."
                : "Loaded current-format save.";
                if (!HasRepairs)
                {
                    return prefix + " Save normalization found no missing sections to repair.";
                }

                string tracked = repairedLabels.Count > 0
                ? $" Repaired: {string.Join(", ", repairedLabels)}."
                : string.Empty;
                return prefix
                + $" Save normalization repaired {TotalRepairs} missing section(s), collection(s), or entry shell(s)."
                + tracked;
            }

            private void TrackLabel(string label)
            {
                if (string.IsNullOrWhiteSpace(label) || repairedLabels.Count >= MaxTrackedLabels || repairedLabels.Contains(label))
                {
                    return;
                }

                repairedLabels.Add(label);
            }
        }

        private FirstSessionGuidanceManager EnsureFirstSessionGuidanceManager()
        {
            if (firstSessionGuidance != null)
            {
                return firstSessionGuidance;
            }

            firstSessionGuidance = FindAnyObjectByType<FirstSessionGuidanceManager>();
            if (firstSessionGuidance != null)
            {
                return firstSessionGuidance;
            }

            GameObject guidanceObject = new("First Session Guidance");
            firstSessionGuidance = guidanceObject.AddComponent<FirstSessionGuidanceManager>();
            return firstSessionGuidance;
        }

        private static int CountTentativeDeals(AcquisitionMarketManager market)
        {
            if (market == null || market.ActiveDeals == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < market.ActiveDeals.Count; i++)
            {
                AcquisitionDealState deal = market.ActiveDeals[i];
                if (deal != null && deal.stage == AcquisitionDealStage.TentativeAgreement)
                {
                    count++;
                }
            }

            return count;
        }

        private void ConfigureFirstSessionGuidance()
        {
            // Guidance can survive a load as a detached MonoBehaviour even when bootstrap did not
            // create it this frame, so rebind it here before and after restore-side UI refresh.
            firstSessionGuidance ??= EnsureFirstSessionGuidanceManager();
            firstSessionGuidance?.Configure(
            hudController,
            managementPanel,
            storeRuntime,
            acquisitionMarket,
            playerPortfolioManager,
            sharedBusinessRuntime,
            townWorld,
            timeManager);
        }

        private void ConfigurePortfolioManager()
        {
            playerPortfolioManager?.Configure(timeManager, hudController);
        }
    }
}
