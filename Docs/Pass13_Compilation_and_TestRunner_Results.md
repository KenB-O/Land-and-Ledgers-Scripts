# Pass XIII Compilation and Test Runner Results

## Runner mechanism

Unity CLI Test Runner was used with Unity `6000.4.2f1`. Earlier invocations stalled during initial import/ILPP and produced no result file; those runs are recorded as incomplete and are not counted as tests. After compatibility corrections, later invocations completed and wrote NUnit XML.

## Completed results

| Run | Total | Passed | Failed | Skipped | Result file |
|---|---:|---:|---:|---:|---|
| `HouseholdReserveInventoryTests` EditMode | 17 | 17 | 0 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\household-reserve-20261028.xml` |
| `Pass12` EditMode filter | 126 | 126 | 0 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\pass12-focused-20261028.xml` |
| `Pass12ScenarioPresentationTests` EditMode | 8 | 8 | 0 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\scenario-presentation-20261028.xml` |
| `OpportunityPressureNoticeTests` + `MineLaborRegisterTests` EditMode | 31 | 31 | 0 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\finance-opportunity-20261028.xml` |
| `TownHallFoundationTests` EditMode | 12 | 9 | 3 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\pass13-civic-focused-20261028.xml` |
| `Pass11Ws6FirstGateBootstrapTests` EditMode | 2 | 0 | 2 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\pass11-bootstrap-20261028.xml` |
| `PlayableScenarioStartupPlayModeTests` PlayMode | 10 | 8 | 2 | 0 | `F:\Land & Ledgers_Pass13_Backups\pass13-reports\first-ledger-playmode-20261028.xml` |

The two PlayMode failures are `FirstLedgerNormalProductionReachesTheLiveLadder` and `ProductionWeeklyPassProducesFarmOutputAndQueuesRealLogistics`. They are production assertions, not runner failures. The two M1/M2 bootstrap tests remain failing and require a separate scenario bootstrap investigation.

