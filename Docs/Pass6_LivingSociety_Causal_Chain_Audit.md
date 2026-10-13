# Pass VI Causal Chain Audit

This document traces at least 20 end-to-end causal chains through the source code, verifying whether each link exists in production. Per-chain fields: trigger, steps with class/method references, expected outcome, actual outcome, classification, adverse outcomes, evidence, and disposition. Adverse outcomes are included where the chain produces a wrong result, not merely an absent one.

Classification key: CONFIRMED (every link runs in production), PARTIAL (some links live), BROKEN (a link produces a wrong result), ABSENT (no production path), TEST-ONLY (exercised only in tests), SOURCE-ONLY (correct logic, no production callers).

## Chain 1: Provider death (flagship)

- Trigger: the household's primary earner dies.
- Steps: MortalityDriver.RecordDeath (DemographicDrivers.cs) -> HouseholdLifecycleManager.RecordDeath (ends membership, retains record) -> EmploymentRelationshipRegistry.EndEmploymentsForDeceased (Pass VI fix; reason Death) -> WageSettlementService no longer pays the deceased -> EstateService.OpenEstate (kinship-derived heirs, dower interest) -> HouseholdDecisionService re-evaluates under income pressure -> SeekIncome / Borrow / Economize.
- Expected: household income drops, estate opens, dependents supported through hardship mechanisms, no identity duplication or deletion.
- Actual (after Pass VI fix): income drops correctly; estate opens; hardship chain engages. Before the fix, the deceased kept being paid weekly (money sink) and the widow's income never dropped.
- Classification: CONFIRMED (after fix). Estate distribution and closure remain SOURCE-ONLY.
- Adverse outcomes (pre-fix): payroll money sink; widow income never drops; business keeps running as if nothing happened.
- Evidence: Worker A findings 2 and 3; Worker D case A; Pass VI fix verified by WageSettlementTests and full harness (828/4).
- Disposition: FIXED in Pass VI. Estate administration runtime driver registered as P1 (Document E).

## Chain 2: Foreman rumor (flagship)

- Trigger: a mine foreman observes a change (rich vein, accident, closure risk) and the information should reach the saloon, spread as rumor, and move NPC behavior (labor supply, investment, prices).
- Steps: (1) foreman observes mine state; (2) foreman tells bartender; (3) bartender tells patrons; (4) rumor spreads with provenance; (5) NPCs act on it; (6) labor/investment/prices respond.
- Expected: information travels through real conversation with provenance before actors react.
- Actual: Step 1 partially exists (mine state is real). Steps 2 through 8 do not exist. No NPC-to-NPC conversation system. Socializing is never planned (zero reservation call sites). SaloonRuntime is test-only with no production callers. No general belief store. No foreman role in MineRole.
- Classification: ABSENT (steps 2-8). The information-authority contract (provenance, observer-dependence) is sound where information exists.
- Adverse outcomes: none (clean absence, honestly reported). Quick-diligence clues are invented rather than read from state (incorrect authority, separate P1).
- Evidence: Worker C findings 1, 2, 4, 5, 8.
- Disposition: REGISTERED as P1 (Document E). This is the flagship social gap.

## Chain 3: Shared horse, competing commitments (flagship)

- Trigger: one horse is needed for plowing (draft) and riding (mount) on the same day.
- Steps: HorseAuthority.TryReserveUse (mount) vs DraftPowerService.TryReserveAnimals (draft) -> availability check -> reservation -> work consumes the horse's day.
- Expected: one horse, one availability truth; the second commitment is refused or queued; genuine opportunity cost.
- Actual: one horse, one stable identity, but TWO disjoint availability truths. HorseAuthority.CurrentUse and the unified-authority animal key do not cross-check. A plowing horse can be mounted and vice versa. Real cross-system double-booking.
- Classification: BROKEN. The defect is confirmed in source, not merely absent.
- Adverse outcomes: the same horse works two jobs in one day; no time cost is honored; downstream scheduling assumes availability that does not exist.
- Evidence: Worker B findings F1-F7; Pass VI source verification.
- Disposition: REGISTERED as P1 (Document E). Unifying the reservation mechanisms is a design decision (which mechanism is canonical).

## Chain 4: Food scarcity

