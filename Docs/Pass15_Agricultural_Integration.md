# Pass XV — Agricultural integration record

Date: 2026-10-29

## Finding

The ordinary Main Scene was forming an `Opening Crop Farm`, but the formation
context selected the first free building without considering the business kind.
The farm therefore commonly received `SharedPremises` on a non-agricultural
site. `FarmCycleDriver` correctly refused to create crop output because no
legitimate `CropFieldState` existed.

## Change

`IBusinessTypedPremisesContext` is now an optional production-context extension
to the existing business-creation authority. `ManagerCreationContext` uses it
for CropFarm and Ranch formation and selects an unassigned building whose live
plot and/or building definition carries the matching agricultural role. Existing
fixtures and adapters retain the original capability-only contract.

`SharedBusinessRuntimeManager.ResolveWeeklySharedOperations` now refreshes
scene-owned authorities before weekly resolution. Farm field registration uses
the actual TownWorld building/plot, stable farm/plot identity, and authoritative
CropFieldAuthority. It does not create seed, Tasks, harvest, inventory or freight.

## Seasonal boundary

The winter Main Scene case intentionally expects zero harvest. A field must be
registered, while planting/harvesting remain unavailable until the legitimate
seasonal Task lifecycle exists. No crop output or shipment is injected.

## Evidence

The pre-change Unity PlayMode run is preserved at
`F:/Land & Ledgers_Pass15_Backups/pass15-playmode-winter-progress.xml` and
showed the renamed First Ledger honest-progress test passing while the farm
field assertion failed. Subsequent Unity reruns reached the project compiler,
but the current Unity/Bee runner hung before producing a final NUnit file;
those runs are reported as incomplete, not passed.

## Remaining work

The growing-season planting, tending and harvesting Task caller is not yet
connected through the ordinary Main Scene. A valid spring/summer integration
fixture is still required before claiming crop output or farm logistics.
