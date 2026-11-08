# Muse campaign full handoff 2026-11-07

Land and Ledgers economy campaign - Deadscripts Interactive

- Branch - muse/economy-campaign-2026-11-07
- Repository - github.com/KenB-O/Land-and-Ledgers-Scripts
- Baseline - 7a0c7e99866916116454a04421e6ccbe6638db81
- Final code HEAD - 0ae3381a1a62a215b3fe20c8f8fd690c5464c07e
- This handoff document - committed as the final commit on the branch after the code freeze - its SHA is recorded in the session checkpoint and in the campaign report to the parent
- Campaign dates - 2026-11-07, about 1650 EST to about 2100 EST
- Status - INTEGRATION HOLD - no further development starts until Kennedy reauthorizes
- Author - Muse, source implementation - ChatGPT is development manager and architect - Codex is local Unity integration and PlayMode verification
- Verification level - Roslyn harness only - Muse has no Unity Editor verification - nothing in this document claims Unity compilation, Test Runner success, PlayMode success, or visual verification

## How to use this document

This is the single complete record of the November 7 economy campaign. Read the summary first, then the commit manifest, then the Codex integration instructions, then the Unity verification plan. The remaining backlog at the end is the input for the next campaign. Nothing here is merged to main. Nothing here is Unity verified. Every claim states its evidence level.

## Campaign summary

The campaign started from the Codex economy handoff and Kennedy manual test evidence. Two evidence sets were carried throughout and never conflated. Set A is the Codex 1x run of about 9 minutes 50 seconds over 7 days with world seed 485455302 and market seed 1474896984 - 0 purchases, 4 meals eaten, 140 missed meals, freight physical travel and return confirmed but no pickup, cargo transfer, or 125c settlement, Bakery blocked by notions with 0 on hand and a 14-day lead, and one focused EditMode failure on a 12c reserve discrepancy. Set B is Kennedy newer manual test at about 10x through day 24 with world seed 1781894988 and market seed 1449120246 - freight success with Millbrook pickup day 3 and Bakery receipt plus 125c charge plus agreement fulfillment day 7, a second shipment picked up day 21 and unloaded day 24, Bakery success days 15 to 18 with 24 loaves baked and transferred daily for 96 total, blocked from day 19 by flour, and household purchases still zero through day 24. Set B supersedes Set A on freight status.

Five plans were completed in source plus a follow-up and a revision cycle. Plan 01 set the 120-second day. Plan 02 repaired household shopping. Plan 03 verified freight repeatability. Plan 04 reconciled Bakery supplies. Plan 05 repaired single-person household shopping and realistic scheduling, finishing through Slice 4 with Slice 5 in progress at the checkpoint and Slice 6 remaining. A priority revision arrived from Kennedy and ChatGPT at about 1859 EST and replaced the original 9-item queue with a 17-item queue. Plan 04 was not interrupted. The revised queue governs the remaining backlog.

Harness progression across the campaign - baseline at 7a0c7e99 was 2331 passed and 18 failed. Plan 01 ended 2343 passed and 18 failed. Plan 02 ended 2375 passed and 18 failed. Plan 03 ended 2386 passed and 17 failed. Plan 04 ended 2396 passed and 19 failed, then a test-only follow-up ended 2398 passed and 17 failed. Plan 05 Slice 4 ended 2423 passed and 17 failed. Final checkpoint harness numbers are in the testing evidence section below. Net change from baseline to the final checkpoint is plus 95 passed and minus 1 failed - 94 new tests passing plus 1 pre-existing failure repaired in source (the freight lot-custody test in Plan 03 Slice 1). Zero regressions were introduced at any slice boundary.

## Commit manifest

All commits are on branch muse/economy-campaign-2026-11-07, in chronological order, oldest first. Times are git commit times in EST.

- c6dd1be - Plan 01 - 120-second game day at 1x
- 3dc58a3 - Plan 02 Slice 0 - discriminator tests for schedule conflict, opening empty store, unservable needs
- 708aa79 - Plan 02 Slice 1 - window search with interruptible-rest preemption
- 0bb6665 - Plan 02 Slice 2 - stop raising needs no seller can offer
- 4c28441 - Plan 02 Slice 3 - prove store stock is visible, add empty-store diagnostic
- cba4b3c - Plan 02 Slice 4 - end-to-end acceptance, 5 households, bread purchase to meal
- 148f980 - Plan 02 Slice 5 - diagnostics and reservation survival
- c7766d9 - Plan 03 Slice 0 - second-haul discriminator plus pickup-gate probes
- 3aba11f - Plan 03 Slice 1 - repair known failing freight test, test only
- ae09c81 - Plan 03 Slice 3 - pickup and return stall diagnostics go day-keyed
- 32c5ff1 - Plan 03 Slice 4 - save and load plus refused-delivery verification
- 7857674 - Plan 04 Slice 0 - multi-day discriminator plus probes, tests only
- 9fc663e - Plan 04 Slice 2 - over-ordering probe, no repair owed, tests only
- 5e2f390 - Plan 04 Slice 4 - blocked-batch diagnostic plus cumulative actuals line
- af3c02e - Plan 04 Slice 5 - notions procurement wired in release builds
- db16e20 - Plan 04 Slice 6 - refused-feed rollback plus flour-pipeline save and load, verification, tests only
- abd99de - Plan 04 follow-up - Tests 2 and 3 revised to the not-a-defect priming reading, tests only
- 3e6553d - Plan 05 Rev 1 Slice 0 - revised single-person shopping discriminators, tests only
- 7eb926e - Plan 05 Slice 1 - close the AutonomousWorkGenerator predicate gap
- 0eb5699 - Plan 05 Slice 2 - real-store port and opening stock, investigation, no repair
- 720158c - Plan 05 Slice 3 - trace the production work-assignment path, tests only
- 2db39a2 - Plan 05 Slice 4 - work reserves and assigns before shopping, starts after, rest preempts, location-corrected search
- 0ae3381 - Plan 05 Slice 5 - failure memory, urgency, honest impossible errands
- handoff document - this file, committed as the final commit on the branch

Plan 02 Slice 2 of the original Plan 05 numbering was skipped because Slice 0 Test 1 passed and proved the second haul. Plan 04 Slices 1 and 3 were skipped because Slice 0 Tests 1 and 4 passed. Plan 05 original Slice 0 was stopped per the plan stop rule and replaced by Rev 1. Total new commits in this campaign, including this handoff document - 24, of which 23 are code and test commits and 1 is this document.

## Changed files

Exact diff against baseline 7a0c7e99 is 30 files, 9108 insertions and 119 deletions, plus this document. Production files changed - 10 files.

- Assets/Core/Time/DefaultSimulationTimeSettings.asset - one line, 60 to 120
- Assets/Scripts/Core/Time/SimulationTimeSettings.cs - script default 60f to 120f
- Assets/Scripts/Domains/Economy/BreadDrivers/BakeryWholesaleDailyDriver.cs - blocked-batch diagnostic plus cumulative actuals line
- Assets/Scripts/Domains/Economy/Labor/ManagerDecisionExecution.cs - StartAssignedTasks split into ReserveAndAssignTasks and StartReservedTasks, rest preemption for Work
- Assets/Scripts/Domains/Population/AutonomousWorkGenerator.cs - optional seller-recognition predicate on need creation
- Assets/Scripts/Domains/Population/HouseholdShoppingLoop.cs - departure-location correction in ExecuteBatch
- Assets/Scripts/Domains/Population/HouseholdShortage.cs - unservable needs suppressed at the monitor
- Assets/Scripts/Domains/Population/PersonScheduleTracker.cs - forward window search with rest-only atomic preemption
- Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs - ungated notions procurement registration
- Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs - work assignment before shopping, forwarder cumulative diagnostics, day-keyed freight stall logging, longest-open-need summary line

