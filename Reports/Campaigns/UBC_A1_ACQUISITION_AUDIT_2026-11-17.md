# UBC Lane A1 — Business Acquisition Continuity Audit

- Date: 2026-11-17
- Lane: A1, workstream A (business acquisition continuity)
- Base: `muse/universal-business-construction-2026-11-17` at `a1c20ad9`
- Scope: what actually survives when a player acquires an NPC business, and what breaks.
  The required test scenario is the NPC General Store: the same enterprise must
  keep selling legitimate stock, customer trading must still work, and the store
  must not be silently replaced by a second, synthetic business.
- Method: read on disk. Every verdict below cites the file and method that
  decides it. Nothing here rests on a planning document.

## Verdicts

### 1. Business ID preserved through a sale — HOLDS

`SharedBusinessRuntimeManager.TryTransferBusinessToPlayer`
(`Domains/Economy/Core/SharedBusinessRuntimeManager.cs:2744`) flips ownership
with `business.TransferToPlayerOwnership()` (`BusinessInstanceState.cs:971`,
which calls `SetOwner(BusinessOwnerIdentity.Player())`). `InstanceId` is never
reassigned anywhere on the path. Both acquisition closes
(`AcquisitionMarketManager.CompleteBusinessAcquisition` ~`:9032` and
`TryPurchaseBusiness` ~`:9336`) move the same `BusinessInstanceState` object
onto the listing; there is no delete-and-recreate path. Regression test A pins
this.

### 2. Inventory preserved, no duplication — HOLDS (at the transfer layer)

`CategoryStock` on `BusinessRuntimeState` is untouched by the transfer path,
and `ApplyPostAcquisitionOpeningCash` touches cash only. The generic inventory
positions on `GenericBusinessConfiguration` are instance-bound
(`BusinessInstanceState.GenericConfiguration`) and travel with the business.
Regression test B pins unit counts and category counts across a transfer.

### 3. Cash not duplicated — HOLDS narrowly, but cash conservation BREAKS

What holds: the opening-cash target is `min(previous cash, motive target)`
(`AcquisitionMarketManager.CalculatePostAcquisitionOpeningCashCents`, `:9190`),
so no cash is created at close.

What breaks: the excess `previous cash - opening target` is destroyed.
`ApplyPostAcquisitionOpeningCash` (`:9171`) calls
`runtime.SetCurrentCashCents(openingCash, false)` — `recordNetDelta: false`,
so the removed cash is not even recorded in the net-delta ledger — while the
message claims "seller cash of X was not acquired", implying the seller kept
it. Nothing credits the seller: there is no seller-proceeds path anywhere in
`AcquisitionMarketManager.cs`, and no seller person id is stored on a listing.

The purchase price itself has the same defect. Both closes charge the buyer
through `TrySpend` -> `PlayerPortfolioManager.TrySpendOwnerCash`
(`PlayerPortfolioManager.cs:483`, called from `AcquisitionMarketManager.cs:9364`),
which deducts from player owner cash and credits no one. The price leaves the
simulated economy at every business purchase.

This is recorded, not repaired, in this slice: crediting the seller needs an
owner decision on seller disposition (does the seller stay in town with a
household ledger to credit, or leave with the cash?) and there is no existing
seller cash authority to wire into. Inventing one here would be inventing
money movement. Follow-up for the owner.

### 4. Liabilities preserved — HOLDS

`RuntimeState.AccruedLiabilityCents` (`BusinessRuntimeState.cs:1464`) is
untouched by the transfer path. Seller-financed obligations are recorded
through `RecordSellerFinanceAtClose` into `FinancialObligationAuthority`, so
the buyer inherits the business's accrued liabilities with the business.
Regression test D pins the liability scalar across a transfer. Minor gap: the
sale terms never state the liabilities explicitly (see 9).

### 5. Applicable contracts continue — HOLDS

