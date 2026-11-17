# UBC Workstream J — Manufacturing / Economic Network Audit

- **Lane:** J1
- **Branch:** `muse/ubc-j1-manufacturing-network-2026-11-17`
- **Base:** integration branch `muse/universal-business-construction-2026-11-17` at `247b3865`
- **Date:** 2026-11-17
- **Author:** Muse (lane J1, one-shot)

## Verdict

The manufacturing and economic network among the businesses that actually
exist in the development town is **fully connected**. Every edge between the
seven authored businesses was verified live in production code (a real
driver, real inventory, real cash, real custody). No production connection
was missing among existing businesses, so J1 makes **no production change**.

The single highest-impact missing edge is **wagon repair** (F-J1-1). Wagons
wear with every haul in production, but the repair side of the loop is
entirely undriven and the repairer business does not exist. That edge needs
an **owner decision first** (see §3): a wheelwright shop must be established
with an owner name the owner provides, or the owner must rule how the
freight company maintains its own wagons and where the parts come from.
Driving the freight company's internal repair path as-is would be a free
repair (cost recorded, never paid, no materials consumed), so J1 does not
drive it.

What J1 does instead, following the F2 precedent (interim scope documented,
boundary pinned): a full audit with file+method evidence for every edge,
and **4 new regression tests** pinning the wagon-repair boundary so any
future repair connection is a deliberate change, not a silent shift.

- **Harness before:** 5261 passed / 0 failed (`/home/hatch/ll-ubc-j1-run0.log`)
- **Harness after:** 5265 passed / 0 failed (`/home/hatch/ll-ubc-j1-run1.log`)
- **FAIL-set delta:** empty (both runs have an empty FAIL set;
  `/home/hatch/ll-ubc-j1-fail0.txt`, `/home/hatch/ll-ubc-j1-fail1.txt`)

## 1. Audit method

Each network edge was checked from the code on disk (repository-first rule):
a production caller in `SimulationDrivers.cs`, `SharedBusinessRuntimeManager.cs`,
a hub/bootstrapper, or a named driver was required for LIVE. A runtime, policy
or service with zero non-test callers is DORMANT. A built mechanism with a
production caller on one side and no counterparty is MISSING. In-repo docs are
v3.1; Canon XXXI and Technical Bible XIV were used for the canon pins cited.
No new industry was created merely because a definition file exists.

## 2. Live edges (verified with production callers)

