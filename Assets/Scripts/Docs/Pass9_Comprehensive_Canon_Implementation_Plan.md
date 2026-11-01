# Pass IX — Comprehensive Canon Implementation Plan

Baseline: commit 147503f11dda7947c6e04689dcb795e678586de3 (Pass VIII final). Development branch: muse/pass9-comprehensive-canon-development. Recovery branch: muse/recovered-development-complete (same commit).
Baseline harness (re-established 2026-10-22 at this commit, csproj regenerated and rebuilt): Roslyn harness 1116 passed, 4 failed — exactly the historical RUN-011 set (three MoneyOrderServiceTests, one P5PropertyLegalJourneyTests). No new failures are permitted against this baseline.
Input: Docs/Pass9_Comprehensive_Canon_Gap_Audit.md (67 findings: A1-01…17, A2-01…15, A3-01…15, A4-01…20, classified A–E with completion levels).

## Coordinator decisions (implementation direction within existing authority; no new policy invented)

1. IDENT-001 consolidation strategy (gates A4-01…A4-05): consolidate through the existing ProductionPathRegistry seam and the existing lot models. CategoryStockState remains the quantity authority for bulk goods in the shared economy (no second inventory authority). Lot identity is enforced at domain boundaries where goods are created, change custody, or are consumed by a named counterparty: production outputs on shared paths record lot provenance (origin event, producing business, input lots where applicable); freight cargo keeps lot identity in transit; mine and household lot models gain split/merge-with-ancestry operations. Full per-lot backing of every interior category-stock movement is the documented phased remainder, not silently claimed.
2. Civic treasury (A1-07/A1-10/A1-11): implement one hub-owned civic cash store following the existing single-writer cash patterns. The player tax payment leg credits it; the NPC County Tax Collector leg credits it (the collector remains the collection agent); schoolhouse teacher pay debits it through a real employment record. If the store cannot cover teacher pay, the shortfall becomes a named Payable in FinancialObligationAuthority — never fiction. Tax rates and levy rules already in code do not change; only the missing recipient is supplied.
3. Appraisal asset identity (A3-08/PASS8-003): manager SellAsset proposals are plumbed with the specific asset id taken from the business's own asset registers at proposal time (the lifecycle-distress signal already identifies candidate assets). The production AppraiseSaleCents source appraises from those registers (book/replacement basis with a documented liquidation discount), an execution step transfers the asset and posts both cash legs on approval, and CloseOrderly gains a production liquidation port. Asking price, estimated value, and sale price stay distinct, as do gross value, equity, operating value, and liquidation value.
4. Mine depletion (A4-07/A4-09): the single RemainingRichness01 dial remains the one depletion authority for this pass. The abstract weekly path is retired as the production path once the detailed cycle is wired (demoted to no production caller and honestly registered if retained). The finite-reserve question stays an open Class E decision, recorded in the register.

## Workstreams

### WS1 — Economic integrity (P0). Worker 1. Owns: SharedBusinessRuntimeManager (service revenue, payroll fallback regions), LogisticsRuntimeManager, PostalService, valuation feeds, liability routing.
- A3-01: remove the service-revenue multiplier leak. Business credit equals household debit per service week. Conservation test: household outflow == business inflow per service type.
- A3-02: BookFreightCharge fail-closed and symmetric. Carrier credited only when the payer leg posts in full; shortfall becomes a named FinancialObligationAuthority Payable, never paid from nothing. Reuse the ResolveSettledSale pattern.
- A3-03: postage gains a real payer. Thread a sender cash authority into PostalService.PostItem at all three call sites; refuse posting when the sender cannot pay.
- A3-04: FreightCompanyRuntime.CollectFreightCharge verifies the named payer was actually debited before revenue posts.
- A3-05: new-business founding cash must name its source; audit founding paths and close any unnamed grant (the documented First Ledger endowment stays, distinguishable).
- A3-06: repair valuation feeds. (a) RecordWeeklyProfit receives a financing-excluded operating figure derived the way BusinessReportBuilder does. (b) Valuation liabilities sourced from FinancialObligationAuthority totals per debtor, so bank loans reduce equity.
- A3-10: payroll fallback conserves cash in all paths (business out == household in), including unwired-lookup and no-household cases (such wages become named Payables, not vanishing or free).
- A3-12: route accruedLiabilityCents into FinancialObligationAuthority as named Payables (creditor = worker household or transfer counterparty), settleable at close-down and visible to valuation.
- A4-14: repro first. If confirmed, fix RecordSawmillConstructionLumberSale linked-node branch to credit revenue only for goods actually deducted.
Acceptance: new conservation tests for each fix; full harness shows no new failures; the 90-day and Ruston scenarios still reconcile.

