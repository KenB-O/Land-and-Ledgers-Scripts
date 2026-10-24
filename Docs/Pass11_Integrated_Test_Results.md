# Pass XI Integrated Test Results

All results below are Roslyn harness results only. NOTHING here is a
Unity compilation, Test Runner, or PlayMode result, and none may be
read as one. Every full run used a properly regenerated csproj
(`./gen-csproj.sh > harness.csproj` from the harness directory, new
test file names grep-verified in the generated csproj), a rebuild
reporting 0 errors, and isolated build outputs. A failed build leaves
the previous binary in place and a stale csproj silently undercounts
tests; both traps occurred during this pass and were corrected before
any count below was trusted.

## Full harness progression

| Checkpoint | Passed | Failed | Change |
|---|---|---|---|
| Pass IX baseline (cc59bec) | 1285 | 4 | Historical RUN-011 set |
| Audit merged (986644e) | 1285 | 4 | Documents only |
| WS1 merged (e5d23b6) | 1297 | 4 | +12 WS1 tests |
| WS3 merged + coordinator integration (3af1863) | 1327 | 3 | +26 WS3 canonical tests, +3 OwnerReporting tests now in subset, +1 historical failure fixed in production (indorsement) |
| WS6 first-gate fixes + WS2 merged + Saloon scan classification (2101ea3) | 1353 | 3 | +8 fix tests, +18 WS2 tests |
| WS4 + WS5 merged + NoticeBoard registry (7c56fb0) | 1396 | 3 | +21 WS4 proof tests, +22 WS5 tests |
| WS4 save/load fixes merged + proof doc amended (6573b31) | 1404 | 3 | +8 save/load fix tests |

Total new Pass XI tests: 118. Arithmetic: 1285 plus 118 new plus 1
historical failure fixed in production equals 1404 passed, 3 failed.
No test was deleted. No assertion was weakened. No new failure was
reclassified as historical. The documents and register commits after
6573b31 (implementation report, handoff, register and registry
updates, this document) change no .cs file, so the 1404/3 count stands
for that code tree; the WS6 final-gate review re-runs the full harness
on the final HEAD and its verdict governs the final report.

## The 3 remaining failures (documented test-side defects, left failing deliberately)

- `MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree` (line
  202): asserts IsNotNull with the message "No duplicate of a non-live
  order" while production correctly returns null for a non-live order.
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

These three were verified as test-side by the coordinator from test
text and by the WS6 first-gate reviewer against production code. The
fourth historical failure (indorsement presentment) was a genuine
production bug and was fixed in WS3, narrowed by WS6 first-gate fix M5.
Recommended next step, not taken in this pass: a dedicated
test-correction review that fixes exactly these three tests.

## Legacy suite re-runs (filters on the 6573b31 binary)

| Suite | Passed | Failed | Note |
|---|---|---|---|
| 90-day (filter NinetyDay) | 6 | 0 | Matches Pass IX 6/0 |
| Ruston Decline (filter Pass8AdverseCirculation) | 8 | 0 | Matches Pass VIII 8/0. The filter "Ruston" matches no test name; the Ruston Decline tests live in Pass8AdverseCirculationTests, so that class filter is the correct one. |
| Tick-size (filter TickSize) | 6 | 0 | Matches Pass IX 6/0 |
| Three-year (filter ThreeYear) | 5 | 0 | Matches Pass IX 5/0 |
| SaveLoad (filter SaveLoad) | 81 | 0 | Growth vs Pass IX 69/0, WS3 71/0, WS5 72/0 is the new save/load coverage: WS4 proof save/restore tests, the 8 WS4-follow-up fix tests, and WS5 save/load-adjacent tests. No SaveLoad test fails. |

## New Pass XI suites (focused runs, all on their workstream branches and confirmed in the merged full runs)

- WS1 scenario start: 12 new tests, all passing (BeginScenario plus
  daily Evaluate driving, qualifier persistence and rebuild, unrebuilt
  reporting, old-save definition adoption, headline deadline fallback,
  latched merging without un-failing, three-store reconciliation, tick
  gate plus driver continuation). Focused runs also passed for the
  unwired registry tests and the assembly scan test (with
  LAND_LEDGERS_REPO pointed at the worktree, per existing test
  convention).
