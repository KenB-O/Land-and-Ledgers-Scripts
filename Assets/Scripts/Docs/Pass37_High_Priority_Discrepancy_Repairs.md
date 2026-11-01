# Pass XXXVII — High-Priority Discrepancy Repairs

Date: 2026-10-30  
Unity: 6000.4.2f1 HDRP  
Repository: `Assets/Scripts`, branch `integration/pass12-unity-readiness`  
Base HEAD at start: `fc3ca269d73604824393487c2a1f8ea4fab06fae` (the working tree was already modified)

## Scope

This continuation made one targeted commerce-integrity correction and established fresh focused Unity regression evidence. It did not attempt to manufacture a missing supplier, historical price, stock, or settlement merely to produce a positive purchase.

## Finding and correction

`GeneralStoreRuntimeManager.BuildHouseholdTradingPort()` translated category-level Business stock into a named household item using a category-to-item mapping. The authored General Store profile has several categories that aggregate different SKUs. The old bridge could therefore expose the category's pooled quantity as one specific item, such as flour. That would make a household purchase credit a product identity that the store's stock authority could not substantiate.

The bridge now exposes an offer only if the authored profile contains exactly one SKU in that category, the legacy mapping resolves to that same SKU, and the category declares it. Ambiguous pooled categories are omitted. No inventory, price, cash, or purchase is created by the correction.

This is a containment correction at the item-identity boundary. Full category-to-lot retail inventory, the compact NPC store's live supplier registration, finite off-map supply orders, and ordinary Main Scene purchases remain unimplemented/unverified and stay OPEN under `LL-PLAY-023`.

## Validation

Unity refreshed the current Editor assemblies after the source edit. The Unity Editor Test Runner discovered 4,261 Editor test cases. Fresh GUI-driven EditMode runs completed:

| Fixture group | Passed | Failed | Skipped | Result XML |
| --- | ---: | ---: | ---: | --- |
| `GeneralStoreItemOfferIdentityTests` | 1 | 0 | 0 | `GeneralStoreItemOfferIdentityTests.xml` |
| Household commerce and needs | 76 | 0 | 0 | `HouseholdCommerceAndNeeds_EditMode.xml` |
| Rent, animal identity/availability, birthdays | 51 | 0 | 0 | `RentAnimalsBirthdays_EditMode.xml` |

The XML files and checkpoint are stored at `F:\Land & Ledgers_Pass37_Backups\20261030_high_priority_repairs`. The targeted source and test edits passed `git diff --check`. No PlayMode run or live Main Scene transaction was performed for this correction.

## Current discrepancy status

- Household need and shopping unit-rule behavior: focused EditMode coverage passes; actual Main Scene purchase remains OPEN.
- Rent and animal registry/availability rule behavior: focused EditMode coverage passes; ordinary populated-tenancy payment and supplied/undersupplied live animal tests remain OPEN.
- Birthday chronology rules: 9 tests in the selected group pass; this does not substitute for multi-day visual observation.
- Pooled General Store SKU identity: source correction and targeted regression PASS.
- Finite off-map supplier, exact lots, shipment custody, buyer/seller cash reconciliation: OPEN. No price or trade terms were inferred from incomplete historical leads.
- Main Scene 30-day full conservation, employment records, Person visual/day-night lifecycle, farming season cycle, and freight: OPEN.

## Fresh Main Scene 30-day PlayMode result

The current test tree was run in the already-open Unity 6000.4.2f1 GUI Editor against `Assets/Main Scene.unity`; no second Editor was launched. The authored First Ledger start panel was used, then the test advanced 720 authoritative simulation hours through `ScenarioValidationHarness.AdvanceHours`, allowing the normal clock callbacks and daily/weekly/monthly drivers to execute. Fresh NUnit XML is `F:\Land & Ledgers_Pass37_Backups\20261030_high_priority_repairs\MainSceneThirtyDay_Detailed_PlayMode.xml` (SHA-256 `A1A89A642AB5E09C80478673BC801B48E811310A89AD18C037A33A6D4DEFDDA1`). Result: 1 passed, 0 failed, 0 skipped, 277.31 seconds. Unity returned to Edit Mode.

