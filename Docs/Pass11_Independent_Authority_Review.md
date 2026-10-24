# Pass XI — Independent Authority Review (WS6 Final Gate)

Reviewer role: independent reviewer only. This review fixes nothing,
merges nothing, and changes no production or test code. The only file
created by this review is this document (plus its `.meta`). Any fix
listed below is for the coordinator to apply and re-verify.

- Review base: `muse/continuous-development` at
  `07471dd1a1bda8107ce6cf815b497072981fd58a`
  ("Pass XI: integrated test results document").
- Review branch (this document only):
  `muse/pass11-ws6-final-review`, created from `07471dd` in an
  isolated git worktree at
  `~/workspace/land-and-ledgers/wt-pass11-ws6final`.
- Review range read: the full diff `3af1863..07471dd` (33 commits,
  75 files, production, tests, documents, register and registry
  updates), plus spot re-verification of the WS1 + WS3 code at HEAD
  that the first gate had already cleared.
- Nothing in this document is a Unity compilation, Test Runner, or
  PlayMode result, and none may be read as one. Every count below is
  a Roslyn harness result.

## Verdict: PASS

No confirmed P0 or P1 defect was found. The mandatory-fix list is
empty. The pass may close on the source side with the recommended
(non-blocking) findings below tracked, not gated. Two standing
external gates remain open by design and are not Pass XI source
defects: the Unity Test Runner pass for the M1/M2 bootstrap tests
(PASS11-008, gate G5) and the Unity RUN-004 thirty-day PlayMode proof
(gate G4). Neither is claimed anywhere in the Pass XI documents.

## Method

1. Read the full `3af1863..07471dd` diff file by file (production
   first, then tests, then documents, register and registry), rather
   than trusting workstream summaries.
2. Traced each changed production method to its production callers
   with grep (callers, not comments, decide whether a claim about
   wiring is true).
3. Re-verified the WS1 + WS3 first-gate claims at HEAD after the
   merges: reconciler behaviour, fee waterfall, assay split,
   asset-sale persistence, payroll guard.
4. Checked the full Pass XI review list (duplicate execution, double
   payment, phantom inventory, unsupported revenue, incorrect tax,
   fake demand, duplicate freight delivery, double mine depletion,
   double payroll, invalid occupancy, incorrect property transfer,
   unfunded banking, missing reservations, save/load duplication,
   stale scenario state, non-determinism, broken Unity interface
   assumptions, reintroduced legacy behaviour) against code, not
   against the review documents' own assertions.
5. Ran the independent full harness from a freshly generated csproj
   variant pointed at the worktree (shared `gen-csproj.sh` untouched,
   shared `harness.csproj` and `sources.txt` untouched, isolated
   `/tmp/ws6final-bin` and `/tmp/ws6final-obj`), with
   `LAND_LEDGERS_REPO` pointed at the worktree, and grep-verified the
   new test files in the generated csproj before trusting the count.
6. Verified document claims against code: implementation-report
   headlines, settlement-proof assertions, performance-report
   equivalence, time-review verdict conditions, register IDs, and
   unwired-registry entries.

## Independent full-harness result (Roslyn harness only)

- Build: 0 errors (30 warnings, pre-existing, none introduced by
  this review; the build ran from `/tmp/ws6final.csproj` against the
  worktree).
- Full run: **1404 passed, 3 failed** — exactly the claimed
  baseline. No new failure, no failure reclassified, no test deleted.
