# Pass 4 — Player Depth: Review and Handoff

Pass 4 of the new series. Base: `muse/pass3-economy-ordering` at
`3fd39d7dc1ec59634a2828c0920441332bdc4d72` (Pass 3, economy
ordering, verified pushed, 13 files, all additions, no duplicate
GUIDs — parent-verified before this pass started). Branch for this
pass: `muse/pass4-player-depth` (SHA and push confirmation are in
the pass report, not in this file, because they change at push time).

Governing docs: Documentation v3.0, Canonical Project Bible Revision
XXXI and Technical Implementation Bible Revision XIV, both dated
2026-10-07. Proposals 001-105 are approved; 106-145 are pending and
were NOT promoted here.

## 0. What this pass is, and what it is not

Pass 4 ONLY. It does NOT start town growth / immigration (Kennedy's
order is task, then economy, then player, then town; town is the next
and last content pass). It does NOT implement housing heating
alternatives: the heating audit is now complete, but Kennedy has not
decided methods, coal unit weight/heat, Dealer vs Mine retail, or
device drivers, so heating implementation stays blocked on those
decisions and is noted here only as pending, never guessed.

It does NOT claim Unity compilation, Unity Test Runner, PlayMode,
scene wiring, prefab wiring, or Inspector values: no Unity run
happened in this pass, and none is claimed. Everything verified here
was verified in a small Roslyn harness (section 11). Unity AI owns
first-compilation busy work. Codex owns hard problem solving and
final integration. Neither prompt is written here; ChatGPT (manager)
writes those from the parent's summary of this pass.

No existing file was edited in this pass. All changes are new files
(3 model files, 1 test file, this doc, plus their .meta files).
PlayerDirector, SimulationDrivers, TaskAuthority, WorkTimeBudget,
Pass 2 catalogs, and Pass 3 ordering were read, not rewritten, so
there is no regression surface in production wiring from this pass
— and equally, no production wiring is claimed: the new model is a
source-level spec for Codex to wire behind PlayerDirector and
SimulationDrivers, not a driver itself.

## 1. Player duty toggle

`Orchestration/Player/Pass4PlayerDuty.cs` — `Pass4PlayerDuty`.

- The player does the SAME tasks NPCs do: the roster is a Pass 2
  task-sequence day (`Pass2TaskSequenceDay`), built from the same
  catalog ids (for example `gs.order-from-supplier`,
  `fd.fell-tree`, `lf.eat-meal`, `lf.sleep`). No second player
  task system exists, and no task definition is authored here.
- The toggle means "doing all daily duties, no micromanagement":
  when duty is effective, the day's duties are the player's roster
  and he works them in sequence until done or unable to be done.
  Adding a duplicate open task, a task with no id, or a task with
  non-positive minutes is refused, moving nothing.
- NPC-mode interaction (see also section 5): duty is effective
  only when the toggle is requested AND `TreatPlayerAsNpc` is false.
  Turning NPC mode on suspends effective duty and preserves the
  roster; the toggle itself is not cleared, so turning NPC mode off
  makes a still-requested toggle effective again, and the console
  status always shows effective duty, so resumption is visible,
  never silent. A player who does not want duty work after NPC mode
  must toggle duty off deliberately. This is a deliberate choice,
  recorded here, not an oversight: clearing the toggle on NPC-mode
  entry would silently discard a player's stated intent, and
  auto-resuming without showing it would be the worse surprise.
- Player travel follows PlayerDirector's rule, modelled here in
  plain C#: a route must exist (production: JourneyModel.FindRoute),
  the player must not be busy, and NPC mode must be off. No route
  means no travel and no location change — never a teleport.
  AdvanceTravel is gated by NPC mode (no progress, no arrival).
  Cancelling travel leaves the player where he is, not teleported
  home; cancelling a task returns it to the roster unfinished.

## 2. Hiring offload, from unfinished-task evidence only

`Pass4PlayerDuty.OffloadTask(taskId, employeeId, evidence, diag)`.

- Offload moves ONE unfinished, player-owned duty task to ONE
  employee, and removes it from the player roster entirely, so it is
  never double-counted as both player work and employee work.
  Offloaded tasks are excluded from the missed-work report: they
  were reassigned, not lost.
