# Pass IX — Comprehensive Canon Gap Audit

Baseline: commit 147503f11dda7947c6e04689dcb795e678586de3 (Pass VIII final, recovered and verified 2026-10-22).
Method: four independent read-only audit sweeps over the whole codebase at that commit, each finding grounded in file, class, and call-site evidence. Full evidence tables live in the Pass IX coordination reports (audit-1 through audit-4); this document is the consolidated classification register.
Governing Canon: Canonical Project Bible Revision XXXI and Technical Implementation Bible Revision XIV (documentation package v2.1, 2026-10-08). The document router lists no newer authorized revision. Proposals 001–105 approved; 106–145 not approved. The v0.3 consolidated bibles are provisional and were not treated as governing.

## Classification key

- A — Critical correctness defect: existing systems violate physical, economic, legal, or simulation authority.
- B — Required current-game functionality: Canon requires it for the currently implemented gameplay loop, but production behavior is absent or incomplete.
- C — Implemented but unwired: authoritative logic exists but is not connected to ordinary gameplay.
- D — Future Canon functionality: intended for the mature game, not needed for current loops. Backlog only.
- E — Unapproved or unresolved design: Canon does not establish the rule. Record the exact decision required; do not invent.

Completion levels used below: Implemented / Connected / Source exercised / Unity verified. Nothing in this pass is Unity verified.

## Verified sound (no gap)

- Scenario objectives observe, never manufacture: FirstLedger and SaloonCircuit evaluators read real state only (ownership counts, employments, BIZ-5 equity; Saloon requires full ownership plus a qualifying sale plus deadline). No prerequisite injection found anywhere (A1-03 substance, doctrine compliant).
- Town-pulse manufactured revenue is genuinely dead: both entry points return 0 and mutate nothing.
- HouseholdLedger remains the single cash writer; FinancialObligationAuthority remains the single debt authority.
- Pass VIII BusinessLenderService is production-connected with real cash-first disbursement, exactly one obligation per loan, and shared underwriting.
- Pass VII/VIII shipment integrity fixes are present in source: persisted deliveredQuantityUnits, acceptance-gated completion, conservation checks, cargo-safe save/load.
- Birth-date aging with no independent age authority and no skipped birthdays under acceleration; household membership authority; death ends employment and opens estates; recruitment dead-guards; person-initiated job pursuit; provider-loss attribution; disability reduces usable minutes through DailyNeedsService.WorkCapacityFactor; strict absence sweep.
- Pass VII estate administration, Pass VIII housing, construction, and provider-loss systems are implemented as designed; their gaps are missing production drivers (Class C), recorded below.

## Domain 1 — World, property, law, scenarios, infrastructure (A1)

