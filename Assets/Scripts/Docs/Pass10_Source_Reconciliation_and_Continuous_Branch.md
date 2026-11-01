# Pass X Source Reconciliation and Continuous Branch

## Verified baselines

- Published GitHub integration: `integration/pass6-living-settlement-gameplay` at
  `6d4186003d288e8a203d81174875a22613419f24` (2026-10-13). Verified from a fresh
  isolated GitHub clone on 2026-10-24. GitHub main is `7b1ca29` (2026-10-10).
  Nothing has been published to GitHub after 2026-10-13.
- Pass VIII final: `147503f11dda7947c6e04689dcb795e678586de3`.
- Pass IX final corrected: `bb2e4bcbbec2718d01b3ccf477be5a94c45b43be`
  (parent `0752ac0`, the Pass IX documentation commit).
- Continuous branch created from Pass IX: `muse/continuous-development`.

## Commit ancestry (verified with git merge-base on 2026-10-24)

Pass IX is a strict linear descendant of the published integration:

```
7b1ca29 (main)
  -> ... -> dc104d3 (integration/pass5)
  -> 6d41860 (integration/pass6, Codex boarding runtime + playtest fixes)
  -> 1139bc0 (Pass VI fixes) -> 4173a26 (Pass VI audit)
  -> Pass VII (foundation b37dbb4, four worker merges)
  -> 147503f (Pass VIII final)
  -> Pass IX (six workstreams, review-fix merge 11158fc)
  -> 0752ac0 (Pass IX docs) -> bb2e4bc (SaveLoad count correction)
```

Measured against `6d41860`, Pass IX is 111 commits ahead, 392 files changed
(+48,522 / -1,099), with **zero files deleted**. Every file present at the
published integration still exists at Pass IX.

## Historical branch inventory (verified 2026-10-24)

A fresh isolated clone of GitHub held 171 branches (160 muse/*, 5 codex/*,
5 integration/*, main). Every one of the 171 branch tips is an ancestor of
Pass IX, and every one is also an ancestor of `6d41860`. Zero branches hold a
commit that Pass IX lacks. No historical branch needs merging, and none was
merged during this pass. The once-feared unpublished Codex checkpoint
`0d7b8d9` ("Wire live tenancy and NPC business runtime adapters", 2026-10-11)
is an ancestor of Pass IX and is contained in integration/pass4, pass5, and
pass6; it was published and incorporated long ago.

## Unity alignment evidence and one correction

ACTUAL UNITY WORKING TREE NOT DIRECTLY VERIFIED. The Windows checkout at
`F:\Land & Ledgers` is not reachable from this machine, and the Pass X research
pass did not inspect it either. That research inspected two things only: the
local Muse clone at `~/workspace/land-and-ledgers/repo` (which was checked out
on the Pass IX branch, not on integration/pass6) and a fresh GitHub clone.
Any reading of the research report as claiming the Unity working tree itself
was at `6d41860` with a clean tree is a misattribution; the research report
labeled the Unity baseline UNVERIFIED. The October 13 commit remains the
latest confirmed *published* integration, not a confirmed checkout state.

Consequence: the continuous branch below is the verified superset of all
committed and published history. Its status as the authoritative Unity-aligned
branch is conditional on one owner confirmation (see Remaining blockers).

## Source comparison (6d41860 vs bb2e4bc)

- The Codex integration commit `6d41860` itself touched only five files:
  one playtest backlog document, `FirstLedgerSliceBootstrapper.cs`,
  `FirstSessionGuidanceText.cs`, and one household-reserve test. The boarding
  runtime it integrated already lived in the earlier Codex line.
- Boarding: all boarding files from `6d41860` survive at Pass IX. Pass VIII
  added `BoardingLodgingBridge.cs` (329 lines) and its tests (289 lines), plus
  additions to `BoardingRoomInventory.cs`. Boarding diff totals 677 insertions
  and 1 deletion across 6 files. The boarding-house runtime integration is
  preserved and extended, not discarded. Category A.
- Key systems present in both trees: `FirstLedgerSliceBootstrapper.cs`,
  `SimulationDrivers.cs`, `SimulationSystemsHub.cs`,
  `SharedBusinessRuntimeManager.cs` and its partials. Category A.
- Pass IX systems (mine driver, freight driver, appraisal, civic treasury,
  provenance ledger, performance indexes) are additive on top. Category C
  where they replace older behaviour: the abstract mine and farm paths that
  created ore and profit directly were retired deliberately in favour of
  real drivers. Those must not be restored.
- Category B (missing legitimate Unity work): none found in any committed or
  published history. The only possible source of missing work is uncommitted
  or local-only state inside the Unity checkout, which is unverifiable here.

## Significant conflicts

None. No merge was required. The continuous branch is Pass IX itself; there
was no divergence to reconcile in committed history.

## Reconciliation decisions

- No historical branch was merged. All are already contained.
- No file was restored, deleted, or reverted.
- No source code was changed in this pass. The only commit on the continuous
  branch beyond Pass IX is this document.
- The continuous branch is `muse/continuous-development` at the commit
  recorded below. Future Muse passes continue from it.

## Test results

Roslyn harness, clean rebuild, run on the continuous branch (source identical
to Pass IX plus this document): **1285 passed, 4 failed**. The 4 failures are
exactly the historical RUN-011 set (three MoneyOrderServiceTests, one
P5PropertyLegalJourneyTests), unchanged. Zero new failures. Integrated
simulations from Pass IX (90-day 6/0, Ruston Decline 8/0, TickSize 6/0,
ThreeYear 5/0, SaveLoad 69/0) were verified on the same source on 2026-10-22
and are unaffected by a documentation-only commit.

## Continuous branch status

- Branch: `muse/continuous-development`
- Created from: `bb2e4bc` (Pass IX final corrected)
- Status: preserved foundation containing all verified published and Muse
  work. Unity-aligned status is CONDITIONAL (see Remaining blockers).

## Remaining blockers

Exactly one. The owner must confirm the state of the Unity Scripts checkout
(`F:\Land & Ledgers\Assets\Scripts`) by providing: `git rev-parse HEAD`,
`git branch --show-current`, and `git status`. If HEAD is `6d41860` or any
other published commit and the tree is clean or near clean, the conditional
status lifts and the branch is the authoritative Unity-aligned continuous
branch. If the checkout holds local-only changes, those changes must be
committed on the Unity side and merged as a small delta on top of this
branch; nothing else about the consolidation changes.

## GitHub publication status

Not published to GitHub. This machine has no GitHub credentials (`gh` is not
logged in; no credential helper; no stored credentials), and no passwords or
tokens were requested, per standing owner rules. Publication requires the
owner to sign in on the machine that pushes, or to push the bundle below
from their side.

## Backup contents

The bundle accompanying this pass contains `muse/continuous-development`
at the final commit below and `muse/recovered-development-complete` at
`147503f` (Pass VIII final), with complete history from the repository root.
It was verified with `git bundle verify` before upload and corresponds
exactly to the final reported commit.
