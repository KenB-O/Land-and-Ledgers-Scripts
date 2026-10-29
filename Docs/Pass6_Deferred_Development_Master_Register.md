# Pass VI Deferred Development Master Register

This register preserves every known unresolved earlier task and gameplay issue. Stable IDs and statuses are maintained. No task is discarded merely because it is unrelated to the current review. No issue is silently marked complete. Any issue already fixed retains its ID, evidence, and resolution date.

Source: Muse Pass VI mission brief section 20, verified against the Pass VI integration baseline (6d41860).

## Runtime backlog (RUN)

### RUN-001: Boarding-house end-to-end proof
Genuine boarding discovery, actual check-in, accommodation rights, rent and save/load remain unproven end to end. Status: OPEN. Owner: Codex/Unity pass.

### RUN-002: NPC construction supplier and material registration
NPC construction supplier/material registration remains unproven. Investigate the construction fixture that advanced with zero labor minutes. Status: OPEN. Owner: Codex/Unity pass, with Muse source support.

### RUN-003: Bank deposit refresh before underwriting
Live bank deposit refresh before underwriting remains unproven. Status: OPEN. Owner: Codex/Unity pass.

### RUN-004: Main Scene 30-day autonomous economic cycle
Ordinary Main Scene 30-day autonomous economic cycle remains unproven. Existing normal-production anchor advanced 28 days. Status: OPEN. Owner: Codex/Unity pass.

### RUN-005: Camera and visibility continuity validation
Actual interactive camera/visibility/visual continuity validation remains unproven. Status: OPEN. Owner: Codex/Unity pass.

### RUN-006: Unity Test Runner reliability
Broad Unity batch Test Runner hangs and missing result XML. Status: OPEN. Owner: Codex/Unity pass.

### RUN-007: Seller-ranking reserve test contamination
Household seller-ranking reserve test order contamination. Status: OPEN. Owner: Muse source pass.

### RUN-008: Possible duplicate authorities
Possible duplicate wage, debt, activity and spatial authorities. Status: UNDER REVIEW in Pass VI. See Document E and the section 18 reconciliation. Preliminary Pass VI verdicts: MineLaborRegister.SettleShiftWages has no production callers (dead code, test-only, not a competing writer); PlayerDebtManager reconciles through FinancialObligationAuthority via obligationId (workflow state, not a competing writer); PersonScheduleTracker is the canonical activity writer with PersonActivityReadModel as read model; BirthdayService and BirthdayScheduling are complementary (service plus id conventions), not competing; ProductionPathRegistry is the wired one-path router; WorkTask.locationId is a documented placeholder while location ids are consumed by the journey authority from other entities.

### RUN-009: Building fit and collider warnings
Generated building-fit and collider warnings. Status: OPEN. Owner: Codex/Unity pass.

### RUN-010: Scenario preset authoring UI
Scenario preset authoring UI remains incomplete. Status: OPEN. Owner: Codex/Unity pass. (Source preset architecture was delivered in Pass V.)

### RUN-011: Historical source-harness failures
Four historical source-harness failures: three MoneyOrderService tests (IndorseOrder_AllowsSingleIndorsement, IssueDuplicate_ReplacesLostOrderFree, RunSettlementCycle_NetsAndSettlesInFull) and one P5PropertyLegalJourney tax-assessment test (Driver_LeviesAnnualAssessmentOncePerTaxYear). Status: OPEN, preserved untouched through Pass VI. Owner: Muse source pass.

### RUN-012: Gregorian calendar and scenario migration verification
Full normal-game Gregorian calendar/scenario/save migration verification. Status: OPEN. Owner: Codex/Unity pass.

## Playtest and UI backlog (LL-PLAY)

Preserved exactly as listed. The Muse Pass VI pass does not consume Codex usage and does not fix visual/UI items. They are preserved for a later dedicated Unity pass.

- LL-PLAY-001: Ghost player-owned General Store/Renting and Boarding. Tested New Game improvement requires wider verification. Status: Already implemented, source verified; Unity validation pending (Pass XII: bootstrapper suppression for all intents, M2 Town default, store flag default false; scene-instance audit + clean New Game check remain). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-002: Excessive PlayMode-only manager roots. Status: Source contract ready; Unity authoring needed (Pass XII: drivers guard + duplicate-authority audit; scene authoring remains). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-003: Duplicate Civic Foundation. Status: Source contract ready; Unity authoring needed (Pass XII: duplicate audit detects CivicFoundation; removal is scene work). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-004: Obsolete first-session tutorial guidance. Status: Source contract ready; Unity authoring needed (Pass XII: ScenarioGuidanceGate). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-005: Missing scenario identity, objectives and deadlines in HUD. Status: Source contract ready; Unity authoring needed (Pass XII: ScenarioHudReadModel + snapshot builder; HUD binding + failure-board wiring remain). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-006: Blacksmith management-panel scrolling and spacing. Status: Confirmed unresolved (Pass XII delivered no layout change). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-007: Seller meeting modal behavior. Status: Source contract ready; Unity authoring needed (Pass XII: PanelModalCoordinator). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-008: Repetitive acquisition controls. Status: Source contract ready; Unity authoring needed (Pass XII: AcquisitionDealReadModel next action). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-009: Missing sale-discovery provenance. Status: Source contract ready; Unity authoring needed (Pass XII: acquisition provenance labels + explicit unverified marker). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-010: Rigid Build Shell / Next Shell construction workflow. Status: Source contract ready; Unity authoring needed (Pass XII: generic construction/formation interface contract). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-011: Business labels without proven real operating Tasks. Status: Source contract ready; Unity authoring needed (Pass XII: Business Completeness Matrix — 17 labels, four live mechanisms, 16 registry runtimes unwired). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-012: Arbitrary staffing 1/2 controls versus actual workload. Status: Source contract ready; Unity authoring needed (Pass XII: WorkloadStaffingReadModel). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-013: Implausible profits and incomplete financial-period presentation. Status: Already implemented, source verified; Unity validation pending (Pass XII: report aggregation integrity + settled-sale stock refusal; live panel comparison remains). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-014: Unclear Civic count-hall language. Status: Source contract ready; Unity authoring needed (Pass XII: SettlementCivicReadModel + panel text). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-015: Unexplained resource and district metrics. Status: Source contract ready; Unity authoring needed (Pass XII: ResourceObservationReadModel). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-016: Flat terrain. Status: Confirmed unresolved (Pass XII delivered no terrain change). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-017: Historical weekday/date UI mismatch. Status: Already implemented, source verified; Unity validation pending (Gregorian calendar; displayed weekday check remains). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-018: Confusing General Store startup/restocking notices. Status: Source contract ready; Unity authoring needed (Pass XII: StartupNoticeGate + ghost suppression). See Docs/Pass12_Playtest_Issue_Reconciliation.md.
- LL-PLAY-019: MainMusicPlayer scene hierarchy/lifecycle. Status: Confirmed unresolved (Pass XII delivered no audio-lifecycle work). See Docs/Pass12_Playtest_Issue_Reconciliation.md.

