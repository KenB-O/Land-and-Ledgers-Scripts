# UBC L1 — Save/Load Continuity Audit (2026-11-17)

Base: `muse/universal-business-construction-2026-11-17` at `1cba10e5`
(E1, S2, D1, A1, S3, G1 merged). Integration harness at audit start:
5049 passed / 1 failed / 5050 total (single failure Pass9, the 7 VetServices
types S4 is classifying — out of scope here).

Scope: business state, ownership transitions, capability changes, production
orders, employee assignments, agreements, construction projects, partial work
packages, site stockpiles, cargo/freight jobs, blacksmith repair orders,
horseshoe inventory, glass orders, construction loans, household inventories
and balances. Rules checked: opening resources apply once; Continue must not
reissue stock, money, or equipment; restored partial projects stay partial;
contracts are not recreated under new identities; business IDs stay stable
across ownership changes. Every finding below was verified against code on
disk; nothing here is speculative.

## L1-1 — TOP BREAK (confirmed): BlacksmithRuntime has no save/load at all

**What is missing.** `Assets/Scripts/Domains/Economy/Businesses/Blacksmith/BlacksmithRuntime.cs`
(694 lines) contains zero save methods: no `BlacksmithRuntimeSaveDto`, no
`CaptureSaveDto`, no `LoadFromSaveDto` (verified by `git grep` over the whole
tree — zero hits for the DTO name). There is no section for it in
`Infrastructure/Persistence/SystemsSaveDtos.cs` (full read — sections exist
for the sawmill driver, log custody, construction projects, contract book,
billing, butcher, boarding house, farm, liabilities, and more, but none for
the blacksmith), no section in `Infrastructure/Persistence/Series2Pass9Save.cs`,
and no reference to `BlacksmithRuntime` in
`Infrastructure/Persistence/SaveLoadManager.cs` (2606 lines, `BuildSaveDto`
captures world, time, population, civic, store, acquisition, debt, portfolio,
guidance, businesses, systems — never the smith). No test round-trips it
(`BlacksmithTests.cs` has zero Save/Load mentions).

**What is lost on Continue.** Everything the runtime holds:
- `materialStock` (iron, coal, steel) and the FIFO `materialLotLedger` with
  lot provenance, plus `consumedLotIds`
- `assets` — every forged horseshoe (D1, individual items), every forged
  hardware piece (D2), every crafted plow/harrow/tool, every repaired item
- `repairQueue` (open repair orders), `quoteBook`, `pickupLedger`,
  `lienRegister`, `nextAssetNumber`, `repairRevenueCents`
