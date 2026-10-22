# Pass IX — Unwired Systems Update

The single registry remains Docs/UnwiredSystems.md with its harness verification tests. No second registry was created. This document records what changed in the registry during Pass IX and the state of the assembly-scan guard at pass close.

## Newly production-reachable in Pass IX (verified by the independent review to caller file and line)

- MineProductionCycle chain (through MineProductionDriver and the ResolveMineOperation delegation).
- FreightCompanyRuntime (owned by FreightProductionDriver through SimulationDrivers).
- FarmCycleDriver. EstateLifecycleDriver (daily step). DomesticLaborDriver (short tick). DailyActivityPlanner and ConversationRunner (day change). WeeklyAttendanceAccumulator (installed and fed in production).
- CivicTreasuryService (hub-owned, persisted, driven by the player and NPC tax legs). AssetSaleAppraisalService and AssetSaleExecutionService (through the manager cycle and the liquidation port). Schoolhouse teacher pay (driven at term start on the day-change path).

## Entries added to the registry in Pass IX

- RealDemandSource: registered as honestly unwired. No executor exists, and registering it as wired would be false. A future pass must wire an executor or retire the type (master register PASS9-008).
- ScenarioObjectiveService: registered by the review-fix round. The service is hub-owned and persisted, its evaluation is source-complete, and goals completed by their deadline now latch. Production scenario start belongs to the scene bootstrap hook and is listed in the Unity handoff.
- HouseholdMoveService: registered by the review-fix round after the review verified it has zero production references outside its own file. It was previously accounted for only inside the scan baseline, which the registry document never mentioned.

## Assembly-scan guard state

- The scan (Pass9RegistryAssemblyScanTests) certifies that every type in the tree is either production-referenced outside its owning feature directory or accounted for in the feature-internal baseline, and that every registry entry still matches reality. It passes on the final tree, together with UnwiredSystemsRegistryTests.
- The baseline is shrink-only: entries leave it only by genuine wiring or registration. During Pass IX the coordinator classified two WS5 appraisal DTOs as feature-internal to a wired service with a review note; the review-fix round removed BusinessCashPostagePayer from the baseline after R-03 gave it a genuine production caller.
- HouseholdLedgerPostagePayer remains in the baseline for one precise reason: its only production construction sits inside RegisteredMailService in the same PostOffice directory, which the scan's name-reference rule counts as feature-internal, so it has no scan-visible production caller. The type is genuinely wired in production (persons' postage resolves through it); the baseline entry is a limitation of the scan rule, recorded here rather than hidden.
- Known scan limitation (review R-13): the name-reference rule counts hub construction as wiring, so a constructed-but-never-driven type can pass the scan. The registry document itself was spot-checked accurate for every entry the review sampled (including BakeryShopRuntime, NewspaperRuntime, SaloonRuntime, and StoreRestockService as still unwired). A method-call-reference rule is the recorded future improvement; it was not smuggled into this pass.

## Dormant versus should-be-running

- Intentionally dormant (design-gated, do not wire): water and sanitation (PASS8-016, Canon verdict), RealDemandSource pending its wire-or-retire decision, the scenario catalog beyond the two coded scenarios.
- Should-be-running but scene-dependent: ScenarioObjectiveService production start and ScenarioBootstrapFactory, both assigned to the Unity handoff. Source connection and scene connection are distinct states throughout the registry, and only source connection is claimed for them.
