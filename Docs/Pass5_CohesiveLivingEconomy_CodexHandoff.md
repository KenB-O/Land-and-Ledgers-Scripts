# Pass V Codex Handoff — The Cohesive Living Economy

Date 2026-10-12. This document hands the completed Muse Pass V source work to
Codex for Unity integration. It covers the starting and final commits, the Tier
One audit, every authority change, the scenario and demographic systems, the
one-simulation contract, persistence, read models, scene-owned dependencies,
manual Unity tests, expected conflicts, remaining defects, and calibration
holds. A companion worker-4 note lives at Docs/Pass5_Worker4_CodexHandoff.md
with the Unity camera-test list, the P2-4 authority-cleanup assessments, known
limitations, and measured performance actuals.

## 1. Starting and final branch and commits

- Starting baseline bd76bc98c4b8fd424a69a8fff859a3799153f314
  (integration/pass4-universal-tasks-equipment-activities, the published Pass IV
  integration). The Pass V audit commit 78b5188 sits directly on top of it.
- Feature branch muse/tier-one-cohesive-living-economy.
- Final HEAD 6e62807423ec4663490dbdd12e9c8ba2dce0fc86.
- Worker branches merged with --no-ff in order.
  - Worker 1 muse/pass5-scenario-presets at 505e923, merged at ce9d70f.
    Scenario presets, coordinator, lifecycle, starting economy (slice L).
  - Worker 2 muse/pass5-aging-demographics at 2afc5e4, merged at c9808a3.
    Gregorian calendar, birth-date authority, demographic drivers, generation
    order (slices K, M).
  - Worker 3 muse/pass5-household-economy at 6703131, merged at 5d0ea58.
    Production consolidation, batched errands, hardship, entrepreneurship
    (slices A through I).
  - Worker 4 muse/pass5-unity-simulation at 0f96ad9, merged at e98e0db.
    Birthday scheduling, observation-mode contract, off-map journeys,
    autonomous society sim, section-25 regression (slices J, K and M support).
- Finisher integration commits on the feature branch.
  - bca75e5. Resolved the worker-4 merge. Unified the duplicate birthday event
    kind (kept DueEventKind.Birthday = 10 from worker 2, retired worker 4's
    BirthdayDue = 10). Deduplicated KinshipRegistry.AllEdges (kept the
    IReadOnlyList property, inlined the defensive copy at the save call site).
    Updated three RecordBirth call sites for the new birthDate parameter.
  - 6e62807. Corrected the society-driver birth provenance to GeneratedExact
    to match the causal-profile contract.
- Scope of change. 146 files changed, 20,184 insertions, 263 deletions, 110
  new files, all on the feature branch. Main was never touched. No
  force-push. No history rewriting.
- Verification. Roslyn harness 828 passed, 4 failed. The 4 failures are the
  documented pre-existing baseline failures (3 MoneyOrderServiceTests, 1
  P5PropertyLegalJourneyTests), preserved untouched. No Unity validation is
  claimed. No Test Runner or PlayMode was run.

## 2. Full Tier One audit

The audit (Docs/Pass5_TierOne_Causality_Audit.md, method source inspection at
the baseline, repository as truth) classified every authority. The P0 and P1
findings that drove the pass follow. P2 and P3 items are in the audit doc.

- P0-1. No birth-date authority existed. No DateOfBirth or BirthDate field
  anywhere in production person code. HouseholdLifecycleManager.AgePerson had
  zero production callers. Population age was frozen at generation. No
  birthday scheduling existed. The calendar was a 336-day fiction with no
  leap years, no real month lengths, and weekdays unaligned to history,
  while the brief requires Gregorian correctness.
- P0-2. Calendar migration risk. absoluteDayIndex is the persisted tick
  authority. Every consumer of DaysPerMonth (28), weeksPerMonth (4), and
  336-day-year assumptions had to be found and corrected.
- P1-1. No scenario preset architecture and no scenario coordinator.
  ScenarioAsset could not author a starting world.
