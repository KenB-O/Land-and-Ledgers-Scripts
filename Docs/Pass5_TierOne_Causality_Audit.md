# Pass V — Tier One Causality Audit

Date: 2026-10-12 · Baseline: `bd76bc98` (integration/pass4-universal-tasks-equipment-activities)
Auditor: Muse Pass V coordinator. Method: source inspection of actual classes,
production callers, runtime registration, and persistence at the baseline commit.
The repository is the truth; prior pass reports were used as leads and re-verified,
never as proof.

Classification key: IMPLEMENTED (in production, connected) · PARTIAL (exists but
incomplete/disconnected) · SOURCE-ONLY (code exists, no live caller) · DUPLICATE
(two authorities for one truth) · INCORRECT (wrong behavior) · MISSING · SCENE
(scene-owned dependency) · UNKNOWN.

Severity: P0 authority/identity violation · P1 broken causal connection ·
P2 cohesion deficiency · P3 calibration/presentation.

---

## DEV-A — Identity, ownership, relationships

| Concept | Authority | Location | Class | Notes |
|---|---|---|---|---|
| Person identity | PersonState (int id, never rewritten) | Domains/Population/PersonState.cs | IMPLEMENTED | Canonical record. Pass III/IV extended. |
| Household identity/membership | HouseholdState + memberships | Domains/Population/ | IMPLEMENTED | Form/Add/Remove/Transfer/Dissolve primitives exist. |
| Kinship | KinshipRegistry (Parent/Child/Spouse/Sibling edges) | Domains/Population/HouseholdLifecycle.cs | SOURCE-ONLY | Edges API exists. PopulationGenerator creates ZERO kinship edges (verified by grep). Generated households have no family relationships. P1. |
| Employment | EmploymentRelationship | Domains/Economy/Core/ | IMPLEMENTED | Sole compensation record, no ownership fields. |
| Land title | TitleAuthority chain | Domains/World/Property/ | IMPLEMENTED | Pass III. |
| Buildings/spaces | PlacedBuilding, functional spaces | Domains/World/Buildings/ | IMPLEMENTED | |
| Animals | AnimalRegistry | Domains/Animals/ | IMPLEMENTED | Fatigue meter exists. |
| Equipment | EquipmentAsset, registers, reservation service | Domains/Economy/Equipment/ | IMPLEMENTED | Pass IV capacity models. |
| Person location | Fragmented | multiple | DUPLICATE | TownGrid/GridCoord (2m), ParcelService, PersonState building refs, JourneyModel, PopulationPathingDirector, AgentMover. WorkTask.locationId is an unwired string. P2. Carried from Pass IV audit. |
| What-is-person-doing | PersonScheduleTracker vs PersonState.scheduleState vs PlayerDirector | multiple | DUPLICATE | P2. Carried from Pass IV audit. |

## DEV-B — Financial and physical truth

| Concept | Authority | Location | Class | Notes |
|---|---|---|---|---|
| Cash | HouseholdLedger (one writer) | Domains/Population/ | IMPLEMENTED | Rejects sourceless inflows. |
| Debt | FinancialObligationAuthority | Domains/Economy/Financing/ | IMPLEMENTED | Moves no cash by design. |
| Rent/board | TenancyPaymentService (period-keyed, arrears to Payable) | Domains/World/Property/ | IMPLEMENTED | |
| Wages | WageSettlementService (Generic, idempotent weekly) | Domains/Economy/Labor/ | IMPLEMENTED | Generic only. |
| Legacy wage path | MineLaborRegister (per-shift) | Domains/Economy/Businesses/Mine/ | DUPLICATE | Test fixed in c6a676a. Second wage path still exists in source. P2. |
| Business inventory | GenericInventoryPosition/Lot (FIFO, reserved/available) | Domains/Economy/Generic/ | IMPLEMENTED | |
| Household inventory | HouseholdInventory lots with holds | Domains/Population/ | IMPLEMENTED | |
| Freight | LogisticsRuntimeManager; CreateBlockedShipment FIXED | Domains/Economy/Logistics/ | IMPLEMENTED | |
| Business stock truth | BusinessRuntimeState category stock vs GenericConfiguration positions | Domains/Economy/ | DUPLICATE | P2. Carried from Pass IV audit. |
| Player loans | PlayerDebtManager (own LoanContract book, bridged via obligationId) | Domains/Economy/Financing/ | DUPLICATE | Mirrored repayments. P2. |

