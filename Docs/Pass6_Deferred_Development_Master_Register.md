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

- LL-PLAY-001: Ghost player-owned General Store/Renting and Boarding. Tested New Game improvement requires wider verification. Status: OPEN.
- LL-PLAY-002: Excessive PlayMode-only manager roots. Status: OPEN.
- LL-PLAY-003: Duplicate Civic Foundation. Status: OPEN.
- LL-PLAY-004: Obsolete first-session tutorial guidance. Status: OPEN.
- LL-PLAY-005: Missing scenario identity, objectives and deadlines in HUD. Status: OPEN.
- LL-PLAY-006: Blacksmith management-panel scrolling and spacing. Status: OPEN.
- LL-PLAY-007: Seller meeting modal behavior. Status: OPEN.
- LL-PLAY-008: Repetitive acquisition controls. Status: OPEN.
- LL-PLAY-009: Missing sale-discovery provenance. Status: OPEN.
- LL-PLAY-010: Rigid Build Shell / Next Shell construction workflow. Status: OPEN.
- LL-PLAY-011: Business labels without proven real operating Tasks. Status: OPEN.
- LL-PLAY-012: Arbitrary staffing 1/2 controls versus actual workload. Status: OPEN.
- LL-PLAY-013: Implausible profits and incomplete financial-period presentation. Status: OPEN.
- LL-PLAY-014: Unclear Civic count-hall language. Status: OPEN.
- LL-PLAY-015: Unexplained resource and district metrics. Status: OPEN.
- LL-PLAY-016: Flat terrain. Status: OPEN.
- LL-PLAY-017: Historical weekday/date UI mismatch. Status: OPEN.
- LL-PLAY-018: Confusing General Store startup/restocking notices. Status: OPEN.
- LL-PLAY-019: MainMusicPlayer scene hierarchy/lifecycle. Status: OPEN.

## New deferred items discovered in Pass VI

These are findings from the Pass VI cohesion audit that are registered for future work, not fixed in this pass. See Document E for full detail and prioritization.

- PASS6-001: Town-pulse demand manufactures business revenue with no buyer (P1). Integration path designed, ranked in Document E. FIXED in Pass VII Workstream A, see below.
- PASS6-002: Estate administration has no runtime driver; business ownership is OwnerKind-keyed (P1). Ranked in Document E. FIXED in Pass VII Workstream C (runtime lifecycle driver), see below. Runtime auto-advancement remains open, see PASS7-001.
- PASS6-003: Remarriage impossible through real relationships (P2). Ranked in Document E. Still open.
- PASS6-004: Horse reservation split, two disjoint availability truths (P1). Integration path designed, ranked in Document E. FIXED in Pass VII Workstream B, see below.
- PASS6-005: Horse condition has no production lifecycle (P1). Ranked in Document E. FIXED in Pass VII Workstream B, see below.
- PASS6-006: Person-level water and hydration missing (P2). Narrow implementation pathway documented, ranked in Document E. Design completed in Pass VII, implementation deferred, see PASS7-002.
- PASS6-007: No NPC-to-NPC conversation system; Socializing never planned (P1). Ranked in Document E. FIXED in Pass VII Workstream E (minimal production foundation), see below.
- PASS6-008: Quick-diligence clues invented; inspection leaks hidden state (P1). Ranked in Document E. Still open.
- PASS6-009: Mine closure and business failure are source-only (P2). Ranked in Document E. Still open.
- PASS6-010: Farm harvest cycle never executes at runtime (P2). Ranked in Document E. Still open.
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

- PASS8-001 (P1): Wire the detailed mine ore chain. MineProductionCycle and the assay/shipment/settlement chain are built and tested but have zero production callers; the abstract weekly path is live. Needs a weekly driver and reconciliation of the two paths. Owner: Muse source pass.
- PASS8-002 (P1): Production freight tick drive. Revenue collection scan exists and is tested, but FreightCompanyRuntime has zero production instances. Wiring a drive means designing freight dispatch production ownership. Owner: Muse source pass, Unity-adjacent.
- PASS8-003 (P1): Production appraisal source for asset sales. The NPC owner-resolution policy is proven but the live appraisal seam is null, so production sale requests are conservatively denied. Needs a real appraisal authority or documented policy valuations. Owner: Muse source pass.
- PASS8-004 (P2): Childcare, caregiving, recovery person-time. Dependents bind no one's schedule. All seams exist; add task definitions, append PersonActivityKind values, schedule through the domestic driver. Owner: Muse source pass.
- PASS8-005 (P2): Wire individual detailed business runtimes. Twelve runtimes registered-unwired in Docs/UnwiredSystems.md; each wiring is a separate scoped item. Owner: Muse source pass.
- PASS8-006 (P2): Migration driver and journey integration. LaborMigrationLinker evaluates but moves nobody; no integrator maps intents to journeys. Owner: Muse source pass.
- PASS8-007 (P2): Remaining social-knowledge chains (tenant/full boarding house, farmer equipment sharing, wage-driven migration). Each needs its real-state authority and decision consumer on the shared evaluator pattern. Owner: Muse source pass.
- PASS8-008 (P2): Service profile calibration. The demand gate works; visit caps and price calibration per service type are untuned. Owner: Muse source pass, needs live-play observation.
- PASS8-009 (P2): Unbounded schedule/packet list remediation. Linear scans on daily hot paths with monotonic growth; needs campaign-scale measurement before tuning, then pruning or indexing. Must preserve truth. Owner: Muse source pass.
- PASS8-010 (P2): Player construction input-support harness coverage. The only tests need UnityEditor; extract the availability rules into a Unity-free seam. Owner: Muse source pass.
- PASS8-011 (P3): Player UI for pending owner requests. NPC policy resolves NPC businesses; player-owned requests await a decision UI. Owner: Codex/Unity.
- PASS8-012 (P3): Full foreclosure-auction sale of seized collateral. Enforcement today is satisfaction-in-kind; auction needs legal-rule design. Owner: Muse source pass with design review.
- PASS8-013 (P3): Household and personal lending through the bank. Business loans served; household borrowing stays with existing directories. Owner: Muse source pass.
- PASS8-014 (P3): Due-event scheduler hub-level clear. Stale events for purged persons are consumed harmlessly; a hub-level new-game clear would be cleaner. Owner: Muse source pass.
- PASS8-015 (P3): Remove vestigial seams (ManagerExecutionServices.FindAsset and similar). Trivial hygiene. Owner: Muse source pass.
- PASS8-016 (P3): Sanitation and water implementation. Both deferred on Canon policy with unblock criteria in Docs/Pass8_Water_Verdict.md. Do not implement until policy decisions land. Owner: Kennedy/design review.

