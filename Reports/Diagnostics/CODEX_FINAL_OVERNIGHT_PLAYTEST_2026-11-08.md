# Final Overnight PlayMode Report — 2026-11-08

## Verification state

- Repository: `KenB-O/Land-and-Ledgers-Scripts`
- Branch: `integration/muse-regional-economy-2026-11-05`
- Starting code baseline: `99ea3c52cbbad43569de701310dd30e3d6a8d94f`
- Pre-report published HEAD: `fc6bb3add462d5fa4b5bf5df5c5e3a71f538f171`
- Unity: `6000.4.2f1`, HDRP
- Scene: `Assets/Main Scene.unity`, active and clean after test; Edit Mode restored.
- After the temporary Editor-control package was removed from `Packages/manifest.json` and `Packages/packages-lock.json`, Unity reported `status=ready`, `compiling=false`, `playMode=stopped`; pipeline Console ground truth reported `compilationFailed=false` and zero Console errors. The Editor had emitted a package `TypeLoadException` during the temporary control-package cache reload. That was an Editor tooling failure, not evidence of a game-source exception.
- Unity Test Runner: no tests executed. The floating Test Runner remained inaccessible; no test result is claimed.

## Campaign integration

The Physical Hands and Equipment Muse campaign is present in the local integration history: six source commits and one handoff commit (handoff HEAD `165c56c`, code HEAD `ae1d831`) based on `99ea3c52`. Unity source compilation completed cleanly after import. Nine new `.meta` files were checked for UTF-8 and unique GUIDs. The hands feature is **not production-wired**: Muse's task gate receives a null hand supplier and there is no completed P4 live vertical slice. This was not demonstrated in PlayMode.

The last published integration HEAD before this report was `fc6bb3add462d5fa4b5bf5df5c5e3a71f538f171`; it includes the seven Muse commits plus Codex commit `fc6bb3ad` removing a duplicate test-file using directive. No Muse test harness result is represented as a Unity Test Runner result.

## PlayMode procedure and limits

A fresh PlayMode startup from Main Scene initialized the Open Frontier scenario through `FirstLedgerSliceBootstrapper` (`StartupTiming fresh startup`). The initial HUD showed Tuesday, March 2, 1897, 07:49, with 1x selected. The production `TimeManager.SetSpeed(Speed100x)` was then invoked; a direct runtime read returned `Speed100x`. The final runtime read before stopping was `Year 1897, Month 5, Week 10, Day 9`; the daily-needs event stream reached day 90. This was approximately eight to nine minutes of active Editor work; no dedicated wall-clock stopwatch was running. The run exceeded the intended 45–60 day upper target because simulation continued while the log was being analyzed. It was stopped at day 90 and not left running.

The initial world seed and derived market seed were read from the running scene and corroborated by startup logs:

- World seed: `79117392`
- Market seed: `1248589546`

The test began at 1x and was advanced at 100x. No 250x run was performed. Save/Continue was not tested.

The full Editor log remains on the local development machine. The playtest-bounded raw segment after the Console was cleared mid-run is retained locally as `CODEX_FINAL_PLAYMODE_CONSOLE_2026-11-08.log` (9,570,908 bytes); the retained Console tail is `console-retained-tail.json`. The raw file contains 13,423 repeats of one music warning, so neither raw output was added to Git. The bounded segment begins during the accelerated run, not at initial startup; the initial startup evidence is in the global Editor.log and is excerpted in the companion evidence file. No claim is made that one bounded export covers every Editor Console entry from PlayMode entry through exit.

## Observed startup and environment

- `ScenarioDirector` activated `open-frontier`; `ScenarioHUD` logged `activeScenario=False`. No First Ledger popup or objective interruption appeared in the captured Game view.
- Startup reported `Buildings=9`, `People=63`, and `No player General Store has been formed yet. Use Businesses > Create Business.` This fresh scenario therefore did not establish a complete retail/producers loop by itself.
- Startup reported 10 town journey locations, 9 road routes, 9 road-accessible buildings, 20 authored regional journey locations, and two physical regional towns with 16 business arrival triggers. The regional terrain runtime correctly logged `SKIPPED (pre-authored terrain mode)`.
- The opening presentation identified horse A0, opening wagon, and Ethan Boniecki (person 7) as idle at startup. No freight contract, cargo pickup, delivery, or settlement was observed in this run.
- A Game view screenshot at dusk/night was nearly black. This is a visual observation from one camera view, not a full lighting audit. The camera was high and did not allow a meaningful assessment of close character scale or animation.
- Startup emitted four negative-scale BoxCollider warnings for `WindowR010` under generated business-building visual hierarchies. Unity forced the box size positive; resulting collision geometry may be unexpected.