| # | Edge | Evidence |
|---|------|----------|
| L1 | Timber rights → felling → logs → sawmill → lumber → yard → construction → site stockpile → install | G1 verified end to end; `ConstructionProcurementService.UnwindFailedProcurement` + `TryVoidSale` port |
| L2 | Sawmill byproduct (fuel wood) → fuel dealer intake | `SawmillShopRuntime.TransferByproductsToFuelDealer` called from `SimulationDrivers.cs:802`; `SlabOffcutFuelLot` with provenance; 1 log → 4 lumber + 2 fuel wood |
| L3 | Sawmill → yard wholesale transfer, yard pays mill on actual delivered delta | `SawmillToYardTransferDriver.RunTransfer` (`:321` debits yard cash port, credits mill); mill delivers with its own teamsters, not Ethan's freight |
| L4 | Yard retail lumber sales to households/projects | `DriveYardRetailDeliveries` in `SimulationDrivers.cs` |
| L5 | Yard kind-level hardware sales (hinge/latch/handle/nails) | D6; per-kind lots, atomic per kind, refuses naming the short kind |
| L6 | Smith supply: iron, forge coal, nails, glass | D4 (`ImportCatalog.IronStockId`, Pittsburgh via railhead, 220 mi, 14 days, 45c), D5 (forge coal, 180 mi, 12 days, 18c), D3 (nails, 30c), F1 (glass, 25c/pane, 14 days); drivers live, idle only because no smith business exists |
| L7 | Smith hardware delivery to yard | D3 transfer driver; smith credited only on verified delivery |
| L8 | Bakery flour from Millbrook (off-map) | `BakeryWholesaleDailyDriver` + `BakeryFlourSupplierComparison`; flour 48c/sack = 18 bakery units |
| L9 | Bakery bread → General Store wholesale | Authored opening agreement in `DevelopmentTownConfiguration.asset`: buyer general-store, `bake.bread-loaf`, 24/day, 4c/unit; driven by `BakeryWholesaleDailyDriver.RunDay` |
| L10 | Bakery notions from the store | `Series2NotionsSupply` / `BakeryNotionsDailySupply` |
| L11 | Bakery fuel from the fuel dealer | `Series2DealerBakeryFuelSource` via `SharedBusinessWeeklyConverter` |
| L12 | Fuel dealer procurement (off-map + sawmill byproduct) and sales to households | `SimulationDrivers.cs:707/:772/:820` (`AssessLocalPurchase` / `RecordLocalPurchase`); `HouseholdFuelCookingDriver` |
| L13 | Carpentry lumber from yard + supplies from store; furniture sold to households | `CarpentryLumberProcurement`, `CarpentrySuppliesPurchase`, `SimulationDrivers.cs:7274 DriveCarpentryFurnitureSales` |
| L14 | Freight hauling + paid revenue | `FreightShipmentService` + `CollectAndPostUncollectedRevenue` (payer debited before revenue posts); freight 25c base + 1c/mile round trip (provisional) |
| L15 | Freight horse feed + store feed procurement | `DriveBusinessHorseFeedProcurement`, `DriveStoreFeedProcurement`; feed 15c/sack = 2 units |
| L16 | Household shopping / consumption / meals | I1/I2; shortage monitor allocates against one shared cover total |
| L17 | NPC payroll, lifecycle (8-week close), commercial leases | B1 (`CloseAfterConsecutiveStruggleWeeks = 8`), B2 (1500c/month rent, 1500c deposit, 12-month term), B3 (close releases premises, refunds deposit, re-lists) |
| L18 | Farm cycle driven weekly for CropFarm businesses | `FarmCycleDriver.Shared.DriveWeekly` from `SharedBusinessRuntimeManager.cs:4833-4834`; no CropFarm business in the dev town, so the drive is live but idle |
| L19 | Hired construction labour paid | `NpcConstructionHiredLabor.TryWorkPackage` from `SimulationDrivers.cs:3612`; `NpcConstructionDriver.cs:749 PayWorkerForPackage` on completion at 750c/week; employments ended by agreement |
| L20 | Logging camp supply; mill crew | `DriveLoggingCampSupply`; Silas Ward is mill crew (logger/teamster) |
| L21 | Horse care | `DriveHorseCareDay` (`SimulationDrivers.cs:10973`, `HorseCareDriver.CareDay`) |
| L22 | Store restock incl. Ashford furniture reorder | `StoreRestockDriver` held by `GeneralStoreRuntimeManager` (player + NPC variants) via `RegionalSupplierAdapter` (Ashford + Redfield) |
| L23 | Boarding leg (household housing search) | M1-R1 `HousingSearchDrive`, daily, boarding-only |

## 3. The top missing edge — wagon repair (F-J1-1, OWNER DECISION)

### What is live

- Wagons wear in production. `FreightResourcePool.ReleaseTeam`
  (`Domains/Economy/Businesses/Freight/FreightResourcePool.cs:421`) calls
  `wagon.Damage(wagonWear01)` on every haul return (0.02 default, or the
  per-mile profile rate via `ReleaseTeamAfterTrip`). `WagonWearModel`
  (`WagonRepairEconomy.cs`) gives the richer per-trip computation.
- The repair state machine exists: `TrySendToRepair` (wagon → InRepair,
  unavailable for hauling, Canon §3.6) and `TryCompleteRepair`
  (restores condition) on `FreightResourcePool`, plus
  `FreightCompanyRuntime.TryCompleteRepairJob` (internal cost allocation,
  Canon §3.6).
- The wheelwright economy is fully built: `WheelwrightPricing`
  (25c/hour shop rate, 20% parts markup — calibrations), `TurnaroundEstimator`
  (480 shop labour-minutes/day), `WheelwrightRepairPolicy` (escalation 7/14
  days, hauling-blocked flagging), `RepairDemandService.ScanFleetForRepair`
  (threshold 0.60, emergency 0.25 — calibrations), `BlacksmithIronworkSupply`
  (`FindAvailableIronwork` reads "wagon-part" assets from smith stock),
  `WheelwrightRuntime` (queue, `ReceiveIronworkPart`, lumber lots).

### What is missing

- **Zero production callers** for all of it: `ScanFleetForRepair`,
  `TrySendToRepair`, `TryCompleteRepairJob`, `FlagHaulingBlockedOrders`,
  `EscalateOverdueOrders`, `ComputeTripWear01` — tests only.
