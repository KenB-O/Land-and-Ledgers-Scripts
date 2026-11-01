# Pass XI WS5 — Simulation-Time Proposal Review (RESEARCH AND PLANNING ONLY)

Nothing in this document is implemented. No change was made to
`SimulationSpeed`, `SimulationTimeSettings`, `TimeManager`, or any
driver timing. All findings were verified against the source on
`muse/pass11-ws5-player-facing-performance` (base `2101ea3`).

## Verdict: TECHNICALLY APPROPRIATE, with conditions

The proposed speed list is technically sound as an engine change and
the day-length mapping is exact. It should ship only with the
conditions in "Required implementation notes" addressed, and the 1:1
mode should be presented as a real-time *observation* mode, not a
playable speed (see "What 1:1 really means"). Dropping 4x/6x/100x from
the player list is a UI-scope decision, not an engine risk.

## The proposal, verified against the code

Owner proposal (intentional, unapproved): Paused = no progression;
genuine 1:1 real-time observation; Normal 1x = 5 real minutes per game
day; 2x = 2:30 per day; 5x = 60 s per day; Default 10x = 30 s per day;
25x = 12 s per day; 50x = 6 s per day.

The clock math (`TimeManager.Update`, `Core/Time/TimeManager.cs`):

```
gameDeltaSeconds = realDeltaSeconds * multiplier
    * (GameSecondsPerDay / realSecondsPerGameDayAt1x)
```

with `GameSecondsPerDay = 86400` and `realSecondsPerGameDayAt1x`
currently `60` (`SimulationTimeSettings.cs`). Actual multipliers come
from `SimulationTimeSettings.GetMultiplier` per enum member, with the
enum int value as fallback only when settings are missing
(`ResolveSpeedMultiplier`).

Setting the base to **300** real seconds per game day at 1x gives,
exactly (real seconds per game day = 300 / multiplier):

| Proposed speed | Multiplier | Seconds / game day | Matches proposal? |
| --- | --- | --- | --- |
| Paused | 0 | — (no progression) | Yes (`GetMultiplier(Paused) = 0`; `SetSpeed` sets `isPaused` when the multiplier is ≤ 0) |
| 1:1 real-time | special (see below) | 86400 by definition | Yes, with the special case |
| Normal 1x | 1 | 300 = 5:00 | Yes |
| 2x | 2 | 150 = 2:30 | Yes |
| 5x | 5 | 60 = 1:00 | Yes |
| Default 10x | 10 | 30 | Yes |
| 25x | 25 | 12 | Yes |
| 50x | 50 | 6 | Yes |

Every mapping is exact — no rounding, no approximation. For reference,
today's list at base 60 gives 1x = 60 s/day, so "Normal" becomes 5×
slower than today while the new default (10x, 30 s/day) advances days
2× faster than today's default.

Enum safety: `SimulationSpeed` is currently
`Paused = 0, 1, 2, 4, 6, 10, 25, 50, 100`. The value **5 is unused**,
so appending `Speed5x = 5` collides with nothing and changes no
existing serialized value. Serialized enums are append-only: existing
members must keep their ints, and any new member must take a fresh int
(see the RealTime warning below).

## What 1:1 really means (and why it needs a special case)

1:1 means one real second equals one simulated second, i.e. a game
day takes 24 real hours. It cannot be expressed as an ordinary
multiplier field without coupling: the required multiplier is
`base / 86400` (300/86400 ≈ 0.00347 at the proposed base), which
breaks if the base is ever re-tuned, and `Sanitize()` only clamps
multiplier fields — it does not recompute them. The clean
implementation is a `TimeManager` special case for the RealTime member
in `Update` (`gameDeltaSeconds = realDeltaSeconds`), with
`GetMultiplier` returning a small positive value for it.

Two traps make the special case mandatory rather than optional:

1. **The fallback-int rule.** When settings are missing,
   `ResolveSpeedMultiplier` returns `(float)(int)speed`. Whatever int
   the RealTime member takes *is* its degraded-mode multiplier, so
   its int must be chosen so the degraded mode is safe — or, better,
   `ResolveSpeedMultiplier` must special-case RealTime to a safe
   value (0, i.e. paused, is the safe degraded behaviour) instead of
   blindly casting.
2. **Save/load forces Paused on a zero multiplier.**
   `LoadFromSaveDto` sets `isPaused` when
   `ResolveSpeedMultiplier(currentSpeed) <= 0` and then rewrites
   `currentSpeed` to `Paused`. A RealTime implementation whose
   multiplier reads as 0 would load every saved RealTime session as
   Paused. The multiplier reported for RealTime must stay positive
   (0.00347… at base 300 satisfies this).

