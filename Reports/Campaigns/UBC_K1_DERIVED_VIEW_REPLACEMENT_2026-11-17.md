# UBC K1 - Derived View Replacement Audit

Date 2026-11-17. Lane K1, workstream K finance and derived-view replacement.
Base integration branch muse/universal-business-construction-2026-11-17 at
ecf64a2a (M1 merged). Integration harness run7 5060 passed, 0 failed.
K1 baseline in worktree ll-ubc-k1 re-verified at the same commit, 5060/0/5060
(log ll-ubc-k1-run1.log).

## Scope

Replace the five derived compatibility views on DevelopmentTownConfiguration
with direct owner-record iteration where safe. Schema-2 opening allocations
are the single authority. No duplicate cash targets are recreated. The nested
DTO types stay. Only the view properties go.

The five views, all in
Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs, all marked
Obsolete:

- OpeningInventory, line 317. Projects Businesses with a non-empty
  opening.inventory into new BusinessOpeningInventory objects. The items
  array is the SAME reference, not a copy.
- BusinessCashTargets, line 322. One entry per business, targetCashCents
  equals b.openingCashCents.
- HouseholdCashTargets, line 325. Authored branch, anchor is the first
  member person key, target is openingCashCents. Generated branch
  (Households.Count equals 0), returns Settings.generatedHouseholdOpeningCash
  unchanged.
- OpeningEquipment, line 336. Returns b.opening.equipment objects and
  MUTATES shared state as a side effect, setting
  b.opening.equipment.businessKey to b.key.
- OpeningEmployees, line 340. Flattens opening.employees and MUTATES each
  entry, setting e.businessKey to b.key.

Sibling views OpeningSpareBlades (line 343) and WholesaleAccounts (line 345)
use the same pattern and are NOT in scope. They stay, as do the nested DTO
types BusinessCashTarget, HouseholdCashTarget, BusinessOpeningInventory,
BusinessOpeningEquipment, BusinessEmployeeDefinition. HouseholdCashTarget
must stay regardless, because GeneralSettings.generatedHouseholdOpeningCash
is a serialized HouseholdCashTarget array.

## Consumer map

Every consumer was found by grep over the worktree at ecf64a2a. No other
consumer exists.

### OpeningInventory

- DevelopmentTownConfiguration.TryResolveOpeningInventory, line 395.
  Iterates the view, matches entry.businessKey case-insensitively, then
  scans entry.items for the item id and applies the deterministic
  FNV-1a hash to pick a quantity in the min-max range. Needs only the
  owning business record and its inventory array.
- ScenarioOpeningStockEndowment.TryApply in
  Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs,
  line 6422. Iterates the view, matches candidate.businessKey to the
  requested key, then reads candidate.items and re-resolves each range
  through TryResolveOpeningInventory. Harness-EXCLUDED:
  GeneralStoreRuntimeManager.cs is not in the harness sources (its own
  dependencies, TownWorldController, PopulationManager, HUD controllers,
  GeneralStoreTradingPort, GeneralStoreSaveDto, are not in the harness
  set). The TryApply replacement was verified by a scratch build that
  adds only this file to the harness set: 42 CS0246 errors, all
  pre-existing missing types elsewhere in the file, zero errors in the
  TryApply region (lines 6405-6502), zero errors naming the changed
  identifiers. Full Unity confirmation of stock seeding is still owed
  to Codex (see Unity follow-ups).
- FirstLedgerSliceBootstrapper.SeedDevelopmentBusinessOpeningInventory,
  Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs line 1579.
  Iterates the view, resolves each entry.businessKey through
  TryGetBusiness, then calls ScenarioOpeningStockEndowment.TryApply with
  entry.businessKey. Harness-excluded (0 includes), so this change is
  mechanical and needs Codex Unity EditMode follow-up.
- DevelopmentTownConfiguration.Validate, inventory loop. Checks each
  entry references a configured business, has a unique business key, and
  holds valid non-duplicate item ranges.

