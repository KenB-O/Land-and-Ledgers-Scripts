# Pass XI WS5 — Performance Report (Roslyn harness only)

Branch: `muse/pass11-ws5-player-facing-performance`. No Unity compilation,
Test Runner, PlayMode, or FPS claims are made anywhere in this document:
every number below comes from the Roslyn harness on this branch, and the
Unity-only changes (HUD/StartPanel text guards) are reported as
source-level, Unity-verification-pending.

## Method

- Workloads live in `Tests/Editor/Reporting/Pass11Ws5BenchmarkTests.cs`.
  They are measurement tests, not timing assertions: each runs its
  workload three times, records the fastest wall-clock time, and only
  asserts the workload produced output, so a slow machine can never
  flake-fail the suite. Per-run lines are printed to stdout and appended
  to `/tmp/ws5-bench.txt` (ephemeral; the tests, not the log, are the
  durable artifact).
- Before/after runs used the same machine, the same isolated build
  shape (`/tmp/ws5-bin`, `/tmp/ws5-obj`), and the same workloads.
  "0 ms" below means sub-millisecond at `Stopwatch` millisecond
  resolution (best of 3), reported honestly as "<1 ms".

## Measured before / after

| Workload | Before | After | Action |
| --- | --- | --- | --- |
| Weekly report, 1200 unique + 600 duplicate ledger rows (`BuildWeekly`) | 105 ms | <1 ms | Fixed (indexed dedup) |
| Monthly rollup, 4 weeks × 600 ledger rows (`BuildMonthlyFromWeeklySummaries`) | 286 ms | 1 ms | Fixed (same fix) |
| Scenario evaluation, 300 goals, mixed latch state (`Evaluate`) | <1 ms | <1 ms (unchanged) | Left alone |
| Weekly staffing, 200 worker rows (`BuildWeekly`) | <1 ms | <1 ms (unchanged) | Left alone |
| Due-event queue, 5000 schedule + pop-all (`DueEventScheduler`) | 6 ms | 6 ms (unchanged) | Left alone (Pass IX heap) |

## The one fix: indexed ledger dedup

`BusinessReportBuilder.CopyLedgerEntries` scanned the entire target list
per source row (`ContainsLedgerEntry`), so copying a campaign-scale
ledger was quadratic: the monthly rollup re-scanned a growing list once
per week. The fix indexes the target once (a `HashSet` for
`ReportSummaryKey` rows, a `HashSet` for `EntryId` rows, a short list
for rows with neither) and keeps the index current as rows append, so
the copy is linear. The identity rule mirrors `AreSameLedgerEntry`
exactly — non-blank key wins, else non-blank id, else the full field
comparison — and keyed/id rows can never collide with keyless rows,
because `AreSameLedgerEntry` compares a key (resp. id) against blank
and finds them unequal. Row order is unchanged: rows append in source
order, duplicates drop in place.

Equivalence evidence: `Tests/Editor/Reporting/Pass11Ws5LedgerDedupTests.cs`
(5 tests) pins duplicate-by-id dropping with order preserved,
duplicate-by-key dropping even when other fields differ,
keyless-only full-field dedup, keyed/id non-collision, and
cross-week monthly dedup keeping the first row. The benchmark kept
row counts identical before and after (1200 weekly, 2400 monthly).
`AddLedgerEntry`'s own linear scan was left alone: it checks at most
the 8 generated summary rows, which is negligible next to the copy.

Determinism, ordering, history, identities, obligations, and save/load
compatibility are untouched: no row is added, removed, reordered, or
re-identified; the only change is how duplicates are detected.
`BusinessReportBuilder` has no persisted state of its own, so no save
format changes.

## Deliberately left alone (measured, not bottlenecks)

- Scenario evaluation over 300 goals, staffing over 200 rows: <1 ms.
  The `Contains` scans in `Evaluate` (latched list) and staffing are
  proportional to goal/worker counts, which are town-scale, not
  campaign-scale. No change.
- Due-event queue: 6 ms for 5000 events. Pass IX WS4 already replaced
  the sorted list with a binary heap; reworking it would redo Pass IX.
  No change.
- `AddReportGap`'s linear gap dedup: gap lists are tens of rows, not
  thousands. No change.

## UI refresh guards (source-level, Unity verification pending)

Two behaviour-preserving guards, not benchmarked in the harness
because they touch `TMP_Text` assignment, which the harness cannot
observe:

- `ScenarioStartPanelController.RenderObjectives` (runs every 0.5 s
  while a scenario is active) now skips the assignment when the
  rendered text is unchanged, avoiding a redundant TextMeshPro layout
  pass per refresh.
- `LandLedgersHUDController.RefreshDate` (runs on every visual frame
  via `OnVisualFrameUpdated`) now skips the assignment when the
  date/clock text is unchanged. The clock text only changes once a
  game minute, so this removes a per-frame rebuild in the common case.

Neither guard changes any displayed string; both need a Unity session
to confirm the rebuild saving, and no FPS gain is claimed here.

## Not measurable in the Roslyn harness (left for Unity / campaign proof)

The following workloads from the WS5 brief have no repeatable harness
form on this branch, so no numbers are claimed for them and no code
was changed on their behalf:

- World generation, terrain queries, road visuals, pathfinding
  allocation: Unity/scene systems outside the harness subset.
- Long-campaign memory and save/restore at campaign scale:
  `SaveLoadManager` is outside the harness subset. Proxy evidence:
  the full harness run and the SaveLoad-filtered run wall times are
  recorded in the WS5 final report; the WS4 campaign-scale proof owns
  save/load timing at campaign scale.
- Business directory searches (`FindBusiness` linear scan in
  `SharedBusinessRuntimeManager`): no repeatable harness workload
  exists without the full manager; directory size is town-scale
  (tens of businesses), so a linear scan there is not a
  campaign-scale bottleneck on current evidence. Left alone.
- Driver scheduling cost: `SimulationDrivers` is outside the harness
  subset. Note for the time-scale review: the proposed default
  (10x, 30 s/day) doubles daily-drive frequency versus today's
  default (1x, 60 s/day); WS4's campaign proof should time one daily
  drive before that default ships.

## Full-suite result

See the WS5 final report for the full-run counts. The benchmark and
dedup tests are included in that run; the only failures are the three
documented test-side defects left failing deliberately.