Playability limits of 1:1 (all consequences of the existing tick
design, not new bugs): the short tick is 15 game minutes, so at 1:1 a
short tick fires every 15 real minutes, and everything driven from
`ShortTick` — due-event pops, domestic-labor execution, player
movement progress in whole minutes, autonomous work minutes — advances
in 15-minute chunks at that cadence. Daily drivers run once per 24
real hours. A 120-game-minute task takes 2 real hours; a cross-town
journey or construction job measured in game hours takes that many
real hours. Nothing breaks; it is simply a watch mode.

## Required implementation notes (what must change, in one place each)

1. **Settings asset, not just the code default.** Changing
   `realSecondsPerGameDayAt1x` from 60 to 300 in
   `SimulationTimeSettings.cs` changes only newly created assets.
   Existing scene settings assets serialize 60 and will keep it
   until the asset itself is updated (a Unity asset edit, outside
   source scope). Same for `startingSpeed`: the code default is
   `Speed1x`; "Default 10x" requires the asset value (and/or the
   code default) to become `Speed10x`, since `TimeManager`
   initializes from `settings.startingSpeed`.
2. **Enum additions.** Append `Speed5x = 5` and a RealTime member with
   a fresh, never-used int. Add its multiplier field(s) to
   `SimulationTimeSettings` with `Sanitize()` clamping, and a
   `GetMultiplier` arm. The `_ => speed1xMultiplier` default arm
   already makes unknown values degrade to 1x rather than crash.
3. **TimeManager RealTime path.** Special-case RealTime in `Update`
   (gameDelta = real elapsed) and in `ResolveSpeedMultiplier`
   (positive, safe degraded value). Keep `SetSpeed`/`SetPaused`
   semantics: pausing from RealTime must remember RealTime as
   `lastNonPausedSpeed` so resume returns to it, and
   `CurrentSpeedMultiplier` must report the RealTime value
   consistently for listeners.
