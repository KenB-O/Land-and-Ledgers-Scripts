# Codex Handoff — Muse Pass III: Real Lives, Land & Livelihoods

Branch: `muse/real-lives-land-livelihoods`
Tip after this doc: see `git log` (handoff commit on top of `7d5ff99`).
Baseline: `62f0e49` (Muse Pass II tip). Main untouched.
Unity: 6000.4.2f1. Code-only pass — no scenes, prefabs, terrain, or UI layouts touched.

Companion handoffs in this branch: `Docs/Pass3_WorkerC_Handoff.md` (household decisions),
`Docs/Pass3D_CodexHandoff.md` (capabilities). This doc is the integrator-level summary.

## 1. New production source files

Land & rights (`Domains/World/Property/Land/`, namespace `LandLedgers.World.Property`):
- `ParcelContracts.cs` — `LandStatus` (12), `ParcelRecord`, `HoldingRecord` (foundation)
- `PropertyInterest.cs` — `InterestKind` (9), `PropertyInterest`, `IPropertyInterestStore` (foundation)
- `ParcelSubdivision.cs` — request/validation/result, `IParcelSubdivisionService` (foundation)
- `OwnershipTransfer.cs` — `IOwnershipTransferService` (foundation)
- `ParcelService.cs` — legal-parcel registry, plotId index, `ParcelAtLocation` (O(1) via `TownCell.plotId`)
- `PropertyInterestStore.cs` — the single non-title rights store
- `ParcelSubdivisionService.cs` — atomic validate-then-execute subdivision

Housing & tenancy:
- `Domains/World/Property/TenancyPaymentService.cs` — rent/board loop driver (`SettleDueRents`)
- `Domains/World/Property/BuildingFootprintLink.cs` — read-only building→footprint view
- `Domains/Population/TenancyTerms.cs` — `TenancyPaymentTerms`, `RentObligationState` (foundation, additively extended)
- `Domains/Population/HouseholdDecisions.cs` — pressure input, decision kinds (foundation, additively extended)

Household decisions & livelihoods (`Domains/Population/`, namespace `LandLedgers.Population`):
- `HouseholdDecisionService.cs` — pure pressure→decision evaluator
- `HouseholdDecisionDriver.cs` — decisions → real operations
- `HouseholdOpportunityLog.cs` — known opportunities with provenance
- `HouseholdAutonomyDriver.cs` — NPC §13 responses (reserves, rent, job loss, housing)
- `DomesticTaskCatalog.cs` + `DomesticLaborService.cs` — 4 domestic task families on `TaskAuthority`

Capabilities (`Core/Skills/`, namespace `LandLedgers.Skills`):
- `CapabilityContracts.cs` — `CapabilityFamily` (10), 4-band `CapabilityBand`, `CapabilityConfidence`,
  `AptitudeRecord`, `ExperienceEntry`, `DomainFamiliarity`, `PersonCapabilityState`,
  `CapabilityEvidenceReading` (foundation)
- `TaskOutcomeContracts.cs` — `CausalOutcomeInputs/Result`, `ITaskOutcomeResolver` (foundation)
- `CapabilityStores.cs` — aptitude/experience/domain/capability stores + formation service
- `CausalTaskOutcomeResolver.cs` — causal resolver + duration estimator (replaces flat rule)
- `CapabilityEvidenceService.cs` — observer-side evidence readings + §4.5 validation

Read models (`ReadModels/Livelihood/`, namespace `LandLedgers.Reporting`):
- `LivelihoodReadModel.cs` (foundation) + `LivelihoodReadModelBuilder.cs`

Every file has a fresh-GUID `.meta`. Folder metas: `Domains/World/Property/Land/`,
`ReadModels/Livelihood/`.

## 2. Modified existing files (all surgical/additive except the two canon corrections)

