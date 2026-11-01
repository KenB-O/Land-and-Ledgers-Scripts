# Pass XI WS2 Implementation Report - Live Economic Circulation

Branch muse/pass11-ws2-economic-circulation, from base 3af1863.
Worktree ~/workspace/land-and-ledgers/wt-pass11-ws2.
No Unity compilation, Test Runner, or PlayMode result is claimed anywhere
in this report. Every result below is a Roslyn harness result or a source
reading. Nothing in this pass was merged into muse/continuous-development.

## Baseline and final counts

- Baseline on this branch before WS2 work, properly regenerated harness
  csproj, isolated build outputs: 1327 passed, 3 failed. The 3 failures
  are the documented test-side defects left failing deliberately (two
  MoneyOrderServiceTests and one P5 tax test). Those tests were not
  touched and were not reclassified.
- Final full run on this branch, properly regenerated csproj with all
  four new test files grep-verified present, run with
  LAND_LEDGERS_REPO pointed at the worktree: 1345 passed, 3 failed.
  The 3 failures are exactly the same three historical failures, named
  in the final report to the coordinator. New tests added: 18
  (9 construction driver, 3 freight handler, 3 bank binding, 3 household
  move). No test was deleted, no assertion was weakened, no new failure
  appeared.
- Registry tests green (UnwiredSystemsRegistryTests and the assembly
  scan), with the scan run under LAND_LEDGERS_REPO pointed at the
  worktree. Note for future runs: the scan defaults to the base repo
  checkout when the variable is unset, which misreports callers for a
  worktree build. Always set it to the tree under test.
- Full-repo Roslyn variant build (every source file except non-test
  Editor folders, plus the harness shims): zero errors in every file
  edited by this pass, including the Unity-side SimulationDrivers and
  SimulationSystemsHub files that the harness subset cannot compile.
  Other pre-existing Unity package gaps elsewhere in the tree were not
  touched and are not reported as WS2 results.

## Item 1 - NPC construction driver (A-03, RUN-002) - FIXED, with tests

Before this pass, NpcConstructionCommissioner.CommissionProject and
ConstructionProjectExecutor.AdvancePackage had zero production callers
outside a test fixture, and SimulationDrivers had no construction drive
at all. That is now changed.

New production code:

- Domains/Economy/Businesses/Construction/NpcConstructionDriver.cs -
  the production driver. It advances each hub project by at most one
  step per day, in package sequence order: commission from an explicit
  request, fund and stage materials, work labor, advance the package,
  complete the project into a HousingAuthority building, and occupy it
  with the commissioning household. A refusal at any step leaves the
  project exactly where it was and stops that project for the day. No
  progress without work, no free materials, no double staging after
  load (staging runs only from Ready, and staged lines persist on the
  project DTO). Paused projects are skipped, never auto-resumed, since
  resuming is an explicit NPC decision through the commissioner.
  Commissioning runs only from explicit requests queued by the caller.
  The driver never commissions on its own, because the trigger rule
  (which NPC households commission, on what evidence) is a design
  decision, not something the driver may invent. The driver also blocks
  blind retry after a failed procurement leg, since the paid-for lot
  stands with the supplier under the procurement service's semantics
  and a retry would buy the materials a second time. An explicit
  AllowProcurementRetry call clears the block.
- Orchestration/Systems/SimulationDrivers.cs - DriveNpcConstruction
  runs on every day change, plus BindNpcConstructionPorts and
  QueueNpcConstructionRequest. The labor leg is fully wired from hub
  authorities: each active member of the commissioning household is
  tried in turn for own labor through the commissioner, and worked
  minutes credit only as the bound task is actually worked. Equipment
  reservations are attached from the hub. Refused commissions stay
  queued for the next day.
- Domains/Economy/Businesses/Construction/NpcCommissionedBuild.cs -
  one additive method, FindRecordForProject, so the driver can fund a
  project restored from a save without a caller-held record reference.
  No other behaviour in that file changed.

Genuine defect found and fixed while building the driver:

