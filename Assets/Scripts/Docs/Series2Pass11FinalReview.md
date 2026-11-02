# Series 2 Pass 11 — Final Review (review-and-fix pass, last in the series)

Branch: `muse/series2-pass11-final-review`, based only on Pass 10
(`muse/series2-pass10-harness`). PULL TARGET: this branch ONLY —
it contains Passes 1-11 chained, so pulling it pulls everything.
Roslyn harness only: no Unity compilation, no Test Runner, no
PlayMode, no scene wiring is claimed anywhere in this pass.
Unity verification is Codex/Unity AI work, listed below.

Kennedy's instruction for this pass: look over Passes 1-10,
make sure everything is good and ready for Unity, make any
changes needed. This is a review-and-fix pass, NOT a new-feature
pass. No new business, no new system, no new canon was added.

## Pass-by-pass check (what was checked, what was found fine)

- Pass 1 Generic Business: verified in files — `TryAddCapability`
  idempotent, templates grant no free cash/inventory/equipment/
  workers, horse and wagon are distinct owned assets, premises
  capacity and the acquisition model stand as documented. No
  fix needed.
- Pass 2 Task: verified — `PersonActivityKind.Idle = 14`,
  preemptible, budget-exempt, excluded from hiring pressure;
  life tasks at existing code durations (eat 30 min, sleep
  540 min); bakery staged transfer through
  `BakeryShopRuntime.ReceiveTransferredBreadLot`; missed-work
  report is the single reporting API. Freight is honestly NOT
  unified (see Pass 8 below). No fix needed.
- Pass 3 Economy Ordering: verified — ordering model with cost,
  price, markup percent AND total, shelf vs back pools;
  wholesale intake into store BACK only with baked-day aging;
  per-item offers (staple_food yields seven one-item offers).
  No fix needed.
- Pass 4 Player Depth: verified — duty effective only when
  requested AND TreatPlayerAsNpc is false; offload exactly one
  task to exactly one employee; the Pass 9 binding pays through
  HouseholdLedger/trade credit FIRST and only then syncs the
  model's CashCents as a mirror, with a cash refund path on
  model failure and a loudly diagnosed credit residual that
  pre-checks make unreachable in normal driving. The Pass 4
  test-only cash gate does NOT leak into production. No fix
  needed.
- Pass 5 Town Growth: verified — lead enrichment refuses to
  invent; employment id format `arrival-<person>-<business>-
  <day>`; economic-need reader has NO Headcount field; town
  driver is a MODEL, not a production driver; legacy roll
  honestly still live; conversation chain extends family-only
  recruitment without replacing it, and friend edges are
  refused without non-empty SharedHistoryEvidence — no friend
  graph is generated. No fix needed.
- Pass 6 Bread Drivers: verified — schedule-gated PlanBatch +
  WorkDay; loud pre-check refusals, failed pre-checks never
  call WorkDay; labour reported never truncated;
  WorkTimeBudget cap untouched (the driver reports over-budget
  only); flour order/arrival callable with caller-supplied
  counterparty; the named save-setter target is Codex's one
  line (`registration.Apply(s => saveLoadManager.
  PassXlProcurementService = s);`) and this pass, like Pass 6,
  never calls it — F19 is still open until Codex does;
  flour-unit conversion is an explicit caller parameter
  (LbPerStoreUnit) with a loud warning on description/number
  mismatch, never silently fixed or refused; one order path
  (legacy OffMapQuoteToOrder, validated quotes only; Pass 3
  drafts are the player-UI path only). No fix needed.
