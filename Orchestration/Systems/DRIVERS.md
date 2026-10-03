# Simulation Drivers (CLN-2)

Thin MonoBehaviour drivers Kennedy attaches in the one scene. They own no logic —
they subscribe to the TimeManager's tick events and forward to the authorities owned
by the `SimulationSystemsHub` (CLN-1).

## Scene setup

1. Add an empty GameObject named **"Simulation Systems Hub"**, attach
   `SimulationSystemsHub` (`LandLedgers.Orchestration.Systems`).
2. Add an empty GameObject named **"Simulation Drivers"**, attach
   `SimulationDrivers` (same namespace).
3. Leave all serialized fields empty — both components find what they need via
   `FindAnyObjectByType` / `TimeManager.Instance` on Awake. Fill fields only to
   override (e.g. a test double).

## What each driver does

| Tick event | Driver action | Authority call |
|---|---|---|
| Awake (once) | Install skill-based durations | `TaskAuthority.SetDurationEstimator(new SkillTaskDurationEstimator(hub.Skills))` |
| Awake (once) | Hand authorities to scenarios | `ScenarioDirector.TaskAuthority = hub.Tasks` |
| `DayChanged` | Roll daily work budgets | `WorkTimeBudgetStore.EnsureDay(absoluteDayIndex)` |
| `DayChanged` | Age perishable meat | `ButcherRuntime.AgeLotsToDay(dayIndex)` per hub runtime |
| `ShortTick` | Advance player travel | `PlayerDirector.RecordMovementProgress(wholeMinutes, hub.WorkTimeBudgets)` |
| `ShortTick` | Scenario goals (CLN-3) | `OnScenarioTick?.Invoke()` when a scenario is active |

## Call sites that are NOT ticks (documented, not driven)

- **`TaskAuthority.RecordWork(taskId, minutes, budgetStore, dayIndex)`** — call from
  every task-execution site when work is performed. On success, immediately call
  **`SkillService.ApplyPracticeFromTask(authority, taskId, minutesWorked)`** so XP
  accrues (TTS-3: 1 XP per minute practiced).
- **Freight/butcher `AdvanceGameSeconds`** — shipments advance inside
  `LogisticsRuntimeManager` (line ~770) and `FreightCompanyRuntime` (line ~191);
  both are already scene MonoBehaviours. No driver needed.
- **Scenario goal evaluation** — the bootstrap assigns `SimulationDrivers.OnScenarioTick`
  (CLN-3 wires `FirstLedgerGoalEvaluator.Evaluate` with the live game state).

## Save/load

The hub persists everything (CLN-1). Drivers hold no state — they re-subscribe on
enable and re-install the estimator on Awake, so a loaded game resumes cleanly.
