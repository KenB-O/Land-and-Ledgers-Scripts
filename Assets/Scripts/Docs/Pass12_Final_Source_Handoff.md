# Pass XII — Final Source Handoff

## What this is

The final Muse source checkpoint before the separately authorized Codex Unity
integration. Everything in Pass XII — implementation (Workers A–E), independent
review and cleanup (Worker F), and coordinator verification — ends on
`muse/continuous-development`. No worker branch is left as the deliverable;
no permanent disconnected Pass XII branch exists.

## Final tree

- Branch: `muse/continuous-development`
- Final code-merged HEAD: `e0be33ac97224a987d69a8ee3065397c77ac890b`
  (merge of Worker F onto the A–E integrated tree `eafe29b`).
- The commit carrying this document and the other coordinator docs is the
  final HEAD; its full SHA, and the final bundle link, checksum, size, and
  expiry, are given in the Pass XII owner report that accompanies this handoff
  (bundle links expire and are transfer mechanisms, not permanent archival —
  the owner report is the delivery record).
- Ancestry: Pass XI `02aca56` → A → C → B → D → E → F → coordinator docs.
  The branch descends from the Unity Scripts baseline `6d41860` (verify with
  `git merge-base --is-ancestor` in the Codex session).
- `main` untouched (still `7b1ca29`), no force-push, no history rewrite.
- GitHub remote status at handoff: the continuous branch on GitHub still sits
  at Pass X `cc59bec` — this VM has no push credentials and none were
  requested. **Codex (or the owner, from his side) must pull from the final
  bundle below, not from GitHub, until the branch is pushed from the owner's
  side and the remote SHA is verified to match the final HEAD.**

## Final test baseline (Roslyn harness, clean build, 0 errors)

- **1533 passed, 0 failed** on the final code tree (Worker F's code tree
  `85a7c7a`; coordinator re-verification on the exact final tree is recorded
  in `Docs/Pass12_Final_Regression_Results.md`).
- Progression: Pass XI 1404/3 → integrated 1511/3 → final 1533/0.
  The 3 RUN-011 failures were corrected as test-side defects (production
  untouched); see `Docs/Pass12_Independent_Review.md`.
- No test deleted, no assertion weakened.

## What changed in Pass XII (one paragraph each; details in the linked docs)

- **Bootstrap/production (A)**: ghost-ownership suppression, single-instance
  Drivers guard + duplicate census, restore-activation idempotence, Rebind's
  first production caller, copy-on-load at six methods.
- **Businesses/tasks (B)**: completeness matrix — 29 types, 19 LIVE / 10 PARTIAL;
  Bakery/smithy proved through the live generic path; staged runtimes gated
  (double-production risk); six-stage construction contract.
- **Economic integrity (C)**: phantom settled-sale revenue closed, manual wage
  double-pay closed, legacy payroll accrual mirrored, report aggregation fixed,
  workload staffing model delivered, distress-sale buyer source honestly absent.
- **Player experience (D)**: scenario HUD contract, guidance gate, acquisition
  next-action + provenance, modal contract, civic/resources/startup models,
  causal explanations, person inspection with per-field visibility.
- **Simulation testing (E)**: WS4 proof preserved exactly; small + large
  settlement fixtures cash-conserved and deterministic; adversarial suites pass.
- **Review/cleanup (F)**: RUN-011 corrections, copy-on-load sweep (14+ areas),
  3 registry runtimes added (16 total), default ownership Town, store flag
  false, ASD accounting fix, Failed stage rows fix, matrix correction.

## What is NOT done (read before integrating)

- No Unity compilation, Test Runner, PlayMode, or scene wiring — all Codex work
  per `Docs/Pass12_Codex_Unity_Integration_Manifest.md` and the acceptance
  checklist in `Docs/Pass12_Pre_Playtest_Acceptance_Checklist.md`.
- LL-PLAY dispositions: 001/013/017 source-verified, Unity validation pending;
  002/003/004/005/007/008/009/010/011/012/014/015/018 source contract ready,
  Unity authoring needed; 006/016/019 confirmed unresolved at Muse handoff.
  Full per-issue evidence and acceptance tests:
  `Docs/Pass12_Playtest_Issue_Reconciliation.md`.
- Open residuals: capture-side DTO sharing (F-RES-08), direct double-run of
  `ResolveWeeklyPayroll`, WageSettlementService replay read-model inaccuracy,
  Generic baseline loop (owner call), PASS11-004/005/006/008, PASS9-004/008/
  010–014, IDENT-001 remainder, PASS8-005/006, PASS7-003, and owner decisions
  G1/G2/G3/A-14/simulation-time (see `Docs/Pass12_Deferred_Owner_Decisions.md`).
- **Do not** wire RealDemandSource into the tick, implement simulation time,
  invent Class E policy, wire BankRuntime into underwriting, or wire any
  gated runtime without first retiring that business's generic/archetype
  operation (per-instance path migration + registry update in the same commit).

## Documents delivered in Pass XII

`Pass12_Source_Baseline_and_Recovery`, `Pass12_Development_Plan`,
`Pass12_Implementation_Report`, `Pass12_Business_Completeness_Matrix`,
`Pass12_PlayerExperience_Contracts`, `Pass12_Playtest_Issue_Reconciliation`,
`Pass12_Production_Integration_Proof`,
`Pass12_Economic_and_Physical_Conservation`, `Pass12_Independent_Review`,
`Pass12_Final_Regression_Results`, `Pass12_Deferred_Owner_Decisions`,
`Pass12_Codex_Unity_Integration_Manifest`,
`Pass12_Pre_Playtest_Acceptance_Checklist`, `Pass12_Final_Source_Handoff`
(this document) — all in `Docs/` with fresh-guid `.meta` files, plus the
updated master register and `UnwiredSystems.md` registry.

## Next phase (separately authorized)

Codex Unity integration per the manifest, then — only after Gates 1–10 of the
acceptance checklist are evaluated — an owner playtest. Do NOT begin Pass XIII.
Do NOT launch Codex from this pass.
