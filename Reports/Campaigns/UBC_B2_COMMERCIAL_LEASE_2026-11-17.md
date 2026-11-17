# UBC B2 — Commercial Lease Authority Audit

**Date:** 2026-11-17
**Lane:** B2 (follow-up F-B1-1)
**Branch:** `muse/ubc-b2-commercial-lease-2026-11-17`
**Base:** integration `muse/universal-business-construction-2026-11-17` at ecf64a2a (M1 merged; run7 5060/0/5060)

## Verdict

The lease authority is built and verified at the source level. NPC formation through a lease is reachable in the harness: a founder with a real opportunity, real savings, and an offerable lease listing forms a business, pays deposit plus first month's rent before creation, and signs a recorded lease after it. A refused creation refunds both payments in full. Monthly rent settles from real business cash to the real landlord, or becomes a real Payable — never informal debt, never paid into a void.

## Findings (file/method evidence)

- **B2-1 — No lease authority existed.** `DriveEntrepreneurshipWeek` left the premises and equipment finders unbound by design (`Orchestration/Systems/SimulationDrivers.cs`, the comment at the context construction). No commercial lease listings, no lease terms, no landlord anywhere in production.
- **B2-2 — The development town has zero vacant shops at boot.** `FirstLedgerSliceBootstrapper.cs` sets `requiredBusinessBuildings = CountPremisesRequiredBusinesses(townConfiguration)`, and `TownWorldController.TryApplyDevelopmentResidentialHomeCount` caps commercial buildings at that count. Every storefront is occupied by one of the authored businesses. Seeding "from the town's actual premises" therefore yields zero listings at boot — recorded honestly, not papered over with invented buildings.
- **B2-3 — Assigned buildings were add-only.** `SharedBusinessRuntimeManager.assignedBuildingIds` had only Add calls; a closed business's building never returned to the pool, so the lease market could never gain a listing from a closure. Repaired: `ReleaseNpcBusinessPremises` removes the id (harness-excluded, Codex-verified).
- **B2-4 — No building owner is recorded.** `PlacedBuilding` carries id, plotId, definition, footprint, and playerOwned — no owner. The landlord for a listing must therefore be the last rights-holder: the closing business's founder for owner-held shops, the lessor from the agreement for leased shops. A never-occupied building has no landlord on record; its listing is recorded but never offered (follow-up F-B2-6).
- **B2-5 — The formation driver path moves no formation cash.** BIZ-1 Step 6 (explicit in `BusinessCreationAuthority`): capital terms on an intent are not a cash settlement. A newly formed NPC business therefore starts with no opening cash of its own; the founder pays the deposit and first rent personally (as a proprietor does in real life). The business's own opening capital is a separate gap (follow-up F-B2-2).
- **B2-6 — Lease fixtures must not be formation assets.** The fitted-shop fixtures (counter, shelving, scale) belong to the landlord and travel with the building. If the driver recorded them as formation assets, `CloseOrderly` would liquidate them on close and the next tenant's lease would list fixtures that were sold. The driver skips them (`NpcEntrepreneurshipDriver.FormBusiness`); the equipment option is evidence for the investigation only.
- **B2-7 — B1's 8-week close is not in this tree.** B1's commit aded62a4 lives on its own unmerged branch; the integration base used here has no `CloseAfterConsecutiveStruggleWeeks` and `EvaluateWeek` never calls `CloseOrderly`. The lifecycle driver's `OnBusinessClosed` hook is therefore tested negatively here (it does not fire for a struggling business); the positive close-to-release-to-relist path is a post-B1-merge check (follow-up F-B2-1).
- **B2-8 — The used-equipment market stays unwired, deliberately.** `UsedEquipmentMarketBook` is complete but has no sellers: seeding listings from other businesses' assets without their agency would invent sale offers. The lease path genuinely needs equipment provenance, and the lease's included fixtures supply it within lease scope. Wiring the book (finder plus live listings plus purchase settlement) is a separate follow-up (F-B2-5).
- **B2-9 — Rent plumbing already existed.** `HouseholdIncomeSource.CommercialRentReceived = 12` (Pass VIII circulation) and the tenancy arrears pattern (`TenancyPaymentService`: partial payment plus a real Payable naming the agreement) were both in the codebase; the settlement mirrors them.
- **B2-10 — Rent calibration anchor already in the codebase.** `PhaseGNpcFormationTests.cs` and `HouseholdVerticalSlicePhaseH` both use 1500c/month as a shop rent. The deposit (one month), 12-month term, and 30-day rent period are PROVISIONAL calibrations for the owner to correct.

## What was built

