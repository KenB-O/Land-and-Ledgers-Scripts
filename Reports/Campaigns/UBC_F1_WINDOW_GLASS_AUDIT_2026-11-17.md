# UBC F1 audit - off-map manufactured window glass

Date 2026-11-17. Lane F1, workstream F glass leg. Commit `debc98cf` on branch `muse/ubc-f1-window-glass-2026-11-17`, base integration tip `ab81d0bf`. One-shot lane report, no Unity verification claimed.

## What was built

Glass is a real manufactured good, import only, with no local glassmaking industry in this pass and no bootstrap endowment. A yard opens with zero panes and the first glazed build waits 14 days for the import. The chain is definition, price path, inventory identity, quantity, custody, transport, delivery, consumption.

- One pane equals one window light, single-strength common window glass, about 8x10 to 10x12 in. One window takes one pane in these designs.
- Supplier is Pittsburgh glassworks via the railhead, 220 miles, 14 days transit. Import material id `glass-panes`.
- The lumber yard is the only glass stockholder and the named supplier (`lumber-yard`). Stock is FIFO lots carrying order id, origin, unit cost and provenance chain. Intake refuses anonymous orders and anonymous origins.
- The driver orders only when covered stock (on hand plus outstanding) is at or below the threshold, one order at a time, paid from the yard's real business cash or honest credit on the liability ledger. No cash and no credit refuses with nothing created, no order, no lot, no payable.
- Price comes from the first stocked glass lot's real cost, never invented. The price port returns -1 with no stock, so a glazed build cannot be priced, estimated or sold without real glass on the shelf.
- A glazed build appends the glass requirement to the openings phase (`Framing - roof`, sequence 2) in the three production designs. The commissioner prices it into the estimate and refuses a glazed evaluation when glass is unpriced. The yard sale is a three-bucket atomic sale (lumber, hardware, glass); a shortfall in any bucket refuses the whole sale and consumes nothing. A voided sale restores the panes with provenance intact.
- Unglazed is the default everywhere. `GlazedWindows` defaults false on the request, the executor overload, the commissioner evaluation and the commission project call. No universal mandatory glass.

## Provisional calibrations for the owner to correct

- Price 25c per pane. Anchor is BLS Bulletin 440, Wholesale Prices 1890-1926, window glass single B f.o.b. works per 50-square-foot box. At 1890s wholesale of roughly $2-3 a box, a 10x12 light (0.83 sq ft) runs about 3-5c at the works, and a fragile manufactured pane landed 220 miles by rail, with breakage, handling and yard margin, lands well above works price, inside the game's band (lumber board 40c, nails unit 30c import, simple hardware 45c yard). The exact 1890 figure could not be read reliably from the FRASER OCR text (columns scrambled), so the price is documented as a calibration, not a quoted price.
- Reorder threshold 4 panes (two family cabins' worth), order lot 12 panes (about six cabins' worth, a box of lights). Single-outstanding-order guard, Millbrook pattern.
- Window counts, mirroring D2's provisional door and window counts. Settler cabin 1 window, family cabin 2, carpenters workshop 2. Unknown designs have no recorded window count and a glazed commission on one is refused, never guessed.

## Section 18 evidence

- T, glass imports require legitimate purchase and delivery. Driver tests place a cash order of 12 panes at 25c arriving day 14 with 300c debited from yard cash and zero panes before arrival, hold the order in transit with no early arrival and no double order, land it as one provenance lot carrying order id and origin, refuse with nothing created when there is no cash and no credit, place on honest credit with a 300c payable when a liability ledger exists, and round-trip the order book through save and load without reissuing an order id while the due order still arrives.
- U, glass does not appear at building automatically. The yard opens with zero panes and no bootstrap path. Unglazed work packages carry no glass requirement. A glazed build on a design with no recorded window count refuses, and a glazed build with no openings phase refuses. The price port returns -1 with no stock. The commissioner refuses a glazed evaluation when glass is unpriced.
- Negative test. `Adapter_GlassRequirementWithoutStock_RefusesNothingSold` builds the glass requirement for a settler cabin against an empty yard, the adapter refuses, no sale is recorded and the glass stock stays zero. The matching positive test sells one pane at 25c with the import order on the provenance.