- Trigger: household food reserves run out.
- Steps: DailyNeedsService.ExecuteDay consumes real lots -> missed meals -> PersonNutritionState.ApplyDay -> WorkCapacityMultiplier reduced -> WorkTimeBudgetStore updated -> HouseholdShortageMonitor raises purchasing need -> batched errand coordinator shops with real cash -> hardship chain (Borrow from private lender) if cash is short.
- Expected: hunger reduces work capacity; the household shops, economizes, or borrows through honest mechanisms.
- Actual: the full chain runs in production. Refusals are honest.
- Classification: CONFIRMED.
- Adverse outcomes: wages are flat agreed amounts regardless of capacity, so undernourishment reduces the time budget but never the paycheck. The health-to-income link is severed (P2).
- Evidence: Worker A (food chain working well); Worker D case C.
- Disposition: ACCEPTED with the wage-capacity severance registered as P2 (Document E).

## Chain 5: Water scarcity

- Trigger: settlement water supply fails or runs short.
- Steps: (none exist).
- Expected: shortage raises need, triggers procurement or rationing, affects households, livestock, production, sanitation.
- Actual: no water supply system, no utility business, no shortage mechanic. AnimalRegistry.Hydration01 exists but nothing writes it. HorseFoundation.ApplyCare has no production callers.
- Classification: ABSENT.
- Adverse outcomes: none (clean absence). The brief's caution is respected: no hydration mechanic is inferred.
- Evidence: Worker A finding 6; Worker D case D.
- Disposition: REGISTERED as P2 with a narrow implementation pathway (Document E).

## Chain 6: Business reaction to shortage

- Trigger: a real shortage or demand change (e.g., grain shortfall, labor scarcity).
- Steps: ManagerBrain.DailyCycle decides (Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset, OpenRecruitment) -> SimulationDrivers executes the decision -> business state changes.
- Expected: the business responds through legitimate actions.
- Actual: ManagerBrain decides, but SimulationDrivers executes ONLY OpenRecruitment. The other six decision types are recorded history with no physical follow-through.
- Classification: PARTIAL (decisions real, execution missing for 6 of 7 types).
- Adverse outcomes: the business appears to decide but nothing happens; a reviewer reading ManagerBrain alone would believe the business reacts.
- Evidence: Worker D decision-driver audit, finding 4 (P2).
- Disposition: REGISTERED as P2 (Document E).

## Chain 7: Financial distress

- Trigger: household income collapses (job loss, death, illness).
- Steps: HouseholdAutonomyDriver builds pressure inputs (obligations, reserves, expected income, shelter risk) -> HouseholdDecisionService.Evaluate selects from the causal ladder (SeekIncome, Economize, Borrow, RelocateCheaper, NegotiateTerms, SellAssets) -> ExecuteBorrow reaches a real private lender -> AcceptOffer advances real cash against a real FinancialObligation -> repayment enforced through the obligation authority.
- Expected: constrained choices, honest credit (lenders can refuse), no phantom money.
- Actual: the chain runs in production. Credit is funded and honest. No unfunded lending found.
- Classification: CONFIRMED.
- Adverse outcomes: none found. Checked and cleared: no automatic wage generation, no unfunded lending, no arbitrary prosperity balancing.
- Evidence: Worker D case C and decision-driver audit.
- Disposition: ACCEPTED.

## Chain 8: Housing and labor interdependence

- Trigger: a business needs workers who need housing.
- Steps: OpenRecruitment -> RecruitmentService finds real candidates -> HireFromInquiry -> worker needs accommodation -> boarding search -> BoardingHouseShopRuntime.
- Expected: employment demand cannot import workers without housing; housing does not spawn workers.
- Actual: recruitment uses only real persons and refuses faked inquiries; migration explicitly guards against auto-spawning. But HireFromInquiry performs no housing check, so hiring succeeds with no accommodation. No employer-lodging system exists. NpcCommissionedBuild has no production callers, so boarding shortage never triggers construction.
- Classification: PARTIAL.
- Adverse outcomes: workers hired with nowhere to live; boarding shortage never resolves through construction.
- Evidence: Worker D case I.
- Disposition: REGISTERED as P2 (Document E).

## Chain 9: Widow takes over the business