- `Domains/World/Property/CommercialLeaseMarket.cs` — the lease authority: listings (building, plot, landlord, rent, deposit, term, included fixtures), agreements (tenant, business, deposit paid, start/end, last settled rent period, rent Payable ids), seeding from real unassigned buildings, activation, ending (re-lists with the same landlord), withdrawal, save/load (clear-then-load).
- `Domains/World/Property/CommercialLeaseRentSettlement.cs` — monthly rent settlement: paid in full from business cash, partial payment plus a real Payable, full arrears when the business cannot pay or the landlord has no ledger, idempotent per period, never settles a period whose arrears cannot be booked.
- `Domains/Economy/Creation/NpcEntrepreneurshipDriver.cs` — weekly market re-seeding; formation against a lease pays deposit plus first month's rent before creation and signs the lease after it; full refund on a creation refusal; leased fixtures skipped as formation assets.
- `Domains/Economy/Creation/NpcBusinessLifecycleDriver.cs` — optional `OnBusinessClosed` callback, invoked when a record closes during the week's evaluation.
- `Domains/Economy/Core/SharedBusinessRuntimeManager.cs` — `ReleaseNpcBusinessPremises` and `ListUnassignedCommercialBuildings` (harness-excluded).
- `Orchestration/Systems/SimulationDrivers.cs` — the premises finder (first-fit building plus offerable listing) and equipment finder (the lease's included fixtures) bound in `DriveEntrepreneurshipWeek`; the rent settlement call and the close callback (release, end lease with deposit refund, or re-list with the founder as landlord) in `DriveNpcBusinessLifecycleWeek` (harness-excluded).
- `Orchestration/Systems/SimulationSystemsHub.cs` + `Infrastructure/Persistence/SystemsSaveDtos.cs` — the hub owns the market; its save/load is wired (harness-excluded).
- `Tests/Editor/Economy/CommercialLeaseMarketTests.cs` — 17 tests (see Verification).

## Calibration (PROVISIONAL — for the owner to correct)

| Term | Value | Basis |
|---|---|---|
| Monthly rent | 1500c | Existing codebase shop-rent figure (B2-10) |
| Deposit | 1500c (one month) | Standard one-month security deposit |
| Term | 12 months | Standard one-year commercial term |
| Rent period | 30 days | One month |
| Upfront at signing | 3000c (deposit + first month) | First month plus deposit, as a tenant pays at signing |
| Included fixtures | counter, shelving, scale | Standard fitted-shop set |
| Formation cost gate | 2000c working capital + 3000c premises + 0c equipment = 5000c | Driver's existing gate, applied to the lease terms |

## Verification

- Build: 0 errors (1167 Compile Includes: 1164 baseline + 2 production files + 1 test file).
- Focused run (`CommercialLeaseMarketTests`): **17 passed, 0 failed** (`/home/hatch/ll-ubc-b2-focused1.log`).
- Full harness run: **5077 passed, 0 failed / 5077 total** (`/home/hatch/ll-ubc-b2-run2.log`) — baseline 5060/0/5060, delta exactly the 17 new tests, FAIL set empty (zero new failures). Pass9 assembly scan passes with no new baseline entries: the new types are accounted by their production callers (driver, SimulationDrivers, hub, save DTO).
- The 17 tests pin: seeding (unresolved landlord, idempotent), listing (real landlord required, duplicates refused), activation (agreement terms, first month prepaid, listing Leased), ending (re-list with same landlord), withdrawal (refused while Leased), save/load round-trip (agreement sequence preserved), settlement (paid in full, idempotent, partial plus Payable, full arrears, landlord without a ledger, arrears unbookable means not settled, multiple periods), driver formation through a lease (deposit plus first rent move, lease signed, no fixture asset), driver refusal (full refund), lifecycle hook (does not fire for a struggling business).
- NOT UNITY VERIFIED: the `SimulationDrivers` finder lambdas, the close callback and deposit refund, the runtime release/list methods, the hub save wiring, and the boot seeding (zero listings in the development town at boot — the market gains its first listings from closures). All are harness-excluded by the shared config and need Codex's Unity EditMode run.

## Follow-ups

- **F-B2-1 — Positive close-to-release-to-relist end-to-end.** Needs B1 merged first (B2-7). After the merge, extend `LifecycleDriver_OnBusinessClosed_NotInvoked_ForStrugglingBusiness` with a closing record (8 struggle weeks through `CloseOrderly`) asserting the callback fires, and confirm in Unity that a closed shop's building re-lists.
- **F-B2-2 — Formation working-capital settlement gap.** The driver path moves no formation cash (B2-5). A newly formed business starts with no opening cash; its first rent (period 1, due 30 days after signing) will go to arrears unless it trades into cash first. Decide how a formed business is capitalized.
- **F-B2-3 — Deposit offset against arrears.** On lease end the deposit is refunded only when no rent Payable on the lease is unsettled; otherwise it is held and the reason is logged. The offset (deposit applied against arrears) is not implemented.
- **F-B2-4 — Rent in the survival reserve.** B1's reserve table (`DefaultSurvivalReserveCents`) does not include rent, so a leased business looks healthier than it is. Rent should enter the reserve (or the lifecycle driver should count it).
- **F-B2-5 — Used-equipment market wiring.** Bind the equipment finder to `UsedEquipmentMarketBook` once a real listing source exists (seller agency — a business or household deciding to sell), with purchase settlement at formation. The lease fixtures cover retail shops; other trades still need a real equipment source.
- **F-B2-6 — Landlord for never-occupied buildings.** Needs the owner: name a property owner (or a townsite company) for the town's commercial buildings, or accept that only buildings freed by closures are leasable. Seeded listings stay landlord-unresolved and unoffered until then.
- **F-B2-7 — The player path can still claim a listed building free.** `TryAssignNpcBusinessPremises` first-fit is shared with player formation; a player forming a business claims the lowest-id unassigned building without a lease. The NPC path is now lease-gated; the player path's free-premises gap is pre-existing and out of this slice.

## Environment note

The write blockage that blocked this lane at 11:43–11:50 EST (git and file writes returning EPERM intermittently) cleared on its own at ~12:00 EST. `git branch` succeeded on retry, the real worktree was created with `git worktree add`, and all work below was done with normal git and file writes. The symlink-tree workaround was removed (`rm -rf /home/hatch/ll-ubc-b2` before the worktree add). `dotnet new` and the harness build also write normally. No workaround remains in the deliverable.
