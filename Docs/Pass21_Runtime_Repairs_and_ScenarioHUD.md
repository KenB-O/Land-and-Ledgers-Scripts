# Pass XXI — Runtime Repairs and Scenario HUD

Date: 2026-10-30
Unity: 6000.4.2f1
Branch: `integration/pass12-unity-readiness`
Starting HEAD: `bc29a1ebafe2bb5eff6bbbf1f157639a1be6ae2d`

## Scope and evidence boundary

This pass made focused source and regression changes around sawmill premises/rollback, population death-day persistence, journey condition binding, scenario HUD geometry, and test fixture/environment assumptions. It did not complete the broad gameplay reconstruction requested in the Pass XXI operation. No Main Scene or prefab was serialized during this pass; the existing Canvas binding was already part of the prior project state. The CropFarm profile stock change is an authored asset edit and is separately backed up outside this nested repository.

## Repairs

- Typed premises selection now requires a `SawmillYard` for a Sawmill. Generic first-fit storefront assignment skips agricultural plots, preserving those sites for their typed production uses.
- Shared lumber consumption rollback now captures the legacy construction support node when the seller is a Sawmill. This mirrors the live stock synchronization path so a failed late transaction can restore both views of the same stock.
- Population save capture/restore now includes `deathDayIndex`; the DTO field defaults to `-1` so pre-field saves deserialize as living rather than inventing a death date. A round-trip and legacy JSON regression was added.
- `SimulationSystemsHub.Journeys` reattaches the hub's route-condition provider whenever the model is retrieved, including lazy/EditMode access before `Awake`.
- Scenario objective panel geometry adapts to the bound Canvas size instead of using a fixed 520x380 panel. A fresh Main Scene screenshot shows the active First Ledger panel and its 0/8 objective summary with the $15,000 owner-equity target.
- Test fixtures now bind the Schoolhouse civic authority to their population just as the production bootstrap does, give postal journey tests explicit route edges, and construct/clean up `TimeManager` with its settings installed before activation. Registry tests resolve the active project checkout rather than relying only on an account-specific repository path.
- The ordinary farm-start PlayMode regression now asserts zero opening crop stock before weekly resolution. It verifies no unearned starting stock; it does not prove a growing-season crop lifecycle.

## Player-facing and runtime findings

The First Ledger HUD is visible in `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-main-scene-first-ledger-hud-fit-20261030.png`. This is live runtime evidence, not an authored hierarchy screenshot. The list is still generated at runtime, the content is dense, and Sandbox, failure, victory, reload, and save/load visual cases were not completed. LL-PLAY-005 remains PARTIAL.

No 30-day Main Scene economic reconciliation or day-15 continuation was completed. Daily needs still report absent journey locations; rent and animal feed authority wiring remain open; farming Tasks, mature-season harvest, commercial demand and freight remain unproven in the Main Scene. Person death-date persistence is narrower than full persistent person lifecycle and visual actor identity.

## Current Test Runner result

After the current source compiled in the open Unity 6000.4.2f1 Editor, the GUI Test Runner completed:

| Platform | Discovered | Passed | Failed | Skipped | Evidence |
|---|---:|---:|---:|---:|---|
| EditMode | 4,226 | 4,027 | 199 | 0 | Final GUI count. Fresh XML export did not succeed. The nearby `pass21-editmode-full-current-20261030.xml` is an earlier tree and reports 4,030/196; it is not the final run. |
| PlayMode | 10 | 10 | 0 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-current-final-20261030.xml`, SHA-256 `E8A5E13F4C8130BA957E9DB6499273BE8779B5CCBCC7089B7CF276A004372CDC`. |

The full EditMode result remains red. These totals are not a Roslyn run and do not establish 30-day gameplay. See `Pass21_TestRunner_Results.md` for the evidence limitations and preserved earlier XML.

## Preservation

The pre-edit CropFarm profile and `.meta` are at `F:\Land & Ledgers_Pass21_Backups\checkpoint_20261029_221349\CropFarmProfile_before_opening-stock-repair.asset` and its matching `.meta`. The screenshot, test artifacts and other Pass XXI evidence are under `F:\Land & Ledgers_Pass21_Backups\pass21-reports`. No scene, prefab or ProjectSettings file was modified.

## Next verified work

1. Export and inspect the exact 199 current EditMode failures, separating regressions, fixture/environment assumptions, and established defects.
2. Complete the alternate-scenario/Continue ownership matrix and author a persistent, inspectable scenario HUD hierarchy if the controller/UI architecture permits it.
3. Trace and register real daily-needs journey destinations, then verify an actual household trip and purchase.
4. Bind rent and animal feeding through existing financial and inventory authorities.
5. Continue Person lifecycle identity/day-night/save-load work.
6. Implement crop stage Tasks only after the runner can repeatedly verify them; then prove a seasonal cycle, legitimate buyer/freight, and save/load.
7. Complete RUN-004 with actual Main Scene state and scoped monetary/physical reconciliation.