| ID | Class | Severity | Finding |
|---|---|---|---|
| A1-01 | C | High | Scenario preset system (ScenarioCoordinator/ScenarioWorldBuilder, New Game/restart/ContinueFailed, population 55–95, fingerprints) fully built but instantiated only in tests. Production ports exist in ScenarioUnityWiring.cs; nobody constructs the coordinator. Live New Game uses a separate TownWorld/ScenarioAsset path. |
| A1-02 | B | High | Preset start date never reaches TimeManager. Three independent date defaults coexist: ScenarioDateUtil epoch 1870, CalendarAnchor 1880-03-01, SimulationTimeSettings startingYear 1892. |
| A1-03 | B | Medium | Neither coded scenario has a failure state: a missed Saloon Circuit deadline fails silently. Scenario catalog beyond two coded scenarios is absent (rest is Class D). HUD identity/deadlines remain LL-PLAY-005. |
| A1-04 | C | Medium | Settlement growth/expansion plots are Canon-shaped and connected (TownWorldController expansion scoring, growth road stamping). No gap beyond scenario coverage. |
| A1-05 | C | High | Estate lifecycle has no daily production caller (PASS7-001 confirmed at this commit). Only the scripted AutonomousSocietyDriver demo drives EstateLifecycleDriver. |
| A1-06 | C | Medium | PropertyDisputes.cs has zero production references. Dispute claims are never recorded at acquisition/registration nor consulted in diligence. |
| A1-07 | B+C | High | Player property tax: weekly levy/accrual is connected, but payment via AcquisitionMarketManager.TrySpend credits no recipient — cash is destroyed. NPC tax path pays a named County Tax Collector person, but NpcPropertyLifeService has no production driver. Player lien filing and tax sale are implemented but deliberately undriven. |
| A1-08 | C | Medium | Construction-risk/lien decision machinery inert without the A1-07 production CreditRegistry driver. |
| A1-09 | C+E | Medium | Legal regime and lawyer services implemented, not connected; no court exists. Canon itself holds court procedure as research — E for procedure, do not invent. |
| A1-10 | B | High | Schoolhouse teacher assignment is real and connected, but RecordVisibleWorkerRole only writes a visible profile: no cash, no fund, no employment record. The teacher is paid in fiction. |
| A1-11 | B | High | No civic treasury exists at all. There is no civic authority holding public money to receive taxes or fund school/public works (see also A1-07). |
| A1-12 | D | Low | Telegraph absent. Future Canon. |
| A1-13 | D | Low | Urban fire (ignition, spread, firefighting, loss) absent; no building fire exists. Prairie fire exists and is driven. Urban fire is also water-dependent. |
| A1-14 | E | — | Water and sanitation: proposal 136 is in the not-approved batch; Canon sets no requirement rates, deprivation consequences, or access rules. Decision required before any implementation. |
| A1-15 | D/E | Low | Homestead/public-land entry as a path to title distinct from purchase: public-land interest is distinct from title in Canon; scope decision required. |
| A1-16 | E | Low | Severable mineral rights: decision required whether parcel-bound mining is the settled rule or severable rights are in scope. |
| A1-17 | — | — | Research revalidation summary for this domain is in the section below. |

## Domain 2 — People and society (A2)

| ID | Class | Severity | Finding |
|---|---|---|---|
| A2-01 | A | Critical | Post-generation marriages are sterile. BirthDriver.EvaluateConceptions gates conception on the legacy PersonState.householdId field; the sole membership authority (HouseholdMembershipRegistry) never writes that field and MarriageDriver.Marry never updates it. Couples from different generated households can never conceive; sim-born children carry householdId 0. Existing tests mask it because both spouses default to 0. |
| A2-02 | B | Major | Remarriage impossible (PASS6-003 confirmed). Kinship spouse edges are never ended; IsMarriageable requires zero spouse edges, so widows and widowers are permanently blocked. Historical edges must be preserved; a current-spouse derivation is the correct fix. |
| A2-03 | C | Major | DailyActivityPlanner unwired: sleep, meals, job-seeking, and managing are never reserved in the live game (test callers only). Canon double-booking protection does not cover baseline daily life. |
| A2-04 | C | Critical | Domestic labor never executes in production. SimulationDrivers calls DomesticLaborDriver.DriveDay daily, which only appends to a pending list; the starter (AdvanceTick) has zero production callers and is never called from OnShortTick. The pending list grows monotonically and is persisted. |
| A2-05 | C | Critical | The Pass VIII attendance-gated payroll seam (WeeklyAttendanceProvider) is installed only in the test-only AutonomousSocietyDriver. The production path (SharedBusinessRuntimeManager.ResolveWeeklyPayroll) runs with a null provider: full wage for a mere employment record. |
| A2-06 | C | Major | Conversation/knowledge has no production producer: ConversationRunner, ConversationOpportunityService, SocialVisitPlanner have zero production callers. The hub knowledge store stays empty in a real game although the supplier-procure consumer is wired. |
| A2-07 | C | Major | Of the three Pass VIII knowledge chains, the supplier chain is connected; the employment chain (WorkerJobSecurityEvaluator → job search) is unwired. |
| A2-08 | E | — | Lender social knowledge (corroborated borrower trouble pricing credit) is intentionally unwired and decision-blocked: BusinessLenderService.BindSocialKnowledge awaits a real bank officer person. Do not invent a person. |
| A2-09 | C | Critical | EstateLifecycleDriver has no daily caller (same root as A1-05 / PASS7-001). Estates open at death but never advance in production. |
| A2-10 | A | Critical | Ghost wages after migration: MigrationDriver.MigrateOut never ends employment (no migration EmploymentEndReason exists) and no payroll path checks isOffMap. Combined with A2-05, departed migrants are paid in full indefinitely. |
| A2-11 | B | Moderate | Adult-child household split reads stale workplaceBuildingId instead of the employment registry — the same stale-legacy-field pattern as A2-01. |
| A2-12 | B/E | Moderate | Childcare, caregiving, and recovery time are absent (PASS8-004 re-verified). Scheduling them needs appended PersonActivityKind values and design care; sequenced after A2-04. |
| A2-13 | C | Minor | Lenient absence predicate exists with tests but the production sweep passes null. Only the SimulationDrivers call site changes. |
| A2-14 | B | Minor | Household inspection rejects id 0 while the allocator starts at 0 (guard is <= 0, should be < 0). No per-person inspection exists (Unity-side data gap is source-only here). |
| A2-15 | B/E | Moderate | Person fatigue is only a carried field, never accumulated; Canon fatigue causality (overwork → errors/injury/turnover) cannot occur. Needs a small design and A2-03 rest reservations to recover against. |

