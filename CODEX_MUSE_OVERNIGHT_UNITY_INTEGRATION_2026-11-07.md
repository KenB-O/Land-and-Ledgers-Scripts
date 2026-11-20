# Muse Overnight Unity Integration — 2026-11-07

## Scope and evidence labels

This was a finite bundle import and Unity verification pass. No source repair, scene edit, prefab edit, commit, merge, or push was made by Codex. Muse's imported changes are present in the local integration branch. Evidence below distinguishes source behavior, Editor compilation, automated EditMode execution, and the live PlayMode observations.

## A. Repository integration

- Repository: `F:\Land & Ledgers`
- Branch: `integration/muse-regional-economy-2026-11-05`
- Starting commit: `f0fd624683f8d527f72ada9f1df8877cdca90003`
- Imported Muse commit / current HEAD: `7fad646e52c2bd93464651130ef2c4b3772bfc49`
- Muse commits after prerequisite: 17; ancestry and exact HEAD verified.
- Current tracking state: 17 commits ahead of `origin/integration/muse-regional-economy-2026-11-05`; no push was attempted, as directed.
- Current status inventory: the 1,048 pre-existing dirty or untracked paths remain untouched. This report adds one new untracked root document, so the current total is 1,049 paths. The pre-integration path inventory was kept outside the project at `%TEMP%\ll_codex_dirty_paths_2026-11-07.txt`. The Muse diff changed 34 paths and did not overlap those inventoried paths. Authored scenes, prefabs, UI, fonts, music, and other local work were not edited by this pass.
- Bundle: `%LOCALAPPDATA%\Temp\overnight-campaign-2026-11-07.bundle`; 96,237 bytes; SHA-256 `0ca6db3d9512104fcb9cd5f1676f592d7e703229ca5870641c84599a651fd29e`. Hash matched. `git bundle verify` passed, the prerequisite was present, and the bundle head resolved to the expected Muse commit.
- No local Codex source changes or repair commits. No merge, reset, cleanup, or publication.

## B. Bundle and imported contents

**Verified:** required prerequisite `f0fd624...` is an ancestor of `7fad646...`; the bundle carried the expected `muse/overnight-campaign-2026-11-07` tip. The branch was fast-forwarded safely. All 17 commits are in the current branch history. Muse's changed `.meta` files accompanied the new tests; no GUID regeneration was run. The Unity scenes and authored asset paths were not part of the Muse diff.

Muse's source changes covered household membership lifecycle and restore consistency, duplicate population diagnostics, journey/Millbrook identity diagnostics, supplier-backed named reserve purchasing, freight request reopening and agreement fulfillment, household reserve receipt reconciliation, local-shopping exclusion for off-map residents, and propagation of `OffMapJourneyAuthority` to shopping/needs callers. No source changes were made here to preempt runtime evidence.

## C. Local authored work

**Preserved:** the 1,048 pre-existing dirty/untracked paths. The repository status still reports 1,048 paths after import. Existing local horse/wagon and reference prefabs, Main Scene-related work, UI, fonts, music, and configuration were left intact. No Unity scene or prefab was saved or modified during this assignment.

## D. Unity compilation

**Unity Editor compiled:** Unity 6000.4.2f1 opened the actual project and completed asset refresh and script compilation. No C# compile errors and no `Compilation failed` event were observed. The Editor log showed `Assembly-CSharp` assemblies loaded; the Console had zero errors before PlayMode. This is Editor evidence, not the Muse Roslyn harness result.

Before PlayMode the Console showed 73 warnings and one Burst JIT cache load error (`Library/BurstCache/JIT/18c4494761fce75a87f6f464a7b6d09f.dll`, Windows error 1392). The import subsequently compiled and ran. Existing negative-scale BoxCollider warnings were also observed on generated building windows. No source change was made to silence either condition.

## E. Focused EditMode tests

The Unity Test Runner discovered 5,273 EditMode tests. Only the following focused groups were run; the complete suite was not run:

| Test group | Result |
|---|---:|
| `HouseholdMembershipRegistryTests` | 19 passed |
| `HouseholdReserveInventoryTests` | 22 passed, 1 failed |
| `DevelopmentMortalityFertilityTests` | 7 passed |
| `DevelopmentTownPopulationGenerationTests` | 14 passed |
| `HouseholdShoppingLoopReserveCreditTests` | 15 passed |
| **Executed total** | **77 passed, 1 failed, 0 skipped** |