- **Condition gates nothing.** `SelectAvailableWagon`
  (`FreightResourcePool.cs:391`) filters on status, capacity, reservation
  and holds — never on condition. A wagon at 0.05 still hauls. Wear is
  currently cosmetic: it accumulates silently with no economic consequence.
- **No repairer exists.** The wheelwright path needs a `WheelwrightRuntime`
  shop — a wheelwright business. The dev town has none, and establishing one
  needs the owner to provide the name first (naming rule). The ironwork input
  needs a smith business, which also does not exist.

### Why the freight company's internal path cannot be driven as-is

`TryCompleteRepairJob(wagonId, materialsCostCents, laborMinutes)` records
the cost and restores 0.5 condition, but `internalRepairCostCents` is only
ever incremented (`FreightCompanyRuntime.cs:278`) and read (`:72`) — it is
**never deducted from any cash, never reconciled against any ledger, and no
materials are consumed from any inventory**. Driving it would be a free
repair: condition restored, cost recorded, nothing paid, nothing consumed.

Making it honest needs a real materials leg (lumber from the yard at 40c and
nails from the store at 30c are priced and available; **ironwork has no
source at all** — no smith), a repair duration with saved state (the pool
records no InRepair entry time), and a cash debit from the freight business
ledger. That is designing the maintenance subsystem, not connecting an
existing edge. It also needs a ruling on who does the work (owner-operator's
own time, hired labour, or a wheelwright) and what a repair consumes.

### Owner decision needed (F-J1-1)

Choose one, then a lane can build it:

- (a) Establish a wheelwright business in the development town (owner
  provides the name first), which unblocks the full `RepairDemandService`
  path and the smith-ironwork input; or
- (b) Rule that the freight company maintains its own wagons, and from where
  the parts come (yard lumber + store nails only, or also ironwork — which
  needs a smith either way); or
- (c) Rule that wagon wear stays cosmetic for now (no repair, no gate) and
  the loop is built later with the wheelwright.

J1 does **not** add a fail-closed condition gate on dispatch: with no repair
path at all, a gate would eventually strand Ethan's only wagon with no
remedy, which is worse than cosmetic wear.

## 4. Other missing / dormant edges

| # | Edge | Status / evidence | Needs |
|---|------|-------------------|-------|
| F-J1-2 | Farrier / horseshoeing | `FarrierService.FitShoes` + `BlacksmithRuntime.TakeHorseshoe` (D1 mechanics live) have zero production callers; no smith business, so no shoes exist to fit. M3 recorded the demand model as an open owner question. | Owner: smith business (name first) + demand model (do horses need shoeing on a schedule?) |
| F-J1-3 | Toll sawing | Fully built (`SawmillShopRuntime.ReceiveTollLogLot` / `RunTollSawing`, `SawmillTollPolicy` cash toll per log + in-kind lumber share, custody lots with provenance, pickup register) with zero production callers — and no live customer: the construction path buys lumber from the yard instead of bringing logs. Alternative supply route, not a broken link. | Owner: decide whether commissioned builds may toll-saw instead of buying from the yard |
| F-J1-4 | Grain mill + grain elevator | `GrainMillShopRuntime`, `GrainElevatorShopRuntime`, `GristMillMerchantTrade` — test/registry callers only. No grain: no farm in the dev town. | Owner: farm business (name first) + grain supply |
| F-J1-5 | Dairy / creamery / cheese | `DairyChain`, `Creamery`, `CheeseChain`, `DairyMarket` — `DairyMarket` has zero non-test callers; the dev-town asset contains no cows, dairy or livestock entries. | Owner: livestock supply (cattle from where?) |
| F-J1-6 | Tailor, tannery, bank, doctor practice, butcher | `TailorRuntime`, `TanneryRuntime`, `BankRuntime`, `DoctorPracticeRuntime`, `ButcherRuntime` — no production caller; no such business in the dev town. Butcher is wired (`TryBuyLivestock` / `Slaughter`) but has no livestock to buy. | Owner: the businesses themselves (names first) + input supply |
| F-J1-7 | Freight service agreements | `FreightServiceAgreements` unused; freight works per-trip with paid revenue (L14). M3 recorded as non-blocking by design. | None (by design); agreements are a future feature, not a missing link |
| F-J1-8 | Generic supplier comparison; off-map supplier catalog | `SupplierComparison` dormant (the bakery has its own live comparison); `QuoteAll` / `RegisterSupplier` have zero production callers and the catalog is empty in production — live routes are registered elsewhere (Millbrook flour, Pittsburgh iron/coal/nails/glass, Ashford furniture). | Owner: single import authority decision (carried from F-M3-1); do not invent suppliers |
| — | NPC pricing decisions | No NPC repricing (F-M3-4 / F-B1-7 confirmed). | Owner: repricing policy |
| — | Construction progress billing | Never settles in production (F-M2-1); billing service built with no cash port. | Owner: stage-acceptance decision (carried) |
| — | Completion handoff / shell registration | F-M2-7 handoff unconsumed; F-M4-1/F-M4-2 queue shells never register as homes. | Blocked on F-D11-2 (shell sleeping capacity, owner decision a/b/c/d) |

