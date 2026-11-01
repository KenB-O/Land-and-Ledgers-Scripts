using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// Pass V P1-1 contract: distinct scenario lifecycle operations. Each has
    /// documented generation/restore semantics; see ScenarioLifecycleSemantics.
    /// APPEND-ONLY.
    /// </summary>
    public enum ScenarioLifecycleAction
    {
        NewGame = 0,        // Generate one realized world from definition + seed.
        LoadGame = 1,       // Restore the saved realized world; never regenerate.
        RestartSameSeed = 2,// Regenerate deterministically from the same seed.
        RestartNewSeed = 3, // Regenerate from a new seed.
        ReplayFromPreset = 4,// Fresh campaign from the selected preset.
        ContinueFailed = 5, // Preserve the current world; never secretly regenerate.
    }

    /// <summary>
    /// Pass V P1-1 contract: authored starting-world configuration for a scenario.
    /// This is what the designer wants the starting world to satisfy. It is NOT the
    /// live world and must never become a second economic ledger.
    /// Population ranges must honor the Canon calibration (55-95 residents); authored
    /// scenarios may define their own explicit ranges where consistent with scenario
    /// rules.
    /// </summary>
    [Serializable]
    public sealed class StartingWorldConfiguration
    {
        [SerializeField] private string scenarioId = string.Empty;
        [SerializeField] private string definitionVersion = "1.0.0";
        [SerializeField] private int startYear = 1880;
        [SerializeField] private int startMonth = 3;
        [SerializeField] private int startDay = 1;

        [SerializeField] private int minResidents = 55;
        [SerializeField] private int maxResidents = 95;
        [SerializeField] private int preferredResidents = 78;

        [SerializeField] private List<string> requiredBusinessIds = new List<string>();
        [SerializeField] private string populationNotes = string.Empty;
        [SerializeField] private string economicNotes = string.Empty;
        [SerializeField] private string propertyNotes = string.Empty;

        // Pass V P1-1 (Worker 1): authored starting-world configuration, appended
        // additively so older serialized presets keep deserializing. These fields
        // describe what the starting world must satisfy; they never hold live
        // world state and must never become a second economic ledger.
        [SerializeField] private List<ScenarioFixedBusiness> fixedBusinesses = new List<ScenarioFixedBusiness>();
        [SerializeField] private int minVariableBusinesses;
        [SerializeField] private int maxVariableBusinesses = 2;
        [SerializeField] private ScenarioPlayerStart playerStart = new ScenarioPlayerStart();
        [SerializeField] private ScenarioEconomyPolicy economyPolicy = new ScenarioEconomyPolicy();
        [SerializeField] private List<ScenarioPropertyGrant> propertyGrants = new List<ScenarioPropertyGrant>();
        [SerializeField] private string householdCompositionNotes = string.Empty;
        [SerializeField] private string validationRulesNotes = string.Empty;

        public string ScenarioId => scenarioId;
        public string DefinitionVersion => definitionVersion;
        public int StartYear => startYear;
        public int StartMonth => startMonth;
        public int StartDay => startDay;
        public int MinResidents => minResidents;
        public int MaxResidents => maxResidents;
        public int PreferredResidents => preferredResidents;
        public IReadOnlyList<string> RequiredBusinessIds => requiredBusinessIds;
        public string PopulationNotes => populationNotes;
        public string EconomicNotes => economicNotes;
        public string PropertyNotes => propertyNotes;
        public IReadOnlyList<ScenarioFixedBusiness> FixedBusinesses => fixedBusinesses;
        public int MinVariableBusinesses => minVariableBusinesses;
        public int MaxVariableBusinesses => maxVariableBusinesses;
        public ScenarioPlayerStart PlayerStart => playerStart;
        public ScenarioEconomyPolicy EconomyPolicy => economyPolicy;
        public IReadOnlyList<ScenarioPropertyGrant> PropertyGrants => propertyGrants;
        public string HouseholdCompositionNotes => householdCompositionNotes;
        public string ValidationRulesNotes => validationRulesNotes;

        /// <summary>
        /// Pass V P1-1: mutating setters used by preset authoring/migration code.
        /// The coordinator and generation ports read through the getters above.
        /// </summary>
        public void SetPopulationRange(int min, int preferred, int max)
        {
            minResidents = min;
            preferredResidents = preferred;
            maxResidents = max;
        }

        public void SetStartDate(int year, int month, int day)
        {
            startYear = year;
            startMonth = month;
            startDay = day;
        }

        public void SetDefinitionVersion(string version)
        {
            definitionVersion = version ?? string.Empty;
        }

        public void SetScenarioId(string id)
        {
            scenarioId = id ?? string.Empty;
        }

        public List<ScenarioFixedBusiness> EditableFixedBusinesses => fixedBusinesses;
        public List<ScenarioPropertyGrant> EditablePropertyGrants => propertyGrants;
        public List<string> EditableRequiredBusinessIds => requiredBusinessIds;
        public ScenarioPlayerStart EditablePlayerStart => playerStart ?? (playerStart = new ScenarioPlayerStart());
        public ScenarioEconomyPolicy EditableEconomyPolicy => economyPolicy ?? (economyPolicy = new ScenarioEconomyPolicy());
    }

    /// <summary>
    /// Pass V P1-1 (Worker 1): one business the scenario requires to exist at world
    /// start. Fixed businesses are authored, never invented at generation time to
    /// force validity: when a fixed business cannot be placed, generation fails
    /// loudly instead of substituting a different business.
    /// </summary>
    [Serializable]
    public sealed class ScenarioFixedBusiness
    {
        [SerializeField] private string businessId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string businessType = string.Empty;
        [SerializeField] private int capitalCents;
        [SerializeField] private string ownerKind = "npc";

        public string BusinessId => businessId;
        public string DisplayName => displayName;
        public string BusinessType => businessType;
        public int CapitalCents => capitalCents;
        public string OwnerKind => ownerKind;

        public ScenarioFixedBusiness() { }

        public ScenarioFixedBusiness(string businessId, string displayName, string businessType, int capitalCents, string ownerKind = "npc")
        {
            this.businessId = businessId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.businessType = businessType ?? string.Empty;
            this.capitalCents = Math.Max(0, capitalCents);
            this.ownerKind = string.IsNullOrWhiteSpace(ownerKind) ? "npc" : ownerKind;
        }
    }

    /// <summary>
    /// Pass V P1-1 (Worker 1): the authored player start. Names the player household,
    /// its founders, and the legitimate starting stake. The stake is posted through
    /// the household cash authority with scenario provenance, never invented.
    /// </summary>
    [Serializable]
    public sealed class ScenarioPlayerStart
    {
        [SerializeField] private string householdName = "Player Household";
        [SerializeField] private int founderCount = 1;
        [SerializeField] private int startingCashCents = 250000;
        [SerializeField] private int startingDebtCents;
        [SerializeField] private string professionId = string.Empty;
        [SerializeField] private bool ownsHomeBuilding = true;

        public string HouseholdName => householdName;
        public int FounderCount => founderCount;
        public int StartingCashCents => startingCashCents;
        public int StartingDebtCents => startingDebtCents;
        public string ProfessionId => professionId;
        public bool OwnsHomeBuilding => ownsHomeBuilding;

        public void SetHouseholdName(string value) { householdName = value ?? string.Empty; }
        public void SetFounderCount(int value) { founderCount = Math.Max(0, value); }
        public void SetStartingCashCents(int value) { startingCashCents = Math.Max(0, value); }
        public void SetStartingDebtCents(int value) { startingDebtCents = Math.Max(0, value); }
        public void SetProfessionId(string value) { professionId = value ?? string.Empty; }
        public void SetOwnsHomeBuilding(bool value) { ownsHomeBuilding = value; }
    }

    /// <summary>
    /// Pass V P1-1 (Worker 1): the authored starting-economy policy. Cash bands bound
    /// per-household starting cash; debt policy bounds legitimate starting debts.
    /// All seeded money moves through HouseholdLedger (one cash writer) and all
    /// debts through FinancialObligationAuthority (one debt authority).
    /// </summary>
    [Serializable]
    public sealed class ScenarioEconomyPolicy
    {
        [SerializeField] private int cashBandMinCents = 25000;
        [SerializeField] private int cashBandMaxCents = 150000;
        [SerializeField] private float employmentTargetRate01 = 0.75f;
        [SerializeField] private float debtChance01 = 0.25f;
        [SerializeField] private int maxDebtCentsPerHousehold = 150000;
        [SerializeField] private List<LandLedgers.Economy.Financing.FinancialObligationKind> allowedObligationKinds =
            new List<LandLedgers.Economy.Financing.FinancialObligationKind>
            {
                LandLedgers.Economy.Financing.FinancialObligationKind.TradeCredit,
                LandLedgers.Economy.Financing.FinancialObligationKind.Loan,
            };

        public int CashBandMinCents => cashBandMinCents;
        public int CashBandMaxCents => cashBandMaxCents;
        public float EmploymentTargetRate01 => employmentTargetRate01;
        public float DebtChance01 => debtChance01;
        public int MaxDebtCentsPerHousehold => maxDebtCentsPerHousehold;
        public IReadOnlyList<LandLedgers.Economy.Financing.FinancialObligationKind> AllowedObligationKinds => allowedObligationKinds;

        public void SetCashBand(int minCents, int maxCents)
        {
            cashBandMinCents = Math.Max(0, minCents);
            cashBandMaxCents = Math.Max(cashBandMinCents, maxCents);
        }

        public void SetEmploymentTargetRate(float value)
        {
            employmentTargetRate01 = Math.Max(0f, Math.Min(1f, value));
        }

        public void SetDebtPolicy(float chance01, int maxDebtCents)
        {
            debtChance01 = Math.Max(0f, Math.Min(1f, chance01));
            maxDebtCentsPerHousehold = Math.Max(0, maxDebtCents);
        }

        public List<LandLedgers.Economy.Financing.FinancialObligationKind> EditableAllowedObligationKinds => allowedObligationKinds;
    }

    /// <summary>
    /// Pass V P1-1 (Worker 1): an authored property grant at world start. Applied
    /// through the property/title authority with scenario provenance; grants that
    /// reference no real household or building are rejected, never invented.
    /// </summary>
    [Serializable]
    public sealed class ScenarioPropertyGrant
    {
        [SerializeField] private string grantId = string.Empty;
        [SerializeField] private int buildingId = -1;
        [SerializeField] private string granteeKind = "household";
        [SerializeField] private string notes = string.Empty;

        public string GrantId => grantId;
        public int BuildingId => buildingId;
        public string GranteeKind => granteeKind;
        public string Notes => notes;

        public ScenarioPropertyGrant() { }

        public ScenarioPropertyGrant(string grantId, int buildingId, string granteeKind, string notes = "")
        {
            this.grantId = grantId ?? string.Empty;
            this.buildingId = buildingId;
            this.granteeKind = string.IsNullOrWhiteSpace(granteeKind) ? "household" : granteeKind;
            this.notes = notes ?? string.Empty;
        }
    }

    /// <summary>
    /// Pass V P1-1 contract: a durable, versioned, reusable scenario preset.
    /// Supports load, validation, versioning, reuse, copy/derive, deterministic
    /// preview generation, diagnostics, and backward compatibility.
    /// </summary>
    [Serializable]
    public sealed class ScenarioPreset
    {
        [SerializeField] private string presetId = string.Empty;
        [SerializeField] private string presetName = string.Empty;
        [SerializeField] private string presetVersion = "1.0.0";
        [SerializeField] private string derivedFromPresetId = string.Empty;
        [SerializeField] private StartingWorldConfiguration startingWorld = new StartingWorldConfiguration();
        [SerializeField] private List<string> diagnostics = new List<string>();

        public string PresetId => presetId;
        public string PresetName => presetName;
        public string PresetVersion => presetVersion;
        public string DerivedFromPresetId => derivedFromPresetId;
        public StartingWorldConfiguration StartingWorld => startingWorld;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Pass V P1-1 (Worker 1): authoring setters. The coordinator validates on
        /// register; direct field mutation outside the library is discouraged.
        /// </summary>
        public void SetIdentity(string id, string name, string version)
        {
            presetId = id ?? string.Empty;
            presetName = name ?? string.Empty;
            presetVersion = version ?? string.Empty;
        }

        public void SetDerivedFrom(string sourcePresetId)
        {
            derivedFromPresetId = sourcePresetId ?? string.Empty;
        }

        public void ClearDiagnostics()
        {
            diagnostics.Clear();
        }

        public void AddDiagnostic(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                diagnostics.Add(message);
            }
        }
    }

    /// <summary>
    /// Pass V P1-1 contract: ONE authoritative scenario coordinator. It manages
    /// scenario identity, configuration, generation, initialization, validation, and
    /// lifecycle. It commands existing initialization services and retains scenario
    /// metadata only. It stores no Person, Household, Business, inventory, property,
    /// or money state.
    /// </summary>
    public interface IScenarioCoordinator
    {
        string ActiveScenarioId { get; }
        string ActivePresetId { get; }
        int ActiveSeed { get; }

        bool RegisterPreset(ScenarioPreset preset, List<string> diagnostics);
        ScenarioPreset GetPreset(string presetId);
        bool ValidatePreset(string presetId, List<string> diagnostics);

        /// <summary>
        /// Executes a lifecycle action with the given seed. Returns false with
        /// diagnostics when generation or validation fails; never invents
        /// money, houses, or businesses to force validity.
        /// </summary>
        bool ExecuteLifecycle(ScenarioLifecycleAction action, int seed, List<string> diagnostics);

        ScenarioPreset CapturePresetDto();
        bool RestorePresetDto(ScenarioPreset dto, List<string> diagnostics);
    }

    /// <summary>
    /// Pass V P1-1 contract: documents what each lifecycle action must and must
    /// not do, so New Game / Load / Restart / Replay can never be confused.
    /// </summary>
    public static class ScenarioLifecycleSemantics
    {
        /// <summary>
        /// State that a genuine New Game must never inherit from an earlier run.
        /// Verified by ghost-state regression tests.
        /// </summary>
        public static readonly string[] NewGameMustNotInherit =
        {
            "player-ownership",
            "household-memberships",
            "employment",
            "scenario-objectives",
            "manager-decisions",
            "generated-businesses",
            "banking-state",
            "stale-reservations",
        };
    }
}
