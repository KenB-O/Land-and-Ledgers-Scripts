# Pass VIII Economic Circulation and Business Adaptation

Worker 5 report. Roslyn harness only. This is not Unity validation.

## Scope

Section 7 of the brief asks for a full economic circulation audit now that Pass VII retired town-pulse manufactured revenue. Section 12 asks for real business failure and adaptation through production authorities, with no guaranteed survival and no forced destruction. This pass audited every money flow for real counterparties and correct accounting, fixed the gaps found, and proved the result with a 45-day adverse scenario called Ruston Decline.

## What the retirement of town-pulse exposed

With manufactured revenue gone, three circulation gaps became visible that had previously been masked.

1. Payroll through the legacy fallback destroyed cash. When `EmploymentRegistry` was null, `ResolveWeeklyPayroll` deducted wages from business cash without crediting any household and without a diagnostic. Owner slots (`owner:xxx`) were paid payroll wages, violating PL-04. Both fixed in `Domains/Economy/Core/BusinessRuntimeState.cs`. Person-slot wages now route through the shared `CreditWorkerHousehold` path with a slot-derived employment reference, and every gap is logged.

2. Reporting counted financing as profit. `BusinessReportBuilder` computed its profit proxy as revenue plus cash transfer in minus known expense, so an owner top-up looked like operating profit, and the runway estimate subtracted owner transfers from burn, hiding distress. Fixed in `ReadModels/Reporting`. The profit proxy is now revenue minus operating expense only. A new `ExplainedCashChangeCents` bridge separates operating profit from financing in and financing out, and runway is estimated from operating burn only. Ledger entries for `cash_transfer_in`, `cash_transfer_out` and `owner_draw` are now `LedgerEntryKind.Financing` (appended as value 5).

3. Close-down destroyed creditor cash. `CloseOrderly` spent business cash to settle debts but never credited the creditor's cash store, so a bank that was repaid watched its money vanish. Fixed in `Domains/Economy/Creation/NpcBusinessLifecycle.cs`. The lifecycle context carries an optional `CreditCashBridge`, and settlements credit the creditor's registered store. When the bridge or store is missing, the gap is logged explicitly rather than going silent.

## Flows audited and confirmed real

Employment wages flow business till to household ledger through employment records. Household purchases move cash household to business through `ResolveSettledSale` with a named buyer. Doctor service revenue settles the household outflow first. Retail sale requires a real buyer and acting person. Rent moves tenant to payee first with partial tolerance and the remainder as a real arrears obligation. Property tax moves through `CreditCashBridge.MoveCash` and is fail-closed. The lending cycle debits the vault before the obligation exists, servicing moves both cash legs, and the delinquency ladder is real. External trade is bounded by counterparty budgets and finite supply with transit delays and explicit restocking. NPC formation requires real founder savings. Acquisitions send the purchase price to the unmodeled seller as a boundary outflow and never top up the acquired cash. The First Ledger starting stake remains the one documented endowment.

## Deliberate non-changes

The unwired-household payroll case still destroys cash but now logs a loud diagnostic. That is a deliberate design choice. Cash with no wired recipient is a broken save, not a hidden reserve, and the log says so.

`BusinessCashTransferRuleState.autoTransferEnabled` still defaults true. The money is real and the setting is player configurable. The reporting fix stops it from inflating profit or runway, which was the actual harm.

## Ruston Decline, the 45-day adverse scenario

`Tests/Editor/Integration/Pass8AdverseCirculationTests.cs` runs a 45-day town decline through production authorities with no secret stabilization. The adverse beats are a major employer closing on day 15, credit contraction refusing the bakery's loan on day 16, a supplier failing and forcing flour imports at a higher price with real transit, wholesale and retail price changes, overdue rent becoming real arrears, a drought halving the second harvest, and the bakery going insolvent on day 36 with the shortfall explicitly assumed by its founder. The town is allowed to decline. Every consequence is real.

## Reconciliation results

Full harness on the final HEAD. 1017 passed, 4 failed. The 4 failures are the documented historical failures and are unrelated to this pass. 3 are `MoneyOrderServiceTests` (`IndorseOrder_AllowsSingleIndorsement`, `IssueDuplicate_ReplacesLostOrderFree`, `RunSettlementCycle_NetsAndSettlesInFull`) and 1 is `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`. All 8 Ruston Decline tests pass.

Cash. Every modeled cent (4 business tills plus 4 household ledgers plus the bank vault) reconciles exactly against the boundary net. The only net changes are the grain export inflow, the flour import outflow, and the millstone liquidation proceeds from the external buyer. The bank vault ends whole. 12000c left on day 2 and the full repayment including real accrued interest came back through the creditor cash bridge on day 15.

Goods. Grain, flour and bread each reconcile exactly from opening stocks through harvests, farm labor, milling, baking, imports, exports, retail and ending stocks. Milling waste is exactly 20 percent as bran.

Debts. The obligation authority reconciles with no errors. Every obligation names its debtor and creditor. The bakery's missed commercial rent existed as a real payable and was settled in full at the close-down. The mill founder's legitimate residual kept H1's household rent covered, so the decline ends with no outstanding debts and no write-offs.

Identities. P1 and P2 employments ended exactly once through the employment authority. P3 stays active. No person holds two active employments.

## Files changed

Production fixes are listed in the commit history on `muse/pass8-economic-circulation`. The scenario test is `Tests/Editor/Integration/Pass8AdverseCirculationTests.cs` with its `.meta`.

## What remains source-only versus Unity-needed

All of the above is source-only and proven in the Roslyn harness. Unity wiring is still needed for the daily drivers that call these authorities from gameplay (payroll day ticks, the rent settlement loop, close-down triggers, the bank's daily cycle). The scenario test exercises exactly the calls Unity will make, so the wiring surface is known.
