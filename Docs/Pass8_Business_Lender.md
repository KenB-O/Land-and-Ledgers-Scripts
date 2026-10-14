# Pass VIII (6.3): The Live Production Business Lender

## The Pass VII hold

Pass VII Workstream D wired every manager decision to a real executor, but
`ManagerExecutionServices.BusinessLender` was left null in production. The
`ManagerBorrowExecutor` therefore failed every non-escalated borrowing with
"no business lender arranged", and the brain escalated all borrowing to a
pending owner-approval request. A live production business lender remained
incomplete.

## What was built

`Domains/Economy/Financing/BusinessLenderService.cs` implements
`IManagerBusinessLender` as the town bank, a formal-bank credit participant
with finite, documented capital. It closes the full real lending cycle.

### The cycle, stage by stage

1. **Financial need.** The manager's Borrow decision arrives through the
   existing delegation path (`ManagerBorrowExecutor` to
   `IManagerBusinessLender.TryArrangeLoan`). Amount and borrower are
   validated, never assumed.
2. **Information.** `BuildBorrowerInformation` assembles the file from
   production sources only. Projections come from the
   `EnterpriseValuationReadModel` (maintainable weekly operating profit over
   the trailing window, the same basis the valuation itself uses, falling
   back to the last realized weekly cash delta). Exposure comes from the
   `FinancialObligationAuthority` (total outstanding, weekly debt service
   from installment terms, default count, existing bank loans). Standing
   comes from the business's real `BusinessReputationState` plus the
   lender's own memory of failed files. The request is filed and two
   evidence records are disclosed through the `CreditOfferWorkflow`.
3. **Lender availability.** The bank lends from its `BankDepositLedger`:
   chartered capital plus deposits, minus the reserve floor
   (`DepositsOwed * ReserveRequirement01`, 15% formal-bank policy), minus a
   single-borrower concentration cap (10% of lendable funds). A request the
   bank cannot cover is refused with the cash position stated. No unfunded
   loan proceeds can exist because disbursement is refused before any debt
   is created.
4. **Underwriting.** The shared `WorkingCapitalUnderwriting` authority
   evaluates the real projections, exposure, collateral and standing, and
   may refuse. Refusals feed lender memory (failed count, caution,
   cooling-off), so a refused borrower cannot immediately re-borrow clean.
   There is no automatic approval for an NPC request.
5. **Agreement.** Terms are the underwritten rate floored at the bank's
   policy minimum (600 bps), 12 monthly installments over a 365-day term.
6. **Actual disbursement.** The bank's real cash is debited FIRST through
   `BankDepositLedger.DisburseLoan` (refusal semantics). Only then is the
   single `FinancialObligation` created in the authority. If creation
   somehow failed, the cash is restored. The loan proceeds reach the
   borrower's till through the existing delegation path (the executor's
   cash credit after `TryArrangeLoan` returns).
7. **Debt.** Exactly one obligation per loan, in the one
   `FinancialObligationAuthority`. An idempotency key
   (borrower/amount/day) refuses a second identical approval, so the debt
   can never duplicate.
8. **Secured obligations and guaranties.** Where the borrower holds real
   transferable assets, the bank files its SENIOR lien (priority 1, no
   junior interests permitted) through `AttachSecurity`. Where the loan is
   significant (at or above 20000c) and the owner is a named person, an
   owner guaranty is recorded through `AddGuaranty`.
9. **Interest and repayment.** `ProcessDay` accrues real interest daily
   through `AccrueInterestThroughDay` and collects each scheduled payment
   from the borrower's real till: authority payment first, then the cash
   legs (borrower debit, bank receipt). Repayment reduces the obligation.
10. **Consequences.** A missed payment climbs the ladder: caution rises,
    the obligation is marked delinquent, and after four consecutive misses
    it is marked defaulted exactly once. Default calls every established
    guaranty (a real new `GuarantyCall` obligation on the guarantor — the
    covered principal is never duplicated) and enforces pledged collateral
    through the senior lien: the bank takes the asset at its appraised
    value, the recovery reduces the debt, the asset is recorded seized so
    it can never be pledged again, and any shortfall stays live as a
    deficiency on the same obligation. Satisfaction releases the security
    and the guaranty.

### Production authorities involved

- `FinancialObligationAuthority` — the one and only debt authority.
  Created, accrued, paid, delinquent, defaulted, secured, guaranteed,
  enforced and settled through it. Nothing else owns a balance.
- `BankDepositLedger` — the bank's one cash truth. Chartered once with a
  named source, finite, persisted.
- `WorkingCapitalUnderwriting` — the one underwriting implementation,
  extracted verbatim from `PlayerDebtManager` (which now delegates to it,
  API unchanged).
- `CreditOfferWorkflow` — request filing and evidence disclosure.
- `CreditEventLog` — the documentary trail (refusals, settlements,
  collections, missed payments, defaults, guaranty calls).
- `EnterpriseValuationReadModel` — current financial projections.
- `BusinessRuntimeState` — the borrower's real till, via the
  `BusinessCashStore` adapter (debits refuse when insufficient).

### How manager Borrow decisions reach the lender

`SimulationDrivers.ConfigureManagerExecution` now sets
`BusinessLender = hub.BusinessLender`, binds the shared business runtime
lookup, and charters the bank once with documented capital. The existing
`ManagerBorrowExecutor` calls `TryArrangeLoan` unchanged; on return it
credits the business till as before. `DriveAutonomousLabor` calls
`hub.BusinessLender.ProcessDay` each day for servicing. The lender and its
servicing books persist through `SystemsSaveDto.businessLender`
(clear-then-load; obligations persist through the authority's own section,
so debt and disbursement are never duplicated on load).

### Household and personal loans

Business loans are this service. Household borrowing stays with the
existing `HouseholdLenderDirectory` (private lenders, finite purses), and
player personal borrowing stays with `PlayerDebtManager` (Pass VI verdict:
valid distinct account, preserved). The bank's borrower identity is a
plain string, so a household underwriting lane could be added later; it is
not built here.

## What remains source-only vs what needs Unity scene work

Source-complete and harness-proven: the lender, the cycle, servicing,
consequences, save/load, and the hub/driver wiring (the wiring compiles
against the same sources; the harness does not compile the Orchestration
or Persistence assemblies, matching the Pass VII pattern).

Needs Unity scene work (Codex integration): the live daily manager
evaluation cadence that produces Borrow decisions in normal gameplay; the
delegation-scope UI for owners; any presentation of loan states, notices
or enforcement; PlayMode validation of the full loop. A player-owned bank
business, when it exists, can have its own `BankDepositLedger` bound to
this service in place of the chartered town-bank capital.

## Tests

`Tests/Editor/Financing/BusinessLenderServiceTests.cs` (9 tests, all
passing in the Roslyn harness):

- Insufficient lender cash is refused with no obligation and untouched cash.
- Uncreditworthy borrowers are refused by real underwriting.
- An approved loan disburses real cash and creates exactly one debt
  record, one senior lien, one guaranty.
- Interest accrues and scheduled repayment reduces the obligation with
  conserved cash on both legs.
- Default after four missed payments calls the guaranty and enforces the
  collateral, leaving the deficiency live.
- A second identical approval cannot duplicate the debt.
- Save/load mid-loan preserves state exactly once and servicing continues.
- A manager Borrow decision executes through the real delegation path
  against the production lender.
- An unchartered lender fails honestly.