## New deferred items discovered in Pass VI

These are findings from the Pass VI cohesion audit that are registered for future work, not fixed in this pass. See Document E for full detail and prioritization.

- PASS6-001: Town-pulse demand manufactures business revenue with no buyer (P1). Integration path designed, ranked in Document E. FIXED in Pass VII Workstream A, see below.
- PASS6-002: Estate administration has no runtime driver; business ownership is OwnerKind-keyed (P1). Ranked in Document E. FIXED in Pass VII Workstream C (runtime lifecycle driver), see below. Runtime auto-advancement remains open, see PASS7-001.
- PASS6-003: Remarriage impossible through real relationships (P2). Ranked in Document E. Still open. RESOLVED in Pass IX - see PASS9-FIX-007 in the Pass IX resolutions section.
- PASS6-004: Horse reservation split, two disjoint availability truths (P1). Integration path designed, ranked in Document E. FIXED in Pass VII Workstream B, see below.
- PASS6-005: Horse condition has no production lifecycle (P1). Ranked in Document E. FIXED in Pass VII Workstream B, see below.
- PASS6-006: Person-level water and hydration missing (P2). Narrow implementation pathway documented, ranked in Document E. Design completed in Pass VII, implementation deferred, see PASS7-002.
- PASS6-007: No NPC-to-NPC conversation system; Socializing never planned (P1). Ranked in Document E. FIXED in Pass VII Workstream E (minimal production foundation), see below.
- PASS6-008: Quick-diligence clues invented; inspection leaks hidden state (P1). Ranked in Document E. Still open.
- PASS6-009: Mine closure and business failure are source-only (P2). Ranked in Document E. Still open.
- PASS6-010: Farm harvest cycle never executes at runtime (P2). Ranked in Document E. Still open. RESOLVED in Pass IX - see PASS9-FIX-006 in the Pass IX resolutions section.
- PASS6-011: Illness is economically inert (P2). Ranked in Document E. Still open.
- PASS6-012: Sim-born labor access, death ends employment, ghost split, recruitment dead check (P1). FIXED in Pass VI, see Document E and the fix commits.

## Pass VII resolutions (2026-10-13)

- PASS7-FIX-001: Town-pulse manufactured revenue retired. TrySellTownPulseDemand and TrySellTownPulseDemandFromBusiness in SharedBusinessRuntimeManager.cs now return zero revenue and record buyerless demand as unfulfilled in the operation summary for manager response. RealDemandSource connects Household and Business needs to actual purchases through the 10-step legitimate purchase path. ExternalMarketAuthority provides modeled external counterparties with bounded budgets and provenance. Verified by RealDemandTests (18 passed, includes SliceA) and ExternalTradeTests (10 passed, includes SliceB). Full harness 948 passed, 4 failed (historical).
- PASS7-FIX-002: Horse availability unified. IAnimalAvailabilityAuthority (thin gateway over the Pass IV IUnifiedReservationAuthority) is now the single availability truth. HorseAuthority.TryReserveUse and DraftPowerService contend on the same "animal:" reservation record. Condition lifecycle: work accrues real condition cost, care recovers, injury blocks incompatible work. Rental changes custody and prevents conflicting owner use. Sale terminates active rental. Verified by Pass7AnimalAvailabilityTests (25 passed, includes SliceC and SliceD).
- PASS7-FIX-003: Estate lifecycle driver implemented. EstateLifecycleDriver advances Opened to Inventoried to ClaimsAdjudicated to Distributing to Closed through the ordinary authorities (TitleAuthority, FinancialObligationAuthority, CreditRegistry, AnimalRegistry). Insolvent estates invent no funds. Beneficiaries not personally liable. Secured creditors ranked before unsecured. Save/load idempotent. Verified by EstateLifecycleDriverTests (20 passed, includes SliceE and SliceF).
- PASS7-FIX-004: Manager decision execution wired. All seven ManagerActKinds (OpenRecruitment, Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset) now execute through real production authorities with delegation-scope enforcement (Do Once, Manager Judgment, Owner Locked). Failed actions produce legitimate pending states, not silent drops. Verified by ManagerDecisionExecutionTests (20 passed) and ManagerExecutionSliceTests (2 passed, includes SliceG and SliceH).
- PASS7-FIX-005: NPC conversation and information foundation. KnowledgeTransmissionStore records transmissions with full provenance (source, speaker, recipient, time, subject, confidence, chain). IndependentSourceCount prevents fabricated corroboration. ConversationOpportunity requires legitimate co-location and time. SocialPlanning drives social motives. The foreman chain (mine conditions to foreman knowledge to saloon conversation to bartender knowledge to downstream decision) is proven end to end. Social activity without economic gain is proven. Verified by ForemanInformationSliceTests (2 passed, includes SliceI) and SocialInformationTests (15 passed, includes SliceJ).

## Pass VIII resolutions (Worker 1, 2026-10-13)

- PASS7-006 (fixed): Retail capacity pool corruption. GenericRetailConfiguration.ReleaseProductSpace no longer mints a zero-allocation pool via GetOrCreatePool; an unconfigured pool imposes no constraint and has nothing to release, while an explicitly configured pool still constrains and still frees room on release. A store that never configures shelf capacity now receives every shipment after every sale. Verified by ShipmentIntegrityPass8Tests (7 capacity tests, including the sale-then-receive corruption repro) and the full harness.
- PASS7-007 (fixed): AddCategoryStockUnits silent cap. Audited every caller; fixed each site that ignored the accepted-units return. GenericRetailSaleAuthority.TryExecuteSale rollback now measures restored units and warns loudly on a capped restore. Sawmill slab/offcut production records stored units, re-stores the surplus after the outlet sale, and records any remainder as disposed mill fuel. Weekly operation output records a shortfall note when storage accepts less than planned. Sawmill-to-yard lumber transfer and LocalRecurringOrderManager.ApplyTransfer capture the seller re-absorb return and record stranded units. StoreButchery caps the wholesale request at receivable room before the butcher deducts, so the store never pays for meat it cannot hold. Verified by ShipmentIntegrityPass8Tests (6 cap/rollback tests) and the full harness.
- PASS7-008 (fixed): Shipment receive failure masked as Delivered. LogisticsShipmentState.AdvanceGameSeconds no longer auto-completes Unloading; completion, partial acceptance, delay, and failure are set only by a delivery authority applying the receipt, so a Completed status always implies actual acceptance. Added LogisticsShipmentState.deliveredQuantityUnits (persisted additively) recording actual accepted units; all four LogisticsRuntimeManager delivery paths accumulate it. FreightShipmentService.WorkUnloading now receives through a wired receiving authority: full acceptance completes with real recipient custody, partial acceptance records exact accepted/remaining counts as Partial (retryable), refusal holds as Delayed with goods retained in wagon custody, and a missing authority holds as Delayed rather than false-delivering. FreightShipmentService.VerifyConservation rejects delivered claims that disagree with actual acceptance. Removed the FromSaveDto ambiguous-quarantine repair that vaporized genuinely loaded cargo on reload; Delayed/Failed shipments with real cargo (including FailShipmentAfterLoad quarantine) now survive save/load intact and idempotent. FreightCompanyRuntime.CollectFreightCharge now requires deliveryApplied, so freight revenue settles only on truly delivered shipments. Verified by ShipmentIntegrityPass8Tests (13 delivery/save-load/conservation tests) and the full harness.

