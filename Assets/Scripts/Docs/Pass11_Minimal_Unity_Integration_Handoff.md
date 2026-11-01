# Pass XI Minimal Unity Integration Handoff

Audience: the Unity session on Kennedy's Windows machine (Unity
6000.4.2f1), project at F:\Land & Ledgers. This document states exactly
what is source-complete, what needs scene wiring, and what must be
verified in Unity. It claims no Unity result. The Roslyn harness results
quoted here are source-level only and must not be read as Unity
compilation, Test Runner, or PlayMode success.

Branch to integrate: `muse/continuous-development` (Pass XI final HEAD
is stated in the owner's final report; this document was written at
pre-final-gate tip 6573b31 and must be re-checked against the final HEAD
before use). The Unity project itself remains on
`integration/pass6-living-settlement-gameplay` at 6d41860 until a
separately authorized integration; this pass changed nothing there.
The continuous branch descends from exactly that baseline, so the
integration is a fast-forward of source content plus the scene checks
below, not a merge of divergent histories. Never merge to main as part
of this handoff.

## What is source-complete in this pass

- Scenario start and objectives (WS1): director start hook calls
  BeginScenario; drivers day change calls Evaluate; dated qualifiers
  persist and rebuild after load; three goal stores reconcile to one
  union; Saloon Circuit qualifiers registered (Ash Creek day 365,
  Prairie Crossing day 730). Completion level: Connected, source
  exercised. Scene work still needed: see Scene inventory below.
- NPC construction daily drive (WS2): NpcConstructionDriver advances
  hub projects one step per day through the driver; site stockpile is
  the freight receiving authority. Outstanding inputs (not invented):
  catalog asset, yard/sawmill lot adapter, TitleAuthority parcel
  adapter, commissioning trigger rule. Until those land, the driver
  advances already-staged packages and waits loudly on Ready packages
  and completion.
- Bank binding contract (WS2): BankLedgerBindingRegistry (one ledger
  per bank, fail-loud on a second, live reads, no cached balance),
  hub-owned lazy property, SimulationDrivers.BindBankLedger binding
  point. Deliberately NOT wired into underwriting (the lender ledger
  and BankRuntime ledgers are disjoint; binding both would
  double-count). Scene work: player-bank scene binding (G4).
- Freight Invalid-handler loudness (WS2): behaviour unchanged,
  diagnostic now names the gated-shipment count, the missing-handler
  cause, and what still moves. Scene question: whether runtimes are
  scene-placed (G4).
- Integrity defects (WS3 plus WS6 first-gate fixes): indorsement
  presentment (including bank-agent presentment after indorsement),
  tax fee waterfall, dispute dedup plus feed plus persistence, assay
  split provenance, offer multiplier feed, payroll fail-closed guard
  with refused-week accrual as an operating liability, asset-sale
  record persistence, owner-reporting separation. Postage stays
  honest-unset (no funded sender invented).
- Settlement proof (WS4): 30-day production-path proof in the Roslyn
  harness, cash and goods conserved exactly, save/restore exactly-once
  at all midpoints, determinism identical across two fresh runs. This
  is NOT the Unity RUN-004 proof; the Main Scene 30-day autonomous
  cycle must still be performed in Unity.
- Save/load persistence fixes (WS4 follow-up): construction sequence
  and freight dispatch authorities now persist (additive DTO fields,
  old saves load safely). Proof compensation removed; the proof now
  exercises the production fix.
- Player-facing source (WS5): loan feedback only on authoritative
  success; headline deadline sentinel normalized on both live and
  post-load paths; scenario failure source surface (headline failure
  fields, presentation builder, exactly-once notice board); blocked
  cash/threshold actions no longer play success sounds; HUD and
  start-panel text assignment skipped when unchanged. Panel call sites
  themselves are outside the harness subset: Roslyn parse-clean, not
  harness-compiled or Unity-verified.
- Performance (WS5): BusinessReportBuilder.CopyLedgerEntries indexed
  dedup (weekly 105 ms to under 1 ms, monthly 286 ms to 1 ms, same
  outputs). No Unity FPS claim.
