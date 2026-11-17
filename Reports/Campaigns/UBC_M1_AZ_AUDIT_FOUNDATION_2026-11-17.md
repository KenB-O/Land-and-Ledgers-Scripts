# UBC M1 A-Z Audit Foundation Slice 2026-11-17

Lane M1, workstream M, first slice. Scope is the foundation areas only, population,
households, property, business formation and ownership, retail. Base is integration
branch muse/universal-business-construction-2026-11-17 at f9e43292, harness 5029 passed
and 8 failed out of 5037 total. Every finding below was verified against code on disk
in this worktree. No speculative issues are listed. The 8 integration failures owned by
lane S3 are not duplicated here.

Status categories used below are implemented and production wired, partially wired,
source implemented without production caller, requires Unity scene or asset integration,
known defective, unverified, design incomplete, and no longer applicable.

## Headline

The foundation is mostly wired and running. Population demography, household daily
drivers, business formation both ways, retail, tenancy settlement, boarding weekly
settlement, and the housing authority all run in production. The one large dead area
is the household housing loop. Households open housing-search needs, and then nothing
ever answers them. The decision engine, the search executor, the acquisition service,
the move service, and the opportunity adapter are all implemented and tested, and none
of them has a production caller. Needs accumulate unresolved, NPCs can never buy, rent,
or board through the system, and distress sales can never complete for lack of a named
buyer. The repair in this package, M1-R1, wires the one leg of that loop that is safe
to run today, boarding, and leaves the rental and purchase legs to follow-up lanes
with their missing inputs named in findings F-M1-5 and F-M1-9.

## Confirmed defects

### F-M1-1 NPC property acquisition is dead in production

Status is source implemented without production caller.

Evidence is Domains/Economy/Financing/NpcPropertyAcquisition.cs line 675,
CloseAcquisition, and lines 231, 332, 510, NegotiatePrice, NegotiateContents,
ValidatePlan. No non-test file references the service type except a doc comment in
NpcContentsNegotiation.cs. Zero production callers for CloseAcquisition, ValidatePlan,
NegotiatePrice, and NegotiateContents.

Expected is that an NPC household that decides to buy can close a purchase through
the service, moving cash, title, contents, and occupancy through the real
authorities. Observed is that no production path ever builds a plan, validates it,
negotiates it, or closes it.

Reproduction is to grep the repo for CloseAcquisition outside Tests, which returns
only the definition, and to run any scenario and watch NpcPropertyLife distress
sales, which always record BuyerHouseholdId -1 and refuse for lack of a buyer.

Root cause is that the acquisition service was built as a standalone capability and
the driver that should call it was never written. The decision engine that would
produce its plans is itself unwired, see F-M1-2, so there is no plan source at all.

Impact is high. The property market has sellers and no NPC buyers, so distress sales
can never complete and no NPC household can ever become an owner-occupier through
the system.

### F-M1-2 Household housing decisions never run

Status is source implemented without production caller.

Evidence is Domains/World/Property/NpcPropertyDecisions.cs line 157, Decide. Zero
production callers. Only the static EstimateBuildCostCents is used, at 3 call sites
in NpcCommissionedBuild and NpcConstructionNeedEvaluator.

Expected is that a household with an open housing-search need gets a decision each
day, rent, buy, build, board, or keep searching, from real opportunities and real
cash. Observed is that Decide is never invoked, so no household ever evaluates
buying, renting, or building.

Reproduction is to grep the repo for NpcPropertyDecisionEngine outside Tests, which
returns only the definition and its test file, and to open a need in a live run and
observe that no decision diagnostic is ever logged.

Root cause is the same missing driver as F-M1-1. The engine needs an opportunity
source, a financing availability, a material price port, and a design catalog, and
no production code assembles those inputs or calls the engine. The opportunity
source itself is unwired, see F-M1-9.

Impact is high. It is the decision half of the dead housing loop.

### F-M1-3 Housing search execution never runs

Status is source implemented without production caller.

Evidence is Domains/World/Property/HousingSearch.cs line 484, HousingSearchExecutor
Execute. Zero production callers. Only callers are Decide, which is itself dead per
F-M1-2, and the test slice HouseholdVerticalSlicePhaseH.

Expected is that a resolved search decision moves the household, records the
agreement and occupancies in HousingAuthority, checks boarders in through the
boarding port, or delegates purchase and finance steps to the owning authorities.
Observed is that Execute is never invoked, so even a decision produced by hand
would never move anyone.

