# Pass XXI Continuation — Main Scene Journey Network

Date: 2026-10-30  
Unity: 6000.4.2f1  
Source branch at start: `integration/pass12-unity-readiness`  
Source HEAD at start: `6a3bb43ad7f431629657d35fc4192cbfa7dcdc01`

## Root cause

`SimulationDrivers.DriveDailyNeeds` correctly refused to route embodied needs when its hub-owned `JourneyModel` had no locations. The Main Scene had a generated `TownGrid`, real buildings and generated plot road-frontage records, but no production startup path populated the hub journey model from those live world authorities. The resulting `DailyNeedsService skipped: no journey locations registered` warning was a genuine source-to-runtime integration gap.

The first builder implementation also found that most `PlacedBuilding` road-access anchors did not directly touch a connected road cell. PlayMode evidence showed 68 buildings in the sampled world had zero locations from the anchor-only builder. `TownPlot.roadAccessCell` is the generated frontage authority; its reciprocal `plot.buildingId`, `building.plotId`, grid bounds and road connectivity can be checked against the same live world. The integration now prefers a usable building anchor and falls back only to that exact building's linked plot frontage when the anchor is absent or disconnected.

## Implementation

- Added `Domains/World/Journeys/TownJourneyNetworkBuilder.cs`. It flood-fills actual road cells into connected components, selects the real road junction nearest town center for each component, measures building-to-junction distance from that junction, and registers only buildings with physically connected frontage. Disconnected components are not bridged. Generated node IDs are stable (`town-center`, `town-road-component:*`, `building:*`).
- Added an idempotent TownWorld subgraph replacement operation to `JourneyModel`, preserving unrelated authored nodes and regional edges attached to stable incoming town nodes.
- Wired `SimulationDrivers` to synchronize the hub-owned journey model before daily needs, cache a world signature, and resolve/update the real Person's home/work/current destination through the building location IDs. The route graph refreshes after road, building, plot-frontage, or Business-site changes.
- Exposed `PopulationManager.Memberships` as the existing current membership authority and bound the production `HouseholdShoppingLoop` to it before daily need execution. The loop rebinds after restore/state replacement without discarding its diagnostic trip history or custody records.
- Replaced unconditional general-store/off-map supply claims with a live check for a stocked, trading, item-resolving supplier reachable from an actual household member's home. Off-map access remains false until an off-map retail supplier is connected to this production directory.
- No scene, prefab, ScriptableObject, terrain, or `.meta` asset changed.

## Runtime evidence

Fresh Main Scene PlayMode test `MainSceneDailyNeedsBuildsJourneysFromRealRoadsAndBuildingAccess` started First Ledger through `ScenarioValidationHarness` and advanced the authoritative clock by 24 hours. The final unfiltered PlayMode run built 71 locations: the town junction plus 70 road-accessible buildings, with 70 real road routes, one connected road component, and zero buildings reported without connected frontage. The test verifies that the selected endpoint resolves to a real `TownWorldController` building, every generated household with a valid home building has a registered reachable route to town center, the live shopping loop holds the exact `PopulationManager` membership registry, and shortage errands reach that loop.

The final unfiltered PlayMode case reported 161 meals eaten, 43 missed, and zero purchases. The shopping loop recorded real attempts, while open needs remained open and their guidance did not recommend “purchase” because the fresh opening had no stocked, open, reachable seller. This verifies an honest refusal path; it does **not** prove a purchase or successful commerce. No synthetic demand, stock, route, money, or transaction was introduced. `LL-PLAY-020` remains **PARTIAL** pending a legitimate stocked seller/purchase pathway and a positive transaction test.

## Main Scene commerce wiring result and remaining seller gap

The earlier zero-purchase day exposed a real wiring gap. `BuildHouseholdShoppingLoop()` requires a membership registry, and `DriveEmbodiedShopping()` returns immediately when the loop is null. The Main Scene had no bootstrap caller for `AttachHouseholdMemberships(...)`. The shortage monitor also received unconditional `HasGeneralStore = true` and `HasOffMapAccess = true` flags. The correction now binds `PopulationManager.Memberships` in the daily driver and rebinds the existing loop after restore; supply guidance is based on a real stocked, trading, reachable item supplier, while unbound off-map retail access is reported false.

