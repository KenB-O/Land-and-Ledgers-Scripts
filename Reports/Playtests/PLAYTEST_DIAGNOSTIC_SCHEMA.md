# Playtest Diagnostic and Snapshot Schema

## Files

- `metadata.json`: run identity, configuration, expected/actual startup counts, day interval, speed, result, console counters, gate status, source branch/SHA, and tracked working-tree state. `trackedWorkingTreeState` is `CLEAN`, `DIRTY`, or `NOT_CAPTURED`; the legacy boolean and count fields remain for compatibility, and a count of `-1` means unknown. Untracked working-tree files are deliberately not enumerated because the project includes owner-local asset packs.
- `console-full.txt`: ordered human-readable raw Unity log entries, including stack traces.
- `console-events.jsonl`: one JSON object per message: `sequence`, `wallUtc`, `severity`, `simulationContext`, `message`, `stackTrace`.
- `daily-summary.csv`: one row per authoritative day boundary. Meals come from `SimulationDrivers.LastDailyNeedsReport`; task status totals come from `TaskAuthority.CaptureDailyLifecycleSnapshot`. Counts and balances use `-1` when unavailable; associated text fields explain `NOT_CAPTURED` seams. Cancellation has no day stamp in the task model, so only retained cancelled-task count is reported, not a fabricated daily value.
- `entity-snapshots.jsonl`: household, household-lot, business and business-lot snapshots from existing ledgers/registries. This is observation only and creates no inventory or cash authority.
- `final-state.json`: completion classification, completed day count, final invariant and explicit unavailable data seams.
- `playtest-report.md`: concise run result and artifact pointers.

## Semantics

Amounts ending in `Cents` are integer cents from the owning ledger/runtime. Inventory quantities are existing lot quantities, not visual meshes. A missing registry/API is `NOT_CAPTURED`; it must never be encoded as a legitimate zero. Message severity follows Unity `LogType`; exceptions and asserts count as errors for summaries while preserving their original severity in JSONL. Sequence numbers define event order; UTC is wall time, while `simulationContext` is updated on the main thread at startup and day boundaries and is therefore a safe approximate context for threaded logs.

## Coverage and known observation gaps

The current runner directly snapshots resident/household/business counts, household ledgers and lots, business runtime cash and retail lots, daily meals, active shopping trips, and task lifecycle counts. It preserves all Unity logs. Physical route outcomes, separate production inputs/outputs, freight custody/settlement, forestry, construction stages, property title and occupancy, employment, wages, sleeping/dining reservations, equipment ownership and persistence comparison remain subsystem-owned observations and are not falsely synthesized into aggregate values. Extend the runner only through public authoritative read seams or correlated source events.

## Startup result vocabulary

- `CALENDAR_COMPLETE_REQUIRES_REVIEW`: the requested authoritative calendar interval elapsed. This is not an economic-health result.
- `PASS` (legacy runs only): do not interpret as proof of economic health; earlier runner versions used it for calendar completion.
- `INVALID`: startup preflight/runtime gate failed, or runtime/evidence continuity was lost. A run may have advanced days before evidence became invalid.
- `INCOMPLETE`: run ended before the target day count.
- `FAILED`: target interval completed but a recorded invariant/diagnostic failed.
- `NOT RUN`: no evidence of execution.

## Playtest integrity

The selected positive target duration is written to `metadata.json` and controls the stop day. `final-state.json` lists any missing day offsets. A missing daily snapshot or a scripting-domain reload after the run enters `RUNNING` marks the result `INVALID`, even if the authoritative calendar reaches its target. Historical run directories and their original classifications are retained unchanged.