## Verification

- Full harness run in the worktree, cwd the worktree root, `LAND_LEDGERS_REPO` set to the worktree Scripts dir. **5096 passed, 0 failed, 5096 total.** Baseline at `ab81d0bf` was **5068 passed, 0 failed, 5068 total**. Delta is plus 28 new tests, zero new failures, FAIL set empty in both runs. All 28 `WindowGlassImportTests` pass. Logs `/home/hatch/ll-ubc-f1-build2.log` and `/home/hatch/ll-ubc-f1-run3.log`.
- First run of the new tests was run from `/home/hatch` instead of the worktree root and failed 141 tests, all repo-file readers that resolve the repo root by walking up from the working directory. That was a runner invocation error, not a code failure. Re-running from the worktree root cleared all 139 environmental failures. Two further failures were real test bugs, the custom designs had no accommodation spaces, fixed by adding a `main room` space spec to each.
- One full run aborted mid-way (exit 134, SIGABRT) after 3585 passes and 0 failures, no error text in the log. Same shape as the earlier service-restart kill of the baseline attempt. The re-run completed 5096 and 0.
- Expanded build check of the four harness-excluded files (`SystemsSaveDtos.cs`, `SimulationSystemsHub.cs`, `FirstLedgerSliceBootstrapper.cs`, `SimulationDrivers.cs`). The expanded csproj adds those four files to the harness includes. Result is **120 unique errors, zero on any changed line, zero naming any new type**. The four files carry 235 insertions and 0 deletions in this lane, pure insertions only, and there are no CS0104 ambiguity errors, so no inserted line can be the source of an error on an existing line. The 120 count matches D3's verified pre-existing set. Log `/home/hatch/ll-ubc-f1-build-exp.log`, unique errors `/home/hatch/ll-ubc-f1-exp-errs-u.txt`, base-mapped errors `/home/hatch/ll-ubc-f1-exp-errs-base-mapped.txt`.
- A pristine-baseline expanded build was attempted but blocked by an intermittent disk quota fault (EDQUOT on bulk checkout, 47G free on the volume, same fault class as the 17:4x incident this session). The line-mapping proof above stands in its place. The fault also truncated `SimulationSystemsHub.cs` to 0 lines once during editing earlier in the session; the file was restored from HEAD and re-edited, verified at 48 insertions, 0 deletions, 2222 lines.
- Pass9 accounting. The Pass9 registry tests pass, but note the scan root falls back to the main repo in both the baseline and this run, because `Docs/UnwiredSystems.md` exists nowhere (not in the worktree, not in the main repo), so `ResolveRepoRoot` returns the main repo path. The scan therefore did not cover the F1 worktree. The accounting was verified by hand instead. Baseline-listed in `FeatureInternalBaseline` are `LumberYardGlassDispenseLine`, `LumberYardGlassStock`, `LumberYardGlassReorderPolicy`, `LumberYardGlassReorderSignal` and `WindowGlassRequirements`. `LumberYardGlassLot` needs no entry because it is accounted by the price port in `Orchestration/Systems`, an outside-dir production caller. `LumberYardGlassImportDriver` needs no entry because it is constructed by the bootstrapper and referenced by the hub, the bootstrapper, `SimulationDrivers` and `SystemsSaveDtos`, all outside-dir callers.
- No Unity verification is claimed. Source tests are not Unity compilation, Test Runner or PlayMode evidence. The four harness-excluded files need Codex Unity EditMode runs on the final checklist, as do A1, A2, M1, I1 and B1.

## Files changed

New, each with a fresh `.meta` GUID.

