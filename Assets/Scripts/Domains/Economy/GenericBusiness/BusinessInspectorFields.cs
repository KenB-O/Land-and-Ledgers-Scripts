// Generic Business Reconstruction, section 5.
// Audit of SharedBusinessRuntimeManager inspector fields that appear
// on all businesses. Classification key, from the pass prompt:
// (1) legitimate universal business concern
// (2) specialized capability
// (3) legal or historical rule
// (4) scenario configuration
// (5) obsolete development artifact
// (6) duplicated authority
// This file is the audit RESULT as data, so Unity AI and Codex can
// act on it without re-auditing. No field is deleted here, and no
// serialized field is renamed: the audit's recommendation for
// mining-specific state is conditional Inspector grouping (or a
// capability-specific component in a later Unity pass), NOT
// deletion, because MineState is valid mining support and its
// serialized shape must not break existing assets.
// Evidence locations are the current repo paths in the new root
// (Assets/Scripts/...). Field names below are quoted exactly from
// the source files read in this pass.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    public enum InspectorFieldClassification
    {
        Unclassified = 0,
        UniversalBusinessConcern = 1,
        SpecializedCapability = 2,
        LegalOrHistoricalRule = 3,
        ScenarioConfiguration = 4,
        ObsoleteDevelopmentArtifact = 5,
        DuplicatedAuthority = 6,
    }

    public sealed class InspectorFieldAuditEntry
    {
        public string FieldName = string.Empty;
        public string Location = string.Empty;
        public InspectorFieldClassification Classification;
        public string Recommendation = string.Empty;
    }

    public static class BusinessInspectorFields
    {
        public static IReadOnlyList<InspectorFieldAuditEntry> Audit()
        {
            return new[]
            {
                new InspectorFieldAuditEntry
                {
                    FieldName = "useLegacyPassiveSeed",
                    Location = "SharedBusinessRuntimeManager (BIZ-1 Formation Workflow), default false, deprecated",
                    Classification = InspectorFieldClassification.ObsoleteDevelopmentArtifact,
                    Recommendation = "Keep for backward compatibility until legacy callers are migrated, then remove in a migration pass. A business becomes operating only through recorded commerce (Canon section 3.2); no passive seed is granted.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "minimumHireLaborAccess (LaborAccessLevel.YoungWorker)",
                    Location = "SharedBusinessRuntimeManager Staffing header",
                    Classification = InspectorFieldClassification.LegalOrHistoricalRule,
                    Recommendation = "KEEP. Historically meaningful labor safeguard (minimum hiring labour access). Do not delete. It is a floor on who may be hired, not a hiring target and not a quota.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "MineState (deposit, stage, safety, last cut, richness, confidence, camp pressure)",
                    Location = "BusinessInstanceState.MineState; returns mineState only when businessType == Mine, else null",
                    Classification = InspectorFieldClassification.SpecializedCapability,
                    Recommendation = "KEEP the support, HIDE the clutter: conditional Inspector grouping so mining state shows only for Mine businesses, or a capability-specific component in a Unity pass. Do not delete valid mining support. Non-mine businesses already see null, so the data is not duplicated; the clutter is presentation only.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "Staffing readouts (FilledWorkerCount / TargetWorkerCount, e.g. Staff {filled}/{target})",
                    Location = "SharedBusinessRuntimeManager.BusinessReadouts and BusinessInstanceState",
                    Classification = InspectorFieldClassification.DuplicatedAuthority,
                    Recommendation = "Reframe, do not use as a hiring target: filled/target is a display of slot fill, never the decision input for hiring. Hiring need must come from task backlog (LaborDemandAssessor unmet minutes), not from this fraction. See task-system audits.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "baselineWeeklyThroughputUnits (default 12), baselineDailyServiceCapacity (default 0)",
                    Location = "BusinessProfileDefinition (Shared Throughput section)",
                    Classification = InspectorFieldClassification.ScenarioConfiguration,
                    Recommendation = "Treat as a template calibration default, not a hard cap. Section 12 capacity (materials, worker time, equipment, fuel, transport, storage) is the replacement computation; these defaults remain only as fallback calibration until callers migrate, and must not be read as a weekly quota.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "AllowedPlotZones / allowedPlotZones (default Business, MixedUse), SiteRequirements.Meets as hard gate",
                    Location = "BusinessProfileDefinition.BusinessSiteRequirementDefinition",
                    Classification = InspectorFieldClassification.ObsoleteDevelopmentArtifact,
                    Recommendation = "Zone as a hard gate is the modern-zoning pattern Canon discourages. Migrate callers to SiteEligibility.Evaluate (zone = warning only). Do NOT delete the serialized field in this pass: existing assets reference it. Mark it legacy in the asset docs and stop gating on it in new code.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "output units per input and local outlet maxima (CropFarm 8, Ranch 2, Butcher 8, Blacksmith 4, FuelDealer 5, GrainMill 4, Bakery 3; outlet maxima 4-110 by business; sawmill weekly maxima)",
                    Location = "SharedBusinessRuntimeManager constants",
                    Classification = InspectorFieldClassification.DuplicatedAuthority,
                    Recommendation = "Legacy calibration constants still read by legacy callers; do NOT delete in this pass. Section 12 ThroughputCapacity is the replacement. Codex migration task: move callers to capacity computation, then retire constants. Do NOT replace them with another unexplained constant set.",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "operatingLedger / business becomes operating only through recorded commerce",
                    Location = "SharedBusinessRuntimeManager BIZ-1 Formation Workflow",
                    Classification = InspectorFieldClassification.UniversalBusinessConcern,
                    Recommendation = "KEEP. Single legal/economic principal with one ledger is the correct universal shape for generic businesses; the capability set in this pass preserves it (no duplicate ledgers per capability).",
                },
                new InspectorFieldAuditEntry
                {
                    FieldName = "workerSlots / RequiredForOpening gating (TryEnsureRequiredStoreStaffForOpening)",
                    Location = "BusinessRuntimeState / GeneralStoreRuntimeManager",
                    Classification = InspectorFieldClassification.ScenarioConfiguration,
                    Recommendation = "Re-express in task terms in a later pass: opening tasks with no worker, not a slot count. Required-for-opening as a bare minimum (a store cannot open with nobody to tend it) is defensible, but it is not a hiring target.",
                },
            };
        }
    }
}