Test files changed - 20 files, including 8 new test files with fresh GUID .meta files.

- Assets/Scripts/Tests/Editor/Economy/BakerySuppliesTests.cs - new, 12 tests
- Assets/Scripts/Tests/Editor/Economy/FreightRepeatabilityTests.cs - new, 10 tests
- Assets/Scripts/Tests/Editor/Economy/FreightShipmentServiceTests.cs - modified, legacy shipment moved to a second wagon
- Assets/Scripts/Tests/Editor/Integration/LivingWorldSim.cs - modified, AWG predicate wired
- Assets/Scripts/Tests/Editor/Population/AutonomousWorkGeneratorTests.cs - modified, predicate cases
- Assets/Scripts/Tests/Editor/Population/HouseholdShoppingEndToEndTests.cs - new
- Assets/Scripts/Tests/Editor/Population/HouseholdShoppingRepairTests.cs - new
- Assets/Scripts/Tests/Editor/Population/HouseholdShoppingUnservableNeedsTests.cs - new
- Assets/Scripts/Tests/Editor/Population/SinglePersonShoppingTests.cs - new, 24 tests at the final checkpoint
- Assets/Scripts/Tests/Editor/Population/StoreOfferVisibilityTests.cs - new
- Assets/Scripts/Tests/Editor/Time/DayLength120SecondsTests.cs - new, 12 tests
- Assets/Scripts/Tests/Editor/Time/SimulationTimeSettingsSpeedTests.cs - modified

## Testing evidence

### Full harness progression

Every harness run in this campaign used the same exports - PATH with dotnet, DOTNET_ROOT, DOTNET_CLI_TELEMETRY_OPTOUT set to 1, LAND_LEDGERS_REPO pointing at the repo Assets/Scripts folder - and isolated build and object paths under /tmp. Omitting LAND_LEDGERS_REPO at run time shows 3 phantom registry failures. The harness shim has no Assert.False - tests use Assert.IsTrue with negation.

- Baseline at 7a0c7e99 - 2331 passed, 18 failed
- Plan 01 boundary - 2343 passed, 18 failed, plus 12 tests, zero regressions
- Plan 02 boundary - 2375 passed, 18 failed, plus 32 tests, zero regressions
- Plan 03 boundary - 2386 passed, 17 failed, plus 11 net tests, one pre-existing failure repaired, zero regressions
- Plan 04 boundary - 2396 passed, 19 failed, plus 10 tests, zero regressions
- Plan 04 follow-up boundary - 2398 passed, 17 failed, plus 2 tests, zero regressions
- Plan 05 Slice 4 boundary - 2423 passed, 17 failed, plus 25 net tests, zero regressions
- Plan 05 Slice 5 boundary - 2426 passed, 17 failed, plus 3 tests, zero regressions
- Final checkpoint boundary - 2426 passed, 17 failed, verified by a fresh full harness run at HEAD 0ae3381 with isolated build paths - the run matches the Slice 5 boundary exactly

### Failing tests at the final checkpoint

- LandLedgers.Editor.Economy.BakeryStagedProductionChainTests.OvenFull_ThirdBatchParksAtBake_RetriesWhenFreed
- LandLedgers.Editor.Economy.BakeryStagedProductionChainTests.TwoBatches_ShareOneOven_BothBake
- LandLedgers.Editor.Economy.ForgeRepairChainTests.ForgeContention_NailRunBlocksRepair_ParksAndRetries
- LandLedgers.Editor.Population.DomesticLaborDriverTests.LargeTickStep_MatchesSmallTickStep
- LandLedgers.EditorTests.Integration.Pass9RegistryAssemblyScanTests.AssemblyScan_EveryScannedType_IsAccountedFor
- LandLedgers.EditorTests.Integration.UnwiredSystemsRegistryTests.RegisteredTypes_HaveNoProductionCallers
- LandLedgers.EditorTests.Population.DevelopmentTownConfigurationTests.AuthoredConfiguration_DirectlyDefinesTheCompleteDevelopmentTown
- LandLedgers.EditorTests.Population.DevelopmentTownConfigurationTests.Configuration_AllowsMoreBusinessRecordsWithoutASecondFixtureType
- LandLedgers.EditorTests.Population.DevelopmentTownConfigurationTests.Configuration_PreservesOwnersCashAndGenericOpeningInventory
- LandLedgers.Tests.Economy.Review2SecondScriptsTests.SaveWiring_SharedInstance_RestoreRepopulatesTheRegistryTheDriverReads
- LandLedgers.Tests.Economy.Review2VerticalD3Tests.D3Save_MidRunSaveRestore_StoreFeedAndRuntimeItemStillFeed
- LandLedgers.Tests.Economy.Review2VerticalD3Tests.D3Save_RestoreTwice_IsIdempotent_AndSkipsInvalidEntries
- PurchasedRetailLotReceiptTests.BusinessFeedReceipt_PreservesNamedSourceAndRejectsDuplicateReceipt
- PurchasedRetailLotReceiptTests.BusinessFeedStorage_ExpandsOnlyToFiniteRestockNeedWithoutCreatingStock
- PurchasedRetailLotReceiptTests.BusinessHorseFeedAdapter_UsesTwoFeedUnitsPerSackAndCarriesRemainder
- PurchasedRetailLotReceiptTests.OpeningFeedOrder_ReceivesTwentySacksOnceAndSettlesBusinessCash
- PurchasedRetailLotReceiptTests.RefusedDestinationReceiptRestoresPurchasedSourceCustody

All 17 are pre-existing known failures from the baseline set, identical by name to the baseline failure set minus the one freight lot-custody test repaired in Plan 03 Slice 1. No new failure was introduced. The harness reports no skipped total.

The known failure set at the Slice 4 boundary was 17 tests - DevTown config 3, registry scan 1, unwired registry 1, Bakery staged 2, Forge 1, DomesticLabor LargeTickStep 1, Review2 second scripts 1, Review2 vertical 2, retail lot receipt 5. One pre-existing failure was repaired during the campaign - the freight lot-custody test LotCustody_MissingSourceDebitAuthority_RefusesLoadWithoutAffectingLegacyShipments, fixed test-side in Plan 03 Slice 1. No other pre-existing failure was repaired. No new regression was introduced at any slice boundary.

### Focused test results by plan

- Plan 01 - DayLength120SecondsTests 12 tests, all passing - 120 seconds at 1x, 12 seconds at 10x, 4.8 seconds at 25x, exactly-once rollover, short-tick and scheduled-event processing, pause and resume, save and load
- Plan 02 - HouseholdShoppingRepair 16 of 16, StoreOfferVisibility 4 of 4, end-to-end acceptance 8 tests - one real bread transaction end to end, five households same day, 3-day repeat, five edge cases, cash and bread conserved
- Plan 03 - FreightRepeatability 10 of 10 - second haul completes end to end, second 125c settles, payer minus 250c and carrier plus 250c total, agreement 2 Fulfilled, capacity freed afterwards, mid-haul save and load exactly once, refused delivery holds cargo with no settlement and completes exactly once on retry
- Plan 04 - BakerySupplies 12 of 12 - day-1 notions order arrives day 15 and feeds, bakes, and transfers with both cash legs reconciled, 14 priming orders consumed by 14 consecutive daily bakes days 15 to 28, flour-blocked priming saves 6 days versus the no-priming control, 1-sack haul cannot bake and the 2-sack reorder delivers and bakes, 5-day margin sequence 2, 1, 1, 1, 1 sacks for 174c, fuel exhaustion at 40 bake days, mid-pipeline save and load exactly once, refused-feed rollback restores store lots and cash
- Plan 05 Slices 0 to 4 - SinglePersonShopping 21 of 21, HouseholdShopping 43 of 43, AutonomousWorkGenerator 13 of 13 at the Slice 4 boundary
- Plan 05 Slice 5 - SinglePersonShopping 24 of 24, plus 3 new tests - LongestOpenNeed_NamedInSummary, UrgentNeed_AttemptedFirst, ImpossibleErrand_FiveDays_NoAccumulation - with two evidence-grounded deviations recorded in the commit message - the longest-open key is CreatedDayIndex, not LastEvaluatedDayIndex, because the monitor refreshes LastEvaluatedDayIndex on every evaluation so every need evaluated today ties at today, and the day-0 work is completed at day end through CompleteTask because StartTask refuses while an InProgress task is held