### WS2 — People and lifecycle wiring (P0/P1/P2). Worker 2. Owns: SimulationDrivers driver regions, BirthDriver, MarriageDriver, MigrationDriver, DomesticLaborDriver call sites, demographic drive.
- A2-01: BirthDriver.EvaluateConceptions reads HouseholdMembershipRegistry (ctx.Memberships.GetActiveHouseholdId) instead of the legacy householdId field; pregnancy stamped with the authority id. Regression test uses two generated households with distinct non-zero ids so the 0==0 masking cannot recur.
- A2-10: migration ends employment. Append a new EmploymentEndReason value (append-only, never renumber); MigrationDriver.MigrateOut ends employments; payroll paths refuse off-map persons.
- A2-05: production weekly attendance accumulator in SimulationDrivers installs WeeklyAttendanceProvider on all business runtimes, including DTO-rebuilt ones.
- A2-04: call DomesticLaborDriver.AdvanceTick from SimulationDrivers.OnShortTick; prune stale pending entries; pending list no longer grows monotonically.
- A2-03: wire DailyActivityPlanner.PlanHouseholdDay into the daily drive (after budgets roll, before labor drives) so sleep/meals/job-seeking/managing are really reserved.
- A2-06+A2-07: daily social drive: plan social visits, resolve conversations into hub.KnowledgeTransmissions, evaluate WorkerJobSecurityEvaluator for employed persons daily. Activates the Pass VII/VIII information stack in production.
- A2-09/A1-05: daily estate advancement step in SimulationDrivers.OnDayChanged beside DriveDemographicDay, feeding real decedents from PopulationManager mortality into EstateLifecycleDriver (PASS7-001).
- A2-02: current-spouse derivation for remarriage. Historical spouse edges preserved; current spouse = living spouse edge; IsMarriageable and BirthDriver use the derivation (PASS6-003).
- A2-11: adult-child split eligibility reads the employment registry instead of stale workplaceBuildingId.
- A2-13: production absence sweep passes the existing lenient predicate instead of null.
- A2-14: FindHouseholdById guard corrected from <= 0 to < 0.
Acceptance: each wiring has a production-path test (driver-level, harness); ghost-wage scenario (migrant + absent worker) pays nothing; post-generation marriage conceives in test; estates advance day by day in test.

