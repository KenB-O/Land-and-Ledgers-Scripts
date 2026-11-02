# Review 2 Pass A — General (decision-free)

Base: `muse/series2-pass11-final-review` at
`34ee209e27c56c75ba7fd813f04f91aa9d4ef0b2`. Branch for this pass:
`muse/review2-passA-general`, parent exactly the Pass 11 tip. No merge,
no force-push, no history rewrite. `main` and
`integration/pass12-unity-readiness` are untouched by this pass.

Context, stated plainly so this pass is not misread: the second
read-only review found NO post-Pass 11 Codex work published on GitHub
(local commit `f505faa3` is not accessible from any published ref), so
the comparison endpoint remains Pass 11 and this pass works from the
Pass 11 baseline and the two reviews' known caveats. Nothing in this
pass reviews, uses, or assumes `f505faa3`, and nothing here claims
Codex local work was examined. The repository is still public at this
pass; visibility, collaborators, and permissions are unchanged, and
the privacy/access plan from the second review is NOT implemented
here — it is an owner action, listed under "left" below.

Scope rule for this pass: decision-free only. No canon, price, rate,
weight, frequency, duration, cap, or semantics change. No fixture
data invented. No `.asset` file touched (that is Pass B). No Main
Scene, prefab, ProjectSettings, bootstrapper, or production driver
binding changed. No production code behaviour changed at all: the
only code change is a new Roslyn test file, and the only other edits
are documentation. Muse "done/verified" here means branch-pushed plus
Roslyn harness only — NOT Unity compilation, Test Runner, PlayMode,
or scene wiring, none of which is claimed.

## 1. What was changed

- `Assets/Scripts/Tests/Editor/Economy/Review2PassAGeneralTests.cs`
  (new) plus its `.meta` (new file, fresh GUID
  `eca61189bd1b489bb0220b7d7442bf72`, verified to appear exactly
  once in the tracked tree at this tip). 8 new tests, all passing:
  - `BakeryAdapter_FailingCashPort_SaleStandsWithWarning_CashUnposted`
    and `FuelAdapter_FailingCashPort_SaleStandsWithWarning_CashUnposted`:
    with a cash port that always refuses, a completed bakery shelf
    sale (5 loaves) and a completed fuel dispense (5 units) still
    stand, stock moves exactly, no cash is posted, and the failure
    is diagnosed as a WARNING. This pins the documented Pass 7
    WARNING-only retail pattern. It is NOT a fix and NOT an
    endorsement: the pattern stays, and its safety rests on the
    production port (next bullet), not on this test.
  - `BakeryAdapter_ProductionPort_SalePostsCashWithNoWarning` and
    `FuelAdapter_ProductionPort_SalePostsCashWithNoWarning`: the
    same sales through the production `Series2BusinessRuntimeCashPort`
    (not the earlier test-only ports) post exactly (bakery 5 x 8c,
    fuel 5 x 28c, both test parameters, never canon) with no
    WARNING. Together with Pass 9's infallibility proof, this is
    the other half of the Pass 7 cash story: on the production
    port the WARNING branch is defensive dead code.
  - `BakeryAdapter_CheckTradingCapability_IgnoresDayArgument_RefusesOnlyWhenUnset`:
    with DayIndex unset, the capability check refuses for any
    argument day; once DayIndex is set, it returns null even for
    argument day 99. The adapter does NOT enforce day equality.
  - `BakeryAdapter_SaleDayPriceGuard_RefusesOnPriceDifference_StockIntact`:
    a day-3 sale against a day-2 quote (4c vs 8c) refuses with
    shelf and cash intact; with DayIndex set to day 3 the same
    sale proceeds at 5 x 4c. The price guard, not day equality,
    is the sale-day check.
  - `DealerSource_OffMapRefusal_LeavesDealerPortionInBakeryStore`:
    dealer holds 2 units, 5 are needed, the off-map callback
    refuses: SupplyTo refuses, but the bakery store holds the 2
    dealer units and dealer stock is 0. Contrast with the
    dealer-only refusal already tested in Pass 8, which dispenses
    nothing. A refused off-map supply is NOT a clean rollback.
  - `DealerSource_OffMapUnderDelivery_LeavesDealerAndImportPortionsInBakeryStore`:
    same setup with a callback that imports only 1 of the 3
    remaining units: SupplyTo refuses, and the bakery store holds
    2 dealer + 1 import = 3 units. Fuel is not lost in either
    case, but neither refusal unwinds the store.
- `Assets/Scripts/Docs/Series2Pass7SellingReview.md` — one
  clarification paragraph added to the BAKERY DAY CAVEAT, marked
  "REVIEW 2 PASS A CLARIFICATION", stating the
  CheckTradingCapability argument behaviour and that day equality
  is not enforced, pinned by the two DayIndex tests above. No
  other wording in that doc was changed, and no code behaviour
  was changed to match the clarification.
