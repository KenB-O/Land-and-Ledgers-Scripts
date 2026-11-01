# Pass XX — Unity Test Runner Results

Date: 2026-10-29  
Editor: Unity 6000.4.2f1, graphical Test Runner  
Source at test start: `407fccc6c41a348d2794b026949099f92cd54812` plus the uncommitted Pass XX working-tree changes.

## Completed fresh test runs

| Platform | Total | Passed | Failed | Skipped | Evidence |
|---|---:|---:|---:|---:|---|
| EditMode full suite, current tree | 4,225 | 4,011 | 214 | 0 | `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-editmode-final-20261029.xml`; `pass20-editmode-final-failures.csv` |
| PlayMode full available suite, final-source rerun | 10 | 9 | 1 | 0 | `F:\Land & Ledgers_Pass20_Backups\pass20-reports\pass20-playmode-final-20261029.xml` |

The final current-tree XML records all three Pass XX regressions as passed: identityless General Store placeholder rejection, January-first birthday distribution, and leap-year date/age preservation. The initial 4,224-test XML is retained as an earlier checkpoint. Failure indexes are in the report directory.

## Current-tree EditMode run

The full run completed all 4,225 tests: 4,011 passed, 214 failed and 0 were skipped. Unity paused for several individual tests and displayed busy callbacks, but the run continued and completed. No process was forcibly terminated.

The earlier `PercentOfAvailableAgreementRespectsSourceReserve` case completed and retained its failure (expected 7, got 0). The prolonged callback was not a confirmed deadlock or environment failure.

## PlayMode failure

`LandLedgers.PlayMode.PlayableScenarioStartupPlayModeTests.ProductionWeeklyPassRegistersFarmFieldAndRespectsWinter` failed because the authored opening world did not contain a real CropFarm Business. No synthetic farm was added. The other nine PlayMode tests passed, including First Ledger honest four-week progress, normal scenario startup, Business identity save round-trip and owned horse/wagon authority checks. This final PlayMode artifact was exported after the current source compiled and the full EditMode run completed.

## Reproduced historical failures

The first full EditMode run reproduced the 12 failures recorded in Pass XIX: five General Store and seven orchestration failures. These remain unresolved pending test-contract and production-authority analysis; expected values were not weakened.

The full suite also contains failures across estate administration, arbitrage, acquisition, transfer, construction, farming/inventory, population and other suites. Names, messages and stack traces appear in `pass20-editmode-final-failures.csv`. The run identifies failing tests but does not prove every one is a gameplay defect or a new regression.

## Validation boundary

The graphical Unity Editor compiled and entered Play Mode during this pass. Full EditMode execution completed but remains red. The final-source PlayMode run completed with one failure. No RUN-004 thirty-day Main Scene reconciliation or interrupted continuation was completed.