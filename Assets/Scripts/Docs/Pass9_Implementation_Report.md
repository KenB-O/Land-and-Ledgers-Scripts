# Pass IX — Implementation Report

Baseline: 147503f (Pass VIII final), 1116 passed / 4 failed on the Roslyn harness.
Final tree: 1285 passed / 4 failed, the failures being exactly the historical RUN-011 set. No test was deleted and no assertion was edited to hide a defect.
Completion levels use the brief's scale (Implemented / Connected / Source exercised / Unity verified). Nothing in this pass is Unity verified. Files that sit outside the Roslyn harness subset (SimulationDrivers.cs, SharedBusinessRuntimeManager.cs, SchoolhouseModels.cs, SimulationSystemsHub.cs) are syntax-checked only, and every workstream below says where that applies.

## WS1 — Economic integrity (audit A3-01 to A3-06, A3-10, A3-12, A4-14)

- Canon requirement: no phantom money; one cash writer; one debt authority; symmetric settlement.
- Starting state: a service-revenue multiplier minted business cash; freight charges credited the carrier without a guaranteed payer debit; postage was collected from senders who were never debited; founding capital was granted; financing proceeds fed operating profit and lender loans were invisible to equity; the payroll fallback could strand wages.
- Changes: the multiplier is deleted and service revenue is exactly the measured household debit; freight charges settle both legs or record a named Payable to the carrier; founding cash is spent from owner cash first; valuation feeds post operating profit only and liabilities include authority-only debt; payroll deducts business cash first and escrows a failed household credit into a named Payable with CashAlreadyDeducted; the sawmill construction lumber sale settles through measured consumption (the audit suspicion was reproduced as a failing test first, then fixed).
- Authorities: HouseholdLedger (cash), FinancialObligationAuthority (debt), BusinessRuntimeState settlement paths.
- Production callers: SharedBusinessRuntimeManager weekly resolution and LogisticsRuntimeManager (harness-excluded file, syntax-checked; the settlement logic itself lives in harness-covered files and is exercised there).
- Test evidence: Pass9Ws1 suite, 25 tests, all passing, including production-path freight charge, postage, and collect-on-delivery cases.
- Limitations: the postage payer seam and the verified freight collection shipped as seams in this workstream and were only wired to production callers in the review-fix round (see the review section below). Save/load: named Payable persistence was missing at this stage and was fixed in the review-fix round.
- Completion level: Connected, with the two seam limitations closed by the review-fix round.

## WS2 — People and lifecycle (audit A2-01 to A2-14)

- Canon requirement: person-time is exclusive, lifecycle events run on production drivers, no ghost wages.
- Starting state: conception read a stale household id on PersonState, so post-generation marriages were sterile; migration never ended employment; the attendance provider was uninstalled in production; several daily systems (domestic labor, activity planning, social drive, job security, estate advancement) had no production caller; the daily estate step required by PASS7-001 was missing.
- Changes: conception reads HouseholdMembershipRegistry; MigrateOut ends employments with the appended EmploymentEndReason.MigratedOut and payroll refuses off-map workers; the production WeeklyAttendanceAccumulator is installed and fed daily; DomesticLaborDriver.AdvanceTick runs from OnShortTick with stale-schedule pruning; DailyActivityPlanner, DailySocialDrive, and WorkerJobSecurityEvaluator run on the day change; DriveDemographicDay runs a daily estate step; GetCurrentSpouseId derives from the relationship registry (PASS6-003).
- Production callers: SimulationDrivers day-change and short-tick paths (harness-excluded file, syntax-checked; the driven services are harness-covered and exercised).
- Test evidence: Pass9PeopleLifecycle suite, 17 tests, all passing.
- Limitations: childcare and caregiving task kinds remain undesigned (Class E, PASS8-004 remainder).
- Completion level: Connected.

## WS3 — Mine, freight, and farm production (PASS8-001, PASS8-002, PASS7-004)

