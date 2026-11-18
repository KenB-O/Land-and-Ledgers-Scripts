# NPC Household Life and Physical Furniture Use — Pass 6

Date: 2026-11-18
Unity: 6000.4.2f1
Branch: `codex/household-life-furniture-use-2026-11-18`
Baseline: `740f43e0a9d6f3288772d8b8a01b4181a278b806`

## Changes

The live sleep driver previously treated an unavailable route, missing building authority, or unsupported movement as if the resident had slept in the reserved bed. Waking then released the reservation as completed and recorded bed use. `SleepTickDriver` now retries while the sleep window remains open, records a completed bed night only after movement reports arrival, and cancels an unreached reservation at wake without awarding bed-use or rest-quality credit. The movement resolver now passes the actual failure reason into the driver.

The PlayMode trace showed a separate doorway dead band. `PopulationPathingDirector` considered residents within at least 2.5 metres ready for a doorway-to-interior route, while `AgentMover` rejected the same route beyond 1.25 metres (or the entrance-width-derived radius). Both now use `AgentMover.BuildingActivityStagingRadius`, so the director routes farther-away residents back to the entrance instead of repeatedly requesting a route the mover refuses.

## Verification

### EditMode and compilation

- Unity compilation: completed, no errors; Editor reported `compilationFailed=false`.
- `SleepTickDriverTests`: 11/11 passed after the lifecycle change and again after the doorway-radius correction.
- `AgentMoverTests`: 22/22 passed after the doorway-radius correction, including three shared-radius cases.
- `MealTickDriverG4Tests`: 12/12 passed.
- `DomesticLaborDriverTests`: 15/15 passed.
- `HouseholdFuelCookingDriverB3Tests`: 16/16 passed.
- Total focused household activity tests: 54/54 passed. The 43 meal, labor, and fuel/cooking tests were run before the final route-radius-only delta; the directly affected sleep and mover suites were rerun after it.

### PlayMode

The actual Main Scene was run in Unity PlayMode from the authored development town. The clock ran at `Speed10x` (10×) and reached **March 4, 1870, day index 3, at 21:58**. This was a short, roughly three-day smoke run, not a long simulation. The scene was not saved. Unity was returned to Edit Mode and reported `playMode=stopped`.

After the route-radius correction, live `SleepingPlaceRegistry` history showed completed bed nights through day index 2 in all eight household registries:

| Household | Residents with recorded bed use | Furniture assets/bindings |
|---|---:|---:|
| H1 | 2 | 6 / 6 |
| H2 | 2 | 5 / 5 |
| H3 | 2 | 5 / 5 |
| H4 | 2 | 6 / 6 |
| H5 | 1 | 1 / 1 |
| H6 | 1 | 4 / 4 |
| H7 | 2 | 6 / 6 |
| H8 | 2 | 1 / 1 |

These are authoritative runtime history records, not planner reservations. They demonstrate bed-use completion in homes with one and two beds. The development configuration has six homes with stoves and two without (H5 and H8); no stove was added to the latter. This short run did **not** establish successful physical cooking or seated dining. Runtime diagnostics still showed domestic meal-preparation tasks remaining open, and some meal-seat movement requests were unsupported. The existing meal driver can report eating despite unsupported chair movement; that behavior remains a separate known limitation and is not claimed as seated-dining success.

The captured screenshot is a Game view of the town overview, not an interior-use screenshot:

`Reports/Playtests/Runs/20261118-pass6-household-life/pass6-live-game-view.png`

## Console evidence and remaining limitations

At the end of the short run, Unity's captured Console ground-truth counters showed 10 errors and 175 warnings for the Editor session. Those are session totals and include unrelated/editor history; they are not attributed to this pass. The retained pipeline buffer also contained unrelated economy, task scheduling, freight and domestic-labor diagnostics. Relevant evidence included:

- Before the route fix, sleep attempts reported `The authored entrance-to-activity route could not start` for the household beds.
- After the route fix, all eight household registries recorded bed nights through day 2.
- Meal diagnostics still reported unsupported chair movement for some diners.
- Domestic-labor diagnostics reported that some homes have no authored firewood storage, and some meal-preparation tasks remained open. These were not changed in this focused pass.

The run did not verify actual cooking/fuel consumption, seat arrival, supply retrieval, furniture purchasing, or a complete day-by-day return-home audit. No false success is claimed for those behaviors. The owner-authored Main Scene, configuration, Natural Wood prefab, terrain metadata, fonts, and local asset packs remain untouched by this pass.

## Files changed for Pass 6

- `Assets/Scripts/Domains/Population/PopulationPathingDirector.cs`
- `Assets/Scripts/Domains/Population/SleepTickDriver.cs`
- `Assets/Scripts/Domains/World/Pathing/AgentMover.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Pathing/AgentMoverTests.cs`
- `Assets/Scripts/Tests/Editor/Population/SleepTickDriverTests.cs`
- `Reports/Diagnostics/NPC_HOUSEHOLD_LIFE_FURNITURE_USE_PASS_2026-11-18.md`
- `Reports/Playtests/Runs/20261118-pass6-household-life/pass6-live-game-view.png`

## Recommended next work

1. Fix the confirmed domestic-task lifecycle issue that leaves meal-preparation tasks open without worked time, while preserving real labor and inventory requirements.
2. Trace and repair unsupported chair movement so seated dining is only recorded after actual arrival, or uses the documented fallback honestly.
3. Author legitimate household firewood storage/interaction locations where the existing homes lack them, then verify fuel movement and stove consumption in PlayMode.
4. Capture interior screenshots and observe a complete cooking-to-dining sequence before claiming full household-life completion.

Publication status: pending final commit and remote verification.
