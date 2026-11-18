# Economic Stability Recovery — 2026-11-18

## Status

**Source progress is ready for review; integrated PlayMode verification is blocked by the local Unity build environment.** No gameplay success is claimed for the current repair set. Unity remains in Edit Mode and the owner's Main Scene was not saved.

## Baseline evidence reviewed

The latest completed integrated run is `20261118T160808Z-fc53cfb5`: Unity 6000.4.2f1, Main Scene, `millbrook-development-town`, seed 1886, 250x, 60/60 calendar transitions. It recorded 178 meals eaten and 1,519 missed. The earlier owner-supplied 66-day run recorded 150 eaten and 1,715 missed; these are different run intervals and should not be treated as a controlled before/after pair.

In the 60-day run, the first day resolved 28 meals with no misses. On day 2, physical shopping trips failed: some trips found no bread at the counter; others could not start because the shopper was already occupied. Day 3 resolved 11 meals and 20 misses. By day 32, successful meals fell to zero; the final 29 days each reported zero meals and 28 misses. Food purchase reconciliation remained zero throughout. Households' cash alone did not provide food.

### Ranked root causes

1. **HIGH — domestic physical tasks retained person ownership indefinitely.** Tasks T47/P9, T48/P13 and T49/P14 (`domestic.meal-prep`) remained `InProgress` from day 1, each requiring 61 minutes but reaching 0/61 worked minutes and never acquiring a due event. By day 3, T47 had already accrued 1,890 active minutes with zero work. Their occupation blocked other obligations: sawmill job starts repeatedly refused because P13 was occupied by T48; carpentry starts refused because P14 was occupied by T49; household shopping attempts for those people were rejected by `TaskAuthority`. This is an integration/lifecycle deadlock, not evidence that those residents legitimately had no time for the whole run.

2. **HIGH — food buying could not recover the larders.** The run showed empty household larders and no settled food charges. Day 2 included genuine bread stockouts at the counter. Other attempts aborted before travel because the selected person was already occupied; two trips balked after a 30-minute service wait. The logs do not prove that every failed trip had an affordable, physically available substitute item, so both stock/offer mapping and task ownership remain relevant.

3. **MEDIUM — horse-feed reorder quantity exceeded store capacity.** After the freight business bought 7 sacks, the store had 13 sacks against its 20-sack target. The reorder placed another 20-sack order instead of the 7-sack deficit. Repeated arrivals were refused with `Category 'animal_feed' capacity would be exceeded`; the supplier lot was restored, so these refusals did not create retail stock or settle a receipt. Separately, the horse's 7-sack purchase was physically delivered and paid for; later procurement was blocked when operator P7 was occupied. This is not a zero-supply initialization failure.

4. **MEDIUM — freight negotiation repeated without dispatch in this run.** No completed shipment was recorded. The opening flour shipment remained undelivered here. Meetings and 125-cent quotes continued to be offered late in the run, but no departure/receipt settlement followed. A separate focused PlayMode test previously completed the Millbrook shipment and reused the wagon, so the evidence points to a recurring scheduling/dispatch condition rather than a universally broken freight authority.

5. **ECONOMIC LIMIT, not yet a defect — bakery fuel was finite.** The run recorded 40 baked batches, and the final period retained a planned 24-loaf batch without proving further production. Fuel exhaustion after the finite opening supply is expected absent replenishment. Flour was locally purchased from the General Store early in the run; the opening off-map flour shipment did not complete in this run.

The sawmill also had real logs delivered, but its only open job could not start because the assigned worker remained occupied by T48. No lumber production completed. The generic business closure at week 56 occurred after ongoing recorded customer-seeking costs and owner cost-cutting; the lumberyard remained open through this 60-day run. The later week-63 lumberyard closure belongs to the separate 66-day evidence. The available logs do not establish that either closure was caused by a scheduling defect.

## Repairs prepared

- `DomesticLaborDriver.ReconcileStalled` now requeues a started domestic task only when it has no live in-flight owner, releases that task's schedule reservation, and credits no work. A live physical flight is preserved.
- `PopulationPathingDirector` now tracks domestic physical route ownership and progress across route legs and applies bounded no-progress and total-route deadlines using the same simulation-clock sample as the driver. A timed-out route is cancelled and reported as unsupported so the domestic driver can interrupt/requeue the task without granting work or goods.
- `StoreHorseFeedReorder` now orders the missing quantity up to its existing 20-sack target rather than another full 20-sack lot. The new regression covers 13 sacks on hand and a 7-sack paid order.