- Trigger: a business owner dies.
- Steps: death -> estate opens -> business ownership transfers to the widow -> she chooses continuation, hired management, sale, or family help.
- Expected: ownership follows the person through the estate.
- Actual: EstateService.OpenEstate runs, but RegisterDecedentParcel has no callers (nothing is inventoried), EstateAdministration has no runtime driver, and BusinessInstanceState.OwnerKind is Player/Npc/Town (never a person). Person-level inheritance is structurally impossible. The business keeps running as if nothing happened.
- Classification: BROKEN (estate opens but the ownership path is structurally absent).
- Adverse outcomes: the business is effectively immortal; the widow has no agency over it.
- Evidence: Worker A findings 3 and 8; Worker D case A.
- Disposition: REGISTERED as P1 (Document E). Requires a runtime estate driver and a person-keyed ownership path.

## Chain 10: Child leaves home

- Trigger: an adult child (18+, employed or married) leaves the parental household.
- Steps: HouseholdSplitDriver (monthly) -> SplitHousehold -> FormHousehold -> membership transfer -> new household with its own ledger.
- Expected: the same person moves; no duplication; no ghost household.
- Actual (after Pass VI fix): the old membership ends before FormHousehold, the person is registered in the new household, and the split is logged truthfully. Before the fix, the membership transfer was rejected, FormHousehold returned a ghost household anyway, and the split was logged as a false success.
- Classification: CONFIRMED (after fix).
- Adverse outcomes (pre-fix): ghost households accumulate; diagnostics lie; the child is counted as departed while still in the parental household.
- Evidence: Worker D case B (test-verified); Pass VI fix verified by Section25ObservedUnobservedTests and full harness.
- Disposition: FIXED in Pass VI. Migration-in gating (flat 4%, host by fewest members, unconditioned by housing) registered as P2.

## Chain 11: Mine closure

- Trigger: a mine becomes uneconomic.
- Steps: SetOperatingPosture (ReducedOperation, TemporarilyIdle, Closed) -> NpcBusinessLifecycleService (Struggling, Pivoting, Closing, Closed) -> CloseOrderly settles obligations and releases staff -> household distress -> boarding vacancy -> retail demand fall -> creditor pressure -> migration -> property market response.
- Expected: orderly closure with downstream consequences.
- Actual: the models exist and CloseOrderly is correct, but SetOperatingPosture and NpcBusinessLifecycleService.EvaluateWeek have no production callers. No layoff concept exists. None of the downstream chain runs.
- Classification: TEST-ONLY / SOURCE-ONLY.
- Adverse outcomes: mines cannot close; the entire downstream consequence chain is inert.
- Evidence: Worker C finding 6; Worker D case E.
- Disposition: REGISTERED as P2 (Document E).

## Chain 12: Equipment breakdown

- Trigger: a mill stone wears out.
- Steps: wear accrues live (GrainMillShopRuntime) -> EquipmentAsset.IsUsable fails at Condition01 <= 0.05 -> real work is blocked -> BlacksmithRuntime completes a quoted work order (terms agreed, journeyman skill check, real materials from lots, real payment through the ledger) -> equipment restored, or RentalFleet as the rental alternative.
- Expected: breakdown blocks work; repair is a real economic transaction.
- Actual: the full chain runs in production.
- Classification: CONFIRMED.
- Adverse outcomes: none found.
- Evidence: Worker B; Worker D case G.
- Disposition: ACCEPTED.

## Chain 13: Farmer at harvest

- Trigger: harvest season arrives.
- Steps: CropChain (PlantField, TendField, HarvestField, ThreshGrain) with a 21-day harvest window -> FarmLaborPlan -> extra labor hiring -> custom services -> weather constraints -> crop outcomes -> debt consequences.
- Expected: the seasonal cycle executes with real labor and real risk.
- Actual: the models are complete and correct, but HarvestField, TendField, and PlantField have no production callers. PlowWorkService is referenced only by its own file and tests. No runtime driver executes the seasonal cycle.
- Classification: SOURCE-ONLY.
- Adverse outcomes: none (clean absence). No phantom harvests are generated.
- Evidence: Worker D case H.
- Disposition: REGISTERED as P2 (Document E).

## Chain 14: Boarding-house operations