- WS3 integrity: 29 new tests (26 canonical plus 3 OwnerReporting in
  the extended build at the time; all 29 run in the canonical subset
  after the Reporting files entered it). Focused: WS3 safeguards 3/3
  re-confirmed by the WS6 first-gate fix worker.
- WS6 first-gate fixes: 8 new tests (M3 plus M5 tests 4/4, seam tests
  4/4, WS1 scenario-start 9/9 re-confirmed). The M1/M2 Unity-runner
  bootstrap tests (Tests/Editor/Scenarios/
  Pass11Ws6FirstGateBootstrapTests.cs) were NOT executed anywhere in
  this pass: there is no Unity runner in the source environment, and
  the bootstrap files cannot compile in the Roslyn harness. They await
  a real Unity Test Runner pass (PASS11-008, gate G5).
- WS2 economic circulation: 18 new tests, all passing (9 construction
  driver, 3 bank binding, 3 freight handler, 3 household move).
- WS4 settlement proof: 21 proof tests, all passing, plus 8
  save/load fix tests, all passing. Focused run after proof
  compensation removal: 29 passed, 0 failed. Full proof figures
  (cash, goods, debts, adverse outcomes, save/restore exactly-once
  midpoints, determinism) are in
  Docs/Pass11_Production_Settlement_Proof.md.
- WS5 player-facing and performance: 22 new tests, all passing (8
  scenario/presentation, 4 feedback-decision, 5 ledger-dedup
  equivalence, 5 benchmark measurement workloads with no timing
  assertions). SaveLoad-filtered run at WS5 time: 72 passed, 0 failed,
  70.7 s. Performance before/after (Roslyn harness, best of 3, same
  workloads): weekly report 105 ms to under 1 ms, monthly rollup 286
  ms to 1 ms, benchmark counts identical (1200 weekly, 2400 monthly).
  No Unity FPS gain is claimed from these numbers.

## Coverage areas exercised by the full run (not separate suites)

The full harness run covers, by test class, the brief's regression
areas: economic conservation, inventory provenance, freight delivery
and rejection, mine production, business lifecycle, payroll, banking,
public treasury, housing and tenancy, construction, scenario progress,
person lifecycle, determinism, save/load, and New Game. No area was
removed from the run to obtain the counts above; the only tests
excluded from the canonical subset are those whose production files
sit outside it (notably the harness-excluded Unity files and their
Unity-runner tests), which is a stated limitation, not a selection.

## Known harness limitations (read before citing any count)

- The harness subset is not the whole repo. Files outside it
  (SimulationDrivers, SharedBusinessRuntimeManager,
  SimulationSystemsHub, SchoolhouseModels, scene-bound panels and
  bootstraps, among others) are not compiled by the harness. Their
  Pass XI edits were checked with a full-repo Roslyn variant build in
  the audit style (zero errors in the edited files; remaining errors
  are Unity package using gaps on untouched lines), which narrows
  PASS9-014 but does not close it.
- The assembly scan test reads LAND_LEDGERS_REPO and defaults to the
  base repo checkout; it only passes when that variable points at the
  tree under test. An unset variable misreports worktree-wired types
  as unwired. This bit the WS2 and WS6-fix workers and is recorded
  here so the next worker does not chase it as a code defect.
- The shared harness sources.txt is local configuration, not
  repo-tracked. Reproducing the 1404/3 count requires its Pass XI
  additions: six ReadModels/Reporting files, ten Construction files,
  three Saloon/ScenarioModels files, and two Presentation/UI feedback
  files, plus the removal of the verbatim IConstructionMaterialPricePort
  extract from harness/Shims/WorldExtracts.cs. Without them, the scan
  test and the feedback/seam tests fail for configuration reasons.
- Timing numbers in this document (benchmark milliseconds, suite wall
  clocks) are Roslyn harness measurements on this VM, not Unity
  performance data. They vary with load; the controlled before/after
  comparisons are the benchmark table above, not the full-run wall
  clocks.
