# Pass XII — Business Completeness Matrix (Worker B)

Date: 2026-10-28. Base: Pass XI final `02aca56f177c6334e6a84568e6f1cb8e1bb495d2`.
Author: Worker B (Generic Businesses & Tasks). Scope: business types, generic
production, equipment capacity, task composition, LL-PLAY-010/011 source side.

**How this matrix was built.** Every classification below comes from reading
the code and grepping production callers, not from design documents. A type is
LIVE only if a real caller in the tick executes production for it. A dedicated
`*Runtime` class with no production caller does **not** make its business
dead, and does **not** make it live either: several LIVE businesses run on a
shared mechanism while their detailed runtime sits unwired. That distinction
is the single most important correction to the October 9 impression that
there are "sixteen rigid Business options".

## 1. Headline counts (29 enum members, no invented types)

`BusinessType` (`Domains/Economy/Core/BusinessDefinitions.cs`) has 29
members: 28 typed values GeneralStore..Lawyer (0..27) plus
`Generic = 255` (classless, descriptive metadata only — it grants
no permission by itself). Classification totals 19 LIVE + 10
PARTIAL = 29. (Worker F correction, round 2: the original §1 table
listed 18 LIVE and omitted Mine, although §4 already classified
Mine as LIVE (driver); the §4 rows were complete at 29.)

| Classification | Count | Types |
|---|---|---|
| LIVE — tick executes production and settles through real counterparties | 19 | Generic, GeneralStore, Blacksmith, Butcher, Ranch, CropFarm, Mine, Sawmill, LumberYard, BoardingHouse, LiveryFreight, Builder, FuelDealer, GrainMill, Bakery, Tailor, Saloon, Barber, Wheelwright |
| PARTIAL — weekly settlement/payroll (and usually baseline throughput) runs, but the trade itself has no tick production operation | 10 | Doctor, Tannery, PostOffice, Newspaper, Restaurant, Hotel, Logging, GrainElevator, Bank, Lawyer |
| SOURCE-ONLY as a separate class | 0 | No business type is source-only as a whole: every type at least reaches weekly settlement/payroll. `WeeklySettlementBusinessTypes` (`SharedBusinessRuntimeManager.cs:812`) itself lists 27 types — it excludes GeneralStore (own-manager payroll) and Generic (driver/service payroll) — so the broader claim rests on those two separate payroll paths, not on that array alone. Source-only status applies to individual *models*, see §3. |
| INTENTIONALLY DORMANT | 0 | Nothing in this matrix is dormant by an owner decision. BankRuntime stays unwired by a Pass XI *engineering* gate (binding would double-count), not by owner policy. |
| UNSUPPORTED (no trade model at all) | 1 of the 10 partials | Lawyer has services (deeds, wills, disputes, title search) but no `*Runtime` trade model and no production operation. |

