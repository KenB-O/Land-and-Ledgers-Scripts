# Pass VI Priority Fix Register

This register ranks every actionable finding from the Pass VI cohesion audit. Each entry carries scope, dependencies, impact, evidence, testing approach, and whether it is Muse-fixable (source change validatable in the Roslyn harness) or Codex-required (Unity, visual, or interactive validation). P0 is data corruption or a money sink in production. P1 breaks a flagship chain. P2 is a source-only or partially connected system. P3 is a missing system or design-direction item.

## P0 (fixed in Pass VI)

### P0-1: HouseholdSplitDriver ghost-household bug
- Scope: FamilyFormationDrivers.SplitHousehold and Marry; one-line membership fix in each.
- Dependencies: none.
- Impact: split and marriage diagnostics lied; ghost households accumulated; persons counted as moved while still in the old household.
- Evidence: Worker D (test-verified); Pass VI source verification.
- Testing: Section25ObservedUnobservedTests; full harness 828/4.
- Disposition: FIXED. Muse-fixable; fixed and verified.

### P0-2: Death did not end employment (payroll money sink)
- Scope: EmploymentEndReason.Death (append-only enum), EmploymentRelationshipRegistry.EndEmploymentsForDeceased, DemographicDriverContext.Employments (additive field), MortalityDriver.RecordDeath wiring, PopulationManager population.
- Dependencies: none.
- Impact: the deceased were paid weekly; household income never dropped; cash conservation violated.
- Evidence: Worker A finding 2; Worker D finding 3 and case A.
- Testing: WageSettlementTests; full harness 828/4.
- Disposition: FIXED. Muse-fixable; fixed and verified.

### P0-3: Sim-born children never gained labor access
- Scope: HouseholdLifecycleManager.LaborAccessForAgeBand (new public static sharing the canonical mapping); PersonState.RefreshDerivedAge applies it.
- Dependencies: none.
- Impact: a growing class of permanent non-participants; sim-born adults could not be hired or act for households.
- Evidence: Worker A finding 1.
- Testing: DemographicContinuityTests; full harness 828/4.
- Disposition: FIXED. Muse-fixable; fixed and verified.

### P0-4: Recruitment pool included the dead
- Scope: deathDayIndex guards in RecruitmentService.FindCandidate and DirectApproach.
- Dependencies: none.
- Impact: deceased persons could be hired.
- Evidence: Worker A finding 5.
- Testing: RecruitmentTests; full harness 828/4.
- Disposition: FIXED. Muse-fixable; fixed and verified.

## P1 (flagship breaks, registered)

### P1-1: Town-pulse demand manufactures revenue with no buyer
- Scope: SharedBusinessRuntimeManager.TrySellTownPulseDemand / TrySellTownPulseDemandFromBusiness; BusinessRuntimeState.ResolveDailySalesPlaceholder; live caller GeneralStoreRuntimeManager.ResolveTownPulseSupportIndustryDemand.
- Dependencies: a real household-demand source to replace the placeholder.
- Impact: businesses earn money from no one; violates OffScreenSimulationRules rule 5; invalidates economic analysis.
- Evidence: Worker D finding 1; Pass VI source verification (Document C chain 24).
- Testing approach: harness test asserting every revenue credit has a matching household debit; remove or gate the placeholder.
- Muse-fixable or Codex-required: Muse-fixable in source, BUT the replacement (real household demand for support-industry goods) is a gameplay-balance decision. Registered, not fixed unilaterally.

### P1-2: Estate administration has no runtime driver; business ownership is OwnerKind-keyed
- Scope: EstateService.RegisterDecedentParcel (no callers), EstateAdministration / Will / WillContest (no production callers), BusinessInstanceState.OwnerKind (never a person).
- Dependencies: estate inventory, distribution, and closure driver; person-keyed ownership path.
- Impact: the widow cannot inherit; businesses are effectively immortal; the provider-death chain stops at estate opening.
- Evidence: Worker A findings 3 and 8; Worker D case A (Document C chain 9).
- Testing approach: harness test driving a death through estate distribution to ownership transfer.
- Muse-fixable or Codex-required: Muse-fixable (source). Ranked as the highest-value P1 after the revenue defect.

### P1-3: No NPC-to-NPC conversation; the foreman chain cannot run
- Scope: conversation system, Socializing planning, SaloonRuntime production wiring, belief store, MineRole foreman.
- Dependencies: none (greenfield within existing architecture).
- Impact: the flagship social chain (foreman to saloon to rumor to action) is absent at steps 2 through 8.
- Evidence: Worker C findings 1, 4, 5, 8 (Document C chain 2).
- Testing approach: harness test tracing an observation through conversation to a behavior change, asserting provenance.
- Muse-fixable or Codex-required: Muse-fixable (source). This is the flagship social gap.

### P1-4: Horse reservation split (double-booking)
- Scope: HorseAuthority (static) versus DraftPowerService unified-authority reservations.
- Dependencies: design decision on the canonical reservation mechanism.
- Impact: one horse works two jobs in one day; scheduling assumes availability that does not exist.
- Evidence: Worker B findings F1-F7 (Document D flagship).
- Testing approach: harness test reserving the same horse for draft and mount, asserting refusal.
- Muse-fixable or Codex-required: Muse-fixable once the canonical mechanism is chosen. Registered pending the design decision.

