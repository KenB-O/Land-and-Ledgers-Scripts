# Pass 2 — Task System Expansion: Review and Handoff

Pass 2 of the new series. Base: `muse/generic-business-reconstruction` at
`c26be3ec98fecc11eee1cb1cedd9662fd470dfc8` (Pass 1, Generic Business
Reconstruction). Branch for this pass: `muse/pass2-task-system` (SHA and
push confirmation are in the pass report, not in this file, because they
change at push time).

Governing docs: Documentation v3.0, Canonical Project Bible Revision
XXXI and Technical Implementation Bible Revision XIV, both dated
2026-10-07. Proposals 001-105 are approved; 106-145 are pending and
were NOT promoted here.

## 0. What this pass is, and what it is not

Pass 2 ONLY. It does NOT start economy ordering UI, player depth, or
town growth / immigration (Kennedy's order is task, then economy, then
player, then town). It does NOT start the queued fuel-dealer off-map
input purchase option or the housing heating alternatives
investigation — those are economy-procurement work for a later pass
and are noted here only as queued, not implemented.

It does NOT claim Unity compilation, Unity Test Runner, PlayMode,
scene wiring, prefab wiring, or Inspector values: no Unity run
happened in this pass, and none is claimed. Everything verified here
was verified in the small Roslyn harness (section 11), except the two
Unity-only test files, which are authored and explicitly NOT RUN.
Unity AI owns first-compilation busy work. Codex owns hard problem
solving and final integration. Neither prompt is written here;
ChatGPT (manager) writes those from the parent's summary of this pass.

## 1. Surviving decisions (recorded, not guessed as canon)

- **Bakery model:** the shop runtime (PlanBatch / WorkDay, used by the
  slice runs) is the production bakery model. The staged 9-step chain
  is a batch-execution path whose finished goods reach customers ONLY
  through the new staged-to-sellable transfer (section 5). It is not
  a second production model.
- **Idle:** a schedule activity (PersonActivityKind.Idle, appended as
  value 14, never renumbered), instantly preemptible, budget-exempt,
  and excluded from hiring pressure. It is NOT a WorkTask, and no
  idle task definition exists to issue by mistake.
- **Store actions:** ordering, shelf/back moves, payment handling,
  and bookkeeping are tasks; choosing a supplier and setting a price
  or margin are instant decisions (recorded, no person-time, never
  counted as labour demand). See Pass2TaskCatalogs.StoreActionDecisions.
- **Crew:** solo work is never refused and never capped per job.
  Effective workers = 1 + 0.7 * (crew - 1); adjusted minutes =
  ceil(base / effective). A 90-minute solo fell is 53 minutes with
  two hands and 30 with four. Tuning, not canon, not a per-job cap.
- **Skills:** hook only. Every new definition carries a SkillIds
  value, a named hook id (forestry, fuel-handling, baking,
  hospitality, teller-work), or an explicit unskilled marker.
  No full skill system was invented and the experience formation
  rate was NOT retuned (Pass III flag, Kennedy decision).
- **Work day:** a sequence of tasks, not a fixed 10-hour block
  (Pass2TaskSequenceDay). The existing 600-minute WorkTimeBudget
  default cap was NOT removed: whether it survives, is raised, or is
  replaced by pure task sequencing is a Kennedy decision, and the
  sequence view and the budget currently coexist. A driver must not
  enforce only one while pretending the other is gone.

## 2. Idle (schedule activity)

- `PersonActivityKind.Idle = 14` appended in PersonScheduleTracker.cs
  (append-only; no existing value renumbered).
- PersonActivityContracts: Idle is interruptible (true) and does
  not consume the work-time budget (false, explicit case).
- Pass2IdleActivity: hiring-pressure minutes excluding idle
  (idle minutes subtract to zero, never below zero). There is no
  idle WorkTask to register, issue, or count.
- OPEN (Kennedy decision, not guessed): idle behaviour — wait in
  place, wander, usual spot, near workplace. No behaviour is encoded.

## 3. Slice jobs first

