# Pass XIV production verification

Date: 2026-10-29

## Starting checkpoint

- Scripts branch: `integration/pass12-unity-readiness`
- Starting SHA: `2edeaf1143c2c97aa5cc6dd19c6a6c6136ffa2c5`
- Unity: `6000.4.2f1`
- Unity authored-project backups remain under `F:\Land & Ledgers_Pass13_Backups`.

## Corrections in this checkpoint

- `SaloonCircuitBootstrap` now binds same-scene production dependencies, constructs its evaluator before restore, and supports late EditMode/runtime awakening without dropping saved scenario state.
- Town generation reserves a civic-sized lot before ordinary lots are created. This preserves the Town Hall prefab footprint instead of shrinking or bypassing the world-grid authority.
- Town Hall presentation distinguishes `Not built` from `Blocked` while retaining contribution values; ready civic state is labeled `Built | Ready`.
- Combined civic contribution wording uses the established `Maturity` label.

## Unity evidence

- `pass14-bootstrap-civic-final-3.xml`: 14/14 passed for `Pass11Ws6FirstGateBootstrapTests|TownHallFoundationTests`, including both M1/M2 bootstrap tests and all Town Hall tests.
- `pass14-production-playmode-after-civic.xml`: 0/2 passed because both tests reached their assertions through the ordinary Main Scene. The farm test observed zero crop stock; the First Ledger test observed the authored completion predicate still false. The runner completed normally and wrote XML.
- Earlier focused baselines remain 17/17 Household reserve, 126/126 Pass XII focused EditMode, 8/8 scenario presentation, and 31/31 finance/opportunity.

## Remaining production blockers

### Farm

The Main Scene forms a CropFarm and hires a farm hand, but does not register a `CropFieldState` in the live `CropFieldAuthority` or create/assign crop-stage Tasks. `FarmCycleDriver` intentionally produces no goods without completed `plant-field`, `tend-field`, and `harvest-field` Tasks, and the opening date is winter. No free harvest or inventory injection was added.

The correct next repair is a production caller that maps an actual agricultural plot to a persistent field, obtains real seed/equipment, creates legitimate CropChain Tasks, and lets TaskAuthority completion drive harvest and logistics.

### First Ledger

The ordinary path forms three player Businesses and hires three workers, but after four simulated weeks the live assertion remains below the authored 1,500,000-cent owner-equity completion target. Victory was not forced. The next investigation must identify which legitimate valuation, production, sales, or time path is missing before changing scenario thresholds.

### Main Scene runtime

No visual Editor inspection or screenshot was completed in this environment because the Unity GUI remains unavailable behind the existing Editor.log rotation/access condition. The automated PlayMode runner did complete and produce result files.
