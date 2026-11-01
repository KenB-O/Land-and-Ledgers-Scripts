# Pass XL Phase 10 - Verification and Prep for Codex

Base: `muse/passxl-slice-phase9` at `796906512c1189037c14f892e68e2c2b687f13ed`.
This branch: `muse/passxl-slice-phase10` (single commit on top of phase 9).
Main (`7b1ca29cb13e570c04870c75a7a62f5ddc625f1d`) and
`integration/pass12-unity-readiness`
(`2bd9cf1f010dfae8591252523cf304cfb6e649e9`) are untouched.
No merge to main is authorized by this pass.

Scope of phase 10: verification and prep only. No new features, no new
production logic, no new business, wage, draw, credit, freight, or horse
behaviour, no fixture change, no gate activation, no retirement of old
migration paths, no Bakery receive API, no fuel dispense API, no
feed-source decision. Anything in those categories is listed below as a
Codex task or a Kennedy decision, not done here.

Read this doc together with the earlier phase docs, which stay the
detailed record and are all present in this branch:

- `Docs/PassXlPhase4Findings.md` (findings F01-F39)
- `Docs/PassXlPhase5Review.md` (findings R01-R17)
- `Docs/PassXlPhase6SliceRun.md` (composed six-person flour-to-meals run)
- `Docs/PassXlPhase7WorkAndPay.md` (wages, owner draw)
- `Docs/PassXlPhase8ThreeBusiness.md` (three-business economy)
- `Docs/PassXlPhase9Review.md` (findings P9-01 to P9-16, Codex handoff,
  design forks)
- `Docs/PassXlMainSceneManagerAudit.md` (phase 3 Main Scene manager audit,
  discrepancies D1/D2/D3)
- `Docs/PassXlThroughTravelerImmigration.md`

## 1. What passes 1-10 got done, area by area

All of this is proved in C# only, through the Roslyn harness (see
section 4). Nothing here claims Unity compilation, Unity Test Runner,
PlayMode, scene wiring, prefab wiring, or Inspector values.

### Off-map procurement, sale, and delivery

The General Store can order a named item (flour) from a finite off-map
supplier, with a goods cost plus a seller-paid freight surcharge, transit
days, and a single lot created on arrival. Duplicate receipt creates no
second lot. Sale is FIFO from the store lot and delivery to a household
inventory is atomic (cash settles once, stock moves once). The General
Store single-SKU guard is intact: the store stocks flour only through
this path. Save and restore of orders, lots, sales, and deliveries was
proved without duplication.

### The six-person slice and the no-immigration gate

The slice is six people in three households: Kennedy Baldwin-Ooms
(General Store), Glen Pierce (Bakery), and Jacob Boniecki (Fuel Dealer),
with Emma, Cassidy, and Grace staying home. The
`SuppressMigrantArrivals` / `AllowNewArrivals` gate still suppresses
arrivals for the slice, and no pass turns immigration, traveler
arrivals, or outmigration on for the slice. A composed run takes
off-map flour to the store lot to the Baldwin-Ooms household inventory
to real meals, surviving save and restore. A player-as-NPC playtest
flag exists (default off).

### Work, pay, and owner draw

Employee wages run through the real `WageSettlementService` and
`BusinessRuntimeState` payroll path. There is no second payroll
authority. Founders are operators, never wage employees. Owner income
is a draw, not a wage: business cash falls by exactly the draw and
household cash rises by exactly the draw, capped by a caller-supplied
reserve that stays in the business. Daily wage and day-to-day draw
were both proved, along with refusals (no attendance means no pay,
insufficient cash means a liability plus suspension rather than
created cash, prorated partial pay happens once, unknown or
wrong-business or dead or off-map workers are refused).

### Three-business economy

Fuel Dealer felling, sawing, and selling fuel; Bakery fed by store
flour through single-custody consumption, baking real loaves through
`BakeryShopRuntime`, and selling to all three households; all three
households buying and eating, with cash reconciling. Store-flour
credit works through the existing `FinancialObligationAuthority`
only (offer yes or no, per-sale, per-customer, and total limits, net
days, block when past due, cash-only still valid, never created cash).
Freight shipment, wagon team, delivery once, and charge once were
proved. Horses are owned, fed, cared for, idempotent, and hitched for
freight, except for the person-owned feed seam below.

### Family trees, recruitment, settlement, reputation, travelers, outmigration

Family trees are opt-in and built from known family info only.
Recruitment is family-only. Arrival settlement is composed and rolls
person flags back if the driver refuses. Employment ids use the format
`arrival-<person>-<business>-<day>`. Outmigration closeout closes
membership and employment (household, property, and debt handling
after departure is still open). Town reputation is a read model with
caller-supplied inputs. Through-traveler visits respect reputation
staleness. None of this is switched on for the slice.

