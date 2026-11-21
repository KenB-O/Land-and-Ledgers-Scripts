# Unity Integration Verification and Economic Recovery — 2026-11-21

## Current checkpoint

- **Repository:** `KenB-O/Land-and-Ledgers-Scripts`
- **Branch:** `codex/muse-semantic-integration-2026-11-21`
- **Source HEAD:** `a5300635130ddc619cfcb31ed9bf9f564ef25a2b`
- **Unity version:** `6000.4.2f1`
- **Integration checkpoint:** semantic merge is complete. This cycle did not repeat or rewrite it.
- **Issue ledger:** [complete running Console and issue checklist](CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md). The ledger reports 586 accessible issue identities at its last reconciliation and preserves the 354 baseline warning signatures and 645 original occurrences. The complete original Muse pass-home register was not supplied, so 586 is not represented as the full Muse/Codex universe.
- **Previous integration evidence:** [semantic integration report](MUSE_CODEX_SEMANTIC_INTEGRATION_2026-11-21.md), [Unity verification backlog](MUSE_UNITY_VERIFICATION_BACKLOG_2026-11-21.md), and [Muse register recovery note](MUSE_REGISTER_RECOVERY_2026-11-21.md).

The source branch was clean after the SKU repair commit. This report and the linked checklist update are documentation changes after that code checkpoint.

## Safe Unity environment recovery

The connected Unity CLI reports one ready Editor, Unity `6000.4.2f1`, PID `25468`, open on `F:\Land & Ledgers`. That is the owner's separate checkout, not the integration project. The isolated project `F:\LandLedgers_SemanticIntegration_2026-11-21` has no Unity instance and would require a cold import. No second Editor was launched; the owner's Editor was not closed or modified.

Latest Windows resource reading: 5,985,812 KiB free physical memory and 4,863,204 KiB free commit. Earlier in this cycle, a read-only inspection of the owner Editor found it stopped and not compiling, with 525 persistent objects marked dirty. A later optional diagnostic probe was rejected because it called an unsupported Unity API overload; it made no source or asset change. We retain the earlier valid dirty-object observation and do not infer that the Editor is safe to close. The recovery backup sentinel confirms the existing recovery copies are present. No owner-authored assets or project settings were changed.

**Unity import, project compilation, assembly reload, Test Runner, PlayMode, and Main Scene remain blocked pending a safe owner Editor close and adequate resource headroom.** This is an environment gate, not a claim that Unity rejected the integration source.

## Work performed this cycle

Investigated F-3, exact SKU-specific receiving capacity in the General Store. The existing identified staple receipt path used the combined `staple_food` category count as its capacity test. That count is a compatibility projection and may contain unidentified legacy stock, so category capacity cannot represent bread or flour receiving headroom.

Implemented the correction through existing business and inventory authorities:

- Added an optional receiving-space port for SKU-specific intake.
- The General Store uses each identified staple's authored reorder target and counts its physical SKU units plus reservations as occupied capacity.
- Identified receipt updates item lots and the category projection once, without relabeling unidentified legacy units.
- Paid receipt checks capacity and acceptance before cash is debited.
- Bakery shelf stocking uses exact destination headroom and leaves an excess identified lot on the source shelf for later retry.
- Existing unidentified legacy intake and non-staple category rules retain their previous behavior.

No free goods, cash, teleportation, or fake settlement was introduced. No historical price, release date, tax, banking, or persistent Playtest setting was changed.

## Changed files

Source checkpoint `a5300635130ddc619cfcb31ed9bf9f564ef25a2b` changed:

- `Assets/Scripts/Domains/Economy/Core/BusinessRuntimeState.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreSupplierAdapter.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreTradingPort.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/StoreProcurementChain.cs`
- `Assets/Scripts/Domains/Economy/Businesses/Bakery/Review2BakeryToStoreWholesaleDriver.cs`
- `Assets/Scripts/Tests/Editor/Economy/Pass12CSettledSaleStockTests.cs`
- `Assets/Scripts/Tests/Editor/Population/GeneralStoreBreadPurchaseIntegrationTests.cs`

This report update also edits `Reports/Diagnostics/CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md`, where the stable F-3 identity now retains its new evidence and next action. No other source or asset files are changed by this recovery cycle.

## Compilation and regression evidence

| Gate | Result | Evidence boundary |
|---|---|---|
| Source inspection | Passed | Exact SKU target, projection, capacity, reservation, and paid-receipt paths reviewed. |
| Unity-reference compilation | Passed, runtime 0 errors / 45 warnings; Editor 0 errors / 118 warnings | Unity compiler and reference response files from the installed `6000.4.2f1` toolchain were used against isolated current source. This is not Unity Editor import or compilation. Logs are in `F:\LandLedgers_SemanticRecovery_20261121\ReferenceCompile`. |
| Focused managed regression fixtures | 40 passed, 0 failed | Run using Unity's bundled Mono, NUnit assertions, and compiled current-source assemblies, outside the Unity Editor/Test Runner. Fixture output is retained in the same `ReferenceCompile` directory. |
| Asset-backed General Store regression | Compiled, not executed | `GeneralStoreBreadPurchaseIntegrationTests.StapleReceivingKeepsRoomForBreadWhenLegacyCategoryIsFull` needs Unity asset/object services. |
| Actual Unity compilation / Test Runner | Not run | Owner Editor is attached to another checkout; isolated cold import was not started. |
| Fresh Main Scene startup and physical gameplay | Not run | No claim of current seed, configuration, resident count, business registration, bakery channel, or service behavior. |
| Extended Playtest | Not run | No current-source day count, speed, throughput, economy result, or 60-day result exists. |