## DEV-C — Generic Businesses and real production

| Concept | Authority | Class | Notes |
|---|---|---|---|
| Live business runtime | SharedBusinessRuntimeManager (8.3k-line monolith) | IMPLEMENTED | Bootstrapper adds it as a scene component; SimulationDrivers wires employment registry, weekly profit callbacks, NPC business creation binding. THIS is the live path. |
| New production path | GenericProductionRuntime / Authority / PolicyExecutor / OutputAuthority | PARTIAL | Pass IV built and tested it; production callers exist for bakery/retail/farm/forge/freight slices. Coexists with the monolith. |
| Production authority split | SharedBusinessRuntimeManager vs GenericProductionRuntime | DUPLICATE | P1. Both can touch the same businesses (duplicate inventory truths above are a symptom). The migration from monolith to Generic path is incomplete and unmapped. Worker 3 must map and consolidate. |
| NPC business formation | NpcBusinessCreationAdapter.TryCreateBusiness | PARTIAL | Bound in SimulationDrivers (line 257). No autonomous opportunity-evaluation driver found calling it on market conditions. P2. |
| Business solvency/failure | inside SharedBusinessRuntimeManager | IMPLEMENTED | Legacy path. |

## DEV-D — Persons, labor, time

| Concept | Authority | Class | Notes |
|---|---|---|---|
| Task authority | TaskAuthority (single mutator) | IMPLEMENTED | Pass IV lifecycle. |
| Person-time | PersonScheduleTracker + WorkTimeBudgetStore | IMPLEMENTED | Overlap refused loudly. |
| Due events | DueEventScheduler (hub-owned, persisted) | IMPLEMENTED | Pass IV. Ready for birthday scheduling. |
| Clock | TimeManager (absoluteDayIndex authority) | IMPLEMENTED | Short-tick overflow deferral and per-day collapse added in Pass IV. |
| Calendar | 336-day custom (7d x 4w x 12m) | INCORRECT | P0. No leap years, no real month lengths, weekdays not aligned to history. Brief section 9 requires Gregorian. See DEV-E. |
| Domestic labor | DomesticLaborService | PARTIAL | Was tests-only at Pass IV audit; Codex integration touched ShoppingTripDriver/HouseholdShoppingLoop. Live-driver status must be re-verified. |
| Skill/capability | SkillService + stores + ExperienceFormationService | IMPLEMENTED | Pass III. |

## DEV-E — Scenario initialization and demographic continuity

