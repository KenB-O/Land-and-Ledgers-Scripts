# Pass VIII Economic Circulation and Business Cohesion

Phase 2 (banking) and Phase 3 (circulation, production networks, business adaptation) of Muse Pass VIII.

## Banking and business financing (Phase 2, Brief 6.3)

The Pass VII hold is closed: a live production business lender now exists. `BusinessLenderService` implements the town-bank lending desk over the full cycle — need through the existing manager delegation path; information from production sources only (projections from `EnterpriseValuationReadModel`, exposure from the debt authority, standing from real reputation plus lender memory); availability from the `BankDepositLedger` (chartered capital plus deposits minus a 15 percent reserve floor minus a 10 percent single-borrower concentration cap, refused before any debt exists when uncovered); underwriting through the shared `WorkingCapitalUnderwriting` cluster, extracted verbatim from `PlayerDebtManager` (which now delegates to it, public API unchanged), so one implementation serves the player lane and the business lender; disbursement debiting real bank cash first via `DisburseLoan` refusal semantics, then exactly one `FinancialObligation`; senior liens and owner guaranties where supported; daily servicing with interest accrual, scheduled collection from the borrower's real till, a missed-payment ladder, default exactly once with guaranty calls and collateral enforcement (seized assets recorded so they can never be re-pledged; shortfall stays live as deficiency); satisfaction releasing security and guaranty; idempotency keys refusing duplicate approvals; clear-then-load save/load. Nine new tests. `PlayerDebtManager` remains a valid distinct account with mutations through the single debt authority, behavior preserved.

## Economic circulation (Phase 3, Brief 7)

Audit findings, fixed:
- The payroll legacy fallback deducted wages from business cash with no household credit and no diagnostic when the employment registry was null, and paid owner slots payroll wages. Now routes person-slot wages through the shared household-credit path, skips owner and non-person slots, and logs gaps loudly.
- `BusinessReportBuilder` counted owner financing as revenue in its profit proxy and subtracted owner top-ups from burn in its runway estimate; transfer and draw entries were booked as revenue and operating expense. Now: profit is revenue minus operating expense only; a new `ExplainedCashChangeCents` bridge separates operating profit from financing flows; runway uses operating burn only; a new append-only `LedgerEntryKind.Financing` classifies transfer and draw entries.
- `NpcBusinessLifecycle.CloseOrderly` settled debts from business cash without crediting the creditor's cash store: a repaid bank watched its money vanish. Now credits the creditor's registered cash store through an optional bridge, logging explicitly when unwired.

Verified real without change: town-pulse retirement holding; settled sales requiring named buyers; doctor and retail revenue settling household outflow first; rent debiting tenants first with real arrears; property tax fail-closed; the debt authority never moving cash with a clean `Reconcile()`; the lender's full cycle; the external market's bounded budgets, finite supply, transit delays, and explicit restock; NPC formation requiring real founder savings. Hidden-stabilizer hunt: none manufacturing profit found.

The 45-day Ruston Decline adverse scenario (mill closure day 15 with full bank-loan settlement plus real accrued interest, credit-contraction refusal day 16, flour imports with 3-day transit, wholesale and retail price rises, overdue commercial rent as a real payable, drought-halved harvest, bakery insolvency day 36) reconciles every modeled cent, all goods through harvests/milling/baking/imports/exports/retail, and all debts exactly, with no secret stabilization. The town declines; consequences stay real. Eight new tests.

## Production networks (Phase 3, Brief 8)

Per-industry audit with common-mechanism fixes:
- Blacksmith: lot-ledger divergence fixed (crafting now records recipe and coal consumption through the shared FIFO ledger); repair revenue conservation fixed (agreed price credited to shop cash through the one revenue sink when wired).
- Farm and butcher: livestock sale ownership convention fixed (the live cull path could never complete); sale atomicity fixed (the butcher's just-appended ownership record is never closed by mistake); butcher payment conservation fixed (refusal when short, no animal taken; inflow matched with outflow).
- Mine: the extraction production function (`MineProductionCycle.RecordBreakOreShift`) — worked break-ore minutes become ore lots with shaft/level/vein provenance, deposit depletion, and hoist-rope wear. The mine had development, labor, hoisting, assay, shipment, and settlement but no function that actually extracted ore.
- Verified sound: general-store daily sales through the household ledger with FIFO lots and a consistent pending-reorder path; freight and livery with the Pass VIII shipment guarantees, atomic team reservation, and thorough horse rental; boarding with tenant-outflow-equals-business-inflow rent collection and real arrears.

Documented with exact blockers rather than fixed without authority: the shared weekly mine path bypassing the detailed ore system (needs scene wiring and Canon sign-off); dead parallel chains (`BakeryStagedProductionChain`, `ForgeRepairChain`, `StoreRestockService` — registered as unwired in Phase 4); no business-closure mechanics existing at all (since fixed in Phase 4 via the lifecycle driver); vein tonnage policy; person-time externalization patterns; abstract local-market sales; unassigned delivery costs; `SellBread` naming no buyer. Eleven new tests.

## Business adaptation (Phase 3, Brief 12; extended Phase 4)

Businesses now genuinely fail and adapt: the NPC lifecycle driver evaluates founded businesses weekly and closes the failed ones through the orderly path; pending owner requests resolve by NPC policy (distressed asset sales approved at or above appraised value; escalated borrows routed to real underwriting); freight, postal, and rental businesses can actually earn; service businesses face real demand. No injected survival, no forced destruction: the Ruston scenario's closures emerge from cash, debt, and demand conditions.

## Remaining deficiencies

The detailed mine ore chain is built but unwired (large scoped item). Twelve detailed business runtimes remain registered-unwired by design. Service-revenue demand gating uses household spending; profile-level calibration of visit caps is future tuning. See `Docs/Pass8_Prioritized_Remaining_Work.md`.
