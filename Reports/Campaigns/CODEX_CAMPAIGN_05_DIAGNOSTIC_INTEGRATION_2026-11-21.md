# Codex Campaign 05 — Diagnostic Integration and Live Incidents

Date: 2026-11-21  
Branch: `codex/muse-unity-integration-2026-11-21`  
Source checkpoint at start: `b822455a645506b21a3b0f72a1bc5edaf813df64`  
Unity: not run in this pass

## Verification boundary

The requested twelve principal diagnostic windows are present in source, each has a `[MenuItem]` registration and a matching `.meta` file. This establishes source registration only. No window was opened, no Editor exception state was observed, and no Unity compile, Test Runner, Main Scene, filtering, selection, export, memory, or gameplay-side-effect test was run. The available Unity Editor remains attached to the owner's separate `F:\Land & Ledgers` project; this pass did not open or control a competing Editor against the integration checkout.

| # | Tool / actual menu path | Window class and source | Source-level data path | Unity result |
|---:|---|---|---|---|
| 01 | `Land & Ledgers/Diagnostics/Incident Investigator` | `IncidentInvestigatorWindow` — `Assets/Scripts/Editor/Diagnostics/IncidentInvestigatorWindow.cs` | Live `SimulationSystemsHub` task trace; retained known-evidence proving cases; shared incident adapter | Registered; not opened or runtime-tested |
| 02 | `Land & Ledgers/Diagnostics/Economy Integrity Auditor` | `EconomyIntegrityAuditorWindow` — `Assets/Scripts/Editor/Diagnostics/EconomyIntegrityAuditorWindow.cs` | Source/config scans and explicit known cases through `EconomyIntegrityAudit`; no second ledger | Registered; not opened or runtime-tested |
| 03 | `Land & Ledgers/Diagnostics/Task Lifecycle Inspector` | `TaskLifecycleInspectorWindow` — `Assets/Scripts/Editor/Diagnostics/TaskLifecycleInspectorWindow.cs` | Live TaskAuthority plus activity trace and incident adapter | Registered; not opened or runtime-tested |
| 04 | `Land & Ledgers/Diagnostics/Business Operations Monitor` | `BusinessOperationsMonitorWindow` — `Assets/Scripts/Editor/Diagnostics/BusinessOperationsMonitorWindow.cs` | Live business/runtime read models | Registered; not opened or runtime-tested |
| 05 | `Land & Ledgers/Diagnostics/Supply Chain Dependency Explorer` | `SupplyChainDependencyExplorerWindow` — `Assets/Scripts/Editor/Diagnostics/SupplyChainDependencyExplorerWindow.cs` | Supply-chain source/runtime read model | Registered; not opened or runtime-tested |
| 06 | `Land & Ledgers/Diagnostics/World Accessibility and Interaction Auditor` | `WorldAccessibilityAuditorWindow` — `Assets/Scripts/Editor/Diagnostics/WorldAccessibilityAuditorWindow.cs` | Authored-world/accessibility source and scene evidence | Registered; not opened or runtime-tested |
| 07 | `Land & Ledgers/Diagnostics/Save-Continue State Comparator` | `SaveContinueStateComparatorWindow` — `Assets/Scripts/Editor/Diagnostics/SaveContinueStateComparatorWindow.cs` | Save DTO and captured pre/post state comparison | Registered; not opened or runtime-tested |
| 08 | `Land & Ledgers/Diagnostics/Household Survival and Food Security Monitor` | `HouseholdSurvivalMonitorWindow` — `Assets/Scripts/Editor/Diagnostics/HouseholdSurvivalMonitorWindow.cs` | Household survival evidence and acceptance model | Registered; not opened or runtime-tested |
| 09 | `Land & Ledgers/Diagnostics/Simulation Performance and Memory Monitor` | `SimulationPerformanceMonitorWindow` — `Assets/Scripts/Editor/Diagnostics/SimulationPerformanceMonitorWindow.cs` | Runtime/editor performance and memory sampling | Registered; no overhead measurement made |
| 10 | `Land & Ledgers/Diagnostics/Scenario Lab` | `ScenarioLabWindow` — `Assets/Scripts/Editor/Diagnostics/ScenarioLabWindow.cs` | Run-artifact reader; evaluates survival/acceptance and calls `AtoZAuditRunner.EvaluateRunChecks` | Registered; no artifact evaluated in Unity |
| 11 | `Land & Ledgers/Diagnostics/Regression Comparator` | `RegressionComparatorWindow` — `Assets/Scripts/Editor/Diagnostics/RegressionComparatorWindow.cs` | Captured run summaries and regression comparison | Registered; not opened or runtime-tested |
| 12 | `Land & Ledgers/Diagnostics/Simulation Health Control Center` | `SimulationHealthControlCenterWindow` — `Assets/Scripts/Editor/Diagnostics/SimulationHealthControlCenterWindow.cs` | Shared bounded `IncidentWarningAuthority`, issue register, snapshot/export controls | Registered; not opened or runtime-tested |

The A-to-Z Audit (`Land & Ledgers/Diagnostics/A-to-Z Audit`) and NPC Activity Monitor (`Land & Ledgers/Diagnostics/NPC Activity Monitor`) are two additional source-registered windows, not counted among the twelve above. Every principal window file and `.meta` was confirmed present. The Editor folder has no dedicated `.asmdef`; Unity compilation is still required to establish assembly references and Editor API compatibility.

