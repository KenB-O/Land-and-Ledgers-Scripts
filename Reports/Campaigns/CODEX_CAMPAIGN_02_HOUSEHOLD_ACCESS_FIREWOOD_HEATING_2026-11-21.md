# Codex Campaign 02 — Household Access, Firewood, Domestic Tasks and Heating

Date: 2026-11-21  
Branch: `codex/muse-unity-integration-2026-11-21`  
Base revision: `b822455a645506b21a3b0f72a1bc5edaf813df64`

## Household-by-household destination audit

The retained development-town run `Reports/Playtests/Runs/20261120T192311Z-343a8cd2/console-events.jsonl` records eight standalone homes, buildings 6–13, all using the `House Natural Wood` home shell. The authored household roster assigns one distinct home ordinal per household. The Firewood activity destination is therefore authored once on the shared reusable home prefab and is inherited by all eight instances; it is not separately duplicated on each household state.

| Household | Runtime ID | Home ordinal | Shared prefab / Firewood point |
|---|---:|---:|---|
| Baldwin-Ooms | 1 | 0 | House Natural Wood / `firewood-handling` |
| Pierce | 2 | 1 | House Natural Wood / `firewood-handling` |
| Boniecki | 3 | 2 | House Natural Wood / `firewood-handling` |
| Ethan-Tia | 4 | 3 | House Natural Wood / `firewood-handling` |
| Shane Bryan | 5 | 4 | House Natural Wood / `firewood-handling` |
| Richard Sithole | 6 | 5 | House Natural Wood / `firewood-handling` |
| Kwindt | 7 | 6 | House Natural Wood / `firewood-handling` |
| Millward | 8 | 7 | House Natural Wood / `firewood-handling` |

The point is a distinct exterior household fuel-handling destination. It is placed 2.25 m east of the measured rear-east footprint corner (`BackRightCornerLocal.x = 4.898758`, `z = -5.286968`), on the rear edge, facing toward the home. It is outside the authored interior floor, separate from `main-entrance` and `porch-dropoff`, and is not aliased to the stove or a generic floor point. Its display name does not claim that a separate visible pile prop exists. Household fuel remains governed by the existing household reserve/stove custody authorities; this change grants no stock.

The point is structurally resolvable by `BuildingFootprintAuthority.TryGetActivityLocation`. That result is not proof of an exterior navigation route. Physical arrival and completed fuel delivery remain pending Main Scene verification.

## Stove-less households and activity bindings

The current configuration identifies Shane Bryan (household 5, ordinal 4) and Millward (household 8, ordinal 7) as the two stove-less households. No free stove was added. The existing equipment acquisition catalog intentionally excludes a cast-iron stove from its first furniture wave; its installation binding also refuses cooking-stove capability, with no authorized stove quote, recipe or acquisition lifecycle. This requires an owner decision before adding a priced stove supplier/acquisition, transport, ownership and installation path. Existing cold-food behavior remains distinct from stove cooking; fuel handling is not schedulable without an installed stove.

Meal preparation already resolves the installed per-stove activity ID. Cleaning already resolves an authored `Cleaning` destination, with the `Porch` route only as its existing alternate. No caller change was needed in this pass.

Domestic task recovery already preserves task identity and Save/Continue route-recovery state, releases assignments after failed movement, and waits without monopolizing a worker when neither a Firewood point nor stove fallback exists. A capability-change sweep re-adopts the same queued task. Existing regression names include:

- `MissingFirewoodAnchor_WaitsForCapabilityChangeAcrossSaveLoadWithoutHoldingWorker`
- `MissingFirewoodAnchor_WithStoveFallback_DoesNotParkTask`
- `MissingFirewoodAnchor_ParkedTaskResumesWhenStoveFallbackAppears`
- `FuelTrip_TwoLegArrival_FeedsStoveAndSchedulesDueEventOnlyAfterDelivery`
- `FuelTrip_LegOneArrivalUnsupported_ReleasesHoldAndDefersWithNothingLost`
- `SaveLoad_FuelTripMidTripRoundTripsWithHoldIntactAndCompletesAfterRestore`

These test fixtures exercise driver behavior with injected movement outcomes; they do not substitute for a live NPC route in the authored scene.

## Heating and daily lifecycle findings

