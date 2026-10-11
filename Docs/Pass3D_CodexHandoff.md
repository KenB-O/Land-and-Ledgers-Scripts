# Worker D Codex Handoff — Capabilities, Skills & Task Outcomes (Pass III)

Branch: `muse/p3d-capabilities-skills`
Baseline: `aa59bf7ab3fbee9e7f3f0868537d33eed8b3a121` (Pass III foundation)
Commits:
- `7d5b9c4` — capability stores, causal outcome resolver, evidence service, SkillService model migration
- `cefcae3` — tests + fixes

## What changed (canon correction)

The XP/level ladder (1 XP/min, 100 XP/level, max 10), the flat "−1 minute per
skill level" duration rule, and the 7-band capability display were
canon-violating (Rev VII §1.1; Tech XIV Part II §2.4; XXXI §4.5 CANON LOCK).
They are replaced by continuous capability, experience-ledger formation,
causal outcome resolution, and observer-side evidence readings.

## New files (all with fresh-GUID .meta)

| File | Contents |
|---|---|
| `Core/Skills/CapabilityStores.cs` | `AptitudeStore`, `ExperienceLedger` (idempotent), `DomainFamiliarityStore`, `PersonCapabilityStore`, `ExperienceFormationService`, `ExperienceFormationInputs` |
| `Core/Skills/CausalTaskOutcomeResolver.cs` | `CausalTaskOutcomeResolver : ITaskOutcomeResolver`, `TaskOutcomeDetermination` (deterministic), `CausalTaskDurationEstimator : ITaskDurationEstimator` |
| `Core/Skills/CapabilityEvidenceService.cs` | `EvidenceKind`, `ICapabilityEvidenceReader`, `CapabilityEvidenceItem`, `CapabilityEvidenceService` (+ `ValidateReading`) |
| `Tests/Editor/Core/CapabilityStoresTests.cs` | 9 tests (aptitude, idempotency, diminishing, formation, migration, anti-inflation, save/load) |
| `Tests/Editor/Core/CausalOutcomeResolverTests.cs` | 5 tests (novice/expert, no gates, causality, determinism, completion path) |
| `Tests/Editor/Core/CapabilityEvidenceTests.cs` | 7 tests (confidence, truth separation, recruitment wiring, save/load) |

## Modified files

| File | Change |
|---|---|
| `Core/Skills/CapabilityContracts.cs` | `ExperienceEntry.EntryId` added (idempotency key) |
| `Core/Skills/TaskOutcomeContracts.cs` | `CausalOutcomeInputs.BaseMinutes` added; stale comment updated |
| `Core/Skills/SkillModels.cs` | `PersonSkillState` → legacy migration record (XP→capability formula documented); `SkillDefinition` gains `CapabilityFamily` |
| `Core/Skills/SkillService.cs` | Model migrated (see below); `SkillTaskDurationEstimator` REMOVED |
| `Domains/Population/CapabilityPresentation.cs` | Rewritten: 4-band from `LandLedgers.Skills` |
| `Domains/Economy/Labor/ContactGraphService.cs` | Additive: `CapabilityEvidence` property + `EvaluateCandidateCapability` |
| `Domains/Economy/Labor/EmploymentNegotiationService.cs` | Additive: `CapabilityEvidence` property + `GetCandidateEvidence` |
| `Domains/Economy/Core/SharedBusinessRuntimeManager.cs` | Call site updated to new `CapabilityEvidenceReading` |
| `Infrastructure/Persistence/SystemsSaveDtos.cs` | `SkillServiceSaveDto` gains 6 new store fields; `skillStates` → `legacySkillStates` |
| `Orchestration/Systems/SimulationDrivers.cs` | Installs `CausalTaskDurationEstimator` |
| `Tests/Editor/Core/SkillServiceTests.cs` | Rewritten for new model |
| `Tests/Editor/Population/CapabilityPresentationTests.cs` | Rewritten for 4-band |

## SkillService API changes (breaking for direct callers)

