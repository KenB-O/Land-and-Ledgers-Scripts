# B3 — Close to Release to Re-list (F-B2-1)

Universal business and construction campaign, 2026-11-17.
Base: integration `muse/universal-business-construction-2026-11-17` at
518aa31d (the B2 merge). Branch:
`muse/ubc-b3-close-release-relist-2026-11-17`.

## What this proves

The F-B2-1 positive path, end to end through the real weekly driver.
A leased NPC business that struggles 8 consecutive weeks closes orderly
through CloseOrderly, the close hook fires, the premises is released, the
lease ends with the deposit refunded (only when no rent Payable is still
unsettled), and the building re-lists with the founder as landlord and
becomes offerable. Also covered: the owner-held-shop variant (the closing
founder becomes the landlord), the unsettled-Payable deposit-held failure
mode, and the release-failure mode (logged loudly, the lease is NOT ended
silently).

B1's 8-week threshold and B2's lease terms (1500c/month rent, 1500c
deposit, 12-month term — all PROVISIONAL) are unchanged.
ProductionDesignPresets.cs and NpcConstructionDesignsDef011Tests.cs are
untouched.

## The one repair the tests exposed

B2's close callback lived in SimulationDrivers.cs, which the Roslyn
harness cannot compile, so the decision logic had no source-testable
home: B2 could only pin the market halves (EndLease re-lists, arrears
naming) and the hook-not-firing negative. To genuinely prove release +
refund + re-list, the callback's decision logic was extracted into a
harness-compiled coordinator, mirroring the existing
CommercialLeaseRentSettlement pattern:

- New `Domains/World/Property/CommercialLeaseCloseHandler.cs` —
  `HandleClosedBusiness` (release, then EndLease + refund, or ListBuilding
  with the founder as landlord) and `RefundLeaseDeposit`, with the
  runtime, market and ledgers supplied as injected ports
  (`ClosePorts`: Market, ReleasePremises, LedgerByPersonId,
  LedgerByHouseholdId, Obligations). Behaviour preserved verbatim from
  B2's SimulationDrivers code: same order, same diagnostic strings, same
  decisions.
- `Orchestration/Systems/SimulationDrivers.cs` is now a thin adapter:
  `ReleaseClosedBusinessPremises` resolves the building id from
  `info.Business.AssignedBuildingId`, the plot id via the runtime's
  `TryGetBusinessPremisesPlotId` (a pure query — resolving it before the
  handler runs, rather than only on the no-lease branch, is
  behaviour-neutral), and the founder id/name from the life record, then
  passes the production ports. The private `RefundLeaseDeposit` is
  deleted; no other caller existed (verified by grep).

## Files changed