Stable backlog ID `LL-PLAY-023` is now **PARTIAL**: the Main Scene loop uses the existing membership authority and passes a no-seller refusal test. The current First Ledger opening still does not provide a stocked, open, reachable food retailer to this purchasing path. A positive purchase requires connecting a legitimate seller and its existing inventory/settlement authority; no stock or buyer was fabricated here.

## Follow-up — live General Store supplier registration

The production bootstrap called `RegisterGeneralStoreSupplier(storeRuntime.BuildHouseholdTradingPort())` before the player had formed a General Store. `BuildHouseholdTradingPort()` originally built its offers from `RuntimeState.CategoryStock`, which was empty at that moment, and captured `Staffed=false`. The supplier directory retained that port, so later forming and staffing a legitimate player store could not make the already-registered supplier offer household items or become trade-capable. This was a real source-to-runtime lifecycle defect, independent of the fresh opening's correct no-seller result.

The port now derives its declared category/item offers from the authored `GeneralStoreBusinessDefinition` and reads stock, price, Business ID, and staffing from the live runtime manager. An unformed store still has zero stock and remains unstaffed. The Main Scene regression constructs the same port/adapter before formation, verifies no inventory and no trading capacity, forms a real Business, assigns premises and a real clerk through existing authorities, then verifies that the pre-existing adapter resolves the formed Business, its actual stock and a positive authoritative price, and refreshes staffing. No inventory, cash, or Business was injected by the adapter.

An independent source review also found that `BuildHouseholdShoppingLoop()` taught every living Person about every supplier whenever the loop was rebuilt. That could silently disclose future non-public suppliers. It now seeds only the locally public General Store, and only after the store has a stable formed Business ID; other supplier knowledge requires an explicit information/discovery path.

The Main Scene still does not provide an observed positive household purchase through the autonomous daily loop. The new regression proves that the actual supplier adapter remains live through normal formation and hiring, not that a full customer purchase, freight chain, or settlement has been completed. `LL-PLAY-023` remains **PARTIAL**.

## Follow-up — first-session clock and live scenario presentation

An actual graphical Main Scene Play Mode inspection showed the authored First Ledger entry panel while the top-bar clock was already advancing; the panel said the campaign clock would start only after pressing Start. After Start, the Main Scene visibly displayed the authoritative First Ledger objective ladder (0/8 complete, all eight goals including the $15,000 owner-equity target). The scenario HUD is therefore visibly present in this runtime path, although it is still created dynamically rather than fully authored as a child hierarchy.

`ScenarioStartPanelController` now pauses the authoritative `TimeManager` when no scenario is active and the first-session choice panel is displayed, then resumes its prior non-paused speed only after `ScenarioDirector.SwitchScenario` succeeds. Already-active restored runs do not get paused by this change. A new Main Scene PlayMode regression holds the choice open for real-time frames, verifies the absolute day and in-day seconds do not advance, invokes the same Start action, and verifies the clock resumes under `first-ledger`.

The separate date mismatch remains open: `FirstLedger.asset` authors `1870-01-01`, its description begins “1870,” but the actual Main Scene runtime clock displayed `1886-01-01`. The controller only switches the scenario; it does not invoke the authored starting-world date/generation coordinator. Changing the narration to 1886 or jumping the clock after population/world creation would conceal or compound the authority mismatch, so neither was done. Track it as `LL-PLAY-024` pending repair at the supported New Game/world-bootstrap boundary.

## Regression results

All results below are fresh Unity Test Runner XML on the continuation tree, Unity 6000.4.2f1:

| Suite | Result | XML SHA-256 |
|---|---:|---|
| `JourneyModelTests` | 10/10 passed | `D67F6F88D44B2A14607D4979008CE14B9405F40D9A52746EA167A8B2252EF6BB` |
| `P6TravelCommsJourneyTests` | 10/10 passed | `FD0E4950675172BB304D34945693E9E501B5E4CF9F60BA6809A0D909A20D6554` |
| `HouseholdShoppingLoopTests` (pre-follow-up tree) | 21/21 passed | `3CF3A36786A54323DD0648B2600CD856BD71F5C9AD5E805CCEDA3F16BB903A80` |
| `EmbodiedPurchaseExecutorTests` | 6/6 passed | `B835AD7DB325062A8DE89EBE798939AF0F65F99E2CA3F89D6340DC3C53248BE9` |
| `HouseholdMembershipAuthorityTests` | 7/7 passed | `4DF51CB02245344C266181FEF23D6898F6E1748959D2D5DB0B1D2B990205D408` |
| `PhaseBHouseholdConsumptionTests` | 20/20 passed | `7486284DCF99CDF5A390786CD87781379776084705AB12F9855A7DF304BDA3E9` |
| `HouseholdShoppingLoopTests` (current tree) | 22/22 passed | `B514573AD635950066FFDADD5F243A8D32FEC23FF1C005AE5CDE0A8F9E4A43EE` |
| Full available PlayMode suite (previous continuation checkpoint) | 11/11 passed | `B3868B956AE56D320F0910BBB55DCD222D6248DDA8EF71EB3F58E59A83C67317` |

Follow-up tests on the supplier-lifecycle correction:

| Suite | Result | XML SHA-256 / notes |
|---|---:|---|
| `ProductionBusinessAuthorityFormsTheScenarioThreeBusinessRoute` | 1/1 passed | `store-adapter-live-regression.xml`, `089B87C21606B58B42E3816B51D9CFB5F8EE34C3651079DF2113703E4CD80EEC` |
| `HouseholdShoppingLoopTests` EditMode | 22/22 passed | `household-shopping-editmode-final.xml`, `F80D0B493A0762875CD757E35B32DE8BDC234B94728275486B44E3F1DB4C859D` |
| `JourneyModelTests` EditMode | 10/10 passed | `journey-model-editmode-final.xml`, `03C42FF427BC10C91E45C2F3351B76E5EE583E7DDF94ABE8D40DED4BDDAFF0A1` |
| `FirstLedgerFourWeekProductionReportsHonestProgress` isolated repeat | 1/1 passed | `first-ledger-four-week-repro.xml`; isolated repeat passed |
| `MainSceneLoadsTheAuthoredScenarioEntryAndStartsFirstLedger` | 1/1 passed | `scenario-clock-start-playmode.xml`, SHA-256 `C3D26B8326B43682DA57641807094BFAB47F28283364A4C6A08336426C1047FA` |
| Full available PlayMode after scenario clock correction | 11/11 passed, 0 failed, 0 skipped | `full-playmode-clock-final.xml`, SHA-256 `E2080BEA8CC95EEDE382164ACBB50F2918FDCD396B5315053392D1EEEA3F4F32` |
| Broad EditMode filter `GeneralStore` | 41/60 passed, 19 failed, 0 skipped | `general-store-editmode-final.xml`, SHA-256 `FD95EF7D56202D2ACF5F1FC10BF685A37B6191E7567A66145EC16D950FD18815`; the name filter selected multiple suites, including known reserve, wholesale, reorder, construction-support, settlement-coverage and HUD tests. This is not a 60-test General Store runtime suite and the failures were not changed by this port repair. |

The first unfiltered PlayMode run after the code fix completed 10/11. The sole failure was `FirstLedgerFourWeekProductionReportsHonestProgress`: its randomized opening population supplied only one eligible hire, while the test attempted and did not require successful Farm/Livery hires before later asserting three employees. The same test passed when rerun alone on a fresh generated opening. A fresh unfiltered PlayMode rerun after the strengthened adapter regression completed 11/11, 0 failed, 0 skipped, in 76.97 seconds. XML: `full-playmode-final-current.xml`, SHA-256 `B9C3DEEA54E76421002095ED31B13B6BAC1AA21651469A2745E8785DF64036F8`. This resolves that observed suite run, but the earlier seed-sensitive failure remains a test reliability concern and should be made deterministic rather than erased from history.

