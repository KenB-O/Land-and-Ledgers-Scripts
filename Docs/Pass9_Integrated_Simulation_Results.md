# Pass IX — Integrated Simulation Results

All results are Roslyn harness runs on this VM. No Unity validation, Test Runner, or PlayMode result is claimed anywhere in this document.

## Baseline and progression

| Stage | Passed | Failed | Note |
|---|---|---|---|
| Pass VIII final (147503f), baseline re-established at pass start | 1116 | 4 | The 4 failures are the historical RUN-011 set |
| Wave 1, WS4 performance merged | 1130 | 4 | Baseline plus 14 WS4 tests |
| Wave 1, WS1 economic integrity merged | 1155 | 4 | Plus 25 WS1 tests (stage count as validated at merge) |
| Wave 1, WS3 mine/freight/farm merged | 1170 | 4 | Plus mine, freight, and farm driver tests |
| Wave 1 complete, WS2 people/lifecycle merged | 1189 | 4 | Plus 17 lifecycle tests |
| Wave 2, WS5 appraisal/manager merged | 1219 | 4 | Plus 30 appraisal, manager, and asset-sale tests |
| Wave 2 complete, WS6 civic/scenario merged, coordinator classification commit (3813305) | 1258 | 4 | Plus 39 civic, scenario, dispute, and registry tests |
| Final tree, review-fix round merged (final HEAD) | 1285 | 4 | Plus 27 review-fix tests |

The 4 failures at every stage are identical: MoneyOrderServiceTests.IndorseOrder_AllowsSingleIndorsement, MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree, MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull, and P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear. No test was deleted, and no assertion was edited to hide a defect. The P5 failure was specifically re-checked after the NPC tax-leg fix (fixtures were corrected to levy the current tax year where the new catch-up levy legitimately applies); the historical failure itself is unchanged in character (expected Assessed, was Delinquent) and remains preserved under RUN-011.

## Required suites on the final tree

| Suite (harness filter) | Result |
|---|---|
| 90-day simulation (NinetyDay) | 6 passed, 0 failed |
| 45-day Ruston Decline adverse circulation (Pass8AdverseCirculation) | 8 passed, 0 failed |
| Tick-size variation (TickSize) | 6 passed, 0 failed |
| Three-year horizon (ThreeYear) | 5 passed, 0 failed |
| Save/load persistence (SaveLoad) | 67 passed, 0 failed |

The same five filters were run on the pre-review-fix tree (3813305) with identical results, so the review fixes changed no simulation outcome.

## Economic integrity coverage (brief 7.1)

- Cash conservation: the Ruston Decline suite reconciles every cent across the adverse scenario; civic treasury tests prove the ledger invariant (sum of credits minus debits equals balance) and that every treasury credit is measured from a payer debit in the same call; teacher-pay tests prove treasury debit equals household credit plus Payable principal.
- No phantom goods: sawmill settlement is measured-consumption only (reproduction test included); mine ore is created only from completed break-ore tasks and smelter revenue settles on or after the delivery day against a named counterparty order; split and merge lot operations conserve tonnage.
- Exactly-once: asset-sale execution refuses a second execution by action key; scenario failure persists exactly once; named Payable settlement after save/load settles exactly once with no double deduction.
- Debt authority: founding, lending, payroll escrow, and teacher shortfall all route through FinancialObligationAuthority; no second debt writer was introduced.

## Adverse scenario coverage (brief 7.2)

- Payroll unaffordable: payroll fallback tests (escrowed named Payable; accrued Payable when cash is short) in the WS1 and review-fix suites.
- Supplier empty and undeliverable shipment: freight delivery holds as Delayed with goods retained in wagon custody (Pass VIII shipment integrity, revalidated in the full run) and the verified collection leaves unpayable charges uncollected.
- Uneconomic mine and borrower default: Ruston Decline adverse circulation suite; BusinessLenderService delinquency and default coverage from Pass VIII revalidated in the full run.
- Provider loss: Pass VIII provider-loss attribution coverage revalidated in the full run; job-security evaluation now runs on the production day change.
- Asset transfer and objective-then-ownership-change: asset-sale execution and rollback tests; the scenario completion latch test proves a goal met by its deadline cannot be failed by a later ownership change.
- Business closure: CloseOrderly liquidation is scoped to the closing business's own assets after the R-01 fix, with cross-business liquidation refused by test.
- Construction material exhaustion: Pass VIII construction full-chain coverage revalidated in the full run.

## Persistence (brief 7.4)

- SaveLoad suite: 67 passed, 0 failed on the final tree, covering New Game clearing, restart, mid-shipment and mid-production restore, and multiple tick sizes (with the TickSize suite).
- Pass IX additions proven by round-trip tests: named Payables (creditor, obligation link, escrow flag), the NPC tax book, scenario latched-goal state, civic treasury state, weekly attendance state, and stock provenance.