## Pass VIII resolutions (full pass, 2026-10-14)

- PASS8-FIX-001: Housing and tenancy source connections. HousingAuthority gained capacity-guarded TryOccupyUnderAgreement, ChangeSpaceCapacity, EndAgreement, TransferAgreement, CloseBuilding, VacantSpaces, and occupant queries; LodgingRegistry binds rentable rooms to real spaces; BoardingLodgingBridge unifies boarding inventory, agreements, and housing occupancy; HouseholdMoveService validates before mutating; HousingAbsenceSweep ends occupancies of the absent with lenient-arrangement support; rent and arrears flow through the single cash writer and debt authority. Verified by HousingTenancyTests and BoardingLodgingBridgeTests (25 passed) and the full harness.
- PASS8-FIX-002: NPC construction full chain. Parcel authorization against the title authority; supplier discovery through the real directory; real-lot sales with price verification; real-cash payment; freight through the unmodified FreightShipmentService; site stockpile with all-or-nothing staging; labor-gated progress; equipment reservations; cancellation with exactly-once restock; completion registering usable spaces. The zero-labor-minutes report was verified as old production behavior already fixed in Pass IV; stale call sites and severed compat overloads were removed. Verified by ConstructionFullChainTests (20 passed) and the full harness.
- PASS8-FIX-003: Production business lender. BusinessLenderService closes the Pass VII hold with the full cycle through the single debt authority: real cash availability with reserve floor and concentration cap, shared WorkingCapitalUnderwriting (extracted verbatim from PlayerDebtManager, which now delegates to it), disbursement debiting real bank cash first, exactly one obligation per loan, senior liens and guaranties, daily servicing with delinquency ladder and default consequences, idempotent approvals, clear-then-load save/load. Manager Borrow decisions reach it through delegation. Verified by BusinessLenderServiceTests (9 passed) and the full harness.
- PASS8-FIX-004: Circulation cash and reporting defects. Payroll fallback no longer destroys cash without household credit and no longer pays owner slots; BusinessReportBuilder profit and runway no longer count owner financing as revenue (new append-only LedgerEntryKind.Financing); CloseOrderly now credits the creditor's cash store. The 45-day Ruston Decline adverse scenario reconciles every cent, all goods, and all debts exactly. Verified by Pass8AdverseCirculationTests (8 passed) and the full harness.
- PASS8-FIX-005: Production network common mechanisms. Blacksmith lot-ledger divergence and repair revenue conservation; livestock sale ownership convention, atomicity, and butcher payment conservation; the mine extraction production function (worked minutes become assayed lots with depletion and hoist wear). Verified by 11 new tests and the full harness.
- PASS8-FIX-006: Person and household autonomy. OpportunityPursuitService connects discovered openings to real applications; attendance-gated payroll ends automatic attendance; provider-loss attribution fixed per member with survivors seeking income through the real pressure path; disability labor capacity now multiplies into usable minutes; dead-worker guards at task assignment, journeys, and scheduling; lenient absence arrangements; RentalFleet.Return releases the reservation hold. Verified by 57 new tests including 90-day and 3-year reconciliation suites and the full harness.
- PASS8-FIX-007: Social knowledge generalized. Three emergent chains (layoff rumor to job search, supplier trouble to steered orders, borrower trouble to corroborated lender pricing) through ordinary authorities with safeguards proven. Verified by SocialKnowledgeIntegrationTests (9 passed) and the full harness.
- PASS8-FIX-008: Scenario, persistence, calendar. New Game authored-state clearing; idempotent inventory grants; short-tick carry across midnight under acceleration; off-map journey save deep-copy. Verified by 14 new tests and the full harness. Water deliberately deferred with Canon verdict in Docs/Pass8_Water_Verdict.md.
- PASS8-FIX-009: Independent discovery fixes (Phase 4). NPC business lifecycle driver (businesses could form but never close); freight and postal revenue collection scans; RentalFleet cash legs; postal determinism via stable FNV-1a hash (eliminated all 7 suite flakes); due-event clear on New Game; NPC owner auto-resolution for pending requests; the machine-checkable unwired-system registry (Docs/UnwiredSystems.md); service-revenue demand gate through household spending. Verified by 36 new tests and the full harness: 1116 passed, 4 failed (the 4 pre-existing historical failures).

## New deferred items discovered in Pass VIII