- P1-2. No demographic runtime drivers. RecordBirth, RecordDeath,
  EstateService, and KinshipRegistry.AddSpouses were source-only with no
  runtime callers. Nothing was born or died at runtime.
- P1-3. PopulationGenerator violated Canon 12.6G order (household-count
  first) and created zero kinship edges.
- P1-4. Production authority split. SharedBusinessRuntimeManager (monolith)
  versus the Generic path could both touch the same businesses, with
  duplicate inventory truths (BusinessRuntimeState category stock versus
  GenericConfiguration positions).
- P1-5. New Game, Load, Restart, and Replay semantics were undefined.
  Ghost-state contamination across runs was unverified.
- All P0 and P1 items have implemented source coverage in this pass, listed
  by worker below. P0-3 (conservation re-verification after the calendar and
  age migration) is covered by the full harness run and the reconciliation
  fixtures.

## 3. Source files added and modified

110 new files, 36 modified. By worker:

- Worker 1. Orchestration/Scenarios/Generation/ScenarioCoordinator.cs,
  ScenarioPreset and preset contracts, GenerationRng (partitioned streams),
  ScenarioCampaignRecord, StartingHouseholdEconomy, plus slice L tests
  (scenario preset and seed reproducibility).
- Worker 2. Core/Time/GregorianDateMath.cs, Core/Time/CalendarAnchor.cs,
  Domains/Population/BirthDateAuthority.cs (BirthDate, AgeAuthority,
  BirthDateProvenance), Domains/Population/BirthdayService.cs,
  Domains/Population/DemographicDrivers.cs (birth, mortality, marriage,
  migration), Domains/Population/FamilyFormationDrivers.cs, reordered
  Domains/Population/PopulationGenerator.cs with kinship edges, plus
  calendar, birthday chronology, and demographic continuity tests (slices
  K, M).
- Worker 3. Domains/Economy/Core/ProductionPathRegistry.cs (one-path
  routing with conserved inventory consolidation),
  Domains/Economy/Creation/NpcEntrepreneurshipDriver.cs and the Npc
  opportunity chain, HouseholdErrandCoordinator (batched errands),
  HouseholdHardshipCredit, HouseholdAssetSale, plus slice A through I
  tests.
- Worker 4. Orchestration/Systems/AutonomousSocietyDriver.cs (30, 90-day,
  and 3-year society sims), Domains/Population/BirthdayScheduling.cs,
  Domains/Population/ObservationMode.cs,
  Domains/World/Journeys/OffMapJourneyAuthority.cs,
  ReadModels/Person/PersonCausalProfile.cs, additive save support for
  membership and kinship registries, plus 6 test files and 32 test cases
  under Tests/Editor/PassV/.
- Finisher. Merge conflict resolutions (DueEventContracts, HouseholdLifecycle,
  BirthdayScheduling, AutonomousSocietyDriver, Section25 tests) and the
  RecordBirth birthDate call-site updates.

## 4. Authority changes

- Calendar. The 336-day derivation is replaced by true Gregorian math in
  GregorianDateMath, anchored by CalendarAnchor (default 1880-03-01 for
  absoluteDayIndex 0). absoluteDayIndex remains the persisted truth.
  Derived Y/M/D recompute. Leap years correct (1880 leap, 1900 not, 1904
  leap). Weekdays align to history.
- Birth-date and age. PersonState carries an authoritative BirthDate with
  provenance (Exact, GeneratedExact, Estimated, Partial, Unknown). Age is
  derived as completed calendar years, never an incremented counter.
  AgePerson is repurposed or removed as authority. Save migration assigns
  estimated dates consistent with prior age values, documented as
  estimates.
- Birthday events. DueEventKind.Birthday = 10, stable id
  "birthday:{personId}", idempotent scheduling. Worker 4's duplicate
  BirthdayDue = 10 was unified into Birthday during the merge.
- Scenario. ScenarioCoordinator is the one orchestrator for scenario
  identity, configuration, generation, initialization, validation, and
  lifecycle. It commands existing services and stores no Person, Household,
  Business, or money data.