## Domain 3 — Economy core and finance (A3)

| ID | Class | Severity | Finding |
|---|---|---|---|
| A3-01 | A | Critical | Service-revenue multiplier mints cash. Households are debited visits × price in ResolveHouseholdPaidServiceVisits, but businesses are credited visits × price × multiplier (Barber 1.2, Saloon 1.15, Builder 1.1, Freight 1.05) in the live weekly path (SharedBusinessRuntimeManager.cs:4270). 5–20% of service revenue is created from nothing, every week. |
| A3-02 | A | Critical | Freight charge legs are asymmetric. LogisticsRuntimeManager.BookFreightCharge credits the carrier in full first; the payer leg spends only min(cash, cost). A broke or missing payer still results in a fully paid carrier, or a payer charged with no recipient. |
| A3-03 | A | High | Postage has no payer. PostalService.PostItem debits nobody (sender is a string) yet DrivePostalRevenue posts the accrued pot to the post office till daily. |
| A3-04 | A | High | FreightCompanyRuntime.CollectFreightCharge checks only that the payer exists, not that the named freight payer was actually debited (Canon §3.6; PASS8-002 related). |
| A3-05 | A | High | New-business starting cash: at least one GeneralStore founding path grants cash without a named source (owner cash, founder savings, or charter). The documented First Ledger starting stake endowment is the one legitimate exception and must stay distinguishable. |
| A3-06 | A | Critical | Valuation feeds corrupted two ways. (a) Weekly operating profit fed to EnterpriseValuationReadModel is actually WeekToDateNetCents, a cash-net including loan proceeds (recordNetDelta: true on Borrow/lender paths); the Pass VIII financing fix reached the report builder but not valuation. (b) BusinessLenderService loans live only in FinancialObligationAuthority, never in BusinessLiabilityLedger, so they never reduce owner equity — Canon §11.3's exact example. |
| A3-07 | C | High | Five of seven manager decision types can never originate in production. All executors are wired and ExecuteDecision is called live, but the sole production caller (SimulationDrivers.cs:1528) never sets ProposedBorrowing/AssetSale/Procurement, ShortageObserved, MaintenanceNeeded, or PricingAdjustmentRequested, and hard-codes MaxBorrowingCents = 0. Only OpenRecruitment and Schedule fire. ManagerBrain.WeeklyCycle has no production caller. |
| A3-08 | B/E | High | PASS8-003 verified precisely: the AppraiseSaleCents seam is null in production (SimulationDrivers.cs:1646), so NPC asset sales are always denied; and an approval would be terminal anyway because no execution step exists. SellAsset requests name an amount but no asset, so nothing can be appraised until asset identity is plumbed. A real Canon-shaped enterprise valuation does exist and is connected; acquisitions have a separate asking/estimated/sale-price system; collateral uses valuation evidence; liquidation value does not exist at all (production passes Liquidation = null to close-down). |
| A3-09 | C | Moderate | CollectRealDemand/RealDemandSource has zero production callers and is NOT registered in Docs/UnwiredSystems.md — a registry omission. It must be wired honestly or registered. |
| A3-10 | A | High | Payroll fallback: when HouseholdLedgers/PersonHouseholdIdLookup are unwired or a worker has no household, the wage path can fail to conserve cash (business out != household in). Wages must conserve in all paths. |
| A3-11 | B | Moderate | Owner reporting must distinguish owner-personal cash, each business's cash, protected cash, principal debt and material liabilities, deposits, and consolidated equity (Canon §1.3: equity is never presented as spendable cash). Current reporting conflates some of these. |
| A3-12 | A/B | High | Three payable representations coexist: FinancialObligationAuthority Payables, BusinessLiabilityLedger entries, and a scalar accruedLiabilityCents for missed payroll/transfer shortfalls that lives outside both. The scalar accrual must be routed into the debt authority as named Payables. |
| A3-13 | B | Moderate | A Canon-listed valuation/pricing factor (market capture) is not fed from the existing market-capture computation during weekly settlement. |
| A3-14 | — | — | UnwiredSystems registry verified accurate at this commit for the 12 detailed per-business runtimes and staged-production machinery, with the A3-09 and A4-06 omissions noted. |
| A3-15 | — | — | Carried Pass VIII items (mine detailed chain, freight tick drive, foreclosure auction, household bank lending) remain exactly as registered; the first two are Pass IX priorities PASS8-001/PASS8-002. |