`SimulationDrivers.DriveHouseholdReserveDay` runs from the daily driver and opening-day bootstrap, independent of a player-owned General Store. It resolves one seeded daily exterior temperature and sends it through household reserve/heating resolution. `HouseholdReserveEvaluator.ResolveDailyUse` is idempotent for an absolute day via `lastReserveDepletionDayIndex`; the later legacy General Store cadence therefore cannot debit a household a second time that day. Reserve/stove movement is transfer-based: failed delivery leaves reserve stock untouched; successful delivery debits the reserve by the quantity credited to the stove. The current authored and existing source tests support these invariants, but current-source Unity execution and actual heating debit were not run in this pass.

Existing targeted source tests include `Lane9bDailyDriversTests.FuelBurnsOnConsecutiveDays_AndIsDayKeyed`, `HouseholdFuelCookingDriverB3Tests.FuelTrip_TwoLegArrival_FeedsStoveAndSchedulesDueEventOnlyAfterDelivery`, and thermal/save DTO coverage in `HouseholdReserveInventoryTests`. Their current Unity Test Runner status remains unverified.

## Retained route-warning evidence

The retained run contains 2,878 occurrences of the exact `StartNotWalkable` warning “Point is outside the authored walkable floor area on floor 0.” Grouping its stack traces gives 2,116 sleep movement calls, 729 meal movement calls, and 33 domestic physical-activity calls. This identifies separate call families, not one shared destination or a verified Firewood cause. The captured warning text predates the current request-attribution source and does not contain the affected person or activity-location ID. No route-authoring change is inferred from that old signature. Capture a fresh trace-enabled Main Scene run before repairing or reclassifying it.

## Changed files

- `Assets/Environment/Buildings/House Natural Wood.prefab` — added the distinct exterior `Firewood` activity point.
- `Assets/Scripts/Core/Diagnostics/WorldAccessibilityAuditor.cs` — current authored inventory reports one reusable prefab point, with the eight-instance scope explicit.
- `Assets/Scripts/Tests/Editor/Diagnostics/WorldAccessibilityAuditorTests.cs` — added an asset-backed check of the actual home prefab, footprint-relative location, roster ordinals, and runtime point resolution; renamed the inventory test and marked the missing-point classifier as historical negative control.
- `Assets/Scripts/Core/Diagnostics/AtoZAudit.cs` and `Assets/Scripts/Editor/Diagnostics/AtoZAuditWindow.cs` — Firewood source audit now checks the actual shared residential prefab instead of searching only the household configuration asset.
- `Assets/Scripts/Tests/Editor/Diagnostics/AtoZAuditTests.cs` — updated the Firewood source-check fixture and test to inspect `activityLocations` serialization.
- `Assets/Scripts/Tests/Editor/Population/DomesticLaborDriverTests.cs` — clarified that its refusal path covers a home without an authored Firewood point, a retained capability case.
- `Assets/Scripts/Core/Diagnostics/TaskLifecycleInspector.cs`, `Assets/Scripts/Core/Diagnostics/IncidentInvestigator.cs`, `Assets/Scripts/Editor/Diagnostics/WorldAccessibilityAuditorWindow.cs`, and `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs` — replaced stale present-tense “no point exists” explanations with the current prefab state and explicit live-verification limit.

## Verification state and remaining work

Source/YAML changes and the exact roster-to-prefab mapping were inspected. No Unity compile or Test Runner execution was performed. `unity status --format json` found the only ready Unity 6000.4.2f1 Editor open against the owner's original `F:\Land & Ledgers` project (PID 23156); this integration checkout is separate. No second Editor was launched because the current handoff records memory pressure and the owner project is active. The targeted tests above are therefore implemented/existing but unexecuted on this source.

The Firewood issue remains **IMPLEMENTED, UNVERIFIED**, not closed. The minimum closure evidence is a fresh Main Scene route where an eligible resident reaches `firewood-handling`, proceeds to the installed stove, household reserve decreases by the delivered amount, stove fuel increases by the same amount, and the task completes once; also verify interruption/recovery and Save/Continue. Verify day-keyed heating fuel burn and cold consequences with daily snapshots. Capture path request attribution to distinguish the retained sleep, meal and domestic route failures. Obtain the owner's decision on stove acquisition policy before implementing an acquisition flow.