REMOVED: `GrantPractice`, `ScaleMinutes`, `ApplyPracticeFromTask`,
`SkillTaskDurationEstimator` (class).
ADDED: `GetCapability`, `RecordTaskExperience`, `BuildCausalInputs`,
`ResolveTaskCompletion`, `ApplyExperienceFromTask`, `SetAptitude`,
`GetLearningRate`, `SetDomainFamiliarity`, `GetDomainFamiliarity`,
`GetExperienceRelevance`, `Evidence` (property).
KEPT (derived/compat): `GetLevel` (1+floor(cap*9), for trade-runtime
journeyman thresholds), registry methods, save DTO plumbing.

Production callers of removed members: NONE (verified by grep — only tests
and the estimator, both updated). Trade runtimes (Blacksmith, HarnessMaker,
Wheelwright) use `GetLevel`, which still works.

## Hub registration

`SkillService` is already hub-owned (`SimulationSystemsHub.Skills`); the new
stores ride in its save DTO — NO new hub property needed. The evidence
service is `Skills.Evidence`; wire it into recruitment where desired:
```csharp
contactGraph.CapabilityEvidence = hub.Skills.Evidence;
negotiation.CapabilityEvidence = hub.Skills.Evidence;
```

## Persistence

`SkillServiceSaveDto` now carries: `definitions`, `legacySkillStates` (read-
only compat), `aptitudes`, `experienceEntries` + `experienceSequence`,
`familiarities`, `capabilities`, `evidenceItems`. All stores clear-then-load.
Legacy XP entries migrate deterministically on load
(`0.05 + (level-1)*0.06 + residual*0.0006` → Ordinary; source XP preserved).

## Integration with unpublished Codex labor changes

The brief notes Codex has uncommitted labor-integration work (task queues ↔
WorkScheduler ↔ payroll). This pass does NOT touch `WorkScheduler`,
`WageSettlementService`, `SimulationDrivers` labor glue, or hub orchestration
beyond the one-line estimator swap. If Codex's changes also touch
`SimulationDrivers.cs` (line ~254) or `SystemsSaveDtos.cs`
(`SkillServiceSaveDto`), merge carefully — the estimator line and the DTO
fields are the overlap points.

## TaskOutcome variation — driver hook

`TaskAuthority.RecordWork` still auto-completes with `Success` (authority
untouched per constraints). The new path: drivers call
`SkillService.ResolveTaskCompletion(...)` which resolves the deterministic
`TaskOutcome` from worker capability AND records the experience episode;
pass the returned outcome to `TaskAuthority.CompleteTask` (which already
accepts varied outcomes). Wiring this into the live completion flow is a
Codex/runtime task.

## Manual Unity tests

- T-D1: Assign a novice and an expert to the same task definition; verify
  PlannedMinutes differ (novice slower) in the Unity editor.
- T-D2: Complete a task via `ResolveTaskCompletion`; verify a novice
  sometimes yields AcceptableWithVariation/RecoverableFailure.
- T-D3: Save/load mid-employment; verify no duplicate experience entries
  and capability is preserved.
- T-D4: Recruitment UI: with evidence recorded, verify candidate display
  shows bands (e.g. "Great–Excellent (Moderate)"); with none, shows
  "Not enough information".
- T-D5: Load an old save with XP-based skill states; verify migration
  (capability in Ordinary range, no errors).

## Known limitations / deferred

- `TaskAuthority.RecordWork` auto-complete remains `Success`; varied outcomes
  need the driver hook above (Codex).
- `WorkerVisibleProfile.generalSkill` (single 0–100) still exists; the
  4-band presentation bridges from it. Full replacement is out of scope.
- Formation base rate is calibrated, not playtested (Codex: sanity-check
  progression feel).
- Roslyn harness used DTO shims for `SystemsSaveDtos` (real file not
  compiled here); Unity compile will verify.
- The `MineLaborRegisterTests` pre-existing failure is untouched (per brief).

## Test report

Roslyn harness (real C# compile+run, not Unity): **33 passed, 0 failed**.
- 12 SkillServiceTests (6 rewritten) + 6 CapabilityPresentationTests
  (rewritten) + 9 CapabilityStoresTests + 5 CausalOutcomeResolverTests
  + 7 CapabilityEvidenceTests... (counts: 6+6+9+5+7=33 ✓).
Unity Test Runner / PlayMode: NOT run (no Unity here — not claimed).