### WS3 — Mine, freight, farm production (P1). Worker 3. Owns: Mine domain, Freight domain, CropChain driver, ResolveMineOperation region of SharedBusinessRuntimeManager (delegates to the Mine domain; WS1 does not touch this region).
- PASS8-001 (A4-06/A4-07/A4-08/A4-10): a production mine cycle driver enumerates mine levels and completed break-ore tasks and calls MineProductionCycle.RecordBreakOreShift, so ore lots are created from worked minutes with provenance, depletion, and hoist wear. ResolveMineOperation is reconciled in the same change: the abstract path no longer creates ore or depletes richness (double-depletion regression test mandatory). Ore enters mine inventory as lots and reaches buyers through the existing external-market/demand path with a counterparty record, not an instant off-map credit. Timber and hoist requirements enforced by the detailed cycle's existing rules.
- PASS8-002 (A4-11/A4-12/A4-13): FreightShipmentService gains wagon/driver/animal exclusivity checks across live shipments FIRST. Then FreightCompanyRuntime gains a production owner: instances are constructed by the production bootstrap path in SimulationDrivers (the instancing gap documented at SimulationDrivers.cs:1083 is closed source-side; scene placement remains Unity work). Shipments advance through creation, loading, transport, arrival, unloading, acceptance, settlement on the tick; rejected deliveries stay in legitimate custody and never disappear; no revenue for unaccepted cargo; save/load mid-transport restores exactly once.
- A4-18: farm cycle driver on the same pattern: enumerate fields, drive Plant/Tend/Harvest through TaskAuthority outcomes; abstract farm output reconciled the same way as the mine (no double production).
- A4-19 (partial): on the paths WS3 touches (mine, farm), outputs are credited at task completion, not at weekly resolution.
Acceptance: end-to-end harness scenarios — mine produces ore lots from real shifts and sells through a counterparty; freight shipment completes the full lifecycle and survives mid-transport save/load; exclusivity test proves no wagon/driver/animal double-booking; farm cycle produces from driven tasks only.

### WS4 — Performance (P3, pulled forward because it is semantics-preserving). Worker 4. Owns: PersonScheduleTracker, KnowledgeTransmissionStore, DueEventScheduler, ManagerDecisionExecution shipment scan.
- PASS8-009 / A4-15: index PersonScheduleTracker by (person, day) and KnowledgeTransmissionStore by (recipient, subject, root); index active shipments for AdvanceShipments so daily cost no longer scales with total history. History is preserved in full; save DTO byte-equivalence test on a fixture is mandatory.
- A4-17: DueEventScheduler replaces the RemoveAt(0) drain with an indexed/heap structure preserving exact deterministic pop order (ties broken identically). Determinism test: identical event sequences before/after on a fixture cohort.
- A4-16: profile a large-save restore; fix only a measured hotspot if one is isolated; otherwise record measurements and leave behavior unchanged.
- Benchmarks: before/after timings recorded in Docs/Pass9_Performance_Benchmarks.md using a repeatable harness scenario (schedule queries at campaign scale, knowledge inserts, due-event cohort drain, shipment scan).
Acceptance: benchmark deltas reported with numbers; determinism and byte-equivalence tests pass; no history deleted; no event skipped.