These changes preserve TaskAuthority, physical movement, real supplier ordering, inventory receipt, and payment. They have **not** been verified in a post-fix PlayMode run.

## Test and Unity results

- `PopulationPathingProfessionRhythmTests`: **52 passed, 0 failed** in the Unity Test Runner for the route ownership, clock, progress, and deadline coverage.
- `DomesticLaborDriverTests`: **17 passed, 0 failed** on the orphaned-task reconciliation change before the route-clock edit. A subsequent request for this suite timed out; no newer result is claimed.
- The new partial feed-reorder regression is added but **not run**.
- A Unity compilation attempt did not complete cleanly. `Editor.log` records `bee_backend` failing to create the IL post-process process with Windows error **1455**, “The paging file is too small for this operation to complete,” followed by “Internal build system error.” This is a Windows/Unity build-process failure, not a C# diagnostic identifying a source line. The Editor status later showed `compiling=false` but `compilationFailed=true`.
- The 60-Day Runner attempt `20261118T164247Z-63bb4526` was safely marked **ABORTED / INVALID**, with **0/60** days and no passed startup gate. It stayed armed in Edit Mode. No New Game economy state was reset and no day was advanced by that runner.
- The six errors from the 66-day report were associated with Unity testing/pipeline activity. They are not classified as gameplay defects here.

Two non-destructive verification approaches were attempted: the focused Unity EditMode suite (which returned 52/52) and the runner/recompile path for integrated verification. The latter exposed the Windows 1455 ILPP process-creation failure. Repeating the same build while the Editor and ILPP worker were still active would add contention without evidence of recovery, so no duplicate run was launched.

## Run comparison and artifacts

The completed pre-fix run remains at:

`Reports/Playtests/Runs/20261118T160808Z-fc53cfb5/`

It reports 178 meals eaten / 1,519 missed, 3,226 TaskSchedule warnings, 5,879 total warnings, 1 error, and 0 exceptions. The six-day and horse-feed logs show task blocking and repeated category-capacity refusal as described above. Bakery activity produced 40 actual batches; the final 20-day period did not establish fresh saleable output.

The blocked post-change attempt is at:

`Reports/Playtests/Runs/20261118T164247Z-63bb4526/`

Its `console-events.jsonl` and `entity-snapshots.jsonl` are empty because Play Mode never began. Its complete Runner artifacts and a compact evidence ZIP are retained locally. The full original Unity log remains unmodified at `%LOCALAPPDATA%\Unity\Editor\Editor.log`; the ZIP includes a focused excerpt and identifies the full log by path, size, and SHA256.

Shareable blocked-run package: `Reports/Playtests/Runs/20261118T164247Z-63bb4526/console-and-simulation-evidence.zip` (6,704 bytes; SHA256 `5CBC53F3DD006F06318CC410BD68900589279824232055F9415A28E23610B5EB`). The original full Editor log snapshot was 445,041,712 bytes, SHA256 `FDE77D3747A0FB54429D949FB71A8E3B21FB5CEDC7E37C1B1B7A82FF4E60C5ED`; it remains in the local Unity log location and is not copied into the package.

The baseline 60-day run's existing complete evidence ZIP is `Reports/Playtests/Runs/20261118T160808Z-fc53cfb5/console-and-simulation-evidence.zip`.

## Remaining work and blocker

1. Restore sufficient Windows commit/page-file headroom or otherwise resolve Unity's error-1455 ILPP process creation. Do not kill the active Editor or import workers to force a build.
2. Compile the exact changed source and run the focused domestic-labor, pathing, and feed-reorder EditMode suites.
3. Run a short actual Main Scene PlayMode observation to verify domestic-route timeout/requeue, household food receipts/consumption, and feed receipt at the store's available capacity.
4. Only after those pass, run one 60-day Runner test and compare actual household meals, purchases, freight, feed, bakery, sawmill, and business lifecycle evidence to the baseline.

No Main Scene, prefab, terrain, font, or local asset-pack file is included in this source checkpoint.
