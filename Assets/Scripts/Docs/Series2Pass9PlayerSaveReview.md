# Series 2 Pass 9 — Player Binding + Save Adoption (review)

Branch: `muse/series2-pass9-player-save`, based only on Pass 8
(`muse/series2-pass8-freight-fuel`). Last content pass before the
Pass 10 harness. Roslyn harness only: no Unity compilation, no
Test Runner, no PlayMode, no scene wiring is claimed anywhere in
this pass. Unity verification is Codex/Unity AI work.

## What was built

### A. Production cash port (prerequisite for production-realistic settlement)

- NEW `Domains/Economy/Core/Series2BusinessRuntimeCashPort.cs`:
  `IBusinessCashPort` over `BusinessRuntimeState`, placed in a
  Domains-layer path so a driver never references the
  orchestrator-layer `SliceWorkAndPay.BusinessRuntimeCashPort`.
  The orchestrator class is untouched; the pattern is copied,
  not referenced, so layering is not inverted.
- Pass 7 adapter decision, stated: PROOF, not rewrite. The test
  `ProductionCashPort_CreditNonNegative_IsInfallible` proves
  Credit for a non-negative amount on a non-null runtime has no
  failure path (the runtime clamps and adds), so no pre-check or
  reversal was added to the Pass 7 adapters and no Pass 7
  adapter file is edited in this pass. The adapter WARNING
  branch stays as defensive dead code. Debits still refuse
  shortfall, negatives, and a null runtime, and a failed debit
  moves nothing.

### B. Save adoption (one new section, wired into the production save)

- NEW `Infrastructure/Persistence/Series2Pass9Save.cs`:
  `Series2Pass9SaveDto` + `Series2Pass9SaveState` (Capture deep
  copy, Restore clear-then-load, idempotent: restoring the same
  DTO twice yields the same state, never a duplicate).
- `PassXlSliceSaveDto` gains ONE additive field, `series2Pass9`
  (null on old saves, which restore as empty Pass 9 state). No
  existing field renamed, removed, or reordered.
- `PassXlSliceSaveCoordinator` gains Capture/Restore overloads
  that carry the Pass 9 section when a state is supplied; the
  old overloads are unchanged in behaviour. A present section
  with no state supplied is diagnosed, never silently dropped.
- `SaveLoadManager` gains ONE property, `Series2Pass9State`,
  following the `PassXlProcurementService` pattern exactly, and
  calls the new overloads at its existing build/restore sites
  (restore passes null freight hooks; see below). SaveLoadManager
  itself is Unity-side and is NOT covered by the Roslyn harness;
  its edit is one property plus two call-site arguments, and
  Unity compilation of it is Codex/Unity AI work.

What each pass's state does in the section:

- Pass 2: sequence-day entries (task, planned minutes,
  completed, missed reason) and the missed-work report (items,
  balked flag, idle minutes). Restored as rebuilt instances,
  because neither class has a clear method.
- Pass 3: store catalog lines (shelf/back stock included),
  order drafts (supplier + SKU offers + lines + freight +
  actor + delegation flags), placed order requests, fuel-input
  orders (dealer, input SKU, draft, conversion rate, received
  flag, input lot), wholesale settlement records (Settled flag
  included), and the wholesale intake's delivered-lot set.
  A reload that rebuilt the intake without its delivered set
  could re-intake a lot; the test proves the restored set
  refuses a duplicate intake, and a restored settled record
  refuses a second settlement.
- Pass 4: duty roster (tasks + offloaded tasks, toggle request,
  NPC flag, location, travel, cash gate) and auto-eat /
  auto-sleep state (toggle, state, location, cash gate,
  seller/lodging, travel), restored in place through the
  models' own `RestoreSnapshot` methods. A restored auto-eat
  at a seller with no seller wired fails safely with no cash
  moved (tested).
- Pass 6: planned day index and batch ids. These were in NO
  save DTO before this pass. Restored through the driver's own
  `RestorePlannedState`, which re-validates batch ids against
  the bakery runtime (a batch id not found in the runtime is
  diagnosed, not invented). The driver re-validates via
  Precheck before any bake after load.
- Pass 7: delivered wholesale lot ids + settlement records
  (Settled state), as above. Adapter DayIndex is deliberately
  NOT restored: Pass 7 says the day driver sets it at day
  start, and restoring a stale day would price at yesterday's
  condition.
