# Pass XXII — Main Scene presentation checkpoint

Date: 2026-10-30  
Project: Land & Ledgers, Unity 6000.4.2f1  
Scripts branch/HEAD at verification: `integration/pass12-unity-readiness` / `c4b41dba6c7c70de3f972b6dcc902190d03745d8`

## Scope

This checkpoint records a targeted Main Scene correction made through Unity's Inspector to the existing `TownGenerationSettings` asset referenced by the Main Scene's `TownWorld` instance. No resource, parcel, mineral, TerrainData, or scene object data was deleted. The asset's `.meta` file was not changed.

## Authored setting changes

The following were disabled in `Assets/Core/World/DefaultTownGenerationSettings 1.asset`:

- All regional debug/inspection markers, while keeping `syncRegionalDebugOverlaySettings` enabled so the runtime overlay follows the asset.
- `showPlots`, which creates developer plot-perimeter visuals.
- `showResourceDistricts` and `showResourceProtoSites`, which create colored resource-area and remote-site visuals. `showResourceSuitabilityOverlay` was already disabled.
- `generateDetailProps`, which generated scattered agricultural-yard support props.
- `generateForestEnvironmentDressing`, which enabled procedurally scattered forest dressing.

The changes preserve the underlying regional, parcel, mineral, forestry and agriculture data. Roads and buildings remain enabled. The separate deterministic working-site dressing path (`BuildAgriculturalWorkingSiteVisuals`) is not gated by these settings and remains a known visual-only representation; it was not changed in this checkpoint.

## Unity observation

The correct Unity 6000.4.2f1 Editor opened `Assets/Main Scene.unity`. In Play Mode, a fresh First Ledger start displayed the scenario start panel, then the visible objective panel with the authoritative eight goals, including the $15,000 owner-equity target. The HUD reported `IN PROGRESS — 0 of 8 required goals complete`. The player-facing cash/net-worth HUD remained visible. The HUD was observed after the real `ScenarioDirector` activation log.

In the observed run, the clock displayed January 1, 1886 at New Game and continued to January 2, 1886. This conflicts with the First Ledger `StartingWorld` date of 1870 and its start-panel narrative. `TimeManager` stores non-negative absolute days relative to an 1880-03-01 anchor; applying 1870 through the ordinary date jump clamps it to the anchor. No date mutation was made because correcting this safely requires a calendar/save-compatibility decision, not a presentation-only jump.

The Unity Console showed no errors at the time inspected and 15 warnings during the continuing session; warnings were not fully triaged. The test runner was not executed in this checkpoint. The Editor process reached roughly 5.6 GB working set with under 300 MB physical memory free, so further suite execution was deferred to protect the active project/session. This is a live visual Play Mode observation, not an automated test or 30-day simulation.

## Preservation

Before editing, the asset and `.meta` were copied to:

`F:\Land & Ledgers_Pass22_Backups\20261030_pre_world_generation_settings`

After editing, both were copied to:

`F:\Land & Ledgers_Pass22_Backups\20261030_post_presentation_settings`

The post-edit asset SHA-256 matches its backup: `6E2FA579BBF2EAE40244EE2FDC8D22BDAD714BD079CB8CD926596561A049F796`. The `.meta` SHA-256 remains `FE25BA8C6E5A77F8135A8EA3BB1F96AF957B976F95F0EC1F39B59BF4EA81DF71`.

## Remaining work from the read-only review

- Reopen the project with adequate memory and run the current full/targeted EditMode and PlayMode suites.
- Verify settings effects after a fresh world regeneration; the current screenshot showed no regional colored debug rectangles, but was not a before/after automated comparison.
- Add a clear authored gate for visual-only agricultural site dressing, then bind actual animal/production visuals to authoritative identities before presenting them as live simulation.
- Resolve the First Ledger start-date/calendar epoch inconsistency with explicit migration and save-compatibility tests.
- Continue the identified journey-location, rent, animal-feed, birthday, Person lifecycle, ownership/formation, production, conservation and 30-day Main Scene work; none of those are declared fixed by this checkpoint.
