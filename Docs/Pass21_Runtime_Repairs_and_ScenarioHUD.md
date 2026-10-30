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

## Final-tree Test Runner result

The GUI count below came from an earlier run and is superseded. Unity was closed before the final-tree batch runs. Fresh Unity 6000.4.2f1 Test Runner runs produced:

| Platform / filter | Discovered | Passed | Failed | Skipped | Evidence |
|---|---:|---:|---:|---:|---|
| EditMode full assembly | 4,226 | 4,031 | 195 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-editmode-full-final-tree-20261030.xml`; SHA-256 `53E48874F4E16D27B96BAECFEA513AFBD85E90EF225ECFFA62EF0334FA54EDB1`. |
| EditMode Sawmill role regression | 1 | 1 | 0 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\sawmill-site-role-final-qualified-20261030.xml`; SHA-256 `51FA3CD8B5C5694EFD7CEFB95C8C6F72ADC33A6AF9C04C8815E5C121DDB61E2E`. |
| PlayMode full available assembly | 10 | 9 | 1 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-final-tree-20261030.xml`; SHA-256 `17A0BA7DA1967BC34AE341456211BDB502496FC583F01252C2D8728D92F303E1`. |
| PlayMode isolated General Store provenance repeat | 1 | 0 | 1 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\general-store-person-provenance-repeat-20261030.xml`; SHA-256 `F3E7B2947CBE4FBA6D557526F6C0C84343E9917C4340036D75CFAB5CCC2355B5`. |

The full EditMode result is red. The final PlayMode run also has one repeatable failure: `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance` records no player-store customer households after one daily sales pass. The test does not establish an affordable household need or guarantee this store wins seller selection. This does not justify fabricated demand; the positive case needs a causally valid fixture or further live-demand evidence. These results are not a 30-day simulation. See `Pass21_TestRunner_Results.md` for detail.

## Final-tree Sawmill site-role correction

The full-run review found that generic shell suitability could allow a Sawmill on a plot without the required `SawmillYard` role. `BusinessProfileDefinition.CanOperate` now refuses a required agricultural Business type when the selected site fails typed-role matching and returns a specific explanation. The targeted matching-yard regression passes, as does the corresponding case in the full EditMode XML. This does not prove end-to-end Sawmill production.

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