### Economic integrity guards held through all ten passes

One cash writer (`HouseholdLedger`), one debt authority
(`FinancialObligationAuthority`), no synthetic inventory, no fake
persons or households, no fake job completion, no duplicate task
authority, no free cash. Fixed-ratio weekly output for Bakery and
Fuel Dealer was NOT restored. Capital contributions must be real
transactions, not free cash, but the amount is a playtest variable
(see section 3).

## 2. What is still Codex-only

These need the Unity project, a scene owner, a driver, or an asset.
None of them can be closed from this Scripts-only repo, and none of
them is claimed as done here. Work through them in this order.

1. Unity compilation of the whole tree, then Unity Test Runner. The
   Roslyn set is not a substitute. Test Runner items to run include
   `Tests/Editor/Orchestration/PassXlPlayerAsNpcTests.cs`,
   `Tests/Editor/Population/SixPersonNoImmigrationTests.cs`, the
   six-person fixture and 30-day tests, and diagnosis of the
   `Tests/Editor/Economy/ConstructionInputSupportTests.cs` timeout.
   PlayMode verification after that.
2. Daily and weekly tick driver in `SimulationDrivers` /
   `FirstLedgerSliceBootstrapper`, calling on a schedule: procurement
   arrivals, sales, deliveries, meals, reputation aggregation,
   traveler generation, `SettleAndApply` for stays,
   `OutmigrationCloseout`, daily and weekly payroll through
   `SliceWorkAndPay` (never settlement direct), and credit aging.
3. Main Scene manager audit discrepancies from
   `Docs/PassXlMainSceneManagerAudit.md` and phase 4 F13/F14/F15:
   D1, place `LogisticsTransportPresentationController` on a Main
   Scene GameObject with its `LogisticsRuntimeManager` and wagon
   prefab references, or retire it on Kennedy's word, plus
   shipment-set unification between `LogisticsRuntimeManager` and
   `FreightProductionDriver` (phase 9 P9-14). D2, place
   `LLFeedbackService` on a Main Scene GameObject with clips
   configured, or keep PlayMode self-creation deliberately. D3, log
   a warning naming the missing component type and property when a
   `FindUnique` bootstrapper target is absent in
   `Editor/PersistentRuntimeHierarchyAuthoring.cs`.
4. `FamilyTreeWiring` call site in `FirstLedgerSliceBootstrapper`,
   after `GeneratePopulationSnapshot` and before first save, with
   world seed and scenario start year (phase 4 F01, phase 9
   section 4 item 2).
5. Residence assignment through `HousingResidence` (string unit id
   to int building id).
6. Postal office registration: `PostalService.RegisterOffice`
   for the General Store office (`"general-store"`, phase 4 F04)
   in Main Scene bootstrap before any letter is posted, so
   `RecruitmentLetterPoster` letters are not refused as unknown
   origin office.
7. `SaveLoadManager.PassXlProcurementService` registration: set
   the production `OffMapNamedSkuProcurementService` instance in
   Main Scene bootstrap before first save (phase 4 F19), and save
   the systems hub with `FinancialObligations` alongside any slice
   save (phase 9 P9-02). Without this, procurement persists empty
   and restores empty.
8. Production supplier registration in
   `OffMapSupplierCatalog` (phase 4 F37). Real supplier ids,
   distances, offered SKUs, and goods prices only, from a
   Kennedy-approved source.
9. `ItemDefinitions`: add the `GeneralStoreItemTiers` tier ids to
   the `GeneralStoreBusinessDefinition` asset in the Unity project
   (phase 4 F07). The asset itself lives outside this repo.
10. Bakery scene owner plus staged transfer: `ReceiveTransferredBreadLot`
    plus the transferred-id set, staged consumption mark, baked-day
    versus transfer-day aging decision, and the Main Scene owner and
    driver (phase 4 F16, phase 9 P9-07). Until this exists, the slice
    bakery is the shop runtime, not the staged chain.
11. Notions supply path: where bakery notions come from (store SKU,
    miller path, import path, or another source, phase 9 P9-07).
12. Fuel dispense API: a production `Dispense` on
    `FuelYardByproductIntake` to replace the bridge's in-place FIFO
    deduction, plus real equipment gates for felling, hauling, and
    sawing.
13. Person-owned horse feed source, then a change to
    `SimulationDrivers.ResolveHorseFeedStock` (phase 9 P9-05).
    Reproduced, not fixed: under the production resolver a
    person-owned horse returns null for feed and degrades unfed
    even when feed exists elsewhere. Do not work around this by
    inventing a feed source; Kennedy decides the source first
    (section 3).
14. Credit policy asset: a per-business credit policy (asset or
    business-definition field) with the default policy and a
    write-off rule (phase 8 D-P8-7, phase 9 P9-12/P9-13). No
    write-off or forgiveness rule exists in code today.
