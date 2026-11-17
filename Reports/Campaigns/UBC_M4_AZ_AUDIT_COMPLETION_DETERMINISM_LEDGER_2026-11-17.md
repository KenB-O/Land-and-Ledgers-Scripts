# UBC M4 — A-Z Audit, Fourth Slice: Construction Completion and Occupancy, Save/Load Determinism, Follow-up Ledger

Date: 2026-11-17. Base: integration branch `muse/universal-business-construction-2026-11-17` at `679a2b7b` (Merge B1 NPC autonomy and 8-week close rule). Worktree `/home/hatch/ll-ubc-m4`. Harness baseline in this tree: **5107 passed, 0 failed, 5107 total** (log `/home/hatch/ll-ubc-m4-run0.log`).

M1 covered population, household, business formation, retail, tenancy, boarding, housing. M2 covered manufacturing, finance, contracts, construction completion, occupancy. M3 covered trade/import, animal services, the NPC decision/market layer. This slice audits the three areas left open by the brief: (1) construction completion and occupancy — the D-11 three clocks, the store-panel routing, and the completion → registration → occupancy → equipment-install chain; (2) save/load, time, and determinism beyond L1's blacksmith slice — clock/speed presets, deterministic seeds, DTO round-trips for the new campaign packages, opening-resources-once; (3) the follow-up ledger — every finding from M1, M2, M3 and the lane follow-ups, reconciled to closed or open with one evidence check each.

Method, as in M1/M2/M3: every finding gives file+method, caller/callee, expected vs observed, a reproduction, and a root cause. "Zero production callers" claims were verified with fully-qualified-aware greps. Nothing here rests on a planning document.

## Headline

Two things are genuinely wired that the brief worried about: the store panel really does route every build action through the weekly queue (D-13 holds, no live caller uses the instant path), and opening resources really do apply once (the development-town cash endowment is NewGame-gated and marker-gated). But the weekly queue itself is the top break in this slice: it completes shells by elapsed weeks alone — a fixed building timer with no worker, no work packages, and no labour commitment — and the shells it completes never register in the HousingAuthority, so no household can ever occupy a player-built shell through the housing system. Both gaps bottom out on the same missing piece, F-D11-2 (no definition→design mapping, no sleeping capacity on a shell), which is still an owner decision. The one repair in this slice is small and in workstream L: open housing-search needs were still lost on save/load (F-M1-7), and the save DTO, hub, and load path now carry them (M4-R1).

## Findings

### F-M4-1 — The player weekly queue completes shells by elapsed weeks alone: a fixed building timer, no real work (TOP BREAK, recorded not repaired)