Reproduction is to grep the repo for HousingSearchExecutor outside Tests, which
returns only the definition and its call inside dead Decide.

Root cause is the missing driver. The executor is complete and safe, including
refusing unknown spaces, unwinding agreements when payment terms are refused, and
capacity-guarded commercial occupancy. It only lacks a caller.

Impact is high. It is the execution half of the dead housing loop.

### F-M1-4 Household move service is dead, and the planner duplicates its job

Status is source implemented without production caller.

Evidence is Domains/World/Property/HouseholdMoveService.cs line 35, MoveHousehold.
Zero production callers. Domains/Population/NewcomerSettlementPlanner.cs line 1108
has its own private TryMoveHouseholdToVacantHome, which sets household
homeBuildingId and person fields directly and never creates a ResidentialOccupancy
in HousingAuthority.

Expected is that moves go through one service that records occupancy in the housing
authority, so the household, its members, and the accommodation registry agree.
Observed is that the service is unused and the one live mover bypasses the registry.

Reproduction is to grep the repo for HouseholdMoveService outside Tests, which
returns only the definition, and to settle a newcomer household and inspect
HousingAuthority occupancies, which hold no record for it.

Root cause is that newcomer settlement was written as a direct state mutation
before the move service existed, and nothing was ever re-routed through the
service. See also F-M1-8.

Impact is medium. Households settle with a home building but no occupancy record,
so occupancy-based reads, sleeping-place accounting, and the absence sweep see a
different truth than the household record.

### F-M1-5 Rental options carry no rent, so the rental leg cannot be executed safely

Status is design incomplete, with a known defective outcome if executed.

Evidence is Domains/World/Property/HousingOpportunityAdapter.cs RentalListings,
which sets ProviderName, PayeeHolderId, Description, SpaceId, BuildingId,
Arrangement, and PlacesProvided, and sets neither RecurringMonthlyCents nor a real
payee unless scenario wiring supplies a landlordResolver, which nothing does.
AccommodationSpace, Domains/World/Property/HousingFoundation.cs line 49, has no
rent field. HousingSearchPlanner EvaluateOption, HousingSearch.cs, only checks
recurring cost when RecurringMonthlyCents is greater than 0, and sorts cheapest
first, so a zero-cost rental is treated as affordable and wins over priced options.
HousingSearchExecutor ExecuteDirectOccupancy creates the tenancy agreement and only
attaches payment terms when a payee is known and the cost is greater than 0,
otherwise it logs that the agreement carries no payment schedule and the rent loop
cannot charge it, and still executes the move.

Expected is that a rental names a real monthly rent and a real payee, so the rent
loop charges actual cash or books real arrears. Observed is that executing the
rental leg today would house households for free.

Reproduction is to build the adapter over a HousingAuthority with a vacant space,
plan a need, and execute the resolved rental, which yields an agreement with no
payment terms and an occupancy.

Root cause is that no rent source exists. Nobody sets rent on a space, no landlord
rent schedule exists, and the adapter cannot invent one. Rent-setting is a missing
feature, not a missing wire. It needs an owner decision on who sets rent and where
it is recorded before the rental leg can run.

Impact is high for the rental leg and zero for the boarding leg. The M1-R1 repair
therefore executes boarding only and refuses to plan rentals. The rental leg stays
blocked until a rent source is authored.

### F-M1-6 Boarding vacancies never exist in production

Status is source implemented without production caller, for the feeder half.

Evidence is Domains/Economy/Businesses/BoardingHouse/BoardingLodgingBridge.cs
line 61, RegisterHouse. Zero production callers. The bridge method calls
LodgingRegistry RegisterLodging at line 99, and the registry is read by the bridge
and by HousingOpportunityAdapter.cs line 119, but nothing ever populates it outside
the test slice. HousingOpportunityAdapter BoardingVacancies therefore always
returns an empty list in production.

Expected is that each registered boarding-house runtime publishes its rooms as
lodging registrations, so the housing search, migration planning, and employer
lodging all see real open beds. Observed is that the registration step was never
wired, even though the runtime registration was, see the wired section below.

Reproduction is to start a scenario with a boarding house, then inspect
hub LodgingRegistry AllActiveRegistrations, which is empty, and to grep the repo
for RegisterHouse outside Tests, which returns only the definition.