- Evidence is per-task and unfinished-task based: the task must be
  on the roster and unfinished, an employee id must be supplied,
  and the caller must name the evidence (for example "missed
  yesterday" or "backlog 90 minutes"). A completed task, a task
  that is not player-owned, an empty employee id, or an empty
  evidence string is refused.
- There is deliberately NO headcount argument, NO quota, and NO
  "hire N people" output anywhere in this pass. Offloading one task
  says nothing about how many employees a business needs in total;
  that question remains LaborDemandAssessor's evidence (unmet
  minutes, recurring balks, queue pressure), never a slot count.
- Offload is refused while NPC mode is on, because player decisions
  are gated in that mode and the roster is preserved.

## 3. Work cap: which model this pass uses, and why

Pass 2 left two models coexisting: the task-sequence day (a planning
view) and WorkTimeBudget (default 600 minutes, a tuning value, not
canon, and not "Kennedy's rule" — Pass 2 corrected that comment to
a code convention, behaviour unchanged).

This pass uses BOTH, in distinct roles, and documents the split so
a driver cannot silently pick one and lie about the other:

- The DUTY ROSTER is the task-sequence day: what the player should
  do today, in order, with no fixed 10-hour block imposed by the
  roster itself.
- The BOOKING CONSTRAINT is the caller-supplied daily budget
  (production: WorkTimeBudget, default 600). The model takes the
  budget as a constructor number, reports planned minutes against
  it (`OverBudgetMinutes()`), and NEVER truncates the roster to fit
  it. A task the roster cannot book is a missed/offload candidate,
  not a task that never existed.
- The 600-minute value is NOT hardcoded as canon in the new code:
  tests pass 600 explicitly as a test parameter, with that provenance
  in the test comment. Whether the cap survives, is raised, or is
  replaced by pure task sequencing is STILL a Kennedy decision, and
  a production driver must not delete the cap silently, and must not
  treat it as canon silently either. Until Kennedy decides, a driver
  that enforces only the sequence while the budget still caps, or
  only the budget while the sequence still plans, is misrepresenting
  one of them — the console status therefore prints both planned
  minutes and over-budget minutes every time.

## 4. Auto-eat and auto-sleep (Project Zomboid-style toggles)

`Orchestration/Player/Pass4AutoCare.cs` — `Pass4AutoEat`,
`Pass4AutoSleep`, plus provider interfaces `IPass4FoodSeller`,
`IPass4Lodging`, `IPass4RouteProvider`.

Modelled as explicit step machines, leg by leg, because Kennedy
flagged this as a complicated system:

- Auto-eat: toggle on → start (need reason recorded) → find a
  seller (supplied by the shopping system; null means failure, not
  a conjured shop) → travel (route required; no route means no
  travel, location unchanged) → at seller (must be open, must have
  food, must be payable) → eat 30 minutes → done.
- Auto-sleep: toggle on → start → find lodging (must be supplied
  AND available; unavailable lodging fails BEFORE travel, so the
  player does not walk to a closed door) → travel (route required)
  → at lodging (must be payable) → sleep 540 minutes → done.
  The "middle of nowhere with a boarding house" case is just a
  lodging location supplied by the provider; nothing special-cases
  it, and nothing teleports the player to it without a route.
- Payment: cash first, then offered trade credit within its limit,
  otherwise failure. No debt is invented, no free meal or night is
  granted, and nothing is charged on failure. CashCents here is a
  caller-supplied MODEL GATE for tests: production payment goes
  through HouseholdLedger, credit only through
  FinancialObligationAuthority, and a Codex driver must replace the
  gate with the real ledger check before this drives a live game.
  That replacement is a Codex item, listed in section 10.
- Failure leaves a KNOWN state: a seller/lodging failure before
  travel leaves the player at his origin; a payment failure after
  travel leaves him at the seller/lodging, not teleported home.
  Toggling auto-eat or auto-sleep off mid-flow cancels the flow and
  leaves the player where he is.
- Durations follow EXISTING code (eat 30, sleep 540), NOT Kennedy's
  example guesses (15-minute meals, 6-8-hour sleep), which remain
  open decisions. Changing them here without a decision would be
  canonising a guess.
- Travel honesty, stated plainly: the model step is a travel LEG
  (route found → location changes, route minutes recorded).
  Minute-by-minute advancement is the production driver's job
  (PlayerDirector.RecordMovementProgress). The model does not claim
  to simulate each minute, and it does not teleport: without a
  route, the leg is refused and the location does not change.