- Domains/Economy/Businesses/Construction/ConstructionMaterialFreight.cs
  - ConstructionMaterialFreightCoordinator.FreightSaleToSite built its
  shipment hooks with no receiving authority, so the shipment service
  honestly held every site delivery in wagon custody and the chain
  could never complete at unloading. The site stockpile is now the
  receiving authority for the leg, with a destination check that
  refuses shipments bound for anywhere but the site. Acceptance is site
  custody only; stockpile bookkeeping follows after conservation is
  verified, and staging consumes the stockpile, so no unit is counted
  twice. This fix is what makes the driver's procurement mode (real
  supplier discovery, real-lot sale, real-cash payment, real-chain
  freight, site staging) actually complete.

Explicitly outstanding, not invented:

- Catalog binding. No catalog asset is registered in source, so the
  driver cannot commission or complete a project until a catalog is
  bound through BindNpcConstructionPorts. Completion needs the catalog
  design; Ready packages wait loudly until then.
- Material source for yard purchases. No yard or sawmill lot adapter
  exists in production. The only production material source adapter is
  the household-inventory source (SimulationDrivers.
  CreateHouseholdConstructionMaterialSource), which fits self-supply
  but not the commissioner's supplier-payment leg (paying a supplier
  for the household's own goods would misstate the books), so the
  driver does not default to it for purchases. A per-household source
  resolver property on the driver exists for future yard or household
  source wiring.
- Parcel authority. No production adapter reads the real TitleAuthority
  for build authorization, so commissioning waits on a bound parcel
  authority. The driver refuses to commission without one.
- Commissioning trigger. No automatic trigger queues commissions. The
  queue is the production entry point; the rule that fills it is a
  design decision recorded below as outstanding.

Tests: 9 new Pass11Ws2NpcConstructionDriverTests, every one driving
through NpcConstructionDriver (commission, drive, cancel), never by
invoking CommissionProject or AdvancePackage directly. They cover
parcel-unauthorized refusal, unfunded-design refusal, the full chain
to completion and occupancy with cash and stock conservation plus
no-change on extra drives, procurement with supplier discovery and
freight clearing the stockpile, insufficient-stock waiting with no
payment followed by advancement after restock, save mid-package and
resume without double funding or double staging, cancel returning
staged materials exactly once with a refused second cancel, pause
skipping without auto-resume, and procurement retry blocking.

## Item 2 - Banking (A-06, RUN-003) - CONTRACT DELIVERED, scene hookup outstanding

Verified live, not redone:

- NPC business cash binding (SimulationDrivers.
  WireNpcBusinessCashStores) and household cash binding
  (RefreshHouseholdCashBindings, which deliberately binds only ledgers
  that already exist in the authoritative registry) are both live
  production paths. No gap was proven there, so nothing was changed
  there.
- The live business lender reads its own ledger at every underwriting
  decision: TryArrangeLoan computes LendableCents from live cash on
  hand and the live reserve floor, with no cached deposit snapshot
  anywhere in the path. A test proves the figure moves with ledger
  events (100000c lendable, 90000c after a 10000c disbursement, and an
  oversized loan refused with the figure unchanged).

The real gap, found by reading the deposit flows:

- Depositor deposits only ever reach BankRuntime ledgers
  (TellerOperations, DepositorConfidence, interbank flows), and
  BankRuntime has zero production instances. The live lender's ledger
  receives charter capital and loan repayments only, never depositor
  deposits. So the two bank cash authorities are disjoint, and binding
  both ledgers for one bank would double-count deposits.

Delivered contract:

- Domains/Economy/Financing/BankLedgerBinding.cs -
  BankLedgerBindingRegistry. It binds at most one deposit ledger per
  bank business instance id, refuses a second distinct ledger loudly
  (the first binding stands), treats same-instance rebind as a no-op,
  and reads deposit positions live from the bound ledger (cash on hand,
  deposits owed, net) with no cached balance stored anywhere. It holds
  references only: no balances, no accounts, no debt. It deliberately
  does not rewire BusinessLenderService, since wiring it in as a second
  funding source would be an unfunded-loan bug. The registry's
  production home is a lazy hub property (SimulationSystemsHub.
  BankLedgerBindings, one minimal additive property) with a
  SimulationDrivers.BindBankLedger binding point.