Directories that are **not** business types and are therefore **not** rows
below: `ImplementDealer` (equipment dealing, no `BusinessType` value),
`Construction` (the builder trade's construction system, not a separate type),
`BoardingHouse` is a type (see row), `Freight` maps to `LiveryFreight`.

### The "sixteen options" resolved

The October 9 count is `AcquisitionMarketManager.ActivationBusinessTypes`
(17 entries, `Domains/Economy/Acquisitions/AcquisitionMarketManager.cs:659`):
GeneralStore, Blacksmith, Butcher, Ranch, CropFarm, Sawmill, LumberYard,
BoardingHouse, LiveryFreight, Builder, FuelDealer, GrainMill, Bakery, Tailor,
Saloon, Barber, Wheelwright. The player-facing **creation** panel
(`BusinessCreationPanelController`) offers only `BusinessType.Generic`
("Trade class: none") plus three templates (merchant lines, bakery, smithy).
So the game advertises 17 activation labels, but behind them are only four
live mechanisms (§2), not 17 distinct simulations. Codex must not present
the 17 labels as 17 distinct simulation depths; the matrix rows say which
mechanism each label actually runs on.

## 2. The four live mechanisms (read this before the rows)

1. **Generic method path (LIVE, driver-called).** `SimulationDrivers`
   → `DriveGenericProduction` → `GenericProductionRuntimeRegistry`
   (`TryStartEligiblePolicy` + `AdvanceBusinessProcesses`), gated by
   `ProductionPathRegistry`: only `BusinessType.Generic` routes here
   durably; every other type routes to LegacyArchetype. Methods/policies
   live on `BusinessInstanceState.GenericConfiguration` (FIFO positions,
   reservations, active processes). Payroll for this path runs through the
   shared `WageSettlementService`, not the monolith weekly settlement.
   **Trap (documented, not a defect):** `GenericBusinessVerticalService.
   ConfigureBakery/ConfigurePrairieForkSmithy` are gated in the creation
   panel to `BusinessType.Generic` only. Calling `ConfigureBakery` on a
   typed `Bakery` business installs methods the driver will never advance
   (path gate refuses them). Never do that; a typed Bakery runs mechanism 2.
2. **Shared archetype path (LIVE, weekly).**
   `SharedBusinessRuntimeManager.ResolveWeeklySharedOperations` →
   `ResolveArchetypeRoutedWeeklyOperations` → `ResolveOperationsForType`
   → `ResolveBusinessOperation`. Goods transformers convert profile input
   categories to output categories at profile throughput, scaled by
   health-adjusted operating efficiency and staffing; service types sell
   visits only when a household actually pays (`ResolveHouseholdPaidServiceVisits`;
   the Pass IX WS1 fix removed the 5–20% revenue multiplier, so business
   credit == household debit exactly). Local-market outlet sales
   (`ResolveArchetypeRoutedLocalMarketSales`) cover 9 types (§4 table).
   Inventory truth here is `BusinessRuntimeState` category stock — a
   *different* truth from mechanism 1, which is exactly why the
   `ProductionPathRegistry` exists and why the two paths must never both
   touch one business.
3. **Dedicated drivers (LIVE).** CropFarm → `FarmCycleDriver` (weekly,
   inside `ResolveBusinessOperation`'s farm branch; production only from
   completed `TaskAuthority` field tasks, never from elapsed time);
   Mine → `MineProductionDriver`/`MineProductionCycle` via the shared
   manager; Sawmill → linked-support + off-map fallback production;
   BoardingHouse → archetype lodging operations; LiveryFreight →
   `FreightCompanyRuntime` (drivers + postal/delivery services) plus
   archetype logistics; GeneralStore → `GeneralStoreRuntimeManager`
   (its own payroll/reorder, deliberately excluded from the shared weekly
   settlement).
4. **Settlement/payroll only (PARTIAL types).** Every type is in
   `WeeklySettlementBusinessTypes`, so every type gets weekly settlement
   reset + payroll — including types with no production operation. That
   is why a PARTIAL business can look "present" in financial reports
   while producing nothing. Codex must label these accordingly (§5).

## 3. The 13 registry runtimes — caller status re-verified by Worker B

`Docs/UnwiredSystems.md` lists 13 `*Runtime` entries. Re-grepping
production callers (excluding the type's own directory, tests, and
comment-only mentions) confirms **all 13 are still unwired**:

| Runtime | Production callers found | Verdict |
|---|---|---|
| BakeryShopRuntime | none | UNWIRED (Bakery itself is LIVE via archetype) |
| BarberShopRuntime | none | UNWIRED (Barber LIVE via service archetype) |
| DoctorPracticeRuntime | none | UNWIRED (Doctor PARTIAL) |
| GrainElevatorShopRuntime | none | UNWIRED (GrainElevator PARTIAL) |
| GrainMillShopRuntime | none | UNWIRED (GrainMill LIVE via goods archetype) |
| HotelShopRuntime | none | UNWIRED (Hotel PARTIAL) |
| LumberYardShopRuntime | none | UNWIRED (LumberYard LIVE via builder demand/baseline) |
| NewspaperRuntime | none | UNWIRED (Newspaper PARTIAL) |
| RestaurantShopRuntime | none | UNWIRED (Restaurant PARTIAL) |
| SaloonRuntime | none | UNWIRED (Saloon LIVE via service archetype) |
| SawmillShopRuntime | none | UNWIRED (Sawmill LIVE via dedicated sawmill production) |
| TailorShopRuntime | none | UNWIRED (Tailor LIVE via goods archetype) |
| BankRuntime | none (only a comment mention in `Financing/BankLedgerBinding.cs`) | UNWIRED by Pass XI gate: `BankLedgerBindingRegistry` exists (one ledger per bank, fail-loud) but is deliberately NOT wired into underwriting — the lender ledger and BankRuntime ledgers are disjoint and binding both would double-count. Scene binding remains a Codex gate (G4). Do not wire. |

Also verified: `BakeryStagedProductionChain` and `ForgeRepairChain` have
zero production callers (editor tests only), and their machinery
(`StagedProductionProcess`, `ProductionDueEventDriver`) is referenced in
production only by those two chains (allowlisted in the registry).
`StoreRestockService` has only the allowlisted Phase H fixture caller.
**Do not wire any of the above in Pass XII** — §6 proves why for the
staged chains specifically.

### Registry gap found (referred to Worker F, not fixed here)

Three further runtime classes are *effectively* unwired but are **not** in
the registry, because the registry's caller test counts hub plumbing as a
caller even when no instance is ever created:

- `ButcherRuntime`: referenced by `SimulationSystemsHub` (list, save/load,
  `GetOrCreateButcherRuntime`) and iterated by `SimulationDrivers` — but
  `GetOrCreateButcherRuntime` has **no production caller**, so the list
  is always empty in production. Butcher itself is LIVE via the archetype.
- `BlacksmithRuntime`: its only non-test reference is
  `Wheelwright/WagonRepairEconomy.cs`, which itself has no production
  caller (tests only). Blacksmith is LIVE via the archetype.
- `TanneryRuntime`: its only non-test reference is
  `Blacksmith/RepairQueueEconomy.cs`, likewise test-only. Tannery is PARTIAL.

Worker B did **not** add these to `Docs/UnwiredSystems.md`: that registry
is machine-checked, Worker F owns it, and adding entries whose "callers"
are hub plumbing would trip its current definition. Recommendation for F:
distinguish *instantiation* from *reference* in the registry test, or
record these three as a documented plumbing-only note. Until then, this
matrix is the honest record.

## 4. Per-type matrix

Shared facts for all rows (not repeated per row): premises are gated by
`BusinessProfileDefinition.SiteRequirements` (default: minimum 1×1 cells,
road frontage required, Business/MixedUse zones) against a `TownPlot`,
plus per-shell `BuildingDefinition.BusinessSuitability` (whether that
physical shell *can host* the trade — not how much land it needs).
Staffing target = `BusinessRuntimeState` worker-slot count; filled =
active `EmploymentRegistry` relationships; archetype efficiency scales
with filled/target and blocks on unfilled roles. Save/load: every
business persists through `BusinessInstanceState.CaptureSaveDto` /
`FromSaveDto` (runtime state + generic configuration + mine state where
applicable); dedicated unwired runtimes have no production save path
because they never run. Unity dependency for every row: the shared
manager, drivers, and hub are MonoBehaviours; no row needs a new
MonoBehaviour, but scene wiring (single hub, single drivers, single
shared manager) is Codex work — see §9.

| Type | Class | Tick production caller | Dedicated model | Customer / counterparty | Stock truth & settlement | Representative tests |
|---|---|---|---|---|---|---|
| Generic (255) | LIVE (method) | `SimulationDrivers.DriveGenericProduction` → registry | None needed: methods/policies on the business | Generic retail lines + local-market sale authority; buyers are real households/businesses via the generic retail path | GenericConfiguration FIFO positions; output only via `TryCreateOutputOnce` | NEW Pass XII: `Pass12BakeryRegistryProofTests` (4 tests, §6). Existing: `GenericBusinessEconomyTests` (excluded from harness subset — see §6 note) |
| GeneralStore | LIVE (manager) | `GeneralStoreRuntimeManager` (daily/weekly; own payroll/reorder) | `GeneralStoreRuntimeManager` itself | Walk-in household shoppers, store sale authority | Category stock; retail lines are a read-projection of it, not a second truth | `GeneralStoreEconomyRepairTests`, `RetailServiceDeskTests`, Pass XI WS4 settlement proof |
| Blacksmith | LIVE (archetype) | Archetype goods group → `ResolveBusinessOperation` | `BlacksmithRuntime` UNWIRED (see §3 gap) | Local-market hardware outlet (capped units/week) + service visits where profiled | Category stock; weekly settlement/payroll | Weekly settlement coverage tests; NEW smithy proof is on the Generic path (§6), not the typed path |
| Butcher | LIVE (archetype) | Archetype goods group + local meat outlet | `ButcherRuntime` hub-plumbed, never instantiated (§3) | Local-market meat outlet; farm cull flow feeds stock | Category stock; perishability aging exists in the runtime but never runs (no instances) — **Codex/owner note: do not advertise lot aging as live** | `StoreButcheryTests`, `FarmLivestockSaleTests` |
| Ranch | LIVE (archetype) | Archetype goods group + local livestock outlet | None (livestock flows via farming) | Local-market livestock outlet; crop-farm feed transfers | Category stock; weekly settlement/payroll | `FarmLivestockSaleTests`, farm integration tests |
| CropFarm | LIVE (driver) | `FarmCycleDriver.Shared.DriveWeekly` from shared manager farm branch | `FarmCycleDriver` + `CropChain` + `PlowWorkService` | Crop-farm output transfers to ranch feed + general-store/local sales | Physical crop lots from completed tasks; transfer through shared logistics authority | `FarmCycleDriverTests` (plant/tend/harvest only from completed tasks), `PlowWorkServiceTests`, `FarmIntegrationTests` |
| Doctor | PARTIAL | None for the trade (weekly settlement/payroll only; employment hooks at shared-manager Doctor branches) | `DoctorPracticeRuntime` UNWIRED | No live patient-visit production path | Category stock baseline only; payroll settles | `DoctorPractice` tests are model-only (no tick proof) |
| Sawmill | LIVE (driver) | `ResolveSawmillOperationsForLinkedSupport` + off-map fallback | `SawmillShopRuntime` UNWIRED | Linked construction support nodes + lumber-yard/builder demand | Category stock + support-node production; hauling via `ResolveSawmillHauling` | Sawmill coverage in weekly settlement tests |
| LumberYard | LIVE (demand) | `ResolveLumberYardLocalBuilderDemand` + off-map fallback + baseline throughput/service | `LumberYardShopRuntime` UNWIRED | Real builder demand (not an invented outlet) | Category stock; weekly settlement/payroll | Lumber-industry tests |
| BoardingHouse | LIVE (lodging) | Archetype lodging operations + hub `BoardingHouseRuntimes` iteration | `BoardingHouseShopRuntime` — instantiated **only** by `FirstLedgerSliceBootstrapper` for slice businesses (drivers `RegisterBoardingHouseRuntime`); other boarding houses run the archetype path only | Lodgers via archetype lodging operations; dedicated runtime (beds/meals) only for slice-seeded houses | Category stock + runtime save DTOs for registered instances | Boarding slice tests |
| LiveryFreight | LIVE (driver) | Archetype logistics + `FreightCompanyRuntime` (drivers, postal, delivery services) | `FreightCompanyRuntime` itself | Real freight customers via postal/delivery/freight authorities; wagon/draft-power reservation truth in hub | Freight custody via logistics authorities; blocked loading refuses (Pass XI WS4) | Freight acceptance/rejection in Pass XI WS4 proof |
| Builder | LIVE (archetype) | Archetype service group → household-paid visits | None (construction execution is `NpcConstructionDriver`, §7 — a different system from Builder businesses) | Households paying for builder service visits | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| FuelDealer | LIVE (archetype) | Archetype goods group + local fuel outlet | None | Local-market fuel outlet | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| GrainMill | LIVE (archetype) | Archetype goods group + local flour outlet | `GrainMillShopRuntime` UNWIRED | Local-market flour outlet; mill output feeds bakers/stores via category transfers | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| Bakery | LIVE (archetype) | Archetype goods group + local bread outlet | `BakeryShopRuntime` UNWIRED; `BakeryStagedProductionChain` UNWIRED (§6) | Local-market bread outlet (typed path). **Generic-template bakeries** (classless + bakery template) run the method path instead — the two must never both run on one business | Typed: category stock. Generic-template: FIFO positions (§6 proof) | Typed: weekly settlement tests. Generic-template: NEW §6 proof |
| Tailor | LIVE (archetype) | Archetype goods group + local clothing outlet | `TailorShopRuntime` UNWIRED | Local-market clothing outlet | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| Saloon | LIVE (archetype) | Archetype service group → household-paid visits | `SaloonRuntime` UNWIRED | Households paying for saloon service | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| Barber | LIVE (archetype) | Archetype service group → household-paid visits | `BarberShopRuntime` UNWIRED | Households paying for barber service | Category stock; weekly settlement/payroll | Weekly settlement coverage tests |
| Wheelwright | LIVE (archetype) | Archetype goods group + local repair outlet | `WheelwrightRuntime` referenced only by test-only repair helpers | Households/businesses paying for wagon repairs | Category stock; weekly settlement/payroll | `RepairQueueEconomyTests`, `WagonRepairEconomyTests` (model-only) |
| Mine | LIVE (driver) | `MineProductionDriver`/`MineProductionCycle` via shared manager | Mine runtime state on the business (`MineRuntimeState`, persisted) | Ore/coal buyers via category transfers; payroll via shared settlement | Mine ledger + category stock; assay provenance (Pass XI WS3) | Mine tests; Pass XI WS4 (mine closure day 20) |
| Tannery | PARTIAL | Baseline throughput/service loop only (not profession-wave, not excluded) + settlement/payroll | `TanneryRuntime` effectively unwired (§3) | No live tannery customer path verified | Category stock baseline only | Tannery model tests only |
| PostOffice | PARTIAL | `PostalService` is driver/hub-referenced (mail, money orders, registered mail) + settlement/payroll + baseline | No `*Runtime` trade model; `PostalService` is the service layer, not a PostOffice-business production operation | Postal customers via PostalService; money-order settlement is live as a *service* (RUN-011 area, Worker F) | Postal books (Pass XI WS4: balanced at 5000, fees 25) | Money-order / postal tests |
| Newspaper | PARTIAL | Baseline throughput/service loop + settlement/payroll | `NewspaperRuntime` UNWIRED | No live circulation/advertising production path verified (ads buy through a real paper only as a design claim, not a tick path) | Category stock baseline only | Newspaper model tests only |
| Restaurant | PARTIAL | Baseline throughput/service loop + settlement/payroll | `RestaurantShopRuntime` UNWIRED | No live meal-service production path verified (served-meal nutrition is a Canon claim, not a tick path here) | Category stock baseline only | Restaurant model tests only |
| Hotel | PARTIAL | Baseline throughput/service loop + settlement/payroll | `HotelShopRuntime` UNWIRED | No live room-night production path verified | Category stock baseline only | Hotel model tests only |
| Logging | PARTIAL | Baseline throughput/service loop + settlement/payroll | `LoggingOperationRuntime` — zero production callers | No live felling/hauling production path verified (rights-gated felling is design, not a tick path) | Category stock baseline only | Logging model tests only |
| GrainElevator | PARTIAL | Baseline throughput/service loop + settlement/payroll | `GrainElevatorShopRuntime` UNWIRED | No live receipt/grading production path verified (bailment custody is design, not a tick path) | Category stock baseline only | Grain-elevator model tests only |
| Bank | PARTIAL (gated) | Settlement/payroll list only; **no** production operation; underwriting deliberately unwired (see §3) | `BankRuntime` UNWIRED; `BankLedgerBindingRegistry` delivered, not wired into underwriting | No live deposit/lending production path for player banks (lender instruments exist separately — Worker C's domain, do not conflate) | No bank ledger registered as a CreditCash store (Pass XI WS1 finding, still true) | Bank binding-contract tests (contract only, not a live bank) |
| Lawyer | PARTIAL (no model) | Settlement/payroll + baseline loop only | No `*Runtime`; services exist: deeds, wills, dispute representation, title search, contracts (`LawyerPractice` + services) | Clients are real persons in the service APIs, but no tick production operation drives them | Category stock baseline only | Lawyer service tests (API-level, not tick) |

### UI-advertised but not functional — flagged, not hidden

- **Doctor, Tannery, PostOffice, Newspaper, Restaurant, Hotel, Logging,
  GrainElevator, Bank, Lawyer are NOT in the 17 activation options**, so
  the acquisition UI does not currently advertise them as activatable
  businesses. They are reachable only as enum values / profiles / service
  APIs. Codex must not add them to an activation list without a tick
  production path; adding the label first would repeat LL-PLAY-011.
- **Within the 17 advertised options, the dedicated shop runtimes for
  Bakery, GrainMill, Tailor, Saloon, Barber, Sawmill, LumberYard are
  unwired.** What the player gets for those labels is the shared
  archetype/demand mechanism, not the detailed shop simulation. Any UI
  copy implying shop-level detail (oven lots, chair leases, press runs)
  for those labels is currently misleading and must be corrected or
  gated by Codex.
- **Butcher lot aging** (`ButcherRuntime.AgeLotsToDay`) is referenced in
  a driver comment but never executes (no instances). Do not advertise
  perishability aging for butcher stock as live.
- **Typed Bakery vs generic-template bakery** are different simulations
  with the same display name. UI must distinguish them or, preferably,
  Codex should route formation through one path and label the other as
  unavailable until the path decision is made (owner decision, not
  Worker B's to take unilaterally).

## 5. Wired vs gated (Worker B's justified selection)

| Pathway | Decision | Proof / reason |
|---|---|---|
| Generic bakery (classless + template) through registry/driver path | WIRED (already live; Worker B proved it, §6) | 4 new tests through `TryStartEligiblePolicy` + `AdvanceBusinessProcesses` + business DTO restore; exactly-once output |
| Generic smithy (classless + template) through registry/driver path | WIRED (already live; Worker B proved it, §6) | Registry start picks a stock method, repair stays order-gated, one nail, no duplicate |
| `BakeryStagedProductionChain` (9-step method, flour lots, oven reservations, waste ledger) | GATED — do not wire | It produces the same output (bread) from the same inputs (flour, fuel) as the live generic bread method. Wiring it on a bakery that also runs the generic path (or the archetype path) double-produces. Safe wiring would require: (a) a per-business path flag retiring the generic bread method for that business, (b) a production caller in drivers, (c) a save/load seam for chain state into the business DTO, (d) retirement of the archetype bread outlet for that business. None exists. The chain stays test-covered and documented. |
| `ForgeRepairChain` (repair orders, nail runs) | GATED — do not wire | Same double-production hazard vs the live generic smithing/repair methods (the generic repair method is already order-gated and produces repaired ironwork). Needs the same four preconditions as the bakery chain. |
| All 13 registry `*Runtime`s | GATED — do not wire | Each would become a second production authority beside the archetype path for the same business (the exact Pass V P1-4 hazard the `ProductionPathRegistry` was created to prevent). Wiring any one requires retiring that type's archetype operation for wired instances — a per-instance path migration, not a flag flip. |
| `StoreRestockService` | GATED — do not wire | Only the Phase H fixture calls it; the live store path is `GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder`. Wiring both would double-order. Needs a retirement decision for the manager's reorder step first. |
| `RealDemandSource`, `HouseholdMoveService` | OUT OF SCOPE for Worker B (gates G1 / by-decision; Worker F / owner) | Not business-production pathways; left exactly as Pass XI left them. |

No production code was changed to achieve the "wired" rows: they were
already wired. Worker B's contribution is the proof (§6) and this matrix.
That is deliberate: the brief asked for a justified selection, not for
wiring volume.

## 6. Bakery proof (representative staged workflow, live generic path)

New tests: `Tests/Editor/Economy/Generic/Pass12BakeryRegistryProofTests.cs`
(+ fresh-guid `.meta`). They run through the registry — the exact seam
`SimulationDrivers.DriveGenericProduction` calls — not through the
executor alone and not through the unwired staged chain:

1. `BakeryRegistry_ShipmentReceiptBakeAndRestore_ProducesExactlyOneLoaf`:
   flour + fuel arrive via `GenericShipmentReceivingAuthority.TryReceive`
   (same shipment id received twice → refused, flour stays 5); registry
   start consumes 1 flour + 1 fuel exactly once; a second loaf is refused
   while the oven is reserved; day-1 advance completes load (phase 1,
   28 min remaining, baker released — `PersonRequiredNow == false`);
   mid-bake business DTO save/restore preserves phase, reservation,
   consumed inputs and zero bread; day-2 advance completes bake (phase 2,
   still zero bread); day-3 advance completes remove-check → exactly 1
   bread; a day-4 advance adds nothing (flour/fuel still 4).
2. `BakeryRegistry_InterruptedBake_ProducesNothingUntilFinished`: a
   partial 10-of-28-minute bake advance leaves phase 1, 18 minutes
   remaining, oven reserved, zero bread, inputs consumed once; resuming
   through the registry finishes to exactly 1 loaf. Interruption uses
   only existing mechanics (partial phase advance + resume); no new
   interruption authority was invented.
3. `SmithyRegistry_NailsThroughLivePath_RepairStaysOrderGated`: iron +
   fuel → registry start picks a `generic-smithing-*` stock method (the
   priority-100 repair policy is MakeToOrder with no order waiting, so it
   is not picked); one 10-minute person phase finishes in one driver-day
   advance to exactly 1 unit; a further advance adds nothing.
4. `SmithyRegistry_WithoutIron_RefusesStart`: no iron → start refused,
   no open process created.

Energy accounting: the generic bread method consumes 1 fuel per loaf as
a material input (the method's `fuel` requirement); there is no separate
energy meter in the generic path, and Worker B did not invent one. Oven
fuel as a distinct physical stock (cf. `BakeryOvenFuelTests`, model-only)
is not reconciled against the generic method's fuel input — a known
modelling seam for a later pass, not a hidden double-count: the method
consumes fuel once, and nothing else consumes it for the same loaf.

Harness-subset note: `GenericBusinessEconomyTests.cs` is **excluded**
from the harness (`sources.txt:235`) because its factory uses
`BusinessCreation.cs`, which is not in the harness subset. The new proof
tests deliberately avoid the creation authority and build the business
with `BusinessInstanceState.Create` + a Generic fallback profile, so they
compile and run in the harness. That is a fixture workaround, not a
production difference: the created state is the same type the authority
returns.

**Staged-chain verdict (why it stays gated).** The staged chain's
9-step method, flour-lot custody, oven workstation reservations, waste
ledger and due-event driver are real and test-covered
(`BakeryStagedProductionChainTests`), but they are a *parallel* bakery,
not a stage of the live one. The live generic bread method already
implements the brief's staged workflow faithfully: load (person) →
bake (passive, oven reserved, person free for compatible work — proven
at bridge level by the existing
`ProductionTaskBridge_ReleasesWorkerDuringBakeAndRequestsRemovalTask`
test) → remove-check (person) → authoritative completion → saleable
output, with save/restore without duplicate output (§6 test 1). Wiring
the chain as well would produce two loaves from one flour. The chain
should be wired only under the four preconditions in §5, owned by a
later path-migration decision.

## 7. Representative loops (beyond bakery)

| Loop | Verdict | Evidence |
|---|---|---|
| Farm plowing + harvest | LIVE, verified (no new tests needed) | `PlowWorkService` (plow orders, team animals, save DTO) + `FarmCycleDriver` (plant/tend/harvest only from completed `TaskAuthority` tasks; `DriveWeekly` called by the shared manager). Tests: `PlowWorkServiceTests`, `FarmCycleDriverTests`, `FarmIntegrationTests`. No new Worker B tests: duplicating that coverage would add count without proof. |
| Blacksmith forging / repair | LIVE on the generic path (new proof, §6 test 3); typed path is archetype-only | Generic smithy methods (8 products + repair) via registry; repair stays order-gated. `ForgeRepairChain` stays gated (§5). Typed Blacksmith production is archetype throughput, not forge work. |
| Sawmill production | LIVE (dedicated) | Linked-support production + hauling + off-map fallback in the shared manager (§4 row). No new tests: the mechanism is manager-internal weekly code, and its coverage lives in weekly settlement tests. |
| Wagon loading | LIVE, verified by Pass XI WS4 | Freight loading refuses overloaded cargo pre-load with zero cargo (WS4 proof); `FreightCompanyRuntime` is driver-referenced. No new tests. |
| General Store receiving / sales | LIVE (manager) | `GeneralStoreRuntimeManager` + `RetailServiceDesk` + generic retail lines as read-projection. Receiving for generic businesses uses `GenericShipmentReceivingAuthority` (idempotent, §6 test 1). No new tests: store coverage lives in `GeneralStoreEconomyRepairTests` / `RetailServiceDeskTests`. |

## 8. Equipment capacity audit (§7.2 of the brief)

No new capacity authority was built, and nothing was flattened to
capacity = 1. The existing system already covers every dimension the
brief lists — but the generic production path does not consult all of it.
That split is the finding.

| Dimension | Existing support | Used by generic production? |
|---|---|---|
| Exclusive operator assignments | `EquipmentCapacityModel.Exclusive` + `EquipmentReservationService` (hub-owned, shared with draft power / freight) | Via its own per-id check, not via the capacity service: `GenericConfiguration.IsEquipmentAvailable` refuses an equipment id reserved by any open process. Effectively exclusive per id — correct for ovens/forges, but see below. |
| Payload / volume / mass / dimensions | `CapacityEnvelope` (MaxMassKg, MaxVolumeLiters, MaxFootprintSquareMeters, MaxLengthMeters, MaxItemCount); `Transport` model checks mass/volume against reservations | No — generic methods carry no envelopes; a wagon-load through the generic path is not mass/volume-checked. Freight loading (dedicated path) is checked. |
| Workstation slots | `Workstation` model (stationCount) + `Workstations` catalog + generic workspace ids (`IsWorkspaceAvailable`, same exclusive-per-id rule) | Partially: workspaces are exclusive per id in the generic path, so a 2-station workspace modelled as one id is under-used (capacity 1 in effect for that id). Modelling two stations as two ids recovers the slots; nothing in code does that mapping automatically. |
| Throughput (rate-limited processing) | `Throughput` model (parallel lanes) + sawmill/thresher-style drivers | No — generic phases are fixed elapsed minutes, not rate-limited lanes. |
| Passive processes | `PassiveProcess` model (batch occupies space/heat while worker released) + generic phase `RequiresPerson = false` with reservations retained | Yes — bake phase is the working example (§6). |
| Wear / repair | `EquipmentConditionProfiles` (wear per active hour, per passive hour, per mile; condition degrades usable capacity fraction) + `EquipmentCapacityService.UsableCapacityFraction` + forge repair chain (gated, §5) | No — generic production does not degrade equipment condition or require repair. Wear lives in the capacity-service world; generic equipment ids have no condition. |
| Multiple compatible concurrent tasks | `GenericProductionTaskBridge` releases the person during passive phases (bridge test proves a cleanup task can run during bake); `TaskAuthority` holds ordinary tasks | Yes for person-time; no for equipment — two processes cannot share one equipment id even if physically compatible (e.g. two batches in one large oven). |

**Proposed policy (not implemented — needs no Canon invention, but does
need an owner/Worker F decision on where the single reservation truth
lives):** bridge, don't duplicate. Let `GenericConfiguration` remain the
single reservation authority for generic production, and let it consult
`EquipmentCapacityService` descriptors as *advisors* (envelope/slot
counts per equipment id) instead of growing a second reservation store.
Until that bridge exists, capacity modelling for generic businesses
should be authored as one id per independently reservable unit (one oven
chamber = one id, one forge = one id). Do not present generic-path
equipment as mass/volume-checked or wear-tracked — it is not.

## 9. Tasks audit (§7.1 of the brief)

Terminology correction first: the brief names `TaskInstance` and
`ActivityInstance`, but those classes do not exist in the production
tree. The real composition is:

- `TaskDefinition` → `WorkTask` via `TaskAuthority` (register / create /
  assign / start / record work / complete; `WorkTimeBudgetStore` bounds
  person-minutes per day). This is the one task authority; nothing else
  schedules person-time.
- Generic production: `GenericProductionTaskBridge` turns each
  person-required phase into an ordinary `WorkTask` (definition id
  `generic-production:<process>:phase:<index>`, skill = the method's
  `PerformanceSkillId`); passive phases advance time without a task and
  keep equipment/workspace reservations. Proven by §6 tests + the
  existing bridge test.
- Farm: `FarmCycleDriver` + `CropChain` drive only from completed
  `WorkTask`s (plant/tend/harvest), with task ids recorded as labor
  provenance. Proven by `FarmCycleDriverTests`.
- Construction: `WorkPackage` (dependency-ordered, staged materials with
  provenance, committed vs required labor minutes, cancellation returns
  staged lines exactly once) driven by `NpcConstructionDriver` from
  `SimulationDrivers` (one step/day, Pass XI WS2). `WorkOrder` as a
  generic class does not exist; domain orders (`ForgeRepairOrder`,
  `PlowWorkOrder`) are separate models, not a second task system.
- `PersonScheduleTracker` holds `PersonActivityReservation`s (indexed by
  person/day since Pass IX WS4) — a schedule/conflict view, not a task
  executor. It does not double-schedule production work; production
  tasks go through `TaskAuthority`, and the tracker is consulted for
  availability, not as a second commitment store. No integration break
  was found in this composition, so no task-system fix was made.

Deliberately **not** imposed: no universal one-minute minimum (phase
durations come from method definitions; `ProductionProcessState.Start`
floors a *phase* at 1 minute only to avoid a degenerate zero-minute
phase — that is a data guard, not a labor rule, and was left alone) and
no fixed skill count (a method names one `PerformanceSkillId`; skills
are per-method data, not a global roster).

## 10. LL-PLAY-010 — generic construction / formation interface contract

Source facts (not a new system — the live construction driver is
`NpcConstructionDriver`, Worker A / existing Pass XI work; Worker B does
not duplicate it):

- **Owning land** — `TitleAuthority` (`Domains/World/Property/PropertyTitle.cs`)
  is the ownership-transfer authority; a `TownPlot` is the parcel. The
  acquisition flow (`AcquisitionMarketManager`) resolves build options
  per plot (`TryResolveBusinessBuildOption`) against a `BuildingDefinition`
  catalog and the plot's site requirements. Ownership ≠ a building.
- **Designing a building** — `BuildingDesignCatalog`
  (`Domains/Economy/Businesses/Construction/BuildingDesignCatalog.cs`):
  a `BuildingDesign` is phases (materials, tools, labor) + space specs +
  a prefab handoff record (`RequestPrefabHandoff`). Designs are data;
  registering a design creates no building and no business.
- **Constructing a building** — `NpcConstructionDriver` (driver-called,
  one step/day) executes `WorkPackage`s in dependency order with staged
  materials from the site stockpile (freight receiving authority) and
  committed person-minutes. A building becomes occupiable only when its
  packages complete; cancellation returns staged materials exactly once.
- **Acquiring premises** — acquisition closing transfers title/occupancy
  through the title and housing authorities (Worker C / Pass XI
  occupancy paths). Acquiring a shell is not forming a business.
- **Establishing a business** — `BusinessCreationAuthority` (BIZ-1:
  intent → identity → ownership/governance → capability-driven premises
  → assets/working capital). Creating the entity does **not** make it
  operating (Canon §3.2); operating status comes from actual commerce
  via `BusinessOperatingLedger`. The creation panel's classless
  (`Generic`) formation + templates is the current player-facing form of
  this step.
- **Equipping and operating** — equipment procurement
  (order → pay/finance → ship → receive → install → operational,
  `GenericEquipmentProcurementAuthority`) followed by mechanism 1/2/3
  production (§2). Equipment installation requires setup before operation
  (`IsOperational` gate); uninstalled equipment must not count as
  available, and the generic path enforces this through registered
  equipment ids, not through the procurement state — Codex must surface
  the installation gate in UI so a player is not told an oven is ready
  when it is still being installed.

**Contract for Codex (LL-PLAY-010).** The obsolete "Build Shell / Next
Shell / Previous Business" controls conflated the six stages above into
one menu. Replace them with six distinct, separately gated UI states,
each reading only its own authority: (1) parcel + title state, (2)
selected design (catalog id + phases + materials), (3) construction
progress (packages completed / materials staged / labor committed),
(4) premises acquisition state (owned shell vs leased vs none),
(5) business formation state (entity exists? operating?),
(6) equipment + operating state (installed assets, registered methods,
active processes). No stage may advance another stage's authority, and
no control may spawn a building without a completed construction path
and a title transfer. The exact GameObject/panel wiring is Codex's
scene work — Worker B cannot name scene paths that were not inspected
in Unity, and does not invent them here. **Acceptance test for Codex:**
start from an owned empty parcel and reach an operating generic bakery
using only those six states, with each transition's authority visible;
a menu-spawned building anywhere in that flow fails the test.

LL-PLAY-011 disposition (Worker B portion): **source contract ready +
matrix delivered; Unity authoring needed.** The source now honestly
classifies every type (§4), proves the two generic-template loops (§6),
and gates the rest (§5). The remaining Unity work is presentation
labelling (§4, flagged items) and the six-stage construction interface
(§10). This issue is **not** "fixed in Unity" by this matrix — it is
fixed at source-classification level only, and Codex must still do the
labelling and interface work before an owner playtest can confirm it.

## 11. What Codex still must do (Worker B portion)

1. Do not wire any §3/§5 gated runtime or chain. If a future pass wires
   one, it must first retire that business's archetype/generic operation
   for wired instances via a per-instance path migration and update
   `Docs/UnwiredSystems.md` + its machine-checked test in the same
   commit (Worker F owns the registry test).
2. Label the 17 activation options by their §2 mechanism; gate or
   correct copy for shop-level detail on Bakery/GrainMill/Tailor/Saloon/
   Barber/Sawmill/LumberYard, butcher lot aging, and typed-vs-generic
   bakery identity (§4 flagged items).
3. Build the six-stage construction/formation interface per §10; never
   menu-spawn a building.
4. For the 10 PARTIAL types, either keep them out of activation UI or
   label them explicitly as not-yet-operating trades; do not add them
   to `ActivationBusinessTypes` without a tick production path.
5. Surface the generic equipment installation gate (§10) and the
   staffing truth (worker slots vs filled roles, §4 shared facts) in
   business inspection — the data exists; the read models are Worker D's
   deliverable, not Worker B's.
6. Unity verification remains open for everything here: no Unity
   compilation, Test Runner, PlayMode, or scene claim is made by this
   matrix. All counts in §12 are Roslyn harness counts.

## 12. Worker B change summary (for the coordinator)

- New: `Tests/Editor/Economy/Generic/Pass12BakeryRegistryProofTests.cs`
  (+ fresh-guid `.meta`, guid `5c4e7e9025a346cfa0e24b9812537a8e`) — 4 tests.
- New: `Docs/Pass12_Business_Completeness_Matrix.md` (+ fresh-guid
  `.meta`, guid `174b95df7c5349c4b7494645f3bce6bd`).
- Changed production files: **none**. No authority was altered, no
  runtime was wired, no test was weakened or deleted, no registry entry
  was added or removed (see §3 gap note for Worker F).
- Shared files touched (coordinator attention): **none** outside Worker
  B's owned folders. The new test file lives in
  `Tests/Editor/Economy/Generic/` (owned); the matrix doc is in `Docs/`
  (deliverable docs, not a production shared file).
- Focused harness results (worktree csproj variant, isolated outputs,
  0 build errors): `Pass12BakeryRegistryProofTests` — 4 passed, 0 failed.
  Full-tree regression is the coordinator's / Worker F's run; Worker B
  did not claim a full-tree count from a partial run.