- `Core/Skills/SkillModels.cs` — `PersonSkillState` → legacy migration record; `SkillDefinition` gains `CapabilityFamily`
- `Core/Skills/SkillService.cs` — model migrated XP→continuous capability; owns new stores;
  removed `GrantPractice`/`ScaleMinutes`/`ApplyPracticeFromTask`/`SkillTaskDurationEstimator`;
  `GetLevel` kept as derived projection for trade runtimes
- `Domains/Population/CapabilityPresentation.cs` — **canon correction**: 7-band → 4-band
  (Ordinary/Great/Excellent/Mastered); "Not enough information" rendering
- `Domains/Economy/Labor/ContactGraphService.cs` — additive optional `ICapabilityEvidenceReader`
- `Domains/Economy/Labor/EmploymentNegotiationService.cs` — additive optional evidence reader
- `Domains/Economy/Core/SharedBusinessRuntimeManager.cs` — call-site update for 4-band
- `Domains/World/Property/PropertyTitle.cs` — `TitleAuthority : IOwnershipTransferService` (+93/−1)
- `Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` — `IsPlayerOwnedPlot` consults the
  title chain for parcel-linked plots; legacy OR-merge kept as fallback (+10/−1)
- `Domains/World/Property/HousingFoundation.cs` — `PropertyAgreement.PaymentTerms` (optional) + helpers
- `Infrastructure/Persistence/SystemsSaveDtos.cs` — `parcels`, `propertyInterests`,
  `tenancyPayments`, `householdDecisions`, `householdDecisionDriver`, `domesticLabor`,
  `householdAutonomy` fields; `SkillServiceSaveDto` extended (capability stores)
- `Orchestration/Systems/SimulationSystemsHub.cs` — lazy `Parcels`, `PropertyInterests`,
  `ParcelSubdivisions`, `TenancyPayments`, `HouseholdDecisions`, `HouseholdDecisionDriver`,
  `DomesticLabor`, `HouseholdAutonomy`, `HousingSearchNeeds` + capture/load
- `Orchestration/Systems/SimulationDrivers.cs` — installs `CausalTaskDurationEstimator`
- `Tests/Editor/Core/SkillServiceTests.cs`, `Tests/Editor/Population/CapabilityPresentationTests.cs` —
  rewritten for the canon correction (documented in commit messages)

## 3. New persistent services & data models

All registered in `SimulationSystemsHub` with capture/clear-then-load in `SystemsSaveDtos`:
`ParcelService` / `PropertyInterestStore` / `ParcelSubdivisionService` / `TenancyPaymentService` /
`HouseholdDecisionService` / `HouseholdDecisionDriver` / `DomesticLaborService` /
`HouseholdAutonomyDriver` / `HousingSearchNeedRegistry` / SkillService capability stores.
Title chain persists via `AcquisitionSaveDto.titleChain` (unchanged).

## 4. Existing authorities reused (not replaced)

`TitleAuthority` (chain extended, not rewritten) · `TownGrid`/`TownCell`/`GridCoord` (2 m grid,
cell→plot O(1)) · `HouseholdLedger` (single cash writer) · `FinancialObligationAuthority`
(arrears as real `Payable`) · `TaskAuthority` (estimator seam) · `ContactGraphService` /
`EmploymentNegotiationService` / `PersonJobDecisions` / `WorkScheduler` / `WageSettlementService`
(Pass II, reused as-is) · `HousingFoundation` occupancy chain · `PlayerTitleBridge` pattern.

## 5. Ownership-truth migration status

Four records answered "who owns this plot" (`TownPlot.playerOwned`,
`PlacedBuilding.playerOwned`, `ownedPlotIds`, title chain). This pass: `TitleAuthority`
implements `IOwnershipTransferService`; `IsPlayerOwnedPlot` delegates to the chain for
parcel-linked plots with the legacy OR fallback. `playerOwned` flags are not yet rewritten
as derived projections — that is Codex-side once all writers route through the transfer
function. Do NOT create another registry.

## 6. Unity scene adapters Codex must provide

