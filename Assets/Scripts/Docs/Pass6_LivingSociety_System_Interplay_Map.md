# Pass VI System Interplay Map

This document maps the REAL production dependencies between living-society systems, with class and method references. Every edge is classified: PRODUCTION (live caller chain verified), SOURCE-ONLY (correct logic, no production callers), TEST-ONLY (exercised only in tests), PARTIAL (some edges live, some missing), or ABSENT (no connection exists). This map is the evidence base for the Causal Chain Audit (Document C).

## 1. Household economy core (Worker A stream)

### 1.1 Daily needs
- HouseholdDriver / SimulationDrivers (daily) -> DailyNeedsService.ExecuteDay -> consumes HouseholdInventory lots (real lots with provenance) -> PersonNutritionState.ApplyDay -> WorkCapacityMultiplier -> WorkTimeBudgetStore.
- Classification: PRODUCTION. The full food to nutrition to work-capacity chain is live.
- Shortfall -> HouseholdShortageMonitor -> purchasing need -> batched errand coordinator -> EmbodiedPurchaseExecutor (real cash, real suppliers, travel time).
- Classification: PRODUCTION.

### 1.2 Household decision and hardship
- HouseholdAutonomyDriver (daily, SimulationDrivers ~line 1035) -> builds pressure inputs -> HouseholdDecisionService.Evaluate -> selects from SeekIncome, Economize, Borrow, RelocateCheaper, NegotiateTerms, SellAssets.
- Borrow -> HouseholdHardshipCredit.ExecuteBorrow -> private lender (seeded wealthy NPC, StartingHouseholdEconomy line 244) -> AcceptOffer advances real purse cash against a real FinancialObligation.
- Classification: PRODUCTION. Refusals are honest (lender can decline).

### 1.3 Demographic lifecycle
- PopulationManager.DriveDemographicDay -> DemographicDriverContext -> DemographicDriverSchedule.EvaluateDay -> MortalityDriver, BirthdayService, FamilyFormationDrivers (Marry, SplitHousehold), MigrationDriver.
- Birth -> RecordBirth -> HouseholdLifecycleManager.RecordBirth -> PersonState created, membership registered, birthday scheduled.
- Death -> MortalityDriver.RecordDeath -> HouseholdLifecycleManager.RecordDeath (ends membership, retains record) -> EstateService.OpenEstate (kinship-derived heirs) -> BirthdayService.CancelForPerson.
- Pass VI fix: RecordDeath now also ends active employments via EmploymentRelationshipRegistry.EndEmploymentsForDeceased (reason Death).
- Classification: PRODUCTION. Estate distribution and closure have no runtime driver (SOURCE-ONLY).

### 1.4 Health
- PopulationManager.TickExistingConditionsAndRollNewCases -> real conditions with severity and duration -> mortality risk multiplier.
- Classification: PARTIAL. laborCapacityPercent and HealthWorkImpactBand have no consumers outside PopulationHealthState. Illness does not reduce work, wages, or trigger business response.

### 1.5 Water
- AnimalRegistry.Hydration01 exists. Nothing writes it. No household water state. No supply, utility, or shortage mechanic.
- Classification: ABSENT.

## 2. Work and shared resources (Worker B stream)

### 2.1 Employment lifecycle
- RecruitmentService (real persons only, refuses faked inquiries) -> CandidateInquiry -> HireFromInquiry -> EmploymentRelationship registered -> WorkScheduler -> WorkTask execution -> WageSettlementService.SettleWeeklyPayroll (sole live payroll) -> PayrollEvent -> household income.
- Classification: PRODUCTION. Four distinct records confirm the chain.
- Pass VI fix: recruitment now skips deceased persons (FindCandidate, DirectApproach).
- PersonJobDecisions.EvaluateOffer (candidate-side judgment): SOURCE-ONLY, no production callers. Live hiring is employer-side only.
- Wages are flat agreed amounts regardless of hours worked or capacity (design question, Document E).

### 2.2 Horse and draft power (flagship)
- HorseAuthority (static service): registration, CurrentUse, TryReserveUse.
- DraftPowerService.TryReserveAnimals / TryReserveDriver: unified-authority reservations creating real contention.
- Classification: PARTIAL with a genuine defect. One horse has one stable identity but TWO disjoint availability truths: HorseAuthority.CurrentUse and the unified-authority animal key do not cross-check. A plowing horse can be mounted and vice versa (double-booking). See Document D.
- Horse condition: IsDraftCapable checks species and IsActive only (ignores condition). HorseFoundation.ApplyCare and the feed-loop horse path have no production callers. Only ApplyWork is called in production, with zero cost. Classification: SOURCE-ONLY lifecycle.
- HorseTrade.ExecuteSale does not release live reservations. Horse rental has no production path. Journey travel (TravelMode.Horseback) uses a speed constant with no animal binding.

### 2.3 Equipment and wagons
- Wear accrues live (GrainMillShopRuntime, SawmillShopRuntime, DraftWorkUnit, FreightResourcePool) -> EquipmentAsset.IsUsable fails at Condition01 <= 0.05 -> blocks real work -> BlacksmithRuntime repair (quoted, skill-checked, real materials, real payment) -> RentalFleet alternative.
- Classification: PRODUCTION.
- EquipmentReservationService: hub-owned, live, with loud conflict refusals and day-scoped release. Contention resolves first-come-first-served by refusal; no decision-maker weighs competing orders.
- Wagon capacity is triple-recorded (legacy seam) but internally consistent.