- Pass 7 Selling: verified — bakery adapter over the real
  BakeryShopRuntime shelf (household "bread" mapped only here
  to shop product "bake.bread-loaf"); fuel adapter over the
  real FuelYardByproductIntake (household "fuel_wood"); both
  IItemResolvingSupplier with RegisterIn; business cash only
  through a caller-supplied IBusinessCashPort; wholesale
  settlement (same-amount debit/credit, insufficient cash
  refuses with nothing moved, payee-credit failure refunds
  the payer, already-Settled refuses); shelf-move back to
  shelf returning `gs.move-back-to-shelf`, 2 min/lot, for
  Codex to issue via TaskAuthority, with the retail-price
  guard. Two caveats CONFIRMED REAL and left stated, not
  fixed: (1) the shared pool — BusinessRuntimeState holds
  one aggregate count per category, not per item, so per-item
  offers share a pool and total units sold can never exceed
  the pool; item-level stock is a save-shape migration
  decision, NOT a Pass 11 silent fix. (2) The retail adapter
  WARNING-only cash pattern (sale stands, WARNING logged, if
  a cash credit fails) is still only defensive dead code
  with the Pass 9 production-port infallibility proof
  (Credit for a non-negative amount on a non-null runtime
  cannot fail); it was NOT silently rewritten. Bakery
  DayIndex must be set at EVERY day start — that
  requirement is stated in the Pass 7, Pass 9, and Pass 10
  review docs. No fix needed.
- Pass 8 Freight/Fuel: verified — dealer-owned fuel callable
  over the real FuelYardByproductIntake (provenance, FIFO
  dispense via TryDispenseUnits, one bakery lot per dispense
  line, custody only, no cash settled here; sawmill receipt
  pays the sawmill nothing and diagnoses that plainly — the
  byproduct price is an open Kennedy decision, NOT a zero
  price); dealer-to-bakery fuel source implementing Pass 6's
  interface without editing Pass 6 files (dealer stock first,
  optional caller-supplied off-map callback, uncovered
  requests refuse, conversion caller-parameterised with
  remainder-forwarding, dealer-only shortfall refuses before
  dispensing); partial-supply nuance verified and left
  stated — with an off-map callback wired, the dealer
  portion moves into the bakery store BEFORE the callback
  can refuse, so a refused supply can leave partial fuel in
  the store (not lost, usable for the next bake); the
  production driver instruction to run the Pass 6 pre-check
  first is stated in the Pass 8 and Pass 9 review docs and
  was NOT silently reordered here; carrier contract over the
  real FreightProductionDriver (a FreightBusiness quote or
  order request with no registered carrier is REFUSED, never
  silently SellerFreight); slice legs as caller-parameterised
  callables moving shipment state, not item stock (stock
  moves only in the caller-wired ReceiveShipment callback,
  wholesale intake into BACK only); delivery-acceptance
  settlement only on Completed + deliveryApplied, Delayed/
  Partial settle nothing and keep cargo in wagon custody,
  atomic across IBusinessCashPort ports with refund on
  credit failure, cross-path guard both ways via
  freightRevenueCollected, HouseholdLedger never used for
  freight charges; household fuel reserve link (bought fuel
  to fuel_wood reserve, bundle-to-reserve conversion a caller
  parameter, never canonising Pass 7's 1=1 default; it buys,
  dispenses, and pays nothing — it only mirrors delivered
  quantity); surviving shipment set — detailed
  (FreightShipmentService + FreightProductionDriver) survives
  for the slice, timer-driven LogisticsRuntimeManager is the
  non-slice path, not edited, not deleted, never run on a
  slice leg; cross-system double-booking is NOT prevented by
  code, so detailed-only for slice legs is a Codex wiring
  rule, stated, not a code guard; tick requirement stated
  (no TaskAuthority + budgets in hooks = 0 shipments
  advanced, with a diagnostic, never a timer-only advance).
  Household-leg freight payer stays a caller parameter —
  dealer-borne and priced-into-retail are both UNDECIDED and
  neither is asserted anywhere. No fix needed.
