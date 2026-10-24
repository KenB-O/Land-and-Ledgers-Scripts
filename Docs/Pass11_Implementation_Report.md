# Pass XI Implementation Report - Unity Integration Readiness, Live Simulation Hardening and Remaining Canon Implementation

Branch: `muse/continuous-development`. Base for this pass: Pass X continuous
tip `cc59bec` (Pass IX `bb2e4bc` plus the Pass X reconciliation document).
Final pre-final-gate tip: `6573b31` (the WS6 final-gate review may add
fix commits after this report; the final report to the owner states the
final HEAD).

Completion-level honesty, stated once and applied throughout: everything
in this pass is Implemented, Connected, or Source exercised in the Roslyn
harness. NOTHING in this pass is Unity compiled or Unity verified. No
claim in this document about Unity compilation, Test Runner results, or
PlayMode behaviour is made, and none may be inferred from a Roslyn count.

## Pass X conditional, resolved and recorded

The Pass X reconciliation document carried a conditional: the continuous
branch could be promoted as the authoritative Unity-aligned branch only
once the live Unity checkout state was verified. That condition was
resolved before Pass XI by an independent inspection of the real Unity
project (not by Pass XI work): the Unity project at F:\Land & Ledgers is
on branch `integration/pass6-living-settlement-gameplay` at HEAD
`6d41860` with a clean tree, no uncommitted or untracked source changes,
and no additional local-only commits, Unity 6000.4.2f1. The continuous
branch descends from exactly that baseline, so it is now the
authoritative Unity-aligned branch. The Unity project itself remains on
the older source until a separately authorized integration. Nothing in
Pass XI modified the owner's Unity checkout. This supersedes the
conditional language in
`Docs/Pass10_Source_Reconciliation_and_Continuous_Branch.md`.

## Harness progression (Roslyn harness only, proper regeneration each time)

| Checkpoint | Passed | Failed | Change |
|---|---|---|---|
| Pass IX baseline (cc59bec) | 1285 | 4 | Historical RUN-011 set |
| Audit merged (986644e) | 1285 | 4 | Documents only |
| WS1 merged (e5d23b6) | 1297 | 4 | +12 WS1 tests |
| WS3 merged + coordinator integration (3af1863) | 1327 | 3 | +26 WS3 canonical tests, +3 OwnerReporting tests now in subset, +1 historical failure fixed in production (indorsement) |
| WS6 first-gate fixes + WS2 merged + Saloon scan classification (2101ea3) | 1353 | 3 | +8 fix tests, +18 WS2 tests |
| WS4 + WS5 merged + NoticeBoard registry (7c56fb0) | 1396 | 3 | +21 WS4 proof tests, +22 WS5 tests |
| WS4 save/load fixes merged + proof doc amended (6573b31) | 1404 | 3 | +8 save/load fix tests |

Total new Pass XI tests: 118. Net: 1285 passed plus 118 new plus 1
historical failure fixed in production equals 1404 passed, 3 failed.
No test was deleted. No assertion was weakened. No new failure was
reclassified as historical.

The 3 remaining failures are documented test-side defects, left failing
deliberately, with face evidence in the test text itself and agreement
from two independent reviews (the WS6 first-gate reviewer spot-checked
all three against production code):

- `MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree` (line
  202): asserts IsNotNull with the message "No duplicate of a non-live
  order" while production correctly returns null for a non-live order.
  The assertion contradicts its own message.
- `MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull` (line
  277, plus lines 280-281): asserts net 2500 with the message "3500
  minus 500 netted bilaterally", which is 3000, which is what production
  computes. Correct values would be net 3000, po-a fund 0, po-b fund
  4000. The test is wrong in three places, not one, but it is still
  test-side.
- `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`
  (line 186): expects Assessed at day 100, past the 60-day due date
  (PropertyTaxCalibration DueDaysAfterLevy 60), where the delinquency
  model proven by sibling tests in the same file reports Delinquent.

Recommended next step, not taken in this pass: a dedicated
test-correction review that fixes exactly these three tests. Changing
historical tests mid-pass risks masking a production bug, so the
conservative, auditable choice was to leave them failing with the
evidence attached.

## Workstream results

### Phase 1 - Integration-readiness audit and plan (branch `muse/pass11-audit-plan`, 57bfcfe)

Deliverables: `Docs/Pass11_Unity_Integration_Readiness_Audit.md` (findings
A-01 to A-37, split fixable-in-source vs requires-Unity-Editor) and
`Docs/Pass11_Implementation_Plan.md` (workstreams WS1 to WS6, gates G1
to G5). Two brief corrections came out of the audit and are recorded
here because later work depends on them. First, the brief path
`Orchestration/Systems/SharedBusinessRuntimeManager.cs` does not exist;
the real path is `Domains/Economy/Core/SharedBusinessRuntimeManager.cs`
plus two partials, 11290 lines combined. Second, the Pass IX handoff
line about calling `ScenarioBootstrapFactory` from the start panel is
partly stale: the panel already calls `ScenarioDirector.SwitchScenario`
(the live asset-based start path); what was missing was any production
call to `ScenarioObjectiveService.BeginScenario` or `Evaluate`, plus
qualifier re-registration after load. `ScenarioBootstrapFactory.TryCreateStart`
still has zero production callers at the end of this pass; wiring it
into the New Game clock path would have been an invented call site, so
it was recorded, not wired.

