# Pass XI - Implementation Plan

Dependency-ordered plan driven by Docs/Pass11_Unity_Integration_Readiness_Audit.md. Finding ids below (A-01 to A-37) refer to that audit. No gameplay code is changed by this plan document itself. Priorities run P0 (conservation and integrity, plus the Unity proof that gates every gameplay claim) through P4 (player-facing and performance). Every workstream states its source-only versus Unity-dependent requirements explicitly. Nothing in this plan may be labelled Unity compiled or Unity verified until a Unity session actually produces that evidence.

## Sequencing and dependencies

WS1 first, because every later workstream needs a trustworthy bootstrap, hub registration and persistence boundary. WS2 and WS3 run after WS1 in parallel, with WS2 owning live circulation and WS3 owning integrity defects. WS4 runs after WS2 and WS3, because an autonomous settlement proof is only meaningful on the fixed paths. WS5 runs after WS2 and WS4, because player-facing data must describe the fixed behaviour and measured performance must be measured on it. WS6 runs last and independently throughout as review, with a final independent pass that may not be staffed by any WS1 to WS5 author.

Dependency chain in one line - WS1, then WS2 and WS3, then WS4, then WS5, then WS6 final sign-off, with WS6 review gates after WS1, after WS3 and at the end.

## Decision gates (no work past a gate without the decision)

- G1 - Kennedy decision on PASS9-008 (RealDemandSource wire-or-retire). WS2 may not build an executor or retire the type before G1.
- G2 - Kennedy decision on PASS9-010 Class E items that WS3 touches (finite mineral reserve, severable mineral rights, bank officer identity, childcare and fatigue designs). WS3 implements only the decided items and records the rest as still deferred.
- G3 - Kennedy decision on foreclosure-auction design (PASS8-012) and household lending design (PASS8-013) before WS3 scopes either one. Both default to out of scope if undecided.
- G4 - Unity session confirmation of scene contents (which MonoBehaviours exist in the scene, which registry assets exist, which FreightCompanyRuntime instances if any are scene-placed, what the scene term boundaries are). WS1 Unity items and all of WS4 PlayMode proof wait on G4.
- G5 - RUN-006 Test Runner reliability. Unity test claims wait on G5. Roslyn harness results are never presented as Unity results.

## WS1 - Integration foundations

Priority - P0 for the Unity proof items, P1 for the source bootstrap items. Findings - A-01, A-02 (source half), A-04, A-12, A-21, A-22, A-31 (scene half), A-06 (registry half).

### Canon requirement

A chosen scenario actually begins, the hub owns every authority exactly once, drivers reach every authority, persistence boundaries round-trip, and the scene holds the objects the source expects. Canon here is the architecture rule that one authority owns each truth and the tick reaches it.

### Source files and classes

- Orchestration/Systems/SimulationDrivers.cs (Awake, AttachBusinessRuntime, SubscribeToTimeManager, OnScenarioTick)
- Orchestration/Systems/SimulationSystemsHub.cs (CaptureSaveDto, LoadFromSaveDto, RegisterBoardingHouseRuntime, ScenarioObjectives)
- Orchestration/Scenarios/ScenarioDirector.cs (SwitchScenario, CaptureSaveDto, RestoreFromSaveDto, RecordLiveGoalCompletion)
- Orchestration/Scenarios/ScenarioStartPanelController.cs (StartScenario)
- Orchestration/Scenarios/Generation/ScenarioObjectiveService.cs (BeginScenario, RegisterDatedQualifier, Evaluate, LoadRunState, BuildHeadlineSnapshot)
- Orchestration/Scenarios/Generation/ScenarioBootstrapFactory.cs (TryCreateStart, ResolveComposition)
- Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs (InitializeSlice, ConfigureScenarioAuthorities, AttachBusinessRuntime call site)
- Orchestration/Scenarios/FirstLedger/FirstLedgerBootstrap.cs and Orchestration/Scenarios/SaloonCircuit/SaloonCircuitBootstrap.cs (OnScenarioTick subscription, evaluator restore)
- Domains/Economy/Core/SharedBusinessRuntimeManager.cs (PreWeeklyResetCallback)
- Domains/Economy/Creation/BusinessCapabilities.cs (BusinessCapabilityRegistry)
- Domains/Economy/Core/EmploymentRelationship.cs (EmploymentRelationshipRegistry)
- Domains/Animals/AnimalRegistry.cs (AnimalRegistry)
- Docs/UnwiredSystems.md and Tests/Editor/Integration/UnwiredSystemsRegistryTests.cs plus Pass9RegistryAssemblyScanTests.cs (registry guard)

### Existing authority

Hub ownership, DTO save and load, Director start path, bootstrap ConfigureScenarioAuthorities, registry guard tests. All exist and are described in audit sections 1, 2, 3 and 7.

### Missing behaviour

- No production call to ScenarioObjectiveService.BeginScenario or Evaluate, and no qualifier re-registration after load.
- No single scenario-goal truth across director state, hub run state and evaluator list.
- No re-subscribe retry if TimeManager appears after drivers Awake and OnEnable, and PlayerDirector assignment only through one bootstrap call.
- BankRuntime missing from the registry.
- PreWeeklyResetCallback single-slot overwrite risk.
- Hub SerializeField fields on non-Serializable types.

### Root cause

