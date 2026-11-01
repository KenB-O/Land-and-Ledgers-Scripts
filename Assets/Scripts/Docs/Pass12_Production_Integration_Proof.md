# Pass XII — Production Integration Proof

This document records which systems are actually driven by production code on
the final tree — not which systems exist. "Wired" below means a real production
caller was verified in source. Anything else is gated or Unity work, stated so.

## Live production paths (verified)

- **Generic business production**: `SimulationDrivers.DriveGenericProduction`
  → `GenericProductionRuntimeRegistry` (Bakery, smithy, and generic methods).
  Proved by B's tests and the WS4 proof.
- **Mine**: `MineProductionDriver` via `SharedBusinessRuntimeManager`
  `ResolveMineOperation`; abstract ore path retired (Pass IX).
- **Farm**: `FarmCycleDriver` via `ResolveFarmOperation`; harvest only from
  completed field tasks.
- **Freight**: `FreightProductionDriver` owns `FreightCompanyRuntime` instances
  through `SimulationDrivers`; equipment exclusivity checked before dispatch;
  rejected deliveries retain custody; revenue settles only on accepted delivery.
  Dispatch-authority persistence fixed in Pass XI (PASS11-002).
- **NPC construction**: `NpcConstructionDriver`, one step/day via
  `SimulationDrivers`, site stockpile as freight receiving authority.
  Construction sequence persistence fixed in Pass XI (PASS11-001).
- **Estate lifecycle**: daily step `DriveDemographicDay` →
  `DriveEstateAdvancement` → `EstateLifecycleDriver.AdvanceOpenEstatesOneStage`.
- **Domestic labor**: `DomesticLaborDriver` on the short tick, gated by the pure
  `DomesticTickGate` (Pass XI PASS9-006 fix); SaveLoadManager resets the gate.
- **Payroll**: weekly employment path + exactly-once manual path (Pass XII C);
  throwing lookup accrues as a named payable on both employment and legacy-slot
  paths; settlement only through `FinancialObligationAuthority`.
- **Scenario**: `BeginScenario` + `Evaluate` production calls; dated qualifiers
  persisted and rebuilt after load; completion/failure latching; failure state
  persisted exactly once. `ScenarioDirector.RestoreFromSaveDto` no longer
  re-issues activation when the DTO already issued it (Pass XII A).
- **Drivers**: `SimulationDrivers.Rebind` now has a real production caller
  (`MarkRestoredFromSave`, post-restore). Single-instance guard on Drivers.
- **Bank binding contract**: `BankLedgerBindingRegistry` (one ledger per bank,
  fail-loud on a second, live reads, no cached balance) with the
  `SimulationDrivers.BindBankLedger` binding point — deliberately NOT wired
  into underwriting (would double-count; see below).

## Still unwired (registry truth)

`Docs/UnwiredSystems.md` is the machine-checked registry (16 runtimes after
Pass XII F: the original 13 + ButcherRuntime, BlacksmithRuntime,
TanneryRuntime). Unwired entries include all detailed `*Runtime` business
models, `StagedProductionProcess` / `ProductionDueEventDriver`,
`BakeryStagedProductionChain`, `ForgeRepairChain`, `StoreRestockService`,
`RealDemandSource`, `HouseholdMoveService`, and `ScenarioFailureNoticeBoard`
(as a live panel surface; its source consumer, the HUD snapshot builder, itself
has zero production callers — the HUD chain is unwired end-to-end).

Also unwired, by verified absence of production callers:
`ScenarioBootstrapFactory.TryCreateStart` (no preset-selection trigger exists),
`BindBankLedger` into underwriting, `AttachHouseholdMemberships`,
`CreateHouseholdConstructionMaterialSource`, `BindNpcConstructionPorts`,
`QueueNpcConstructionRequest`, `RegisterDueEventHandler`, and
`BoardingLodgingBridge.RegisterHouse` (parcel mapping + boarding trigger
missing; boarding bed capacity via `BoardingHouseShopRuntime` is not blocked).

## Integration proof (source level)

- WS4 30-day production settlement proof: preserved exactly (see E above).
- Small + large settlement fixtures: cash conserved exactly, determinism-twin
  identical, 90-day restore-at-45 identical.
- Legacy suites on the final tree: see
  `Docs/Pass12_Final_Regression_Results.md` (90-day, Ruston Decline,
  TickSize, ThreeYear, SaveLoad 85/0, freight, banking, obligations,
  production, scheduling, employment, household, scenario, New Game).
- **Boundary, stated plainly**: none of this is the Unity RUN-004 Main Scene
  30-day PlayMode proof. That proof, the Unity Test Runner (including M1/M2
  bootstrap tests, PASS11-008), full-project compilation including the four
  harness-excluded files (Pass XI PASS9-014, narrowed), and all scene/prefab
  wiring remain Codex/Unity work. No Unity verification is claimed anywhere
  in Pass XII.
