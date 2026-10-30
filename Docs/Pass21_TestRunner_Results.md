# Pass XXI — Unity Test Runner Results

Date: 2026-10-30  
Unity: 6000.4.2f1  
Source tree: local `integration/pass12-unity-readiness` worktree based on `bc29a1ebafe2bb5eff6bbbf1f157639a1be6ae2d` plus uncommitted Pass XXI modifications.

## Current GUI runs

The existing graphical Unity Editor compiled/reloaded the changed source and completed the following Unity Test Runner runs:

| Run | Total | Passed | Failed | Skipped | Result artifact |
|---|---:|---:|---:|---:|---|
| EditMode full assembly | 4,226 | 4,027 | 199 | 0 | Current result was visible in Test Runner. Export did not create a fresh XML. |
| PlayMode full available assembly | 10 | 10 | 0 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-current-final-20261030.xml` |

PlayMode XML SHA-256: `E8A5E13F4C8130BA957E9DB6499273BE8779B5CCBCC7089B7CF276A004372CDC`.

The current EditMode GUI result showed `Land & Ledgers (4226 tests), 199 tests failed`, with 4,027 passed and zero skipped. The available XML named `pass21-editmode-full-current-20261030.xml` predates the latest fixture correction and reports 4,030 passed / 196 failed. It is retained for comparison only and is not claimed as the current tree's XML. The earlier `pass21-editmode-final-20261030.xml` reports 4,029/197. Thus the full suite is still red and the exact current failure identities remain to be exported and classified.

## Current PlayMode result

The full available PlayMode assembly finished with 10/10 passing and zero failures. It includes `CampaignSaveRoundTripPreservesProductionBusinessIdentity` and `FirstLedgerFourWeekProductionReportsHonestProgress`. The 10-test run took several minutes; the Test Runner completed and returned to idle. This is automated PlayMode evidence, not an observed hands-on owner session or 30-day economic proof.

The same run was exported to the reports folder using Unity Test Runner. The independent XML parse reports `total=10`, `passed=10`, `failed=0`, `skipped=0`.

## Limits

- CLI/Pipeline access to the already-open Editor was unavailable; tests were run through the GUI Test Runner, not a second Unity process.
- The full EditMode XML export was unavailable for the latest run. The UI count is recorded accurately, and an earlier XML is kept distinct.
- No full suite failure was suppressed or removed to obtain these counts.
- These totals do not validate Sandbox/Continue ownership matrix, complete Person day/night behavior, rent, animal feeding, crop Tasks/freight, the First Vein scenario, or RUN-004.