Failure: `HouseholdReserveInventoryTests.PartialFillReattemptsOnlyTheRemainingDeficit`, assertion at line 791. Expected wallet `99856`; actual `99844` (12 cents lower). Runtime test diagnostic recorded a 3-meat purchase at 30 cents plus 126 cents freight, total 156 cents. The expected balance differs by 12 cents from that recorded charge. This failure is not classified as a Muse regression without a baseline execution; it is a concrete test/accounting expectation mismatch for follow-up.

Not executed: the other newly requested groups including `CoupledTransportMovementControllerTests`, `PopulationManagerDevelopmentModeTests`, `JourneyLocationRegistrationTests`, `MillbrookRuntimeResolutionTests`, `FreightDispatchActivationIntegrationTests`, `GeneralStoreItemOfferIdentityTests`, `TransportFoundationTests`, and the animator check. No full Test Runner run. A Test Runner scene-save prompt was cancelled; no scene was saved.

## F. Fresh Open Frontier PlayMode

**Observed in actual Unity PlayMode:** fresh world generated from seed `738424587`; market seed `1111202569`; the in-game speed control visibly showed `100x`. The session began Monday, March 1, 1897 and the final Game View showed Saturday, August 21, 1897, day 174 (approximately 173 elapsed in-game days). The Background Tasks elapsed indicator was about 167 seconds at stop. The session was then stopped and Unity was left in Main Scene Edit Mode.

The raw Editor log contains a later second fresh-generation marker (`1665401983`) associated with the Test Runner/editor recovery sequence; the captured PlayMode evidence file is deliberately cut to the completed seed-738 run and ends before that second startup. This avoids mixing two runs.

`ScenarioDirector` logged `Scenario 'open-frontier' active`. The Game View showed the simulation directly, with no First Ledger introduction or acknowledgement popup. This verifies the active sandbox scenario and absence of an interrupting First Ledger popup in this run. First Ledger itself was not separately started in this pass.

The final Game View Console counters showed 999+ informational entries, 379 warnings, and 0 errors. The captured Editor log slice is approximately 6.25 MB / 51,221 lines and includes the session's actual informational records, warnings and stack traces. There were no PlayMode exceptions or error logs in the captured session. The raw log is outside Git at:

`C:\Users\kenne\AppData\Local\Temp\CODEX_MUSE_OVERNIGHT_PLAYMODE_CONSOLE_2026-11-07.log`

## G. Household membership — Muse P0-1

**PlayMode observed:** development population generation reported 9 residents and 5 households. The day-needs driver resolved named people under households H1–H5 and repeatedly processed their meal slots; domestic tasks and household errands were created. This is evidence that the membership path was not empty in this run. The focused membership-registry and development-population tests also passed (19/19 and 14/14).

No duplicate PopulationManager warning was seen in the captured startup. The runtime did not emit a dedicated membership-registry count/instance diagnostic, so exact per-household registry cardinalities were not independently read from the live object. The observed actor coverage plus passing registry tests supports the registration repair; direct runtime registry inspection remains unverified.

## H. Journey registration — Muse P0-2

**PlayMode observed and source connection confirmed:** `SimulationDrivers.EnsureTownJourneyNetwork()` ran the successful path. Its serialized `TownWorldController` reference was `Town World`, instance `111742`; Grid was present and `GenerationPassCount=1`. The diagnostics reported 10 local journey locations, 9 road routes, 9 road-accessible buildings, one connected road component, and zero inaccessible buildings. The same startup path then called `EnsureRegionalJourneyRoutes()`; it registered 20 authored Ashford/Redfield journey locations on the hub `JourneyModel` before daily needs.

The logged active driver was `Game Systems`, instance `111788`. No missing hub, town, grid, route-component, or duplicate-authority failure appeared. The General Store is in the generated nine-building connected local network; the diagnostic reports the aggregate network rather than printing a per-business location lookup. There is no runtime evidence of the former “zero journey locations” failure in this run. The diagnostic identifies no current P0-2 startup blocker.

Relevant sources: `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`, `EnsureTownJourneyNetwork()` and `EnsureRegionalJourneyRoutes()`; `Assets/Scripts/Domains/World/Regional/RegionalTownRegistry.cs`, `ApplyAll()`.

## I. Millbrook runtime — Muse P0-3