15. Traveler generator: a driver that creates
    `ThroughTravelerVisit` records and calls
    `ThroughTravelerImmigration.EvaluateVisit` (phase 4 F24).
16. Reputation reader: production aggregation of housing vacant
    and total units, open and filled job slots, growth, and
    good-opinion scores into `TownReputationInputs` (phase 4 F23).
17. SelfCollect travel: real player or NPC travel execution for a
    procurement pickup (phase 4 F38). The quote bridge refuses
    SelfCollect quotes today; a SelfCollect order must never
    teleport goods.
18. Fixture-to-production id confirmation: confirm the slice
    fixture person and household ids match production ids before
    claiming any run is the Main Scene slice, and confirm the
    playtest Inspector bools for the slice gate and the
    player-as-NPC flag.

## 3. What is still Kennedy's decision

Nothing in this section is decided by this pass, by Codex, or by a
test parameter. Where Codex needs one of these, flag it and wait
rather than guessing.

- Starting conditions as test variables. Technical Bible XIV
  section 3.1 says business creation includes Asset, Inventory, and
  WorkingCapital assignment but specifies no amount. Seed cash,
  starting stock, and starting assets are caller-supplied test
  inputs to vary between runs, never fixed canon. The dropped
  "zero starting capital" rule must not be reintroduced: the
  development roster `OpeningBusinessRoster.SixPersonDevelopment`
  (0 values, never selected by ordinary scenarios) is one test
  roster, not a rule. Phase 10 corrected stale comments and
  docstrings in the phase 6-8 run files that presented 0 as if it
  were a rule; it changed no production behaviour, save shape, or
  fixture value to do it.
- Starting horse and wagon as a test variant only: approve or
  reject, person-owned versus business-owned, opening feed units,
  wagon capacity and condition, and whether the fixture or
  `OpeningBusinessRoster` carries starting assets (phase 8 D-P8-11,
  phase 9 P9-06). Not viable for a person-owned horse until the
  feed source above is decided.
- Slice-departure gate activation (phase 4 F09).
- Retiring the old 4 percent arrival roll and the old outmigration
  draw (phase 4 F10/F35).
- Unifying departure paths with destinations (phase 4 F11/F12).
- Double-ordering guard between procurement authorities
  (phase 4 F26).
- Friend graph versus family-only recruitment (phase 4 F36).
- Experience formation rate (phase 4 F34).
- Meal-count denominator: 2 per day in `DailyNeedsService`
  versus up to 3 per day under the default meal policy.
- Employment-id format: the current `arrival-<person>-<business>-<day>`
  format (phase 5 R05) before the driver ever runs in production.
- Wage, draw, credit, freight, and horse parameters: daily wage
  rate basis (separate daily rate versus prorated weekly, phase 8
  D-P8-5); production owner-draw reserve rule
  (`PlayerPortfolioManager.ResolveOwnerDistribution` versus the
  bridge caller-supplied reserve, phase 8 D-P8-6); default credit
  policy and write-off rule (phase 8 D-P8-7); freight route
  representation (`FreightRoute` object versus
  source-to-destination with a wagon asset, phase 8 D-P8-8);
  person-owned horse feed source (phase 8 D-P8-9).
- Flour unit semantics and the Bakery path (phase 8 D-P8-1/D-P8-2),
  notions supply (phase 8 D-P8-3), multi-SKU batch ledger
  (phase 4 F06), post-departure household, property, and debt
  handling (phase 4 F08, phase 9 P9-09).
- All prices, rates, weights, and frequencies. Every price, rate,
  weight, and frequency in the phase 6-8 runs is a test parameter
  only, never canon. That includes the phase 1 freight pattern
  (250c surcharge on 1000c goods), supplier distances and prices,
  and traveler frequencies.
- D1 place versus retire, plus shipment-set unification
  (phase 8 D-P8-10, phase 9 P9-14): Codex does the work, Kennedy
  makes the call.

## 4. Phase 10 verification record

- Branch ancestry: phase 10 is one commit on top of phase 9
  (`7969065`), which sits on phase 8 (`c2300e5`), phase 7
  (`835548e`), phase 6 (`401d3c7`), phase 5 (`ad3cd19`), phase 4
  (`63b1c25`), phase 3 (`2591ba2`), phase 2 (`e7da22`), phase 1
  (`ccc296a`), and the Pass XL checkpoint (`2bd9cf1`). Main and
  integration branches were not touched.
- File check: no stray or untracked files at commit time. No
  phase 1-10 commit adds a `.unity` file. One `.asset` file exists
  in the tree, `Orchestration/Scenarios/FirstLedger/FirstLedger.asset`,
  and it predates the phases (present at `2bd9cf1`); no phase added
  or changed it. This repo is the Scripts folder only: no Main
  Scene file, prefab, or Inspector wiring lives here, so none is
  claimed.
