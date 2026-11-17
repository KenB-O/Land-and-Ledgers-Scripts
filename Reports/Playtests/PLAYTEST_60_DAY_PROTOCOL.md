# 60-Day Integrated Economy Playtest Protocol

## Purpose and acceptance rule

This protocol records a controlled run of **60 complete calendar-day transitions** through the existing `TimeManager` and production drivers. A wall-clock timer is never a substitute for elapsed simulation days. The runner must abort before day 1 when its startup gate fails; generated scenery alone is not a valid town.

## Modes

| Mode | Use | Procedure |
|---|---|---|
| A — Startup smoke | Validate initialization and first daily drive | Open the intended scene, verify the New Game bootstrap and deterministic scenario, then observe the initial runtime gate and first day. |
| B — Physical activity | Observe movement, service, equipment and freight | Use Normal or a modest speed. Record each task, route, arrival, interaction, custody and settlement from authoritative logs/state. Do not infer arrival from a task assignment. |
| C — Integrated 60-day | Economy stability and recurring activity | Use the Editor runner or manual procedure below. Require 60 authoritative day changes and a valid pre-day-1 gate. |

## Preflight and startup gate

1. Record the current Git branch/full SHA, Unity version, active scene, scenario/configuration ID, startup mode and deterministic seed source/value.
2. Confirm the owner's scene is not being edited concurrently. Do not save or reload it to prepare a run without authorization.
3. Confirm `DevelopmentTownConfiguration.Validate` succeeds and the active bootstrapper points at the intended configuration.
4. Start New Game through the real bootstrap. Before accepting day 1, verify bootstrap initialized without failure; expected population and households exist; configured businesses are registered with valid owner/cash identities; required Pass 9 holders and hubs exist; `SimulationDrivers` references active authorities; time settings report 86400 game seconds/day; and town/navigation are initialized.
5. Any failed or timed-out check makes the run **INVALID**. Do not continue an empty or partially initialized economy to day 60.

## Execution

The Editor menu is `Land & Ledgers > Diagnostics > 60-Day Playtest Runner` (`Assets/Scripts/Editor/Playtests/Playtest60DayRunnerWindow.cs`). It captures a deterministic configured seed, arms a run, enters Play Mode, waits for the runtime startup gate, chooses the selected existing simulation speed, and completes only after the requested number of `TimeManager.DayChanged` transitions. Start/Stop are explicit. Stop, runtime exception, startup failure, missing clock, backward day index or early Play Mode exit records an aborted/incomplete result. The runner does not implement another clock, generate resources, bypass daily drivers or save the scene.

Use Normal or slower observation for physical activity. A faster preset is appropriate for long-run progression only after a separate physical test. Keep the configured 1440 real seconds per simulated day at 1x; do not change production time to satisfy stale test expectations. Record any speed schedule and the exact multipliers in the run report.

## Evidence and reconciliation

Each run directory should contain `metadata.json`, `console-full.txt`, `console-events.jsonl`, `daily-summary.csv`, `entity-snapshots.jsonl`, `final-state.json`, and `playtest-report.md`. JSONL records preserve sequence, UTC timestamp, severity, simulation date context, raw message and stack. The Unity threaded log callback only enqueues immutable strings; the Editor update thread writes incrementally. Raw entries are not filtered. Missing access to a requested authoritative value must be written as `NOT_CAPTURED`, never as zero.

At each day boundary compare household/business cash and lot states with transaction, production and custody events. For any claimed transfer, require matching source debit, destination credit, identity/quantity, and exactly-once result. Capture meal, task, movement, freight, construction and property details from their owning systems and preserve their source evidence; aggregate fields with no public observation seam remain explicitly `NOT_CAPTURED`.

## Required run metadata

Run ID; UTC start/end; branch and full SHA; Unity version; scene/scenario/startup mode; world seed; start/end game dates and absolute day indices; speed/multiplier schedule; approved day length; expected/actual people, households and businesses; completed/aborted day count; Console counts; startup gate and final invariants; result (`PASS`, `INVALID`, `INCOMPLETE`, or `FAILED`).

## Completion and handoff

Only 60 completed day transitions with a passing startup gate qualify as a completed standard run. Retain the run directory, calculate SHA-256 for raw console files before handoff, add its exact path/checksum/retrieval instructions to the run index, and do not commit large logs unless repository policy permits. Leave Unity in Edit Mode after the run. See `PLAYTEST_DIAGNOSTIC_SCHEMA.md` for field semantics and `PLAYTEST_RUN_INDEX.md` for known runs.