On the four harness-excluded files, the audit narrowed PASS9-014 but did
not close it: a harness-subset variant build gave 112 errors, every one
a type existing in the repo outside the subset (a subset artifact, not
a source defect); a full-repo Roslyn build of 1154 files gave 1358
error lines, all CS0246/CS0234, zero in any of the four files, the bulk
being Unity package types the harness shims do not provide. Real Unity
compilation, scene serialization, and PlayMode remain unproven and stay
Unity-only items.

### WS1 - Integration foundations (branch `muse/pass11-ws1-integration-foundations`, e28c1a5, 3 commits)

- Scenario start wiring: `ScenarioDirector.SwitchScenario` now begins
  the hub objective service for the started scenario and fires a new
  `ScenarioStarted` event; drivers day change evaluates the active
  scenario with the current absolute day index.
- Dated qualifier registrations persist in run state as goal id plus
  deadline plus stable scenario-plus-goal predicate key; bootstraps
  rebuild live predicates after load, adopting fresh definitions only
  for old saves with no registrations. Unrebuilt goals are reported
  loudly, not silently ignored. Headline snapshots fall back to
  persisted deadlines instead of reporting -1 after load.
- Saloon Circuit qualifier set registered from live evaluator state
  with its two authored deadlines (Ash Creek day 365, Prairie Crossing
  day 730) and the no-deadline sentinel elsewhere.
- Three goal stores reconcile to one union on capture and restore,
  never un-completing a goal and never un-failing a run, with every
  disagreement logged. Restore suppresses the start hook so hub-loaded
  run state is never wiped. The director stays the one scenario
  authority; no second authority was created; no scenario definitions
  were altered.
- Drivers `Rebind` entry point: re-runs scene discovery, TimeManager
  subscription, and AttachBusinessRuntime wiring, safe to call twice
  through the existing guards. It forwards an already-assigned
  PlayerDirector, which still cannot be scene-discovered because it is
  plain C#. Rebind has zero production callers at the end of this pass;
  it must not be described as production rebind until a scene or load
  path actually calls it (WS6 first-gate finding R1).
- BankRuntime registered honestly as the thirteenth unwired runtime in
  `Docs/UnwiredSystems.md` (zero production references outside its own
  directory, no ledger registered as a CreditCash store, no live
  deposit refresh), with a note recording why both registry tests
  missed it. `ScenarioObjectiveService` entry removed because WS1 wired
  it deliberately.
- PreWeeklyResetCallback guard: the callback keeps its single slot but
  refuses a second distinct setter with a loud diagnostic and keeps the
  installed valuation posting. SerializeField removed from the animal,
  capability, and employment registry fields only (none of those three
  types carries Serializable and all hold Dictionaries Unity cannot
  scene-persist).
- PASS9-006 watermark: new pure `DomesticTickGate` class with the exact
  old rule (only forward motion advances); `SaveLoadManager` resets it
  to the loaded clock after every restore, touching only the float, so
  completed work, pending tasks, and person-time reservations are
  preserved.
- 12 new tests, all passing. Deliberately unchanged: A-14 late-latch
  semantics (see Decisions below).

### WS3 - Integrity defects (branch `muse/pass11-ws3-integrity-defects`, 0303d41, 9 commits)

- RUN-011 indorsement (d9f06d7): genuine production bug fixed. A payee
  could still present after indorsing an order away; presentment by the
  payee now requires no indorsement on the order. Open edge, not
  invented: an order both indorsed and bank-collected still presents
  through the bank agent check (closed later by WS6 first-gate fix M5,
  which clears BankCollectionAgent on indorsement and gates bank
  presentment on empty IndorsedToName).
- PASS9-002 fee waterfall (6482cfa): FeesPaidCents plus FeesPaidTracked
  tracked separately on the assessment, so fees accruing after a
  partial principal payment route to the County Tax Collector instead
  of the civic treasury, with no double collection. Old saves
  reconstruct fees-first. The player leg now refuses a Satisfied
  assessment before spending, so no debit is stranded. Open question,
  not silently changed: the player leg still credits the civic
  treasury for its fee portion because no collector cash store exists
  in its call context; the fee split is booked on the assessment so
  later collector wiring can use it. Known limitation for old saves
  (WS6 first-gate finding R3): the migration reconstructs FeesPaidCents
  as Min(AmountPaid, fees accrued), so principal paid before fees
  accrued marks fees as paid that were never paid, and a later
  AuditTaxes routes zero fees to the collector. This matches pre-fix
  behaviour and is documented as best-effort, not a regression.
- PASS9-003 dispute records (257d364): same (parcel, claimant, basis)
  dedup on Open or Contested claims, distress-sale feed, player-path
  persistence through the acquisition manager DTO, title authority
  integration with claims remaining records never ownership, evidence
  redaction preserved with diligence clues showing counts and
  certainty only. Coordinator integration commit ce76318 added the
  one-line drivers hook WS3 requested:
  `saleContext.FeedDisputes(acquisitionMarket.PropertyDisputes)` in
  `DriveNpcPropertyLifeDay`, null-safe.
- PASS9-005 assay provenance (f976556): split child no longer inherits
  the assay certificate; it starts unassayed at 0.3 estimate confidence
  and earns its own assay through RecordAssay. Tonnage conserved,
  parent keeps its certificate, assayed lots still never re-merge, old
  saves with a duplicated assay id load with exactly one certified lot
  plus a diagnostic.
