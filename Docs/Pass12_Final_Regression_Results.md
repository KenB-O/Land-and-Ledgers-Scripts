# Pass XII — Final Regression Results (Worker F)

Date: 2026-10-28. Tree verified: `muse/pass12-f-review` at
`518b4a8` (Worker F final code tree). The full run below was
executed on that exact tree; the only commit after it is the
commit adding this document's final update (Docs-only, no code
or test change), so the counts stand for the final tree.
(An earlier full run on `ed95de6` also gave 1525/0; the
`518b4a8` run re-confirms it after two test-only coverage
additions — Orders.Count == 2 in the duplicate test and an
Assessed-stage assertion after ResolveWeek(0) — which add
assertions inside existing tests, not new tests, so the total
stays 1525.)

## Procedure (reproducible)

- Worktree: `~/workspace/land-and-ledgers/worktrees/wt-pass12-f`,
  branch `muse/pass12-f-review`.
- Harness csproj variant generated from the harness generator with
  REPO pointed at the worktree (`/tmp/f.csproj`, via `/tmp/gen-f.sh`),
  verified to include the new test files
  (`Pass12FDefaultOwnershipTests`, `Pass12FCopyOnLoadTests`) and
  `ReadModels/Person/PersonInspectionReadModel.cs`.
- Build: `dotnet build /tmp/f.csproj -o /tmp/pass12f-bin
  -p:BaseIntermediateOutputPath=/tmp/pass12f-obj/` — **0 errors**.
- Run: `LAND_LEDGERS_REPO=<worktree> dotnet
  /tmp/pass12f-bin/LandLedgers.Harness.dll [filter]`.
- Canonical harness `sources.txt` was fixed during this review
  (F-01): the `PersonInspectionReadModel.cs` line was appended
  (now line 493). Without it the build fails with 1 error (CS0246).
  Note: the variant generator also appends that line, so the
  variant csproj lists the file twice; MSBuild tolerates the
  duplicate and the build still reports 0 errors. The coordinator's
  gate build should use the updated canonical file.
- Harness-excluded production files (unchanged): SimulationDrivers,
  SharedBusinessRuntimeManager, SchoolhouseModels,
  SimulationSystemsHub. Nothing in this document claims Unity
  compilation, Test Runner, or PlayMode results for them.

## Full run — 1525 passed, 0 failed (exit 0)

`Roslyn harness: 1525 passed, 0 failed`, full-run output
`/tmp/pass12f-final2.txt` (retained on the VM, not committed).

Arithmetic, verified at each step (not assumed):
- Pass XI final: 1404 passed / 3 failed (1407 tests).
- Integrated Pass XII tree (Workers A–E): 1511 passed / 3 failed
  (1514 tests) = 1404 + A28 + C33 + B4 + D22 + E20, with the 3
  failures exactly the RUN-011 set. Verified by Worker F's own
  baseline run before any Worker F fix.
- Worker F added 11 tests: 4 default-ownership + 7 copy-on-load =
  1525 tests total.
- RUN-011 corrections converted the 3 historical failures to
  passes (production untouched). First final full run after those
  corrections: 1522 passed / 3 failed — the 3 failures were NEW
  regressions from Worker F's own M2 and copy-on-load changes
  (F-05), caught by the full run, not by focused runs. After the
  F-05 test corrections: **1525 passed / 0 failed**.
- No test was deleted. No assertion was weakened; two test
  corrections strengthened their assertions (tax test now also
  asserts the snapshot is untouched; RUN-011 second-duplicate
  assertion now asserts refusal instead of asserting a duplicate
  exists).

## Filter table (all on the final tree, 0 failures throughout)

| Filter / suite | Result | Note |
|---|---|---|
| (full run, no filter) | 1525 / 0 | Exit 0. |
| MoneyOrderServiceTests | 20 / 0 | Includes both corrected RUN-011 tests. |
| P5PropertyLegalJourneyTests | 15 / 0 | Includes the corrected assessment test. |
| Pass12FDefaultOwnershipTests | 4 / 0 | M2 + flag tests (Worker F). |
| Pass12BusinessOwnershipTests | 8 / 0 | Worker A ownership tests, still green after M2 flip. |
| Pass12FCopyOnLoadTests | 7 / 0 | Same-snapshot double-restore tests. |
| UnwiredSystemsRegistryTests | 3 / 0 | Includes the 3 new runtime rows. |
| Pass9RegistryAssemblyScanTests | 2 / 0 | Assembly-scan baseline intact. |
| BusinessLenderServiceTests | 9 / 0 | After F-05 fixture correction. |
| Pass11Ws3TaxFeeWaterfallTests | 5 / 0 | After F-05 live-state correction. |
| NinetyDay (90-day suite) | 6 / 0 | Matches Pass XI 6/0: 4 Pass7NinetyDayScenarioTests + RedeemOrder_NinetyDayValidity + NinetyDayRun_WithDemographicEvents. (A narrower "Pass7NinetyDay" filter gives 4/0 — same 4 class tests.) |
| Pass8AdverseCirculation (Ruston Decline) | 8 / 0 | Matches Pass XI 8/0. Bare "Ruston" matches nothing. |
| TickSize | 6 / 0 | Matches Pass XI 6/0. |
| ThreeYear | 5 / 0 | Matches Pass XI 5/0. |
| SaveLoad | 85 / 0 | Pass XI was 81/0; growth is Pass XII persistence additions (manual-wage records, copy-on-load coverage). |

## What was NOT verified in this run (stated, not hidden)

- The four harness-excluded files have no Roslyn harness coverage.
  Worker F did not run a full-repo Roslyn build on the final tree,
  so Pass XII edits to those files (notably Worker A's
  SimulationDrivers guard wiring) are Unity-verified only and
  remain Unity-compilation-pending (PASS9-014). Pass XI's
  full-repo build (zero errors in those files) does not cover
  Pass XII edits to them.
- BoardingHouse and HotelRoom copy-on-load clones have no harness
  test (their production dirs are outside the harness subset);
  they are Unity-session verified only.
- M1/M2 Unity-runner bootstrap tests were not executed anywhere
  (PASS11-008); the RUN-004 Main Scene 30-day PlayMode proof was
  not attempted in source.
- The earlier full-run output `/tmp/pass12f-final.txt` (1522/3,
  pre-F-05-fix) and the baseline output `/tmp/pass12f-full.txt`
  (1511/3, integrated tree) are superseded by
  `/tmp/pass12f-final2.txt` (1525/0, final tree). The Pass XI
  baseline caveat about `sources.txt` no longer applies: the
  canonical file now includes all Pass XII lines Worker F found
  missing (one line, F-01).

## Regression verdict

No unexplained new regressions. Every failure observed during
Worker F's verification is accounted for: the 3 integrated-tree
failures were the documented RUN-011 test defects (corrected,
production untouched); the 3 first-final-run failures were Worker
F's own regressions (F-05, corrected, production untouched).
The final tree passes its entire Roslyn harness with zero
failures, and every legacy simulation suite re-run matches or
exceeds its Pass XI count.