The strengthened Main Scene journey/shopping test also passed within the full PlayMode suite (XML `full-playmode-final-current.xml`). The restore-rebind regression proves that an existing loop selects the restored Person through the restored membership authority and completes a legitimate finite-stock purchase with matching settlement. The new supplier regression separately proves pre-formation port rebinding through normal formation and hiring; neither is a full autonomous Main Scene retail run.

The clock-start correction was followed by a fresh full available PlayMode run on the modified source tree: 11/11 passed, 0 failed, 0 skipped. Its XML is `F:\Land & Ledgers_Pass21_Backups\journey-network-continuation_20261030_011226\reports\full-playmode-clock-final.xml` (SHA-256 recorded above). The focused clock test XML is in the same directory. The Unity GUI was returned to Edit Mode and closed without saving; no authored scene asset changed.

## v2.2 handoff audit — blocked shipment conservation

The supplied v2.2 package is intact at `C:\Users\kenne\Downloads\Land_and_Ledgers_Documentation_Package_v2.2_2026-10-29.zip`. Its staged copy and the existing live `F:\Land & Ledgers\Planning Sources` installation both pass all 138 manifest size/SHA-256 entries; all 24 router paths resolve. ZIP entries have no absolute/traversal paths or symlink entries. Because the live documentation installation already matches v2.2, it was left in place; its unrelated temporary Office files were preserved.

The handoff's P0 `CreateBlockedShipment(...)` warning was checked against current source and exercised in Unity 6000.4.2f1 on source `c23dd913206107f74adcf4de4e692fa8330f274c`. Existing production behavior records pre-load blocked shipments with zero remaining cargo, no source commitment, no load, and terminal `Failed` state. Load normalization repairs provably never-loaded legacy records; post-load failures return cargo to source or quarantine it on the shipment when the source is gone. No source change was needed in this continuation.

Fresh EditMode filter `LogisticsConservationGateTests`: 16/16 passed, 0 failed, 0 skipped. XML: `F:\Land & Ledgers_Pass21_Backups\pass21-reports\pass21-blocked-shipment-conservation.xml`, SHA-256 `187AFE0158969745B8A5C64F0B9C7BE8C940142C23FE976AFEEC55D2A16132E8`. This covers the actual blocked call-path families, financial conservation, retry, save/load, malformed legacy repair, blocked delivery refusal, and post-load return/quarantine. It validates the documented safeguard; it does not close the broader freight or Main Scene shipping integration.

A full EditMode run was attempted on the same tree, but it did not complete and produced no NUnit XML. The log grew to approximately 25 MB and then stopped advancing at `2026-10-30 01:32:04`; by `01:35:10`, the Editor remained responsive with stable working set near 5.3 GB, while its busiest thread had accumulated over six minutes of CPU time. After preserving the log and confirming no visible suite progress, only that identified Land & Ledgers batch test process was stopped. The attempt has no pass/fail count and is not represented as a completed suite. The preserved log is `full-editmode-final.log`.

`HouseholdShoppingLoopTests` had a prior failing assertion that expected no sale when a 60-unit need required delivery but the delivery fee was unaffordable. Production code explicitly permits an honest smaller in-person purchase within the shopper's 50-unit carry limit. The regression now verifies a 50-unit sale, 10 units still needed, a 6,000-cent ledger outflow, no delivery-fee charge, and matching store/inventory balances. This updates the obsolete test expectation; it did not change the production shopping policy.

## Limits and next work

- The full EditMode suite did not complete; no 30-day Main Scene simulation, day-15 save/load continuation, or manual visual inspection was run in this continuation.
- A daily report with missed meals and zero purchases remains an economic integration question. The production call-site gap is documented above and tracked under `LL-PLAY-023`; next trace supplier contents, Business IDs/locations, household affordability, and route/work-time rejection data through the actual day callback. Keep the transaction positive case causal.
- The builder's measured route passes through a component junction and may be longer than the shortest path on that road component. It does not invent disconnected links; a future refinement can use a compact road graph if travel-distance accuracy needs more precision.
- Existing full-suite failures from Pass XXI remain outside this focused continuation.