- Pass 9 Player/Save: verified — production cash port in a
  Domains-layer path over BusinessRuntimeState, no driver
  referencing the orchestrator class; infallibility proof
  holds (AddCashCents clamps, no failure path; its weekly
  cash tracking mutation is intended bookkeeping, not a
  failure path); save adoption in one additive series2Pass9
  section with coordinator Capture/Restore overloads and
  SaveLoadManager.Series2Pass9State following the
  procurement pattern, covering Pass 2 sequence/report,
  Pass 3 drafts/orders/fuel plans/wholesale intake
  (delivered-lot set adopted, reload cannot re-intake) and
  settlements (Settled preserved), Pass 4 duty + auto-care,
  Pass 6 planned day/batch ids, Pass 8 fuel intake lots via
  the EXISTING DTO (no new DTO invented), fuel-source
  parameters only (delegate not serialized), carrier
  settlement via the persisted freightRevenueCollected flag
  (no second settled set; flagged shipments refuse
  re-collection, unflagged settle exactly once); adapter
  DayIndex deliberately NOT restored, bakery plan day
  restored and re-validated; all restores clear-then-load
  and idempotent; player binding as pure C# callables
  (unknown definitions refused; NPC mode refuses with roster
  preserved; over-budget reported, never truncated; offload
  exactly one task to exactly one employee with no
  double-counting; re-issue adopts an already-open task
  instead of duplicating; auto-care pays through
  HouseholdLedger — cash, then trade credit via
  ITradeCreditPort only, no debt invented, no free meal/
  night, known failure states, cash refund path on model
  failure; missed-work reports via the Pass 2 API only).
  SaveLoadManager restore passes null freight hooks as a
  documented default — shipments, cargo, deliveryApplied,
  freightRevenueCollected round-trip in the DTO, reservation
  rebinding is skipped, task bindings re-resolve by
  definition id on the next tick — this pass does NOT claim
  the null-hooks path rebinds reservations. Driver re-supply
  obligations after load (roster re-issue, seller/lodging
  re-supply, fuel-source rewiring) are stated, never assumed
  away. Restore drops are documented behaviour (saved
  current task not an unfinished in-progress roster task
  dropped; saved travel dropped when a task is current;
  player stays at the saved location; out-of-range auto-care
  restores as Inactive with a diagnostic) — tested as
  behaviour, never "fixed" into teleports or phantom tasks.
  No fix needed, except the Pass 9 review-doc claim corrected
  below.
- Pass 10 Harness: verified — town gates decision-gated only
  (defaults live: arrivals suppressed in slice, departure
  gate Undecided, legacy roll NOT retired, Validate refuses
  retirement without a departure decision; legacy departures
  stay LegacyUntracked, never rewritten; new channel refuses
  untracked destinations; settlement refuses with no housing/
  terms/plan flag); Pass 5 save adoption in ONE additive
  series2Pass10 field (Pass 9 section untouched) —
  Pass5LeadSaveRecord, Pass5TravelerVisitRecord,
  caller-supplied-only Pass5ChainRecord, clear-then-load,
  idempotent with double-restore assertions, old saves empty
  with diagnostic; snapshot — no Pass 5 evaluation save
  record existed, so Pass 10's shape-only
  Series2Pass10TownSnapshotDto (caller values verbatim, null
  = no snapshot, never a placeholder score) stays shape-only;
  Pass 3 double-restore assertion verified present in the
  Pass 10 tests (drafts, placed requests, settlements,
  delivered set restore twice without doubling; re-intake
  still refused after the second restore); harness on
  production driver classes only (never orchestrators);
  bread chain, fuel chain, all five freight legs, mid-run
  Pass 9 + Pass 10 double restore, DayIndex rules (bakery
  PlannedDayIndex verbatim, adapter day NOT restored, early
  refusal, late diagnosed catch-up, pre-checks), missed work
  via Pass 2 API with console rendering (idle = standing by,
  never pressure), Shane-shaped buying positive controls
  through shortage monitor → loop → adapter only and both
  negative controls (no cash, no adapter → need stays Open,
  no debt, no free stock), Shane-shaped hiring positive
  control (sustained evidence over a caller threshold across
  multiple days → effort → HireFromInquiry → employment)
  and negative controls (single missed task, below-threshold
  day, all-done → no effort, no quota anywhere),
  9-person/5-household shape test (21 tests in the Pass 10
  file, all passing in the Roslyn harness). Phase H
  regression re-confirmed in this pass (see Verification).
  One real gap found and fixed below (SaveLoadManager had no
  Pass 10 state).