**PlayMode observed:** bootstrap and negotiation used the same active `Game Systems` driver instance `111788`; the negotiation diagnostic reported authority claim held and `Millbrook runtime=attached, BusinessId=millbrook_grain_co`. `FirstLedgerSliceBootstrapper.ConfigureMillbrookAidTable()` loaded the Millbrook runtime from Resources and attached it to that driver. The live starting inventory was flour 230 lb, wheat 512 lb, sugar 36 lb, and yeast 23 lb. One flour order debited Bakery business cash 48c and credited Millbrook 48c, reducing flour 230→220 lb and creating stable lot `aidlot-millbrook_grain_co:opening-flour-b1`. The configured quote was 48c per 10 lb sack, with 18 Bakery units per sack and a 50-mile one-way route; these values were used, not modified.

The opening freight request resolved that same Millbrook supplier/lot identity. P0-3's null/wrong-runtime startup failure did not occur. Supplier-adapter registration was not separately dumped as an adapter object identity, but the actual stock-backed purchase, lot creation, freight request, pickup, and subsequent weekly stock replenishment demonstrate that the runtime authority was being used.

Relevant sources: `FirstLedgerSliceBootstrapper.ConfigureMillbrookAidTable()`; `SimulationDrivers.AttachMillbrookAidRuntime()` and freight negotiation/reorder path.

## J. Household shopping and reserve reconciliation

**PlayMode observed:** 172 daily-needs summaries were logged. Their aggregate was 4 meals eaten, 3,092 missed, and 0 purchases. By day 5 the report was 0 eaten / 18 missed per day and remained so through day 174. Household logs state that meal resolution found zero food in household inventory. The Store held opening bread and later received Bakery bread, but no household purchase was logged during this test. On day 174, the specific bread refusal was that the General Store was closed on weekday-index 6; other failed needs included items with no known seller and flour out of stock. That Sunday refusal alone does not explain all weekday failures; the live retail-to-household errand path remains the next point to trace.

The focused reserve-credit loop test passed 15/15. `HouseholdReserveInventoryTests` had the one 12c wallet mismatch above. No claim is made that all household reserve receipts or shopping reconciliations passed runtime verification.

## K. Freight and agreement verification

**PlayMode observed:** Glen P3 requested the correct Millbrook flour lot, reached Ethan P7 at B3, and completed a 5-minute meeting. Quote was 50 mi one way, 100 chargeable round-trip miles, 25c base + 100c mileage at 1c/mi = 125c, payable after successful delivery. One accepted agreement and one shipment per logged contract were created.

First run: hitch task T10 reserved Horse A0 and `opening-freight-wagon`; completed 10 actual in-game minutes later. Existing dispatch task T11 entered active at absolute simulation time 230134s, then completed/authorized dispatch at 233376s. Ethan boarded DriverSeat, and the coupled transport controller logged 82 waypoints from (14,47) toward the regional route. The system logged physical arrival at Millbrook location `ashford-millbrook_grain_co` with Ethan, horse and wagon active; it picked up the identified 10 lb lot on day 4. The team then returned along the gateway and town road path, dismounted Ethan, and logged the team idle at staging.

A later genuine reorder `flour-reorder-d20-n2` also negotiated the same quote and had a separate journey identity; the log records Millbrook arrival and pickup of its identified 10 lb lot on day 22, followed by a physical return to staging. The run also showed repeated carrier-capacity deferrals while A0 was unavailable; the same request was reconsidered rather than lost. These are strong state/log observations of two outbound pickups and returns, but the Game View was not camera-following the distant team, so detailed human visual confirmation of the actual world-space movement was not obtained.

**Not observed:** no log in this captured run confirmed delivery of either picked-up lot into Bakery custody or 125c freight settlement to Ethan. Millbrook's 48c flour goods purchase was settled separately; that is not freight payment. No duplicate settlement was observed. Therefore the complete freight transaction remains unverified past pickup/return.

Relevant sources: `SimulationDrivers` freight agreement, dispatch and pickup drivers; `CoupledTransportMovementController`; `OffMapJourneyAuthority`; freight settlement/receipt authorities.

## L. Bakery, fuel, and store

**PlayMode observed:** the live Bakery had an opening, finite 40-unit fuel endowment; it consumed one unit per completed batch. The fuel factor remained unset and no live Fuel Dealer intake/replenishment path was demonstrated. This run did not invent further fuel.

The first bake was blocked on 1 notion. Daily notions orders were issued through a General Store procurement service identity (`general_store_000`), with finite off-map supply and a 14-day lead. The day-15 receipt was explicitly transferred from Store inventory to Bakery bin through `Series2StoreToBakeryFeed`, charged 10c, and entered as a Bakery lot. This is a legitimate custody and payment path, not an arrival simply mislabeled as Bakery stock.

