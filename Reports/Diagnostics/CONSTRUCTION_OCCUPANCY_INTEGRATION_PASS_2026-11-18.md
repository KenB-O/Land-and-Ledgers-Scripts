# Construction and Occupancy Integration Pass — 2026-11-18

## Status

**Partial, fail-closed correction only.** The player building-shell queue no longer treats elapsed weeks as completed labor. A genuine end-to-end player or NPC build was not verified in PlayMode during this pass.

## Starting source

- Branch: `codex/forestry-lumber-recovery-2026-11-18`
- Starting commit: `559833d7e44b1128211b865528e1eaf9c0b2e863`
- Unity: `6000.4.2f1`

## Confirmed source defect

`AcquisitionMarketManager.ResolveWeeklyConstructionQueue` previously started the player shell quote, spent cash and consumed listed inputs, then incremented `weeksProgressed` once per weekly builder slot. Once the counter reached `weeksRequired`, it called `TownWorldController.TryConstructBuildingShell`. That path did not commit worker time through `TaskAuthority` or register the resulting shell and accommodation with the shared `HousingAuthority`.

This was not a labor shortage; the player queue had no production binding to the existing construction executor at all. Its abstract builder capacity and labor-unit quote were not evidence of committed labor.

## Change made

The weekly queue now leaves player `BuildingShell` requests blocked before start with a specific explanation. It does not spend cash, consume supplier inventory, increment progress, or create a shell. Requests remain visible and recoverable. The change avoids fabricating completed work while a complete adapter is absent.

Regression expectations now assert that several weekly reviews and an abstract labor-pool increase cannot complete a shell or debit cash/materials. They also assert that available inputs remain with suppliers while the request cannot enter the authoritative construction process.

## Existing construction authorities reviewed

The NPC path already uses `ConstructionProjectExecutor`, staged `WorkPackage`s, `TaskAuthority`, schedule reservations, daily work budgets, real material sources/procurement, and `HousingAuthority` on completion. Its completion data defines accommodation spaces and has occupancy methods. This supports source-level/Editor-test evidence for the NPC project authority; it is not evidence that a physical structure was instantiated, traversed, or occupied in live gameplay.

The player queue is not connected to this authority. There is no configured `BuildingDefinition` to `BuildingDesign` mapping in the inspected path, no player labor/contract port for these queue entries, no project material custody bridge, and no physical shell/housing completion handoff. Existing `BuildingDefinition.ResidentHouseholdCapacity` is household capacity and does not supply a sleeping-capacity policy. No such mapping or policy was invented here.

## Validation

- Unity script recompile command: completed with `failed=false`, `compilationFailed=false`, `errors=[]`.
- `NpcHomeConstructionTests`: **21 passed, 0 failed**.
- `Pass11Ws2NpcConstructionDriverTests`: **20 passed, 0 failed**.
- Targeted `QueuedShellConstruction...` test: **failed before reaching its assertions**. Test fixture town generation emitted an unhandled Unity error while loading `Assets/Environment/Buildings/Building Animations/Single Story Business Building White Plaster Animation 1.anim`: “File may be corrupted or was serialized with a newer version of Unity.” The test runner stack points to `TownWorldController.TryInstantiateGameObject` during fixture setup. The asset was not edited or suppressed.
- One PlayMode entry was attempted. Unity began domain reload and then became unresponsive during managed callbacks/import; the Pipeline connection was lost. No scenario startup, construction activity, or PlayMode result was observed. The Editor was not force-restarted or closed, and no scene was saved. Unity Edit Mode could not be confirmed after the hang.

## Not verified / required next work

1. Connect player commissioning to the existing `ConstructionProjectExecutor` and its real worker, task, time-budget, tool, material custody, procurement/freight, and payment authorities.
2. Author an explicit design mapping or selection from a chosen building definition to a catalog design, including an owner-approved accommodation-capacity relationship.
3. Add a physical completion handoff that registers the placed shell and lawful building/parcel ownership consistently with navigation and HousingAuthority, then records occupancy only under a legitimate household arrangement.
4. Resolve or isolate the generated-town test fixture's White Plaster animation import failure without masking the asset error.
5. Run a real PlayMode project only after the above integration exists and Unity is stable. The authored development town was not altered or provided with synthetic resources for this pass.

## Files changed by this pass

- `Assets/Scripts/Domains/Economy/Acquisitions/AcquisitionMarketManager.cs`
- `Assets/Scripts/Tests/Editor/Economy/ConstructionInputSupportTests.cs`
- `Reports/Diagnostics/CONSTRUCTION_OCCUPANCY_INTEGRATION_PASS_2026-11-18.md`

Owner-local scene, prefab, terrain, font, configuration and asset-pack changes were not staged or edited by this pass.