### Tests that were altered, and why

- Plan 04 Tests 2 and 3 were revised after commit in abd99de because the first version asserted a defect that did not exist. Slice 2 proved the 14 notions orders over days 1 to 14 are priming for 14 future daily bakes - Set B proves it, because bakes on days 15 to 18 needed orders placed days 1 to 4. The revised tests assert the verified priming behavior with the same rigor - order counts, bake counts, per-day linkage, and cash. No production expectation changed. No assertion was weakened.
- Plan 05 original Slice 0 tests were revised before commit under Rev 1 because the first version asserted a displacement mechanism that did not exist. The shopping reservation is released on trip completion, so it cannot block work. The real mechanisms are the 600-minute day budget and the rest sprinkle. Tests 1, 2, and 3 were corrected to 240-minute tasks, the authored catalog maximum for field work. No production expectation changed. No assertion was weakened.

### Source-only verification limits

The Roslyn harness does not compile SimulationDrivers.cs, GeneralStoreRuntimeManager.cs, SaveLoadManager.cs, SaveDtos.cs, SimulationDrivers-adjacent files, or the bootstrapper, and it excludes GeneralStoreTradingPort.cs and several other Unity-only files. Changes to those files are verified by source review plus harness-reachable component tests, and the live behavior is Unity-owed. The harness also does not exercise the real store port builder, the NewGame seeding path, the Continue and restore paths, or any PlayMode behavior. No harness result in this campaign is Unity verification. Every Unity-owed item is listed in the verification plan below.


## Progress by system

### A - Simulation time, 120-second day

COMPLETE in source, REQUIRES UNITY VERIFICATION in PlayMode.

The normal game day is now 120 real seconds at 1x, 12 seconds at 10x, and 4.8 seconds at 25x. The change is a single scalar, realSecondsPerGameDayAt1x, edited in exactly two places - the script default in SimulationTimeSettings.cs line 114 from 60f to 120f, and the live asset Assets/Core/Time/DefaultSimulationTimeSettings.asset line 44 from 60 to 120. No scene hack, no second clock. One day still contains 1440 in-game minutes and 96 fifteen-minute short ticks. At 1x the clock advances 720 game seconds per real second, at 10x it advances 7200, at 25x it advances 18000. Daily events fire exactly once per day. Short ticks and due events are properly ordered. Pause freezes progression. Save and load preserve simulation time. Twelve focused tests cover the day lengths, exactly-once rollover, short-tick and scheduled-event processing, pause and resume, and save and load. One test-writing note matters for future work - a full day advanced from midnight in one AdvanceGameSeconds call fires only 64 short ticks because of the pre-existing tested 64-per-frame cap, with 32 deferred not discarded, so two tests run from the scenario start at 21600 seconds in. Unity-owed - scene wiring confirmation that the open scene TimeManager points at DefaultSimulationTimeSettings with no scene-local override, plus live 1x, 10x, and 25x PlayMode day-length checks. The 250x speed option replacing 25x was approved by Kennedy and is NOT implemented in this campaign - it is in the remaining backlog.

### B - Household shopping

COMPLETE in source through Plan 05 Slice 4, PARTIAL overall, REQUIRES UNITY VERIFICATION in PlayMode. Slice 5 diagnostics and Slice 6 acceptance remain - see the remaining backlog.

Plan 02 proved the full lifecycle in the harness - need, shopper, schedule, journey, seller, offer, purchase, payment, goods, household reserve, consumption. Two separate failure classes were found and repaired. First, a binding schedule conflict - OnDayChanged ran DriveDailyActivityPlans before DriveDailyNeeds, SprinkleRest put a Resting block on 480 to 510 for every person, shopping tried exactly one departure at 480, and ChooseShopper required the whole window free, so every member was rejected every day and the identical slot was retried indefinitely. The repair is a forward window search in 15-minute steps with rest-only atomic preemption. Rest is interruptible per PersonActivityPolicy. Sleep, work, cooking, and other committed activities are not. Second, needs raised for items no seller recognizes - preserved_meat and others are absent from the authored store definition - and the store opening with zero sellable stock for every meal item because BusinessRuntimeState deliberately excludes authored starting stock from physical stock. The repair suppresses unservable needs at the monitor with a loud diagnostic, and adds a once-per-day empty-store diagnostic. Bread is meal-eligible in code. Sunday closure is intended behavior. Potatoes and salt stay unservable under owner decision D-5.

Plan 02 Slice 4 proved the 10-point end-to-end acceptance on H1 bread, five households same day, 3-day repeat of 6 then 4 then 4 purchases, five edge cases, cash and bread conserved. Slice 5 added five-mode per-need reasons, a daily shopping summary under the 12-per-day cap, and a drive-order guard proving the shopping reservation survives the rest of the day.

Plan 05 extended shopping to single-person working households and realistic scheduling. The planner first misread the displacement mechanism and the coder stopped correctly at Slice 0 per the plan stop rule. The corrected mechanisms, verified in source, are the 600-minute day budget and the rest sprinkle - not reservation overlap, because the shopping reservation is released on trip completion. The repair, committed in Slice 4, splits work assignment - ReserveAndAssignTasks runs before shopping and StartReservedTasks runs late, because TaskAuthority.StartTask refuses when the person already holds an InProgress task and the shopping loop starts trip legs as tasks. Work reservations now preempt Resting through the existing TryReservePreemptingRest seam, Work only, with the released rest named in diagnostics. The departure-location lookup in ExecuteBatch was corrected so the pre-check, estimate, and legs all use the shopper resolved location. Single employed man, single employed woman, two employed adults with staggered shifts, one adult available during store hours, full-coverage shift, remote worker, Sunday closure, and the no-feasible-opportunity case are all tested in the harness. A genuinely impossible errand stays unfulfilled for a legitimate reason and does not loop.

The AutonomousWorkGenerator path is closed in source. GenerateForHousehold called Needs.CreateNeed directly with no recognition predicate, and it had zero production callers. Slice 1 added the optional predicate and wired it at the LivingWorldSim caller. The real-store port question is answered with a build-flavor split - the 18 opening bread loaves are real identified lots in dev and editor builds, created by ScenarioOpeningStockEndowment.TryApply through TryAddRetailItemLot, exactly once, seeded before the port is built. Release builds open with zero sellable stock and use a fallback port whose offers carry UsesItemLevelInventory set to false, selling from pooled category stock. The harness cannot prove either half because the manager file is not compiled and the port file is excluded. The release opening-stock policy is a recorded owner decision, not repaired here.

Remaining limitations - the pre-check route at line 568 still uses member-0 home for supplier viability, the single-need path at line 204 and line 1259 carry the same estimate and leg mismatch shape as the batch path, commute shopping has no code path, R-REMOTE workplace-departed trips are deferred to priority 6, and no PlayMode purchase has been observed in Unity yet.

### C - Bakery

