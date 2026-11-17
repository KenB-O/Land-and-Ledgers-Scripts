# Playtest Diagnostic and Snapshot Schema

## Files

- `metadata.json`: run identity, configuration, expected/actual startup counts, day interval, speed, result, console counters and gate status.
- `console-full.txt`: ordered human-readable raw Unity log entries, including stack traces.
- `console-events.jsonl`: one JSON object per message: `sequence`, `wallUtc`, `severity`, `simulationContext`, `message`, `stackTrace`.
- `daily-summary.csv`: one row per authoritative day boundary. Counts and balances use `-1` when unavailable; associated text fields explain `NOT_CAPTURED` seams.
- `entity-snapshots.jsonl`: household, household-lot, business and business-lot snapshots from existing ledgers/registries. This is observation only and creates no inventory or cash authority.
- `final-state.json`: completion classification, completed day count, final invariant and explicit unavailable data seams.
- `playtest-report.md`: concise run result and artifact pointers.

## Semantics

Amounts ending in `Cents` are integer cents from the owning ledger/runtime. Inventory quantities are existing lot quantities, not visual meshes. A missing registry/API is `NOT_CAPTURED`; it must never be encoded as a legitimate zero. Message severity follows Unity `LogType`; exceptions and asserts count as errors for summaries while preserving their original severity in JSONL. Sequence numbers define event order; UTC is wall time, while `simulationContext` is updated on the main thread at startup and day boundaries and is therefore a safe approximate context for threaded logs.

## Coverage and known observation gaps

The current runner directly snapshots resident/household/business counts, household ledgers and lots, business runtime cash and retail lots. It preserves all Unity logs. Meals are correlated from `[FoodEconomy]` events. Task completions, physical route outcomes, separate production inputs/outputs, freight custody/settlement, forestry, construction stages, property title and occupancy, employment, wages, sleeping/dining reservations, equipment ownership and persistence comparison remain subsystem-owned observations and are not falsely synthesized into aggregate values. Extend the runner only through public authoritative read seams or correlated source events.

## Startup result vocabulary

- `PASS`: all configured startup authorities and expected entities passed before day 1.
- `INVALID`: startup preflight/runtime gate failed; no simulation days accepted.
- `INCOMPLETE`: run ended before the target day count.
- `FAILED`: target interval completed but a recorded invariant/diagnostic failed.
- `NOT RUN`: no evidence of execution.