## Repairs implemented

1. **Incident evidence completeness.** The production `LogRefusalDayKeyed` seam previously sent no simulated seconds and no correlation IDs: refusal reports always recorded `SimSeconds = -1`. The funnel now forwards `TimeManager.PreciseAbsoluteGameSeconds` when available and accepts typed correlation fields. Known IDs are forwarded for domestic fuel handling (person/household), stale queued tasks (task), physical freight pickup (shipment), and furniture home/footprint refusals (household/building). Unknown identities remain empty.
2. **Incident aggregation and retention.** The local raw-key dedup gate retained every dynamic task/shipment key for the full session and its early return prevented same-day repeats from reaching the incident authority. `DayKeyedRefusalLogGate` now retains normalized signature keys only for the current simulated day; each occurrence reaches the bounded authority for counting, and raw Console warnings remain limited to one per normalized signature/day. A focused gate regression was added.
3. **A-to-Z run evidence correctness.** `EvaluateRunChecks` previously matched all IDs beginning `N-S` as survival stages, which accidentally included `N-SAVE-CONTINUE`. It now matches the actual catalogued `SurvivalStageSpec` IDs. A run with a supplied run ID but missing transaction/Continue artifacts now remains BLOCKED with the actual run ID and missing evidence named, instead of falsely claiming no run exists. Source-only audit copy likewise distinguishes absent supplied evidence from a factual claim about all runs on the branch.
4. **Incident closure guard.** Source inspection confirms `CloseIncident` returns false for an unknown signature and therefore cannot emit a close before an incident has been opened. This behavior was not changed.

Regression tests were added for refusal timestamp/entity correlation, normalized per-day gate behavior and cleanup, and `N-SAVE-CONTINUE` routing/run-ID explanation. They are in `IncidentCoreAdapterTests.cs` and `AtoZAuditTests.cs` respectively. Neither test fixture was executed in this pass.

## Incident and A-to-Z findings

- `SimulationDrivers` has one bounded live refusal funnel serving 136 production callsites (137 textual matches including its method declaration); `IncidentWarningAuthority` performs a second occurrence aggregation/cap and defaults to at most 512 retained incidents, 12 warning lines per day and 3 per signature/day. Its local raw-Console gate now discards the prior day's normalized keys on rollover. `NpcActivityTraceRecorder` defaults to 5,000 prehistory events, 20,000 session events and 2,000 anomalies, with drop counters. `DriveDailyHealth` is called from the daily driver. These are source-verified bounds, not proof of emitted Main Scene incidents or measured performance.
- Known correlations are now carried at four high-impact source call families. The other funnel calls still identify the system/reason and timestamp but leave entity IDs empty where their callers do not pass a durable identity. A live run must verify stable signature normalization and occurrence aggregation for real identifiers.
- `AtoZAuditRunner.EvaluateRunChecks` is invoked from Scenario Lab after it reads run metadata, daily summary and Console JSONL artifacts and evaluates survival evidence. It is an artifact-based post-run evaluation; the Editor menu A-to-Z Audit is a separate source/config and recorded-finding pass. `EconomyIntegrityAudit.AuditProduction` has test callers only and no production callsite. No common event authority spans all active production paths at this revision, so a partial production feed was not added or described as complete.
- Causal labels, severity, task recovery ownership, duplicate incident behavior, and hidden-mover state were not live-exercised. No warning was suppressed or reclassified in this pass.

## Issue register and evidence

The existing [Console Investigation Checklist](../Diagnostics/CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md) retains the historical 354 CI identities and all recorded occurrences. Two new **silent diagnostic defects** were appended as `CI-SILENT-INCIDENT-TIMESTAMP-CORRELATION-001` and `CI-SILENT-A2Z-RUN-EVIDENCE-CLAIM-001`, each marked `IMPLEMENTED, UNVERIFIED`. These are not counted as historical Console warning occurrences.

The complete Muse `DEFECT_REGISTER.md` and pass-home `HANDOFF.md` were not found in the published checkout. The accessible A-to-Z catalog is a narrower list; this report does not claim to reconstruct or reproduce the missing 90-DEF original register. The checklist preserves CI identity instead of flattening it into SIG mechanisms.

## Tests and performance

- Executed Unity compilation: **not run**.
- Executed Unity EditMode tests: **not run**.
- Executed Main Scene or PlayMode test: **not run**.
- Live Editor windows and exports: **not verified**.
- Diagnostic overhead: **not measured**. Static bounds found: incident retention 512; daily incident line caps 12 total / 3 per signature. This is not a runtime memory or CPU measurement.
- `git diff --check` on the full dirty checkout still reports pre-existing trailing whitespace in `GeneralStoreSupplierAdapter.cs`, outside this diagnostic repair. The touched diagnostic/test files are checked separately before handoff.

## Next verification

At the next safe Editor checkpoint on this exact integration source: compile and run `IncidentCoreAdapterTests` and `AtoZAuditTests`; verify all twelve windows open without exceptions and with truthful empty/live states; feed a real blocked task through the funnel and inspect its timestamp, stable signature, entity ID, occurrence count and recovery authority; evaluate a run artifact in Scenario Lab and confirm run-dependent checks accurately identify missing evidence; then measure ordinary versus Deep diagnostic overhead. A bounded production-event record should be considered only after the production authorities expose a safe shared completion event without double-recording outputs.
