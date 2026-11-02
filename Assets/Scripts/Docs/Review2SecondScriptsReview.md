# Review 2 Second Scripts Pass — Review

Second scripts pass: bottleneck tracing A to Z through the vertical
slice, with real meaningful changes. Base: D3 tip
`851bbcf3ec6d48ca454e2b4e2bf4865ab800f0cf`
(`muse/review2-vertical-D3`). This branch:
`muse/review2-second-scripts`.

What this pass claims, in one sentence: the two freight collection
mechanisms now provably guard each other in both orders, the D3
save section is joined to the driver's live feed registry by shared
instance, a catalog conflict provably cannot corrupt feeding math,
the hiring chain is traced end to end with negative controls at
each step, and a trip that buys less than the need now names its
reason instead of leaving callers with an empty failure reason.
What this pass does NOT claim: Unity compilation, Test Runner,
PlayMode, scene wiring, or the final Unity import GUID check. New
GUIDs are checked only via `git grep` (mutually distinct, once
each). The SimulationDrivers branch itself is NOT Roslyn-testable
(MonoBehaviour, excluded from the harness): the domain resolvers
it calls ARE tested, but the registration call site, per-day
household adapter construction, and CareDay wiring still need
Unity verification.

## 1. What changed (file paths)

Production changes, each tied to a traced bottleneck:

- `Assets/Scripts/Domains/Population/HouseholdShopping.cs` —
  one additive field on `ShoppingTripResult`: `PartialFillReason`
  (empty by default). Diagnostic only; see section 2.
- `Assets/Scripts/Domains/Population/HouseholdShoppingLoop.cs` —
  `EvaluateCandidates` records the partial-fill cause on the
  chosen candidate (affordability-downsized, else stock-limited)
  and copies it to the trip result; `SettleAtCounter` records a
  settlement-shortfall cause when the counter sells fewer units
  than planned and evaluation left the field empty; `Fail()`
  clears the field, so a partial reason only ever survives on a
  trip that actually bought something. No cash, stock, Success,
  or need-status semantics changed.
- `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightCompanyRuntime.cs` —
  diagnostic wording ONLY on the legacy (unverified)
  `CollectFreightCharge` overload: it no longer says the charge
  was collected "from" the named payer, because that overload
  debits nobody. Return value, flag behaviour, and every other
  overload are untouched.
- `Assets/Scripts/Infrastructure/Persistence/Review2SecondScriptsSaveWiring.cs`
  (+ `.meta`, GUID `63a2aa0263c3414bad3169139deb637a`) — new pure
  C# helper `BindDriverRegistry`: binds the driver's live feed
  registry to the D3 save state by shared instance. See section 3.

Tests:

- `Assets/Scripts/Tests/Editor/Economy/Review2SecondScriptsTests.cs`
  (+ `.meta`, GUID `b6f3f9c9be9e47c683ffee355680724d`) — 8 new
  tests: freight both directions (2), legacy diagnostic (1),
  save wiring (3), catalog conflict feeding (1), hiring chain (1).
- `Assets/Scripts/Tests/Editor/Population/HouseholdShoppingLoopTests.cs` —
  3 new tests added, no existing test touched: affordability
  downsizing attribution, stock-limited attribution, full
  purchase carries an empty reason.

No `.asset` changed, created, or deleted. No fixture authored.
No Main Scene, prefab, or ProjectSettings change. No repository
visibility or permission change. No serialized type, field, or
namespace renamed. No orchestrator used as the system under test.

## 2. Partial-fill attribution (the Phase H diagnostic gap)

Traced end to end: a cash-short household's trip goes through
`EvaluateCandidates` (HouseholdShoppingLoop.cs, the "honest
downsizing" branch: ledger covers fewer units than the need, so
it buys what it can afford, never debt), then `SettleAtCounter`
(real sale with partial-fill true-up), then `ReceiveSettledGoods`
(Success = true even when the need stays open). Until this pass,
such a trip carried NO reason for its shortfall: Success was
true, FailureReason was empty, and only UnitsStillNeeded hinted
anything was unmet. Any caller probing "why is this need still
open?" — including the Phase H proofs — got nothing.