- PASS9-007 safeguards (689a405): offer multiplier weekly leg now reads
  financing-excluded WeeklyOperatingProfitCents instead of
  WeekToDateNetCents; AllTimeNetCents distress gate unchanged; payroll
  guard fails closed on lookup exceptions, records failure and
  exception type, all three payroll callers diagnose lookup failure
  distinctly from confirmed dead or off-map person. Withholding moves
  no cash and invents no liability. The WS3 comment claiming the
  refused week's wage could still be paid later was false for the
  weekly employment path and was corrected by the WS6 first-gate fix
  M3 (see below).
- PASS9-001 asset-sale records (eb7662a plus golden follow-up 0303d41):
  executed sale records persist through the decision ledger DTO with
  additive CompletedAssetSales list; re-execution refused from the
  persisted record before status and action-key gates; old saves load
  empty rather than null. The full run caught one consequence: the
  manager-ledger byte golden changed by exactly the appended empty
  list, so the golden was updated to the new approved bytes with the
  assertion still checking exact equality.
- PASS9-004 postage (4409026): documented honest-unset, no production
  change. No safe funded sender exists in existing contracts; the
  advice letter has zero production callers and no funded sender in
  its call context; generic mail has no production PostItem caller.
  No sender was invented. Locked by test: with a production-shaped
  resolver wired, an unfunded advice sender is refused with a named
  diagnostic and nothing is registered, while a real funded sender
  household pays its 3c letter postage.
- PASS9-009 owner reporting (baa0b83): FillStaffing now excludes owner
  worker rows from filled-worker and weekly payroll, tracking them in
  additive ownerLaborSlots and ownerLaborWageCents with an
  informational gap note; the slot still counts as filled; fields
  carry through CopyStaffing into the monthly rollup and onto the
  portfolio business line. Portfolio financial payroll totals were
  already clean since runtime payroll expense never pays owner slots.
  Pass VIII financing separation locked by test rather than
  re-implemented. Broader A3-11 portfolio and Unity surfaces
  (owner-personal cash, protected cash, principal debt, consolidated
  equity never presented as spendable cash) remain open.
- PASS9-011 documentation only: mine (line 4149) and farm (line 4194)
  delegate to detailed task drivers in SharedBusinessRuntimeManager,
  while all other archetypes still resolve through the generic
  ResolveBusinessOperation path (line 4365) crediting output at
  resolution time. Converting each remaining archetype is WS2-scale
  routing work with dual-execution risk, so it was not attempted.
- PASS9-013 measurement only: SaveLoad-filtered run 71 passed, 0
  failed, 72s wall clock (WS3 run); canonical full run 77s. No
  bottleneck confirmed, nothing optimized, no timing assertions.
- Explicitly out of scope, untouched: PASS9-006 (WS1 drivers hook),
  PASS9-008 (needs Kennedy decision, gate G1), PASS9-010 (needs gates
  G2/G3), PASS9-012 (scene-side remainder, documented only), PASS9-014
  (Unity-only).
- 29 new tests (26 canonical plus 3 OwnerReporting, which required
  the six Reporting files to enter the harness subset; see Harness
  configuration below).
- Coordinator integration commit 3af1863 classified the 20 newly
  visible report DTO/snapshot types as feature-internal in the scan
  baseline, with a dated comment documenting the precedent (consumed
  by the builders and by Unity presentation; same category as existing
  BusinessWorkReadModel and save-DTO clusters). No genuinely unwired
  system was hidden. The WS6 first-gate review later corrected that
  comment (R2): the builders themselves are static-excluded from the
  scan (static classes are abstract plus sealed in IL), not accounted
  for by production callers, which have zero production .cs callers.

### WS6 first gate - Independent review of WS1 plus WS3 (at 3af1863)

Verdict: PASS WITH FIXES. No P0 found. No double payment, no phantom
money, no goal un-completion, no run un-failure in the reconciler.
The reviewer read the full diff 986644e to 3af1863 (52 files, 2917
insertions), traced each changed production method to its production
callers, and independently verified 1327 passed, 3 failed. All 41 new
Pass XI tests at that point passed. The reviewer fixed nothing itself;
the fixes below were implemented by a fix worker on branch
`muse/pass11-ws6-first-gate-fixes` (56f338a, 8 commits) and merged
(554b7ba):

- M1 (P1): Saloon restore runs director restore itself when the
  director has no active scenario; every skip path logs loudly.
- M2 (P2): both bootstraps self-register qualifiers on Awake when the
  director already runs their scenario; registration is idempotent.
