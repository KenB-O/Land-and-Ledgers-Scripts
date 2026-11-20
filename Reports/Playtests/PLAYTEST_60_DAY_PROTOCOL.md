# Integrated Economy Playtest Protocol

## Purpose and acceptance rule

This protocol records a controlled run of a user-selected number of complete calendar-day transitions through the existing `TimeManager` and production drivers. Presets are 5, 10, 15, 30, 60 and 90 days, with a positive custom duration. A wall-clock timer is never a substitute for elapsed simulation days. The runner must abort before day 1 when its startup gate fails; generated scenery alone is not a valid town.

## Modes

| Mode | Use | Procedure |
|---|---|---|
| A — Startup smoke | Validate initialization and first daily drive | Open the intended scene, verify the New Game bootstrap and deterministic scenario, then observe the initial runtime gate and first day. |
| B — Physical activity | Observe movement, service, equipment and freight | Use Normal or a modest speed. Record each task, route, arrival, interaction, custody and settlement from authoritative logs/state. Do not infer arrival from a task assignment. |
| C — Integrated playtest | Economy stability and recurring activity | Use the Editor runner or manual procedure below. Require the selected number of authoritative day changes and a valid pre-day-1 gate. |

## Preflight and startup gate

1. Record the current Git branch/full SHA, Unity version, active scene, scenario/configuration ID, startup mode and deterministic seed source/value.
2. Confirm the owner's scene is not being edited concurrently. Do not save or reload it to prepare a run without authorization.
3. Confirm `DevelopmentTownConfiguration.Validate` succeeds and the active bootstrapper points at the intended configuration.
4. Start New Game through the real bootstrap. Before accepting day 1, verify bootstrap initialized without failure; fixed configured resident, household and business counts match exactly; configured businesses have valid owner/cash identities; required Pass 9 holders and hubs exist; `SimulationDrivers` references active authorities; time settings report 86400 game seconds/day; and town/navigation are initialized. Excess residents, households or businesses fail the gate rather than passing as a minimum-count match.
5. Any failed or timed-out check makes the run **INVALID**. Do not continue an empty or partially initialized economy to the requested duration.

## Execution

The Editor menu is `Land & Ledgers > Diagnostics > Playtest` (`Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs`). It captures a deterministic configured seed, arms a run, enters Play Mode, waits for the runtime startup gate, chooses the selected existing simulation speed, and completes only after the requested number of `TimeManager.DayChanged` transitions. Start/Stop are explicit. Stop, runtime exception, startup failure, missing clock, backward day index or early Play Mode exit records an aborted/incomplete result. The runner does not implement another clock, generate resources, bypass daily drivers or save the scene.

Use Normal or slower observation for physical activity. A faster preset is appropriate for long-run progression only after a separate physical test. Keep the configured 1440 real seconds per simulated day at 1x; do not change production time to satisfy stale test expectations. Record any speed schedule and the exact multipliers in the run report.

## Evidence and reconciliation

Each run directory should contain `metadata.json`, `console-full.txt`, `console-events.jsonl`, `daily-summary.csv`, `entity-snapshots.jsonl`, `final-state.json`, and `playtest-report.md`. JSONL records preserve sequence, UTC timestamp, severity, simulation date context, raw message and stack. The Unity threaded log callback only enqueues immutable strings; the Editor update thread writes incrementally. Raw entries are not filtered. Missing access to a requested authoritative value must be written as `NOT_CAPTURED`, never as zero. The current runner captures daily meal-resolution counts and task state totals; cancellation is a retained-history count because the current task model does not date cancellations. Physical route outcomes, freight custody/settlement, production, construction and wages remain uncaptured until their owning authorities expose reliable read-only snapshot seams.

At each day boundary compare household/business cash and lot states with transaction, production and custody events. For any claimed transfer, require matching source debit, destination credit, identity/quantity, and exactly-once result. Capture meal, task, movement, freight, construction and property details from their owning systems and preserve their source evidence; aggregate fields with no public observation seam remain explicitly `NOT_CAPTURED`.

## Required run metadata

Run ID; UTC start/end; branch and full SHA; tracked working-tree dirty state; Unity version; scene/scenario/startup mode; world seed; start/end game dates and absolute day indices; speed/multiplier schedule; approved day length; expected/actual people, households and businesses; completed/aborted day count; Console counts; startup gate and final invariants; result (`CALENDAR_COMPLETE_REQUIRES_REVIEW`, `INVALID`, `INCOMPLETE`, or `FAILED`). Untracked files are not enumerated by the runner because this workspace contains large owner-local asset packs.

## Simulation-speed correctness

The approved simulation day remains 86,400 simulation seconds. `TimeManager` queues elapsed game time and drains ordered short-tick boundaries; callbacks must use `SimulationTickContext` or the clock's dispatch-time properties rather than frame-end time. Do not add a parallel clock. Compare a deterministic short scenario at 1x, 10x and 250x, recording tick/day boundaries, task starts and completions, before treating 250x as a reliable test speed. The runner's completed calendar interval is not an economic-health assessment.

## Completion and handoff

A run is calendar-complete only after its configured number of day transitions and a passing startup gate. Missing daily snapshots or an unexpected scripting-domain reload invalidate the run; economic health always requires separate review of sustained activity, transactions and invariants. Retain the run directory, calculate SHA-256 for raw console files before handoff, add its exact path/checksum/retrieval instructions to the run index, and do not commit large logs unless repository policy permits. Leave Unity in Edit Mode after the run. See `PLAYTEST_DIAGNOSTIC_SCHEMA.md` for field semantics and `PLAYTEST_RUN_INDEX.md` for known runs.