Three scenario systems grew at different times (asset Director path, hub dated-qualifier path, per-scenario evaluator path) and were never unified. Drivers assume bootstrap order instead of re-binding. The registry guard only sees types inside the harness subset, so the Bank directory escapes it.

### Required changes

1. Define one scenario start contract in source. ScenarioDirector.SwitchScenario (or the start panel, whichever the scene actually uses) calls ScenarioObjectiveService.BeginScenario for the same scenario id, and a drivers daily hook calls Evaluate with the current absolute day index. Record the contract in the audit follow-up, not in a comment alone.
2. Persist qualifier registrations, not just latched ids. Store goal id plus deadline plus a stable predicate key per qualifier in the run state, and rebuild the predicate list from live game state on load. If a predicate cannot be rebuilt from live state, the goal must fail loudly in diagnostics rather than silently never evaluating.
3. Unify the three goal stores on load. One restore order (director state, then evaluator list, then hub latched list) with a reconciliation diagnostic when they disagree, and one capture order that writes all three from the reconciled state.
4. Add a drivers rebind entry point. A public Rebind method that re-runs the FindAnyObjectByType discovery plus subscription plus AttachBusinessRuntime wiring, callable by bootstrap after late object creation and safe to call twice (guarded by the existing subscribed flags).
5. Register BankRuntime in Docs/UnwiredSystems.md with its story, or wire it (WS2 decides under G1-style review, but registration alone is WS1). Update the scan test candidate mirror if the type is registered.
6. Make the weekly valuation post multicast or guarded. Either change PreWeeklyResetCallback to a multicast event with add and remove, or add a guard that refuses a second distinct setter with a loud diagnostic. Keep the drivers OnDisable clearing behaviour consistent with whichever shape is chosen.
7. Resolve the SerializeField question. Either add Serializable to BusinessCapabilityRegistry, EmploymentRelationshipRegistry and AnimalRegistry after checking their fields are all Unity-serializable, or remove SerializeField from the hub fields. Do not do both blindly. The DTO save path is unaffected either way and must be regression-tested after the change.
8. Unity items (need G4). Confirm the scene holds one hub, one drivers object, one manager, one TimeManager, the director, the panel, the registry assets and the two scenario bootstraps. Confirm the CalendarAnchor scene object matches the source anchor (default 1880-03-01, anchor frame day 60). Confirm whether any FreightCompanyRuntime instances are scene-placed. Record the scene inventory as a dated note, not as an assumption.

### Dependencies

None inside the plan. WS1 gates WS2, WS3 and WS4. G4 gates item 8 only, not items 1 to 7.

### Risks

- Unifying goal stores can change which goals show as complete in existing saves. Mitigate with an additive reconciliation that never un-completes a latched goal and never un-fails a failed scenario.
- Qualifier persistence keys must be stable across versions. Mitigate by keying on goal id plus scenario id, never on a delegate or a display string.
- Rebind called twice must not double-subscribe. The existing subscribed flags are the guard and must be covered by a test.

### Integration points

TimeManager events, SaveLoadManager restore order, FirstLedgerSliceBootstrapper Continue path (which must also run the new start contract and qualifier rebuild), puppet master panel goal reads.

### Save and load implications

Additive fields only. Old saves load with empty qualifier registrations and must rebuild them from live state on first load, with a diagnostic when a qualifier cannot be rebuilt. No renames, no enum changes, no .meta changes to pre-existing files.

### Performance implications

Negligible. One extra daily Evaluate call over a small qualifier list, one rebind pass at boot.

### Test strategy

- Roslyn harness - new tests for BeginScenario plus Evaluate daily driving, qualifier rebuild after a save round trip, three-store reconciliation (including disagreement diagnostics), rebind idempotence (subscribe once, bind twice, no duplicate drive), callback guard (second setter refused or multicast both fire), registry test still green with BankRuntime registered.
- Unity Test Runner (needs G5) - the existing goal-forwarding multicast test plus a new scene-presence smoke for item 8. Never report harness numbers as Unity numbers.

### Acceptance criteria

- A coded scenario started through the panel (or through bootScenarioId) has a non-empty ScenarioObjectiveService state, dated qualifiers registered, and Evaluate called on the day change.
- Save mid-scenario, reload, and Evaluate still runs with rebuilt qualifiers and the same latched and failed sets.
- Director, evaluator and hub goal sets agree after load, or disagree with a diagnostic and a defined winner.
- Drivers created after bootstrap still drive player work and owner labor after Rebind.
- Registry tests green with BankRuntime accounted for.
- Scene inventory note recorded with a date and a scene name.

### Source-only versus Unity-dependent

Items 1 to 7 are source-only. Item 8 and the Test Runner tests are Unity-dependent.

## WS2 - Live economic circulation

Priority - P1 for construction, banking and demand decisions. P2 for moves, distress sales, migration, illness-adjacent economy items that are circulation rather than policy. Findings - A-03, A-06 (bank half), A-07, A-10, A-11, A-25, A-26, A-28, A-30, A-32.

### Canon requirement

Production consumes real labor, equipment and stock. No automatic profit. No buyerless revenue. One cash writer, one debt authority, symmetric settlement. Businesses that exist in the world actually run, and businesses that close actually stop.

### Source files and classes

