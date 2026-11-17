# UBC Lane M2 — A-Z Audit, Second Slice: Manufacturing, Finance and Contracts, Construction Completion and Occupancy

- Date: 2026-11-17
- Lane: M2, workstream M (A-Z audit second slice)
- Base: `muse/universal-business-construction-2026-11-17` at `cb222ae4` (A2 merged; integration harness run6 5053/0/5053)
- Scope: manufacturing (sawmill, bakery, carpentry, butcher, tailor, tannery, grain mill, mine, wheelwright, fuel dealer, freight), finance and contracts (lending, obligations, workouts, foreclosure, bank runtime, construction progress billing), and construction completion and occupancy (project completion, occupancy, handoffs, inspection).
- Method: read on disk. Every verdict below cites the file and method that decides it. Nothing here rests on a planning document. Callers were checked with `grep -rn` over `Assets/Scripts` (production code only; test files excluded unless stated).
- Relation to M1: M1's housing-loop findings (F-M1-1 to F-M1-9) and its boarding-leg repair (M1-R1) are not duplicated here. This slice takes an M1 follow-up only where its own audit finds it the top break; it does not.

## Status categories used

- implemented and production wired
- partially wired
- source implemented without production caller
- requires Unity scene or asset integration
- known defective
- unverified
- design incomplete
- no longer applicable

## Findings

### F-M2-1. Construction progress billing never settles in production — the hired-builder contract flow is unwired end to end (highest impact)

Status: partially wired (service, book, contract, policy exist, are hub-owned and save/load-wired) / source implemented without production caller (every entry point that would move money).

What exists: `ConstructionProgressBillingService` (`Domains/Economy/Businesses/Construction/ConstructionProgressBillingService.cs`) with `RecordDeposit` (:113), `RecordMilestone` (:191), `RecordCompletionBalance` (:279), `SettleCancellation` (:418), milestone-stage links and retainage, all failing closed when no cash port is bound (`CheckCashPortBound`, :600). `ConstructionContract` carries a real lump-sum `AgreedPriceCents` and `PaymentTerms` with `DepositCents` and `Milestones` (`ConstructionContract.cs:38-39, :165-173`). `NpcCommissionedBuild.CommissionBuilderContract` (:1612) runs the full hired-builder path at the API level: bid request, bid, `AcceptBid` into an Executed contract, `executor.BindContract` (:1655), and records the contract id on the commissioning record.

What is missing, in three layers:

1. The hired-builder path is never entered. `CommissionBuilderContract` has zero production callers. `NpcConstructionDriver.cs` has no builder references at all; it runs only the self-build path (`CommissionProject`, `FundAndStagePackage`, `PayWorkerForPackage`, `ServiceConstructionLoan`, `SettleCancellationWages`).
2. Even on the hired-builder path, no draw is ever released. `AcceptBuilderStageAndReleaseDraw` (`NpcCommissionedBuild.cs:1678`), `CancelBuilderContract` (:1855) and `RecordDeposit` have zero production callers. The driver never touches the billing service (`NpcConstructionDriver.cs` has no billing reference).
3. Even if called, every billing call refuses. The hub constructs the service with a null cash port in both places (`Orchestration/Systems/SimulationSystemsHub.cs:1132` and `:1610`: `constructionBilling ??= new ConstructionProgressBillingService(null)`). The only `IConstructionBillingCashPort` implementation in the repository is `PhaseHConstructionCashPort`, a private sealed class inside the test slice `Orchestration/HouseholdSlice/HouseholdVerticalSlicePhaseH.cs:1589`. The hub's `ConstructionBilling` property has no production user outside the hub itself (save/load at `:1796` and `:1931` only).

Expected: a hired builder business receives the agreed contract price through deposit, milestone draws and completion balance, and cancellation settles obligations through the same port. Observed: the live construction economy pays materials (procurement via `FundAndStagePackage`) and hired-labor wages (payroll via `PayWorkerForPackage`, cancellation wages via `SettleCancellationWages`), and the contract price itself is never settled anywhere. A lump-sum contract's price never reaches the builder business.