## 5. Regression tests added

`Tests/Editor/Economy/UBC_J1_ManufacturingNetworkTests.cs` (+ `.meta`, fresh
GUID `551cf76b8de949af99cc25e77fafdab0`). No production file changed. All four
pass in the post-change harness run.

1. `WagonWear_AccumulatesAcrossHauls_WithNoAutoRepair` — five hauls through
   the real reserve/release path wear WG-1 from 1.00 to 0.90, status stays
   Available, nothing restores it.
2. `RepairDemand_NullShop_OpensNothing` — the wheelwright bridge fails
   closed: a wagon at 0.45 with no shop opens zero orders, stays Available,
   diagnostic names the missing wheelwright shop.
3. `WornWagon_StillDispatchable_ConditionGatesNothing` — a wagon at 0.05
   condition still reserves for a haul (condition gates nothing today).
4. `DevTown_HasNoRepairOrSupplyBusinesses` — the authored asset holds exactly
   the seven businesses (general-store, bakery, fuel-dealer, freight-company,
   sawmill, lumber-yard, carpentry-workshop) and no blacksmith, wheelwright,
   farm, dairy, creamery, grain-mill, grain-elevator, tannery, tailor, bank,
   doctor, butcher, hotel, restaurant, saloon, mine or post-office. This pins
   *why* the §4 edges are idle.

Existing coverage was checked first and not duplicated: pricing, turnaround,
policy, wear model, demand scan with a shop, and ironwork supply are covered
by `WagonRepairEconomyTests`; the internal-repair cost recording is covered by
`FreightCompanyTests.RepairJob_RecordsCost_NotRevenue`; toll-sawing mechanics
are covered by `LumberManufacturingBuyerC8Tests` / `SawmillDepthTests`.

## 6. Provisional calibrations

None introduced. Calibrations cited are pre-existing and unchanged: wagon wear
0.02/haul, repair threshold 0.60 / emergency 0.25, shop rate 25c/hour, parts
markup 20%, 480 shop labour-minutes/day, escalation 7/14 days, internal repair
restores 0.5 condition. All are marked CALIBRATION in their source files.

## 7. Codex Unity checklist

None. No harness-excluded file was changed and no production file was changed
at all; the only new files are a harness-compiled test and its `.meta`. The
asset-reading test uses the same `Application.dataPath` pattern as the
`SawmillAuthorshipC8Tests`, which already pass in the harness.

## 8. Follow-ups for the owner / next lanes

- **F-J1-1 (owner):** wagon repair — choose (a) wheelwright business
  (name first), (b) ruled freight-company self-maintenance with a real parts
  source, or (c) wear stays cosmetic for now. Until then the wear loop is
  pinned by the §5 tests, not silently fixed.
- **F-J1-2 (owner):** farrier demand model + smith business (name first).
- **F-J1-3 (owner):** whether commissioned builds may toll-saw.
- **F-J1-4/5/6 (owner):** farm/grain, livestock/dairy, and the remaining
  service businesses all need owner-provided names and input supply before
  any lane wires them. Do not invent them.
- **F-J1-7/8 (owner, carried):** freight agreements (by design, no action)
  and the single-import-authority question (F-M3-1).

## 9. Files changed

| File | Change |
|------|--------|
| `Assets/Scripts/Tests/Editor/Economy/UBC_J1_ManufacturingNetworkTests.cs` | New, 4 boundary tests |
| `Assets/Scripts/Tests/Editor/Economy/UBC_J1_ManufacturingNetworkTests.cs.meta` | New, fresh GUID `551cf76b8de949af99cc25e77fafdab0` |
| `Reports/Campaigns/UBC_J1_MANUFACTURING_NETWORK_2026-11-17.md` | New, this report |

No production file changed. No `sources.txt` change needed (the test directory
is already listed). No money pins, labour pins, hardware pins, lease terms or
close rules touched. No new supplier, free inventory, or instant delivery
invented.