- Domains/Economy/Businesses/Construction/NpcCommissionedBuild.cs (NpcConstructionCommissioner)
- Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs (ConstructionProjectExecutor, AdvancePackage)
- Orchestration/Systems/SimulationDrivers.cs (new construction drive, freight handler verification)
- Orchestration/Systems/SimulationSystemsHub.cs (ConstructionProjects, NpcConstructionCommissioner, CreditCash)
- Domains/Economy/Businesses/Bank/BankRuntime.cs, BankDepositLedger in Domains/Economy/Financing/BankDeposits.cs, TellerOperations.cs
- Domains/Economy/Financing/BusinessLenderService.cs, PlayerDebtManager (deposit refresh path, RUN-003)
- Domains/Economy/Core/RealDemandSource.cs, RealDemandContracts.cs, SharedBusinessRuntimeManager.cs (CollectRealDemand, RegisterBusinessInputNeed)
- Domains/World/Property/HouseholdMoveService.cs, HousingAuthority (live move paths)
- Domains/Economy/Creation/NpcBusinessLifecycleDriver.cs (distress-sale buyer resolution)
- Domains/Economy/Businesses/Freight/FreightProductionDriver.cs (EnsureCompany, AdvanceTick handler)
- Domains/Economy/Creation/NpcBusinessLifecycle.cs (migration intents, if WS2 takes PASS8-006)
- Domains/Economy/Businesses/Mine (closure lifecycle, if WS2 takes PASS6-009)

### Existing authority

Construction full-chain services (tested, fixture-driven). Business lender cycle through the debt authority. CreditCash stores for NPC businesses and households. Freight verified collection. HousingAuthority and tenancy settlement. Entrepreneurship and lifecycle weekly drivers.

### Missing behaviour

- No production commissioning or advancement of NPC construction projects.
- No BankRuntime instance in production and no bank ledger registered as a CreditCash store. No live deposit refresh before underwriting.
- No RealDemandSource executor and no production caller for its manager wrappers. Intents are not persisted.
- No production caller for HouseholdMoveService.
- Distress sales always refuse for lack of a named buyer.
- Freight Invalid-handler behaviour unverified.
- Migration intents never become journeys. Mine closure lifecycle never runs live. Archetype outputs-at-resolution exist only for mine and farm.

### Root cause

Each of these services was built and tested in isolation and its driver was either never written (construction, moves, migration, mine closure) or never connected to the live authority it needs (bank ledger, named buyer, intent executor).

### Required changes

1. Construction driver. Add a drivers daily or weekly construction drive that, for each hub ConstructionProject, funds and stages the next ready package from the owning household real cash store through CreditCash, reserves labor through the employment authority, advances the package through ConstructionProjectExecutor.AdvancePackage, and handles cancellation with exactly-once restock. Commissioning itself needs a trigger rule (which NPC households commission, on what evidence, with what funding check) and that rule must be reviewed as a design input, not invented silently. Do not copy the Phase H fixture into production without that review.
2. Bank binding. Construct BankRuntime instances for bank businesses from the same business registry the manager uses, register each BankDepositLedger as the CreditCash store for that bank owner, and refresh deposits from the ledger immediately before underwriting reads them. If a player-owned bank has a scene ledger object, bind that object instead of constructing a second ledger, and fail loudly if both exist. This item needs G4 scene confirmation.
3. Demand decision (needs G1). If wired, build the intent executor on the production sale authorities (embodied shopping path and general store sale authority), persist intents that survive a day boundary, re-collect after failure, and ignore business input needs from unwired runtimes. If retired, delete RealDemandSource, its contracts interface if unused elsewhere, and the manager wrappers, and remove the registry entry with a dated note. Either outcome must keep the registry tests green.
4. Household moves. Choose the live move paths that must go through HouseholdMoveService (whole-household relocation on acquisition, tenancy change or estate settlement are the candidates) and route exactly those paths through it. Leave single-person occupancy changes on HousingAuthority direct paths and document the split. Update the registry entry accordingly.
5. Distress sales. Define the named-buyer source (a solvent buyer business through the appraisal liquidation port is the existing mechanism) and pass it instead of -1, or record a deliberate decision that distress sales stay refused until that source exists and remove the path from player-facing claims. Do not invent a buyer.
6. Freight handler. Verify AdvanceTick behaviour with an Invalid handler by test (dispatch, wage attribution, diagnostics) and either handle it explicitly (skip dispatch with a diagnostic, or resolve a fallback handler from the carrier business) or document it as safe.
7. Migration, mine closure and archetype outputs. Only if WS2 capacity allows after items 1 to 5. Each is a separate scoped driver (migration intents to OffMapJourneyAuthority, mine closure stages through the mine lifecycle, archetype outputs per business kind through the manager delegation pattern used for mine and farm). If any one is deferred, it stays in the register with its id, not silently dropped.

### Dependencies

WS1 (bootstrap, hub, registry, rebind). G1 for demand. G4 for bank scene binding. G2 only if mine closure touches mineral reserve questions.

### Risks

- Construction driver touches cash, labor, freight and stock at once. Mitigate with the existing full-chain tests plus new driver tests that start from a persisted project and advance exactly one package per drive.
- Bank binding risks double ledgers (scene object plus constructed instance). Mitigate with the fail-loud rule in item 2 and a test that constructs the binding twice.
- Demand executor risks double buying if intents are also satisfied by embodied shopping. Mitigate by consuming an intent exactly once on execution and by routing through the same sale authority, never around it.