Reproduction: `grep -rn 'CommissionBuilderContract\|AcceptBuilderStageAndReleaseDraw\|RecordDeposit(' --include='*.cs' Domains/ Orchestration/ | grep -v '/Tests/'` returns no production caller for any of them; `grep -rn 'new ConstructionProgressBillingService' --include='*.cs' . | grep -v '/Tests/'` returns only the two null-port hub constructions.

Recorded, not repaired, in this slice: the repair is not small. It needs (a) a production `IConstructionBillingCashPort` that moves real cash between owner and builder parties through the live cash authorities, (b) a driver hired-builder path that commissions contracts, accepts stages and releases draws, and (c) an owner decision on when a stage counts as accepted for draw release. Inventing payment semantics here would be inventing money movement. Follow-up for the owner / a dedicated lane, with the evidence above.

### F-M2-2. Sawmill drive never re-begins lots that miss their delivery-day begin — stranded lots sit unsawn forever (REPAIRED — M2-R1)

Status: known defective.

`DriveSawmillProduction` (`Orchestration/Systems/SimulationDrivers.cs:1525`) has exactly two ways a lot gets sawn: the C7 pass re-attempts only jobs in Planned/Sawing state (`:1657`, "Failed jobs are terminal and are never re-attempted"), and the delivered-lot pass begins only lots delivered that day (`:1667-1690`, via the snapshot diff against `yardIdsBefore`). `BeginProduction`'s only production caller is the delivered pass (`:1683`). When neither pass has anything to do, the drive returns early (`:1649-1653`, "nothing-to-saw") without inspecting the yard.

A lot with logs in the yard and no open job naming it is therefore stranded by three reachable paths:

(a) No-sawyer day. If `PickSawmillSawyer` returns Invalid, the whole delivered pass is skipped (`:1667-1677`, "no-sawyer"). The lots stay in the yard; the next day they are no longer "delivered today" and no job names them, so the early return fires and they are never revisited, even after a sawyer appears.
(b) FIFO-blocked delivery. `BeginProduction` refuses a lot that is not the oldest in the yard (`SawmillProductionDriver.cs`, FIFO guard). A lot delivered while an older open job is stuck pre-deduction (task/budget refusal, blade gate the filer loop cannot clear) is refused at begin; once the older job clears, nothing re-begins the refused lot — it is not delivered-today any more and no open job names it.
(c) Any transient begin refusal on the delivery day, for the same reason: the delivered pass is the lot's only chance, and it runs once.

The API-level variants confirm the same gap from the other side. `RunProduction` (`SawmillProductionDriver.cs:331`) marks a job Failed when `RunSawing` returns null after deducting logs (`:474`, "the deducted logs are lost"), and a Failed job is terminal (`:331` refusal, "a terminal job never runs again"). The drive's own Step 9 reconciliation names the recovery path (`SimulationDrivers.cs:2116-2119`, "a fresh BeginProduction for any remainder is the recovery path") — but no production code calls it: `FailProduction` (`SawmillProductionDriver.cs:~560`, whose doc says a fresh begin "is allowed because no open job remains") has zero production callers, and `ToSaveDto` persists only Planned/Sawing jobs, so a Failed job's record is dropped on save/load while its remainder lot stays in the yard.

Expected: every log lot in the mill yard is eventually sawn (or loudly accounted for) as long as a sawyer exists. Observed: a lot that misses its delivery-day begin has no recovery path and sits in the yard unsawn forever, and the drive logs "nothing-to-saw" in exactly that state.

Reproduction (pre-repair): place a log lot in the mill yard with no production job naming it (for example, receive a lot, begin a job for it with a partial run size, abandon it via `FailProduction`, or simply receive a lot and never begin it), then observe that `DriveSawmillProduction`'s C7 pass (open jobs only) and delivered pass (today's deliveries only) never touch it. The new harness tests `SawmillStrandedRemainderM2Tests` pin the pre-repair behaviour at the driver level and the post-repair recovery.