## Fixes made in this pass

1. SaveLoadManager now carries the Pass 10 section (the one
   real cross-pass wiring gap found). Pass 10 shipped the
   Pass 10 coordinator overloads but no SaveLoadManager
   property, so the production save path could never carry
   the Pass 10 section — only direct coordinator calls
   could. Pass 11 adds `Series2Pass10State` to
   `Infrastructure/Persistence/SaveLoadManager.cs`, following
   the Series2Pass9State pattern exactly, and wires it into
   both production call sites (the 5-argument Capture
   overload and the 7-argument Restore overload). Null-safe:
   a null state captures a null section and restores as
   empty with a diagnostic, exactly like Pass 9. What
   remains Codex work is ASSIGNING the live state before
   the first save/load, plus Unity compilation of the edit
   (SaveLoadManager uses Unity APIs and is not covered by
   the Roslyn harness, which covers the coordinator
   overloads). The combined coordinator path (both sections
   through one Capture/Restore, restored twice without
   doubling) was already tested in Pass 10.
2. Pass 9 review-doc claim corrected. The Pass 9 review said
   "every round-trip test restores a second time." That was
   overstated: the Pass 9 Pass 3 round-trip restored only
   once, while Pass 2/4/6/8 restored twice. The sentence in
   `Series2Pass9PlayerSaveReview.md` now says so, and notes
   that Pass 10 added the Pass 3 double-restore assertion,
   so the "restores twice" claim now holds for Pass 2/3/4/
   6/8. Do not repeat the old wording.
3. Pass 10 review-doc Codex item amended (two places: the
   save-adoption section and the Codex list). Both
   previously said SaveLoadManager wiring for the Pass 10
   state was not in Pass 10 and was Codex work. The property
   and call-site wiring now exist (fix 1 above); assigning
   the live state and Unity compilation remain Codex work.
   The amendments are marked "PASS 11 AMENDMENT" in place
   so the history stays readable.
4. New Roslyn tests (2) in
   `Tests/Editor/Economy/Series2Pass11FinalReviewTests.cs`
   (fresh GUID in its .meta, never copied) proving the
   contract fix 1 relies on: a null Pass 10 state captures
   a null section while the Pass 9 section still rides, and
   a present Pass 10 section with no state supplied is
   diagnosed, never silently dropped; an old save with no
   sections restores empty without inventing state, with a
   null snapshot (never a placeholder score).

## Verification in this pass

- Roslyn harness (workspace `roslyn-harness/harness-pass11.
  csproj`, not part of the repo): build 0 errors; the full
  Series 2 set passes 74/74 (Pass 6: 8, Pass 7: 16, Pass 8:
  12, Pass 9: 15, Pass 10: 21, Pass 11: 2).
- Phase H regression re-confirmed, not fixed, not claimed
  fixed: `HouseholdVerticalSlicePhaseHTests` runs 12 passed,
  3 failed with the same three failures and messages as
  baseline — `EndToEndTrace_InsufficientFunds_FailsThenRecovers`
  (Expected true), `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`
  (Expected string containing 'unaffordable', got ''),
  `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`
  (Expected 60, got 59). The `ConstructionInputSupportTests`
  1,800-second Unity timeout remains a Codex diagnosis item.
- GUID check, actually run this time via `git grep` over the
  tracked tree: all 31 new Pass 6-10 `.meta` GUIDs appear
  exactly once each. The Pass 11 test file's GUID is fresh
  and distinct from all of them. Unity import remains the
  final GUID check (a full working-tree scan stalls on the
  ~213 MiB repo, as in prior audits).