### Integration points

CreditCashBridge, FinancialObligationAuthority, employment registry, freight driver, HousingAuthority, appraisal liquidation port, embodied shopping path, general store sale authority, SaveLoadManager.

### Save and load implications

Construction projects and commissioner state already persist, so the driver must be idempotent across a save taken mid-package (a package funded but not advanced must not fund twice). Intents, if wired, need a new additive DTO section with exactly-once consumption. Bank ledger state needs a DTO section if BankRuntime becomes live, or must reuse the existing BankRuntimeSaveDto through the systems DTO with an additive field. Household move results need no new DTO if moves complete atomically inside one drive.

### Performance implications

Construction drive is per-project per day or week and small. Bank refresh is per underwriting decision. Demand collection is per day and must reuse the existing supplier directory refresh, not rebuild it per intent.

### Test strategy

- Roslyn harness - construction driver tests (commission, fund, stage, advance, cancel with restock, save mid-package and resume), bank binding tests (single ledger, no double ledger, deposit refresh before underwriting, refusal when no store), demand tests per G1 outcome (executor exactly-once execution, or retirement with no dangling references), move tests (routed paths use the service, unrouted paths documented), distress-sale tests (named buyer executes, no buyer refuses), freight Invalid-handler test.
- Unity Test Runner (needs G5) - bank scene binding smoke and construction end-to-end in PlayMode (WS4 owns the PlayMode proof, WS2 owns the source tests).

### Acceptance criteria

- An NPC construction project created in production advances package by package on the driver and completes with usable spaces registered, or cancels with exactly-once restock.
- A bank business ledger is the same store underwriting reads, and a second ledger cannot silently appear.
- Demand is either executed through production sale authorities with exactly-once intent consumption, or fully retired with no references left.
- Routed household moves complete atomically or refuse before mutation.
- Distress sales either execute against a named buyer or are documented as refused by decision, not by accident.

### Source-only versus Unity-dependent

Items 1, 3, 4, 5, 6 and 7 are source-only. Item 2 is source plus Unity-dependent scene binding.

## WS3 - Remaining integrity defects

Priority - P1 for RUN-011, P2 for fee waterfall, disputes, Class E decided items, illness consumers if decided, P3 for the bounded defects. Findings - A-05, A-08, A-09, A-15, A-16, A-17, A-18, A-19, A-20, A-24 (decided subset), A-27 (if decided), A-29 (if decided), A-34 and A-35 (only if G3 decides them).

### Canon requirement

No phantom money, no phantom goods, one debt authority, symmetric settlement, exactly-once execution, taxes conserve into a civic store, lot identity is tracked at boundaries without a second inventory authority.

### Source files and classes

- Tests/Editor/Economy/MoneyOrderServiceTests.cs and Tests/Editor/World/P5PropertyLegalJourneyTests.cs (RUN-011, fix the production cause or the fixture, never the assertion to hide a defect)
- Domains/Economy/Businesses/PostOffice/MoneyOrderService.cs (settlement netting)
- Domains/Economy/Financing/NpcPropertyLifeService and NpcPropertyAcquisitionService (tax fee waterfall, dispute recording, levy ordering)
- Domains/Economy/Acquisitions/AcquisitionMarketManager.cs (RecordTitleAcquisition dispute recording, offer multiplier WeekToDateNetCents)
- Domains/Economy/Core/BusinessRuntimeState.cs (IsUnavailableForPayroll fail-open, named Payables, WeekToDateNetCents)
- Domains/Economy/Businesses/Mine/MineOreLots.cs (CreateSplit assay duplication)
- Domains/Economy/Businesses/Mine/MineOreStock (merge guard, already correct, regression test only)
- Orchestration/Systems/SimulationDrivers.cs (domestic watermark reset hook)
- Infrastructure/Persistence/SaveLoadManager.cs (drivers watermark reset on load, if the hook lives there)
- Domains/Economy/Businesses/AssetSale (execution record persistence, PASS9-001)
- Domains/Economy/Recruitment/RecruitmentService.cs and Domains/Economy/Businesses/PostOffice/RegisteredMailService.cs (postage payer contexts, PASS9-004)
- ReadModels/Reporting/BusinessReportBuilder.cs (owner-reporting distinctions, PASS9-009)
- Domains/Economy/Core/SharedCategoryStockLedger.cs (IDENT-001 interior migration, phased per domain)

### Existing authority

HouseholdLedger (cash), FinancialObligationAuthority (debt), BusinessRuntimeState settlement paths, CivicTreasuryService, SharedCategoryStockLedger provenance ledger, AssetSaleExecutionService exactly-once execution.

### Missing behaviour

- Four RUN-011 failures with causes still to be fixed, not just preserved.
- Fee portion computed from AmountPaidCents after partial payments, so fees can route to the civic treasury instead of the County Tax Collector. Player leg spends before booking, so a refusal on a satisfied assessment strands the debit.
- NPC distress-sale context built without the Disputes service, player-path claims not persisted, no parcel plus claimant plus basis dedup.
- Domestic watermark never reset on load.
- Ore split assay duplication.
- Offer multiplier on a financing-inclusive figure, payroll guard fails open.
- Asset-sale execution records runtime-only.
- Money-order advice postage sender unset, generic mail has no PostItem caller.
- Owner-reporting distinctions unaddressed.
- Class E items undecided (see G2). Illness consumers missing. Childcare task kinds undesigned.