### WS5 — Appraisal and manager origination (P1/P2). Worker 5 (wave 2, after WS1 lands). Owns: ManagerBrain inputs, appraisal service, asset-sale execution, CloseOrderly port.
- A3-07: build ManagerCycleInputs from live state in DriveAutonomousLabor (shortage from stock vs reorder target, maintenance from equipment condition, pricing from capture/price index, borrowing from cash-vs-reserve gap with the delegation's real MaxBorrowingCents, asset-sale proposals from lifecycle distress with asset id per coordinator decision 3); call ManagerBrain.WeeklyCycle on the weekly tick. All seven decision kinds become reachable in production.
- A3-13: feed the market-capture factor from the existing computation during weekly settlement.
- PASS8-003 + A3-08: production appraisal source and asset-sale execution as per coordinator decision 3; liquidation value defined and supplied to CloseOrderly (no more Liquidation = null in production).
Acceptance: harness tests show each of the seven decision kinds originating from live-state fixtures; an approved asset sale transfers the asset and posts both cash legs exactly once; appraisal refuses confident estimates without evidence; close-down uses a real liquidation figure.

### WS6 — Civic, scenario, identity, registry (P1/P2/P4). Worker 6 (wave 2). Owns: civic store, tax driver legs, schoolhouse employment, scenario bootstrap factory, ProductionPathRegistry provenance, UnwiredSystems registry updates.
- A1-11 + A1-07: civic treasury per coordinator decision 2; player tax payment credits it; NPC collector leg credits it; NpcPropertyLifeService gains a periodic production driver over hub holdings (tax, maintenance, occasional sale through the same authorities as the player).
- A1-10: schoolhouse teacher routed through the employment registry and paid from the civic store (shortfall → named Payable).
- A1-06: record DisputeClaim interests at acquisition/registration; diligence consults them.
- A1-01 + A1-02 (source half): production bootstrap factory constructs ScenarioCoordinator with the existing ScenarioUnityWiring ports and applies the campaign start date to TimeManager (one date authority wins; the three defaults are reconciled so the preset date drives the clock). Harness-prove the bootstrap logic; the scene hook is Unity handoff.
- A1-03: scenario failure states: missed Saloon Circuit deadline produces a real failure state; maintained-goal re-evaluation evidence persisted; HUD-facing progress/deadline snapshot read model (data half only).
- A1 candidate: wire the existing road rect-merging helpers into BuildRoadVisuals (visual result unchanged, fewer GameObjects; Unity visual check still required, never claimed verified).
- IDENT-001 phased wiring per coordinator decision 1: production outputs on shared paths record lot provenance through ProductionPathRegistry; freight cargo keeps lot identity in transit; split/merge-with-ancestry added to MineOreStock and HouseholdInventory.
- A3-09: RealDemandSource resolved honestly — wired into the daily drive if WS6 capacity allows with its intents executed, otherwise registered in Docs/UnwiredSystems.md with the registry test candidate list extended so future unwired systems are caught automatically.
- Registry hygiene: MineProductionCycle entry resolved by WS3 wiring (remove entry only when the production caller exists and the registry test agrees); every system this pass wires or discovers is reflected in Docs/UnwiredSystems.md.
Acceptance: tax cents conserve end to end (player debit == treasury credit; teacher pay == treasury debit or named Payable); scenario bootstrap test starts a preset campaign on the preset date; provenance records exist for production outputs on touched paths; registry test passes with reality.

## Priority map (brief §3)

- P0 conservation/authority: WS1 (all), WS2 items A2-01, A2-10, A2-05.
- P1 missing core production: WS3 (all), WS2 items A2-04, A2-09, PASS8-003 in WS5, WS6 civic treasury + scenario date.
- P2 unwired needed for gameplay: A3-07 (WS5), A2-02/A2-03/A2-06/A2-11/A2-13 (WS2), A1-06/A1-10 driver halves (WS6), A3-09 (WS6).
- P3 usability/performance: WS4 (all), road rect merging (WS6), A2-14 (WS2).
- P4 larger approved additions fitting scope: IDENT-001 phased boundary wiring (WS6 with WS3 paths), scenario failure states (WS6).

## Dependency graph (text)

- WS1 has no dependencies; it lands first because WS5's valuation work builds on A3-06 and WS3's freight work builds on A3-02's fail-closed contract.
- WS4 is independent (data structures only) and may land in any wave-1 position.
- WS2 is independent of WS1 except the attendance seam: Pass VIII already built WeeklyAttendanceProvider into ResolveWeeklyPayroll; WS2 only installs it. No file conflict expected.
- WS3 depends on WS1's A3-02 contract for freight settlement symmetry; its SharedBusinessRuntimeManager region (ResolveMineOperation) is disjoint from WS1's regions. Merge order resolves residual conflicts.
- WS5 depends on WS1 (valuation feeds, liability truth) and on WS2 landing first is NOT required; WS5's SimulationDrivers region (DriveAutonomousLabor inputs) is disjoint from WS2's regions.
- WS6 depends on WS1's cash patterns only by convention; its teacher-pay Payable uses FinancialObligationAuthority directly. Scenario bootstrap is independent.
- Merge order: wave 1 = WS1 → WS4 → WS2 → WS3; wave 2 = WS5 → WS6. Full harness after each merge; a merge that adds failures is fixed forward or reverted, never papered over.

## Shared contracts (fixed before workers start)

- SharedBusinessRuntimeManager regions: WS1 owns service-revenue, payroll, postal-adjacent, and settlement regions. WS3 owns only the ResolveMineOperation region and must delegate its body to the Mine domain. Nobody else edits this file.
- SimulationDrivers regions: WS2 owns demographic/daily/short-tick driver regions. WS3 owns the freight instancing region near the documented gap comment. WS5 owns the DriveAutonomousLabor inputs region. WS6 adds a new bootstrap factory file in Orchestration instead of editing driver regions, except the start-date application point.
- EmploymentEndReason: append-only. WS2 appends the migration value at the end; no other worker touches that enum.
- FinancialObligationAuthority and HouseholdLedger signatures do not change. New obligations are created through existing APIs.
- Serialized enums remain append-only everywhere; save DTOs gain fields additively only.
- Every new .cs/.md file gets a .meta with a fresh GUID. No file renames. No namespace changes. New code goes in the folder that owns the domain per repo AGENTS.md; tests under Tests/Editor/<Domain>.

## Test strategy

- Focused harness tests per fix, exercising the actual production path (driver-level where the fix is a driver), never a test-only substitute.
- Conservation tests are mandatory for every WS1 item and for the civic treasury: sum of debits equals sum of credits plus named obligations.
- Regression gates: full Roslyn harness after each worker merge; final counts compared against the 1116/4 baseline; the 4 historical failures must remain exactly those 4.
- Integrated scenarios: re-run the 90-day fixture and the 45-day Ruston Decline after all merges; add the brief §7.2 adverse scenarios as harness tests where source-feasible (payroll unaffordable, supplier empty, undeliverable shipment, uneconomic mine, borrower default, provider loss, construction material exhaustion, business closure, asset transfer, objective completed then enabling asset changes ownership).
- Long-horizon run with throughput, schedule growth, save/load time, and reconciliation measurements for the performance document.
- Persistence suite: New Game, restart, save/load, mid-shipment and mid-production restore, pending contracts, reservations, schedules, loans, inventory identities, scenario milestones, multiple tick sizes with deterministic continuation.

## Known blockers and explicit deferrals

- GitHub publication is impossible from this VM (no credentials; the owner's rule forbids requesting secrets). Publication proceeds by the brief's allowed alternative: verified git bundles uploaded to transferable storage at every checkpoint, URLs and expiries recorded in the Pass IX checkpoint log and final report. If gh auth appears, switch to real pushes and verify with git ls-remote.
- Class D (deferred to backlog): telegraph, urban fire service, scenario catalog beyond the two coded scenarios, homestead entry if scoped D, zoning/municipal borrowing, the 12 detailed per-business runtimes as live replacements (they stay registered unwired; the generic runtime remains the live path).
- Class E (decision required, not implemented): water/sanitation rules, court procedure, bank officer identity for lender social knowledge, finite mineral reserve model, severable mineral rights, childcare/caregiving and fatigue designs. Each is recorded in the master register with the exact decision needed.
- A2-12 childcare/caregiving scheduling is sequenced after A2-04 lands and still needs its activity-kind design approval; this pass delivers the A2-04 prerequisite only.
- A4-16 save-restore performance: measure first; fix only a proven hotspot.

## Expected Unity integration needs (handoff, never claimed done)

- Scene hook for the ScenarioCoordinator bootstrap factory from the start panel.
- FreightCompanyRuntime instance placement in the scene (source instancing path is provided; scene objects are Unity work).
- Saloon scene validation for the conversation chains; bank ledger binding for player-owned banks; weekly attendance accumulator verification in PlayMode.
- Road rect-merging visual check.
- Full Unity compilation, Test Runner (including harness-excluded suites), and PlayMode beats per Docs/Pass9_Minimal_Unity_Integration_Handoff.md at pass end.