- PASS8-001 (P1): Wire the detailed mine ore chain. MineProductionCycle and the assay/shipment/settlement chain are built and tested but have zero production callers; the abstract weekly path is live. Needs a weekly driver and reconciliation of the two paths. Owner: Muse source pass. RESOLVED in Pass IX - see PASS9-FIX-001 in the Pass IX resolutions section.
- PASS8-002 (P1): Production freight tick drive. Revenue collection scan exists and is tested, but FreightCompanyRuntime has zero production instances. Wiring a drive means designing freight dispatch production ownership. Owner: Muse source pass, Unity-adjacent. RESOLVED in Pass IX - see PASS9-FIX-002 in the Pass IX resolutions section.
- PASS8-003 (P1): Production appraisal source for asset sales. The NPC owner-resolution policy is proven but the live appraisal seam is null, so production sale requests are conservatively denied. Needs a real appraisal authority or documented policy valuations. Owner: Muse source pass. RESOLVED in Pass IX - see PASS9-FIX-003 in the Pass IX resolutions section.
- PASS8-004 (P2): Childcare, caregiving, recovery person-time. Dependents bind no one's schedule. All seams exist; add task definitions, append PersonActivityKind values, schedule through the domestic driver. Owner: Muse source pass. PARTIALLY ADVANCED in Pass IX - domestic execution and daily planning are live (see PASS9-FIX-009); the childcare and caregiving task kinds remain open pending a design decision (Class E).
- PASS8-005 (P2): Wire individual detailed business runtimes. Twelve runtimes registered-unwired in Docs/UnwiredSystems.md; each wiring is a separate scoped item. Owner: Muse source pass.
- PASS8-006 (P2): Migration driver and journey integration. LaborMigrationLinker evaluates but moves nobody; no integrator maps intents to journeys. Owner: Muse source pass.
- PASS8-007 (P2): Remaining social-knowledge chains (tenant/full boarding house, farmer equipment sharing, wage-driven migration). Each needs its real-state authority and decision consumer on the shared evaluator pattern. Owner: Muse source pass.
- PASS8-008 (P2): Service profile calibration. The demand gate works; visit caps and price calibration per service type are untuned. Owner: Muse source pass, needs live-play observation.
- PASS8-009 (P2): Unbounded schedule/packet list remediation. Linear scans on daily hot paths with monotonic growth; needs campaign-scale measurement before tuning, then pruning or indexing. Must preserve truth. Owner: Muse source pass. RESOLVED in Pass IX - see PASS9-FIX-004 in the Pass IX resolutions section.
- PASS8-010 (P2): Player construction input-support harness coverage. The only tests need UnityEditor; extract the availability rules into a Unity-free seam. Owner: Muse source pass.
- PASS8-011 (P3): Player UI for pending owner requests. NPC policy resolves NPC businesses; player-owned requests await a decision UI. Owner: Codex/Unity.
- PASS8-012 (P3): Full foreclosure-auction sale of seized collateral. Enforcement today is satisfaction-in-kind; auction needs legal-rule design. Owner: Muse source pass with design review.
- PASS8-013 (P3): Household and personal lending through the bank. Business loans served; household borrowing stays with existing directories. Owner: Muse source pass.
- PASS8-014 (P3): Due-event scheduler hub-level clear. Stale events for purged persons are consumed harmlessly; a hub-level new-game clear would be cleaner. Owner: Muse source pass.
- PASS8-015 (P3): Remove vestigial seams (ManagerExecutionServices.FindAsset and similar). Trivial hygiene. Owner: Muse source pass.
- PASS8-016 (P3): Sanitation and water implementation. Both deferred on Canon policy with unblock criteria in Docs/Pass8_Water_Verdict.md. Do not implement until policy decisions land. Owner: Kennedy/design review.

## New deferred items discovered in Pass VII

- PASS7-001: Estate runtime auto-advancement. The EstateLifecycleDriver is complete and tested but has no daily runtime caller; estates advance only when driven by tests or the scripted AutonomousSocietyDriver.OpenAndSettleEstate demo. A daily estate-processing step in the demographic driver is the designed path. Severity P1. Owner: Muse source pass. RESOLVED in Pass IX - see PASS9-FIX-005 in the Pass IX resolutions section.
- PASS7-002: Person-level water system. Full design and authority map completed in Pass VII (see re-audit). Existing: horse care water scalar, regional watercourse geography, infrastructure capability flags. Missing: physical water goods, wells, hauling, household reserves, person hydration, livestock consumption, commercial demand, sanitation, firefighting. Implementation deferred pending Canon policy decisions on requirement rates and health effects. Severity P2. Owner: Muse source pass with design review. Pass VIII Worker 9 verdict (2026-10-13): Canon evidence reviewed; requirement rates, health/death consequences, and water rights (proposal 136, not approved) remain undetermined — policy-dependent portion deferred again, options A-D and exact unblock criteria recorded in Docs/Pass8_Water_Verdict.md. No water mechanics implemented.
- PASS7-003: Mine closure lifecycle runtime wiring (PASS6-009 carried forward). Still source-only. Severity P2. Owner: Muse source pass.
- PASS7-004: Farm harvest cycle runtime driver (PASS6-010 carried forward). Still no production callers for HarvestField, TendField, PlantField. Severity P2. Owner: Muse source pass. RESOLVED in Pass IX - see PASS9-FIX-006 in the Pass IX resolutions section.
- PASS7-005: Illness economic integration (PASS6-011 carried forward). laborCapacityPercent and HealthWorkImpactBand still have no consumers. Severity P2. Owner: Muse source pass.
- PASS7-006: Retail capacity pool sharp edge (found in 90-day simulation). ReleaseProductSpace calls GetOrCreatePool, which mints a zero-allocation pool on first sale; TryOccupyProductSpace then refuses all future shipment receives for that product line, and AdvanceShipments marks shipments "Delivered" anyway, so goods vanish. Workaround: configure pool capacity at build. A store that never configures shelf capacity silently loses every shipment after its first sale. Severity P1. Owner: Muse source pass.
- PASS7-007: AddCategoryStockUnits caps silently (found in 90-day simulation). Returns acceptedUnits, but callers ignoring the return value over-count production. All Pass VII drivers record only accepted units. Audit existing callers. Severity P2. Owner: Muse source pass.
- PASS7-008: Shipment receive failure masked as Delivered (found in 90-day simulation). When TryReceive fails, the shipment is still marked "Delivered" with only a diagnostic line. Drivers must measure actual stock deltas rather than trusting ledger status. Severity P2. Owner: Muse source pass.

## Pass IX resolutions (2026-10-22)