### BusinessCashTargets

- FirstLedgerSliceBootstrapper.ApplyDevelopmentTownOpeningCash, lines
  3331, 3353, 3387. Three loops. Builds a businesses-by-key map from the
  view, prechecks each target against the live runtime balance, then
  applies the top-up. Harness-excluded.
- FirstLedgerSliceBootstrapper.RunOpeningEconomyAudit, lines 3465, 3477.
  Two loops. Builds the same businesses-by-key map, then fills the audit
  authored dictionary from the view. Harness-excluded.
- DevelopmentTownConfiguration.Validate, generated branch and authored
  branch. Checks each target references a configured business, is
  non-negative, and has a unique key. The checks are redundant against
  the business loops that run just before them, but the error text is
  kept.
- OpeningEconomyAudit.cs uses its OWN Dictionary fields named
  BusinessCashTargets and HouseholdCashTargets, not the config views.
  Unaffected.

### HouseholdCashTargets

- FirstLedgerSliceBootstrapper.ApplyDevelopmentTownOpeningCash, lines
  3368, 3400. Two loops. Prechecks each household target against the
  shared household ledger balance, then applies the top-up.
  Harness-excluded.
- FirstLedgerSliceBootstrapper.RunOpeningEconomyAudit, line 3486. One
  loop. Fills the audit authored dictionary from the view.
  Harness-excluded.
- DevelopmentTownConfiguration.Validate, generated branch and authored
  branch. Generated branch checks each stored generated target resolves
  to a generated person id inside maximumStartingResidents, is
  non-negative, and has a unique anchor. Authored branch checks each
  anchor resolves to a configured person, is non-negative, and is unique.
- Tests/Editor/Population/DevelopmentTownConfigurationTests.cs lines
  116-117. Harness-excluded Unity-only test. Asserts generated configs
  expose targets all equal to 25000 through the fallback branch. Must be
  updated to read Settings.generatedHouseholdOpeningCash directly.

### OpeningEquipment

- ScenarioOpeningEquipmentEndowment.TryApply,
  Domains/Economy/Equipment/ScenarioOpeningEquipmentEndowment.cs line 62.
  Iterates the view, takes the first candidate whose businessKey matches
  the requested key case-insensitively. Reads only ovens, kitIds, and
  assetEntries from the entry. All error messages use the businessKey
  parameter, never the entry field. Harness-compiled.
- ScenarioMillOpeningEquipmentEndowment.TryApply,
  Domains/Economy/Equipment/ScenarioMillOpeningEquipmentEndowment.cs
  line 65. Same lookup shape, reads only assetEntries. Harness-compiled.
- ScenarioCarpentryOpeningEndowment.TryApply,
  Domains/Economy/Equipment/ScenarioCarpentryOpeningEndowment.cs line 75.
  Same lookup shape, reads assetEntries and kitIds. Harness-compiled.
- DevelopmentTownConfiguration.Validate, equipment loop. Checks each
  entry references a configured business, kit ids resolve in
  ToolKitCatalog, oven component counts equal the BakeOven definition,
  and asset entries are valid, unique, and never the slice fixture id.
- Test fixture builders in BakeryOpeningEquipmentTests.cs line 79,
  MillOpeningEquipmentEndowmentTests.cs line 70,
  CarpentryWorkshopDriverG1Tests.cs line 88,
  CarpentryWorkshopF1Part2Tests.cs line 92, and
  OpeningEmploymentEndowmentTests.cs line 82 read entry.businessKey, but
  only on HAND-BUILT entries whose businessKey the test set itself
  (BakeryEquipmentEntry, MillEquipmentEntry, MillHandEntry). They do not
  read the config view. The BuildConfig match loops in those tests keep
  working unchanged.

### OpeningEmployees

