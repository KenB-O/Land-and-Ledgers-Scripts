# Pass XL Phase 9 Review — full review of passes 1–8, then safe refinements

Branch: `muse/passxl-slice-phase9`, chained from `muse/passxl-slice-phase8`
at `c2300e5be0e28115274b9309f2baa35c6064219e`. No main merge, no
integration push, no force-push, no history rewrite. Roslyn harness only;
nothing here claims Unity compilation, Test Runner, PlayMode, scene,
prefab, or Inspector wiring. All money, price, wage, draw, freight, and
quantity numbers in this document and in the phase 9 tests are
caller-supplied test parameters only, never canon.

Phase 4 findings F01–F39 and phase 5 findings R01–R17 are
cross-referenced, not rewritten. New findings are P9-01 onwards.

## 1. Area-by-area review

### A. Player-as-NPC flag (phases 1, 2, 5)
Claimed and confirmed in code: three SimulationDrivers gates, OR
forwarding from the bootstrapper bool, persistence via
PassXlSliceSaveCoordinator, Continue restore without regenerating
population. No phase 6–8 file changed the flag semantics. Unity-only
`PassXlPlayerAsNpcTests` still unrun in Roslyn, as in every prior pass.
No new finding; carried as a Unity-only test item.

### B. Off-map procurement, sale, delivery, save (phases 1, 2, 6, 8)
Claimed and confirmed: market-validated orders, receipt-idempotent
arrivals, FIFO sale, atomic sell-and-deliver with rollback, delivered
marks persisted, and — verified in this review, not assumed — lot
consumptions ARE carried in `NamedSkuProcurementSaveDto.ExportState` /
`ImportState` (`OffMapNamedSkuProcurementService.cs`), so the phase 8
bakery feed survives save/restore, as phase 8's test already proved
by consumption count. No production code in phases 6–8 weakened the
General Store single-SKU guard; category stock is never read or
written by the slice. Remaining gap is not in this area: the
procurement service is still single-SKU (F06) and no production
driver calls it (P9-04).

### C. No-immigration slice gate (phases 1–4, 8)
Re-verified intact: `PopulationManager.DriveDemographicDay` maps
`AllowNewArrivals = !SuppressMigrantArrivals`, and the phase 8
composed run restores suppress = true. No phase 9 change turns
immigration, traveler arrivals, or outmigration on for the slice.
The departure side is still not mapped there (P9-10).

### D. Family trees, recruitment, settlement (phases 3, 4, 5, 9)
Phase 3 claims confirmed: idempotent tree building, off-map relatives
as real ids, sibling edges written, materialisation reusing the same
record. Phase 4/5 settlement driver claims confirmed, including the
phase 5 rollback on driver refusal. One genuine cross-phase
inconsistency was found here, and only here, by reading the pieces
together: see P9-01. Recruitment remains family-only (no friend
graph, F36); letter posting remains blocked in production by the
unregistered office (F04), with the poster now correctly refusing a
mismatched origin (phase 5 R02).

### E. Town reputation and immigration conditions (phase 3, 5)
Confirmed: caller-supplied weights, normalisation with a diagnostic,
staleness refusing gating until recomputed, and the phase 5
`EvaluateVisitAtDay` closing the traveler staleness gap. No
production reader aggregates housing, work, growth, or opinion from
live runtimes (F23); both immigration and traveler decisions still
wait on a Codex/driver reader. No new code finding; carried.

### F. Through-travelers and outmigration (phases 3, 5, 8)
Traveler stay/pass/word-of-mouth logic confirmed as written, with
the phase 5 staleness fix. Outmigration confirmed: checkable
triggers only, mandatory destination, nothing deleted, closeout
closing membership and employment only (phase 5 R06). The remainder
— household dissolution, property, debt — is still F08, and the old
paths are still live (P9-10). Person-owned horse feed is a separate,
still-open seam (P9-05), not an outmigration issue.

### G. Work, pay, draw, credit (phases 7, 8)
Phase 7 claims confirmed: real `WageSettlementService` +
`BusinessRuntimeState` payroll, no second authority, mine shift path
deliberately not used, founders as operators never wage employees,
owner draw with reserve cap and separate business/household cash.
Phase 8 additions confirmed: daily wage through the same partial
settlement path with netting against weekly payroll, day-to-day
draw as the same draw under a day name, and store-flour credit
through the existing `FinancialObligationAuthority` with
per-sale / per-customer / total limits, net days, and
block-when-past-due. Two limits are real and carried, not fixed
here: credit has no void API (P9-12), and credit has no policy
asset and no bread/fuel legs (P9-13). Daily-wage attendance lives
in the bridge, not in settlement (P9-11).