- Simulation-time review (WS5, research only): verdict technically
  appropriate with conditions. NOT implemented. See the conditions
  section below; do not implement from this handoff alone without the
  owner's decision and the acceptance tests in
  Docs/Pass11_Simulation_Time_Review.md.

## Scene inventory (G4, must be confirmed in the Unity session)

Confirm the scene actually holds, exactly once each unless stated: one
SimulationSystemsHub, one SimulationDrivers object, one
SharedBusinessRuntimeManager, one TimeManager, the ScenarioDirector,
the start panel, the registry assets, the First Ledger bootstrap and
the Saloon Circuit bootstrap, and a CalendarAnchor matching the
canonical calendar (DaysBeforeAnchorYear 3652). Confirm the start hook
runs in a real scene. If a scene holds no bootstrap, bootstrap code
must assign ScenarioDirector ObjectiveService before any start, or a
start warns and tracking stays idle (contract documented on the
director property). If a scene holds the Saloon bootstrap without the
First Ledger bootstrap, director restore now runs from the Saloon
restore itself (WS6 first-gate fix M1); verify that path in Unity.
If a bootstrap awakens after the first start, it now self-registers
qualifiers on Awake (fix M2); verify that path in Unity.

Additional scene checks from this pass:

- One drivers object: the PreWeeklyResetCallback guard refuses a second
  distinct setter with a loud diagnostic and keeps the first valuation
  posting. If two SimulationDrivers objects ever exist in a scene, the
  second one's valuation posting is lost. State the single-drivers
  assumption; do not silently add a second drivers object.
- Rebind is not a production entry point yet (zero production
  callers). Do not describe it as production rebind until a scene or
  load path actually calls it. Its guarded parts are double-call safe;
  it uses ??= throughout, so it cannot recover from stale non-null
  references, and it never re-registers objective qualifiers.
- Bank scene binding: bind the player-owned bank ledger through
  SimulationDrivers.BindBankLedger (one ledger per bank, fail-loud on
  a second). Do NOT bind both the lender ledger and BankRuntime
  ledgers for the same bank (double-count). Verify deposit refresh
  before underwriting once bound (RUN-003).
- StartPanel failure-board wiring (PASS11-006): wire
  ScenarioFailureNoticeBoard and ScenarioObjectivePresentation into
  the live panel. The panel currently reads director state only and
  holds no ScenarioObjectiveService reference. The source surface
  (board, builder, headline FailedGoalId and FailedDeadlineDayIndex)
  is done and tested; the panel wiring is scene/reference work.
- Boarding RegisterHouse (PASS11-004): bootstrapped houses get runtime
  rooms but no HousingAuthority building or LodgingRegistry
  registration until a parcel mapping and a boarding trigger rule
  exist. Do not invent the mapping in the scene; it needs the
  BusinessInstanceState AssignedBuildingId mapping plus a trigger
  rule.
- ScenarioBootstrapFactory.TryCreateStart still has zero production
  callers. Do not wire it blindly; the live start path is
  ScenarioDirector.SwitchScenario.
- SerializeField removals (WS1): the hub animal, capability, and
  employment registry fields lost SerializeField because none of those
  types carries Serializable and all hold Dictionaries Unity cannot
  scene-persist. The lazy properties and DTO save path own the runtime
  instances. No scene data is lost by this change, but verify the hub
  inspector shows no broken references after integration.

## Unity verification checklist (all outstanding, none claimed)

1. Compile the full Unity project on the integrated branch, including
   the four harness-excluded files (SimulationDrivers,
   SharedBusinessRuntimeManager, SimulationSystemsHub,
   SchoolhouseModels) and all Pass XI edits to them (PASS9-014, narrowed
   by the audit's full-repo Roslyn build showing zero errors in those
   files, but NOT closed by it). Report any Unity-only error with file
   and line; do not fix by weakening a Roslyn test.
