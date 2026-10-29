# Pass XII — Source Baseline and Recovery

## Starting point (verified, not assumed)

- Branch: `muse/continuous-development`
- Pass XI final HEAD: `02aca56f177c6334e6a84568e6f1cb8e1bb495d2`
- Pass XI parent ancestry verified: Pass X `cc59bec73a47bca7cd1879f49100e56577b14495`
  and Pass IX `bb2e4bcbbec2718d01b3ccf477be5a94c45b43be` are both ancestors of
  `02aca56`. Pass XI is 54 commits ahead of `cc59bec`.
- Working tree at recovery: clean, no uncommitted work, no additional accepted
  changes outside the branch.
- Pass XI documents present on the branch: all Pass XI docs including
  `Pass11_Implementation_Report`, `Pass11_Integrated_Test_Results`,
  `Pass11_Production_Settlement_Proof`, `Pass11_Unity_Integration_Readiness_Audit`,
  `Pass11_Independent_Authority_Review`, `Pass11_Simulation_Time_Review`,
  `Pass11_Performance_Report`, `Pass11_Minimal_Unity_Integration_Handoff`,
  `Pass11_Implementation_Plan`, and `Pass11_WS2_Implementation_Report`
  (each with a `.meta`).

## Baseline re-verified by the coordinator (before any new development)

- Harness regeneration: 584 Compile Includes, 0 build errors.
- Roslyn harness: **1404 passed, 3 failed**, exactly the Pass XI claimed baseline.
- The 3 failures were exactly the RUN-011 set, later corrected by Worker F
  (see `Docs/Pass12_Independent_Review.md`): `MoneyOrderServiceTests`
  `IssueDuplicate_ReplacesLostOrderFree` and `RunSettlementCycle_NetsAndSettlesInFull`,
  and `P5PropertyLegalJourneyTests.Driver_LeviesAnnualAssessmentOncePerTaxYear`.

## Pass XI preservation (Gate 1, completed before development)

- GitHub remote at recovery time: `muse/continuous-development` still at
  `cc59bec73a47bca7cd1879f49100e56577b14495`. No push credentials exist on
  this VM (`gh` not logged in); per standing owner rule no secrets were requested.
- Recovery bundle created from the local branch and independently verified
  (`git bundle verify`: OK, complete history):
  - File: `pass12-pass11-recovery.bundle`
  - Size: 6,094,044 bytes
  - SHA-256: `0860a4df6649e77116549698b20a410a794d5992e401895de1f958b1703c25a2`
  - Ref: `muse/continuous-development` @ `02aca56f177c6334e6a84568e6f1cb8e1bb495d2`
  - Upload: https://muse.ai/files/1395186587007305/1150501507314143/9hpz7n1n0qb9ubpy4ahmfgjf/pass12-pass11-recovery.bundle
  - Expires: 2026-10-30T23:13:03Z
  - Muse links expire and anyone with the link can download; links are a
    transfer mechanism, not permanent archival.
- No Pass XII work began until this bundle was verified and uploaded.

## Integrated checkpoint (mid-pass, before cleanup)

- After merging Workers A, B, C, D, E (in that order, all `--no-ff`, all clean):
  HEAD `eafe29b38d3dcffcfb07753018c26771d8043e06`
- Integrated bundle (verified): `pass12-integrated-a-e.bundle`, 6,208,084 bytes,
  SHA-256 `d5d5aac50910bb5cab9af6e3cec471e08e8628a23bc4996365f036f0ee871698`.
- Integrated harness (clean build, 610 Compile Includes, 0 errors):
  **1511 passed, 3 failed** = 1404 + A28 + C33 + B4 + D22 + E20, zero new regressions.

## Final checkpoint

See `Docs/Pass12_Final_Source_Handoff.md` for the final HEAD, bundle, checksum,
link, and expiry. The final bundle supersedes the recovery and integrated
bundles for Codex use, but all three are retained as checkpoints.

## Harness reproduction note (important)

`harness/sources.txt` is local to the harness directory and is NOT tracked in
the repository. Reproducing Pass XII counts requires the Pass XII additions to
that file (saved in the coordinator's working notes and listed in
`Docs/Pass12_Final_Regression_Results.md`): `Tests/Editor/Bootstrap`,
`ReadModels/Scenario`, `ReadModels/Causal`, `ReadModels/Civic`,
`Presentation/UI/PanelModalCoordinator.cs`,
`Presentation/UI/StartupNoticeGate.cs`,
`Domains/Economy/Acquisitions/Presentation/AcquisitionDealReadModel.cs`,
`Orchestration/Bootstrap/SingleInstanceAuthorityGuard.cs`,
`ReadModels/Person/PersonInspectionReadModel.cs`, plus the pre-existing
`ReadModels/Reporting/WorkloadStaffingReadModel.cs` line and the
`PersistenceDtos` shim mirror for `ManualWagePaymentSaveDto`.
One integrated run initially failed to build (CS0246, PersonInspectionReadModel)
and its stale-DLL counts were discarded; only clean-build (0 error) counts are
reported anywhere in Pass XII.