- Scene hookup explicitly outstanding under G4: a future Unity step
  binds either a scene ledger object or a constructed BankRuntime
  ledger for a player-owned bank, never both. Until that step lands,
  the registry is a tested contract, not a live binding, and must never
  be described as wired. PlayerDebtManager underwriting keeps reading
  its own finite lender purse, which is live at approval and commit
  time; no invented lending capacity and no second debt ledger were
  introduced.

Tests: 3 new Pass11Ws2BankBindingTests (single-ledger rule, live
position reads after deposits, lender lendable figure moving with its
ledger).

## Item 3 - Housing and boarding (A-10) - VERIFIED, one genuine gap documented as blocked

Verified live by source reading:

- Boarding parcel registration path: FirstLedgerSliceBootstrapper
  registers one BoardingHouseShopRuntime per BoardingHouse business
  with authored bed capacity above zero, through
  SimulationDrivers.RegisterBoardingHouseRuntime into the hub, which
  dedups by reference, and the bootstrapper separately dedups by
  business instance id. A business with zero authored bed capacity is
  skipped with a warning and never gets a runtime. Bed capacity comes
  from BaselineDailyServiceCapacity through
  GetBoardingHouseBedCapacity.
- Room capacity, drives, tenancy, rent, arrears, closure, save and
  load: DriveBoardingHouseDays and DriveBoardingHouseWeeks execute the
  runtimes daily and weekly, with weekly dues settled through
  RentCollection against household ledgers and the obligation
  authority. DriveTenancyPayments settles due rents daily through the
  tenancy service, with partial payment arrears becoming real
  obligations. Hub boarding runtime save and load dedups by business
  instance id, so restore cannot duplicate a runtime. The absence
  sweep vacates boarders through BoardingLodgingBridge.
  VacatePersonFromHouse, and bridge CloseHouse checks every boarder
  out and ends every lodging registration, so closure never strands an
  occupant. HousingAuthority.Occupy itself ends a person's current
  occupancy before recording a new one, so one person never holds two
  current beds through that path.

Genuine gap, documented as blocked rather than patched with an invented
parcel:

- BoardingLodgingBridge.RegisterHouse has zero production callers, so
  bootstrapped boarding houses get runtime rooms but no HousingAuthority
  building and spaces and no LodgingRegistry registration. Bridge
  CheckIn, the only path that grants a boarder a HousingAuthority
  occupancy right, therefore always refuses for bootstrapped houses
  ("not registered as lodging"). The runtime's own CheckInBoarder also
  has no production caller: boarding stays in production are recorded
  through population settlement flags, not through either check-in
  path, so runtime boarder registers are empty in live play and weekly
  dues settle only for boarders checked in through a path nobody calls.
  Fixing this by calling RegisterHouse from the bootstrapper would
  require a parcel id that BusinessInstanceState does not carry (it
  carries only AssignedBuildingId) and would create a second building
  record ("bld-boarding-" plus id) beside the business's assigned
  building. That mapping, plus the trigger rule for who boards when and
  at what rate, is a design decision. It is recorded here as
  outstanding, not silently wired.

HouseholdMoveService decision (A-10):

- The service stays caller-free in production by decision. Tests prove
  its guarantee (see below), but no approved gameplay path was found
  to require whole-household atomicity as its correctness condition.
  Live moves are single-person or single-space operations through
  HousingAuthority (acquisition occupancy, estate paths, absence
  sweep, construction occupancy), where the service's cross-person
  atomicity does not bite. The one whole-household candidate,
  acquisition closing occupancy, does not state whether the buyers
  relocate or take a second property, so routing it through the service
  (which ends all old occupancies) would change its semantics without
  a design decision. Routing it requires a relocation flag on the
  acquisition plan plus that decision, and is recorded here as
  outstanding.

Tests: 3 new Pass11Ws2HouseholdMoveTests proving the service guarantee
through its own API: a whole-household move ends old occupancies with
no dual housing rights and a recorded agreement, an over-capacity
commercial move is refused before any mutation with prior occupancies
intact, and unknown-space and unstated-arrangement moves are refused
with no mutation.

## Item 4 - Freight registration (A-25) - VERIFIED, one loudness fix

Verified live by source reading and test:

- FreightProductionDriver.EnsureCompany keys companies by business
  instance id and reuses the instance on a second call, so daily
  DriveFreightCompanies registration cannot duplicate a company.
  Registration is manager-driven (one runtime per live LiveryFreight
  business from DriveFreightCompanies), not scene-driven, and every
  live business runtime is registered with the driver on the same
  drive so each shipment payer resolves to its real till for verified
  collection, with unresolvable payers left uncollected and no legacy
  fallback. Save and load restores shipments exactly once through the
  service DTO, with dispatch plans re-derived from restored shipments'
  bound transport, so a mid-transport restore neither loses nor
  duplicates cargo. No second freight system was introduced.
- The Invalid-handler path was verified, not assumed. AdvanceTick
  guards every handler-gated step (Planned, Loading, Arrived,
  Unloading, undelivered Partial) on handler validity, so a carrier
  with no owner and no active employees stalls those shipments, while
  InTransit shipments still advance and revenue still collects only
  for accepted deliveries. The defect was silence: the stall produced
  no diagnostic. AdvanceTick now names the wait (count of gated
  shipments, why no handler exists, what still moves) with no behaviour
  change. The remaining Unity question from the audit (whether any
  scene-placed FreightCompanyRuntime instances exist independently of
  manager businesses) still needs G4 scene confirmation and is not
  answerable from source.

Tests: 3 new Pass11Ws2FreightHandlerTests (planned stall with
diagnostic and no phantom dispatch, in-transit advance under an
Invalid handler followed by a loud wait on arrival, no duplicate
company on repeated ensure).

## Item 5 - Named-buyer distress sales (A-11) - DOCUMENTED AS BLOCKED, honest refusal kept

Investigation result: no legitimate named-buyer source exists in
contracts for the WS3 distress-sale path, so the driver context keeps
BuyerHouseholdId -1 and sales keep refusing honestly. Nothing was
wired, because every candidate source fails on inspection:

- The business liquidation port (RegisterLiquidationPort) sells
  business formation assets to solvent business tills. It cannot
  supply a household buyer id, a parcel price, or note assumption
  consent, and its buyer is a business, not a household.
- HousingSearch negotiations are buyer-initiated purchases with a
  named buyer household, but there is no standing register of
  solvent, willing household buyers that a forced distress sale could
  draw on. Picking an arbitrary solvent household (for example the
  lowest-id household with cash) would be a forced-sale legal power
  that Canon does not grant, at a price nobody appraised.
- Tax-sale bids (TaxSaleBid) carry a bidder name string, not a
  household id, and no production code conducts a tax sale or produces
  bids, so they cannot feed the context either.
- Even with a buyer, the driver context sets SalePriceCents to zero,
  and TryBrokerSaleFromDistress refuses on a zero price. No appraised
  parcel sale price feed exists for the context, and no creditor
  consent source exists for note assumption.

What is missing, stated precisely so it can be decided rather than
invented: a solvent household buyer register with willingness evidence,
an appraised parcel sale price feed into the distress-sale context,
and a creditor consent source for note assumption. Until all three
exist, distress sales stay refused by decision, not by accident, and
the path must not appear in player-facing claims as available.

## Item 6 - Demand (A-07, GATE G1) - RESEARCH ONLY, nothing wired

No code was changed for RealDemandSource. No executor was built, no
retirement was performed, and no competing purchase authority was
created. The gate held.

What the source actually shows:

- RealDemandSource is a collection-side replacement for the retired
  town-pulse revenue path. CollectDemand builds PurchaseIntents from
  real household needs (HouseholdNeedRegistry, household ledgers) and
  registered business input needs, each naming a real buyer, a real
  stocked supplier at a valid price, and documented provenance.
  Collection never moves money or goods. BusinessInstanceSellerAdapter
  is read-only by design: its Sell method returns zero and records
  that execution goes through the sale authorities.
- The live purchase paths that already execute demand are the
  embodied shopping path (EmbodiedPurchaseExecutor, driven daily) and
  the general store sale authority, both routed through
  ProductionPathRegistry to exactly one path per business. The manager
  wrappers CollectRealDemand and RegisterBusinessInputNeed have no
  production caller outside the manager file. Intents are not
  persisted.