- No `.asset`, bootstrapper, Main Scene, prefab, or Inspector
  change in this pass. No SixPersonDevelopmentFixture or
  `.asset` change. No canon, price, rate, weight, frequency,
  duration, cap, or semantics change. Serialized enums/
  classes/fields append-only; the only serialized change in
  the whole Series 2 chain remains the additive save-DTO
  fields (series2Pass9, series2Pass10).

## Deliberately NOT built / left open (do not treat as done)

- The NAMED expanded fixture (Ethan B., Tia B., Shane Bryan)
  is BLOCKED, not built. No full surnames, ages, business
  name, houses, horses/wagons, or Shane cash amount have
  arrived, and inventing any of them would violate the
  no-fake-persons rule. The harness runs on expanded-shape
  test inputs only, clearly labelled as shape tests, never
  named Ethan/Tia/Shane and never presented as the slice
  fixture. Inputs still required before ANY fixture
  authoring: Ethan full surname (confirm whether "B." is
  Boniecki — a second Boniecki household must still be a
  distinct household from Jacob's, never merged), age,
  household name, freight business name, confirmation the
  business maps to the existing LiveryFreight profile or a
  new name, home building and freight yard/depot building
  (ids or descriptions), horses count, wagons/carts count,
  starting cash, starting stock if any, whether he starts
  with wagons/horses or must buy them, freight charge/
  pricing model (or confirm it stays a caller parameter);
  Tia full name, age, role (works in the freight company,
  stays home, or another job — do not assume the stay-home
  pattern), relationship to Ethan (do not assume partner/
  wife); Shane age, extra house building (id or
  description — a real building, not a placeholder) and
  tenure, exact starting cash amount, anything besides cash
  (tools, stock, or nothing — do not invent), job-seeking
  details if any (role preference or any work — do not
  assume); general slice inputs: seed for the new
  households/businesses (profile cash/stock vs
  caller-supplied owner equity vs trade credit + household
  credit — profile values are test parameters, not canon),
  which freight legs run first, who pays freight on each
  leg (including the household leg).
- No legacy departure destinations rewritten; no friend
  graph; no bootstrapper/Main Scene/prefab/Inspector values;
  no item-level stock migration; no seconds migration; no
  WorkTimeBudget change; no heating/coal/stove work; no
  price/rate/weight/frequency/duration fixed as canon.
  Proposals 106-145 are NOT approved.

## Pull-readiness checklist

### What Unity AI should do FIRST (busy-work only, never hard problem solving)

1. Pull this branch ONLY (`muse/series2-pass11-final-review`)
   into the local project; do not pull any other Series 2
   branch — this one contains Passes 1-11 chained.
2. Open the project and check the console on first
   compilation. Fix busy-work only: namespace issues,
   using-aliases, random Unity import issues, and the like.
3. Report every error found, every error fixed, and every
   error left for Codex, with file and line. Never invent a
   driver or adapter. Never change canon or treat a test
   parameter as canon. Never delete a legacy constant that
   legacy callers still read. Never claim PlayMode success.
4. Asset work is import verification only: confirm the new
   files import with unique GUIDs (see the GUID check
   above); do not delete, rename, or rewrite any `.asset`.
5. Compile `SaveLoadManager.cs` including the Pass 9 and
   Pass 11 edits — neither is Roslyn-proven Unity behaviour.

### What Codex should do AFTER Unity AI (hard integration)

1. Production driver hook-up: call Pass 6 PlanBatches →
   RunScheduledDay from the production day driver with the
   Pass 8 fuel source (not the Pass 6 in-stock source); run
   the Pass 6 pre-check before Pass 8 fuel supply in
   production (partial-supply nuance) or explicitly accept
   partial-store state.
2. Save registration, two one-liners: (a) set
   `SaveLoadManager.PassXlProcurementService` before the
   first save (`registration.Apply(s => saveLoadManager.
   PassXlProcurementService = s);` — F19, still open until
   this is done); (b) set `SaveLoadManager.Series2Pass9State`
   and the NEW `SaveLoadManager.Series2Pass10State` to the
   live holders before the first save AND load, filling the
   Pass 9 holder's fields with the live Pass 2/3/4/6/8
   instances and the Pass 10 holder with the live Pass 5
   instances. Verify the Pass 9 and Pass 11 SaveLoadManager
   edits in Unity.
3. After every load: re-issue restored roster tasks through
   the Pass 9 binding (the roster alone has no authority
   tasks); re-supply sellers/lodging for resumed auto-care;
   rewire the dealer fuel source from
   `RestoredFuelSourceParams` (including the off-map
   callback when its flag says one was wired); pass
   fully-wired freight hooks through `State.Restore` when
   available (the null-hooks path is the documented default
   and does NOT rebind reservations).
4. Supplier adapters and cash port: register the Pass 7
   bakery + fuel adapters in SupplierDirectory from the
   bootstrapper with the Pass 9 production IBusinessCashPort
   (Domains path — do not reference the orchestrator port
   class) and real trading checks; set the bakery adapter
   DayIndex at EVERY day start (a fresh adapter refuses
   trading until set; the adapter day is deliberately NOT
   restored); wire wholesale intake → settle → retail-price
   decision → back-to-shelf task (issued through
   TaskAuthority) → shelf sale. Confirm the Pass 7 adapter
   WARNING-only cash pattern is acceptable with the
   production port (Credit cannot fail for a non-negative
   amount on a non-null runtime), or add a pre-check/
   reversal — do not leave it as an unexamined assumption.
5. Freight: register the freight carrier (generic business
   id, real wagon, real driver + animals); choose ONE
   settlement path per leg as the normal route (driver
   collection on the tick, or the Pass 8 settlement — the
   flag prevents double collection but the normal route must
   still be one path); enforce detailed-only freight for
   slice legs as a wiring rule (no code guard prevents
   cross-system double-booking); wire slice leg receiving
   callbacks (procurement arrival, Pass 6 feed, Pass 3
   wholesale intake, Pass 8 dealer supply, Pass 8 reserve
   link); register real flour/notions counterparties
   (the production catalog is empty until then).
6. Scene/prefab/Inspector wiring: exactly one First Ledger
   bootstrapper and exactly one Civic Foundation Manager in
   Main Scene; manager audit assignments (D1
   LogisticsTransportPresentationController complete but 0
   production references; D2 LLFeedbackService self-creates
   PlayMode-only; D3 bootstrapper FindUnique silently
   skipped if absent); demo/debug scripts absent/disabled;
   playtest Inspector bools wired to production drivers, not
   just models; dialogue UI/wording for Pass 5 recruitment;
   on-screen UI/NPC messages for missed work, driven by the
   Pass 2 missed-work API; scene confirmation that no scene
   caller already invokes WorkDay, PlaceOrder, or the
   adapters (every Muse audit's Git-negative caveat).
7. Unity verification: Unity compilation of ALL Series 1 +
   Series 2 files; Unity Test Runner (EditMode) for Pass 1
   Unity tests (4), Pass 2 bakery transfer (2) and idle (1),
   Pass 4 per its review doc, Pass 7's restated
   GeneralStoreItemOfferIdentityTests, six-person fixture
   test, six-person 30-day test, PassXlPlayerAsNpcTests,
   SixPersonNoImmigrationTests, and the new Pass 11 tests;
   PlayMode full-chain checks in Main Scene (flour order to
   bakery bake to bread sale to household purchase to
   household meal, and the three-store circle, not a Roslyn
   orchestrator; plus bread + fuel + freight + Shane buying/
   hiring once the named fixture exists); ConstructionInputSupportTests
   timeout diagnosis; Phase H confirmation in Unity Test
   Runner (do not treat the 3 failures as new).
8. ScriptableObject items (confirm before deleting anything):
   the 8-font scrap list (GUID search in Main Scene/prefabs
   first, delete `.asset` plus `.meta` together only if the
   search is empty — Kennedy has NOT yet approved this scrap
   list); General Store canonical decision (GeneralStoreProfile.
   asset vs GeneralStoreBusinessDefinition.asset, byte-identical
   except script/name, both read by different paths — confirm
   which the Main Scene's GeneralStoreRuntimeManager.
   StoreDefinition points to before deleting either);
   large-livery rename/scrap decision; population default vs
   compact decision; 9 profile-less business types (Tannery
   19, PostOffice 20, Newspaper 21, Restaurant 22, Hotel 23,
   Logging 24, GrainElevator 25, Bank 26, Lawyer 27, plus
   Generic 255 — author or explicitly rule out);
   FirstLedger.asset founders (the asset requires founder
   names before bootstrapping).
9. The named expanded fixture as `.asset`/scene work once
   Kennedy inputs arrive — never from invented data — and
   town-gate activation ONLY on Kennedy's one line
   (DepartureGateDecision = ActivateGate; legacy-roll
   retirement only with his separate decision). Until then,
   arrivals stay suppressed in the slice and departures stay
   live on the legacy roll.

### Kennedy decisions still blocking (listed, none decided, nothing invented)

Seconds small vs full (blocks all sub-minute task authoring);
departure-gate activation; retiring the 4% legacy arrival
roll; departure unification (future legacy departures through
Outmigration.Depart with a caller-supplied destination vs
accepting untracked departures forever; past departures never
rewritten); store category shape (asset split vs per-item
offers — Pass 3 model exists, production choice open);
first-cycle/expanded-slice seed; flour-unit semantics;
notions supplier identity and price, and notions ownership
(store service vs bakery-owned); fuel input identity (Pass 3
generic SKU — logs, slabs, cordwood, firewood, or other);
fuel bundle-to-household-reserve conversion; heating (methods
for first pass, coal as one item or three, one game unit's
weight/heat, device driver, bakery oven fuel staying cordwood,
heating-vs-food priority, starting stove/fireplace/nothing);
bakery aging (baked-day working default, transfer-day open
alternative); WorkTimeBudget cap vs pure task sequencing;
meal duration (code 30 min vs example 15 min); sleep duration
(code 540 min vs guess 6-8h); life-task driver (day-boundary
meals vs Pass 2 life tasks, acting-adult gate); freight
shipment-set survival and freight realism (abstract surcharge
arrival vs real wagon journey) plus household-leg freight
payer; double-ordering guard (F26 — none found); friend graph
vs family-only recruitment; R05 employment-id format
acceptance (`arrival-<person>-<business>-<day>`); experience
formation rate (do not retune); roles/need measurement
(player-stated vs task-evidence vs both, never a headcount
quota); settlement data authoring (stove/fuel/retention,
off-map relative profession/location); wholesale-vs-retail
price; all fixture inputs above; all prices/rates/weights/
frequencies (test parameters only, never canon).

### What "done" means after Unity AI and Codex

A nine-person (once the fixture is authored) 30-day Main Scene
run in which businesses have non-zero cash from real sales
(not seed alone), households buy and eat bread and fuel from
real sellers, the bakery bakes from real flour/fuel/notions,
the Fuel Dealer dispenses real fuel, the store sells staple
food item by item, the freight company carries slice legs with
acceptance-gated settlement, Shane buys through real logic and
is hired only on unfinished-work evidence, players and NPCs
complete and miss tasks visibly, and newcomers (if any) arrive
only through a driven channel with housing and terms or an
explicit unemployed-with-plan flag. Until that run exists,
Passes 1-11 are models and callables on branches, not a
working in-game economy.
