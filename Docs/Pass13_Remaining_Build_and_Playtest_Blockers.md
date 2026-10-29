# Pass XIII Remaining Build and Playtest Blockers

1. `PlayableScenarioStartupPlayModeTests.FirstLedgerNormalProductionReachesTheLiveLadder` fails because ordinary production does not reach Victory (`owned=4, employees=3, registry=3, equity=656910c`).
2. `PlayableScenarioStartupPlayModeTests.ProductionWeeklyPassProducesFarmOutputAndQueuesRealLogistics` observes zero crop inventory from the staffed farm path.
3. `Pass11Ws6FirstGateBootstrapTests` M1/M2 both fail in Unity; saved Saloon restoration and late qualifier registration need a dedicated scenario bootstrap correction.
4. `TownHallFoundationTests` has three failures: generated town places zero qualifying town halls, old-save civic load remains unbuilt, and the unbuilt summary now reports `Blocked` instead of the older `Not built` wording.
5. The Pass XII manifest's scene-dependent items remain: scenario HUD/read-model bindings, bank underwriting caller, boarding parcel mapping, NPC construction supplier registration, and detailed business runtime activation.
6. The full 30-day ordinary Main Scene proof and complete visual UI review remain unproven.