### Root cause

Each defect is a seam left by an earlier pass (a computed field reused for a second purpose, a context built without one service, a drivers field outside every DTO, a split that copies a whole record instead of minting a new assay identity, a heuristic that was never re-pointed after the valuation feed fix).

### Required changes

1. RUN-011. Reproduce each failure, fix the production cause, and keep the test assertions unchanged unless the test itself is proven wrong with evidence. The P5 tax test needs the levy ordering question answered first (does the catch-up levy legitimately apply in the fixture year, or does the driver levy twice). Money-order netting needs the bilateral netting arithmetic fixed (the failing expectation is 2500 against an actual 3000 on a 3500 minus 500 netting case).
2. Fee waterfall. Track fees paid separately on the assessment, route fees to the County Tax Collector regardless of partial principal payments, and reorder the player leg to book before spending (or to spend through a compensating rollback on refusal, matching the teacher-pay pattern).
3. Disputes. Feed the Disputes service into the NPC distress-sale context, persist player-path claims additively, and add parcel plus claimant plus basis dedup to RecordClaim. Keep the diligence clue identity-safe (counts and certainty only).
4. Watermark. Give SimulationDrivers a reset entry point called by SaveLoadManager on load (and on New Game clear) that sets previousDomesticTickGameSeconds from the loaded clock, or move the watermark into a DTO-owned section. Cover with a test that loads an earlier-time save and advances domestic labor on the next tick.
5. Assay. Mint a fresh assay identity on split (child starts unassayed, or gets its own assay record), never copy assayId and assayed flag. Add a regression test that splits an assayed lot and asserts exactly one lot carries the original assay.
6. Offer multiplier and payroll guard. Re-point the weak-performance multiplier at the post-operating-profit valuation feed (the fixed feed, not WeekToDateNetCents). For IsUnavailableForPayroll, either keep the deliberate fail-open with a loud diagnostic and a bounded fallback, or change it to fail closed with a named Payable path, and record which one was chosen with the reason.
7. Asset-sale records. Persist execution records additively (action key, asset id, parties, amounts, day index) through the business DTO and refuse re-execution from the persisted record as well as from runtime state.
8. Postage. Either supply a funded sender for the money-order advice letter context or record a decision that advice postage stays unfunded and is never collected. For generic mail, either add a production PostItem caller with a real sender or record that generic mail stays unwired. Do not invent a sender.
9. Owner reporting. Implement the A3-11 distinctions (owner financing versus operating revenue, owner labor versus payroll, owner draws versus business expense) in BusinessReportBuilder from the existing ledger entry kinds, including the append-only Financing kind, and add tests that a financing inflow never appears as operating profit.
10. Class E, illness, childcare, foreclosure auction, household lending. Implement only the G2 and G3 decided subset, each as its own small change with its own tests. Everything undecided stays in the register with its id.

### Dependencies

WS1 (persistence boundary rules, registry guard). WS2 only where a fix touches a driver WS2 owns (watermark hook, dispute context construction). G2 and G3 for items 10.

### Risks

- RUN-011 fixes can change simulation outcomes in the 90-day and Ruston suites. Mitigate by running the full harness plus the five required suites (90-day, Ruston Decline, TickSize, ThreeYear, SaveLoad) after each fix, not only at the end.
- Fee routing changes can move money between civic treasury and collector in existing saves. Mitigate with additive fee-paid tracking that defaults old assessments to the previous behaviour and applies the new routing only to new payments.
- Assay identity changes can strand existing split lots in old saves. Mitigate by migrating loaded split lots (a child carrying a copied assay id gets its own assay record or is marked unassayed with a diagnostic, never silently re-certified).

### Integration points

Tax collection, civic treasury, acquisition market, payroll, mine chain, asset-sale execution, postal services, report builders, SaveLoadManager.

### Save and load implications

Additive DTO fields only (fees paid per assessment, dispute claims, asset-sale records, watermark or its replacement). Old saves must load with safe defaults and must not re-settle anything on load.

### Performance implications

None material. Dedup checks are per-claim dictionary lookups. Fee tracking is per-assessment arithmetic.

### Test strategy

- Roslyn harness - RUN-011 fixes with unchanged assertions (or proven fixture corrections with evidence), fee waterfall partial-payment tests (fee recipient correct after partial principal), dispute tests (NPC context fed, player claims persist, dedup refuses a second identical claim), watermark load test, assay split test, offer multiplier test on the fixed feed, asset-sale record round-trip test, postage decision tests, owner-reporting tests.
- Full harness plus the five required suites after each fix. No assertion edits to hide a defect. Any fixture correction needs a written reason in the commit message.

### Acceptance criteria

- Full harness shows fewer than 4 failures, with each fixed RUN-011 test named in the commit and no new failures.
- Partial tax payments route fees to the collector and never strand a player debit on refusal.
- Duplicate dispute claims are refused and player claims survive a save round trip.
- Domestic labor advances on the first tick after loading an earlier-time save.
- A split assayed lot yields exactly one lot carrying the original assay.
- Asset-sale records survive a save round trip and still refuse re-execution.

### Source-only versus Unity-dependent

Entirely source-only, except that fee and dispute display consequences (if any) belong to WS5 Unity work.

