# Land & Ledgers — Codex to Muse Overnight Handoff

Date: November 6, 2026
Studio: Deadscripts Interactive
Unity: 6000.4.2f1 HDRP
Repository: `KenB-O/Land-and-Ledgers-Scripts`

This handoff records the final local integration work and the actual fresh-world PlayMode evidence. It deliberately separates implemented source, Unity compilation, Editor Test Runner discovery, and runtime behavior. A long run with no crash is not proof that an economic loop works.

## A. Repository state and publication

- Repository root: `F:\Land & Ledgers`.
- Branch at start: `integration/muse-regional-economy-2026-11-05`.
- Start revision: `b10866df594e56207c25ea4d6edb201abce77f8c`, tracking `origin/integration/muse-regional-economy-2026-11-05`.
- The branch ancestry includes the 48 Muse commits through `8afd67887cb89fec30e2eb86575364f13e592ea7`, followed by the published Unity integration and repair commits already on this branch.
- Starting worktree was substantially dirty: 1,089 reported path entries at the post-test inventory, including authored prefabs, UI, fonts, music, ScriptableObjects, asset packs, machine-generated logs/XML, and source changes. No global cleanup, reset, stash, or revert was performed. Main Scene is local/ignored and was not rewritten.
- The implementation commit and handoff commit are to be recorded below once published. The exact final SHA and remote SHA are supplied by the accompanying owner report; this document is included at the branch tip.
- Only explicitly selected source, focused tests, development configuration, transport presentation references, and the two structured handoff documents are intended for the integration commits. The raw 4.36 MB PlayMode log remains local/outside Git to avoid growing the repository with generated runtime output.
- Kennedy's authored Horse and Wagon prefabs, Wagon prefab, UI Canvas, fonts, music asset/script changes, and unrelated asset packs remain outside the intended publication. `DevelopmentTownConfiguration.asset` and `OpeningTransportPresentationAssets.asset` are directly relevant to this runtime test and are included only as explicitly reviewed configuration/presentation changes. The Main Scene was not saved or edited.

## B. Completed source work in the current workstream

### Freight agreement selection and temporary capacity deferral

Files:

- `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightServiceAgreements.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Economy/FreightServiceAgreementTests.cs`

Changes:

- `FreightServiceAgreementBook.FindNextAwaitingPreparation` selects an accepted agreement only when it has a linked planned shipment, no preparation timestamp/task, no pending registration, and no load applied. This prevents a completed old agreement from permanently occupying the sole opening wagon's preparation selector.
- `SimulationDrivers.DriveOpeningHitchPreparation` can reuse the existing attached/valid A0 and opening wagon for a later shipment. It binds preparation to the new shipment identity instead of requiring the already-hitched horse to be hitched again or creating duplicate equipment.
- `FreightServiceAgreementBook.DeferTemporarily` returns a still-unaccepted request to Open after a temporary capacity refusal, retains the stable request/cargo/quote identity and reason, and clears stale travel/meeting task handles. It refuses to defer an accepted agreement or any request already represented by an agreement.
- `SimulationDrivers.HandleCompletedFreightMeetingTask`, capacity checks, and `IsTemporaryFreightCapacityRefusal` now defer a busy-carrier/wagon/incompatible-task case instead of terminally refusing a legitimate follow-up request. The task/request is not duplicated and no equipment or cargo is fabricated.
- Test added: `AgreementBook_TemporarilyDefersBusyCarrierWithoutLosingStableRequest` verifies stable request identity, cleared stale handles, and re-acceptance without duplication. Existing selection test covers moving past completed prior agreements.

### Physical return boundary

Files:

- `Assets/Scripts/Domains/Economy/Transport/CoupledTransportMovementController.cs`
- `Assets/Scripts/Domains/Economy/Transport/OpeningTransportPresentationController.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Economy/OffMapJourneySaveSeamTests.cs`
- `Assets/Scripts/Tests/Editor/Economy/CoupledTransportProgressTests.cs`

Changes:

