# Pass IX — Minimal Unity Integration Handoff

Pass IX was source-only. Nothing below was done in Unity, and no scene wiring is claimed. This is the complete list of Unity-session work the pass identified, in the order a session should take it.

## 1. Compile first

- Four production files were edited in Pass IX but sit outside the Roslyn harness subset, so they are Roslyn syntax-checked only: Orchestration/Systems/SimulationDrivers.cs, Orchestration/Systems/SharedBusinessRuntimeManager.cs, Orchestration/Systems/SimulationSystemsHub.cs, Domains/Civic/SchoolhouseModels.cs. Compile the project in Unity and resolve anything the full Unity compiler surfaces before any PlayMode work.
- Confirm the additive save DTO fields load old saves cleanly (BusinessRuntimeSaveDto.namedPayables, NpcPropertyLifeSaveDto tax book, ScenarioRunState latched goals, SystemsSaveDto civic treasury, weekly attendance, and stock provenance). All are additive with safe defaults; the harness proves the logic round trips, not the Unity serializer.

## 2. Scene hooks

- Scenario start panel: call ScenarioBootstrapFactory from the start panel so a chosen scenario actually begins; register the Saloon Circuit dated qualifier from live goal state so ScenarioObjectiveService.Evaluate runs on the day change. Until this hook exists the service is registered as unwired-by-design in Docs/UnwiredSystems.md.
- Freight: confirm FreightCompanyRuntime instances are placed or spawned where FreightProductionDriver expects to own them, and that DriveFreightCompanies reaches them in the scene's update order.
- CalendarAnchor: place or confirm the scene calendar anchor object so the scenario date frame (anchor day index 60 for 1880-03-01) matches the scene's configured anchor.
- Schoolhouse: the schoolhouse is scene-owned by CivicFoundationManager, and Pass IX drives teacher pay from the hub day-change path guarded to term-start days because no production school-term flow exists. In the scene session, confirm the term boundaries the scene uses match the guard, or move the call into the scene's own term processing.
- Saloon validation: run the saloon scenario validation in PlayMode (ownership plus a qualifying sale plus deadline) now that completed goals latch.
- Player-owned banks: bind the bank ledger scene objects for player-owned banks so the player tax and lending legs display against the same stores the source drives.
- Roads: visually check the merged road rectangles from TownWorldController.BuildRoadVisuals on the town map; the merging is source-wired and untested visually.

## 3. Test Runner and PlayMode beats

- Run the Unity Test Runner edit-mode suite once (RUN-006 reliability item stays open until it is trustworthy) and record the result separately from the Roslyn harness numbers, which are the pass's only claimed validation.
- PlayMode smoke: New Game into a coded scenario, 30 days of autonomous running, one mine week, one freight delivery end to end, one asset sale, one tax day for both a player parcel and an NPC holding, save mid-shipment and reload.
- RUN-004 (Main Scene 30-day autonomous economic cycle) remains the standing end-to-end proof item and is the natural vehicle for the smoke beats above.

## 4. Not in this handoff

- The future backlog in the master register (RUN and LL-PLAY items, PASS8-005 to PASS8-016 remainders, scenario editor and HUD work) is unchanged by Pass IX except where the Pass IX resolutions section records closures. Water stays deferred on the Canon verdict.