- The 3 failures, verified from the run output, are exactly the
  documented test-side defects, and each was confirmed against the
  test text and production code rather than taken on trust:
  - `LandLedgers.EditorTests.Economy.MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree`
    (`Tests/Editor/Economy/MoneyOrderServiceTests.cs:202`):
    asserts `IsNotNull` with the message "No duplicate of a
    non-live order" — the assertion contradicts its own message.
    Production correctly returns null for a non-live (already
    duplicated) order.
  - `LandLedgers.EditorTests.Economy.MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull`
    (`:277`): expects net 2500 with the message "3500 − 500 netted
    bilaterally"; 3500 − 500 = 3000, which is what production
    computes (run output: expected 2500, actual 3000).
  - `LandLedgers.EditorTests.World.P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`
    (`Tests/Editor/World/P5PropertyLegalJourneyTests.cs:186`):
    expects `Assessed` at day 100, past the 60-day due date
    (`PropertyTaxCalibration.DueDaysAfterLevy = 60`,
    `Domains/World/Property/PropertyTaxCalibration.cs:22`); the
    delinquency model reports `Delinquent` (run output: expected
    Assessed, actual Delinquent).
- All Pass XI new tests pass in this run, including all 8 WS6
  first-gate seam/indorsement/payroll tests, all 8 WS4 save/load fix
  tests, all 22 WS5 tests, and the WS4 proof run line
  (`freightDelivered=100`, `breadObjective=FAILED-at-deadline`).

## Mandatory-fix list

None. No item in this review requires a production or test change
before the pass closes, and no item would require a redesign. The
recommended findings below are explicitly non-blocking.

## Recommended non-blocking findings

- R-F1 (test-correction review, recommended, not gated): fix exactly
  the three failing tests above in a dedicated review — change the
  `IssueDuplicate` assertion to match its own message, correct the
  settlement net to 3000 with funds 0 / 4000, and correct the P5
  expectation to the delinquency model's `Delinquent` at day 100 —
  without touching production code. This was already the integrated
  test-results document's recommended next step; this review concurs
  and confirms the test-side diagnosis independently.
- R-F2 (NPC construction labor port, efficiency/bookkeeping only):
  `SimulationDrivers.DriversConstructionLaborPort.WorkPackage`
  (`Orchestration/Systems/SimulationDrivers.cs:308`) re-commits the
  package's full `LaborMinutesRequired` on every day the package is
  still short, even when only a small remainder is owed, and each
  partial day leaves an earlier open task behind. This cannot create
  phantom completion: `WorkCommittedLabor` credits only minutes the
  `TaskAuthority` actually records, and `AdvancePackage` still gates
  on `LaborMinutesCommitted >= LaborMinutesRequired`. The fix, when
  wanted, is small: commit only the remaining minutes, and work the
  already-bound task instead of committing a fresh one when one is
  open. Not integrity, not a gate.
- R-F3 (M1 failure-path retry, observation only):
  `SaloonCircuitBootstrap.RestoreFromSaveDto` retries the director
  restore when `HasActiveScenario` is false, which includes the case
  where the First Ledger restore already failed. A failed restore
  retries loudly (both skip paths now log errors), and the success
  path cannot double-apply (see M1 below), so this is a hardening
  note, not a defect: a future pass could record "restore attempted"
  separately from "restore active". Not a gate.
- R-F4 (carried residuals, already registered, not re-opened):
  PASS11-003 (manual `PayAgreedWage` after lookup recovery could
  duplicate an M3 liability — automated payroll cannot),
  PASS11-004 (boarding-house registration needs a parcel mapping),
  PASS11-005 (named-buyer distress sales have no legitimate buyer
  source), PASS11-006 (NoticeBoard/panel wiring is Unity scene work),
  PASS11-007 (legacy-slot payroll path still refuses a throwing
  lookup with no accrual). Each is honestly recorded in
  `Docs/Pass6_Deferred_Development_Master_Register.md` and none was
  papered over with invented data. PASS11-008 (Unity-runner bootstrap
  tests) and RUN-004 (Unity PlayMode proof) remain open external
  gates, as stated in the verdict.

## Checked sound, with evidence

### WS6 first-gate fixes M1–M5 (all hold after the merges)