- GUID check: all 1860 `.meta` files in the tree (1859 at phase 9
  plus the new phase 10 doc `.meta`) carry unique `guid:` values;
  the duplicate check returned none. The new file's GUID is in
  section 5.
- Roslyn harness: new harness file
  `harness-passxl-phase10.csproj` in `roslyn-harness/` (copied
  from the phase 9 harness, paths repointed from `repo-passxl9`
  to `repo-passxl10`; earlier harnesses untouched). Full-set
  result: 378 passed, 3 failed. The 3 failures are the known
  pre-existing Phase H failures, unchanged from phases 4-9:
  `HouseholdVerticalSlicePhaseHTests.EndToEndTrace_InsufficientFunds_FailsThenRecovers`,
  `HouseholdVerticalSlicePhaseHTests.Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`,
  and
  `HouseholdVerticalSlicePhaseHTests.Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`.
  No new failures. This is a Roslyn result only, not Unity
  compilation, not Test Runner, not PlayMode, and not a
  full-regression claim beyond the Roslyn full set.
- Slice gate re-verified in the runs: `SixPersonSliceRun` and
  `ThreeBusinessSlice` both set `SuppressMigrantArrivals = true`
  and prove it survives save and restore. No phase 10 change
  touches the gate, immigration, traveler arrivals, or
  outmigration.
- Cleanup in phase 10 (wording only, no behaviour, save-shape, or
  fixture change): stale "0 capital" wording corrected to
  caller-supplied test input in `Orchestration/Player/SixPersonSliceRun.cs`,
  `Orchestration/Player/SliceWorkAndPay.cs`,
  `Orchestration/Player/ThreeBusinessSlice.cs`,
  `Tests/Editor/Population/PassXlPhase6SliceRunTests.cs`,
  `Docs/PassXlPhase6SliceRun.md`, `Docs/PassXlPhase7WorkAndPay.md`,
  and `Docs/PassXlPhase8ThreeBusiness.md`. Mechanical statements
  were kept in mechanical form (a store with no cash cannot order
  on CashOnArrival). Deliberately NOT cleaned: the production
  file `Domains/Economy/Creation/OpeningBusinessRoster.cs`
  (its "no-capital development roster" docstring describes that
  roster's own values and it is never selected by ordinary
  scenarios; changing a production file in a verification-only
  pass would break scope, and section 3 above states the correct
  framing); older Pass 10-13 docs outside the phase 6-8 run
  files (pre-existing, out of scope); and any test parameter
  value (5000c, 2000c, 1000c seeds and similar stay exactly as
  the runs used them, labelled as test inputs).
- NOT proved by phase 10 (same as phase 9): Unity compilation,
  Unity Test Runner, PlayMode, scene wiring, prefab wiring,
  Inspector values, Main Scene visual behaviour, a tick driver
  running on a schedule, a journey, a residence, a postal office,
  a production supplier, a credit policy asset, Bakery staged
  output, person-owned horse feed, first-cycle self-sufficiency,
  or full-regression status beyond the Roslyn full-set result.

## 5. New file in phase 10

- `Docs/PassXlPhase10CodexPrep.md` (this doc), with companion
  `Docs/PassXlPhase10CodexPrep.md.meta`, GUID
  `f607743d4f9a470aa29d45ec7daca894` (freshly generated, not
  copied from any other file).

## 6. Copyable Codex prompt

The block below is the prompt text only. Copy the block contents
as-is.

```
Pull the branch named muse/passxl-slice-phase10 and read the handoff doc at Docs/PassXlPhase10CodexPrep.md along with the phase 4 findings doc, the phase 5 review, the phase 6 run doc, the phase 7 work and pay doc, the phase 8 three business doc, and the phase 9 review. Verify everything in the branch, then get it all into Land and Ledgers and working in Unity so Kennedy can test it. That means Unity compilation, Unity Test Runner including the player as NPC tests, the six person no immigration tests, the six person fixture and 30 day tests, and diagnosis of the ConstructionInputSupportTests timeout, then scenes, drivers, and wiring from section 2 of the handoff doc, then PlayMode checks. Do not merge to main without Kennedy's authorization. Do not change canon. The governing docs are Canonical Bible XXXI and Technical Bible XIV, both dated 2026-10-07, with proposals 001 to 105 approved and 106 to 145 pending. Do not treat any test parameter as canon. Seed cash, starting stock, starting assets, prices, rates, weights, and frequencies in the runs are caller supplied test inputs to vary, never fixed rules. Where the handoff doc lists a Kennedy decision, flag it and wait for his call instead of guessing. Report back what you verified, what you wired, what still blocks testing, and what still needs Kennedy to decide.
```