- `Domains/Economy/Businesses/LumberYard/LumberYardGlassLots.cs`
- `Domains/Economy/Businesses/LumberYard/LumberYardGlassReorderPolicy.cs`
- `Domains/Economy/Businesses/LumberYard/LumberYardGlassImportDriver.cs`
- `Domains/Economy/Businesses/Construction/WindowGlassRequirements.cs`
- `Tests/Editor/Economy/WindowGlassImportTests.cs`

Edited.

- `Domains/Economy/Trade/ImportService.cs`, D3 `PlaceOrderForBusiness`, `NextOrderNumber`, `LoadOrders` and `ParseOrderNumber` ported verbatim
- `Domains/Economy/Businesses/LumberYard/LumberYardSupply.cs`, glass import registration
- `Domains/Economy/Businesses/Construction/ConstructionSupportNodeDefinition.cs`, `ConstructionResourceKind.Glass = 3` appended
- `Domains/Economy/Businesses/LumberYard/LumberYardConstructionSales.cs`, glass lines and provenance rendering
- `Domains/Economy/Businesses/LumberYard/LumberYardShopRuntime.cs`, glass stock, three-bucket sale, void restock, save DTO
- `Domains/Economy/Businesses/LumberYard/LumberYardConstructionSaleAdapter.cs`, glass bucket
- `Orchestration/Systems/LumberYardConstructionPricePort.cs`, glass priced from first stocked lot
- `Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs`, glazed work-package overload
- `Domains/Economy/Businesses/Construction/NpcCommissionedBuild.cs`, glazed evaluation and commission overloads
- `Domains/Economy/Businesses/Construction/NpcConstructionDriver.cs`, `GlazedWindows` request field
- `Tests/Editor/Integration/Pass9RegistryAssemblyScanTests.cs`, baseline entries
- `Infrastructure/Persistence/SystemsSaveDtos.cs`, glass driver save field
- `Orchestration/Systems/SimulationSystemsHub.cs`, driver register, save capture, deferred restore
- `Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`, import wiring and restore call
- `Orchestration/Systems/SimulationDrivers.cs`, daily glass import drive
- Shared harness shim `/home/hatch/ll-ubc-harness/Shims/WorldExtracts.cs`, `Glass = 3` added to the `ConstructionResourceKind` extraction (S3 precedent for shared-shim fixes, on disk, not in the repo)

## Merge notes for the parent

- D3 is not in the F1 base, its ImportService additions were ported verbatim, so the D3 merge should land identical hunks. Watch the hub restore instead. D3 adds `LoadHardwareDriverSectionsFromSaveDto`, F1 adds `LoadGlassDriverSectionFromSaveDto`, both called from `ApplySawmillRestoreAfterPass9`. The glass line can be folded into D3's method or left as its own method. The order book has no yard-stock dependency, so either restore path is correct.
- The F1 harness copy gained a sources line for `WindowGlassRequirements.cs` because the Construction dir is file-listed, not a directory entry. The shared `/home/hatch/ll-ubc-harness/sources.txt` needs the same line before the integration harness runs this code, and the shared shim already carries `Glass = 3` as noted above.
- `ConstructionResourceKind.Glass = 3` was added to both the real enum (harness-excluded `ConstructionSupportNodeDefinition.cs`) and the shim mirror. The two must stay in sync.

## Residuals and open items

- The openings phase is the roof phase (`Framing - roof`) until a later H-lane splits a dedicated openings phase, per the E2 note. Glazing rides with D2 door hardware there for now.
- Alternatives in the evaluator are priced lumber-only. A glazed alternative path does not exist yet; glazing is a commission-time choice, not an alternative branch.
- The glass driver restores through its own deferred method `LoadGlassDriverSectionFromSaveDto`. The parent may fold it into D3's hardware restore method.
- No local glassmaking industry was created, as directed. A future glassmaking lane would need a furnace, sand and soda supply, and a glazier skill, none of which exist here.
- The carpenter wage is still unruled and the bed margin question is untouched by this lane. Hay still has no priced supply.