- M3 (P2): throwing payroll lookup accrues the refused week's wage as
  an operating liability via the named-payable path (option a);
  confirmed-unavailable persons still accrue nothing; suspension was
  deliberately not copied (a broken lookup is not an employer default,
  and suspension would strand every future week's wage). Automated
  payroll cannot double-pay the liability (weekly payroll pays the
  current week only, never arrears). Residual, recorded as PASS11-003:
  a manual PayAgreedWage for the identical missed week after lookup
  recovery could duplicate the accrued liability; the weekly
  diagnostic directs settlement through the liability route, and
  mechanical enforcement is a design decision, not something invented
  here. Second residual, PASS11-007: the legacy-slot payroll path
  still refuses a throwing lookup with no accrual, deliberately
  unchanged to keep the diff minimal.
- M4 (P2): Saloon reconcile and DTO restore use new
  RestoreHistoricalGoals, filing only historical ids; lapsed finals
  no longer enter predicateHolding or CompletedGoalIds, still-holding
  finals re-derive on the next Evaluate, historical goals are never
  un-completed.
- M5 (P3): IndorseOrder clears BankCollectionAgent AND RedeemOrder
  gates bank presentment on empty IndorsedToName (the second gate is
  needed because RecordBankCollection can run after IndorseOrder).
- R2: scan-baseline comment corrected (see WS3 above).
- Recommended, not blocking, and carried into the handoff: Rebind is
  not a production entry point yet (R1); fee-waterfall old-save
  migration preserves the old misattribution in the principal-first
  case (R3); the PreWeeklyResetCallback two-drivers case keeps the
  first valuation posting and logs an error, so the single-drivers
  assumption must be stated (R4); Evaluate late-latch is pre-existing
  and now live (R5, which is A-14 below).
- The fix worker's "4 failures at base" discrepancy was resolved by
  the coordinator as a harness-configuration artifact: the shared
  sources.txt already carried WS2's Construction additions, so at base
  3af1863 the Construction types compiled without WS2's scan-baseline
  classification and the scan failed, and NpcConstructionDriver.cs was
  a stale sources.txt entry at base. After the WS2 merge that entry
  exists and its types are classified, so the artifact disappears.
- The fix worker's M1/M2 Unity-runner bootstrap tests
  (`Tests/Editor/Scenarios/Pass11Ws6FirstGateBootstrapTests.cs`) were
  NOT executed in the source environment (no Unity runner; the
  bootstrap files cannot compile in the Roslyn harness) and are
  recorded as PASS11-008, needing a real Unity Test Runner pass.

### WS2 - Live economic circulation (branch `muse/pass11-ws2-economic-circulation`, 2adf57b, 7 commits)

- NPC construction driver (A-03, RUN-002): new NpcConstructionDriver
  plus daily DriveNpcConstruction in SimulationDrivers, advancing hub
  projects one step per day through commission, funding, staging, real
  labor, advancement, completion, and occupancy. 9 driver tests, all
  passing, driving through the driver only. Genuine defect fixed en
  route: ConstructionMaterialFreightCoordinator built shipment hooks
  with no receiving authority, so site freight could never complete;
  the site stockpile is now the receiving authority with a destination
  check. Still outstanding, not invented: a catalog asset, a
  yard/sawmill lot adapter, a TitleAuthority parcel adapter, and the
  commissioning trigger rule. Until those land, the driver advances
  already-staged packages and waits loudly on Ready packages and
  completion.
- Banking (A-06, RUN-003): verified NPC business and household cash
  binding are already live (no gap proven). Found the real gap: the
  live lender reads its own ledger live at underwriting (no stale
  snapshot), but depositor deposits only ever reach BankRuntime
  ledgers, which have zero production instances, so the two ledgers
  are disjoint and binding both would double-count. Delivered
  BankLedgerBindingRegistry (one ledger per bank, fail-loud on a
  second, live reads, no cached balance, no balances created) with a
  hub-owned lazy property and a SimulationDrivers.BindBankLedger
  binding point. Deliberately NOT wired into underwriting. 3 tests
  passing. Player-bank scene binding stays outstanding under G4 and
  must never be described as wired.
- Housing and boarding (A-10): boarding runtime registration, bed
  capacity, drives, tenancy, rent, arrears, closure, and save/load all
  verified live. Genuine gap NOT patched with invented data, recorded
  as PASS11-004: BoardingLodgingBridge.RegisterHouse has zero
  production callers, so bootstrapped houses get runtime rooms but no
  HousingAuthority building or LodgingRegistry registration, and
  bridge CheckIn always refuses. Fixing it needs a parcel mapping
  (BusinessInstanceState carries only AssignedBuildingId) and a
  boarding trigger rule. HouseholdMoveService stays caller-free BY
  DECISION: 3 tests prove its atomic guarantee, but no approved
  gameplay path requires whole-household atomicity, and acquisition
  closing occupancy does not state relocation vs second property, so
  routing it without a relocation flag would change its semantics.
- Freight registration (A-25): EnsureCompany keyed by business
  instance id (no duplicates), save/load restores shipments exactly
  once, payer resolution to real tills with no legacy fallback. The
  Invalid-handler path was safe but silent: it now emits a loud
  diagnostic naming the gated-shipment count, the missing-handler
  cause, and what still moves. Behaviour unchanged. The
  scene-placed-runtime question still needs G4 scene confirmation.
  3 tests passing.
- Named-buyer distress sales: DOCUMENTED AS BLOCKED, honest refusal
  kept, recorded as PASS11-005. No legitimate named-buyer source
  exists in contracts. Ruled out: the business liquidation port
  (business tills, not households), HousingSearch negotiations (no
  standing solvent-buyer register; picking an arbitrary household
  would be an invented forced-sale power), and tax-sale bids (name
  strings only, no tax sale conducted). Missing: a solvent household
  buyer register with willingness evidence, an appraised parcel
  sale-price feed, and a creditor consent source for note assumption.
- Demand (G1): RESEARCH ONLY, nothing wired. RealDemandSource is a
  collection-side replacement for the retired town-pulse path: it
  builds intents from real needs and never moves money or goods. Live
  purchase already happens through embodied shopping and the general
  store sale authority. The WS2 report contains the Kennedy decision
  memo with four options and recommends the intent-executor option
  only if demand collection should affect play, otherwise leaving it
  unwired, with the double-buying hazard (intents vs embodied shopping
  consuming the same needs exactly once) as the central design fact.
- Migration, mine closure, archetype outputs: PLANS ONLY (capacity
  clause invoked; each is WS2-scale routing work with dual-execution
  risk). A scoped migration integrator mapping accepted intents to
  OffMapJourneyAuthority journeys exactly once; a stage-gated mine
  closure driver gated on the G2 mineral reserve decision; per-kind
  archetype delegation retiring archetype handling first, one kind at
  a time.
- Registry accounting: adding the Construction files to the harness
  subset surfaced 13 feature-internal construction types, classified
  into the scan baseline with a dated WS2 comment (mine, lawyer, and
  report-DTO precedent), NOT registered as unwired.
  BankLedgerBindingRegistry instead got its production home (hub
  property plus drivers binding point), so it is accounted by a real
  production reference.
- 18 new tests, all passing. No WS1 scenario files touched, no WS3
  tax/dispute/assay/offer/payroll/asset-sale/postage/owner-reporting
  code touched, no historically failing test touched.

### WS4 - Production-driven 30-day settlement proof (branch `muse/pass11-ws4-settlement-proof`, bde43be, 1 commit)

Deliverable: `Docs/Pass11_Production_Settlement_Proof.md` and 21 proof
tests in `Tests/Editor/Integration/Pass11Ws4SettlementProof.cs`. Days
0 to 29, 5 households, 5 businesses, actual production bootstrap,
drivers, and authorities wherever they can be instantiated outside
Unity. Never a simplified economy, never bypassing production logic,
never manufacturing outcomes.

- Cash conserves exactly: initial 2647500 plus boundary inflow 4100
  (grain export 3500 plus ore sales 600) minus boundary outflow 2275
  (flour import 2250 plus postal fee 25) plus liquidation 0 equals
  2649325 actual. Ending: mill 28100, bakery 1650, farm 11700, store
  24160, mine 0; H1 0, H2 4260, H3 3260, H4 59295, H5 0; bank vault
  2488000, yard purse 28000, treasury 900.
- Goods exact: grain 720 (520 plus 400 harvest plus 200 labor minus
  300 milled minus 100 exported), flour 270, bread 32, ore 0,
  in-transit 0; milled 300, baked 100, retailed 118, ore dug equals
  sold equals 30, freight delivered 100, yard fieldstone 920, lumber
  50.
- Debts: FinancialObligationAuthority Reconcile empty; 5 obligations,
  12996 outstanding (mill loan 12081, H1 rent arrears 340 plus 225, H5
  boarding arrears 100, mine payroll payable 250); deposits owed 0;
  mill tax 900 paid, landlord parcel Delinquent at 1204.
- Postal (separate books): po-a 2500 plus po-b 2500 equals 5000, fees
  25, order Paid, second redemption refused.
- Adverse outcomes, all genuine: unaffordable purchase refused; mine
  day-18 payroll suspended (FOA Payable, no cash paid); empty supplier
  refused; ship-2 delayed day 9, delivered day 12; 1000-unit shipment
  blocked pre-load with zero cargo; mine assay 0.02 vs 0.10 break-even,
  closed orderly day 20 (staff released 1); cabin-2 framing stalled at
  50/600 lumber; bakery loan 25000 refused; H1/H5 rent arrears as real
  Payables; demand 2 to 1 loaves/day from day 16; bread-120 objective
  FAILED at its day-25 deadline and stayed failed (supply cap 110
  below 120 by stock, not tuning). 7 refusals logged, zero
  "unexpectedly" diagnostics.
- Save/restore exactly-once (hash-equal continuations to day 30):
  mid-shipment (7,0), mid-transaction (8,0), mid-employment (4,5),
  mid-production (21,1), mid-tax-obligation (21,0), mid-objective
  (16,0), double-restore at (8,0), mid-load freight task (source
  deducted exactly once, custody exactly 60), equipment
  reserve/refuse/release round-trip, mid-batch bakery chain, farm
  driver save/load (production exactly once). Determinism: two fresh
  full runs produce identical hashes.
- This is NOT the Unity RUN-004 thirty-day proof, which stays open
  until performed in Unity.
- Subsystems not exercised, with blockers, all in the proof document:
  Saloon Circuit goal ladder (no saloon in the WS4 town; evaluator
  covered by WS1/WS6 tests); boarding-house business runtime (not in
  the harness subset; boarding exercised as tenancy plus payment
  terms); Unity bootstrap (SimulationSystemsHub, scene bindings,
  ScenarioUnityWiring, cannot instantiate in the Roslyn harness); full
  formation execution (NpcBusinessCreationAdapter not in the harness
  subset; production Validate probed with a stub port, and Validate
  never invokes the port); player tax driver in the main loop (probe
  only, status never asserted, known P5 test-side defect).
- Two production save/load findings were reported, not fixed in the
  proof, and then fixed in the follow-up round below. The mine
  close-down creditor payment to person:5 needed the employee's
  household ledger registered as the person:5 bridge store; that is
  scenario-side wiring (production correctly flags an unregistered
  creditor), not a production defect.

### WS4 follow-up - Save/load persistence fixes (branch `muse/pass11-ws4-saveload-fixes`, 82648b6, 2 commits)

- Gap 1 (PASS11-001): ConstructionProjectExecutor.sequence was absent
  from ConstructionProjectSaveDto, so a restored executor re-minted
  colliding ids. Fixed with additive ExecutorSequence (default -1
  means absent); restore takes max(persisted, recomputed floor from
  restored ids), so old saves load safely.
- Gap 2 (PASS11-002): FreightShipmentService.dispatchAuthorities was
  absent from FreightServiceSaveDto, so a restored in-transit shipment
  completed without releasing its wagon reservation. Fixed with
  additive ShipmentIdsWithDispatchAuthorities; restore rebinds the map
  for transport-bound live shipments, with an empty list on old saves
  falling back to shipment state. Release stays idempotent; double
  restore overwrites the same map key. Limitation: if
  hooks.Reservations is null at restore, nothing can be rebound (same
  pattern and limitation as the existing task-binding rebind).
- Proof compensation removed (both reflection blocks plus the
  now-dead TrailingNumber helper) with no proof assertion changed;
  focused run 29 passed, 0 failed (8 new plus 21 proof), so the proof
  now exercises the production fix.
- 8 new tests. Coordinator amendment commit 6573b31 updated the proof
  document to record both gaps as FIXED with commit refs, preserving
  the original finding text.

### WS5 - Player-facing source, performance, simulation-time review (branch `muse/pass11-ws5-player-facing-performance`, 775b526, 5 commits)

- F1 loan success on rejection FIXED: OwnershipWorkflowController
  loan mode discarded SubmitApplication's result and always played
  StampApproval, including on rejection and with no debt manager at
  all. Now branches on the result via new pure helper
  Presentation/UI/TransactionFeedbackDecision (Warning on rejection).
  Status text was already correct via RefreshLoan.
- F2 headline deadline absurdity FIXED: ScenarioObjectiveService
  BuildHeadlineSnapshot reported raw int.MaxValue (2147483647) on the
  live path for no-deadline goals (every First Ledger goal registers
  that sentinel); only the post-load fallback normalized to -1. Both
  paths now normalize; live-then-registration precedence preserved.
- F3 scenario failure invisible FIXED at source level:
  ScenarioFailed had zero production subscribers and the StartPanel
  rendered director goals only (no failure, no deadlines). Added
  additive headline fields FailedGoalId and FailedDeadlineDayIndex,
  Unity-free ScenarioObjectivePresentation (panel lines with deadline
  text whose overdue boundary exactly matches Evaluate: deadline day
  reads "Due today", never "Overdue"), and ScenarioFailureNoticeBoard
  (one notice per scenario plus goal even with two subscribers).
  Remaining Unity step, recorded as PASS11-006: wiring the board and
  presentation builder into the live panel (the panel reads director
  state only and holds no ScenarioObjectiveService reference), which
  is scene/reference work for a Unity session, not a safe blind edit.
- F4 cash/threshold success sounds on blocked actions FIXED (5 call
  sites, GeneralStore panel): deposit, withdrawal, and quick transfer
  played cash sounds on blocked transfers (quick transfer passes the
  signed amount unclamped, so it sounded the full requested amount);
  threshold-apply and auto-transfer toggle played UIConfirm on
  rejection. All now branch on the Try* bool. Verified-clean, no
  change: hiring handler (branches on TryAssign/TryFire plus message
  status), GeneralStore loan submit (branches on
  HasPendingApplication), threshold-adjust (branches on changed),
  sell-business and start-business handlers (branch plus authority
  message), HUD cash (single production caller pushes audited wealth
  from PlayerPortfolioManager, not a prediction), inspection read
  models (timestamped AsOfDay/minute; PersonCausalProfile bounded plus
  provenance-marked). Business reporting separations (financing vs
  operating, owner labor vs payroll) were already in place from WS3
  and Pass VIII and are preserved.
- UI refresh performance FIXED at source level (Unity verification
  pending): StartPanel 0.5 s refresh and HUD per-frame RefreshDate now
  skip TMP_Text assignment when text is unchanged. No displayed string
  changes; no FPS gain claimed.
- Performance (Roslyn harness only, best of 3, same workloads before
  and after): BusinessReportBuilder.CopyLedgerEntries was quadratic
  (full target scan per row); now indexes the target once (key-set,
  id-set, keyless list) with the exact AreSameLedgerEntry identity rule
  and unchanged row order. Weekly report, 1200 unique plus 600
  duplicate ledger rows: 105 ms to under 1 ms. Monthly rollup, 4 weeks
  times 600 ledger rows: 286 ms to 1 ms. Scenario evaluation (300
  goals), weekly staffing (200 rows), and due-event queue (5000
  schedule plus pop) were under 1 ms, 1 ms, and 6 ms and were left
  alone; the Pass IX heap was not redone. 5 equivalence tests pin kept
  rows, order, and dedup semantics; benchmark counts identical (1200
  weekly, 2400 monthly). No determinism, ordering, history, identity,
  obligation, or save-format change. Benchmark workloads live in
  Tests/Editor/Reporting/Pass11Ws5BenchmarkTests.cs (measurement only,
  no timing assertions). Not measurable in this harness and left alone
  with reasons in the performance report: world generation, terrain,
  road visuals, pathfinding allocation, campaign-scale save/restore
  (SaveLoadManager outside subset; SaveLoad-filtered run 72 passed, 0
  failed, 70.7 s), business-directory scan (town-scale, no repeatable
  harness workload), driver-scheduling cost (SimulationDrivers outside
  subset; note that the proposed 10x default doubles daily-drive
  frequency vs today's default, so a campaign proof should time one
  daily drive).
- Simulation-time review: VERDICT TECHNICALLY APPROPRIATE, with
  conditions, RESEARCH ONLY (no clock or driver code changed). Full
  detail in Docs/Pass11_Simulation_Time_Review.md. In brief: at base
  300 s/day the mapping is exact (Paused 0; 1x 300 s; 2x 150 s; 5x 60 s;
  10x 30 s; 25x 12 s; 50x 6 s); enum value 5 is unused, so Speed5x = 5
  is collision-free; conditions are settings assets serializing 60
  needing an asset update (a code-default change alone does not
  retune them; same for startingSpeed to 10x), 1:1 needing a
  TimeManager special case rather than a multiplier field (required
  multiplier 300/86400 breaks on retune; the fallback-int rule makes
  any RealTime int its degraded-mode multiplier; LoadFromSaveDto
  forces Paused when the multiplier reads at or below 0), AgentMover
  visual scale (clamp(multiplier, 0, 12) gives about 0.0035 at 1:1,
  near-frozen agents unless the visual scale is floored or decoupled;
  25x/50x already saturate at 12x, not a regression), HUD list changes
  (RealTime/5x buttons, 4x/6x/100x removal, highlight arms, day-length
  tooltips; old 100x saves still load and run, just unhighlighted),
  and 1:1 as an observation mode only (short ticks every 15 real
  minutes, daily drivers every 24 real hours, game-minute tasks,
  travel, freight, and construction taking real-time durations).
  Throughput: the proposed max (50x) is slower than today's 100x max,
  so no new worst case; caps (64 short ticks per frame, 7 rollovers
  per frame with per-day collapse, deferral-not-discard) hold. 11
  mandatory acceptance tests are listed in the review document.
- 22 new tests, all passing. Deliberately left unfixed, with reasons:
  StartPanel failure-board wiring (PASS11-006, G4); confirmation
  dialogs for irreversible actions (outcome reporting verified
  correct, bool-branched plus authority message; adding a confirm gate
  is a Unity UX design decision, not a data defect);
  business-directory linear scan, world, terrain, road, and
  pathfinding, campaign-scale save/restore (no repeatable harness
  workload; no speculative tuning per the brief).
- A-14 left unchanged (see Decisions).

### Coordinator integration commits (on muse/continuous-development)

- ce76318: WS3-requested one-line PASS9-003 distress-sale dispute-feed
  hook in SimulationDrivers.DriveNpcPropertyLifeDay, null-safe.
- 3af1863: Reporting DTO/snapshot scan-baseline classification (see
  WS3).
- 2101ea3: Saloon/ScenarioModels scan-baseline classification. The
  fix seam tests pulled three Saloon/ScenarioModels files into the
  canonical subset, surfacing 5 types; each has genuine production
  callers in harness-excluded scenario files (ScenarioGoal and
  ScenarioObjective used by ScenarioService, ScenarioAsset, and both
  scenario definitions; PopulationInclusionRules and
  PlayerHouseholdDeclaration by FirstLedgerScenario and ScenarioAsset,
  director for the declaration; SaloonCircuitGoalEvaluator by
  SaloonCircuitBootstrap), verified by grep, so feature-internal
  classification is subset-visibility accounting, not hiding an
  unwired type.
- 7c56fb0: ScenarioFailureNoticeBoard registered honestly as UNWIRED
  in Docs/UnwiredSystems.md (zero production callers in any file, so
  registry entry, not baseline classification), with note and
  changelog.
- 6573b31: settlement proof document amendment for the fixed save/load
  gaps (see WS4 follow-up).

### Harness configuration (local, NOT repo-tracked; must be reproduced for future harness setups)

The shared `harness/sources.txt` gained, across this pass: the six
ReadModels/Reporting files (WS3 OwnerReporting tests); the ten
Construction files (WS2); the three Saloon/ScenarioModels files (WS6
first-gate seam tests); the two Presentation/UI feedback files
(LLFeedbackKind.cs, pre-existing but not previously in the subset, and
TransactionFeedbackDecision.cs, new; WS5 feedback tests). The verbatim
IConstructionMaterialPricePort extract was removed from
`harness/Shims/WorldExtracts.cs` (it became a duplicate definition once
the real file entered the subset). The shared `harness.csproj` is
generated (never hand-edited); regeneration requires
`./gen-csproj.sh > harness.csproj` from the harness directory, then a
grep for new test file names, then a rebuild with 0 errors required
before any count is trusted. A failed build leaves the previous binary
in place; a stale csproj silently undercounts tests. Both traps bit
during this pass and are recorded in `~/AGENTS.md`.

## Decisions still open (nothing below was decided silently)

- A-14 late-latch semantics: ScenarioObjectiveService.Evaluate latches
  a goal whenever its predicate holds, even on first evaluation after
  its deadline. WS1 deliberately did not change it (lenient behaviour
  pinned by existing latch tests); WS5 re-examined it and also left it
  unchanged (flipping it is a Canon/design decision about evaluation
  cadence vs deadline strictness, not something source evidence alone
  settles). If it flips, the latch tests and the WS5
  presentation-boundary comment must be updated together.
- G1 (PASS9-008, RealDemandSource wire-or-retire): still needs
  Kennedy's decision. WS2 delivered a decision memo (four options;
  intent-executor recommended only if demand collection should affect
  play, otherwise leave unwired). The type stays registered unwired.
  Collection was NEVER wired to the live tick merely to claim it
  wired, and no competing purchase authority was created.