- Pass 8: dealer intake lots through the EXISTING
  `FuelYardByproductIntakeSaveDto` — no new DTO was invented
  for it. Fuel-source state as parameters only (dealer id,
  intake-per-bakery-unit conversion, off-map-receipt flag);
  the off-map delegate itself is not serialized, and its
  order/origin are named by the rewired callback at run time.
  Restored parameters are exposed on the state holder as
  `RestoredFuelSourceParams` for Codex to construct a new
  source from; they are never applied to a live source behind
  its back. Carrier settled set: NO second shipment DTO and
  NO second settled-set field is persisted. The shipment's
  `freightRevenueCollected` flag, which IS part of the
  shipment save DTO, is the settlement authority across
  saves; restore rebuilds the in-memory set from those flags,
  and `SettleOnAcceptance` refuses a flagged shipment even
  with an empty set — a restored settlement can never
  double-collect (tested both ways: flagged refuses, unflagged
  settles exactly once through the production port).
- Freight restore takes a `FreightShipmentHooks` object; the
  SaveLoadManager path passes null hooks. That is the
  documented default, not a silent loss: shipments, cargo,
  `deliveryApplied`, and `freightRevenueCollected` all
  round-trip in the DTO itself, and reservation rebinding is
  skipped with null hooks. Codex may pass fully-wired hooks
  through `State.Restore` directly when it has them.
- Verified, not assumed: bakery shelf/flour/fuel lots are
  already covered by `BakeryShopRuntimeSaveDto` and are NOT
  duplicated here; business cash is already in
  `BusinessRuntimeState`'s own save; household cash is already
  in `HouseholdLedgerState`; TaskAuthority has its own
  `TaskAuthoritySaveDto` (the binding's WorkTasks persist
  there, not here — this section persists the player roster
  the binding mirrors, and a driver that restores the roster
  without re-issuing through the binding has a roster with no
  authority tasks). Bakery DayIndex is driver plan state and
  IS restored (unlike adapter DayIndex, which is a pricing
  day set at day start).

Mid-run save/restore is proven by the tests: every round-trip
test restores into fresh instances and then restores a second
time, asserting no duplication.

Two production-code fixes found by the harness (not test-only):
`CopyLine` and `CopySettlement` in `Series2Pass9Save.cs`
returned the live model types instead of the DTO types; both
now return the DTO types. No Pass 2/3/4/6/7/8 internals were
changed to make the tests pass.

### C. Player binding (pure C# callables, Codex hooks them up)

NEW `Orchestration/Player/Series2PlayerBinding.cs`:

- `Series2PlayerDutyBinding`: duty roster to TaskAuthority
  issuance / start / advancement / missed / offload.
  Unknown definitions are refused, never conjured. NPC mode
  refuses issuance, advancement, and offload, roster preserved
  (duty is effective only when requested AND NPC mode off).
  Over-budget rosters are reported, never truncated or
  deleted. Offload is exactly one task to exactly one
  employee: employee issuance first, roster offload second,
  player cancellation third, with a diagnosed residual if
  that third step fails; a failed employee issuance leaves
  the player untouched. Re-issue adopts an already-open
  authority task for the same definition instead of creating
  a duplicate (including tasks left queued by a failed
  Assign). The binding accepts a caller-supplied
  `IBusinessCashPort` (now the production port above) and
  validates it, but duty work itself moves no business cash,
  and the port is NEVER used for household meals or nights.
- `Series2AutoCareLedgerBinding` + `Series2AutoCarePayment`:
  auto-care payment through `HouseholdLedger` (cash first,
  balance-checked by the ledger itself), then offered trade
  credit within limit through `ITradeCreditPort` (production:
  the port over `FinancialObligationAuthority`, the one debt
  authority; no debt is ever invented). The driver still runs
  the Find/Travel legs first; payment runs only at the
  seller/lodging, re-checking open / has-food / available
  immediately before paying. The model's `CashCents` is then
  synced to the price so the model's internal gate passes
  trivially — model cash mirrors a payment that already
  happened, it is never the payment itself. A 0c price moves
  no ledger entry (the seller's price, not a conjured free
  good). Failure leaves a known state: still at the
  seller/lodging, nothing eaten or slept, nothing paid. If
  the model step fails after a cash payment, the payment is
  refunded through the ledger as an OtherDocumented inflow
  with a refund reason; a credit obligation created before a
  model failure cannot be un-created through the port, so
  that residual is diagnosed loudly for the driver to cancel
  through the authority directly (the pre-checks exist so
  this path is unreachable in normal driving).
- Missed work: NO second system. `BuildMissedWorkMessages`
  and `RenderMissedWorkConsole` delegate to Pass 4's
  `BuildMissedWorkReport` and `Pass4PlayerMessages`, which
  read the Pass 2 report. Offloaded tasks are never missed
  work; idle is reported as standing by, never as missed
  work. Note on Pass 2 semantics, not a Pass 9 change: the
  Pass 2 report counts unfinished roster work as missed, so a
  roster with an unfinished (not yet missed) task shows it in
  the report; the tests assert the offloaded task specifically
  is absent.