- This doc (`Assets/Scripts/Docs/Review2PassAGeneralReview.md`,
  plus its `.meta` with fresh GUID
  `5b4c09500a4847218db718d044651d5b`, generated with
  `python3 -c "import uuid; print(uuid.uuid4().hex)"`, never copied).

No production `.cs` file was edited. No `.asset` file was edited.
No serialized enum, class, or field was renamed. No public
interface used by production code was moved.

## 2. What was checked and left alone, and why

- Pass 9 / Pass 11 doc-correction history: the Pass 9 "every
  round-trip restores twice" overstatement was already corrected
  in Pass 11, and the Pass 10 doc already carries the PASS 11
  AMENDMENT notes. No further correction was needed there, and
  none was made.
- Pass 8 partial-supply wording: the Pass 8 review doc already
  states the partial-supply nuance precisely (dealer-only refusal
  dispenses nothing; with a callback wired, a dealer portion may
  move before the callback refuses). This pass pins that contract
  in tests instead of rewording a doc that was already correct.
- Pass 6 flour conversion: the mismatch warning (applied number
  wins, description corrected by the caller) was already tested
  in Pass 6. It was not duplicated here.
- Town gates: `Validate()` refusing retirement without a
  departure decision was already tested in Pass 10. Not duplicated.
- Shared per-category pool, wholesale settlement/refund rules,
  carrier refusal without a carrier, freight acceptance-gated
  settlement, save adoption, and restore drops: all already
  tested in Passes 7-10. This pass adds no second test for them.
- `SimulationDrivers.ResolveHorseFeedStock` (around line 2407):
  re-read for this pass. It still returns a feed stock only for
  farm-owned horses and for LiveryFreight owners with a
  `livery_feed_upkeep` category, and null otherwise — including
  General Store horses and person-owned horses. That is the
  verified General Store horse feed gap, and it is deliberately
  NOT pinned as a passing "expected null" test here: pinning a
  known defect as expected behaviour would make the future fix
  look like a regression. The gap stays documented in this doc
  and in the reviews, and its fix is decision-gated (see below),
  for Pass C or Codex, not for this pass.
- Cross-system freight double-booking: still a wiring rule, not
  a code guard (detailed service enforces exclusivity within
  itself only; nothing prevents a timer-manager shipment on the
  same goods). No guard was added here, because choosing the
  surviving path per shipment class is a binding decision for
  the Codex binding pass, and a guard added blind would risk
  breaking a legitimate non-slice path.
- Retail WARNING-only pattern: NOT "fixed" to atomic
  pre-check/reversal here. The Pass 9 proof plus the new
  production-port tests show the WARNING branch cannot fire on
  the production port; rewriting the adapters to reverse a sale
  would change sale behaviour on a path that is already safe in
  production, and would need a Codex decision about which port
  a future capped port would use.
- Dealer fuel source partial supply: NOT restructured to call
  the off-map callback before dispensing dealer stock. That
  reordering would change refused-supply behaviour (no partial
  store state) and is a driver-binding decision, not a
  semantics-preserving hygiene fix. The production driver must
  run the Pass 6 pre-check first, or explicitly accept
  partial-store state — stated in Pass 8, repeated here.
- No attempt was made to "fix" the 3 Phase H failures, the
  ConstructionInputSupportTests timeout, or any Unity-only test.
  See verification below.

## 3. Deliberately left (decision-gated, Codex/Unity, or later passes)

- Horse feed source for General Store and person-owned horses
  (Kennedy decision; question K4 in the first review). Fixing it
  in `ResolveHorseFeedStock` requires choosing one source.
- Flour and fuel unit semantics as canon (1 lb vs 10 lb sack;
  intake-to-bakery and bundle-to-reserve conversions). Code
  keeps them as caller parameters; this pass asserts no value.
- General Store canonical asset (profile vs business
  definition), item-level stock migration, and every other
  ScriptableObject discrepancy: Pass B scope, not this pass.
  No `.asset` was opened for editing here.
- Player-fund reconciliation, fixture cash targets as code,
  nine-person fixture, Tannis replacing Cassidy, Ethan/Tia/Shane
  data, houses, horses, wagons, suppliers, relatives: NOT
  authored, NOT edited. The six-person fixture and its asset
  are untouched.
- Mortality threshold, work-eligibility threshold, freight payer
  per leg and commission, seconds granularity, meal/sleep
  durations, departure gate activation, 4% roll retirement,
  departure unification, double-ordering guard design, friend
  graph, and all prices/rates/weights/frequencies: all still
  open Kennedy decisions. Nothing in this pass decides any of
  them, and no test in this pass asserts any of them.