- PASS9-FIX-001: Mine production end to end (PASS8-001 resolved). MineProductionDriver drives the detailed ore chain weekly through SharedBusinessRuntimeManager.ResolveMineOperation delegation; the abstract ore path is retired; a single RemainingRichness01 dial is the only depletion authority; ore sells only against a named counterparty on delivery. Verified by the Pass IX mine tests and the full harness (1285 passed, 4 historical failures).
- PASS9-FIX-002: Freight production driver (PASS8-002 resolved). FreightProductionDriver owns FreightCompanyRuntime instances through SimulationDrivers; equipment exclusivity is checked before dispatch; rejected deliveries retain custody; revenue settles only on accepted delivery through the verified payer collection; mid-transport save/load is covered. Verified by the Pass IX freight tests, the review-fix freight collection tests, and the full harness.
- PASS9-FIX-003: Evidence-based appraisal and asset sale execution (PASS8-003 resolved). AssetSaleAppraisalService values from live registers only and refuses without evidence, so the owner policy denies blind sales; AssetSaleExecutionService executes exactly once with title transfer and rollback on refusal; CloseOrderly liquidation runs through a registered port. The Phase 6 review fixes scoped formation assets to their owning business, transfer formation assets to the buyer on sale, and made the SellAsset RequestId unique per asset. Verified by the AssetSale suite and the full harness.
- PASS9-FIX-004: Performance indexing (PASS8-009 resolved). Schedule (person, day) buckets, knowledge-store indexes, an active-shipment subset, and a due-event heap replace the linear scans; DTO output is byte-identical and determinism is proven by equivalence tests. Benchmarks are recorded in Docs/Pass9_Performance_Benchmarks.md.
- PASS9-FIX-005: Estate daily advancement (PASS7-001 resolved). DriveDemographicDay now runs a daily estate step, DriveEstateAdvancement into EstateLifecycleDriver.AdvanceOpenEstatesOneStage.
- PASS9-FIX-006: Farm runtime driver (PASS7-004 and PASS6-010 resolved). FarmCycleDriver runs the weekly farm cycle, SharedBusinessRuntimeManager.ResolveFarmOperation delegates to it, and the abstract crop conversion output is retired.
- PASS9-FIX-007: Remarriage derivation (PASS6-003 resolved). GetCurrentSpouseId derives from the relationship registry, so remarriage through real relationships is reachable.
- PASS9-FIX-008: Economic integrity repairs from the comprehensive Canon audit. The service-revenue multiplier that minted cash is removed; freight charges settle symmetrically with a named Payable on shortfall; founding cash is owner-funded; valuation feeds exclude financing and count authority-only liabilities; the payroll fallback escrows into named Payables, and named Payables are now persisted across save/load with creditor, obligation link, and escrow flag intact; player and NPC tax legs conserve into a hub-owned civic treasury. Verified by the Pass9Ws1 suite (25 tests), Pass9CivicTreasury (8), the review-fix persistence tests, and the full harness.
- PASS9-FIX-009: People and lifecycle wiring from the comprehensive Canon audit. Conception reads HouseholdMembershipRegistry; migration ends employment with the appended EmploymentEndReason.MigratedOut and payroll refuses off-map workers; the production WeeklyAttendanceAccumulator is installed and fed; DomesticLaborDriver advances on the short tick; DailyActivityPlanner, DailySocialDrive, and WorkerJobSecurityEvaluator run on the day change. Verified by Pass9PeopleLifecycle (17 tests) and the full harness.
- PASS9-FIX-010: Civic and scenario foundations. Teacher pay credits the teacher household from the civic treasury at the term wage of 12000 cents with exactly one shortfall Payable and is driven at term start; NPC holdings register at the acquisition closing, are levied once per tax year, and the NPC tax book persists; dispute claims record at title acquisition with an identity-safe diligence clue; ScenarioBootstrapFactory and ScenarioDateUtil anchor on the canonical calendar with DaysBeforeAnchorYear 3652; scenario failure state persists exactly once and goals completed by their deadline latch. Verified by the Pass9Schoolhouse and review-fix teacher-pay tests, Pass9Scenario (9), the review-fix suites (27 tests in total), and the full harness.

## New deferred items discovered in Pass IX

- IDENT-001 (P1): Universal item and lot identity. Status after Pass IX, boundary wiring delivered, interior migration open. Delivered, ProductionPathRegistry consolidation; the SharedCategoryStockLedger records production output, external supply, and arrivals with provenance at domain boundaries; SplitLot and MergeCompatibleLots on HouseholdInventory and MineOreStock conserve quantity; the hub assigns its persisted StockProvenance ledger to the production registry. Open remainder, CategoryStockState remains the bulk quantity authority inside business interiors; per-lot identity inside those interiors is a phased migration to schedule per domain, highest-value paths first. No second inventory authority may be introduced. Owner: Muse source pass.
- PASS9-001 (P3): Asset-sale executed records are not persisted across save/load. Title transfer persists and re-execution is impossible, but the execution record itself is runtime-only. Owner: Muse source pass.
- PASS9-002 (P2): Tax fee waterfall after a partial payment (Phase 6 review R-09). NpcPropertyLife.AuditTaxes computes the fee portion from AmountPaidCents, so fees accruing after a partial principal payment can route to the civic treasury instead of the County Tax Collector; the player leg also spends before booking, so a refusal on a satisfied assessment strands the debit. Money is conserved in both cases; the recipient can be wrong. Fix by tracking fees paid separately on the assessment. Owner: Muse source pass.
- PASS9-003 (P2): Property dispute completeness (Phase 6 review R-10). NPC distress-sale recording is not fed in production because the context is built without the Disputes service; player-path claims are not persisted; RecordClaim has no (parcel, claimant, basis) dedup. The diligence clue itself is verified identity-safe. Owner: Muse source pass.
- PASS9-004 (P3): Money-order advice letter postage has no funded sender in its call context (the context names only the postmaster and senderPersonId is -1 in production-shaped use), so its payer is honestly left unset. Generic household mail and generic business mail have no production PostItem caller at all. Owner: Muse source pass with design review.
- PASS9-005 (P3): Ore split copies the assay certificate (Phase 6 review R-16). MineOreLot.CreateSplit duplicates the assayed flag and assayId to the child, so one assay certifies two lots; tonnage is conserved and assayed lots can never re-merge. Owner: Muse source pass.
- PASS9-006 (P3): Domestic tick watermark survives load (Phase 6 review R-18). The SimulationDrivers previousDomesticTickGameSeconds field is never reset on save/load, so loading an earlier-time save in the same session stalls DomesticLaborDriver.AdvanceTick until game time passes the old watermark. Owner: Muse source pass.
- PASS9-007 (P3): Offer-pricing heuristic still reads the financing-inclusive figure (Phase 6 review R-17). AcquisitionMarketManager derives its weak-performance multiplier from WeekToDateNetCents; the valuation feed itself is fixed. Also noted, BusinessRuntimeState.IsUnavailableForPayroll fails open on lookup exceptions, deliberate and commented. Owner: Muse source pass.
- PASS9-008 (P2): RealDemandSource wire-or-retire decision. The type is registered in Docs/UnwiredSystems.md as honestly unwired because no executor exists. A future pass must either build the executor or retire the type. Owner: Kennedy/design review, then Muse source pass.
- PASS9-009 (P3): Audit item A3-11, owner-reporting distinctions, was not addressed in Pass IX. Owner: Muse source pass.
- PASS9-010 (P2, Class E, decision required): Unresolved Canon design questions recorded by the comprehensive audit, including finite mineral reserve, severable mineral rights, water rules, court procedure detail, bank officer identity, and childcare and fatigue designs. Canon does not determine these; do not invent. Owner: Kennedy/design review.
- PASS9-011 (P3): Archetype outputs-at-resolution beyond the mine and farm paths (audit item A4-19). Owner: Muse source pass.
- PASS9-012 (P3, Class D): Scenario catalog beyond the two coded scenarios, and the CalendarAnchor scene-side remainder (scene object placement). Owner: Muse source pass plus Unity scene work.
- PASS9-013 (P3): Save and restore profiling at campaign scale (audit item A4-16). Measured during Pass IX with no single hotspot; revisit if campaign saves grow. Owner: Muse source pass.
- PASS9-014 (P2): Harness-excluded production files. The Pass IX edits in SimulationDrivers.cs, SharedBusinessRuntimeManager.cs, SchoolhouseModels.cs, and SimulationSystemsHub.cs are Roslyn syntax-checked only; their Unity compilation is outstanding and is listed in Docs/Pass9_Minimal_Unity_Integration_Handoff.md. Owner: Unity session.