The Bakery completed six 24-loaf batches (144 loaves total) on days 15–19 and 27. The logs recorded flour/notions/fuel consumption, lot creation, and six wholesale transfers of 24 units each to the General Store at 2c per loaf (48c per lot), with cash settlement and Store shelf stock updates. Thus Bakery production and Bakery-to-Store wholesale were actually demonstrated; after day 27, no further successful batch was logged through day 174. Bakery planning repeatedly reported 24 units covered in the pipeline after a batch was stuck; later logs show it resumed and baked, so this was a delay/recovery concern rather than permanent blockage in this run. The store received bread but households did not purchase it.

## M. Save/load

Not tested. No save or restore operation was performed. Persistence cannot be inferred from save-owner wiring or source tests.

## N. Console, warnings, and runtime limits

- PlayMode Console at stop: 999+ informational entries, 379 warnings, 0 errors.
- Captured-session Editor log: approximately 51,221 lines / 6,250,056 bytes. It contains repeated daily diagnostics and stack traces; no exceptions or `Debug.LogError` calls were counted in the isolated run segment.
- Main repeated warning families: missed meals/starvation, shopping failures/no eligible sellers or closed seller, household domestic-labour task cannot fit, autonomous Bakery labour demand, and generated town negative-scale BoxCollider messages. Warnings reflect both real gameplay failures and authored/generated collider geometry warnings; they should not be blanket-suppressed.
- The Console uses collapse/group counts; the 379 warning counter is the live Console count at the observed stop, not a deduplicated root-cause count. Raw unfiltered Editor log is preserved externally.
- The project completed the run without a crash. There was no PlayMode error/exception observed.

## O. Next repair recommendations for Muse

1. **Household retail/consumption first:** reproduce why all 5 households resolve needs but record zero purchases after opening food; inspect `SimulationDrivers.DriveDailyNeeds`, household errand assignment/arrival, seller trading-day checks, and the live `GeneralStoreRuntimeManager` offer/stock path. Assert a paid household receipt and subsequent inventory decrement at consumption.
2. **Close freight custody and settlement:** after actual destination arrival and return, connect pickup cargo to Bakery receipt and settle the accepted 125c once through existing authorities. Preserve lots, shipment/agreement IDs, and exactly-once retry semantics. Add runtime assertions for the first and second haul.
3. **Make Bakery inputs sustainable:** the day-15 notions store-to-Bakery feed works, but no continuing Bakery fuel procurement from Fuel Dealer was observed; opening fuel is finite and the fuel conversion is unset. Wire a legitimate seller-funded replenishment using documented/configured quantities or a provisional explicitly labeled development calibration. Do not treat procurement plans as stock.
4. **Clarify/repair repeat bake continuation:** six batches completed, then no more through day 174 despite periodic demand and held flour. Trace the production-planning pipeline and task scheduling after day 27; preserve in-progress work while releasing terminal failures.
5. **Investigate 12c EditMode mismatch:** reconcile the expected 144c versus actual 156c household reserve purchase in `HouseholdReserveInventoryTests.PartialFillReattemptsOnlyTheRemainingDeficit`; retain the real goods+freight charge and correct the expectation or calculation only after establishing intended accounting.
6. **Directly inspect membership and local route entries in runtime:** current evidence is positive but registry cardinalities and business-to-location IDs were not printed. Add bounded one-time diagnostics or focused tests only if source owners need stronger proof; avoid repeating daily spam.
7. **Human visual follow-up:** camera-follow the coupled carrier at staging, outbound edge, regional destination, and return to verify rider placement and active object continuity. No visual/prefab changes were made in this import task.

## Confirmation matrix

| Area | Status |
|---|---|
| Bundle SHA, verify, and exact Muse HEAD | Verified |
| 17-commit integration and authored-file preservation | Verified |
| Unity C# compilation | Verified in actual Editor; 0 compile errors |
| Focused EditMode | 77 passed / 1 failed; listed above |
| Open Frontier active, no First Ledger popup in observed startup | PlayMode observed |
| Household registry/actors | Runtime actor activity observed; exact live registry read unverified |
| Town and regional JourneyModel registration | PlayMode diagnostics verified |
| Millbrook runtime identity and stock-backed purchase | PlayMode verified |
| Bakery production and wholesale | 6 batches / 144 loaves logged; 6 transfers settled |
| Household purchases and sustained meals | Failed in observed run: 0 household purchases, 4 meals eaten, 3,092 missed |
| Freight physical state progression | Logs show two pickups and returns; close visual confirmation absent |
| Freight Bakery delivery and 125c settlement | Not observed / unverified |
| Save/load | Not tested |
| Commit/push | Not done; branch remains 17 commits ahead of origin |