- Production. ProductionPathRegistry routes every business through exactly
  one path (LegacyArchetype or GenericPath) and heals the duplicate
  inventory truth by conserved 1:1 consolidation with provenance.
- Kinship. KinshipRegistry.AllEdges is the single read accessor (the
  duplicate AllEdges() method added during worker 4 was removed at merge).

## 5. ScenarioDefinition and preset changes

ScenarioAsset now carries starting-world configuration. It did not before.
The new fields cover scenario id, definition version, historical start
date, settlement setting, world-generation parameters, population
constraints (ranges honoring the 55 to 95 Canon calibration), demographic
calibration, existing persons, households, businesses, player start,
ownership and property state, buildings and infrastructure, equipment and
animals, inventory, employment, cash, deposits, credit, debts, agreements,
migration relationships, external markets, historical-event settings,
objectives and deadlines, authored overrides, controlled randomness, and
validation rules.

ScenarioPreset is versioned and serializable in the project's existing data
conventions. It supports loading, validation, versioning, reuse, copying
and deriving, deterministic preview generation, diagnostics, and backward
compatibility where supported.

## 6. Authored versus generated starting conditions

The preset distinguishes three layers. Authored records are fixed by the
designer (a named historic business, a required player asset, a specific
person). Generated records are produced procedurally within the preset's
constraints (ordinary residents, ordinary businesses). Derived values are
computed from the other two (household composition from kinship, economic
roles from assets). Scenario-authored historical state (debts, relationships,
occupation histories present at start) is marked as authored history and is
never presented as simulated runtime events. Real simulated post-start
events carry full provenance.

## 7. Seed and randomization behavior

GenerationRng provides partitioned RNG streams per generation concern, so a
change to one feature does not reshuffle unrelated population. The contract
is exact. Same definition version, same seed, same start date, same inputs
produce an identical starting world. Different seeds produce different valid
worlds. If generation violates its constraints, it retries within a bounded
policy or reports an actionable failure. It never invents money, houses, or
businesses to force validity.

## 8. New Game, restart, and load semantics

- New Game generates one realized world from the selected definition and
  seed, then records the scenario id, definition version, seed, generation
  version, realized start date, and reproducibility references in the
  ScenarioCampaignRecord.
- Load Game restores the saved realized world through clear-then-load. It
  never regenerates persons, businesses, or assets.
- Restart Scenario uses documented same-seed or new-seed semantics.
- Replay Scenario starts a fresh campaign from the preset.
- Continue Failed Scenario preserves the current world where supported.
- Edit Preset changes future generation only. An authored preset changing
  after a campaign starts never retroactively transforms the saved
  campaign. No migration workflow runs without an explicit migration.
- Ghost-state prevention is test-verified. A new game inherits no player
  ownership, household memberships, employment, objectives, manager
  decisions, businesses, banking state, or stale reservations from a
  previous run.

## 9. Birth-date and age authority

Every persistent person has an authoritative BirthDate with provenance.
Exact is an authored documentary fact. GeneratedExact is internally
determined at generation with scenario provenance recorded. Estimated comes
from an age or partial record (for example save migration). Partial covers
year-only or incomplete historical information. Unknown marks no
information. The simulation distinguishes its internal exact truth from
observer and documentary uncertainty. It never presents an invented exact
historical date as documentary fact. AgeAuthority.CompletedYears is the
single derivation. No independently incremented age field survives as
authority.

## 10. Birthday scheduling

Birthdays schedule on the hub DueEventScheduler with the stable id
"birthday:{personId}". Scheduling the same id replaces the event, so
scheduling is idempotent across save/load and repeated calls. Never
duplicates, never double-ages. The handler re-derives age from the birth
date and schedules next year's event, so a double-fired event is harmless.
Deceased persons are excluded (their event is cancelled at death). Off-map
persons are included. BirthdayService.EnsureScheduledForPerson is the live
game path, wired through DemographicDrivers, FamilyFormationDrivers,
PopulationManager, and the SimulationDrivers event handler.
BirthdayScheduling (worker 4) is the scenario driver's local scheduling
helper used only by AutonomousSocietyDriver, which runs its own scheduler
instance. Both share the "birthday:{personId}" payload convention.