- G2 (PASS9-010 Class E): finite mineral reserve, severable mineral
  rights, water rules, court procedure detail, bank officer identity,
  childcare and fatigue designs. Still needs owner design approval.
  WS2's mine-closure plan is gated on the mineral reserve decision.
- G3: foreclosure-auction and household-lending designs (PASS8-012,
  PASS8-013). Still open; nothing invented.
- G4 (Unity session): scene inventory outstanding (one hub, one
  drivers object, one manager, one TimeManager, director, panel,
  registry assets, two bootstraps, CalendarAnchor match); bank scene
  binding; StartPanel failure-board wiring (PASS11-006); boarding
  RegisterHouse parcel mapping is a design-plus-data gap (PASS11-004),
  not purely scene work. No source work is blocked by G4.
- G5 (RUN-006 Test Runner reliability): blocks Unity test claims only,
  including the M1/M2 Unity-runner bootstrap tests (PASS11-008).
- ScenarioBootstrapFactory.TryCreateStart still has zero production
  callers (recorded, not wired).
- SimulationDrivers.Rebind has zero production callers (recorded, not
  described as production rebind).
- The 3 remaining test-side failures await a dedicated test-correction
  review (see above).
- M3 residual (PASS11-003): manual PayAgreedWage duplication risk is
  diagnostic-only; mechanical enforcement is a design decision.
