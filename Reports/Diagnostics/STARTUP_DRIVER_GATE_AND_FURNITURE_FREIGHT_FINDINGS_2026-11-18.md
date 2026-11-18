# Startup Driver Gate and Furniture Freight Findings — 2026-11-18

## Startup failure containment

The development-town bootstrapper already rejects an invalid `DevelopmentTownConfiguration`, and `TownWorldController` blocks its fallback shell generation after that failure. `SimulationDrivers` did not check the bootstrapper state at its weekly, daily, or short-tick event entry points. A rejected town could therefore continue running simulation drivers if its clock advanced.

The repair gates all three event paths when a bootstrapper is present. Drivers remain paused until startup completes, and remain paused after a terminal startup failure. Scenes without a bootstrapper retain their existing standalone behavior. A single error names the rejected bootstrap status; it is not repeated every tick.

Regression coverage was added to `TownWorldStartupGenerationGateTests` for standalone, uninitialized, successful, and failed bootstrap states.

## Unity verification

- Unity: `6000.4.2f1`.
- The first recompile reported a missing namespace import in `SimulationDrivers.cs`; that import was added.
- Unity then wrote `Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` after the edited source timestamps. The Editor log contains no compiler errors after that correction. The pipeline did not return a terminal `completed` status before the Editor entered a long asset refresh/domain reload, so the compile is recorded as **assembly output observed; terminal status unavailable**.
- The focused startup-gate test command did not produce a fresh result. Its command timed out and Unity logged `PipelineTestRunner.InvalidatePreviousRun`; the Editor became unresponsive during the resulting reload/test-runner activity. No test pass is claimed for the new regression.
- The Editor was not restarted, no scene was saved, and no PlayMode session was started in this pass.

## Furniture freight follow-up

The live `FurnitureStorePurchaseDriver` currently chains `TryShip`, `TryDeliver`, and `TryInstall` in one daily call. `TryDeliver` directly performs loading, the full transit duration, unloading, and receipt. This bypasses the tick-driven production freight lifecycle. F4's consolidated shipment is tracked by `FreightShipmentService`, but is not accepted by `FreightCompanyRuntime`; physical coupled departure is currently connected to an accepted freight agreement's off-map journey. The available coupled-presentation path does not provide an in-town wagon route to a household.

No freight behavior was changed here. Replacing the synchronous call safely requires a production-supported household-delivery handoff that carries the order-specific consolidated custody callbacks through the freight driver's ticks and provides a real destination route. Reusing the off-map agreement journey or marking the shipment delivered would misrepresent movement and custody.

## Changed portable files

- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/World/TownWorldStartupGenerationGateTests.cs`
- `Reports/Diagnostics/STARTUP_DRIVER_GATE_AND_FURNITURE_FREIGHT_FINDINGS_2026-11-18.md`

No owner scene, prefab, configuration, terrain, font, or local asset-pack changes are included.