## 3. Social and information economy (Worker C stream)

### 3.1 Information with provenance
- Contact-graph job discovery: PRODUCTION with provenance.
- Postal letters: PRODUCTION (real objects).
- Reputation from real transactions: PRODUCTION.
- ObserverVisibility rules: sound but TEST-ONLY / unwired.

### 3.2 The missing social chain (flagship)
- NPC-to-NPC conversation: ABSENT. No conversation system exists.
- Socializing activity: never planned (zero reservation call sites).
- SaloonRuntime.ServeDrink / ServeMeal: TEST-ONLY. Increment counters only (no person, cash, inventory); zero production callers. No recreation concept in Population. No person travels to a saloon.
- General belief store: ABSENT.
- Foreman role: ABSENT from MineRole.
- Telegraph: ABSENT.
- Mine owners reacting to decline: SOURCE-ONLY (SetOperatingPosture has no production callers).
- Quick-diligence clues: invented (seeded switch), never reads business state. INCORRECT AUTHORITY.
- Acquisition inspection: leaks hidden mine richness without diligence gating. INFORMATION LEAK.

### 3.3 Mine economics
- Depletion economics: PRODUCTION (real).
- Payroll to release to household income: PRODUCTION (real).
- Closure and labor response: SOURCE-ONLY / ABSENT (no layoff concept, no runtime driver).

## 4. Business and regional consequences (Worker D stream)

### 4.1 Enterprise decisions
- NpcEntrepreneurshipDriver.EvaluateWeek (weekly): observes real unfulfilled needs, forms businesses with founder household money. PRODUCTION. No population-count trigger.
- ManagerBrain.DailyCycle (daily): makes Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset, OpenRecruitment decisions. PARTIAL: only OpenRecruitment is executed (ExecuteManagerRecruitment). The rest are recorded history.
- NpcBusinessLifecycleService (Struggling, Pivoting, Closing, Closed with orderly CloseOrderly): SOURCE-ONLY. EvaluateWeek has no production callers.

### 4.2 Revenue integrity
- Real sales: GenericRetailSaleAuthority with real buyers, real cash, real custody transfer. PRODUCTION.
- Town-pulse demand: SharedBusinessRuntimeManager.TrySellTownPulseDemand -> BusinessRuntimeState.ResolveDailySalesPlaceholder adds revenue with NO BUYER. PRODUCTION but INCORRECT: manufactures revenue, contradicting OffScreenSimulationRules rule 5. P1. See Document E.

### 4.3 Banking and credit
- Household hardship credit (private lenders): PRODUCTION, honest.
- B2B contractor credit (lumber yard): PRODUCTION.
- Merchant-to-household store tab: ABSENT.
- Bank deposit refresh before underwriting: unproven (RUN-003).

### 4.4 Property and construction
- NpcCommissionedBuild: SOURCE-ONLY (no production callers). Boarding shortage never triggers construction.
- Building fire, property damage, insurance: ABSENT.

### 4.5 Agriculture
- CropChain (PlantField, TendField, HarvestField, ThreshGrain), FarmLaborPlan, FarmSeasons, AgriculturalRisk: SOURCE-ONLY. HarvestField and kin have no production callers. PlowWorkService unreferenced. No runtime driver executes the seasonal cycle.

### 4.6 Railroad
- RailConnection: data model with no usages outside its own file. DESIGN DIRECTION ONLY. Confirmed: no railroad revenue modifier exists.

## 5. Cross-cutting authorities (section 18 reconciliation)

| Possible duplicate | Verdict | Canonical writer | Notes |
| MineLaborRegister.SettleShiftWages vs WageSettlementService | Not competing | WageSettlementService | MineLaborRegister has no production callers; dead code / test-only |
| PlayerDebtManager vs FinancialObligationAuthority | Valid distinct account | FinancialObligationAuthority (money); PlayerDebtManager (loan workflow state) | All mutations flow through the authority via obligationId |
| PersonScheduleTracker vs PersonActivityReadModel vs others | Not competing | PersonScheduleTracker | ReadModel is a read model by design |
| BirthdayService vs BirthdayScheduling | Complementary | BirthdayService (service); BirthdayScheduling (static id/payload helper) | Not competing; stable event ids |
| ProductionPathRegistry vs other production paths | Single path | ProductionPathRegistry | Wired into GenericBusinessEconomy; Pass V one-path routing active |
| WorkTask.locationId | Incomplete connection | Journey authority (FindRoute) consumes location ids from other entities | Placeholder documented; not a duplicate |
| Business cash vs owner cash vs household cash | Distinct accounts | BusinessRuntimeState (business); household ledgers; owner purse | Meaningfully separate; movements through real transactions |

## 6. Map summary

The production-connected core: household needs, hardship credit, purchasing, payroll, recruitment honesty, identity continuity, equipment wear and repair, depletion economics, entrepreneurship, demographic lifecycle (birth, death, marriage, split, migration).

The source-only periphery: estates administration, wills, mine operations and closure, business lifecycle, farm cycle, horse lifecycle, saloon runtime, manager decisions (except recruitment), illness economics.

The absent: NPC conversation, belief store, telegraph, water, fire, insurance, railroad events, merchant store-tab credit, employer lodging, layoffs.

The incorrect: town-pulse manufactured revenue, invented diligence clues, inspection information leaks, horse double-booking.