COMPLETE in source, REQUIRES UNITY VERIFICATION in PlayMode.

Plan 04 reconciled Bakery supplies, production coverage, and financial accounting across 7 slices. The notions path is store-mediated by design, not bakery-owned - the General Store buys at the market and the Bakery pays the store at feed time. A day-1 notions order arrives day 15 through the 14-day lead, feeds, bakes the same batch, and transfers 24 loaves with both cash legs reconciled - store minus 10c at receipt, Bakery minus 10c and store plus 10c at feed, store minus 48c and Bakery plus 48c at transfer. The 14 orders placed over days 1 to 14 for one blocked batch are priming, not over-ordering - they are earmarked for 14 future daily bakes, proved by Set B bakes on days 15 to 18 needing orders placed days 1 to 4. Flour-blocked days still order notions, and that priming saves 6 days versus the no-priming control. A 1-sack Millbrook lot is 10 lb and can never bake a 12 lb batch alone - the 2-sack reorder delivers and the batch bakes once both lots land, with bin 30 lb and Bakery cash 2000 minus 48 minus 96 plus 48 equals 1904c. The 5-day margin sequence buys 2, 1, 1, 1, 1 sacks for 60 lb and 174c, giving actual 44.8c per batch versus 48c revenue for plus 3.2c per batch actual, against the minus 20c standard-cost line - the gap is carryover, and the cumulative actuals line now reports both definitions side by side. A blocked batch logs daily with its cause and retry, with no new Failed or Cancelled state. Fuel exhausts at 40 bake days with a diagnostic naming D-2 and D-7. Save and load mid-pipeline restores exactly once, and a refused feed rolls back the store lot and cash exactly.

Two wiring facts matter. First, the notions procurement service was dev-gated inside ConfigurePlaytestHorseFeed, so a release build had no service and baking refused on notions forever. Slice 5 added an ungated RegisterNotionsProcurement that reads catalog terms - 10c, 14 days, railhead origin - registers the 30-unit finite counterparty, and assigns the service only when the save slot is empty, so dev builds are a no-op and release builds get the only registration. The horse-feed order stays dev-gated. Second, BakeryFlourBootstrap with 120 lb flour plus 30 notions and BakeryFuelBootstrap with 40 units of fuel have zero production callers - only tests call them. The live bin starts empty for flour and notions by every source path found. The 40-unit fuel plausibly comes from the dev-gated SeedDevelopmentBakeryOpeningFuel path through town-config OpeningBakeryFuelUnits, but the asset value is unverified from source and a release build may start with 0 fuel. Opening-stock policy is a recorded owner decision for a follow-up plan, not decided here. No wholesale price, package weight, or fuel import policy was changed. D-1, D-2, D-5, D-6, and D-7 stay open.

### D - Freight

COMPLETE in source as verification plus one test-side repair, REQUIRES UNITY VERIFICATION in PlayMode.

Set B supersedes Set A on freight status - the first haul already worked in the live game, with Millbrook pickup day 3 and Bakery receipt plus 125c charge plus agreement fulfillment day 7. Plan 03 verified rather than rebuilt it. The second haul completes end to end in the harness - second lot placed by PlaceReorder on day 13, 48c debited once, second 125c settles, Bakery holds 2 lots of 10 lb with distinct ImportOrderIds, payer minus 250c and carrier plus 250c total, agreement 2 Fulfilled, capacity freed afterwards. Set A versus Set B is closed - the pickup gate blocks pickup until the physical trigger, the D-plus-2 fallback covers a missed pickup, and the 4-plus day-boundary count plus once-only logging explain Set A empty settlement inside a 7-day 1x window, with no code-path difference found. Mid-second-haul save and load restores exactly once. A refused delivery on haul 2 holds as Delayed with cargo in wagon custody, no settlement and no fulfilment, then resumes and settles exactly once. Slice 3 switched five stall-logging sites to day-keyed LogRefusalDayKeyed, same keys and text, no behavior change. Slice 1 repaired one known failing freight lot-custody test test-side - the only pre-existing failure repaired in this campaign.

One disposition is recorded, not repaired - R1. The store weekly demand path cannot see Millbrook lots because OutstandingOrderedUnits is 0, while the Bakery daily path already counts them. R1 needs an owner decision naming the demand path before any fix. No allocation change was made and no defect was filed.

### E - Personal inventory and carrying capacity

NOT STARTED, except for the authorities this campaign verified and reused.

No personal inventory system, no carrying-capacity rules, no weight or volume limits, no item dimensions, no character carrying attributes, no bags, baskets, pockets, lunch pails, saddlebags, horse packs, or wagon cargo compartments were built in this campaign. The freight custody structures that exist - shipment services, lot identities, ownership authorities, custody transfers, wagon capacity and exclusivity, household reserves, business inventories - are real and were verified, but they are NOT a shared inventory framework and must not be described as one. Revised priorities 3 and 4 - shared physical inventory foundation, then personal carrying capacity and portable storage - are the next major workstream and are fully specified in the remaining backlog. EquipmentCapacityContracts and EquipmentCapacityService exist and are named as reuse targets in the revised directive. Nothing in this campaign changes them.

### F - Named goods and household provisions

NOT STARTED, except for findings recorded.

No generic product was replaced with a specific SKU identity. staple_food, meat_unspecified, and preserved_food remain as they were. Bread is meal-eligible in code and is the only food proven end to end from purchase to meal. No household stock inspection activity was built. No anticipatory procurement was built. No packed meals were built. No intentional meal skipping was distinguished from defect-driven missed meals in code. D-5 potato and salt package weights and D-6 meal eligibility and preserved-goods identity remain unresolved owner decisions. The revised directive items 5, 6, and 7 - inventory inspection, packed meals, and specific physical food products - are in the remaining backlog with their constraints.

### G - Variable production

NOT STARTED.

The Bakery still produces exactly 24 loaves per batch. No smaller run, no recipe scaling, no minimum output, no fixed-cost separation, and no defer-or-run operating policy were built. Revised priority 9 - variable production quantities, Bakery first - is in the remaining backlog.

### H - Task duration diagnostics

NOT STARTED.

No predicted-versus-actual instrumentation, no overrun warnings, no blocked-time classification, and no anti-spam diagnostic logic were built. The campaign did record two timing facts that motivate it - the 15-day freight lag in the November 6 playtest was a task-system scheduling defect, and the 600-minute budget gate is the operative refusal in the Plan 05 scheduling repair. Revised priority 8 - task duration diagnostics - is in the remaining backlog and may be done earlier as a short independent slice if it helps diagnose ongoing defects.

### I - Nighttime lighting

NOT STARTED in source. Investigation only, no code change, no visual verification claimed.

Night lighting was revised priority 5 in the original queue and revised priority 10 in the revised queue. No source change was made and no proposal was written in this campaign, because the local Unity project holds newer unpublished terrain and lighting modifications that may not exist in this repo checkout, including a locally newer DayNightVisualController, HDRP Volume profile, Main Scene, and terrain assets. The requirement stands - night stays visibly dark but readable, with buildings, people, roads, nearby terrain distinguishable, and it must not look like daylight. Longer-term, date-dependent moon phase and moonlight are wanted, with historical and real-time mode aiming for realistic darkness and normal gameplay prioritizing readability, on the same date and moon phase. Astronomical lighting is not a prerequisite for fixing the nearly black night. The work is in the remaining backlog as a narrow source-level improvement if safe, otherwise precise Codex instructions.

### J - Roads, buildings, and navigation

NOT STARTED in source. Investigation only, no code change.