### D. Tests

NEW `Tests/Editor/Economy/Series2Pass9PlayerSaveTests.cs`,
15 tests, all passing in the Roslyn harness, plus the Pass 6
(8), Pass 7 (16), and Pass 8 (12) suites re-run green in the
same harness: 51 passed, 0 failed. The 3 pre-existing
`HouseholdVerticalSlicePhaseHTests` Roslyn failures and the
1,800-second Unity timeout for `ConstructionInputSupportTests`
are unchanged and are Codex/Unity verification items, not
touched here.

## Deliberately NOT built (scope held)

- Pass 5 leads/visits: Pass 10 scope, not here.
- Fixture authoring: BLOCKED on Kennedy inputs. No
  Ethan/Tia/Shane names, ages, cash, business names, houses,
  horses, or wagons were invented anywhere in this pass.
- Town gates, Main Scene/UI, bootstrapper registration,
  heating/coal/stove, item-level stock migration
  (`BusinessRuntimeState` is per-category, not per-item),
  prices/rates as canon (all rates here are caller/test
  parameters).
- NPC-mode semantics, WorkTimeBudget cap semantics, and
  sub-minute tasks: untouched.
- No Pass 6/7/8 internals changed except through their public
  callables, plus the two DTO-type fixes in the new Pass 9
  file itself and the additive snapshot/restore methods
  already added in Pass 9's earlier work (wholesale delivered
  set, bakery planned state, carrier settled set, duty and
  auto-care snapshot restores, dealer id getter). No .asset
  file touched. Serialized enums/classes/fields append-only:
  the only serialized change is one additive field on the
  slice DTO.

## Documented, NOT decided (Kennedy decisions, unchanged)

- Household-leg freight payer: `Series2SliceLegs.DealerToHousehold`
  defaults the payer to the dealer itself. A household cannot
  pay freight through business ports, so household-leg freight
  must be explicitly priced into the retail fuel price OR
  borne by the dealer. The code treats payer as a caller
  parameter and asserts no split in any test; dealer-borne is
  NOT canonised as the game rule here.
- Pass 8 partial-supply nuance: with an off-map callback
  wired, the dealer portion can move into the bakery store
  before the callback refuses, leaving partial fuel in the
  store (not lost, usable for the next bake). The production
  driver must run the Pass 6 pre-check first, or explicitly
  accept partial-store state. Pass 8 internals were NOT
  rewritten to fix this silently.
- Cross-system double-booking is NOT prevented by either
  freight system. Detailed-only for slice legs is a Codex
  wiring rule, not a code guard, and no guard is claimed here.

## What Codex must hook up (not claimed as done)

1. Set `SaveLoadManager.Series2Pass9State` to the live
   `Series2Pass9SaveState` before the first save, exactly as
   for procurement; a null state saves an empty Pass 9 section
   with a diagnostic. Fill the holder's fields with the live
   Pass 2/3/4/6/8 instances (catalog, drafts, requests, fuel
   orders, intake, settlements, duty, auto-care, bakery
   driver, fuel intake, freight driver, carrier settlement,
   fuel source).
2. After a load, re-issue restored roster tasks through
   `Series2PlayerDutyBinding` (the roster alone has no
   authority tasks), rewire the fuel source from
   `RestoredFuelSourceParams` (including its off-map callback
   when the flag says one was wired), and pass fully-wired
   freight hooks through `State.Restore` when available.
3. Wire `Series2PlayerDutyBinding` (with the production cash
   port) and `Series2AutoCareLedgerBinding` (with the real
   `HouseholdLedger` and the trade-credit port over
   `FinancialObligationAuthority`) into the day driver, in
   the leg order the binding requires (Find/Travel before
   PayAndEat/PayAndSleep; duty Issue before Start before
   Advance).
4. Unity compilation, Test Runner, PlayMode, and scene wiring
   for everything above, including the SaveLoadManager edit.
5. The three documented-not-decided points above, which need
   Kennedy's decisions before they can be wired as rules.

## Still needs doing (after this pass)

- Pass 10 harness (Agent Two audit after this pass, then
  Agent One Pass 10): town gates decision-gated only, Pass 5
  leads/visits save adoption, expanded-slice fixture only if
  Kennedy inputs arrive, and a 9-person acceptance harness
  with Shane buying and hiring positive and negative controls.
- The Series 2 ChatGPT manager prompt, revised to fold Series 2
  in, once Series 2 finishes. The Series 1 prompt was delivered
  but has not been sent; do not send a competing prompt.