## 11. Leap-year handling

GregorianDateMath implements true leap-year rules. 1880 is a leap year.
1900 is not. 1904 is a leap year. Month lengths are accurate. A person born
February 29 uses one documented convention everywhere. The anniversary in
non-leap years is February 28. Legal age attainment and social birthday
observance are kept separate where relevant. The systems never switch
between February 28 and March 1 inconsistently.

## 12. Demographic lifecycle hooks

DemographicDrivers wires the runtime lifecycle. Births flow through the
birth driver into RecordBirth with Exact provenance, then schedule the
birthday event. Deaths flow through the mortality driver into RecordDeath,
which ends memberships, cancels the birthday event, retains the record and
id, and triggers EstateService for estate, dependents, property, and
succession. Marriage formation uses real persons and Canon-calibrated
distributions (1890 medians near 26 for men and 22 for women as anchors,
never triggers). Migration preserves identity, history, kinship, ownership,
debts, and agreements. Off-map persons keep aging. Household splitting
keeps the same Person record; an adult child leaving home is not a new
person. No automatic marriage at one age, no automatic job at 18, no
automatic departure on a birthday, no births to hit targets.

## 13. Household coordination

The household plans as a unit and acts through real persons.
HouseholdErrandCoordinator batches errands across needs so one trip can
serve several needs where feasible. Plans, reservations, and responsibility
flow through existing authorities. The household decides what it needs. A
real available person performs the activity with actual time, travel,
knowledge, money or credit, and goods. Several members never independently
buy the same goods for the same shared need. If the available member
becomes unavailable, the household re-plans without duplicating time or
purchases. Routine domestic knowledge is shared efficiently. External
discoveries still require legitimate information channels.

## 14. Economic hardship

The full chain is implemented and tested. Income loss draws down savings,
reduces purchases, uses available credit, accumulates arrears, and can be
refused further credit on real debt, losses, resources, and lending
judgment. Credit refusal creates no money. The person can seek work, sell
assets (HouseholdAssetSale with real pricing), move to cheaper
accommodation, and migrate. No hidden rescue, no guaranteed loan, no
guaranteed employment, no automatic landlord forgiveness, no free food.
Severe deprivation affects health only through supported authorities. There
is no debt-threshold suicide event. Death preserves estate, dependents,
property, and succession records.

## 15. Entrepreneurship

NpcEntrepreneurshipDriver evaluates legitimate opportunities through the
chain of known opportunity, resources, judgment, authority, property and
equipment and work, transactions, and consequences. It is wired to
autonomous opportunity evaluation under manager judgment and owner
restrictions. Businesses are never created from population quotas. Customer
demand is never guaranteed. NPC businesses operate under the same physical
and financial rules as the player. They can downsize, diversify, sell
assets, or fail on real conditions.

## 16. One-simulation presentation contract

There is one authoritative simulation. Capabilities, time, speed,
equipment, routes, tasks, goods, wages, purchases, production, inventory,
health, relationships, and outcomes are identical whether an entity is
rendered or not. ObservationMode defines four modes (fully represented,
off-screen, streamed-out, off-map) as a presentation bridge only. The mesh
is a puppet of the data. No camera-dependent economic formulas exist in
source. The section-25 regression tests lock this in at source level for
workers, presentation toggling, streamed save and load, wagon off-map
return, high-speed runs, headless scheduling, birthdays while unloaded, and
birth, death, and household split with no mesh. Unity camera verification
remains Codex-side; the exact manual tests are in section 22 and in
Docs/Pass5_Worker4_CodexHandoff.md.

## 17. World movement and off-map continuity

