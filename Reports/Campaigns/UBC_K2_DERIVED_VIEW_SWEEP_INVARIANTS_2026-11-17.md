# UBC K2 - Derived-View Removal Sweep and Invariant Regression Coverage

Date 2026-11-17. Lane K2, workstream K finance and derived-view replacement.
Base integration branch muse/universal-business-construction-2026-11-17 at
e29484e7 (K1 merged). Integration harness at the base 5158 passed, 0 failed.
K2 baseline in worktree ll-ubc-k2 re-verified at the same commit, 5170 passed,
0 failed, 5170 total (log ll-ubc-k2-run0.log) — the 12 K1 tests are in.

## Scope

Two parts. (1) Sweep the whole tree for any remaining reference to the
seven derived compatibility views and for any other derived view that
survived K1; replace the last two the same way K1 replaced the first five
where a direct-iteration replacement reproduces them exactly. (2) Add
harness-compiled regression tests for as much of the three K invariants
as CAN be pinned in the harness, and record exactly what stays Unity-only.

No production behaviour changes. No file outside the sweep and the
invariant tests is touched. ProductionDesignPresets.cs and
NpcConstructionDesignsDef011Tests.cs untouched.

## Part 1 - sweep table

Every remaining reference to the seven view names was found by grep over
the worktree at e29484e7. Disposition for each class of hit:

| Reference | Where | Disposition |
|---|---|---|
| `OpeningInventory`, `BusinessCashTargets`, `HouseholdCashTargets`, `OpeningEquipment`, `OpeningEmployees` in production code | DevelopmentTownConfiguration.cs (K1's own "deleted X view" comments), FirstLedgerSliceBootstrapper.cs (K1 comments + kept `HouseholdCashTarget` DTO type + `EnumerateHouseholdCashTargets` helper), GeneralStoreRuntimeManager.cs (K1 comment + kept `OpeningInventoryItem` type + kept `TryResolveOpeningInventory` method) | KEEP. Accurate comments documenting the replacement; kept nested DTO types and kept method names. No live view read. |
| Same five names in tests | Test-method names (`OpeningEquipment_AppliesOvenAndKit_FromConfig`, `DevelopmentTownOpeningInventory_IsPerBusinessFiniteAndIdempotent`), OpeningEconomyDef013Tests.cs:20,29 (sets the audit file's OWN `BusinessCashTargets` / `HouseholdCashTargets` dictionaries — the name collision, see below), UBC_K1 test-file docstring, DevelopmentTownConfigurationTests.cs:116 (K1 comment) | KEEP. Method names and the audit DTO, not view reads. |
| `OpeningEconomyAudit.cs` `AuthoredOpening.BusinessCashTargets` / `HouseholdCashTargets` | Domains/Economy/Core/OpeningEconomyAudit.cs:21,24 (fields), :66,:79 (reads); FirstLedgerSliceBootstrapper.cs:3710,3719 (fills the audit dictionaries) | NAME COLLISION, not a defect. Confirmed again by E-for-K1 and by this lane: `authored` is the file's own nested `AuthoredOpening` type. K2 adds a clarifying comment on the `AuthoredOpening` docstring saying the fields are local to the audit file and never read the deleted views, so the next reader does not repeat the confusion. No behaviour change. |
| `OpeningSpareBlades` | DevelopmentTownConfiguration.cs:329 (the view, now DELETED), ScenarioMillOpeningSpareBladeEndowment.cs:71 (consumer), DevelopmentTownConfiguration.cs:611 (Validate loop), SawmillBladeLifecycleC5Tests.cs:476-477 (test read) | DELETED and replaced (see below). |
| `WholesaleAccounts` | DevelopmentTownConfiguration.cs:331 (the view, now DELETED), ScenarioOpeningWholesaleAccounts.cs:70 (consumer) | DELETED and replaced (see below). |
| `BusinessSpareBladeDefinition.businessKey`, `BakeryWholesaleAccountDefinition.sellerBusinessKey` (NonSerialized stamp fields) | DevelopmentTownConfiguration.cs:227,276; DevelopmentTownConfigurationMigration.cs:449 (editor migration reads the serialized seller key from the OLD asset — a migration reader, not a view consumer) | KEPT on the types for serialization stability, with a K2 comment on each field: nothing populates it from config any more; tests may still set it on hand-built entries. The migration reader is untouched. |
| Single-value readers `OpeningLoggingCampProvisionCoverDays`, `OpeningBakeryFuelUnits`, `OpeningBakeryFlourPounds`, `OpeningBakeryNotionsUnits`, `OpeningCarpentryLumberUnits`, `FuelDealerIntakeUnitsPerBakeryUnit`, `OpeningHorseId`, `OpeningWagonId` | DevelopmentTownConfiguration.cs | NOT derived views. They already read the owning business's opening record directly through `FindBusinessByOperation` / `FindBusiness`. Left as they are. |

## Part 1 - the two sibling replacements

Both are exact reproductions of the K1 pattern. Neither changes
behaviour; both remove a shared-state mutation (the view stamped the
owning key onto the record on every read).

### OpeningSpareBlades

- The view projected `business.opening.spareBlades` and stamped
  `x.businessKey = b.key`. It was already marked
  `[Obsolete("Read spare blades from each owning BusinessDefinition.")]`.
- `ScenarioMillOpeningSpareBladeEndowment.TryApply` already resolved
  the owning business through `TryGetBusiness` at entry. It now
  iterates `business.opening?.spareBlades` and the `entry.businessKey`
  filter is dropped. All error messages use the `businessKey`
  parameter, as before.
- `Validate` iterates `Businesses` and each business's
  `opening?.spareBlades`, keeping the blank-id and uniqueness checks
  and the error text. The business is configured by construction, so
  the "must reference a configured business" check is the loop itself.
- `SawmillBladeLifecycleC5Tests.cs:476-477` read the view. The two
  assertions now collect the blades by direct iteration over
  `valid.Businesses` and assert the same facts (1 blade, attributed
  to the mill key). The hand-built `SpareEntry()` still sets
  `businessKey = MillKey`; the installer ignores it now, as it ignores
  the hand-set `sellerBusinessKey` in the wholesale fixtures.

### WholesaleAccounts

- The view projected `business.opening.wholesaleAgreements` and
  stamped `x.sellerBusinessKey = b.key`. It was NOT marked obsolete;
  it is now deleted.
- `ScenarioOpeningWholesaleAccounts.TryApply` now keeps the
  `TryGetBusiness` result instead of discarding it (`out _` became
  `out sellerBusiness`) and reads this seller's entries from
  `sellerBusiness.opening.wholesaleAgreements`.
- The `hasAnyEntry` flag is preserved EXACTLY, including its
  config-wide quirk: the old code set it for any non-null candidate
  in the flattened view, i.e. for an entry belonging to ANY seller,
  so the demand-derived fallback books only when the config lists no
  wholesale accounts at all. The replacement scans every business's
  opening record for any non-null entry. A seller with no entries of
  its own while another seller has entries gets no fallback and no
  booking, exactly as before. This is the documented rule (the
  applicator's docstring: "a config that lists no wholesale accounts
  at all books the single demand-derived fallback account"), not a
  bug, and it is now pinned by
  `WholesaleAccounts_OtherSellerEntry_BlocksFallback` in
  BakeryWholesaleAccountConfigTests.cs.
- No `Validate` loop exists for wholesale accounts; none was added.

## Part 2 - invariant coverage table

| Invariant | Pinned in the harness (new tests) | Stays Unity-only (exact test) |
|---|---|---|
| 1. Acquisition payment conservation | `UBC_K2_InvariantRegressionTests.SaleTerms_Conservation_PreCloseCashSplitsIntoIncludedPlusWithdrawal`: pre-close 35,400c splits into included 5,400c + withdrawal 30,000c; proceeds are price 250,000c + withdrawal = 280,000c; the whole deal balances (buyer payment + pre-close cash = seller proceeds + retained cash); the figures survive the DTO round-trip. `SaleTerms_NegativeInputs_ClampToZeroProceeds`: negative price / withdrawal / included cash clamp to 0, so proceeds can never go negative and destroy cash. | The runtime credit step. `AcquisitionMarketManager.CreditSellerProceeds` lives in `Domains/Economy/Acquisitions/AcquisitionMarketManager.cs`, which the harness does NOT compile (the Acquisitions directory has no sources.txt entry; only `Presentation/AcquisitionDealReadModel.cs` is listed). The task brief's suggestion to use it in the harness is wrong; this lane verified it from the generated csproj (0 includes). Pin it in Unity EditMode with `UBC_A1_AcquisitionContinuityTests.DirectPurchaseCreditsSellerAndRecordsTerms`, `SellerProceedsFallBackToNamedPayableWhenNoHousehold`, and `SellerProceedsFailLoudWhenNoHouseholdAndNoAuthority`. The terms-level pins above cover the arithmetic; the credit step covers the ledger movement. |
| 2. Debts distinct from personal obligations | `Debts_BusinessDebt_NotCountedAsOwnerPersonalDebt`: business loan 50,000c (debtor `general-store-1`, the raw instance id, as `BusinessLiabilityLedger` does) and personal loan 12,000c (debtor `household:3`, as `NpcPropertyLife` does) — each `TotalOutstandingFor` returns only its own; the creditor's receivable is the 62,000c sum; `Reconcile()` reports no error. `Debts_PayingBusinessDebt_LeavesPersonalDebtUntouched`: paying the business loan in full leaves the personal 12,000c outstanding. `Debts_AssumptionWithoutNovation_KeepsOriginalDebtor`: an assumption without novation keeps the business as debtor and adds the buyer only to `LiableParties`; the buyer's outstanding stays 0. `Debts_AssumptionWithNovation_MovesDebtorExplicitly`: a novation without creditor consent is refused (debtor unchanged); with consent the debtor moves to the buyer and `OriginalDebtorReleased` is set. `Debts_SaveLoad_PreservesSeparation`: the authority DTO round-trip preserves both totals. | Nothing. The whole authority (`FinancialObligationAuthority.cs`) is harness-compiled, including `CaptureSaveDto` / `LoadFromSaveDto`. The production callers that create business vs personal debts (liability ledger, property life, SliceBusinessCredit) are the wiring, not the invariant; their behaviour is the authority's separation, which is pinned here. |
| 3. Property title non-transfer | `Title_BusinessOwnerChange_LeavesTitleWithSeller`: registering plot-7 under the seller, then moving the business record's `ownerPersonKey` from person-1 to person-2, leaves `CurrentHolder("plot-7")` with the seller. `Title_ExplicitTransfer_MovesTitleWithInstrument`: `PlayerTitleBridge.RecordPlayerAcquisition` with instrument `deal-1` moves the holder to `Player`. `Title_PurchaseWithoutInstrument_Refused`: a `Purchase`-basis transfer with a blank instrument is refused and the holder is unchanged. `Title_SaveLoad_PreservesHolder`: the title DTO round-trip preserves the holder. | The market-close wiring. The runtime business transfer (`SharedBusinessRuntimeManager.TryTransferBusinessToPlayer`) contains zero title calls (verified by grep: its only "title" hit is the `RecordPlayerAcquisitionTerms` method name). Title moves only through `AcquisitionMarketManager.RecordTitleAcquisition`, the single choke point, called from the four close paths (land deal, business deal, land purchase, business purchase) with the plot, the listing id as instrument, and the final price. Unity EditMode should confirm a business purchase close records the premises plot title and a bare runtime transfer does not — there is no existing EditMode test for that split; the harness pins the two halves separately (owner change alone, explicit transfer). |

## Title clarification (read this before quoting the invariant)

The task brief phrases invariant 3 as "a business sale does not
transfer property title". That is too broad as written, and this lane
does not claim it. What the code actually does:

- `CompleteBusinessAcquisition` (AcquisitionMarketManager.cs:9023)
  and `TryPurchaseBusiness` (:9497) BOTH mark the business's premises
  plot player-owned AND call `RecordTitleAcquisition(plot, ...)` with
  the listing id as the conveyance instrument and the final price.
  A market purchase of a business therefore transfers the premises
  plot title — explicitly, with instrument, price, and day recorded
  in the append-only chain, as part of the same close that moves the
  plot's `playerOwned` flag and records the land-appreciation
  purchase. The listing is for the business AND its site.
- What never happens is title following business ownership BY ITSELF:
  the runtime business transfer moves only the business identity and
  owner and contains zero title calls, and no code path transfers a
  plot title without going through the explicit choke point.

The precise invariant, as pinned: business ownership and plot title
are separate records changed by separate operations; a business
transfer alone never moves title; title moves only through an
explicit transfer with a stated basis and (for a purchase) a
conveyance instrument. If Kennedy wants business purchases NOT to
carry the premises title, that is an owner decision about the
listing's terms, not a defect this lane found — recorded here, not
decided.

## Verification

- Baseline in this worktree before any change, 5170 passed, 0 failed,
  5170 total, log ll-ubc-k2-run0.log (1180 Compile Includes, build
  exit 0, run exit 0).
- Post-change run1, 5181 passed, 2 failed, 5183 total. Both failures
  were test bugs in the new tests, not production defects: (a) the
  wholesale quirk fixture omitted the freight business's horse and
  wagon, so `Validate` refused the config; fixed by giving the
  fixture bakery the same opening horse/wagon as the existing
  fixture. (b) The novation test expected the business outstanding
  to reach 0 after moving one loan, forgetting the second loan
  created for the no-consent refusal check; fixed by asserting the
  remaining 8,000c explicitly.
- Post-fix run2, 5183 passed, 0 failed, 5183 total, log
  ll-ubc-k2-run2.log (build exit 0, run exit 0). Delta vs baseline:
  +13 tests (12 in the new K2 file, 1 wholesale quirk test), zero
  new failures. FAIL-set delta is empty (both runs have 0 FAIL
  lines). All 12 new K2 tests pass; the wholesale quirk test passes;
  the updated C5 assertions pass.
- Final sweep grep: the deleted view names appear only in K2's own
  accurate comments, the reflection test's name list, and the K1
  report. No live reference remains. The reflection test
  `DerivedViews_AllSeven_AbsentFromConfiguration` pins all seven
  names against re-addition.
- Harness coverage note: every production file this lane edited is
  harness-compiled (DevelopmentTownConfiguration.cs,
  ScenarioMillOpeningSpareBladeEndowment.cs,
  ScenarioOpeningWholesaleAccounts.cs, OpeningEconomyAudit.cs). No
  harness-excluded file was touched, so no expanded build was needed.
  The new test file's .meta GUID d44e0dc2dbaf4803a5f6f2e7282f46b4
  occurs in exactly one .meta file across the worktree.
- Harness note: this lane copied the shared harness config into
  ll-ubc-k2-harness and repointed HARNESS at its own directory
  (symlinked Shims/Runner from the shared dir) so its own sources.txt
  copy insulates it from concurrent shared edits; the shared
  sources.txt at copy time carries only the D3 Blacksmith line, which
  this tree has. The lane's gen script reads its own sources.txt.

## Unity follow-ups owed to Codex

- The acquisition proceeds credit step: run
  `UBC_A1_AcquisitionContinuityTests` in Unity EditMode, in
  particular `DirectPurchaseCreditsSellerAndRecordsTerms`,
  `SellerProceedsFallBackToNamedPayableWhenNoHousehold`, and
  `SellerProceedsFailLoudWhenNoHouseholdAndNoAuthority` (all
  harness-excluded; they exercise `CreditSellerProceeds` in the
  excluded `AcquisitionMarketManager.cs`).
- The market-close title split: confirm in a Unity run that a
  business purchase close records the premises plot title through
  `RecordTitleAcquisition` while a bare runtime transfer
  (`TryTransferBusinessToPlayer`) does not move title. No existing
  EditMode test covers that split; the harness pins the two halves
  separately.
- The spare-blade and wholesale installers' bootstrapper call sites
  are harness-excluded (as in K1); confirm in a Unity run that New
  Game still seeds spare blades and wholesale accounts identically.
- No Unity scene, prefab, or asset changes in this slice.

## Risks and edge cases

- The `businessKey` / `sellerBusinessKey` NonSerialized fields stay
  on the DTO types. Nothing populates them from config after the
  views go. Any future consumer that reads them from a
  config-sourced entry would see null; it must resolve the owner
  through `TryGetBusiness` instead (same standing note as K1's for
  the equipment/employees fields).
- The wholesale `hasAnyEntry` rule is config-wide by design (see the
  quirk test). If Kennedy ever wants the fallback to be per-seller,
  that is a behaviour change to the applicator's documented rule,
  not a sweep fix.
- The title clarification above is a scope statement, not a repair:
  if the owner wants business purchases to exclude the premises
  title, the listing and close code must change, with a new owner
  decision.