- **Files:** `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` (`ResolveWeeklyConstructionQueue` :2055, `TryStartQueuedConstructionProject` :11661, `TryCompleteQueuedShellConstruction` :11850), `Assets/Scripts/Domains/World/TownWorldController.cs` (`TryConstructBuildingShell` :1419).
- **Caller/callee:** the store panel calls the `TryQueue*` entry points (panel :5174, :5289, :5321, :5329); the queue resolves weekly from `OnWeekChanged` (`AcquisitionMarketManager.cs:7728-7729`); the queue never calls the construction executor (`ConstructionProjectExecutor` / `NpcHomeConstruction`), never calls `CreateProject`, `CommitLabor`, `WorkCommittedLabor`, or `AdvancePackage`.
- **Expected:** per workstream H, a building never completes by planned date alone — duration arises from work packages, material quantities, person-hours, workers, skill, equipment, dependencies; per workstream E, no single fixed building timer.
- **Observed:** `ResolveWeeklyConstructionQueue` advances each started project by exactly one week per weekly tick (`project.weeksProgressed += 1`, gated only by a builder-capacity slot count) and completes it when `weeksProgressed >= weeksRequired`, where `weeksRequired` comes from `GetTypicalBuildWeeksForDefinition` (authored shell ranges 1–2 / 2–4 weeks). Completion calls `townWorld.TryConstructBuildingShell`, which places a finished shell. The only labour involved is a quote resource: `plannedLaborUnits` is consumed as an input quantity at start (`TryStartQueuedConstructionProject` spends cash and consumes inputs, then marks the project InProgress). No person works any minute; no work package exists; no skill or equipment is involved. The weekly summary text "Worked this week" is narrative — no work is performed by anyone.
- **Reproduction:** queue a shell on an owned plot, advance the clock the authored number of weeks with no worker assigned anywhere, and watch the shell complete. Compare with the NPC path (`NpcConstructionDriver.cs:907` → executor `CompleteProject`, `:946` → `OccupyCompletedHome`), which works packages over days with real committed labour.
- **Root cause:** D-11 unified the player paths on the weekly queue, but the queue itself was never converted to real work — it is the old timer model with a queue wrapper. The conversion is blocked on F-D11-2: the queue builds shells from a `BuildingDefinition`, the executor builds homes from a design (`BuildingDesignCatalog`); there is no definition→design mapping, so the queue cannot hand a shell to the executor at all.
- **Disposition:** recorded, not repaired in this slice. The repair is a multi-lane job: (a) the owner decision on F-D11-2 (author sleeping capacity, rule a mapping, rule a default, or rule shells are never inhabited), then (b) a queue-to-executor bridge that starts a real executor project from a queued shell and drives it with committed labour. Inventing the mapping here would be silently deciding F-D11-2.

### F-M4-2 — Queue-completed shells never register in the HousingAuthority: no household can occupy a player-built shell (recorded, not repaired)

- **Files:** `Assets/Scripts/Domains/World/TownWorldController.cs` (`TryConstructBuildingShell` :1419), `Assets/Scripts/Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs` (`CompleteProject` :908, `RegisterBuilding` :996), `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` (`TryCompleteQueuedShellConstruction` :11850).
- **Caller/callee:** `TryConstructBuildingShell` places a `PlacedBuilding`, stamps the footprint, claims it, creates anchors, adds it to the town's building list, and marks the plot. It never calls `HousingAuthority.RegisterBuilding` and never defines accommodation spaces. The only production callers of `RegisterBuilding` are the executor's `CompleteProject` (`NpcHomeConstruction.cs:996`, spaces from design `SpaceSpecs`) and the boarding bridge (`BoardingLodgingBridge.cs:81`). The queue completion path (:11875) calls `TryConstructBuildingShell` and nothing else.
- **Expected:** a completed building is registered in the housing authority with its spaces, so occupancy, sleeping-place accounting, and the absence sweep all see it — the same transition the NPC path performs.
- **Observed:** a player-built shell exists only as a plot/building record. `OccupyCompletedHome` requires a completed `ConstructionProject`, which the queue never creates, so no household can ever be placed in a player-built shell through the housing system. The completion message itself concedes the gap: "Operator assignment and business activation remain pending."
- **Reproduction:** complete a shell through the weekly queue, then list `HousingAuthority` buildings — the new shell is absent; `OccupyCompletedHome` cannot target it.
- **Root cause:** same missing piece as F-M4-1. A shell has no design `SpaceSpecs` (F-D11-2), so there is nothing to register as spaces. Registering the shell with zero spaces would be misleading — a building the authority reports as uninhabitable — and would not let anyone sleep in it.
- **Disposition:** recorded, not repaired. A zero-space registration is not an honest repair; the real fix rides on the F-D11-2 decision and the F-M4-1 bridge.

### F-M4-3 — D-13 verified: the store panel routes through the weekly queue; the instant path is retired but retained (verified, no finding)