### H. Items, tiers, suppliers, quotes, freight (phases 3, 4, 8)
Tiers confirmed disjoint and complete in the catalog, including the
phase 3 sugar/coffee/tea fix. Supplier quotes confirmed: SelfCollect
(requires journey, no travel execution, F38), SellerFreight, and
FreightBusiness (surcharge only, creates no business). Freight
shipment, team reservation, exclusivity, delivery-once, and
charge-once were proved in Roslyn in phase 8; the driver tick and
the `FreightRoute` question are still open. Fuel sale confirmed
as an in-place FIFO deduction because the intake has no dispense
API; the intake DOES have a save DTO, which phase 9 now tests
(P9-03).

### I. Horses (phase 8)
Horse ownership, care idempotency by `LastCareDayIndex`, working
ration when draft-reserved, and degradation on shortfall confirmed
in code and in phase 8 tests. The production feed resolver
confirmed, by reading `SimulationDrivers.ResolveHorseFeedStock`,
to return null for every person-owned horse, every household-owned
horse, and every non-farm, non-LiveryFreight business-owned horse
(P9-05). Starting horse/wagon remains a proposal only (P9-06).

### J. Bakery and Fuel Dealer (phases 2, 4, 6, 8)
Shop-runtime bread confirmed as a real path (flour provenance,
fuel, price schedule, shelf sales) in phase 8, with the staged
chain still unused. Staged-to-sellable transfer, notions supply,
and flour unit semantics are still open (P9-07). Fuel production
confirmed in Roslyn (stand, rights, felling, haul, sawing,
byproduct transfer, sale bridge) with gates null and no dispense
API. Neither business is proved in Main Scene.

### K. Main Scene manager audit (phase 3, re-verified)
MonoBehaviour count re-verified in this review: 61 files with a
MonoBehaviour class, the same count as phases 3 and 5; no phase
6–8 file added one. D1 remains sharper after phase 8: the
controller visualises `LogisticsRuntimeManager` shipments, not
`FreightProductionDriver` company shipments (P9-14). D2 and D3
unchanged. No scene assignment is claimed from C# alone.

### L. Save, persistence, and the harness (phases 2, 5, 8)
Slice DTO, coordinator, bootstrapper forwarding, and old-save
defaults confirmed. Two persistence facts verified in this review
and recorded as non-findings: procurement consumptions are in the
slice DTO, and credit obligations are in the systems-hub DTO
(`SimulationSystemsHub.CaptureSaveDto` carries
`FinancialObligations`), so a production save that captures the
hub can carry credit — but the slice coordinator does not, so a
driver that saves slice state alone would lose credit (P9-02).
Phase 8 harness shims are documented in the phase 8 doc and are
not mistaken for proofs here, notably the
`EquipmentRequirements.ForHolder` shape-only stub that refuses
loudly; equipment gates are therefore still unproved anywhere.

## 2. New review findings (P9-01 onwards) with dispositions

- **P9-01 FIXED (docstring + tests):** Family-tree materialisation
  and settlement contradict each other when read together.
  `FamilyTreeBuilder`'s docstring said membership joining belongs
  to settlement and the builder only sets pointers, which reads as
  "materialise first, then settle". `ArrivalSettlement.Settle`
  refuses a person who is already on map, so that order strands
  the arrival. The correct composed order is a still-off-map tree
  relative straight into `ArrivalSettlementDriver.SettleAndApply`,
  which materialises internally. Docstring corrected in this
  pass; behaviour unchanged. Tests prove the composed order
  works with no duplicate person, and that materialise-first
  then settle is refused.
- **P9-02 FIXED (test gap) / Codex wiring still open:** Credit
  obligations plus procurement consumption had never been proved
  to survive a DTO round-trip together. New tests prove
  `FinancialObligationAuthority` capture/load keeps outstanding
  unchanged and procurement export/import keeps the consumption
  and lot balance, with no second lot. Production wiring is
  still Codex: the driver must save the systems hub (which
  carries obligations), not the slice DTO alone.
- **P9-03 FIXED (test gap):** Fuel sale plus intake save/restore
  had never been proved together. New test sells fuel to a
  household, captures and loads the intake DTO, and proves the
  total is unchanged and an over-sale is still refused. Zero-unit
  lots persist but never re-sell (`TotalFuelWoodUnitsFor` counts
  `Max(0, units)`), which is confirmed, not changed.
- **P9-04 CONFIRMED, Codex driver task, unchanged:** No production
  driver calls any slice piece. `SimulationDrivers` contains no
  caller of `OffMapNamedSkuProcurementService`,
  `TownReputationModel`, `ThroughTraveler`,
  `ArrivalSettlementDriver`, `SliceBusinessCredit`,
  `SliceWorkAndPay`, or `ThreeBusinessSlice`; it is a flag
  holder plus the pre-existing horse-care, household-autonomy,
  and demographic drivers. The composed runs are Roslyn
  orchestrators, not production drivers. This is the central
  pass 10 / Codex item.