Root cause is that RegisterLiveBoardingHouseRuntimes registers the runtime and its
meal source but never calls the bridge, and SimulationDrivers
RegisterBoardingHouseRuntime binds the business but never calls the bridge either.
The bridge needs a parcel id, which is resolvable from the business premises plot
through the canonical PlayerTitleBridge ParcelKeyForPlot mapping, but nobody
passes it.

Impact is high. It is the feeder gap that makes the whole housing loop see zero
opportunities. The M1-R1 repair wires this call.

### F-M1-7 Open housing needs are lost on save and load

Status is known defective.

Evidence is Domains/World/Property/HousingSearch.cs, HousingSearchNeedRegistry
ExportState and ImportState, which exist and are correct. Orchestration/Systems
SimulationSystemsHub.cs CaptureSaveDto at line 1756 never calls ExportState, and
the load path never calls ImportState. The only hub references to the registry are
the field at line 1021, the property at lines 1422 to 1423, and the constructor
argument at line 1250 for HouseholdDecisionDriver.

Expected is that an open need survives a save and load round trip, like the
housing authority, the lodging registry, and the household ledgers do. Observed is
that a load starts with an empty need registry.

Reproduction is to open a need, save, load, and inspect hub HousingSearchNeeds,
which holds no open needs.

Root cause is that the registry was added to the hub without save wiring, and the
save DTO carries no needs slot. The DTO change must keep the harness shim
byte-identical, so this is a follow-up, not part of M1-R1.

Impact is low in practice. HouseholdDecisionDriver re-evaluates daily and re-opens
a need for any household still unsuitably housed, so a lost need is re-created the
next day. It is still a real gap, and it matters more once the drive in M1-R1
starts resolving needs, because a resolved need that is lost and re-opened could
re-trigger a search for a household that is already boarded. The drive guards
against that in two ways. It skips needs whose CurrentCondition is SuitablyHoused,
and it resolves a need without a second check-in when every living member already
holds a boarding agreement, see the repair section.

### F-M1-8 Newcomer settlement bypasses housing occupancy

Status is partially wired, authority divergence.

Evidence is Domains/Population/NewcomerSettlementPlanner.cs line 1108,
TryMoveHouseholdToVacantHome, which writes household homeBuildingId,
settlementArrangement, dwellingKind, and person homeBuildingId fields directly.
Domains/Population/SettlementFoundationReconciliation.cs is a bootstrap-time check
only, called from FirstLedgerSliceBootstrapper.cs line 1302, and creates no
occupancies. No runtime reconciler maps homeBuildingId to ResidentialOccupancy.

Expected is that a settled household holds an occupancy in HousingAuthority for
the space it sleeps in. Observed is that settled newcomers have a home building
and no occupancy record.

Reproduction is to settle a newcomer household and list HousingAuthority
CurrentOccupants for its building, which is empty.

Root cause is the same direct-mutation settlement path as F-M1-4. The fix is to
route settlement moves through HouseholdMoveService once the service is wired,
which is follow-up work, not part of M1-R1.

Impact is medium. It is the same divergence as F-M1-4 seen from the settlement
side. Sleeping-place accounting and the absence sweep read occupancies, so settled
newcomers are invisible to them until some other path occupies them.

### F-M1-9 No live opportunity source exists at all

Status is source implemented without production caller.

Evidence is Domains/World/Property/HousingOpportunityAdapter.cs, the production
IHousingOpportunitySource built from real facts, vacant spaces become rental
listings and actively registered lodging with genuinely open beds becomes
boarding vacancies. It is constructed nowhere in production. The only
IHousingOpportunitySource constructed outside tests is none. The test source
HousingOpportunityDirectory, HousingSearch.cs line 184, is test-only by design.
The adapter also returns empty lists for kin, employer lodging, shelter, purchase,
seller finance, lender, land, and relocation paths, by design, with a comment that
scenario wiring composes this adapter with sources that own those data. No such
composition exists in production.

Expected is that Decide and the planner receive a source built from the live
housing authority and lodging registry. Observed is that they receive nothing,
because they are never called, and the one real source is never built.

Reproduction is to grep the repo for new HousingOpportunityAdapter outside Tests,
which returns nothing.

Root cause is the missing driver. The adapter is complete for rental and boarding
discovery and honestly empty elsewhere. It is the source the M1-R1 drive builds.

Impact is high. It is the feeder half of the dead loop, alongside F-M1-6.

## Implemented and production wired