## Pass XI resolutions (2026-10-24)

All Pass XI work is on `muse/continuous-development` (Pass IX baseline
cc59bec through pre-final-gate tip 6573b31; the WS6 final-gate review
may add fix commits after). Roslyn harness at that tip: 1404 passed, 3
failed. Nothing in this section is Unity verified.

- PASS11-FIX-001: RUN-011 partially fixed. The indorsement failure was
  a genuine production bug and is fixed (WS3 d9f06d7, narrowed by WS6
  first-gate fix M5: IndorseOrder clears BankCollectionAgent and
  RedeemOrder gates bank presentment on empty IndorsedToName). The
  other three historical failures are documented test-side defects
  left failing deliberately, with face evidence in the test text and
  agreement from two independent reviews: IssueDuplicate asserts
  IsNotNull with the message "No duplicate of a non-live order" while
  production correctly returns null; RunSettlementCycle asserts net
  2500 while its own comment computes 3500 minus 500 equals 3000, and
  the same wrong figure propagates into both fund assertions (correct
  values: net 3000, po-a fund 0, po-b fund 4000); the P5 levies test
  expects Assessed at day 100, past the 60-day due date, against the
  delinquency model proven by sibling tests. Recommended next step: a
  dedicated test-correction review. RUN-011 is NOT closed.
- PASS11-FIX-002: RUN-002 substantially advanced at source level (WS2).
  NpcConstructionDriver now advances hub projects one step per day
  through SimulationDrivers, and the site stockpile is the freight
  receiving authority (fixing site freight that could never complete).
  Still outstanding, not invented: a catalog asset, a yard/sawmill lot
  adapter, a TitleAuthority parcel adapter, and the commissioning
  trigger rule. The driver advances already-staged packages and waits
  loudly on Ready packages and completion. RUN-002 is NOT closed;
  Unity and scene parts remain open.
- PASS11-FIX-003: RUN-003 substantially advanced at source level (WS2).
  NPC business and household cash binding were verified already live.
  BankLedgerBindingRegistry is delivered (one ledger per bank,
  fail-loud on a second, live reads, no cached balance, no balances
  created) with a hub-owned lazy property and a
  SimulationDrivers.BindBankLedger binding point, deliberately NOT
  wired into underwriting (the lender ledger and BankRuntime ledgers
  are disjoint; binding both would double-count). Player-bank scene
  binding stays outstanding under gate G4. RUN-003 is NOT closed.
- PASS11-FIX-004: RUN-004 remains OPEN for Unity. The WS4 30-day
  production settlement proof is a Roslyn harness proof, not the Unity
  Main Scene proof, and is recorded as such; it must not be cited as
  RUN-004 closure.
- PASS9-001 (fixed in Pass XI, WS3): executed asset-sale records now
  persist through the decision ledger DTO with an additive
  CompletedAssetSales list; re-execution is refused from the persisted
  record; old saves load empty rather than null. The manager-ledger
  byte golden changed by exactly the appended empty list and was
  updated with the assertion still checking exact equality.
- PASS9-002 (fixed in Pass XI, WS3): fee waterfall fixed by tracking
  FeesPaidCents plus FeesPaidTracked separately on the assessment, so
  fees accruing after a partial principal payment route to the County
  Tax Collector with no double collection; old saves reconstruct
  fees-first; the player leg refuses a Satisfied assessment before
  spending, so no debit is stranded. Known limitations, not hidden:
  the player leg still credits the civic treasury for its fee portion
  because no collector cash store exists in its call context (the fee
  split is booked on the assessment for later wiring), and the old-save
  migration preserves the old misattribution in the principal-first
  case (fees marked paid that were never paid, best-effort, matching
  pre-fix behaviour).
- PASS9-003 (fixed in Pass XI, WS3 plus coordinator hook): same
  (parcel, claimant, basis) dedup on Open or Contested claims,
  distress-sale feed, player-path persistence through the acquisition
  manager DTO, title authority integration with claims as records
  never ownership, evidence redaction preserved. The drivers hook
  (FeedDisputes in DriveNpcPropertyLifeDay) is wired and null-safe.
- PASS9-004: unchanged in Pass XI. Postage stays honest-unset (no
  funded sender invented); locked by test. Still OPEN.
- PASS9-005 (fixed in Pass XI, WS3): split child no longer inherits
  the assay certificate; it starts unassayed at 0.3 estimate
  confidence and earns its own assay; tonnage conserved, parent keeps
  its certificate, assayed lots still never re-merge, old saves with
  a duplicated assay id load with exactly one certified lot plus a
  diagnostic.
- PASS9-006 (fixed in Pass XI, WS1): DomesticTickGate (pure, exact old
  rule, only forward motion advances) plus SaveLoadManager reset to
  the loaded clock after every restore; completed work, pending tasks,
  and person-time reservations preserved.
- PASS9-007 (fixed in Pass XI, WS3, with a WS6 first-gate evolution):
  offer multiplier weekly leg now reads financing-excluded
  WeeklyOperatingProfitCents; payroll guard fails closed on lookup
  exceptions with distinct diagnostics. The WS3 comment claiming the
  refused week's wage could still be paid later was false for the
  weekly employment path; WS6 first-gate fix M3 changed the behaviour
  to accrue the refused week's wage as an operating liability via the
  named-payable path (confirmed-unavailable persons still accrue
  nothing; suspension deliberately not copied). Residual recorded as
  PASS11-003.
- PASS9-008: still OPEN (gate G1, Kennedy decision required). WS2
  delivered a decision memo with four options, recommending the
  intent-executor option only if demand collection should affect
  play, otherwise leaving it unwired, with the double-buying hazard
  (intents vs embodied shopping consuming the same needs exactly
  once) as the central design fact. The type stays registered unwired
  in Docs/UnwiredSystems.md. Collection was never wired to the live
  tick merely to claim it wired.
- PASS9-009 (partially fixed in Pass XI, WS3): the reporting part is
  fixed (FillStaffing excludes owner rows from filled-worker and
  weekly payroll, tracked in additive ownerLaborSlots and
  ownerLaborWageCents; Pass VIII financing separation locked by
  test). Broader A3-11 portfolio and Unity surfaces (owner-personal
  cash, protected cash, principal debt, consolidated equity never
  presented as spendable cash) remain open.
- PASS9-010: still OPEN (gate G2, owner design approval required). No
  Canon decisions invented. WS2's mine-closure plan is gated on the
  mineral reserve decision.