| Concept | Authority | Class | Notes |
|---|---|---|---|
| ScenarioDefinition | ScenarioAsset (ScriptableObject) | PARTIAL | Has goals, objectives, population inclusion rules, tunables, player household declaration. Has NO starting-world configuration: no population range, no businesses, no property, no economy, no player assets. P1. |
| Scenario runtime | ScenarioDirector / ScenarioService | IMPLEMENTED | Goal/objective/tunable runtime ("puppet master" dev tool). NOT a world-generation coordinator. |
| Scenario coordinator (one orchestrator for identity/config/generation/lifecycle) | — | MISSING | P1. Brief section 5. |
| Scenario presets (versioned, serializable, reusable) | — | MISSING | P1. Brief section 6. |
| New Game generation | PopulationManager -> PopulationGenerator.Generate | PARTIAL | Generates from town shell + PopulationGenerationSettings. Seed = townWorld.Settings.seed + seedOffset. |
| Generation order | household-count-first, fills home slots | INCORRECT | P1. Canon 12.6G requires persons-first: population envelope, demographics, kinship, family histories, households FROM relationships, accommodation, economic roles, validation. Current code picks household count, then member counts, with no kinship edges at all. |
| Population calibration | PopulationGenerationSettings (55-95, target 78) | IMPLEMENTED | Matches Canon preservation note (55-95, preferably 70-85). DO NOT CHANGE. |
| Controlled randomness / seed reproducibility | town seed + offset | PARTIAL | Deterministic per seed, but no partitioned RNG streams; no scenario-level seed; no reproducibility contract. P1. |
| Birth-date authority | — | MISSING | P0. No DateOfBirth/BirthDate field anywhere in production person code. |
| Runtime aging | HouseholdLifecycleManager.AgePerson | SOURCE-ONLY | ZERO production callers (verified). Dead code. Population age is frozen at generation. P0. |
| Birthday scheduling | — | MISSING | P0. No events, no hooks. |
| RecordBirth | HouseholdLifecycleManager | SOURCE-ONLY | One caller: HouseholdVerticalSlice (test orchestrator). No runtime birth driver. P1. |
| RecordDeath | HouseholdLifecycleManager | SOURCE-ONLY | ZERO production callers. Nothing dies at runtime. P1. |
| EstateService | Domains/Economy/Estates/ | SOURCE-ONLY | Full estate distribution logic; no runtime deaths to trigger it. |
| Marriage formation | KinshipRegistry.AddSpouses | SOURCE-ONLY | No runtime driver. P1. |
| Migration | LaborMigrationLinker | PARTIAL | Labor-linked only. General migration for household formation/distress/opportunity: MISSING. P1. |
| New Game vs Load vs Restart semantics | SaveLoadManager clear-then-load | PARTIAL | Load is clear-then-load (good). New Game contamination (stale persons/businesses/objectives/reservations across runs) NOT verified. Restart/Replay/Continue-Failed are undefined. P1. |
| Starting wealth/assets/debts/employment | — | MISSING | Generated persons have no coherent starting economy beyond household reserves/cash sweep. P1. |

## DEV-F — Simulation and presentation separation

| Concept | Authority | Class | Notes |
|---|---|---|---|
| Streaming / active regions / culling | — | MISSING (by design) | No streaming concept exists. Everything is always simulated. |
| Observed vs simulated economy | — | IMPLEMENTED (trivially) | No camera-dependent economic formulas found. AgriculturalRisk hit was a false positive (no camera/visibility logic). All production, wages, and travel run on simulation state. Brief section 25 still requires observed/unobserved REGRESSION TESTS to lock this in. P2 (tests missing, architecture fine). |
| Off-map entities | LogisticsShipmentKind.OffMapInboundPurchase; off-map approach vectors | PARTIAL | Shipments model off-map legs as cell counts. Person off-map travel (migration, external freight drivers) has no persistent journey authority. P2. |
| Presentation activation | scene-owned | SCENE | Out of Muse scope; adapters + handoff notes only. |
| SimulationSystemsHub | Orchestration/Systems/ | IMPLEMENTED | ~40 authorities, save boundary Capture/Load. DueEventScheduler hub-owned and persisted. |
| Save/load | SaveLoadManager (2.1k lines) | IMPLEMENTED | Central orchestrator, clear-then-load. |
| Render-controlled simulation | — | NONE FOUND | Good. |

---

## Cross-cutting P0/P1 summary

P0-1: No birth-date authority, no runtime aging (AgePerson dead), no birthday scheduling, and a 336-day fictional calendar where the brief requires Gregorian correctness. The entire demographic time axis is missing.
P0-2: Calendar migration risk. absoluteDayIndex is the tick authority and persists in saves; derived Y/M/D must become truly Gregorian. All consumers of DaysPerMonth (28), weeksPerMonth (4), and 336-day years must be found and corrected.
P1-1: No scenario preset architecture and no scenario coordinator. ScenarioAsset cannot author a starting world.
P1-2: No demographic runtime drivers (birth, death/mortality, marriage, migration). RecordBirth/RecordDeath/EstateService/AddSpouses are source-only.
P1-3: PopulationGenerator violates Canon 12.6G order and creates no kinship.
P1-4: Production authority split (SharedBusinessRuntimeManager monolith vs Generic path) unmapped; duplicate inventory truths.
P1-5: New Game / Load / Restart / Replay semantics undefined; ghost-state contamination unverified.
