# Pass XII — Development Plan (as executed)

## Sequence actually followed

1. **Gate 1 — recover and preserve Pass XI** (see
   `Docs/Pass12_Source_Baseline_and_Recovery.md`). No new development before it.
2. **Audit** — Pass XI docs, the master register
   (`Docs/Pass6_Deferred_Development_Master_Register.md`), `Docs/UnwiredSystems.md`,
   and the LL-PLAY-001..019 backlog were re-read against the Pass XI tree.
   The coordinator's own baseline harness run (1404/3) confirmed the starting point.
3. **Implementation wave (Workers A–E, parallel, isolated worktrees/branches,
   file ownership enforced, merged in fixed order A, C, B, D, E with `--no-ff`)**
   - A — production/bootstrap: single-instance guards, ghost-ownership
     suppression, scenario/restore idempotence, copy-on-load (E-DEFECT-02).
   - B — businesses/tasks: completeness matrix, Bakery/smithy live-path proofs,
     equipment-capacity audit, construction interface contract.
   - C — economic integrity: exactly-once wages (PASS11-003/007), settled-sale
     stock refusal (E-DEFECT-01), report aggregation (LL-PLAY-013), workload
     staffing (LL-PLAY-012), banking provenance, PASS11-005 buyer-source audit.
   - D — player experience: scenario HUD contract (LL-PLAY-005, PASS11-006),
     guidance gate (LL-PLAY-004), acquisition next-action + provenance
     (LL-PLAY-008/009), panel modal contract (LL-PLAY-007), civic/resources/
     startup models (LL-PLAY-014/015/018), causal explanations, person inspection.
   - E — simulation testing only: WS4 proof preserved, small + large settlement
     fixtures, adversarial suites, performance measurement.
4. **Integration** — all five merges clean, no conflicts except the shared
   `SaveDtos.cs` additive fields, which combined cleanly (both fields verified
   present by grep). Integrated full harness: 1511/3.
5. **Cleanup wave (Worker F, independent)** — full-diff review, RUN-011
   test-correction review, copy-on-load sweep rounds 1–2, registry gaps,
   default-ownership fixes, master register + LL-PLAY reconciliation, draft
   review/regression/decision docs.
6. **Final gate (coordinator)** — coordinator docs, final full harness on the
   exact final tree, final bundle + upload, one owner report.

## What was deliberately NOT done

- No Codex use, no real Unity project modification, no scene/prefab work.
- No merge to `main`, no force-push, no history rewrite.
- No simulation-time implementation (proposal only, preserved).
- No RealDemandSource tick wiring (G1 stays an owner decision).
- No Class E policy invention (G2), no foreclosure/household-lending invention (G3).
- No A-14 late-latch semantics change.
- No wiring of all unwired runtimes (would double production against
  `SharedBusinessRuntimeManager`); gating decisions are documented per runtime.
- No test deleted, no assertion weakened anywhere in the pass. The only test
  expectation changes are the three RUN-011 corrections, each independently
  justified in `Docs/Pass12_Independent_Review.md`.
- No BankRuntime wiring into underwriting (would double-count bank cash);
  the binding contract remains deliberately bounded.

## Ownership and merge rules applied

- One worker per owned folder set; shared files flagged in worker reports and
  resolved by the coordinator at merge time (not by last-commit-wins).
- Every worker claim was verified by the coordinator from git objects
  (branch HEAD, diff stat, worktree cleanliness) before merging, and every
  worker's full-suite claim was re-checked by the coordinator's integrated
  and final runs.
- Worker F did not author the code it reviewed (except its own fixes, which
  were re-verified by the coordinator's final run).
