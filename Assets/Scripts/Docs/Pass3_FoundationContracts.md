# Pass III Foundation Contracts — Muse Pass III (Real Lives, Land & Livelihoods)

Branch: `muse/real-lives-land-livelihoods` · Baseline: `62f0e49` (Muse Pass II tip)
Date: 2026-10-11 · Status: foundation commit; implementation workstreams build on this.

## Contract files (this commit)

| File | Namespace | Contents |
|---|---|---|
| `Domains/World/Property/Land/ParcelContracts.cs` | `LandLedgers.World.Property` | `LandStatus` (12 values), `ParcelRecord`, `HoldingRecord` |
| `Domains/World/Property/Land/PropertyInterest.cs` | `LandLedgers.World.Property` | `InterestKind` (9 values), `PropertyInterest`, `IPropertyInterestStore` |
| `Domains/World/Property/Land/ParcelSubdivision.cs` | `LandLedgers.World.Property` | `SubdivisionRequest/Validation/Result`, `IParcelSubdivisionService` |
| `Domains/World/Property/Land/OwnershipTransfer.cs` | `LandLedgers.World.Property` | `IOwnershipTransferService` + duplicate-truth migration path |
| `Domains/Population/TenancyTerms.cs` | `LandLedgers.Population` | `TenancyPaymentTerms`, `RentObligationStatus`, `RentObligationState` |
| `Domains/Population/HouseholdDecisions.cs` | `LandLedgers.Population` | `HouseholdPressureInput`, `HouseholdDecisionKind`, `HouseholdDecision`, `IHouseholdDecisionService` |
| `Core/Skills/CapabilityContracts.cs` | `LandLedgers.Skills` | `CapabilityFamily` (10), `CapabilityBand` (4-band), `CapabilityConfidence`, `AptitudeRecord`, `ExperienceEntry`, `DomainFamiliarity`, `PersonCapabilityState`, `CapabilityEvidenceReading` |
| `Core/Skills/TaskOutcomeContracts.cs` | `LandLedgers.Skills` | `CausalOutcomeInputs/Result`, `ITaskOutcomeResolver` |
| `ReadModels/Livelihood/LivelihoodReadModel.cs` | `LandLedgers.Reporting` | `LivelihoodReadModel` (derived-only, observer-visibility rule) |

Pure data + interfaces. No logic, no Unity dependencies, no scene references.

## ID conventions

- `parcel-` + 32 lowercase hex · `holding-` + 32 hex · `interest-` + 32 hex
- Deterministic ids where the domain already uses them (e.g. Pass II `emp-<offerId>`); never a bare GUID object per grid cell — `GridCoord` is the cell address.
- Serialized enums are append-only; do not renumber.

## Authority table (established by the 2026-10-11 audits — do not re-litigate)

| Fact | Authority | Notes |
|---|---|---|
| Legal parcel identity/geometry/lineage | `ParcelRecord` (new) | TownPlot covers physical footprint only |
| Who holds title | `TitleAuthority` chain | `IOwnershipTransferService` is its transfer function |
| Non-title rights (lease/easement/charge/entry/ROW/restriction/dispute/lien) | `PropertyInterest` store (new, single) | No second rights registry |
| Economic parcel grouping | `HoldingRecord` (new) | Grouping only, never merges title |
| Spatial substrate / cell→plot lookup | `TownGrid`/`TownCell`/`GridCoord` (existing, 2 m) | Already canonical; the array IS the index |
| Rent/board payment terms | `TenancyPaymentTerms` on `PropertyAgreement` (additive) | No second rent-cash ledger |
| Cash movement | `HouseholdLedger` (existing single writer) | Arrears via `FinancialObligationAuthority` |
| Capability truth vs observer knowledge | `PersonCapabilityState` vs `CapabilityEvidenceReading` | Never expose hidden truth directly |
| Task outcome variation | `ITaskOutcomeResolver` via existing estimator seam | Replaces flat "-1 min/level" rule |

## Duplicate-ownership-truth migration (from the audit)

Four records answer "who owns this plot" today: `TownPlot.playerOwned`,
`PlacedBuilding.playerOwned`, `AcquisitionMarketManager.ownedPlotIds`, `TitleAuthority` chain.
`IsPlayerOwnedPlot` is the load-bearing OR-merge. Migration: new flows use
`IOwnershipTransferService`; flags become derived projections; `ownedPlotIds`
becomes a chain query. Do NOT break the OR-merge before the chain covers every
writer. Do NOT create a third registry.

## Worker ownership (merge order: A, B, C, D)

- **Worker A — land core**: `ParcelRecord`/`HoldingRecord` services, `IPropertyInterestStore`
  impl, `IParcelSubdivisionService` impl (atomic, access-preserving), `IOwnershipTransferService`
  impl on `TitleAuthority`, derived `IsPlayerOwnedPlot`, save/load DTOs, spatial perf tests.
- **Worker B — buildings/housing/tenancy**: `PropertyAgreement` + payment terms (additive),
  rent/board payment loop driver, landlord credit/arrears, occupancy wiring through the
  existing `HousingFoundation` chain, save/load, tests.
- **Worker C — household decisions/livelihoods/domestic**: `IHouseholdDecisionService` impl,
  `LivelihoodReadModel` builder, NPC autonomous responses (Phase I), domestic task families
  on existing `TaskAuthority`, save/load, tests.
- **Worker D — capabilities/skills**: aptitude/experience/domain stores, experience formation
  (diminishing returns, no calendar-time gains, no double-credit on restore),
  `CapabilityEvidenceReading` service wired into contact-graph recruitment, causal outcome
  resolver replacing `SkillTaskDurationEstimator`'s flat rule, 4-band migration of
  `CapabilityPresentation` + its tests, save/load, tests.

## Protected — additive only, do not rewrite

Pass II labor glue: `SimulationDrivers`, hub orchestration, `WageSettlementService`,
`WorkScheduler`, payroll drivers. The existing `SkillService` storage/save-DTO plumbing
(model migrates inside it). `TaskAuthority` (plug in via the estimator seam).
`HousingFoundation` models. `FinancialObligationAuthority`, `HouseholdLedger`.

## Environment (for every worker)

- Fresh GUID per new `.meta` — generate with `python3 -c "import uuid; print(uuid.uuid4().hex)"`
  (no `uuidgen` on this VM). Folder `.meta` files use the `folderAsset: yes` format.
- C# SDK: `export DOTNET_ROOT=$HOME/workspace/.tools/dotnet; export PATH="$DOTNET_ROOT:$PATH"`
  then `dotnet --list-sdks` must show 8.0.425. Compile-check via the Roslyn harness before pushing.
- Tests: `Tests/Editor/<Domain>/`, deterministic, no Unity claims. Never describe a test as
  passing unless compiled and executed. Pre-existing `MineLaborRegisterTests` failure is
  recorded, not re-litigated (fix lives in the unpublished Codex branch).
- Canon: proposals 001–105 approved; 106–145 NOT approved. Cite document/revision/section
  for consequential decisions. Genuine forks → record and escalate, never guess.
