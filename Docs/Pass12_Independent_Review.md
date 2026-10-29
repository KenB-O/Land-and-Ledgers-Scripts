# Pass XII — Independent Review (Worker F)

Date: 2026-10-28. Reviewer: Worker F (non-author of Workers A–E work).
Integrated tree reviewed: `eafe29b` (A 70c57ae, C 79c9341, B df7b078,
D a04ed1f, E eafe29b) plus Worker F fixes on `muse/pass12-f-review`.
Baseline: Pass XI final `02aca56`, 1404 passed / 3 failed.

## Method

Every worker claim relied on below was re-verified from source by the
reviewer (files read, callers grepped, test counts run), not accepted
from worker reports. Two delegated read-only investigations were also
spawned (RUN-011 corroboration; copy/registry corroboration; worker
claims plus defect sweep); their full reports had not arrived when
this review was written, so nothing in this document rests on them —
every verdict here is the reviewer's own. Harness runs used the
worktree csproj variant with isolated outputs and `LAND_LEDGERS_REPO`
pointing at the worktree, per the harness-from-worktree procedure.

## 1. Integrated-diff review verdict

The integrated diff vs `02aca56` is 80 files, +8300/−62, as claimed.
Full-harness baseline on the integrated tree before Worker F fixes:
1511 passed, 3 failed — exactly the expected arithmetic
(1404 + A28 + C33 + B4 + D22 + E20), and the 3 failures were exactly
the RUN-011 set. No confirmed P0/P1 defect in the integrated diff
survived unfixed except the Unity-only and owner-policy items
listed in §5, which are identified with their exact reasons and
are never called source-fixed. Worker F's OWN changes then caused
3 regressions, caught by the final full run (not by the focused
runs) and fixed before delivery — see F-05. This is why the final
count in Docs/Pass12_Final_Regression_Results.md is taken from a
full re-run after those fixes, not from the focused runs.

## 2. Worker claim verification

### Worker A — bootstrap, guards, ghost suppression, restore idempotence, copy-on-load

- Ghost suppression VERIFIED: `FirstLedgerSliceBootstrapper` now calls
  `storeRuntime?.SetCreatePlayerBusinessOnInitialize(false)`
  unconditionally at the top of `InitializeSlice` for every startup
  intent (previously only when a ScenarioDirector was present).
  Execution order confirmed by attribute: bootstrapper 240, store
  runtime 260.
- Restore activation VERIFIED: `MarkRestoredFromSave` now calls
  `SimulationDrivers.Rebind()` — the first production caller for
  `Rebind`. `TryCreateStart` still has zero production callers and
  must not be described as wired.
- Single-instance guard VERIFIED: `SingleInstanceAuthorityGuard`
  (Orchestration/Bootstrap) is pure C#, keyed by authority name and
  holder reference identity, with a test-only `ResetAll` seam nothing
  in production calls. `SimulationDrivers` claims in Awake,
  `AttachBusinessRuntime`, `Rebind` and `OnEnable`, and releases on
  disable/destroy. LIMITATION: only `SimulationDrivers` is
  guard-suppressed. Hub, Civic, ScenarioDirector and Bootstrapper
  duplicates are detected and logged by
  `ReportDuplicateSceneAuthorities` (which runs on Start and after
  every restore) but not suppressed — a duplicate Civic Foundation
  still operates twice in Unity until Codex removes the duplicate
  scene object (see LL-PLAY-003).