## WS4 - Autonomous settlement proof

Priority - P0 for the Unity PlayMode proof (it gates every gameplay claim), P1 for the production-path 30-day harness proof. Findings - A-01 (PlayMode half), A-02 (scenario half), A-11, A-25 (verification half), plus RUN-001, RUN-004, RUN-005, RUN-012 as Unity proof items.

### Canon requirement

The settlement runs itself for 30 days with no invented money, goods or buyers, survives save and load mid-flight, and degrades honestly under adversity instead of minting or vanishing value.

### Source files and classes

- Tests/Editor/Integration (90-day, Ruston Decline, TickSize, ThreeYear, SaveLoad suites, plus new production-path 30-day harness)
- Orchestration/Systems/SimulationDrivers.cs (daily and weekly drives under proof)
- Domains/Economy/Core/SharedBusinessRuntimeManager.cs (weekly settlement under proof)
- Infrastructure/Persistence/SaveLoadManager.cs (mid-shipment and mid-production save points)
- Tests/PlayMode/PlayableScenarioStartupPlayModeTests.cs (existing PlayMode file, Unity-only)

### Existing authority

90-day suite (6 tests), Ruston Decline adverse circulation suite (8 tests), TickSize suite (6 tests), ThreeYear suite (5 tests), SaveLoad suite (69 tests), all green on the Pass IX tree and re-verified at 1285 passed and 4 failed on the Pass XI base.

### Missing behaviour

- No production-path 30-day harness run that starts from a coded scenario (Director start path plus WS1 objective driving), runs the full daily and weekly drives, and reconciles cash, goods and debts at the end. The existing 90-day suite is the closest ancestor but is not a scenario-start proof.
- No PlayMode proof at all. RUN-004 (Main Scene 30-day autonomous economic cycle) is open. The Pass IX handoff beats are unrun (New Game into a coded scenario, 30 days autonomous, one mine week, one freight delivery end to end, one asset sale, one tax day for a player parcel and an NPC holding, save mid-shipment and reload).
- RUN-001 boarding end-to-end proof (genuine discovery, check-in, accommodation rights, rent, save and load) is open.
- RUN-012 Gregorian calendar and scenario migration verification in a normal game is open.

### Root cause

Proof was built around service-level suites and scripted drivers, not around the scene start path, because the scene start path itself was never fully wired (see WS1). WS4 cannot precede WS1.

### Required changes

1. Production-path 30-day harness. Build a harness scenario that uses the WS1 start contract, runs TimeManager day and week events for 30 days through SimulationDrivers and the manager (or through the closest production-faithful driver assembly the harness can construct), and asserts end reconciliation (every cent accounted, every good accounted, every debt accounted, no uncollected freight credit, no escrowed wage lost, no named Payable settled twice). Include one mine week, one freight delivery, one asset sale, one player tax day and one NPC tax day, and one save and reload at a mid-shipment point with continued execution after reload.
2. Adversity scenarios in the harness. Payroll unaffordable, supplier empty, undeliverable shipment, uneconomic mine, borrower default, provider loss, business closure, construction material exhaustion, asset transfer then ownership change, objective met then ownership change. Reuse the Ruston Decline pattern and assert conservation in each case, not just survival.
3. Conservation reporting. The proof must print a reconciliation summary (cash in versus cash out plus Payables, goods produced versus consumed versus stocked versus shipped, debts opened versus settled) so a failure names the leaking path.
4. Unity items (need G4 and G5). Run the Unity Test Runner edit-mode suite once and record the result separately from harness numbers. Run the Pass IX PlayMode beats in order (New Game into a coded scenario, 30 days autonomous, one mine week, one freight delivery end to end, one asset sale, one tax day for a player parcel and an NPC holding, save mid-shipment and reload). Record RUN-004, RUN-001, RUN-005 and RUN-012 outcomes separately with scene names and dates. Do not merge Unity results into harness counts.

### Dependencies

WS1 (start contract, qualifier rebuild, rebind). WS2 (construction, bank, demand outcomes must be in the proof if they are live by then, otherwise the proof must record them as excluded with reasons). WS3 (integrity fixes must be in, otherwise the proof measures the old defects).

### Risks

- A harness proof that uses scripted drivers instead of production drivers proves nothing. Mitigate by requiring the proof to go through SimulationDrivers day and week methods and the manager WeekChanged path, and by having WS6 check that requirement explicitly.
- PlayMode flakiness (RUN-006) can waste a session. Mitigate by running the edit-mode suite first, then the beats in order, stopping at the first beat that fails instead of pushing through.

### Integration points

Every WS1 to WS3 system. SaveLoadManager. Scenario bootstraps. TownWorldController generation.

### Save and load implications

The proof must include at least one mid-shipment save, one mid-production save and one mid-term schoolhouse save, each followed by continued execution that still reconciles.

### Performance implications

The proof must record wall-clock time for the 30-day run and for save plus load at campaign scale, feeding WS5 measurement (PASS9-013). No optimization is attempted inside WS4.

### Test strategy

- Roslyn harness - the new 30-day production-path suite plus the adversity suites, all green alongside the five required suites.
- Unity Test Runner and PlayMode - beats recorded separately, with failures filed as findings rather than worked around in the proof.

### Acceptance criteria