- Legacy-slot payroll path (PASS11-007): still refuses a throwing
  lookup with no accrual, unlike the employment path.

## Gates status

- G1: open, blocks only WS2 demand work (done: research and memo only).
- G2: open, blocks only the decided subset of WS3 item 10 and the WS2
  mine-closure plan.
- G3: open, blocks only foreclosure-auction and household-lending
  items.
- G4: open, blocks WS1 scene inventory, WS2 bank scene binding, and
  all WS4 PlayMode beats, but no source work.
- G5: open, blocks Unity test claims only.
- Largest schedule risk remains the Unity session itself (scene
  access, Test Runner reliability, PlayMode stability). Source work
  proceeded while that session is arranged, and is complete at source
  level.

## Register and registry updates in this pass

- Master backlog (`Docs/Pass6_Deferred_Development_Master_Register.md`)
  updated in place with a Pass XI resolutions section and new items
  PASS11-001 to PASS11-008. All stable IDs preserved; nothing closed
  without implementation evidence. Closed in this pass: PASS9-001,
  PASS9-002, PASS9-003, PASS9-005, PASS9-006, PASS9-007 (with the M3
  accrual evolution), PASS11-001, PASS11-002. Partially advanced:
  RUN-002, RUN-003, PASS9-009 (reporting part). Still open, with
  evidence noted: RUN-001, RUN-004, RUN-005, RUN-006, RUN-007, RUN-008,
  RUN-009, RUN-010, RUN-011 (1 of 4 fixed), RUN-012, PASS9-004,
  PASS9-008, PASS9-010, PASS9-011, PASS9-012, PASS9-013, PASS9-014,
  IDENT-001 (interior migration remainder), PASS8-005, PASS8-006,
  PASS7-003, and the LL-PLAY set (LL-PLAY-005 has source-level
  progress from WS5; it remains open as a Unity item).
