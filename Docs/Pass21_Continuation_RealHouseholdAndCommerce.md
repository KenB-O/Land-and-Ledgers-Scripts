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

The focused PlayMode suite demonstrates scenario household assignment, funded General Store procurement/sales provenance, winter farm registration, honest First Ledger progress, and existing save-roundtrip cases. It does not prove a manually observed 30-day Main Scene economy, a complete growing-season crop cycle, the positive daily autonomous shopping loop, night-cycle visual persistence, or day-15 Main Scene continuation. The initial continuation authored the Civic Foundation; the later HUD work below also modifies the Main Scene, with pre/post scene copies preserved outside the project.

The latest captured visual reference remains `pass21-main-scene-first-ledger-hud-fit-20261030.png`; it shows the visible objective panel but also its still-limited runtime-generated layout. The test results above are programmatic PlayMode/EditMode evidence.

## Continuation checkpoint — 2026-10-30

The source branch was rechecked at `f0dab5c4bf7c7244691e536e6bb5232e52bb2f33`; `origin/integration/pass12-unity-readiness` matched exactly before these uncommitted continuation changes. Unity 6000.4.2f1 completed a fresh Bee/ILPP compile after the routing and construction edits. The Editor Test Runner discovered 4,237 EditMode tests, including the new route regression.

Two additional source corrections were made:

* `JourneyModel.FindRoute` sorts eligible route locations and outgoing edges by stable identifiers before equal-cost alternatives are resolved. This removes registration-order-dependent route choices without changing route cost. `JourneyModelTests` passes 11/11, including the new reverse-registration-order case. Fresh XML: `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-journey-model-final-tree-20261030.xml`; SHA-256 `5464243A01A9D07D5982C75064F0458BD1A3128DCE9ACB8A9686512ABC7D43DE`.
* Construction readiness no longer treats a prior weekly blocked-reason string as a current prerequisite after the actual materials and builder capacity recover. Fresh `ConstructionInputSupportTests` reports 49 passed / 45 failed of 94; the added recovery assertion passes. XML: `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-construction-support-final-tree-20261030.xml`; SHA-256 `B38C8A9018FDBFD82C84C058D7D3FAB599BA84EF3C86EFF35A0C6C81673BBA3C`. The class remains substantially failing; this is not a green suite.

The Main Scene now has one authored `CivicFoundationManager` component on the existing `Game Systems` GameObject. The pre-edit scene and `.meta` are preserved at `F:\Land & Ledgers_Pass21_Backups\20261030_continuation_scene_pre_civic`; the saved post-edit scene and `.meta` are at `F:\Land & Ledgers_Pass21_Backups\20261030_continuation_scene_post_civic`. The active scene contains exactly one `CivicFoundationManager` script reference; its `.meta` GUID was preserved. The existing bootstrap's shared `FindOrCreate` path resolves the authored manager rather than creating a second one.

Manual GUI Play Mode was entered in the actual Main Scene. Startup logs show the First Ledger scenario start panel bound to the authored `UI Canvas` (Screen Space Overlay, sorting order 100). Clicking “Play First Ledger” activated the authoritative `first-ledger` scenario; the HUD became visible at the upper-right and showed its objective list. This confirms that the current runtime panel becomes visible after scenario activation, but its panel and objective content are still created dynamically. Sandbox, failure, victory, save/load and reload visual cases were not captured. No screenshot file was written from this GUI observation.

The saved scene SHA-256 is `F9F4044563B2B58C67CF0E43399A0FB94FCB8ED8305DE7BEF711307A54AF6CAB`; the scene `.meta` SHA-256 remains `841B50C2925B4A0B459AF03D63E63F992FAFD72C547618DDCE9322A5E43BACE5`. The editor emitted repeated HDRP building-window BoxCollider negative-scale warnings during startup; these prefab/asset warnings remain unrepaired.

### Updated disposition

* LL-PLAY-003 moves to PARTIAL: the missing authored civic component was added and the shared creation path is idempotent, but an expanded runtime hierarchy count was not captured.
* LL-PLAY-005 remains PARTIAL: active First Ledger HUD visibility is now directly observed in GUI Play Mode after scenario activation. The panel is still runtime-generated and broader scenario/persistence visual states remain unverified.
* New LL-PLAY-027 records the construction readiness self-lock. The current predicate fix is covered by a passing regression assertion, but the containing 94-test class remains 45/94 failing and requires failure-cluster review.