- The double-buying hazard is the central design fact. Household
  purchasing needs are already consumed by the embodied shopping loop.
  An intent executor that also satisfied those same needs would buy
  them twice unless intent consumption and shopping consumption share
  one exactly-once record. Business input needs registered by unwired
  runtimes would likewise need to be ignored until those runtimes are
  wired, or the executor would buy for businesses that do not run.

Decision needed from Kennedy, stated in the Kennedy section below.
The options are read model, input to the existing purchase executor,
replacement for an older demand path, or not yet necessary, and the
recommendation from this workstream is recorded there.

## Item 7 - Migration, mine closure, archetype outputs (capacity clause) - PLANS ONLY

Capacity did not remain after items 1 to 6, and each of these three is
WS2-scale routing work with dual-execution risk, so they are
documented as plans, not attempted:

- Migration (PASS8-006, A-28). LaborMigrationLinker evaluates only:
  it returns a MigrationEvaluation whose TravelTaskRequested flag is an
  intent, and its own documentation states the integrator maps accepted
  intents to journey tasks and that the linker moves nobody. The hub
  owns the linker and the drivers own OffMapJourneyAuthority, but no
  integrator maps intents to journeys in production. Plan: a scoped
  migration integrator driver that consumes accepted intents exactly
  once, checks contact awareness, housing fit and payback evidence
  from the evaluation record, creates the journey through
  OffMapJourneyAuthority, and records the outcome on the evaluation so
  a second drive cannot re-create the journey. Conservation test
  required: one intent, one journey, no duplicated person.
- Mine closure (PASS6-009, PASS7-003, A-26). MineOperations names
  closure as a lifecycle state, but no live driver runs closure stages
  through the mine production path, and closure touches the mineral
  reserve question that sits under decision gate G2. Plan: a mine
  lifecycle driver stage-gated on the existing production driver,
  with closure stages that stop production, settle outstanding ore
  shipments, and release labor and equipment, each stage persisted so
  a save mid-closure resumes rather than restarts. No closure work
  starts before the G2 mineral reserve decision.
- Archetype outputs beyond mine and farm (PASS9-011, A-30). Mine and
  farm run through the manager delegation pattern; every other
  archetype runs through the manager's archetype weekly operation, and
  the twelve detailed runtimes are registered unwired with a
  dual-execution flag. Plan: per-kind delegation through the manager
  pattern used for mine and farm, one kind at a time, retiring the
  archetype handling for the affected instances first so no business
  produces twice, with a conservation test per kind. No detailed
  runtime is wired beside its archetype handling in this pass.

## Kennedy decision section

The following text is drafted for Kennedy. It states the RealDemandSource
decision that gate G1 requires, plus the other decisions this pass
surfaced. No decision below has been taken in source.

RealDemandSource and what it should become

RealDemandSource today collects purchase intents from real household
needs and real business input needs, and it never buys anything. The
town already buys through two live paths, embodied shopping for
households and the general store sale authority for store sales. That
leaves four possible futures for the collector, and this workstream
needs Kennedy to pick one before any code moves.

Option one is to keep it as a read model only. It would report what
the town wants to buy, with no execution role, and the manager wrappers
would be documented as reporting only. This is safe and needs no new
money path, but it leaves a collector with no effect on play.

Option two is to feed its intents into the existing purchase executor.
Each intent would route through the same sale authorities that
embodied shopping uses, and each intent would be consumed exactly once
on execution so the same need cannot be bought twice. This is the
most useful future, but it needs the exactly once record shared
between intents and shopping, plus persistence for intents that
survive a day boundary, plus a rule for re-collecting after a failed
intent, plus ignoring input needs from unwired runtimes.

Option three is to replace an older demand path with it. No older live
demand path was found in source that it could replace, since the
town-pulse revenue path it replaced is already retired, so this option
looks empty unless Kennedy names a path this workstream missed.

Option four is to leave it unwired as not yet necessary. The town
already shops and sells through live paths, so nothing in play today
waits on the collector. This keeps the registry honest and costs
nothing, and the executor can be built later if a gameplay need for
it appears.

This workstream recommends option two if Kennedy wants demand
collection to affect play, and option four if no such need is pressing.
Option one is acceptable as a reporting step toward option two. Option
three should not be picked without naming the path it replaces.

Other decisions this pass surfaced