- Copy-on-load (A's six) VERIFIED: BankDeposits, FinancialObligation
  Authority, HouseholdLedger, BusinessLenderService, Employment
  Relationship, MoneyOrderService clone DTO objects on load. The
  obligation clone is sound (Terms is a string, PaymentTerms is
  deep-copied, string lists are AddRange'd into new lists).
- Harness caveat: `SimulationDrivers` and the bootstrapper are
  harness-excluded files, so the guard wiring itself is
  Unity-verified only; the guard class and audit class are
  harness-tested.

### Worker C — exactly-once wages, settled-sale refusal, reporting, staffing

- Exactly-once manual wages VERIFIED: `ManualWagePaymentRecord`
  (employment + day + normalized period label), persisted in
  `BusinessRuntimeSaveDto.manualWagePayments`; weekly netting of
  manual cash only (by PayrollDayIndex/7); `PayAgreedWage` refuses a
  manual re-pay while a preserved lookup-failure liability stands.
  BRITTLENESS NOTED: the preserved-liability lookup matches on the
  reason text string. It functions today (the legacy accrual text
  matches), but a future reason-text edit silently disables the
  guard. A stable liability kind would be safer; no change made
  (behavioral risk outweighs a cosmetic refactor in the cleanup
  wave). Mid-week manual cash now counts in LastWeeklyPayrollCents
  (covered by C's PartialPeriodCashPay test).
- PASS11-007 legacy accrual VERIFIED: the legacy loop now accrues on
  throwing lookup, mirroring the employment path; it never pays
  arrears, and the preserved-liability guard blocks manual re-pay.
- Settled-sale refusal VERIFIED (E-DEFECT-01): `ResolveSettledSale`
  returns false with no mutation when stock on hand is short.
  Both production callers (GeneralStoreRuntimeManager,
  SharedBusinessRuntimeManager) go through this method. The obsolete
  buyerless placeholder (`ResolveDailySalesPlaceholder`) has zero
  production callers; it is retained because two test files still
  exercise it as an observation path, and it posts no cash.
- Reporting/staffing VERIFIED at the read-model level: report
  aggregation (period rollups from monthly summaries, financing
  excluded from operating revenue, rent/tax bridge) and
  `WorkloadStaffingReadModel` (FTE scale, vacancy, unfilled shifts)
  are harness-tested; the staffing types are classified
  feature-internal in the assembly scan.

### Worker B — business completeness matrix

- Counts VERIFIED exactly from source: `BusinessType` has 28 values
  (GeneralStore 0 through Lawyer 27) plus `Generic = 255`;
  `AcquisitionMarketManager.ActivationBusinessTypes` has exactly 17
  entries (GeneralStore through Wheelwright); the creation panel
  offers `Generic` plus three templates. The LIVE/PARTIAL
  classification (18 LIVE / 10 PARTIAL / Lawyer unsupported) is
  accepted from B's matrix with the mechanism distinction
  spot-checked (weekly settlement list vs archetype resolution),
  not re-derived type-by-type.
- B re-verified all 13 registry runtimes still unwired and did NOT
  wire the staged chains — confirmed by the registry test passing
  with all rows intact. B's referral of Butcher/Blacksmith/Tannery
  is resolved in §4 below.

### Worker D — player-experience read-model contracts

- Contracts VERIFIED present and harness-tested:
  `ScenarioHudReadModel` + `ScenarioHudSnapshotBuilder`
  (`Pass12ScenarioPresentationTests`), `ScenarioGuidanceGate`
  (same file), `PanelModalCoordinator`, `StartupNoticeGate`,
  `SettlementCivicReadModel`, `ResourceObservationReadModel`,
  `PersonInspectionReadModel`, `AcquisitionDealReadModel`
  (`Pass12PlayerExperienceReadModelTests`).
- "Town Council" is NOT an invention by D: it is the pre-existing
  canonical steward display name in `TownHallDefinition` and
  `TownHallModels`. That nit was investigated and dropped.
- The live panel's Civic text change sits in
  `GeneralStorePanelController`, a harness-excluded file, so that
  half is Unity-verified only. The `ScenarioHudSnapshotBuilder`
  consumes a failure board but has no production caller; the live
  panel binding (PASS11-006) remains Codex scene work.
- D's contract document (`Docs/Pass12_PlayerExperience_Contracts.md`)
  was read and matches the code for the files checked.

### Worker E — settlement expansion tests

- Method count VERIFIED: 20 `[Test]` methods in
  `Pass12SettlementExpansionTests.cs`, including the preserved WS4
  proof, determinism twins, 90-day save/load at day 45,
  double-restore idempotence, closure payroll shortfall, guarded
  sale, credit default, empty bank liquidity, worker-absence
  throwing lookup, no-manager no-autonomous-spending, and
  construction interruption.
- The preserved WS4 proof asserts exactly the Pass XI numbers
  (2647500 + 4100 − 2275 = 2649325; grain 720, flour 270, bread 32,
  ore 0, in-transit 0) — spot-verified consistent with the Pass XI
  proof. The full sim arithmetic of E's new scenarios was NOT
  independently recomputed by this reviewer; the tests are
  self-checking (conservation assertions inside each sim) and all
  pass on the final tree. That scoping is stated, not hidden.

## 3. Defects found by Worker F, fixed and tested

### F-01 — Harness sources omission (process defect)

The canonical harness `sources.txt` was missing one Pass XII line:
`ReadModels/Person/PersonInspectionReadModel.cs`, referenced by
`Pass12PlayerExperienceReadModelTests` line 301. The first worktree
build failed with 1 error (CS0246) because of it. Fixed by appending
the line to the canonical `sources.txt` (now line 493) and to the
worktree variant. The coordinator's gate build must use the updated
file. Other Pass XII production files absent from the csproj
(GeneralStorePanelController, SaveDtos, FirstLedgerSliceBootstrapper,
FirstSessionGuidanceManager, ScenarioDirector, SimulationDrivers)
are pre-existing harness exclusions or non-subset files, not
regressions. The four harness-excluded files remain SimulationDrivers,
SharedBusinessRuntimeManager, SchoolhouseModels, SimulationSystemsHub.

### F-02 — Copy-on-load aliasing, nine further areas (P1 class)

Worker A's fix covered six load methods; the flagged remainder was
audited method by method. A hazard requires BOTH direct DTO storage
AND in-place mutation afterward (disk loads are safe because
deserialization mints fresh objects; only in-memory double restore
corrupts). Confirmed aliasing, fixed with clone-on-load, each with
a regression test in same-snapshot double-restore style
(`Pass12FCopyOnLoadTests`, 7/0):

| Area | Mutator proving the hazard | Fix |
|---|---|---|
| PropertyTaxService assessments/sales | AdvanceDay (status, penalties, interest), PayTax, sale settlement | Clone all TaxAssessment and TaxSale fields. Secondary fix: the Pass XI old-save fee migration now runs on the copy only, not the DTO. |
| HousingFoundation occupancies | New occupancy ends the current one by writing UntilDayIndex; EndOccupanciesForPerson | Clone all 8 fields; added null/blank-entry guard (load previously threw on null). |
| NpcCommissionedBuild records | Commissioning binds ContractId and appends Decisions on the found record | Deep copy incl. PackageFundings (with id lists), Overruns, Decisions. |
| CreditRegistry instruments (all five) | Status transitions to Called/Defaulted/Satisfied/Released | Clone all five types; added null guards for DTO lists and elements (load previously threw on null lists). |
| UsedEquipmentMarket listings | Status to Withdrawn/Sold | Clone incl. both diligence snapshots (OwnershipSnapshot via pre-existing Clone()). Remaining shared reference: Rules (configuration, no mutator found) — recorded, not a confirmed hazard. |
| PersonScheduleTracker reservations | Release sets Released = true | Clone all fields before indexing. |
| HouseholdErrandPlanRegistry plans | Assign/MarkCompleted/MarkFailed/Cancel set private status | New public HouseholdErrandPlan.Copy() — Assign could not serve (it forces status Assigned, corrupting Proposed/Completed/Failed/Cancelled plans on restore). |
| BoardingHouseReservations | ReleaseReservation sets IsActive = false | Clone all 11 fields. |
| HotelRoomReservations | Check-in writes ConsumedBeds; release sets IsActive = false | Clone all 13 fields. |

Reproduction proof: the tax clone was temporarily reverted to
direct-store via a file backup/restore (NOT git stash, which is
banned in worktrees) and the tax test FAILED on aliased code
(snapshot showed Delinquent), then the fix was restored (git diff
confirmed intact). Boarding/hotel clones are OUTSIDE the harness
subset (their production dirs were never included), so they have
NO harness test and are Unity-session verified only — stated
plainly, not claimed as harness-covered.

Proved copy-safe and LEFT stored-direct: civic treasury ledger
entries (append-only history, no in-place mutator found);
property dispute claims (append-only; RecordClaim creates new
objects); tenancy rent obligations (the payment loop creates a NEW
RentObligationState and replaces the dict entry); title records (no
in-place mutator found); housing buildings/spaces/assignments/
agreements (replaced, never edited); market sales and inspections
(completed append-only records); staged-production state
(registry-unwired, test-only, no production double-restore hazard);
WageSettlementService events (append-only history; settled-week
markers are re-keyed ints, not shared objects). "Task authorities"
resolved to PersonScheduleTracker, DomesticLaborDriver,
HouseholdErrandCoordinator and the staged-production machine (no
class named TaskAuthority exists); "market import" resolved to the
UsedEquipmentMarket book (fixed) with disputes delegating to the
append-only dispute load.

### F-03 — Registry gaps: three runtimes effectively unwired (process defect)

Verified from source with a Python mimic of the registry test's
exact logic (comments stripped, word-boundary type-name match,
excluding Tests/, the type's own file, and its owning directory):
- ButcherRuntime: production references are hub plumbing only
  (SimulationSystemsHub list/property/GetOrCreate/rehydration,
  parameter types in StoreButchery, FarmLivestock, CullFlow).
  GetOrCreateButcherRuntime has NO production caller, so the hub
  list is always empty; StoreButchery, FarmLivestockMarket.
  ExecuteSale and CullFlow.CullAndSellCow have ZERO production
  callers. No instance is ever created in production. Butcher
  itself is LIVE via the archetype path — only the detailed
  runtime class is unwired.
- BlacksmithRuntime: sole non-test production reference is a
  parameter type in WagonRepairEconomy.FindAvailableIronwork, and
  WagonRepairEconomy itself has zero production callers.
  Blacksmith itself is LIVE via the archetype path.
- TanneryRuntime: ZERO production references at all (only a comment
  mention, which the test strips). Tannery itself is PARTIAL per
  B's matrix.
Action (registry + test in the same commit, as required): three
rows added to `Docs/UnwiredSystems.md` with plumbing-only
allowlists (Butcher: SimulationSystemsHub, StoreButchery,
FarmLivestock, CullFlow; Blacksmith: WagonRepairEconomy; Tannery:
none), the "thirteen *Runtime* entries" note updated to sixteen,
and all three names added to CandidateTypes in
`UnwiredSystemsRegistryTests` (direction B now enforces them).
CAVEAT recorded in the registry: the allowlists cover plumbing
only, NOT wiring — a future real wiring must remove the
allowlisted file from the row, and reviewers must check
GetOrCreateButcherRuntime callers directly because drivers iterate
the ButcherRuntimes property (not the type name) and the type-name
test cannot see driver-side wiring. None of the three runtime files
is in the harness assembly, so this file-based registry test is
their only machine check.
D's 5 rows re-confirmed: PanelModalCoordinator,
SettlementCivicReadModel, ResourceObservationReadModel,
SettlementCausalExplanation, PersonInspectionReadModel —
RegisteredTypes_HaveNoProductionCallers passes with all rows,
proving none has a production caller. Assembly-scan baseline:
C's Pass XII additions are classified feature-internal; D's note
records ScenarioHudReadModel as accounted for by its caller
(ScenarioHudSnapshotBuilder, another directory) and
AcquisitionDealReadModel, StartupNoticeGate, ScenarioGuidanceGate,
ScenarioHudSnapshotBuilder as scan-exempt (abstract+sealed in IL).
No newly added production type was found missing from the
assembly-scan baseline beyond the F-01 harness-sources omission.

### F-04 — Default-ownership holes (M2 and GeneralStore flag)

- `BusinessInstanceState` serialized owner default Player → Town,
  with an explanatory comment. Enum order unchanged.
  Already-serialized scene instances keep their stored value, so
  Codex must still audit scene instances for a stored Player owner.
  New tests: `Pass12FDefaultOwnershipTests` (4/0); A ownership
  tests (8/0) still pass.
- `GeneralStoreRuntimeManager.createPlayerBusinessOnInitialize`
  default true → false, with a comment noting the bootstrapper
  suppression remains as belt-and-braces for scene instances
  serialized with the old default. The only non-bootstrapper
  behavior change: a standalone store runtime now returns "No
  player General Store has been formed yet. Use Businesses > Create
  Business." instead of auto-creating a player store; the
  bootstrapper path is unchanged. The file is harness-excluded, so
  this half is Unity-verified only.

### F-05 — Regressions caused by Worker F's own fixes, caught by the final full run

The first final full run (after F-01..F-04 and the RUN-011
corrections) gave 1522 passed, 3 failed — three NEW failures, all
traced to Worker F changes, none to Workers A–E:

1. `BusinessLenderServiceTests.ApprovedLoan_DisbursesRealCash_
   CreatesExactlyOneDebtRecord` and `Default_CallsGuaranty_And_
   EnforcesCollateral`: the test fixture builds its borrower with
   bare `new BusinessInstanceState()` and no owner, which
   previously defaulted to Player — so the lender created a Player
   guaranty. With the M2 Town default, no guaranty is created
   (OwnerGuarantorName returns empty for Town). The test's intent
   is explicitly a player-owned borrower ("owner is the player",
   Guarantor "Player"), so the FIXTURE was corrected to call
   `SetOwner(BusinessOwnerIdentity.Player())` with a comment
   stating the new contract: fixtures intending player ownership
   must set it explicitly. Production is unchanged; the M2 safety
   fix stands. Note: many other tests also use bare
   `new BusinessInstanceState()`, but the full run shows no other
   outcome changed — they do not depend on the Player default —
   and production paths always set owners explicitly. A bare
   constructed state now yields no owner guaranty for significant
   loans; that behavior is reachable only in test scaffolding,
   never in production.
2. `Pass11Ws3TaxFeeWaterfallTests.OldSave_UntrackedAssessment_
   ReconstructionPreservesSettledFees`: the test asserted the
   old-save fee migration ON THE DTO OBJECT ITSELF
   (FeesPaidTracked true, FeesPaidCents 200 after load). Worker
   F's copy-on-load change runs the migration on the live copy
   only, so the snapshot is untouched. Both production callers of
   the tax load (AcquisitionMarketManager, NpcPropertyLife) pass
   deserialized DTOs and never read them afterward, so snapshot
   mutation is unobservable in production — the test had encoded
   the old aliasing implementation, not a contract. The test was
   corrected to assert the reconstruction on the LIVE assessment
   via `GetAssessment("old-1")` (same values, same intent) AND to
   assert the snapshot is unchanged, which locks in the
   copy-on-load contract instead of weakening the test.
Focused verification after both corrections:
BusinessLenderServiceTests 9/0, Pass11Ws3TaxFeeWaterfallTests 5/0.
Lesson recorded for the coordinator: focused runs are not a
substitute for the final full run; the M2 flip's blast radius
included test fixtures, not just production paths.

## 4. RUN-011 test-correction review (brief §10) — production untouched in all three

Procedure per test: read the test, read the production
implementation, identify the authoritative governing behavior,
recompute the expected result independently, check siblings and
adjacent workflows, verify the correction conceals no defect.
All three verdicts are TEST-WRONG:

1. `MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree`:
   the test asserted IsNotNull for a second duplicate of an
   already-Duplicated original, while its own message states the
   governing behavior ("No duplicate of a non-live order").
   Production refuses unless Status is Issued ("duplicates replace
   live lost orders only"); a Duplicated original is no longer
   live, and a second live replacement would double the lost order.
   Siblings agree (RepayOrder/RedeemOrder refuse non-live orders;
   the replaced original can never be paid). Correction: IsNotNull
   → IsNull on that one assertion, with an explanatory comment.
   The negative coverage (second duplicate refused) is now
   actually asserted, not weakened.
2. `MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull`:
   recomputed from the claims — po-b paid 2500 + 1000 on po-a's
   orders (claims against po-a: 3500) and po-a paid 500 on po-b's
   order (claim against po-b: 500), so net is po-a owes po-b 3000.
   Production nets aOwes − bOwes exactly and settles in full. The
   test's own message already said "3500 − 500 netted bilaterally"
   (= 3000) while its literal expectation was 2500, as if the 500
   were subtracted twice. Correction: NetAmountCents 2500 → 3000,
   po-a fund 500 → 0, po-b fund 3500 → 4000. All other assertions
   unchanged.
3. `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`:
   levy at day 0 is due at day 60 (PropertyTaxCalibration.
   DueDaysAfterLevy = 60); ResolveWeek(100) advances through
   AdvanceDay, which flips Assessed → Delinquent past the due date
   (Canon §17.7 staged collection, cited in the service's own
   diagnostic). Siblings agree (Driver_AdvancesDelinquencyAndAccrues
   Penalties asserts Delinquent at dueDay + 45; SaveLoadRoundTrip
   asserts Delinquent at day 200). The once-per-tax-year property
   under test is the count == 1, which is unchanged. Correction:
   status Assessed → Delinquent at day 100, plus a pinned assertion
   DueDayIndex == 60 explaining the mechanism. Production's
   delinquency transition is correct and untouched.
Focused verification: MoneyOrderServiceTests 20/0,
P5PropertyLegalJourneyTests 15/0. No test deleted, no assertion
weakened. (Note: the task brief's parenthetical attributing the
"3500 − 500" message to production was a misattribution — it is
the test's own AreEqual message.)

## 5. Residuals — explicitly carried, not silently closed

- F-RES-01 (P2, read-model only): WageSettlementService replay
  Paid inaccuracy. The replay in SettleWeeklyPayroll computes
  `paid = attended && wage > 0 && replayCash >= wage` using the
  full agreed wage, while the single-writer path pays wageDue after
  netting manual cash (C's change). After a manual partial
  payment, a replay entry can show Paid=true with the full wage
  while only the remainder actually moved. TotalPaidCents IS
  corrected to observed cash movement and mismatches are logged
  loudly, but per-entry Paid/AmountCents remain wrong. NOT fixed
  here: a correct fix must net manual records by the same week key
  the runtime used (service weekIndex vs runtime PayrollDayIndex/7
  plus period-label normalization), and a divergent replay would
  be worse than the current loudly-logged inaccuracy. Fix sketch
  for a future pass: in the replay loop, subtract the manual cash
  recorded for the same employment-week before the
  replayCash >= wageDue test, and assert entry AmountCents ==
  remainder; test: manual partial pay, then service settlement,
  assert entry Paid reflects remainder coverage.
- F-RES-02 (open limitation): direct double-run of
  BusinessRuntimeState.ResolveWeeklyPayroll for the same week is
  unguarded; only WageSettlementService checks its SettledWeekIndex
  map. A stamp-derived guard inside ResolveWeeklyPayroll was
  deliberately NOT added: it misfires on legitimate service
  settlements whose week index advances while the stamp does not
  (proven: service settles week 57 at day 406, 406/7 = 58).
  Production direct callers each run once per weekly event
  (SharedBusinessRuntimeManager.ResolvePayrollForType;
  GeneralStore OnWeekChanged with a subscribed-flag guard), and
  LastPayrollSettledWeekIndex is NOT persisted in
  BusinessRuntimeSaveDto (the hub re-stamps it from
  WageSettlementService SettledWeeks on restore). Owner/design
  decision: whether mechanical enforcement belongs in the runtime
  or stays with the service.
- F-RES-03 (latent, test-only): MineLaborRegister.SettleShiftWages
  credits any IsActive assignment without death/off-map/attendance
  checks, but it has NO production caller (only
  MineLaborRegisterTests). If a future driver calls it, it must
  pass an eligibility-checked assignment set. Documented, not
  fixed against no production caller.
- F-RES-04 (not investigated in this review): the COGS/reorder
  producer contract (which production component owns cost-of-goods
  and reorder decisions for archetype businesses). Carried open;
  no claim is made about it.
- F-RES-05 (owner decisions, do not invent): bank capacity
  consolidation and related-party policy (what caps a bank's
  lending, and which related-party transactions are legitimate).
- F-RES-06 (Unity-only, exact reasons): duplicate Civic Foundation
  suppression (only drivers are guard-suppressed; scene-object
  removal is Codex work); GeneralStore flag and M2 scene-instance
  stored values (already-serialized values survive default flips);
  boarding/hotel copy-on-load clones (files outside the harness
  subset); M1/M2 Unity-runner bootstrap tests (never run in a real
  Unity Test Runner session, PASS11-008); the RUN-004 Main Scene
  30-day PlayMode proof (not attempted in source); LL-PLAY-006
  layout, LL-PLAY-016 terrain, LL-PLAY-019 audio lifecycle (no
  Pass XII source work; see the reconciliation doc).
- F-RES-07 (owner-policy-blocked, exact reasons): RealDemandSource
  wire/replace/retire (gate G1 — see Deferred doc; wiring it into
  the tick merely to claim it active would risk double-buying);
  Class E policy questions (gate G2 — no water/hydration rules,
  mine reserve policy, government powers, historical legal rules,
  population thresholds or stabilizers invented); foreclosure
  auction and household lending designs (gate G3 — no law,
  eligibility or settlement procedure invented); A-14 late-latch
  semantics (unchanged, owner decision required to flip);
  simulation-time change (proposal only, not implemented).
- Other carried items (unchanged by Pass XII): PASS11-004 (3 gaps),
  PASS11-005 (no legitimate buyer source), PASS11-006 (panel wiring),
  PASS9-004 (postage honestly unset), PASS9-008 (G1), PASS9-010
  (G2), PASS9-011 (archetype outputs beyond mine/farm), PASS9-012
  (scenario catalog + scene remainder), PASS9-013 (campaign-scale
  save/restore profiling), PASS9-014 (Unity compilation of the 4
  harness-excluded files), IDENT-001 interior migration remainder,
  PASS8-005 (16 runtimes now registered), PASS8-006, PASS7-003.
  ScenarioBootstrapFactory.TryCreateStart still has zero
  production callers.

## 6. Cleanup performed

No genuinely obsolete helper, incorrect adapter or dev-only
scaffolding was removed in this review: none was identified that
could be removed without breaking a test or a serialized contract.
Specifically NOT removed: the obsolete buyerless sales placeholder
(still exercised by two test files as an observation path, posts
no cash, zero production callers); the preserved-liability
reason-text lookup (functional, see §2); the UsedEquipmentMarket
Rules shared reference (configuration, no mutator). No cosmetic
mass renaming was performed. No file unrelated to an acceptance
gate was touched. `Docs/UnwiredSystems.md` was updated to the final
tree (three entries added, none removed — no new real production
caller was verified for any registered entry).

## 7. What this review did NOT do

- No Unity compilation, Test Runner, PlayMode, scene or prefab
  verification is claimed anywhere in this document.
- No Canon document was modified.
- RealDemandSource was not wired into the tick; the simulation-time
  change was not implemented.
- Nothing was merged to muse/continuous-development, pushed, or
  bundled by Worker F.
