# Land & Ledgers Codex Instructions

This repository is a Unity project for **Land & Ledgers**, a realism-first frontier ownership and business empire sim set in a Dakota-style town in spring 1897. The game is grounded, serious, premium, readable, materially believable, and frontier-practical. World comes first, UI second.

## Project Direction

- Treat the canonical Bible and the latest working scripts as the source of truth.
- When older notes conflict with newer clear design locks, follow the newest clear lock.
- Preserve the living-town, ownership, acquisition, staffing, supply, repair, and project identity.
- Keep the game ownership-led. It is not a hand-placed city builder.
- Keep the tone commercially practical. Avoid cute, theme-park, action-game, fantasy-western, or novelty drift.
- Rail comes later. Telegraph follows the rail corridor after rail. Electricity comes later than telegraph and starts in the town core.

## Engineering Priorities

Prioritize work in this order unless the user gives a narrower task:

1. Runtime authority.
2. Save/load authority.
3. Tests.
4. Player-facing decision support.
5. UI readability.
6. Wording polish.

Use existing scripts and existing architecture first. Prefer one coherent system over overlapping half-systems. Keep edits small, targeted, and easy to verify. Do not create random new systems, speculative abstractions, or widened scope just because they sound useful.

## Unity Guidance

- Confirm this root has `Assets`, `Packages`, and `ProjectSettings` before making Unity changes.
- Preserve existing naming, namespace, serialization, prefab, ScriptableObject, and editor-test patterns.
- Do not silently rescale building prefabs to fit plots. Plot footprint authority and visual prefab scale authority are separate.
- Businesses should share one broad runtime shell. Do not fork new one-off business runtimes unless a design lock requires it.
- Net worth is the primary VP success metric, and Liquid Cash remains visible in the HUD.
- Tabs are dashboards and launch points. Serious acquisition and loan flows may break into dedicated workflow screens.
- Favor data/source-of-truth changes over scene-object assumptions.

## Codex Workflow

- Use `rg`/`rg --files` first. Avoid scanning generated folders unless directly relevant: `Library`, `Temp`, `Logs`, `obj`, `.vs`, `UserSettings`, `Build`, and `Builds`.
- Read the narrowest relevant files before editing.
- Do not change gameplay code for setup or instruction tasks.
- Do not make unrelated edits or broad formatting passes.
- Run the most relevant validation available before finishing. For Unity code, prefer focused editor tests when possible; if Unity cannot run here, validate with static checks and state the exact blocker.
- Report changed files in the final response.
- If a check cannot run, say exactly why.

Done means the requested behavior works, changed files are listed, relevant validation is reported, and no unrelated edits were made.
