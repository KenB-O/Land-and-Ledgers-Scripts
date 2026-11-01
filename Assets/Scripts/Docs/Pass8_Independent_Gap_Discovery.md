# Pass VIII Independent Gap Discovery

Muse Pass VIII, Phase 4, Brief Section 15. Investigation by an independent worker with no implementation role; fixes executed separately per Brief Section 16.

## Method

Read-only audit of the fully integrated Pass VIII codebase at branch head a80ee76 (Wave 1, Wave 2, and all prior passes merged). Primary technique: exhaustive caller-graph analysis over the repo (every public method and class cited had its production callers enumerated; tests and self-references excluded), targeted source reads, and runtime probes for determinism. An isolated Roslyn harness copy confirmed a clean build with zero errors. Pass VIII implementation claims (retail pools, shipment idempotency, payroll attendance, lender cycle, Ruston reconciliation) were spot-verified real, not re-audited.

## Complete Ranked Issue Register

### G-01 — NPC businesses can form but never close (P1) — FIXED in Pass VIII
`NpcBusinessLifecycleService.EvaluateWeek` and `CloseOrderly` (Domains/Economy/Creation/NpcBusinessLifecycle.cs) were fully implemented and tested but had zero production callers; only formation ran weekly. Fixed by adding a weekly lifecycle driver (`NpcBusinessLifecycleDriver.cs`) hooked into the Unity weekly tick beside the entrepreneurship call. Creative destruction now operates: distressed NPC businesses close through the proven orderly path with creditor settlement.

### G-02 — Freight and postal revenue could never be collected (P1) — FIXED in Pass VIII
`FreightCompanyRuntime.CollectFreightCharge` and `PostalService.CollectPostageRevenue` had no live callers; both business types were structurally unprofitable. Fixed with pure idempotent collection scans posting to the business ledger with provenance, hooked into the day/week tick. Known limitation: freight has no production tick drive yet (no live freight company instances exist; wiring one would have meant redesigning freight dispatch, out of scope).

### G-03 — RentalFleet recorded fees but moved no cash (P1) — FIXED in Pass VIII
`RentalFleet.RentOut` recorded `FeeCents` with no debit or credit anywhere. Fixed with refusal-first renter debit and owning-business credit through the legitimate cash authorities; damage liability settles on return.

### G-04 — Postal dispatch RNG seeded with string.GetHashCode (P2) — FIXED in Pass VIII
`PostalService.cs` seeded its reliability RNG with `destOfficeId.GetHashCode()`, which .NET randomizes per process, breaking scenario determinism and flaking `RegisteredMailServiceTests.ReturnReceipt_TravelsAsRealMail`. Fixed with a stable FNV-1a hash; dispatch probabilities untouched. The fix eliminated all 7 postal/registered-mail flakes from the full suite.

### G-05 — Due-event ghost state on New Game (P2) — FIXED in Pass VIII
`DueEventScheduler` had no `Clear()`; `ResetForNewGame` cleared everything except due events, so the previous campaign's domestic-labor and work-allocation events fired in the new world. Fixed with `Clear()` on both scheduler implementations, called from the reset port as a "due-events" cleared category.

### G-06 — Pending owner-approval requests never resolved (P2) — FIXED in Pass VIII
`ManagerPendingRequest`s (always-escalated SellAsset, escalated Borrow) grew unboundedly with no resolver. Fixed with NPC-owner auto-resolution policy: distressed businesses approve asset sales at or above appraised value; escalated borrows route to real lender underwriting. Player-owned requests stay pending for the future UI. Conservative by design: with no production appraisal source, live sale requests are denied with a recorded reason rather than approved blind.

### G-07 — Service revenue without payer or demand (P2) — FIXED in Pass VIII
The `BusinessThroughputMode.Service` branch posted revenue from staffed capacity alone via payerless `AddWeeklyLocalTransferRevenue`: a hidden profitability floor contradicting the circulation audit. Fixed per coordinator decision (option a): service visits now route through household service spending, mirroring the doctor's daily path. Zero demand earns zero; shortfalls record as unfulfilled demand.