## New deferred items discovered in Pass VII

- PASS7-001: Estate runtime auto-advancement. The EstateLifecycleDriver is complete and tested but has no daily runtime caller; estates advance only when driven by tests or the scripted AutonomousSocietyDriver.OpenAndSettleEstate demo. A daily estate-processing step in the demographic driver is the designed path. Severity P1. Owner: Muse source pass.
- PASS7-002: Person-level water system. Full design and authority map completed in Pass VII (see re-audit). Existing: horse care water scalar, regional watercourse geography, infrastructure capability flags. Missing: physical water goods, wells, hauling, household reserves, person hydration, livestock consumption, commercial demand, sanitation, firefighting. Implementation deferred pending Canon policy decisions on requirement rates and health effects. Severity P2. Owner: Muse source pass with design review. Pass VIII Worker 9 verdict (2026-10-13): Canon evidence reviewed; requirement rates, health/death consequences, and water rights (proposal 136, not approved) remain undetermined — policy-dependent portion deferred again, options A-D and exact unblock criteria recorded in Docs/Pass8_Water_Verdict.md. No water mechanics implemented.
- PASS7-003: Mine closure lifecycle runtime wiring (PASS6-009 carried forward). Still source-only. Severity P2. Owner: Muse source pass.
- PASS7-004: Farm harvest cycle runtime driver (PASS6-010 carried forward). Still no production callers for HarvestField, TendField, PlantField. Severity P2. Owner: Muse source pass.
- PASS7-005: Illness economic integration (PASS6-011 carried forward). laborCapacityPercent and HealthWorkImpactBand still have no consumers. Severity P2. Owner: Muse source pass.
- PASS7-006: Retail capacity pool sharp edge (found in 90-day simulation). ReleaseProductSpace calls GetOrCreatePool, which mints a zero-allocation pool on first sale; TryOccupyProductSpace then refuses all future shipment receives for that product line, and AdvanceShipments marks shipments "Delivered" anyway, so goods vanish. Workaround: configure pool capacity at build. A store that never configures shelf capacity silently loses every shipment after its first sale. Severity P1. Owner: Muse source pass.
- PASS7-007: AddCategoryStockUnits caps silently (found in 90-day simulation). Returns acceptedUnits, but callers ignoring the return value over-count production. All Pass VII drivers record only accepted units. Audit existing callers. Severity P2. Owner: Muse source pass.
- PASS7-008: Shipment receive failure masked as Delivered (found in 90-day simulation). When TryReceive fails, the shipment is still marked "Delivered" with only a diagnostic line. Drivers must measure actual stock deltas rather than trusting ledger status. Severity P2. Owner: Muse source pass.

## Fixed in Pass VI (retained with evidence)

- PASS6-012a: HouseholdSplitDriver ghost-household bug. Fixed by ending the old membership before FormHousehold and applying the same correction to MarriageDriver. Verified by Section25ObservedUnobservedTests and the full harness.
- PASS6-012b: Death did not end employment, creating a payroll money sink. Fixed by adding EmploymentEndReason.Death, EmploymentRelationshipRegistry.EndEmploymentsForDeceased, and wiring the employment registry into the mortality path. Verified by WageSettlementTests and the full harness.
- PASS6-012c: Sim-born children never gained labor access. Fixed by sharing the LaborAccessForAgeBand mapping and applying it in PersonState.RefreshDerivedAge. Verified by DemographicContinuityTests and the full harness.
- PASS6-012d: Recruitment pool included the dead. Fixed with deathDayIndex guards in FindCandidate and DirectApproach. Verified by RecruitmentTests and the full harness.