OffMapJourneyAuthority gives persons and vehicles persistent journey
authority for off-map travel. A journey carries traveler, origin,
destination, route, segment progress, speed, vehicle, animals, cargo, and
financial obligations under one identity. Off-screen, streamed-out, and
off-map are distinct states and are never interchanged. A wagon that leaves
on an external route is the same entity when it returns. No recreated
vehicle, no duplicated inventory, no teleported goods. Position is
reconstructable from authoritative state. Visible animation follows
authoritative progress.

## 18. Hub and driver registration

SimulationSystemsHub remains the composition root at about 40 authorities,
with the save boundary Capture and Load. DueEventScheduler is hub-owned
and persisted. New Pass V registrations include the Gregorian calendar
services, AgeAuthority, BirthdayService wiring, DemographicDrivers,
ScenarioCoordinator, GenerationRng, ProductionPathRegistry, the
OffMapJourneyAuthority, and the AutonomousSocietyDriver (scenario scope).
SimulationDrivers handles the hub birthday due-event dispatch. PopulationManager
runs EnsureScheduledForPopulation after generation and after load.

## 19. Persistence and migration

Save and load is clear-then-load throughout. New persisted state in this
pass includes birth dates with provenance, birthday due-events with stable
ids, scenario campaign records (id, definition version, seed, generation
version, realized start date, reproducibility references), preset versions,
kinship edges (KinshipRegistry save DTO, additive), household memberships
(additive), off-map journey state, and production-path assignments.
Migrations are additive only. The calendar migration keeps absoluteDayIndex
as the persisted truth; Gregorian Y/M/D recompute. Existing saves receive
estimated birth dates consistent with prior ages, documented as estimates.
The birthday event chain survives save and load without duplication or
double-aging. Repeated load is idempotent.

## 20. Causal read models

PersonCausalProfile (P3-1) answers who acted, why, what they knew, what
they chose, what alternatives failed, what authorized the action, which
task, activity, or transaction occurred, what resources changed, and what
happened next. It exposes age and birth-date provenance, household,
occupation, employment, current activity, residence, property
relationships, cash and credit access, obligations, scenario origin, and
demographic history, all bounded and visibility-respecting. Visible age
derives from the simulation date and birth-date authority. No separate UI
age counter exists.

## 21. Scene-owned dependencies

Muse cannot touch Unity scenes, prefabs, terrain, or ProjectSettings. The
following remain Codex-side. Boarding-house runtime registration. Housing
search reaching real boarding-house capacity. NPC construction material
source registration. Bank deposit projection refresh before underwriting.
Full ordinary Main Scene autonomous economic behavior and visual
inspection. Presentation activation and culling for the
observation-mode contract. The scenario preset authoring UI (section 23).
The Unity Test Runner suite, including PopulationGeneratorIntegrationTests,
which needs the TownWorldController MonoBehaviour and is excluded from the
Roslyn harness for that reason.

## 22. Exact Unity PlayMode and manual tests

Run these in the Unity Test Runner or by hand. Do not mark them passed
without executing them.

- A. Focus the scene camera on a worker for a full in-game week, then run
  the same week with the camera pointed at empty terrain. Compare the
  economy ledger hashes. They must match.
- B. Repeatedly enable and disable the worker portrait and nameplate UI
  during a week. No duplicate persons, no duplicate ledger entries, no
  wage drift.
- C. Save while a region is off-screen, load, and continue. Cash, goods,
  and person records must equal an uninterrupted run.
- D. Send the wagon off-map on an external route and let it return. It
  must be the same vehicle entity. Inventory must not duplicate and goods
  must not teleport.
- E. Run the game at 10x speed for a week with the camera following a
  worker. Outcomes must equal a 1x run.
- F. Run headless (no rendering) for a week and compare against a
  rendered run.
- G. Keep a region unloaded across a birthday. The birthday event must
  still fire on its stable id "birthday:{personId}". Aging must not pause
  and must not double.
- H. Trigger a birth, a death, and a household split with no character
  mesh loaded. Person ids must not duplicate and histories must not be
  lost.