- Unwired registry (`Docs/UnwiredSystems.md`) updated in place:
  BankRuntime added as the thirteenth entry (WS1); ScenarioObjectiveService
  entry removed as wired (WS1); ScenarioFailureNoticeBoard added as
  honestly unwired (WS5); BankRuntime note updated for the WS2 binding
  contract; HouseholdMoveService note updated for the WS2 by-decision
  re-confirmation; RealDemandSource note updated for the WS2 decision
  memo. The twelve detailed business runtimes plus BankRuntime remain
  unwired; the construction driver and freight driver are live paths,
  not runtime entries, and must not be confused with them.

## What this pass did not do (explicit non-goals, kept out)

- No Unity compilation, Test Runner, or PlayMode claims. No scene,
  prefab, terrain, Package, ProjectSettings, or Canon document edits.
- No time-scale implementation (research and planning only).
- No merge to main. No force-push, no history rewriting. No GitHub
  publication claim (see publication note in the final report; remote
  continuous branch still at cc59bec at last check, bundles are the
  transfer path).
- No Codex involvement in any Pass XI work.
- No broad rewrite (including the 11290-line manager), no new
  industries, no UI redesign, no water or sanitation mechanics, no
  unapproved proposals 106 to 145, no silent wiring of any registered
  unwired system, no second scenario authority, no second freight
  system, no second debt ledger, no second stock ledger.
- No invented suppliers, buyers, senders, catalog assets, parcel
  mappings, or trigger rules where the contracts do not supply them.
  Each such gap is named with its missing input instead.