- Provider wiring is Codex work: IPass4RouteProvider is implemented
  by JourneyModel.FindRoute, IPass4FoodSeller by the shopping
  system, IPass4Lodging by the boarding/lodging system. Until those
  adapters exist, auto-care cannot run in production — and that is
  correct, because running it on invented sellers would be faking
  the complicated system Kennedy asked to be modelled carefully.

## 5. The Pass 1 stall, addressed, not hidden

A task issued while NPC mode is on stalls InProgress, because
autonomous drivers do not convert player tasks into NPC work (see
PlayerNpcPlaytestPolicy). `Pass4PlayerDuty.ResolveStalledTask()`
returns that reason and exactly one honest recommendation: cancel
the player task, or turn NPC mode off and re-issue it deliberately.
This model NEVER auto-converts a stalled player task to NPC work.
Auto-conversion would silently change ownership, break person-time
exclusivity accounting, and hide the stall from the player.

## 6. Missed-work reporting: one source of truth, plus messages

`Orchestration/Player/Pass4PlayerMessages.cs` — built ONLY from
Pass 2's `Pass2MissedWorkReport`. No second missed-work system is
invented.

- `BuildFromReport()` turns each missed or balked item into a
  plain-language message ("could not get this done: <task> —
  <reason>"), and turns recorded idle minutes into a standing-by
  line, never a missed-work line (Pass 2 rule: idle is standing by,
  not failure).
- `BuildOffloadMessage()` tells the player a task was offloaded and
  is no longer his task.
- `BuildAutoCareFailedMessage()` tells the player auto-care failed,
  why, and where he actually is.
- `RenderConsoleLines()` works today, console-first, as Kennedy
  asked ("rely on console a lot, tracking a lot"). On-screen UI
  and NPC message display are Codex/Unity work READING these
  records; no UI is claimed here, and no message is invented that
  is not in a Pass 2 report.

## 7. Player ordering guard, unchanged

`Pass4PlayerDuty.ValidateOrdering()` delegates to
`Pass3OrderingSession.ValidateAutomatic()` and nothing else. A
player automatic order is refused; an employee automatic order
needs delegation permission; a deliberate player order passes.
Pass 4 does not weaken, widen, or re-interpret the Pass 3 guard.

## 8. Seconds: clarification applied, blocklist kept, small option NOT implemented

Parent update, applied in full, and recorded here as a conflict
check (no silent scope change):

- Seconds ARE representable in newer layers: DurationContracts
  says game seconds are the precise internal unit, V2 estimators
  use GameSeconds, and clock/due events and passive production
  stages are seconds-native.
- Seconds are NOT yet usable end-to-end as tasks in TaskAuthority:
  TaskDefinition.baseMinutes is int, minimum 1; WorkTask minute
  fields and RecordWorkProgress take int minutes with a
  whole-minute clamp; drivers flooring ticks to whole minutes
  advance ZERO progress on sub-60-second ticks.
- Pass 2 correctly blocklisted sub-minute saloon work instead of
  working around it with 1-minute booking (which would overcharge
  12 five-second orders as 12 minutes). Pass 4 KEEPS that blocklist
  and authors NO sub-minute task, NO sub-minute auto-care leg, and
  NO seconds-native budget.
- The small seconds option (additive seconds durations + due-event
  completion, budget stays minutes with a stated 1-minute booking
  overcharge) was NOT implemented in this pass, by the brief's own
  terms: it is a separate, documented option, not a drive-by, and
  the full seconds migration (seconds everywhere, including a
  36,000 sec/day budget, planner, drivers, and save migration) is a
  separate Kennedy decision still open. Implementing either here
  without that decision would have been scope change. Sub-minute
  player work therefore remains BLOCKED, and any future pass that
  claims it must first implement and document one of the two
  options explicitly.

## 9. Heating: audit complete, implementation still blocked

The read-only heating audit is now complete (parent update). Its
findings, NOT re-audited here, are: heating today is fuel_wood
only; HouseholdFuelType has only Wood; no stove device exists;
slice households have no stove data; Mine coal_ton exists but is
never burned for household heat; bakery, boarding, hotel, and
restaurant fuels are separate lot universes.

Heating implementation remains BLOCKED on Kennedy decisions:
which methods to include, coal unit weight/heat, Dealer vs Mine
retail, and device drivers. Pass 4 implements NO heating leg, NO
stove check, NO fuel purchase in auto-care, and NO coal item. Any
pass that adds heating must first resolve those decisions and must
fix the wood purchase path first (Fuel Dealer supplier adapter,
dispense API, production driver), because every alternative
inherits that break.

## 10. Still missing / Codex / Unity AI only

- Production driver binding: duty roster → TaskAuthority issuance
  and advancement (SimulationDrivers/PlayerDirector wiring), life
  task driver (whether meal consumption stays day-boundary or
  becomes task-driven — Pass 2 left this open, Pass 4 does not
  decide it), auto-care provider adapters (JourneyModel route,
  shopping seller, boarding lodging), and replacement of the
  CashCents model gate with HouseholdLedger /
  FinancialObligationAuthority checks.
- On-screen UI and NPC message display reading
  Pass4PlayerMessages records; console rendering works from this
  pass, UI does not.
- Supplier adapters in SupplierDirectory (Bakery, Fuel Dealer),
  store staple-food single-item resolution, flour order/arrival
  driver, store-to-bakery feed driver, fuel production driver,
  and all scene/prefab/Inspector wiring — carried from Pass 2/3,
  not closed here.
- Unity compilation, Unity Test Runner (EditMode), PlayMode.
- Decisions still open (none guessed): seconds small vs full;
  meal duration (30 existing vs 15 example); sleep duration (540
  existing vs 6-8 h guess); WorkTimeBudget cap vs pure task
  sequencing; bakery transfer-day vs baked-day aging (Pass 2 chose
  baked-day); store category shape; freight shipment-set survival;
  heating decisions (section 9); all prices/rates/weights/
  frequencies (test parameters only).

## 11. Tests and migration (Pass 4 test list)

Roslyn harness (small harness, direct run, NOT Unity):
- File: `Tests/Editor/Orchestration/Pass4PlayerDepthTests.cs`.
- Runner: small Roslyn harness
  (`roslyn-harness/harness-pass4.csproj`: NUnit shim + Runner +
  Pass2MissedWorkReport, Pass2TaskSequenceDay, Pass2DemandChains,
  Pass3OrderingModel, the 3 new Pass 4 model files, and the test
  file only; .NET 8 SDK at
  `~/workspace/land_and_ledgers/dotnet/sdk/dotnet`).
- Result: 10 passed, 0 failed (final run). Two failures were found
  and fixed during the pass, honestly, and both were TEST errors,
  not model errors: (a) a day-end missed-count expectation that
  ignored unfinished roster tasks (a roster task that is neither
  completed nor missed at day end IS missed by sequence-day
  semantics — the test now completes the other tasks so the single
  missed task is isolated); (b) a wording assertion that searched
  for "auto-convert" inside a recommendation that says "Do not
  auto-convert" (the test now asserts the refusal wording and the
  absence of a promised auto-conversion).