Terrain-painted roads, floating buildings and foundations, and 2.5D navigation were revised priorities 6, 7, and 17 in the revised queue. No source change was made in this campaign and no proposal was written, for the same unpublished-local-work reason as lighting - no permanent TerrainData mutation in PlayMode, no overwriting authored terrain, no destructive prefab changes. The logical road network is untouched. The Plan 05 location fix in ExecuteBatch is a scheduling-correctness change, not a navigation change, and it does not touch routes, terrain, or prefabs. All three items are in the remaining backlog.

### K - Freight consolidation and profitability

NOT STARTED.

No consolidation, no dedicated freight, no pooled shipments, no carrier-initiated consolidation, no operating-cost accounting, and no margin breakdown were built. The 125c charge stays as prototype. Revised priorities 11 and 12 are in the remaining backlog, with the sequencing constraint that consolidation must not be implemented before the necessary capacity and custody contracts from revised priorities 3 and 4 exist.

### L - Save and load and audit queue

PARTIAL. Q04 NOT STARTED in this campaign. Audit queue otherwise retained, not restarted.

Save and load was verified in the harness at three seams - mid-second-haul freight, mid-notions-pipeline Bakery, and mid-flour-pipeline Millbrook reorder - each restoring exactly once with no duplicate order, receipt, or settlement. Those are source-level persistence checks and do not replace real Unity Continue verification. Q04 fresh-scene Continue, Pass 9 plus Millbrook, was not started in this campaign - it is revised priority 14. The overnight audit queue Q01 to Q30 is retained. Q01, Q02, and Q03 were completed in the overnight campaign. Q04, Q05 with AUD-144, Q06 with AUD-016, Q07 with AUD-182 and AUD-183, and Q11 with AUD-185 continue after higher-priority repairs or during suitable independent slices. Q10 is blocked on D-5. The 341-defect audit was not repeated and must not be restarted.

### M - Technical warnings

NOT STARTED, classification only, no investigation completed in this campaign.

The technical backlog from the Codex handoff and Kennedy manual test is unclassified - the TempJob warning with 119438 allocations, the Burst InvalidOperationException on GPU Resident Drawer material classification, and the negative-scale BoxCollider warnings. No origin was established in this campaign - project code, Unity packages, renderer settings, or transient Editor state. Do not claim the TempJob warning proves a persistent leak without reproduction. Do not modify Unity package sources. Classification is in the remaining backlog.


## Codex integration instructions

Read this whole section before touching the local project. The local Unity project lives on drive F under Land and Ledgers and contains more than 1000 unrelated modified or untracked paths from Unity authoring and prior work. Those paths must be protected. Do not use destructive Git operations, broad staging, forced checkout, or automatic overwrites. Do not assume the local worktree is clean.

### Safe workflow, recommended

- Work in an isolated location first. Clone the branch to a clean directory, or fetch the branch into a scratch worktree, and verify the branch builds there before touching the authored project.
- In the authored project, commit or stash nothing on behalf of the author. List local changes first with git status and git diff, and keep a written record of every file you touch.
- Integrate file by file from the changed-files list below. Never run git checkout with a pathspec that could overwrite authored assets, never run git clean, never run git reset with hard, and never force-push.
- New scripts arrive with their .meta files and fresh GUIDs. Copy each .meta beside its script. Never copy a GUID from another file. Never rename serialized types, fields, namespaces, classes, or enums. Serialized enums are append-only. Preserve every existing .meta GUID.
- Import order - import the 10 production files first, then the test files, then open the project and let Unity reimport. If Unity reports a compile error, stop and report the exact error with the file and line - do not edit Muse code to force it through without telling the coordinator.
- No editor initialization step is required beyond a normal Unity reimport. No new assembly definition files were added. No new event bus or dependency-injection framework was added.

### Exact files Muse changed

Production files, 10 files - Assets/Core/Time/DefaultSimulationTimeSettings.asset, Assets/Scripts/Core/Time/SimulationTimeSettings.cs, Assets/Scripts/Domains/Economy/BreadDrivers/BakeryWholesaleDailyDriver.cs, Assets/Scripts/Domains/Economy/Labor/ManagerDecisionExecution.cs, Assets/Scripts/Domains/Population/AutonomousWorkGenerator.cs, Assets/Scripts/Domains/Population/HouseholdShoppingLoop.cs, Assets/Scripts/Domains/Population/HouseholdShortage.cs, Assets/Scripts/Domains/Population/PersonScheduleTracker.cs, Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs, Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs. Plus this handoff document at the repo root.

New test files, 8 files with .meta files - BakerySuppliesTests.cs, FreightRepeatabilityTests.cs, HouseholdShoppingEndToEndTests.cs, HouseholdShoppingRepairTests.cs, HouseholdShoppingUnservableNeedsTests.cs, SinglePersonShoppingTests.cs, StoreOfferVisibilityTests.cs, DayLength120SecondsTests.cs. Modified test files - FreightShipmentServiceTests.cs, LivingWorldSim.cs, AutonomousWorkGeneratorTests.cs, SimulationTimeSettingsSpeedTests.cs.

### Potential collisions with local source changes

- SimulationDrivers.cs - Muse edited the OnDayChanged order, the freight stall logging sites, the forwarder cumulative diagnostics, and the daily shopping summary line. If the local project has newer edits to OnDayChanged or DriveDailyNeeds, merge by hand around those blocks - do not take either side wholesale. The new methods are DriveWorkAssignmentBeforeShopping and StartPreAssignedBusinessWork.
- ManagerDecisionExecution.cs - Muse refactored StartAssignedTasks into ReserveAndAssignTasks and StartReservedTasks. If local work changed the manager Schedule executor, keep the composed StartAssignedTasks entry point intact - the executor still calls it.
- FirstLedgerSliceBootstrapper.cs - Muse added the ungated RegisterNotionsProcurement method and its call. The horse-feed order stays inside the dev-gated block. If local work changed the bootstrap order, keep seed-before-port order at lines 545 and 575.
- HouseholdShoppingLoop.cs, PersonScheduleTracker.cs, HouseholdShortage.cs - Muse changed the window search, the departure-location lookup, and the unservable-need suppression. If local work changed the same methods, merge by hand.
- DefaultSimulationTimeSettings.asset - Muse changed one line, 60 to 120. If the local asset was edited for other reasons, keep the 120 value and merge the rest by hand. The .meta GUID b2f7e5b13b78b59419a4f872fe9e66dd is untouched.
- DayNightVisualController.cs, the HDRP Volume profile, the Main Scene, and terrain assets - Muse did NOT touch these. Local newer versions win. Do not overwrite them with repo versions.
- Scenes - there are no .unity scenes in the repo. Do not import scenes from the branch. Scene wiring for the time settings must be confirmed in the local scene, not copied.

### Required ScriptableObject and scene updates

- DefaultSimulationTimeSettings - the 120 value must be live in the asset the scene TimeManager references. Confirm the open scene TimeManager points at DefaultSimulationTimeSettings with no scene-local override.
- DevelopmentTownConfiguration - no value was changed by Muse. The bread opening-inventory range of 12 to 24 and the fuel endowment are read-only inputs to the verification plan.
- GeneralStoreBusinessDefinition - no value was changed by Muse. Catalog startingStockUnits of 24 for bread is not inventory and must not be treated as a receipt.

## Unity verification plan

Run in order. Quote log lines as evidence, do not summarize them. Recommended seeds - the previous manual test used world seed 1781894988 and market seed 1449120246, and the previous 1x test used world seed 485455302 and market seed 1474896984. Reuse those seeds where a check names them, because the harness expectations were built against them. Do not invent guaranteed outcomes from random world generation - where a check depends on seed-resolved quantities, the check names the seed and the expected value. Test duration is given per check. Each check states its mode - EditMode or PlayMode.