- `Assets/Scripts/Domains/World/Property/CommercialLeaseCloseHandler.cs` (new + .meta, fresh GUID)
- `Assets/Scripts/Tests/Editor/Economy/CommercialLeaseCloseHandlerTests.cs` (new + .meta, fresh GUID)
- `Assets/Scripts/Tests/Editor/Economy/CommercialLeaseMarketTests.cs` (B2 guard test updated — see below)
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` (adapter + deleted RefundLeaseDeposit)
- `Reports/Campaigns/UBC_B3_CLOSE_RELEASE_RELIST_2026-11-17.md` (this report)

## Tests

New file `CommercialLeaseCloseHandlerTests.cs` — 7 tests, all through the
real `NpcBusinessLifecycleDriver` (GeneralStore reserve 20000c, business
cash 5000c; the driver's 1000c/week customer-seeking spend drains the
till to 0c by week 5, so the close returns a 0c residual — the arithmetic
is exact and asserted):

1. `Close_LeasedBusiness_ReleasesEndsLease_RefundsDeposit_RelistsOfferable`
   — 8 struggle weeks from day 20 close the business on day 69, the hook
   fires exactly once, the premises is released, the lease ends on day 69,
   the 1500c deposit leaves the landlord's ledger (1500c to 0c) and reaches
   the founder (10000c to 11500c), the listing is Active with the same
   landlord, offerable, fixtures intact (3), refund logged.
2. `Close_OwnerHeldShop_RelistsWithFounderAsLandlord` — no lease exists;
   the closed shop re-lists with the founder (person 42) as landlord,
   offerable, fixtures intact, no money moves.
3. `Close_UnsettledRentPayable_HoldsDeposit_LeaseStillEnds` — rent period 1
   is booked as a real 1500c Payable through the settlement (till empty on
   day 30) before the 8 struggle weeks; the lease still ends on day 80 and
   the shop re-lists, but the deposit is HELD (landlord keeps 1500c,
   founder stays 10000c), logged loudly. The offset against arrears stays
   a follow-up (F-B2-3), not silently decided.
4. `Close_ReleaseRefused_LeaseNotEnded_LoggedLoudly` — the release port
   refuses (building never assigned); the business still closes, but the
   lease stays Active, the listing stays Leased, no refund moves, refusal
   logged loudly.
5. `Close_NoAssignedBuilding_NothingReleased_LeaseUntouched` — no live
   instance (building id -1); the lease and listing are untouched, no
   refund, logged loudly.
6. `Close_MissingLandlordLedger_DepositNotRefunded_LeaseStillEnds` — the
   landlord's ledger cannot be resolved; the lease still ends and the shop
   re-lists, but no refund moves, logged loudly.
7. `HandleClosedBusiness_MissingMarket_NeverReleasesSilently` — direct
   handler call with null ports; the missing market is logged loudly and
   nothing is released.

Updated `CommercialLeaseMarketTests.cs` — B2's
`LifecycleDriver_OnBusinessClosed_NotInvoked_ForStrugglingBusiness` was
written when B1's close was not in the tree, so its premise (EvaluateWeek
never closes) became false at the B2 merge: it started at 7 struggle
weeks and the 8th week closed the business, firing the hook. It now
starts at 6 weeks and expects 7 weeks still Struggling with the hook
silent; the positive path is pinned by test 1 above. The B2 file header
now points at the B3 file. No B2 test was deleted.

## Harness runs (Roslyn/.NET 8, source tests only)

Baseline in the B3 worktree at 518aa31d (before changes):
`5165` tests total — run0b log `/home/hatch/ll-ubc-b3-run0b.log`:
**5157 passed / 1 failed**. The single failure was the stale B2 guard
test above. (An earlier run0 showed 5 failures; 4 were sparse-checkout
artifacts — the DevelopmentTownConfiguration.asset lives outside
Assets/Scripts, so Assets/Core/Population was added to the sparse
checkout and those 4 cleared.)

Post-change run1 log `/home/hatch/ll-ubc-b3-run1.log`:
**5165 passed / 0 failed / 5165 total** — 5158 base tests + 7 new, the
stale guard fixed by its update, zero regressions. FAIL-set delta vs the
integration state at base: the 1 baseline failure is gone, 7 new tests
pass, nothing else moved.

Harness config: `/home/hatch/ll-ubc-b3-harness/` (copy of the shared
config, REPO repointed to the B3 worktree, build into
`/home/hatch/ll-ubc-b3-bin` / `-obj`). The handler and test files are
under directory-listed paths (Domains/World/Property, Tests/Editor/Economy)
so no sources.txt line was needed; the csproj carries 1181 includes.

## Expanded build (SimulationDrivers adapter check)

The adapter edit is in harness-excluded SimulationDrivers.cs, so the
harness run cannot compile it. An expanded build (harness set + the 4
excluded wiring files) was run before and after the change:

- Baseline: `/home/hatch/ll-ubc-b3-build-expanded-baseline.log`, 240 error lines, 74 unique messages.
- Post-change: `/home/hatch/ll-ubc-b3-build-expanded-post.log`, 240 error lines.
- Error multiset (message + count): **byte-identical**. No error names
  CommercialLeaseCloseHandler or the changed lines; the 240 are the
  pre-existing Unity-scene missing-type noise.

Limitation, stated plainly: SimulationDrivers.cs already carries CS0246
missing-type errors in its field declarations, which suppress method-body
diagnostics compilation-wide, so the expanded build proves no NEW error
appeared, not that the adapter's method body is error-free. The adapter's
compile correctness is a Codex Unity checklist item below.

## Environment notes

- The transient disk-quota (EDQUOT) fault recurred during this lane:
  writes to /home/hatch failed outright for a stretch (even a lock file
  and a 589KB log copy), then cleared after ~29MB of my own build
  outputs were deleted. Runs used the cleaned environment with
  DOTNET_ROOT set and stdout redirected to /tmp, logs copied to
  /home/hatch afterwards.
- Harness-copy gotcha found and fixed: the copied gen-csproj script
  hardcodes `HARNESS="/home/hatch/ll-ubc-harness"` and reads the SHARED
  sources.txt, so it picked up D4/D5 driver lines the parent added to
  the shared file after my copy — lines for files my tree does not have.
  The B3 copy now points HARNESS at its own directory (as the D4/D5
  copies do). Any lane copying the script must change that line too, not
  just REPO.

## Codex Unity checklist

- The SimulationDrivers adapter (`ReleaseClosedBusinessPremises`
  delegating to `CommercialLeaseCloseHandler`, `RefundLeaseDeposit`
  deleted) must compile in Unity — the Roslyn harness cannot compile
  SimulationDrivers.cs, and the expanded-build check is limited as noted
  above.
- Live behaviour: lease an NPC shop, drive it to 8 struggle weeks, and
  confirm in PlayMode that the premises releases, the lease ends, the
  deposit refunds to the founder's household, and the building re-lists
  with the founder as landlord and is offerable.
- Same live check for the owner-held-shop variant and for a close with
  an unsettled rent Payable (deposit held, lease still ends, shop still
  re-lists).
- The 7 authored businesses' close path in a live run (B1 item) still
  applies — B3's handler is the path their closes now take.

## Follow-ups (recorded, not decided)

- F-B2-3: deposit offset against arrears (the held deposit is not applied
  to the outstanding Payable here).
- F-B2-2: formation working-capital gap (a new business starts with no
  opening cash; period-1 rent may go to arrears).
- F-B2-6: landlord for never-occupied buildings; F-B2-7: the player path
  can still claim a listed building free.
- The A2 opening-cash-target interpretation (seller's pre-close
  withdrawal vs buyer keeps all cash) is unchanged by B3.
