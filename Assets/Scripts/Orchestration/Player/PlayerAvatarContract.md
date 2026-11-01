# Player Avatar Contract — what you build in Unity (TTS-4)

The player is a Person in the player household (HF-3 `DeclarePlayerHousehold`) who
performs tasks through the SAME task system as NPCs. Scripts own the rules; your
prefab owns the body. This doc is the exact split.

## Scripts provide (already written, `Orchestration/Player/`)

- `IPlayerAvatar` — the interface your prefab's script implements.
- `PlayerDirector` — script-side authority. It issues movement/task intents, books
  the player's minutes against the shared TTS-1 work-time budget (same as NPCs),
  assigns/starts/interrupts tasks through the shared `TaskAuthority`, and tracks
  where the player logically is.

## You build in Unity

1. **Prefab + model**: the visible player (model, animator, whatever you like).
   Name it whatever you want; the script that implements `IPlayerAvatar` lives on it.
2. **Movement**: your character controller moves the transform. `ExecuteMove(dest,
   minutes)` is your cue to start walking there; the director advances travel
   progress as the clock runs (`RecordMovementProgress`, called from your game
   loop with elapsed whole minutes). Arrival is script-side — do not teleport the
   logical location yourself.
3. **Input → director**: WASD/click input must translate into `PlayerDirector`
   calls (`TryIssueMoveIntent`, `TryIssueTaskIntent`, `CancelCurrent`). Never move
   the player or start work without going through the director — that is what keeps
   the budget and task rules honest.
4. **Camera**: follow cam, whatever feels right. Not script-side.
5. **Task visuals**: `ExecuteTask(taskId)` is your cue to play working visuals at
   the task's location (look up the task in `TaskAuthority` for its definition and
   location). `ExecuteCancel()` stops them.

## Intent flow

```
Input (Unity) → PlayerDirector.TryIssue* (scripts: validates, books budget)
  → IPlayerAvatar.Execute* (Unity: performs visuals/movement)
  → game loop: RecordMovementProgress / TaskAuthority.RecordWork (scripts: time passes)
  → arrival / task complete (scripts)
```

## Rules your side must respect

- Do not change the player's transform location without a director move intent.
- Do not mark tasks complete/in-progress yourself; the authority owns task state.
- The director works headless (no avatar) — your prefab is replaceable; keep game
  logic out of it.
- Travel minutes are flat per-route estimates for now. When the location/journey
  model lands, `TryIssueMoveIntent` will feed it instead; your `ExecuteMove`
  implementation does not change.

## Tuning you own without touching scripts

- The player's daily work-time budget (`WorkTimeBudget.DefaultDailyWorkMinutes`
  is the default; per-person values can differ).
- Travel minute estimates per route (your data, passed into `TryIssueMoveIntent`).