1 - Clean compilation verification - Mode EditMode. Open the integrated project in Unity 6000.4.2f1 with HDRP. Expected result - zero C# compile errors. Observable evidence - the Unity console shows no errors after reimport. Relevant logging - console errors with file and line. Known limitations - Roslyn harness compilation is not Unity compilation. If a compile error appears, stop and report it exactly.
2 - Focused Unity EditMode regression tests - Mode EditMode. Run the new test files through the Unity Test Runner - DayLength120SecondsTests, HouseholdShoppingRepairTests, HouseholdShoppingEndToEndTests, HouseholdShoppingUnservableNeedsTests, StoreOfferVisibilityTests, SinglePersonShoppingTests, FreightRepeatabilityTests, BakerySuppliesTests, AutonomousWorkGeneratorTests. Expected result - all tests in those files pass, plus no new failures elsewhere. Observable evidence - Test Runner summary counts. Known limitations - harness-excluded files are still not covered by these tests.
3 - Two-minute day at 1x - Mode PlayMode. Fresh Open Frontier NewGame, world seed 1781894988, market seed 1449120246, speed 1x, run at least one full in-game day. Expected result - one in-game day takes 120 real seconds. Observable evidence - clock readouts at day start and day end, timed. Known limitations - none established.
4 - Correct 10x and 25x progression - Mode PlayMode. Same run, switch to 10x for one day and 25x for one day. Expected result - 12 real seconds per day at 10x, 4.8 real seconds per day at 25x, with 1440 in-game minutes per day at every speed. Observable evidence - clock readouts and timed day lengths. Known limitations - the 250x option is not implemented yet.
5 - Exactly-once daily events - Mode PlayMode. Same run, at least 3 days. Expected result - each daily event fires exactly once per day, including DayChanged, the Bakery daily driver, and the shopping drive. Observable evidence - log lines with day indices, counted. Known limitations - none established.
6 - Fresh Open Frontier NewGame - Mode PlayMode. World seed 1781894988, market seed 1449120246. Expected result - 9 residents, 5 households, 9 buildings, the store opens, the Bakery exists, Millbrook is registered as the off-map supplier. Observable evidence - the WorldStartup report line and the opening-inventory report line, quoted. Known limitations - the dev build seeds 18 real bread loaves, the release build opens with zero sellable stock. Both are correct for their flavor - see check 7.
7 - Household shopping with actual store lots - Mode PlayMode, both build flavors. Dev build first. Expected result in the dev build - the store holds 18 identified bread loaves as real lots with lot id opening-endowment and owner and custodian equal to the store, ItemStockUnits for bread equals 18, and a household buys bread on day 1 with the lot count dropping by exactly the units sold. Expected result in the release build - the store holds zero sellable stock for every meal item on day 1, no household buys on day 1, and the first purchase happens only after a real receipt lands, with the log naming that receipt. Observable evidence - the opening-inventory report line, the store view, purchase log lines with receipt ids, and cash reconciliation. Relevant logging - the BakeryProduction, BakeryNotionsDailySupply, and feed log lines. Known limitations - the harness cannot prove either half. If the release build shows day-1 purchases from nothing, that is a defect - report it.
8 - A single employed adult completing a legitimate shopping errand - Mode PlayMode. Fresh run, a single-person employed household. Expected result - the work task starts at 480 over the rest sprinkle with the rest-release diagnostic quoted from the log, the trip departs after the shift with departure and work window both quoted, the purchase settles, and no shopping reservation overlaps a Work reservation. Observable evidence - schedule log lines and the purchase receipt. Known limitations - the trip must depart after the shift, never over it.
9 - Household money and inventory reconciliation - Mode PlayMode. Same run as check 8, at least 3 days. Expected result - buyer outflow equals units sold times 5c, store cash rises by exactly that amount, store stock falls by exactly the units sold, household inventory gains an identified lot with purchase provenance and receipt id, total cash and total bread are conserved, and no receivable exists. Observable evidence - cash and stock readouts before and after each purchase. Known limitations - none established.
10 - Consumption of purchased food - Mode PlayMode. Same run, next-day meal resolution. Expected result - at least one meal is served from the purchased lot, the meal log references the lot id, and the reserve is credited on receipt and decremented by the meal. Observable evidence - meal log lines with lot ids. Known limitations - bread is the only food proven end to end.
11 - Bakery notions delivery - Mode PlayMode. Fresh run, world seed 1781894988, market seed 1449120246, run through day 15. Expected result - a notions order is placed day 1, the lot arrives day 15 in the store service ledger, the feed moves 1 unit into the bin with Bakery minus 10c and store plus 10c, and the same planned batch bakes that day. Observable evidence - the notions order log, the arrival log, the feed log line, and the bake log line, all quoted. Known limitations - the 14-day lead is by design, so a run shorter than 15 days proves nothing here.
12 - Bakery flour replenishment - Mode PlayMode. Same run, through day 19. Expected result - the opening 1-sack Millbrook lot alone bakes nothing, the 2-sack reorder delivers, and the batch bakes once both lots land with bin 30 lb. Observable evidence - the reorder log, the custody adapter receipt lines, the bin readout, and the bake log line. Known limitations - flour price is the live store price, 29c per sack in tests, and Millbrook is 48c per sack plus 125c freight.
13 - Bakery production and wholesale receipt - Mode PlayMode. Same run, days 15 to 18. Expected result - 24 loaves baked and transferred daily, 96 total, with store minus 48c and Bakery plus 48c per transfer and the account advanced once per transfer. Observable evidence - the transfer log lines and the cumulative actuals line after the first bake. Known limitations - production blocks from day 19 on flour until the next reorder lands, which is expected behavior.
14 - Two successive Millbrook freight deliveries - Mode PlayMode. Fresh run, at least 24 days. Expected result - haul 1 completes with pickup, receipt, and 125c settlement, and haul 2 completes with a second 125c settlement, Bakery holding 2 lots of 10 lb with distinct ImportOrderIds. Observable evidence - the FreightPickup, FreightAgreement, OffMapJourney, and collection log lines, quoted. Known limitations - the D-plus-2 fallback covers a missed pickup, so pickup timing may vary by a day.
15 - Freight cargo custody and 125c settlement - Mode PlayMode. Same run as check 14. Expected result - cargo identity is preserved from pickup through delivery, the 125c charge settles exactly once per haul, and no duplicate delivery credit appears. Observable evidence - the custody log lines and the settlement lines with order ids. Known limitations - none established.
16 - Repeated shipment money reconciliation - Mode PlayMode. Same run as check 14. Expected result - payer minus 250c and carrier plus 250c total across the two hauls, with no duplicate debit or credit. Observable evidence - payer and carrier cash readouts before haul 1 and after haul 2. Known limitations - none established.
17 - Active shipment save and load - Mode PlayMode. Fresh run, start a haul, save mid-journey, load in a fresh scene, Continue. Expected result - the shipment resumes exactly once with no duplicate order, pickup, receipt, or settlement, and the haul completes. Observable evidence - the save and load log lines and the completion log lines. Known limitations - this is the source-level seam only - full Q04 Continue verification is a separate check, see check 18.
18 - Fresh-scene Continue restoring household, Bakery, and Millbrook state - Mode PlayMode. Fresh run, save in-flight, fresh scene, Continue. Expected result - household inventories, Bakery orders and production state, Millbrook lots, simulation time, and task reservations all restore with no duplicate seed stock, order, pickup, receipt, or settlement. Observable evidence - the Continue log lines and before-and-after state readouts. Known limitations - Q04 is NOT STARTED in source and this check is expected to expose defects. Record them exactly rather than working around them.
19 - Nighttime readability and HDRP presentation - Mode PlayMode. Night run in a fresh Open Frontier scene. Expected result - night stays visibly dark but readable, with buildings, people, roads, and nearby terrain distinguishable, and it does not look like daylight. Observable evidence - screenshots at night from at least three viewpoints, plus a note of the HDRP Volume and exposure settings in use. Known limitations - no source change was made for lighting in this campaign, so this check measures the current authored state and records what needs changing. Do not claim a fix from this check alone.
20 - Road, terrain, building, and character presentation regression checks - Mode PlayMode. Same run as check 19, daytime and night. Expected result - roads follow the logical network, buildings sit on terrain without visible floating gaps, characters and wagons are grounded, and no new presentation regression appears against the pre-campaign baseline. Observable evidence - screenshots from the same viewpoints as the pre-campaign baseline where available. Known limitations - terrain-painted roads, foundations, and 2.5D grounding were not implemented in this campaign, so this check records the current state for the next campaign.

