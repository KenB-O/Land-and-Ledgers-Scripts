# Final Unity PlayMode Findings — 2026-11-06

## Run identity

- Project: `F:\Land & Ledgers`; Unity `6000.4.2f1` HDRP.
- Starting source revision: `b10866df594e56207c25ea4d6edb201abce77f8c` on `integration/muse-regional-economy-2026-11-05`.
- Scenario: `open-frontier` (runtime log confirms `[ScenarioDirector] Scenario 'open-frontier' active`). The Game view opened directly to the town and HUD without a First Ledger acknowledgement/objective popup.
- World seed: `1537408365`; market seed: `1717440339`.
- Speed: `100x` (highest speed button visibly selected in the live HUD).
- Start: March 1, 1897, day 1; end: December 11, 1897, day 285. The final visible screenshot shortly before stop showed December 4, day 277; eight more days advanced before the Editor returned to Edit Mode. This exceeds the requested 30-day target.
- Wall time: approximately five minutes from the recorded pre-Play Editor-log offset to stop, including Unity PlayMode assembly reload. No independent stopwatch was running.
- Console at stop: 27 informational entries, 335 warning entries, 1 error entry in the Unity Console counters. Editor.log retains repeated calls that Unity's collapsed Console counter does not present as separate rows.
- Raw unfiltered Editor.log segment: [`CODEX_FINAL_PLAYMODE_CONSOLE_2026-11-06.log`](CODEX_FINAL_PLAYMODE_CONSOLE_2026-11-06.log), 4,363,725 bytes. It begins before fresh-world startup, includes startup, messages, warnings, errors and stack traces, and ends after PlayMode stopped. It is not a filtered or reconstructed transcript.

## Result summary

The fresh world bootstrapped nine residents, five households, and four businesses. Open Frontier was active, and the player entered the sandbox without a First Ledger popup. Startup applied the authored finite Bakery fuel endowment (40 units), installed the Bakery's oven and hand kit, booked the development fallback wholesale account (24 loaves/day at 2c, explicitly still provisional), and registered the finite 30-unit notions source with a 14-day transit estimate.

The economic loop did not execute. From day 1, all five household records were reported as having no active members; `DailyNeedsService` reported no acting adult, so household meal/purchasing execution had no eligible actor. The run contains no bake completion, bread output, wholesale transfer, household purchase or meal-consumption event. The population-generation summary says residents and households were generated, but the membership registry reads used by household shopping/needs return no active members. This is the first observed causal break for the consumer side; inspect `PopulationManager.GeneratePopulationSnapshot` → `CompletePopulationSnapshot` → `RebuildMembershipRegistry`, `HouseholdMembershipRegistry.Register/GetActiveMembers`, and `DailyNeedsService.FindActingAdult`.

Freight also stopped before a legitimate request: one startup diagnostic says the daily request driver cannot evaluate the opening order because the Millbrook runtime is unresolved. A separate warning repeats that no journey locations are registered on every one of the 285 daily passes. No agreement, shipment, dispatch start, pickup, journey, return, delivery or payment occurred in this fresh run. This run therefore does not verify the earlier physical freight-return path or the newly added transient carrier-capacity deferral; those changes compiled but require a later runtime test after startup wiring is repaired.

The opening fuel stock and configured equipment existed, but no production event used them. The 14-day notions source registration is evidence of a supplier definition, not of a Bakery-owned order/receipt. No fuel replenishment or household fuel purchase was established. The run does not prove a functioning repeated food cycle.

The mortality-disabled setting worked in observed old-age cases: 15 `Death suppressed` events were recorded, including P38 on day 285 (1897-12-11), with no completed death/estate event observed in the captured segment. Source-level `DailyNeedsService.WorkCapacityFactor(..., conditionDebuffsSuppressed)` and health-capacity helpers have a suppression path; this PlayMode run does not prove productive work under starvation because the household/member and production paths failed first.

## Distinct runtime diagnostics and severity

Counts below are counts in the raw log segment; repeated stack frames are retained in the raw file.