- ScenarioOpeningEmploymentEndowment.TryApply,
  Domains/Economy/Equipment/ScenarioOpeningEmploymentEndowment.cs line 87.
  Iterates the view, filters by entry.businessKey equals the requested
  key, reads personKey, role, weeklyWageCents. All error messages use the
  businessKey parameter. The installer already resolves the owning
  business through TryGetBusiness at entry. Harness-compiled.
- DevelopmentTownConfiguration.Validate, employees loop. Checks each
  entry references a configured business and person, has a non-blank
  role and a positive wage, is not the business owner, and holds no other
  opening job.

### Confirmed non-consumers

- ScenarioMillOpeningSpareBladeEndowment.cs line 71 uses
  OpeningSpareBlades, out of scope, untouched.
- ScenarioOpeningWholesaleAccounts.cs line 70 uses WholesaleAccounts,
  out of scope, untouched.
- BakeryMarginLiveValuesTests.cs, BakeryWholesaleDailyDriverTests.cs,
  GeneralStoreItemOfferIdentityTests.cs, LogHaulWagonTeamsterTests.cs,
  and OpeningEconomyDef013Tests.cs match the view names only through
  type names, method names such as TryResolveOpeningInventory, or
  comments. They do not read the views.
- SawmillBladeLifecycleC5Tests.cs lines 476-477 reads the
  OpeningSpareBlades view, out of scope, untouched.

## Headline findings

1. All five views are replaceable. No view is load-bearing in a way
   direct iteration cannot reproduce exactly.
2. The cash views have NO harness-compiled consumer. Their only
   production consumers are the harness-excluded bootstrapper methods,
   plus Validate. The cash replacement is mechanical and must be flagged
   for Codex Unity EditMode follow-up, not claimed as harness-verified.
   The same holds for the stock endowment: GeneralStoreRuntimeManager.cs
   is not in the harness sources, so its TryApply replacement is verified
   by the scratch type-check described in the consumer map, while the
   identical lookup path in TryResolveOpeningInventory (harness-compiled)
   is pinned by golden-value tests.
3. The equipment and employees views carry a mutation side effect
   (businessKey stamped onto the shared record). No production consumer
   relies on the stamped field. All three equipment installers and the
   employment installer match on the field only to FIND the entry, and
   all of their messages use the businessKey parameter they were handed.
   Tests read the field only on entries they built themselves.
4. The generated-household fallback is the one place a view does real
   branching. HouseholdCashTargets returns the stored
   generatedHouseholdOpeningCash array when no households are authored.
   The replacement must keep that branch in Validate and in the
   bootstrapper household loops. The stored array itself is the owner
   record there, so iterating it directly is still direct iteration.
5. Business keys are unique by Validate, so first-match lookup and
   direct TryGetBusiness lookup return the same entry. Matching stays
   case-insensitive through TryGetBusiness, which trims and compares
   OrdinalIgnoreCase, same as the view loops.
6. OpeningInventory shares its items array reference with the business
   record. Direct iteration reads the same array. No copy semantics
   change, and no consumer writes through it.

## Replacement design

Delete the five Obsolete properties last, after every consumer is moved.

- TryResolveOpeningInventory. Resolve the business with
  TryGetBusiness, then scan business.opening.inventory with the same
  null guards and the same hash. The hash, the key string, and the
  min-max clamp are untouched.
- ScenarioOpeningStockEndowment.TryApply. Resolve the business with
  TryGetBusiness and read business.opening.inventory directly. Null or
  empty inventory keeps the same no-stock report.
- The three equipment installers. Resolve the business with
  TryGetBusiness and read business.opening.equipment, keeping the null
  guard that produces the same no-equipment report. The installers
  already call TryGetBusiness at entry; the result is now kept instead
  of discarded.
- ScenarioOpeningEmploymentEndowment.TryApply. Iterate
  business.opening.employees with the same null guard. The installer
  already holds the owning business.