- **P9-05 CONFIRMED, Kennedy decision then Codex, unchanged:**
  Person-owned horse feed resolves to null in production
  (`SimulationDrivers.ResolveHorseFeedStock` returns a stock
  only for farm businesses and for LiveryFreight businesses
  with a `livery_feed_upkeep` category stock). A person-owned
  horse therefore degrades unfed even when feed exists
  elsewhere. This is phase 8 decision D-P8-9, unchanged.
- **P9-06 PROPOSAL ONLY, Kennedy decision, unchanged:** Starting
  Kennedy with a horse and wagon is not implemented, not in
  the fixture, and not canon. `SixPersonDevelopmentFixture`
  validates exactly 6 persons and 3 households and has no
  asset, animal, wagon, feed, or capital fields, so a horse
  plus wagon would be a granted asset breaking the
  0-granted rule. It is also not viable until P9-05 is
  decided. This is phase 8 decision D-P8-11.
- **P9-07 NOT DONE, Kennedy + Codex, unchanged:** Bakery
  staged-to-sellable transfer (needs `ReceiveTransferredBreadLot`,
  a staged consumption mark, baked-day versus transfer-day
  aging, and a Main Scene owner/driver), notions procurement
  (labelled run input in phase 8, no store SKU, miller, or
  import path), and flour unit semantics (`lbPerStoreUnit`
  caller parameter, 1 in the composed run).
- **P9-08 NOT DONE, Kennedy decision, unchanged:** Multi-SKU
  batch procurement ledger (F06) and the double-ordering guard
  between `StoreProcurementChain` and the slice service (F26).
  A naive batch retry would double-order; no batch ledger
  was faked.
- **P9-09 NOT DONE, Codex driver + Kennedy, unchanged:**
  Outmigration post-departure remainder (F08): household
  dissolution, property handling, and debt handling after a
  departure are named in the record, not executed by any
  driver. Closeout covers membership and employment only.
- **P9-10 NOT CHANGED, Kennedy decision, unchanged:** Old
  migration paths still live: 4% monthly arrival roll
  (F10/F35) and distress/opportunity out-migration draw
  (F11/F12), and the slice-departure gate is still not wired:
  `PopulationManager.DriveDemographicDay` maps
  `AllowNewArrivals = !SuppressMigrantArrivals` but does not
  map `AllowDepartures`, so the phase 4/5 gate mechanism
  exists and is tested but is not activated for the slice.
- **P9-11 CONFIRMED, documented, no code change:** Daily-wage
  attendance lives in the bridge, not in settlement.
  `WageSettlementService.SettlePartialPeriod` does not check
  attendance, so a production driver that calls settlement
  directly could pay an unattended day. The driver must use
  `SliceWorkAndPay.PayDailyWage`, which refuses unattended
  days before calling settlement. This is a driver-contract
  finding, not a settlement bug to fix by changing
  settlement semantics silently.
- **P9-12 CONFIRMED integrity risk, NOT FIXED, Kennedy/Codex
  decision:** A credit sale that fails at delivery, after
  consumption and after the obligation is created, leaves an
  obligation for goods never delivered, because
  `FinancialObligationAuthority` has no void API. The bridge
  already diagnoses this as needing manual reconciliation
  and never deletes the obligation silently. Adding a void
  or write-off rule would be a behavioural change to the
  debt authority and is not made here; whether a write-off
  or forgiveness rule should exist is phase 8 decision
  D-P8-7.
- **P9-13 NOT DONE, Codex + Kennedy, unchanged:** No credit
  policy asset or business-definition field exists, so the
  policy must be supplied by the caller and must never be
  defaulted silently to credit-on. No credit legs exist for
  bakery bread or dealer fuel; the policy class is reusable
  for them, but their cash bridges have no credit leg.
- **P9-14 CONFIRMED, Codex + Kennedy, unchanged:** D1
  shipment-set unification. `LogisticsTransportPresentationController`
  visualises `LogisticsRuntimeManager.Shipments`, which has
  no reference to `FreightProductionDriver`,
  `FreightShipmentService`, or `FreightCompanyRuntime`, so
  placing the controller in Main Scene would not visualise
  driver shipments until the two shipment sets are unified
  or the controller is re-pointed. This is phase 8 decision
  D-P8-10.
- **P9-15 CARRIED, Kennedy decisions, unchanged:** Meal-count
  denominator (2/day in `DailyNeedsService` versus up to
  3/day under the default meal policy), phase 5 employment-id
  format (R05, accept before the driver ever runs in
  production), experience formation rate (F34), and
  family-only recruitment with no friend graph (F36).