## Domain 4 — Physical goods, industries, technical foundations, performance (A4)

| ID | Class | Severity | Finding |
|---|---|---|---|
| A4-01 | B/E | High | IDENT-001: bulk-goods lot identity is siloed. Mine ore lots (global EntityId, full provenance, FIFO dispense) and household lots (sourceless additions rejected, HeldUnits reservation) are strong; but the live shared economy's inventory authority, CategoryStockState, is bare integer counts — no lot, origin, custody, or reservation. The consolidation strategy is the decision recorded in the implementation plan. |
| A4-02 | C | Moderate | Four-plus inventory authorities coexist (category stock, household lots, mine ore stock, per-runtime lot ledgers) with consolidation only at ProductionPathRegistry. No second authority may be created; consolidation must go through existing seams. |
| A4-03 | B | Moderate | No split/merge-with-ancestry API exists in MineOreStock or HouseholdInventory. Partial dispense decrements in place; dispense lines record (lotId, tons, destination) — provenance flows forward only at dispense. |
| A4-04 | B | Moderate | Ownership vs custody vs location: mine lots have custody implicit in the holding stock and location as a category string; household lots have no custody/location fields; freight states do distinguish them. |
| A4-05 | B | Moderate | Reservation of specific goods: household HeldUnits exists and is enforced; category stock has no reservation (pending-reorder is a target, not a hold); mine ore stock has no reservation. |
| A4-06 | C (+latent A) | High | PASS8-001: MineProductionCycle.RecordBreakOreShift (worked minutes → assayed ore lots with shaft/level/vein provenance, depletion, hoist wear) has callers only in tests. No weekly/shift driver enumerates levels or reads completed break-ore tasks. MineProductionCycle is also missing from the machine-checked UnwiredSystems registry. |
| A4-07 | A (latent) | High | Both mine paths deplete the same RemainingRichness01 dial: the abstract ResolveMineOperation and the detailed cycle. Wiring a driver without reconciling the abstract path double-produces and double-depletes. |
| A4-08 | B | High | Live mine path uses crew counts + efficiency multipliers — no worked minutes, no hoist requirement, no timber consumption (Canon 21.7L: production from worked person-time + equipment). Implemented only as an abstract substitute. |
| A4-09 | E | — | Deposit finiteness: richness is a rate scalar, not a finite reserve. Whether Canon requires a true finite reserve quantity is a decision; the single-dial depletion model is the current settled implementation. |
| A4-10 | B | High | Ore reaches buyers by instant off-map sale inside the production call (ResolveMineOperation credits soldRevenueCents the same tick). The detailed shipment/settlement path is unwired. |
| A4-11 | C | High | PASS8-002: FreightCompanyRuntime has zero production instances; SimulationDrivers.cs:1083 documents the gap itself. Ordinary freight actually moves through LogisticsRuntimeManager (ShortTick-driven, category-stock cargo). |
| A4-12 | C | Moderate | Two parallel freight systems share LogisticsShipmentState: the manager (category units, state-machine load/delivery) and FreightShipmentService (TaskAuthority handling, custody, acceptance). One system of record must be established for detailed shipments. |
| A4-13 | A (latent) | High | FreightShipmentService never checks wagon/driver/animal exclusivity across live shipments. Must be fixed before or with any production wiring of the service. |
| A4-14 | A (suspect) | High | RecordSawmillConstructionLumberSale's linked-node branch sets mirroredConsumed = requested unconditionally and credits full revenue — suspect live cash creation. Needs a repro before class-A confirmation; test first, fix second. |
| A4-15 | B | High | PASS8-009: PersonScheduleTracker keeps append-only reservations and every query scans full campaign history; KnowledgeTransmissionStore does two full scans per insert (O(n²) cumulative); ManagerDecisionExecution.AdvanceShipments scans all shipments ever, daily. |
| A4-16 | B | Low | Quadratic save restore: claim only partially revalidated. No single smoking-gun loop isolated; risk is distributed linear reference resolution across subsystem restores. Needs a profiler pass on a large save before any fix. |
| A4-17 | B | Moderate | DueEventScheduler.PopDue drains with RemoveAt(0) — O(k·n) per cohort; Schedule/Cancel are O(n) list operations. Correct but slow at cohort scale. |
| A4-18 | C | High | Farm cycle undriven (PASS7-004 carried): CropChain.PlantField/TendField/HarvestField have no production callers outside FarmSeedStore helpers and tests; live farm output flows through the abstract weekly resolver. Same pattern as the mine. |
| A4-19 | A/B | High | Archetype weekly resolvers credit output units in the weekly call from staffing counts and efficiency — outputs credited at resolution time rather than at task completion, and work does not consume person time on these paths. The mine is the worst case (A4-08); the pattern is general. |
| A4-20 | B | Moderate | Sales require buyers: town-pulse buyerless revenue is retired and verified; remaining instant-sale paths (notably mine ore, A4-10) sell without a buyer-side counterparty record. |