1. `IParcelBuildingIndex` ← `PlacedBuilding` records (footprint + plot linkage). Subdivision
   warns (does not verify) until set.
2. `ITenancyPayeeDirectory` — person→household (`HouseholdMembershipRegistry`),
   business→cash sink (`BusinessRuntimeState.AddCashCents`, same pattern as boarding-house
   `IRentCashSink`).
3. `TitleAuthority.TitleHolderLookup` on the subdivision service (parcelId→holder).
4. `Parcels.Grid` ← live `TownGrid` for spatial queries.
5. Hub exposure of `HousingAuthority` (scene/manager-owned) for the tenancy driver.

## 7. Runtime caller connections (drivers)

- Daily: `hub.TenancyPayments.SettleDueRents(housingAuthority, dayIndex, ledgers, payeeDirectory,
  obligations, ids, diag)`; `HouseholdAutonomyDriver.EvaluateHouseholdDay` per household
  (supply `RentObligationsFor`, `ShelterRiskFor`, memberships); `EmploymentNegotiation.AdvanceDay`
  (existing).
- Weekly: `WageSettlementService.SettleWeeklyPayroll` per business (existing, idempotent).
- Task completion: drivers should call `SkillService.ResolveTaskCompletion(...)` and pass the
  result to `TaskAuthority.CompleteTask` — the live `RecordWork` auto-complete still defaults
  to Success until this hook is wired (harness proves the path).
- `DomesticTaskCatalog.RegisterAll` alongside `RegisterTaskCatalogs`.
- Optional: `contactGraph.CapabilityEvidence = hub.Skills.Evidence`.

## 8. Read models for future UI

`LivelihoodReadModel` (person livelihood, observer-visibility enforced) ·
`TenancySettlementResult` (settled/partial/overdue + diagnostics) ·
`ManagerActivityLog`, `PayrollEvents`, `LaborDemandReport`, `WorkAssignmentReport`,
`EmploymentOffer` (Pass II, unchanged).

## 9. Conflicts anticipated with Codex's unpublished labor integration

The unpublished `integration/muse-autonomous-labor` branch was reported to touch business task
queues, `WorkScheduler`, person-time reservations, manager recruitment, payroll, persistence.
Overlap watch: `SimulationDrivers.cs` (~line 254, estimator installation),
`SkillServiceSaveDto`, `SystemsSaveDtos.cs`, `SimulationSystemsHub.cs`. All Pass III edits
there are additive; merge carefully, keep every field set.

## 10. Manual Unity tests

Pass II (still open): T11 payroll idempotency · T12 manager unattended week ·
T13 recruitment letter round-trip · T14 slice smoke.
New: T-D1–T-D5 (capabilities — see `Docs/Pass3D_CodexHandoff.md`).
New: tenancy partial-payment/arrears in PlayMode; subdivision in the live world
(physical re-survey: children clear `PlotId` — one `TownPlot` cannot map to N legal parcels,
world-authority work needed); save/load mid-subdivision and mid-tenancy.

## 11. Known limitations & deferred work

- No Unity verification here (Roslyn harness only): 485 passed, 1 failed — the failure is the
  pre-existing `MineLaborRegisterTests.SettleShiftWages_PaysBusinessOutflowAndWorkerInflow`
  at baseline, unrelated, untouched.
- **Experience formation rate is nearly invisible** over realistic timescales
  (`BaseGainPerHour = 0.0001`): recommend playtest-feel review before tuning; flagged, not redesigned.
- `WorkerVisibleProfile.generalSkill` (0–100) retained; 4-band presentation bridges from it.
- Migration-linked movement reuses journey tasks (evaluation-only, as briefed).
- `HouseholdShoppingLoop` does not yet consume `HouseholdSpendingPolicy` (recorded instruction).
- `TenancyNegotiationRequest`s sit Pending — landlord response is NPC-side.
- Borrow records the liability; the lender's cash payment is the lender's own ledger action.
