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
- No scene, prefab, ScriptableObject, terrain, or `.meta` asset changed.

## Runtime evidence

Fresh Main Scene PlayMode test `MainSceneDailyNeedsBuildsJourneysFromRealRoadsAndBuildingAccess` started First Ledger through `ScenarioValidationHarness`, advanced the authoritative clock by 24 hours, and observed the normal `SimulationDrivers` daily callback build 69 locations: the town junction plus 68 road-accessible buildings, with 68 real road routes, one connected road component, and zero buildings reported without connected frontage. The test also verified that the selected endpoint resolves to a real `TownWorldController` building and that every generated household with a valid home building has a registered, reachable route to town center.

The same day reported 156 meals eaten, 42 missed, and zero purchases. This confirms that the daily-needs drive no longer skips for an empty journey model; it does **not** prove a purchase or successful commerce. No synthetic demand, stock, route, money, or transaction was introduced. `LL-PLAY-020` therefore remains **PARTIAL** pending an observed legitimate household purchase or an evidence-backed explanation of unmet needs and seller availability.

## Regression results

All results below are fresh Unity Test Runner XML on the continuation tree, Unity 6000.4.2f1:

| Suite | Result | XML SHA-256 |
|---|---:|---|
| `JourneyModelTests` | 10/10 passed | `D67F6F88D44B2A14607D4979008CE14B9405F40D9A52746EA167A8B2252EF6BB` |
| `P6TravelCommsJourneyTests` | 10/10 passed | `FD0E4950675172BB304D34945693E9E501B5E4CF9F60BA6809A0D909A20D6554` |
| `HouseholdShoppingLoopTests` | 21/21 passed | `3CF3A36786A54323DD0648B2600CD856BD71F5C9AD5E805CCEDA3F16BB903A80` |
| `EmbodiedPurchaseExecutorTests` | 6/6 passed | `B835AD7DB325062A8DE89EBE798939AF0F65F99E2CA3F89D6340DC3C53248BE9` |
| `PlayableScenarioStartupPlayModeTests` | 11/11 passed | `D35D5F1F33D8838E8956D26543559681724F5391BD1555B9C4F0E809F88868F8` |
| Full available PlayMode suite | 11/11 passed | `DB462765E1FCD9086864E8116404FC04AF9359A4BAC5C53E98717508AC0E44BE` |

The targeted Main Scene journey test also passed 1/1 (XML SHA-256 `F72171149298EFA349A508456AEF33B5C98B79194B182FFA9D50FF420489742D`). The full available PlayMode run completed 11 tests, 11 passed, 0 failed, 0 skipped, on the final continuation tree; Unity 6000.4.2f1 reported the run from `2026-10-30 05:35:43Z` to `05:36:54Z`. Its XML is `full-playmode-final.xml` in the reports directory.

A full EditMode run was attempted on the same tree, but it did not complete and produced no NUnit XML. The log grew to approximately 25 MB and then stopped advancing at `2026-10-30 01:32:04`; by `01:35:10`, the Editor remained responsive with stable working set near 5.3 GB, while its busiest thread had accumulated over six minutes of CPU time. After preserving the log and confirming no visible suite progress, only that identified Land & Ledgers batch test process was stopped. The attempt has no pass/fail count and is not represented as a completed suite. The preserved log is `full-editmode-final.log`.

`HouseholdShoppingLoopTests` had a prior failing assertion that expected no sale when a 60-unit need required delivery but the delivery fee was unaffordable. Production code explicitly permits an honest smaller in-person purchase within the shopper's 50-unit carry limit. The regression now verifies a 50-unit sale, 10 units still needed, a 6,000-cent ledger outflow, no delivery-fee charge, and matching store/inventory balances. This updates the obsolete test expectation; it did not change the production shopping policy.

## Limits and next work

- The full EditMode suite did not complete; no 30-day Main Scene simulation, day-15 save/load continuation, or manual visual inspection was run in this continuation.
- A daily report with missed meals and zero purchases remains an economic integration question. Next trace the authoritative need reasons, SupplierDirectory contents, supplier Business IDs/locations, household affordability and route/work-time rejection data through the actual day callback. Keep the transaction positive case causal.
- The builder's measured route passes through a component junction and may be longer than the shortest path on that road component. It does not invent disconnected links; a future refinement can use a compact road graph if travel-distance accuracy needs more precision.
- Existing full-suite failures from Pass XXI remain outside this focused continuation.
