# Pass XIII Main Scene Manager Audit

Main Scene: `Assets/Main Scene.unity`

The authored scene contains one visible `SimulationSystemsHub`, one `SimulationDrivers`, one `ScenarioDirector`, one `FirstLedgerSliceBootstrapper`, one `Time Manager`, one `PopulationManager`, and the authored UI/world roots. The scene still contains many intentionally empty serialized dependency fields; runtime auto-wiring supplies some of them.

During the first ordinary Play Mode startup, the bootstrap census reported two live `CivicFoundationManager` instances. Source inspection found independent fallback creation in `FirstLedgerSliceBootstrapper` and `SaveLoadManager`. `CivicFoundationManager.FindOrCreate()` now includes inactive authored objects and is used by both callers. Subsequent focused Unity runs produced no duplicate Civic Foundation diagnostic.

Remaining scene-authoring work includes assigning the Pass XII manifest's HUD/read-model bindings, bank runtime binding, boarding parcel mapping, and NPC construction supplier registration. Those were not replaced with permissive fallback managers.