- PASS9-011: still OPEN. WS3 verified by documentation only (mine and
  farm delegate to detailed task drivers; all other archetypes still
  resolve through the generic ResolveBusinessOperation path crediting
  output at resolution time). WS2 left a per-kind delegation plan
  (retire archetype handling first, one kind at a time); conversion
  was not attempted (dual-execution risk).
- PASS9-012: still OPEN (scene-side remainder plus scenario catalog
  beyond the two coded scenarios; Unity scene work).
- PASS9-013: measurement only in Pass XI. WS3 SaveLoad-filtered run
  71 passed, 0 failed, 72s; WS5 SaveLoad-filtered run 72 passed, 0
  failed, 70.7s; canonical full run 77s (WS3 measurement). No
  bottleneck confirmed, nothing optimized. Still OPEN as a revisit
  item if campaign saves grow.
- PASS9-014: narrowed, still OPEN (Unity session). The Pass XI audit's
  full-repo Roslyn build showed zero errors in the four
  harness-excluded files (only Unity package using gaps elsewhere),
  and WS1, WS2, WS3, WS5, and WS4-follow-up edits to excluded files
  were checked the same way, but real Unity compilation, scene
  serialization, and PlayMode remain unproven. The M1/M2 Unity-runner
  bootstrap tests are delivered but unrun (PASS11-008).
- IDENT-001: interior migration remainder still OPEN. Related Pass XI
  progress only: assay-split provenance (PASS9-005 fix) and
  WS4-proof provenance conservation; CategoryStockState remains the
  bulk quantity authority inside business interiors, and no second
  inventory authority was introduced.
- PASS8-005: still OPEN. The registry now holds thirteen detailed
  business runtimes (BankRuntime added by WS1) plus
  ScenarioFailureNoticeBoard (added by WS5, a presentation surface,
  not a business runtime). The construction driver and freight driver
  are live paths, not runtime entries. Construction types are
  accounted in the scan baseline because they are wired through
  drivers (WS2); the runtimes themselves remain unwired.
- PASS8-006: still OPEN. WS2 left a scoped migration integrator plan
  (accepted intents to OffMapJourneyAuthority journeys exactly once);
  no integrator was built.
- PASS7-003: still OPEN. WS2 left a stage-gated mine closure driver
  plan gated on the G2 mineral reserve decision; no closure driver
  was built.
- PASS6-008: still OPEN, with related progress: the PASS9-003 work
  preserved evidence redaction with diligence clues showing counts
  and certainty only (identity-safe, verified). The broader
  quick-diligence clue question is not closed by that.
- LL-PLAY-005: still OPEN as a Unity item, with source-level progress
  from WS5 (headline deadline normalization, failure fields,
  presentation builder, exactly-once notice board). HUD wiring is
  Unity work. No other LL-PLAY item is closed or advanced by Pass XI
  source work; the full LL-PLAY set remains preserved for a dedicated
  Unity pass.
- A-14 (not a register item, recorded here so it is not lost):
  late-latch semantics unchanged through Pass XI (WS1 and WS5 both
  examined it; lenient behaviour pinned by existing latch tests;
  flipping it is a Canon/design decision). If it flips, the latch
  tests and the WS5 presentation-boundary comment must be updated
  together.

## Pass XII resolutions (Worker F review, 2026-10-28)

- PASS11-003 (P3): PARTIALLY CLOSED in Pass XII. Worker C implemented
  the manual path: PayAgreedWage refuses a manual re-pay while a
  preserved lookup-failure liability stands, refuses an identical
  manual payment twice (employment+day+period key, persisted in
  BusinessRuntimeSaveDto.manualWagePayments), and the weekly run nets
  manual cash already paid for the same employment-week instead of
  paying the full agreed wage again. Verified by Worker F from source
  and by Worker C's reproduction tests. REMAINING DISTINCT HOLE,
  deliberately not closed by a stamp guard: a direct second call to
  BusinessRuntimeState.ResolveWeeklyPayroll for the same week is
  unguarded (only WageSettlementService checks its SettledWeekIndex
  map). A stamp-derived guard was rejected because it misfires on
  legitimate service settlements whose week index advances while the
  stamp does not (proven: week 57 settlement at day 406, 406/7 = 58).
  See Docs/Pass12_Independent_Review.md.
- PASS11-007 (P3): CLOSED in Pass XII. Worker C made the legacy-slot
  payroll path mirror the employment path: a throwing eligibility
  lookup accrues the week's wage as a named operating liability
  (confirmed-unavailable persons still accrue nothing); the legacy
  loop never pays arrears, so the accrual cannot double-pay there,
  and the preserved-liability guard blocks a manual re-pay of the
  same week. Verified by Worker F from source.
- E-DEFECT-01 (Pass XII Worker C): CLOSED. BusinessRuntimeState.
  ResolveSettledSale refuses a sale larger than stock on hand (total
  refusal, no partial delivery, no scaled revenue); both production
  callers (GeneralStoreRuntimeManager, SharedBusinessRuntimeManager)
  go through this method, and the obsolete buyerless placeholder has
  zero production callers. Verified by Worker F from source; tests
  Pass12CSettledSaleStockTests.
- E-DEFECT-02 (Pass XII Worker A, extended by Worker F): CLOSED for
  the audited class. Worker A cloned DTO objects on load for
  BankDeposits, FinancialObligationAuthority, HouseholdLedger,
  BusinessLenderService, EmploymentRelationship and MoneyOrderService.
  Worker F's sweep (same-snapshot double-restore tests,
  Pass12FCopyOnLoadTests, 7/0) added PropertyTaxService, Housing
  occupancies, NpcCommissionedBuild records, CreditRegistry
  instruments, UsedEquipmentMarket listings, PersonScheduleTracker
  reservations, HouseholdErrandPlanRegistry plans, BoardingHouse
  reservations and HotelRoom reservations; proved copy-safe and left
  stored-direct: civic treasury ledger entries, property dispute
  claims, tenancy rent obligations, title records, housing
  buildings/spaces/assignments/agreements, market sales and
  inspections, staged-production state (registry-unwired, test-only),
  WageSettlementService events. Boarding/hotel clones are outside the
  harness subset and are Unity-session verified only. See
  Docs/Pass12_Independent_Review.md for the full table.
- RUN-011: CLOSED in Pass XII (Worker F test-correction review, brief
  §10). All three historical failures were TEST-WRONG; production
  untouched. MoneyOrderServiceTests.IssueDuplicate_
  ReplacesLostOrderFree: second duplicate of an already-Duplicated
  order now asserted refused (IsNull), matching the test's own
  governing message. RunSettlementCycle_NetsAndSettlesInFull: net
  corrected 2500 to 3000 (3500 - 500), funds corrected to po-a 0 and
  po-b 4000. P5PropertyLegalJourneyTests.Driver_
  LeviesAnnualAssessmentOncePerTaxYear: day-100 status corrected
  Assessed to Delinquent (due day 60, Canon staged collection), with
  the once-per-year count assertion unchanged. No test deleted, no
  assertion weakened.
