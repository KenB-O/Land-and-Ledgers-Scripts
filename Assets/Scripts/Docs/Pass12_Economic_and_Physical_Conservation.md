# Pass XII — Economic and Physical Conservation

## Authorities (single-writer rules, re-verified)

- One household cash writer: `HouseholdLedger`.
- One business cash writer class: `BusinessRuntimeState` (lender/portfolio
  transfers funnel through `AdjustCashCents`).
- One debt authority: `FinancialObligationAuthority`; named payables settle
  via `TrySettleNamedPayable`, which refuses overpayment and cash-short
  settlement for non-escrowed payables.
- One property/title system; one Task authority (`TaskAuthority`); one
  inventory/custody model per business interior (`CategoryStockState` is the
  bulk quantity authority inside interiors — IDENT-001 interior migration
  remainder still open; no second inventory authority was introduced).
- Exactly-once settlement: payroll (Pass XII C), asset sales (Pass IX, record
  persistence Pass XI), freight delivery (accepted-delivery settlement only),
  scenario failure persistence (exactly once).

## Defects closed in Pass XII that were conservation holes

- **E-DEFECT-01 (phantom revenue)**: `ResolveSettledSale` credited full revenue
  for clamped (partial or zero) delivery. Fixed: refuse with no mutation when
  stock < units. The zero-unit shape is preserved by design for the two-step
  consume-then-record pattern — a future caller that skips consume would mint
  revenue; that seam is documented as caller-discipline (F-RES-10).
- **E-DEFECT-02 (restore aliasing)**: in-memory restores shared DTO objects
  with the live sim, so a second restore loaded already-run state. Fixed on
  the load side at 6 methods (A) + 14 further areas (F rounds 1–2), with
  same-snapshot double-restore regression tests. **Capture side remains OPEN**
  (F-RES-08): capture methods are still shallow, so post-capture mutations
  leak into an in-memory snapshot before load. Disk-serialized round-trips
  are unaffected in principle.
- **Manual wage double-pay (PASS11-003)**: fixed mechanically (guard + dedup
  key + weekly netting + persistence). Residual: a direct double-run of
  `ResolveWeeklyPayroll` bypasses the service guard (service-driven runs are
  covered); a stamp guard was proven to misfire on service settlements and
  was deliberately not added.
- **Restore-activation duplication (A)**: fixed via `startingActivationIssued`.
  Residual seams (F-RES-09): old saves re-issue activation until re-saved;
  activation steps themselves never dedupe.
- **ImportState stale ledgers (F-06f)**: a smaller save loaded in the same
  session used to leave stale ledgers behind; import now clears on a non-null
  import (null still means "no data section", so old saves are not wiped).

## Conservation evidence

- WS4 30-day: cash 2647500 + 4100 − 2275 = 2649325 exactly; goods exact
  (grain 720, flour 270, bread 32, ore 0, in-transit 0); obligation authority
  reconciles empty (5 obligations, 12996 outstanding); postal books balanced
  (5000, fees 25, second redemption refused).
- Small settlement: cash 11500 → 11500 exactly. Large settlement: cash
  97500 → 97500 exactly; 90-day restore-at-45 hash-identical.
- Failures in the fixtures arise naturally (refused purchases, suspended
  payroll as payable, stockouts, delayed freight, blocked loading, mine
  closure, stalled framing, refused loans, rent arrears, declining demand,
  failed objectives). No stabilizing cash or goods were injected by any
  fixture — with the one disclosed exception noted in the Implementation
  Report: the large-town fixture's 40 grain/week "unmodeled land" stand-in
  for farm production (goods conservation is therefore asserted only for
  small-town bread; cash conservation holds in both).
- No phantom goods or money, no fabricated demand, no automatic revenue to
  stabilize struggling businesses, no free construction materials, no second
  production system driving the same business, no invented government
  authority, no manufactured buyers, no unfunded credit in any Pass XII change.

## Known conservation seams carried open (not hidden)

- Capture-side DTO sharing (F-RES-08, above).
- `WageSettlementService` replay read-model Paid inaccuracy (cash totals
  corrected and mismatches logged loudly; per-entry Paid/AmountCents can still
  be wrong; retry-after-exception double-pay risk in the service itself).
- `MineLaborRegister.SettleShiftWages`: zero production callers (test-only);
  if ever called, a rejected worker inflow would destroy cash with no escrow.
- Settled-sale guard vacuous at units == 0 by design (two-step pattern).
- Buyer principals are unvalidated strings; `GetOrCreate` fabricates ledgers
  (no production caller currently passes a phantom).
- Preserved-liability payroll guard is person-scoped and reason-string
  dependent (blocks later legitimate weeks until settled).
- `PayAgreedWage` null conflates paid with nothing-owed; unknown-day manual
  records never net.
- Generic baseline loop: Generic businesses receive shared-manager baseline
  throughput/service in addition to driver generic production; path gates
  prevent goods double-production, but two writers touch one business's weekly
  state — whether baseline counts as "production" needs an owner call
  (F-RES-11); the loop was not changed.