Repair M2-R1 (this slice): `SawmillProductionDriver.FindStrandedLotIds` (pure query: yard lots with `LogUnits >= 1` and no Planned/Sawing job naming them, excluding a caller-supplied set) plus `SawmillProductionDriver.BeginStrandedRemainders` (begins and runs each stranded lot once, oldest first, one attempt per lot per drive call, so a fresh remainder from a failed run is picked up by the next day's sweep, never re-begun in the same sweep); the drive calls the query before the "nothing-to-saw" early return, and the sweep after the delivered pass, reusing the picked sawyer (or picking one when the delivered pass ran with none). See "Files changed in this slice".

Note on scope: the repair recovers stranded lots; it does not change the post-deduction loss semantics. When `RunSawing` returns null after deduction, the deducted logs are lost by design ("never re-invented"); the only post-deduction null in `RunSawing` (`SawmillShopRuntime.cs:154`) is a lumber-intake refusal (`SawmillLumberLots.cs:98`, `ReceiveLot`), which in production requires a provenance or configuration defect (the pre-deduction checks already verify owner and stand). Restoring deducted logs would be inventing stock.

### F-M2-3. Bank business runtime is source implemented without a production caller; the live loan engine is elsewhere

Status: source implemented without production caller.

Zero `new BankRuntime(`, `new BankLoanBook(` or `new NoteDesk(` anywhere outside tests. `BankRuntime` is referenced outside its own folder only in doc comments in `BankLedgerBinding.cs`. `BankLoanBook` is referenced only by `NoteDesk.cs`. `BankLedgerBinding` binds deposit ledgers (`BankDepositLedger`), not the bank runtime, and is called from `SimulationDrivers.cs:2492` and `SimulationSystemsHub.cs`. `BusinessType.Bank = 26` exists, a bank profile exists (`BusinessProfileDefinition.cs:136`), and `NpcBusinessLifecycleDriver.cs:178` has a Bank capital case (25000), but no bank business exists in the dev town (7 businesses: General Store, Bakery, Fuel Dealer, Freight, Sawmill, Lumber Yard, Carpentry Workshop). The live loan engine is `BusinessLenderService` (daily, `SimulationDrivers.cs:10711`), which owns its own `BankDepositLedger` (`BusinessLenderService.cs:211`) and does not use these classes.

Expected vs observed: the bank runtime classes read as the future bank-business implementation; nothing in the live economy needs them today. Not a break, not repaired here. If a bank business is ever authored into the town, this is the wiring to do first.

### F-M2-4. `TailorShopRuntime` is never constructed in production

Status: source implemented without production caller.

The only reference outside its own file is a doc comment in `TailorAlterations.cs:16` (`<see cref="TailorShopRuntime.RecordPinningFitting"/>`). No `new TailorShopRuntime` exists in production. A tailor profile exists (`BusinessType.Tailor`, capital 15000 in `NpcBusinessLifecycleDriver.cs:188`) but no tailor business is in the dev town. Same verdict as F-M2-3: dormant capacity, not a live break.

### F-M2-5. `TanneryRuntime` is never constructed in production

Status: source implemented without production caller.

The only references outside its own file are a doc comment in `Domains/Economy/Businesses/Blacksmith/RepairQueueEconomy.cs:623` and a nested-type parameter (`TanneryRuntime.LeatherLot`) in `Domains/Economy/Businesses/Tannery/HarnessMaker.cs:73` — a data-type consumer, not a runtime consumer. No `new TanneryRuntime` exists in production, and no tannery business is in the dev town. Dormant capacity, not a live break.

### F-M2-6. `GrainMillShopRuntime` is never constructed; there is no milling driver

Status: source implemented without production caller.

`GrainMillShopRuntime` (`Domains/Economy/Businesses/GrainMill/GrainMillShopRuntime.cs`) implements a full grist design (toll and merchant batches, wheat to flour, corn to meal, oats to feed, flour lots flowing to the bakery) but no driver constructs it and no `DriveMill` exists in `SimulationDrivers.cs`. The live flour source is the Millbrook off-map import (48c/sack), which is the flour the bakery actually uses. This is dormant capacity behind a live import route, not a live break; whether a local mill is intended at all is a design question, not a defect found here.

### F-M2-7. Construction completion handoff is published but never consumed

Status: partially wired.

`PublishCompletionHandoff` in `NpcHomeConstruction.cs` populates `completionHandoffs` (building id, parcel, owner household) for B's installation contract, but no production caller reads them outside `NpcHomeConstruction.cs`. The completion itself is wired (`CompleteProject`, `:908`, registers the building in `HousingAuthority`, defines spaces with sleeping capacity from design `SpaceSpecs`, publishes the handoff) and occupancy is wired (`OccupyCompletedHome`, `:958`, fills spaces to sleeping capacity, overcrowding recorded as a risk not a block per Canon 12.7). The handoff is a known pending integration, not a break; the named consumer is B's installation contract.

### F-M2-8. No inspection or acceptance stage exists anywhere in the construction domain

Status: design incomplete (possibly by design).

A grep for "inspect" over the construction domain returns nothing: no inspection stage, no acceptance record, no inspector role. The campaign brief lists inspection/acceptance as a stage "as applicable", so absence may be by design. Noted, not repaired. It is the same gap that F-M2-1's follow-up needs an owner decision on (when a stage counts as accepted for draw release).

### F-M2-9. Loan servicing frequencies differ by lane — open question, not a defect

Status: implemented and production wired (both lanes).

Business loans are serviced daily by `BusinessLenderService.ProcessDay`, called from `SimulationDrivers.cs:10711` inside `DriveAutonomousLabor`; terms are deliberate and documented (12 monthly installments over a 365-day term, `PaymentIntervalDays = 30`, `BusinessLenderService.cs:157`). Player debt is a separate lane and is weekly (`PlayerDebtManager.cs`: `RepaymentFrequency.Weekly`, `FirstPaymentOffsetDays = 7`, `RepeatMissedPaymentIntervalDays = 7`). The F7 note "loan servicing is WEEKLY" appears to refer to the player debt lane. The two lanes have different frequencies by design; verify against the F7 document before treating this as a defect.

## Verified wired (no finding)

- Bakery: `Series2BakeryProductionDriver` constructed by the bootstrapper (`FirstLedgerSliceBootstrapper.cs:2594`) and by `SimulationDrivers.cs:11708`; `DriveBakeryWholesalePlanning` (`:11690`) runs daily; `RunScheduledDay` exists (`Series2BakeryProductionDriver.cs:340`).
- Carpentry: `DriveCarpentryWorkshop` (`SimulationDrivers.cs:6264`) and `DriveHouseholdFurniturePurchases` (`:4568`) run daily.
- Butcher: `hub.ButcherRuntimes` iterated at `SimulationDrivers.cs:4634`.
- Sawmill production drive exists and runs daily (`DriveSawmillProduction`, `:1525`); the sawmill, lumber and byproduct stocks, blade lifecycle, lubrication and filer loop are wired.
- Fuel dealer: `FuelDealerRuntime` constructed at `SimulationDrivers.cs:536`; fuel-dealer suppliers registered (`:476-503`).
- Freight: `DriveFreightCompanies` (`SimulationDrivers.cs:7010`).
- Construction completion and occupancy: `CompleteProject` and `OccupyCompletedHome` are called in production by `NpcConstructionDriver.cs:907` and `:946`. The player weekly queue is wired: `AcquisitionMarketManager.ResolveWeeklyConstructionQueue` (`:2055`) and `ResolveWeeklyConstructionSupportProduction` (`:2195`), called from `OnWeekChanged` (`SimulationDrivers.cs:7728-7729`).
- `NpcConstructionCommissioner` is wired (callers: `NpcConstructionDriver.cs`, `NpcConstructionHiredLabor.cs`, `NpcConstructionNeedEvaluator.cs`, `NpcConstructionProcurementAdapter.cs`, `SimulationSystemsHub.cs`, `SimulationDrivers.cs`).
- `NpcHomeConstruction` (`ConstructionProjectExecutor` is a class inside this file): `CreateProject`, `CreateWorkPackages`, `StagePackageMaterials`, `CommitLabor`, `AdvancePackage` (dependency order enforced, "stages are never skipped"), `CancelProject`, `CompleteProject`, `OccupyCompletedHome`, `OccupyCompletedHomeAsLodger` (`:1030`, whose doc states the production caller is a future driver slice — M1 already carries the D-10 lodger path as an open item), `BindContract` (`:1117`, called by `NpcCommissionedBuild.cs`), save/load DTOs.
- Finance authorities are live: `FinancialObligationAuthority` (1009 lines; `ApplyScheduledPayment`, `AccrueInterestThroughDay`, `MarkDelinquent`, `MarkDefaulted`, guaranty and security-interest handling) has production callers including `NpcCommissionedBuild.cs`, `BusinessLenderService.cs`, `NpcCreditDecisions.cs`, `CreditRegistry.cs`, `CreditWorkoutService.cs`, `ForeclosureService.cs`, `PlayerDebtManager.cs`, `TradeCreditAging.cs`, `NpcPropertyLife.cs`, `SliceBusinessCredit.cs`, `AutonomousSocietyDriver.cs`. `CreditWorkoutService` is called by `NpcBusinessLifecycle.cs` and `NpcCreditDecisions.cs`; `ForeclosureService` by `NpcBusinessLifecycle.cs`, `CreditWorkoutService.cs` and `NpcCreditDecisions.cs`. `BankCollectionsDesk` is referenced only by `BankLoanBook.cs` (dormant with the bank runtime, F-M2-3).
- Progress billing and contracts exist at reference level: `ConstructionProgressBillingService` callers include `ConstructionContract.cs`, `NpcCommissionedBuild.cs` and the hub; `BindContract` is called by `NpcCommissionedBuild.cs`; `ConstructionContractBook` callers include `ConstructionContractRecords.cs`, `NpcCommissionedBuild.cs` and the hub. The money-movement gap is F-M2-1, not a missing-reference problem.
- Wheelwright and mine runtimes are referenced by live consumers (`RepairQueueEconomy.cs`, `RepairWorkOrder.cs`, `WagonRepairEconomy.cs`, `WheelwrightBuildOrder.cs`, `WheelwrightUsedWagons.cs`; `MineLaborRegister.cs`, `MineProductionCycle.cs`, `MineProductionDriver.cs`, `MineRuntimeModels.cs`, `BusinessInstanceState.cs`) and are plausibly wired; no defect found in this slice.

## Files changed in this slice

- `Reports/Campaigns/UBC_M2_AZ_AUDIT_MANUFACTURING_FINANCE_2026-11-17.md` — this document.
- `Assets/Scripts/Domains/Economy/Businesses/Logging/SawmillProductionDriver.cs` — M2-R1: `FindStrandedLotIds` and `BeginStrandedRemainders`.
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` — M2-R1 call site: stranded query before the "nothing-to-saw" early return, sweep after the delivered pass, sawyer reuse.
- `Assets/Scripts/Tests/Editor/Economy/SawmillStrandedRemainderM2Tests.cs` (+ `.meta`, fresh GUID) — regression tests for F-M2-2.

## Verification note (honest)

- Harness (Roslyn, .NET 8) only. Baseline in this worktree: 5053 passed / 0 failed / 5053 total (log `/home/hatch/ll-ubc-m2-run0.log`). Post-repair full run: **5063 passed / 0 failed / 5063 total**, run exit 0 (log `/home/hatch/ll-ubc-m2-run2.log`). FAIL-set delta: none; the +10 are the new M2 regression tests, all passing. Invocation matters: `LAND_LEDGERS_REPO` must be the Scripts checkout (`<worktree>/Assets/Scripts`) and the working directory inside the worktree. Pointing the variable at the repo root makes the C8 toll-sawing scan count test files as production callers (1 failure); running from outside the worktree makes 139 repo-file tests fail to locate the root. Both are harness-invocation artifacts, not code failures (verified by the re-runs in `/home/hatch/ll-ubc-m2-run1.log`).
- The shared dotnet host (`/home/hatch/dotnet/dotnet`) segfaults under this agent's normal shell environment (`dotnet --version` exits 139) but works with a cleaned environment (`env -i HOME=/home/hatch PATH=/usr/bin:/bin DOTNET_CLI_HOME=/home/hatch/.dotnet`); the build and all runs in this slice used that cleaned environment. Other lanes may hit the same segfault; the cause in the shell environment is unidentified.
- A harness pass is not a Unity pass. The `SimulationDrivers.cs` call site is harness-excluded, so it needs Codex Unity EditMode verification like M1-R1/A1. The driver methods and the new tests are harness-compiled and covered by the full run.
- No Main Scene, scene, prefab, mesh, material or camera/lighting changes. No new asmdefs, event buses or DI frameworks. No serialized enum changes. The new test file carries a fresh-GUID `.meta`; the driver and drive edits touch existing files only.