- Validate. Generated branch, iterate Businesses directly for the cash
  checks and iterate Settings.generatedHouseholdOpeningCash directly
  for the household checks. Authored branch, iterate Businesses for
  cash, Households for household cash (anchor is memberPersonKeys[0],
  and households with no members are already refused earlier in the
  method), Businesses with non-empty opening.inventory for inventory,
  Businesses with a non-null opening.equipment for equipment, and
  Businesses opening.employees for employees. Error texts are unchanged
  except that entry.businessKey becomes business.key, which names the
  same business.
- Bootstrapper cash methods. Iterate configuration.Businesses directly
  for the business loops. For the household loops, iterate
  configuration.Households directly (anchor memberPersonKeys[0],
  target openingCashCents), falling back to
  configuration.Settings.generatedHouseholdOpeningCash when no
  households are authored. The fallback is collected in one private
  helper in the bootstrapper so the three household loop sites share
  it. This file is harness-excluded, so the change is mechanical.
- Bootstrapper SeedDevelopmentBusinessOpeningInventory. Iterate
  configuration.Businesses with a non-empty opening.inventory and use
  business.key.
- DevelopmentTownConfigurationTests.cs lines 116-117. Read
  generated.Settings.generatedHouseholdOpeningCash directly. Unity-only
  follow-up for Codex.

## Invariants

- Acquisition payment conservation. The replacement creates, moves, or
  destroys no cash. Opening cash targets are read from the same
  openingCashCents fields, once per business, in the same order.
  No acquisition, escrow, or payment code is touched by this slice.
- Debts distinct from owners personal obligations. No debt, loan, or
  obligation code is touched. The household cash target still anchors
  to the shared household ledger through the first member, exactly as
  before.
- Property title not passing automatically with business ownership. No
  title, deed, or premises code is touched.
- No loan, credit, or pricing behaviour changes. No file in those areas
  is in the change set.

Coverage note. None of the three invariants has a harness-compiled
regression test in the current baseline. The acquisition continuity
tests (UBC_A1_AcquisitionContinuityTests.cs, AcquisitionVpFlowTests.cs)
are harness-excluded, and no harness-compiled test pins title
non-transfer or debt separation by name. The invariants are therefore
verified in this slice by construction (the diff touches no code in
those areas) plus the full harness pass, not by a new invariant test.
Writing an acquisition-close test here would require pulling the
harness-excluded acquisition runtime into the harness, which is out of
scope for a view replacement. Flagged as a gap for the campaign, not
papered over.

## Regression tests

New file Tests/Editor/Economy/UBC_K1_DerivedViewReplacementTests.cs,
harness-compiled through the Tests/Editor/Economy directory entry:

- Validate refuses a business with negative openingCashCents, a
  household with a negative target, an inventory range with max below
  min, equipment naming an unknown kit, and an employee with a
  zero wage.
- TryResolveOpeningInventory returns the same seeded quantities as the
  old lookup path. Golden values pinned from the pre-change code at
  seed 9217, config id k1-derived-view-test, business general-store.
  flour 10-20 gives 19, salt 5-5 gives 5, bread 0-3 gives 0,
  nails_hardware_lot 2-6 gives 6. Seed 0 flour gives 12.
- Direct cash enumeration yields each authored business exactly once
  with its authored openingCashCents, and the generated-household
  fallback returns the stored array unchanged.
- Stock, equipment, and employment endowments stay idempotent through
  the direct path. Equipment and employment apply-twice behaviour is
  pinned by the existing installer tests (Bakery, Mill, Carpentry,
  Employment), which run against the replaced code. Stock apply-twice
  behaviour has NO harness-compiled test: the existing
  GeneralStoreItemOfferIdentityTests
  .DevelopmentTownOpeningInventory_IsPerBusinessFiniteAndIdempotent
  test is harness-excluded, and ScenarioOpeningStockEndowment lives in
  the harness-excluded GeneralStoreRuntimeManager.cs, so a
  harness-compiled stock test cannot reference it. The stock lookup
  itself is pinned by the TryResolve golden tests above (same
  TryGetBusiness plus inventory-array path), and the TryApply
  replacement type-checked clean in the scratch build. Stock
  apply-twice confirmation is a Unity follow-up.