- Unity-only tests: NONE authored deliberately. The model is plain
  C# with no UnityEngine dependency, so the Roslyn harness covers
  it fully; a Unity stub would add nothing. Driver and UI
  verification is Unity AI/Codex work, not a Roslyn stub, and must
  be reported as NOT RUN until it happens. Existing production
  wiring tests (PassXlPlayerAsNpcTests, PlayerDirectorTests) were
  NOT re-run here, because this pass edits no production file and
  the small harness compiles a subset only; they remain Codex
  Unity Test Runner work.
- Earlier tests were not re-run (small harness, subset only); the
  pre-existing Phase H Roslyn failures and the
  ConstructionInputSupportTests Unity timeout are unchanged and
  unrelated.
- Migration: additive only. No serialized type, field, enum value,
  or namespace was renamed; no existing file was edited; no save
  DTO was changed. A driver that adopts the duty/auto-care model
  must persist the toggle, roster, travel, and auto-care state
  itself — no persistence format is claimed here, and old saves
  are unaffected because no existing save field moved.

## 12. New files (each with a fresh .meta GUID)

- `Assets/Scripts/Orchestration/Player/Pass4PlayerDuty.cs`
- `Assets/Scripts/Orchestration/Player/Pass4AutoCare.cs`
- `Assets/Scripts/Orchestration/Player/Pass4PlayerMessages.cs`
- `Assets/Scripts/Tests/Editor/Orchestration/Pass4PlayerDepthTests.cs`
- `Assets/Scripts/Docs/Pass4PlayerDepthReview.md` (this file)
