# Pass V Worker 4 Codex Handoff

## Unity camera tests to record (brief section 25, Codex side)

The source level proof is done. The simulation has no streaming or culling
concept. Everything is always simulated, so camera state cannot change
outcomes. The Roslyn tests lock this in. What remains is Unity side
verification that the presentation layer honors the contract.

Record these in the Unity Test Runner.

A. Focus the scene camera on a worker for a full in game week, then run the
same week with the camera pointed at empty terrain. Compare the economy
ledger hashes. They must match.

B. Repeatedly enable and disable the worker portrait and nameplate UI during
a week. No duplicate persons, no duplicate ledger entries, no wage drift.

C. Save while a region is off screen, load, and continue. Cash, goods, and
person records must equal an uninterrupted run.

D. Send the wagon off map on an external route and let it return. It must be
the same vehicle entity. Inventory must not duplicate and goods must not
teleport. Covered at source level by OffMapJourneyAuthorityTests.

E. Run the game at 10x speed for a week with the camera following a worker.
Outcomes must equal a 1x run.

F. Run headless (no rendering) for a week and compare against a rendered run.

G. Keep a region unloaded across a birthday. The birthday event must still
fire on its stable id `birthday:{personId}`. Aging must not pause and must
not double.

H. Trigger a birth, a death, and a household split with no character mesh
loaded. Person ids must not duplicate and histories must not be lost.

## P2-4 authority cleanups (documented, not changed)

Four duplicate or legacy authorities were assessed. None were changed because
each cleanup carries medium risk or needs design review. Details follow.

### Duplicate "what is person doing"

Three sources answer this. PersonScheduleTracker holds activity reservations
and refuses double booking. PersonState.scheduleState holds a
PopulationScheduleState snapshot. PlayerDirector holds player facing
direction state. They serve different consumers (simulation, persistence,
presentation) but nothing enforces that they agree. A cleanup would need a
single source of truth with the other two as projections, plus a migration
for saved scheduleState. Needs design review. Do not merge without it.

### MineLaborRegister legacy wage path

MineLaborRegister.SettleShiftWages is a second wage scheduler alongside
WageSettlementService. It moves cash through HouseholdLedger.RecordOutflow
and RecordWagePayment, so the single cash writer rule holds. It references
assignment.EmploymentId as provenance. The risk is double payment if a miner
is also in the EmploymentRelationshipRegistry and gets paid by both paths.
Cleanup needs cross authority idempotency keyed on employment id and pay
period. Medium risk. Left alone.

### PlayerDebtManager dual truth loan book

PlayerDebtManager keeps its own loan records separate from
FinancialObligationAuthority. Unifying them would move player loans into the
single debt authority, but PlayerDebtManager is not shimmable in the Roslyn
harness (Unity dependencies), so the cleanup cannot be tested here. Needs
Unity side verification. Left alone.

### WorkTask.locationId unwired string

WorkTask.locationId is a plain string, documented as a placeholder until the
location model lands. The JourneyModel now exists and OffMapJourneyAuthority
reuses it. A low risk cleanup would add a resolver from locationId to a
JourneyModel location, refusing unknown ids loudly. Optional. Left alone
because no consumer needs it yet.

## Known limitations

The autonomous society driver is a scenario, not the game. It wires real
authorities but makes scenario choices.

The economy is tight. The forge is sustainably profitable. The mill, bakery,
and store degrade over a multi year run. The store goes staffless on day 100
and survives on capital. Households dissave slowly. This is coherent
distress, not a bug. Reconciliation holds throughout.

Wages were halved from the original scenario values so the 17 person
consumer base can support the wage bill. The original values made every
business insolvent by day 100.

The 3 year run shows 4 births, 2 deaths, 44 birthdays, 3 eligibility reviews,
6 households, 17 persons. All reconcile clean.

Birthday chaining searches from the day after a birthday fires. Searching
from the firing day reschedules the same day and it fires again tomorrow.

The population id allocators must be seeded past authored ids. AddPerson
seeds the EntityIdRegistry but not the population's internal allocators.
BuildStandard now calls RestorePersonIdAllocator(14) and
RestoreHouseholdIdAllocator(6).

Equipment assets must be registered with the EquipmentReservationService
before use. Unregistered assets read as missing usable equipment. The driver
registers the communal oven and wagon-1 at capacity 1.

Production workers (miller, baker, smith) do their work through the
production runtime. They must not also consume a separate 480 minute
work shift task, or person-time is double booked and production never
completes.

## Performance actuals (Roslyn harness, not Unity)

Scheduling 2000 birthdays took 10 ms. 2000 birth-date age queries took less
than 1 ms. A 30 day society sim took 114 ms wall time. A 90 day sim took
13 ms. A 3 year sim took about 200 ms.

## Files added

- Orchestration/Systems/AutonomousSocietyDriver.cs (the society sim)
- Domains/Population/BirthdayScheduling.cs (stable birthday ids, scheduler)
- Domains/Population/ObservationMode.cs (four modes, presentation bridge)
- Domains/World/Journeys/OffMapJourneyAuthority.cs (person and vehicle off map travel)
- ReadModels/Person/PersonCausalProfile.cs (P3-1 causal read model)
- Tests/Editor/PassV/ (6 test files, 32 tests)

## Files modified

- Core/Time/DueEventContracts.cs (added DueEventKind.BirthdayDue = 10)
- Domains/Population/BirthDateAuthority.cs (added BirthDateProvenance.Unknown = 4)
- Domains/Population/HouseholdMembershipRegistry.cs (AllMemberships for save)
- Domains/Population/KinshipRegistry.cs (AllEdges for save)