- 30-day production-path harness reconciles cash, goods and debts exactly, with the beat list above all exercised.
- Every adversity scenario conserves and degrades honestly (refusal, Payable, Delayed shipment, closure) with no invented value.
- PlayMode beats recorded with a scene name and a date, pass or fail, without mixing counts into harness numbers.
- RUN-004 either closes with evidence or stays open with the failing beat named.

### Source-only versus Unity-dependent

The harness proof is source-only. The Test Runner and PlayMode beats are Unity-dependent.

## WS5 - Player-facing source and performance

Priority - P3 for source read models and feedback correctness, P4 for measured optimizations. Findings - A-13 (notification half), A-14 (deadline display half), A-20 (reporting half), A-31 (catalog half, source side), A-33 (Unity side, not WS5 source), A-36, A-37 (source-visible halves), LL-PLAY-005, LL-PLAY-009, LL-PLAY-013, LL-PLAY-017, LL-PLAY-018 (source data halves).

### Canon requirement

What the player sees must be what the simulation did. Dates, deadlines, money, ownership and inspection data must come from live registers, never from invented or stale values. No confident valuation without evidence. Private information stays private.

### Source files and classes

- Orchestration/Scenarios/Generation/ScenarioObjectiveService.cs (BuildHeadlineSnapshot deadline after load)
- Orchestration/Scenarios/ScenarioDirector.cs (RecordLiveGoalCompletion, headline reads)
- Orchestration/Scenarios/ScenarioStartPanelController.cs (objective panel refresh)
- ReadModels/Reporting/BusinessReportBuilder.cs, PortfolioReportBuilder.cs, ReportSnapshots.cs (weekly and monthly summaries)
- ReadModels/Valuation (EnterpriseValuationReadModel feeds)
- Domains/Economy/Acquisitions/AcquisitionMarketManager.cs (diligence clue, sale-discovery provenance)
- Domains/Civic/SchoolhouseModels.cs (SchoolhousePresentationUtility summaries)
- Domains/Economy/Core/SharedBusinessRuntimeManager.cs (BusinessReadouts partial)
- Infrastructure/Persistence/SaveLoadManager.cs (save and restore timing hooks for measurement)

### Existing authority

BusinessReportBuilder weekly and monthly summaries from snapshots. Valuation read model fed by weekly settlement. Diligence clue identity-safe. Civic snapshot per-frame cache. StartPanel 0.5 second objective refresh.

### Missing behaviour

- Headline deadline -1 after load.
- Scenario failure notification absent.
- Owner-reporting distinctions (WS3 owns the ledger fix, WS5 owns the read-model display of it).
- Sale-discovery provenance missing in player-facing data (LL-PLAY-009).
- Financial-period presentation incomplete (LL-PLAY-013).
- Store startup and restocking notices confusing (LL-PLAY-018, source data half).
- Campaign-scale save and restore measurement outstanding (PASS9-013).

### Root cause

Read models were built for service correctness, not for a player reading them after a load, after a failure, or mid-period. Diagnostics stayed in logs because no read model owned the player-visible outcome.

### Required changes

1. Fix the headline deadline after load. BuildHeadlineSnapshot must read deadlines from the persisted qualifier registration (WS1 item 2) when the live qualifier list is empty, and must never report -1 for a goal that has a deadline in the run state.
2. Wire scenario failure notification to an existing surface. Subscribe to ScenarioFailed in the bootstrap or panel layer that already owns scenario UI, and surface scenario id plus goal id plus deadline day. If no suitable surface exists, record a deliberate decision and keep failure visible in the scenario panel state instead of inventing a new notification system.
3. Finish the read-model halves of WS3 reporting fixes. Weekly and monthly summaries must separate owner financing from operating revenue, owner labor from payroll, and draws from expenses, using the ledger entry kinds WS3 fixes.
4. Sale-discovery provenance. Expose the existing provenance records (SharedCategoryStockLedger records, FarmFlowLedger sold flags, asset-sale records once WS3 persists them) through the diligence and sale read paths that already exist, without leaking hidden state (claimant identity stays hidden, per the Pass IX clue rule).
5. Measurement only, no speculative tuning. Profile save plus load at campaign scale (large schedule, knowledge and shipment histories), profile the StartPanel objective refresh path, and profile the weekly manager iteration. Optimize only a measured hotspot, with the same equivalence discipline Pass IX used (byte-identical DTO output or a proven equivalence test). If no hotspot exceeds an agreed threshold, report that and stop.

### Dependencies

WS1 (qualifier persistence, goal reconciliation). WS3 (reporting ledger fixes, asset-sale record persistence). WS4 (campaign-scale save data for measurement).

### Risks

- Read-model changes can leak private state (claimant identity, hidden diligence). Mitigate with the existing clue rule as a test (no claimant identity in any player-facing string).
- Notification wiring can double-fire if both bootstraps subscribe. Mitigate with the multicast discipline already used for OnScenarioTick and a test that two subscribers produce one player-visible notice per failure.

### Integration points

Scenario panel, puppet master panel, report builders, diligence service, provenance ledger, SaveLoadManager timing.

### Save and load implications

None beyond WS1 qualifier persistence. Read models must be rebuildable from loaded DTOs without extra persisted state.

### Performance implications

This workstream owns measurement. No optimization without a measured hotspot and an equivalence test.

### Test strategy

