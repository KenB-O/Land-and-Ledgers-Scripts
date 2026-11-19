# Overnight Development Pass 02 — Population and Task Lifecycle

Date: 2026-11-18 (local; latest run artifacts are timestamped November 19 UTC)
Unity: 6000.4.2f1
Branch: `codex/player-economic-participation-2026-11-18`
Starting HEAD: `bd4136423be2ca416087b770cb1ea4294a7a7af7`

## Findings

### Population discrepancy

The 60-day run `20261119T020752Z-d05cae9b` did not generate 98 settlement residents. It generated the configured 14 people, then `FamilyTreeWiring.ApplyAfterGeneration` added 84 explicitly off-map kin records to the same `PopulationState.people` collection. The 60-day metadata reported `expectedResidents=14`, `actualResidents=98`, and incorrectly passed its lower-bound startup test. `PersonState.isOffMap` and the bootstrap comments establish that these genealogy records are retained identities, not residents or migrants.

**Correction implemented:** `PopulationState.LivingResidentCount` counts living, on-map residents separately from all stored person identities. A fixed development roster now fails New Game startup if the count differs from the exact configured roster, before family-tree records are appended. Bootstrap and manager summaries distinguish residents from person records. The existing playtest startup gate uses the same census, requires an exact match for fixed rosters, and reports both counts. Variable generated-population configurations retain their variable population policy but may not pass with zero residents.

### H1 bread trip

The original saved run records H1's trip `trip-2-77` in `Outbound` with need 0 on daily diagnostics through at least absolute day 45; the same need is shown open since day 1. The exported artifacts do not contain daily trip state for days 46–60, so persistence through day 60 is not independently established from this export. The day-1 `Incident` entry alone was not proof of an active trip; later `[FoodEconomy]` summaries are the evidence that the specific trip remained active through day 45.

Existing `ShoppingTripDriver` code has a route watchdog, return-on-balk handling, and abort cleanup; `ShoppingTripDriverTests.NonProgressingAuthoritativeRouteEventuallyFailsAndCancelsWithoutArrival` covers a stationary/retreating route. The run evidence therefore indicates an integration/runtime discrepancy, but the precise H1 cause is not yet confirmed. One plausible mechanism requiring live measurement is simulation time advancing much faster than visible movement: `AgentMover` caps presentation movement to 1.5x while the campaign run used 250x. This is a testable hypothesis, not a proven root cause. The daily runner now records each active shopping trip's ID, household, shopper, state, need, and failure text so a controlled run can distinguish stuck physical movement from an unresolved need.

### Hidden movers and lifecycle coverage

`PopulationPathingDirector.InitializeIfNeeded` shows newly created agents. Production task routes shown in `TrySendAgent` and domestic routes use visible movement; production source search found no caller of `TryHidePerson` or `TryShowPersonAtCurrentLocation`. The 60-day raw log does not report an `operator is hidden` refusal. Hidden movers are therefore **not confirmed** as this run's cause; no global unhide change was made.

Domestic task recovery already exists in `DomesticLaborDriver.ReconcileStalled`: an active domestic task without an in-flight owner is interrupted/requeued without work credit and its schedule reservation is released, or cancelled if interruption is refused. Shopping's balk and route watchdog cleanup exist. Domestic driver state and shopping trips have existing save DTOs, and EditMode save tests cover active shopping trips. This pass did not establish these behaviors in fresh Unity PlayMode or a real Main Scene Save/Continue cycle.

## Changes

- Added a resident census and exact-count predicate to `PopulationState`.
- Changed `PopulationManager.GeneratedPersonCount` summary semantics to living on-map residents; added a separate total `PersonRecordCount` read.
- Added fixed-roster count validation to New Game bootstrap and clarified startup summary output.
- Corrected the playtest startup gate to reject an incorrect fixed-roster count instead of accepting any count above the expected value.
- Extended the existing runner's daily evidence with total person-record counts and active shopping-trip state. This is an edit to an owner-local modified runner file; only the new Pass 02 hunks are intended for this checkpoint.
- Added `PopulationStateResidentCensusTests` for the 14-resident/84-off-map-record case, deceased/null records, and mismatch detection.

## Verification and environment

- Source review used the controlling Canon XXXI and Technical XIV extracted originals, authority-precedence register, repository `AGENTS.md`, and the Pass 01 checkpoint. Canon distinguishes residents from broader service/population records and preserves physical task time; no rule supports counting off-map kin as town residents.
- Current Unity Editor is responsive, in Edit Mode, and not compiling/domain-reloading. Unity is 6000.4.2f1.
- System has 16 GB RAM; latest measurements show about 2.97 GB free physical RAM, 51.26 GB committed of a 52.53 GB commit limit, and about 1.27 GB commit headroom. The F drive has about 154 GB free. Prior Unity IL post-processing failed with Windows error 1455.
- No Unity compile, EditMode test, PlayMode test, or Save/Continue test was run in this pass. The limited commit headroom makes invoking the compiler/test pipeline unsafe. `git diff --check` passed for the changed source paths.
- The focused tests to run when commit headroom is restored are `PopulationStateResidentCensusTests`, `Playtest60DayRunnerTests`, `ShoppingTripDriverTests`, `DomesticLaborDriverTests`, `TaskDurationDiagnosticsTests`, `AgentMoverTests`, and `PopulationPathingProfessionRhythmTests`. Follow with a short Main Scene test that captures active trip state at 1x and 10x before considering accelerated runs.

## Remaining work

1. Run the census tests and verify the actual DevelopmentTownConfiguration New Game initializes exactly 14 living on-map residents while retaining off-map kin records.
2. In Main Scene, capture H1's shopper ID, route mode, mover visibility, destination, remaining route distance/ETA, active task owner/status, due events, and trip state at normal and accelerated speeds. Establish whether route progress is physically occurring before changing watchdog or speed behavior.
3. Verify domestic work, employment, shopping, delivery, and freight reservations across interruption and Save/Continue with authoritative task and reservation state. Do not infer physical execution from assignment logs.
4. Resolve the White Plaster animation asset-load failure before relying on Unity Test Runner PlayMode coverage; preserve the owner asset while identifying its supported serialization/provenance.

No Main Scene, prefab, ScriptableObject, terrain, font, or asset-pack files were changed by this pass. Owner-local changes were left untouched.