- **P9-16 VERIFIED NON-FINDINGS, no action:** Procurement
  consumptions are in the slice DTO (see section B); credit
  obligations are in the systems-hub DTO (see section L);
  General Store single-SKU guard intact; slice gate intact;
  MonoBehaviour count still 61; one cash writer and one
  debt authority on spot check, no new direct cash mutation
  found in phases 6–8.

## 3. Stage 2 implementation notes

- **ADDED:** `Tests/Editor/Population/PassXlPhase9ReviewTests.cs`
  (4 tests: tree-relative composed settlement, materialise-first
  refusal, credit-plus-consumption DTO round-trip, fuel sale
  plus intake save/restore).
- **CHANGED:** `FamilyTreeBuilder.cs` docstring only (P9-01);
  no behaviour, save shape, namespace, or serialized field
  changed anywhere in this pass.
- **REFINED:** Review doc only; no production logic refactored.
- **NOT DONE, with reason, not faked:** P9-04 driver, P9-05
  feed source, P9-06 starting assets, P9-07 bakery transfer /
  notions / unit semantics, P9-08 batch ledger and
  double-ordering guard, P9-09 post-departure remainder,
  P9-10 old-path retirement and gate activation, P9-12
  void/write-off rule, P9-13 policy asset and bread/fuel
  credit legs, P9-14 shipment-set unification.

## 4. Codex handoff (Editor / scene / asset / driver tasks)

Exact names, in priority order: (1) daily tick driver in
`SimulationDrivers` / `FirstLedgerSliceBootstrapper` that calls,
on a schedule, procurement arrivals, sales, deliveries, meals,
reputation aggregation, traveler generation, `SettleAndApply`
for stays, `OutmigrationCloseout` plus F08 decisions, daily and
weekly payroll via `SliceWorkAndPay` (never settlement direct,
P9-11), and credit aging; (2) family-tree call site in
`FirstLedgerSliceBootstrapper` after
`GeneratePopulationSnapshot`, before first save, with world
seed and scenario start year (F01); (3) residence assignment
via `HousingResidence` (string unit id to int building id);
(4) postal office registration (`PostalService.RegisterOffice`,
"general-store", F04) before any letter is posted; (5)
production supplier registration
(`OffMapSupplierCatalog.RegisterSupplier`, F37) and
`SaveLoadManager.PassXlProcurementService` registration before
first save (F19), plus saving the systems hub with
`FinancialObligations` alongside any slice save (P9-02);
(6) credit policy per business (asset or business-definition
field, default policy D-P8-7, write-off rule D-P8-7);
(7) Bakery `ReceiveTransferredBreadLot` plus staged
consumption mark, baked-day aging, and Main Scene owner/driver
(P9-07); (8) notions procurement path (P9-07); (9) fuel intake
dispense API (production `Dispense` on
`FuelYardByproductIntake`, replacing in-place deduction) and
equipment gates for felling/haul/sawing; (10) person-owned
horse feed source, then a change to
`SimulationDrivers.ResolveHorseFeedStock` (P9-05), and only
then a starting horse/wagon fixture if Kennedy approves it
(P9-06); (11) D1 controller placement versus retirement plus
shipment-set unification (P9-14); (12) D2 service placement,
D3 missing-target warnings, playtest Inspector bools,
`GeneralStoreBusinessDefinition` asset tier ids, and
fixture-to-production id confirmation.

## 5. Design forks needing Kennedy's decision

Slice-departure activation (P9-10); retire old 4% arrival roll
and old out-migration draw, and unify departure paths with
destinations (P9-10); flour unit semantics and Bakery path
(P9-07); notions supply (P9-07); multi-SKU batch ledger and
double-ordering guard (P9-08); post-departure household /
property / debt handling (P9-09); credit default policy and
write-off/forgiveness rule (P9-12/P9-13); daily wage rate basis
(separate daily rate versus prorated weekly) and production
owner-draw reserve rule (`PlayerPortfolioManager.ResolveOwnerDistribution`);
freight route representation (`FreightRoute` object versus
source-to-destination with a wagon asset); person-owned horse
feed source (P9-05); starting horse and wagon, person-owned
versus business-owned, opening feed units, wagon capacity and
condition, fixture versus `OpeningBusinessRoster` (P9-06);
D1 place versus retire plus shipment-set unification (P9-14);
meal-count denominator, employment-id format, experience rate,
friend graph (P9-15); all prices, rates, weights, and
frequencies (test parameters only, never canon).

## 6. What this pass did NOT prove

No Unity compilation, Test Runner, PlayMode, scene, prefab, or
Inspector result of any kind. No Main Scene behaviour, no tick
driver running on a schedule, no journey, no residence, no
postal, no production supplier, no credit policy asset, no
Bakery staged output, no horse feed for person-owned horses,
no first-cycle self-sufficiency, and no full-regression status
beyond the Roslyn full-set result in the parent report.