### G-08 — Detailed mine ore chain built but unwired (P2) — OPEN
`MineProductionCycle.RecordBreakOreShift` (Pass VIII production-networks work) has zero production callers, as do `MineOreShipment`, `MineAssay`, `MineGradeSettlement`, `MineHoisting`. The live path remains the abstract weekly category-stock route. Wiring the full chain (worked minutes → assayed lots → graded settlement → delayed shipment) is a large scoped work item for a future pass. The abstract path is coherent and depletes richness; no duplication exists today.

### G-09 — Twelve detailed business runtimes (~32k lines) with zero production callers (P2) — REGISTERED
Bakery, Barber, Doctor, GrainElevator, GrainMill, Hotel, LumberYard, Newspaper, Restaurant, Saloon, Sawmill, and Tailor runtimes are fully built and tested but never execute; production runs on the generic weekly path. Registered in `Docs/UnwiredSystems.md` with a machine-checkable harness test asserting registry-vs-reality in both directions. Wiring any runtime is a separate scoped item; the registry prevents future workers from mistaking dead code for the live path.

### G-10 — Dead staged-production subsystem (P2 trap / P3 code) — REGISTERED
`StagedProductionProcess`, `ProductionDueEventDriver`, `BakeryStagedProductionChain`, `ForgeRepairChain`: three bakery production implementations exist (staged chain, shop runtime, generic path) with only the generic path live. Registered as unwired; no deletions made.

### G-11 — Childcare, caregiving, recovery have no scheduled person-time (P2) — OPEN
The domestic catalog covers only meal-prep, fuel-handling, animal-care, and chores; `PersonActivityKind` has no Childcare, Caregiving, or Recovery. Dependents bind no one's schedule, weakening the household pressure model. All seams exist (`DomesticTaskCatalog`, `DomesticLaborDriver`, `DailyActivityPlanner`); a medium, self-contained future item.

### G-12 — StoreRestockService: dead second procurement engine (P3) — REGISTERED
A full procurement chain with zero production callers; the live path is `ManagerProcureExecutor`. Registered as unwired.

### G-13 — LaborMigrationLinker undriven (P3) — OPEN
Migration evaluation exists ("this linker moves nobody") with no driver and no intent-to-journey integrator. Documented deferral; needs journey-integration design.

### G-14 — Unbounded schedule/packet lists, O(n) hot-path scans (P3) — OPEN
`PersonScheduleTracker` and `KnowledgeTransmissionStore` grow monotonically with linear scans on daily hot paths. Correctness unaffected; a long-campaign performance tax. Needs measurement before tuning; see the Pass VIII performance audit.

### G-15 — ConstructionInputSupportTests excluded from harness (P3) — OPEN
The only coverage of player construction input-support rules needs `UnityEditor` and is excluded. Coverage gap, not a behavior bug; a Unity-free seam would restore harness coverage.

### G-16 — ManagerExecutionServices.FindAsset vestigial (P3) — OPEN
Declared delegate with zero reads and no wiring. One-line deletion when convenient.

## What the discovery proved about the pass

The highest-severity finds (G-01, G-02, G-03) share a shape: fully implemented subsystems with no production driver calling them. Passes I through VII built vertical slices; the integration step of actually invoking them in the live tick was repeatedly deferred. The unwired-system registry (G-09/G-10/G-12) exists to stop this pattern from compounding: no future worker should mistake dead code for the live path, and no new subsystem should land without its driver call.

## Issues deliberately not fixed

G-08 (large, own work item), G-11 (medium, self-contained future item), G-13/G-14/G-15/G-16 (P3s). G-14 awaits measurement; G-08 awaits a driver-design decision. All are recorded in the deferred register with stable IDs.