- Trigger: a worker needs a room.
- Steps: housing search (production adapter) -> BoardingHouseShopRuntime (rooms, boarder register, rent collection, kitchen, fuel, conduct) -> rent enforced -> save/load.
- Expected: genuine discovery, check-in, accommodation rights, rent, persistence.
- Actual: the runtime is rich and the adapter is live, but the end-to-end proof (discovery through check-in through rent through save/load) remains unproven. Preserved as RUN-001.
- Classification: PARTIAL.
- Adverse outcomes: none confirmed; the proof is missing, not the mechanism.
- Evidence: Worker D case I; RUN-001.
- Disposition: DEFERRED (RUN-001, Codex/Unity pass).

## Chain 15: Saloon evening

- Trigger: NPCs seek recreation after work.
- Steps: person travels to saloon -> buys drink/meal (cash, inventory) -> social contact -> information exchange.
- Expected: a real local economy and social hub.
- Actual: SaloonRuntime.ServeDrink and ServeMeal increment counters only (no person id, no cash, no inventory) with no production callers. No recreation concept exists in Population. No person travels to a saloon. No saloon-only currency exists, but only because no saloon economy exists at all.
- Classification: ABSENT.
- Adverse outcomes: none (clean absence).
- Evidence: Worker C finding 4; Worker D case J.
- Disposition: REGISTERED as P2 (Document E).

## Chain 16: Merchant extends credit

- Trigger: a household cannot pay cash for goods.
- Steps: household requests goods on credit -> merchant opens a store tab -> tab settled against future income.
- Expected: merchant-to-household goods-on-credit through a real obligation.
- Actual: the live path is private-lender cash loans (HouseholdHardshipCredit), not a store tab. B2B contractor credit exists at the lumber yard. Merchant-to-household goods credit specifically is absent.
- Classification: PARTIAL (credit exists, but not the merchant-tab form).
- Adverse outcomes: none; the existing credit is honest.
- Evidence: Worker D case K.
- Disposition: REGISTERED as P3 (Document E).

## Chain 17: Household moves after a fire

- Trigger: a building burns.
- Steps: fire event -> property damage -> shelter loss -> relocation chain -> insurance claim.
- Expected: fire destroys, households relocate, insurance pays.
- Actual: no building fire events, no property damage mechanics, no insurance classes. No shelter-loss or relocation chain exists.
- Classification: ABSENT.
- Adverse outcomes: none (clean absence).
- Evidence: Worker A finding 11; Worker D case L.
- Disposition: REGISTERED as P3 (Document E).

## Chain 18: Illness in a small enterprise

- Trigger: a key worker falls ill.
- Steps: illness occurs -> work capacity reduced -> wages affected -> business responds (temp hiring, delayed work, customer substitution).
- Expected: sickness has economic consequences.
- Actual: illness occurs at runtime (TickExistingConditionsAndRollNewCases, real severity and duration, mortality risk). But laborCapacityPercent and HealthWorkImpactBand have no consumers. Illness reduces no work capacity, affects no wages, and triggers no business response.
- Classification: PARTIAL (occurrence real, consequences absent).
- Adverse outcomes: the simulation records sickness that changes nothing; a reviewer reading the health system would expect economic effects that do not exist.
- Evidence: Worker D case M.
- Disposition: REGISTERED as P2 (Document E).

## Chain 19: One family asset supports several enterprises

- Trigger: two businesses need the same wagon, tool, or draft team on the same day.
- Steps: EquipmentReservationService / DraftPowerService.TryReserveAnimals -> contention detected -> decision-maker weighs competing orders -> reservation granted or refused with economic reasoning.
- Expected: shared assets create real tradeoffs resolved by an economic decision-maker.
- Actual: reservations are hub-owned and live with loud conflict refusals and day-scoped release. Contention resolves first-come-first-served by refusal. No decision-maker weighs competing business orders or consolidates economic tradeoffs. RentalFleet is the rental alternative.
- Classification: PARTIAL (contention real, arbitration absent).
- Adverse outcomes: economically inferior orderings (a low-value job blocking a high-value one) with no recourse.
- Evidence: Worker B; Worker D case N.
- Disposition: REGISTERED as P2 (Document E).

## Chain 20: Sim-born economic participation