| Evidence | Count | Finding |
|---|---:|---|
| `[LLMainMusicPlayer] Active source stopped unexpectedly. Attempting recovery.` | 4,541 | Per-frame warning spam from `LLMainMusicPlayer.Update` (line 156). The Console remains noisy; the recovery does not repair the repeatedly stopped source. Inspect authored AudioSource/clip/loop wiring before changing behavior. |
| `[SimulationDrivers] No journey locations are registered; daily needs will consume eligible household stock, while new errands remain blocked.` | 285 | Daily warning and real functional gap: `SimulationDrivers.OnDayChanged` cannot resolve route locations. Household needs can still consume eligible stock, but new physical errands are blocked. |
| Household membership/actor diagnostic, aggregated in daily report | 285 daily summaries; each summary lists H1–H5; 1,425 household occurrences | All households lack active registered members from the first day. Each daily report says each household has no active members and `DailyNeedsService` has no acting adult. H1's errand plan fails each day. No household shopping is possible. |
| `Unexpected error in Burst compilation: ... DllNotFoundException ... Library\BurstCache\JIT\...dll, error code 1392` | 1 error | Unity cache/Burst native-library load failure. This is an Editor/tool-cache error, not a C# compile error. Do not delete the cache blindly; investigate Unity/Burst cache integrity in a controlled maintenance pass. |
| `[FreightAgreement] ... Millbrook runtime is unresolved.` | 1 | Freight request path is connected but cannot resolve the supplier runtime during startup/day 1. No contract or shipment follows. Trace `FirstLedgerSliceBootstrapper.ConfigureMillbrookAidTable` and the `SimulationDrivers` freight participant resolver/registration order. |
| `[ScenarioDirector] Player household 'Development Household' declared ... (GHOST-DEF-006)` | 1 | Expected current sandbox/player-household placeholder is visibly declared by scenario source; it is not evidence of a generated resident household or autonomous shopping actor. Decide whether this player founder is intended in Open Frontier. |
| `[Population] ... Death suppressed ... Mortality disabled` | 15 | Expected development immortality diagnostic; suppression works in sampled old-age cases. |

The Console's warning badge (335) is a grouped/display counter and differs from raw repeated `Editor.log` writes. Do not interpret the 4,541 music-warning lines as 4,541 unique warning causes. The two repeating causal families are the music Update recovery and the per-day missing journey-location registration; the household membership failure is also repeated once per household per daily report.

## Source state reached in this session

The completed freight work in the dirty source tree includes:

- `FreightServiceAgreementBook.FindNextAwaitingPreparation`: only selects accepted, linked, unprepared agreements whose linked shipment is still Planned and not loaded, so completed agreements do not monopolize the single opening wagon.
- `SimulationDrivers.DriveOpeningHitchPreparation`: can prepare a later shipment using the already correctly attached and reserved horse/wagon instead of returning forever because the pair is already hitched; shipment-scoped hitch identity is refreshed without duplicating the asset.
- `FreightServiceAgreementBook.DeferTemporarily` plus `SimulationDrivers.HandleCompletedFreightMeetingTask` / capacity checks: temporary live carrier/wagon conflicts return the same request to Open with its stable identity and reason, release stale meeting handles, and avoid terminal refusal or duplicate request creation. Accepted agreements cannot be deferred.
- `CoupledTransportMovementController.StartRegionalReturn` and `OpeningTransportPresentationController.BeginCoupledPhysicalReturn`: after a confirmed physical supplier arrival and valid loaded cargo, the existing coupled team follows the regional return segment and then the reversed town route; identity/destination/origin confirmations gate the state transitions.
- `SimulationDrivers.ConfirmPhysicalFreightOriginArrival` and physical-return processing: the final return arrival is the point that allows the existing delivery/custody/settlement authority to run; the animation/movement layer alone does not move money or cargo.

Evidence predating this final fresh run: a preceding PlayMode session on this local source had one complete Millbrook trip and one subsequent shipment complete with the same horse/wagon; each received the flour lot and settled 125c once. A third order met a busy carrier and was terminally refused, which motivated the new temporary deferral change. The final run here failed earlier at Millbrook runtime resolution, so it neither confirms nor regresses the trip path. Do not treat previous-run evidence as evidence for this final fresh seed.

## Save/load and visual checks

- This final run did not save or restore state. Save/load for household memberships, active freight, cargo, physical regional travel, rider presentation and the newly changed request deferral remains unverified in this run.
- The active PlayMode Game view showed the development town and HUD. It did not reach an active shipment, so Ethan/A0/wagon travel, driver-seat pose, actual destination trigger entry, and persistent off-map GameObjects were not visually testable here.
- The repository contains `Assets/Core/Travel/Off Map Town.prefab` and `Wagon Cargo Horse.prefab`; no asset named `Auth Map Town` was found in `Assets/Core/Travel`. Do not silently treat the existing Off Map Town as the owner-authored Auth Map Town without checking its structure and approval.

## Validation status

- Unity source compilation: prior actual Editor compilation after the last code/test edits succeeded (`Tundra build success`, 9 script items updated, 938 evaluated; zero C# compile errors). The Burst cache error above is separate and occurred at runtime/test startup.
- Unity Test Runner discovery: 5,178 EditMode tests visible in the actual Test Runner. No tests were executed. Focused search/input against the detached Test Runner window did not work through this UI bridge; no full suite was started.
- PlayMode: actual fresh Open Frontier run, 100x, 285 game days, stopped and returned to Edit Mode. It failed the household and freight prerequisites described above; it is not a successful circular-economy acceptance test.
- Git diff: source and focused test edits are in the local dirty tree. Final commit/push status is recorded in the Muse handoff after publication.