- M1 — Saloon restore is self-sufficient.
  `SaloonCircuitBootstrap.RestoreFromSaveDto`
  (`Orchestration/Scenarios/SaloonCircuit/SaloonCircuitBootstrap.cs`)
  runs the director restore only when `!HasActiveScenario`, and
  every skip path now logs an error. `SaveLoadManager`
  (`Infrastructure/Persistence/SaveLoadManager.cs:379-391`) calls
  the First Ledger restore first (which always runs the director
  restore, `FirstLedgerBootstrap.cs:170`) and then the Saloon
  restore, so the success path cannot double-run the director
  restore. Seam test
  `SaloonLoad_WithoutFirstLedgerBootstrap_HistoricalGoalsSurvive_LapsedFinalDoesNotResurrect`
  passes.
- M2 — Late-awakening bootstraps self-register, idempotently.
  Both bootstraps register on Awake when the director already runs
  their scenario (`SaloonCircuitBootstrap.cs`, `FirstLedgerBootstrap.cs`
  Awake blocks). Registration is idempotent by construction:
  `EnsureQualifierRegistrations` adopts definitions only when no
  registrations exist, and `RebuildQualifiers` clear-and-rebuilds
  from persisted registrations
  (`ScenarioObjectiveService.cs:231-290`). Seam test
  `LateRebuild_IsIdempotent_AndKeepsEvaluating` passes.
- M3 — Throwing payroll lookup accrues, confirmed-unavailable does
  not. `BusinessRuntimeState.cs` weekly path accrues the week's wage
  as an operating liability only when `PayrollEligibilityLookupFailed`
  is set, via the same named-payable + scalar accrual as the
  shortfall branch; confirmed dead/off-map persons accrue nothing;
  the employment is not suspended. The code comment and the test
  name both record that a later weekly run pays the current week
  only, so automated paths cannot double-pay the liability.
  Test
  `WeeklyPayroll_ThrowingLookup_AccruesLiability_WithoutSuspension_AndLaterRunPaysCurrentWeekOnlyOnce`
  passes.
- M4 — Reconcile/restore file historical goals only.
  `SaloonCircuitGoalEvaluator.RestoreHistoricalGoals` never touches
  `predicateHolding`; `ReconcileGoalStores` writes the union to the
  evaluator through it and marks director completions only for
  goals the evaluator currently counts as complete
  (`GoalHolds`). `CaptureSaveDto` overwrites the saved list with
  `evaluator.CompletedGoalIds()`, so a lapsed final cannot persist
  into the saved file. Seam tests
  `LapsedFinal_EvaluateAfterRestore_StaysAbsent_WhileHistoricalPersists`
  and `StillHoldingFinal_ReDerivesOnNextEvaluate` pass.
  Residual, by design and consistent with A-14 stickiness (below):
  the director's in-memory completed list is never un-marked for a
  lapsed final; the saved file is the corrected truth.
- M5 — Indorsement ends payee bank agency twice over.
  `IndorseOrder` clears `BankCollectionAgent` with a history entry,
  and `RedeemOrder` additionally requires an empty `IndorsedToName`
  for bank presentment (`MoneyOrderService.cs`), so a bank recorded
  after the indorsement still cannot present. Both indorsement-bank
  tests pass, including single-payment-only.

### WS1 + WS3 claims re-verified at HEAD

- Reconciler never un-completes or un-fails: `MergeLatchedGoals`
  only adds, never removes; `Evaluate` never removes a latch or a
  failure; the reconciler's union write goes through the
  historical-only path above.
- A-14 late-latch semantics unchanged: `Evaluate`
  (`ScenarioObjectiveService.cs:304-334`) still latches a qualifier
  whose predicate holds at any evaluation, including one first
  observed after its deadline, and fails only an unlatched qualifier
  past its deadline with a false predicate. The diff touches only
  the headline/failure-field code in this file; no `Evaluate` or
  latch line changed in range.