- M2 (Pass XI residual): CLOSED in Pass XII (Worker F). Business
  InstanceState serialized owner default flipped Player to Town;
  every production creation path sets the owner explicitly. Enum
  order unchanged; already-serialized scene instances keep their
  stored value and still need a Codex scene audit.
- GeneralStore auto-create flag (Pass XI/Worker B referral): CLOSED
  at source default in Pass XII (Worker F). createPlayerBusiness
  OnInitialize default flipped true to false; the bootstrapper
  suppression remains as belt-and-braces for scene instances
  serialized with the old default. The file is harness-excluded, so
  this half is Unity-verified only.
- PASS11-004: still OPEN (unchanged by Pass XII). RegisterHouse still
  has zero production callers; the three gaps (parcel mapping,
  boarding trigger rule, bootstrapped houses lacking
  HousingAuthority/LodgingRegistry registration) are unchanged.
- PASS11-005: still OPEN (unchanged by Pass XII). Worker C audited
  and ruled out business liquidation, HousingSearch negotiations and
  tax-sale bid name strings as buyer sources; no solvent-household
  buyer register, appraised parcel sale-price feed or creditor
  consent source was built.
- PASS11-006: still OPEN as Unity scene work. Pass XII added the
  ScenarioHudSnapshotBuilder contract consuming a failure board, but
  it has no production caller; live panel binding remains Codex work.
- PASS11-008: still OPEN. M1/M2 Unity-runner bootstrap tests remain
  unrun in a real Unity Test Runner session.
- PASS8-005: still OPEN; count language updated. The registry now
  holds sixteen detailed business runtimes (ButcherRuntime,
  BlacksmithRuntime, TanneryRuntime added by Worker F) plus
  ScenarioFailureNoticeBoard (a presentation surface, not a runtime).
- PASS9-004, PASS9-008, PASS9-010, PASS9-011, PASS9-012, PASS9-013,
  PASS9-014, IDENT-001 remainder, PASS8-006, PASS7-003: unchanged by
  Pass XII; still OPEN with their existing reasons. No item was
  silently closed.

## New deferred items discovered in Pass XI

- PASS11-001 (P2): ConstructionProjectExecutor.sequence was not
  persisted in ConstructionProjectSaveDto, so a restored executor
  re-minted colliding project ids (found by the WS4 proof; proof
  restore compensated). FIXED in Pass XI follow-up: additive
  ExecutorSequence field, restore takes max(persisted, recomputed
  floor), old saves load safely. Verified by
  Pass11Ws4SaveLoadFixTests; proof compensation removed.
- PASS11-002 (P2): FreightShipmentService.dispatchAuthorities was not
  persisted in FreightServiceSaveDto, so a restored in-transit
  shipment completed without releasing its wagon reservation, leaving
  a stale hold (found by the WS4 proof; proof restore compensated).
  FIXED in Pass XI follow-up: additive
  ShipmentIdsWithDispatchAuthorities field, restore rebinds the map
  for transport-bound live shipments (empty list on old saves falls
  back to shipment state), release idempotent, double restore
  overwrites the same map key. Limitation: if hooks.Reservations is
  null at restore, nothing can be rebound (same pattern as the
  existing task-binding rebind). Verified by
  Pass11Ws4SaveLoadFixTests; proof compensation removed.
- PASS11-003 (P3): A manual PayAgreedWage for the identical missed
  week after lookup recovery could duplicate the accrued refused-week
  liability (WS6 first-gate M3 residual). Automated payroll cannot
  double-pay it (weekly payroll pays the current week only). The
  weekly diagnostic directs settlement through the liability route;
  mechanical enforcement is a design decision, not invented here.
  Owner: Muse source pass with design review.
- PASS11-004 (P2): BoardingLodgingBridge.RegisterHouse has zero
  production callers (found by WS2 verification, not patched with
  invented data). Bootstrapped houses get runtime rooms but no
  HousingAuthority building or LodgingRegistry registration, and
  bridge CheckIn always refuses. Fixing it needs a parcel mapping
  (BusinessInstanceState carries only AssignedBuildingId) and a
  boarding trigger rule. Owner: Muse source pass with design input.
- PASS11-005 (P2): Named-buyer distress sales have no legitimate buyer
  source (found by WS2, honest refusal kept). Missing: a solvent
  household buyer register with willingness evidence, an appraised
  parcel sale-price feed, and a creditor consent source for note
  assumption. Ruled out as sources: the business liquidation port,
  HousingSearch negotiations, and tax-sale bid name strings. Owner:
  Muse source pass with design review.
- PASS11-006 (P3): StartPanel live wiring of ScenarioFailureNoticeBoard
  and ScenarioObjectivePresentation into the panel (WS5 source
  surface done and tested; the panel reads director state only and
  holds no ScenarioObjectiveService reference). Wiring is
  scene/reference work for a Unity session. Owner: Unity session.
- PASS11-007 (P3): Legacy-slot payroll path still refuses a throwing
  lookup with no accrual, unlike the employment path (WS6 first-gate
  M3 residual, deliberately unchanged to keep the diff minimal).
  Consider mirroring the employment-path accrual. Owner: Muse source
  pass.
- PASS11-008 (P3): M1/M2 Unity-runner bootstrap tests
  (Tests/Editor/Scenarios/Pass11Ws6FirstGateBootstrapTests.cs) are
  delivered but were never run in the source environment (no Unity
  runner; the bootstrap files cannot compile in the Roslyn harness).
  They need a real Unity Test Runner pass. Owner: Unity session.

## Fixed in Pass VI (retained with evidence)

- PASS6-012a: HouseholdSplitDriver ghost-household bug. Fixed by ending the old membership before FormHousehold and applying the same correction to MarriageDriver. Verified by Section25ObservedUnobservedTests and the full harness.
- PASS6-012b: Death did not end employment, creating a payroll money sink. Fixed by adding EmploymentEndReason.Death, EmploymentRelationshipRegistry.EndEmploymentsForDeceased, and wiring the employment registry into the mortality path. Verified by WageSettlementTests and the full harness.
- PASS6-012c: Sim-born children never gained labor access. Fixed by sharing the LaborAccessForAgeBand mapping and applying it in PersonState.RefreshDerivedAge. Verified by DemographicContinuityTests and the full harness.
- PASS6-012d: Recruitment pool included the dead. Fixed with deathDayIndex guards in FindCandidate and DirectApproach. Verified by RecruitmentTests and the full harness.