General Store (Pass2TaskCatalogs, all through TaskAuthority):
gs.order-from-supplier (30 min, explicit unskilled — ordering has
no skill id in the repo today), gs.move-shelf-to-back and
gs.move-back-to-shelf (2 min/lot, stocking), gs.handle-payment
(2 min, customer-tending), gs.bookkeeping (15 min, explicit
unskilled, low priority). Instant actions, decided and documented:
gs.choose-supplier and gs.set-price-margin.

Fuel Dealer (its OWN tasks, no borrowed Logging tasks):
fd.fell-tree (90 min, forestry hook), fd.haul-log (60 min,
forestry), fd.split-fuel (30 min, fuel-handling), fd.stack-fuel
(15 min, fuel-handling), fd.dispense-fuel (5 min, fuel-handling),
fd.sell-fuel (4 min, customer-tending).

Life activities as tasks (AUTHORING ONLY): lf.eat-meal (30 min),
lf.drink (1 min floor placeholder), lf.sleep (540 min). Durations
follow EXISTING code (planner meals 30 min, sleep 22:00-07:00 = 9
hours), NOT Kennedy's example guesses (15-minute meals, 6-8-hour
sleep), which remain open decisions. Registering these definitions
does NOT replace day-boundary meal consumption (DailyNeedsService)
or planner meal/sleep reservations; a production driver that issues
life tasks, and the decision about which system serves meals, are
Codex/driver work.

## 4. Demand-driven chains (sequences, not drivers)

Pass2DemandChains states, in order, which tasks a demand event must
produce and which trigger starts each step. No driver is wired here;
a driver that creates tasks without these triggers would be
inventing demand, and must not do so.

- Hotel: deal with customer (arrival event) -> escort to room
  (check-in done) -> checkout (next morning, night complete) ->
  refurbish room (checkout done, room dirty). Opening-day idle is
  the Idle schedule activity, not a task.
- Saloon: opening cleaning (routine: saloon opens, area dirty) ->
  take order (customer ready) -> pour drink (order taken) ->
  serve at bar (bar customer, no walk) / serve at table (table
  customer, walk). Bar and table are different steps. Glass and
  container granularity is explicitly NOT modelled.
- Bank: serve customer (customer arrives, window open) ->
  balance books (day-boundary routine: window closes, transactions
  exist to balance).

Routine-trigger gaps (states that must exist before the routine
task may be created): store dirtiness, saloon dirtiness, room
dirtiness beyond the linen queue, bank window-open-with-customer.
Until a state exists, its task stays unwired.

## 5. Bakery: one model, transfer closed (F16)

- BakeryShopRuntime.ReceiveTransferredBreadLot(staged, dayIndex,
  idRegistry, diag): converts a staged lot into a sellable shop
  lot, records the staged id in TransferredStagedLotIds (persisted
  in the save DTO as an additive field; old saves load empty), and
  refuses duplicates, unknown products, and non-positive units,
  moving nothing on refusal.
- Aging is preserved from the BAKED day and aged to the TRANSFER
  day: a loaf baked yesterday is day-old when transferred today,
  never fresh again. That choice is the single place to change if
  Kennedy wants transfer-day aging instead.
- Pass2BakeryStagedTransfer.Transfer(chain, shop, stagedLotId,
  dayIndex, registry, diag): finds the staged lot, converts it,
  and ONLY on success removes it from the staged shelf. Removal IS
  the delivered mark. Stale staged lots (older than one day past
  baked day) are refused, not converted and not silently dropped.
- The staged chain's own comment about "no transfer" is now
  outdated for the transfer itself (the bridge exists); the
  remaining staged-chain gaps are driver wiring and scene ownership,
  listed in section 10.

## 6. Fuel Dealer: real dispense API

FuelYardByproductIntake.TryDispenseUnits(dealer, units, dayIndex,
diag) returns FIFO FuelYardDispenseLine records (lot id, units
taken, provenance chain). Shortfall dispenses NOTHING (never a
partial lot); emptied lots are removed, so a zero-unit lot is never
re-sold. This replaces the ThreeBusinessSlice in-place FIFO
deduction as the production model; the bridge itself is left
untouched for its orchestrator tests, and a production driver must
switch to this API (section 10).