Transfer agreements are keyed by business instance id, not by owner:
`BusinessTransferAgreementState` carries `sourceBusinessInstanceId` /
`destinationBusinessInstanceId` (`BusinessTransferAgreementModels.cs`), and
`RealDemandContracts` key the seller by `SellerBusinessInstanceId`
(`RealDemandContracts.cs`). Wholesale accounts resolve per business. Nothing
on the transfer path clears or rewrites them. Regression test C pins an
agreement naming the acquired store as destination surviving the transfer.
Caveat: supplier routing uses `FindGeneralStoreSupplierId`
(`SharedBusinessRuntimeManager.cs:788-792`), which returns the FIRST
GeneralStore in the registry — see 8.

### 6. Employment continues per actual terms — PARTIAL, one silent defect

Worker slots and wages are untouched by the transfer, and employment wiring
is per-business: `WireEmploymentRegistryToBusinesses`
(`SharedBusinessRuntimeManager.cs:344`) attaches the shared
`EmploymentRegistry` to each business `RuntimeState`, so the registry
reference survives ownership change.

The defect: `TryTransferBusinessToPlayer` calls
`business.EnsureOwnerOperatorStaffing(formerOwner)` BEFORE flipping ownership,
and `BusinessRuntimeState.TryAssignOwnerOperator` (`:3905`) inserts the
FORMER OWNER into an open worker slot as `owner:{personId}` at the slot wage
when no worker is yet filled — with no agreement, no consent, and no record
in the sale terms. When slots are already filled, nothing happens and the
seller simply leaves. The two behaviours are inconsistent and neither is
stated in the terms. Recorded for follow-up; not changed in this slice, because
the correct behaviour (seller stays on as operator vs leaves) is an owner
decision, not a code repair.

### 7. NPC runtime keeps supplying households after player ownership — BREAKS (highest priority)

This is the top break and the one repaired in this slice.

The acquired General Store is never bound to `GeneralStoreRuntimeManager`.
The acquisition close path never calls `TryBindFormedPlayerBusiness` — its
only caller is `BusinessCreationPanelController.cs:330`, for freshly formed
stores. Consequences, all verified on disk:

- `GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder` (`:1584`)
  returns early when `currentBusiness` is null: the acquired store gets no
  weekly payroll, no weekly reorder, and no owner distribution.
- `ResolveDailySales` (`:1460`) runs on `currentBusiness` only: the acquired
  store gets no daily household-reserve sales resolution.
