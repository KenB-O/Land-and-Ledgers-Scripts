# UBC C1 — Multi-Capability Audit and Repair (2026-11-17)

- **Branch:** `muse/ubc-c1-multi-capability-2026-11-17`
- **Base:** `muse/universal-business-construction-2026-11-17` at `247b3865` ("F2 report: soften the overbroad roofing sentence (E's NIT)")
- **Worktree:** `/home/hatch/ll-ubc-c1` (sparse: Assets/Scripts, Reports/Campaigns, Assets/Core/Population)
- **Harness:** `/home/hatch/ll-ubc-c1-harness/` (1198 includes after this lane; own REPO/HARNESS lines)
- **Scope:** Workstream C — retail as a configurable capability; one business, multiple compatible activities.

## 1. Verdict in one paragraph

All five compatible-activity pairs are genuinely wired to the one canonical
business entity — none of them runs on a competing parallel runtime. The
breaks are in the capability LAYER underneath them: the designed "Add
Operation" path is dead with no actor assigned to invoke it, the capability
registry is empty in production, and player formation silently discarded
every capability choice. The top break (no actor for Add Operation) needs an
owner decision, so this lane repaired the next one: **player-formed
businesses now carry the capabilities they were formed with**, at both the
formation layer (intent carries ids, `TryCreate` attaches them, the form
passes them through) and the template layer (applying a setup template
records the installed vertical). Seven new regression tests pass; the full
harness is green at 5268/0.

## 2. Files changed

| File | + / - | What |
|---|---|---|
| `Assets/Scripts/Domains/Economy/Creation/BusinessCreation.cs` | +37 / -1 | `CreateBusinessIntent` gains an optional `capabilityIds` constructor parameter; `BusinessCreationAuthority.TryCreate` attaches intent ids to the created instance (Step 5b) |
| `Assets/Scripts/Domains/Economy/Creation/CreateBusinessFormModel.cs` | +6 / -1 | `ToIntent()` passes the form's `capabilityIds` through to the intent instead of dropping them |
| `Assets/Scripts/Domains/Economy/Generic/GenericBusinessEconomy.cs` | +16 / -0 | `GenericBusinessSetupTemplateCatalog.TryApply` records the installed vertical on the business via `AddCapability(template.TemplateId)` for Bakery / Smithy / Carpentry |
| `Assets/Scripts/Tests/Editor/Economy/C1TemplateCapabilityTests.cs` (new, 145 lines) | +145 / -0 | 7 regression tests pinning the template half of the repair |
| `Assets/Scripts/Tests/Editor/Economy/C1TemplateCapabilityTests.cs.meta` (new) | — | Fresh GUID `ad40f81ee5f244faa668ef8eb0a56c76` |
| `Reports/Campaigns/UBC_C1_MULTI_CAPABILITY_2026-11-17.md` (this report) | — | Audit findings, repair, verification |

No changes to `ProductionDesignPresets.cs` or
`NpcConstructionDesignsDef011Tests.cs`. No change to the 45c simple-hardware
convention, the D2 requirements (9/18/18), the D3/D4/D5/F1 drivers, any money
pins, or the B1 8-week close rule / B2 lease terms (1500c/month, 1500c
deposit, 12-month term). No supplier invented, no free inventory, no instant
delivery. No Main Scene, prefab, or asset changes. No new asmdefs, event
buses, or DI frameworks.

## 3. Harness verification

