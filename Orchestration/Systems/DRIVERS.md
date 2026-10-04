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
4. P6 travel & communication bootstrap (all scene work, none of it invented by the hub):
   - Author the world layout: register `JourneyLocation`s and road `JourneyEdge`s on
     `hub.Journeys` (e.g. via `WorldLayoutBuilder.Build`), plus `RouteConditionService.SetEdgeAttributes`
     for fords/ferries/dirt roads so NX-2C weather can close them.
   - Assign `hub.WeatherSeed` from the world's canonical seed (deterministic weather).
   - Register post offices via `hub.Postal.RegisterOffice` (office id, journey
     location id, business instance id, departure weekdays, seed).
   - Move intents: call `PlayerDirector.TryIssueMoveIntentViaJourney(hub.Journeys, ...)`
     from the map UI — no production caller issues move intents yet, so the player
     cannot travel without it.

## What each driver does

| Tick event | Driver action | Authority call |
|---|---|---|
| Awake (once) | Install skill-based durations | `TaskAuthority.SetDurationEstimator(new SkillTaskDurationEstimator(hub.Skills))` |
| Awake (once) | Register all static task catalogs | `SimulationSystemsHub.RegisterTaskCatalogs(hub.Tasks, hub.Skills, hub.Postal, diag)` — P1: no production caller ever registered any catalog, so the first `CreateTask` threw "Unknown TaskDefinitionId" |
| Awake (once) | Hand authorities to scenarios | `ScenarioDirector.TaskAuthority = hub.Tasks` |
| Awake (once) | Hook weekly profit posts | `SharedBusinessRuntimeManager.PreWeeklyResetCallback = PostWeeklyProfitToValuation` |
| Awake (once) | Wire employment authority | `SharedBusinessRuntimeManager.EmploymentRegistry = hub.Employments` — P1: payroll now pays through `EmploymentRelationship` records (agreed wages) instead of the legacy slot-template path |
| `DayChanged` | Roll daily work budgets | `WorkTimeBudgetStore.EnsureDay(absoluteDayIndex)` |
| `DayChanged` | Age perishable meat | `ButcherRuntime.AgeLotsToDay(dayIndex)` per hub runtime |
| `DayChanged` | Accumulate owner work minutes | Player's `MinutesWorked` → weekly accumulator (CLN-4) |
| `DayChanged` | Advance weather/route conditions | `hub.RouteConditions.AdvanceDay(dayIndex, hub.WeatherSeed, diag)` — P6: NX-2C closures actually close roads through the journey model's condition provider (blizzard → warning logged) |
| `DayChanged` | Dispatch/arrive mail | `hub.Postal.AdvanceDay(hub.Journeys, dayIndex, diag)` — P6: NX-2A letters actually move posted → in-transit → arrived on office schedules |
| `DayChanged` | Advance money-order books | `hub.MoneyOrders.AdvanceDay(dayIndex, diag)` — P6: advices arrive, stale orders expire (D4E) |
| `DayChanged` | Audit registered mail | `hub.RegisteredMail.AuditOverdue(dayIndex, diag)` — P6: overdue items audited (D4F) |
| `DayChanged` | Advance recruitment | `hub.Recruiting.AdvanceDay(population, hub.Employments, dayIndex, diag, hub.Postal)` when a population exists — P6: T2B inquiries accrue from real persons; postal-routed letters resolve only on real arrival |
| `DayChanged` | Execute NPC daily needs | `DailyNeedsService.ExecuteDay(population, planner, executor, dayIndex, diag, hub.WorkTimeBudgets)` (NX-2C drive-by — needs PopulationManager in scene + a non-empty journey model: `SimulationDrivers.Journeys`, falling back to the hub-owned `hub.Journeys`) |
| `WeekChanged` | Sync liabilities to valuation | `hub.Liabilities.SyncAllToValuation(null)` — P1: the SWN-3 liability ledger now feeds the BIZ-5 read model weekly |
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
- **Liabilities**: the SWN-3 `BusinessLiabilityLedger` (hub-owned, save-persisted)
  feeds the read model via `hub.Liabilities.SyncAllToValuation(null)` every
  `WeekChanged` (P1) — real balances, re-synced weekly, never synthesized.

## Call sites that are NOT ticks (documented, not driven)

- **`TaskAuthority.RecordWork(taskId, minutes, budgetStore, dayIndex)`** — call from
  every task-execution site when work is performed. On success, immediately call
  **`SkillService.ApplyPracticeFromTask(authority, taskId, minutesWorked)`** so XP
  accrues (TTS-3: 1 XP per minute practiced).
- **Freight/butcher `AdvanceGameSeconds`** — shipments advance inside
  `LogisticsRuntimeManager` (the live scene MonoBehaviour path for local-trade
  transfers). `FreightCompanyRuntime` is NOT a scene MonoBehaviour — no live
  instance exists yet, so its `AdvanceGameSeconds` is test-only. P3 added the
  code bridge `DeliveryService.DispatchViaFreightCompany` (build → accept →
  dispatch → depart), which gives `TryDispatchShipment` a real caller path;
  the remaining gap is live scene wiring: a delivery-job source, a
  freight-company instance, and real draft-animal/driver lists.
- **Scenario goal evaluation** — the bootstrap assigns `SimulationDrivers.OnScenarioTick`
  (CLN-3 wires `FirstLedgerGoalEvaluator.Evaluate` with the live game state).

## Save/load

The hub persists everything (CLN-1). Drivers hold no state — they re-subscribe on
enable and re-install the estimator on Awake, so a loaded game resumes cleanly.