- **Files:** `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/Presentation/GeneralStorePanelController.cs` (:5174, :5289, :5321, :5329), `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` (queue entries :1790, :1842, :1858, :1891, :1959; retained instant wrappers :5088, :5104).
- **Verified:** all four panel build actions (house shell, business shell, household upgrade, business fit-out) call the `TryQueue*` entry points, which create a `ConstructionProjectState` with progress `Queued` and add it to `activeConstructionProjects` — no immediate build. The old instant wrappers (`TryConstructHouseOnOwnedPlot`, `TryConstructBusinessShellOnOwnedPlot`) carry explicit "UNCALLED IN PRODUCTION" comments and are retained only because `ConstructionInputSupportTests` still exercises the immediate path directly. A repo-wide grep finds no production caller of either wrapper or of `TryConstructResolvedShellOnOwnedPlot` outside the wrappers themselves.
- **Note:** retired-but-retained is the honest state — the wrappers still work and are one re-wire away from being live again. Do not re-wire a production caller to them (their own comments say so).

### F-M4-4 — Completion handoff still published but never consumed; lodger path still has no production driver (reconfirmed, not repaired)

- **Files:** `Assets/Scripts/Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs` (`PublishCompletionHandoff` :1208, `CompletionHandoffs` :249, `HandoffForProject` :260).
- **Verified:** `HandoffForProject` has zero production callers; the only reader of `completionHandoffs` is the lookup method itself. The named consumer (B's installation contract) does not exist as a type — the closest live shape is `GenericBusinessEconomy` equipment-installation state, which no completion consumer drives. `OccupyCompletedHomeAsLodger` (:1094) has zero production callers outside its own file. This reconfirms F-M2-7 and the carried D-10 item; nothing changed since M2.

### F-M4-5 — F-D11-2 reconfirmed: a shell carries no sleeping capacity and no definition→design mapping exists (reconfirmed, owner decision)

- **Files:** `Assets/Scripts/Domains/World/Buildings/BuildingDefinition.cs` (`ResidentHouseholdCapacity` :142, `CanHostHouseholds` :160).
- **Verified:** a `BuildingDefinition` carries household slots (`ResidentHouseholdCapacity`, 1 for residential, 0 otherwise) but no per-person sleeping capacity. The executor's spaces and sleeping capacity come from design `SpaceSpecs` at completion (`NpcHomeConstruction.cs:996`). A repo-wide grep for any definition→design mapping returns nothing. All four owner options (a) author sleeping capacity, (b) rule a mapping, (c) rule a default, (d) rule shells are never inhabited — remain open; nothing in this slice chooses among them.

### F-M4-6 — Clock and speed presets verified correct; 25x survives only as legacy surface (verified, no finding)

- **Files:** `Assets/Scripts/Core/Time/SimulationTimeSettings.cs` (`realSecondsPerGameDayAt1x = 1440f` :116, `speed250xMultiplier = 250f` :156), `Assets/Scripts/Core/Time/SimulationSpeed.cs` (`Speed250x = 102` :19), `Assets/Scripts/Presentation/UI/LandLedgersHUDController.cs` (speed list :20-22, buttons :555-562).
- **Verified:** day length is 1440 real seconds at 1x, per Kennedy's ruling. The player-facing speed control offers RealTime, Observe, 1x, 2x, 5x, 10x, 50x, 100x, 250x — no 25x button. The 25x ruling (25x replaced by 250x) IS implemented in the player UI. What remains of 25x is legacy surface only: the `Speed25x = 25` enum value (kept deliberately — the file comment says "Keep existing serialized speed values unchanged"), the `speed25xMultiplier` settings field, a testing-only keyboard shortcut (`HandleTestingSpeedShortcuts`, gated on `enableTestingSpeedShortcuts`), a dev validation-window button, and a night-exposure preset. None is player-facing. No change made.

### F-M4-7 — Determinism: production randomness is explicitly seeded; two `GetHashCode` seeds are a harness-only caveat (verified, note only)

- **Verified:** a repo-wide grep finds no `UnityEngine.Random` use in production. Every production `System.Random` is seeded from stable inputs: day index, stable hashes, definition seeds, the town settings seed (`TownGenerationSettings.seed`, default 1886, used for postal registration at `SimulationDrivers.cs:6934`). `SimpleDueEventScheduler` documents deterministic pops from seed + schedule order.
- **Caveat:** two sites seed from `string.GetHashCode()` — `DairyChain.cs:402` (cow initial condition) and `LivestockDisease.cs:251` (outbreak draw). Under Unity Mono, `string.GetHashCode()` is deterministic for a given runtime; under .NET 8 (this harness) it is randomized per process. That is a harness-vs-Unity divergence risk, not a live Unity defect, and neither site is in this slice's scope. Recorded; not repaired.

### F-M4-8 — Opening resources apply once: verified for the development-town cash endowment and the carpentry endowment (verified, no finding)

- **Files:** `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs` (`ApplyDevelopmentTownOpeningCash` :3318, call site :617; `ConfigureCarpentryWorkshop`).
- **Verified:** `ApplyDevelopmentTownOpeningCash` returns early unless `startupIntent == NewGame`, and every allocation is marker-gated (`HasAppliedScenarioEndowment` / `RecordAppliedScenarioEndowment` with a stable allocation id per business/household), so a Continue can never reissue business or household opening cash. The inventory, equipment, fuel, and wholesale-account seeds are likewise NewGame-gated at the call site (:629-641). The carpentry workshop endowment runs on NewGame and Continue alike but is marker-gated, per its own comment. Opening-once holds for the two packages checked.

### F-M4-9 — New-package DTO round-trips in this tree: blacksmith save and sale terms pass; queued projects survive save/load (verified, no finding)

- **Verified in the baseline run (5107/0):** all 6 `BlacksmithSaveRoundTripTests` pass (L1) and all 9 `BusinessSaleTermsTests` pass (A2). Queued construction projects are captured into the save DTO (`AcquisitionMarketManager.cs:1226-1230`, `CaptureSaveDto` per project) and restored clear-then-load (:1274), so a mid-construction queue survives a Continue with its progress state.
- **Not in this tree:** B2's lease-market, D3's hardware-driver, and M2's stranded-lot tests are on unmerged branches (D3/M2 E-verified, B2 E pending). Their round-trips are covered by their own lanes' claims, not re-verified here.

## Repair implemented — M4-R1: open housing needs survive save/load (F-M1-7)

**The break.** `HousingSearchNeedRegistry.ExportState` / `ImportState` (`Domains/World/Property/HousingSearch.cs:440/:446`) existed and were correct, but had zero production callers: the hub never called them and the save DTO carried no needs slot. A load therefore started with an empty need registry. M1 recorded the impact as low in practice (the decision driver re-opens needs daily), but the gap matters more now that M1-R1 resolves needs — a resolved need lost and re-opened could re-trigger a search for a household that is already boarded, and the drive's guards are the only thing standing between that and a double booking.

**The repair (five files + one test file, following the purchasing-needs pattern exactly).**

- `Infrastructure/Persistence/SaveDtos.cs` (`PopulationSaveDto`): new `public List<HousingSearchNeed> housingSearchNeeds = new();` directly after `purchasingNeeds`.
- `Shims/PersistenceDtos.cs` (shared harness shim): the identical field in the mirrored `PopulationSaveDto`, same position, plus the `using LandLedgers.World.Property;` the real file already carries. The shim is otherwise untouched. Verified after the edit: a full diff of the `PopulationSaveDto` class in the real file vs the shim shows zero differing lines — the two classes are identical, including the new field. (An earlier note in my working record claimed the shim carried an extra `unstableHouseholdCount` field the real DTO lacked; that was wrong — the field exists in both, real :382, shim :782.)
- `Orchestration/Systems/SimulationSystemsHub.cs`: new `ExportHousingSearchNeeds` / `ImportHousingSearchNeeds`, mirroring `ExportPurchasingNeeds` / `ImportPurchasingNeeds` line for line.
- `Infrastructure/Persistence/SaveLoadManager.cs`: capture calls `systemsHub.ExportHousingSearchNeeds(save.population.housingSearchNeeds)` after the purchasing line; restore calls `systemsHub.ImportHousingSearchNeeds(save.population.housingSearchNeeds)` after the purchasing line.
- `Domains/World/Property/HousingSearch.cs` (`HousingSearchNeedRegistry.ImportState`): clear-then-load, like the household ledger and inventory registries — clears `all` and `openByHousehold` and resets the id sequence before importing, so a load replaces the live registry instead of merging into it, and advances `sequence` past any imported `hsn-N` id so a later `OpenNeed` never reuses an id that already exists. `ExportState` is unchanged (it already exports all needs, open and resolved).
- Regression tests in `Tests/Editor/World/HousingSearchNeedsSaveRoundTripM4Tests.cs` (+ `.meta`, fresh GUID `3e4f38aa10284d62a60fb19761b39da8`): open needs survive export/import with fields intact and resolved needs stay resolved-not-open; importing into a registry with a stale need clears the stale need; a second import replaces the first; a need opened after import gets `hsn-2`, never reusing `hsn-0`/`hsn-1`; null and empty imports are tolerated and an empty import clears.

**Harness-excluded wiring (Codex Unity checklist):** the hub methods, the `SaveLoadManager` capture/restore calls, and the `SaveDtos.cs` field itself are harness-excluded; the registry methods and the new tests are harness-compiled and covered by the full run. The wiring needs a Codex Unity EditMode run before being called proven, in the same position as A1/M1.

## Follow-up ledger

Status words: CLOSED (in integration), REPAIRED-BRANCH (committed and E-verified or E-pending, awaiting merge), OPEN (no repair yet), NOT-A-DEFECT (by design), OWNER (needs Kennedy's decision).

### M1 findings (F-M1-1 to F-M1-9)

- **F-M1-1** NPC property acquisition dead — OPEN. Evidence: still zero production callers for `CloseAcquisition` (B1-5 reconfirms).
- **F-M1-2** housing decisions never run — OPEN. `NpcPropertyDecisionEngine.Decide` still has zero production callers.
- **F-M1-3** housing search execution never runs — OPEN as a general leg; the boarding-only drive (M1-R1) executes boarding decisions through the executor, but `Decide` itself is still unwired.
- **F-M1-4** household move service dead; planner duplicates its job — OPEN. `HouseholdMoveService.MoveHousehold` still has zero production callers.
- **F-M1-5** rental options carry no rent — OPEN. No residential rent source exists. (B2's 1500c/month commercial lease rent does not close this: it prices commercial premises, not residential tenancies.)
- **F-M1-6** boarding vacancies never exist — CLOSED in integration. M1-R1 feeder call wires `RegisterHouse`; merged at `ecf64a2a`.
- **F-M1-7** open housing needs lost on save/load — REPAIRED in this slice (M4-R1, commit pending E verification + merge).
- **F-M1-8** newcomer settlement bypasses housing occupancy — OPEN. Settlement still mutates `homeBuildingId` directly.
- **F-M1-9** no live opportunity source — CLOSED for the boarding leg (M1-R1 drive builds the adapter over live authority; merged at `ecf64a2a`). The adapter's kin, employer-lodging, shelter, purchase, seller-finance, lender, land, and relocation paths remain empty by design.

### M2 findings (F-M2-1 to F-M2-9)

- **F-M2-1** construction progress billing never settles — OPEN, OWNER. Needs the stage-acceptance decision plus a production cash port and a driver hired-builder path. Evidence: `CommissionBuilderContract`, `AcceptBuilderStageAndReleaseDraw`, `RecordDeposit` still have zero production callers; hub still constructs the billing service with a null cash port.
- **F-M2-2** sawmill stranded lots — REPAIRED-BRANCH. M2-R1 (`d7c241ea`, E-verified), awaiting merge.
- **F-M2-3** Bank runtime dormant — OPEN, NOT-A-DEFECT. No bank business in the dev town; live loan engine is elsewhere.
- **F-M2-4** Tailor runtime dormant — OPEN, NOT-A-DEFECT.
- **F-M2-5** Tannery runtime dormant — OPEN, NOT-A-DEFECT.
- **F-M2-6** Grain mill dormant — OPEN, NOT-A-DEFECT. Live flour is the Millbrook import.
- **F-M2-7** completion handoff never consumed — OPEN. Reconfirmed in this slice (F-M4-4).
- **F-M2-8** no inspection/acceptance stage — OPEN, OWNER. Possibly by design.
- **F-M2-9** loan frequencies differ by lane — NOT-A-DEFECT. Monthly business loans vs weekly player debt, both wired by design.

### M3 findings (F-M3-1 to F-M3-7)

- **F-M3-1** ImportService is a dead parallel authority — OPEN, OWNER. D3 makes `ImportService` live for nails/iron only; the eight doc comments still name it as the reorder path for everything else. The single-authority decision is still Kennedy's.
- **F-M3-2** animal services fully implemented, entirely undriven — OPEN, OWNER. No demand model (no shoe wear, no illness); wiring a driver alone would change nothing.
- **F-M3-3** off-map supplier catalog empty — OPEN. No supplier-registration authority runs in production; do not invent suppliers.
- **F-M3-4** no NPC pricing decisions — OPEN (= F-B1-7). No price authority, no driver ever reprices.
- **F-M3-5** freight agreement book unused — OPEN, NOT-BLOCKING by design (the driver guards behind a non-empty agreement id).
- **F-M3-6** generic supplier comparison dormant — OPEN. Folds into F-M3-4.
- **F-M3-7** entrepreneurship walks away at the finder gate — REPAIRED-BRANCH. B2's lease authority (`d651a69a`, E pending), awaiting merge.

### B1 follow-ups (F-B1-1 to F-B1-7)

- **F-B1-1** commercial premises evidence for NPC formation — REPAIRED-BRANCH. B2 (`d651a69a`, E pending), awaiting merge.
- **F-B1-2** pivot trigger policy — OPEN (close half repaired by B1, merged at `679a2b7b`; `Pivot` still has no production caller).
- **F-B1-3** real cost-cutting response — OPEN. Still an event only.
- **F-B1-4** wire the NPC credit loop — OPEN. Borrow context still null in the lifecycle driver.
- **F-B1-5** production caller for `CloseAcquisition` — OPEN (= F-M1-1).
- **F-B1-6** NPC hiring driver — OPEN.
- **F-B1-7** NPC pricing decisions — OPEN (= F-M3-4).

### D2/D3 follow-ups

- **F-D2-1** smith-to-yard delivery driver — REPAIRED-BRANCH. D3 (`e721118d`, E-verified), awaiting merge.
- **F-D2-2** nails import driver — REPAIRED-BRANCH. D3 (`e721118d`, E-verified), awaiting merge.
- **F-D2-3** yard hardware sale is bucket-level — OPEN. Kind-level sale lines still missing.

### L1 follow-ups (F-L1-1, F-L1-2)

- **F-L1-1** hand the live BlacksmithRuntime to the save state — OPEN, with the fix now precisely named. Evidence: L1's holder exists in `Series2Pass9SaveState` (merged) and its capture takes the "no live runtime" branch; D3's `ConfigureLiveBlacksmithSaveOwner` obtains the live runtime from the hub but never assigns `pass9.BlacksmithRuntime` (no such assignment exists anywhere in D3's branch). The repair is one line at the D3 merge: `pass9.BlacksmithRuntime = liveSmithRuntime;` in `ConfigureLiveBlacksmithSaveOwner`, after the hub lookup succeeds. Do not close this until that assignment lands and the save captures a non-empty smith section.
- **F-L1-2** wire ForgeRepairChain when its driver lands — OPEN. `ForgeRepairChain` is still referenced only by its own file (plus tests and the Pass9 registry proof files).

### B2 follow-ups (F-B2-1 to F-B2-7)

- **F-B2-1** positive close→release→re-list end-to-end — OPEN, now unblocked: B1 is merged (`679a2b7b`), so the follow-up lane can extend the lifecycle-hook test with a closing record.
- **F-B2-2** formation working-capital gap — OPEN, OWNER. A newly formed business starts with no opening cash.
- **F-B2-3** deposit offset against arrears — OPEN.
- **F-B2-4** rent in the survival reserve — OPEN.
- **F-B2-5** used-equipment market wiring — OPEN. Needs a real seller-agency listing source.
- **F-B2-6** landlord for never-occupied buildings — OPEN, OWNER. Needs a property owner or townsite company named.
- **F-B2-7** player path can still claim a listed building free — OPEN. Pre-existing gap, out of B2's slice.

### New from this slice

- **F-M4-1** player queue is a fixed building timer — OPEN. Needs F-D11-2's decision plus a queue-to-executor bridge.
- **F-M4-2** queue shells never register in HousingAuthority — OPEN. Rides on F-D11-2 and F-M4-1.
- **F-M1-7** — REPAIRED in this slice (M4-R1), pending E verification + merge.

## Open owner questions (recorded, not decided)

Carried: F-M2-1 (stage acceptance), F-M2-8 (inspection stage), I1 F2 (off-map members eat from the larder), F-D11-2 (shell sleeping capacity, options a–d), carpenter wage, hay priced supply, F-M3-1 (single import authority), F-M3-2 (whether animals need care), NPC repricing policy (F-M3-4/F-B1-7), freight agreements (F-M3-5), F-B2-2 (formation capital), F-B2-6 (landlord for never-occupied buildings), A2's opening-cash-target interpretation (seller's agreed pre-close withdrawal).

## Files changed in this slice

- `Reports/Campaigns/UBC_M4_AZ_AUDIT_COMPLETION_DETERMINISM_LEDGER_2026-11-17.md` — this document.
- `Assets/Scripts/Domains/World/Property/HousingSearch.cs` — M4-R1: `ImportState` clear-then-load + id-sequence restore.
- `Assets/Scripts/Infrastructure/Persistence/SaveDtos.cs` — M4-R1: `housingSearchNeeds` slot on `PopulationSaveDto`.
- `Assets/Scripts/Orchestration/Systems/SimulationSystemsHub.cs` — M4-R1: `ExportHousingSearchNeeds` / `ImportHousingSearchNeeds`.
- `Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs` — M4-R1: capture + restore calls.
- `Assets/Scripts/Tests/Editor/World/HousingSearchNeedsSaveRoundTripM4Tests.cs` (+ `.meta`, fresh GUID `3e4f38aa10284d62a60fb19761b39da8`) — M4-R1 regression tests.
- Shared harness config (outside the repo): `/home/hatch/ll-ubc-harness/Shims/PersistenceDtos.cs` — M4-R1: identical `housingSearchNeeds` field in the mirrored `PopulationSaveDto` plus the `using LandLedgers.World.Property;` the real file already carries. No other shared-config change.

## Verification

- **Baseline (pre-repair, this tree, base `679a2b7b`):** 5107 passed, 0 failed, 5107 total. Build exit 0, run exit 0. Log `/home/hatch/ll-ubc-m4-run0.log` (build `/home/hatch/ll-ubc-m4-build0.log`). FAIL set empty — matches the current integration FAIL set (empty).
- **Post-repair (this tree, M4-R1 applied):** **5112 passed, 0 failed, 5112 total.** Build exit 0 (0 errors, 113 warnings), run exit 0. Log `/home/hatch/ll-ubc-m4-run1.log` (build `/home/hatch/ll-ubc-m4-build1.log`). The delta is exactly the 5 new M4 tests, all PASS:
  - `OpenNeeds_SurviveExportImportRoundTrip`
  - `ImportState_ClearsStaleNeedsInsteadOfMerging`
  - `ImportState_SecondImportReplacesFirst`
  - `OpenNeed_AfterImport_NeverReusesAnImportedId`
  - `ImportState_ToleratesNullAndEmpty`
- **FAIL-set delta vs the current integration FAIL set (empty):** none. No test flipped, no new failure.
- **Attempts that did not count:** the first post-repair build failed with CS2001 (see Environment notes — the shared `sources.txt` had gained D3's transfer-driver line, which this pre-D3 tree lacks); the first retry's build aborted with exit 134 and its run aborted mid-way (exit 134, 3804 passes before the crash, no failures); two log writes failed with "Disk quota exceeded". All four are the intermittent sandbox fault, not code faults. The final run1 is a clean, complete full run from a clean build.

## Codex Unity checklist items (harness-excluded, needs Unity EditMode)

- M4-R1 wiring: the hub `ExportHousingSearchNeeds` / `ImportHousingSearchNeeds` methods, the `SaveLoadManager` capture/restore calls, and the `PopulationSaveDto.housingSearchNeeds` field are harness-excluded. Confirm in Unity EditMode that a save with open housing needs restores them on Continue (open need present with fields intact, resolved need present as resolved, no duplicates, no re-opened already-boarded need).
- F-M4-1 / F-M4-2 are audit findings, not code changes — no Unity check in this package. When the queue-to-executor bridge lands, verify in Unity that a player-queued shell completes through real committed labour and registers in the HousingAuthority with spaces.
- The shared-shim edit (`housingSearchNeeds` field + using) must be confirmed field-identical to the real `SaveDtos.cs` by watchdog E.

## Environment notes

- Worktree creation failed twice with "Disk quota exceeded" on `git worktree add` (files under `Assets/Scripts/Planning Sources`), then succeeded on the third attempt with all 5419 files. Single-file writes of 200 MB and 900 MB succeeded throughout, and `df` showed 48 GB free — the fault is intermittent, same family as the 11:44 EST EPERM episode, not a real quota. A second wave hit mid-session (even a 5-byte redirect failed) and cleared after a few minutes. No workaround remains in the deliverable.
- One self-inflicted harness misconfiguration: the first baseline build used `REPO=/home/hatch/ll-ubc-m4` (worktree root) instead of the Scripts checkout; the generated csproj referenced paths without `/Assets/Scripts` and the build failed with CS2001 file-not-found errors. Corrected to `REPO="/home/hatch/ll-ubc-m4/Assets/Scripts"`; the numbers below are from the corrected config.
- The shared `sources.txt` changed under this lane: integration merged D3 (tip now `3e31921c`, "Merge M3") while M4's tree was still on `679a2b7b`, so the shared list gained `BlacksmithToYardHardwareTransferDriver.cs`, which this tree does not contain. The first post-repair build failed CS2001 on that file. Per the standing lesson (filter sources to files present in the worktree), my harness copy now carries a filtered `sources.txt` with that one line commented out and my copy's gen script reads the filtered list; the shared `sources.txt` was NOT touched. The filtered copy is outside the repo and is not part of the commit.
- The post-repair run was fought through the intermittent sandbox fault: one build abort (exit 134) during restore, one harness abort mid-run (exit 134, 3804 passes, 0 failures before the crash), and two "Disk quota exceeded" failures on log writes. The `env -i HOME=/home/hatch PATH=/home/hatch/dotnet:/usr/bin:/bin DOTNET_CLI_HOME=/home/hatch/.dotnet` workaround got the build through, and a retry after a two-minute pause got the full run through. None of these is a code fault; the final run1 is clean.
