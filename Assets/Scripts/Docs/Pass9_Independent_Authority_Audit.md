# Pass IX — Independent Authority Audit

Reviewer: an independent agent that authored none of the code under review. Tree reviewed: muse/pass9-comprehensive-canon-development at 3813305 (the wave-2 complete tree), diffed against 147503f. Method: full diff read, production-caller greps across the whole tree, and independent harness runs. Roslyn harness only; no Unity validation is claimed.

## Verification basis

- Independent build: 0 error CS. Independent full run: 1258 passed, 4 failed, exactly the historical RUN-011 set. The pass's stated counts were accurate.
- Serialized compatibility: no renames in the diff, no .meta modification on any pre-existing file, enums append-only (EmploymentEndReason.MigratedOut appended), DTO changes additive only, removed members private implementation only.
- Calendar authority: ScenarioDateUtil and CalendarAnchor were property-checked against an independent proleptic-Gregorian implementation over 1296 dates (1870–1905, including every Feb 29 and the 1900 non-leap edge) with 0 mismatches; DaysBeforeAnchorYear 3652 is arithmetically right.

## Confirmed sound

- A3-01 service revenue (multiplier deleted; credit equals the measured household debit).
- A3-02 freight-charge settlement (both legs resolved first; shortfall is a named Payable; no credit-first path remains).
- A3-05 owner-funded founding cash. A3-06 valuation feeds (no double count; authority-inclusive liabilities). A3-10 payroll fallback in memory (business-out always equals household-in plus a named Payable). A4-14 sawmill measured consumption.
- WS2 and WS3 production wiring, each traced to a caller with file and line (estate daily step, domestic tick, activity planner, social drive, attendance provider, mine driver, freight driver, farm delegation).
- WS4 index equivalence, including DTO byte-equivalence and the due-event total order.
- WS5 no-evidence discipline and title-refusal rollback on the equipment path.
- The civic treasury store itself: single hub-owned store, overdraft-refusing debit, ledger invariant holds, persisted.
- The diligence dispute clue leaks no claimant identity.

## Findings and dispositions

Blocking findings (all fixed in the review-fix round and revalidated at 1285 passed / 4 failed after merge):

- R-01 (Class A, high): formation assets were unscoped, so any distressed business could sell, and CloseOrderly could liquidate, every other business's formation assets. Fixed by an additive OwnerBusinessId with filtering in proposal, appraisal, execution, the liquidation port, and CloseOrderly; sales transfer ownership to the buyer (also closing R-12's buyer-receives-nothing defect); deferred-sale notes bounded.
- R-02 (Class A/B, high): teacher pay had no production caller and no household credit leg, so a funded term would have destroyed 12000 cents. Fixed with the HouseholdLedger credit leg in the same call, a no-ledger refusal, a compensating rollback, and a term-start production drive.
- R-03 (Class A/B, high): the postage payer was a seam with no production caller while production collected the pot. Fixed by resolving payers from the real sender identity inside the services and wiring them on the production path, with a foot-messenger fallback instead of free post. One honest remainder: the money-order advice letter has no funded sender in its call context (register PASS9-004).
- R-04 (Class A/B, high): the production freight driver used the legacy unverified collection while the verified overload was test-only. Fixed by switching the driver to the verified resolver overload; unresolvable payers stay uncollected.
- R-05 (Class A, high): named Payables were not persisted, so the A3-10/A3-12 escrow records anonymized on save/load. Fixed with an additive save DTO and a round-trip test proving exactly-once settlement after load.
- R-06 (Class B, high): the SellAsset RequestId excluded the asset id while the dedup key included it, permanently stranding a second same-day same-amount sale. Fixed by composing the RequestId with the asset id.
- R-07 (Class B, high): the NPC tax drive audited an unlevied, unpersisted book over unregistered holdings. Fixed with registration at the acquisition closing, an annual levy inside the drive, and tax-book persistence.
- R-08 (Class B, medium-high): scenario objectives were never driven in production and had no completion latch. The latch is fixed and persisted; the service is registered in Docs/UnwiredSystems.md as source-complete with production start assigned to the scene bootstrap handoff, which was always the plan's declared split.

Non-blocking findings (recorded in the master register, not fixed in this pass):

- R-09 fee waterfall after partial payments and the player-leg spend-before-book ordering: register PASS9-002.
- R-10 dispute recording on the NPC path, player-path persistence, and claim dedup: register PASS9-003. The clue itself is confirmed identity-safe.
- R-14 teacher-pay term semantics beyond the fixed ordering: subsumed by the R-02 fix and register notes.
- R-15 WS4 theoretical index notes: no production mutators exist; no action.
- R-16 ore split assay certificate duplication: register PASS9-005.
- R-17 residual WeekToDateNet offer heuristic and the fail-open payroll guard note: register PASS9-007.
- R-18 domestic tick watermark surviving load: register PASS9-006.
- R-11 (provenance ledger never assigned in production) and R-13 (scan baseline grandfathering the postage payers) were fixed in the round; the HouseholdLedgerPostagePayer baseline entry remains with the precise reason recorded in the implementation report.

## Refuted draft findings

An earlier draft of this review alleged three further defects. The coordinator verified each against the tree and all three are refuted:

- PlayerDebtManager was alleged to register a FinancialObligationAuthority obligation with no cash leg. In the tree, approval commits lender capital through TryCommitLenderCapital and credits owner cash through AddOwnerCashToPortfolio before LinkActiveLoanToSharedObligation registers that already-disbursed loan with the authority. Registering an existing cash-backed loan is not minting.
- An inverted hasUnresolvedShortfall flag in the valuation summary was alleged; no such symbol exists in the tree.
- A stale 3478 comment in ScenarioDateUtil was alleged; the comment is a correct explanatory note that 3478 is arithmetically wrong and 3652 is right.
- A claim that WS1's production-path tests existed only on an unmerged branch was also refuted: the freight charge, postage, collect-on-delivery, and sawmill reproduction tests are in the merged tree and run in the Pass9Ws1 suite.

## Verdict

As first merged, the pass was not closable: five headline claims overstated production reality and two new defects (R-01, R-06) had been introduced. After the review-fix round, every blocking finding is fixed on its production path with tests, the non-blocking findings are registered with stable IDs, and the merged tree revalidates at 1285 passed / 4 historical failures with the 90-day, Ruston Decline, tick-size, three-year, and save/load suites all green. The pass closes on the source side. Unity compilation, scene wiring, Test Runner, and PlayMode remain outstanding and are not claimed.
