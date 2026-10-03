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
| Awake (once) | Hook weekly profit posts | `SharedBusinessRuntimeManager.PreWeeklyResetCallback = PostWeeklyProfitToValuation` |
| `DayChanged` | Roll daily work budgets | `WorkTimeBudgetStore.EnsureDay(absoluteDayIndex)` |
| `DayChanged` | Age perishable meat | `ButcherRuntime.AgeLotsToDay(dayIndex)` per hub runtime |
| `DayChanged` | Accumulate owner work minutes | Player's `MinutesWorked` → weekly accumulator (CLN-4) |
| `WeekChanged` | Post owner labor | `Valuation.RecordOwnerLabor` per player-owned business (CLN-4) |
| `ShortTick` | Advance player travel | `PlayerDirector.RecordMovementProgress(wholeMinutes, hub.WorkTimeBudgets)` |
| `ShortTick` | Scenario goals (CLN-3) | `OnScenarioTick?.Invoke()` when a scenario is active |

## Valuation events (CLN-4)

Weekly profit, owner labor, and (when it exists) liability data feed the BIZ-5
valuation read model from real settlement paths — event-fed, never per-frame:

- **Weekly profit**: `SharedBusinessRuntimeManager.PreWeeklyResetCallback` fires for
  each business before its weekly sales reset; the driver posts
  `RuntimeState.WeekToDateNetCents` via `Valuation.RecordWeeklyProfit`.
- **Owner labor**: the driver accumulates the player's daily worked minutes (from
  the real work-time budgets) and posts weekly hours per player-owned business via
  `Valuation.RecordOwnerLabor`. Replacement cost and draw default to 0 until the
  compensation paths provide them.
- **Liabilities**: businesses carry no balance-sheet liability ledger yet, so there
  is no settlement path to wire. `Valuation.RecordLiabilities` stays available for
  when one exists — nothing is synthesized in the meantime.

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