These edits are not committed yet. The remote still points to the verified starting commit; this working tree is not published.

## Continuation — authored scenario HUD, ownership projection, fresh verification

The actual Main Scene now serializes a dedicated scenario objective panel beneath its existing `UI Canvas`. `Editor/ScenarioHudSceneAuthoring.cs` provides an idempotent authoring command, and `ScenarioStartPanelController` binds the authored panel, label, viewport, and toggle to the existing `ScenarioDirector`/`ScenarioObjectiveService`. The panel presents live required goals, latched completion, failure/sandbox status, and dated-qualifier deadlines through `ScenarioObjectivePresentation`; it does not maintain UI-owned progress. A compatibility runtime fallback remains only for scenes with incomplete authored references and emits a warning.

The saved scene changed from SHA-256 `F9F4044563B2B58C67CF0E43399A0FB94FCB8ED8305DE7BEF711307A54AF6CAB` before HUD authoring to `817F22A74FACDC20A2F0E2B36451AA1293571E11041484078823B25E952261B9` after authoring. `Assets/Main Scene.unity.meta` retained SHA-256 `841B50C2925B4A0B459AF03D63E63F992FAFD72C547618DDCE9322A5E43BACE5`. Pre/post scene and metadata copies are in `F:\Land & Ledgers_Pass21_Backups\20261030_scenario_hud_pre_authoring` and `F:\Land & Ledgers_Pass21_Backups\20261030_scenario_hud_authored`.

Fresh Test Runner evidence on the current source tree:

| Suite | Result | Artifact | SHA-256 |
|---|---:|---|---|
| `PlayableScenarioStartupPlayModeTests` | 11 / 11 passed; 0 failed; 0 skipped; 79.94 s | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-scenario-hud-final-playmode-20261030.xml` | `3752F735B0EB7FF6EA2CC070CE29DF909865423CE1617FF4FC608C8260C231CD` |
| `GeneralStoreEconomyRepairTests` | 29 / 34 passed; 5 failed; 0 skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-general-store-economy-post-identity-guard-20261030.xml` | `EC5AF67AB8F947793496EA1EE2120BC55B290A62AEC5A4272884756605F1A833` |

The three Business-panel projection regressions pass: a coordinator without a Business yields no player holding row/count; an identityless Player-owned store shell is also rejected; one real player-owned General Store is represented once. A manual GUI Play Mode pass on the actual Main Scene showed the First Ledger start card, then the active `first-ledger` objective panel after selection; its header collapsed and expanded on click. Unity returned to Edit Mode and the authored scene hash remained unchanged. This is direct visual inspection, but no screenshot image file was exported from the desktop capture, and sandbox/failure/victory/save/load HUD visuals remain unverified.

The five existing General Store failures reproduced in the fresh 34-case suite: the protected-reserve target (44,000c versus 48,000c), two wholesale-price floors (106c versus 108c), low-cash triage shelf inventory (0 versus positive), and reorder budget (0 versus 5,000c). No economic policy or assertion was altered. In particular, inventory cannot appear through a queue without logistics delivery; the triage test fixture's missing logistics authority is a fixture defect, while reserve, price-floor, and reorder-buffer expectations remain policy/fixture questions to reconcile against governing decisions.

At this continuation checkpoint, local and remote integration refs still match `f0dab5c4bf7c7244691e536e6bb5232e52bb2f33`; edits remain uncommitted. Fresh compilation and PlayMode execution completed in Unity 6000.4.2f1. This does not claim a full EditMode green baseline, a 30-day economic proof, complete Business formation, or general playtest readiness.

## Continuation — membership persistence and fresh-world parcel wiring — 2026-10-30

An independent source review identified two connected persistence/startup risks and one malformed-save edge case. Household lifecycle changes updated the authoritative `HouseholdMembershipRegistry`, while older runtime consumers still read `PersonState.householdId` and `HouseholdState.memberIds`. Population saves also reconstructed membership from those legacy fields instead of retaining ended membership history. The population save DTO now captures a deep copy of the registry, restores it when present, retains the legacy-copy migration when the DTO field is absent, and projects active authoritative membership back into the legacy fields after lifecycle changes, save capture, and load. A maximum persisted membership ID now leaves the allocator exhausted at `int.MaxValue` with a warning instead of overflowing to a negative cursor.