- Fee waterfall conserves: tax-sale proceeds are capped at each
  step (`Math.Min(remaining, owed)`, `remaining` decremented,
  `PropertyTax.cs:336-360`); payment splits track fees separately
  (`FeesPaidCents`, never derived from the mixed
  `AmountPaidCents`); the old-save migration caps reconstructed
  fees at fees accrued and at what was paid, preserving the previous
  recipient (first-gate R3, carried, not a defect).
- Assay split sound: `MineOreLot.CreateSplit`
  (`Domains/Economy/Businesses/Mine/MineOreLots.cs:186-212`) sets
  the child `assayed = false`, `assayId` empty, confidence reset to
  0.3, and `MineOreStock.SplitLot` removes the tons from the source
  — one assay certifies one lot, tonnage conserved.
- Asset-sale persistence sound: `AssetSaleExecutionService` books
  every executed sale on the durable
  `ManagerDecisionExecutionLedger` (`CompletedAssetSales`, deduped
  by request id) and refuses re-execution from the persisted record
  before the status gate (`AssetSaleAppraisal.cs:399-400`).
- Payroll guard sound (see M3 above).

### WS2 circulation

- NPC construction driver wired, exactly once per day:
  `DriveNpcConstruction` is called from one place,
  `OnDayChanged` (`SimulationDrivers.cs:1337`), and nowhere else.
  It advances hub projects through the production driver, commissions
  only explicitly queued requests, and only when a parcel authority
  is bound; unbound ports wait loudly instead of inventing input.
  The labor leg works only through committed `TaskAuthority` tasks
  (see R-F2 for the non-blocking remainder issue), and occupancy
  (`TryOccupy`) records a placement only for members not already
  occupying the new building — a completed project re-driven daily
  cannot double-occupy.
- Bank binding is a contract, not a second authority:
  `BankLedgerBindingRegistry` binds at most one ledger per bank,
  fail-loud on a second distinct ledger, idempotent on the same
  instance, holds references only, creates no balances, and is
  deliberately not wired into underwriting
  (`BankLedgerBinding.cs`, `SimulationSystemsHub.BankLedgerBindings`,
  `SimulationDrivers.BindBankLedger`). `BindBankLedger` has no
  production caller yet and is documented as such; the scene hookup
  stays outstanding under G4.
- Freight Invalid-handler path is loud but behaviour-identical:
  the WS2 addition in `FreightProductionDriver` only counts and
  diagnoses waiting shipments; nothing is dispatched without a real
  handler, and in-transit advance and revenue collection are
  untouched.
- Construction freight delivery cannot double-count:
  the new site `ReceiveShipment` callback
  (`ConstructionMaterialFreight.cs`) accepts into site custody only
  for shipments bound for that site, and the stockpile is credited
  only after `RunShipmentChain` and a passing
  `VerifyConservation(shipment, sale.TotalUnits)`; a partial
  delivery fails conservation instead of reaching the stockpile.
  The shipment service itself marks delivery only from the accepted
  quantity (`FreightShipmentService.cs:551-570`).

### WS4 settlement proof

- The proof's conservation claims are internally consistent with
  its test file: freight delivers 100 units exactly once
  (asserted, `Pass11Ws4SettlementProof.cs:2361`); save/restore
  continuations assert state-hash equality against uninterrupted
  runs, including double-restore; determinism is asserted by
  `Determinism_RepeatRun_Identical`; the freight mid-task case
  asserts `VerifyConservation` clean with source deducted exactly
  once and recipient custody exactly 60.
- The proof compensation is genuinely removed: the restore path
  carries only the comment that the reflection compensation
  formerly there is removed (`:1764`), and restores through the
  production `RestoreSaveDto` / `LoadFromSaveDto` paths.
- The production save/load fixes are genuinely in place:
  `ConstructionProjectSaveDto.ExecutorSequence` with
  `max(persisted, recomputed floor)` restore
  (`NpcHomeConstruction.cs`), and
  `FreightServiceSaveDto.ShipmentIdsWithDispatchAuthorities` with
  dispatch-authority rebind on restore and an old-save fallback to
  shipment state (`FreightShipmentService.cs`). All 8
  `Pass11Ws4SaveLoadFixTests` pass, covering uniqueness, old-save,
  zeroed-field, persisted-ahead, exactly-once release, and double
  restore.