The fix is diagnostic only, by design: `ShoppingTripResult`
now carries `PartialFillReason`, set from exactly three causes
in precedence order — affordability-downsized (evaluation),
stock-limited (evaluation, when affordability did not already
downsize), settlement-shortfall (counter, only when evaluation
left the field empty). `Fail()` clears it, so it never survives
on a trip that bought nothing. Success still means "the trip
completed with a real purchase", not "the need is fulfilled":
callers that need fulfillment must check UnitsStillNeeded /
need status, and callers that need the reason a need stays open
after a purchase must read this field, not FailureReason.
Proved by the 3 new loop tests (downsized 200c need-10 trip:
1 unit, 120c, need open with 9 remaining, reason contains
"affordability", FailureReason empty; stock-limited 3-unit
trip: reason contains "stock"; full trip: reason empty), and
by the pre-existing loop tests still passing, including
`ExecuteNeed_InsufficientFunds_NoSaleNeedStaysOpen` (100c,
below one unit's price: rejected with "unaffordable", no sale)
and `ExecuteNeed_PartialStock_PartialFillNeedStaysOpen`.

## 3. D3 save wiring: two registries, now joined

Traced precisely, with file references. D3 proved its save
section round-trips when the test drives the state object
directly, but in production there are two feed registries and
nothing joined them:

- `SimulationDrivers.storeFeedRegistry`
  (`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:105`)
  — the live registry the driver's `ResolveHorseFeedStock`
  reads (line 2476 area). This is live truth.
- `Review2VerticalD3SaveState.FeedRegistry`
  (`Assets/Scripts/Infrastructure/Persistence/Review2VerticalD3Save.cs:136`)
  — the registry the coordinator's Capture reads and Restore
  repopulates (PassXlSliceSaveCoordinator.cs:227-256).

So the D3 save feature was dormant TWICE over, not once:
assigning the state to SaveLoadManager alone does NOT wire it,
because Capture would persist the state's own (empty, unless
someone populated it) registry, and Restore would repopulate a
registry the driver never reads.

The fix is one shared instance, not a copy:
`Review2SecondScriptsSaveWiring.BindDriverRegistry` assigns the
driver's registry object to the state. Capture then reads live
driver truth, and Restore mutates the shared instance in place
(it clears and repopulates, it never replaces a non-null
registry), so the driver sees restored entries with no re-bind
step. The helper refuses loudly, changing nothing, when the
state's existing registry already holds entries that binding
would discard (wrong instance, or bound after a restore); the
correct order is bind at bootstrap, before the first save AND
before the first load. Re-binding the same instance is a null
no-op. If SaveLoadManager ever replaces the state object
itself, bind again to the new object — Codex must verify the
property object identity across save/load in Unity.

Proved by 3 new tests: an unbound state captures zero store
feeds (the dormant shape, proved not assumed); after binding,
Capture carries the driver's registration verbatim; a refused
bind leaves the original binding intact; and a shared-instance
round trip (bind, capture, restore into the same state) leaves
the driver registry resolving the feed, with CareDay feeding
the store horse (20 store units -> 16, idle ration 2 feed
units x 2 store units).

Unity-side step left for Codex (one line at bootstrap, plus
assigning the state to SaveLoadManager before save/load as
Pass 9/10 require): call `BindDriverRegistry` with the driver's
live registry and the D3 state. That call site is MonoBehaviour
code and is NOT Roslyn-tested here. Note the same dormancy
applies to the Pass 9 and Pass 10 states (no production code
assigns `Series2Pass9State`, `Series2Pass10State`, or
`Review2VerticalD3SaveState` anywhere — verified by grep);
this pass binds only the D3 registry question and does not
claim the Pass 9/10 assignments.

## 4. Freight: both mechanisms guard each other (same object)

Traced: the detailed path owns one `FreightCompanyRuntime` per
freight business (`FreightProductionDriver`), and the Series 2
carrier creates shipments through that same driver
(`Series2FreightProductionCarrier`), so the settlement and the
driver scan operate on the SAME `LogisticsShipmentState` object
(`TryAcceptShipment` stores the object it accepts, no copy).
Both mechanisms check the same `freightRevenueCollected` flag:
the scan's `FindUncollectedFreightChargeShipmentIds` excludes
flagged shipments, and `Series2CarrierSettlement`
refuses flagged shipments. D3 proved settlement refuses a
restored leg; nobody had proved the two LIVE mechanisms refuse
each other.

Proved by 2 new tests on one completed shipment (250c,
payer 1000c, carrier 0c), in both orders, with exact cash:
settlement-then-scan leaves payer 750c / carrier 250c with the
scan posting 0; scan-then-settlement leaves the same balances
with the settlement refused. The legacy diagnostic fix (section
1) is part of this trace: the legacy overload's old wording
("Collected ... from {payer} (external payer)") read as a
verified debit, but that overload debits nobody; production
callers use only the verified resolver overload (verified by
grep: the driver at FreightProductionDriver.cs:326 is the sole
production collection caller, and it uses the resolver
overload). The legacy overloads have zero production callers
and stay test-only.