The Main Scene’s `SimulationDrivers.Awake` could attach parcel services while the authored TownWorld had not generated its grid. The New Game bootstrap now calls the existing driver's idempotent `Rebind` immediately after successful world generation. `Rebind` also reattaches the current parcel grid and title lookup. A new Main Scene PlayMode assertion verifies reference identity between the generated TownWorld grid and `SimulationSystemsHub.Parcels.Grid`.

Fresh Unity 6000.4.2f1 evidence on the current final source tree:

| Suite | Result | Artifact | SHA-256 |
|---|---:|---|---|
| `HouseholdMembershipAuthorityTests` | 10/10 passed, 0 failed/skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-membership-authority-final-20261030.xml` | `E742D892E81BCE4A5B26FFBEA601C5A06B25B878D0824311EDBC2814624FDEFD` |
| `NewcomerSettlementSaveLoadTests` | 8/8 passed, 0 failed/skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-newcomer-save-load-final-tree-20261030.xml` | `5AF65829EA5231F4D4E5744BF5F5897AB62E68F16B00DE35BAC59BE86A38B1F3` |
| `SimulationDriversSceneBindingTests` | 1/1 passed, 0 failed/skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-simulation-drivers-scene-binding-20261030.xml` | `E6B6F6A189A0A21B2B0DF1C125C271E9D25F8EFE6575AFD41321CF44A7807BE8` |
| `PlayableScenarioStartupPlayModeTests` | 11/11 passed, 0 failed/skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playable-scenario-final-tree-20261030.xml` | `D241B7CC8194393E105686A483E89C71DD3F69F57F3D7AAFC320E1BB48D7CF96` |
| Full EditMode suite | 4,052/4,244 passed; 192 failed; 0 skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-editmode-full-final-tree-20261030.xml` | `72F099FFD6C57732B6C17A8692C981277CFB1E353E5D7A5B985DCE91AB55D80E` |
| Full available PlayMode suite | 11/11 passed; 0 failed/skipped | `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-playmode-full-final-tree-20261030.xml` | `BB99B48C6F139FD41254BA7451EC5E94A6E3CCCEB20CD35C7B825C63E0CD9AB7` |

The focused startup suite loaded the actual Main Scene and asserted the generated-grid connection alongside the authored scenario HUD, real First Ledger equity threshold, household adoption, and existing scenario-start/farm checks. It is not a thirty-day economy run or a complete farm Task lifecycle. Fresh full EditMode completed with 4,244 discovered, 4,052 passed, 192 failed and 0 skipped in 276.11 seconds. Fresh full available PlayMode completed 11/11 passed in 80.74 seconds. The full EditMode XML is not green: its 192 failures include 45 ConstructionInputSupport, 11 TownWorldFootprintSaveLoad, 8 AcquisitionVpFlow, 8 OwnershipWorkflow UI, 7 PopulationHealthEconomy, 6 BusinessCashTransfer, and 5 General Store economy failures, along with smaller clusters. The full PlayMode XML is green for the currently available 11 tests, but these tests do not execute a 30-day economy. Latest scoped General Store and Construction results remain 29/34 and 49/94 respectively, with failure clusters open.

Main Scene asset was not modified in this continuation: SHA-256 `817F22A74FACDC20A2F0E2B36451AA1293571E11041484078823B25E952261B9`; `.meta` SHA-256 `841B50C2925B4A0B459AF03D63E63F992FAFD72C547618DDCE9322A5E43BACE5`. The existing HUD screenshot is `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-main-scene-first-ledger-hud-fit-20261030.png`; no new screenshot was captured for this continuation.

New backlog IDs: `LL-PLAY-028` tracks parcel authority binding after fresh TownWorld generation (verified by the final Main Scene PlayMode test); `LL-PLAY-029` is PARTIAL. The lifecycle/save-load path is covered by the 10-test authority suite, including transfer, death, round-trip, old-save fallback and allocator exhaustion, but other population/newcomer mutation paths still write legacy fields directly and need a full authority migration audit. These fixes do not close ordinary daily purchase, rent payment, animal feed procurement, day/night visual identity, crop lifecycle/freight, or 30-day reconciliation work.