## 7. Attended vs unattended

Existing model only: WaitingOnPassiveProcess releases the worker
while equipment, workspace, and materials stay reserved, resumed by
the due event. Pass2AttendedUnattended states the baker pattern
(load attended -> bake passive -> unload attended) for drivers.
No second passive-process system was invented, and no stage timing
was changed: stage timings remain the ProductionStageMachine /
staged-chain values, not Kennedy's rounded oven example, until a
driver wires a specific product's stages.

## 8. Crew, skills, and hiring pressure

- Crew: Pass2CrewDuration (section 1). No crew cap per job, no
  solo refusal, no quota.
- Skills: hook ids only (section 1). No new skill system, no
  experience retune.
- Hiring pressure: unchanged authority. LaborDemandAssessor
  remains the evidence model (unmet minutes, recurring balks,
  queue pressure), never a quota, and idle minutes are excluded
  from it by Pass2IdleActivity. No staffing number, slot count, or
  1/2-style default was added anywhere in this pass.

## 9. Unification: what is unified, what remains parallel

UNIFIED in this pass: fuel dispense (real API, section 6), bakery
staged-to-sellable (single transfer bridge, section 5), slice
store/fuel work (single TaskAuthority catalogs, section 3).

REMAINS PARALLEL, explicitly, with reasons (not faked as unified):
- Daily labour pools (Bakery 840-minute pool, Mine minutes-to-tons):
  still exist; the task-sequence view (section 1) does not replace
  them until the budget-cap decision is made.