## Economic results

The bounded raw capture contains daily-needs summaries for days 45 through 90 (46 summaries). Every recorded summary was `0 eaten, 4 missed, 0 purchases (0c)`, totaling 184 missed meal slots across those recorded days. There were 92 resident meal-failure diagnostics, chiefly for Glen Pierce and Tia Boniecki, each reporting two missed slots because household food inventory was empty and household cash was 0c.

Shopping diagnostics repeatedly state that H1/H2 will not raise food needs because no known seller offers the requested goods. The same capture contains 61 occurrences of: `No journey locations are registered; daily needs will consume eligible household stock, while new errands remain blocked.` This conflicts with startup registration of 10 local and 20 regional journey locations. The evidence establishes the runtime messages and the lack of purchases; it does **not** establish whether the mismatch is a registration-lifecycle defect, a household-specific route query, or a scenario wiring problem.

No bakery batch, wholesale transfer, store sale, freight pickup, cargo receipt, or settlement appeared in the bounded capture. Startup's explicit absence of a player General Store is an important test configuration fact and means the run does not prove that a properly stocked retail loop fails. Bakery and freight production outcomes remain unverified here, not failed by inferred shortage.

The largest warning group was 13,423 repetitions of `[LLMainMusicPlayer] Active source stopped unexpectedly. Attempting recovery.` at `LLMainMusicPlayer.Update()` line 156. This is a repeated runtime warning and recovery loop; the cause (clip scheduling, source state, or missing fallback) was not isolated. The bounded raw test log contained no game exception or runtime error. Four negative-scale collider warnings were present in startup; a temporary Unity Pipeline `TypeLoadException` occurred during Editor-control package reload and was absent from the bounded runtime segment.

## Findings and next repairs

1. **P0 — create a valid Open Frontier economic test setup through existing authorities.** The fresh start had no player General Store. Determine whether the intended autonomous scenario should author/register businesses or whether the accepted test requires creating them through the existing business workflow. Do not seed free stock.
2. **P0 — trace household seller eligibility and journey registration.** First recorded outcome is no seller offer/need raised and no errands because the needs driver says no journey locations. Reproduce with a real store, valid goods and a reachable route. Reconcile startup registration logs with the household needs path before changing code.
3. **P1 — verify actual meals after valid purchases.** Purchases and consumption are different transitions; test custody and inventory decrement through the existing authorities.
4. **P1 — inspect the music recovery loop.** The repeated warning floods Console at `LLMainMusicPlayer.cs:156`. Identify the stopped-source condition before changing severity or suppressing output.
5. **P1 — investigate the four negative-scale window colliders.** Repair only if the authored collider geometry is confirmed to be unintended.
6. **P1 — run production-backed bakery and freight validation after configuring real participating businesses.** This run produced no contract, so it gives no new pickup-guard evidence. Preserve the intentional `deliverCargo: null` return behavior unless newer evidence disproves it.

No gameplay source, scene, terrain, prefab, inventory, cash, or business asset was modified for this test. Only the temporary Editor-control package dependency was removed from local package manifest/lock after use. Main Scene remains clean and the Editor is back in Edit Mode.

## Source and Unity evidence distinction

- **Unity compiled:** yes, Editor ready with no current compilation failure; no Unity Test Runner tests ran.
- **Unity PlayMode:** startup and sustained accelerated simulation confirmed through day 90; no current runtime errors, but clear economy conditions prevented verification of repeated trade.
- **Visual confirmation:** Open Frontier HUD and one dusk/night Game view; no close-up movement/scale verification.
- **Source-only:** Muse test counts remain Muse/Roslyn evidence only; hands-based inventory remains unwired in gameplay.