- `forgeStationReady` / `ForgeStationDerivedFromComponents`
- the D1 quarter pools — `ironShoeQuarters`, `coalShoeQuarters`,
  `ironShoeQuarterLotId`, `coalShoeQuarterLotId` (the named gap; the comment
  at `BlacksmithRuntime.cs:79-81` says "Session-scoped like materialStock —
  a future save pass must persist these together with materialStock")

**Expected vs observed.** Brief section 18 scenario X requires save/load to
preserve partial projects and orders, and workstream L names blacksmith
repair orders and horseshoe inventory explicitly. Expected: a smith that
forged one shoe (3 iron quarters + 3 coal quarters waiting, lot ids recorded),
crafted a plow, and holds an open quoted repair order keeps all of it across
save/continue. Observed: there is no API to capture it and no save section
to hold it, so a Continue restores an empty smith — the shoes, the iron, the
open orders, and the paid-for quarter remainders are all gone.

**Reproduction.** Build a smith in a test (`new BlacksmithRuntime("smith-1",
"Test Smithy", true)`), `ReceiveMaterial` iron + coal, `ForgeHorseshoe`
once, `CraftEquipment("plow", …)`, open a repair order via
`Repairs.Intake` + `Diagnose` + `RepairQuotes.IssueOffer` +
`AcceptOffer`. There is no `CaptureSaveDto` to call. The harness has no
test that could catch this because the save surface does not exist.

**Root cause.** The foundation line's `BlacksmithRuntime.cs` never gained the
save DTO its sibling runtimes have. (The Nov 10 line's 771-line version of
the same file DOES contain `BlacksmithRuntimeSaveDto` + Capture/Load — the
two lines diverged before this campaign.) Every sibling has it:
`BakeryShopRuntimeSaveDto` (`BakeryShopRuntime.cs:1788`, persisted via
`Series2Pass9Save`), `LumberYardShopRuntimeSaveDto`
(`LumberYardShopRuntime.cs:668`, same), `SawmillProductionDriverSaveDto`
(`Logging/SawmillProductionDriver.cs:606`, via `SystemsSaveDto` + hub),
`FuelDealerRuntimeSaveDto` (via `Series2Pass9Save`). All four blacksmith
sub-authorities are already save-capable with null-safe clear-then-load:
`RepairQueue.ToSaveDto` / `LoadFromSaveDto`
(`RepairWorkOrder.cs:189` / `:201`), `RepairQuoteBook`
(`RepairQueueEconomy.cs:350` / `:357`), `RepairPickupLedger` (`:1004` /
`:1011`), `ArtisanLienRegister` (`:1268` / `:1275`). `EquipmentAsset` is
`[Serializable]`. The only missing piece is the runtime-level DTO + wiring.

**Honest nuance.** No production code currently owns a live
`BlacksmithRuntime` instance — only tests construct it
(`SimulationDrivers.cs` mentions it only in a comment at `:11690`;
`ForgeHorseshoe` / `ForgeHardware` have no production callers yet; D3 is
building the delivery driver now). So the loss is latent today and becomes
live the moment a driver lands. It still ranks first: it is the only
production runtime in the tree with zero persistence, it is explicitly in
scope (repair orders, horseshoe inventory), all of its parts are already
save-capable, and the D1/D2 production features (individual shoes, hardware)
have no durable home without it.

**Repair (implemented in this package).** `BlacksmithRuntimeSaveDto` +
`CaptureSaveDto()` + `LoadFromSaveDto()` on `BlacksmithRuntime`, covering
every field above including the quarter pools (clamped 0..3 on load, the
only values the forge can produce); a `BlacksmithRuntime` section in
`Series2Pass9SaveDto` + holder field on `Series2Pass9SaveState` +
capture/restore blocks following the bakery precedent exactly (identity
guard, clear-on-missing-section, diagnostic notes). Regression tests in
`Tests/Editor/Economy/BlacksmithSaveRoundTripTests.cs` (runtime round-trip,
idempotency, null-DTO clear, coordinator capture/restore, identity
mismatch). See "Verification" below.

## L1-2 — VERIFIED OK: construction projects, partial packages, site stockpiles

- `NpcHomeConstruction.ConstructionProjectSaveDto` + `CaptureSaveDto` /
  `LoadFromSaveDto` (`NpcHomeConstruction.cs:1190` / `:1201` / `:1303`,
  duplicate-project and duplicate-package guards on load)
- `ConstructionSiteStockpileSaveDto` (`ConstructionMaterialFreight.cs:398`
  / `:407`)
- `ConstructionContractBook` (`ConstructionContractBook.cs:425` / `:448`),
  `ConstructionProgressBillingService` (`:699` / `:743`),
  `NpcConstructionCommissioner` (`NpcCommissionedBuild.cs:1901` / `:1921`),
  `BuildingDesignCatalog` (`BuildingDesignCatalog.cs:201` / `:211`)
- Wiring: the hub captures via
  `ConstructionExecutor.CaptureSaveDto(...)`
  (`SimulationSystemsHub.cs:1793`) into
  `SystemsSaveDto.constructionProjects`; contract book and billing have
  their own sections. Partial work persists as partial (packages carry
  their own committed-labour state in the project DTO). No repair needed.

## L1-3 — VERIFIED OK: business state and ownership transitions

- `BusinessRuntimeSaveDto` (`SaveDtos.cs:1064`) — cash, retail receipts,
  COGS, revenue, payroll, reorder, liabilities, worker slots, category
  stock, retail item lots, applied endowments, named payables, manual wage
  payments, main focus, capacity. Wired via
  `sharedBusinessRuntime.CaptureSaveDtos(...)` in
  `SaveLoadManager.BuildSaveDto`.
- `AcquisitionSaveDto` (`SaveDtos.cs:1250`) — `activeDeals`
  (`AcquisitionDealSaveDto`), title chain, property tax, disputes, escrow,
  construction projects. A2's sale-terms fields were added to the real
  acquisition-deal DTO and the harness shim mirror identically.
- Business IDs are stable across ownership changes (A1/A2 preserve
  identity; `businessId` is the DTO key). No repair needed.

## L1-4 — VERIFIED OK: household inventories and balances

`PopulationSaveDto` (`SaveDtos.cs:324`) — people, households, membership
registry, animals, `householdLedgers` (HF-4, balances derive from entries),
per-household item-lot inventories (Phase B). No repair needed.

## L1-5 — VERIFIED OK: cargo, freight, and in-transit jobs

`FreightResourcePoolSaveDto` + `TransportAssetRegistrySaveDto`
(`SystemsSaveDtos.cs`); `HouseholdShoppingCustodySaveDto` + shopping trips
(same); `InTransitSaveLoadTests` and `OffMapJourneySaveSeamTests` pin the
round-trips. No repair needed.

## L1-6 — VERIFIED OK: construction loans and obligations

`FinancialObligationSaveDto`, `CreditRegistry`, `BusinessLenderServiceSaveDto`
(`SystemsSaveDtos.cs`); named payables in `BusinessRuntimeSaveDto`. Loan
proceeds are not profit and obligations are distinct from owners' personal
obligations (workstream K). No repair needed.

## L1-7 — VERIFIED OK: employee assignments, agreements, capabilities

`EmploymentRegistrySaveDto`, `CapabilityRegistrySaveDto`
(`SystemsSaveDtos.cs`); transfer agreements and recurring-order
relationships in `BusinessPortfolioSaveDto` (`SaveLoadManager.BuildSaveDto`);
`TradeCreditBook` + `CreditEventLog` saved; `SkillServiceSaveDto` covers
capabilities (aptitudes, experience, familiarities, evidence). No repair
needed.

## L1-8 — NOT APPLICABLE: glass orders

No glass import system exists yet (workstream F glass leg is unbuilt), so
there is nothing to save. Revisit when the glass route lands. No repair.

## L1-9 — RECORDED, NOT REPAIRED: ForgeRepairChain has a DTO but no wiring

`ForgeRepairChainSaveDto` + `CaptureSaveDto` / `LoadFromSaveDto`
(`ForgeRepairChain.cs:516` / `:546` / `:581`) exist, but `git grep` shows
zero references outside its own file and tests — no section in
`SystemsSaveDto` or `Series2Pass9SaveDto`, no hub registration, no
production owner. Same latent status as the blacksmith was, except its DTO
already exists, so L1-1 outranks it. Follow-up F-L1-2: wire the chain DTO
into the save path when its production driver lands.

## L1-10 — FOLLOW-UP: driver registration (F-L1-1)

When D3's delivery driver (or any production owner) creates the live
`BlacksmithRuntime`, it must hand the instance to the Pass 9 state
(`Series2Pass9SaveState.BlacksmithRuntime` field — the "Codex fills these
fields with the live model instances" holder pattern, same as
`BakeryRuntime`), or capture keeps saving an empty section. Until then the
field is null and capture records the honest "no live BlacksmithRuntime
supplied" diagnostic, exactly like the bakery's.

## Verification

Roslyn harness only (this campaign has no Unity readiness gate; NOT UNITY VERIFIED — Codex Unity EditMode run still needed for the new tests, as with A2's).

- Baseline run0 (branch head `1cba10e5`, before the repair): **5049 passed / 1 failed / 5050 total**, run exit 1. Log `/home/hatch/ll-ubc-l1-run0.log`, build `/home/hatch/ll-ubc-l1-build0.log`, FAIL set `/home/hatch/ll-ubc-l1-fails0.txt`.
- Repair run1 (this commit, csproj regenerated to 1163 Compile Includes): **5055 passed / 1 failed / 5056 total**, run exit 1. Build `/home/hatch/ll-ubc-l1-build1.log` (exit 0, zero errors), run log `/home/hatch/ll-ubc-l1-run1.log`, FAIL set `/home/hatch/ll-ubc-l1-fails1.txt`.
- FAIL-set delta: **zero new failures.** The single failure in both runs is the same test with byte-identical content — `Pass9RegistryAssemblyScanTests.AssemblyScan_EveryScannedType_IsAccountedFor`, listing the same 7 VetServices types (S4 is closing it on a separate branch; not duplicated here). The new nested DTO does not appear in the scan's unaccounted list (it has a production caller in `Series2Pass9Save.cs`).
- All 6 new tests in `BlacksmithSaveRoundTripTests` PASS:
  - `RoundTrip_PreservesStockQuartersAssetsOrdersAndRevenue` — forge 1 shoe + craft 1 plow + 1 open quoted order (266c) + 1 completed repair (266c revenue, lien, pickup) survive capture/load: iron 87, coal 47, lot balances 87/47, 3+3 quarters, shoe + plow by id, revenue 266, next asset `EQ-smith-1-002`.
  - `RoundTrip_IsIdempotent` — capture/load/capture DTOs equal; loading the same DTO twice duplicates nothing.
  - `LoadFromSaveDto_Null_ClearsToEmpty` — null DTO clears a populated runtime.
  - `Coordinator_CaptureAndRestore_RoundTripsSmith` — Pass 9 `Capture` carries the section; `Restore` restores it.
  - `Coordinator_Restore_IdentityMismatch_LeavesLiveSmithUntouched` — another smith's section never overwrites the live one (diag notes the mismatch).
  - `Coordinator_Restore_NoSection_ClearsLiveSmith` — old save with no section clears the live smith (old-save default, bakery precedent).