- The coupled controller can execute a regional physical-return segment and then traverse the reversed authored town route.
- `OpeningTransportPresentationController.BeginCoupledPhysicalReturn` validates the active journey, confirmed destination, loaded shipment, wagon identity, driver identity, physical destination proximity, path and controller before starting. It reuses the actual Ethan/horses/wagon presentation and keeps identities together.
- `SimulationDrivers.ConfirmPhysicalFreightOriginArrival` and the physical freight return processor are the authoritative boundary for confirming arrival and allowing the existing delivery/settlement flow. The movement controller itself does not transfer custody or cash.
- Added save-seam coverage for physical arrival proof. The new focused tests were not executed in this Unity session.

### Household work-condition suppression and economic custody repairs already in the active dirty source set

Relevant files include:

- `Assets/Scripts/Domains/Population/DailyNeedsService.cs`
- `Assets/Scripts/Domains/Population/PopulationHealthState.cs`
- `Assets/Scripts/Domains/Population/PopulationManager.cs`
- `Assets/Scripts/Tests/Editor/Population/WorkCapacityFactorTests.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs`
- `Assets/Scripts/Domains/Economy/Core/SharedBusinessRuntimeManager.cs`
- `Assets/Scripts/Domains/Economy/Farming/Integration/BusinessFeedPurchase.cs`
- `Assets/Scripts/Domains/Economy/Farming/Integration/BusinessFeedCustodyLedger.cs`
- `Assets/Scripts/Domains/Economy/Logistics/LogisticsRuntimeManager.cs`
- `Assets/Scripts/Domains/Economy/Ordering/Review2BakeryToStoreWholesaleDriver.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreTradingPort.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreSupplierAdapter.cs`
- `Assets/Scripts/Domains/Economy/BreadDrivers/BakeryWholesaleDailyDriver.cs`

The development configuration's mortality-disabled value is propagated as `ConditionDebuffsSuppressed` to daily work capacity and work-availability calculations while underlying nutrition/health states remain recorded. The feed receipt seam now requires valid transaction identity, positive units and source-lot provenance before custody is held, preventing an empty/refused receipt from becoming a purchase receipt. Bakery wholesale receipts can preserve the Bakery lot identity when the General Store uses item-level retail inventory. Runtime proof for these paths remains incomplete because the final run had no active household members and did not reach the live trade/production callers.

### Opening Bakery setup

Files:

- `Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryOvenFuel.cs`
- `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`
- `Assets/Core/Population/DevelopmentTownConfiguration.asset`
- `Assets/Scripts/Tests/Editor/Economy/BakeryOvenFuelTests.cs`

The development scenario currently authors 40 units of finite Bakery opening fuel and a specific oven/toolkit. `ScenarioOpeningBakeryFuelEndowment.TryApply` makes the one-time endowment idempotent and identified; it is not a replenishment path. Startup logs in the final run confirmed the 40 units, oven chamber/kneading/proofing/peels, and `baker-hand-kit` were applied. No bake completed, so this is verified setup, not verified production. Fuel procurement/settlement still needs a legitimate continuing route.

## C. Unity compilation and tests