- No production code was modified for the proof beyond those two
  fixes, per the proof document's scope statement, which the diff
  supports (the proof file is the only WS4 addition).

### WS5 player-facing, performance, time

- Feedback only on authoritative success: `TransactionFeedbackDecision`
  returns the success kind only when the operation's bool reports
  success, else `Warning`; its cash thresholds (10000 heavy, 1000
  medium) exactly mirror
  `GeneralStorePanelController.ResolveMoneyFeedbackKind`. All five
  panel call sites now thread the operation result through instead
  of discarding it. HUD and StartPanel text guards skip only
  identical assignments, so behaviour is unchanged.
- Dedup semantics preserved: the indexed ledger copy was checked
  field by field against `AreSameLedgerEntry`
  (`BusinessReportBuilder.cs:954-970`): keyed rows match by key,
  id rows by id, keyless rows by full field comparison, and the
  cross-category comparisons (keyed vs keyless, id vs keyless)
  agree in both implementations because `AreSameLedgerEntry`
  compares a blank key/id against a non-blank one as unequal. The
  single-entry `ContainsLedgerEntry` path at line 631 is a different
  method (one summary row), not the rollup, and was correctly left
  alone. All 22 WS5 tests pass, including the dedup equivalence
  tests and the measurement workloads.
- Time review verdict conditions accurately stated: the review's
  verdict is "TECHNICALLY APPROPRIATE, with conditions", the
  conditions are the six "Required implementation notes" (settings
  asset, enum additions, RealTime path, HUD list, agent-movement
  visual scale, old saves at removed speeds), and 1:1 is presented
  as a real-time observation mode, not a playable speed. The
  underlying code facts check out: base 60
  (`SimulationTimeSettings.cs:114`), `startingSpeed = Speed1x`
  (`:150`), `GetMultiplier` default arm falling back to 1x
  (`:196`), and the clock formula in `TimeManager.cs:360`.

### Wiring, registry, and document checks

- `ScenarioBootstrapFactory.TryCreateStart`: zero production
  callers (only its own file and tests reference it).
- `SimulationDrivers.Rebind`: zero production callers; the only
  other `.Rebind(` in the tree is Unity's `Animator.Rebind`, which
  is unrelated.
- `RealDemandSource`: still unwired and still registered. Its only
  production-tree entry points are `SharedBusinessRuntimeManager.CollectRealDemand`
  / `RegisterBusinessInputNeed`, which have no production callers
  (only a Pass 7 test calls the register path); collection was never
  wired to the live tick in Pass XI, and the registry note records
  the still-open gate-G1 decision with the double-buying hazard.
- `HouseholdMoveService`: still caller-free by decision; the only
  references outside tests are inside its own file, and the
  registry note records the routing rationale.
- `ScenarioFailureNoticeBoard`: registered as unwired in
  `Docs/UnwiredSystems.md`, not hidden in the assembly-scan
  baseline — verified: it has zero production references outside
  its own file, and the registry note says so explicitly, including
  why (the StartPanel holds no `ScenarioObjectiveService`
  reference, so wiring is Unity scene work under G4).
- Saloon/ScenarioModels scan-baseline five: each has the claimed
  production caller in a harness-excluded file, verified by grep:
  `ScenarioGoal`/`ScenarioObjective` in `ScenarioService.cs` and
  `ScenarioAsset.cs` plus both scenario definitions;
  `PopulationInclusionRules`/`PlayerHouseholdDeclaration` in
  `FirstLedgerScenario.cs` and `ScenarioAsset.cs` (plus the
  director for the declaration); `SaloonCircuitGoalEvaluator` in
  `SaloonCircuitBootstrap.cs`. None of those caller files is in the
  generated harness csproj, while the three scenario files are —
  so the classification is subset-visibility, not concealment.
  The Construction cluster classification is likewise sound:
  `NpcConstructionDriver` and its commissioner/executor are wired
  through `SimulationDrivers`, and the listed component/record
  types are consumed inside the feature.