### P1-5: Horse condition has no production lifecycle
- Scope: HorseFoundation.ApplyCare (no production callers), feed-loop horse path (no callers), IsDraftCapable (ignores condition).
- Dependencies: P1-4 (availability unification) for full value, but independently implementable.
- Impact: horses never tire and never need care; draft eligibility ignores condition.
- Evidence: Worker B findings F2, F3.
- Testing approach: harness test working a horse and asserting condition decline and care recovery.
- Muse-fixable or Codex-required: Muse-fixable (source).

### P1-6: Quick-diligence clues invented; inspection leaks hidden state
- Scope: diligence clue generation (seeded switch); BuildMineInspectionLine (no diligence gating).
- Dependencies: none.
- Impact: business intelligence is fabricated; hidden mine richness leaks to unqualified observers.
- Evidence: Worker C findings 2 and 3.
- Testing approach: harness test asserting clues read business state and inspection respects diligence.
- Muse-fixable or Codex-required: Muse-fixable (source).

## P2 (source-only or partially connected, registered)

### P2-1: Mine closure and business failure are source-only
SetOperatingPosture and NpcBusinessLifecycleService.EvaluateWeek have no production callers; no layoff concept. Wire the runtime drivers. Muse-fixable.

### P2-2: Farm harvest cycle never executes
CropChain.HarvestField, TendField, PlantField have no production callers; PlowWorkService unreferenced. Add the seasonal runtime driver. Muse-fixable.

### P2-3: Illness is economically inert
laborCapacityPercent and HealthWorkImpactBand have no consumers. Connect health to work capacity, wages, and business response. Muse-fixable.

### P2-4: Manager decisions (except OpenRecruitment) have no follow-through
SimulationDrivers executes only OpenRecruitment. Implement execution for Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset. Muse-fixable.

### P2-5: Water and hydration missing
No supply, utility, shortage mechanic, or household effects. Narrow pathway: reserve category, hydration state, DailyNeedsService phase (Worker A). Muse-fixable (source); balance tuning may need playtesting.

### P2-6: Saloon is test-only
ServeDrink and ServeMeal have no production callers; no recreation concept; no travel to saloon. Wire the saloon into the population and economy, or scope it explicitly. Muse-fixable.

### P2-7: Migration-in unconditioned by housing
Flat 4% monthly arrival; host picked by fewest members, not housing availability. Gate arrivals on accommodation. Muse-fixable.

### P2-8: Remarriage impossible
Spouse kinship edge survives death with no removal API. Add edge removal and drive remarriage through real relationships. Muse-fixable; policy input on remarriage rules.

### P2-9: Journey travel uses phantom horses
TravelMode.Horseback is a speed constant with no animal binding. Bind travel to real animals. Muse-fixable. Depends on P1-4.

### P2-10: Worker time double-bookable
Draft-driver hold invisible to WorkScheduler. Unify time reservations. Muse-fixable.

### P2-11: No guardianship; DissolveHousehold has no production callers
Orphaned children have no guardian assignment. Drive guardianship and household dissolution. Muse-fixable.

### P2-12: Business ownership does not follow the person
OwnerKind seam (documented in EstateService). Person-keyed ownership path. Muse-fixable. Related to P1-2.

### P2-13: Wages flat regardless of hours or capacity
Health-to-income link severed. Design question: piece rates, capacity-adjusted pay, or accepted abstraction. Registered for a policy decision.

### P2-14: Horse sale does not clear reservations; rental has no production path
Clear reservations on trade; build the rental path. Muse-fixable. Depends on P1-4.

### P2-15: Hiring succeeds with no accommodation; no employer lodging
Add housing check to HireFromInquiry or build employer lodging. Muse-fixable.

### P2-16: Sleep/rest has no physiological consequence; social contact is activity-label only
Wire rest to a real need or scope explicitly. Muse-fixable.

## P3 (missing systems and design direction, registered)

### P3-1: Railroad arrival or bypass
Design direction only. No events, no market effects. Registered; no action until scoped.

### P3-2: Fire, property damage, and insurance
Entirely absent. Registered; no action until scoped.

### P3-3: Telegraph
Entirely absent. Registered; subsumed by P1-3 (information travel) if scoped.

### P3-4: Merchant-to-household store-tab credit
Private-lender credit exists; the store tab specifically does not. Registered.

### P3-5: Buildings have no economic occupancy capacity
Registered.

### P3-6: Animals cannot be pledged as collateral
Registered.

### P3-7: Land-use allocation (farming versus grazing versus building)
Registered.

## Muse-fixable versus Codex-required summary

All P0 items were Muse-fixable and are fixed. All P1 and P2 items are Muse-fixable in source except where a gameplay-policy decision is required (P1-1 revenue replacement, P2-13 wage model, P2-8 remarriage rules, P1-4 canonical reservation mechanism). No P1/P2 item requires Unity, visual, or interactive validation; all are validatable in the Roslyn harness. The Codex/Unity backlog (RUN-001 through RUN-006, RUN-009, RUN-010, RUN-012, LL-PLAY-001 through LL-PLAY-019) is preserved untouched in Document F.
