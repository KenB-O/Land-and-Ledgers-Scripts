# Pass XXI — Unity Test Runner Results

Date: 2026-10-30
Unity: 6000.4.2f1
Source tree: `integration/pass12-unity-readiness` at `6fbda31c505cf04fbbb917e56620937aa45194d3`, based on published `bc29a1ebafe2bb5eff6bbbf1f157639a1be6ae2d`. The final site-role correction was present in the source tree used by the batch Test Runner. The two earlier Pass XXI commits and this correction remain local and unpublished because the overall suites are red.

## GUI baseline

The existing graphical Unity Editor completed an initial full pass before the final Sawmill site-role correction:

| Run | Total | Passed | Failed | Skipped | Result artifact |
|---|---:|---:|---:|---:|---|
| EditMode full assembly | 4,226 | 4,027 | 199 | 0 | GUI count only. |
| PlayMode full available assembly | 10 | 10 | 0 | 0 | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-current-final-20261030.xml` |

PlayMode XML SHA-256: `E8A5E13F4C8130BA957E9DB6499273BE8779B5CCBCC7089B7CF276A004372CDC`.

The GUI run is superseded by the fresh XML runs below. The previous GUI PlayMode 10/10 is not used as final-tree evidence.

## Final-tree batch Test Runner runs

Unity was closed before the batch runs; the installed Unity CLI launched the required 6000.4.2f1 Editor. These are fresh Unity/NUnit results, not Roslyn results.

| Platform / filter | Total | Passed | Failed | Skipped | Duration | Result artifact |
|---|---:|---:|---:|---:|---:|---|
| EditMode full assembly | 4,226 | 4,031 | 195 | 0 | 274.604 s | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-editmode-full-final-tree-20261030.xml` |
| EditMode Sawmill role regression | 1 | 1 | 0 | 0 | 0.024 s | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\sawmill-site-role-final-qualified-20261030.xml` |
| PlayMode full available assembly | 10 | 9 | 1 | 0 | 79.801 s | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-final-tree-20261030.xml` |
| PlayMode General Store provenance test, isolated repeat | 1 | 0 | 1 | 0 | 22 s | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\general-store-person-provenance-repeat-20261030.xml` |

SHA-256:

- Full EditMode: `53E48874F4E16D27B96BAECFEA513AFBD85E90EF225ECFFA62EF0334FA54EDB1`
- Sawmill regression: `51FA3CD8B5C5694EFD7CEFB95C8C6F72ADC33A6AF9C04C8815E5C121DDB61E2E`
- Full PlayMode: `17A0BA7DA1967BC34AE341456211BDB502496FC583F01252C2D8728D92F303E1`
- Isolated General Store repeat: `F3E7B2947CBE4FBA6D557526F6C0C84343E9917C4340036D75CFAB5CCC2355B5`

The full EditMode exit was `TESTS_FAILED`; its XML contains all 4,226 cases and the exact 195 failures. The Sawmill matching-yard case passes in the full XML and in its separate one-case run. The prior Sawmill failure was a source defect: generic shell suitability had allowed Sawmill operation on a non-Sawmill agricultural plot. The final evaluator now rejects agricultural Business types unless the plot/building relationship has the required role.

The final full PlayMode result has one failure: `GeneralStoreDailySaleKeepsRealHouseholdAndPersonProvenance`. The isolated repeat fails on the same assertion: zero player-store customer households after one `ResolveDailySales` call. Its test setup forms, sites, staffs and binds a store, then calls one daily sale pass; it does not establish an affordable unmet household reserve need or ensure that this store wins a real seller comparison. Production only sells to an embodied household with actual needs, spendable ledger funds, available stock, and a successful charge. Do not interpret this as permission to fabricate demand. The positive scenario remains open pending a causally valid fixture or additional live-demand evidence.

The full PlayMode run includes the Main Scene scenario startup, First Ledger four-week honest progress, winter farm field registration/no unearned crop stock, and save/load tests; these pass in the final-tree run except for the one customer-sale case. This is automated PlayMode evidence, not a manually observed full-day life path or a 30-day reconciled simulation.

## Limits

- Unity CLI Pipeline was not installed in the GUI Editor. After that Editor was closed, CLI `unity test` ran the current tree successfully enough to write fresh NUnit XML. The full EditMode run exited with test failures rather than an infrastructure timeout.
- A first attempt at the Sawmill filter used an incorrect namespace and wrote a valid zero-case XML; it was discarded. The qualified rerun selected exactly one NUnit case and passed.
- No full suite failure was suppressed or removed to obtain these counts.
- These totals do not validate Sandbox/Continue ownership matrix, complete Person day/night behavior, rent, animal feeding, crop Tasks/freight, the First Vein scenario, or RUN-004.
