# Pass VI Canon and Motivation Review

This document reviews the design intent behind the living-society systems (the Canon motivation sections relevant to households, work, information, and enterprise) against what the source code actually does. It states where the intent is realized, where it is partially realized, and where it is not realized. It is the foundation for the System Interplay Map (Document B) and the Causal Chain Audit (Document C).

Canon references below are to the consolidated Canon markdown (v0.3, provisional, pending verification against Canon XXXI). Where the consolidated text was ambiguous, the claim is marked as provisional.

## 1. The governing intent

The living-society design rests on a small number of load-bearing commitments:

1. **Households are the economic unit.** Persons belong to households; households hold reserves, earn income, bear obligations, and make decisions under pressure. (Canon XIII, household economy.)
2. **Work begins from real need and consumes actual availability.** No phantom labor, no manufactured customers, no conjured buyers. (OffScreenSimulationRules: never manufacture labor, goods, money, capacity, or customers.)
3. **Information must travel before actors react.** Actors act on what they know, with provenance. Observer-dependent information. (Canon information provisions.)
4. **Death keeps identities distinct.** Kinship, dependents, membership, and estate persist separately; the simulation never invents replacement persons. (Canon XIII 13.x, GHOST-DEF provisions.)
5. **Business and household cash are meaningfully separate.** Owner draws, wages, and obligations move money between distinct accounts through real transactions.
6. **Hiring happens through legitimate channels** with varying-confidence evidence. No auto-hiring, no spawned workers.
7. **Care duties follow actual circumstances**, not stereotypes. (Canon household provisions.)

## 2. Where the intent is realized in source

**Household as economic unit: REALIZED.** The section-26 household cycle (DailyNeedsService, HouseholdShortageMonitor, batched errand purchasing, HouseholdDecisionService, HouseholdHardshipCredit) runs end to end in production with real inventory lots, real cash, and honest refusals. Food becomes nutrition, nutrition becomes work capacity, work capacity feeds the time budget. Heat, fuel, health, treatment, rent, and arrears are all wired. This is the strongest connected system in the audit.

**No manufactured persons: REALIZED.** RecruitmentService refuses to fake inquiries; LaborMigrationLinker explicitly guards against auto-spawning; household splitting and migration move the same Person records. The identity continuity tests (Section25) confirm persons are never duplicated.

**Information provenance: REALIZED where it exists.** Contact-graph job discovery carries provenance; postal letters are real objects; reputation derives from real transactions; ObserverVisibility rules are sound. The problem is not dishonest information but missing information channels (see section 3).

**No phantom wages: REALIZED.** WageSettlementService is the sole live payroll path, settling agreed wages from real business cash. The MineLaborRegister.SettleShiftWages alternative has no production callers.

**Equipment wear and repair: REALIZED.** Wear accrues on mill stones, sawmill blades, draft implements, and freight wagons; unusable equipment blocks real work; blacksmith repair consumes real materials and real payment.

## 3. Where the intent is not realized

**No manufactured customers: VIOLATED.** SharedBusinessRuntimeManager.TrySellTownPulseDemand resolves "demand" through BusinessRuntimeState.ResolveDailySalesPlaceholder, which adds revenueCents directly to business cash with no buyer, no household payment, and no goods custody transfer. The method name itself concedes it is a placeholder. This directly contradicts OffScreenSimulationRules rule 5 ("Never manufacture customers: demand comes from real purchasing needs, never from a quota"). P1. See Documents D and E.

**Death ends the economic story: PARTIALLY REALIZED.** Death correctly ends membership, retains the person record, opens an estate with kinship-derived heirs, and cancels scheduled birthdays. But Pass VI found and fixed two breaks: death did not end employment (the deceased kept being paid weekly, a money sink), and estate administration (inventory, distribution, closure) has no runtime driver. The widow's household income never drops; the business keeps running as if nothing happened. P1. See Documents C and E.

**Sim-born persons participate economically: NOT REALIZED (now fixed).** Children born in-simulation kept LaborAccessLevel.None for life because RecordBirth did not set it and no age-refresh path upgraded it. Sim-born adults could not be hired or act for their households. Pass VI fixed this by sharing the canonical age-band mapping. The parallel defect (ghost households on split) is also fixed.

**Information travels before actors react: PARTIALLY REALIZED.** The information that exists carries provenance. But the flagship social chain (foreman observes, tells bartender, rumor spreads, NPCs act) cannot run: no NPC-to-NPC conversation exists, Socializing is never planned, the saloon is a test-only counter with no production callers, and there is no general belief store. Quick-diligence clues are invented rather than read from business state. P1. See Document C.

**Businesses react to real conditions: PARTIALLY REALIZED.** ManagerBrain makes real decisions and OpenRecruitment is executed, but the other five decision types (Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset) are recorded history only. Mine posture and NPC business lifecycle have no runtime drivers. Illness has no economic consequence. The farm harvest cycle never executes. P2. See Documents C and E.

**Water as a survival need: NOT REALIZED.** No supply, no utility, no shortage mechanic, no household or livestock effects. AnimalRegistry.Hydration01 exists but nothing writes it. P2 with a narrow implementation pathway. See Documents C and E.

## 4. The central tension

The audit finds a consistent pattern: **the codebase models broadly but connects selectively.** Dozens of systems (estates, wills, mine operations, crop cycles, saloon runtime, telegraph, fire, insurance, railroad) exist as source capabilities with correct internal logic, but a large fraction have no runtime driver, no production callers, or no downstream consumers. The connected core (household needs, payroll, purchasing, equipment, recruitment honesty, identity continuity) is genuinely strong. The periphery is a museum of well-built machines with no power.

This is not a criticism of the modeling. The models are the necessary precondition. But the living-society claim, that systems interact to produce emergent social and economic consequences, currently holds for the household-economy core and fails for most cross-system chains. Documents B and C make this precise.

## 5. Motivation review verdict

The design intent is sound and the connected core proves it can be realized. The priority is not new models but **connection**: runtime drivers for existing models, production callers for existing services, and consumers for existing state. Document E ranks the connection work by value. Document F preserves the items that remain deferred.