Observed opening/closing runtime state:

| Measure | Opening | Closing |
| --- | ---: | ---: |
| Absolute day | 2132 | 2162 |
| Persons / unique IDs | 12 / 12 | 12 / 12 |
| Households / unique IDs | 2 / 2 | 2 / 2 |
| Unregistered Persons | 0 | 0 |
| Businesses | 3 | 3 |
| Workplace assignment hints | 9 | 9 |
| Employment registry | 0 | 0 |
| Tasks complete / open | 0 / 0 | 0 / 0 |
| Household cash | 14,400¢ | 14,400¢ |
| Business cash | 82,000¢ | 104,670¢ |
| Business category inventory | 747 units | 702 units |

The test now emits per-Business snapshots and last-week operation/settlement summaries. The General Store remained at 45,000¢ with no household sales. The Blacksmith ended at 46,738¢ and its last weekly trace recorded a 6,206¢ local-market transfer alongside 1,050¢ procurement; the Livery ended at 12,932¢ and its last-week upkeep spend was 752¢. Across the run, the operation logs describe Blacksmith output and sales, but no formal worker relationships or completed Tasks were registered. Household daily-needs diagnostics recorded repeated missed meals with no purchases, and horse A0 received 0/2 feed units from the available stock. This is a clock/driver and identity-integrity proof, not a full commerce or conservation acceptance.

### Confirmed opening-capital data mismatch

The compact roster declares 15,000¢ for the General Store, 8,000¢ for the Blacksmith and 12,000¢ for the Livery (35,000¢ combined). However, `BusinessCreationAuthority.TryCreate` only writes the intent amount into a diagnostic; it does not apply the amount to `BusinessRuntimeState`. The live opening cash exactly matches the three authored profile values: `GeneralStoreProfile.asset` 45,000¢, `BlacksmithProfile.asset` 22,000¢ and `LiveryFreightProfile.asset` 15,000¢ (82,000¢ total). Consequently the roster's stated contribution is not reflected as runtime capitalization. No adjustment was made: the current startup founder is “Town” without an authoritative Town cash ledger, so debiting or replacing capital would invent a funding policy and risk changing valid profile opening balances. This must be resolved at the authoritative funding/source-of-funds layer before calling opening money reconciled.

### Market-sale boundary

`ResolveArchetypeRoutedLocalMarketSales` derives captured units from a modeled market-capture ratio and credits proceeds with `AddWeeklyLocalTransferRevenue`. Unlike the separate explicit Business-to-Business transfer routines, this route has no named buyer, buyer cash debit, or settled household transaction in this test. Its “local market” and “off-map or competitors” language therefore does not establish a reconciled counterparty. The 6,206¢ shown in the final Blacksmith weekly trace is a recorded modeled market transfer, but the full Main Scene cash boundary remains unreconciled. No production change was made because removing this route or inventing a regional market account would change economy policy beyond the evidence here.

### Remaining high-priority live-path gaps

- `LL-PLAY-020` stays OPEN: the run generated repeated household food shortfalls and zero purchases despite the journey-network wiring; no funded supplier/order/settlement path was observed.
- `LL-PLAY-021` stays OPEN for live integration: focused rule tests pass, but this Main Scene run had no occupied rent settlement and one horse with no feed. No free supply or payment was inserted.
- `LL-PLAY-027` stays OPEN: nine workplace assignment hints did not yield an EmploymentRegistry relationship or Task in the 30-day snapshot.
- RUN-004 remains PARTIAL: 30 consecutive days and identity/baseline counts were verified, but not full monetary counterparties, goods custody, real employment, or household purchase reconciliation.
- First Ledger's active date remains an unresolved scenario/calendar mismatch noted in earlier backlog evidence; this test deliberately does not reinterpret its absolute day index.

## Changed files

- `Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs`
- `Tests/Editor/Economy/GeneralStoreItemOfferIdentityTests.cs` and its Unity `.meta`
- `Docs/Pass6_LivingSettlement_Playtest_Backlog.md`
- `Docs/Pass37_High_Priority_Discrepancy_Repairs.md`

