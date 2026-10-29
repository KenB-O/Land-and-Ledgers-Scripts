# Pass XII — Pre-Playtest Acceptance Checklist (for Codex)

Only after these gates are evaluated should Codex recommend an extended owner
playtest. A partial result can still be useful, but it must be labeled
accurately — do not recommend a playtest on an unmeasured gate.

## Gate 1 — Project integrity
- [ ] Unity opens cleanly, full source compiles (including §4 of the manifest),
  references intact, no unwanted asset migration.
- [ ] `.meta` identities preserved; new files carry their fresh GUIDs.

## Gate 2 — Main Scene initialization
- [ ] Exactly one legitimate instance of each required manager; duplicate
  census logs no errors on Start or after restore.
- [ ] Clean New Game shows ZERO player-owned businesses and zero flipped NPC
  buildings, with AND without a ScenarioDirector in the scene.
- [ ] No ghost ownership after Continue/Load (activation not re-issued).

## Gate 3 — Scenario identity
- [ ] Correct selected scenario, date, objective display, deadlines, completion
  and failure states in the HUD; Sandbox shows a clean state.
- [ ] Expired objectives stay failed; latched goals stay complete across loads.
- [ ] Failure notice board surfaces in the panel (PASS11-006 wired).

## Gate 4 — Living settlement
- [ ] People actually work, shop, consume, travel, earn, pay, and use real
  businesses through ordinary runtime execution (not test fixtures).

## Gate 5 — Business interaction
- [ ] The player can inspect, acquire, form, staff, and operate supported
  businesses without misleading interface states.
- [ ] Acquisition shows one next legitimate action + deal details; provenance
  is shown or honestly marked Unknown; the 10 PARTIAL types are not advertised
  as fully functional.

## Gate 6 — Construction and property
- [ ] Land ownership, building design, material supply, construction, and
  occupancy remain distinct and legitimate; no menu-spawned buildings.

## Gate 7 — Financial clarity
- [ ] Revenue, operating profit, cash, obligations, and valuation do not
  contradict the economic ledger; financing is never reported as operating
  revenue; no transaction is double-counted across periods.

## Gate 8 — Save/load
- [ ] The normal game saves and restores an active economy without duplicate
  events, duplicate wages, duplicate activation, or corruption — including
  double restore and old-save fallbacks.

## Gate 9 — Presentation
- [ ] The October 9 P0/P1 visual issues are corrected, WITH screenshots:
  management-panel layout (006), seller-meeting modality (007), acquisition
  flow (008), Civic/Resources panels (014/015), General Store startup notices
  (018). Terrain (016) and audio lifecycle (019) are separately labeled
  (confirmed unresolved at Muse handoff — Codex must evaluate, not assume).

## Gate 10 — Main Scene simulation (RUN-004)
- [ ] Thirty-day autonomous PlayMode proof performed through the actual Unity
  scene, with cash/goods reconciliation and naturally arising failures
  (including a deadline failure that stays failed).

## Playtest recommendation rule
Recommend an extended owner playtest only when Gates 1–10 are evaluated and
the failures, if any, are listed with their impact. If Gates 2, 3, 5, or 10
fail, the playtest will reproduce the October 9 experience and should not be
recommended yet.
