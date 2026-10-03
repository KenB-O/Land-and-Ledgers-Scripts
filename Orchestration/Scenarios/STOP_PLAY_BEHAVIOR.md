# Stop-Play Behavior — Land & Ledgers (DEV-3)

**Standing law: NOTHING VANISHES.** This document is the one place that states, per
data type, what reverts at stop-play, what persists, and where it persists to.
If a new system introduces runtime state, its entry goes here.

## Authored definitions — PERSIST (always)

- `ScenarioAsset` fields (identity, player-household declaration, population inclusion,
  goals, objectives, starting tasks, tunables): ScriptableObject assets on disk.
- `TaskDefinition` catalogs, skill registry entries: assets/code on disk.
- **Exception:** explicit "Persist to ASSET" in the puppet master writes a tunable
  back to the asset — audit-logged, deliberate, and permanent.

## Runtime overrides — REVERT at stop-play

- `ScenarioRuntimeState`: goal/objective completion flags, reworded objectives,
  runtime-added objectives, tunable overrides. Lives in memory per run; evaporates.
- `PuppetEntityLookup` registrations: dev aid only, cleared.
- `TimeManager` clock state: reverts to the scene's serialized values.

## Runtime-spawned entities — DESTROYED at stop-play (by design)

- Everything under the **"Runtime Entities"** hierarchy root is transient scene state.
  Unity destroys it on stop-play; the scene returns to its pre-play contents.
- Their *definitions* (prefabs, scenario assets) persist — only the live instances go.
- Because every spawned object has a visible, meaningful name under a visible root
  (DEV-3 rule 2), you can inspect all of it *during* play mode. Nothing is hidden.

## Explicit saves — PERSIST to disk

- Save-game DTOs (`LandLedgersSaveGameDto` and domain DTOs): persist only when
  Kennedy triggers a save through `SaveLoadManager`. Autosave behavior, if added,
  will be documented here.

## The rule for new systems

Ask: "If Kennedy stops play mode right now, can he find this in the editor?"
- If it's authored: it must be an asset (or scene object), editable in the editor.
- If it's runtime-only: it must be visible under "Runtime Entities" (or otherwise
  hierarchy-visible) during play, and its transient nature must be documented here.
- If it's neither: it doesn't ship. Use `DevGuards` to fail loudly instead.