2. Run the Unity Test Runner, including the M1/M2 Unity-runner
   bootstrap tests
   (Tests/Editor/Scenarios/Pass11Ws6FirstGateBootstrapTests.cs,
   PASS11-008) and the full editor suite. RUN-006 Test Runner
   reliability (batch hangs, missing result XML) is still open; if the
   runner hangs, report the hang with the last passing test, do not
   report a partial run as a pass.
3. PlayMode: New Game through scenario start, 30-day autonomous cycle
   (RUN-004), scenario completion and failure paths (including a
   deadline failure staying failed), save/load mid-shipment,
   mid-production, mid-employment, and mid-objective with exactly-once
   continuation, and old-save loading (pre-Pass-XI saves: qualifier
   registrations absent, fee fields absent, ExecutorSequence absent,
   dispatch-authority list absent; each has a defined fallback, verify
   them in Unity rather than trusting the Roslyn fallbacks).
4. Scene inventory (above) plus: confirm no duplicate manager roots
   (LL-PLAY-002), no duplicate Civic Foundation (LL-PLAY-003), and
   scenario identity, objectives, and deadlines visible in the HUD
   (LL-PLAY-005; source surface done, HUD wiring is Unity work).
5. The 3 remaining Roslyn failures are test-side defects (see the
   implementation report for face evidence). They do not block Unity
   integration, but do not "fix" them in Unity by editing production
   code to match wrong assertions; they await a dedicated
   test-correction review. Note the settlement test is wrong in three
   places (net, both funds), not one.
6. Old-save loading checks specific to this pass: Saloon goals survive
   a load without the First Ledger bootstrap path (M1); late-awakening
   bootstrap registration (M2); lapsed Saloon final-state goals do not
   resurrect into saved CompletedGoalIds (M4); indorsed orders cannot
   be redeemed by the payee's bank agent (M5); refused-week payroll
   liability appears exactly once and is not double-paid by automated
   payroll (M3; manual PayAgreedWage for the same week after recovery
   is the known diagnostic-only residual, PASS11-003).

## Harness configuration the Unity session does NOT need, but a future source setup does

The Roslyn harness subset (`harness/sources.txt`, local config, not
repo-tracked) gained in this pass: six ReadModels/Reporting files, ten
Construction files, three Saloon/ScenarioModels files, and two
Presentation/UI feedback files; the verbatim
IConstructionMaterialPricePort extract was removed from
`harness/Shims/WorldExtracts.cs`. These are Roslyn-harness-only
concerns. They do not change Unity compilation, but a future source
worker that reproduces the 1404/3 Roslyn count must apply them, or its
scan test and feedback/seam tests will fail for configuration reasons
rather than code reasons. Full detail is in the implementation report.

## Simulation-time proposal (research only, NOT approved, NOT implemented)

Do not implement the speed list from this handoff. The review verdict
is technically appropriate with conditions (settings assets serialize
60 and need an asset update; 1:1 needs a TimeManager special case, not
a multiplier field; AgentMover visual scale needs an explicit
floor-or-decouple decision; HUD list changes; 1:1 is an observation
mode only). The 11 mandatory acceptance tests are in
Docs/Pass11_Simulation_Time_Review.md. Any implementation needs the
owner's decision first, then the asset updates, then the acceptance
tests, in that order.

## Deferred items the Unity session owns (not source work)

RUN-001 (boarding end-to-end proof), RUN-004 (Main Scene 30-day cycle),
RUN-005 (camera/visibility continuity), RUN-006 (Test Runner
reliability), RUN-009 (building fit/collider warnings), RUN-010
(scenario preset authoring UI), RUN-012 (Gregorian calendar and
scenario migration verification), PASS9-012 (scenario catalog plus
CalendarAnchor scene-side remainder), PASS9-014 (Unity compilation of
harness-excluded files), PASS11-006 (StartPanel failure-board wiring),
PASS11-008 (M1/M2 Unity-runner tests), and the LL-PLAY set. The source
pass deliberately did not touch scenes, prefabs, terrain, Packages,
ProjectSettings, or Canon documents, so none of these can be closed
from the source side.