4. **HUD speed list (prefab/scene + code).** `LandLedgersHUDController`
   currently wires buttons for 1x, 2x, 4x, 6x, 10x, 25x, 50x, 100x
   plus pause, with tooltips ("Very fast testing pace", "Maximum
   testing pace") that do not state day lengths. The proposal needs
   RealTime and 5x buttons, removal/repurposing of 4x/6x/100x,
   `RefreshSpeedState` highlight arms for the new speeds, and
   tooltips stating the verified day lengths (e.g. "10x — a game day
   every 30 seconds"). The editor `ScenarioValidationWindow` speed
   row (1x/10x/25x/50x/100x) and `PuppetMasterPanel` 10x/100x
   shortcuts are dev-only but should be updated for consistency.
5. **Agent movement visual scale (the one real visual risk).**
   `AgentMover.GetMovementTimeScale` returns
   `clamp(CurrentSpeedMultiplier, 0, maxTimeScaleMovementMultiplier
   = 12)`. At the proposed speeds that gives 1, 2, 5, 10, 12
   (capped), 12 (capped) — fine — but at 1:1 it gives ≈ 0.0035, so
   agents would move ~285× slower than at 1x: visually near-frozen
   while the clock runs real-time. The 1:1 implementation must
   decide: floor the *visual* movement scale at 1 for RealTime (or
   drive visuals from game-seconds elapsed rather than the
   multiplier), or ship 1:1 with near-static agents and say so.
   Note the existing cap already saturates 25x/50x visuals at 12×;
   that is current behaviour, not a regression.
6. **Old saves at removed speeds.** `TimeSaveDto` persists
   `currentSpeed`/`lastNonPausedSpeed` as enum values, so a save made
   at 100x still loads and runs at 100x after the UI list drops it —
   it simply has no highlighted button until the player picks a
   listed speed. That is acceptable, but it must be a conscious
   choice, not a surprise. Enum additions are save-safe (additive);
   no save-format migration is needed for the clock itself, though
   pacing of a loaded save changes with the new base (only future
   advancement uses the new base; persisted
   `gameSecondsIntoDay`/`absoluteDayIndex` are untouched).

## What does NOT need to change (verified)

- **Daily/weekly/monthly boundaries.** `ProcessDayRollover` fires
  `DayChanged`, and `WeekChanged`/`MonthChanged` when the derived
  Week/Month changes, once per crossed day — including inside
  `CollapseRemainingDayRollovers`, which loops per day rather than
  skipping days. Payroll weeks, production days, and scenario
  progress cannot be silently skipped by fast-forward, at any speed.
  The Gregorian calendar (`CalendarAnchor`, `GregorianDateMath`) is
  independent of speed.
- **Short-tick deferral.** `ProcessShortTicks` caps at
  `maxShortTicksPerFrame = 64` and *defers* the remainder into later
  frames (accumulator carries across midnight per the Pass VIII
  fix); accelerated time never loses simulated seconds. At 60 fps a
  50x frame advances 240 game seconds, i.e. ~0.27 short ticks per
  frame on average (16 ticks per real second) — far under the
  64-per-frame cap, which would only bind during a sustained frame
  stall. At 10x it is ~3.2 ticks per real second; at 1x (base
  300) one tick per ~3.1 real seconds.
- **Tasks, travel, freight, construction, manual activities.** All
  consume game time through `DayChanged`/`ShortTick`/due events on
  the absolute-game-seconds timeline (`AbsoluteGameSeconds`), or
  whole minutes per tick (`RecordMovementProgress`,
  `AutonomousSocietyDriver.AdvanceMinutes` with its 1440-minute day
  and carry across ticks). No driver hard-codes a real-seconds
  day length: the only hard-coded day length in the clock path is
  `GameSecondsPerDay = 86400` game seconds, which is definitional.
  Durations therefore scale automatically — a one-game-day activity
  takes 24 h real at 1:1, 5 min at 1x, 30 s at 10x, 6 s at 50x.
- **Pause/resume.** `SetSpeed(Paused)` pauses; `SetPaused(false)`
  resumes to `lastNonPausedSpeed` (or 1x if that was Paused).
  `LoadFromSaveDto` maps a Paused `lastNonPausedSpeed` to 1x.
  No change needed beyond the RealTime multiplier-positivity point.
- **Due events.** `DueEventScheduler` pops in due order on
  `ShortTick` before other advancement (`SimulationDrivers`
  processes due events first), so accelerated ticks cannot skip
  completions or deadlines. The scheduler is heap-indexed (Pass IX).
- **No exhaustive speed switches.** The only `switch` over
  `SimulationSpeed` is `GetMultiplier`, which has a default arm.
  HUD code uses equality checks, which safely ignore new members
  until their buttons are added.

## Throughput risk assessment

The proposed maximum (50x, 6 s/day) is *slower* than today's maximum
(100x at base 60 = 0.6 s/day), so the proposal introduces no new
worst-case throughput. The risk that does grow is at the new
*default*: 10x at base 300 runs a day every 30 real seconds versus
today's default day every 60 real seconds, doubling daily-drive
frequency. That is a WS4 campaign-proof measurement (time one daily
drive at campaign scale), not a reason to reject the list. Sustained
low frame rates at high speeds grow the deferred short-tick backlog
through the existing deferral mechanism; the per-frame caps
(`maxShortTicksPerFrame = 64`, `maxDayRolloversPerFrame = 7`, with
per-day collapse) bound per-frame work. The HUD date text already
refreshes per visual frame; WS5's text-change guard in
`RefreshDate` removes the redundant rebuild in the common case.

## Mandatory acceptance tests (must all pass before this ships)

1. Day-length table: for each listed speed, advancing N real seconds
   at a fixed frame rate advances game time by exactly the table
   above (Paused: zero; 1:1: exactly the real elapsed; 1x: 300 s/day
   equivalent; 2x: 150; 5x: 60; 10x: 30; 25x: 12; 50x: 6).
2. Enum stability: existing serialized ints unchanged; `Speed5x`
   and the RealTime member take fresh ints; an old save at 100x
   loads and runs (button highlight may be absent).
3. Settings-asset test: a settings asset with the new base (300) and
   `startingSpeed = Speed10x` starts a session at 10x; an old asset
   still carrying 60 is detected/reported rather than silently
   running the old pacing (or is migrated — either behaviour must
   be explicit and tested).
4. RealTime: 60 real seconds advance exactly 60 game seconds; a
   saved-then-loaded RealTime session does not come back Paused;
   pause from RealTime resumes to RealTime.
5. Pause/resume: Paused advances nothing (including short-tick
   accumulators and due events); resume continues from the exact
   persisted `gameSecondsIntoDay` and short-tick accumulator.
6. Short-tick quantization: at 1:1, a due event scheduled 5 game
   minutes out fires on the next 15-minute short tick (documented
   existing behaviour), and no due event is lost across a day
   boundary at 50x (deferral + per-day collapse).
7. Daily/weekly integrity at 50x: a 30-day run at 50x fires exactly
   30 `DayChanged` events and the correct `WeekChanged` count, with
   weekly payroll/production identical to the same 30 days run at 1x
   (determinism across speeds).
8. Movement visuals: at 1:1 agents move at the decided visual rate
   (per the floor decision in item 5 above), not at 0.0035×; at
   25x/50x movement remains capped at the existing 12× visual scale.
9. HUD list: every proposed speed has a working button with the
   correct tooltip day length; the active speed is highlighted for
   every listed speed; removed speeds (4x/6x/100x) have no dead
   buttons.
10. Save/load round trip at each proposed speed: speed, day index,
    time of day, and short-tick accumulator survive exactly, and
    post-load pacing follows the new base.
11. Long-run determinism: the same scripted 30 days produce identical
    end state (cash, obligations, history counts) at 1x, 10x, and
    50x, apart from the documented short-tick deferral diagnostics.