Population demography is wired. SimulationDrivers DriveDemographicDay at line
4526 calls PopulationManager DriveDemographicDay, which calls
DemographicDriverSchedule EvaluateDay and EvaluateMonth in
Domains/Population/FamilyFormationDrivers.cs lines 20 to 41, running BirthDriver,
MortalityDriver, MarriageDriver, MigrationDriver, and HouseholdSplitDriver.

Household daily drivers are wired. EvaluateHouseholdDay runs per household, meal
and sleep tick drivers tick at SimulationDrivers.cs lines 13612 and 13643,
ShoppingTripDriver AdvanceTick at line 13545, and PlanForHousehold at line 12849.

Housing needs are opened in production. HouseholdDecisionDriver opens a need at
HouseholdDecisionDriver.cs line 190 on the RelocateCheaper path, and the registry
is hub-owned at SimulationSystemsHub.cs lines 1422 to 1423. Needs feed
construction demand through HousingOpportunityAdapter UnmetSleepingPlaces, which
sums RequiredSleepingPlaces over open needs.

NPC entrepreneurship is wired weekly. DriveEntrepreneurshipWeek calls EvaluateWeek,
and premises come through TypedPremisesAssigner bound in BindNpcBusinessCreation
at SimulationDrivers.cs line 3887, through TryAssignNpcBusinessPremises.

Business formation is wired both ways. The player path goes through the presenter
to BusinessCreationAuthority, and the NPC path goes through
NpcBusinessCreationAdapter to the same authority. One authority, no parallel
formation path.

Retail is wired. RegisterGeneralStoreSupplier is called from
FirstLedgerSliceBootstrapper.cs lines 669 and 2501. EmbodiedPurchaseExecutor is
constructed at SimulationDrivers.cs line 7608 and used at line 7631. The store
supplier adapter registers the real store.

Tenancy settlement is wired. TenancyPaymentService SettleDueRents is called at
SimulationDrivers.cs line 5193. PlayerPropertyTaxDriver ResolveWeek is called at
AcquisitionMarketManager.cs line 7691.

The housing authority is wired. RegisterBuilding is called by NpcHomeConstruction
line 932 and BoardingLodgingBridge line 81. Occupy is called by NpcHomeConstruction,
HousingSearch, NpcPropertyLife, and NpcPropertyAcquisition line 988.

Boarding weekly settlement is wired. DriveBoardingHouseWeeks at
SimulationDrivers.cs lines 6176 to 6190 calls runtime ExecuteWeek and settles the
dues through hub RentCollection SettleDues with the household ledgers, so boarders
are charged real weekly board. The daily boarding drive runs at line 4651.

Boarding runtime registration is wired. FirstLedgerSliceBootstrapper
RegisterLiveBoardingHouseRuntimes at lines 2047 to 2093 creates a runtime per
authored BusinessType BoardingHouse business with authored bed capacity and
registers it through SimulationDrivers RegisterBoardingHouseRuntime at line 2995,
which binds the business and registers the runtime and its meal source. The dev
town configs carry a boarding-house business, for example
largedevelopmenttown-boarding-house in LargeDevelopmentTownConfiguration.asset.

The customer queue is wired. GeneralStoreRuntimeManager.cs lines 400 to 434 hold
one shared queue per manager, refreshed from the business served on every access,
bound into the Pass 9 save state at load and at loop build, with the patience-tick
watermark re-armed on restore. One general-store supplier is registered per run,
so one waiting line serves whichever store the shopping loop uses.

Save and load cover the audited drivers, with the one exception in F-M1-7.
HousingAuthority is saved at SimulationSystemsHub.cs line 1821 and loaded at line
1943. LodgingRegistry is saved at line 1822 and loaded at line 1944.
RentCollection is saved at line 1785 and loaded at line 1906. Boarding house
runtimes are saved at line 2053. Household ledgers export and import state through
HouseholdLedgerRegistry.

Lodging registration is partially wired. RegisterLodging is called by the bridge
at BoardingLodgingBridge.cs line 99, and registrations are read by the bridge and
by HousingOpportunityAdapter.cs line 119. The registration step itself is dead,
see F-M1-6.

## Partially wired and open items carried forward

The D-10 lodger path has no production driver call. The authored endowment stands,
Josh Saunders boards with the Kwints, and the bridge reads registrations, but no
household-need-driven lodger placement driver exists in production. Unchanged by
this slice.

The evaluator loan-awareness item stays dormant until lending terms and an
applicant-profile source exist. The NpcPropertyDecisionEngine financed-purchase
branch needs a financing availability source, a material price port, and a design
catalog, none of which is assembled in production. Unchanged by this slice.