- Canon requirement: production consumes real labor, equipment, and stock; no automatic profit; rejected deliveries never disappear.
- Starting state: the detailed mine chain, the freight runtime, and the farm cycle were built and tested with zero production callers, while an abstract weekly path created ore and profit directly.
- Changes: MineProductionDriver drives the mine weekly and SharedBusinessRuntimeManager.ResolveMineOperation delegates to it, retiring the abstract path; a single RemainingRichness01 dial is the only depletion authority (double-depletion regression test included); ore sells only against a named counterparty on delivery. FreightProductionDriver owns FreightCompanyRuntime instances through SimulationDrivers, with equipment exclusivity checked before dispatch. FarmCycleDriver runs the farm cycle and ResolveFarmOperation delegates to it.
- Test evidence: 15 new tests in this workstream plus 2 farm reconciliation tests, all passing; freight and mine suites green in the full run.
- Limitations: farm abstract input procurement was retired as a documented behavior change (inputs now flow through the driver's real paths). Unity needs: FreightCompanyRuntime scene placement is in the Unity handoff.
- Completion level: Connected.

## WS4 — Performance (PASS8-009, audit A4-15 to A4-17)

- Canon requirement: preserve history and determinism; never skip events; index before tuning.
- Changes: PersonScheduleTracker (person, day) buckets; KnowledgeTransmissionStore indexes for duplicate-telling refusal and per-recipient packets; a due-event binary heap with lazy stale-entry discard; a manager in-transit shipment subset. DTO output is byte-identical to the pre-index code, proven by golden serializations, and a seeded 400-step operation stream matches an independent reference model step for step.
- Test evidence: Pass9Performance suite, 14 tests, all passing. Benchmarks: Docs/Pass9_Performance_Benchmarks.md.
- Limitations: two low theoretical notes (index key staleness if key fields were mutated after insert, and heap order among hand-built duplicate sequence pairs) were recorded by the review; no production mutators exist.
- Completion level: Connected.

## WS5 — Appraisal, manager decisions, and asset sales (PASS8-003, audit A3-07, A3-13)

- Canon requirement: no confident valuation without evidence; private information stays private; sales move title through the legal authority exactly once.
- Starting state: the production appraisal seam was null, so sale requests were conservatively denied; the manager cycle builder hard-coded borrowing capacity to zero and was never called weekly.
- Changes: ManagerCycleInputsBuilder derives all seven decision kinds from live state and the weekly cycle runs weekly; borrowing capacity resolves from StandingInstructions; the market-capture feed is live. AssetSaleAppraisalService values from live registers only (asking, estimated, sale, gross, equity, operating, and liquidation values kept distinct) and returns no value without evidence, which the owner policy turns into a denial. AssetSaleExecutionService executes exactly once, transfers title through EquipmentAsset.TransferOwnership, and reverses both cash legs on a late refusal. A registered liquidation port replaces the null seam in CloseOrderly.
- Test evidence: AssetSale suite, 17 tests, plus appraisal and manager tests inside the workstream's 30 new tests, all passing.
- Limitations: executed sale records are runtime-only (PASS9-001); formation-asset scoping defects found by the review were fixed in the review-fix round.
- Completion level: Connected.

## WS6 — Civic treasury, scenarios, identity, registry (audit A1 and A4 items, IDENT-001)

- Canon requirement: taxes conserve into a civic store; scenario state is exact; lot identity is tracked at boundaries without a second inventory authority.
- Changes: hub-owned CivicTreasuryService with measured credit legs for player and NPC tax payment; NPC property life is driven daily; teacher pay at the SchoolTermWageCents rate of 12000 with a shortfall recorded as exactly one FinancialObligationAuthority Payable; dispute claims record at AcquisitionMarketManager.RecordTitleAcquisition and the diligence clue exposes counts and certainty only, never claimant identity; ScenarioBootstrapFactory and ScenarioDateUtil anchor on the canonical calendar (DaysBeforeAnchorYear is 3652; the 3478 figure in circulation is arithmetically wrong); scenario failure state persists exactly once through ScenarioRunState; road rectangle merging is wired into TownWorldController.BuildRoadVisuals; IDENT-001 boundary provenance lands through SharedCategoryStockLedger (RecordProductionOutput, RecordExternalSupply, RecordArrival) with SplitLot and MergeCompatibleLots on HouseholdInventory and MineOreStock; RealDemandSource is registered in Docs/UnwiredSystems.md as honestly unwired because no executor exists; an assembly-scan registry test guards the unwired registry with a shrink-only baseline.
- Test evidence: Pass9CivicTreasury 8, Pass9Schoolhouse 5 (8 after the review-fix round), Pass9Scenario 9, Pass9PropertyDispute 6, Pass9Registry 2, all passing.
- Limitations at this stage: teacher pay had no household credit leg and no production caller; NPC tax inputs were unwired; scenario objectives were undriven. All three were review findings and were fixed or honestly registered in the review-fix round.
- Completion level: Connected after the review-fix round.

## Review-fix round (Phase 6 findings R-01 to R-13)

- R-01 and R-12: NpcFormationAsset gained an additive OwnerBusinessId, set at formation; DeriveAssetSale, AppraiseFormation, execution, the liquidation port, and CloseOrderly all filter to the acting business; a formation-asset sale now transfers ownership to the buyer; deferred-sale resolution notes are dedup-bounded. 4 new tests.
- R-06: the SellAsset RequestId includes the asset id, matching the dedup RequestKey, so two same-day same-amount sales of different assets both execute. 1 new test.
- R-04: FreightProductionDriver uses the verified payer-resolver collection; an unresolvable payer leaves the charge uncollected with no credit and no legacy fallback. New driver-level tests; the freight filter runs 44/0.
- R-03: postage payers resolve inside the services from the actual sender identity (RecruitmentService for hiring businesses, RegisteredMailService for persons, including ReturnReceiptPayer per delivery), wired in DriveTravelAndComms; a wired-but-unresolvable sender falls back to a foot messenger, never to free post. The money-order advice letter has no funded sender in its call context and is honestly left unset (PASS9-004); generic household and business mail have no production PostItem caller at all. 4 new tests.
- R-02: PayTeacher credits the teacher household through HouseholdLedger in the same call (treasury debit equals household credit plus Payable principal), refuses to debit when no ledger is supplied, and rolls the debit back if Payable creation fails; because the schoolhouse is scene-owned and no production school-term flow exists, the production caller is a term-start-guarded drive on the hub day-change path. 8 teacher-pay tests after the update.
- R-05: named Payables persist through an additive BusinessNamedPayableSaveDto on BusinessRuntimeSaveDto, with capture and restore preserving the accrued liability scalar; an escrowed wage round-trips and settles exactly once. 3 new tests. Coordinator note: the harness DTO shim snapshot was mirrored with the same additive block after the merge.
- R-07: NpcPropertyAcquisitionService.CloseAcquisition registers holdings at the closing choke point; NpcPropertyLifeService levies once per holding per tax year before the audit; the NPC tax book persists in NpcPropertyLifeSaveDto. 4 new tests.
- R-08: ScenarioRunState latches goals completed by their deadline (additive persisted list), so a later predicate change cannot fail a completed goal; ScenarioObjectiveService is registered in Docs/UnwiredSystems.md as source-complete with production start pending the scene bootstrap hook. 3 new tests.
- R-11: the hub constructs the production registry with its persisted StockProvenance ledger assigned, and a production output driven through the registry path lands in that ledger across a save round trip. 2 new tests.
- R-13: BusinessCashPostagePayer left the scan baseline once genuinely wired; HouseholdLedgerPostagePayer stays in the baseline with the precise reason recorded (its only production construction sits inside its own feature directory, which the scan counts as feature-internal); HouseholdMoveService was verified caller-free and registered in Docs/UnwiredSystems.md.
- Round totals: 10 commits, 27 new tests, full harness 1285 passed / 4 historical failures after merge.

## Remaining completion honesty

- Source exercised and Connected are the highest levels claimed anywhere in this pass. Unity compilation of the harness-excluded files, scene wiring, Test Runner, and PlayMode all remain outstanding and are listed in Docs/Pass9_Minimal_Unity_Integration_Handoff.md.