## Remaining backlog

Prioritized for the next campaign. Classification values are COMPLETE, PARTIAL, NOT STARTED, BLOCKED, and REQUIRES UNITY VERIFICATION. Do not turn the next campaign into another broad audit. The Q01 to Q30 queue is retained and feeds the items below where noted.

### Revised priority 2 - single-person households and realistic shopping

- PARTIAL. Plan 05 Slices 0 to 4 complete in source. Slice 5 diagnostics - PARTIAL, in progress at the checkpoint, see the checkpoint note. Slice 6 acceptance - NOT STARTED. R-REMOTE workplace-departed trips - BLOCKED on a dispatch and pathing design decision, deferred to priority 6. The pre-check route at line 568 still uses member-0 home for supplier viability. The single-need path at line 204 and line 1259 carry the same estimate and leg mismatch shape. Commute shopping has no code path. All require Unity verification checks 7, 8, 9, and 10 above.

### Revised priority 3 - shared physical inventory foundation

- NOT STARTED. Major workstream. Data contracts and conservation rules in manageable tested slices. Reuse item and lot identities, ownership authorities, custody transfers, EquipmentCapacityContracts, EquipmentCapacityService, household reserves, business inventories, freight shipment services, wagon capacity and exclusivity, and persistence systems. Each tangible item spec supports stable product identity, quantity and unit, individual unit mass, dimensions or packaging envelope, volume, condition and perishability where applicable, container compatibility, ownership, current custody, storage location, and handling requirements where relevant. Avoid per-instance duplication of immutable product specs. Lots may represent multiple fungible units. Do not force every grain of flour into an individual GameObject. Preserve save compatibility where feasible. Use clearly identified provisional balance values where legitimate values are not established. Do not silently convert an illustrative value into an authoritative historical rule.

### Revised priority 4 - personal carrying capacity and portable storage

- NOT STARTED. Blocked on priority 3. Capacity rules consider strength, body size, age, health, fatigue, and equipment. Men on average capable of heavier or larger loads than women, implemented through character attributes and plausible population distributions, not one immutable limit determined solely by sex. Individual variation must remain possible. Support weight, volume, object dimensions, carrying equipment, handling method, encumbrance, movement speed, and fatigue. No unlimited carrying. Design portable containers incrementally - bags, baskets, pockets, lunch pails, saddlebags, horse packs, wagon cargo compartments. Equipment must actually exist and be owned, acquired, or assigned. Start with shared capacity contracts and atomic transfers. No visual assets in a source-only campaign.

### Revised priority 5 - household inventory inspection and anticipatory purchasing

- NOT STARTED. A household member may perform a short inventory-inspection activity with a reasonable initial duration of 5 in-game minutes, subject to existing task-duration architecture. Cadence depends on circumstances - daily quick checks, periodic complete inspections, extra checks when low, checks before long absences or journeys. Consider food remaining, expected consumption, condition and expiration, heating, clothing condition, medical needs, expected next shopping opportunity, purchasing power, and available members. A single worker with limited store access may need a larger reserve. Do not force unnecessary inspections when authoritative inventory state is already reliably known. Use existing household needs and task authorities.

### Revised priority 6 - packed meals and remote work

- NOT STARTED. Portable meals approved. Workers bring actual provisions - bread rolls, preserved foods, prepared meals in a lunch pail or bag, horse-mounted storage for riders. Real inventory transfers only - household inventory to personal or portable container to consumption. No free meals, no magically generated lunch pails, no instant access to provisions stored far away. Workers may eat away from home with accessible provisions and an appropriate opportunity, and may deliberately skip a meal. Distinguish intentional skipping, inability to afford food, food unavailable at location, and missed meal from a scheduling or implementation defect. Do not require every NPC to eat a fixed number of meals because a slot exists. Preserve nutrition and needs authority. D-6 remains open. R-REMOTE is the first slice here.

### Revised priority 7 - specific physical food products

- NOT STARTED. No vague products - staple_food, meat_unspecified, preserved_food - as actual goods in inventory, shops, shipments, or meals. Real inventory holds identifiable products. Wheat bread, rye bread, bread rolls, beef, salt pork, bacon, venison, potatoes, canned beans, and dried apples are examples, not permission to introduce every product into the opening economy. Generic categories may remain as internal planning classifications. Preserve the distinction between need categories, product definitions, SKU identities, packaging, inventory lots, and consumption eligibility. Audit existing names and mappings. Use current Canon for eligibility and identity. Where D-5 or D-6 is unresolved, record the decision required. No uncontrolled global rename that breaks serialized data or saves.

### Revised priority 8 - task duration diagnostics

- NOT STARTED. Lightweight, measure predicted versus actual task execution. Inspect TaskDurationEstimators, DurationContracts, TaskDurationEstimatorBridge, ActivityInstance timing, and task scheduling authorities. Do not create a second scheduling system. Track task creation time, expected start, actual start, expected and actual active duration, queue waiting, travel time, blocked or paused time, final completion time, and reason for legitimate delay. All times on the authoritative simulation clock. A 15-minute task taking two in-game days must be explainable. Actionable diagnostics for unjustified overruns - task, owner, expected, actual, state, likely blocked transition. Avoid console flooding with configurable thresholds, bounded diagnostics, and once-per-event or once-per-day logging. Good candidate for a short independent slice if existing timing hooks are present, and it may be done earlier if it helps diagnose ongoing defects.

### Revised priority 9 - variable production quantities

- NOT STARTED. Bakery first, do not rewrite all verticals. Smallest physically feasible output, generally one actual unit where recipe and equipment support it. Separate physical feasibility from economic desirability - a Bakery may be able to make one loaf but judge firing the oven uneconomical. Use recipe ratios and actual input requirements. Do not invent ingredients or fractional inventory. Account for ingredient quantities, equipment, labor, fixed setup costs, fuel, whole output units, waste, custody, and product quality where supported. Bakery chooses a smaller run or defers based on operating policy. Shared patterns for later extension.

### Revised priority 10 - nighttime readability

- NOT STARTED. High-impact presentation. Night stays visibly dark but readable. Investigate the HDRP day and night controller and exposure config. Newer local scene, Volume, and presentation modifications may not exist in this repo - do not overwrite them. Narrow source-level improvement if safe, otherwise precise Codex instructions. Longer-term - date-dependent moon phase and moonlight, historical and real-time mode aiming for realistic darkness, normal gameplay prioritizing readability, same date and moon phase underlie both. Do not make astronomical lighting a prerequisite for fixing the nearly black night. Requires Unity verification check 19.

### Revised priority 11 - freight consolidation and dedicated services

