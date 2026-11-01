# Pass IX — Recovery and Baseline Report

Date: 2026-10-22. Branch: muse/pass9-comprehensive-canon-development.

## Recovery

- The Pass VIII final tree was recovered intact at commit 147503f11dda7947c6e04689dcb795e678586de3. Working tree clean; no development existed after Pass VIII.
- Ancestry verified: Pass VI (4173a26) and Pass VII (cf88454) are ancestors of the recovered HEAD, and the four Pass VI integrity fixes in 1139bc0 remain intact in the line.
- Two branches were created at the recovered commit: muse/recovered-development-complete (the recovery record) and muse/pass9-comprehensive-canon-development (all Pass IX work). Nothing was merged to main at any point in the pass.

## Authority check

- The documentation package router was re-checked at pass start. The governing documents remain the Canonical Project Bible Revision XXXI and the Technical Implementation Bible Revision XIV (package v2.1, 2026-10-08). Nothing newer is authorized.
- Proposals 001–105 are approved; proposals 106–145 are not approved and were treated as unapproved design. The v0.3 consolidated bibles are provisional and were not treated as governing.
- The consolidated research PDF (Land_and_Ledgers_Three_Research_Passes_Comprehensive_Review_2026-10-14.pdf) is not present in this environment. It was not read and no claim in this pass rests on it. Phase 5 revalidation was worked from the brief's topic lists against the current source instead, because the research passes inspected 6d41860 rather than the Pass VIII tree.

## Baseline

- Roslyn harness at the recovered commit, re-run at pass start: 1116 passed, 4 failed.
- The 4 failures are the historical set recorded under RUN-011 and are preserved untouched: MoneyOrderServiceTests.IndorseOrder_AllowsSingleIndorsement, MoneyOrderServiceTests.IssueDuplicate_ReplacesLostOrderFree, MoneyOrderServiceTests.RunSettlementCycle_NetsAndSettlesInFull, and P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear.
- Validation at every later stage is reported against this baseline. Only Roslyn harness results are reported anywhere in this pass; no Unity validation, Test Runner, or PlayMode result is claimed.

## Publication

- This VM holds no GitHub credentials (gh is installed and not logged in; no credential helper or stored token exists), and the standing rule against requesting secrets outranks the push instruction. Per the brief's Phase 10 fallback, each major checkpoint was secured as a verified git bundle uploaded through Muse file storage.
- Checkpoint 0 (recovery, both branches at 147503f): bundle verified and uploaded; the download link and its expiry are recorded in the Pass IX coordination checkpoint log.
- Checkpoint 1 (post-plan, b390a70): bundle verified and uploaded; link and expiry in the coordination log.
- Checkpoints 2 and 3 (wave 1) were bundled and verified on disk; their uploads were interrupted by approval expiries and a runtime restart and are superseded by the final checkpoint bundle.
- The final checkpoint bundle (development branch at the final HEAD, plus the recovery branch) is bundled, verified, and uploaded at pass close; its link and expiry are recorded in the coordination checkpoint log and in the pass final report. A bundle upload is a verified transferable backup, not a GitHub publication; the two are reported distinctly.