NOT claimed: cross-instance copies. If two driver instances
hold different objects for one shipment id (e.g., a restore
that rebuilds one side but not the other), the flag cannot
guard across them, because the guard lives on the object.
That stays a detailed-only wiring rule for Codex: one driver
instance per carrier business, and settlement + scan must
operate on that driver's shipment objects. The timer-driven
`LogisticsRuntimeManager` path holds its own shipment list
and applies load/delivery to category stock; it must never be
wired to the same slice legs as the detailed driver, or
delivery could apply twice (stock, not cash — the timer path
collects no freight revenue).

## 5. Catalog conflict: metadata only, never feeding math

Traced: `HouseholdItemCatalog` definitions carry display name,
unit label, reserve category, meal serving units, and food
flag — NOT conversions and NOT prices. Conversions live in
adapter constructor parameters (`Review2HouseholdHorseFeed`,
`Review2StoreHorseFeed`), and lot counts live in per-inventory
instance state. The static runtime dictionary gates only
whether `AddLot` accepts an unknown id.

Proved by 1 new test: after restoring a conflicting definition
for the same runtime id (diagnosed, existing definition wins),
`AddLot` still accepts the id, and an adapter built with the
caller conversion 3 draws exactly 3 household units per feed
unit (idle ration 2 feed units = 6 units, inventory 6 -> 0,
horse fed). So a cross-world conflict in one process can only
conflate metadata — and, for a FOOD item, meal serving units —
never feeding math or lot counts. The remaining hazard is
narrow: two worlds using the SAME runtime id with different
meanings in one process (an id collision, not a conversion
problem). A per-world catalog would be a broad architectural
change and is NOT made here; the documented contract stands:
a driver running two worlds in one process must register
identical items in both or accept the conflict diagnostic.
In practice a game process runs one world; multi-world in one
process is a test-harness / editor scenario.

## 6. Hiring: full chain, negative controls at each step

Traced: the chain is complete in pure C# except for one
MonoBehaviour link, and that link EXISTS — it is not missing
code, it is Unity-side code. `ManagerBrain.DailyCycle` outputs
an intent reference (`intent:recruitment-effort:bakery-1:d1`);
`SimulationDrivers.ExecuteManagerRecruitment`
(SimulationDrivers.cs:3041, called from the manager decision
path at line 2673) opens a WordOfMouth effort from that intent
via `hub.Recruiting.OpenEffort`; `RecruitmentService` then
takes a real inquiry (`ApplyForEffort`, real adult person,
no duplicate open inquiry) to a real hire (`HireFromInquiry`,
positive wage required, inquiry must be Open, effort closes
on hire, one `EmploymentRelationship`, Active).

Proved by 1 new chained test (D3's assessor + brain setup:
2400 open minutes, 0 available, 6 waiting, 3 balks): intent
alone creates 0 efforts and 0 relationships; the driver step,
performed explicitly here and labelled as the driver's call,
opens exactly 1 WordOfMouth effort (0c, unlike handbill 150c)
and still hires nobody; a real adult's application opens 1
inquiry and still hires nobody; acceptance creates exactly 1
Active relationship (employee 3, employer bakery-1), closes
the inquiry as Hired and the effort; a second hire from the
same inquiry and a hire from an unknown inquiry both refuse,
leaving exactly 1 relationship. What Roslyn cannot prove is
the driver's per-day invocation and hub wiring — that is the
Unity verification step for Codex (see section 9).

## 7. Phase H's 3 baseline failures: a policy fork, NOT fixed here

Investigated with diagnostics (temporary harness-only tests,
not committed): all three failures share ONE root cause, and
it is not a leak, a rounding error, or a missing guard.

Root cause, proved: `HouseholdShoppingLoop.EvaluateCandidates`
contains a deliberate "honest downsizing" branch
(HouseholdShoppingLoop.cs, affordability chain): when the
ledger covers at least one unit's price but not the full need,
the household buys what it can afford — a smaller real trip,
never debt — and the trip reports Success = true with the need
left open. Vane holds 200c; one flour unit costs 120c; so
Vane's 10-unit need downsizes to 1 unit (120c, balance 80c,
flour 1u, store stock 50u -> 49u, need open with 9 remaining,
no failure reason). That single purchase then cascades:

- `EndToEndTrace_InsufficientFunds_FailsThenRecovers` fails at
  its first assertion (`FailedAsExpected = !Success` is false),
  and its later numbers also diverge from the test's
  all-or-nothing assumption (balance after attempt 80c not
  200c, flour 1u not 0u, recovery buys the remaining 9u for
  1080c not 10u for 1200c — though final balance 1000c and
  flour 10u still match).
- `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`
  fails only on `TripFailureReason` containing "unaffordable"
  (got ""): the trip bought 1 unit and reported no failure,
  while need-open, meals-served 0, meals-missed 4, records 4,
  and inflow count 1 all still hold. (Balance 200c / flour 0u
  in that proof are captured BEFORE the trip, not after.)
- `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`
  fails only on `StockBefore` (59, not 60): the Hardship
  proof's 1-unit purchase consumed one unit of store stock
  before the Restock proof runs in the same world. Every cent
  and unit reconciles — store cash +120c, Vane -120c, stock
  -1u, inventory +1u — so D3's "one-unit reconciliation leak"
  hypothesis is DISPROVED: there is no leak, only the cascade.

Why this is a fork, not an unambiguous defect: the downsizing
policy is documented in code comments and a Decision event,
and blessed by two passing unit tests —
`ExecuteNeed_InsufficientFunds_NoSaleNeedStaysOpen` (100c,
below one unit's price: rejected with "unaffordable", no sale
— the branch only triggers at or above one unit's price) and
`ExecuteNeed_UnaffordableFreight_BuysOnlyWhatShopperCanCarry`
("the existing partial-fill policy may settle a smaller
in-person amount"). The Phase H tests encode the opposite
policy (cash-short household buys nothing; trip fails with
"unaffordable"; balance and stock unchanged) with no comment
acknowledging downsizing. No Canon text mandates
all-or-nothing needs (searched; nothing found), and git
history is squashed to the migration commit, so neither side
can be dated as newer.

This pass therefore does NOT rewrite Phase H expectations and
does NOT remove downsizing — either move silently picks a
game-design side. Instead it adds the partial-fill attribution
(section 2), so the fork is now observable in the result
object, and records the two resolutions with their costs:

- Resolution 1 (keep downsizing; RECOMMENDED on cost): update
  the 3 Phase H tests to downsizing reality — attempt buys 1u
  for 120c (balance 80c, flour 1u, stock 49u, need open with 9
  remaining, PartialFillReason contains "affordability");
  recovery buys 9u for 1080c; Hardship asserts the downsizing
  attribution instead of "unaffordable"; Restock asserts
  StockBefore 59 with the cascade note. No production change.
- Resolution 2 (all-or-nothing needs): remove or gate the
  downsizing branch so a cash-short need fails with
  "unaffordable" and buys nothing; then update
  `ExecuteNeed_UnaffordableFreight_BuysOnlyWhatShopperCanCarry`
  (which blesses partial carry) and any other partial-cash
  expectation; Phase H passes unchanged. This changes live
  shopping behaviour for every cash-short household, so it
  needs Kennedy's explicit decision, not a pass author's.

Until Kennedy or the development manager picks one, Phase H
stays at 3 failures — reported as the fork above, never as
new failures and never as fixed.

## 8. Checked and left alone

- Pass A pins (retail WARNING-only cash, bakery DayIndex
  gating, dealer fuel partial supply), Pass C ItemSkuMap and
  weekly converter guard, D1 wholesale driver and shelf
  consume method, D2 feed behaviour (store leg, person leg,
  runtime registration rules), D3 save behaviour (additive
  section, clear-then-load, idempotent restore): all re-run,
  all passing, none changed, no existing test edited.
- General Store profile-vs-definition dual authority:
  surfaced, not resolved — deleting or overwriting one
  authority is a decision this pass was told not to make.
  Note for the second ScriptableObjects pass: the D3 feed
  registration reads the runtime category while offers may
  read the profile, so a profile/definition disagreement
  could still make a registered feed item unsellable or a
  sold item unregistered.
- Weekly converter guard and item-level stock migration:
  traced, untouched. The guard blocks only empty-input /
  non-empty-output converters; the store never reaches the
  converter, Service businesses return before the guard, the
  Mine has its own operation, and the LumberYard has a real
  input — so the guard is dormant-safe in the slice, and any
  migration from per-category pools to per-item stock is a
  save-shape decision for Kennedy/Codex.
- Player-fund reconciliation and the player-household
  spending-permission setting: not started, and no rule
  invented to make a trace balance — every trace in this
  pass reconciles exactly on existing rules.
- Feed-need origination (who opens a feed need, at what
  quantity): still undecided (Kennedy/Codex). Tests construct
  needs by the caller and label them as such.
- No decision taken on: flour/fuel conversions as canon,
  item-level stock migration, player-fund reconciliation,
  mortality/work-eligibility thresholds, freight
  payer/commission, seconds, meal/sleep durations, departure
  gate, the 4% roll, double-ordering guard, friend graph,
  horse feed item id/price/unit as canon, or any price/rate
  as canon. Every such value in this pass's tests is a
  caller-supplied test parameter.
- `ConstructionInputSupportTests` NOT re-run, per task.

## 9. Verification (Roslyn only, honest)

- Main harness, freshly built `harness-SS.csproj` (D3 main
  harness retargeted to this branch, plus the wiring file and
  the new test file): **131 passed, 0 failed** (D3's 123 + 8
  new). Series 2, Pass A, Pass C, D1, D2, D3 suites all re-run
  inside that count; no existing test changed.
- Phase H harness, freshly built `harness-SS-phaseh.csproj`
  (D3 Phase H harness retargeted, plus the wiring file, the
  new test file, and the production files the new tests need
  that Phase H did not already include: ManagerBrain,
  StandingInstruction, ManagerExecutionContracts,
  Review2HouseholdHorseFeed): **94 passed, 3 failed** — the
  8 new tests pass, and the 3 failures are EXACTLY the
  baseline failures, unchanged (section 7).
- Shopping loop harness, freshly built
  `harness-SS-loop.csproj` (Phase H harness + the loop test
  file): **25 passed, 0 failed** for the whole
  HouseholdShoppingLoopTests file, including the 3 new
  attribution tests and the pre-existing insufficient-funds,
  partial-stock, and unaffordable-freight tests.
- New GUIDs checked via `git grep` (mutually distinct, once
  each): `63a2aa02...` (wiring file), `b6f3f9c9...` (new test
  file). Final Unity import GUID check is still Codex work.
- NOT claimed: Unity compilation (including the
  SaveLoadManager-adjacent wiring helper and the
  SimulationDrivers-adjacent registry question), Test Runner,
  PlayMode, scene wiring, daily tick driving, task issuance,
  Main Scene purchase/meal evidence, or final Unity import
  GUID check.

## 10. What still needs doing

- Second ScriptableObjects pass (descriptive objects, same
  A-to-Z tracing): General Store profile-vs-definition
  authority and its feed-offer interaction (section 8); any
  asset-side consequence of the D3/D2 feed work (none known
  — no asset change was needed here); the Pass B scrap list
  (16 Core font copies, " 1" town variant) still awaits a
  Main Scene GUID search before any deletion.
- Final verification cleanup pass, to get everything ready to
  be pulled. Pull target is the final cleanup branch, NOT
  this branch, NOT Pass 11, NOT any intermediate branch.
- Codex / Unity: Unity compilation of all second-scripts
  files including the wiring helper; Test Runner (suites run
  INDIVIDUALLY, never one forced combined run — the earlier
  combined-run OOM lesson); the one-line bootstrap bind
  (`BindDriverRegistry` with the driver's live registry and
  the D3 state) plus assigning the D3 state (and, separately,
  the Pass 9/10 states) to SaveLoadManager before save/load,
  with a save/load round trip proving a store feed
  registration survives in-game; verification that the
  SaveLoadManager state object identity persists across
  save/load (else re-bind after load); driver per-day
  household adapter construction and CareDay wiring after a
  load (a stale adapter across days would misdate
  consumption; a post-load CareDay wired to a fresh empty
  registry would silently unfed store horses); manager
  recruitment per-day driver invocation and hub wiring
  (missed work -> assessor -> brain intent -> driver effort
  -> inquiry -> employment, confirming no step auto-hires a
  person the player never approved); freight detailed-only
  wiring (one driver instance per carrier; timer manager
  never on slice legs); PlayMode full-chain proof with flow
  assertions; Phase H disposition per section 7 (Kennedy /
  development manager decision required).
- Kennedy decisions still open (unchanged by this pass):
  Phase H policy fork (section 7); feed-need origination;
  flour/fuel conversions as canon; item-level stock
  migration; player-fund reconciliation and the
  spending-permission setting; mortality/work-eligibility
  thresholds; freight payer/commission; seconds; meal/sleep
  durations; departure gate; the 4% roll; double-ordering
  guard; friend graph; all prices/rates/weights/frequencies;
  General Store profile-vs-definition authority; horse feed
  item id/price/unit as canon (all values here are
  caller-supplied test parameters).