- NOT STARTED. Blocked on priorities 3 and 4. Both approved, different contractual services. Customer-initiated consolidation - multiple businesses agree in advance to share a carrier and costs, contracted prices reflect pooled service. Carrier-initiated consolidation - Ethan accepts a shipment at an agreed price and later uses available wagon capacity for additional compatible cargo, carrier retains the utilization benefit unless contract terms say otherwise. Dedicated freight - exclusive or priority use, carrier must respect restrictions. Build on shipment, wagon, custody, contract, and exclusivity authorities. Forbid cargo capacity violations, double-booked exclusive capacity, mixed-up ownership, duplicate delivery credits, duplicate settlements, and unauthorized sharing of dedicated trips. Do not implement consolidation before the necessary capacity and custody contracts exist.

### Revised priority 12 - freight profitability and operating costs

- NOT STARTED. The 125c charge stays as prototype. Evaluate against actual operating expenses - driver labor, self-employed owner labor opportunity cost, horse feed, wagon wear, harness wear, repairs, capital cost or depreciation, borrowing or rental, journey duration, cargo handling, empty returns, vehicle utilization, and revenue from additional customers. Do not raise 125c to an arbitrary historically inspired number. Functional economics before historically precise balance. Transparent provisional values where necessary. Self-employed owner versus hired employee have different cash expenses while both recognize the economic cost of time. Provide margin and cost breakdown. Preserve the distinction between actual business expenses and calculated opportunity costs.

### Revised priority 13 - household reserve accounting, 12c discrepancy

- NOT STARTED. Investigate, do not update test expectations without proving the correct economic result. Verify purchases, partial fills, retries, cash charges, inventory receipts, and duplicate prevention. The failing test is HouseholdReserveInventoryTests.PartialFillReattemptsOnlyTheRemainingDeficit, wallet 99844 versus expected 99856. Reconcile the 144c expected versus 156c implied charge from the fixture before changing production behavior or the assertion. Resolve the failed Unity-facing test where possible through source-level repairs, and leave Unity verification instructions.

### Revised priority 14 - save and load, Q04

- NOT STARTED. Continue the fresh-scene Continue repair. Preserve household inventories, personal inventories where implemented, active errands, goods in transit, freight shipments, vehicle custody, business inventory, Millbrook lots, Bakery orders, production state, simulation time, equipment ownership, and task reservations. Avoid duplicate loading, duplicate receipts, and double settlement. Source-level persistence checks are necessary but do not replace real Unity Continue verification. Audit identifiers AUD-274 and AUD-275. Requires Unity verification check 18.

### Revised priority 15 - terrain-painted roads

- NOT STARTED. Keep the authoritative road network. Investigate safe visual representation using terrain material layers. Do not destructively modify newly authored terrain. Do not permanently mutate shared TerrainData in PlayMode. Consider persistence, seeds, repeated NewGame, and performance. Reversible prototype behind an explicit toggle if source-only and low risk, otherwise a proposal for Codex. Requires Unity verification check 20.

### Revised priority 16 - building grounding and foundations

- NOT STARTED. Investigate small visual gaps beneath buildings. Determine whether they come from height sampling, slope, prefab pivots, root placement, visual mesh offsets, or existing grounding logic. Prefer correcting real placement errors. Optional foundations may cover small unavoidable gaps. Do not globally resize or bury buildings. Do not alter authored building prefabs in the Muse source branch. Requires Unity verification check 20.

### Revised priority 17 - 2.5D navigation and grounding

- NOT STARTED. Prototype and architecture task, not a large navigation rewrite. Route planning largely horizontal while an authoritative grounding system resolves surface elevation. Preserve logical route topology, terrain collision, walkable floors, stairs, slopes, building entrances, physical character root location, obstacle collision, and NPC and wagon navigation. Do not remove every wall or roof collider. Do not move only the rendered mesh while leaving the physical root at the wrong elevation. A simplified stair walking surface may be preferable to colliding with every decorative step.

### Other backlog items

- Audit queue Q05 AUD-144, Q06 AUD-016, Q07 AUD-182 and AUD-183, Q11 AUD-185 - NOT STARTED. Continue after higher-priority repairs or during suitable independent slices. Q10 is BLOCKED on D-5.
- Technical warnings classification - NOT STARTED. TempJob allocations, Burst compilation exception, negative-scale BoxCollider warnings. Classify origin - project code, Unity packages, renderer settings, or transient Editor state - and ownership. Do not modify Unity package sources. Do not claim the TempJob warning proves a persistent leak without reproduction.
- 250x speed option replacing 25x - NOT STARTED. Approved by Kennedy. Retain 100x where supported.
- Pipeline drain follow-up from Plan 04 - NOT STARTED. The notions pipeline shows a 14-on and 14-off cycle with stall days 29 to 42 after 14 consecutive daily bakes. Recorded as a follow-up, not a defect.
- Freight Work reserves share the rest refusal - NOT STARTED. SimulationDrivers lines 4369 and 4377 reserve Work for Ethan and Glen through strict TryReserve on the freight path. A future plan may apply the same one-line seam if a failing case is shown. Do not expand Plan 05 Slice 4 to cover it.
- Opening-stock policy - BLOCKED on an owner decision. Either confirm the live store intentionally opens empty and name its first stock source, or authorize a release opening endowment. The same decision covers the Bakery bootstrap endowments and the release-build fuel question.
- Employment leave policy - BLOCKED on an owner decision. Canon has no leave policy. If leave is ever wanted, an owner decision must name the policy - which employers grant it, for what, at what cost.
- R1 demand path - BLOCKED on an owner decision. Name the demand path before any fix to the store weekly Millbrook lot visibility.

### Next five implementation slices recommended after Codex testing

1 - Plan 05 Slice 6 acceptance. Depends on Slice 5 and on Codex checks 7 to 10. No owner decision needed. It closes the single-person shopping work with the A-to-H matrix proven end to end.
2 - Task duration diagnostics, short independent slice. Depends on nothing except the existing timing hooks. No owner decision needed. It gives the next campaign its diagnostic tool for every later defect.
3 - Q04 fresh-scene Continue, Pass 9 plus Millbrook. Depends on Codex check 18 evidence. No owner decision needed except where the check exposes a design fork. It is the highest-value save and load repair.
4 - 12c reserve discrepancy. Depends on nothing. No owner decision needed - the fixture arithmetic decides it. It closes the last failing Unity-facing test from Set A.
5 - Shared physical inventory foundation, first slice. Depends on priorities 3 and 4 sequencing and on the Codex check 7 and 8 evidence about what the store port actually sells. Owner decisions D-5 and D-6 stay open and bound the slice - no potato or salt weights, no meal-eligibility change. It starts the major workstream that priorities 4, 6, 7, and 11 all depend on.

## Owner decisions still open

- D-1 - wholesale units per delivery plus cadence, wholesale price evidence 5c baseline and 2c wholesale. Do not invent prices.
- D-2 and D-7 - Bakery fuel supply route plus IntakeUnitsPerBakeryUnit conversion, and fuel import execution design - business-cash payment port for ImportService, persistence for import orders, pre-Precheck reorder step. Do not place an import order until answered.
- D-5 with R1 - potato sack and salt bag package weights, flour known 10 lb per sack. R1 needs an owner decision naming the demand path.
- D-6 - meal eligibility plus daily use, preserved-goods identity is a D-6 decision.
- Release opening-stock policy - see the backlog above.
- Employment leave policy - see the backlog above.
- R-REMOTE dispatch and pathing design - see the backlog above.

## Integration hold

This campaign is in INTEGRATION HOLD as of about 2100 EST on 2026-11-07. Final code HEAD is 0ae3381a1a62a215b3fe20c8f8fd690c5464c07e, plus this handoff document as the final commit on the branch. No further development starts until Kennedy reauthorizes. No new plan, no new slice, no merge to main. The next step is Codex integration and Unity verification per the plan above, then the next campaign starts from the backlog above.