The 40 focused fixture results are:

- `Pass12CSettledSaleStockTests`: 7 passed.
- `BakeryToStoreWholesaleTransferTests`: 13 passed.
- `StoreRestockDriverTests`: 11 passed.
- `CampaignBShelfFlourReorderTests`: 9 passed.

The new exact-SKU receipt assertion passed in the standalone `Pass12CSettledSaleStockTests` fixture. The real asset/port regression remains unexecuted. This meets targeted managed regression evidence for the pure authority paths, but it does not meet Unity validation levels 4–6.

## Current configuration and bakery startup evidence

The integrated source report identifies Main Scene's serialized configuration GUID as `f5820dc040b04928a91079ae0c36b461` and the authored development-town bakery configuration as Retail-only with an empty opening wholesale-agreement list. This source-level evidence does not establish which Unity asset instance is loaded in a fresh New Game, the effective seed, runtime wholesale account list, or initial sales-channel decision. The historical day-19 capture is from a different source provenance and is not reused as a current integration result.

**Bakery acceptance remains unverified:** no current Unity startup occurred; no automatic Kennedy's account is either confirmed absent or observed reappearing in this cycle.

## Issue continuity and counts

Counts below describe this recovery cycle's changes and executions. They are not a reclassification of all historical issues.

| Measure | Count | Note |
|---|---:|---|
| Accessible stable issue identities in previous ledger reconciliation | 586 | Ledger states original Muse pass-home source set was incomplete; not a complete universe claim. |
| New issue identities this cycle | 0 | F-3 remains the same identity. No new Console signature or occurrence is claimed. |
| Issues investigated | 2 | F-3 exact SKU receiving; F-6 physical clerk service integration seam. |
| Repair attempts | 1 | One coherent cross-path receiving correction. |
| Implemented repairs | 1 | Source checkpoint `a5300635`. |
| Regression-verified fixes | 1 | Exact SKU authority paths passed targeted standalone current-source regressions; Unity integration pending. |
| Live-verified fixes | 0 | No Unity runtime evidence. |
| Suspected fixes | 0 | F-3 has targeted regression evidence; other items were not reclassified. |
| Unresolved defects | 2 scoped issues remain live-unverified | F-3 end-to-end Unity behavior and F-6 physical clerk arrival/service lifecycle. This is not the total historical unresolved count. |
| Blocked verification gates | 1 environment gate | Owner Unity Editor / memory-safe isolated import. Not counted as a separate product defect. |
| Not reproduced | 0 | No live test was run. |
| Explained legitimate | 0 | No new runtime observation. |
| New regressions | 0 observed | Unity execution is pending; this is not proof of no regressions. |
| Owner design decisions pending | 1 known decision item | Town-pulse spillover acceptance remains unchanged and pending as documented in the backlog. |
| Current cycle Console occurrences | 0 | No Unity test or Playtest generated Console evidence. Historical 645 occurrence count remains retained. |

The prior ledger contains many more open issues than F-3/F-6. This cycle does not close or remove any of them. Historical risks still outstanding include food replenishment and physical purchase/receipt, bakery startup policy, household Firewood/stoves/heating, worker attendance, freight and furniture lifecycle, Save/Continue obligations, warning accuracy and memory growth, DEF-046 double production consumers, the pending spillover decision, and unresolved baseline warning signatures. F-6 still distinguishes its implemented incident feed from unresolved physical clerk completion.

## Current status and next recovery order

1. **Unity integration gate — BLOCKED WITH REASON.** Owner Editor is open on another checkout with dirty persistent objects observed earlier and limited commit headroom. Resume only after it is safely saved/closed and memory/commit is rechecked. Do not terminate the active Editor or import the same project in a competing process.
2. **F-6 physical clerk service — UNRESOLVED, source cause confirmed.** Production binds physical counter service as required and resolves clerks at the building, but the exact counter-arrival callback has no production binding and no retail clerk route caller exists. After safe Unity opening, confirm General Store Counter point resolution/reachability, implement a task-owned route with exact arrival and recovery, and verify one-time settlement and Save/Continue.
3. **F-3 exact SKU receiving — REGRESSION VERIFIED, integrated Unity verification outstanding.** Run the asset-backed receipt test, relevant Test Runner fixtures, then observe bread/flour SKU custody, exact cash movement, category projection, excess shelf custody, and Save/Continue in Main Scene.
4. **F-5/F-4 bakery configuration and diagnostic accuracy — IMPLEMENTED, UNVERIFIED.** In the fresh New Game, record the loaded configuration object/GUID/hash, effective seed, initial channel decision, and wholesale initialization report; confirm no automatic 24-loaf, 4-cent Kennedy's agreement while preserving explicit legitimate contracts and Continued-save obligations.
5. **Household necessities, heating, freight, furniture, and Save/Continue — unresolved register items.** Run the focused Unity fixtures in the backlog, then short 10x live physical verification before any extended test.
6. **15–30 day and then 60-day campaign — not authorized by evidence yet.** Start only after current Unity compile, focused gates, fresh seed-1886 startup, and short physical gameplay acceptance pass. Preserve 250x as the preferred long-run preset; measure actual throughput and collect daily economic/conservation metrics.

## Classification summary

- **Confirmed local integration repair:** F-3 category-vs-SKU receiving integration was corrected in current source and passed focused managed regression fixtures.
- **Unity/live status:** the correction is not yet proven in Unity's asset serialization, Editor/Test Runner, Main Scene, or PlayMode.
- **Campaign status:** partial recovery checkpoint only. The project is not yet certified for the requested Unity playtest or economic durability claim.