- All 9 required documents exist with `.meta` files (Audit, Plan,
  Implementation Report, Production Settlement Proof, Integrated
  Test Results, Performance Report, Simulation Time Review,
  Minimal Unity Integration Handoff, and this Independent Authority
  Review). The new review `.meta` carries a fresh GUID, and no two
  new `.meta` files in range share a GUID.
- Master register preserves all stable IDs: `RUN-001..012`,
  `LL-PLAY-001..019`, `PASS7`, `PASS8`, `PASS9-001..014`,
  `IDENT-001`, and `PASS11-001..008` all appear in
  `Docs/Pass6_Deferred_Development_Master_Register.md`; the
  unwired registry entries match reality for every item checked
  above.
- No test weakening, no test deletion in range: the test diff is
  additions plus 5 replaced lines, all inside the scan-baseline
  name lists (reordered/expanded classifications, not assertions);
  the three failing test files are untouched in range.
- Test-count arithmetic holds: 1285 + 118 new + 1 historical
  failure fixed in production (indorsement, WS3, narrowed by M5)
  = 1404 passed, independently reproduced above.
- No Unity claim anywhere in the Pass XI documents: the
  Implementation Report, Integrated Test Results, Performance
  Report, Settlement Proof, and Time Review each disclaim Unity
  compilation / Test Runner / PlayMode success explicitly; the
  remaining mentions in the Plan describe those beats as
  Unity-dependent and outstanding, never as passed.
- No time-scale implementation exists: the diff touches no
  `SimulationSpeed`, `SimulationTimeSettings`, `TimeManager`, or
  driver-timing code — the only time-related lines in the diff are
  documentation and the pre-existing `Rebind` comment. The time
  review's own header states nothing was implemented.
- No second scenario authority, freight system, debt ledger, or
  stock ledger was introduced: the director remains the one
  scenario authority, `FreightShipmentService` the one freight
  chain, `FinancialObligationAuthority` the one debt authority,
  and the bank binding registry holds references only.
- No scenes, prefabs, terrain, Packages, ProjectSettings, or Canon
  docs were touched: the diff contains only `.cs`, `.md`, and
  `.meta` files. No serialized enum changed in range (no enum
  member added, removed, or renumbered in the diff).
- Legacy behaviour not reintroduced: abstract mine/farm production
  paths were not restored; the town-pulse path's synthetic
  fallback demand was already removed (PL-03) and its resolution
  records demand loss only, producing no revenue and no stock
  movement (`GeneralStoreRuntimeManager.cs:2280+`); the
  `RealDemandSource` replacement path was not wired (see above),
  so no manufactured revenue re-enters through it either.

## Items explicitly not verified (with reason)

- Unity compilation, Test Runner results, and PlayMode behaviour:
  not verifiable from this environment (no Unity runner; the
  bootstrap test files cannot compile in the Roslyn harness).
  This is the standing PASS11-008 / RUN-004 external gate, not a
  gap in this review — the Pass XI documents claim none of it.
- The full economy was not re-derived from first principles: the
  WS4 proof was checked for internal consistency between its
  document claims and its test assertions (cash, goods, debts,
  exactly-once save/restore), as briefed, not re-proven
  independently.
- Live-scene contents (which MonoBehaviours exist in a scene,
  registry assets, scene-placed runtimes): Unity-session work
  under G4, out of scope for a source review.

## Scope discipline

Review branch `muse/pass11-ws6-final-review` was created from
`07471dd` and contains only this document and its `.meta`. Nothing
was merged to `main` or to `muse/continuous-development`, no history
was rewritten, no stash was used in any worktree, and no production
or test file was modified by this review.
