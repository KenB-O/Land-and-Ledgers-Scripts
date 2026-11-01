# Pass VIII Worker 7: Person and Household Autonomy plus Shared Assets

## Scope

Brief sections 9 and 10. Person and household economic autonomy (can a normal
generated person genuinely participate: seek employment, work, earn, help the
household, shop, consume, house, travel, socialize, learn, change employment,
respond to hardship, care for dependents, join family and demographic events)
and shared assets with real opportunity costs (extend the Pass VII reservation
principle only where a proven gap exists).

## 1. Per-cycle audit: the first missing connection in each major cycle

The required chain is household circumstances to person decisions to actual
work to wages to household budget to real consumption to business revenue to
business response to opportunities or hardship. For each link, the first
missing connection found on disk, with file references. "Wired" means a
production caller exists, not just an authority.

### 1a. Circumstances to decisions: WIRED

`HouseholdAutonomyDriver.EvaluateHouseholdDay`
(`Domains/Population/HouseholdAutonomyDriver.cs`) builds pressure from real
state (ledger reserves, rent obligations, expected income from the employment
registry, dependents, known opportunities, shelter risk) and evaluates through
`HouseholdDecisionService` (`Domains/Population/HouseholdDecisionService.cs`),
then executes through `HouseholdDecisionDriver`
(`Domains/Population/HouseholdDecisionDriver.cs`). Driven daily per household
in production by `SimulationDrivers.DriveHouseholdAutonomy`
(`Orchestration/Systems/SimulationDrivers.cs`). No gap.

### 1b. Decisions to actual work: MISSING, now fixed

`HouseholdDecisionDriver.ExecuteSeekIncome` discovered openings through the
contact graph and recorded them in `HouseholdOpportunityLog`
(`Domains/Population/HouseholdOpportunityLog.cs`). Nothing ever consumed that
log. `UnpursuedCount` fed back into pressure inputs, but no person could ever
apply to a discovered opening: the entire person-side of the labor market was
a dead end, and all hiring was employer-initiated
(`RecruitmentService.OpenEffort` to `DirectApproach`/`AskForReferral` to
`HireFromInquiry` in `Domains/Economy/Recruitment/RecruitmentService.cs`).
Fixed by `OpportunityPursuitService`
(`Domains/Population/OpportunityPursuitService.cs`), `RecruitmentService`
`.ApplyForOpening` plus `.OpenEffortsForBusiness`, and daily pursuit wiring in
`SimulationDrivers.DriveOpportunityPursuit`. An application is an inquiry,
never an employment: hiring stays the employer's decision.

### 1c. Work to wages: MISSING, now fixed

`BusinessRuntimeState.ResolveWeeklyPayrollFromEmployments`
(`Domains/Economy/Core/BusinessRuntimeState.cs`) paid every Active employment
its full agreed weekly wage on the employment record plus business cash, with
no check that the worker actually worked. Employment alone meant automatic
attendance and automatic pay. Fixed by the optional
`WeeklyAttendanceProvider` seam on `BusinessRuntimeState` (null keeps legacy
behavior), honored identically by `WageSettlementService`'s payroll replay
(`Domains/Economy/Labor/WageSettlementService.cs`) so the `PayrollEvent` read
model never credits a worker the till did not pay. An unworked week earns no
wage and accrues no employer liability.

Attendance means verified presence, not just production output. A production
worker whose business has no production to run (target stock reached, inputs
short) still attended: upkeep and standby occupy the shift through the real
task system (budget, tracker, minutes) in `RunBusinessProduction`, so payroll
attendance is satisfied without conjuring goods. A genuinely absent worker
(dead, off-map on a multi-week journey, incapacitated) records nothing and is
not paid. Wired in the 90-day scenario driver from real per-week minutes
worked. Save and load safe: the provider is a live closure, not save data,
so `CopyInto` reinstalls it on DTO-rebuilt businesses; without that, a
restored run silently fell back to legacy payroll (caught by the 3-year and
streamed-region save/load identity tests).

### 1d. Wages to household budget: WIRED

`ResolveWeeklyPayroll` and `PayAgreedWage` credit the worker's household ledger
through `HouseholdLedger.RecordWagePayment`
(`Domains/Population/HouseholdLedger.cs`) via the employment record. One cash
writer, no second wage pathway. No gap.

### 1e. Budget to real consumption: WIRED

`HouseholdShoppingLoop` (`Domains/Population/HouseholdShoppingLoop.cs`)
spends through the household ledger with rollback on failure, and consumes the
`HouseholdSpendingPolicy` standing instruction produced by Economize decisions
(wired in `SimulationDrivers` line 186). No gap.

### 1f. Consumption to business revenue: WIRED

Household purchases credit business cash through the single-writer path;
failed stocking rolls the sale back on both sides. No gap.

### 1g. Business revenue to business response: WIRED