- The shared weekly pass deliberately excludes GeneralStore from payroll and
  settlement (`SharedBusinessRuntimeManager.WeeklySettlementBusinessTypes`,
  `:1090`, comment: "its payroll/settlement runs through
  GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder"), so nothing else
  pays the store's workers either. Workers go unpaid and the store degrades.
- Meanwhile `DriveDailyRestock` (`GeneralStoreRuntimeManager.cs:2623`)
  resolves the NPC store by instance id with no owner-kind check
  (`ResolveDevelopmentNpcRestockBusiness`, `:1186`), so the NPC restock
  driver KEEPS ordering restock for the now-player-owned store, spending
  business cash without player consent. `ConfigureDevelopmentNpcRestock`
  (`:1174`) refuses player-owned businesses at registration, but registration
  happened at startup when the store was NPC-owned.
- The household trading port DOES survive: `GeneralStoreTradingPort.ActiveBusiness`
  (`GeneralStoreTradingPort.cs:44-46`) falls back to the bound business when
  `manager.CurrentBusiness` differs, so household shopping still reads the
  acquired store's stock.

Net effect: the store is half-alive — sellable stock, autonomous NPC restock,
but no payroll, no weekly reorder, no distribution, no player control. Brief
section 18 scenario E fails for the General Store.

**Repair (this slice):** in
`SharedBusinessRuntimeManager.TryTransferBusinessToPlayer`, after a successful
transfer of a GeneralStore or Generic business, bind it to
`GeneralStoreRuntimeManager` via `TryBindFormedPlayerBusiness` when the
runtime is present and not already managing a different store; surface the
refusal in the transfer message when binding is refused. The repair sits in
the single transfer choke point, so both acquisition closes (and any future
caller) are covered. Binding also fixes the double-driver issue:
`DriveDailyRestock` skips the NPC branch when the NPC business id equals
`currentBusiness`'s id (`:2645`). Regression test E pins the binding and the
weekly resolve running for the acquired store.

### 8. No one-fixed-store-per-world assumption — BREAKS (documented limitation)

`GeneralStoreRuntimeManager` manages exactly one store:
`TryBindFormedPlayerBusiness` (`:1319`) refuses when already managing another
store ("already managing..."), and `TryOpenAtPlayerOwnedShell` refuses
additional locations ("Additional locations are not available yet").
`FindGeneralStoreSupplierId` returns the FIRST GeneralStore in the registry.
A second General Store can exist in the shared registry but has no runtime,
no payroll, and no supplier identity. This bounds the repair in 7: binding
works only when the runtime is free, and the refusal is surfaced in the
transfer message rather than silent. A multi-store runtime is a separate
workstream, not this slice.

### 9. Save/continue preserves the transaction — PARTIAL

The persistence side is ready: `BusinessInstanceSaveDto` includes `saleTerms`
(`BusinessInstanceState.cs:1280`, restored at `:1316`), plus owner, runtime,
and generic configuration. `GeneralStoreRuntimeManager` persists
`currentBusiness` (`:1035`), so a bound store's binding survives save/load.

But the player acquisition path NEVER calls `RecordSaleTerms` — only
`TryTransferBusinessToTown` does (`SharedBusinessRuntimeManager.cs:3072-3073`,
via `BuildSaleTerms`, `:3089`). After a player purchase plus save/load, the
terms are default-empty: the transaction's cash, inventory, and employee
inclusion is silent and unrecoverable. Recorded for follow-up; the repair in
this slice does not add terms recording, because the terms must reflect the
post-close cash state (see 3) and getting that ordering right belongs with
the seller-proceeds decision.

### 10. No hardcoded NPC PersonId assumptions — HOLDS

No hardcoded person-id literals were found in the acquisition paths; owner
comparisons use `OwnerKind` and instance ids. (One equality comparison at
`SharedBusinessRuntimeManager.cs:6903` compares owner person ids
legitimately, as an identity check rather than an assumption.)

## Reverse direction (recorded, not repaired)

Transferring a store TO the town leaves it bound as the store runtime's
`currentBusiness`, so weekly settlement keeps running it as if player-owned.
There is no unbind path on `GeneralStoreRuntimeManager`. This is the mirror
image of 7 and belongs with it in a follow-up; this slice repairs only the
player-acquisition direction.

## Files changed in this slice

- `Assets/Scripts/Domains/Economy/Core/SharedBusinessRuntimeManager.cs` —
  bind the acquired General Store / Generic business to the store runtime in
  `TryTransferBusinessToPlayer`.
- `Assets/Scripts/Tests/Editor/Economy/UBC_A1_AcquisitionContinuityTests.cs`
  (new) — regression tests for brief section 18 scenarios A–G.
- This audit document.

## Verification note (honest)

The Roslyn harness baseline config does NOT compile the acquisition cluster:
`SharedBusinessRuntimeManager.cs`, `GeneralStoreRuntimeManager.cs`,
`PlayerPortfolioManager.cs`, `AcquisitionMarketManager.cs`,
`SimulationSystemsHub.cs`, `SimulationDrivers.cs`, `SaveLoadManager.cs`, and
the test files that exercise them (`ConstructionInputSupportTests.cs`,
`BusinessTransferAgreementTests.cs`, `GeneralStoreRuntimeManagerTests.cs`)
are all excluded from `sources.txt`. Adding the cluster to the harness was
attempted: it fails with 278 errors (missing types across the whole
Unity-coupled layer — `TownWorldController`, `PopulationManager`,
`LandLedgersHUDController`, save DTOs that live in the shim-mirrored
`SaveDtos.cs`, `UnityEngine.SceneManagement`, and others). That is a
harness-coverage gap, not a code defect, and expanding the harness to compile
the whole game is out of scope for this slice.

So the repair is verified by: (a) a full baseline-config harness run on this
worktree showing zero FAIL-set change versus the baseline (the changed files
are outside the compiled set, so the run must be identical); (b) the new
regression tests, which are Unity EditMode tests in the same pattern as
`BusinessTransferAgreementTests.cs` and must be run by Codex in Unity; (c) a
before/after comparison of the expanded-config build error set, confirming the
edit introduces no new diagnostics. The repair itself is a six-line call to
an existing public method at the single transfer choke point, with the
refusal path surfaced in the transfer message.