- I. Run PopulationGeneratorIntegrationTests in the Test Runner. It
  verifies the Canon 12.6G generation order, the 55 to 95 calibration,
  birth-date authority, and kinship coherence. It cannot run in the
  Roslyn harness.
- J. Play a generated scenario from the Frontier Enterprise preset (or
  equivalent) for 30 in-game days. Verify employment, wages, purchases,
  rent, and household consumption reconcile.

## 23. Scenario-preset authoring and editor requirements

The source models, validation, and serialization for presets are complete.
What remains is Unity-side. A preset editor that can author the
starting-world configuration (section 5), preview deterministic generation
for a seed, run validation with clear diagnostics, and save versioned
presets as assets. Copy and derive workflows for making a new preset from
an existing one. The editor must never write preset data into a live
campaign. No editor framework was built in this pass; build on the
project's existing data-authoring conventions.

## 24. Expected integration conflicts

- The unpublished Codex checkpoint 0d7b8d9 was never pushed. It reportedly
  contains live tenancy, NPC business, and HousingAuthority integration.
  When it appears, expect conflicts in Domains/World/Property tenancy
  files, Domains/Economy/Creation NPC business files, and any housing
  authority registration it touched. The production-path registry (section
  4) is the authority for routing conflicts; new businesses must land on
  exactly one path.
- The birthday kind unification (section 4) renamed worker 4's BirthdayDue
  to Birthday. Any Codex-side code referencing DueEventKind.BirthdayDue
  must move to DueEventKind.Birthday.
- KinshipRegistry.AllEdges is now a property, not a method. Callers using
  AllEdges() must use the property.
- RecordBirth now requires a BirthDate parameter. Any unconverted callers
  will fail to compile; the fix is to construct the date from the known
  birth day, following DemographicDrivers as the pattern.
- ScenarioAsset has new starting-world fields. Old authored assets need a
  validation pass; unknown fields must not silently default to a
  different economy.

## 25. Remaining Tier One defects

- P2-4 authority cleanups were assessed and deliberately not changed (see
  Docs/Pass5_Worker4_CodexHandoff.md for the full analysis). Duplicate
  "what is person doing" (PersonScheduleTracker versus
  PersonState.scheduleState versus PlayerDirector) needs a design review
  before unification. The MineLaborRegister legacy wage path is a second
  wage scheduler; cleanup needs cross-authority idempotency keyed on
  employment id and pay period. The PlayerDebtManager loan book is separate
  from FinancialObligationAuthority; it is not shimmable in the Roslyn
  harness, so cleanup needs Unity-side verification. WorkTask.locationId
  is an unwired string; a resolver to JourneyModel locations is optional
  low-risk work.
- PopulationGeneratorIntegrationTests cannot run in the Roslyn harness
  (section 21). It is the only direct test of the 12.6G generation order
  and must run in the Unity Test Runner.
- The 4 pre-existing harness failures (3 MoneyOrderServiceTests, 1
  P5PropertyLegalJourneyTests) remain open and are outside this pass.
- BirthdayService (live game path) and BirthdayScheduling (scenario
  driver helper) share the payload convention but are separate
  schedulers. They do not conflict today because the scenario driver
  owns its scheduler instance, but a future unification should pick one
  canonical scheduler.

## 26. Historical research and calibration holds

- Legal majority, schooling requirements, child labor rules, and marriage
  eligibility by jurisdiction and year are explicit research holds. The
  code uses no invented universal thresholds. Age modifies eligibility
  inputs; events still require their own causes.
- The 55 to 95 opening population calibration (preferably 70 to 85) is
  preserved per the Canon note and was not changed. The illustrative 75
  to 100 range appears only where a scenario explicitly configures it.
- Historical wage, price, and productivity figures used in calibration
  remain seed evidence with source, geography, year, units, and
  confidence attached. Unknown rates are configurable estimates, never
  hard-coded universal constants.
- The experience-formation rate from Pass III remains as calibrated. It
  was flagged for playtest feel and deliberately not retuned.