- **Before (run0):** 5261 passed / 0 failed / 5261 total. Log: `/home/hatch/ll-ubc-c1-run0.log`. FAIL set: `/home/hatch/ll-ubc-c1-fail0.txt` (empty).
- **After (run1):** 5268 passed / 0 failed / 5268 total. Log: `/home/hatch/ll-ubc-c1-run1.log`. FAIL set: `/home/hatch/ll-ubc-c1-fail1.txt` (empty).
- **FAIL-set delta:** identical (both empty). +7 tests, exactly the new `C1TemplateCapabilityTests` (all 7 PASS in run1; strict `grep -c '^PASS '` = 5268, matching the runner summary).
- **Build (build1):** exit 0, 0 errors, 97 warnings (pre-existing). Log: `/home/hatch/ll-ubc-c1-build1.log`.
- **Invocation discipline:** cd inside the worktree root; `PATH="$HOME/dotnet:$PATH" DOTNET_ROOT="$HOME/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 LAND_LEDGERS_REPO=<worktree>/Assets/Scripts`; build into isolated `/home/hatch/ll-ubc-c1-bin` / `/home/hatch/ll-ubc-c1-obj/`; apphost run with the cleaned `env -i` environment including `DOTNET_ROOT`; stdout redirected to `/tmp` and copied to `/home/hatch` afterward (EDQUOT workaround).

### 3a. Expanded build for the harness-excluded formation half

`BusinessCreation.cs` and `CreateBusinessFormModel.cs` are harness-excluded
(0 includes), so the formation half was verified by an expanded build:
harness csproj plus those two files. Log:
`/home/hatch/ll-ubc-c1-expanded-build.log`.

- Result: 2 errors, both **pre-existing** and unrelated to this lane:
  `CS0246: The type or namespace name 'OperatingLedgerSaveDto' could not be
  found` at `BusinessCreation.cs:704` and `:730`, in `CaptureSaveDto` — a
  method this lane did not touch. The type is defined only in
  `Infrastructure/Persistence/SystemsSaveDtos.cs` (harness-excluded), and
  that file cannot be added to the expanded build because 10 of its types
  duplicate the harness shim (`Shims/PersistenceDtos.cs`: `DraftPowerServiceSaveDto`,
  `EmploymentRegistrySaveDto`, `EquipmentCapacityServiceSaveDto`,
  `EquipmentReservationAuthoritySaveDto`, `FreightResourcePoolSaveDto`,
  `SkillServiceSaveDto`, `TaskAuthoritySaveDto`, `TransportAssetRegistrySaveDto`,
  `ValuationReadModelSaveDto`, `WorkTimeBudgetSaveDto`).
- **Zero errors in the edited regions** (intent constructor, `TryCreate`
  Step 5b, `ToIntent`), so the formation half type-checks in the expanded
  context. It is NOT covered by a runnable harness test: the test files that
  exercise `BusinessCreationAuthority` (`BusinessCreationTests.cs`,
  `GenericBusinessEconomyTests.cs`) are themselves harness-excluded. Unity
  Test Runner confirmation is a Codex checklist item (Section 6).

### 3b. Harness-copy fix (process note)

This lane's copied `gen-csproj.sh` still carried the template `REPO=` /
`HARNESS=` paths (`workspace/land-and-ledgers/repo`,
`harness-overnight`). The first regeneration overwrote the correct
1197-include `harness.csproj` with a wrong 1001-include one pointing at the
overnight repo; the script was repointed to this lane and the project
regenerated correctly (1198 includes, new test file present, paths verified
before building). Future lanes copying the harness must verify `REPO=` /
`HARNESS=` before regenerating. The run0 baseline (5261/0) was taken before
any regeneration and matches the integration baseline at `247b3865`, so it
stands.

## 4. Audit: the five pairs — all wired to the one entity

| Pair | Verdict | Evidence |
|---|---|---|
| General Store retail + hardware | WIRED | Opening stock includes `hand_tool` 2–4 (`Assets/Core/Population/DevelopmentTownConfiguration.asset`); `tools_hardware` category reorders (`GeneralStoreRuntimeManager.cs:4426-4431`); household retail path proven by I1. Hardware lives in the yard for construction supply, in the store for retail — one store entity, one cash ledger, no parallel state. |
| Boarding house + meals | WIRED | `DriveBoardingHouseDays` / `DriveBoardingHouseWeeks` (`SimulationDrivers.cs:7052` / `:7113`), B1. |
| Bakery + retail | WIRED | `BakeryShopRuntime.SellBread` via `Series2BakerySupplierAdapter.cs:260` and `ThreeBusinessSlice.cs:559`; wholesale driver; `ConfigureBakery` installs the bread method + retail line; `DriveGenericProduction` (`SimulationDrivers.cs:7652`) executes it. |
| Carpentry + furniture sales | WIRED | `DriveCarpentryWorkshop` step (d) → `DriveCarpentryFurnitureSales` (`SimulationDrivers.cs:7257`) → `CarpentryFurnitureSale.TrySellFinishedPieces` with the workshop's own cash/register. |
| Sawmill + wholesale lumber | WIRED | `SawmillToYardTransferDriver`; yard pays mill via business cash ports; G1 proved conservation. |