- Timer-driven freight (LogisticsRuntimeManager's own shipments)
  vs task-based FreightShipmentService: NOT unified in this pass.
  Which shipment set survives is a Codex/driver decision that needs
  the production driver to exist first; unifying by deleting one
  set here, without that driver, would strand live shipments.
  Freight tasks already exist in FreightShipmentService (load,
  dispatch, unload); the remaining work is driver binding, not a
  new freight task system.
- Day-planner reservations (meals, sleep) vs TaskAuthority life
  tasks: both exist (section 3). The driver decision about which
  serves meals/sleep is open and documented, not silently resolved.
- Daily aggregate sales / nightly settlement with caller labour
  minutes: still exist alongside lot-based shopping. Whether
  aggregate sales ever become household food is a playtest
  question from the bread/fuel audits, unchanged by this pass.

## 10. Codex / Unity AI only (not done here, not claimed)

- Production drivers: bakery WorkDay driver, flour-order/arrival
  driver, store-to-bakery feed driver, fuel production driver,
  hotel/saloon/bank demand generators wired to real arrival events,
  life-task driver, freight driver binding (which shipment set
  survives), and a driver that switches fuel sales to
  TryDispenseUnits.
- Supplier adapters: BakerySupplierAdapter and
  FuelDealerSupplierAdapter in SupplierDirectory (households still
  cannot buy bread or fuel in production without them).
- Store staple-food single-item resolution (asset split or
  per-item trading port) — the multi-item staple_food block from
  the bread/fuel audits is unchanged by this pass.
- Scene wiring, prefab wiring, Inspector values, Main Scene
  drivers, on-screen UI and NPC messages for the missed-work
  report (the report API is in section 11; the UI is not).
- Unity compilation, Unity Test Runner, PlayMode: all pending.
  The two Unity-only test files below must be run there.

## 11. Missed-work reporting (console-first API)

Pass2MissedWorkReport: missed tasks, balked demand, and idle
minutes, rendered as plain console lines. Idle is reported, never
counted as missed work and never fed to hiring pressure.
Pass2TaskSequenceDay feeds it via AddFromSequenceDay. On-screen UI
and NPC messages read MissedItems through this API; they do not
exist in this pass.

## 12. Seconds: OPEN decision, and what is blocked on it

CORRECTION: WorkTimeBudget.cs previously called the one-minute
floor "Kennedy's rule". That was a code comment, NOT a Kennedy
decision. The comment is corrected in this pass; behaviour is
unchanged (the floor still books at whole minutes today).

Kennedy has NOT decided: small option (additive seconds durations
+ due-event completion, budget stays minutes with a stated
1-minute booking overcharge) vs full migration (seconds everywhere
including a 36,000 sec/day budget, planner, drivers, save
migration). Until that decision:

BLOCKED (listed in Pass2DemandChains.Pass2SecondsBlocklist, NOT
implemented, NOT worked around by booking at 1 minute):
saloon order-taking at example durations, saloon serve at bar /
at table as sub-minute tasks, any task whose real duration is
under one minute, and a seconds-native budget/planner/drivers/
save migration.

What Pass 2 did instead: all seconds-agnostic decomposition,
demand sequences, idle, crew, skill hooks, unification work, and
missed-work reporting above.

## 13. Tests and migration (section 12 in the brief's numbering)

Roslyn harness (small, direct DLL run, NOT Unity):
- File: `Tests/Editor/Economy/Pass2TaskSystemTests.cs`
  (5 tests, all passing at the end of this pass).
- Runner: small Roslyn harness
  (`roslyn-harness/harness-pass2.csproj`: NUnit shim +
  UnityEngine shim + a minimal Pass2 Persistence shim for the two
  save DTOs the compiled subset uses + the TaskAuthority /
  Skills / Time subset + the Pass 2 files + the test file only).
  The full-repo Roslyn harness was NOT used for this pass.
- Result: 5 passed, 0 failed. One failure was found and fixed
  during the pass, honestly: a test-arithmetic error in the
  crew example (90 / 3.1 = 29.1, ceiling 30, not 29) — the code
  was right, the test and its comment were wrong, and both were
  corrected.
- Coverage: idle policy is NOT in the Roslyn set (see below);
  catalog registration and skill hooks, crew scaling, fuel
  dispense FIFO/shortfall/emptied-lot behaviour, demand chain
  order and seconds blocklist, and sequence-day sums with
  missed-work rendering are.

Unity-only tests (AUTHORED, NOT RUN, NOT PASSED):
- File: `Tests/Editor/Economy/Pass2BakeryTransferUnityTests.cs`
  (2 tests). BakeryShopRuntime / staged chain drag the full
  Generic / Equipment / BusinessDefinitions closure, which the
  small Roslyn harness does not compile. They must be run in
  Unity Test Runner (EditMode) by Codex / Unity AI.
- File: `Tests/Editor/Economy/Pass2IdleUnityTests.cs`
  (1 test). PersonActivityKind lives in PersonScheduleTracker.cs,
  whose full file drags the population state closure. Same
  Unity-only status.
- Until those runs happen, report them as authored, not passed.

Parse check (NOT a compile claim): the edited Bakery, Sawmill,
Population, and Time files were run through the Roslyn compiler
in a bare invocation; the only CS1xxx-class error was CS1110 for
a pre-existing extension method, caused by the bare invocation
missing a framework reference — not a syntax error in any Pass 2
edit. That is a parse sanity check only; it does not replace
Unity compilation.

Earlier tests: no earlier test was re-run in this pass, because
the small harness compiles a subset only. In particular, the
pre-existing Phase H Roslyn failures and the
ConstructionInputSupportTests Unity timeout are unchanged and
unrelated to this pass.

## 14. Queued, NOT done here (for the parent's pipeline)

- Fuel-dealer off-map input purchase option (generic "input" ->
  fuel output; firewood is a guess, not decided) and housing
  heating alternatives investigation: queued for a later
  economy-procurement pass. Nothing in this pass implements,
  hardcodes, or forecloses either one.
- Economy ordering UI, player depth, town growth / immigration:
  later passes, in Kennedy's order. Nothing in this pass starts
  them.
- Kennedy decisions still open (from this pass and the audits):
  seconds option, idle behaviour, meal duration (30 min existing
  vs 15 min example), sleep duration (9 hours existing vs 6-8
  hours guess), work-time budget cap vs pure task sequencing,
  bakery transfer-day vs baked-day aging (Pass 2 chose baked-day
  aging; Kennedy may overrule), store category shape, freight
  shipment-set survival, driver binding for every chain above,
  and all prices/rates/weights/frequencies (test parameters only).