- **Unity compile:** actual Editor compilation succeeded after the last code/test edits (`Tundra build success`, 9 script items updated, 938 evaluated; zero C# compile errors). Existing source warnings remain. The subsequent PlayMode log contains a separate Burst native-library `DllNotFoundException` (error code 1392) from `Library\BurstCache\JIT\18c4494761fce75a87f6f464a7b6d09f.dll`; this is not a C# compile error.
- **Test Runner discovery:** Unity's actual Test Runner window discovered 5,178 EditMode tests in `Assembly-CSharp-Editor.dll`.
- **Tests executed:** zero. Focused test search/selection in the detached Test Runner window was not operable through the available UI bridge; no full suite or second Unity process was started. New tests are source-present but unverified in the Unity Test Runner.
- No claims of passing Unity EditMode or PlayMode tests are made. Earlier standalone source harness results belong to Muse's source harness and are not repeated here as Unity results.

## D. Final PlayMode and circular economy

See [`CODEX_FINAL_PLAYMODE_FINDINGS_2026-11-06.md`](CODEX_FINAL_PLAYMODE_FINDINGS_2026-11-06.md) and the raw log link above for counts, exact startup evidence, and diagnostic classification.

Final run: fresh New Game, Open Frontier, world seed 1537408365, 100x, day 1 to day 285 (March 1 to December 11, 1897), about five minutes including Editor domain reload. The sandbox became active directly without the First Ledger mission acknowledgement. The run did not crash.

### Actual outcome

- Startup: nine residents, five households, four businesses. General Store opening inventory was configured as bread 16 loaves, flour 8 ten-pound sacks, potatoes 7 sacks, sugar 6 lb, coffee 3 lb, tea 4 lb, salt 8 small bags. Bakery finite opening fuel 40 units; oven and baker kit installed; 24-unit/day at 2c fallback wholesale account booked; notions supplier definition registered with 30 finite units and 14-day transit estimate.
- Households: from day 1, all H1–H5 lacked active membership in the household membership authority and no acting adult could be resolved. Each daily summary retained unmet need/errand failures. The needs driver therefore did not perform its normal lot-backed consumption or embodied purchase path. No reliable meal-eaten/purchase count was available because the responsible actor prerequisite failed first.
- Bakery: no logged batch completion, bread output, or wholesale transfer. The 40-unit opening fuel and equipment were present; this is not proof they were consumed or that the Bakery could continue operating.
- General Store: initial stock was seeded; no Bakery bread was received and no household retail transaction was observed. The run cannot demonstrate restocking or sustained inventory.
- Fuel Dealer: the startup log confirms a separate business and opening cash, but no live Bakery fuel purchase, supplier receipt, or replenishment occurred.
- Freight: the daily order driver logged once on day 1 that Millbrook runtime was unresolved. The daily warning also said no journey locations were registered for all 285 days. There was no contract, task, pickup, physical trip, return, delivery or 125c settlement in this run.
- Immortality: mortality disabled and 15 old-age deaths suppressed, including P38 on day 285. No completed death/estate event appeared in the captured session. The final PlayMode run did not establish condition-immune productivity because no household actor/work chain succeeded.
- Console: Unity Console counters at stop showed 27 logs, 335 warnings, one error. Raw log has 4,541 repeated music-player recovery warnings, 285 day-by-day missing route registry warnings, 285 household summary events (each listing five households), 15 mortality suppression events, and one Burst compilation error. Routine music recovery is not functioning cleanly; repeated warnings should be corrected at the audio reference/loop/clip cause rather than muted.

## E. Incomplete systems and first causal failures

### Immediate blockers from this exact run

1. **Household membership registration (critical):** `PopulationGenerator.GenerateDevelopmentTown` fills each `HouseholdState.memberIds`; `PopulationManager.CompletePopulationSnapshot` calls `RebuildMembershipRegistry`; `DailyNeedsService.FindActingAdult` reads `HouseholdMembershipRegistry.GetActiveMembers` when provided. Runtime has generated households but the authoritative active-member view is empty. Inspect registration IDs and error diagnostics around `RebuildMembershipRegistry`/`HouseholdMembershipRegistry.Register` rather than bypassing the sole membership authority with stale legacy fields. First-day failure prevents household buying/eating and all downstream food-demand evidence.
2. **Journey location registration (critical for shopping and freight):** `SimulationDrivers.OnDayChanged` reports no locations registered. Connect the configured JourneyModel/registry initialization before daily errands and freight request creation; preserve stable IDs and idempotent route registration.
3. **Millbrook runtime resolution (critical for freight):** `SimulationDrivers.DriveMillbrookFreightNegotiation` runs but cannot resolve Millbrook despite the startup log stating a Millbrook off-map aid table was loaded. Compare the object wired to `FreightProductionDriver`/supplier lookup with the adapter initialized in `FirstLedgerSliceBootstrapper.ConfigureMillbrookAidTable`; likely caller-order or identity mismatch, but verify actual IDs.
4. **Audio restart spam:** `LLMainMusicPlayer.Update` emits 4,541 warnings in ~5 minutes. Investigate `MainMusicPlayer` GameObject, AudioSource, clip, loop and DSP scheduling state; avoid per-frame recovery warnings.
5. **Burst cache/runtime error:** one native DLL load failure (error 1392) is present. Do not claim zero Unity errors. Diagnose the cache/library integrity issue in a controlled way that does not delete user caches blindly.

### Existing limitations not resolved by the final run

- Bakery production is currently configured with finite opening fuel but no complete verified recurring fuel purchase/receipt/consumption cycle. Glen's labour is condition-aware and suppression-aware in source, but actual time booking and production task completion still require live verification.
- Notions source registration does not prove Bakery-owned receipt. Track order buyer/recipient and ensure arrived lot custody reaches Bakery runtime before production.
- Failed/stuck batches must be distinguished from active input procurement; ensure failed batches cease counting as cover while in-progress batches remain protected against duplicate output.
- Bakery-to-Store needs identified lot transfer and balanced B2B settlement, followed by item-level store receipt and household purchase/consumption. Code hooks exist, but this final runtime did not reach them.
- General Store opening inventory is finite. Replenishment must be a real purchase/order/receipt, not recurring stock creation. Existing regional supplier/weekly reorder paths should share pending-order state.
- Repeat freight: source changes now reuse already attached equipment and temporarily defer a busy carrier. Final fresh run failed before any request because supplier runtime was unresolved. A complete second-haul proof is still needed.
- Route identities: `OffMapJourneyAuthority` can preserve route-based journey state, and the coupled controller has a physical route-return implementation. Current `JourneyModel` has no locations registered in PlayMode, so physical regional travel is unreachable.
- Owner has directed that off-map people/horse/wagon GameObjects stay active; renderers may be hidden outside the view buffer. Do not use generic `SetActive(false)`, destruction, or timer-only arrival to solve scale/precision.
- Authored `Assets/Core/Travel/Off Map Town.prefab` and `Wagon Cargo Horse.prefab` exist. The specifically named `Auth Map Town` was not found under this directory in this repo state. Inspect assets before assigning them as destinations. Physical town triggers, stable business-building mapping, and actual Rigidbody/trigger arrival were not verified.
- Open Frontier is active; First Ledger remains present in scenario source. The log includes one `GHOST-DEF-006` player-household diagnostic. Confirm campaign selectability and that no objective HUD/popup appears in a human visual check after the Main Scene references are reviewed.
- Save/load of active request, deferred meeting, repeated freight, physical route return, cargo and business transactions was not executed in this final session.

## F. Population, condition and labour authorities

- `PopulationManager` owns the `PopulationState`, `HouseholdMembershipRegistry`, kinship and demographic lifecycle stack, health settings, save state and daily budget updates.
- `HouseholdMembershipRegistry` is the sole active household membership truth; `PersonState.householdId` and `HouseholdState.memberIds` are mirrors/legacy inputs. Do not patch errands to read a second competing membership authority without fixing registration.
- `DailyNeedsService.WorkCapacityFactor(PersonState, conditionDebuffsSuppressed)` returns 1 when suppression is enabled; otherwise multiplies nutrition and health labor capacity. `PopulationManager.ConditionDebuffsSuppressed` derives from the development mortality setting. Related `PopulationHealthEvaluator` calls in the General Store/shared business manager now pass the same mode.
- The health/nutrition measurements still exist and warnings should remain. Suppression is not infinite time and must not bypass schedule, rest, occupancy, travel, inventory or costs.
- The final runtime showed mortality suppression active; it did not show successful workers under adverse conditions because membership resolution failed before those jobs.
- Domestic labour, meal scheduling, person-time budgets, business task start windows, migration and schedule data are separate legitimate constraints. Continue to use `TaskAuthority`, `WorkTimeBudgetStore`, `PersonScheduleTracker`, and existing scheduling drivers.

## G. Business, autonomy, and implementation tiers

The checked-in `Assets/Scripts/Docs/Pass12_Business_Completeness_Matrix.md` records 29 business enum members: 19 LIVE through one of the shared/dedicated mechanisms and 10 PARTIAL. Its key warning is architectural: there are four live mechanisms (Generic method path, shared archetype path, dedicated drivers, settlement/payroll-only path), not 29 equally complete independent systems. Business expansion should deepen connected chains rather than add one-off runtime shells.

Useful working source references:

- `SimulationDrivers.DriveGenericProduction` / `GenericProductionRuntimeRegistry` / `ProductionPathRegistry` — generic process execution and exclusivity.
- `SharedBusinessRuntimeManager.ResolveWeeklySharedOperations` / `ResolveArchetypeRoutedWeeklyOperations` — typed archetypes, wholesale/local market and settlement.
- `GeneralStoreRuntimeManager` — live retail, shopping port, payroll/reorder.
- `BakeryWholesaleDailyDriver`, `BakeryOvenFuel`, `BakeryNotionsDailySupply`, `BakeryFlourLocalPurchase`, `Review2BakeryToStoreWholesaleDriver` — bakery purchase/inputs/production/transfer seams.
- `FreightServiceAgreements`, `FreightProductionDriver`, `LogisticsRuntimeManager`, `OffMapJourneyAuthority` — contract, shipment, journey, custody and settlement boundaries.
- `BusinessFeedPurchase`, `BusinessFeedCustodyLedger` — horse-feed paid purchase and retryable custody.

Do not treat payroll/settlement presence as proof of production. The matrix says some dedicated `*Runtime` classes are unwired while the business still operates through an archetype; it specifically flags Doctor, Hotel, Newspaper, Restaurant, Tannery, PostOffice, GrainElevator, Bank and Lawyer as partial mechanisms. Avoid duplicating inventory truth between `BusinessRuntimeState`, generic positions, and domain runtime inventories.

## H. Transport and regional world

- `JourneyModel.RegisterLocation` is the route authority. In the final runtime no location was registered, so neither household errands nor a real regional supplier trip could start.
- `LogisticsRuntimeManager` resolves shipment carrier, wagon, horse and driver identity. Current work corrected a person EntityId format mismatch so the carrier's actual Person can bind rather than produce a riderless pair.
- `OpeningTransportPresentationController` prepares horse and wagon as siblings under a neutral transport group, boards the resolved assigned person at `DriverSeat`, and delegates movement to `CoupledTransportMovementController`.
- `CoupledTransportMovementController` moves the coupled pair by actual positions and can require identity-bound destination/origin confirmation. It must not be interpreted as the transaction authority.
- Prior PlayMode (before the final fresh seed) observed a Millbrook pickup, physical return and 125c delivery settlement, then a second completed load. The final run's supplier resolver failed, so this session's final runtime did not confirm it.
- Off-map active GameObjects and non-rendered region representation remain a larger spatial problem. Owner requires active entities and eventually real physical towns/building triggers; current `OffMapJourneyAuthority` records and timer/day progression alone do not prove physical arrival.
- The project presently contains `Assets/Core/Travel/Off Map Town.prefab` and `Assets/Core/Travel/Wagon Cargo Horse.prefab`; no `Auth Map Town` asset by that exact name was found. Do not replace either Kennedy-authored asset or assume one has the requested ten trigger buildings without inspection.

## I. Save/load and transaction integrity

Authorities exist for population, household and business ledgers, business runtime inventory, shipment states, off-map journey records, Bakery runtime, retail lots, and save DTOs. This final run did not save/restore any state. The recent source edits include save-seam coverage for journey arrival proof but that test was not run. Required live checks: a deferred freight meeting, active outbound journey, supplier cargo receipt, physical return, household lot receipt, and exactly-once settlement across save/restore. Verify no duplicate accounts/endowments on Continue and no duplicate stock or cash.

## J. Prioritized defect register

### P0 — No household members visible to the membership authority

- **Repro:** Fresh Open Frontier, world seed 1537408365, day 1 onward.
- **Expected:** Each authored household resolves its configured resident members; an eligible adult is chosen for needs/shopping.
- **Actual:** H1–H5 all say “has no active members”; `DailyNeedsService` says no acting adult; no needs/purchases are executed.
- **Code:** `PopulationGenerator.GenerateDevelopmentTown`; `PopulationManager.GeneratePopulationSnapshot`, `CompletePopulationSnapshot`, `RebuildMembershipRegistry`; `HouseholdMembershipRegistry.Register/GetActiveMembers`; `DailyNeedsService.FindActingAdult`.
- **Best-supported cause:** household `memberIds` input or generated active registry is empty/invalid at runtime despite the generator summary. Verify actual membership ids after `CompletePopulationSnapshot`, including registry diagnostics. This is inferred from the runtime and code contract, not a proven single bad line.
- **Fix/test:** instrument/report generated member IDs and membership registry counts at the boundary; fix registration/ID allocation only through `HouseholdMembershipRegistry`; test all five households have correct members and one adult, then real household shopping and meal decrements.

### P0 — Journey locations not registered

- **Repro:** Same fresh run; warning on every day 1–285.
- **Actual:** no route location registry; errands blocked; freight movement cannot establish origin/destination.
- **Code:** `SimulationDrivers.OnDayChanged` around line 3483; `JourneyModel.RegisterLocation`; startup registration owner in world/scenario/bootstrap.
- **Fix/test:** register scenario-defined local and regional locations once before daily drivers, idempotently, preserving existing IDs; verify local store routes and Millbrook 50.32-mile wagon route after startup/Continue.

### P0 — Freight request cannot resolve Millbrook runtime

- **Repro:** Same fresh run, day 1.
- **Actual evidence:** “Daily request driver is connected … cannot evaluate the opening order because Millbrook runtime is unresolved.” Despite startup saying OffMapAid loaded Millbrook Grain Co. with flour 230 lb.
- **Code:** `SimulationDrivers.DriveMillbrookFreightNegotiation` and blocker resolver; `FirstLedgerSliceBootstrapper.ConfigureMillbrookAidTable`; `FreightProductionDriver`; supplier adapter registration.
- **Fix/test:** verify stable supplier ID, runtime instance ownership, initialization order and the exact object lookup used by opening request; prove one agreement references the loaded Millbrook lot and one shipment.

### P1 — Music player repeats warning on each Update

- **Repro:** Fresh Main Scene PlayMode.
- **Actual:** 4,541 `[LLMainMusicPlayer] Active source stopped unexpectedly. Attempting recovery.` lines in about five minutes.
- **Code:** `Assets/Scripts/Presentation/Audio/LLMainMusicPlayer.cs:156`, authored MainMusicPlayer object/audio source/clip.
- **Fix/test:** determine why the active source stops; repair clip-loop/DSP scheduling setup; only log a warning on a genuine state transition, while keeping the first real failure visible. Verify music continuity and no repeated warnings.

### P1 — Burst native cache load failure

- **Repro:** Editor PlayMode startup; captured once.
- **Evidence:** native library load error 1392 from `Library/BurstCache/JIT/...dll`, `DllNotFoundException`, `AggregateException`.
- **Fix/test:** diagnose Editor/Burst cache corruption/version/build environment without blindly deleting `Library`; run a small Burst-enabled validation and confirm no runtime error.

### P1 — Household food and Bakery chain not runtime exercised

- **Repro:** Same run, no membership actors.
- **Actual:** opening stocks and Bakery setup logged, but zero actual baked/wholesale/retail/consumption milestones in the final session.
- **Code:** `BakeryWholesaleDailyDriver`; `BakeryOvenFuel`; `BakeryNotionsDailySupply`; `BakeryFlourLocalPurchase`; `Review2BakeryToStoreWholesaleDriver`; `GeneralStoreRuntimeManager`; `HouseholdShoppingLoop`; `DailyNeedsService`.
- **Fix/test:** first resolve household membership and routes. Then validate actual lots/cash/labour/fuel/input receipts and one repeated bake–B2B–retail–consumption cycle. Do not claim startup endowment as sustainability.

### P1 — No final-run freight or second-run verification

- **Repro:** Millbrook resolver failure short-circuits request generation.
- **Actual:** zero trip/arrival/payment in final run; current deferral patch not exercised there.
- **Code:** `DriveMillbrookFreightNegotiation`, `HandleCompletedFreightMeetingTask`, `DeferTemporarily`, preparation selector, `TryDepartMillbrookShipment`, coupled controller and arrival confirmations.
- **Fix/test:** after supplier and JourneyModel initialization work, verify a busy-carrier request defers and later reuses stable IDs; complete first/second hauls with physically confirmed pickup/return and exactly-once payer/carrier settlement. Preserve the accepted 125c quote.

### P2 — Save/load integration remains unverified

- **Code:** `SaveLoadManager`, `SaveDtos`, `PopulationManager` membership DTO, Bakery runtime state, `OffMapJourneyAuthority`, shipment/cargo states.
- **Test:** save during deferral and active trip; restore twice; verify no duplicate members/orders/lots/payments and visible rider/team identity returns correctly.

## K. Development recommendations for Muse

### Category 1 — Immediate blockers

1. **Repair fresh-world household membership.** Verify resident Person IDs in `PopulationGenerator.GenerateDevelopmentTown`, the membership `EntityId` allocator, `PopulationManager.RebuildMembershipRegistry`, and registry diagnostics. Expected result: 9 residents, 5 nonempty households, one active membership per intended resident, active adult resolution. Test actual shopping and consumption, not only counts.
2. **Register local JourneyModel locations before day drivers.** Use the scenario's existing `JourneyModel` and startup authority. Expected: actual store/vendor destinations, valid road access, no daily “No journey locations” warnings, stable registration after Continue.
3. **Fix Millbrook runtime binding.** Compare the logged loaded Millbrook supplier id and lot authority with `FreightProductionDriver`'s actual participant resolution. Ensure initialization order; no duplicate supplier/inventory authority. Expected: one request with real Millbrook lot and correct stable entity identity.
4. **Fix repeated music recovery.** Inspect actual Main Scene component references and DSP/clip state, not merely the warning line. Expected: playback proceeds normally, and warnings only occur for genuine transitions/failures.
5. **Investigate the single Burst native load failure.** Validate environment/cache compatibility safely; keep all local cache work non-destructive.

### Category 2 — High-value integration

1. **Run the full live Bakery chain once routes/memberships work.** Validate actual buyer cash, flour/notions/fuel lot custody, Glen's availability, actual timed work, per-batch ingredient/fuel removal, bread lot output, B2B settlement, retail SKU receipt, household purchase and meal lot decrement. Use `BakeryWholesaleDailyDriver` and `Review2BakeryToStoreWholesaleDriver`; maintain one authority per inventory.
2. **Complete ongoing fuel procurement.** `BakeryOvenFuel` and authored opening endowment exist; no verified recurring Fuel Dealer purchase/receipt exists. Make purchase require real stock/cash and receipt capacity, with no pending fuel treated as on-hand.
3. **Validate transient freight deferral and repeat order gates.** Keep request state Open when the only carrier is busy; keep stable request/cargo identity, no duplicated offer/contract/task, then recontact after availability. Track completed/fulfilled prior agreements without losing audit history.
4. **Complete physical regional travel with active objects.** Use `CoupledTransportMovementController` and `OffMapJourneyAuthority`, stable route coordinates, active entity transforms, and exact physical arrival confirmation. Register actual destinations before a trip. Do not turn day timers into physical arrivals or hide/deactivate entities as a generic transition.
5. **Prove serialization on live in-flight state.** Household membership and active freight transitions are high-risk. Save/restore should preserve IDs, reservations, cargo ownership, request deferral, inventory lots and money.

### Category 3 — Major development expansion

1. **Resolve the roadmap's connected business tiers from current documentation, not invented labels.** `Pass5_TierOne_Implementation_Plan.md` prioritizes P0 authority/conservation; P1 scenario presets, demographics, generation order, single production path and coherent opening economy; P2 household coordination, hardship/credit, entrepreneurship and duplicate authority cleanup. Use this as the implementation ordering, and reconcile with current Canon/roadmap before adding industries.
2. **Build connected business supply chains using existing mechanisms.** Deepen partial business types and supplier/producer/retail demand where their material inputs and labor are known. Current Pass XII matrix classifies mechanisms and should prevent a proliferation of placeholder runtime classes.
3. **Improve NPC autonomy through existing shared authorities.** Independent supplier comparison, negotiated terms, constrained cash decisions, hiring and expansion require explicit task/knowledge/ledger interactions. Avoid a second NPC-only economy when player-facing parity is a design requirement.
4. **Personal physical carrying capacity.** Inventory searches should begin with item mass/volume, business/household lot custody, containers, equipment, transport load and current logistics. A future person-carried system should move real identified lots between household, person, workplace, bag/lunch pail and wagon; capacity should reflect item size/weight and person capability/equipment, without automatic sex-based penalties. Do not implement during the immediate economy repair.
5. **Regional coordinate and authored destination system.** Inspect existing `JourneyModel`, regional registry, `OffMapJourneyAuthority`, and `Off Map Town.prefab`; design stable world/region coordinates, per-business building assignment, physical trigger confirmation and origin return. Coordinate precision and simulation cost at 50/100 miles need deliberate treatment; do not build an unbounded NavMesh or shorten distances while labeling them unchanged.
6. **Finance/property/contract cohesion.** Map existing contract/obligation/bookkeeping, lender/business ownership and property/lease flows before connecting credit or related-party transactions. The Pass XII deferred-decision file keeps lending/foreclosure/related-party rules open; honor those holds.
7. **Testing reliability.** Keep compact fixture-backed tests for authority transitions, high-speed scheduling, save/load repeatability, invalid custody and no-duplicate settlements. The Unity Editor discovered 5,178 EditMode tests in this run but test selection via detached UI was inaccessible; fix the supported focused test workflow rather than starting all tests after each code edit.

## L. Owner decisions and constraints

- Current owner decision: development immortality suppresses death and all negative personal-condition performance debuffs; preserve visible condition measurements and hunger warnings. It does not grant extra time or remove schedules, rest or costs.
- Open Frontier is the active freeform sandbox; First Ledger remains available as a separate scenario. Do not allow campaign acknowledgements/objectives to block sandbox startup.
- Off-map physical entities remain active; only their renderers may be disabled beyond the view/safety buffer. Arrival must be physical and destination-specific before cargo or cash authority runs.
- Do not invent permanent Canon values, package sizes, economic prices, fuel conversions or guaranteed profit. Any development-only calibration must be explicit, finite, reversible, and labeled provisional. Existing D-1/D-2/D-5/D-6/D-7 decisions and the December 2026 owner directions should be read before changing inputs.
- The 24-loaf/day at 2c wholesale fallback is explicitly demand-derived development calibration. The 40-unit Bakery fuel is a finite scenario endowment, not fuel production.
- Preserve `DevelopmentTownConfiguration` GUID, current scene references, prefab GUIDs/transforms/materials and `.meta` files.

## M. Workflow observations

The workflow remained productive when source changes were followed by actual Editor compilation and a bounded live run. The main practical obstacle was focused test selection: Test Runner did discover 5,178 tests, but the detached window's search field did not accept text through the UI bridge. Do not launch a competing Editor or all tests as a workaround. A small documented queryable runner/API for focused EditMode tests would materially improve verification. Preserve the current Muse→Codex integration workflow, separate Roslyn evidence from Unity evidence, and maintain raw runtime captures when debugging repeated warnings.

## Final verification checklist for Muse

1. Start with current published branch tip and inspect the latest report/log; do not assume the final Open Frontier run proved the economy.
2. Confirm compilation and run focused tests for membership rebuilding, `FreightServiceAgreementBook.DeferTemporarily`, next-preparation selection, feed receipt identity, wholesale lot receipt, physical arrival proof and condition-debuff suppression.
3. Fresh Open Frontier: verify nine person IDs map to five household registries before the first daily needs event; resolve routes before day 1; verify stores can actually be reached.
4. Confirm actual flour/notions/fuel lots enter the Bakery authority, a real worker gets a time reservation, a batch finishes and consumes inputs once.
5. Follow the new identified bread lot through B2B cash/inventory settlement, General Store offer, a household purchase and a recorded meal decrement.
6. Run a real accepted Millbrook freight contract, physical supplier trigger, cargo custody receipt, return trigger and exactly-once 125c settlement. Then verify a second request defers if carrier busy and eventually completes without creating a duplicate.
7. Save/restore during a pending request and loaded journey; verify all relevant stable IDs, active physical entities, ownership, cash and lots.
8. Keep Unity in Main Scene Edit Mode at end and report raw counts and causal states, not only task names.