`ManagerDecisionExecution` (`Domains/Economy/Labor/ManagerDecisionExecution.cs`)
turns demand and cash position into hire, procure, borrow, and schedule
decisions; `NpcBusinessLifecycleService` handles formation and closure. No gap
found at the first-connection level.

### 1h. Opportunities and hardship to new decisions: WIRED, with one fixed defect

Pressure inputs already carried reserves, obligations, expected income,
dependents, known opportunities, shelter risk, credit, saleable surplus, and
job loss. Defect found and fixed while proving provider loss: the job-loss
fingerprint in `HouseholdAutonomyDriver` was household-scoped but stored and
checked per member, so when the provider died the surviving spouse was
misreported as the job loser ("P2 employment ended" when P1 died). Now
per-member own-employment fingerprints, plus departed-earner detection so a
dead provider's household still registers provider loss exactly once.

## 2. Daily time

Person-time exclusivity is enforced twice. `WorkTimeBudget`
(`Core/Time/WorkTimeBudget.cs`) caps usable minutes per day and refuses
over-commitment loudly. `PersonScheduleTracker`
(`Domains/Population/PersonScheduleTracker.cs`) is the simultaneity guard:
overlapping activity windows for one person are refused, never silently
overlapped. `DailyActivityPlanner` (`Domains/Population/DailyActivityPlanner.cs`)
reserves sleep, meals, job-seeking, and management blocks on real time, and
`DomesticLaborService` schedules domestic tasks through the task authority,
budgets, and the tracker together. All assignment paths funnel through
`TaskAuthority.AssignTask`/`StartTask`, so the new worker-availability seam
covers the work scheduler, manager execution, domestic labor, and the scenario
driver alike.

Finding, not fixed: no person-time is scheduled anywhere for childcare,
caregiving, or recovery. Dependents are counted in pressure inputs but nobody
reserves time to care for them. `PersonActivityKind` has `FamilyWork` and
`Resting` as the honest near fits today. If care activities are modeled later,
they must go through the tracker, or the simultaneity guarantee breaks.

Second jobs are bounded by the same per-person budget: a worker taking evening
work after a full shift is refused at commit time. No free errands: tasks
without definitions, owners, or budget are refused.

## 3. Provider loss

Preserved: the Pass VII estate system is untouched. `MortalityDriver`
still ends employments at death, opens the estate exactly once, and invents no
replacement earnings (proven again in `Pass8ProviderLossTests`).

New proof that loss changes behavior: after the provider's death the surviving
household's decision moves from `NoAction` to `SeekIncome` through the real
pressure path (expected income zeroed, shortfall real), the survivor pursues a
genuinely open hiring effort through the new pursuit chain, and the job-loss
note names the deceased provider, never the survivor.

Disability had no economic teeth: `PersonHealthState.laborCapacityPercent`
was computed by `PopulationHealthEvaluator` and consumed nowhere. Now
`DailyNeedsService.WorkCapacityFactor` multiplies nutrition by health, so a
seriously injured provider (15 percent capacity) drops from 600 to 90 usable
minutes, cannot commit a full shift, and with attendance-gated payroll earns no
wage for unworked weeks. Proven in `Pass8ProviderLossTests`.

## 4. Demographic continuity

* Real birthday aging: `BirthdayService` re-derives age from the birth-date
  authority and reschedules exactly one event per living person; the dead are
  excluded. No gap.
* Employment eligibility: `PersonState.RefreshDerivedAge` refreshes age band
  AND labor access from the birth date. Defect found in the 90-day scenario
  driver: `ProcessBirthdays` mirrored `age`/`ageBand` by hand and never
  refreshed `laborAccessLevel`, so scenario children stayed
  `LaborAccessLevel.None` forever. Fixed: the driver now sets
  `person.birthDate` at creation and calls `RefreshDerivedAge` on birthdays.
* Family formation, inheritance, migration: `BirthDriver`, the estate
  lifecycle driver, and `OffMapJourneyAuthority` are wired. No gap.
* Dead never offered work, beyond recruitment: `TaskAuthority` gained the
  `IsWorkerUnavailable` seam, enforced at both `AssignTask` and `StartTask`
  (a worker who dies between assignment and start never starts);
  `OffMapJourneyAuthority.DepartPerson` gained `IsPersonUnavailable` (the
  dead never travel); `SimulationDrivers.AddLaborWorker` skips deceased owners
  in scheduling. Both seams are wired in production
  (`SimulationDrivers.EnsureWorkerAvailabilitySeam`) and in the scenario
  driver. Proven in `Pass8DeadWorkerGuardTests`.
