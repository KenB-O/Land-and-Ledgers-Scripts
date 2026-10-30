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
| Full available PlayMode suite (current tree) | 11/11 passed | `B3868B956AE56D320F0910BBB55DCD222D6248DDA8EF71EB3F58E59A83C67317` |

The strengthened Main Scene journey/shopping test also passed within the full PlayMode suite (XML `shopping-authority-playmode-r1.xml`, 11/11, SHA-256 `82AE25DE5627F9E3C9CDE18008489F5CEC4C5006D32C79A628150E9404EA4883`). The new restore-rebind regression proves that an existing loop selects the restored Person through the restored membership authority and completes a legitimate finite-stock purchase with matching settlement. The final unfiltered PlayMode XML is `full-playmode-shopping-final.xml`; all results are under the checkpoint reports directory.

A full EditMode run was attempted on the same tree, but it did not complete and produced no NUnit XML. The log grew to approximately 25 MB and then stopped advancing at `2026-10-30 01:32:04`; by `01:35:10`, the Editor remained responsive with stable working set near 5.3 GB, while its busiest thread had accumulated over six minutes of CPU time. After preserving the log and confirming no visible suite progress, only that identified Land & Ledgers batch test process was stopped. The attempt has no pass/fail count and is not represented as a completed suite. The preserved log is `full-editmode-final.log`.

`HouseholdShoppingLoopTests` had a prior failing assertion that expected no sale when a 60-unit need required delivery but the delivery fee was unaffordable. Production code explicitly permits an honest smaller in-person purchase within the shopper's 50-unit carry limit. The regression now verifies a 50-unit sale, 10 units still needed, a 6,000-cent ledger outflow, no delivery-fee charge, and matching store/inventory balances. This updates the obsolete test expectation; it did not change the production shopping policy.

## Limits and next work

- The full EditMode suite did not complete; no 30-day Main Scene simulation, day-15 save/load continuation, or manual visual inspection was run in this continuation.
- A daily report with missed meals and zero purchases remains an economic integration question. The production call-site gap is documented above and tracked under `LL-PLAY-023`; next trace supplier contents, Business IDs/locations, household affordability, and route/work-time rejection data through the actual day callback. Keep the transaction positive case causal.
- The builder's measured route passes through a component junction and may be longer than the shortest path on that road component. It does not invent disconnected links; a future refinement can use a compact road graph if travel-distance accuracy needs more precision.
- Existing full-suite failures from Pass XXI remain outside this focused continuation.
