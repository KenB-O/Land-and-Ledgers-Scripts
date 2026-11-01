# Phase I — Regression Sweep (I1)

Branch: `muse/real-people-shared-credit` @ `fad195e` (32 commits from `7b1ca29`).
Date: Oct 10, 2026. Run on the Phase I Roslyn harness (`harness-i.csproj`).

## Method

Extended the Roslyn `csc.dll` + reflection harness (UnityEngine/NUnit shims) to
compile and run **everything harnessable**: all Phase B–H test suites plus the
pre-existing suites they touch. Real repo source is compiled as-is; shimmed
types are documented below. Nothing is claimed as a Unity result.

New in `harness-i` vs `harness-h` (see `roslyn-harness/README.md` for the full
shim inventory):

- Added test fixtures: `PhaseBWageCreditingTests`, `PhaseBHouseholdConsumptionTests`,
  `HouseholdShoppingLoopTests` (Phase B/C, never previously harness-run),
  `BusinessLiabilityLedgerTests`, `MineLaborRegisterTests`, `CreditRegistryTests`,
  `FinancialObligationAuthorityTests` (28), `FirstLedgerGoalEvaluatorTests` (6),
  `EmbodiedPurchaseExecutorTests`, `HouseholdLedgerTests`.
- Added real sources: `BusinessRuntimeState.cs`, `MineLaborRegister.cs`,
  `RecruitmentService.cs`, `PostalService.cs`, `JourneyModel.cs`, `JourneyTravel.cs`,
  `RouteConditions.cs`, `MineShaftPlan.cs`, `MineRuntimeModels.cs`, `MineOperations.cs`,
  `MineOreLots.cs`, `MineAssay.cs`, `MineCostLedger.cs`, `MineTimbering.cs`,
  `MineHoisting.cs`, `MineGradeSettlement.cs`, `MineOreShipment.cs`,
  `HouseholdLegacyCashMigrator.cs`, `FinancialObligationReadModel.cs`,
  `HouseholdShoppingReadModel.cs`, `ScenarioModels.cs`,
  `FirstLedgerGoalEvaluator.cs`, `FirstLedgerScenario.cs`.
- Shim additions (all documented in-file): `BusinessRuntimeSaveShim.cs` (verbatim
  DTO shapes from `Infrastructure/Persistence/SaveDtos.cs`),
  `BusinessCoreShim.cs` (verbatim `BusinessThroughputMode`, `BusinessMainFocusState`,
  `BusinessMainFocusDefinition`, `BusinessOwnerKind`, `BusinessOwnerIdentity`,
  `BusinessOwnerSaveDto`), `MineSaveShim.cs` (verbatim `MineralResourceKind`,
  `MineRuntimeSaveDto`, `EquipmentRequirementCodes`; minimal documented
  `BusinessInstanceState` shape — the real class is the Unity-bound creation
  closure), `DomainShimI.cs` (DomainShim minus the `WorkerSlotState` stub, which
  the real `BusinessRuntimeState.cs` now provides — per the harness honesty rule
  the real type wins).
- Shim behavior extensions: `Mathf.Approximately`, `CollectionAssert.AreEqual`,
  `Assert.Contains`, non-generic `Assert.AreEqual(object, object)`, `[SetUp]`
  support in the runner, and fixture detection without `[TestFixture]` (matches
  NUnit semantics — two pre-existing fixtures omit the attribute).

## Result

**RESULT: 261 passed, 1 failed** (262 tests, 29 fixtures).

| Fixture | Pass | Fail |
|---|---|---|
| FinancialObligationAuthorityTests | 28 | 0 |
| PhaseGNpcFormationTests | 21 | 0 |
| HouseholdShoppingLoopTests | 21 | 0 |
| PhaseBHouseholdConsumptionTests | 20 | 0 |
| HouseholdVerticalSlicePhaseHTests | 15 | 0 |
| PhaseESellerFinanceTests | 10 | 0 |
| PhaseBWageCreditingTests | 9 | 0 |
| PhaseGNpcBusinessLifeTests | 8 | 0 |
| PhaseFNpcPropertyLifeTests | 8 | 0 |
| NpcHomeConstructionTests | 8 | 0 |
| MineLaborRegisterTests | 8 | 1 |
| HousingResidenceTests | 8 | 0 |
| HouseholdLedgerTests | 8 | 0 |
| BoardingRentCollectionTests | 8 | 0 |
| PhaseFNpcPropertyDecisionTests | 7 | 0 |
| PhaseFNpcCommissionedBuildTests | 7 | 0 |
| PhaseFNpcAcquisitionTests | 7 | 0 |
| PhaseECreditBackendTests | 7 | 0 |
| HousingSearchTests | 7 | 0 |
| HouseholdVerticalSliceTests | 7 | 0 |
| PhaseENpcCreditLoopTests | 6 | 0 |
| FirstLedgerGoalEvaluatorTests | 6 | 0 |
| EmbodiedPurchaseExecutorTests | 6 | 0 |
| CreditRegistryTests | 6 | 0 |
| BusinessLiabilityLedgerTests | 6 | 0 |
| PhaseELenderDifferentiationTests | 5 | 0 |
| HousingDiagnosticsTests | 4 | 0 |

## The one failure — PRE-EXISTING at 7b1ca29, unrelated, not fixed

`MineLaborRegisterTests.SettleShiftWages_PaysBusinessOutflowAndWorkerInflow`:
"Expected 1, got 0."

Evidence it is pre-existing and unrelated to Phases B–H:

1. `git log 7b1ca29..fad195e` touches NEITHER
   `Domains/Economy/Businesses/Mine/MineLaborRegister.cs` NOR its test file.
2. `Domains/Population/HouseholdLedger.cs` is untouched by B–H, and the
   insufficient-funds rejection in `RecordOutflow` is byte-identical at
   `7b1ca29` (verified via `git show 7b1ca29:...`).
3. Root cause: the test constructs `new HouseholdLedger(50)` (balance 0) and
   asks `SettleShiftWages` to pay a 200c wage outflow. `RecordOutflow` rejects
   any outflow exceeding the balance ("insufficient funds, nothing moved"), so
   `paid` stays 0. The test's setup never funds the ledger. This fails
   deterministically against the base-branch code — pure C#, no Unity types on
   this path, so it is not a harness artifact either.
4. The behavior is the intended Canon 13.2 hardening (no overdraft minting);
   "fixing" it would mean weakening the ledger guard, which is out of scope.

Left as-is per the Phase I brief (document, do not fix unrelated code).

## Written-for-Unity exclusions (not runnable in this harness, unchanged)

- `LogisticsConservationGateTests` — needs Unity `AssetDatabase`/`MonoBehaviour`
  (Phase A verdict stands; 16 tests, base coverage intact).
- `FirstLedgerGameStateTests` — needs `ScriptableObject.CreateInstance` and the
  full business-creation closure (Codex/Luna surface).
- `FirstLedgerStakeTests` — needs the 1043-line `FirstLedgerSliceBootstrapper`
  with its Civic/Camera/Valuation dependency closure.
- `FirstLedgerGoalEvaluatorTests` (6) DID run here — pure C# with a fake state.
- `NpcBusinessCreationAdapter` and the general-store `MonoBehaviour` trading
  port remain written-for-Unity (unchanged from prior phases).

No regressions from Phases B–H were found. The full-Unity run remains
Codex/Luna's job.