- Roslyn harness - headline deadline after load, failure notification exactly once with two subscribers, financing versus operating separation in weekly and monthly summaries, provenance exposure without identity leak, save and load timing tests at campaign scale.
- Unity Test Runner (needs G5) - panel refresh behaviour only. No UI redesign is attempted here.

### Acceptance criteria

- Headline deadline is correct after a save round trip.
- A scenario failure produces exactly one player-visible notice naming scenario, goal and deadline.
- Weekly and monthly summaries never count financing as operating profit.
- Sale provenance is visible where diligence data is shown, with no claimant identity leaked.
- Campaign-scale save and load timings are recorded, and any optimization has an equivalence test.

### Source-only versus Unity-dependent

Items 1, 3, 4 and 5 are source-only. Item 2 is source plus the existing Unity UI surface it attaches to. No UI redesign is in scope.

## WS6 - Independent review

Priority - P0 as a gate, not as a workstream that ships features. Findings - all, as a check on all.

### Canon requirement

Every claim in WS1 to WS5 must be verified by someone who did not author it, against the tree, with production-caller evidence, not with prose.

### Source files and classes

Everything WS1 to WS5 touched, plus the full harness suite, plus the registry tests, plus the Unity results recorded by WS4.

### Existing authority

The Pass IX independent authority audit is the model. It caught five overstated production claims and two newly introduced defects in Pass IX, and its verdict method (full diff read, production-caller greps across the whole tree, independent harness runs) is adopted here.

### Missing behaviour

Independent review does not exist yet for Pass XI. This workstream creates it.

### Root cause

Authors overstate wiring. The Pass IX review proved that a blocking review round is the only reliable correction.

### Required changes

1. Review gate after WS1. Verify the start contract with production-caller greps, verify qualifier rebuild after load by test, verify the three-store reconciliation, verify rebind idempotence, verify the registry still guards (including BankRuntime).
2. Review gate after WS3. Verify RUN-011 fixes did not edit assertions to hide defects, verify fee routing with partial-payment tests, verify dispute dedup and persistence, verify assay identity, verify watermark reset, verify no enum renames and no non-additive DTO changes.
3. Final review. Full diff read of the whole pass, production-caller greps for every newly wired system, independent full harness run plus the five required suites, independent check that no finding was marked fixed without a test, and a verdict document that names any overstated claim and blocks the pass until it is fixed or honestly registered.
4. Unity results check. Verify that no Unity claim in any WS4 note exceeds what the Unity session actually recorded, and that harness numbers and Unity numbers were never mixed.

### Dependencies

WS1, WS3 and WS5 outputs. WS6 reviewers must not author WS1 to WS5 code.

### Risks

- Review without teeth repeats the Pass IX first-merge problem (claims merged before review). Mitigate by making the gates blocking in the plan, not advisory.
- Review scope creep (reviewer redesigns instead of verifying). Mitigate by limiting WS6 to verification and registration of findings, with redesign returned to the owning workstream.

### Integration points

All workstreams. Coordinator handoff.

### Save and load implications

WS6 verifies additive-only DTO discipline across the whole pass.

### Performance implications

WS6 verifies that WS5 optimizations kept equivalence.

### Test strategy

Independent harness runs by the reviewer, not by the authors. Registry tests run by the reviewer. Production-caller greps repeated by the reviewer.

### Acceptance criteria

- Two gate reviews and one final verdict document exist, each naming what was checked and what failed.
- No WS1 to WS5 claim ships without a test or a Unity record behind it.
- The pass closes only on the final verdict, not on author assertion.

### Source-only versus Unity-dependent

Review of source is source-only. Review of Unity claims needs the Unity session records, not a second Unity session.

## What is NOT in scope

- No broad rewrite of any subsystem, including the 11290-line manager. Surgical changes only, per the repo governance rules.
- No time-scale implementation. Time-scale work is research and planning only in a later phase.
- No new industries and no new business kinds.
- No UI redesign. WS5 attaches to existing surfaces only.
- No water or sanitation mechanics (PASS8-016, PASS7-002). Policy decisions for water stay deferred.
- No proposal from the unapproved 106 to 145 set is implemented.
- No scene, prefab, terrain, Package or ProjectSettings edits from source workstreams. Scene work happens only in the Unity session items (WS1 item 8, WS4 beats) and is recorded, not assumed.
- No Canon document edits. Canon changes need Kennedy and a separate decision.
- No silent wiring of any registered unwired system. Every wiring goes through the registry guard and a dated registry update.

## What blocks implementation workers from starting

- Nothing blocks WS1 source items 1 to 7, WS3 source items, or WS6 planning. Those can start immediately on the audit branch pattern (fresh branch from muse/continuous-development, isolated worktree, no stash in a worktree, isolated build outputs).
- G1 blocks WS2 demand work only, not the rest of WS2.
- G2 blocks only the Class E subset of WS3 item 10.
- G3 blocks only foreclosure auction and household lending.
- G4 blocks WS1 item 8, WS2 bank scene binding and all WS4 PlayMode beats. It does not block source work.
- G5 blocks Unity test claims only. Roslyn harness work proceeds regardless.
- The single largest schedule risk is not a code risk. It is the Unity session itself (scene access, Test Runner reliability under RUN-006, and PlayMode stability). Source work should proceed while the Unity session is arranged, and no gameplay claim should be made until WS4 PlayMode beats are recorded.