- Trigger: a child is born in-simulation and grows to adulthood.
- Steps: RecordBirth -> birthdays advance age -> age band changes -> labor access upgrades -> the adult can be hired and act for the household.
- Expected: sim-born adults participate fully.
- Actual (after Pass VI fix): PersonState.RefreshDerivedAge now applies the canonical LaborAccessForAgeBand mapping, so sim-born persons gain HouseholdHelper, JuniorLowTrust, YoungWorker, and FullLaborMarket access as they age. Before the fix, they kept LaborAccessLevel.None for life.
- Classification: CONFIRMED (after fix).
- Adverse outcomes (pre-fix): a growing class of permanent non-participants; hiring and household agency silently exclude them.
- Evidence: Worker A finding 1; Pass VI fix verified by DemographicContinuityTests and full harness.
- Disposition: FIXED in Pass VI.

## Chain 21: Recruitment honesty

- Trigger: a business needs a worker.
- Steps: OpenRecruitment -> RecruitmentService (real persons only) -> CandidateInquiry with provenance -> HireFromInquiry -> EmploymentRelationship -> WorkScheduler.
- Expected: no faked candidates, no spawned workers, the dead not hireable.
- Actual: the service refuses to fake inquiries; migration guards against auto-spawning; Pass VI added deathDayIndex guards to FindCandidate and DirectApproach.
- Classification: CONFIRMED (after fix).
- Adverse outcomes (pre-fix): the recruitment pool included the dead.
- Evidence: Worker A finding 5; Worker D case I; Pass VI fix verified by RecruitmentTests and full harness.
- Disposition: FIXED in Pass VI.

## Chain 22: Railroad arrival or bypass

- Trigger: the railroad arrives (or bypasses) the settlement.
- Steps: arrival event -> freight costs change -> travel times change -> supplier markets shift -> drayage opportunities -> obsolete routes.
- Expected: a regional economic shock with winners and losers.
- Actual: RailConnection is a data model with no usages outside its own file. No events, no cost changes, no market shifts. Confirmed: no railroad revenue modifier exists anywhere.
- Classification: DESIGN DIRECTION ONLY.
- Adverse outcomes: none (clean absence, honestly reported).
- Evidence: Worker D case F.
- Disposition: REGISTERED as P3 (Document E).

## Chain 23: Manager decisions beyond recruitment

- Trigger: a delegated manager sees a problem (stock low, price wrong, equipment failing).
- Steps: ManagerBrain.DailyCycle decides -> SimulationDrivers executes -> business state changes.
- Expected: all seven decision types execute.
- Actual: only OpenRecruitment executes. Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset are decided and recorded but never executed.
- Classification: PARTIAL.
- Adverse outcomes: the business appears managed but six-sevenths of management is theater.
- Evidence: Worker D decision-driver audit, finding 4.
- Disposition: REGISTERED as P2 (Document E).

## Chain 24: Town-pulse demand revenue

- Trigger: households need goods from support industries.
- Steps: GeneralStoreRuntimeManager.ResolveTownPulseSupportIndustryDemand -> SharedBusinessRuntimeManager.TrySellTownPulseDemand -> TrySellTownPulseDemandFromBusiness -> BusinessRuntimeState.ResolveDailySalesPlaceholder -> revenue added to business cash.
- Expected: a real buyer pays real cash; goods transfer custody.
- Actual: revenueCents is computed as unitsSold times unit price and added directly to currentCashCents. No household ledger is debited. No buyer exists. The operation summary says "sold to town pulse," but the town pulse is a fictional aggregate buyer. The method name concedes it is a placeholder.
- Classification: BROKEN (manufactured revenue in production).
- Adverse outcomes: businesses earn money from no one; the project violates its own OffScreenSimulationRules rule 5; any economic analysis built on this revenue is unsound.
- Evidence: Worker D decision-driver audit, finding 1 (P1); Pass VI source verification.
- Disposition: REGISTERED as P1 (Document E). Not fixed in Pass VI: replacing the placeholder with real household demand is a gameplay-balance decision.

## Summary counts

- CONFIRMED: 8 (chains 1, 4, 7, 10, 12, 20, 21, and 14 partially)
- PARTIAL: 8 (chains 6, 8, 13, 16, 18, 19, 23, and 11 as test-only)
- BROKEN: 4 (chains 3, 9, 22 as design-only, 24)
- ABSENT: 5 (chains 2, 5, 15, 17, and 22)
- SOURCE-ONLY / TEST-ONLY: 3 (chains 11, 13)
- Fixed in Pass VI: 4 (chains 1, 10, 20, 21)