* Absent persons and accommodation: the strict sweep is unchanged. The lenient
  side is designed and implemented in `AbsenceArrangementPolicies`
  (`Domains/World/Property/AbsenceArrangementPolicies.cs`): a valid arrangement
  is a still-standing agreement (`HousingAbsenceSweep.AgreementStillStands`)
  OR an active off-map person-journey with an expected return (freight runs,
  buying trips, family travel). One-way migration, completed journeys, and
  overdue returns are not valid arrangements, and death is never one (optional
  `isDeceased` guard). The predicate captures the day and is rebuilt per sweep
  day. Proven in `Pass8LenientAbsenceArrangementTests`, including strict
  ending what lenient holds.

## 5. Shared assets and opportunity costs (section 10)

Principle applied: one physical resource serves multiple purposes over time
but never incompatible full capacity simultaneously; owner choices carry real
costs. Only proven gaps were touched.

* Proven gap, fixed: `RentalFleet.Return`
  (`Domains/Economy/Equipment/RentalFleet.cs`) removed the rental record but
  never released the unified-authority hold taken by `RentOut`. A returned rig
  stayed reserved forever and could never be rented or used again. `Return`
  now takes the reservations service (optional, legacy call shape kept) and
  releases the hold, warning loudly when the service is absent. Proven in
  `Pass8RentalFleetReturnTests`, including rent-return-rent.
* Verified sound, untouched: horse rental (`HorseRental`) releases holds on
  return, settles fees with real cash or real obligations, moves custody, and
  terminates loudly on sale; draft power reservations are unified; the freight
  pool, `EquipmentReservationService` atomicity, and workstation gating were
  re-checked and hold.
* Finding, not built: `RentalFleet` records `FeeCents` but moves no cash, and
  it has no production callers. Wiring a rental-cash path with no caller would
  be framework for its own sake, so it is documented here instead of built.

## 6. What remains source-only vs Unity-needed

All changes are plain C# in `Core`, `Domains`, `Orchestration`, and `Tests`;
nothing touches scenes, prefabs, terrain, packages, or project settings. The
Roslyn harness compiles and runs everything below. Unity-needed: the two new
production seams (`TaskAuthority.IsWorkerUnavailable`,
`OffMapJourneyAuthority.IsPersonUnavailable`) are wired in `SimulationDrivers`
for the task authority; the journey authority's seam is wired in the scenario
driver and needs its production caller (wherever `DepartPerson` is invoked in
game wiring) to install the same one-line death check. The attendance seam is
wired in the 90-day scenario driver; the Unity hub needs a weekly attendance
accumulator fed by task completion before it can install
`WeeklyAttendanceProvider` in production (the seam, the fail-closed
semantics, and the replay honesty are all in place).

## 7. Files changed and added

Modified: `Core/Tasks/TaskAuthority.cs` (worker-availability seam),
`Domains/Economy/Core/BusinessRuntimeState.cs` (attendance seam),
`Domains/Economy/Equipment/RentalFleet.cs` (return releases the hold),
`Domains/Economy/Labor/WageSettlementService.cs` (attendance-aware replay),
`Domains/Economy/Recruitment/RecruitmentService.cs` (`ApplyForOpening`,
`OpenEffortsForBusiness`), `Domains/Population/DailyNeedsService.cs`
(health-aware capacity factor), `Domains/Population/HouseholdAutonomyDriver.cs`
(per-member job-loss fingerprints plus provider-loss detection),
`Domains/World/Journeys/OffMapJourneyAuthority.cs` (person-availability seam),
`Orchestration/Systems/AutonomousSocietyDriver.cs` (seam wiring, birthday
labor-access fix, weekly attendance tracking and payroll wiring),
`Orchestration/Systems/SimulationDrivers.cs` (seam wiring, dead-owner skip in
scheduling, daily opportunity pursuit).

Added: `Domains/World/Property/AbsenceArrangementPolicies.cs` (lenient
arrangement recognition), `Domains/Population/OpportunityPursuitService.cs`
(discovery to application), six test fixtures under `Tests/Editor`
(`Pass8LenientAbsenceArrangementTests`, `Pass8DeadWorkerGuardTests`,
`Pass8AttendancePayrollTests`, `Pass8OpportunityPursuitTests`,
`Pass8ProviderLossTests`, `Pass8RentalFleetReturnTests`), each with a fresh
`.meta` guid, plus fresh `.meta` files for the two new production files.

## 8. Incomplete and why

* Childcare, caregiving, and recovery have no scheduled person-time (section 2).
  Modeling them is new scope, not a broken connection; recorded as a finding.
* `RentalFleet` rental fees move no cash and the fleet has no production
  callers (section 5). Building the cash path without a caller would be
  framework for its own sake; recorded as a finding.
* The Unity hub does not yet install `WeeklyAttendanceProvider` (section 6):
  production has no weekly attendance accumulator, and inventing one without
  the task-completion feed would be guessing. The seam is proven and the
  scenario driver demonstrates the wiring.
* The deferred PASS7-001 (daily estate-processing step driving open estates
  through the lifecycle driver) was deliberately not taken: the mission said
  to preserve the Pass VII estate system, and the deferred register owns that
  integration.
