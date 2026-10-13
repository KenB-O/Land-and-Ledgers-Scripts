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

- PASS6-001: Town-pulse demand manufactures business revenue with no buyer (P1). Integration path designed, ranked in Document E. Not fixed in Pass VI (gameplay-policy decision required).
- PASS6-002: Estate administration has no runtime driver; business ownership is OwnerKind-keyed (P1). Ranked in Document E.
- PASS6-003: Remarriage impossible through real relationships (P2). Ranked in Document E.
- PASS6-004: Horse reservation split, two disjoint availability truths (P1). Integration path designed, ranked in Document E.
- PASS6-005: Horse condition has no production lifecycle (P1). Ranked in Document E.
- PASS6-006: Person-level water and hydration missing (P2). Narrow implementation pathway documented, ranked in Document E.
- PASS6-007: No NPC-to-NPC conversation system; Socializing never planned (P1). Ranked in Document E.
- PASS6-008: Quick-diligence clues invented; inspection leaks hidden state (P1). Ranked in Document E.
- PASS6-009: Mine closure and business failure are source-only (P2). Ranked in Document E.
- PASS6-010: Farm harvest cycle never executes at runtime (P2). Ranked in Document E.
- PASS6-011: Illness is economically inert (P2). Ranked in Document E.
- PASS6-012: Sim-born labor access, death ends employment, ghost split, recruitment dead check (P1). FIXED in Pass VI, see Document E and the fix commits.

## Fixed in Pass VI (retained with evidence)

- PASS6-012a: HouseholdSplitDriver ghost-household bug. Fixed by ending the old membership before FormHousehold and applying the same correction to MarriageDriver. Verified by Section25ObservedUnobservedTests and the full harness.
- PASS6-012b: Death did not end employment, creating a payroll money sink. Fixed by adding EmploymentEndReason.Death, EmploymentRelationshipRegistry.EndEmploymentsForDeceased, and wiring the employment registry into the mortality path. Verified by WageSettlementTests and the full harness.
- PASS6-012c: Sim-born children never gained labor access. Fixed by sharing the LaborAccessForAgeBand mapping and applying it in PersonState.RefreshDerivedAge. Verified by DemographicContinuityTests and the full harness.
- PASS6-012d: Recruitment pool included the dead. Fixed with deathDayIndex guards in FindCandidate and DirectApproach. Verified by RecruitmentTests and the full harness.