## Risks and edge cases

- A business record whose opening is null. The views guarded with
  b?.opening. Direct loops keep the same guards.
- A business record whose opening.equipment is null. The view omitted
  it and installers reported no equipment. Direct reads keep the null
  check and the same report.
- Duplicate business keys. Refused by Validate before any consumer
  runs. First-match and direct lookup agree while keys are unique.
- The businessKey field on BusinessOpeningEquipment and
  BusinessEmployeeDefinition stays on the types (NonSerialized, set by
  tests on hand-built entries). Nothing populates it from production
  config after the views go. Any future consumer that reads it from a
  config-sourced entry would see null. The audit found no such
  consumer. If one appears, it must resolve the owner through
  TryGetBusiness instead.
- The bootstrapper and the Unity-only config test cannot be verified
  in the Roslyn harness. Their changes are mechanical and listed as
  Codex Unity EditMode follow-ups below.

## Verification

- Baseline in this worktree before any change, 5060 passed, 0 failed,
  5060 total, log ll-ubc-k1-run1.log.
- Pre-change run with the 12 new K1 tests added, 5072 passed, 0
  failed, 5072 total, log ll-ubc-k1-run2.log. The TryResolve golden
  values were pinned against the old lookup path here.
- Post-change run, 5072 passed, 0 failed, 5072 total, log
  ll-ubc-k1-run3.log. Final run after the test-file cleanup, 5072
  passed, 0 failed, 5072 total, log ll-ubc-k1-run4.log. Zero new
  failures. FAIL-set delta against the integration run7 FAIL set is
  empty (both sets are empty).
- Scratch build adding only GeneralStoreRuntimeManager.cs to the
  harness set, log ll-ubc-k1-scratch-build.log: 42 CS0246 errors, all
  pre-existing missing types elsewhere in the file (GeneralStoreTradingPort,
  GeneralStoreSaveDto, TownWorldController, PopulationManager, HUD
  controllers), zero errors in the TryApply region (lines 6405-6502),
  zero errors naming the changed identifiers. The TryApply replacement
  type-checks.

## Unity follow-ups owed to Codex

- FirstLedgerSliceBootstrapper cash and inventory seeding changes are
  harness-excluded. Needs Unity EditMode confirmation that NewGame
  opening cash, household cash, and store stock still seed identically.
- ScenarioOpeningStockEndowment.TryApply in
  GeneralStoreRuntimeManager.cs is harness-excluded (scratch
  type-checked only). Needs Unity confirmation that store stock seeds
  identically, including the apply-twice idempotence covered Unity-only
  by GeneralStoreItemOfferIdentityTests
  .DevelopmentTownOpeningInventory_IsPerBusinessFiniteAndIdempotent.
- DevelopmentTownConfigurationTests.cs lines 116-117 now reads the
  stored generated array directly. Needs a Unity Test Runner pass.
- No Unity scene, prefab, or asset changes in this slice.

## Harness note

This session could not copy the shared harness or read-then-write
other agents file bytes through the shell (EPERM on write). The
workaround is a hardcoded generator, gen-k1.sh in ll-ubc-k1-harness,
transcribed from the shared sources.txt. Its output was verified
byte-equivalent as a set against the shared harness.csproj, 1164
includes, identical sorted-set hash
aa0e39f7c0fd07e92b616fe1e4cfc6fd6cd8f3fa0e0a2fedde8475d4fdb1ac5c.
The shared generator fails the same way in this session because it
reads sources.txt. Future K1 runs should use gen-k1.sh and re-check
the count and hash after any sources.txt change.