- Production binding: Pass 6 driver invocation from the day
  driver, bakery/fuel adapter registration in the bootstrapper,
  daily DayIndex setting, F19 save registration
  (`SaveLoadManager.PassXlProcurementService`), assigning live
  Pass 9 / Pass 10 save states, roster re-issue, seller/lodging
  re-supply, and fuel-source rewiring after load. All remain
  Codex work; this pass does not wire any of them, and the
  adapters/drivers remain callable surfaces, not live selling
  or live production, until that binding lands.
- GitHub privacy/access: the second review's minimal-permission
  procedure (verify Muse authenticated private-repo access with
  Contents Read on this repo only, before any visibility flip;
  the broad Credential Manager bundle is NOT required) is NOT
  implemented here. Visibility stays public in this pass; the
  owner performs any flip through authorized GitHub settings,
  after verifying Muse access. No collaborator, permission, or
  App installation was changed by this pass.
- Main Scene, prefabs, ProjectSettings, five-tab UI, Sketchbook
  mockup, and any Main Scene purchase/meal evidence: PC-local
  or unavailable from GitHub, unchanged and unverified here.
- The 45-day Main Scene claim: still unverified from GitHub
  (no new evidence in the accessible snapshot). Not repeated
  as a finding here.

## 4. Verification (Roslyn harness only)

Harness projects are NOT in the repo; this pass used
`~/workspace/land_and_ledgers/roslyn-harness/harness-passa.csproj`
(based on the Pass 11 harness, repointed at this work copy, plus
the new test file) and a separate Phase H project
`harness-passa-phaseh.csproj` with its own assembly name.

- Build: 0 errors (pre-existing warnings only, same classes as
  the Pass 11 build).
- Pass A suite: 8 passed, 0 failed.
- Re-run of the Series 2 suites on this tip: Pass 6: 8, Pass 7:
  16, Pass 8: 12, Pass 9: 15, Pass 10: 21, Pass 11: 2 — all passed,
  0 failed. Combined Series 2 + Pass A: 82 passed, 0 failed.
- Phase H (separate harness, freshly built binary
  `PassAPhaseH`): 12 passed, 3 failed — the EXACT baseline
  failures, unchanged, not fixed and not claimed fixed:
  `EndToEndTrace_InsufficientFunds_FailsThenRecovers`
  (Expected true); `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`
  (Expected string containing 'unaffordable', got '');
  `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`
  (Expected 60, got 59).
- CAUTION for future passes, found during this pass: the old
  `PhaseHHarness` binary in the harness output folder is stale
  (dated Oct 10) and reports 15 passed, 0 failed on code that is
  not this tip. Do NOT quote that binary. Phase H must be run
  from a freshly built binary for the tip under test, as was
  done here.
- NOT run: ConstructionInputSupportTests (1,800-second Unity
  timeout; do not rerun unchanged — narrow it first, Codex
  task). NOT claimed: Unity compilation, Unity Test Runner,
  PlayMode, scene wiring, or the final Unity import GUID check
  (new GUIDs are mutually distinct and appear once each in the
  tracked tree; Unity import remains the final check).

## 5. What still needs doing (in pass order)

- Pass B (ScriptableObjects): audit and, where decision-free,
  correct the `.asset` discrepancies the second review listed
  (General Store profile vs definition authority is a decision
  and must NOT be silently resolved there either — surface it
  if it is still unresolved); do not author the nine-person
  fixture in Pass B unless Kennedy's inputs have arrived.
- Pass C (scripts): the decision-gated script work this pass
  deliberately left — horse feed source once Kennedy chooses
  it, driver binding support, and any script fixes the second
  review ranked above binding — plus re-running this pass's
  new tests as regression guards (a future fix that changes
  WARNING-only, DayIndex, or partial-supply behaviour must
  update these tests explicitly and say why, never silently).
- Codex / Unity AI (unchanged from Pass 11, now with this
  pass's pins as regression guards): bind the drivers and
  adapters, set DayIndex daily, assign live save states and
  the F19 registration, then Unity compilation, Test Runner
  (suites individually, never one forced combined run),
  PlayMode full-chain proof with named buyers/sellers and flow
  assertions (purchases, meals, bread units, fuel units, freight
  deliveries, wage payments), and the nine-person fixture once
  inputs arrive. Until that run exists, Passes 1-11 plus this
  pass are models, callables, and tests on branches, not a
  working in-game economy.
- Owner: the GitHub privacy decision (verify Muse private-repo
  access first, then flip visibility if desired) and the open
  Kennedy decisions listed in section 3. Neither is assumed
  by this pass.