Selling a business to Town leaves the store bound, per the A1 audit. Unchanged
by this slice.

## Repair M1-R1, boarding-only housing-search drive

The repair wires the smallest safe leg of the dead housing loop, boarding, end to
end, and leaves rental and purchase legs blocked on their missing inputs.

New harness-compiled driver class HousingSearchDrive in
Domains/World/Property/HousingSearchDrive.cs. It runs once per day over open
needs from HousingSearchNeedRegistry. For each need it resolves the household
from PopulationState, builds the composition through
HouseholdShelterSuitabilityEvaluator SummarizeComposition, reads cash from
HouseholdLedgerRegistry, builds the production source as a
HousingOpportunityAdapter over the live housing authority and lodging registry
wrapped in a boarding-only decorator, plans through HousingSearchPlanner, and
executes a resolved boarding decision through HousingSearchExecutor with the
boarding port resolved by business instance id. On success it marks the need
resolved. Needs with no affordable boarding option stay open. Documented income
is passed as zero, so affordability falls back to the planner cash runway rule,
one quarter of current cash, which is the planner own documented fallback. The
drive never plans rentals, so it can never create a rent-free tenancy per F-M1-5.
It never touches purchase, seller-finance, lender, land, or relocation paths,
which the adapter returns empty for in any case. Two guards keep the drive
honest. It skips needs whose CurrentCondition is SuitablyHoused, because those
households hold a tenancy they cannot afford, and boarding them without ending
that tenancy would double-house them. They wait for the rental leg. And it
resolves a need without a second check-in when every living member already holds
a boarding agreement, so a need re-opened after a save and load, see F-M1-7,
cannot double-book a bed.

Feeder wiring is part of the same repair. FirstLedgerSliceBootstrapper
RegisterLiveBoardingHouseRuntimes calls BoardingLodgingBridge RegisterHouse after
registering each runtime, with the parcel id resolved from the business premises
plot through the canonical PlayerTitleBridge ParcelKeyForPlot mapping. That
populates LodgingRegistry in production for the first time, which unblocks the
adapter boarding vacancies, and also unblocks migration and employer-lodging
consumers of those vacancies.

The daily call site is a one-line addition in SimulationDrivers, placed after the
boarding daily drive, calling a private DriveHousingSearchDay that gathers the
hub pieces and invokes HousingSearchDrive Drive. SimulationDrivers.cs and
FirstLedgerSliceBootstrapper.cs are harness-excluded, so those two call sites are
Unity-side, in the same position as the A1 repair. They need a Codex Unity
EditMode run before being called proven. The drive class itself, the boarding-only
source decorator, and the new tests are harness-compiled and verified by the
Roslyn harness in this package.

Regression tests in Tests/Editor/World/HousingSearchDriveTests.cs cover the full
chain at the unit level. A household with an open need and enough cash boards
through a registered lodging room, the need resolves, and the boarder port records
the check-in. A household with no affordable boarding option keeps its need open.
A household with a vacant space available but no boarding vacancy keeps its need
open and gains no occupancy, which pins the F-M1-5 guard that the drive never
creates a rent-free tenancy. A suitably housed need waits for the rental leg
with no check-in, which pins the double-housing guard. An already-boarded
household resolves its need with no second check-in. A feeder test registers
lodging through the registry and shows the adapter returning the vacancy with
its real rate and payee.

Follow-up lanes, in order, are a rent source so the rental leg can run, the
NpcPropertyDecisionEngine and NpcPropertyAcquisitionService wiring once purchase
listings and financing sources exist, the needs persistence slot per F-M1-7, and
routing newcomer settlement through HouseholdMoveService per F-M1-4 and F-M1-8.

## Files changed in this package

Reports/Campaigns/UBC_M1_AZ_AUDIT_FOUNDATION_2026-11-17.md, this document.
Domains/World/Property/HousingSearchDrive.cs, the new daily drive, boarding only.
Tests/Editor/World/HousingSearchDriveTests.cs, regression tests for the drive.
Orchestration/Systems/SimulationDrivers.cs, one-line daily call site.
Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs, feeder call in
RegisterLiveBoardingHouseRuntimes, plus the premises plot resolution it needs.
Domains/Economy/Core/SharedBusinessRuntimeManager.cs, the public
TryGetBusinessPremisesPlotId accessor the feeder resolves the plot through.