Construction commissioning trigger. The driver is live and advances
projects, but nothing in production decides which NPC households
commission a build, on what evidence, and with what funding check
beyond the driver's own affordability refusal. Kennedy or a design
owner needs to state that trigger rule before the queue is filled by
anything automatic.

Construction material ports. A yard or sawmill lot adapter, a catalog
asset, and a TitleAuthority parcel adapter are all still missing in
production. Each one is a small adapter with a real authority behind
it, and none of them may be faked with invented stock, prices, or
parcels.

Bank ledger consolidation. The live lender and BankRuntime hold
separate ledgers, and depositor deposits never reach the lender's
lendable figure. Kennedy needs to decide whether the bound BankRuntime
ledger becomes the lender's ledger at charter time, or whether the
two stay separate with the bound ledger serving display and leg
binding only. Until that decision lands, the binding registry must
not be wired into underwriting.

Boarding registration. A parcel mapping for boarding businesses and a
trigger rule for who boards when are needed before bridge registration
and check-in can go live. Without them, boarding stays are recorded
through population settlement flags only.

Distress sale buyer. A solvent household buyer register with
willingness evidence, an appraised parcel sale price feed, and a
creditor consent source are all missing. Until all three exist,
distress sales stay refused honestly.

Household move routing. A relocation flag on the acquisition plan is
needed before acquisition closing occupancy can be routed through the
atomic move service, since relocation and second property are
different moves and only relocation should end old occupancies.

## Harness sources and shim note (for the coordinator)

The harness sources.txt (harness directory, not repo-tracked) gained
these WS2 entries, in addition to the pre-existing Reporting entries
that were already there and were not removed:

- Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs
- Domains/Economy/Businesses/Construction/NpcCommissionedBuild.cs
- Domains/Economy/Businesses/Construction/NpcConstructionDriver.cs
- Domains/Economy/Businesses/Construction/ConstructionMaterialFreight.cs
- Domains/Economy/Businesses/Construction/ConstructionContract.cs
- Domains/Economy/Businesses/Construction/ConstructionContractBook.cs
- Domains/Economy/Businesses/Construction/ConstructionProgressBillingService.cs
- Domains/Economy/Businesses/Construction/IConstructionProgressBilling.cs
- Domains/Economy/Businesses/Construction/ConstructionBillingCashPort.cs
- Domains/Economy/Businesses/Construction/ConstructionProgressBillingPolicy.cs

The harness shim Shims/WorldExtracts.cs previously carried a verbatim
extract of IConstructionMaterialPricePort because the real file was
excluded from the subset. That duplicate definition was removed from
the shim when the real file entered the subset; the interface itself
is unchanged. The coordinator merge must carry both the sources.txt
additions and the shim change, or the merged tree will not build in
the harness.

## File ownership and conflicts

- No WS1 scenario files were touched. No WS3 tax, dispute, assay,
  offer, payroll, asset-sale, postage, or owner-reporting code was
  touched. No historically failing test was touched.
- Two minimal additive integration changes sit outside strict WS2
  ownership, both justified in their commit messages: one lazy hub
  property for the bank binding registry in
  Orchestration/Systems/SimulationSystemsHub.cs (no DTO, no behaviour
  change), and the feature-internal baseline classification for the
  construction cluster in
  Tests/Editor/Integration/Pass9RegistryAssemblyScanTests.cs (the scan
  guard's own documented classification route).
- No file-ownership conflicts were hit. No other worker's files were
  edited. No stash was used in any worktree.

## What was deliberately not claimed

- No Unity compilation, Test Runner, or PlayMode success is claimed.
  SimulationDrivers and SimulationSystemsHub changes are verified
  only by the full-repo Roslyn build described above.
- The construction driver is not claimed to be fully live in play:
  its labor and advancement legs are wired from hub authorities and
  run daily, but commissioning and completion wait on catalog,
  material, and parcel adapters that do not exist yet in production,
  and the commissioning trigger rule is undecided.
- The bank binding registry is not claimed to be wired to any scene
  object or underwriting path.
- Boarding bridge registration, distress sales, demand execution,
  migration, mine closure, and archetype delegation are not claimed
  as done. Each is documented above with its missing piece named.