## Research revalidation (Phase 5 topics, checked against current source, not the older 6d41860)

Still true: per-cell road GameObjects (BuildRoadVisuals creates one visual per cell; the rect-merging helpers FindUnconsumedRoadRect/MarkConsumed in the same file are dead code); scenario maps absent; schoolhouse teacher funding absent (A1-10); no civic financial authority (A1-11); telegraph absent (D); urban fire absent (D); notifications have no source authority for person events (diagnostics only).
Fixed since the research: authored Unity Terrain; building placement on slopes; NPC entrance anchors; seasonal road conditions (mud/snow/blizzard, daily-driven); prairie fire; postal revenue with determinism (Pass VIII stable hashing).
Partially changed: freight delivery is receipt-honest after Pass VII/VIII but FreightCompanyRuntime still has zero production instances and wagons do not physically move (freight is a state machine plus visual controller); person inspection exists for households with the A2-14 id-0 defect, per-person inspection absent; scenario HUD data (objectives, deadlines, headline) exists source-side, HUD itself is Unity-side; time controls support 1x–100x source-side with capped short-tick counters; UI refresh costs are event-driven summary/layout rebuilds, no per-frame rebuild found.
The consolidated research PDF itself is not present in this environment and was not read; verdicts above come from direct source inspection.

## Class E decisions required from the owner (recorded, not invented)

1. Water and sanitation rules (A1-14): requirement rates, deprivation consequences, access rules. Proposal 136 is not approved.
2. Court procedure (A1-09): Canon holds procedure as research.
3. Bank officer identity for lender social knowledge (A2-08): which real person staffs the bank for knowledge intake.
4. Finite mineral reserve vs rate-scalar depletion (A4-09).
5. Severable mineral rights scope (A1-16) and homestead/public-land entry scope (A1-15).
6. Childcare/caregiving scheduling design (A2-12) and fatigue design (A2-15): activity kinds and recovery rules need approval before implementation.