Supporting wiring confirmed along the way: generic retail lines are
transacted (`RetailServiceDesk.cs:203-205` / `:483`,
`ManagerDecisionExecution.cs:847` / `:1038` / `:1079`); `DriveGenericProduction`
runs every business except the carpentry workshop (G-1 exclusion — its
processes run through the furniture binding, `SimulationDrivers.cs:7652`);
template application is live in production via the player panel
(`BusinessCreationPanelController.cs:318`) and the bootstrapper for Don's
workshop (`FirstLedgerSliceBootstrapper.cs:1802`).

## 5. Audit findings

### F-C1-1 — TOP BREAK, needs an owner decision (not repaired)

**Expected:** a formed business can gain a second compatible capability
after formation through the designed Add Operation path (GHOST-DES-033),
gated by the capability registry.
**Observed:** the mechanism exists and is tested, but has no actor.
`BusinessExpansion.TryAddOperation` (`Domains/Economy/Creation/BusinessCapabilities.cs`)
has **zero production callers**. The hub constructs an empty registry and
never populates it (`SimulationSystemsHub.cs:1711`); the only two
`RegisterAll` implementations — `StoreButchery.RegisterAll`
(`Domains/Economy/Businesses/GeneralStore/StoreButchery.cs`) and
`GrainChain.RegisterAll` (`Domains/Economy/Farming/Crops/GrainChain.cs`) —
have **zero production callers**. `HasCapability` has zero production
callers outside `BusinessInstanceState.cs` / `BusinessCapabilities.cs`, so
nothing gates on capabilities at execution.
**Why it needs the owner:** wiring a caller means choosing the actor — a
player "Add Operation" UI action, an NPC autonomous expansion policy, or a
weekly driver. That is a design fork, and the NPC side is the same gap
(B1's F-B1-2 pivot has no expansion mechanism either). Populating the
registry alone would change no behavior today (registration never grants
permission, by the code's own contract), so this lane did not do a cosmetic
population. **Owner decision needed:** who invokes Add Operation, and under
what policy.

### F-C1-2 — REPAIRED: player formation drops capabilities (this lane)

**Expected:** a business formed with capabilities carries them, for NPC and
player alike (brief P0: NPC/player capability parity).
**Observed:** `CreateBusinessIntent.capabilityIds`
(`Domains/Economy/Creation/BusinessCreation.cs:359`) was a dead field —
private, no setter, not a constructor parameter — so no intent could ever
carry a capability. `BusinessCreationAuthority.TryCreate` never attached
intent capabilities to the created instance. `CreateBusinessFormModel.ToIntent()`
(`Domains/Economy/Creation/CreateBusinessFormModel.cs:374`) used the form's
`AddCapability` list only to merge premises requirements, then dropped it —
the player's explicit capability choices were silently discarded.
`GenericBusinessSetupTemplateCatalog.TryApply`
(`Domains/Economy/Generic/GenericBusinessEconomy.cs:714`) installed
methods/policies/retail lines but left `CapabilityIds` empty. Only the NPC
adapter attached capabilities, manually, after creation
(`NpcBusinessCreationAdapter.cs:139`). Every player-formed business had an
empty capability list; every NPC-formed business carried its activities.
**Repair (two halves, one finding):**
- *Formation half* (`BusinessCreation.cs`, `CreateBusinessFormModel.cs`):
  the intent constructor takes an optional `IEnumerable<string>
  capabilityIds` (deduped, blanks ignored; all four existing call sites
  unaffected); `TryCreate` attaches intent ids to the instance at Step 5b
  via `AddCapability` (dedup-safe); `ToIntent()` passes the form's ids
  through. The NPC adapter is untouched and unaffected — it builds its
  intent without ids and still attaches manually afterward (a no-op dedup
  either way).
- *Template half* (`GenericBusinessEconomy.cs`): `TryApply` records the
  installed vertical via `business.AddCapability(template.TemplateId)` for
  Bakery (`bakery_retail_setup`), Smithy (`prairie_fork_smithy_setup`) and
  Carpentry (`carpentry_setup`), using the template catalog's own ids as
  the capability ids. Blank records nothing (no activity installed);
  General Merchandise records nothing (a disabled-catalog plan, not an
  installed operation). Reapplying stays idempotent.
- Recording grants no operation: feasibility is still decided at execution
  from real equipment, workspace, premises right, people, materials and
  authority. No money, inventory, equipment, or workers are created.
- **Tests:** 7 new tests in `C1TemplateCapabilityTests.cs` pin the template
  half (each vertical records exactly one id; Blank / General Merchandise /
  unknown record none; reapply does not duplicate; two verticals accumulate
  to two ids on the one identity). The formation half has no runnable
  harness test (Section 3a); it is verified by expanded build + Codex
  checklist.

### F-C1-3 — Capabilities are labels only; nothing gates on them

`HasCapability` has zero production callers. No runtime refuses an operation
for lacking a capability, and no runtime enables one by having it. The
specialized runtimes run on their own logic (equipment, stock, cash, task
authority). This is the designed contract ("capability registration never
grants permission to act"), but it means the capability list is currently a
record, not a gate. Any future lane that starts gating on `HasCapability`
must first canonicalize the id vocabulary (F-C1-8) and populate the registry
(F-C1-1). No owner decision needed now; recorded as a constraint on future
work.

### F-C1-4 — Two parallel generic-business models; one is test-only

`GenericBusinessCapabilitySet`, `GenericBusinessFormation`,
`GenericBusinessTemplates.Apply` and `AcquisitionInterfaceModel`
(`Domains/Economy/GenericBusiness/`) have zero production callers. The
whole "reconstruction" model (capability set + feasibility checker + Unity
adapter the file headers say "Unity AI maps...") is test-only. The live
generic path is `GenericBusinessSetupTemplateCatalog.TryApply` +
`GenericBusinessVerticalService` + `DriveGenericProduction` +
`RetailServiceDesk`. Per the repo `AGENTS.md` rule ("prefer one coherent
system over overlapping half-systems"), a future lane should reconcile or
retire the reconstruction model rather than extend it. No owner decision
needed to record this; retiring it is a cleanup decision for a later lane.

### F-C1-5 — Store butchery counter never driven (no in-town counterparty)

`StoreButchery.BuyWholesaleCuts`
(`Domains/Economy/Businesses/GeneralStore/StoreButchery.cs`) has zero
production callers: the designed butcher-to-store wholesale-cut purchase
(buy cuts, retail at markup through the store's `meat` category) never
executes. The butcher runtime exists in the hub and is aged daily
(`SimulationDrivers.cs:5521`, cull flow feeds it), but there is **no butcher
business in the dev town** (no butcher in
`DevelopmentTownConfiguration.asset`). The store's meat is supplied through
its normal named-supplier weekly reorder instead, so this is a dead designed
link, not a starving category. Wire it only when a butcher business exists;
no owner decision needed now.

### F-C1-6 — Grain capabilities never registered (no in-town counterparty)

`GrainCapabilities.RegisterAll` (`Domains/Economy/Farming/Crops/GrainChain.cs`)
is never called, and `GrainChain` has no production callers at all. Grain
dealing / storage / milling / feed-retail exist only as registry data. There
is no grain dealer or mill in the dev town; Millbrook Grain Company operates
off-map in Ashford. Same disposition as F-C1-5: wire only when a
counterparty exists. No owner decision needed now.

### F-C1-7 — Recorded capabilities do not survive save/load (generic path)

`BusinessInstanceState` carries `capabilityIds` in memory only — no save or
load of the list exists in that file. Only `NpcBusinessLifecycle` persists
them (its DTO, for NPC-formed businesses). The capabilities this lane
records (template verticals, formation ids) are therefore runtime-only and
lost on save/load. Fixing it needs a save-DTO change (`SystemsSaveDtos.cs`
is harness-excluded); left as a follow-up, not silently fixed here.

### F-C1-8 — Capability id vocabulary is loose and uncanonicalized

Three vocabularies coexist with no canonical list: NPC activities use
coarse ids (`"retail"`, `NpcEntrepreneurshipDriver.cs:393`); the registry
uses task-flavored ids (`"meat-retail"`, `"slaughter"`,
`"grain-dealing"`, `"grain-storage"`, `"milling"`, `"feed-retail"`); this
lane records template ids (`"bakery_retail_setup"`,
`"prairie_fork_smithy_setup"`, `"carpentry_setup"`). This lane reused the
live path's own vocabulary rather than inventing a fourth. Before any
runtime gates on ids (F-C1-3), a lane must canonicalize the vocabulary.
No owner decision needed to record this.

## 6. Provisional calibrations

**None.** This lane set no prices, quantities, rates, or terms. The repair
records existing ids only; no economic value was introduced or changed.

## 7. Codex Unity checklist (harness-excluded items)

- [ ] **Formation half (Unity compile + Test Runner):** `BusinessCreation.cs`
  and `CreateBusinessFormModel.cs` are harness-excluded. In Unity, form a
  player business through the Start Business presenter path with
  capabilities chosen in the form and confirm the formed business's
  `CapabilityIds` carry them; confirm the existing
  `BusinessCreationTests` / `GenericBusinessEconomyTests` (excluded from
  the Roslyn harness) still pass in the Test Runner. Also confirm the NPC
  formation path is unchanged (adapter still attaches after creation).
- [ ] **Panel path:** `BusinessCreationPanelController.cs:318` →
  `SharedBusinessRuntimeManager.TryCreatePlayerBusiness` builds its own
  intent without ids; capabilities on that path come from `TryApply`
  (Half B, harness-verified). In Unity, form a Generic business with the
  Bakery template via the panel and confirm `HasCapability("bakery_retail_setup")`.
- [ ] **Bootstrapper:** Don's workshop template application
  (`FirstLedgerSliceBootstrapper.cs:1802`) now also records
  `"carpentry_setup"` — confirm the workshop's capability list in a Unity
  run, and that furniture production/sales are unchanged.
- [ ] **Save/load (expected current behavior, not a regression):** confirm
  recorded capabilities do NOT yet persist across save/load (F-C1-7).
- [ ] **Do NOT test:** `TryAddOperation` callers and registry population —
  no caller exists; that is F-C1-1 and needs the owner decision first.
- [ ] No scene, prefab, or asset changes in this lane; nothing to verify
  there.

## 8. Follow-ups

- **F-C1-1 (owner decision):** who invokes Add Operation — player UI
  action, NPC autonomous expansion policy, or weekly driver — and under
  what policy. Blocks wiring `TryAddOperation` and populating the
  registry. Related: B1's F-B1-2 NPC pivot.
- **F-C1-7:** persist `CapabilityIds` on `BusinessInstanceState` save/load
  (save-DTO change; `SystemsSaveDtos.cs` harness-excluded).
- **F-C1-8:** canonicalize the capability-id vocabulary before any runtime
  gates on ids.
- **F-C1-4:** reconcile or retire the test-only GenericBusiness
  reconstruction model vs the live setup-template model.
- **F-C1-5 / F-C1-6:** butcher and grain capability links have no in-town
  counterparty; wire only when a butcher / grain business exists.
- **Optional cleanup:** the NPC adapter could pass ids through the new
  intent parameter instead of attaching after `TryCreate`
  (`NpcBusinessCreationAdapter.cs:117-145`); behavior-identical either way,
  dedup-safe, so left untouched.
- **K2 title question (not this lane):** still open with the owner — whether
  a business purchase should carry the premises plot title at all.
