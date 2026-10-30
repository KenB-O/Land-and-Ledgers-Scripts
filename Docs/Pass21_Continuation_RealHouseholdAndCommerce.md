# Pass XXI Continuation — Household Identity, Funded Commerce, and Final-Tree Tests

Date: 2026-10-30  
Unity: 6000.4.2f1  
Branch: `integration/pass12-unity-readiness`  
Starting HEAD: `794da8bed678028ad853f158979c4d9e26348c18`

## Changes

Scenario activation previously treated an empty founder-name list as “no founder,” even though the generated world already contained real people and households. It also passed a generic declaration ID instead of the selected scenario ID. `ScenarioDirector` now asks the injected resolver for real generated founders when the list is intentionally empty, binds the declared player household to the selected scenario, and records success only when `HouseholdLifecycle` returns an actual household.

`HouseholdLifecycle.DeclarePlayerHousehold` now adopts an existing household when all declared founders are already active co-residents. It refuses to merge members from different households, to adopt a person whose current membership conflicts with the founder set, or to overwrite another scenario’s existing player-household assignment. It does not manufacture people, household records, or membership transitions.

Feed and farm-construction purchases now validate positive price and affordability before asking finite suppliers to decrement stock. Orders are capped at the buyer’s actual cash capacity. Payment refusals do not credit the farm/feed inventory or construction project, and negative tests verify that an unfunded buyer leaves supplier inventory untouched. Positive test fixtures record a documented owner contribution before purchasing; the original un-funded setup was not treated as legitimate working capital.

The Main Scene sale-provenance PlayMode fixture was corrected to exercise its actual production prerequisites: it transfers owner cash through `PlayerPortfolioManager`, assigns premises and staffing through normal formation authorities, runs the shared weekly procurement callback, verifies real staple inventory, then exercises the sale path. This is a test-fixture correction supported by the neighboring production-path test and the runtime’s no-free-opening-stock rule; no stock or revenue is injected.

## Fresh Unity evidence

All XML files below were exported from the open Unity 6000.4.2f1 Test Runner and are outside the Unity project at `F:\Land & Ledgers_Pass21_Backups\pass21-reports`.

| Suite | Result | Fresh XML | SHA-256 |
|---|---:|---|---|
| Full EditMode | 4,044 / 4,236 passed; 192 failed; 0 skipped | `pass21-editmode-full-latest-20261030.xml` | `7118FC459AF3FFB3FF088B119B171855E3EA70E6F470F4D13931ADA256B9394E` |
| `PlayableScenarioStartupPlayModeTests` | 11 / 11 passed; 0 failed; 0 skipped | `pass21-main-scene-focused-post-funding-20261030.xml` | `CC1AC6EDACAEF4E935D9668D844374F488A50B331F02A463A5F7F4357B3F5904` |
| `HouseholdLifecycleTests` | 10 / 10 passed | `pass21-household-lifecycle-20261030.xml` | `366C9CCE4D15EB9B0F8C26DF1552BFB1E741DAA9C74FDB018A2E11C83C5D2521` |
| `FarmIntegrationTests` | 13 / 13 passed | `pass21-farm-integration-after-transaction-fix-20261030.xml` | `16DFEEF88C5CF818603066850700A86EA6C730CD333BCF4175BF7764DE90E57C` |
| `BoardingRentCollectionTests` | 8 / 8 passed | captured in the full EditMode XML | — |
| `DailyNeedsServiceTests` | 5 / 5 passed | captured in the full EditMode XML | — |
| `P2HouseholdNeedsJourneyTests` | 8 / 8 passed | captured in the full EditMode XML | — |
| `BirthdayChronologyTests` | 7 / 7 passed | captured in the full EditMode XML | — |
| `LogisticsConservationGateTests` | 16 / 16 passed | captured in the full EditMode XML | — |
| `GeneralStoreEconomyRepairTests` | 26 / 31 passed; 5 failed | `pass21-general-store-economy-after-latest-20261030.xml` | `FA0B6763AAE75D08D673DCA328C7572CB22D5935027FC6635BC30B57187C3808` |

The complete EditMode run completed on the current source tree. It is not a passing baseline: the largest failure clusters are `ConstructionInputSupportTests` (46), `TownWorldFootprintSaveLoadTests` (11), `AcquisitionVpFlowTests` (8), `OwnershipWorkflowUiRepairTests` (8), and `PopulationHealthEconomyTests` (7). Five failures are the long-standing General Store tests noted below. No tests were removed or disabled.

## General Store failures reviewed

The fresh 31-test run reproduces five existing assertions. They were not changed because there is no documented economic rule establishing the tests’ assumed thresholds over the current implementation:

1. Early-store reserve: expected at least 48,000c, observed 44,000c. The test assumes a 12,000c fragility increment; current code adds an 8,000c early-store reserve and adds separate 4,000c increments only when the corresponding weakness/shortage conditions are present. This fixture does not establish those additional conditions.
2. Two wholesale-price assertions: expected at least 108c from a 100c landed cost; current pricing code explicitly computes a 6% survivability floor (106c) and then constrains it against transfer and retail prices. Raising the floor to meet the tests would alter price policy without a Canon or approved decision supporting that change.
3. Low-cash triage: the test creates no `LogisticsRuntimeManager`, while the production reorder path returns before shipment creation when logistics is absent. It then asserts immediate shelf inventory, although the method queues inbound freight rather than bypassing custody. This is an invalid fixture for testing delivery and should be replaced by an authority-complete shipment test in a separate focused change.
4. Reorder budget: the helper uses the explicit 25,000c post-reorder cash buffer. At 20,000c available it correctly returns zero; the assertion for 5,000c reflects a lower buffer than the current API’s declared default. No policy or production amount was changed to make the test green.

These tests remain OPEN. The production code’s arithmetic and fixture dependencies should be reconciled against a governing economic decision before updating either policy or expectations.

## Scope limits

The focused PlayMode suite demonstrates scenario household assignment, funded General Store procurement/sales provenance, winter farm registration, honest First Ledger progress, and existing save-roundtrip cases. It does not prove a manually observed 30-day Main Scene economy, a complete growing-season crop cycle, the positive daily autonomous shopping loop, night-cycle visual persistence, or day-15 Main Scene continuation. No scene or prefab was modified during this continuation.

The latest captured visual reference remains `pass21-main-scene-first-ledger-hud-fit-20261030.png`; it shows the visible objective panel but also its still-limited runtime-generated layout. The current test results are programmatic PlayMode/ EditMode evidence, not a new hands-on visual play session.
