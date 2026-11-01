# TECH-XIV — complete page-referenced extraction

**Authority:** exact accompanying original PDF; extraction is a search aid, not a replacement. **Pages:** 170.

**Known limitation:** paragraph ordering, multi-column tables, figures and typographic superscripts may require PDF inspection.


## TECH-XIV — original PDF page 1 of 170

                    LAND & LEDGERS
  TECHNICAL IMPLEMENTATION BIBLE - REVISION XIV
                     THE CROSSING Systems Integration
 Generic Business Authority, Universal Production, Contact Graphs, Delegated Operations, Transport Enterprise & Historical
                                              Event Configuration

                                            Revision date: October 7, 2026
                   Source baseline: Technical Implementation Bible Revision XIII - October 5, 2026
                       Canon compatibility target: Canonical Project Bible Revision XXXI
       Primary new source: THE CROSSING playtest integration and generic-business post-playtest synthesis - 2026
         Status: Active technical planning reference - repository evidence remains authoritative for implemented code

PRESERVATION GUARANTEE
Revision XIV preserves Technical Implementation Bible Revision XIII in full except where this integration explicitly
adds, deepens, corrects or supersedes technical guidance. The complete Revision XIII document follows this
integration unchanged. Repository evidence remains authoritative for what is actually implemented. Conceptual
record/type names below describe responsibilities and acceptance behavior, not proof that matching Unity classes
already exist.

PURPOSE
Revision XIV translates THE CROSSING into implementation architecture. The largest change is not another business
archetype. It is removal of remaining BusinessType permission logic in favor of one generic Business authority whose
current activities are derived from and executed through physical/economic state. The same pass also deepens
universal production, contact-graph hiring, manager autonomy, physical storage/transport, poultry policy, objective
ownership predicates and configurable historical-event injection.

REVISION XIV ARCHITECTURE LOCKS
 • Business is the legal/economic authority. BusinessOperation, CommercialCapability, BusinessCapabilityRegistry,
   profiles, tags and templates may be configuration/read-model/activity records only. They must never become
  replacement permission classes.
 • CreateBusiness creates identity/ownership/governance first. Available commerce and production derive from actual
  Persons, Tasks, ProductionMethods, equipment, inventory, FunctionalSpaces, rights, Agreements, information and
   time.
 • Ordinary production attempt feasibility may not require a specialist occupation/capability gate. Person capability
  modifies execution outcome. Task assignment still resolves Person response/agency separately.
 • ProductionMethod is object/method-centric and declares physical prerequisites. ProductionPolicy schedules real
  work through targets, maxima, input reserves and priorities; it does not add throughput.
 • Recruitment candidate lists are derived from legitimate contactability/evidence. No global hidden applicant pool or
  omniscient skill sheet is authorized.
 • Enterprise grouping and real reorganization are distinct workflows. A true merger transfers/assumes authoritative
   assets, liabilities, employees, Agreements and cash authority; a UI grouping does not.
 • Warehouse/storage, freight, ferry, drayage, forwarding and river transport remain distinct service/capacity records
   that may coexist under one Business.
 • Historical Events configuration gates authored DatedHistoricalEvent/Regime injection, defaults ON, persists with
  scenario/save state and does not suppress endogenous incidents.
 • Objective predicates must query authoritative ownership, operating state, cumulative evidence and scenario-defined
   equity; presentation shortcuts may not satisfy objectives.

Contents
PART I - GENERIC BUSINESS AUTHORITY & OPERATING STATE.......................................................................2
PART II - PRODUCTIONMETHOD, TASK EXECUTION & PRODUCTION POLICY.................................................2
PART III - CONTACT GRAPH, RECRUITMENT & CAPABILITY EVIDENCE............................................................3
PART IV - DELEGATION, MANAGER ACTIVITY & FLEXIBLE WORK.....................................................................4
PART V - PROPERTY, CONSTRUCTION, MILLING & VERTICAL INTEGRATION..................................................4
PART VI - FREIGHT, WAREHOUSE, FERRY & RIVER TRANSPORT......................................................................5

                                          LAND & LEDGERS  1

## TECH-XIV — original PDF page 2 of 170

PART VII - POULTRY LINEAGE & BIOLOGICAL PRODUCTION POLICY...............................................................5
PART VIII - HISTORICAL EVENT CONFIGURATION.................................................................................................6
PART IX - OBJECTIVES, VALUATION & RECONCILIATION....................................................................................6
PART X - REGRESSION & ACCEPTANCE FIXTURES..............................................................................................6
PART XI - IMPLEMENTATION PRIORITY...................................................................................................................7
PART XII - RESEARCH / CALIBRATION HOLDS.......................................................................................................8
PART XIII - REVISION XIV DEEPENING MAP............................................................................................................8

PART I - GENERIC BUSINESS AUTHORITY & OPERATING STATE

1.1 Business remains the source authority
TECH LOCK - One Business record/authority owns the legal/economic identity, ownership/governance, cash
relationship, debt links, employees/roles, property/use relationships, inventory ownership, contracts and operating
history. New activity models may not create parallel Business truth.

1.2 Revised CreateBusiness workflow
Conceptual workflow: CreateBusinessIntent -> Business Identity -> Ownership/Governance -> optional descriptive
Activity/Operation records -> real premises/asset/inventory/working-capital assignment -> delegated authority ->
Tasks/Agreements/Transactions -> derived operating state. No mandatory BusinessType selection is required for
ordinary enterprise formation.

1.3 Activity/operation records are never permission flags
TECH LOCK - BusinessOperation, CommercialCapability, BusinessCapabilityRegistry or successor records may group
UI, analytics, manager policy, opportunity reasoning or activity history. Their presence/absence must not by itself permit
or forbid an action that physical/economic/legal state otherwise supports.

1.4 Descriptive labels and templates
General Store, Blacksmith, Sawmill, Construction, Saddlery, Pharmacy and similar labels may be inferred/read-model
text or optional templates that preconfigure expected space/equipment/policies. A template materializes ordinary state;
it must not add hidden permissions.

1.5 Operating status is derived without Capability=true
Retain Forming, CapableButNotTrading, Operating, Suspended and Closed style read states if useful, but derive them
from actual people/time, space where required, stock/equipment, access/authority, executable tasks and
completed/active commerce. Do not replace BusinessType with one opaque Capability boolean.

1.6 Retail offer model
Retail availability resolves from inventory ownership/custody, allocation/reserve policy, a valid selling/fulfillment location
or channel, price/terms, labor/time and legal constraints. A storefront is one FunctionalSpace pattern, not a
RetailBusiness subclass.

1.7 Grouping versus reorganization
Enterprise/Division grouping remains a management/read-model relationship. Add or reuse an explicit
Reorganization/Merger workflow when assets, employees, Agreements, debts and cash authority truly transfer.
Creditor consent/assumption and predecessor provenance must persist.

PART II - PRODUCTIONMETHOD, TASK EXECUTION & PRODUCTION POLICY

2.1 ProductionMethod definition
A producible Item/Product/Asset references one or more ProductionMethods. Minimum conceptual fields where
relevant: output reference/quantity/quality model; input item/lot requirements; equipment/tool requirements;
workspace/location requirements; task/work sequence; labor/time estimate; concurrency; prerequisites; environment
tolerances; waste/scrap/byproducts; failure/rework/safety outcomes; provenance/version.

2.2 Feasibility resolution
TECH LOCK - Ordinary production feasibility checks physical prerequisites and current authority, not occupation class.
Missing forge, saw, stock, workspace, route, legal right or time can make a method unavailable. Missing expected
occupation/skill alone does not make the attempt mechanically impossible.

2.3 Person response is separate



                                          LAND & LEDGERS  2

## TECH-XIV — original PDF page 3 of 170

TaskAssignment -> PersonResponse remains distinct from ProductionMethod feasibility. A worker can Accept, Refuse,
Negotiate, Abandon, Escalate or otherwise respond under existing agency architecture. FeasibleByPhysics does not
imply WillPerform.

2.4 Execution outcome resolver
Resolve duration, quality, waste, failure, rework and safety from ActualCapability/experience evidence, task difficulty,
instruction/supervision, equipment condition, materials, fatigue, health, environment and time pressure. Avoid both hard
skill gates and flat SkillMultiplier shortcuts where causal inputs can be evaluated.

2.5 Shared resource reservation
Production Tasks reserve Persons, equipment stations, workspace and inputs. One forge/anvil/workbench/saw/wagon
cannot satisfy two incompatible simultaneous Tasks. Reservation history feeds workload and manager scheduling.

2.6 ProductionPolicy
Introduce/reuse standing per-product policy with fields such as Enabled, TargetStock, MaximumStock,
MinimumInputReserve, Priority, MakeToStock, MakeToOrder, AllowExternalSale, AllowInternalOrders and
override/escalation conditions. Policy schedules eligibility; actual throughput remains task/resource derived.

2.7 WorkOrder integration
Repair/fabrication WorkOrders reference desired output/repair target, ProductionMethod or task method, materials,
urgency, customer/internal owner, quote/terms and scheduled resources. The same WorkOrder lifecycle can serve
smithing, carpentry, wagon repair, building maintenance and similar operations.

2.8 Internal production and consolidation
Internal requisitions consume physical inventory and capacity. Intercompany charges remain optional
Agreement/Transaction policy. Consolidated reporting eliminates controlled internal revenue/receivable/payable while
preserving wages, external inputs, depreciation/maintenance where modeled, transport and receiving asset basis.

PART III - CONTACT GRAPH, RECRUITMENT & CAPABILITY EVIDENCE

3.1 ContactRelationship / Contactability read model
Derive recruitable/contactable Persons from existing relationship/evidence/message state. Useful fields/read outputs:
PersonId, known name/identity confidence, relationship path, referring Person/Business, available contact channel(s),
address/location evidence, last contact, interest evidence and privacy/unknown state where modeled.

3.2 ReferralLead
An employee/associate may produce a lead only when a real relationship supports it. Persist referrer, referred Person,
claimed relationship, job/property/business topic, contact method, date and evidence confidence. A referral may yield
no contact or an uninterested Person.

3.3 RecruitmentCampaign channels
Known Person contact, word of mouth, handbill/notice, local business/travel network, correspondence, messenger,
newspaper advertising and telegraph are RecruitmentCampaign/InformationMessage channels. They create
awareness/inquiries over time, not candidate spawn events.

3.4 CandidateEvidenceReadModel
For a target position/business, rank known relevant capabilities first, followed by other known evidence. Each displayed
reading references evidence provenance and confidence. Unknown values remain absent/Not enough information
rather than reading ActualCapability directly.

3.5 Confidence renderer validation
TECH LOCK - High confidence renders exactly one canonical band; Moderate exactly two adjacent bands; Low exactly
three adjacent bands; insufficient evidence renders Not enough information. Validation must reject a single exact band
paired with Moderate/Low confidence and must clamp ranges to Ordinary/Great/Excellent/Mastered.

3.6 EmploymentOffer
EmploymentOffer/Agreement should support wage/pay, housing/board, trial/probation, guaranteed/conditional status,
start date, duties/task expectations, manager authority, term/termination where material and communication channel.
Sending an offer through mail/message must respect message travel and response time.

3.7 Stranger interaction



                                          LAND & LEDGERS  3

## TECH-XIV — original PDF page 4 of 170

World selection of an unknown Person may expose interaction actions but must not automatically add full
identity/contact/skills. Introductions, conversation, observation and referrals create Knowledge/Evidence records that
later make the Person searchable/contactable.

PART IV - DELEGATION, MANAGER ACTIVITY & FLEXIBLE WORK

4.1 StandingInstruction semantics
Extend/reuse StandingInstruction/Policy to distinguish one-shot DoOnce, ongoing ManagerJudgment and persistent
OwnerLocked constraints. Names are conceptual; the technical requirement is semantic distinction and save/load
persistence.

4.2 Manager authority
Delegated managers may execute ordinary staffing, procurement, pricing within policy, maintenance, production
allocation, customer/service decisions, routine financing and property development within explicit thresholds. Authority
checks act on ordinary actions rather than routing everything through owner UI.

4.3 ManagerActivity log
Persist/report material autonomous decisions with actor, authority source, triggering evidence, action,
commitments/cash/resource reservations, result and escalation status. The log is an explanation layer over real events,
not a second simulation.

4.4 Flexible worker scheduling
Schedulers should match available Persons to compatible Tasks across activity families. A carpenter can shift from
internal construction to external repair; a smith from stock fabrication to customer repair. Occupation informs expected
fit/evidence but must not be the sole task-routing key.

4.5 Portfolio manager
A portfolio/general manager is an ordinary Person with delegated cross-business authority. Opportunity review,
property search, supplier negotiation and coordination consume time/information and must respect business-level
cash/ownership boundaries.

PART V - PROPERTY, CONSTRUCTION, MILLING & VERTICAL INTEGRATION

5.1 Generic construction operation
Construction uses Business + Person + Project/WorkPackage/Task + materials/equipment + Agreement authorities. Do
not require ConstructionBusiness class membership. Internal-project priority remains a policy over
schedules/reservations.

5.2 Mill/lumber production chain
Logs/sawlogs are physical inputs with source/rights. Milling consumes mill equipment, fuel/power, labor, time and
storage capacity to create lumber lots/byproducts. Transport between source, mill, yard and project remains
Shipment/Trip work. Lumber quality/dimension suitability is data, not guaranteed by Mill ownership.

5.3 Causal vertical-integration accounting
When controlled smith/mill/freight/construction operations serve a controlled property Project, calculate actual internal
resource consumption and eliminate artificial consolidated profit. Do not implement VerticalIntegrationDiscount or free-
input shortcuts.

5.4 Property options and seller willingness
Extend/reuse Agreement/PropertyInterest for purchase option, seller-financed parcel sale, easement and similar
arrangements. PropertySearch/Offer workflows query actual owners and willingness/evidence. Desired adjacency does
not make a parcel available.

5.5 Mixed-use spaces
Building -> FunctionalSpace -> UseRelationship remains controlling. Ground-floor commercial and upper residential
uses can be leased together or separately without the Building imposing a Business activity type.

5.6 Vacancy/reserve brake
Development automation evaluates vacancies, inquiry evidence, debt service, deployable cash, finance terms and
construction capacity. Repeat authority may commission familiar projects only when those facts remain inside policy
bounds.

5.7 Migration remains Person/residence derived

                                          LAND & LEDGERS  4

## TECH-XIV — original PDF page 5 of 170

Employment offer, housing offer and migration lead are separate records. Population changes only on actual
Person/Household arrival/residence stabilization according to scenario definition.

PART VI - FREIGHT, WAREHOUSE, FERRY & RIVER TRANSPORT

6.1 Composable TransportService activities
A single Business can expose freight haul, drayage, ferry crossing, warehouse/storage, forwarding and river-vessel
service through ordinary TransportService/Asset/Agreement records. Service composition must not be encoded as one
BusinessType enum.

6.2 Warehouse capacity
TECH LOCK - Cargo storage requires an actual storage FunctionalSpace/Asset with capacity, access, custody rules
and handling labor/equipment. A carrier without suitable storage may haul freight but may not silently accumulate
warehouse inventory.

6.3 Internal transport pricing
Internal portfolio tariffs can be zero/at cost/market/other policy. Every Trip/Shipment still reserves vehicle/animal/vessel,
worker and route capacity and records real input/wear costs. Consolidation removes only artificial intercompany
revenue/receivable/payable.

6.4 Reorganization fixture
A Freight + Ferry merger must create one successor authority only through a real transfer/assumption workflow.
Save/load must preserve predecessor history, transferred assets/employees/contracts and outstanding creditor rights
without duplicate ownership.

6.5 Charter/service-capacity agreements
Before owning a vessel, a Business may buy/charter cargo/passenger capacity from another carrier through Agreement
+ reservation + Shipment/Journey fulfillment. These transactions generate utilization/rate/demand evidence for later
investment decisions.

6.6 Owned river vessel
A powered river vessel is an Asset requiring crew roles, fuel/stores, maintenance, navigation/route state,
mooring/terminal access and operating schedule. Ownership alone does not create service. Vessel, ferry, wagon and
warehouse capacities remain separable.

6.7 Seasonal route state
River/ferry RouteEdgeState and TransportService must represent closures/degradation from ice, flood, wind or
navigation season. Existing reservations may be delayed, rerouted or canceled. No generic seasonal revenue scalar.

PART VII - POULTRY LINEAGE & BIOLOGICAL PRODUCTION POLICY

7.1 BreedingGroup / mating provenance
Deepen poultry state with explicit breeding-group membership or equivalent query, sire/dam/source lineage,
mating/incubation batch linkage and generation timing sufficient to avoid unintended close-line breeding when the
manager rotates sires.

7.2 Sire replacement policy
A StandingInstruction can require an unrelated replacement rooster before daughters from the current sire enter the
breeding group. Relationship/lineage queries determine eligible candidates; the policy cannot spawn a rooster.

7.3 Breeding versus commercial laying
Poultry manager policy may allocate mature hens among breeding, commercial laying and replacement roles.
Transition thresholds are authored/scenario policies, not global constants. Egg inventory allocation must respect eggs
intentionally set for incubation versus sale/consumption.

7.4 Male disposition
Male chicks/cockerels persist through juvenile state and final disposition. Sale/slaughter/retention create ordinary
inventory/animal events. Do not auto-delete non-layers.

7.5 Biological accounting
Biological growth changes physical state first. Cash/equity recognition follows actual sale/transfer and the selected
accounting/read-model policy. Do not post market-value profit merely on hatch or maturation unless a specifically
defined valuation report is being computed.

                                          LAND & LEDGERS  5

## TECH-XIV — original PDF page 6 of 170

PART VIII - HISTORICAL EVENT CONFIGURATION

8.1 ScenarioHistoricalEventSettings
Add/reuse scenario configuration with HistoricalEventsEnabled default true plus optional EventOverride map keyed to
authored event/regime IDs where supported. Persist settings in scenario save metadata and expose them before
scenario start or through explicitly supported rules.

8.2 Authoring event gate
TECH LOCK - DatedHistoricalEvent/HistoricalRegime injection checks scenario configuration before applying authored
causal inputs. Disabled authored events do not mutate route, credit, migration, crop, animal or other state merely
because the date is reached.

8.3 Endogenous incidents remain separate
EnvironmentalIncident, lender failure, local demand collapse, disease, fire, flood and similar simulation-generated
events remain possible when HistoricalEventsEnabled=false unless a separate simulation option explicitly changes
them. The historical toggle is not a GlobalAdversity flag.

8.4 No retroactive rewrite
Changing the setting after an event has already injected state should not silently undo transactions, deaths, route
changes or agreements. Require restart/replay or an explicit scenario-authorized migration workflow if runtime changes
are allowed.

PART IX - OBJECTIVES, VALUATION & RECONCILIATION

9.1 Ownership-aware objective predicates
ObjectivePredicate should query beneficial/controlling ownership according to authored semantics. Independent NPC
Business count must exclude disallowed player ownership/control; 50/50 joint objective must inspect actual percentage
and operating Business state.

9.2 Event/current/maintained semantics
Retain event/current/maintained distinction. Cumulative freight/passenger and first-business/first-household milestones
persist once supported by evidence; maintained ownership/population predicates reevaluate when wording requires
current state.

9.3 Operating Business objective
Creating Business identity or finishing a building cannot satisfy OperatingBusiness. The predicate derives from actual
executable/service state plus commerce according to Canon XXXI 2.5.

9.4 Scenario equity definition
ScenarioDefinition should enumerate included asset/liability categories or reference a versioned EquityPolicy. Starting
supplied assets must receive consistent treatment. Estimated market/appraised values and book/cost values must not
be mixed silently.

9.5 Ledger corrections
Introduce/reuse explicit Adjustment/Reconciliation transaction/event with reason, source evidence, affected authority,
original/corrected interpretation and timestamp. A display bug or transcription error cannot be repaired by rewriting
historical cash/inventory without provenance.

9.6 Land valuation read models
Book basis, estimated market value, appraised transferable value and realized proceeds are separate read
models/events. Objective configuration selects which one counts. Appreciation must not post cash or revenue.

PART X - REGRESSION & ACCEPTANCE FIXTURES
 Fixture                                                       Required result
 GENERIC BUSINESS                                              Create Business with no hard type. Add shelves + purchased goods
                                                             and confirm retail can operate; add forge/resources and confirm
                                                                         smithing tasks can operate without changing Business class.
 NO OPERATION PERMISSION FLAG                                Remove/alter descriptive BusinessOperation record while physical
                                                                              state remains. Confirm feasible Tasks/transactions do not disappear
                                                                               solely because the descriptive record changed.
 NOVICE AT FORGE                                                Assign a low-evidence/non-smith Person to a physically supported
                                                                      simple forging task. Confirm task can be attempted, with
                                                                    outcome/time/waste determined causally rather than blocked by
                                                                         occupation.

                                          LAND & LEDGERS  6

## TECH-XIV — original PDF page 7 of 170

 Fixture                                                       Required result
 WORKER REFUSAL                                               Assign physically feasible but unsafe/out-of-contract work. Confirm
                                                              PersonResponse may refuse despite method feasibility.
 PRODUCTION TARGET                                          Enable nails target 75/max 100 and customer-order priority. Confirm
                                                                        stock work pauses at target/max and resumes after consumption while
                                                                            external jobs reserve the same forge/worker.
 CONTACT GRAPH                                                 Create a qualified Person with no known path and confirm Add
                                                             Employee does not list them. Add referral/address evidence and
                                                                        confirm they become contactable.
 LETTER OFFER                                            Send employment offer by mail. Confirm no immediate acceptance/hire
                                                                                            until message arrival and Person response.
 CAPABILITY RANGES                                            Generate High/Moderate/Low readings and verify one/two/three
                                                                       adjacent canonical bands only.
 GENERIC CONSTRUCTION                                        Generic Business with carpenter/tools builds an internal project and
                                                                                       later performs external repair without class switch.
 LUMBER CHAIN                                          Move logs -> mill -> lumber lot -> project. Confirm hauling, mill time,
                                                                                          fuel, storage and lumber suitability remain real; no free lumber from mill
                                                                      ownership.
 WAREHOUSE REQUIRED                                                Carrier without storage cannot hold staged cargo beyond valid
                                                                         vehicle/temporary custody. Add warehouse and confirm
                                                                           storage/consolidation capacity appears from physical space.
 TRANSPORT MERGER                                           Reorganize ferry + freight into one successor. Confirm
                                                                           assets/debt/contracts transfer once, predecessors retain history and no
                                                                            duplicate cash/assets exist.
 CHARTER BEFORE VESSEL                                  Buy third-party river capacity, record actual shipments/passengers and
                                                                                         utilization evidence, then evaluate owned-vessel purchase without
                                                                spawning demand.
 POULTRY SIRE ROTATION                                      Breed daughters from sire A, then enforce policy requiring unrelated
                                                                                    sire before daughters enter breeding group. Confirm no ineligible sire is
                                                                         selected and no bird is spawned.
 HISTORICAL EVENTS OFF                                           Disable authored Panic of 1873. Confirm no Panic-authored credit/rail
                                                                            inputs inject, while an independently generated lender failure can still
                                                                          occur.
 HISTORICAL EVENTS ON                                        Enable Panic fixture. Confirm only causal input state changes first; no
                                                                                   direct revenue/price/business-failure percentage is posted.
 INDEPENDENT BUSINESS OBJECTIVE                             Create five player subsidiaries and two NPC firms. Confirm a five-
                                                                     independent-business objective reads 2, not 7.
 STARTING ASSET EQUITY                                          Provide scenario-start horse/tack and test EquityPolicy include/exclude
                                                                      behavior remains consistent across save/load/reporting.
 LEDGER CORRECTION                                               Introduce a known price transcription error and correct via explicit
                                                                                 reconciliation event; confirm audit trail and downstream balances
                                                                               reconcile.
 SAVE LOAD                                                       Save/load during active recruitment mail, production queue,
                                                                             reorganization, poultry incubation, historical-event configuration and
                                                                                       river charter. Confirm no duplicate Persons, Tasks, inventory,
                                                                              obligations or event injections.


PART XI - IMPLEMENTATION PRIORITY

11.1 P0 - authority and hard-gate removal
 • Audit and remove BusinessType/BusinessOperation/CommercialCapability checks that act as authoritative action
  permission gates. Preserve them only where they are descriptive/configuration/read-model data.
 • Audit task feasibility for specialist-capability hard gates. Ordinary production must route through ProductionMethod
   prerequisites plus Person execution outcome, with worker agency separate.
 • Preserve one Business, cash, inventory, Person, Property and Task authority. No generic-business refactor may
   introduce duplicate truths.
 • Implement/validate contactability and evidence-limited recruitment so UI cannot expose global hidden applicant/skill
   state.
 • Keep reconciliation/save-load idempotence as release gate while migrating business/production state.

11.2 P1 - production policy, delegation and transport composition
 • ProductionPolicy targets/maxima/input reserves/priorities integrated with task scheduling and shared reservations.
 • Manager Activity and standing-instruction semantics sufficient for zero-owner-click ordinary operation.
 • True enterprise reorganization distinct from portfolio grouping.
 • Warehouse/storage capacity, charter capacity and composed transport services under one Business.
 • Objective ownership filters and scenario EquityPolicy.

                                          LAND & LEDGERS  7

## TECH-XIV — original PDF page 8 of 170

11.3 P2 - long-run development and historical configuration
 • Property options/land acquisition, generic construction/milling chains and vacancy-aware repeat development.
 • Poultry breeding groups/sire-rotation policy and production-role allocation.
 • HistoricalEventsEnabled/EventOverride UI, persistence and regression coverage.
 • Contact-graph UX that shows how a Person is known and which channels can reach them.

PART XII - RESEARCH / CALIBRATION HOLDS
 • Exact schema/class names remain repository-audit dependent; this revision specifies authority and behavior, not
  mandatory Unity naming.
 • Production quality/time/waste/injury curves by craft, equipment and worker evidence remain calibration datasets.
 • Historical trade licensing and legal constraints remain LegalRegime data; universal physical attemptability does not
  erase law.
 • Exact employee contract forms, guaranteed-work terms, employer lodging and communication costs/latencies by
   period/place.
 • Exact small-mill productivity, log/lumber quality, dock/warehouse design, ferry/river vessel costs and crew
  economics.
 • Exact poultry breeding/laying/mortality curves and commercially rational flock-policy thresholds.
 • Exact event severity/probability and per-event configuration catalog remain authoring data.
 • Exact owner-equity valuation method remains scenario configuration unless a future Canon revision standardizes
  one.

PART XIII - REVISION XIV DEEPENING MAP
 • Technical XIII/IX CreateBusiness and operating-status architecture is deepened into a fully generic Business
   authority. Preserved "business type" references are superseded wherever they act as permission logic.
 • Technical construction/task architecture is deepened so ProductionMethod physical prerequisites and Person
  execution replace specialist capability as a universal ordinary-production gate.
 • Preserved Person response states remain authoritative: physical feasibility and employee willingness are separate.
 • Recruitment Channels and referral graph are deepened into an explicit contactability/evidence read model used by
  Add Employee and related lead workflows.
 • Capability evidence rendering is hardened with structural validation for one/two/three adjacent-band confidence
   output.
 • Delegation/Full Autonomy is deepened with standing-instruction semantics and ManagerActivity explanation over real
  events.
 • Property/construction/logistics architecture is deepened with generic construction/mill examples, warehouse physical
   capacity and causal vertical-integration accounting.
 • Route/TransportService architecture is deepened with composed ferry/freight/dray/warehouse/forwarding/river
   activities, chartered capacity and actual reorganization workflow.
 • Animal state is deepened with breeding-group/sire-rotation policy and commercial-laying allocation.
 • HistoricalRegime/DatedHistoricalEvent architecture is deepened with per-scenario Historical Events configuration,
   default ON, without creating a parallel event economy.
 • Objective/reconciliation architecture is deepened with ownership-aware independent-business predicates, scenario
   EquityPolicy and explicit ledger-correction provenance.
END OF REVISION XIV INTEGRATION
The preserved Technical Implementation Bible Revision XIII follows unchanged and remains authoritative except where
Revision XIV explicitly adds, deepens, corrects or supersedes technical guidance.





                                          LAND & LEDGERS  8

## TECH-XIV — original PDF page 9 of 170

                    LAND & LEDGERS
    TECHNICAL IMPLEMENTATION BIBLE - REVISION XIII

                        The Market Town Systems Integration
Portfolio Treasury, Rental Development, Private Banking, NPC Venture Formation, Health, Demography & Autonomous QA

                                         Revision date: October 5, 2026
                 Source baseline: Technical Implementation Bible Revision XII - October 5, 2026
                     Canon compatibility target: Canonical Project Bible Revision XXX
      Status: Active technical planning reference - repository audit remains required before implementation claims

PRESERVATION GUARANTEE

Revision XIII preserves Technical Implementation Bible Revision XII in full except where this integration explicitly adds,
deepens, corrects or supersedes technical guidance. The complete Revision XII document follows this integration
unchanged. Repository evidence remains authoritative for what is actually implemented. Conceptual types below are
target responsibilities, not proof that matching Unity classes already exist.

PURPOSE

Revision XIII translates the MARKET TOWN playtest into implementation architecture. Revision XII already provides
historical-regime, property-right, credit, information, distress and environmental-shock foundations. Revision XIII con-
centrates on newly exposed implementation needs: protected liquidity/read models, owner cash sweeps, autonomous
rental development, third-party loan placement, deposit-bank policy, intentional illegality, long-run NPC venture formation,
pharmacy/health chains, generational demography and strict playtest checkpoint provenance.

REVISION XIII ARCHITECTURE LOCKS

 • Earmarked/protected cash is metadata/read-model state over one authoritative cash balance; it must never be duplicated
   into a second treasury.

 • Owner distributions, capital contributions and intercompany loans are distinct transaction types with different consolidated
   treatment.

 • Rental development is a chain of real Property, Project, Finance, Construction, Lease and Migration state; no BuildRental
  command may directly create residents or profit.

 • Controlled internal construction can create capitalized cost at the receiving asset while consolidated internal rev-
   enue/profit is eliminated.

 • Third-party funded loans/notes remain owned by the outside lender. Servicing/placement relationships do not clone
   principal into the player’s balance sheet.

 • Deposit accounts create bank liabilities; bank liquidity and loan maturity/concentration must be queryable from authorita-
   tive instruments.

 • Bank policy is a structured set of authorities/limits, not one risk scalar. Intentional unlawful policy/actions should pass
  through LegalRegime and evidence/enforcement workflows when technically possible.

 • NPC venture formation consumes the same opportunity, property, capital, labor, supplier and information authorities as
   the player.

 • Health events link Person availability, treatment tasks, medical providers, pharmacy inventory/dispensing and outcome
   history.

 • Long-run demography re-evaluates actual Persons for work, relationships, marriage, household formation, fertility, birth
  and death; population remains derived from Person/residence truth.

 • Autonomous QA must preserve checkpoint ancestry and reconciliation status. Invalid or soft-continuity branches cannot
   silently become authoritative exact state.

Contents


PART I - PORTFOLIO TREASURY & PROTECTED LIQUIDITY                                            4




                                          LAND & LEDGERS  1

## TECH-XIV — original PDF page 10 of 170

   1.1 ProtectedLiquidity read model   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   1.2 Owner distribution and capital contribution .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   1.3 Standing capital-allocation policy  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4


PART II - RENTAL PORTFOLIO & DEVELOPMENT PIPELINE                                            4

   2.1 ManagedProperty / property-level economics  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   2.2 DebtServiceReservePolicy   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   2.3 DevelopmentCandidate and repeat authority   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   2.4 TenantRecruitmentCase .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   4

   2.5 Vacancy brake  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   2.6 Rental-first disposition  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5


PART III - CONTROLLED CONSTRUCTION & CAPITALIZATION                                          5

   3.1 Internal-project priority .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   3.2 Internal transfer and consolidation   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   3.3 Duration decomposition  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5


PART IV - FINANCE OFFICE, BANKING HOUSE & BANK POLICY                                        5

   4.1 Institution capability progression   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   4.2 ThirdPartyLoanPlacement .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   4.3 DepositAccount / deposit liability  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   4.4 BankPolicyProfile    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   5

   4.5 LoanPortfolioExposureReadModel  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6

   4.6 CorrespondentAccount   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6

   4.7 Bank panic / withdrawal stress   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6

   4.8 Connected lending and consolidation   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6

   4.9 Intentional unlawful bank action .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6


PART V - NPC VENTURE FORMATION & COUNTERPARTY NETWORKS                                  6

   5.1 OpportunityPressure -> VentureDevelopment  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   6

   5.2 BusinessFormationCase    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   5.3 Counterparty graph   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7


PART VI - HEALTH, TREATMENT & PHARMACY ARCHITECTURE                                        7

   6.1 HealthEpisode / InjuryEvent .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   6.2 TreatmentCase  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   6.3 Pharmacy dispensing and retail    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   6.4 Work availability   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   6.5 Barber versus dental capability  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7


PART VII - DEMOGRAPHIC MATURATION & HOUSEHOLD FORMATION                                  7

   7.1 Periodic life-stage review   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   7

   7.2 Youth employment  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   7.3 RelationshipFormationCase .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   7.4 Marriage and household split  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   7.5 Birth and death .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8


PART VIII - WORKLOAD-DRIVEN BUSINESS SCALING & MIXED-USE SPACE                              8

   8.1 Demand propagation   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8



                                          LAND & LEDGERS  2

## TECH-XIV — original PDF page 11 of 170

   8.2 Supplier relationships between NPC and player businesses  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   8.3 Mixed-use professional building .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   8.4 Butcher business profile  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8


PART IX - AUTONOMOUS PLAYTEST CHECKPOINT & PROVENANCE                                    8

   9.1 PlaytestCheckpoint    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   9.2 Reconciliation status .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   8

   9.3 Long-run export additions  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   9


PART X - SCENARIO FIXTURES                                                                    9

   10.1 MARKET TOWN long-run fixture   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   9

   10.2 A TOWN FROM NOTHING / THE PROMOTER fixture   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   9


PART XI - REGRESSION & ACCEPTANCE TESTS                                                     9


PART XII - IMPLEMENTATION PRIORITY                                                            10

   12.1 P0 - authority and reconciliation .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  10

   12.2 P1 - property, banking and long-run autonomy .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  10

   12.3 P2 - health, pharmacy and demographic maturation   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  10


PART XIII - RESEARCH / CALIBRATION HOLDS                                                     11


PART XIV - REVISION XIII DEEPENING MAP                                                        11





                                          LAND & LEDGERS  3

## TECH-XIV — original PDF page 12 of 170

PART I - PORTFOLIO TREASURY & PROTECTED LIQUIDITY

1.1 ProtectedLiquidity read model

Introduce or extend a read model that derives cash required by known near-term obligations and owner/manager policies.
Conceptual output:

 • AuthoritativeCash;

 • RequiredNearTermObligations;

 • PolicyReserveTarget;

 • CommittedProjectCash where specifically earmarked;

 • ProtectedCash = max/union according to non-duplicative rules;

 • DeployableCash = AuthoritativeCash - ProtectedCash.

TECH LOCK - ProtectedCash never posts a transfer. It is classification over the one authoritative cash balance. Two
 overlapping obligations may not double-count the same future need unless both payments are genuinely required.

1.2 Owner distribution and capital contribution

Use explicit transaction/event families for BusinessDistribution and OwnerCapitalContribution. A sweep workflow should:

1. calculate business deployable cash after real obligations;

2. create an authorized distribution from business cash to owner cash;

3. optionally create a separate owner capital contribution into the target business;

4. preserve timestamps and source/target entity IDs;

5. eliminate the movements from consolidated revenue/profit while retaining entity-level cash history.

Do not represent this as Sale revenue, miscellaneous income or a hidden intercompany loan.

1.3 Standing capital-allocation policy

Extend Policy/StandingInstruction with reusable portfolio scopes: reserve floor, sweep destination, excluded businesses,
reinvestment mandate and escalation conditions. MARKET TOWN requires the policy “sweep mature operating businesses
to finance, exclude Rental Holdings.” Policy execution remains manager activity over real cash.

PART II - RENTAL PORTFOLIO & DEVELOPMENT PIPELINE

2.1 ManagedProperty / property-level economics

Rental Holdings should maintain or derive per-property: PropertyId, units/spaces, tenants, leases, rent receivables, debt
links, tax/insurance obligations where applicable, condition, maintenance work, vacancy history, property-level operating
cost and construction/improvement basis.

2.2 DebtServiceReservePolicy

A property policy may designate cash protection for the next debt payments. Store the policy/obligation references and
computed amount, not a second balance. Reports should expose cash and available cash together.

2.3 DevelopmentCandidate and repeat authority

A delegated property manager can create Investigation -> PropertySearch -> Inspection/Diligence -> FinancingQuote ->
Acquisition -> ProjectCommission tasks inside approved bounds. Candidate scoring is a read model over actual facts such
as site size, access, drainage/water, price, financing, expected household fit and existing vacancies; it cannot manufacture
a parcel.

2.4 TenantRecruitmentCase

Represent recruitment/letting as a real funnel linked to one or more available/upcoming AccommodationSpaces: Aware-
ness/Lead -> Inquiry -> HouseholdFit -> Income/EmploymentEvidence -> Offer -> Acceptance -> Travel -> Arrival ->
Lease/Occupancy -> Stabilization/Departure. An ExternalPriority policy changes ordering/effort but never guarantees a
household.





                                          LAND & LEDGERS  4

## TECH-XIV — original PDF page 13 of 170

2.5 Vacancy brake

Development automation should evaluate existing vacant supply, inquiry quality, debt-service reserve, financing headroom
and construction capacity before starting another project. A vacancy can be outweighed by strong evidence, but the system
should never interpret an owner reinvestment mandate as unconditional construction.

2.6 Rental-first disposition

Property policy may prefer HoldForRent while still exposing a Sale action when a real buyer exists. Tenant Employment,
Lease, Construction labor and Property ownership are separate relationship authorities.

PART III - CONTROLLED CONSTRUCTION & CAPITALIZATION

3.1 Internal-project priority

Extend project/crew scheduling so a construction business can prioritize owner-controlled Projects while accepting external
WorkOrders/Contracts in spare capacity. Priority changes reservations/queue ordering; it does not grant extra labor hours.

3.2 Internal transfer and consolidation

At business level, Construction may issue an internal requisition/charge so its own cost accounting remains usable.
Consolidated reporting must eliminate controlled internal revenue and receivable/payable pairs while preserving:

 • wages actually paid;

 • materials consumed;

 • freight/hauling;

 • external specialist cost;

 • overhead allocation where accounting policy supports it;

 • receiving asset’s capitalized project basis.

3.3 Duration decomposition

Project scheduling should continue to distinguish ActiveWork, ElapsedTime, LeadQueueTime and InterruptionTime.
Repeated designs may reduce planning/error probability through experience evidence, but they never bypass actual work
packages. Use the Historical Work, Construction & Infrastructure Calibration Reference rather than one BuildingTypeDura-
tion table.

PART IV - FINANCE OFFICE, BANKING HOUSE & BANK POLICY

4.1 Institution capability progression

Represent financial institution capability separately from the business name. A business can expose combinations of Ow-
nAccountLending, NoteDiscounting, LoanPlacement, Collections, ExchangeRemittance, Safekeeping and DepositTaking
according to dated LegalRegime, staff, premises/equipment and policy. Do not require a single linear upgrade enum if
composition works better after repository audit.

4.2 ThirdPartyLoanPlacement

Add/reuse an agreement/workflow linking Borrower, OutsideLender, Originating or servicing business, principal, instrument,
fees, servicing duties, payment routing, default/workout authority and retained risk if any.

TECH LOCK - The loan asset belongs to the true lender. The servicing business records only its actual fee
 receivable/cash and any contractually retained exposure.

4.3 DepositAccount / deposit liability

Deposit-taking requires a persistent customer-bank relationship. Minimum conceptual state: AccountId; Depositor
endpoint; institution; product/type; balance; transaction history; withdrawal/notice rules; interest/fees where supported;
status; legal/regulatory metadata; beneficiary/joint handling where later supported. The account balance is a bank liability
and depositor asset/claim. Cash deposited increases bank cash and deposit liability simultaneously, not bank equity.

4.4 BankPolicyProfile

A bank’s delegated/personal control surface should be composable from policy records:




                                          LAND & LEDGERS  5

## TECH-XIV — original PDF page 14 of 170

 • target liquidity and correspondent balance;

 • minimum operational cash and stressed-withdrawal buffer;

 • rate/pricing matrix by loan/product/risk where lawful;

 • collateral and appraisal rules;

 • maximum exposure per borrower/household/group;

 • concentration limits by sector, node, collateral family and maturity;

 • connected-party lending limits/approval path;

 • maturity preferences and renewal policy;

 • delinquency/workout/enforcement policy;

 • dividend/retained-earnings/capital policy;

 • deposit product and withdrawal terms;

 • manager/loan-officer approval thresholds;

 • staffing/reconciliation/audit controls.

The UI can provide presets, but presets must materialize into these ordinary policy values.

4.5 LoanPortfolioExposureReadModel

Provide aggregate views by borrower, connected group, purpose, sector, geography, collateral family, maturity bucket,
delinquency stage and rate. Read-model totals reconcile to authoritative Loan/FinancialObligation objects.

4.6 CorrespondentAccount

Represent correspondent balances and services as real Institution/Agreement/Account relationships where material.
Correspondent cash is liquid bank asset but may have transfer latency, fees, limits or disruption.  It is not an infinite
emergency line.

4.7 Bank panic / withdrawal stress

A panic fixture should create actual withdrawal requests against DepositAccounts. Resolution consumes immediately
available cash/correspondent capacity, may accelerate asset sale/borrowing/capital calls and can force suspension/failure
under dated rules. Reputation can affect withdrawal propensity but cannot satisfy withdrawals by itself.

4.8 Connected lending and consolidation

Connected-party loans remain authoritative at bank and borrower ledgers. Add ConnectedParty metadata/evidence for
policy, disclosure and concentration. Consolidated owner reporting eliminates the intercompany asset/liability pair but bank
solvency views must still include the loan because depositor liabilities remain external.

4.9 Intentional unlawful bank action

TECH LOCK - The action system should be able to represent a player explicitly ordering conduct that the current
 LegalRegime marks unlawful when the action is otherwise physically possible. Legality validation should return
 AllowedLawful, AllowedButUnlawful/Risky, or ImpossibleByAuthority/Physics rather than collapsing every illegal act
 into disabled UI.

An unlawful action/event should record actor, grantor/order, institution, policy/action, applicable legal rule, evidence trail
and concealment/disclosure state where relevant. Manager response can include comply, refuse, escalate, resign or report
according to capability, loyalty, ethics, incentives and legal exposure. Enforcement remains a later causal workflow; no
generic CrimeMeter is authorized.

PART V - NPC VENTURE FORMATION & COUNTERPARTY NETWORKS

5.1 OpportunityPressure -> VentureDevelopment

Deepen the existing Opportunity & Pressure read model so qualifying NPC owners/managers can consume a pressure
object as evidence for ordinary VentureDevelopment tasks. The chain can include investigation, premises search,
capital/credit, supplier setup, recruitment, licensing, construction/fit-out and opening.





                                          LAND & LEDGERS  6

## TECH-XIV — original PDF page 15 of 170

5.2 BusinessFormationCase

Where current architecture needs explicit persistence, use a formation workflow referencing prospective owner(s), business
concept/profile, evidence/opportunity source, required property/space, capital stack, staff/capability needs, supplier
dependencies, approvals/licenses and open/abandon outcome. No NPC-only shortcut may directly create cash, stock,
premises or employees.

5.3 Counterparty graph

A newly formed NPC business must immediately participate in ordinary systems.  It may become supplier, customer,
debtor/borrower, tenant, employer, competitor or acquisition target based on real agreements and transactions. The
MARKET TOWN bakery is the acceptance example.

PART VI - HEALTH, TREATMENT & PHARMACY ARCHITECTURE

6.1 HealthEpisode / InjuryEvent

Extend Person health history with persistent episodes carrying onset, symptoms/diagnosis evidence, severity, con-
tagiousness where supported, cause/injury mechanism, work-availability effect, treatment needs, progression, recov-
ery/impairment/death outcome and linked evidence/events.

6.2 TreatmentCase

Treatment should link Patient, Provider Person/Business, TreatmentSpace or visit location, known symptoms/diagnosis,
recommended intervention, actual intervention, supplies/medicines, cost/payment, follow-up and outcome. Diagnosis
knowledge and true underlying condition remain separable where uncertainty matters.

6.3 Pharmacy dispensing and retail

A pharmacy business uses ordinary Inventory, Supplier, Transaction and Workload authorities plus pharmacy-specific
capability/equipment. A Medication/Preparation item should support:

 • ingredients/formulation or product identity where needed;

 • manufacturer/proprietary brand and dated availability;

 • labeled/advertised claims as Evidence/marketing text;

 • actual modeled effect/risk profile separately;

 • compounding versus packaged sale;

 • required pharmacist/physician authority according to LegalRegime;

 • lot/inventory/expiry or deterioration where relevant.

TECH LOCK - Advertising claims cannot directly set treatment effectiveness.

6.4 Work availability

Health episodes reserve/reduce Person labor availability. A business covers the resulting gap through existing schedules,
relief workers, overtime or lost capacity. Do not apply a business-wide SickPenalty when one worker is absent.

6.5 Barber versus dental capability

BarberStation/Kit enables grooming services. Dental treatment requires separate Dental capability/equipment/authority. If
an individual historically performs limited tooth extraction, represent that as evidence-backed capability, not an automatic
Barber occupation privilege.

PART VII - DEMOGRAPHIC MATURATION & HOUSEHOLD FORMATION

7.1 Periodic life-stage review

Extend monthly/low-frequency demographic evaluation to consider age transitions, education/apprenticeship, household
need, labor opportunities, relationship eligibility, courtship, marriage, pregnancy/fertility and independent household
formation. Use age as one input, not a trigger table that forces events.





                                          LAND & LEDGERS  7

## TECH-XIV — original PDF page 16 of 170

7.2 Youth employment

Employment search/availability should allow historically supported adolescent/young-adult work subject to dated law,
occupation entry ranges, household consent/need where applicable, schooling, capability and schedule. Turning 18 does
not auto-create an EmploymentRelationship.

7.3 RelationshipFormationCase

Conceptual state may include Person endpoints, acquaintance/kin/community context, interaction history, compatibility
evidence, current relationship stage, legal/household constraints and resolution. Avoid a universal RomanceScore if
ordinary relationship evidence can support the state.

7.4 Marriage and household split

MarriageEvent changes spouse/partner relationship and may trigger Household merge/split, accommodation search and
financial arrangements.  It does not create population. An external spouse increases local population only on actual
migration/residence.

7.5 Birth and death

Birth creates a stable PersonId and kinship links. Death changes living state, work availability, household/estate dependen-
cies and population but preserves historical identity. Long-run QA must reconcile all population deltas to Person/residence
events.

PART VIII - WORKLOAD-DRIVEN BUSINESS SCALING & MIXED-USE SPACE

8.1 Demand propagation

Population/catchment growth should create demand through actual Persons/Households/Businesses and transaction
opportunities. Existing business telemetry - transactions/hour, queues, stockouts, refused demand, cleaning/restock,
overtime and route load - drives manager hiring/schedule proposals. No SettlementPopulationMultiplier may directly post
profit.

8.2 Supplier relationships between NPC and player businesses

Standing SupplyAgreement/Procurement relationships can link an NPC producer to player hospitality/retail. The bakery-to-
Pike-Hall bread arrangement is a fixture: bakery inventory/production decreases, shipment/pickup occurs, Pike pays, and
Pike’s food/service process consumes the bread. No SynergyBonus is needed.

8.3 Mixed-use professional building

Reuse Building -> FunctionalSpace -> SpaceUseAgreement. A physician tenant, player pharmacy and player barber can
occupy separate rooms under the same roof. Landlord costs, tenant rent and each business’s revenue/employees remain
separate.

8.4 Butcher business profile

Butcher workflow should preserve animal/carcass/byproduct allocation, cold/preservation capacity, spoilage and re-
tail/wholesale channels. Town growth can increase demand but throughput remains constrained by physical processing,
storage and customer reach.

PART IX - AUTONOMOUS PLAYTEST CHECKPOINT & PROVENANCE

9.1 PlaytestCheckpoint

For autonomous/text scenario harnesses, persist or export a checkpoint record with at least: CheckpointId; timestamp/date;
parent checkpoint; scenario/version; reconciliation status; cash/debt/population/entity counts; known invariant failures;
source decision/event range; and optional authority-state hashes/digests where practical.

9.2 Reconciliation status

Suggested QA states:

 • Reconciled - no known authority contradiction within required export checks;

 • Unreconciled - exact output exists but known mismatch remains;

 • Voided - branch is explicitly invalidated;




                                          LAND & LEDGERS  8

## TECH-XIV — original PDF page 17 of 170

 • SoftContinuity - simulation continued for qualitative/design exploration after a known unresolved issue.

Names are conceptual; repository audit may choose different representation.

TECH LOCK - A derived checkpoint may not be marked Reconciled merely because later narration repeats its values.
 Reconciliation requires authoritative evidence or an explicit replay/repair workflow. Objective completion relying on a
 tainted quantity must remain unverified.

9.3 Long-run export additions

Extend autonomous QA exports with:

 • cash and protected/deployable cash per business;

 • owner distributions/capital contributions/intercompany loans;

 • property-unit occupancy, vacancies, debt-service reserve and development pipeline;

 • bank deposits, cash/correspondent assets, owned loans, third-party serviced loans, connected exposure and delin-
  quency;

 • business workload/staffing changes;

 • health episodes/treatment/pharmacy transactions and worker absence;

 • age/work/marriage/household/birth/death events;

 • NPC business formation and resulting counterparty relationships;

 • checkpoint ancestry/reconciliation status.

PART X - SCENARIO FIXTURES

10.1 MARKET TOWN long-run fixture

A regression/acceptance world should verify behavior rather than reproduce the playtest’s exact late balances. Required
outcomes over a multi-year run:

 • one or more ordinary businesses can become fully delegated;

 • housing construction creates units, not residents;

 • external households may migrate through real leads/jobs/housing;

 • rental reinvestment can pause on vacancy or liquidity;

 • a finance office can place outside-funded loans without asset duplication;

 • deposit banking later creates liabilities and can face withdrawal stress;

 • NPC entrepreneurs can open a missing business and transact with player firms;

 • growth increases workload and can trigger staffing changes;

 • teenagers/young adults age into work/relationships without population duplication;

 • illness/injury affects labor and can generate doctor/pharmacy demand;

 • internal construction is eliminated correctly in consolidated profit;

 •  all save/load and checkpoint reconciliations remain idempotent.

10.2 A TOWN FROM NOTHING / THE PROMOTER fixture

Create a scenario profile with no pre-existing service town at the focal site. External nodes/routes/land rights can exist, but
local permanent structures, businesses, housing and population begin near zero according to authored setup. Objectives
query actual development; no objective callback may directly create settlers, customers or businesses.

PART XI - REGRESSION & ACCEPTANCE TESTS


  Fixture                         Required result

  PROTECTED CASH              Mark the next debt payment as protected. Confirm authoritative cash does not increase and
                                  DeployableCash falls only once.



                                          LAND & LEDGERS  9

## TECH-XIV — original PDF page 18 of 170

  CASH SWEEP                Sweep excess cash from Business A into Finance B through owner. Confirm two transfers,
                                     zero revenue and unchanged consolidated equity except subsequent real profit/loss.
  INTERNAL CONSTRUCTION       Construction business builds owner rental. Confirm property basis increases by actual
                                              eligible cost while consolidated internal revenue/profit is eliminated.
  RENTAL VACANCY BRAKE        Leave one completed unit vacant. Confirm reinvestment policy may delay next project instead
                                           of forcing construction.
  EXTERNAL TENANT PRIORITY     Prefer outsiders but provide only a valid local applicant. Confirm vacancy policy can accept
                                           local tenant after configured wait/conditions; no population bonus.
  MIGRATION CHAIN                Sign lease with an outside household but block travel. Confirm population does not rise until
                                        actual arrival/residence.
  THIRD-PARTY  LOAN  PLACE-  Outside lender funds borrower through player office. Confirm principal is outside lender
  MENT                               asset, not player loan/cash, while fees belong to player office.
  DEPOSIT ACCOUNTING           Deposit customer cash. Confirm bank cash and deposit liability rise equally and bank equity
                                                 is unchanged.
  BANK PANIC                    Request clustered withdrawals. Confirm survival/failure follows real liquid assets, correspon-
                                     dent access and legal actions.
  CONNECTED LOAN             Bank lends controlled property company. Confirm bank/borrower ledgers show the loan,
                                      consolidated external debt eliminates it and bank concentration still includes it.
  ILLEGAL BANK POLICY            Player orders a policy prohibited by current LegalRegime. Confirm UI can warn, actor can
                                      comply/refuse where physically possible, evidence is created and later enforcement can act.
  NPC BAKERY                      Opportunity pressure supports NPC bakery. Confirm ordinary premises/capital/staff/supply
                                     are required; later player supply agreement and bank loan use ordinary transactions.
  HEALTH ABSENCE                  Injure one carpenter. Confirm only that Person’s availability falls; project schedule and
                                     replacement/overtime respond causally.
  PHARMACY DISPENSE            Physician recommends treatment; pharmacy must have product/ingredients and capable
                                               staff before sale/dispense. Marketing claim does not set efficacy.
  BARBER DENTISTRY             Give barber grooming capability only. Confirm dental treatment unavailable until separate
                                           capability/tools/authority exist.
  YOUTH AGING                 Age resident from 17 to 18. Confirm no automatic job, marriage or household split; ordinary
                                       decision systems may later create them.
  MARRIAGE POPULATION         Marry two current residents and move them into a new rental. Confirm household/occupancy
                                 changes but population does not.
  BIRTH/DEATH                        Birth creates one Person and death removes one living resident while preserving his-
                                           tory/kinship. Population delta reconciles exactly.
  SOFT CONTINUITY              Mark a checkpoint void/unreconciled then continue. Confirm downstream exact states inherit
                                               taint and cannot satisfy reconciled-objective assertions until repaired/replayed.
  SAVE LOAD                      Save/load during active rental build, tenant migration, bank deposits/loans, health episode
                               and relationship formation. Confirm no duplicate cash, Persons, liabilities, inventory or
                                   agreements.


PART XII - IMPLEMENTATION PRIORITY

12.1 P0 - authority and reconciliation

 • Protected/deployable cash must be a read model over authoritative cash.

 • Distribution/contribution/intercompany transaction types and consolidation must reconcile.

 • Deposit liabilities and owned/serviced-loan ownership must be impossible to duplicate.

 • Autonomous checkpoint ancestry/reconciliation state must stop invalid branches from masquerading as authoritative QA
   output.

12.2 P1 - property, banking and long-run autonomy

 • Rental development pipeline, debt-service reserve policy, vacancy brake and external-tenant routing.

 • BankPolicyProfile, deposit accounts, exposure/maturity views, correspondent balances, connected-party tags and panic
   fixture.

 • NPC VentureDevelopment/BusinessFormation using existing Opportunity & Pressure and ordinary economic authorities.

 • Workload-driven staffing and internal/external construction scheduling.

12.3 P2 - health, pharmacy and demographic maturation

 • HealthEpisode/TreatmentCase integration with work availability and pharmacy transactions.



                                         LAND & LEDGERS  10

## TECH-XIV — original PDF page 19 of 170

 • Product marketing claims separated from actual medicine effects.

 • Relationship formation, youth-employment transitions, marriage/household split and generational long-run calibration.

 • A TOWN FROM NOTHING long-run scenario fixture after the core authority paths are reliable.

PART XIII - RESEARCH / CALIBRATION HOLDS

 • Exact private-bank legal powers, disclosure/examination, insolvency, reserve, deposit and unlawful-conduct rules by
   place/date/institution form.

 • Exact finance-office/bank staffing economics, fee schedules, loss rates, deposit growth and capitalization thresholds.

 • Exact rent, land, vacancy, tax/insurance, maintenance and residential-development economics by scenario.

 • Exact illness/injury distributions, medicine catalog/effect/toxicity and pharmacy law by date/location.

 • Exact relationship/marriage-age distributions and youth-work participation by region/community/period.

 • Exact NPC venture opportunity thresholds and time-to-entry; no population-to-business formula is authorized.

 • Exact construction productivity remains data/calibration under the supplemental historical work reference.

PART XIV - REVISION XIII DEEPENING MAP

 • Revision XII Finance/Distress architecture is deepened with bank deposit accounts, third-party loan placement, bank
   policy, connected exposure and intentional unlawful actions through LegalRegime.

 • Existing Policy/Delegation architecture is deepened with portfolio cash sweeps, exclusions and protected-liquidity read
  models.

 • Revision IX Rental Holdings architecture is deepened with repeat development, debt-service reserve policy, tenant
   recruitment and vacancy brakes.

 • Existing Opportunity & Pressure / Venture Manager direction is deepened into NPC business formation parity and
   counterparty-network validation.

 • Existing Person/Household health and demographic architecture is deepened with treatment/pharmacy chains and
   visible age-to-work/relationship transitions.

 • Existing long-run QA is deepened with explicit checkpoint ancestry and reconciliation/soft-continuity state.





                         END OF REVISION XIII INTEGRATION

 The preserved Technical Implementation Bible Revision XII follows unchanged and remains authoritative except where
                  Revision XIII explicitly adds, deepens, corrects or supersedes technical guidance.




                                         LAND & LEDGERS  11

## TECH-XIV — original PDF page 20 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           1

        LAND & LEDGERS
   TECHNICAL IMPLEMENTATION BIBLE -
              REVISION XII

  Historical Regime & Frontier Institution Architecture: Land Entry, Route
States, Information Latency, Secured Credit, Receipts, Cooperatives, Distress
                 & Environmental Shocks

                                          Revision date: October 5, 2026
  Source baseline: Technical Implementation Bible Revision XI - October 5, 2026; Canon compatibility target: Canonical
                                               Project Bible Revision XXIX
        New source integrated: Historical Research Passes I-IV and conversation synthesis - October 2026
                                                Status: Active integration


PRESERVATION GUARANTEE

REVISION XII preserves Technical Implementation Bible Revision XI in full except where this integration explicitly adds,
deepens, clarifies, corrects or supersedes technical guidance. The complete Revision XI document follows unchanged as the
preserved historical layer. Canonical Project Bible Revision XXIX controls intended gameplay behavior. Conceptual names
below describe target responsibilities and must not be treated as proof that matching Unity classes already exist.

PURPOSE

Revision XII turns the October frontier-history research into implementation architecture without creating a separate historical-
event simulation. Existing stable-ID authorities for Persons, Households, Businesses, Property, Agreements, Transactions,
Projects, Inventory, Logistics, Finance, Information/Evidence, Institutions and LegalRegime remain the source of truth.
Historical data configures those systems and event/shock objects mutate their physical/legal inputs.

REVISION XII ARCHITECTURE LOCKS

 • HistoricalRegime and DatedHistoricalEvent data may schedule or parameterize causes, but may not directly post revenue,
   customers, migration, price multipliers or business failure.

 • Land entry, patent, lease, railroad/company land, tax-sale interest, security interest and restricted/reservation geography
   remain typed rights/relationships over the same Property authority.

 • Route infrastructure state and service capability are separate. Rail laid, ferry available, ice crossing usable, bridge open,
   mail service, passenger service, freight service and express service cannot be collapsed into one Connected flag.

 • True world state and known/reported state remain separate. Every historical information channel has latency, access and
   provenance.

 • Supplier discovery, payment and physical fulfillment are separate.  Catalog/mail-order, traveling-sales and express
   workflows cannot create inventory without a shipment/transfer.

 • Commercial credit uses evidence available to the counterparty. Credit reports, bank statements/exams, trade references
  and payment history are information records, not hidden access to true Net Worth.

 • Security interests and warehouse receipts reference real collateral/inventory and dated legal rules. Custody, ownership
  and secured claim remain separate.

 • Cooperative/mutual organizations reuse ordinary ownership, governance, Business cash, Agreement and Transaction
   systems; member assets/cash do not merge by implication.

 • Distress source/procedure is preserved through private sale, foreclosure, execution, tax sale, estate/assignment or
   bankruptcy. DistressedOpportunity is a read model/opportunity over those authorities.

 • Environmental shocks change crops, animals, routes, buildings, water, inventory or worker availability first; price/opportunity
   systems observe the result.



                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 21 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           2



 • All new historical datasets require source, date, geography, scope/denominator and confidence metadata where applicable.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 22 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           3


MASTER TABLE OF CONTENTS


PRESERVATION GUARANTEE                                                                       1


PURPOSE                                                                                        1


REVISION XII ARCHITECTURE LOCKS                                                                1


PART I - HISTORICAL REGIME, GEOGRAPHIC PROFILE & AUTHORING DATA                               5

   1.1 HistoricalRegimeDefinition  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   1.2 RegionalScenarioProfile   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   1.3 HistoricalObservation and provenance   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5


PART II - LAND ENTRY, PROPERTY INTERESTS & TENURE                                              5

   2.1 LandEntryCase  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   2.2 Typed PropertyInterest / encumbrance   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   2.3 FarmTenureRelationship  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5


PART III - ROUTE, SERVICE & TEMPORARY TRAFFIC ARCHITECTURE                                     5

   3.1 RouteEdgeState and service decomposition   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   3.2 River-crossing transitions .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   3.3 TemporaryTrafficPulse / corridor boom   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    5

   3.4 Rail tariff/claim data .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6


PART IV - ENVIRONMENTAL & BIOLOGICAL SHOCK PROPAGATION                                       6

   4.1 EnvironmentalIncident   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   4.2 AnimalHealth / draft-capacity integration   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   4.3 Fire/flood/weather state    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6


PART V - INFORMATION LATENCY, COMMUNICATION NETWORKS & MARKET EVIDENCE                    6

   5.1 InformationMessage and access graph  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   5.2 WeatherEvidence and MarketQuoteEvidence .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   5.3 CommercialCreditEvidence   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   5.4 Postal/express/telephone services .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6


PART VI - SUPPLIER CHANNELS, WHOLESALE NETWORKS & TRAVELING SALES                          6

   6.1 SupplierChannel extension .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    6

   6.2 TravelingSalesVisit   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   6.3 Mail-order workflow  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   6.4 Trade credit and collections   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7


PART VII - SECURED CREDIT, WAREHOUSE RECEIPTS, INSURANCE & DISTRESS                          7

   7.1 SecurityInterest  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   7.2 WarehouseReceipt  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   7.3 InsurancePolicy / Claim .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   7.4 DistressedOpportunity and legal procedure .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   7.5 Bankruptcy chronology  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 23 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           4



PART VIII - COOPERATIVES, MUTUALS, UTILITIES & INSTITUTIONS                                       7

   8.1 CooperativeOrganization  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   8.2 UtilityProject and Connection    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    7

   8.3 InstitutionLocation    .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8

   8.4 ImmigrationCampaign   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8


PART IX - AGRICULTURAL MARKET & LIVESTOCK ARCHITECTURE                                       8

   9.1 CustomAgriculturalService  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8

   9.2 Grain grade / elevator / futures evidence   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8

   9.3 Dairy processing/testing   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8

   9.4 Livestock brand, estray and stockyard .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8

   9.5 Finite rural collection resources   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .    8


PART X - TECHNICAL DATASET REGISTER                                                            8


PART XI - REGRESSION & ACCEPTANCE FIXTURES                                                    9


PART XII - IMPLEMENTATION PRIORITY, HOLDS & SUPERSESSION                                      10

   12.1 Priority after repository audit   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   10

   12.2 Research/data holds   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   11

   12.3 Supersession/deepening map   .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .  .   11


RESEARCH BASIS - REVISION XII                                                                   11


PRESERVED TECHNICAL REVISION LAYERS                                                         14


Preserved Revision XI - Construction & Project Execution                                              15


Preserved Revision X - Town, Catchment & Workforce                                                  24


Preserved Revision IX - Ghost Town Systems Integration                                               30


Preserved Revision VIII - 1870 Historical Architecture                                                  46


Preserved Revision VII - Rural Systems Integration                                                    54


Preserved Revision VI - Continuous Person & Physical Capability                                        64


Preserved Revision V - The Contractor Integration                                                     74


Preserved Revision IV - Saloon Circuit Integration                                                     79


Preserved Revision III - Autonomous Enterprise Ladder                                                84


Preserved Revision II - Third Full Playtest Integration                                                  91


Preserved Revision I - Core Technical Reference                                                     130





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 24 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           5


PART I - HISTORICAL REGIME, GEOGRAPHIC PROFILE & AUTHORING DATA

1.1 HistoricalRegimeDefinition

TECH LOCK - Represent broad periods as authoring context over existing systems: date bounds, applicable geography,
major causal drivers, outside-market/rail/credit priors and source/confidence. Runtime systems consume the resulting route,
credit, production, migration and information conditions; the regime object does not post economic outcomes.

1.2 RegionalScenarioProfile

Scenario geography should identify the historical skeleton and regional calibration family: southeastern Dakota, southwestern
Minnesota, Nebraska, northern Dakota, West River/Black Hills or another researched corridor. The profile supplies priors and
required external nodes, not deterministic business rosters.

1.3 HistoricalObservation and provenance

Use source-versioned records for dated local facts such as an artesian well, telephone exchange, railway crossing, incor-
poration, stage-route asset scale, fair, county-seat move or stockyard. Store source type and potential promotional bias.
Generator/testing code may query the record; runtime economy is not rebuilt as a literal historical replay.

PART II - LAND ENTRY, PROPERTY INTERESTS & TENURE

2.1 LandEntryCase

TECH LOCK - Public-land entry should be a persistent case/workflow linked to Person/Household, parcel, statutory program
and LegalRegime. Minimum conceptual state: filing/application, qualifying land status/survey, residence/presence evidence,
cultivation/improvement/tree/irrigation obligations as applicable, deadlines, witnesses/proof, contests/abandonment, fees and
patent/final outcome. A claim/entry is not the same PropertyInterest as final patent.

2.2 Typed PropertyInterest / encumbrance

Extend/reuse PropertyInterest so ordinary private title, lease, public-land entry, railroad/company ownership, mortgage/security,
tax-sale purchaser interest, redemption right and legally restricted/federal/reservation status can coexist without duplicate
Parcel ownership flags. Unknown/disputed remains representable.

2.3 FarmTenureRelationship

Add/reuse a farm operating relationship separate from Employment: OwnerOperator, CashTenant, ShareTenant, Par-
tOwnerOperator and other dated lease forms where research supports them. Crop/output shares settle through Agree-
ment/Transaction; tenant operation does not create wage payroll unless a separate employment relationship exists.

PART III - ROUTE, SERVICE & TEMPORARY TRAFFIC ARCHITECTURE

3.1 RouteEdgeState and service decomposition

TECH LOCK - Road/stage/ferry/river/rail edges store physical/seasonal state. Passenger, freight, mail, express and drayage
services reference that infrastructure separately. One edge can be physically present but lack the service/capacity needed by
a shipment or journey.

3.2 River-crossing transitions

A crossing may resolve to ferry transfer, seasonal ice route, temporary rail transfer or permanent bridge. Routing uses current
capacity/reliability/cost and can invalidate reservations when weather or construction changes state.

3.3 TemporaryTrafficPulse / corridor boom

Represent end-of-track crews, Black Hills gateway traffic, military/government contracts or similar surges as generated
Journeys, Shipments, procurement needs and temporary employment attached to real outside origins/destinations. When the
source condition moves/ends, demand decays naturally.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 25 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           6



3.4 Rail tariff/claim data

Route/class rates and posted-tariff evidence should be versioned by carrier/date where needed. Carrier disputes use
CommercialClaim linked to Shipment/Agreement/Property damage evidence. Do not create a railroad-only claim ledger.

PART IV - ENVIRONMENTAL & BIOLOGICAL SHOCK PROPAGATION

4.1 EnvironmentalIncident

TECH LOCK - A historical/environmental incident targets real geography and mutates authoritative physical state: crop/forage
condition, animal health, route capacity, water availability, structure/inventory condition or worker availability. Market/pricing/opportunity
systems react afterward. Event definitions may specify exposure functions and source evidence but not final economic
percentages.

4.2 AnimalHealth / draft-capacity integration

Horse epizootic and other supported diseases act through individual/coarsely batched animal health and usable-team
reservation. Wagon count remains unchanged while healthy draft capacity falls. Veterinary tasks, rest/quarantine and
replacement purchase become ordinary responses.

4.3 Fire/flood/weather state

Reuse Fire and physical-adversity architecture. Add river ice/flood and prairie-fuel/backfire inputs as data where needed.
Long-winter/blizzard scenarios primarily close/degrade routes, increase heating/feed demand and threaten Persons/animals;
they do not invoke global WinterPenalty.

PART V - INFORMATION LATENCY, COMMUNICATION NETWORKS & MARKET
EVIDENCE

5.1 InformationMessage and access graph

TECH LOCK - Messages/reports must carry sender, receiver, channel, send time, arrival time, cost, reliability and evidence
provenance. Access may be direct office/subscriber/private line, shared party line, railway/post route or indirect courier.
TownHasTelephone/Telegraph is insufficient as an authority.

5.2 WeatherEvidence and MarketQuoteEvidence

True weather/market state remains separate from observations and forecasts. WeatherEvidence includes station/source
timestamp, forecast horizon and confidence. MarketQuoteEvidence includes market, commodity/grade, cash/future contract,
quote time and arrival time. Decision systems use only evidence known to the actor.

5.3 CommercialCreditEvidence

Trade references, R.G. Dun-style reports, public bank statements/exams and direct payment history are EvidenceRecords
that may feed lending/supplier decision models. Reports can be stale or incomplete; do not derive them directly from hidden
authoritative NetWorth at decision time unless the reporting workflow legitimately collected that fact.

5.4 Postal/express/telephone services

Postal money orders, telegraph money transfer, Railway Mail, express/COD, RFD and telephone exchanges are dated
service capabilities over physical/network providers. Each service declares eligible locations/transactions, fees/limits and
travel/message latency. New services do not retroactively alter old transaction histories.

PART VI - SUPPLIER CHANNELS, WHOLESALE NETWORKS & TRAVELING
SALES

6.1 SupplierChannel extension

ProcurementNeed may resolve through local producer/merchant, regional jobber/wholesaler, direct manufacturer, implement
dealer, catalog/mail-order house or traveling salesman. SupplierOffer keeps the actual seller and fulfillment path; a channel is





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 26 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           7



not an anonymous price source.

6.2 TravelingSalesVisit

A drummer is an actual Person/activity chain:  travel -> lodging/sample-room capacity -> merchant meetings -> sam-
ples/evidence -> orders/credit terms -> departure. Accepted orders generate later supplier obligations/shipments. Samples
can be physical items/lots where material.

6.3 Mail-order workflow

TECH LOCK - Catalog discovery and order placement cannot directly increment inventory. Model Discovery/Evidence ->
Order/Agreement -> payment/remittance -> supplier fulfillment -> express/rail/freight shipment -> depot/local delivery ->
receipt/inventory. RFD may change mail legs but not erase heavy-goods logistics.

6.4 Trade credit and collections

TradeCreditAccount/Obligation retains invoice/note terms, balance, arrears, security, references and collection stage. A
remote creditor can delegate correspondence/collection/legal tasks to a local Agent/Lawyer without transferring the debt itself.

PART VII - SECURED CREDIT, WAREHOUSE RECEIPTS, INSURANCE & DIS-
TRESS

7.1 SecurityInterest

TECH LOCK - Use PropertyRight/Encumbrance + Agreement/Obligation for mortgages, chattel security and pledges.
Collateral identity and dated attachment/perfection/enforcement rules come from LegalRegime. Future property/crop rules
are jurisdiction/date specific.

7.2 WarehouseReceipt

WarehouseReceipt links depositor/owner, warehouse/custodian, commodity grade/quantity, physical pool/lot references,
issue/transfer/pledge status and redemption/withdrawal. Commingling may be physical while ownership remains allocated.
Warehouse failure must not silently convert depositor grain into general warehouse inventory.

7.3 InsurancePolicy / Claim

Reuse Insurance and CommercialClaim authorities for fire/hail/mutual policies. Policy/organization solvency remains causal:
claim eligibility does not guarantee timely/full settlement if reserves/assessment capacity are inadequate.

7.4 DistressedOpportunity and legal procedure

Create a read model/opportunity over actual source proceedings: private liquidation, mortgage/chattel foreclosure, judgment
execution, tax sale, estate/assignment or bankruptcy. Preserve right-holder, sale authority, encumbrances, redemption rights,
condition and occupancy. The opportunity object does not own the asset.

7.5 Bankruptcy chronology

LegalRegime must allow the 1878-1898 federal bankruptcy gap and the 1898 Act as different procedure availability. Generic
Insolvent state can describe finances but cannot decide the legal route by itself.

PART VIII - COOPERATIVES, MUTUALS, UTILITIES & INSTITUTIONS

8.1 CooperativeOrganization

TECH LOCK - Cooperative/mutual entities reuse Business/Organization ownership, cash, assets, Agreements and gover-
nance. Member contribution, voting/control, service/patron relationship and distribution remain explicit. Supported cases
include creamery, elevator, mutual insurance, building-and-loan and farmer telephone line.

8.2 UtilityProject and Connection

Water, artesian wells, irrigation, electric-light/power and telephone networks use Project/PropertyRight/Finance/Asset/Agreement
authorities. Distinguish charter/franchise, constructed trunk/network capacity and individual connection/service relationships.




                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 27 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           8



Failed drilling or incomplete construction can consume capital without producing service.

8.3 InstitutionLocation

Courthouse, county seat, capital, experiment station, post office, depot and similar institutional nodes emit journeys,
employment, procurement and records access. Relocation changes the node relationship and future traffic; it does not rewrite
past transactions.

8.4 ImmigrationCampaign

Campaigns create Awareness/Lead state in actual households/Persons using media, correspondence and relationship graphs.
Kin/community links affect credibility, lodging and travel feasibility. No campaign directly increments population.

PART IX - AGRICULTURAL MARKET & LIVESTOCK ARCHITECTURE

9.1 CustomAgriculturalService

Traveling threshing/reaping or other equipment service uses Business + Asset + Task + Route + seasonal Agreement. Shared
expensive equipment gains value from multi-customer utilization while maintenance and travel reduce available capacity.

9.2 Grain grade / elevator / futures evidence

GrainLot carries grade/weight/condition evidence.  Elevators expose purchase versus storage capabilities.  Terminal
cash/futures quotes are information inputs; hedging may be represented at major merchant/elevator depth without re-
quiring ordinary farmer speculation.

9.3 Dairy processing/testing

CreamSeparator and Babcock-style testing enter through equipment/method/evidence data. Milk settlement may depend on
tested butterfat quality. Cooperative ownership of processing equipment stays separate from member farm production.

9.4 Livestock brand, estray and stockyard

LivestockBrand is evidence linked to animal/owner/jurisdiction. EstrayCase manages find/notice/appraisal/recovery under
dated law. LivestockConsignment handles producer -> commission merchant/stockyard -> buyer/packer custody, weigh-
ing/shrinkage, fees and proceeds.

9.5 Finite rural collection resources

FiniteResourcePatch supports exhaustible side-economies such as buffalo-bone collection.  Extraction reduces stored
quantity; OpportunityPressure observes depletion rather than respawning resources.

PART X - TECHNICAL DATASET REGISTER

These conceptual records/read models are the target integration map for the historical findings. Repository audit decides
whether each extends an existing class, becomes data on an existing authority or remains authoring-only. Duplicate sources
of truth are prohibited.



   Concept / dataset                           Required responsibility
    HistoricalRegimeDefinition                     Date range, geography, causal drivers, source/confidence. Regime adjusts inputs/priors only; it never posts revenue,
                                                            yield or migration directly.
   LegalRegimeRule                                   Effective date + jurisdiction + source + actor/property/instrument applicability. Supports land entry, liquor, rail, estray,
                                                           chattel mortgage, tax sale, bankruptcy and regulatory chronology.
    HistoricalObservation                               Local/date-specific evidence such as De Smet service population, Bismarck-Deadwood traffic, artesian well, telephone
                                              exchange or corporation filing. Never silently generalized.
    TechnologyAvailabilityRecord                     Existence, commercial availability, regional channel, support/parts, capital and evidence. Supports barbed wire,
                                                      separators, telephone, refrigeration, binder equipment and similar diffusion.
    LandStatus / PropertyInterest                    Public-domain entry, unperfected claim, patent, railroad land, lease, tax-sale interest, mortgage/security interest,
                                                         reservation/federal restriction and ordinary private ownership remain distinct.
   LandEntryCase                                    Applicant, statute/program, parcel, filing, residence/cultivation/improvement evidence, deadlines, proof/witnesses,
                                              abandonment, contest and patent outcome.
   TownsitePromotionCase                          Promoter/railroad/land company, plat, lots/options, advertised claims, proposed infrastructure, actual sales and
                                                subsequent survival/decline.
   RouteEdgeState                                     Road/stage/ferry/river/rail edge with seasonal capacity, service state, toll/rate, closure and transfer requirements.
                                                 Supports ferry -> ice crossing -> bridge sequences.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 28 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                           9





   Concept / dataset                           Required responsibility
    TransportService                              Passenger, freight, mail, express and drayage are separate services that can share infrastructure but not capability
                                                                truth.
    TemporaryTrafficPulse                            Construction front, gold rush or government contract produces actual Journeys/Shipments/Customers for a bounded
                                                     period and source region.
    EnvironmentalIncident                               Weather/biological/fire/flood event mutates real crops, animals, route capacity, inventory, structures or worker availability
                                                     before markets react.
    AnimalHealthState / Outbreak                      Species-specific illness and geographic spread; draft capacity, sale eligibility, reproduction and mortality consequences
                                                      derive from animal state.
   WeatherEvidence                              True weather is separate from forecast/observation. Forecast source, timestamp, station/network, lead time and
                                                   confidence determine what actors know.
   InformationMessage                            Sender, receiver, channel, send/arrival time, cost, reliability and payload provenance for mail, messenger, telegraph,
                                                  telephone and reports.
    InformationAccessRelationship                     Direct office/wire/subscriber/party-line/indirect courier access; town-level availability does not imply universal connec-
                                                                 tion.
   MarketQuoteEvidence                           Commodity/market/date/time/source/latency and cash/future contract identity. Quotes inform decisions but do not
                                                mutate local price directly.
   CommercialCreditEvidence                     Trade references, R.G. Dun-style report, bank statement/exam, payment history, property/debt evidence and confi-
                                                 dence. Lenders/suppliers consume only legitimately available evidence.
    SupplierChannel                                  Manufacturer, wholesaler/jobber, traveling salesman, catalog/mail-order, local dealer, express/COD or direct producer.
                                              Channel drives lead time, price, package and information.
    TravelingSalesVisit                                Person/wholesaler, route, hotel/sample-room reservation, samples, merchant meetings, orders and follow-up shipment;
                                                     creates leads/orders, not inventory.
    TradeCreditAccount                                 Creditor/debtor, limit/terms, invoices/notes, arrears, security, payment history, collection status and renegotiation.
    SecurityInterest                                   Mortgage/chattel lien/pledge with collateral identity, attachment/perfection/notice rules, priority, default and enforcement
                                                     supplied by LegalRegime.
   WarehouseReceipt                               Depositor/owner, warehouse, commodity grade/quantity, commingled pool, receipt status, transfer/pledge and redemp-
                                                         tion/withdrawal. Custody is not ownership.
    GrainLot / GradeEvidence                         Physical quantity plus grade/weight/moisture/condition evidence and inspector/weigher source. Settlement proceeds
                                                      derive from accepted grade/contract.
    DistressedOpportunity                        Source process: private liquidation, estate, mortgage/chattel foreclosure, sheriff execution, tax sale, bankruptcy or
                                                           creditor workout; preserves encumbrance/redemption/condition uncertainty.
   BankruptcyCase                                  Available only when dated LegalRegime supports it. Separate from generic insolvency/default; preserves creditor
                                                        claims, estate assets and proceedings.
    TaxSaleInterest                                Tax delinquency, sale, purchaser interest, notice and redemption window. Title transition occurs only through applicable
                                                          legal process.
    InsurancePolicy / Claim                       Covered peril, property/lot/animal, limits, deductible/assessment, mutual membership/reserves, claim evidence and
                                                       settlement. Physical damage is separate.
    CooperativeOrganization                         Members/shares/contributions, governance, patron/service relationship, business cash/assets and distributions.
                                                 Supports creamery, elevator, insurance, building-and-loan and farmer telephone cases.
   BuildingAndLoanAccount                   Member shares/dues + loan obligation + maturity/payoff logic; do not coerce into ordinary bank mortgage schema if
                                                       material differences matter.
    FarmTenureRelationship                         Owner-operator, cash tenant, share tenant, fixed-product/other dated lease, part-owner and hired labor remain distinct
                                                           relationships.
    CustomAgriculturalService                         Traveling thresher/reaper or other custom machinery business uses equipment, crew, route, seasonal contract and
                                               maintenance authorities.
    LivestockBrand / EstrayCase                    Animal ownership evidence, recorded brand jurisdiction, found-animal notice/appraisal/recovery workflow and local
                                                         statutory exceptions.
   LivestockConsignment                          Producer -> commission agent/stockyard -> buyer/packer with custody, shrinkage, weighing, fees, sale proceeds and
                                                                          rail shipment.
    UtilityProject / Connection                        Water, artesian, electric light, irrigation, telephone and related networks use Project + PropertyRight + finance +
                                                     service-connection data; incorporation alone is not service.
    InstitutionLocation                             Courthouse/county seat/capital/experiment station/post office/depot location emits real journeys, employment, pro-
                                               curement and record-access demand and may relocate.
   ImmigrationCampaign                           Promoter, target geography/community, media/channel, claims/evidence, inquiries, correspondence and household
                                                      migration decisions. Never directly increments population.
   TemporaryMarketEvent                              Agricultural fair, stock show, medicine show or similar event schedules transient Persons/businesses, temporary
                                                             stalls/capacity, sales, awards and awareness.
   AwardEvidence                                 Fair/show award references actual Person/animal/product/event and can alter known reputation/price expectations
                                                      without hidden capability bonuses.
    FiniteResourcePatch                             Buffalo-bone or similar exhaustible collection resource with quantity, accessibility and depletion; no infinite gathering
                                                               job.
   RecordSource / Provenance                    Every historical authoring datum carries source type, geography, date, denominator/context and confidence. Promo-
                                                              tional material can be stored with bias/interest metadata.

PART XI - REGRESSION & ACCEPTANCE FIXTURES




    Fixture                                Required result
  HOMESTEAD NOT OWNERSHIP              File a qualifying entry but do not satisfy residence/improvement proof. Confirm possession/claim can exist without patented
                                                                        title and ordinary sale workflow respects the current right.
   TIMBER CULTURE FAILURE               Create a tree-culture entry with poor survival/noncompliance. Confirm no automatic patent and no generic acreage bonus.
   RAILROAD LAND PROMOTION         A railroad/land company advertises lots. Confirm awareness increases but migration/business formation still require real
                                             household/business decisions.
   END-OF-TRACK BOOM                Move rail construction front away from a node. Confirm transient journeys and related hotel/livery demand fall without
                                                   scripted closure.
   FERRY-ICE-BRIDGE                   Change one Missouri crossing through ferry, seasonal ice and bridge states. Confirm route cost/capacity/reliability and
                                            shipment routing update separately.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 29 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                          10





    Fixture                                Required result
  HORSE EPIZOOTIC                       Sicken a large share of local draft animals. Confirm wagons remain owned but route/farm/fire-service capacity collapses
                                              according to healthy teams.
  LONG WINTER                           Block rail/road access for weeks. Confirm existing stock/reserves continue, new shipments queue/fail/reroute and price
                                              pressure emerges from availability.
   PRAIRIE FIRE                                  Ignite dry grass with wind and firebreak options. Confirm spread is spatial and building/crop/forage loss creates later
                                               insurance/market consequences.
   ARTESIAN PROJECT                 Run one successful and one failed drilling project. Confirm capital can be consumed without service and successful yield
                                                creates real water capacity.
   MAIL ORDER                              Discover a distant catalog supplier, place order and pay through available channel. Confirm goods require express/rail/wagon
                                                          fulfillment before inventory appears.
   RFD                               Add a rural postal route. Confirm information/mail accessibility changes but merchandise delivery and town trip behavior
                                           respond only through relevant channels.
   TELEPHONE SUBSCRIBER                Create a town exchange with 25 lines and only some subscribers. Confirm non-subscribers cannot place direct calls merely
                                         because the town has an exchange.
  WEATHER FORECAST EVIDENCE         Provide stale newspaper forecast versus faster telegraph observation. Confirm actors see different knowledge over the
                                    same true weather.
   R.G. DUN CREDIT                           Distant supplier receives an imperfect commercial report. Confirm credit decision uses report timestamp/confidence and
                                         can differ from true current finances.
   CHATTEL DEFAULT                     Secure a machinery note with named assets, default, satisfy notice, then foreclose. Confirm collateral transfers through
                                             procedure and unrelated property remains untouched.
   FUTURE CROP RULE                      Attempt to secure debt with future crop under a regime that disallows it. Confirm no valid security interest is created despite
                                          agreement intent.
  WAREHOUSE RECEIPT                   Deposit grain for storage, commingle physically, then fail the warehouse. Confirm depositor ownership/receipt claims are
                                             preserved separately from warehouse-owned grain.
   RECEIPT PLEDGE                      Pledge a warehouse  receipt  to a bank.   Confirm custody,   title and  security  rights remain  distinct through
                                                    sale/withdrawal/default.
   GRAIN GRADE DISPUTE                   Weigher/inspector and seller disagree on grade. Confirm payment/claim depends on evidence/contract rather than one
                                             hidden quality scalar.
   COOPERATIVE CREAMERY                Several farmers fund one creamery. Confirm organization owns equipment/cash, member farms remain separate, and
                                                   milk/service transactions reconcile.
   BUILDING AND LOAN                 Member contributes dues/shares and borrows. Confirm institution cash, member equity/share balance and debt are distinct.
   MUTUAL INSURANCE CATASTROPHE      Concentrated hail/fire losses hit many members. Confirm claims can overwhelm reserves rather than insurance auto-paying
                                                 without solvency.
   BANK PANIC                                  Trigger deposit withdrawals plus reduced correspondent access. Confirm bank can tighten lending, suspend/fail or survive
                                           depending on liquidity rather than reputation score.
   BANKRUPTCY GAP                   Run identical insolvent merchant in 1894 and 1899. Confirm available legal workflows differ.
   TAX SALE REDEMPTION                     Sell delinquent property for taxes, then redeem inside the lawful window. Confirm purchaser interest is not treated as
                                           immediate unconditional title.
   SHARE TENANT                         Operate a farm under crop-share lease. Confirm Person is FarmOperator/economically active without wage Employmen-
                                                  tRelationship and output share settles contractually.
  DRUMMER VISIT                             Traveling wholesaler arrives with samples, books sample room, takes orders, then departs. Confirm no stock appears until
                                                         later supplier shipment.
   COLLECTION ATTORNEY               Remote supplier places delinquent account with local lawyer. Confirm letters/negotiation/legal action consume time and can
                                        end in workout or enforcement.
   DISTRESSED ASSET PROVENANCE      Generate opportunities from foreclosure, sheriff sale and private liquidation. Confirm each listing identifies the real
                                                  right-holder/process and surviving encumbrances.
  COUNTY SEAT MOVE                     Relocate courthouse/records. Confirm legal traffic, jobs and supporting demand move through ordinary journeys/businesses
                                                instead of SettlementRole bonus.
   FAIR MARKET EVENT                    Schedule agricultural fair with exhibitors, animals, machinery and awards. Confirm temporary traffic, awareness and sales
                                              occur from actual participants.
   BUFFALO BONE DEPLETION                Collect a finite bone patch until exhausted. Confirm buyer/freight business loses that source and no resource respawns by
                                                       timer.
   STOCKYARD CONSIGNMENT             Ship cattle to terminal yard through commission agent. Confirm shrinkage, weighing, fees, sale proceeds and cus-
                                              tody/ownership sequence reconcile.
   SAVE LOAD                               Save/load during active homestead proof period, tax redemption, warehouse-receipt pledge, insurance claim, traveling-sales
                                                order and chattel foreclosure. Confirm no duplicate rights, money or inventory.

PART XII - IMPLEMENTATION PRIORITY, HOLDS & SUPERSESSION

12.1 Priority after repository audit

 • P0 - Preserve existing authority boundaries; no historical subsystem may create duplicate balances, property ownership,
   inventory or population truth.

 • P0 - Make LegalRegime date/jurisdiction lookup deterministic and save/load safe before adding new land-entry, security-
   interest or distress workflows.

 • P1 - Implement/extend evidence latency and network access so mail/telegraph/telephone/market/weather/credit information
   uses one provenance model.

 • P1 - Extend Route/Service separation and environmental incidents so horse health, weather, crossing state and infrastruc-
   ture outages propagate through logistics.

 • P1 - Add typed LandEntry/FarmTenure/SecurityInterest/WarehouseReceipt relationships over existing Property/Agreement
   authorities.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 30 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                          11



 • P2 - Add supplier-channel/traveling-sales/mail-order workflows over SupplierOffer/Shipment/Message/Agreement authori-
    ties.

 • P2 - Add cooperative/mutual organization patterns and utility/institution location workflows without separate cash or Person
   systems.

 • P2 - Add distress opportunity read models and dated bankruptcy/tax/execution workflows.

 • P3 - Build historical datasets from Dakota incorporations, Sanborn maps, rail/tariff records, tenancy, wholesale catalogs
  and legal session laws for calibration.

12.2 Research/data holds

 • Exact class/schema names remain repository-audit dependent.

 • Land-entry, allotment, chattel, warehouse, estray, tax-sale, bankruptcy and insurance procedure must be source-versioned
   by jurisdiction/date rather than coded from the summary examples here.

 • Event severity/probability values for epizootic, drought, blizzard, grasshopper, flood, prairie fire and cattle winter remain
   calibration data.

 • Exact mail/express/COD/money-order/telephone/weather/market-information tariffs, coverage and latencies remain au-
   thoring datasets.

 • Actual cooperative governance, creamery payout, building-and-loan dues, mutual-insurance assessments and ware-
   house/futures practice require further historical calibration.

 • Indigenous land/allotment and horticultural-knowledge mechanics require community-specific research and provenance
   before implementation detail is locked.

12.3 Supersession/deepening map

Revision XII deepens Technical VIII historical architecture, Technical IX property/finance/evidence, Technical X market-
territory/workforce and Technical XI project execution. Existing DatedHistoricalEvent, LegalRegime, PropertyRight, Agree-
ment/Obligation, Transaction, Shipment/Route, Evidence/Message, Insurance/Claim, Business/Organization, Project/Task,
Person/Household and Inventory authorities remain controlling. New conceptual names are integration responsibilities, not
permission to duplicate current repository state.

RESEARCH BASIS - REVISION XII

Technical XII uses the same research basis as Canon XXIX. Historical records are authoring inputs and acceptance anchors;
repository evidence remains authoritative for what is currently implemented in Unity.

  1. National Archives - Homestead Act / homestead case files - Public-land entry, residence/improvement and patent
    evidence.

     https://www.archives.gov/milestone-documents/homestead-act

  2. National Archives - Dawes Act - Federal allotment chronology; detailed local implementation remains a research hold.

     https://www.archives.gov/milestone-documents/dawes-act

  3. National Archives - Interstate Commerce Act - 1887 rail-rate, posting and complaint framework.

     https://www.archives.gov/milestone-documents/interstate-commerce-act

  4. National Archives - Bankruptcy history - 1878 repeal and 1898 federal bankruptcy chronology.

     https://www.archives.gov/publications/prologue/2014/fall/bankruptcy

  5. National Park Service - 1868 Fort Laramie Treaty / Black Hills - Treaty geography and Black Hills chronology.

     https://www.nps.gov/places/1868-treaty-field-area.htm

  6. National Park Service - Windmills - Water-pumping and railroad windmill context.

     https://www.nps.gov/articles/windmills.htm

  7. National Park Service - Water in the West / Desert Land Act - Western water/reclamation context.

     https://www.nps.gov/articles/2-water-in-the-west.htm

  8. National Park Service - Marquis de Mores - Dakota refrigerated-meat experiment.

     https://home.nps.gov/thro/learn/historyculture/marquis-de-mores.htm





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 31 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                          12



  9. National Park Service - Kinkaid settlement context - 1904 Nebraska Sandhills land regime.

     https://www.nps.gov/niob/learn/historyculture/people.htm

10. National Park Service - Natural ice / icebox - Ice harvesting/storage economy.

     https://www.nps.gov/articles/000/the-icebox-the-predecessor-of-modern-refrigeration.htm

11. National Park Service - Minneapolis milling / Washburn explosion - 1878 milling disaster and regional processing context.

     https://www.nps.gov/miss/learn/historyculture/river-of-hisory-chapter-6.htm

12. North Dakota Studies - Great Dakota Boom - Settlement boom chronology.

     https://www.ndstudies.gov/gr4/early-settlement-north-dakota/part-2-great-dakota-boom/section-5-great-dakota-boom

13. North Dakota Studies - Drought - Late-1880s/1890s agricultural stress.

     https://www.ndstudies.gov/gr8/content/unit-iii-waves-development-1861-1920/lesson-1-changing-landscapes/topic-5-blizzards-floods-and

     -drought/section-2-drought

14. North Dakota Studies - Northern Pacific - Rail chronology, Bismarck and Panic-era interruption.

     https://www.ndstudies.gov/gr8/content/unit-iii-waves-development-1861-1920/lesson-1-changing-landscapes/topic-6-railroads/section-2-n

     orthern-pacific-railway-north-dakota

15. North Dakota Studies - Bonanza farms - Seasonal scale and mechanization.

     https://www.ndstudies.gov/gr8/content/unit-iii-waves-development-1861-1920/lesson-2-making-living/topic-3-farming/section-4-bonanza-f

     arms

16. North Dakota Studies - Rural telephone - Private farm lines and later farmer networks.

     https://www.ndstudies.gov/gr8/node/382

17. South Dakota State Historical Society - Agricultural context - Drought, farm systems, grading and rural development.

     https://history.sd.gov/preservation/docs/SDAgriculturalContext2013.pdf

18. South Dakota State Historical Society - Water towers/waterworks - Artesian and municipal water-project evidence.

     https://history.sd.gov/preservation/docs/SDWaterTowers.pdf

19. South Dakota State Historical Society - Hutterite colonies - Communal settlement/ownership context.

     https://history.sd.gov/preservation/docs/HutteriteColonies.pdf

20. South Dakota State Archives - Territorial Articles of Incorporation - Corporate-form and business-type calibration.

     https://history.sd.gov/archives/AI.aspx

21. Smithsonian National Postal Museum - Mail order - Catalog/express constraints.

     https://postalmuseum.si.edu/mail-order

22. Smithsonian National Postal Museum - Railway Mail - Railway-mail service mechanics.

     https://postalmuseum.si.edu/exhibition/about-postal-operations-transportation/railway

23. Smithsonian National Postal Museum - Money orders / transactions - Postal financial services.

     https://postalmuseum.si.edu/exhibition/america%E2%80%99s-mailing-industry-impacting-lives/conducting-transactions

24. Smithsonian National Postal Museum - Catalogs and RFD - Catalog chronology and rural delivery.

     https://postalmuseum.si.edu/exhibition/america%E2%80%99s-mailing-industry-industry-segments/catalogs-and-the-mail-order-industry

25. Library of Congress - Standard Railway Time - 1883 time standardization.

     https://www.loc.gov/item/today-in-history/november-18/

26. Library of Congress / Sanborn maps - Built-form and fire-risk calibration.

     https://guides.loc.gov/fire-insurance-maps/sanborn

27. Smithsonian / NMAH - Barbed wire - 1874-era fencing transformation.

     https://americanhistory.si.edu/collections/object/nmah_856978

28. Smithsonian Magazine - 1872 horse epizootic - Transport/draft-animal shock.

     https://www.smithsonianmag.com/history/how-horse-flu-epidemic-brought-19th-century-america-stop-180976453/

29. North Dakota State Historical Society - Bismarck-Deadwood stage - Temporary freight/stage corridor evidence.

     https://www.history.nd.gov/historicsites/bdst/index.html

30. North Dakota State Historical Society - Buffalo bones - Finite bone-collection trade.

     https://www.history.nd.gov/publications/buffalo-bones.pdf

31. U.S. Geological Survey - Dakota artesian water - Artesian basin chronology and physical evidence.

     https://www.usgs.gov/publications/artesian-water-supply-dakota-sandstone-north-dakota-special-reference-edgeley



                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 32 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                          13



32. USDA / FSIS - Bureau of Animal Industry history - 1884 animal-disease institution.

     https://www.fsis.usda.gov/about-fsis/history

33. CFTC - U.S. futures-market history - Chicago/Minneapolis grain futures context.

     https://www.cftc.gov/About/HistoryoftheCFTC/history_precftc.html

34. Federal Reserve History - Gilded Age banking panics - Panic propagation and liquidity context.

     https://www.federalreservehistory.org/essays/banking-panics-of-the-gilded-age

35. Harvard Library - R.G. Dun credit reports - Commercial credit-information network.

     https://library.harvard.edu/collections/rg-dun-company-credit-reports

36. Federal Reserve Bank of Richmond - Building and loan associations - Mutual home-finance structure.

     https://www.richmondfed.org/publications/research/economic_brief/2019/eb_19-01

37. Dakota Territory / North Dakota session laws - Chattel mortgage, estray, tax, warehouse and related legal chronology.

     https://ndlegis.gov/research-center/session-laws

38. Sioux City Public Museum - Stockyards - Livestock terminal, commission and industrial-cluster development.

     https://www.siouxcitymuseum.org/history-website/stock-yards

39. History Nebraska - South Omaha stockyards - Livestock-market geography and packing development.

     https://history.nebraska.gov/the-birth-of-the-south-omaha-stockyards/

40. History Nebraska - Jobbers Canyon - Regional wholesaling/jobbing system.

     https://history.nebraska.gov/big-ugly-red-brick-buildings-the-fight-to-save-jobbers-canyon-daniel-d-spegel/

41. History Nebraska - Samuel M. Chapman papers - Collections for distant manufacturers and secured debt.

     https://history.nebraska.gov/collection_section/samuel-m-chapman-1839-1907-guide-to-microfilm-rg2974-am/

42. History Nebraska - Francis E. Newton papers - Implement-dealer supplier network and terms.

     https://history.nebraska.gov/collection_section/francis-e-newton-rg4269-am/

43. U.S. Census - 1900 enumerator instructions - Tenant/share farmer classification.

     https://www.census.gov/programs-surveys/decennial-census/technical-documentation/questionnaires/1900/1900-instructions.html

44. USDA Census of Agriculture historical tables - South Dakota farm-tenure context.

     https://www.nass.usda.gov/AgCensus/archive/

45. NOAA / National Weather Service history - 1870 weather-service chronology and forecast networks.

     https://www.weather.gov/timeline





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 33 of 170

LAND & LEDGERS / REVISION XII / TECHNICAL IMPLEMENTATION BIBLE                                                                          14


PRESERVED TECHNICAL REVISION LAYERS

The complete prior Technical Implementation Bible follows unchanged. The table of contents links each preserved revision
layer for faster navigation.





                                                    LAND & LEDGERS

## TECH-XIV — original PDF page 34 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            1



       LAND & LEDGERS
   TECHNICAL IMPLEMENTATION
        BIBLE - REVISION XI

  Construction & Project Execution Architecture: Estimates, Work
      Packages, Capacity, Billing, Claims, Surety & Recovery

                                      Revision date: October 5, 2026

             Source baseline: Technical Implementation Bible Revision X - September 26, 2026

                  Canon compatibility target: Canonical Project Bible Revision XXVIII

  New source integrated: Construction, Contracting & Project Delivery Research Passes I-III - October 2026

          Status: Active integration - repository audit remains required before implementation claims


 PRESERVATION GUARANTEE

  REVISION XI preserves Technical Implementation Bible Revision X in full except where this integration
   explicitly adds, deepens, clarifies, corrects or supersedes technical guidance. The complete Revision X
  document follows unchanged as the preserved historical layer. Canonical Project Bible Revision XXVIII
   controls intended behavior. Conceptual names below describe target responsibilities and must not be
   treated as proof that matching Unity classes already exist.

 PURPOSE

   Revision XI converts the three construction research passes into implementation architecture while reusing
  the existing Agreement -> Obligation, Project -> WorkPackage -> Task, Evidence, CommercialClaim,
   PropertyRight, Transaction, Credit/Contingent Exposure, Insurance and LegalRegime authorities. The goal is
   to deepen construction without creating a second economic simulation.

 REVISION XI ARCHITECTURE LOCKS

    - Do not implement authoritative ConstructionPoints or BuildingProgress currency. Physical
    WorkPackage/Task results and Building/site state remain truth.

    - Estimate, Proposal/Bid, accepted Agreement, Project Baseline, Current Forecast and Actual remain distinct
     persisted records/views.

    - Construction scope must be decomposable into measurable quantities and responsibility-bearing
    WorkPackages, while animation-level hand motions remain presentation.

    - Material/labor/equipment procurement responsibility must be resolved from
    Agreement/Obligation/WorkPackage data rather than assumed globally.

    - Crew throughput is resolved from Tasks, capabilities, simultaneous work fronts, reservations, supervision
    and method; headcount alone cannot multiply speed linearly.

    - Project payment state distinguishes earned/certified/billable, invoiced/claimed, receivable/payable,
     retained/withheld, disputed and settled amounts where the Agreement needs those states.

    - Backlog/committed capacity is derived from future reservations/commitments and liquidity burden, not a
     scalar score.

    - Downstream subcontractor/supplier obligations remain separate from owner-prime settlement.

    - Construction defect/nonconformance evidence is separate from responsibility attribution; rework creates
   new real Tasks and costs.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 35 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            2




    - CommercialClaim/Property encumbrance/contingent surety relationships are reused for lien, default and
    bond behavior; no construction-only legal ledger.

    - Agreement termination may replace the executing contractor without deleting the Project or completed
     physical work.

    - Fire/casualty mutates actual physical state first; insurance and risk-of-loss resolution operate afterward
    through existing claim/Agreement authorities.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 36 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            3


 PART I - ESTIMATE, BID, AGREEMENT &
 RESPONSIBILITY MODEL

  1.1 Estimate data is actor knowledge
  Represent a construction estimate as source-labeled quantities/rates/quotes/assumptions rather than one
  opaque total where practical. Useful conceptual components include scope/work-package reference,
   quantity/unit, material source/grade, labor method, expected worker-time, equipment/transport burden,
  subcontract quote, contingency/risk allowance, quote validity, confidence and source evidence. Exact
  schema follows repository audit.

  1.2 Bid/Proposal does not mutate the estimate
  A Bid/Proposal exposes commercial terms to a counterparty and may intentionally differ from internal
  expected cost. Underbidding must remain possible. Acceptance creates/activates the
  Agreement/Obligations; it must not revise the internal estimate so the project later appears to have been
   correctly priced.

  1.3 Responsibility allocation
  Extend/reuse Agreement/Obligation/WorkPackage fields so responsibility can be attributed for labor,
   material purchase, freight/hauling, tools/equipment, temporary works, permits/inspection where applicable,
   specialist installation, owner-provided inputs and risk/security obligations. One Project may mix owner-,
   prime-, subcontractor- and direct-supplier responsibilities.

  1.4 Baseline / Forecast / Actual remains immutable-history oriented
   Baseline scope/budget/schedule is the approved snapshot. Current Forecast updates from new quotes,
   quantities, discovered conditions, schedule/resource state and accepted amendments. Actual is
   event-derived from executed Tasks, consumed lots/assets, invoices, payments and accepted outcomes.
   Forecast updates never destructively rewrite the Baseline.

  1.5 Typed estimate uncertainty
  Where useful, expose uncertainty by cause: Quantity, UnitCost/Quote, Productivity, Schedule/Dependency
  and Collection/Payment. These may remain read-model dimensions over existing evidence rather than
   requiring a new runtime enum if repository structures already cover the need.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 37 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            4


 PART II - WORK PACKAGE, QUANTITY, CREW &
 CAPACITY EXECUTION

  2.1 WorkPackage requirements
  A construction WorkPackage should be able to reference meaningful scope quantity, method/recipe,
   required capabilities, labor demand, minimum/maximum useful concurrency, equipment/space
   reservations, material requirements/lots, prerequisites, environmental tolerances, location, responsible
  Agreement party, cost attribution and acceptance evidence. Do not require every field for every package.

  2.2 Method-driven material/labor grammar
   Building definition plus chosen method should derive required quantities and compatible task methods.
  Technology/access changes should swap requirements or feasible methods - for example dimensional
   lumber/nails versus heavier timber work - rather than applying generic speed percentages.

  2.3 Crew throughput resolver
  Resolve productivity from the actual scheduled Persons, required capability coverage, working
   lead/supervision where relevant, tools/equipment, available parallel work fronts, congestion and
  environment. Additive worker-speed formulas should be avoided. A helper may improve throughput on
   suitable tasks without satisfying specialist capability by themselves.

  2.4 Active-work interval versus blocked/waiting state
   Persist enough Project/WorkPackage execution history to distinguish Scheduled, ActiveWork,
   Blocked/Waiting, Suspended, CompletedPhysical, Accepted and other relevant states. Blocking reason may
   reference missing material, supplier shipment, finance, Person/equipment reservation, environmental
   condition, inspection/decision or predecessor work.

  2.5 CommittedCapacityReadModel
  Derive contractor committed capacity from existing future Task/Activity reservations, WorkPackage
  windows, Persons/capabilities, exclusive assets, supervisor attention and expected liquidity burden. Use it in
   qualification and scheduling instead of storing one BacklogScore. Caches are permitted if invalidated by
   underlying state.

  2.6 Project progress is a read model
   UI may expose overall completion, but must be calculated from the actual package state/weighting
   appropriate to that project. It may not drive Task completion, cash settlement or objective truth by itself.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 38 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            5


 PART III - PROJECT FINANCIAL STATE & CONTRACTOR
 LIQUIDITY

  3.1 ProjectFinancialReadModel
  Expose the minimum set needed for decisions without creating duplicate money authority. Recommended
   outputs: Original Contract/Baseline Value, Approved Change Value, Committed Cost, Spent/Consumed to
  Date, Forecast Cost to Complete, Forecast Final Cost, Billable/Certified Amount, Invoiced/Claimed Amount,
   Receivable/Payable, Retained/Withheld/Disputed Amount and Settled Cash. Values derive from Agreement,
   Obligation, Transaction, Inventory, Payroll/Task and Claim authorities.

  3.2 Cash-needed-before-next-receipt
  Add a contractor/project liquidity read model that projects obligations expected before the next plausible
  customer receipt: payroll, supplier bills, subcontractors, freight/equipment, debt service and other
  commitments minus available cash, approved undrawn credit and legitimate incoming receipts. This is a
  read model; it may not create a restricted ProjectCash pool unless a real Agreement/account restriction
   exists.

  3.3 Progress billing workflow
  Agreement terms can trigger eligibility/certification based on delivered material, milestone, measured work,
  completion or other terms. Eligibility is not cash. A bill/claim can create receivable/payable state; settlement
   creates the cash transaction. Withholding/retention is represented as an amount/right attached to the
   underlying obligation, not as an unexplained percentage haircut.

  3.4 Downstream obligations
  Prime receipts and supplier/subcontractor payments are separate Transaction/Obligation chains. Never
  cascade Settlement from owner -> prime into automatic Settlement for prime -> supplier. Internal
   control/accounting can report project-attributed cash movement without inventing a second ledger.

  3.5 Resource-cost attribution without fake transactions
  Owner labor, family labor, donated/community labor, vertically integrated material, owned equipment use
  and internal hauling can produce project cost/opportunity-cost attribution without fabricating third-party
  cash sales. Consolidated reporting continues to eliminate synthetic intercompany profit while preserving
   real physical and business-level cost.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 39 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            6


 PART IV - CHANGE, NONCONFORMANCE, DELAY &
 CLAIM ARCHITECTURE

  4.1 Amendment/ChangeOrder
  Reuse the existing persistent Amendment/ChangeOrder model. Construction changes should carry initiating
   party/source condition, affected scope/WorkPackages, quantity/material/method change, price delta or
   pricing basis, schedule/time delta, risk/responsibility change, authority/consent, effective date and resulting
   obligations. The original terms remain queryable.

  4.2 Defect / Nonconformance evidence
  Use Evidence plus Task/WorkPackage/Building condition history to record observed nonconformance.
   Store/derive reported symptom, affected work/asset, inspection evidence, current physical severity,
  known/unknown cause, acceptance impact and linked corrective requirement. Do not require omniscient
   responsibility at detection.

  4.3 Rework execution
  Recoverable construction failure creates follow-up TaskInstances/WorkPackages that reserve real Persons,
   materials, equipment and time. Link the corrective work to the originating package and Claim/Agreement
   decision so cost responsibility can be attributed separately from the physical repair.

  4.4 Delay evidence and entitlement
  Track material delay causes through ordinary state/events. Where consequences matter, a read/workflow
  can compare delay cause and Agreement terms to determine extension, damages, escalation or no
   contractual consequence. Do not encode Late = ContractorBreach.

  4.5 CommercialClaim reuse
   Construction payment disputes, defective work, extras, delay damages and termination claims use the
   existing CommercialClaim lifecycle linked to Agreement/Obligation/PropertyRight/Transaction evidence.
   Negotiation, expert/referee/architect determination, settlement and legal proceeding remain possible
   resolution paths according to contract and LegalRegime.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 40 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            7


 PART V - LIEN, SURETY, DEFAULT, REPLACEMENT &
 CASUALTY

  5.1 Construction lien / property encumbrance integration
  Do not invent a universal LienStatus bool on Building. When dated law permits a construction/material
   claim, create/link the claimant's CommercialClaim and the applicable PropertyInterest/Encumbrance against
  the correct improvement/property interest, with notice/timing/enforcement data supplied by LegalRegime.
   Title, sale, financing and diligence query the resulting right/claim state.

  5.2 Surety / performance security
  Represent performance bond/surety as an Agreement/contingent financial obligation referencing principal
   contractor, obligee/customer, surety, covered Project/Agreement, coverage/penal amount, covered default,
  term, notice/claim requirements and resolution. A bond must not auto-complete work or auto-pay
  immediately on Project failure.

  5.3 Default / termination workflow
  A material default can create Notice -> Cure window -> response -> waiver/amendment/termination/claim
  according to Agreement and LegalRegime. Terminating an Agreement detaches or ends that contractor's
   obligations/authority but does not delete the Project, Building state, on-site lots or historical Tasks.

  5.4 Replacement contractor continuity
  Reuse the existing Project. Create an inspection/re-estimate path over remaining acceptable work,
   defective work, materials, stabilization and remaining scope; then create a new Proposal/Agreement for the
  replacement actor. Historical Baseline and prior contractor Actual remain preserved for variance/claim
   analysis.

  5.5 Ownership/control of site materials after default
  Resolve from actual purchase/title/custody/security/Agreement state. Do not transfer unrelated contractor
   assets globally. Site material possession can differ from title and may be subject to contractual or legal
   rights.

  5.6 Casualty and builders-risk handling
   Fire/accident first mutates Building, WorkPackage, MaterialLot and Asset condition/location. Then
  Agreement/RiskOfLoss policy plus InsuranceClaim determines economic consequences. Insurance payment
   is later settlement; physical destruction remains unless new Tasks rebuild.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 41 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            8


 PART VI - PUBLIC PROCUREMENT & QUALIFICATION
 WORKFLOWS

  6.1 Procurement case
   Public/institutional work can reuse a Procurement/Plan/Project workflow with authored legal/procedural
  requirements: plans/specifications approval, advertisement window, bid submission, bidder qualification,
   appropriation/cost ceiling, award decision, required security, certification, payment limits/retention and final
  acceptance. Exact terms are dated authoring/legal data.

  6.2 ContractorQualificationReadModel deepening
  Add required security/bonding feasibility and committed capacity/liquidity to the existing evidence set of
  accepted work, quality/history, references, specialist Persons, equipment/assets, geography and backlog.
   Qualification is evidence-limited to the counterparty and never becomes an RPG level.

  6.3 Architect/superintendent/project administrator authority
  Where an Agreement appoints an architect, superintendent, engineer or other administrator, represent the
  Person/Business relationship and delegated contract authority explicitly: inspection, interpretation,
   certification, approved routine changes or recommendations within scope. Title alone does not grant
   authority.

 PART VII - REGRESSION & ACCEPTANCE FIXTURES

    Fixture                 Required result

   UNDERBID                  Bid below the contractor internal expected cost. Accept. Confirm Agreement price does not
                                 automatically rise when Actual cost exceeds the bid.

    DELIVERY STRUCTURE        Build equivalent structure once with owner-procured separate trades and once with prime-contractor
                                 supplied materials. Confirm different obligations/cash flows while physical result can converge.

    BASELINE IMMUTABILITY     Approve a project, then authorize an extra. Confirm original Baseline remains unchanged, Amendment
                                            is appended and Forecast/Actual reflect the new scope.

    ELAPSED VS ACTIVE         Block framing lumber for ten days. Confirm calendar duration increases while ActiveWork intervals do
                                not fabricate ten days of labor.

   CREW DIMINISHING        Add helpers beyond available work fronts. Confirm throughput does not scale linearly and exclusive
   RETURNS                    skilled/equipment reservations still bind.

   PAYMENT THRESHOLD       Perform work before the contract billing milestone. Confirm physical progress occurs without
                                unauthorized cash/receivable creation.

    PROFITABLE BUT ILLIQUID    Give a profitable contractor enough backlog to create payroll/material collision before next receipt.
                               Confirm liquidity emergency can occur without rewriting job profitability.

   DOWNSTREAM            Owner pays prime in full; prime does not pay supplier. Confirm supplier payable remains and a
   NONPAYMENT               dated-law lien/claim can arise without reversing owner payment.

    DEFECT AFTER            Occupy a usable building with a documented defect. Confirm Occupancy, physical completion,
   OCCUPANCY                acceptance, Claim and final settlement can remain distinct.

   REWORK COST              Force defective plaster/roof work. Confirm corrective Tasks consume new material/time and original
                                resources are not restored.

   ABANDONMENT             Stop contractor mid-project. Confirm partial Building/site and materials persist; Project can continue
                              under replacement Agreement.

    SURETY DEFAULT            Default a bonded public contractor. Confirm work stops/persists physically; surety exposure/claim
                              appears but no instant completion or automatic full payment occurs.

   WEATHER SELECTIVITY      Freeze ground/mortar conditions while interior work is available. Confirm only incompatible Tasks
                                   block; no global ConstructionDisabled state.

    CASUALTY                 Burn part of a covered project. Confirm physical loss persists, insurance claim is separate and payment
                             does not resurrect prior progress.

    SAVE LOAD                 Save/load during active ChangeOrder, retention, supplier claim and replacement transition. Confirm no
                                  duplicate billing, liens, Tasks, receipts or claim events.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 42 of 170

LAND & LEDGERS / REVISION XI / TECHNICAL IMPLEMENTATION BIBLE                                                            9


 PART VIII - IMPLEMENTATION PRIORITY, DATA HOLDS &
 SUPERSESSION

  8.1 Priority after repository audit
    - P0 - Preserve Project/Agreement/Task/Building/Transaction authority boundaries and remove any remaining
     construction path that posts cash/progress without real obligations or work.

    - P0 - Make baseline/change/forecast/actual and progress-billing state save/load idempotent before adding
    more project types.

    - P1 - Extend WorkPackage quantity/method/responsibility and crew/capacity scheduling over existing
     Task/Activity reservations.

    - P1 - Add ProjectFinancial/ContractorLiquidity read models from authoritative obligations/transactions rather
    than new balances.

    - P1 - Implement default/termination/replacement continuity without deleting partial physical state.

    - P2 - Integrate construction Claim/lien/surety/public-procurement workflows over LegalRegime,
    PropertyRight and contingent-obligation systems.

    - P2/P3 - Build evidence-backed historical calibration datasets for wages, productivity, material quantities,
     project costs and public/private contract terms.

  8.2 Historical/data holds
  Keep data-driven and source-versioned: local construction wage series; crew productivity by method;
   project-type quantity takeoffs; lumber/stone/brick/fixture availability and delivered price; exact lien
   notice/perfection/enforcement procedure; performance-bond underwriting/premiums; builders-risk
  insurance terms; public bid/appropriation thresholds; architect/superintendent fee/custom practice; and
   local contractor payment/change-order conventions.

  8.3 Supersession/deepening map
    - Revision VI Agreement -> Obligation, Amendment, Claim and Project Baseline/Forecast/Actual architecture
      is retained and deepened by construction-specific estimate, responsibility, billing, default and recovery
     behavior.

    - Revision V contractor qualification and ActiveWork evidence are retained and deepened by
     committed-capacity/liquidity/security inputs.

    - Revision II Task/Project/contractor execution remains authoritative; WorkPackage quantity/method and
     construction failure paths now receive explicit detail.

    - Revision IX/X Property, Procurement, Delegation, MarketTerritory and workforce authorities remain
    unchanged and are reused for material supply, premises, hiring, public demand and contractor operations.

    - Repository evidence remains authoritative for what exists in Unity. No conceptual state name in Revision XI
      is permission to duplicate an existing source of truth.

 END OF REVISION XI INTEGRATION

  The complete Technical Implementation Bible Revision X follows unchanged as the preserved baseline and
  remains authoritative except where Revision XI explicitly adds, deepens, corrects or supersedes technical
  guidance. Canonical Project Bible Revision XXVIII controls intended gameplay behavior.





                                                     LAND & LEDGERS

## TECH-XIV — original PDF page 43 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                1

          LAND & LEDGERS
  TECHNICAL IMPLEMENTATION BIBLE -
              REVISION X
      Town, Catchment & Workforce Architecture: Market Territories, Economic
    Participation, Family Labor, Business Capabilities & Demand-Driven Generation

                                            Revision date: September 26, 2026
                    Source baseline: Technical Implementation Bible Revision IX - September 26, 2026
                        Canon compatibility target: Canonical Project Bible Revision XXVII
                  New source integrated: Historical Research Findings - September 26, 2026
                                                     Status: Active integration

 PRESERVATION GUARANTEE
 REVISION X preserves Technical Implementation Bible Revision IX in full except where this integration explicitly adds,
 deepens, corrects or supersedes technical guidance. The complete Revision IX document follows unchanged as the
 preserved historical layer. Canonical Project Bible Revision XXVII controls intended behavior. Numeric catchment ranges,
 workforce percentages, staffing bands and town-business observations from the new source are authoring/calibration
 data unless explicitly promoted.
 PURPOSE
 Revision X translates the September 26 Historical Research Findings into implementation architecture for market
  territories, population/economic read models, workforce relationships, demand-driven business generation and long-run
  calibration. It closes the major technical risk exposed by the research: treating TownPopulation as a generator variable
 can create impossible labor forces, under-populated commercial districts and false business limits. The new architecture
 derives market and workforce views from stable Persons, Households, Farms, routes, institutions, businesses and actual
 work.
 REVISION X ARCHITECTURE LOCKS
   - Settlement resident count, rural service population and effective market population are derived read models over stable
    world entities; none is an independent mutation authority.
   - MarketTerritory is derived from actual reachable rural households/farms, routes, generalized travel cost, competing
    centers, institutions and observed/contractual trade relationships. Never store RuralPopulationMultiplier.
   - Economic participation is separate from EmploymentRelationship. A Person may be economically active as
    proprietor/operator or family enterprise labor without being a wage employee.
   - Labor demand is task-time by date/season and simultaneous concurrency. FTE and headcount are outputs, not staffing
    slots.
   - Farm labor relationship types must distinguish operator, family enterprise labor, permanent employment,
    seasonal/casual employment and contract/service work.
   - Business generation consumes demand drivers and feasible capabilities; it may not execute BusinessCount =
   TownPopulation / X.
   - CommercialCapability/BusinessOperation remains distinct from Business identity so one firm can provide several
    services.





                                                LAND & LEDGERS

## TECH-XIV — original PDF page 44 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                2


   - Premises resolution continues to allow house/barn/yard/mixed/shared/mobile work when the real task requirements
    permit it.
   - Historical settlement/business rosters and workforce tables live in evidence-backed authoring datasets with
    date/geography/confidence, never scattered constants.
   - Regression must include high-variance small-town examples so future tuning cannot reintroduce monotonic population
    formulas.
 PART I - SETTLEMENT POPULATION & MARKET-TERRITORY READ
 MODELS
 1.1 SettlementPopulationReadModel

 TECH LOCK - Do not store one SettlementPopulation value and reuse it as resident count, workforce size, customer
 base and business-support capacity.

 Expose distinct derived values such as BuiltUpResidentCount, RuralServicePersonCount (or confidence/range where
 knowledge is incomplete), EffectiveMarketPopulation, TemporaryPresentCount and other scenario-specific views.
 BuiltUpResidentCount resolves from Person PrimaryResidence/settlement geography. Effective market measures are
  analytical views over reachable actors and relationships; they do not create Persons or demand.
 1.2 MarketTerritory graph/query
 Implement/reuse a query over stable Farm, Household, Settlement, Route/Edge, Crossing/Ferry/Bridge and
  Business/Institution nodes. The query evaluates reachability, generalized travel time/cost, route condition/seasonality,
 service frequency, competing centers, existing supplier relationships and relevant production/output. A territory may be
 fuzzy or capability-specific: the market area for a blacksmith need not equal the market area for a newspaper or
 physician.
 1.3 No stored multiplier or rigid radius
 The source's catchment brackets are authoring envelopes only. Do not implement RuralPopulationMultiplier=7,
 ServiceRadius=8Miles as the truth or other shortcuts that fabricate demand. Spatial caching is allowed for performance if
   it is invalidated/recomputed from the underlying graph when routes, settlement, competition or services change.
 1.4 EconomicParticipationReadModel
 For each Person/settlement/district, derive separate Resident, EconomicallyActive, PrimaryOccupation and
 WageEmployee flags/views. EconomicallyActive can include business owners/operators, family enterprise labor and
 other meaningful productive work. WageEmployee must resolve only from a legitimate employment relationship.
 PART II - DEMAND-DRIVEN SETTLEMENT & BUSINESS
 GENERATION
 2.1 Generation order

 TECH LOCK - Prefer causal generation order: geography/rights -> rural farms/households/output -> routes/access
  -> settlement catchment -> institutions/transients -> demand -> businesses/capabilities -> staffing.

 This order allows a 45-Person town to support several firms when it serves a large rural district, while preventing a larger
 but bypassed settlement from receiving businesses solely because its resident count crossed a threshold.
 2.2 BusinessOpportunity / DemandDriver model
 Create or extend data-driven demand drivers that query real world state. Examples from the source: GeneralStore
 demand <- households + purchasing power + competing merchants + accessibility; Blacksmith <- horses + wagons +
 machinery + freight traffic + competing capacity; GrainElevator <- grain output + rail access + storage + competing
 buyers; Hotel <- transient arrivals + journey duration + alternatives; Lumber <- construction + repair + farm demand +
  transport; Bank <- deposits + borrowing demand + commercial volume + available capital.





                                                LAND & LEDGERS

## TECH-XIV — original PDF page 45 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                3


 DemandDriver output is evidence/opportunity pressure. It cannot post revenue, create inventory or spawn a Business
  directly. Entrepreneur/NPC formation still requires a Person/organization, capital, rights, premises, equipment, information
 and a real decision.
 2.3 InstitutionGeneratedDemand
  Institutions should emit/derive concrete service needs and activity flows rather than a generic RoleBonus.
 Courthouse/legal records, depot freight/passenger traffic, mine procurement, mill fuel/maintenance and similar state feed
 ordinary ProcurementNeed, Journey, Activity, Agreement and Transaction systems.
 2.4 SettlementRoleProfile is authoring context, not a tier
 AgriculturalServiceTown, Railhead/Junction, AdministrativeCenter, MiningNode, RiverCenter, IndustrialNode and similar
 descriptors can seed priors, institutions and likely demand drivers. Runtime businesses remain emergent. Do not use
 RoleProfile as a deterministic roster template.
 PART III - BUSINESS IDENTITY, COMMERCIAL CAPABILITIES &
 PREMISES
 3.1 Business vs CommercialCapability
 Reuse/extend BusinessOperation/Capability structures so one Business can expose multiple commercial capabilities - for
 example general goods + groceries + implements + post function + produce purchasing - without creating synthetic
 subsidiary businesses. Each capability references the actual required stock, task methods, equipment, authority,
 agreements and service space.
 3.2 Capability-driven premises
 Continue Revision IX FunctionalSpace doctrine. A productive establishment can resolve to a dedicated storefront, a room
  in a house, finished barn space, yard/shop area, shared premises or a mobile/route operation. Premises type is selected
 from task and equipment requirements, not from BusinessType -> RequiredBuilding prefab.
 3.3 Business density is not an allocation formula
  Historical roster datasets should inform priors/distributions and QA. Never compute store/blacksmith/hotel counts by
 resident population division. Generator code should tolerate non-monotonic observations and wide small-town variance
 without logging them as errors.
 PART IV - WORKFORCE RELATIONSHIPS, FAMILY LABOR &
 WORKER-TIME
 4.1 WorkRelationship taxonomy

 TECH LOCK - Do not force all productive work into EmploymentRelationship.

 Use one authoritative relationship/event model capable of distinguishing at least: Farm/BusinessOperator,
 FamilyEnterpriseLabor, PermanentEmployment, SeasonalOrCasualEmployment and ContractOrServiceWork.
 EmploymentRelationship remains authoritative for wage employment; family work and contracts reference their own
 compensation/obligation rules rather than fake payroll.
 4.2 LaborDemandPlan
 Represent demand as TaskFamily/Task, required capability, date/season window, labor-hours/days, minimum concurrent
 Persons, peak concurrency, location, equipment/space reservations and flexibility/deadline. Actual Persons are
 scheduled through shared Activity/Task authority. This lets one farm report 1.2 annual FTE while still needing seven
 simultaneous workers during harvest.
 4.3 FTE and headcount are read models
 Annualized FTE, average staff and peak headcount are analytics derived from executed/planned worker-time. They must
 not become causal WorkerSlot counts that fabricate Persons or suppress seasonal hiring.





                                                LAND & LEDGERS

## TECH-XIV — original PDF page 46 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                4


 4.4 Primary occupation is separate from current tasks
 OccupationProfile/PrimaryOccupation remains a livelihood/classification read model. Current tasks may include mixed
 farm duties, household production, secondary trade work or short contracts without rewriting the Person into
 ChickenFarmer, WheatFarmer or another over-specialized class.
 4.5 Household/family labor persistence
 Record economically material family work through task/activity history and the household/business relationship needed
  for accounting and capability growth. Do not require a wage transaction merely to acknowledge the work. Demographic
  calibration should be able to report both census-like gainful employment and broader productive participation.
 PART V - AUTHORING DATASETS & CALIBRATION
 5.1 HistoricalSettlementObservation
 Add/use evidence records for dated settlement observations: resident population, service population where known,
  institutions, route/rail role, business roster, surrounding county/rural context, source type, promotional-bias caution and
 confidence. Observations such as Menoken, Sterling, Alexandria, Elk Point, Bismarck and De Smet remain records, not
  rules.
 5.2 BusinessPrevalenceDataset
 The next research target should store distributions across many settlements and population/role/year brackets. Generator
  calibration should consume ranges/quantiles conditional on market territory and role, not a single
 establishments-per-1,000 constant.
 5.3 WorkforceCalibrationEnvelope
 Store source-labeled historical anchors separately from bridge estimates. The latest industry-percentage table and
 catchment table are bridge-estimate authoring envelopes and must carry uncertainty/version metadata. Separate
 town-proper and rural-district distributions should be supported rather than requiring one blended mix.
 5.4 Dated hard anchors from the source

   Evidence                                                Technical use

  De Smet, Feb. 1880: town 50; post-office population served    QA anchor proving service population can far exceed resident count; not a
   500                                                                     multiplier

   1880 Dakota Territory occupation table: agriculture 49.3% of     Regional/context evidence with explicit caution about territory mix and
   57,844 workers                                                 possible agriculture undercount

   1890 South Dakota: farmers + agricultural laborers ~64.0% of   Hard lower/context anchor, not a local target
    gainfully occupied males

   1900 South Dakota: agricultural pursuits 60.4% of gainful         Later-period context anchor
   occupations

   1880 manufacturing: ~3.4 workers/establishment; flour/grist       Staffing calibration evidence, not fixed worker counts
   ~2.9; sawmills ~7.4

 5.5 Latest bridge-estimate tables
 Retain the source's latest versions, not earlier message variants. They are data for calibration/testing only and should be
 versioned so later research can replace them without code changes.

   Built-up residents           Rural persons served           Approx. farms                Effective service pop.

   10                         50-150                          10-30                       60-160

   50                         150-350                         30-70                      200-400

   100                        300-600                         60-120                     400-700

   200                         500-1,000                       100-200                     700-1,200

   500                          1,000-2,000                     200-400                      1,500-2,500

   1,000                        1,500-3,000                     300-600                      2,500-4,000

   2,000                        2,500-5,000                      500-1,000                    4,500-7,000



                                                LAND & LEDGERS

## TECH-XIV — original PDF page 47 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                5


  Applicability: normal agricultural Dakota settlement/catchment approximately 1878-1890; not mining camps, railway
  division points, military posts or major river centers. Physical generation and competition must be able to produce values
 outside these envelopes.
 PART VI - 1870 OPENING AUTHORING
 6.1 Opening priors
 For the normal 55-95 Person 1870 opening, authoring should bias toward lower rural density than mature 1880s areas,
 higher agricultural participation, fewer dedicated storefronts, more house/yard/barn/mixed-premises work, thin formal
 banking and wagon/stage/ferry/river logistics. Exact business counts remain generated from the actual catchment and
 route role.
 6.2 No automatic institutions by population threshold
 Do not attach Bank, GrainElevator, LumberYard, Newspaper, Hotel or other institutions merely because resident
 population reached a threshold. Their prerequisites should come from finance depth, production/output, transport,
  transient flow, information demand, materials market and actual counterparties.
 6.3 Longitudinal transformation
 Regression/world-generation harnesses should be able to evolve sparse 1870 mixed-use economies into denser
 1880s-1900s commercial districts through real migration, rail access, institutions, building projects, business formation
 and demand rather than a template swap.
 PART VII - REGRESSION & ACCEPTANCE FIXTURES
   - DE SMET SERVICE POPULATION - Build a 50-resident settlement with a historically plausible large rural service area.
   Confirm effective market population can approach 500 without creating 450 fake town residents.
   - NO POPULATION BUSINESS FORMULA - Double resident population while holding catchment, traffic, production and
   demand constant. Confirm businesses do not automatically double.
   - ALEXANDRIA VS ELK POINT VARIANCE - Calibration harness must accept a smaller town with more general stores
    than a larger town when evidence/market territory supports it.
   - RURAL CATCHMENT PHYSICALITY - Close a ferry/bridge or improve a route. Confirm catchment membership/travel
    cost and resulting opportunity evidence can change without changing town resident count.
   - INSTITUTION DEMAND - Add a courthouse/depot/mill through real world state. Confirm related activities create
   demand through ordinary systems, not RoleBonus revenue.
   - FAMILY LABOR != EMPLOYEE - Spouse/adult child performs farm work. Confirm task time/capability/output update
    without a wage EmploymentRelationship unless one actually exists.
   - SEASONAL PEAK - Create a farm with low annual FTE but a seven-Person harvest requirement. Confirm
    scheduling/recruitment reacts to peak concurrency.
   - MULTI-CAPABILITY BUSINESS - One merchant adds a post function or implement line. Confirm one Business identity
   can expose both capabilities without duplicate cash or ownership.
   - BUSINESS != BUILDING - Operate qualified craft work from a house/barn/yard. Confirm no dedicated storefront
    requirement is fabricated.
   - CALIBRATION BOUNDARY - Change the bridge-estimate catchment dataset version. Confirm architecture remains
   unchanged and saved world entities are not silently rerolled.
 PART VIII - IMPLEMENTATION PRIORITY & RESEARCH HOLDS
 8.1 P0/P1 implementation
   - P0 - Audit every place where TownPopulation, SettlementPopulation or resident count directly produces business
    counts, workers, revenue or customers; remove synthetic paths.
   - P0 - Ensure wage employment remains distinct from proprietor/operator, family enterprise labor and contract/service
    work.


                                                LAND & LEDGERS

## TECH-XIV — original PDF page 48 of 170

LAND & LEDGERS / REVISION X / TECHNICAL IMPLEMENTATION BIBLE                                                                6


   - P1 - Implement MarketTerritory queries/read models over Farms, Households, routes and competing centers with
    deterministic save/load behavior.
   - P1 - Implement LaborDemandPlan/task-time and seasonal concurrency so staffing can emerge from actual work.
   - P1 - Add/extend CommercialCapability on Business and preserve premises flexibility.
 8.2 P2/P3 calibration tooling
   - P2 - HistoricalSettlementObservation dataset and generator QA dashboard comparing resident, catchment, workforce
   and business outputs.
   - P2 - Separate town-proper and rural-district workforce reporting plus source-labeled bridge-estimate envelopes.
   - P3 - Business-prevalence distributions from 15-30 historical settlements, then year/rail-state matrix calibration.
 8.3 Research holds from the source
 Keep open: workers-per-business by type; business prevalence per rural population; hired-vs-family farm labor change
 through time; final population/catchment/workforce/business matrix; reconciliation/provenance of computed fragments;
 exact retail percentages; and exact agricultural-generation/business-distribution probabilities. This source also records a
 100-job ranking task as pending inside that research thread; Revision X does not treat that note as architecture and does
 not supersede separate occupation work.
 PART IX - SOURCE-SECTION DISPOSITION REGISTER

   Source section                                Technical disposition

   1. Town population is not economic population     Core read-model and market-territory architecture

   2. Documented historical anchors                EvidenceRecord / HistoricalSettlementObservation inputs

   3. Town business profiles                        Generator calibration/QA dataset

   4. Business density / market territory              Demand-driven generation; reject population formulas

   5. Business size and staffing                   LaborDemandPlan + calibration evidence

   6. Farm labor                                    WorkRelationship taxonomy + seasonal worker-time

   7. Industry percentage calibration                 Versioned bridge-estimate data only

   8. Catchment model                             Versioned bridge-estimate data; physical catchment algorithm controls

   9. 1870 opening calibration                            Historical authoring priors

   10. Generator philosophy / demand drivers         BusinessOpportunity/DemandDriver architecture

   11. Glossary                                    Terminology synchronized across technical models

   12. Open questions                                Research/calibration backlog; no invented closure

 END OF REVISION X INTEGRATION
 The complete Technical Implementation Bible Revision IX follows unchanged as the preserved baseline and remains
  authoritative except where Revision X explicitly adds, deepens, corrects or supersedes technical guidance. Canonical
 Project Bible Revision XXVII controls intended gameplay behavior.





                                                LAND & LEDGERS

## TECH-XIV — original PDF page 49 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           1


         LAND & LEDGERS
  TECHNICAL IMPLEMENTATION BIBLE -
             REVISION IX

    Ghost Town Systems Integration: Property Rights, Business/Premises State,
      Perishable Lots, Recruitment, Delegation, Tenancy, Finance & Valuation

                                        Revision date: September 26, 2026

    Source baseline: Technical Implementation Bible Revision VIII - September 22, 2026; Canon compatibility target
                              updated to Canonical Project Bible Revision XXVI

                                               Status: Active integration


 PRESERVATION GUARANTEE

 REVISION IX preserves the supplied baseline in full except where this integration explicitly adds, deepens, clarifies,
 corrects or supersedes prior wording. The complete baseline document follows this integration unchanged as the
 preserved historical layer. Where a conflict exists, this integration controls. Scenario-specific values remain
 scenario data or calibration unless this integration explicitly promotes them to a general rule.

 PURPOSE

 Revision IX translates the complete Ghost Town playtest ledger into implementation architecture and acceptance
  fixtures. It preserves Revision VIII architecture unless explicitly superseded, keeps repository evidence
 authoritative for current Unity implementation, and treats all conceptual type names below as target
 responsibilities rather than claims that matching classes already exist. The integration is intentionally
  authority-first: each new UI or workflow must resolve to stable Persons, businesses, rights, inventory, agreements,
 transactions, tasks and evidence rather than creating synthetic state.

 REVISION IX MASTER DOCTRINE

  • Technical IX targets Canonical Project Bible Revision XXVI and supersedes all preserved seven-band human
    capability terminology with Ordinary -> Great -> Excellent -> Mastered presentation over continuous capability.

  • Population, households, kinship, animals, businesses, ownership, premises, inventory, debt and property rights
    remain stable-ID authorities; read models and UI must not become duplicate truths.

  • Create Business creates identity/governance; operating status is derived from real capability and commerce
    events.

  • FunctionalSpace and resource reservation must allow multi-site businesses, mixed-use buildings, station/room
    leasing and shared assets without duplicate capacity.

  • PerishableLotState and ProcessingBatch preserve provenance, condition, safety risk and transformation history.
    Mixing or processing cannot reset source history.

  • Recruitment creates awareness/leads through real channels and relationship graphs; it never instantiates
   anonymous candidates simply because the player clicked Hire.

  • Procurement uses SupplierOffer/Quote plus landed-cost decomposition. Retail pricing reads from actual cost and
    serving yield.

  • Delegated authority explicitly carries credit/commitment permissions, owner locks, policies and escalation rules.
    Managers optimize by acting on telemetry rather than modifiers.

  • Property agency/listing authority, property ownership, occupancy and commercial tenancy are distinct relationship
    records. Unauthorized physical use can exist without valid premises rights.

  • Valuation is an event-fed read model with gross enterprise value and owner equity value; business cash is separate
   and business liabilities remain causal.

  • Internal transfers and repairs preserve business-level cost information but eliminate fake consolidated
    revenue/profit.





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 50 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           2




  • Historical legal gates, prices, media markets and exact route/front positions are authoring data with
    provenance/confidence, not hard-coded global constants.

  • All Ghost Town defects become regression fixtures, especially counterparty provenance, debt continuity, labor
     allocation, rural catchment, property provenance and player-household integrity.

 INTEGRATION INDEX

  • Part 1: Canon Synchronization & Core Authorities

  • Part 2: Stable Identity, Population, Kinship & Animal State

  • Part 3: Business Creation, Premises, Space & Shared Capacity

  • Part 4: Inventory Lots, Processing & Food Safety

  • Part 5: Commerce, Procurement, Pricing & Service Capacity

  • Part 6: Recruitment, Migration, Media & Evidence

  • Part 7: Delegation, Portfolio Policies & Rival Decision Logic

  • Part 8: Property Rights, Agency, Tenancy & Residence

  • Part 9: Finance, Debt, Acquisition & Valuation

  • Part 10: Freight, Rail Promotion & Related-Party Operations

  • Part 11: Historical Authoring, Calibration & Scenario Data

  • Part 12: Regression Fixtures, Priorities & Traceability





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 51 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           3


 PART I - CANON SYNCHRONIZATION & CORE AUTHORITIES

 1.1 Capability terminology supersession

  TECH LOCK - TECH LOCK - Player-facing human capability bands are Ordinary -> Great -> Excellent ->
  Mastered. All preserved references to Abysmal/Bad/Poor/Average/Good as human capability bands are
  superseded. Underlying ActualCapability remains continuous or sufficiently granular.

 Confidence rendering remains: High = one band, Moderate = exactly two adjacent bands, Low = exactly three
 adjacent bands, insufficient evidence = Not enough information. Because four bands now exist, range generation
 must clamp cleanly and never fabricate a removed label. Good may continue as an asset/building condition
 adjective where that separate system uses it.

  Playtest provenance: GHOST-CAN-014, GHOST-CAN-015, GHOST-DES-052, GHOST-TECH-005

 1.2 No duplicate authority from new Ghost Town UI

 Create Business, Recruitment Channels, Enterprise Evaluation, Active Listings, Media Advertising, Portfolio
 Overview and similar screens are read/write workflows over existing authorities. None may own an independent
 balance, applicant pool, property inventory, population count or business-value score. The source of truth remains
 the relevant Person/Household/Business/Property/Agreement/Transaction/Inventory/Task authority.

  Playtest provenance: GHOST-DES-029, GHOST-DES-030, GHOST-DES-051, GHOST-DES-081, GHOST-TECH-001

 1.3 Operating status is derived

 Implement operating status as a derived/state-machine outcome from actual productive/service capability and
 completed/active commerce, not a button-set boolean. Useful states can include Forming, CapableButNotTrading,
 Operating, Suspended and Closed, but transitions must be caused by real state. Acceptance must prove that
 creating a Business record alone cannot satisfy an operating-business objective.

  Playtest provenance: GHOST-DES-009, GHOST-DES-029

 PART II - STABLE IDENTITY, POPULATION, KINSHIP & ANIMAL STATE

 2.1 Population remains a read model over Persons

 Do not initialize Population=14 as a separate causal truth. Permanent population is derived from identifiable
 Persons and their residence/settlement status. Temporary presence, travelers, boarders and the player can be
 counted through explicit scenario/read-model definitions. Scenario objectives must specify inclusion rules.

  Playtest provenance: GHOST-TECH-001, GHOST-DEF-006

 2.2 Person ID, household membership and kinship

  TECH LOCK - TECH LOCK - PersonId never changes because a Person moves, marries, changes
  employment or joins/leaves a Household. Kinship/family-tree relationships are distinct from current
  household membership.

 Persist parent/child/spouse and other supported kinship edges separately from domestic economic membership.
 This allows referrals, inheritance, migration and family history to span settlements and generations without
 rewriting Person identity.

  Playtest provenance: GHOST-TECH-003

 2.3 Poultry lifecycle and lineage

 Animal state for poultry must support egg/fertility/incubation/hatch, sex when known, juvenile stage, mortality,
 owner/location, parentage/source and final disposition. Male chicks must remain present even before they become
 market-ready. Correcting a missing classification can backfill biological history but may not fabricate sales that did
 not happen.

  Playtest provenance: GHOST-DES-005, GHOST-DES-022, GHOST-DEF-003, GHOST-DEF-004





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 52 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           4


 PART III - BUSINESS CREATION, PREMISES, SPACE & SHARED
 CAPACITY

 3.1 CreateBusiness workflow

 Conceptual workflow: CreateBusinessIntent -> Business identity -> Ownership/Governance -> optional
 BusinessOperation records -> PremisesRequirement resolution -> Asset/Inventory/WorkingCapital assignment ->
 delegated authority -> commerce. A business type may declare no dedicated premises requirement when its
 actual task methods permit remote/in-person/correspondence operation.

  Playtest provenance: GHOST-DES-029, GHOST-DES-031, GHOST-TECH-004

 3.2 Functional space assignment

 Extend/reuse Property -> Building -> FunctionalSpace -> UseRelationship. An occupied parcel remains selectable
 when compatible real space exists. Space assignments should carry area/room/station/yard identity, access,
 shared-facility rights, capacity constraints and effective dates. This supports multi-site production, mixed use and
 Add Separate Business Here without treating the entire parcel as a binary occupied flag.

  Playtest provenance: GHOST-DES-003, GHOST-DES-004, GHOST-DES-031, GHOST-DES-033, GHOST-DES-070

 3.3 Premises capability and reserved buildable envelope

 Premises suitability resolves from actual Task/Equipment/Space requirements. A butcher requires work surfaces,
 sanitation/storage and tool capacity; poultry requires land/coop; an agency can operate initially without an office.
 Parcel planning should reserve geometry/capacity for circulation, receiving, utilities, waste/privy/water, fire
 separation, additions and future structures rather than exposing generic RearYardCapacity.

  Playtest provenance: GHOST-DES-008, GHOST-DES-064, GHOST-TECH-004

 3.4 Shared assets reserve one physical capacity

  TECH LOCK - TECH LOCK - A shared Asset/Person/Space reservation cannot satisfy two simultaneous
  tasks simply because two business operations reference it.

 Freight and Wagon Works provides the acceptance example: wagon under repair is unavailable for hauling; worker
 reserved for repair is unavailable for driving; repair bay occupancy blocks another incompatible job.
 Shared-property operating units can use one ledger but must not duplicate assets in their unit views.

  Playtest provenance: GHOST-DES-071, GHOST-DES-072

 3.5 Partnership and enterprise grouping

 Represent ownership percentage, capital contribution, governance/manager authority and internal profit/loss
 allocation as relationships. External creditor obligations remain whole according to the actual agreement.
 Enterprise/portfolio groupings are management/read-model structures unless a real reorganization changes
 legal/economic ownership or cash authority.

  Playtest provenance: GHOST-DES-035, GHOST-DES-033

 PART IV - INVENTORY LOTS, PROCESSING & FOOD SAFETY

 4.1 PerishableLotState

  TECH LOCK - TECH LOCK - Meaningful perishables require lot/batch provenance; aggregate quantity
  alone is insufficient when age, condition or safety can differ.

 Suggested state: LotId; Item/Species/ProductId; source transaction/animal/batch; owner/business;
 location/container; produced/acquired timestamp; quantity/units; condition/quality dimensions; safety-risk
 state/evidence; storage/handling history; reservations; current process link; disposition. Coarse batching is
 allowed when items are materially homogeneous.

 Combining lots may create a composite reservation/display, but must not reset the older lot's age/condition.
 Consumption/sale should use a defined lot-selection policy while preserving provenance.

  Playtest provenance: GHOST-CAN-010, GHOST-CAN-011, GHOST-TECH-002





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 53 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           5



 4.2 ProcessingBatch / transformation chain

 Curing, salting, smoking, tanning and prepared-food production should use persistent
 ProcessingBatch/WorkInProgress state with input lot links, recipe/method, Person/task reservations, required
 equipment/space, ingredients, start time, planned/check intervals, process conditions, failures/delays and output
  lot creation. Output inherits relevant source history instead of receiving fresh age/condition by default.

  Playtest provenance: GHOST-DES-006, GHOST-CAN-012, GHOST-CAN-013, GHOST-DES-063

 4.3 Carcass allocation and byproducts

 Slaughter creates a carcass/yield event that allocates actual mass/items into cuts, fat/tallow inputs, hide,
 offal/other supported byproducts, household/wholesale allocations and waste. Inventory and accounting should
 reconcile to the physical inputs/outputs within calibration tolerance. The allocation step must occur before sale
 intents so one carcass cannot be sold twice through abstract buttons.

  Playtest provenance: GHOST-DES-010, GHOST-DES-011, GHOST-DES-012, GHOST-CAL-004

 4.4 Tanning versus leatherworking capability

 Model TanningProcess capability/equipment separately from Leatherworking/Saddlery task requirements. The
 output Leather lot becomes an input to later manufacturing; producing the material does not grant the Person or
 business the ability to make finished leather goods.

  Playtest provenance: GHOST-DES-006, GHOST-DES-007

 PART V - COMMERCE, PROCUREMENT, PRICING & SERVICE
 CAPACITY

 5.1 SupplierOffer / Quote architecture

 ProcurementNeed should resolve real candidate suppliers/channels. A SupplierOffer/Quote should expose
 SupplierId, Product/GradeId, package/quantity, minimum order, tier/discount rules, unit/package price, refundable
 container/deposit, freight/handling/other charges, payment terms, availability/lead time and quote validity.
 LandedCostReadModel calculates consumable cost and separate cash tied in refundable deposits.

  Playtest provenance: GHOST-DES-056, GHOST-DES-065, GHOST-DES-060

 5.2 Samples and qualification evidence

 A SampleRequest creates a real order/message/shipment or in-person transfer and then an inspection/use
 evidence record. Supplier quality should not become a permanent hidden scalar from one sample; evidence has
 source, date and scope, and future lots may differ.

  Playtest provenance: GHOST-DES-066

 5.3 Delegated procurement

 AuthorityAssignment/Policy can let a manager choose suppliers, grades, package sizes and stock quantities within
 budget, minimum-quality, reserve, lead-time and prohibited-item constraints. The manager should consume the
 same quotes/evidence available to the business and create real purchase commitments/transactions.

  Playtest provenance: GHOST-DES-062, GHOST-DES-067

 5.4 Internal requisition and consolidation

 For common ownership, an InternalRequisition can request physical inventory from another business without
 inventing a fixed outside-style contract. If a transfer value is recorded for business-level management accounting,
 consolidated reporting must eliminate internal revenue/profit while preserving inventory movement, labor/cost
 and cash/receivable state actually used. Existing external agreements still constrain availability.

  Playtest provenance: GHOST-DES-061, GHOST-DEF-008

 5.5 Service capacity and congestion

 Represent service positions and queue state from physical stations, tables/chairs/bar positions, floor/route
 capacity and on-duty labor. Customers can balk, queue, stand, substitute a table, shorten visits or leave according
 to waiting/crowding preferences. ExpectedCrowdEvidence may come from schedule/reputation/prior visits;
 ExperiencedCrowd comes from actual arrival state. Avoid a single Capacity or CrowdPenalty scalar when physical
 constraints matter.

  Playtest provenance: GHOST-DES-054, GHOST-DES-058, GHOST-DES-059




                                          LAND & LEDGERS

## TECH-XIV — original PDF page 54 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           6



 5.6 Staffing from workload telemetry

 Staffing recommendations should consume observed workload: transactions/hour, service wait, cleaning/restock
 tasks, bookkeeping backlog, overtime/fatigue, task collisions and refused/lost demand. Do not instantiate fixed
  staff recipes by business type. One Person can cover multiple compatible duties only when Activity/time
 reservations permit.

  Playtest provenance: GHOST-DES-050

 5.7 Sales require counterparties

  TECH LOCK - TECH LOCK - No egg, food, retail or service sale may post revenue without a real
  buyer/channel and transaction provenance.

 Historical totals with missing counterparty detail may remain immutable playtest history if already accepted, but
 new transactions must name a Person/Business/remote market mechanism. Do not retrospectively fabricate
 buyers merely to fill missing records.

  Playtest provenance: GHOST-DEF-005

 PART VI - RECRUITMENT, MIGRATION, MEDIA & EVIDENCE

 6.1 RecruitmentCampaign and channels

 Replace applicant-generation actions with a RecruitmentCampaign/Need linked to target role/capabilities,
 wage/terms, location/housing context and allowed channels. Channel instances can include direct contact,
 employee referral, handbill, business network, traveler/freight word-of-mouth, postal letter and newspaper
 advertisement. The channel creates Awareness/Lead state in actual Persons; it does not create a Person.

  Playtest provenance: GHOST-DES-051

 6.2 Referral graph and correspondence

 Referral queries traverse known relationship edges - family, former coworkers, friends, merchants or trusted
 contacts - and produce zero or more plausible leads with provenance. A lead can be contacted through
 Message/PostalLetter, then transition through NoReply/Interested/Negotiating/VisitPlanned/Declined/etc. Manual
 Ask About People remains a player action unless authority/policy delegates it; passive word-of-mouth is a separate
 information process.

  Playtest provenance: GHOST-DES-021, GHOST-DES-034, GHOST-DES-039, GHOST-DES-041

 6.3 Migration is a household decision over real causes

 Migration evaluation should combine actual jobs/wages, business ownership or land/property opportunity,
 kin/social links, household composition, travel cost/time, available housing and expected conditions. Housing
 capacity gates feasibility but does not itself create desire to move. Business sale/control transfer can therefore be
 a migration offer. Settlement population remains derived from actual Persons/Households.

  Playtest provenance: GHOST-DES-016, GHOST-DES-018, GHOST-DES-020, GHOST-DES-025, GHOST-DES-032, GHOST-TECH-001

 6.4 Rural catchment must feed opportunity and demand models

 Settlement opportunity analysis must query reachable rural Persons/Households/Farms, travel routes, trip
 frequency and existing sourcing rather than only residents inside the town node. Regression must prevent a
 missing catchment from falsely declaring agriculture-facing enterprises impossible.

  Playtest provenance: GHOST-DES-002, GHOST-DEF-001

 6.5 MediaMarket and advertising funnel

 Represent publication city, issue frequency, circulation/reach geography, audience, ad inventory/size/placement,
 rate, repeat terms and available issue slots. An AdvertisingInsertion generates awareness probabilistically/causally
 among actual reachable Persons; evaluation reports impressions/awareness where knowable plus inquiries,
 qualified inquiries, visits and resulting actions. Do not implement AdvertisingEffectiveness as a universal
  multiplier.

  Playtest provenance: GHOST-DES-076, GHOST-DES-079





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 55 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           7



 6.6 Evidence-limited business reporting

 Separate true simulation state from Known/Recorded/Reported read models. Occupancy, tenant health, property
 condition and competitor changes can be known through direct observation, records or delegated reporting with
 confidence/provenance. Multiple independent observations may improve evidence but should not be combined as
 simple arithmetic skill bonuses.

  Playtest provenance: GHOST-DES-055, GHOST-DES-096, GHOST-DES-097

 PART VII - DELEGATION, PORTFOLIO POLICIES & RIVAL DECISION
 LOGIC

 7.1 AuthorityAssignment extension for borrowing/guaranty

 Authority data should distinguish spending limit, contract/commitment limit, company borrowing, ability to
 solicit/accept lender terms, authority to bind owner as co-borrower/guarantor where expressly granted, collateral
 scope, hiring powers, ownership-change locks and escalation rules. Manager title remains metadata; execution
 checks the relationship.

  Playtest provenance: GHOST-DES-044

 7.2 Evidence-driven management optimization

 Managers should consume operational telemetry and create real decisions/tasks. Freight examples: loaded/empty
 miles, wagon-days, driver utilization, refused jobs, late delivery, route/customer margin and maintenance.
 Hospitality examples: queue time, overtime and stockouts. Optimization changes schedules, prices, procurement,
 hiring and capital proposals rather than applying an efficiency multiplier.

  Playtest provenance: GHOST-DES-045

 7.3 Persistent owner policies and exception reporting

 Implement reusable Policy/StandingInstruction records for reserves, reinvestment, preferred lead routing,
 purchasing constraints, recruitment preferences and owner-lock conditions. Full portfolio autonomy becomes a
 composition of AuthorityAssignments + Policies + periodic reports + exception triggers. The UI can filter detail by
 OwnerAttention without suppressing the underlying simulation.

  Playtest provenance: GHOST-DES-057, GHOST-DES-068, GHOST-DES-069

 7.4 Venture-development work is ordinary delegated activity

 A VentureManager/BusinessDevelopment assignment can create Investigation, Inspection, MarketResearch,
 Negotiation, Recruitment, SupplierSetup, StartupBudget and BusinessFormation tasks. It must reserve Person time
 and use owner capital/credit authority. Opportunity signals may prioritize investigation but may never directly
 create a business or transaction.

  Playtest provenance: GHOST-DES-089

 7.5 Rival entrepreneur parity

 NPC entrepreneur decision logic should use the same market, property, finance, staffing, supplier and information
 authorities as the player. Difficulty can seed/produce stronger capabilities, better information/relationships, larger
 legitimate capital or different risk/debt policies, but never free cash, omniscience, teleportation, guaranteed
 pricing advantages or hidden profit multipliers.

  Playtest provenance: GHOST-DES-053, GHOST-DES-089, GHOST-DES-093

 PART VIII - PROPERTY RIGHTS, AGENCY, TENANCY & RESIDENCE

 8.1 Property right, possession and use are separate authorities

 Represent legal/contractual PropertyInterest or Right relationships separately from physical Occupancy/Use state.
 Unauthorized use can therefore exist without granting title. PropertyStatusReadModel exposes only legitimately
 known ownership/status, current possession basis and available actions. Legal enforcement proceeds through
 notice/claim/court/enforcement events over time rather than an immediate collision barrier.

  Playtest provenance: GHOST-CAN-002, GHOST-CAN-003, GHOST-CAN-004, GHOST-CAN-005, GHOST-CAN-006, GHOST-CAN-007





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 56 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           8



 8.2 Property market provenance

 A Listing/Opportunity record must reference a real PropertyId, current owner/right-holder or explicit
 unknown/disputed state, source channel, discovered timestamp, known reason/motivation evidence and
 authorized agent if any. Property-generation code must not spawn detached $X cottage opportunities without a
 world object and provenance.

  Playtest provenance: GHOST-DEF-011

 8.3 AgencyAgreement and listing authority

 AgencyAgreement should define principal/owner, agent business/Person, represented PropertyIds or scope,
 sale/letting/rent-collection/maintenance/tax/negotiation powers, exclusivity, compensation method, expenses,
 term, termination and conflict disclosures. Active Listings/Managed Properties are read models over these
 agreements, not inventory ownership.

  Playtest provenance: GHOST-DES-027, GHOST-DES-028, GHOST-DES-030

 8.4 Related-party conflict and portable relationships

 When agent/principal/buyer relationships conflict, surface a ConflictOfInterest/Disclosure requirement before
 commission settlement where applicable. Employment departure does not transfer business-held exclusive
 agreements automatically, but Person relationship edges remain with the Person and can influence future
 leads/competition.

  Playtest provenance: GHOST-DES-036, GHOST-DES-046

 8.5 Rental Holdings and commercial SpaceUseAgreement

 A property business can own rental assets as an operation while keeping property-level
 rent/debt/condition/maintenance ledgers. Commercial room/chair/station leasing uses a SpaceUseAgreement
 linked to FunctionalSpace and TenantBusinessId; tenant revenue remains with the tenant. Track landlord common
 costs separately from tenant operating costs. Tenant recruitment is a lease funnel, not Employment.

  Playtest provenance: GHOST-DES-047, GHOST-DES-048, GHOST-DES-074, GHOST-DES-075, GHOST-DES-077, GHOST-DES-078, GHOST-DES-080

 8.6 PrimaryResidence and homestead compliance

 Person/Player state should distinguish PrimaryResidence relationship from TonightSleepLocation/temporary
 lodging. Historical land-entry compliance queries actual residence duration, cultivation/improvement evidence and
 abandonment/change-of-residence events. It must not be satisfied by a static Home=true flag while the Person
 actually lives elsewhere.

  Playtest provenance: GHOST-DES-084, GHOST-DES-085

 PART IX - FINANCE, DEBT, ACQUISITION & VALUATION

 9.1 FinancingOffer / negotiated structure

 Financing offers should carry cash down, financed principal, rate, payment schedule/amortization, maturity,
  collateral/security, guaranty/co-borrower, prepayment terms, fees and conditions. Negotiation changes these
 terms individually or as a package, with acceptance driven by lender/seller risk and alternatives. Do not reduce
 finance to PriceDiscount or LoanApproved booleans.

  Playtest provenance: GHOST-FIN-001

 9.2 Debt assumption

 Ownership transfer can include AssumptionOfDebt linked to the existing secured obligation and required
 lender/contract consent, while seller note/cash/equity cover remaining consideration. The debt's identity, balance,
  collateral and creditor history persist; sale settlement does not silently recreate or erase the obligation.

  Playtest provenance: GHOST-FIN-002, GHOST-DEF-010

 9.3 EnterpriseValuationReadModel

  TECH LOCK - TECH LOCK - Valuation exposes at least GrossGoingConcernValue and
  OwnerEquityValue/EstimatedSaleProceeds. Business cash is separate and contributes zero to Ghost
  Town objective value.

 Inputs should include normalized maintainable operating profit, historical windows/seasonality, margin/trend,
 customer concentration, contracts, supplier stability, competition, records/reputation, management/key-person




                                          LAND & LEDGERS

## TECH-XIV — original PDF page 57 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                           9




 continuity, owner-dependency map, premises security, legal/licensing risk, replacement-capital need, transferable
 productive assets and business-specific liabilities. Output should be a range/confidence when evidence is weak,
 not false precision.

 Normalize unpaid owner labor using a competent replacement cost. Productive assets can create an asset floor or
 adjustment but must not be blindly added to earnings value when those assets are already required to produce
 the earnings. Cash is excluded; receivables/payables and inventory can be separate closing components.
 Inventory-sale treatment remains an explicit open design item, with perishables requiring
 realizable-value/condition logic.

  Playtest provenance: GHOST-DES-043, GHOST-DES-081, GHOST-DES-087, GHOST-DES-088, GHOST-DES-092

 9.4 SellerMotivationEvidence and distressed opportunities

 Acquisition negotiation should query known evidence about seller plans, liquidity need, attachment, alternatives,
 creditor pressure and employment/management willingness. Unknown remains unknown. DistressedOpportunity
 events can be surfaced by owner, creditor, employee or agent and link to actual financial/operating evidence; they
 must not reveal a hidden CauseOfFailure answer to the player.

  Playtest provenance: GHOST-DES-094, GHOST-DES-095

 9.5 Plain-language finance projection

 Finance UI can translate underlying instruments into a stable summary: lender/creditor, principal/amount owed,
 rate, next payment, maturity, collateral/security, guaranty/assumption and arrears/default state. The summary is
 derived; it must not merge mortgage, seller note, lien, guaranty or debt assumption into one generic Debt record.

  Playtest provenance: GHOST-UX-001

 PART X - FREIGHT, RAIL PROMOTION & RELATED-PARTY
 OPERATIONS

 10.1 FarmRoute / standing pickup relationships

 Freight services should support standing customer/pickup relationships, route consolidation and compatible cargo
 planning over the shared route graph. Route plans consume actual vehicle/team/driver capacity and create
 shipments for actual cargo. They are not aggregate revenue generators.

  Playtest provenance: GHOST-DES-040

 10.2 Related-party service agreements

 Exclusive/preferred freight agreements use ordinary Agreement state with parties, scope, rate/discount, service
 window, exception/fallback rights, term and governance approval. When minority owners exist,
 approval/related-party checks prevent value stripping. The exact Ghost Town discount remains deal data.

  Playtest provenance: GHOST-DES-042

 10.3 Internal repair accounting

 When one operating unit repairs its own enterprise asset, post labor/material consumption and asset-condition
 change without external sale revenue. If separate legal businesses transact with each other, real intercompany
 transactions may exist but consolidated reporting eliminates internal profit according to ownership/reporting
 scope.

  Playtest provenance: GHOST-DES-073, GHOST-DEF-008

 10.4 Rail strategy classification and promotion projects

 A freight planning read model may classify corridors as likely rail-displaced, rail-feeder or rail-insulated based on
 actual network/project evidence. It must not directly mutate revenue. Railroad promotion is a Project/Institutional
  effort that can assemble traffic evidence, land/right-of-way offers, civic finance, depot/siding proposals and
 correspondence with the railway; the railway evaluates its own engineering/economics.

  Playtest provenance: GHOST-DES-082, GHOST-DES-083





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 58 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          10


 PART XI - HISTORICAL AUTHORING, CALIBRATION & SCENARIO
 DATA

 11.1 Historical legal gate data

 Represent dated licensing/bond/surety requirements as historical authoring/legal-rule records with jurisdiction,
 effective dates, source/provenance, obligation type and compliance workflow. The Ghost Town 1873 Dakota
 saloon rule is an authoring anchor from the playtest research, not a universal SaloonCost constant.

  Playtest provenance: GHOST-LEGAL-001

 11.2 Calibration boundaries

 Ghost Town prices for opening property, repairs, poultry/livestock, tools/materials, slaughter yields, meat,
 horses/wagons and exact local operating costs remain scenario/calibration data. Store them in replaceable
 authoring/scenario datasets with provenance/version; do not hard-code them as global historical constants merely
 because the playtest used them consistently.

  Playtest provenance: GHOST-CAL-001, GHOST-CAL-002, GHOST-CAL-003, GHOST-CAL-004, GHOST-CAL-005

 11.3 Generated quote provenance

 Scenario-generated supplier/commodity/freight/tax quotes should record that they are generated, the historical
 evidence/calibration set used and effective date/location. The player may treat the quote as authoritative for that
 world, but developer/research tooling must not mislabel it as an archival primary-source number.

  Playtest provenance: GHOST-RES-003

 11.4 Research holds

 Support uncertain/held authoring values instead of forcing exactness. The May 1872 Sioux City-Vermillion
 construction front has no exact milepost established in the playtest research. The prevalence of individual
 barber-chair rental in 1870s Dakota is likewise not locked. Camera-versus-direct-observation boundaries remain a
 design hold. Store confidence/range/open-question metadata where useful.

  Playtest provenance: GHOST-RES-001, GHOST-DES-077, GHOST-DES-097

 11.5 Ghost Town scenario data

 Scenario data may retain Morrow Creek boom/opening population calibration, chosen starting-property state,
 scenario objective values, company standing policies and specific negotiated terms without promoting them into
 generic systems. The chosen starting opportunity must persist in save history while unchosen opportunities
 remain available to NPC simulation.

  Playtest provenance: GHOST-DES-049, GHOST-DES-069, GHOST-DES-097, GHOST-CAN-008

 PART XII - REGRESSION FIXTURES, PRIORITIES & TRACEABILITY

 12.1 P0 regression fixtures from Ghost Town

  • RURAL CATCHMENT - A tiny settlement with surrounding farms must expose agriculture-facing
    demand/opportunities from reachable rural actors.

  • NO PHANTOM BUYERS - Egg/food/service revenue cannot post without a Person/Business/remote-market
    counterparty and transaction provenance.

  • PLAYER HOUSEHOLD - If the scenario says the player resides locally, create/retain the player residence/household
    economic state; do not omit personal food/fuel living costs while counting everyone else.

  • POULTRY SEX PIPELINE - Hatched birds remain physical; later sex identification reclassifies history but cannot
    create missed sales automatically.

  • PERISHABLE MIXING - Combining fresh and older inventory cannot reset the old lot condition/safety history.

  • INTERNAL TRANSFER ELIMINATION - Internal requisitions move real inventory and can support business-level
    costing without creating consolidated profit.

  • LABOR ALLOCATION - Property-management or other shared worker time is charged to the operation/business that
   consumed it.

  • DEBT CONTINUITY - Acquisition, assumption, save/load and reporting retain exact external principal and do not
    invent repayments.

  • PROPERTY PROVENANCE - Every listed/acquired property opportunity resolves to a real world object, owner/right
    status and source channel.




                                          LAND & LEDGERS

## TECH-XIV — original PDF page 59 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          11




  • CREATE BUSINESS != OPERATING - A newly created entity with no real commerce cannot satisfy operating-business
    objectives.

  • SHARED CAPACITY - One wagon/worker/bay cannot be reserved by simultaneous freight and repair activities.

  • CAPABILITY DISPLAY - No removed capability labels appear; confidence widths are 1/2/3 adjacent bands or Not
   enough information.

  Playtest provenance: GHOST-DEF-001, GHOST-DEF-003, GHOST-DEF-004, GHOST-DEF-005, GHOST-DEF-006, GHOST-DEF-008, GHOST-DEF-009,
 GHOST-DEF-010, GHOST-DEF-011, GHOST-TECH-002, GHOST-TECH-005

 12.2 P1/P2 implementation order

  • P1 - Canon synchronization, stable Person/Household/Kinship state, perishable lot/process architecture, property
    provenance/right-state and supplier/transaction counterparty integrity.

  • P1 - Create Business + FunctionalSpace + mixed-use/shared-capacity workflow, then
    AgencyAgreement/SpaceUseAgreement and finance/debt assumption continuity.

  • P2 - Recruitment channels/referral graph/media market, delegated portfolio policies, evidence-driven manager
    telemetry and Venture Manager activities.

  • P2 - Enterprise valuation read model with owner-labor normalization, liability adjustment and evidence/confidence
    outputs.

  • P3 - Rail-promotion project UX, corridor strategy read models, richer advertising analytics and unresolved
    camera/direct-observation design pass.

 12.3 Complete Ghost Town finding disposition register

  All 137 findings are listed below so no playtest feedback is lost between design and implementation. The
 destination column identifies the primary Technical IX integration area; many findings also remain traced in Canon
 XXVI.


  Finding                 Title                                            Disposition              Integrated at

  GHOST-DES-002         Rural Production Must Generate Town-Facing            Implementation requirement   Production, perishables and processing
                             Opportunities

  GHOST-DES-003        Production Space and Retail Space Must Be Allowed to    Implementation requirement   Production, perishables and processing
                          Separate

  GHOST-DES-004         Multi-Site Microenterprise Works Well                   Implementation requirement   Production, perishables and processing

  GHOST-DES-005         Poultry Expansion Needs Biological Accounting           Implementation requirement   Production, perishables and processing

  GHOST-DES-006        Tanning must be a process, not a crafting recipe.         Implementation requirement   Production, perishables and processing

  GHOST-DES-007        Leather production and leatherworking are distinct       Implementation requirement   Production, perishables and processing
                                capabilities.

  GHOST-DES-008       A butcher startup has a meaningful minimum tool floor.   Implementation requirement   Production, perishables and processing

  GHOST-DES-009       A business becomes operating through actual            Implementation requirement   Production, perishables and processing
                        commerce, not setup.

  GHOST-DES-010         First-animal economics expose carcass-balance risk      Implementation requirement   Production, perishables and processing
                           immediately.

  GHOST-DES-011        Slaughter should create a carcass-allocation decision.     Implementation requirement   Production, perishables and processing

  GHOST-DES-012        Byproducts can stabilize a butcher's economics.          Implementation requirement   Production, perishables and processing

  GHOST-DES-013        Casual specialist/helper relationships are useful.          Implementation requirement  Labor and working-capital continuity

  GHOST-DES-014        Casual labor policy:                                   Implementation requirement  Labor and working-capital continuity

  GHOST-DES-015        Startup businesses need bridge income.                 Implementation requirement  Labor and working-capital continuity

  GHOST-DES-016        Housing alone should not create migration.              Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-017       Our butcher can become part of an anchor, but it is not   Implementation requirement   Migration, settlement and opportunity
                        one yet.                                                                         evidence

  GHOST-DES-018       The ghost-town bottleneck is now clearly employment,    Implementation requirement   Migration, settlement and opportunity
                           not buildings.                                                                    evidence

  GHOST-DES-019        Anchor investigation can fail productively.               Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-020        Business sale can itself drive migration.                 Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-021         Referral & Correspondence Recruitment.                Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-022         Poultry lineage management.                          Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 60 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          12





  Finding                 Title                                            Disposition              Integrated at

  GHOST-DES-023         Business-sale supply agreements.                      Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-024         Post-sale supplier relationships.                        Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-025        Population strategy should target households, not raw    Implementation requirement   Migration, settlement and opportunity
                           headcount.                                                                      evidence

  GHOST-DES-026       Abandoned mining is evidence, not a binary state.        Implementation requirement   Migration, settlement and opportunity
                                                                                                           evidence

  GHOST-DES-027        Property brokerage/relocation service.                  Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-028        Property Agency Agreements.                          Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-029        Create Business UI.                                   Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-030       Agency “inventory” is represented property, not owned   Implementation requirement  Property agency, business formation and
                              inventory.                                                                       ownership

  GHOST-DES-031         Universal Create Business workflow.                    Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-032        Recruitment + housing workflow validated.              Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-033       Two ways to expand from the Business UI.               Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-034         Referral chains become standing migration              Implementation requirement  Property agency, business formation and
                               infrastructure.                                                                   ownership

  GHOST-DES-035         Partnership / partial-ownership formation.               Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-036         Related-party brokerage conflict.                       Implementation requirement  Property agency, business formation and
                                                                                                          ownership

  GHOST-DES-037       A settlement does not require one dominant “anchor.”    Implementation requirement  Settlement growth, recruitment and
                                                                                                                                freight

  GHOST-DES-038        Three-business objective now has a value floor.          Implementation requirement  Settlement growth, recruitment and
                                                                                                                                freight

  GHOST-DES-039        Passive migration awareness is separate from manual    Implementation requirement  Settlement growth, recruitment and
                                recruiting.                                                                                     freight

  GHOST-DES-040        Farm-route freight should be a first-class commercial     Implementation requirement  Settlement growth, recruitment and
                              pattern.                                                                                        freight

  GHOST-DES-041         Referral clarification.                                  Implementation requirement  Settlement growth, recruitment and
                                                                                                                                freight

  GHOST-DES-042         Related-party exclusive freight agreements.             Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-043        Business-value objectives should use transferable equity  Implementation requirement  Related parties, delegation, rentals and
                              value, not gross financed assets.                                                         objectives

  GHOST-DES-044        Delegated borrowing authority.                         Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-045       Management optimization should operate on evidence.   Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-046        Brokerage employees can build portable relationships.    Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-047        Property Agency can add a Rental Holdings operation.    Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-048          Rental-first lead routing.                               Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-049        Three-business objective strengthened again.            Implementation requirement  Related parties, delegation, rentals and
                                                                                                                       objectives

  GHOST-DES-050        Saloon staffing should scale from observed workload, not  Implementation requirement   Hospitality, recruitment, reporting and
                        a fixed employee recipe.                                                        procurement

  GHOST-DES-051         Hiring is a recruitment workflow, not an                 Implementation requirement   Hospitality, recruitment, reporting and
                             applicant-generation button.                                                     procurement

  GHOST-DES-052        Candidate capability display corrected.                  Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-053         Strong-rival balancing doctrine.                        Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-054        Premises utilization needs physical capacity + imperfect  Implementation requirement   Hospitality, recruitment, reporting and
                          knowledge.                                                                   procurement





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 61 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          13





  Finding                 Title                                            Disposition              Integrated at

  GHOST-DES-055        Business reporting is evidence, not omniscience.         Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-056        Procurement must be supplier-first.                     Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-057          Portfolio autonomy / attention filtering.                  Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-058        Patron density preference.                             Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-059        Service congestion replaces abstract crowd penalties.     Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-060         Retail pricing follows procurement, not the reverse.       Implementation requirement   Hospitality, recruitment, reporting and
                                                                                                       procurement

  GHOST-DES-061         Internal food sourcing competes with existing contracts.   Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-062       Opening equipment purchasing delegated to Samuel.     Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-063        Prepared-food batches are physical inventory.           Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-064         Parcel expansion planning needs a reserved buildable     Implementation requirement   Internal sourcing, inventory, premises
                           envelope.                                                                 and autonomy

  GHOST-DES-065        Procurement needs full landed-cost truth.               Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-066         Supplier sampling/qualification.                        Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-067       Opening stock can be delegated after constraints are     Implementation requirement   Internal sourcing, inventory, premises
                                 set.                                                                      and autonomy

  GHOST-DES-068          Full portfolio autonomy.                               Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-069      100% rental reinvestment mandate.                    Implementation requirement   Internal sourcing, inventory, premises
                                                                                                and autonomy

  GHOST-DES-070         Miners' Rest becomes mixed-use.                       Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-071         Freight + Wagon Works can be one enterprise.           Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-072        Shared assets do not duplicate capacity.                Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-073         Internal repairs are not fake revenue.                   Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-074        Chair/room leasing model.                             Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-075         Station leasing.                                       Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-076       Newspaper advertising needs a real media market.       Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-077         Professional tenant recruitment.                        Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-078       Commercial tenancy needs operating evidence.          Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-079         Advertising effectiveness is observable, not              Implementation requirement  Mixed-use enterprise, leasing and media
                              deterministic.                                                                 market

  GHOST-DES-080        Shared commercial-building costs belong to the landlord.  Implementation requirement  Mixed-use enterprise, leasing and media
                                                                                                     market

  GHOST-DES-081         Enterprise value needs two numbers.                   Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-082         Railroad promotion / civic lobbying.                     Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-083          Rail-resilient freight strategy.                          Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-084         Player Primary Residence versus nightly sleep.           Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-085       Homestead compliance must use actual residence, not a  Implementation requirement   Valuation, rail strategy and residence
                           checkbox.

  GHOST-DES-086          Historical starting year is gameplay.                    Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-087       Cash does not count toward Business Value.             Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-088        Operating profit is the primary valuation evidence.       Implementation requirement   Valuation, rail strategy and residence

  GHOST-DES-089        Venture Manager / Business Development Agent.         Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information




                                          LAND & LEDGERS

## TECH-XIV — original PDF page 62 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          14





  Finding                 Title                                            Disposition              Integrated at

  GHOST-DES-090         Player may begin as an employee.                      Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-DES-091         Player and NPC capability advancement use the same    Implementation requirement   Entrepreneurship, rivals, acquisition and
                           system.                                                                               information

  GHOST-DES-092        Business sale value must include productive assets, but   Implementation requirement   Entrepreneurship, rivals, acquisition and
                           avoid double counting.                                                                 information

  GHOST-DES-093        Entrepreneur debt policy.                              Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-DES-094         Distressed-business opportunities.                      Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-DES-095          Seller motivation belongs in acquisition negotiations.     Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-DES-096        Property changes are information, not pop-ups.          Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-DES-097       Camera versus direct observation remains open.         Implementation requirement   Entrepreneurship, rivals, acquisition and
                                                                                                                   information

  GHOST-CAN-001        Scenario Narration                                  Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-002        Vacant Is Not Ownerless:                            Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-003        Property Action Clarity.                              Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-004        Unauthorized Use Is Physically Possible.                Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-005        Legal Rule Is Not Instant Enforcement.                 Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-006        Property Troubleshooting Is a Workflow.                Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-007        Good-Faith Claim Is Not Knowing Trespass.              Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-008        Player-Selected Starting Opportunity                   Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-009         Starting Investment Follows Investigation               Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-010        Perishable Condition & Food Safety.                   Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-011         Quality and safety must be separate.                  Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-012        Preservation transforms inventory; it does not reset it.    Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-013        Preservation work must consume calendar time.         Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-014         Capability bands changed.                           Implement canon lock         Architecture + acceptance fixtures

  GHOST-CAN-015         Capability bands revised.                            Implement canon lock         Architecture + acceptance fixtures

  GHOST-DEF-001        Missing Rural Catchment                               Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-002        Three-business objective is much easier than its 1885     Regression fixture / data       Part XII - Regression suite
                            deadline suggests.                                        correction

  GHOST-DEF-003        Male poultry pipeline was underreported.                Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-004        Male poultry accounting omission.                       Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-005       Egg sales lacked proper counterparties.                  Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-006         Player household was missing from the simulation.        Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-008        South Garden egg arrangement corrected.               Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-009        Property management labor wasn't being fully charged.   Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-010        Debt continuity correction.                             Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-DEF-011        Property source/provenance must be explicit.            Regression fixture / data       Part XII - Regression suite
                                                                                        correction

  GHOST-CAL-001       CALIBRATION                                              Authoring/calibration data     Part XI - Authoring and calibration
                                                                                    only

  GHOST-CAL-002        Current poultry, land and livestock prices remain          Authoring/calibration data     Part XI - Authoring and calibration
                               calibration values                                       only

  GHOST-CAL-003           All exact tool, livestock and material prices below remain  Authoring/calibration data     Part XI - Authoring and calibration
                            scenario calibration.                                     only

  GHOST-CAL-004         First-slaughter yields and local meat prices remain        Authoring/calibration data     Part XI - Authoring and calibration
                               calibration values.                                       only

  GHOST-CAL-005        Horse/wagon pricing remains calibration.                  Authoring/calibration data     Part XI - Authoring and calibration
                                                                                    only




                                          LAND & LEDGERS

## TECH-XIV — original PDF page 63 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          15





  Finding                 Title                                            Disposition              Integrated at

  GHOST-TECH-001      VALIDATED SYSTEM                                     Technical architecture lock     Parts I-IV

  GHOST-TECH-002       Perishable Lot State.                                    Technical architecture lock     Parts I-IV

  GHOST-TECH-003        Persistent Person IDs + family trees.                     Technical architecture lock     Parts I-IV

  GHOST-TECH-004       Premises are capability-dependent.                       Technical architecture lock     Parts I-IV

  GHOST-TECH-005       Technical terminology now needs synchronization.        Technical architecture lock     Parts I-IV

  GHOST-FIN-001         Startup Financing Should Support Structure Negotiation   Finance architecture           Part IX - Finance and valuation

  GHOST-FIN-002        Debt-assumption sales.                                Finance architecture           Part IX - Finance and valuation

  GHOST-RES-001       RESEARCH HOLD                                       Research/authoring hold       Part XI - Authoring and calibration

  GHOST-RES-003        Exact 1873 local quotes remain calibration.              Research/authoring hold       Part XI - Authoring and calibration

  GHOST-LEGAL-001      Saloon opening has a major legal gate we had not yet     Legal-gate data/workflow      Part VIII / Part XI
                             surfaced.

  GHOST-UX-001         Plain-Language Finance.                               Read-model/UI requirement    Part X - Information and UX





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 64 of 170

LAND & LEDGERS / REVISION IX / TECHNICAL IMPLEMENTATION BIBLE                                          16


 END OF REVISION IX INTEGRATION

 The complete Technical Implementation Bible Revision VIII follows unchanged as the preserved baseline and
 remains authoritative except where Revision IX explicitly adds, deepens, corrects or supersedes technical
 guidance. Canonical Project Bible Revision XXVI controls intended gameplay behavior.





                                          LAND & LEDGERS

## TECH-XIV — original PDF page 65 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      1

             LAND & LEDGERS
 TECHNICAL IMPLEMENTATION BIBLE - REVISION VIII
  1870 Historical Architecture Integration: World Skeleton, Rail Segment/Corporate State, Multimodal Transport,
            Technology Availability, Environmental Regimes, Postal Channels & Migration Integrity

                                                 Revision date: September 22, 2026
                        Source baseline: Technical Implementation Bible Revision VII - September 21, 2026
                            Canon compatibility target: Canonical Project Bible Revision XXV
                                                          Status: Active integration

 PRESERVATION GUARANTEE
 Revision VIII preserves Technical Implementation Bible Revision VII in full except where this integration explicitly adds, deepens,
  corrects or supersedes technical guidance. The complete Revision VII document follows this integration unchanged. Canonical
  Project Bible Revision XXV controls intended behavior. Repository evidence remains authoritative for what is actually implemented;
 conceptual types below are target architecture, not proof of existing Unity classes.
 PURPOSE
 Revision VIII translates the 1870-start historical research and closing architecture audit into implementation guidance. It does not
 replace Revision VI continuous-Person, transaction, equipment, agreement or institutional architecture, nor Revision VII rural/livestock
  architecture. It adds a historically constrained world-generation/data layer, explicit infrastructure lifecycles, multimodal transport,
 technology diffusion/supportability, environmental-regime inputs, postal/mail-order channels, office-record methods and a concrete
  migration checklist for synthetic/duplicate truth discovered in the earlier repository audit.
 REVISION VIII ARCHITECTURE LOCKS
   * Normal historical configuration begins in spring 1870 and reaches the 1905 Historical Horizon; date/region availability is data, not
    scattered hard-coded unlock checks.
   * Historical world generation separates immutable/constrained regional skeleton from procedural local geography and emergent
    simulation.
   * Railway Segment physical state is distinct from corporate owner/operator/controller relationships and from commercial
    service-open state.
   * Infrastructure projects advance through explicit stages; track laid or building complete cannot silently imply service availability.
   * One multimodal route graph can expose mode/capability edges for Person travel, cargo shipment and mail/message carriage
    without merging their economic authorities.
   * TechnologyDefinition + regional/local availability/access/supportability data replaces universal TechnologyUnlocked booleans.
   * HistoricalRegime data supplies priors/context to ordinary causal systems; it never posts revenue, crop yield or migration directly.
   * Research evidence/source metadata should be retained in authoring data so observation, generalization and confidence remain
    distinguishable.
   * Existing P0 Person/customer/transaction/shipment integrity requirements remain active, including removal of any still-present
    synthetic sales/income/capital paths.
 PART I - HISTORICAL AUTHORING DATA & REGIME ARCHITECTURE
 1.1 HistoricalEnvelopeConfig
 Add/configure one canonical authored historical envelope rather than scattering date checks. Suggested data: StartDate/Season;
  HistoricalHorizonDate; region profile; scenario override rules; source/version metadata. Open Frontier default is spring 1870 through
 1905. Scenario definitions may override start/end inside the supported envelope.





                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 66 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      2

 1.2 EvidenceRecord
  Historical authoring data should preserve EvidenceRecord metadata where practical: EvidenceId; claim/observation; SourceId; source
  type; geography; date/start/end; relationship type where relevant; confidence; notes/limitations; whether the evidence is observation
 versus generalized rule. This is development data, not necessarily runtime save data.
 1.3 HistoricalStateRecord & AuthoringRule
 A HistoricalStateRecord can assert that a network, institution, technology or corporate relationship existed in a place/time. Game
 Authoring Rules derive availability windows, prerequisites and ranges from multiple records. Runtime simulation consumes authoring
  rules; it should not scrape research prose.
 1.4 HistoricalRegime
 Use data-driven regional/date regimes for broad priors such as CreditContext, RailInvestmentContext, MigrationContext,
 ClimateContext, CommodityContext and LandEntryPressure. Regimes feed ordinary decision/yield/finance/project models as inputs.
 No HistoricalRegime may directly mutate revenue, yield, cash, population or completion state.
 1.5 Supersede 1880 Baseline Assumptions
 Any generic availability registry described as an 1880 opening set should be reframed as 1870-1905 dated data. Existing 1880
 observations remain valid records. Avoid mass-renaming data whose actual effective dates differ; add date/region availability rather
 than assuming everything listed in the occupation/equipment tables exists locally in 1870.
 PART II - WORLD GENERATION & HISTORICAL SKELETON
 2.1 World Layers
   Layer                         Technical responsibility

   HistoricalSkeleton               Major river/network geometry, external market nodes, treaty/reservation/federal geography, major historical infrastructure
                                   approaches/events.

   ProceduralRegionalLayer         Local settlement seeds, secondary roads, farms, small landings/ferries, plausible station candidates constrained by historical
                                  geography.

   EmergentWorldState             Actual Persons, Businesses, parcels/rights, Projects, infrastructure decisions, migration and settlement outcomes.

 2.2 Stable Geography Identity
  Historical and procedural nodes need stable IDs so saved campaigns remain deterministic. Seed + historical-data version should be
  stored. Authoring-data updates must use migration/version policy; do not silently reroll an established world.
 2.3 Historical Project Intention vs Forced Outcome
 A dated historical proposal/project can instantiate a Plan/Project/Actor with intended corridor and financing context. Completion still
 resolves through ordinary rights, capital, construction and world state. Canon XXIV/VII divergence behavior remains authoritative.





                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 67 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      3

 PART III - RAILWAY SEGMENT, PROJECT & CORPORATE RELATIONSHIPS
 3.1 RailSegment Authority
 Introduce or deepen a persistent physical RailSegment/TrackSegment authority distinct from the company relationship. Conceptual
  fields: SegmentId; endpoints/corridor geometry; gauge; physical owner; condition; capacity/rail class where modeled; facilities;
  construction state; service state; date history; route restrictions.
 3.2 Railway Development State Machine
 Conceptual states should support at least Proposed/Promoted, Incorporated/Authorized, Surveyed, FinancePending/Financed,
 RightOfWayPending/Secured, Grading, TrackLaid, FacilitiesIncomplete/Ready, ServiceTesting if used, ServiceOpen,
 Suspended/Abandoned/Closed. Exact enum boundaries may be consolidated, but tests must prove that TrackLaid != ServiceOpen.
 3.3 RailwayOrganizationRelationship
 Do not store one RailroadCompanyId as the only corporate truth. Reuse shared ownership/control/agreement structures or add a
  relationship read model sufficient to distinguish Builder, InfrastructureOwner, LegalRailroadEntity, LesseeOperator,
 ManagingOperator, Parent/Controller and HistoricalSystemFamily over dated intervals.
 3.4 Corporate Succession
  Merger/lease/sale/reorganization appends relationship history. Physical segment IDs, earlier Agreements, project evidence, Person
 employment history and transaction provenance remain unchanged. Reports can resolve the operator/owner at any historical
 timestamp.
 3.5 RailNode Facilities
  Station/depot capability derives from actual assets/spaces: passenger/depot office, freight platform/house, sidings, livestock pens,
  elevator relationship, warehouse, water/fuel, engine servicing and interchange. A track crossing a settlement does not automatically
  grant every service.
 3.6 Railway Technology
 Track, bridge, locomotive, rolling stock, brake/coupler and service-facility assets should carry compatible capability/constraint data
 where materially simulated. Regulatory regimes may make particular interstate service configurations noncompliant without deleting
  assets. Avoid RailTechLevel.
 PART IV - MULTIMODAL TRANSPORT GRAPH
 4.1 Shared Geographic Graph, Distinct Payload Authorities
 Where practical, represent roads/trails, rail, river, ferry and other route edges in a common topology with mode/capability metadata.
 Person Journey, cargo Shipment and Mail/Message routing can query the same graph while retaining distinct payload/economic
  authorities.
 4.2 Edge Capability
 Useful edge data may include permitted modes, seasonal availability, capacity, cost/rate model reference, speed/travel-time model,
  required asset/animal class, toll/fare, service schedule, operator and transfer nodes. Do not encode economic movement as
  straight-line distance when a route/landing/bridge/ferry constraint exists.
 4.3 Transshipment
 Switching wagon -> steamboat -> rail or rail -> drayage creates explicit transfer/handling state. Cargo location/custody moves at
 load/unload completion. Reservations apply to handling space, vehicle capacity and labor where material.
 4.4 Stage/Freight Service
 A stage/freight route is a service over real vehicles, teams, drivers, stations and schedule. It may be coarse simulated but cannot
 create trips beyond available asset/crew capacity. Way stations consume feed, lodging and maintenance resources where
 represented.
 4.5 Rail Reconfiguration Tests
 Acceptance tests should show long-haul wagon demand falling after rail service while farm-to-depot/drayage/local-delivery demand
 can rise. No system may apply a generic FreightObsolescence modifier detached from actual route choice.



                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 68 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      4

 PART V - TECHNOLOGY DEFINITION, AVAILABILITY & SUPPORTABILITY
 5.1 TechnologyDefinition
 Use TechnologyDefinition primarily as authoring/classification data, not as an unlock flag. Suggested fields: TechnologyId; domain;
  prerequisites/capabilities; commercial-production window; compatibility; required inputs/network; operator capability;
 maintenance/support requirements; source metadata.
 5.2 RegionalTechnologyAvailability
 Store regional/local evidence or adoption windows separately: RegionId/SettlementId; EarliestEvidence; TypicalAvailabilityWindow;
 Supplier/DealerNetwork; infrastructure prerequisite; local support/repair capability; confidence. A first-known observation should not
 be interpreted as 100% regional penetration.
 5.3 TechnologyAccess
 Person/Business/Site access can resolve through owned Asset, lease/rental, hired service, public/network connection or third-party
  provider. Suggested states/read model: Obtainable, Ordered, InTransit, Acquired, Installed/Connected, Supported, Operational.
 Physical truth still resides in Assets, networks, Agreements and Inventory.
 5.4 Supportability
  Calculate/resolve supportability from required consumables/fuel, replacement parts, compatible motive power/network, operator
  capability and repair/service access. Equipment can be operationally unavailable without being destroyed or unowned.
 5.5 Historical Anchor Registry
 Maintain dated anchor records for technologies established by Canon research (reaper pre-1870; barbed wire 1874 era; typewriter
 1874; telephone/Deadwood 1878; waterworks Deadwood 1879 and Yankton/Sioux Falls 1884; electric light Deadwood 1883;
 standard railway time 1883; rare internal combustion near horizon). These are constraints/authoring anchors, not automatic world
  events.
 PART VI - ENVIRONMENT & MACRO REGIME INPUTS
 6.1 EnvironmentalState
 Represent enough regional/local environment state for causal agriculture/logistics outcomes: precipitation/soil moisture proxy,
 temperature, snow, wind/exposure, grasshopper pressure, prairie-fire fuel/conditions and river/route conditions where relevant. Exact
  physical fidelity remains a performance/calibration decision.
 6.2 Crop/Forage/Fire Integration
 Environment feeds existing crop, forage, livestock, building/fire and route systems. Drought lowers actual yields/forage; grasshoppers
 consume actual vegetation; fire propagates through fuel/weather/ignition/mitigation; river conditions change route availability/capacity.
  Historical regime data sets priors/events but never posts economic outcomes directly.
 6.3 Macro/Credit Integration
 Panic-era regime data can alter lender liquidity/risk priors, investor appetite, railway-finance feasibility and information signals. Loan
  denial, project suspension, default, asset sale or migration still executes through lenders, obligations, Projects, Transactions and
 Persons.
 6.4 Correlated Events
 Reuse Revision VII correlated-exposure doctrine beyond livestock where appropriate. One drought/storm/fire can affect multiple
 farms/routes/businesses through a shared incident/context rather than independent identical random rolls.





                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 69 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      5

 PART VII - AGRICULTURE, EQUIPMENT & MATERIAL-SUPPLY AUTHORING
 7.1 Farm Generation Inputs
 Do not generate FarmLevel. Generate/read: land/right mix; improved/unimproved acreage; buildings; fencing; draft animals;
  productive livestock; equipment access; household labor; hired labor; working capital/debt; crop/livestock mix; local
  market/connectivity. Archetypes may seed distributions but runtime truth remains component state.
 7.2 Equipment Adoption
 Equipment method selection already exists conceptually through EquipmentRequirementSet. Add availability/supportability queries so
 a method can be technically valid but regionally inaccessible or economically infeasible. Hired machine service should create real
  provider Task/Journey/Asset reservations.
 7.3 Construction Material Supply
  Building recipes/projects should resolve actual locally obtainable materials and transport cost. Rail/river access can change available
  supplier sets and landed cost, enabling different building forms without a SettlementTech gate.
 7.4 Historical Farm Dataset
 Create an authoring dataset from 1870/1880/1890/1900 Census observations rather than hard-coded archetype constants. Preserve
 geographic applicability and confidence. World generation samples plausible correlated farm states rather than independently
 randomizing every field.
 PART VIII - POSTAL, MESSAGE & MAIL-ORDER ARCHITECTURE
 8.1 Message/Information Channel
  Information delivery should reference ChannelType such as InPerson, Messenger, PostalLetter, Newspaper/Print, Telegraph,
 Telephone, Report/Ledger or SystemUI. Channel affects travel/transmission delay, cost, evidence/document status, availability and
  possible origin/destination endpoints.
 8.2 PostalRoute/Access
 Postal access can be implemented as institution/network/service data over the shared route graph. Household/Business read models
 expose nearest post office, service frequency and later RFD service. Mail collection may create actual Person errands until delivery
  service exists.
 8.3 MailOrderIntent
  Mail order reuses ProcurementNeed + remote PurchaseIntent + Message/Order + Agreement/Transaction + Shipment.
  Catalog/advertising changes Awareness/feasible suppliers. Heavy goods still require freight/express/rail physical fulfillment; do not
 create MailOrderInventory directly at the household.
 8.4 Time Standards
 Keep one absolute TimeManager authority. Historical schedules can reference TimeStandardId / UTC-equivalent offset logic before
  standardization. A dated authoring event on November 18, 1883 can shift regional railway timetable presentation without changing
 elapsed-time physics.





                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 70 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      6

 PART IX - RECORDS, DOCUMENT QUALITY & OFFICE EQUIPMENT
 9.1 Record Quality Dimensions
 Where consequential, Record/Document evidence may track or derive ContentAccuracy, ArithmeticAccuracy, Legibility, CopyFidelity
 and Completeness/Organization rather than one quality float. Do not require all dimensions for every trivial note.
 9.2 Production Method
 Document method may reference handwritten, preprinted form, typewritten, carbon-copied or other supported processes. Person
  clerical/penmanship/typewriting capability plus equipment and workload determine duration/error/legibility. Typewriter reduces
 ambiguity/copy burden but cannot prevent an incorrect source value from being typed.
 9.3 Equipment Integration
  Typewriter, cash register, adding machine, safe/file system and similar office equipment should be
 EquipmentAsset/Workstation/ToolKit capability where scarcity, maintenance or reservation matters. They feed actual Tasks such as
 Correspondence, InvoicePreparation, LedgerPosting, Reconciliation or StatementPreparation - not generic AdministrationEfficiency.
 9.4 Discrepancy Events
 Record misread, arithmetic error, omitted entry or conflicting copies can create an ordinary discrepancy/evidence-investigation path
 using Revision VI internal-control architecture. Keep such events rare enough that office equipment remains meaningful without
 becoming clerical-error spam.
 PART X - REPOSITORY MIGRATION CHECKLIST FROM PRIOR AUDIT
 10.1 Status
 These findings came from the earlier public/baseline repository audit and must be rechecked against the current code before edits.
 They are migration targets if still present, not claims about an unaudited newer branch.
 10.2 Household Membership
  If Person.householdId and Household.memberIds both remain causal/persisted truth, migrate toward one HouseholdMembership
  relationship authority with derived/cached views. Boarding/lodging must remain separate from domestic economic membership.
 10.3 Employment
  If Person profession/wage/workplace and Business WorkerSlot assignment/wage remain independent authorities, create/extend one
 EmploymentRelationship authority. Worker slots become role/vacancy/staffing requirements or reference active employment; a
  display name alone must not constitute a real staffed Person.
 10.4 Synthetic Customer Sales
  If General Store or another business still resolves household demand/town-pulse demand directly into revenue without a legitimate
  acting Person/channel, stop execution at ProcurementNeed/Intent and route through Activity/Journey/service/Transaction. Aggregate
 demand remains analytical.
 10.5 Synthetic Cash Inflows
 Remove or isolate bootstrap/debug paths that periodically add generic household income or minimum startup working capital without
 an EconomicEvent source. Off-map income is allowed when represented as an explicit external-employer/remittance/investment/etc.
 source.
 10.6 Service Without Activity
 Medical, retail, hospitality and comparable local services must reserve/consume real Person time and spatial/service capacity when
 embodied. A domain manager may select a patient/customer but cannot skip the shared Activity/Journey/Transaction authorities.
 10.7 Fast-Forward / Due Event Safety
  If TimeManager can compress short ticks/day notifications, Activity, obligations, projects and historical events must resolve elapsed
 due boundaries from authoritative timestamps rather than assuming every intermediate tick/day callback fires. Add an
 elapsed-time-safe due-event scheduler or equivalent.





                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 71 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      7

 PART XI - PERSISTENCE, PERFORMANCE & AUTHORING VERSIONING
 11.1 Persist Consequential Historical State
  Persist active infrastructure Projects/segments/service state, current corporate relationships needed for simulation,
 TechnologyAccess/install/support states when consequential, environmental incidents that cross saves, scheduled
 messages/orders/shipments and historical-regime/version references required for deterministic continuation.
 11.2 Compact Research/History Appropriately
 Do not embed entire source bibliographies in every save. Runtime data references stable authoring IDs/versions. Long-completed
  infrastructure/corporate history can compact after updating retained segment/entity histories and scenario/audit evidence.
 11.3 Historical Data Version
 Save files should record historical-authoring dataset version alongside game/system version. If authoring research changes a
  date/rule after a campaign begins, migration policy decides whether the saved world preserves generated history or adopts corrected
  future-only data. Never silently reposition already-built track or businesses on load.
 11.4 Performance
  Historical scale does not require frame simulation of the whole territory. Use event-driven/coarse route, project, environmental and
  settlement resolution while preserving IDs, inventories, cash, rights, travel time and commitments. Regional aggregates may
  forecast/plan but cannot anonymously mutate local entity truth.
 PART XII - REVISION VIII ACCEPTANCE & REGRESSION FIXTURES
 1870 OPENING - Generate Open Frontier at spring 1870. Confirm no locally operating railroad is fabricated where the historical
 skeleton supplies only outside rail access; wagon/river routes and sparse institutions remain usable.
 TRACK LAID != SERVICE - Create a segment with track complete but no operating service. Confirm freight/passenger routing cannot
 use it until ServiceOpen.
 RAIL CORPORATE SUCCESSION - Lease/operate/sell/reorganize a segment. Confirm physical SegmentId and prior
  contracts/history persist while owner/operator/controller queries change by date.
 RAIL-CREATED TOWN - Open a depot on a new site near an older settlement. Confirm businesses/migration can choose either
 node; no forced old-town growth bonus.
 MULTIMODAL SHIPMENT - Move cargo wagon -> river/rail -> drayage. Confirm custody/location/cost and transshipment reconcile
  without duplicate inventory.
 RAIL RECONFIGURES WAGON - Open rail service and verify long-haul wagon demand can fall while depot/drayage demand can
 increase from actual route economics.
 TECH EXISTS BUT UNSUPPORTED - Give a business an owned machine lacking required parts/fuel/operator support. Confirm the
 asset remains owned but its method is unavailable until support resolves.
 EARLY ADVANCED FARM - Create a highly capitalized connected 1870s farm with early machinery evidence. Confirm date rules
 permit it without making the technology universal to neighboring farms.
 DROUGHT CHAIN - Apply regional drought context. Confirm crop/forage/livestock/finance consequences arise through actual state,
  with no direct RevenuePenalty mutation.
 PANIC 1873 - Tighten lender/rail investor conditions. Confirm some projects/loans fail or reprice through counterparties while
 unrelated cash is not automatically removed.
 MAIL ORDER - Create catalog awareness, remote order, payment and rail/express shipment. Confirm no household inventory
 appears before physical delivery/collection.
 RFD ERRAND CHANGE - Before RFD, require a legitimate mail-collection trip; after route activation, confirm mail delivery can occur
  without that errand while goods still follow shipment rules.
 HANDWRITING VS TYPEWRITER - Generate a consequential handwritten record with low legibility and a typed record from the
 same source. Confirm legibility/copy risk differs but wrong source values can remain wrong in either method.
 FAST FORWARD DUE EVENTS - Jump across multiple days/months. Confirm scheduled obligations, Activities, messages, project
  transitions and historical events resolve once each without depending on every tick callback.
 NO SYNTHETIC INCOME - Advance a week with no legitimate household income source. Confirm no generic wallet refill occurs.


                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 72 of 170

LAND & LEDGERS / REVISION VIII / TECHNICAL IMPLEMENTATION BIBLE                                                                      8


 NO PHANTOM TOWN SALES - Increase aggregate demand/traffic model without Persons or remote purchase channels. Confirm no
 business revenue posts.
 PART XIII - IMPLEMENTATION PRIORITY
 13.1 P0 - Integrity / Migration Before Historical Expansion
   * Re-audit current repository for Person/Household membership, Employment, synthetic customer sales, synthetic household/startup
    cash and service-without-Activity paths.
   * Preserve/fix shipment inventory integrity and transaction actor provenance from Revision VI/VII.
   * Implement elapsed-time-safe due-event handling before relying on long historical schedules/fast-forward.
   * Add authoring-data/version foundation for 1870 historical envelope.
 13.2 P1 - Historical World / Transport Spine
   * HistoricalSkeleton + procedural regional generation boundaries.
   * RailSegment development/service state and dated corporate relationships.
   * Multimodal route graph support for road/stage/ferry/river/rail and transshipment.
   * Rail node/facility capability and railway-created settlement workflow.
 13.3 P2 - Technology / Environment / Communications
   * TechnologyDefinition + regional availability + access/supportability resolution.
   * EnvironmentalState and HistoricalRegime inputs feeding agriculture/logistics/finance.
   * Postal/message channel, mail-order procurement and RFD access.
   * Record/document production methods and office equipment hooks.
 13.4 P3 - Authoring Depth & Long-Run QA
   * Railway GIS/segment dataset and corporate succession reconciliation.
   * 1870/1880/1890/1900 farm generation dataset.
   * Technology-diffusion/evidence database and settlement/Sanborn longitudinal dataset.
   * Long-run 1870-1905 regression across transport, settlement, institutions, environment and save/load.
 PART XIV - RESEARCH / CALIBRATION HOLDS & SUPERSESSION
 14.1 Holds
   * exact railway tariffs, schedules, locomotive rosters, rail weights, bridge ratings and complete branch succession;
   * farm-generation percentile bands, equipment ownership rates, yields, wage/price series and dealer availability;
   * exact technology adoption/repair-network rates, office-equipment error frequencies and mail-order penetration;
   * weather/yield, grasshopper/fire and route-seasonality coefficients;
   * complete Sanborn/business-prevalence extraction and all uncommon legal edge cases.
 14.2 Supersession
 Revision VIII supersedes generic Technical references that define spring 1880 as the normal campaign start or treat 1880 as the
 opening equipment/technology baseline. It does not invalidate dated 1880 records. Revision VII livestock/land/delegation architecture
 and Revision VI continuous-Person/Activity/Transaction/Equipment/Institutional architecture remain active and are deepened rather
 than replaced.
 END OF REVISION VIII INTEGRATION
 The preserved Technical Implementation Bible Revision VII follows unchanged and remains authoritative except where Revision VIII
  explicitly adds, deepens, corrects or supersedes technical guidance.



                                                 LAND & LEDGERS

## TECH-XIV — original PDF page 73 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               1


 LAND & LEDGERS
 LAND & LEDGERS
TECHNICAL IMPLEMENTATION BIBLE - REVISION
 VII
 ASHES & NOTES Systems Integration: Capability Calibration, Animal Authority, Livestock Health &
 Markets, Rural Land Control, Delegated Authority, Calendar, Freight & Historical Divergence
  Revision date: September 21, 2026
 Source baseline: Technical Implementation Bible Revision VI - September 19, 2026
 Canon compatibility target: Canonical Project Bible Revision XXIV
  Status: Active technical planning reference - repository audit remains required before implementation claims
 PRESERVATION GUARANTEE
  Revision VII preserves Technical Implementation Bible Revision VI in full except where this integration explicitly adds, deepens,
  corrects or supersedes technical guidance. The complete Revision VI document follows this integration unchanged as the preserved
  baseline. Canonical Project Bible Revision XXIV controls intended behavior. Repository evidence remains authoritative for what is
  actually implemented; conceptual names below are target architecture, not proof of existing Unity classes.
 PURPOSE
  Revision VII translates the ASHES & NOTES integration into implementation architecture. Revision VI already establishes continuous
 Person identity, shared activity/time, task-level equipment requirements, agreement/economic-event depth, institutional continuity and
  event-fed read models. Revision VII concentrates on newly exposed integrity needs: capability-distribution calibration, delegation-first
  authority, animal identity/genetics, causal mortality, differentiated livestock markets, stocker economics, finite dairy markets, rural
  land-control rights, universal calendar projection, character-sourced information, 1:1 world distance and long-run rural acceptance
  fixtures.
 REVISION VII ARCHITECTURE LOCKS
    • Capability must be stored at enough internal granularity that player-facing bands are thresholds/read models rather than seven
     discrete XP levels.
    • Capability presentation must enforce High=1 band, Moderate=2 adjacent bands, Low=3 adjacent bands and Not enough information
    when evidence is insufficient.
    • Delegated authority is a relationship over Person + scope + permissions/policy/limits; role titles are not the authority source.
    • Animal identity and historical pedigree must persist when economically material; sold/dead animals may compact but cannot
     disappear when history depends on them.
    • Actual pedigree/genetics/reproductive state are distinct from observed/recorded evidence.
    • Calf mortality resolves from stage-specific causal state and can be correlated across animals sharing weather, environment or
     disease exposure.
    • Livestock market opportunities are channel/event data over actual animals and buyers; market UI is not a sale authority.
    • Stocker-lot economics require acquisition, carrying, biological gain, financing and settlement history rather than a profit modifier.
    • Dairy production flows into physical allocation/processing/storage and finite buyer relationships.
    • Land control distinguishes ownership, contracts/leases, grazing rights and nonexclusive/open-range use.
    • Calendar entries are projections from authoritative obligations/events and may contain date ranges/certainty; the calendar is never a
    second scheduling truth.
    • World coordinates preserve 1:1 economic distance across active and coarse simulation.
    • Historical infrastructure projects resolve against current world rights/resources rather than unconditional date scripts.
    • All Revision VI P0 shipment/inventory-integrity requirements remain active and are not superseded.



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 74 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               2

 PART I - CAPABILITY AUTHORITY & POPULATION CALIBRATION
 1.1 Continuous Actual Capability
  ActualCapability should use a continuous or sufficiently granular internal representation. Presentation bands are thresholds over that
  internal value. Do not implement progression as Average -> Great -> Excellent -> Mastered level-ups.
 1.2 Capability Formation
  Capability development may consume prior experience, relevant task exposure, task difficulty, novelty, feedback, mentorship,
  aptitude, physical constraints, health/injury and domain familiarity. Calendar time without relevant activity contributes no automatic
  increase. Repeated already-routine work should have diminishing developmental effect.
 1.3 Population Calibration Service / Harness
 Add a calibration path capable of generating and evolving large populations, then reporting capability distributions by age, occupation,
  years of relevant experience, domain and candidate pool. This is QA/calibration infrastructure rather than runtime player UI.
 TECH LOCK - Regression tests must detect systemic upper-band inflation. Average cannot cease to be the central ordinary
 band merely because long campaigns generate experienced workers.
 1.4 Tail Protection
 Mastered and Abysmal should be reached by true underlying capability tails rather than guaranteed special slots per world seed.
  Exact prevalence remains calibration. Experience can shift probability but cannot directly force Mastered.
 1.5 Confidence Rendering Invariant
 TECH LOCK - High returns one band; Moderate returns exactly two adjacent bands; Low returns exactly three adjacent
 bands; insufficient evidence returns Not enough information.
 Add development/debug validation so illegal width/confidence combinations are surfaced before presentation. Existing evidence
  sources and Known/Recorded separation remain authoritative.
 PART II - DELEGATED AUTHORITY & DELEGATED ACTIVITY
 2.1 AuthorityAssignment
  Generalize delegation as a relationship rather than a role class. Conceptual fields may include AuthorityAssignmentId, PersonId,
  BusinessId/SiteId/PortfolioScope, TaskFamily/DecisionFamily, recurring/one-time status, spending/credit/commitment limits, hiring
  authority, policy references, owner locks, escalation conditions, effective dates and grantor.
 Do not rename working repository authorities merely to match this conceptual name. Repository audit determines whether existing
  DelegatedAuthority can be extended cleanly.
 2.2 Titles Are Metadata/Presentation
 Manager, GeneralManager, Foreman or Superintendent may remain occupation/role labels, but execution permission must resolve
  from authority relationships, governance and ordinary Task/Agreement/Employment systems.
 2.3 DelegatedActivityEvent
  Generalize ManagerDecisionEvent direction toward a broader Delegated Activity event capable of representing a delegated decision,
  purchase, sale, staffing action, schedule change, inspection, coordination step or exception response. Financial truth remains in
  authoritative transactions and obligations.
 2.4 Nested Coordination
 A Person with scheduling/supervisory authority can create eligible subordinate Tasks/Activities. Nested assignment must still reserve
  the subordinate Person's time, location and resources through shared authorities.
 2.5 Cross-Business Authority
 One Person may hold assignments across multiple businesses/sites. Do not merge BusinessRuntimeState, owner cash, debt,
  inventory or transaction authorities because the responsible Person is shared.




                                               LAND & LEDGERS

## TECH-XIV — original PDF page 75 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               3

 PART III - ANIMAL REGISTRY & HISTORICAL IDENTITY
 3.1 Animal Authority
 Add or extend a persistent Animal authority where individual resolution matters. Conceptual state: AnimalId; species/breed context;
  sex; date/estimated date of birth; birthplace; current owner/business; current physical location; dam/sire links; biological state;
  observed/recorded state; ownership history; production/reproductive history; health history; commercial status; final disposition.
 3.2 Stable Identity
 TECH LOCK - Animal IDs are never reused. Sale or death changes state/disposition; it does not rewrite history.
 3.3 Lightweight Historical Animals
  Animals no longer requiring active full simulation may compact into lightweight historical records while retaining the identifiers and
  fields needed by pedigree, objective evidence, transaction history and later audit.
 3.4 Cohort Compatibility
  Large commercial groups may use LivestockCohort representation when individual differentiation is immaterial. The architecture must
  allow promotion from cohort to individual representation where an animal becomes consequential without creating duplicate
  ownership or inventory truth.
 PART IV - PEDIGREE, GENETICS & REPRODUCTION
 4.1 ActualPedigree vs RecordedPedigree
  Store actual biological parent links separately from claims/evidence. A pedigree claim or record can reference source, date,
  confidence, documentation and claimant. Unknown ancestry propagates uncertainty rather than being interpreted as zero
  relatedness.
 4.2 Genetic Profile
 Use hidden inherited values or another data-driven genetic abstraction sufficient to produce supported inherited traits. Avoid one
  BreedingQuality multiplier. Exact allele/trait depth is a later implementation/calibration decision.
 4.3 Inbreeding
  Relationship/inbreeding derives from actual pedigree where known to simulation truth. Player-facing knowledge can be poorer. The
 system should affect inherited-risk/trait-expression mechanisms rather than apply a generic penalty directly from pedigree
  percentage.
 4.4 Reproductive State
  Animal reproductive state should support where relevant: not breeding-ready, eligible/cycling, serviced, pregnancy unknown,
 pregnancy evidence/suspected, late gestation, calving, postpartum, lactating, dry and reproductive-failure evidence. Do not expose
 one immediate isPregnant truth directly to the player after service.
 4.5 Parity
  Store parity/calving history independently of reproductive state. The player-facing Type remains Heifer until first calving, then Cow.
 4.6 Progeny Evidence
  Breeding assessments can update from observed offspring performance. Evidence changes the estimate; it does not rewrite hidden
  inherited state.
 PART V - LIVESTOCK HEALTH & CAUSAL MORTALITY
 5.1 Reproductive Outcome Pipeline
 Model pregnancy -> late gestation -> parturition -> neonatal survival -> pre-weaning survival -> weaning as distinct causal stages.
 Each stage consumes relevant biological/environmental state.





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 76 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               4

 5.2 Dystocia Inputs
  Potential inputs include dam age/parity, structural/pelvic suitability where modeled, fetal size, presentation, sire/dam genetics, dam
  condition, labor duration, observation delay and assistance capability.
 5.3 Neonatal State
  Track enough state to resolve birth vigor, cold/wet exposure, nursing/colostrum success, maternal acceptance, infection exposure
 and recovery/treatment. Exact biological fidelity remains calibrated to gameplay/performance needs.
 5.4 Maternity Environment
 Add environmental state or derived data sufficient to represent occupancy, crowding, bedding moisture/cleanliness, sanitation,
  ventilation/drafts, temperature/exposure and separation/isolation capacity. Nominal building capacity alone is insufficient.
 5.5 Correlated Exposure / Incident
 TECH LOCK - Weather and disease events must be able to affect multiple animals through shared causal exposure. Do not
  resolve a herd outbreak as independent identical death rolls.
 5.6 Livestock Mortality Calibration Harness
 Add long-run automated fixtures for ordinary operations, well-prepared operations, poor management, crowded maternity areas,
  severe weather birth periods, young-dam-heavy herds, purchased-stock introductions and stressful weaning. Export late losses,
  stillbirths, live births, 24-hour deaths, 7-day deaths, pre-weaning deaths, weaned calves and cause/context attribution.
  Historical target envelopes remain external calibration data. The harness measures whether causal parameters generate plausible
  aggregate outcomes; it must not tune toward one hard-coded target death probability at runtime.
 PART VI - LIVESTOCK COMMERCIAL CLASSIFICATION & MARKETS
 6.1 LivestockMarketOpportunity / MarketEvent
  Conceptual fields: MarketEventId; location; start/end/entry dates; channel type; eligible animal classes; documentation/evidence
  expectations; buyer pool; fees; transport requirements; market-depth/capacity evidence; settlement terms; information source.
 6.2 Commercial Classification Read Model
 A market-facing read model may classify likely applicable channels such as breeding stock, breeding prospect, stocker/feeder,
  finished commercial animal or cull/slaughter. Classification is derived from animal state/evidence and may itself be uncertain.
 6.3 Market Quote
 Quote records should retain buyer/market, timestamp, animal class, price basis, quantity/lot assumption, known fees, delivery
  responsibility, information source and expiry/staleness. A quote is information, not a guaranteed transaction.
 6.4 Commission Agreement
 Commission merchants reuse Agreement/Obligation architecture. Consignment links actual animals/product lots to Shipment and
  later sale/settlement events. Revenue recognition follows the applicable economic-event lifecycle rather than occurring when the
  consignment is booked.
 6.5 Objective Shipment Integrity
  Cumulative rail-liveweight objectives should consume authoritative completed shipment/sale evidence with qualifying origin and
  liveweight. Booking, loading intent or market quote does not qualify unless the objective explicitly defines that state.
 PART VII - STOCKER/FEEDER LOT ACCOUNTING
 7.1 Stocker Lot Read Model
 Purchased stocker cohorts should support acquisition date, head count, starting estimated/actual weight, purchase cost, financing,
  attributed feed/pasture cost, health loss, labor, transport, current estimated value and realized settlement contribution.





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 77 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               5

 7.2 No Guaranteed Margin
 TECH LOCK - Do not store an ExpectedProfitMultiplier or StockerBonus. Profit emerges from actual biological and
 economic events.
 7.3 Financing Link
  Livestock-secured seller notes or other chattel financing should reference the actual purchased animals/cohort where practical.
  Sale/disposition of secured cattle must interact with creditor/security rights and settlement.
 7.4 Lot History
  Preserve enough weight and cost history to analyze contribution by lot without requiring every routine daily feed event to persist
  forever. Aggregation/compaction is allowed after authoritative state and audit outputs are updated.
 PART VIII - DAIRY, EGGS, PROCESSING & BUYER CAPACITY
 8.1 Milk Production
  Milk availability should derive from actual lactating animals and relevant state such as genetics, parity, days in milk, nutrition,
  condition, health, pregnancy stage, calf nursing and milking practice. Exact production curves remain calibration data.
 8.2 Milk Allocation
  Represent gross milk flow into nursing, household/employee use, sale, processing, byproduct/feed use and loss/spoilage rather than
  immediately converting production into revenue.
 8.3 ProcessingBatch
  Butter and cheese can reuse a processing-batch structure containing input lots, process type, worker/task, workspace/equipment,
  ingredients, start/end, yield, quality, storage requirements and byproducts. Do not add domain-specific cash shortcuts.
 8.4 Perishability
  Product lots should expose shelf-life/spoilage behavior at an appropriate abstraction level. Transport time and storage condition can
  affect usable/saleable state.
 8.5 Buyer Capacity / Standing Account
  Recurring buyer relationships should store committed, stated, estimated or unknown practical demand/capacity rather than accept
  unlimited quantity. Capacity can update from negotiations, order history and buyer conditions.
 8.6 Quality/Inspection
 Processed farm goods can be accepted, downgraded or rejected through ordinary inspection/acceptance evidence where materially
  relevant. A first trial consignment can therefore establish or fail to establish a standing commercial relationship.
 PART IX - RURAL LAND CONTROL & GRAZING RIGHTS
 9.1 LandInterest / UseRight
 Extend property relationships to distinguish fee ownership, purchase contract/equitable interest where applicable, lease, seasonal
  grazing agreement, other exclusive use right and nonexclusive/tolerated/open-range use. Exact legal labels vary by regime; the
  conceptual distinction is mandatory.
 9.2 Security of Control
 A derived read model may explain security/expiry but does not replace the underlying right. Open-range use can end because world
  ownership/legal state changes. Lease rights end according to agreement. Ownership persists until valid disposition/enforcement.
 9.3 Parcel Actions
  Action Discovery should derive available interactions from parcel/legal state: Approach Owner, Make Offer, Request Lease, Contact
 Land Agent, Review Public-Land Entry, Inspect Title or No Ordinary Acquisition Available. UI cannot create a permission the
  legal/ownership state does not support.



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 78 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               6

 9.4 Agricultural Acreage Read Model
  Portfolio/farm reporting should separately expose Owned, Long-Term Controlled/Leased, Seasonal Grazing and
 Nonexclusive/Open-Range Use. Never sum these categories into Owned Acreage.
 9.5 Open-Range Loss Fixture
  Support a world event in which previously used nonexclusive public/open range becomes legally claimed or otherwise unavailable.
 The ranch loses use but no owned parcel is deleted. This should propagate to carrying-capacity/plan decisions.
 PART X - UNIVERSAL CALENDAR
 10.1 CalendarEventProjection
  Calendar entries are projections/read models from authoritative sources such as FinancialObligation, Employment/Payroll, Lease,
  Agreement, Shipment, Project, Animal/Reproduction, MarketEvent, DelegatedPolicy or player-authored notes.
 10.2 Calendar Entry Data
  Conceptual fields may include CalendarEntryId, source reference, title, category, responsible Person/business, exact date or
  start/end range, certainty, status, urgency/priority, location and linked action/view.
 10.3 Forecast Dates
 TECH LOCK - Do not create a fake authoritative exact date merely to place an uncertain event on a calendar. Forecast
 windows remain windows.
 10.4 Source Synchronization
 When the source obligation/event changes, the projected calendar entry updates. Completion, cancellation, reassignment or
 amendment propagates through source linkage. The calendar never becomes a second debt, shipment, project or animal-event
  authority.
 10.5 Filters
  Category filters affect presentation only. Hiding payroll does not disable payroll or prevent alerts generated by underlying obligations.
 PART XI - INFORMATION PROVENANCE & CHARACTER DELIVERY
 11.1 Alert Provenance
 Where appropriate, extend player-facing information with SourcePersonId, SourceBusinessId, SourceInstitutionId and/or
 SourceDocument/MessageId. The source remains optional for system-owned UI events.
 11.2 Delivery Channels
  Information can arrive through conversation, letter, telegram, notice, report/ledger or system UI. Delivery timing and content must
  respect knowledge, communication and travel state.
 11.3 Advice Candidates
  Contextual guidance may discover actions such as Find Another Buyer, Ask Station Agent, Request Distant Quote, Hire Commission
  Agent, Convert Surplus Milk or Arrange Shipment. These are feasible-action suggestions over real conditions, not optimal-strategy
  rankings.
 PART XII - WORLD DISTANCE, RESOLUTION & HISTORICAL
 DIVERGENCE
 12.1 One-to-One World Distance
 TECH LOCK - Authoritative spatial coordinates and route lengths must preserve 1:1 economic distance.
 Rendering/streaming techniques may shift origins or reduce detail but must not compress travel economics.





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 79 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               7

 12.2 Off-Map Resolution
  Transition to coarse simulation preserves entity ID, route, distance remaining, cargo, time, ownership, condition and commitments.
  Returning to active simulation remanifests the same state.
 12.3 Historical Project State
  Historical infrastructure should be represented through persistent actors, plans/projects, corridors, rights and resource/finance state.
  Avoid unconditional if date == historicalDate then force local completion logic when rights, route or capital have diverged.
 12.4 Scenario Basis Metadata
 Extend ScenarioDefinition with basis classification such as Historical/Fictional plus optional historical-basis summary and
  source/reference metadata. Basis is presentation/context, not a simulation-rule switch.
 PART XIII - FREIGHT MICRO-BUSINESS & PORTFOLIO LOGISTICS
 13.1 No Mandatory Yard
  Freight capability derives from actual team(s), wagon(s), drivers, feed/water/stabling access, cargo handling and route availability. A
 one-team carrier may operate without owning a yard or warehouse.
 13.2 Host-Site Agreement / Policy
 A freight business using another controlled farm's stable/site should have an explicit or policy-backed relationship sufficient to
  attribute feed, stabling, maintenance, space burden and cost. Shared ownership is not free capacity.
 13.3 Internal Freight
 Owned-business hauling creates real Trip/Shipment work and costs. Separate businesses may record intercompany charges;
  consolidated reporting eliminates artificial external revenue while preserving individual-business performance.
 13.4 Exclusive Resource Reservation
  Dedicated or shared horses, wagons and teamsters continue to use the shared reservation authority. A team hauling hay cannot
  simultaneously accept a merchant load.
 PART XIV - HUD & CONTEXTUAL READ MODELS
 14.1 Universal Financial Strip
 Recommended compact universal state: Date | Owner Cash | Current Business Cash | Debt/Liabilities. Exact display can adapt to
  screen size. Monetary authority remains exact even if presentation is rounded.
 14.2 Contextual Rural Panel
  Agricultural context may expose cattle/head structure, breeding herd, commercial animals, hay/feed, water, shelter, land/control and
  near-term reproductive/market events. Do not place cattle-specific fields in the universal HUD for unrelated businesses.
 14.3 Objective Presentation
 Completed historical/event objectives should present their persisted completion date/evidence. Incomplete numeric objectives should
  derive exact numerator/denominator from authoritative Lifetime Ledger/Shipment/etc. rather than generic In Progress text.
 PART XV - ASHES & NOTES END-TO-END ACCEPTANCE FIXTURE
 15.1 Fixture Purpose
 Add ASHES & NOTES as a long-run rural integration fixture. The purpose is state integrity and emergent-system validation, not
  reproduction of the text playtest's exact prices, dates or success path.
 15.2 Required Scenario Stages
    • Create one fire-damaged farm with surviving assets and a secured/seller obligation; confirm destruction and debt remain distinct.
    • Rebuild enough shelter/feed capacity while preserving exact owner/business cash and creditor obligations.


                                               LAND & LEDGERS

## TECH-XIV — original PDF page 80 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               8


    • Hire workers with realistic capability distribution and evidence-limited estimates; verify no systemic Great inflation.
    • Breed cattle through persistent pedigree/reproductive state and resolve multiple calving seasons with causal mortality.
    • Reach finite local dairy demand, create additional buyer/commission relationships and physically allocate milk into sale/processing.
    • Activate a rail market through independent historical/world project state; no objective may teleport service open.
    • Execute qualifying rail cattle shipments and record cumulative origin/liveweight evidence.
    • Delegate routine operations for an owner-absence period; verify no hidden owner callback is required for ordinary work.
    • Acquire or control additional land through different tenure forms and explicitly distinguish open-range use.
    • Run at least one stocker cohort from acquisition through biological gain, financing and sale.
    • Operate a small freight business using actual teams/wagons and internal/external hauling without creating consolidated fake
     revenue.
    • Scale to multiple controlled farms while preserving separate cash/debt/inventory and shared Person/asset scheduling.
    • Complete the revised 15,000-pound and 100,000-pound rail objectives from qualifying Shipment/Lifetime Ledger evidence.
    • Save/load at several points and reconcile animals, pedigree, cash, debt, land rights, shipments, objectives and delegated activity
     idempotently.
 15.3 Fixture Export Bundle
    • animal births, deaths and attributed causes/context;
    • pedigree consistency and compacted ancestor references;
    • service/pregnancy/calving history;
    • animal/cohort purchases, financing, sales and weight history;
    • cumulative qualifying rail liveweight by origin/business;
    • stocker-lot carrying and realized economics;
    • milk/egg production allocation, processing lots and buyer capacity;
    • hay/feed, shelter/maternity environment and land-control state;
    • Person assignments, cross-site travel and delegated decisions;
    • freight trips and internal-transfer elimination;
    • business cash, owner cash, debt, payables and scenario objective evidence.
 PART XVI - REGRESSION TESTS & INVARIANTS
    • Capability width: force High/Moderate/Low estimates and verify 1/2/3 adjacent bands exactly.
    • Population inflation: simulate long worker careers and fail calibration if Great/Excellent become ordinary across unrelated
      capabilities.
    • Animal ID: sell/death/save/load and verify IDs are never reused or duplicated.
    • Pedigree persistence: compact dead/off-map ancestors and verify living descendants retain valid parent references.
    • Pregnancy uncertainty: service an animal and confirm player state does not instantly expose hidden pregnancy.
    • Correlated outbreak: create shared contaminated/crowded exposure and verify several animals can be affected from one incident.
    • Maternity capacity: keep nominal shelter capacity under limit while degrading bedding/crowding and confirm health risk still
     changes.
    • Stocker note: secure purchased cohort, sell animals and verify creditor/security settlement is respected.
    • Finite buyer: exceed standing dairy buyer capacity and confirm unsold inventory/alternate processing rather than automatic sale.
    • Market event: miss an entry deadline and verify the buyer pool/opportunity closes without deleting the animals.
    • Open range: remove insecure use due to legal world change and verify owned/leased acres remain unchanged.
    • Calendar sync: amend/cancel a loan or shipment and verify projected calendar entry updates without duplicate authority.


                                               LAND & LEDGERS

## TECH-XIV — original PDF page 81 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                               9


    • Cross-business Person collision: assign the same worker to overlapping tasks at two farms and verify one assignment waits/fails.
    • Freight consolidation: charge a controlled farm for internal hauling and verify individual ledgers move while consolidated external
    revenue does not increase.
    • Historical divergence: acquire a parcel needed by a historical project and verify the project responds through ordinary
     rights/route/negotiation rather than forced completion.
    • Rail objective: book but do not complete a shipment and verify cumulative shipped-weight objective does not advance.
    • Shipment integrity: retain all Revision VI blocked-shipment/inventory P0 tests; no source-restored lot may coexist with fabricated
     loaded cargo.
 PART XVII - IMPLEMENTATION PRIORITY
 17.1 P0 - State Integrity & Calibration Invariants
    • Capability presentation invariant and anti-inflation calibration harness.
    • Animal identity/pedigree persistence and actual-vs-recorded separation.
    • Causal reproductive/calf mortality pipeline with correlated exposure support.
    • Preserve/repair Revision VI shipment and inventory authority before provenance-dependent objectives rely on it.
    • Land-control rights separation and save/load integrity.
 17.2 P1 - Rural Commerce & Delegation
    • Delegation-first AuthorityAssignment / DelegatedActivity integration over existing authority code.
    • Livestock market channels/events and commission agreements.
    • Stocker lot economics and livestock-secured finance references.
    • Finite dairy buyer capacity, milk allocation and basic butter/cheese processing.
    • Calendar projection/read model with date-range certainty.
 17.3 P2 - World/UX/Portfolio Depth
    • Character/source provenance on notices and contextual guidance.
    • 1:1 distance/streaming enforcement and historical-project divergence behavior.
    • Scenario Historical/Fictional basis metadata and Historical Basis UI.
    • Freight micro-business/no-yard configurations and portfolio internal service.
    • Contextual rural HUD and long-run ASHES & NOTES acceptance fixture.
 PART XVIII - RESEARCH / CALIBRATION HOLDS & DEEPENING MAP
 18.1 Holds
    • Exact capability-band population percentages and Mastered career thresholds.
    • Historical calf mortality, disease and veterinary-treatment rates by place/date.
    • Cattle growth curves, prices, market spreads and breeding-stock premiums.
    • Dairy/egg production curves, process yields, spoilage and buyer capacities.
    • Farm/acre prices, railroad-land terms, private/public ownership distributions and open-range law/custom.
    • Forage carrying capacities, hay yields, shelter/maternity capacity and seasonal labor ratios.
    • Horse/wagon costs, freight rates and optimal carrier scale.
    • ASHES & NOTES exact deadline and economic balance.
 18.2 Deepening Map
    • Revision VI capability evidence is deepened by continuous capability, population calibration and rendering invariants.


                                               LAND & LEDGERS

## TECH-XIV — original PDF page 82 of 170

LAND & LEDGERS / REVISION VII / TECHNICAL IMPLEMENTATION BIBLE                                                              10


    • Revision IV/V delegated authority is deepened into delegation-first AuthorityAssignment and broader DelegatedActivity.
    • Revision VI animal/equipment reservation direction is deepened by persistent livestock identity, pedigree and reproduction state.
    • Existing logistics/provenance systems are deepened by differentiated livestock markets and qualifying rail-liveweight evidence.
    • Existing Agreement/finance architecture is reused for commission merchants, stocker seller notes, land contracts and grazing rights.
    • Existing Project/large-world systems are deepened by 1:1 distance and historically divergent infrastructure state.
    • Existing read-model architecture is deepened by calendar projection, rural HUD and source-provenance notices.
    • Existing long-run autonomous QA is deepened by the ASHES & NOTES rural integration fixture.
 END OF REVISION VII INTEGRATION
 The preserved Technical Implementation Bible Revision VI follows unchanged and remains authoritative except where Revision VII
  explicitly adds, deepens, corrects or supersedes technical guidance.





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 83 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            1


LAND & LEDGERS
 TECHNICAL IMPLEMENTATION BIBLE - REVISION VI
 Continuous-Person & Physical-Capability Integration: Activity Authority, Customer Provenance,
 Equipment Requirement Sets, Agreement/Transaction/Project Depth & Institutional Continuity
 Revision date: September 19, 2026
 Source baseline: Technical Implementation Bible Revision V - September 18, 2026
 Status: Active technical planning reference - repository audit remains required before implementation claims

 PRESERVATION GUARANTEE
 Revision VI preserves Technical Implementation Bible Revision V in full except where this integration explicitly adds,
 deepens, corrects or supersedes technical guidance. The complete Revision V document follows this integration
 unchanged as the preserved baseline. Canonical Project Bible Revision XXIII controls intended behavior. Repository
 evidence remains authoritative for what is actually implemented; conceptual types below are target architecture, not proof
  of existing Unity classes.
 PURPOSE
 Revision VI translates the Revision XXIII continuous-Person, equipment, authority-consolidation and institutional-depth
  rules into implementation architecture. The central goals are to eliminate anonymous local economic activity, unify Person
  time/location across work and life, attach physical capability to task-level equipment/resource requirements, deepen the
 transaction/agreement/project cores and provide reusable authorities for governance, succession, internal controls,
 disputes, insurance, distress, utilities and labor markets.
 REVISION VI ARCHITECTURE LOCKS
    • PersonId is the persistent identity behind local human activity. Simulation resolution may change representation detail,
     never identity or economic continuity.
    • Locally embodied retail/service Transactions must identify BuyerPrincipal and ActingPerson; aggregate demand
     cannot directly post a sale.
    • One Person time/activity authority resolves incompatible use of the same Person across Tasks, travel, sleep,
     shopping, domestic work and leisure.
    • Current spatial state must have one authoritative source; occupancy, Journey, Activity and scene manifestation
     reference that state rather than storing contradictory locations.
    • TaskDefinition/Capability data specifies equipment/workstation/material/motive-power requirements and acceptable
      alternative methods. TaskInstance reserves actual resources.
    • Small tools may use ToolKit assets; significant capital equipment remains individually persistent when scarcity,
      condition, location, collateral or capacity matters.
    • Agreement contains obligations/conditions/amendments/history; execution objects such as Shipment, Task,
    WorkOrder and Project remain separate.
    • EconomicEvent/Transaction truth precedes bookkeeping and Lifetime Ledger attribution.
    • Projects preserve Baseline, CurrentForecast and Actual state rather than overwriting estimates.
    • Customer traffic, demand pressure and Opportunity/Pressure remain derived/event-fed read models.
    •  All new systems must survive save/load and on-map/coarse resolution idempotently.
 PART I - AUTHORITY CONSOLIDATION MAP

   Concern                    Preferred authority / rule                                    Do not create

   Person identity                Persistent Person + relationships                                       Temporary customer/worker species



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 84 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            2




   Concern                    Preferred authority / rule                                    Do not create

   Person time                Shared Activity/Time allocation over Tasks/Journeys/life activities             Separate availability flags per business system

   Person location               Single authoritative spatial state referenced by Activity/Journey/occupancy     Independent CurrentLocation copies

   Household demand          Household state + ProcurementNeed + Person action                            Retail Demand generator that posts sales

   Customer traffic              Derived visit/transaction history                                                       Traffic counter that spawns customers

   Agreement                Agreement + Obligations/Conditions/Amendments                          Separate supply/freight/lease contract truth where
                                                                                                    shared grammar suffices

   Resource reservation        Shared exclusive/committed resource reservation semantics                 Per-system double-booking logic

   Information                 Evidence/Known/Recorded/Forecast relationship                            Independent awareness/reputation truth silos

   Economic event             Transaction/EconomicEvent authority                                     Ledger entry as causal event

   Bookkeeping                Recorded-state representation of economic events                       Second money/inventory truth

   Lifetime Ledger                 Historical aggregation/attribution                                               Narrative reconstruction at scenario end

   Capability                   Derived from Person + equipment + workspace + materials + authority + time   BusinessLevel or HasCapability flag detached from
                                                                                                        resources

  Demand/O&P                Event-fed read models                                                      Opportunity object that fabricates its own cause

 1.1 Compatibility With Repository Audit
 Do not rename or replace working repository authorities merely to match conceptual names. First map existing Person,
 Task, inventory, transaction, travel, scheduling, equipment and customer code to the behaviors below. Migrate only when
 duplicate truth or missing authority is confirmed.
 PART II - CONTINUOUS PERSONHOOD & DAILY-ACTIVITY
 ARCHITECTURE
 2.1 Person Persistent State Extension
 Extend the existing Person direction so life continuity can be resolved without embedding every subsystem directly in
 Person. Minimum conceptual links/read state may include CurrentSpatialState, CurrentActivityId, ActiveJourneyId,
 AvailabilityReadModel, fatigue/recovery, recent sleep, food adequacy, current health constraints, household
  responsibilities and relevant preference/profile data. Exact storage follows repository audit.
 2.2 ActivityInstance
 Add or generalize a shared ActivityInstance/PersonActivity authority representing consequential occupation of a Person's
 time outside the narrow Task system. Conceptual fields: ActivityId; PersonId; ActivityType; purpose; start/expected
 end/actual end; location/destination; related Task/Business/Household/Agreement/Transaction intent; priority;
  interruptibility; status; related Persons. Activity types can include WorkTask, Travel, Sleep, Meal, Shopping, JobSearch,
 HouseholdChore, Childcare, SocialVisit, SaloonVisit, MedicalVisit, School, Rest and BusinessMeeting.
 2.3 Task and Activity Relationship
 TaskInstance remains authoritative work. When a Task consumes a Person's time, it creates/reserves the corresponding
  Activity interval rather than maintaining an unrelated WorkerBusy flag. Completion/cancellation releases the reservation
 through the same authority.
 2.4 One Spatial Authority
 Person physical position/region is authoritative. Journey mutates movement/progress; Activity explains purpose;
 ResidentialOccupancy explains the legitimate shelter relationship; scene GameObjects manifest the same state. None
 may independently place the Person elsewhere. Abstract interiors are valid spatial states when Canon permits them.
 2.5 Daily Planning Is Event/Schedule Driven
 Do not frame-poll every Person. Generate/revise activity blocks at meaningful triggers: new shift, need urgency, household
 event, travel completion, opening/closing window, illness, invitation/relationship event, job loss, service appointment or
  interruption. Coarse simulation can batch ordinary sleep/meals/household activity while preserving the same time budget
 and economic consequences.
 2.6 Household ProcurementNeed
 Represent economically meaningful needs as household/person procurement state rather than direct store demand.
 Suggested fields: NeedId; HouseholdId/beneficiary; product/service category; quantity/amount; reason; urgency;



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 85 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            3



 acceptable substitutes; known candidate suppliers/evidence; affordability/credit constraints; earliest reasonable action;
 deadline where real; deferrability; status. Ordinary small consumption may be batched by category/reserve; durable or
 consequential purchases can remain item-specific.
 2.7 Purchase/Service Intent & Acting Person Selection
 When a need should be acted upon, select a real acting Person or legitimate remote/delivery mechanism based on time
  availability, location, ability, household role, existing errands and knowledge. Trip chaining is allowed: a worker already
  traveling home may stop at a store without creating a separate teleporting shopper.
 2.8 Transaction Actor Provenance
 Extend Transaction/EconomicEvent grammar where needed with BuyerPrincipalType/Id, ActingPersonId, Beneficiary
 reference when material, seller/provider, time, location/channel, goods/service, quantity, consideration/payment method
 and source/destination inventory/possession. For ordinary local in-person retail/service, ActingPersonId is required.
 Mail/telephone/telegraph/standing-order exceptions still require legitimate initiating/fulfilling actors/channels.
   TECH LOCK - A local business cannot post revenue merely from aggregate CustomerDemand. The sale
   command must resolve a legitimate principal, acting Person/channel, supplier availability, stock/service
    fulfillment and settlement path.
 2.9 Meals & Consumption Events
 Food inventory remains physical. Meal preparation can create a batched Meal/Consumption event allocating portions to
 present Persons. Boarding/employment meal agreements can satisfy the consumer without creating a second retail
 purchase. Nutrition/food-adequacy state derives from actual meal/access history and household reserves rather than a
  parallel consumable currency.
 2.10 Sleep Events & Occupancy
 Sleep is a Person Activity linked to a valid shelter/location state. ResidentialOccupancy remains the continuing
 right/arrangement. A freight driver may sleep away from the primary residence without changing household membership; a
  hotel guest can sleep locally without becoming a resident.
 2.11 Alcohol Consumption
 Alcohol uses ordinary Inventory -> service Task -> Transaction -> Person Consumption. Optional human-condition state
 can summarize recent drinks/time and consequences. Do not create saloon-only inventory, customer or money authorities.
 2.12 PersonStub Promotion & Travelers
 Lightweight off-map kin/person stubs remain valid only while they are not materially participating in the live regional
 economy. When a stub migrates in, visits as a modeled traveler, becomes a counterparty or performs a locally modeled
  transaction, promote it to a persistent Person while retaining identity and relationships.
 2.13 Coarse Resolution Contract
 A coarse-simulated Person must still satisfy time, location, affordability, inventory/service availability and relationship
  constraints. Returning to active simulation must reconcile cash, possessions, health, schedule and location with the same
 PersonId. Resolution change cannot manufacture free time or erase a transaction.
 PART III - EQUIPMENT, TOOLKIT & PHYSICAL-CAPABILITY
 ARCHITECTURE
 3.1 EquipmentClass / CapabilityRequirement Data
 TaskDefinition should reference one or more EquipmentRequirementSets rather than a single optional list. A requirement
 set describes one feasible method of performing the task. Suggested data: requirement-set ID; required equipment
 classes; required workstation capabilities; motive-power requirement; minimum quantities; required condition/operational
  state; material/consumable inputs; collaboration/crew needs; method-specific duration/capacity/failure modifiers;
 date/region availability rules where needed.
 3.2 Alternative Method Sets
 A task can have several historically plausible methods. HarvestGrain may accept HandCradleMethod or
 ReaperBinderMethod. The resolver selects a feasible method according to available resources and policy; it does not infer
  that the modern/fastest method is mandatory.




                                               LAND & LEDGERS

## TECH-XIV — original PDF page 86 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            4



 3.3 EquipmentAsset
  Significant equipment should use the shared Asset lifecycle with persistent identity when location, condition, ownership,
 reservation, collateral, maintenance or utilization matters. Examples: plow, wagon, reaper, thresher, forge/anvil, oven,
  printing press, survey transit, steam engine, locomotive, dynamo, hoist.
 3.4 ToolKit Asset
 Use ToolKit definitions for low-value ordinary hand-tool collections. A ToolKit has a canonical contents/capability profile,
 owner/custodian, location, usable/condition state and reservation status. Examples: CarpenterHandToolKit, FarrierKit,
 StableGroomingKit, DoctorBag, SurveyFieldKit, RailSectionToolSet. ToolKit abstraction does not mean the underlying
  tools are historically unspecified.
 3.5 WorkstationCapability
 Fixed productive setups should expose capabilities derived from actual component assets/space: ForgeStation,
 BakeOven, SawmillSawLine, PrintingPressStation, PharmacyCompoundingBench, SaloonBar, AssayBench,
 TelegraphDesk, TelephoneSwitchboard, LocomotiveCab, WaterPumpStation. A functional space alone does not grant the
 workstation.
 3.6 Motive Power
 Represent motive power separately where it is a genuine constraint: human, horse, mule, ox, water, steam or electric.
  Draft animals remain Persons? No - they remain persistent Animal/Asset entities with feed, fatigue, health and assignment.
 Equipment may require a compatible power source/team before reservation succeeds.
 3.7 Access / Custody / Ownership
 Requirement resolution asks whether the operating actor/business has legitimate access, not only ownership. Accepted
 access sources may include owned, employer-provided, rented, leased, borrowed, customer-supplied, contractor-supplied
 or service-provider equipment. Agreements/tasks must make the provider and cost/risk explicit when economically
  material.
 3.8 One Reservation Authority
 Before task start, reserve exclusive Persons, equipment, workstations, animals and required inventory/materials. Shared
 asset scheduling from earlier revisions should be generalized so a wagon, reaper, transit, oven or horse cannot be
 double-booked across controlled businesses or household work.
 3.9 Condition, Failure & Replacement
 Requirement validation consumes Asset usable/condition state. A damaged plow can block PlowField or force a slower
  alternative method. Failure creates repair/replacement/rental/subcontract options through existing work-order and
 action-discovery systems, not a generic efficiency debuff.
 3.10 Occupation Profiles Are Data, Not Class Gates
 Implement the Canon occupation/equipment reference as data mapping typical Tasks/Capabilities to requirement sets. Do
 not encode FarmerRequiresAllFarmTools or BlacksmithClassRequired. A Person can perform a task if capability, authority
 and resource requirements resolve.
 3.11 Initial Equipment Requirement Registry
 The detailed Canon XXIII table is the content authority. The initial technical data registry should at minimum cover the
  following requirement families so every named occupation can be expressed without bespoke code.

   Requirement family               Representative classes / kits / workstations                           Used by

   Farm soil/planting                  Plow; Harrow; Cultivator; GrainDrill/Seeder; FarmHandTools; DraftTeam; Harness  Farmer, farmhand, planting crew

   Harvest/hay                          GrainCradle/SickleKit; Reaper/Binder; ScytheKit; Mower; HorseRake; HayForks;    Harvest hands, hay workers, threshing crews
                                     Thresher

   Livestock/stable                    Saddle/Tack; RopeKit; GroomingKit; Corral/Chute; Feed/Water; HoofCare        Ranch hands, livery, hostlers

   Freight/drayage                   Wagon/Cart; DraftTeam; Harness; Rope/Chain/TarpKit; Lantern; BasicRepairKit    Teamsters, draymen, delivery

    Smith/farrier                         ForgeStation; Anvil; SmithToolSet; FarrierKit; Fuel; Metal/Shoes/Nails              Blacksmith, farrier

   Wagon/leather                     WoodworkingKit; WheelJig; Jack/Lift; HarnessLeatherKit; Workbench              Wheelwright, wagon repair, saddler

   Construction carpentry              CarpenterHandToolKit; Ladder/Scaffold; BlockAndTackle; Workbench              Carpenters, framers, roofers

   Construction masonry/earth         MasonKit; MixingBox; Wheelbarrow; PickShovelKit; Scraper/Grader              Masons, laborers, graders



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 87 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            5




   Requirement family               Representative classes / kits / workstations                           Used by

   Survey/engineering                  SurveyTransit; Tripod; Chain/Tape; Level/Rod; Stakes; FieldNotebook             Surveyors, rail/project engineers

   Logging/lumber                         FellingKit; CrosscutSaw; Peavey/CantHook; LogWagon/Sled; SawmillSawLine;     Loggers, haulers, sawmill crews
                                               FilerKit

   Mining                           MiningHandTools; Drill/Sledge; Lighting; OreCart; Hoist; Pump; AssayBench        Miners, muckers, hoist/pump operators,
                                                                                                               assayers

    Retail/grain                         StoreCounter; Shelving/Bins; ScaleMeasureSet; Cash/RecordSet; PlatformScale;   Merchants, clerks, feed/grain workers
                                      BulkBins

   Food/bakery                    BakeOven; Dough/BakeToolSet; KitchenRange; ButcherKit; BarStation;            Bakers, cooks, butchers, bartenders, brewers
                                    BreweryVesselSet

   Hospitality/domestic                Bed/Bedding; HousekeepingKit; LaundrySet; Kitchen; Keys/Records                Hotel/boarding staff, household labor

   Medical/pharmacy                  DoctorBag/SurgicalSet; TreatmentSpace; VeterinaryKit; CompoundingBench;       Doctor, vet, assistant, pharmacist
                                      Balance/Mortar

    Barber/tailor                          BarberStation/Kit; TailorHandKit; Ironing/CuttingTable; SewingMachine optional     Barber, tailor/seamstress

   Print/communications              TypeCases/ComposingStick; PrintingPress; TelegraphDesk; Switchboard;            Printer, compositor, telegraph/telephone staff
                                      LineWorkerKit

    Rail operation/maintenance          RailSectionToolSet; Handcar; Locomotive; Fuel/Water; Signal/Lantern;             Section crews, engineer/fireman, conductor,
                                      DepotScale/Platform                                                                  depot/freight staff

    Utilities                          Dynamo/Generator; Boiler/Engine; Switchboard; LineKit; PumpStation;              Electric/water workers
                                     PipeWorkerKit

    Civic/fire                           Bucket/Ladder/HookKit; Hose/Engine when organized; Clerk/Records;                Firefighters, civic staff, teachers
                                   SchoolhouseSet

    Office/professional                DeskRecordSet; Safe/Vault access; Loan/Legal/Insurance files; Inspection         Managers, bankers, lawyers, insurance,
                                    notebook                                                                           auditors

    Investigation/law                  CaseNotebook/Maps; TravelAccess; AuthorityDocuments; Restraint/issued         Constable, detective/reward agent
                                   equipment when lawful
 PART IV - AGREEMENT, ECONOMIC-EVENT & PROJECT DEPTH
 4.1 Agreement -> Obligation Graph
 Agreement remains the relationship container. Add subordinate Obligation records/components with Obligor, Obligee,
 performance type, subject, quantity/standard, location, due window, dependency/condition, evidence/acceptance method,
 status and consequence references. Avoid one giant agreement status that hides partially completed obligations.
 4.2 Condition & Amendment
 Conditions precedent/subsequent and Amendments/ChangeOrders should be persistent, versioned and append historical
 terms rather than overwrite originals. An amendment records authority/consent, changed scope/price/time/risk and
  effective date.
 4.3 Notice / Claim / Cure Hooks
 Provide reusable Notice/Claim links so breach/cure/dispute systems can reference the underlying obligation and evidence.
 Domain legal procedures remain specialized data over the same claim lifecycle.
 4.4 EconomicEvent / Transaction Grammar
 A transaction/economic event should distinguish order/commitment, fulfillment, acceptance, recognition, settlement and
  later adjustment. Suggested references include SourceAgreement/Obligation, physical lot/asset/service evidence, principal
  parties, acting Persons, recognition classification, cash/receivable/payable effects, asset/inventory mutation and
 reversal/adjustment linkage.
 4.5 Reversal Instead of Destructive Rewrite
 Refund, return, write-off, bad debt, shrink discovery, duplicate entry correction or post-close adjustment should create
 linked correcting events. Historical records remain auditable and idempotent through save/load.
 4.6 Project Baseline / Forecast / Actual
 Project state should retain approved baseline budget/schedule/scope separately from current forecast and actual realized
 values. Do not mutate baseline when an estimate changes. Reports can explain variance by causal categories.
 4.7 WorkPackage





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 88 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            6



 Use WorkPackage between Project and Task where useful for meaningful scope/dependencies, material lots,
 subcontractor responsibility, equipment burden, acceptance conditions and cost attribution. Do not create packages for
 animation-level hand motions.
 4.8 Progress Billing & Acceptance
 Payment events should attach to actual contract terms: deposit, delivered materials, milestone/progress, completion/final,
 withholding/retention only where historically/contractually appropriate. Project physical completion, customer acceptance
 and final financial settlement remain separate states.
 PART V - NPC PLANNING, INFORMATION & CUSTOMER CHOICE
 5.1 Persistent Plan / Intent State
 Add lightweight PlanState where an NPC economic action spans investigation and commitment over time. Conceptual
 stages: Detected -> Interested -> Investigating -> Feasibility/Quotes -> Financing/Negotiation -> Committed -> Executing
 -> Operating/Reassessing -> Completed/Abandoned. Plans reference real resource reservations/agreements only after
 commitment.
 5.2 Capital Allocation Pass
 NPC business decisions should compare constrained uses of cash/capacity through the same obligations, reserves,
 maintenance, staffing, inventory and project state as the player. A plan may be rejected because the same funds are
 needed elsewhere.
 5.3 Customer Choice Resolver
 Build feasible-choice sets from Known/Reachable/Open/InStock/ServiceAvailable/Affordable states. Then apply
 purchase-type-specific decision policy using price, distance, wait, quality, trust, credit, urgency and prior experience. Do
 not run a universal CustomerAttractiveness score that writes transactions directly.
 5.4 Lost-Sale / Abandoned-Intent Events
 When an intent fails because of closure, stockout, queue, affordability, distance or rejection, write an event/evidence item
  that can feed O&P and merchant diagnostics. Do not automatically route the sale to a competitor without resolving that
 competitor as another feasible choice.
 5.5 Evidence / Reputation
 Use shared Evidence records or compatible domain histories with Subject, Source, Observer, Timestamp, Type,
 Confidence, Staleness and Access. Domain reputation read models select relevant evidence rather than update a single
 global score.
 PART VI - GOVERNANCE, SUCCESSION, INTERNAL CONTROLS &
 INSTITUTIONS
 6.1 GovernanceAction / Decision Case
 Represent consequential corporate/partnership decisions as proposals/cases that resolve authority classification,
  notice/information, required approvals, votes/consents, amendments, final authorization and execution. Integrate existing
 CapTable/ControlResolution rather than adding a second ownership/control model.
 6.2 Capital Call / Financing Proposal
 A capital need can create a governance financing proposal referencing required amount, permitted funding types,
  participation rights, dilution/covenant consequences and responses from actual investors/lenders. Participation is not
 automatic.
 6.3 SuccessionContinuityCase
 When a key owner/manager/authorized Person dies, becomes incapacitated, retires or exits, create a persistent continuity
 case when material. Fields can include affected entity/rights, missing authority/capabilities, interim authorized actors,
 heirs/partners/known claimants, surviving agreements/obligations, lender/supplier reactions, resolution candidates and
  final transfer/management state. Exact estate/legal procedure remains in dated LegalRegime data.
 6.4 InternalControlAssignment





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 89 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            7



 Model economically meaningful control responsibilities: Custody, Authorization, Recording, Receiving and
  Verification/Reconciliation. Very small firms may assign several to one Person. Larger firms can assign separate
 Persons/policies. Discrepancy events reference actual stock/cash/records and investigation evidence rather than a
 random TheftDetected flag.
 6.5 CommercialClaim / Dispute Case
 Use a shared claim lifecycle linking claimant/respondent, underlying Agreement/Obligation/PropertyRight/Transaction,
 demanded remedy, notice, response, negotiation/settlement, proceeding, decision/judgment, enforcement and costs.
 LegalRegime determines which procedures/remedies exist by date/jurisdiction.
 6.6 InsurancePolicy / Claim
 Add persistent policy data only after historical/product research: insured interest, risk/property scope, term, premium, limit,
 retained loss/deductible concept where applicable, exclusions/conditions, insurer and lender/other interest where relevant.
 Claim links the physical loss event to notice, investigation, coverage decision, valuation and payment/dispute. Loss never
 disappears because a policy exists.
 6.7 DistressCase
 Aggregate serious multi-obligation financial trouble into a case/read workflow without replacing underlying obligations.
 Inputs include cash timing, realizable assets, earning capacity, secured/unsecured obligations, arrears/defaults and
  creditor actions. Proposed remedies execute through real asset sales, refinancing, agreements, staffing changes, capital
 contributions or liquidation.
 6.8 CivicProject / Public Counterparty
 Represent civic/public projects using ordinary Project, Agreement, property/right and finance authorities with an
  institutional sponsor/counterparty. Proposal, authority/franchise, financing, procurement, construction, service opening and
 maintenance remain explicit. Do not create TownUpgradeComplete as causal truth.
 6.9 UtilityServiceAgreement
 Physical network connection and service capacity remain infrastructure truth. Add service relationship data for
 customer/account, premises connection, tariff/contract, billing cadence, arrears, disconnection/reconnection, load/capacity
 commitment and extension contribution where relevant. Network expansion projects use expected/committed load
 evidence.
 6.10 Labor-Market Workflow
 Vacancy/Search/Offer should use persistent Person and Employment authorities. Candidate awareness,
 interview/reference evidence, offer terms, counteroffer, acceptance/refusal, start date, probation/learning where used and
 separation all remain event history. Poaching closes/changes one Employment relationship before or while another begins;
   it does not clone the worker.
 PART VII - SAVE/LOAD, PERFORMANCE & DERIVED READ MODELS
 7.1 Persistence Additions
 Where implemented, persist ActivityInstances that cross save boundaries, procurement needs/intents that materially affect
 outcomes, significant equipment/toolkit assets and reservations, obligation/amendment history, project baseline/forecast
 snapshots needed for audit, governance/succession/distress/claim cases and customer-actor provenance on transactions.
 7.2 Derived Histories May Be Bounded
 Not every routine meal or sleep event needs permanent decade-long detailed retention. Preserve authoritative state and
 the history required for causal consequences, audit, scenarios and debugging; compact/archive old low-value activity
 events after they have safely updated retained summaries.
 7.3 No Anonymous Aggregate Settlement
 Aggregate household/customer simulation may batch decisions for performance, but settlement must still identify the
 actual affected Person/Household/Business records before mutating local cash, inventory, health, accommodation or
 transaction history. Aggregates may forecast; they may not bypass entity truth.
 7.4 O&P Feeds From Events





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 90 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            8



 Opportunity & Pressure consumes transaction, stockout, failed-intent, vacancy, housing mismatch, closure, route delay,
  project need and similar events. It remains analytical and cannot execute customer sales, migrations or contracts directly.
 PART VIII - REVISION VI ACCEPTANCE & REGRESSION FIXTURES
 8.1 UNEMPLOYED SHOPPER
 Create a Person who loses employment but retains household cash. Give the household a durable need during an
 unscheduled afternoon. Confirm the Person can travel, purchase from real stock and settle the transaction. Unemployment
 must not block shopping.
 8.2 WORK/SHOP TIME COLLISION
 Schedule the same Person at work when a household need matures. Confirm they cannot simultaneously shop. Another
 household member, later trip or legitimate delivery may satisfy the need.
 8.3 STORE OPEN, ZERO SHOPPERS
 Keep a store open while no reachable Person has a qualifying intent/means/time. Confirm zero sales and no minimum
  traffic spawn.
 8.4 CLOSED STORE / LOST INTENT
 Have a Person arrive after closing. Confirm no transaction, then resolve postponement or rival choice through the normal
 choice system and write lost-intent evidence.
 8.5 FAMILY MEAL
 Five-person household prepares one supper task from real food stock. Confirm one preparation consumes labor/inputs
 and the five actual Persons receive meal effects without five duplicate cooking jobs.
 8.6 BOARDING MEAL
 Worker with board including meals receives a meal through the boarding agreement and boarding-house inventory/labor.
 Confirm no second retail meal sale is created.
 8.7 SALOON EVENING
 Two coworkers finish work, travel, buy drinks from real inventory and later sleep. Confirm money, inventory, time, Person
  location and recovery state reconcile.
 8.8 TRAVELER CONTINUITY
 Activate/promote an off-map traveler who uses livery, hotel, meal and store services then departs. Confirm one PersonId
  links every transaction/activity.
 8.9 COARSE PERSON PURCHASE
 Resolve a legitimate purchase while the Person is coarse-simulated. Reactivate region and confirm same Person location,
 cash, possession and history.
 8.10 SMALL FARM EQUIPMENT
 Create a small farm with hand tools, one team, one plow, one harrow and wagon. Confirm modest acreage can be
 completed. Increase acreage until labor/time becomes binding; add reaper/binder and observe a real capacity change.
 8.11 HIRED THRESHING SERVICE
 Farm lacks a thresher. Hire a traveling threshing provider. Confirm provider Persons/equipment travel, reserve the
 machine, perform Tasks and receive payment; no thresher is silently granted to the farm.
 8.12 BROKEN PLOW
 Break the farm's only usable plow. Confirm PlowField cannot use that method until
 repair/replacement/rental/borrowing/service resolves access.
 8.13 EXCLUSIVE TRANSIT
 Assign one survey transit to two projects with overlapping time. Confirm shared reservation prevents double use and one
  project waits or sources another instrument.




                                               LAND & LEDGERS

## TECH-XIV — original PDF page 91 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                            9



 8.14 PARTIAL LUMBER CONTRACT
 Commit 4,000 board feet; deliver only part; reject some; accept some; invoice according to terms. Confirm outstanding,
 accepted, rejected, payable and paid amounts remain distinct.
 8.15 PROJECT VARIANCE
 Award a project from a baseline estimate, then force a site/material change. Confirm baseline remains unchanged,
 forecast changes and actual records final result.
 8.16 GOVERNANCE CAPITAL CALL
 Company needs capital. One shareholder participates, one declines and one negotiates. Confirm cash, dilution/rights and
  authority follow actual approved structure rather than automatic pro-rata refill.
 8.17 OWNER DEATH / CONTINUITY
  Kill/incapacitate a sole proprietor with an experienced manager. Confirm business can continue operationally while
 succession/ownership is unresolved; obligations and Person histories persist.
 8.18 INTERNAL CONTROL DISCREPANCY
 Separate receiving, cash custody, recording and reconciliation. Introduce a discrepancy whose cause is initially unknown.
 Confirm investigation can resolve error/shrink/theft/short delivery or remain unresolved without omniscient flag.
 8.19 INSURED LOSS
 Destroy a covered asset. Confirm asset remains destroyed, debt/security remains according to agreement, claim is
 investigated and payment occurs only after coverage/valuation resolution.
 8.20 DISTRESS WORKOUT
 Create illiquid but viable enterprise with multiple creditors. Confirm negotiated extension/asset sale/refinance can recover it
 without triggering Commercial Ruin solely from one negative cash moment.
 PART IX - IMPLEMENTATION PRIORITY
 9.1 P0 - Identity, Time & Transaction Integrity
    • Repository audit of Person identity/location/scheduling and current customer-sale path.
    • Guarantee one Person/location authority across active/coarse simulation.
    • Transaction actor provenance for local retail/service.
    • Shared exclusive Person/activity reservation.
    • Preserve existing shipment/inventory P0 integrity fixes from Revision V.
 9.2 P1 - Equipment & Household-Customer Causality
    • EquipmentRequirementSet + alternative methods.
    • ToolKit/workstation/motive-power integration with existing Assets and Tasks.
    • Household ProcurementNeed / acting-Person selection.
    • Lost-intent/stockout/closure evidence feeding O&P.
    • Core fixtures: unemployed shopper, no phantom sales, farm equipment, hired threshing.
 9.3 P2 - Agreement / Transaction / Project Depth
    • Obligations/conditions/amendments/notice links.
    • Economic-event recognition/settlement/reversal grammar.
    • Project baseline/forecast/actual and WorkPackage/change-order support.
    • Customer choice/repeat relationship evidence.
 9.4 P3 - Institutional Continuity
    • Governance proposal/capital-call workflows.



                                               LAND & LEDGERS

## TECH-XIV — original PDF page 92 of 170

LAND & LEDGERS / REVISION VI / TECHNICAL IMPLEMENTATION BIBLE                                                           10



    • SuccessionContinuityCase and internal-control assignments.
    • Commercial claims/legal procedure integration.
    • Insurance, distress/workout, civic/public procurement, utility service economics and labor-market workflows.
 PART X - RESEARCH / CALIBRATION HOLDS & SUPERSESSION
 MAP
 10.1 Historical/Data Holds
 Exact equipment prices/counts, regional adoption dates, tool wear, output/time effects, insurance products/premiums,
  utility tariffs, court procedure, estate/probate detail, employment customs, boarding/meal quantities and
 alcohol-consumption tuning remain researched/calibrated data. Revision VI defines architecture and invariants; it does not
 invent unsupported exact values.
 10.2 Supersession / Deepening Map
    • Revision V Person/population integrity is deepened into continuous Person activity, customer provenance and life
      continuity.
    • Revision II Task equipment fields are deepened into alternative EquipmentRequirementSets, ToolKits, workstation and
     motive-power architecture.
    • Revision II shared-asset scheduling is generalized to one exclusive reservation model across Persons, equipment,
     animals and workstations.
    • Revision I Agreement direction is deepened with Obligations, Conditions, Amendments and claim/cure hooks.
    • Revision XVII Canon transaction grammar is translated into explicit economic-event recognition, settlement and
     reversal architecture.
    • Revision II/III Project architecture is deepened with Baseline/Forecast/Actual, WorkPackages, change orders and
     acceptance/payment separation.
    • Revision III/IV governance/control architecture is deepened with proposal/approval and capital-call execution.
    • Household/customer market simulation is clarified: derived demand and O&P never directly post local sales.
    •  All prior condition, maintenance, logistics, housing, provenance, finance and scenario authorities remain active unless
      explicitly superseded above.
 END OF REVISION VI INTEGRATION
 The preserved Technical Implementation Bible Revision V follows unchanged and remains authoritative except where
 Revision VI explicitly adds, deepens, corrects or supersedes technical guidance.





                                               LAND & LEDGERS

## TECH-XIV — original PDF page 93 of 170

LAND & LEDGERS / REVISION V / TECHNICAL IMPLEMENTATION BIBLE                                                                  1

LAND & LEDGERS
 TECHNICAL IMPLEMENTATION BIBLE - REVISION V
 The Contractor Integration: Population Reconciliation, Physical/Operating State, Opportunity Provenance, Contractor
 Qualification, Delegated Capital & Long-Run QA
  Revision date: September 18, 2026
 Source baseline: Technical Implementation Bible Revision IV - September 15, 2026
 Canon compatibility target: Canonical Project Bible Revision XXII
  Status: Active technical planning reference - repository audit remains required before implementation claims
 PRESERVATION GUARANTEE
 Revision V preserves Technical Implementation Bible Revision IV in full except where this integration explicitly adds, deepens, corrects or
 supersedes technical guidance. The complete Revision IV document follows this integration unchanged as the preserved baseline.
 Repository evidence remains authoritative for what is implemented. No conceptual type, field, enum, service or workflow below is proof that
   it already exists in Unity.
 PURPOSE
 Revision V translates THE CONTRACTOR playtest integration into target architecture. Revision IV already establishes objective
  predicates/evidence, task response, capability evidence, granular delegation, exact money, financial exposure, provenance and save/load
  fixtures. Revision V concentrates on newly exposed cross-system integrity needs: population/accommodation reconciliation,
  project/building/business state separation, independently progressing external infrastructure, opportunity provenance, contractor
  qualification, historical active-work evidence, recurring delegated capital mandates, configurable financing authority, settlement
  durability/migration incentives and stronger long-run QA reconciliation.
 REVISION V ARCHITECTURE LOCKS
  •   Settlement resident counts derive from authoritative Person and residence/accommodation state; no independent population counter
   may contradict physical truth.
  •   Project completion, Building state, occupancy/business use, Business opening and financial performance remain separate
     authorities/read models.
  •   External infrastructure fronts persist independently from scenario objective state and continue through ordinary Project/corridor/world
    systems.
  •   Material opportunities carry causal source/provenance and do not exist solely because an objective requires them.
  •   Contractor qualification consumes evidence from authoritative work history, relationships, capability/capacity and commitments rather
    than a ContractorLevel.
  •   Overlap and maintained-crew objectives use persisted actual work intervals/assignments, not contract signatures or UI crew counts.
  •   Recurring capital mandates use ordinary DelegatedAuthority, Manager Activity, Project, Agreement and Transaction systems.
  •   Financing authority is configurable by scope; Full Autonomy does not silently override owner/governance locks.
  •   Settlement durability and migration-incentive UI are derived read models over real state, not parallel simulation authorities.
  •   Long-run QA must export enough authoritative state to reconcile population, buildings, business operation, work, assets, manager
    decisions and objective evidence.
 PART I - POPULATION & ACCOMMODATION RECONCILIATION
 1.1 Resident Population Read Model
 Settlement population should be derived from stable Person identity plus authoritative current residence/occupancy relationships. Reuse
  the existing Person, Household, ResidentialOccupancy, AccommodationSpace, Building and SettlementNode authorities rather than adding
 a competing SettlementPopulation truth.
 A conceptual resolution chain is:

    Person -> current Residence/Occupancy -> AccommodationSpace or authorized temporary accommodation ->
    Building/Site -> SettlementNode
  Useful derived outputs include PermanentResidents, TransientOvernightResidents, TemporaryCampResidents,
 DisplacedOrUnhousedPersons, TotalOvernightPopulation and, where useful, DaytimePresentPopulation. These are read models, not
 independent counters.





                                                       LAND & LEDGERS

## TECH-XIV — original PDF page 94 of 170

LAND & LEDGERS / REVISION V / TECHNICAL IMPLEMENTATION BIBLE                                                                  2


 1.2 Reconciliation Invariant
 TECH LOCK - A Person counted as physically resident must resolve to a legitimate current residence/accommodation state or an
  explicit displaced/unhoused state. A summary field cannot silently create residents. Duplicate occupancy must obey the actual
  capacity/relationship rules of the accommodation model.
 1.3 Migration/Residence Transition
  Migration arrival should not increment settlement population before the household/Person actually reaches the destination and establishes
 a valid current residence. Departures, evictions, fire damage, demolition or other events that invalidate accommodation should update the
 residence relationship and derived settlement counts without deleting the Person.
 PART II - PROJECT, BUILDING & BUSINESS OPERATING-STATE BOUNDARIES
 2.1 Separate State Authorities
 Do not overload one generic Complete/Open/Active flag. Preserve distinct authoritative transitions for construction Project completion,
  physical Building completion/condition/usability, FunctionalSpace availability, occupancy/business-use assignment, Business opening
  preparation, Business open/operating state and financial performance.
 Conceptual distinctions may include ProjectStatus, BuildingOperationalState, SpaceOccupancyState and BusinessOperatingState, but
 exact implementation names follow repository audit.
 2.2 Objective Qualification
 A permanent-building objective should query the Project/Building authority that proves physical completion. A business-opening objective
 should query Business operating state. A profitability objective should query authoritative financial/economic evidence. ScenarioState stores
  qualifying evidence/references, not duplicate building or business truth.
 2.3 State-Transition Ordering
  Construction completion may make a building usable before an occupant exists. Occupancy may begin before public opening. Opening may
 occur before sustained profitability. Save/load and scene manifestation must preserve each transition independently.
 PART III - EXTERNAL INFRASTRUCTURE & OPPORTUNITY PROVENANCE
 3.1 Persistent External Project/Work Front
 Reuse/extend RailwayCorridor, SegmentProject, generic Project and world-scheduled work architecture so a scenario-authored external
  railway or infrastructure project can maintain route/alignment, current work fronts, stage progress, delays, facilities, contractors, service
 openings and geographic position independent of scenario objective state.
 TECH LOCK - Scenario objective events may observe or reference infrastructure progress; they must not be the authority that
  teleports or fabricates the infrastructure front.
 3.2 Opportunity Provenance
 A material Opportunity/ActionCandidate should be traceable to an authoritative source condition or counterparty. Where useful, a
 provenance record/read model can expose source entity/counterparty, economic cause, location, scope, information channel, availability
 window, qualification requirements, expiry/status and resulting Agreement/Project if accepted.
  Action discovery remains a read model. It should not create a qualifying customer project independently of the economy merely because
  the player needs one.
 3.3 Contractor Qualification Read Model
 When a counterparty is selecting or considering a contractor, derive qualification evidence from relevant completed work, accepted
  quality/history, references/relationships, known specialist Persons, equipment/assets, liquidity/financeability, geography, current
 commitments/backlog and other authored requirements. Hidden true capability may remain information-limited to the counterparty. Avoid
  ContractorLevel gates unless a scenario intentionally models a real certification/license equivalent.
 3.4 Opportunity Failure
 An opportunity can expire, be awarded to a rival, fail financing, be withdrawn, change scope or become infeasible. A scenario evaluator
 must tolerate ordinary opportunity loss unless the authored scenario explicitly guarantees the availability of a required opportunity.
 PART IV - HISTORICAL ACTIVE-WORK & CREW EVIDENCE
 4.1 Active Work Intervals
 Agreements/Projects should retain enough authoritative execution history to distinguish Signed, Scheduled, ActiveWork, Suspended,
 Completed and Accepted states. A scenario requiring overlapping active work computes against actual qualifying work intervals, not
  signature dates or planned duration.
 4.2 Crew/Worksite Evidence
 Maintained multi-crew objectives should derive qualifying groups from real Employment/Task/Project assignments, location/worksite and
  the authored crew definition. For THE CONTRACTOR reference fixture, store enough history to prove at least three qualifying employees,
 one designated working lead and independent worksite progress across the required interval. This is scenario evidence, not a universal
  crew-size rule.


                                                       LAND & LEDGERS

## TECH-XIV — original PDF page 95 of 170

LAND & LEDGERS / REVISION V / TECHNICAL IMPLEMENTATION BIBLE                                                                  3


 4.3 Historical Evidence Persistence
  Objective evidence should reference qualifying entity IDs and actual interval/event records. Save/load must preserve maintained-duration
 progress and final evidence idempotently. Do not reconstruct multi-year overlap/crew objectives from narrative summaries at campaign end.
 PART V - DELEGATED CAPITAL MANDATES & FINANCING AUTHORITY
 5.1 Recurring Capital Policy
 Extend DelegatedAuthority/policy data so a responsible Person can receive a recurring strategic mandate such as reinvest distributable
  rental cash in qualifying housing, preserve protected liquidity, replace failed assets or stage expansion inside a defined region/business
 scope.
  Useful policy dimensions may include scope, objective/priority, protected-liquidity source/read model, eligible project/property classes,
 geographic constraints, financing permissions, escalation triggers, recurrence and termination/review conditions.
 5.2 Execution Still Uses Real Systems
 The mandate can create ManagerDecisionEvents, ActionCandidates, Projects, Agreements, financing requests and Transactions through
  their owning authorities. It must not write a direct HousingCount increment or transfer imaginary cash.
 5.3 Financing Authority Scope
 Deepen DelegatedAuthority beyond a single spending threshold. Configurable finance scope may distinguish supplier credit,
  working-capital draw, secured construction borrowing, mortgages, seller finance, refinancing, maximum exposure, permitted collateral,
  eligible entities/geography and required escalation/approval. Exact fields follow finance/governance audit.
 TECH LOCK - Full Autonomy is not equivalent to CanBorrowAnything. Major financing proceeds automatically only when the
  responsible actor actually holds the required delegated/governance authority and the lender/legal systems approve it.
 PART VI - SETTLEMENT DURABILITY & MIGRATION INCENTIVES
 6.1 Durability/Dependency Read Models
 Do not add one authoritative SelfSufficiency float. Create derived diagnostics over authoritative settlement state as useful: employer
  concentration, permanent/transient demand mix, housing availability by household type, vacancy, essential-service availability, transport
 dependency, import dependency, local production diversity, major infrastructure dependency and temporary-boom exposure.
 6.2 Migration Incentive Terms
 Represent free rent, discounted rent, relocation assistance, employer-paid travel, discounted lots/leases or similar incentives through
  ordinary Agreement/Property/Employment/Transaction/Information relationships. Household migration evaluation consumes the changed
 expected cost, feasibility and information; population changes only after actual movement/residence transition.
 6.3 Incentive Expiry and Retention
 When a scenario tests post-subsidy durability, expiry of the incentive changes the household/business economics through the same
 agreements and cash flows. Maintained-state objectives may then query resident/economic conditions across the authored retention
  interval.
 PART VII - SCENARIO OUTCOME, CONTINUATION & PRESENTATION CLARIFICATIONS
 7.1 Outcome Is Historical State
 Continue Revision IV objective evidence and Revision III completion presentation. When all required predicates complete, persist
 Outcome=Completed and the qualifying completion timestamp/evidence before or independently of any cinematic. Continued simulation
 does not rewrite the original completion timestamp.
 7.2 Failure and Continued World
 Scenario failure remains separate from Commercial Ruin. The scenario state can record Failed while ordinary world simulation continues.
 UX may expose Continue Failed World, Restart Scenario and Load Save where the product workflow supports them; these choices do not
  create a second economic state.
 7.3 Compact Objective Visibility
 Scenario UX should provide readily accessible objective status, deadline and qualifying progress using the same predicate authority
 described in Revision IV. Do not create a second checklist state that can drift from scenario evaluation.
 PART VIII - THE CONTRACTOR END-TO-END ACCEPTANCE FIXTURE
 8.1 Fixture Purpose
 THE CONTRACTOR is a deterministic reduced-presentation acceptance fixture for cross-system state integrity. It should test the
 mechanics exposed by the playtest without reproducing the campaign's exact generated prices or timeline.
1   Create a low-capital contractor and complete one legitimate customer job personally.
2    Hire Persons and complete later work using hired labor; confirm role/capability and worker response remain ordinary systems.
3   Create at least two independently progressing worksites with exclusive worker/equipment assignment.


                                                       LAND & LEDGERS

## TECH-XIV — original PDF page 96 of 170

LAND & LEDGERS / REVISION V / TECHNICAL IMPLEMENTATION BIBLE                                                                  4


4   Generate a railway/infrastructure opportunity from real counterparty/project provenance and contractor qualification evidence.
5   Execute equipment-dependent grading/structure work with actual animals/vehicles/tools and causal downtime/repair where forced.
6   Maintain two contracts with overlapping actual ActiveWork intervals and verify the objective from historical execution evidence.
7   Delegate routine railway/construction operations to a qualified manager; confirm Manager Activity and no hidden owner clicks.
8   Create a developing settlement node around persistent infrastructure/employment; add legitimate lodging/housing.
9   Migrate Persons into the settlement and reconcile every counted resident to an accommodation/residence state.
10  Grant a property manager a recurring rental-development mandate; confirm real financing, Projects and Transactions execute.
11  Complete a permanent building but leave its occupant business unopened; confirm a building objective qualifies while a business-open
     objective does not.
12  Complete the scenario, continue simulation afterward and verify the original completion timestamp/evidence does not change.
13  Save/load at multiple checkpoints and verify no duplicate cash, inventory, Persons, residents, buildings, contracts, objective evidence or
    Manager Activity events.
 PART IX - ADDITIONAL REGRESSION TESTS
  •   Population overcount: create more Persons than valid accommodation assignments; confirm resident read model counts only
     legitimate residence states and exposes displaced/unhoused Persons separately.
  •   Destroyed accommodation: invalidate a building through fire/demolition; confirm households persist while occupancy/resident state
    updates.
  •   Building vs business: complete physical building, delay stocking/staffing/opening and confirm objective/read models remain distinct.
  •   Opportunity provenance: remove the source counterparty/project need and confirm the qualifying contract cannot exist solely from
    scenario demand.
  •   Active overlap: sign two contracts on the same date but start work four days apart; confirm overlap uses actual ActiveWork intervals.
  •  Crew continuity: move a required worker/lead away mid-interval and confirm maintained-state evidence reflects the break when the
    authored definition requires it.
  •   Delegated borrowing: test identical financing action with and without explicit manager authority; lender approval alone must not
    override governance/owner locks.
  •   Capital mandate liquidity: reinvest aggressively while payroll/debt/project obligations exist; confirm deployable cash comes from
     liquidity policy/read models rather than the raw cash balance.
  •   Migration subsidy: offer free rent and confirm it changes household evaluation/transactions without directly mutating population.
  •   Collateral destruction: destroy a secured asset and confirm the obligation persists until an authoritative
    repayment/insurance/settlement/legal event resolves it.
 PART X - LONG-RUN QA / RECONCILIATION OUTPUT BUNDLE
 10.1 Expanded Autonomous Test Export
 Deepen Revision III 9.2 so long-run Contractor/settlement tests can export and cross-check:
  •   Scenario objective state, completion/failure evidence and deadlines;
  •   Lifetime Ledger and major Transaction/Agreement history;
  •   exact Person counts, residence/occupancy links and settlement population decomposition;
  •   Buildings/FunctionalSpaces and their physical/condition/occupancy state;
  •   Businesses and operating/opening state separate from physical premises;
  •   Projects, contracts, actual active-work intervals and acceptance/completion events;
  •   crew/worksite assignments and working-lead evidence for maintained objectives;
  •   animal/vehicle/equipment ownership, exclusive assignments, defects, restrictions and downtime;
  •   Manager Activity, delegated capital decisions, financing actions and escalations;
  •   migration/residence changes, vacancy and housing fit;
  •   save/load checkpoints and any invariant/reconciliation failures.





                                                       LAND & LEDGERS

## TECH-XIV — original PDF page 97 of 170

LAND & LEDGERS / REVISION V / TECHNICAL IMPLEMENTATION BIBLE                                                                  5


 10.2 Reconciliation Failures Are First-Class Test Output
 The harness should surface impossible or contradictory states directly rather than silently repairing them for the report. Examples include
  resident count without residence, duplicate exclusive asset assignment, completed-building objective without qualifying physical completion,
  or objective evidence referencing missing entities.
 PART XI - IMPLEMENTATION PRIORITY
 11.1 P0 - State Integrity
  •   Population/residence/accommodation reconciliation and debug invariants.
  •   Project/Building/Business state separation where current repository authorities are conflated.
  •   Historical active-work / maintained-state evidence required by scenario predicates.
  •   Idempotent save/load of the new evidence/read-model inputs.
 11.2 P1 - Opportunity, Delegation & Settlement Intelligence
  •   Opportunity provenance and contractor qualification read models.
  •   Persistent external infrastructure front integration over existing corridor/project systems.
  •   Recurring delegated capital mandates and configurable finance authority.
  •   Settlement durability/dependency read models and migration-incentive integration.
 11.3 P2/P3 - Scenario UX and Extended Fixtures
  •   Continue/Restart/Load presentation for failed scenarios where supported.
  •   Compact objective-progress surfaces over existing predicate authority.
  •  THE CONTRACTOR long-run acceptance fixture and extended reconciliation export.
 PART XII - RESEARCH / CALIBRATION HOLDS & DEEPENING MAP
 12.1 Holds
  •   Exact contractor qualification thresholds, reference weighting and historical bonding/security requirements by place/date/project type.
  •   Exact representation and performance strategy for large accommodation histories and daytime-presence calculations.
  •   Exact settlement durability summary presentation and thresholds, if any; no universal Self-Sufficiency score is authorized.
  •   Exact migration incentive response strength, subsidy duration and retention-window tuning.
  •   Exact railway/infrastructure scenario-front pacing and opportunity frequency.
  •   Exact timber/resource productivity, crew/equipment ratios and Contractor playtest prices remain data/calibration rather than architecture.
 12.2 Revision V Deepening Map
  •   Revision IV objective authority is deepened with active-work/crew historical evidence and physical-building versus business-state
     qualification.
  •   Revision III world/scenario architecture is deepened with independently progressing external infrastructure fronts that objectives observe
     rather than control.
  •   Revision II Action Discovery and Opportunity/Pressure direction is deepened with explicit opportunity provenance and contractor
     qualification evidence.
  •   Revision IV delegation is deepened with recurring capital mandates and configurable financing authority scope.
  •   Existing Person/Household/Occupancy architecture is deepened with a hard settlement-population reconciliation invariant.
  •   Revision III long-run QA is deepened by THE CONTRACTOR fixture and expanded authoritative reconciliation exports.
 END OF REVISION V INTEGRATION
 The preserved Technical Implementation Bible Revision IV follows unchanged and remains authoritative except where Revision V explicitly
 adds, deepens, corrects or supersedes technical guidance.





                                                       LAND & LEDGERS

## TECH-XIV — original PDF page 98 of 170

LAND & LEDGERS / REVISION IV / TECHNICAL IMPLEMENTATION BIBLE                                                  1
LAND & LEDGERS
 TECHNICAL IMPLEMENTATION BIBLE - REVISION IV
 Saloon Circuit Integration: Objective Predicates, Provenance, Task Response, Delegation, Contingent Exposure,
 Property & Saloon Architecture
 Revision date: September 15, 2026
 Source baseline: Technical Implementation Bible Revision III - September 8, 2026
 Canon compatibility target: Canonical Project Bible Revision XXI
 Status: Active technical planning reference - repository audit remains required before implementation claims
 PRESERVATION GUARANTEE
 Revision IV preserves Technical Implementation Bible Revision III in full except where this integration explicitly adds, deepens,
 corrects or supersedes technical guidance. The complete Revision III document follows this integration unchanged as the preserved
 baseline. Repository evidence remains authoritative for what is implemented. No conceptual type, field, enum or service below is
 proof that it already exists in Unity.
 PURPOSE
 Revision IV translates the Saloon Circuit Master Playtest Integration into target implementation architecture. Revision III already
 establishes persistent ScenarioDefinition/ScenarioState, compound objective evaluation, stable world identity across resolution,
 ControlResolution, relationship transferability, segment reporting, Lifetime Ledger persistence and autonomous scenario QA. Revision
 IV concentrates on the newly exposed gaps: objective completion semantics, product provenance, exact ownership/debt predicates,
 employee task response, capability evidence, granular delegation, functional-space access, gambling concessions, customer
 awareness, buyer financing/guaranties and save/load acceptance fixtures.
 REVISION IV ARCHITECTURE LOCKS
  Scenario objective truth must be composed from authoritative systems. UI labels, scene objects and convenience flags cannot
     qualify objectives by themselves.
  Event objectives require durable qualifying evidence. Current-state objectives must re-evaluate against present authoritative state.
  Product provenance must be sufficient to prove internal-supply objectives without duplicating inventory or manufacturing history.
  Task assignment and task execution are separate. Persons may delay, refuse, abandon or complete with error.
  Delegation stores real actor authority, limits and escalation. Full Autonomy is configuration over the same authority layer.
   Principal debt and contingent exposure must be distinct read-model dimensions.
  Gambling, concessions and independent room-use relationships should reuse shared authorities rather than create parallel
     saloon-only truth.
  Save/load must preserve objective evidence, provenance, obligations, relationships and cumulative metrics idempotently.
 PART I - SCENARIO OBJECTIVE AUTHORITY
 1.1 Predicate Composition
 Deepen Revision III Compound Objective Evaluation with reusable predicate families. Exact class names follow repository audit;
 conceptually useful predicates include HistoricalEventPredicate, CurrentOwnershipPredicate, DebtPredicate, GeographicPredicate,
 MetricThresholdPredicate, RankPredicate, MaintainedStatePredicate and DatePredicate.
 Scenario definitions should compose predicates declaratively where practical rather than requiring a bespoke C# class for every
 authored goal.
 1.2 Predicate Semantics
   HistoricalEventPredicate evaluates qualifying event evidence and becomes complete according to authored one-time semantics.
   Current-state predicates query present authoritative state and may become false after previously being true.
   Maintained-state predicates require authoritative evidence that a condition remained true across the authored interval.
 TECH LOCK - Completion semantics belong to the objective definition/evaluator. UI checkmarks never become scenario authority.
 1.3 Objective Completion Evidence
 Persist enough evidence for later audit without cloning full economic state into ScenarioState. A completion record can conceptually
 include ObjectiveId, CompletionTimestamp, QualifyingEventIds, RelevantEntityIds, ScopeId and definition/version information.
 1.4 Attached Criteria
 A headline objective and its tooltip/detail criteria should consume the same predicate set. Presentation can collapse detail, but there
  is no second hidden objective authority.
 1.5 Countdown Read Model
 Derive remaining time from authoritative simulation time and authored deadline. Formatting may select months/days/hours and
 completed-early text. Never maintain an independent countdown clock that can drift from world time.
 PART II - OWNERSHIP, DEBT & QUALIFICATION READ MODELS
 2.1 Ownership Percentage Resolver
 Scenario ownership predicates must resolve the authoritative economic ownership percentage or required ownership interest. Do not
  infer from PlayerOwned flags, prefab ownership markers, management authority, business name, scenario registration or
 ControlResolution. Control and ownership remain distinct.



                                                        LAND & LEDGERS

## TECH-XIV — original PDF page 99 of 170

LAND & LEDGERS / REVISION IV / TECHNICAL IMPLEMENTATION BIBLE                                                  2
 2.2 Debt Scope
 A DebtPredicate must define scope and included exposure. Useful dimensions include borrower scope, principal debt, accrued
  interest, arrears, seller notes, scenario-originated debt and whether contingent guaranties count. "Debt free" is not technically safe
  until the authored definition resolves these dimensions.
 2.3 Financial Exposure Read Model
 Provide a read model that can distinguish current principal debt, accrued interest, arrears, seller notes/vendor finance, guaranteed
 exposure, pledged collateral and other contingent obligations. The read model does not become a new finance authority; it consumes
 authoritative loans, agreements, transactions and collateral/security state.
 PART III - PRODUCT PROVENANCE & VERTICAL-INTEGRATION EVIDENCE
 3.1 Provenance Chain
 When objectives or analysis depend on origin, preserve a causal chain such as SourceEnterprise -> ProducedInventoryLot ->
 Shipment/Transfer -> DestinationInventory -> Recipe/Transformation -> FinishedGood -> CustomerTransaction. Exact schema may
 use lot ancestry, transformation records or transaction linkage, but the player must not receive provenance credit merely because two
 enterprises share an owner.
 3.2 Distinct Product Identity
 Distinct-product objectives need stable semantic product/recipe identity. Five lots of the same egg dish do not become five distinct
 products because they have different lot IDs or display text.
 3.3 Internal Transfers
 Internal transfers between controlled businesses or segments must move real inventory and attribute real landed/transport cost
 without manufacturing external revenue or duplicate cash. If the businesses remain legally/economically separate, preserve the actual
 intercompany agreement/transaction treatment required by Canon rather than collapsing them to one segment.
 3.4 Shipment Arrival Integrity
 Destination inventory and provenance should update only when the authoritative shipment or justified off-map leg actually completes.
 Failed/blocked shipments must not create qualifying destination inventory.
 TECH LOCK - A provenance objective is only as trustworthy as shipment and inventory integrity.
 PART IV - PERSON TASK RESPONSE, CAPABILITY EVIDENCE & RELATIONSHIPS
 4.1 Task Assignment Versus Worker Response
 Extend TaskInstance/Employment execution so assignment can produce explicit response/outcome states. Conceptual examples:
 Accepted, Delayed, Refused, PartiallyCompleted, Abandoned, Completed, CompletedWithError. Domain-specific failure/result state
 continues to capture quality, loss, injury or rework.
 4.2 Response Causes & Observability
 Response logic may consume workload, fatigue, capability, fit, safety, legality, compensation, reliability, treatment, relationship history
 and alternatives. Player-facing reason text should respect information visibility; the simulation may know more than the player.
 4.3 Relationship State
 Employment/supervisory relationships should persist enough event/evidence history for behavior systems to evaluate treatment and
 continuity. Avoid one universal Morale value when specific pay, schedule, conflict, trust or prior-interaction evidence is available.
 4.4 Capability Evidence
 Separate ActualCapability from ObservedEvidence, player-facing estimate/band and confidence. Practical demonstrations, references
 and observed work append evidence without revealing hidden truth directly.
 4.5 Capability Presentation Read Model
 The Canon display bands are Abysmal/Bad/Poor/Average/Great/Excellent/Mastered with Low/Moderate/High confidence. The read
 model should be able to return one band at High confidence, two adjacent at Moderate, three adjacent at Low or Not enough
 information. Exact estimation algorithm remains implementation/calibration detail.
 PART V - GRANULAR DELEGATION & FULL AUTONOMY
 5.1 Delegated Authority State
 Deepen existing DelegatedAuthority/Manager Activity direction with explicit responsible Person, decision/task family, scope,
 recurring/one-time status, financial/spending limits, policy references, escalation triggers and owner-reserved locks. Use existing
 Task, Agreement, Employment and Business authorities where they own execution.
 5.2 Full Autonomy Preset
 Full Autonomy should be implemented as authored/player-configurable policy that assigns broad routine authority through the same
 delegation layer. It must not bypass Task execution, resource reservation, Manager Activity, financial authority, exceptions or owner-
 reserved actions.
 5.3 Owner-Absence Acceptance
 Automated QA should support a period in which the player issues no routine Crown decisions. The test passes only if real delegated
 people continue required decisions/tasks, obligations remain current and no hidden owner callback is required for routine work.





                                                        LAND & LEDGERS

## TECH-XIV — original PDF page 100 of 170

LAND & LEDGERS / REVISION IV / TECHNICAL IMPLEMENTATION BIBLE                                                  3
 PART VI - BUSINESS CAPABILITY, BUILDING CONDITION & ACCESS
 6.1 Functional Space
 Preserve/extend the existing Parcel -> Building -> Functional Space -> occupancy/business-use separation. A business can reference
 multiple spaces/buildings and one building can host multiple uses without duplicating the Business authority.
 6.2 Building Condition
 Shared Building/Asset state should support the Canon condition domains Structural, Exterior, Interior and Functional. Player-facing
 bands are Critical/Poor/Fair/Good/Excellent; deeper component state can remain internal where necessary.
 6.3 Access Graph
 Represent enough topology/rights state to distinguish exterior access, internal connections, staff/customer access, sealed
 connections and concealed connections. Remodeling actions mutate physical/access state through real Projects/Tasks rather than
 toggling a saloon-only flag.
 6.4 Concealment
 Concealed spaces/doors require physical state and information/discoverability logic. They should not be represented by an
 unconditional Hidden boolean that defeats inspection, witnesses or later discovery.
 PART VII - GAMBLING, CONCESSIONS & INDEPENDENT ROOM USE
 7.1 GamblingOperation Reuse
 Deepen Revision II saloon/gambling architecture through shared Person, Task, Asset, Space, Transaction, LegalRegime and
 Agreement authorities. Free poker can operate with zero gaming-revenue transactions while associated customer behavior creates
 ordinary food/drink transactions.
 7.2 Faro Bankroll
 A banking game requires explicit bankroll owner/exposure and game settlement. The premises owner does not automatically own the
 dealer/banker bankroll or gambling P&L.
 7.3 Concession Agreement
 Agreement architecture should be able to express premises/space, term, payment cadence, exclusivity, supplied services, bankroll
 responsibility, operating responsibility, licensing responsibility, food/board consideration, revenue ownership, breach and termination.
 Hank Mercer's Crown faro concession is the reference fixture, not a universal contract template.
 7.4 Independent Room Arrangement
 A room-use/occupancy agreement can coexist with independent economic activity without creating Employment. If historically
 supported prostitution or other vice activity is implemented, use shared Person, Occupancy/Premises, Agreement, Transaction,
 LegalRegime, Enforcement, Information and risk authorities.
 PART VIII - CUSTOMER AWARENESS, MARKET RESEARCH & COMPETITION
 8.1 Awareness Representation
 Provide a bounded customer/group awareness representation sufficient for causal marketing and market research without maintaining
 a perfect unbounded event history for every Person. Awareness can reference source/category, recency/strength/confidence where
 useful, but exact storage requires profiling.
 8.2 Marketing Event Flow
 Marketing actions should create messages/exposures through real channels and then influence awareness/consideration. Do not
 write a direct permanent DemandBonus onto the business merely because a handbill action occurred.
 8.3 Standardized Traffic Observation
 Traffic-rank objectives require an authored metric definition and observation window. The technical layer must distinguish actual
 customer activity from player-observed/reported samples where imperfect information is intended. Exact Saloon Circuit rank-window
 calibration remains open.
 8.4 Market Research Actions
 Conversations with customers/staff should create Information/Evidence records or otherwise update Known State based on the
 responding Person's actual knowledge, error and incentives. Employee recommendations are evidence-bearing suggestions, not
 guaranteed truth.
 PART IX - SALE FINANCING, GUARANTIES & CLOSING
 9.1 Transaction Structure
 Business sale workflows should explicitly distinguish CashAtClosing, BuyerThirdPartyDebt, SellerNote, RetainedEquity,
 SellerGuaranty and PledgedCollateral. These may coexist in one closing and must not be collapsed into one financed-amount field.
 9.2 Guaranty Persistence
 A guaranty remains a persistent Agreement/Obligation after the player sells 100 percent of the business if the contract says so.
 Save/load, lender diligence and financial exposure reports must continue to see it.





                                                        LAND & LEDGERS

## TECH-XIV — original PDF page 101 of 170

LAND & LEDGERS / REVISION IV / TECHNICAL IMPLEMENTATION BIBLE                                                  4
 9.3 Guaranty Trigger & Settlement
 When borrower default or another contractual condition triggers the guaranty, transition the obligation through the actual
 agreement/claim/transaction path. Do not simply delete contingent exposure or convert it to debt without a triggering event and
 settlement authority.
 9.4 Lender Information State
 Credit decisions should consume lender Known/Recorded State, representations and diligence results rather than full hidden player
 state. Misrepresentation/concealment mechanics, if implemented, operate through Information/Agreement/Legal systems rather than
 a scripted lie button that magically succeeds or fails.
 9.5 Institution/Legal Type
 Financial institutions should carry enough legal/institution type to gate powers that differ by jurisdiction/date. Exact
  national/state/territorial/private banking rules remain researched data; avoid scattered one-off loan eligibility checks.
 PART X - MONEY, UI & EXACTNESS
 10.1 Exact Monetary Authority
 Continue to store/settle authoritative money at exact supported precision. Compact UI rounding, truncation or whole-dollar
 presentation cannot alter affordability, debt payoff, closing proceeds or objective evaluation.
 10.2 Known Versus Estimated Read Models
 Known recorded transaction facts should display exact values where appropriate. Forecasts and hidden-state estimates should carry
 confidence/range/Unknown. Do not run known ledger facts through a generic uncertainty formatter.
 PART XI - SAVE/LOAD, HISTORIES & STATE INTEGRITY
 11.1 Idempotent Objective Persistence
 Save/load must preserve completed-event evidence, current-state evaluation inputs and maintained-state progress without duplicate
  firing or reset. Objective histories should be bounded/indexed for UI while retaining the authoritative evidence needed for audit.
 11.2 Provenance Persistence
 Inventory lineage, transformation ancestry and qualifying transaction links required by objective truth must survive save/load and
 resolution changes without duplication or orphaning.
 11.3 Obligation Persistence
 Loans, seller notes, guaranties, collateral/security and closing agreements remain persistent until authoritative events resolve them.
 Disposition of the underlying business cannot silently delete a seller's surviving obligation.
 11.4 Repository Integrity Gate - Blocked Shipment Defect
 Project authority-audit planning identified a serious existing-repository defect around CreateBlockedShipment(...): a blocked shipment
 can retain fabricated loaded cargo while callers restore seller stock or pending reorder state, allowing duplicated physical inventory
 and incorrect later delivery/settlement semantics through save/load. Before provenance-dependent objectives are trusted,
 implementation must re-audit and repair the authoritative shipment creation/failure sequence so one physical lot cannot exist
 simultaneously at source and in a blocked shipment.
 TECH PRIORITY - Shipment/inventory integrity is P0 for provenance-dependent Saloon Circuit objectives.
 PART XII - ACCEPTANCE FIXTURES
 12.1 THE CHICKEN - Provenance Objective
 Create one owned farm chicken, perform the required processing, transport it physically to Crown, prepare a qualifying product and
  sell it to one customer. Confirm the fifth distinct-product event completes. Then save, sell the farm and reload; the historical objective
 remains complete. Repeat with outside-purchased poultry and confirm no internal-supply credit.
 12.2 THE BAKERY BUYOUT - Guaranty & Debt Separation
 Sell the bakery for cash to a buyer financed by a third-party lender while the player guarantees up to $100. After closing, player
 ownership is zero, seller-note balance is zero, principal debt can be zero and contingent guaranteed exposure remains $100. Trigger
 buyer default and confirm the guaranty transitions through its agreement rather than disappearing.
 12.3 AUTONOMOUS CROWN - Delegation
 Assign broad routine Crown authority to a qualified manager and simulate an extended owner-absence period. Real Tasks,
 purchasing, schedules, cash handling, inventory, maintenance and staffing decisions continue through delegated authority. Routine
 operation must not require hidden owner clicks. Owner-reserved sale/mortgage/major borrowing actions still escalate.
 12.4 Additional Regression Cases
  Sell 1 percent of a required 100 percent saloon stake and confirm the current ownership predicate becomes false.
  Create a failed/blocked shipment and confirm no destination provenance credit and no duplicated lot after save/load.
  Assign an unusually long shift and confirm no universal hard refusal threshold; response uses Person state/circumstances.
  Run free poker and confirm gaming revenue is zero while ordinary food/drink transactions remain separate.
  Run a faro concession and confirm dealer/banker P&L does not flow into Crown except through explicit concession/food-drink
     terms.
  Set authoritative cash to $19.72, display a compact $20, price an item at $20 and confirm purchase is denied.





                                                        LAND & LEDGERS

## TECH-XIV — original PDF page 102 of 170

LAND & LEDGERS / REVISION IV / TECHNICAL IMPLEMENTATION BIBLE                                                  5
 PART XIII - IMPLEMENTATION PRIORITY
 13.1 P0 - State Integrity & Scenario Authority
  Composable objective predicate framework and event/current/maintained semantics.
   Historical milestone evidence and authoritative ownership percentage queries.
   Principal debt versus contingent exposure read model.
   Inventory/product provenance and shipment cargo-integrity repair.
  Stable transaction/event identity and save/load idempotence.
 13.2 P1 - Human, Delegation, Property & Commercial Systems
  Task acceptance/refusal/abandonment/error and relationship history.
   Capability evidence/confidence and practical hiring demonstrations.
  Granular delegation authority and Full Autonomy configuration.
  Functional spaces, building condition and access/concealment topology.
  Customer awareness/marketing and saloon gambling/concession reuse of shared systems.
 13.3 P2/P3 - Finance Depth & Scenario UX
  Buyer financing, guaranties/sureties, lender information state and institution/legal type.
  Scenario countdown/criteria presentation and traffic-rank observation calibration.
  Later adversarial/crime consequences through shared world/legal systems after the relevant foundations exist.
 PART XIV - REVISION IV RESEARCH / CALIBRATION HOLDS
  Exact bank charter powers, guaranty/surety terminology, security, enforcement and disclosure requirements by jurisdiction/date.
   Traffic-rank observation window, marketing persistence model and awareness-history storage/performance strategy.
  Exact worker refusal/rapport tuning and capability-estimation thresholds.
  Exact simulation speed ratios and deadline-display thresholds.
   Historical gambling/prostitution legal data remains separate from architecture.
 PART XV - REVISION IV SUPERSESSION / DEEPENING MAP
  Revision III compound objective evaluation is deepened with explicit event/current/maintained semantics, completion evidence
    and countdown read models.
  Revision III Lifetime Ledger/history doctrine is deepened with objective-specific persistent qualifying evidence and provenance.
  Revision II Task architecture is deepened with Person response states rather than assignment implying execution.
  Revision II delegation and Manager Activity are deepened with explicit authority scope/limits and Full Autonomy configuration.
  Revision II saloon/gambling architecture is deepened with concession, bankroll ownership and independent room-use
     relationships.
  Revision II/III finance and sale workflows are deepened with guaranties and contingent exposure persistence.
   Persistence/testing guidance is deepened by THE CHICKEN, THE BAKERY BUYOUT and AUTONOMOUS CROWN end-to-end
      fixtures.
 END OF REVISION IV INTEGRATION
 The preserved Technical Implementation Bible Revision III follows unchanged and remains authoritative except where Revision IV
  explicitly adds, deepens, corrects or supersedes technical guidance.





                                                        LAND & LEDGERS

## TECH-XIV — original PDF page 103 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

          LAND & LEDGERS
   TECHNICAL IMPLEMENTATION BIBLE - REVISION III

   Autonomous Enterprise Ladder Integration: Scenario Geography, Control Resolution, Segment
                   Reporting, Persistent Career Metrics & Completion Cinematics

                                            Revision date: September 8, 2026
                    Source baseline: Technical Implementation Bible Revision II - September 7, 2026
                         Canon compatibility target: Canonical Project Bible Revision XX
           Status: Active technical planning reference - repository audit remains required before implementation claims

PRESERVATION GUARANTEE
Revision III preserves Technical Implementation Bible Revision II in full except where this integration explicitly adds, deepens,
corrects or supersedes technical guidance. The complete Revision II document follows this integration unchanged as the preserved
baseline. Repository evidence remains authoritative for what is implemented. No conceptual type, field or service name below is
proof that it already exists in Unity.
PURPOSE
Revision III translates the autonomous Enterprise Ladder playtest and subsequent scenario/world-scale notes into implementation
architecture. Revision II already covers tasks, Manager Activity, Action Discovery, dynamic liquidity, cap tables, railway corridors,
governance and corporate sale. Revision III therefore concentrates on the remaining gaps: geographic scenario qualification, very
large multi-settlement map assumptions, effective-control resolution, relationship transferability, owner-dependency diligence,
internal segment/cost-center reporting, generic major-enterprise development and capital-stack read models, persistent Lifetime
Ledger attribution, automated long-run scenario QA and route-agnostic scenario completion cinematics.
REVISION III ARCHITECTURE LOCKS
 • Scenario evaluation must be able to query authoritative geography, not just economic values.
 • On-map and off-map are simulation resolutions of the same entities and relationships, never parallel economic authorities.
 •  Effective enterprise control is derived from governance and authority, not inferred from one owner field or ownership percentage.
 •  Relationship continuity and change-of-control behavior must be explicit enough for diligence and closing workflows.
 •  Internal segment reporting is tagging/attribution over one enterprise truth, not a second ledger authority.
 •  Lifetime scenario metrics must persist continuously from authoritative events when objectives depend on them.
 • Scenario completion presentation must be data-driven, subtitle-capable, provider-agnostic and compatible with multiple valid
   player paths.
PART I - SCENARIO DEFINITION & GEOGRAPHIC QUALIFICATION
1.1 ScenarioDefinition / ScenarioState Authority
Deepen the existing scenario-state direction with persistent authored scenario identity, primary settlement/region references,
objective definitions, objective history, completion state and scenario presentation references. Scenario state should consume
authoritative world data rather than duplicate business, property or population truth.
1.2 ScenarioObjective Geographic Constraint
Objectives should support an optional GeographicConstraint linked to authoritative settlement, parcel group, district, corridor, region
or other stable geographic identity. A qualifying business/property/project/disposition command is accepted only when its resolved
authoritative location satisfies the constraint.
 • RequiredSettlementId or permitted settlement set.
 • RequiredRegionId / corridor / district where authored.
 • Location policy for multi-site enterprises: primary site, any qualifying site, all required sites or explicit site target.

                                                            1

## TECH-XIV — original PDF page 104 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

 • Time condition: location at acquisition, operation, sale or other authored checkpoint.
 • Clear diagnostic reason when an otherwise valid objective fails only because geography is wrong.
TECH LOCK - Objective geography must be evaluated from simulation location authority, not from UI labels or prefab
names.
1.3 Compound Objective Evaluation
The scenario engine should compose geographic constraints with richer objective predicates already implied by the project: value,
debt, ownership, property title, sustained performance, control, historical accumulation, sequence and disposition. Prefer
composable predicates over a new hard-coded class for every scenario.
1.4 Primary Scenario Settlement Is Not the Whole World
A scenario may designate one settlement as the narrative/economic anchor while all other settlements continue normal simulation.
Scenario focus must not pause or simplify unrelated towns unless a separate performance rule explicitly changes simulation
resolution.
PART II - VERY LARGE MULTI-SETTLEMENT WORLD &
ON-MAP/OFF-MAP RESOLUTION
2.1 World Model Assumption
Future world architecture should assume multiple economically meaningful settlements can coexist on one very large playable map.
SettlementNode and geography systems therefore must not encode a permanent one-town singleton assumption.
2.2 On-Map Primacy
The design target favors on-map business, travel, logistics and development wherever practical. This is implemented through
active-region streaming and multi-resolution simulation rather than by keeping every distant GameObject alive.
2.3 Same Entity Across Resolution Boundaries
When an on-map person, wagon, shipment, business activity or other persistent entity transitions into coarse/off-map simulation,
retain the same stable identity, commitments, cargo, ownership, task state, route and timing. Returning to active simulation
re-manifests the same entity.
TECH LOCK - Resolution change cannot create, destroy, duplicate or economically reset an entity.
2.4 No Parallel Off-Map Economy
Off-map market and travel systems may aggregate detail for performance, but their transactions and physical consequences must
settle into the same authoritative cash, inventory, obligations, relationships and world histories. Do not maintain an unrelated
regional money/inventory simulation whose results are later approximated into local truth.
2.5 Geospatial Indexing
Large-map queries should use indexed settlement/parcel/route relationships and dirty/event-driven updates. Scenario objective
checks, market search, migration search and business discovery should not scan every world entity every frame.
PART III - EFFECTIVE CONTROL & ENTERPRISE SLOT RESOLUTION
3.1 ControlResolution Read Model / Service
Extend Revision II cap-table and governance architecture with a derived control resolver capable of explaining who currently
controls an enterprise and the authority chain producing that result. It should not become a competing ownership authority.
 • Economic holdings/share classes.
 • Voting rights and proxies where supported.
 • Board appointment/removal rights.
 •  Officer/executive appointment authority.
 • Reserved shareholder/board powers.
 • Lender or contractual restrictions that materially limit control.
 • Temporary receivership or other researched control states where later implemented.


                                                            2

## TECH-XIV — original PDF page 105 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

3.2 Scenario Controlled-Enterprise Evaluation
The Enterprise Ladder one-enterprise rule should query effective control plus substantive development status. A formed
railway/mine/utility development company can occupy the slot before revenue when the player controls it and has committed real
capital/development work.
3.3 Control-Block Disposition
Deepen Revision II Corporate Sale/Merger workflow so a transaction can assemble a control block from multiple shareholders.
Closing must update each selling holder separately, calculate proceeds by actual holdings/terms, apply governance changes and
then recompute ControlResolution.
3.4 Qualification Event
A scenario disposition objective should fire from the post-closing control state, not from a sale button, officer resignation or
seller-note repayment. If control remains with the player, the objective remains incomplete.
TECH LOCK - Ownership mutation, governance mutation and scenario qualification are separate steps with explicit
ordering.
PART IV - RELATIONSHIP TRANSFERABILITY & OWNER-DEPENDENCY
DILIGENCE
4.1 Change-of-Control Metadata
Agreements and economically meaningful relationship records should expose enough state for acquisition workflows to determine
whether ownership/control change preserves, assigns, terminates or requires consent for the relationship. Exact field names follow
domain architecture.
 • assignment/transfer rule;
 •  change-of-control clause/status;
 • counterparty consent requirement and response;
 •  renewal/termination rights;
 •  person-specific dependency flag or evidence;
 • known/unknown transferability confidence.
4.2 Transferability Diligence Read Model
Extend Revision II Enterprise Evaluation to report not only whether key agreements are assignable but why continuity is secure or
uncertain. Customer and supplier relationships without formal contracts may still require evidence-based continuation estimates
rather than a boolean.
4.3 OwnerDependencyReport
Add a read model that identifies which capabilities or relationships disappear with a departing owner or key person. Inputs can
include actual Task/Decision history, DelegatedAuthority, manager coverage, customer/supplier interaction history, technical
capability, records access and person-specific agreements.
 •  physical work;
 •  technical expertise;
 • management/decision families;
 • purchasing/sourcing judgment;
 • customer relationships;
 •  supplier/lender relationships;
 •  records/institutional knowledge;
 •  reputation/contact dependence.
Do not reduce this to ReplacementOwnerWage alone.





                                                            3

## TECH-XIV — original PDF page 106 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

PART V - INTERNAL SEGMENT / COST-CENTER REPORTING
5.1 Segment Is Reporting/Attribution, Not Enterprise Authority
Introduce a lightweight InternalSegment or CostCenter concept only where needed for diagnostics. It can represent a site,
department, route, function, product family or project inside one consolidated enterprise. It must not own independent cash or
external revenue unless it is genuinely a separate legal/economic entity.
5.2 Transaction and Work Attribution Tags
Authoritative Transactions, Tasks, WorkOrders, Shipments, inventory movements, payroll and capital projects may carry optional
segment attribution. Enterprise-wide statements aggregate the authoritative events once; segment reports partition/attribute them
without duplication.
5.3 Internal Transfer Treatment
A movement between segments of the same consolidated enterprise may create location, custody, cost-allocation or managerial
records but does not automatically create external revenue, receivable, payable or cash movement. Separate legally distinct
subsidiaries continue to use real intercompany relationships until actually merged.
TECH LOCK - Segment reporting may explain economics but cannot mutate consolidated economic truth.
5.4 Read Models
Useful outputs include revenue contribution where externally earned, direct/allocated cost, labor demand, asset utilization,
inventory/working-capital usage, capacity bottlenecks, project burden and contribution margin. Allocation methodology must be
disclosed when the number depends on a policy choice.
PART VI - GENERIC MAJOR-ENTERPRISE DEVELOPMENT & CAPITAL
STACK
6.1 EnterpriseDevelopmentState
Generalize Revision II RailwayCorridor/Segment development concepts so other capital-intensive enterprises can persist
meaningful pre-operation state. A generic major-enterprise development workflow can link investigation, promotion/formation,
site/rights, studies, capital assembly, construction/setup, commissioning and operation without forcing every industry into
railway-specific classes.
6.2 CapitalStack Read Model
Financeability should be able to summarize founder equity, outside equity, secured debt, bonds, vendor finance, seller finance,
credit facilities, committed but undrawn funds, contingency sources and current funding gap. Each underlying claim remains in its
proper authoritative finance entity.
6.3 Physical Commissioning Versus Standalone Viability
Project and network systems should be able to mark a stage physically complete/commissioned and route actual service through it
while EconomicViabilityReadModel separately shows that the stage may still lose money after operating, maintenance and financing
burden.
6.4 Capital Calls and Governance
When forecast final cost changes, the company should issue real financing proposals/capital calls through governance. Outside
investors can participate, decline, negotiate dilution or impose restrictions according to their rights. Avoid automatic outside-money
refill.
6.5 Reserve Equipment Analysis
Asset reporting may expose availability, idle time, failure coverage and lost-capacity risk so a reserve asset can be evaluated
economically. Do not encode a static redundancy bonus.
PART VII - LIFETIME LEDGER & OWNER/ENTERPRISE ATTRIBUTION
7.1 Persistent CareerMetric Authority
Deepen the existing Lifetime Ledger direction into a persistent append/update authority fed by authoritative transactions and
lifecycle events. Metrics required for scenario evaluation should not be reconstructed from UI summaries at campaign end.



                                                            4

## TECH-XIV — original PDF page 107 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

 •  starting/current/peak Net Worth;
 • owner liquid cash;
 •  enterprise external revenue while controlled;
 •  enterprise operating profit while controlled;
 • owner distributions/dividends;
 •  interest paid/earned;
 •  payroll and taxes;
 •  capital contributions and withdrawals;
 • businesses founded/acquired/sold;
 • purchase and disposition consideration;
 •  realized disposition result;
 •  seller-note issuance/collections/defaults;
 •  construction and capital invested;
 • time each enterprise was controlled.
7.2 Attribution Dimensions
Every cumulative metric should identify the accounting perspective needed by the objective: owner, specific enterprise, consolidated
controlled group, or full world. A corporation's $X external revenue must not automatically become owner lifetime income when
other shareholders own most of the economics.
7.3 Idempotent Persistence
Save/load and migration must preserve cumulative counters/event identity without double-counting past activity. Scenario
reconstruction should be able to validate selected totals against transaction/event history in debug/test builds.
PART VIII - SCENARIO COMPLETION PRESENTATION & CINEMATIC
PIPELINE
8.1 ScenarioCompletionPresentationDefinition
Add authored completion-presentation data separate from scenario objective truth. Conceptually it may reference completion title,
narration audio, subtitle text/timing, music cue, camera beats, shot selectors, duration policy and follow-up result screen.
Completing the objective triggers the presentation; the presentation never decides completion.
8.2 Route-Agnostic Narration
The standard Enterprise Ladder voiceover should be authored around the structural arc rather than named industries. Avoid runtime
lines that assume the player used a specific bakery, freight company, bank, mine or railway unless the scenario variant actually
requires it.
8.3 World-State Camera Targets
Camera beats should prefer actual authored/simulation entities from the completed campaign: the designated scenario settlement,
the player's early-enterprise site where still present, later enterprise/infrastructure, sold businesses, depot/industrial areas or a wide
settlement shot. Use robust fallback selectors when a specific prior site no longer exists or is not visually suitable.
8.4 Three-Beat Default
Current production guidance for The Enterprise Ladder is approximately three visual beats: early enterprise, integrated/mid-scale
enterprise, major-enterprise/wider-world result. A roughly 25-35 second narration is a useful prototype target after the one-minute
text-to-speech test felt longer than necessary. Treat timing as configurable calibration, not a simulation rule.
8.5 Audio Provider Independence
Narration should enter Unity as a normal replaceable audio asset with subtitle text. Prototype audio can come from a temporary
synthetic-voice workflow, while final production may use another synthetic source or a human actor without changing scenario logic.
Do not create a runtime dependency on one external TTS provider unless deliberately designed later.



                                                            5

## TECH-XIV — original PDF page 108 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

8.6 Voice Direction Metadata
Production notes may store intended performance style - grounded North American/frontier period tone, calm authority, restrained
pacing - without making accent a gameplay rule. The British prototype voice demonstrated useful period gravitas but is not the
intended setting lock.
8.7 Captions and Accessibility
Every voiced ending should provide captions/subtitles and allow the player to skip or replay the sequence according to final UX
policy. Completion state must be persisted before or independently of the cinematic so skipping or interruption cannot lose scenario
completion.
TECH LOCK - Completion presentation consumes scenario state; it never owns scenario truth.
PART IX - AUTOMATED SCENARIO QA / AUTONOMOUS PLAYTEST
HARNESS
9.1 Long-Run Scenario Harness
Deepen Revision I deterministic scenario testing with a headless or reduced-presentation harness capable of running months/years
using seeded worlds and scripted/AI decision policies. The purpose is regression and balance evidence, not to replace human
playtesting.
9.2 Output Bundle
A long-run test should export scenario objective state, Lifetime Ledger, enterprise/control history, major transactions, Manager
Activity summaries, failures/exceptions, save/load checkpoints and relevant world statistics. This prevents narrative-only test notes
from becoming the sole record.
9.3 Information Firewall Test Mode
For AI-driven scenario QA, support separate host Actual State and player-facing Known/Recorded/Forecast inputs. The test agent
should not receive hidden state that a human player could not legitimately know.
PART X - TESTING & REGRESSION ADDITIONS
10.1 Geographically Bound Objective
Create two otherwise identical qualifying enterprises in different settlements. Confirm only the one inside the authored geographic
scope satisfies the objective.
10.2 Multi-Site Geographic Semantics
Test primary-site, any-site and explicit-site objective modes so a multi-site enterprise cannot qualify accidentally through an
unrelated branch.
10.3 On-Map / Coarse Resolution Continuity
Move a Person, shipment, wagon and active contract across the resolution boundary, save/load, then reactivate. Confirm identity,
cargo, assignment, cash and timing remain singular and continuous.
10.4 Control Resolution
Test majority ownership without control, minority ownership with valid control rights, board removal, lender restriction and
post-acquisition control-block transfer. Scenario controlled-enterprise state must follow effective control.
10.5 Relationship Change of Control
Sell an enterprise with a mix of assignable, consent-required, person-specific and terminable agreements. Confirm each follows its
own rule and no relationship silently retargets.
10.6 Segment Attribution
Move inventory between two segments of one consolidated enterprise. Confirm segment reports update while consolidated external
revenue, cash and profit do not duplicate.
10.7 Lifetime Ledger Persistence
Run transactions, payroll, taxes, interest, acquisition and sale; save/load repeatedly; confirm cumulative career metrics remain exact
and idempotent.

                                                            6

## TECH-XIV — original PDF page 109 of 170

                         LAND & LEDGERS / REVISION III / TECHNICAL IMPLEMENTATION BIBLE

10.8 Completion Cinematic Branch Robustness
Complete the same route-agnostic scenario through different business paths. Confirm the same generic narration remains valid,
camera selectors find appropriate real-world fallback targets and skipping the cinematic cannot undo completion.
PART XI - PRODUCTION & PERFORMANCE GUIDANCE
11.1 Do Not Implement Every Revision III System at Once
Revision III is target architecture. Near-term implementation should continue to follow the master roadmap and repository migration
sequencing. Scenario geography, Lifetime Ledger correctness and authority-safe control/relationship models should land before
deep late-game cinematic polish if dependency order requires it.
11.2 Bounded Histories and Aggregates
Manager Activity, Lifetime events and scenario histories may grow over decades. Persist authoritative records needed for audit and
scenario truth while using indexed summaries/archives for UI and performance rather than loading every historical event into every
screen.
11.3 Streaming Presentation Is Not Simulation Authority
Cinemachine shots, scene activation, LOD, terrain streaming and other presentation systems must not alter ownership, inventory,
task or scenario truth merely because an area is not rendered.
PART XII - REVISION III RESEARCH / CALIBRATION HOLDS
12.1 World Scale
Exact playable-map dimensions, settlement count, active-region radius and the final on-map/off-map activity share remain
performance and design calibration holds.
12.2 Corporate Control Detail
Exact period governance forms, proxy rules, shareholder voting thresholds, takeover procedure and creditor consent continue to
depend on researched legal/company context.
12.3 Cinematic Production
Exact 25-35 second target, camera path, number of shots, voice actor/accent, music, TTS provider and recording workflow remain
production calibration. The architecture should keep these replaceable.
PART XIII - REVISION III SUPERSESSION / DEEPENING MAP
13.1 Revision II Deepened
 • Scenario-state direction is deepened with explicit geographic objective constraints and designated settlement/region references.
 • Revision II corporate governance is deepened with derived effective-control resolution and control-block closing semantics.
 • Revision II Enterprise Evaluation is deepened with relationship-transferability and owner-dependency reporting.
 • Revision II Division/rail reporting is deepened with internal segment/cost-center attribution that never becomes a second ledger.
 • Revision II railway development is generalized into a reusable major-enterprise development/capital-stack pattern.
 •  Existing Lifetime Ledger direction is deepened into persistent owner-versus-enterprise cumulative attribution.
 • Scenario testing is deepened with long-run autonomous/headless runs and an information-firewall option.
 • Scenario completion gains a provider-agnostic cinematic/audio/subtitle presentation layer tied to real world-state camera targets.

END OF REVISION III INTEGRATION
The preserved Revision II document follows unchanged and remains authoritative except where Revision III explicitly adds,
deepens, corrects or supersedes technical guidance.





                                                            7

## TECH-XIV — original PDF page 110 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

            LAND & LEDGERS
         TECHNICAL IMPLEMENTATION BIBLE - REVISION II

  Third Full Playtest Integration: Task Authority, Action Discovery, Delegation Logs,
                   Corporate/Rail Architecture & Historical Events

                                            Revision date: September 7, 2026
                     Source baseline: Technical Implementation Bible Revision I - August 29, 2026
                        Canon compatibility target: Canonical Project Bible Revision XIX
           Status: Active technical planning reference - repository audit remains required before implementation claims

PRESERVATION GUARANTEE
Revision II preserves Technical Implementation Bible Revision I in full except where this integration explicitly adds, deepens,
corrects or supersedes its technical guidance. The complete Revision I document follows this integration unchanged as the
preserved baseline. Where a conflict exists, Revision II controls. No class names, service names or schema fields below should
be treated as proof that the current Unity repository already contains them. Repository evidence remains authoritative for
implemented reality.

PURPOSE
Revision II translates the third full text playtest into technical architecture. The playtest exposed implementation requirements
that Revision I only partially covered: persistent meaningful tasks, manager-decision auditability, action-discovery UI, adaptive
simulation cadence, dynamic liquidity, line-of-credit authority, cap tables and corporate governance, staged railway corridor
entities, dated historical events, noncash barter, clerk-mediated retail execution, early telephone subscriber/public-access
behavior, saloon/gambling/vice reuse of shared authorities, reward/private-detective case workflows and whole-corporation
transfer.
The core Revision I doctrine remains unchanged: Canon defines intended behavior; the Technical Bible defines preferred
implementation architecture; the current repository defines what actually exists. Audit before editing.

PART I - REVISION II ARCHITECTURE PRINCIPLES

1.1 High action visibility without simulation spam
The UI should expose meaningful actions that already exist in the authoritative simulation. It should not create a second quest-
like state machine that manufactures possibilities independently of business, project, legal, labor or opportunity state.
The implementation target is:
Authoritative condition -> Action Candidate / Recommendation Read Model -> Player chooses self-manage, delegate,
subcontract, defer or ignore -> Command goes to owning system.

1.2 Complexity through shared authorities
New features should prefer existing shared authorities for Person, Employment, Task, Agreement, Shipment, Project,
PropertyRight, Asset, Transaction, LegalRegime, Information, Opportunity/Pressure, Corporate ownership and persistence.
Avoid isolated minigame managers for saloon gambling, railway construction, barter, bounty work or telephone use when the
underlying structure already exists elsewhere.

1.3 No magic automation
Delegation and subcontracting may reduce player clicks but must preserve underlying execution, resource use, time, risk, cost,
quality, persistence and failure.





                                                   1

## TECH-XIV — original PDF page 111 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART II - TASK DEFINITION / TASK INSTANCE AUTHORITY

2.1 Purpose
Revision I describes workload and work orders but does not yet define a reusable small-work execution authority. Revision II
adds a shared Task layer for economically meaningful labor below major Project/Work Order scale.

2.2 TaskDefinition
A TaskDefinition is authored/configured data describing a reusable type of work. Suggested conceptual fields:
  • TaskDefinitionId
  • domain/capability tags
  • default duration model
  • compatible locations/workstations
  • required or optional equipment classes
  • material/input requirements
  • dependency rules
  • collaboration rules
  • interruptibility
  • failure/outcome profile
  • learning/experience tags
  • default priority/urgency hints
Do not encode business-specific runtime truth in the definition.

2.3 TaskInstance
A TaskInstance is persistent actual work. Suggested conceptual state:
  • TaskId
  • TaskDefinitionId
  • owning business/project/agreement/work order link
  • worker assignment(s)
  • location
  • reserved equipment/assets
  • reserved materials/inputs
  • prerequisite/dependency links
  • earliest start
  • deadline/window
  • planned/estimated duration
  • actual start
  • progress
  • priority
  • interruptibility/preemption state
  • status
  • completion/failure result
  • created follow-up tasks
  • cost/ledger attribution hooks
  • history timestamps
Exact field names follow repository audit and implementation design.

2.4 Authoritative time
Task progress uses the continuous world clock. A 37-minute task should persist its actual progress across save/load and
continue from the correct timestamp or remaining work. Do not rely solely on a scene coroutine.

2.5 Parallel work and exclusivity
Independent tasks may proceed concurrently. Resource reservation prevents one worker, locomotive, wagon, oven, workstation
or other exclusive resource from performing physically incompatible work simultaneously.


                                                   2

## TECH-XIV — original PDF page 112 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

2.6 Dynamic dependencies
Completion, arrival, inspection result, material delivery, weather change or other authoritative events may unlock or block
dependent tasks. Avoid hard-coded business-specific daily scripts where the same dependency graph can express the work.

2.7 Failure and rework
A TaskInstance may finish with acceptable completion, quality variation, recoverable failure, serious failure or domain-specific
exceptional outcome. Recoverable failure can generate actual rework TaskInstances rather than a generic Efficiency penalty.

2.8 Task versus WorkOrder versus Project
Use the smallest authoritative execution object that matches the problem:
  • TaskInstance: small meaningful labor step.
  • WorkOrder: service/repair/maintenance request with scope, target and provider relationship.
  • Project: coordinated multi-work-package capital/development effort with budget, authorization and major dependencies.
A Project can create many WorkPackages/Tasks; a WorkOrder can create many Tasks. Do not make every hand movement a
Project.





                                                   3

## TECH-XIV — original PDF page 113 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART III - CONTRACTOR & SUBCONTRACTOR EXECUTION

3.1 Agreement-based contractor model
Hiring a contractor should create or activate a real Agreement/Contract linked to Project/WorkPackages/Tasks rather than
resolving work immediately.
Contract scope can conceptually include:
  • counterparties
  • defined scope/deliverables
  • estimate or fixed/variable price structure
  • payment schedule
  • expected duration
  • quality/specification
  • owner-reserved decisions
  • contractor discretion
  • permitted substitutions
  • contingency/change-order authority
  • dependencies
  • completion/acceptance conditions
  • breach/termination

3.2 Prime contractor pattern
A prime contractor can coordinate surveyors, labor crews, subcontractors and specialists. The player may hire one firm for a
coherent service package while underlying work remains simulated and attributable.

3.3 Contractor progress read model
Expose progress at a comprehensible level, for example:
Prairie Fork Engineering - 14/37 tasks complete - 31% - expected report October 12.
Players may drill down when they care, but ordinary operation should not require managing every subordinate task.

3.4 Change authority
Contractor and manager authority should include explicit thresholds for routine field changes. A minor culvert change inside
approved contingency can proceed automatically; a large scope or cost change escalates.





                                                   4

## TECH-XIV — original PDF page 114 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART IV - ACTION DISCOVERY / "WHAT CAN I DO NEXT?"

4.1 First-class ActionCandidate read model
Add a reusable action-discovery read model generated from authoritative state. Possible conceptual fields:
  • ActionCandidateId
  • source authority/system
  • target entity/entities
  • title/player-facing action
  • reason / trigger
  • prerequisite summary
  • cost range / known commitments
  • expected duration or timing
  • likely effect
  • confidence / uncertainty
  • deadline/expiry where real
  • consequence of ignoring
  • execution modes: owner / manager / contractor / workflow
  • owning command/workflow endpoint
This is a read model, not simulation truth.

4.2 Sources
Action candidates can originate from:
  • bottlenecks
  • unresolved work
  • manager capital requests
  • opportunities/pressures
  • legal deadlines
  • expiring options
  • project exceptions
  • supplier/customer proposals
  • staffing gaps
  • maintenance
  • route capacity
  • corporate governance
  • historical event changes

4.3 UI requirements
The player should be able to see why an action exists and what system will execute it. Avoid hidden features that require prior
player knowledge of specialist terminology.

4.4 No popup flood
Available actions should normally live in browsable surfaces, recommendations or contextual panels. Interrupt only for configured
exceptions, urgent deadlines, owner-reserved decisions or serious failures.





                                                   5

## TECH-XIV — original PDF page 115 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART V - ADAPTIVE SIMULATION CADENCE & TIME ADVANCE

5.1 Advance-until-exception
Add an adaptive time-advance workflow capable of simulating hours, days, weeks or months while stopping when a material
condition requires attention.
Possible stop conditions:
  • owner-reserved decision
  • project overrun/change outside authority
  • liquidity emergency
  • major accident/failure
  • contract breach or deadline
  • financing/closing condition
  • key employee/manager loss
  • legal/historical event requiring notice
  • material new opportunity/pressure threshold
  • user-configured stop rule

5.2 Simulation still executes underneath
Fast-forward is not a monthly arithmetic shortcut. Tasks, shipments, transactions, payroll, maintenance, weather effects, arrivals,
agreements, construction progress and failures still resolve through authoritative systems at their required cadence.

5.3 Reporting cadence is presentation
Monthly/quarterly summaries are read models generated from event history. They do not replace the underlying event stream.





                                                   6

## TECH-XIV — original PDF page 116 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART VI - MANAGER ACTIVITY LOG

6.1 Purpose
Add a persistent decision/activity stream for delegated management so the player can inspect what managers and other
authorized decision-makers handled autonomously.

6.2 Suggested ManagerDecisionEvent
Conceptual fields:
  • DecisionId
  • timestamp
  • actor PersonId / role / office
  • business/site/division
  • authority source: policy, threshold, office, emergency rule
  • trigger / context
  • decision type
  • decision parameters
  • financial commitment where applicable
  • linked Task/Agreement/Shipment/Transaction/Project
  • outcome
  • escalation required / not required
  • measurable value reference when defensible

6.3 Separate from the Ledger
The Manager Activity Log explains decision ownership and workload reduction. The Ledger remains authoritative for money. Do
not duplicate financial truth into a parallel pseudo-ledger.

6.4 Aggregation
UI may summarize:
  • number of autonomous decisions
  • owner interventions
  • decisions by category
  • escalations
  • unresolved decisions
  • directly measurable value protected/created
Do not invent value attribution when no defensible counterfactual exists.





                                                   7

## TECH-XIV — original PDF page 117 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART VII - ENTERPRISE EVALUATION & TRANSFERABILITY READ MODELS

7.1 Enterprise Evaluation
Create a read model capable of supporting the canonical summary:
  • Profit
  • Customers
  • Operations
  • Transferability
  • Estimated Sale Value
Each item derives from authoritative records and forecasts.

7.2 Transferability audit
Transferability should query whether routine decision families have active authority coverage, whether required workers/roles are
present, whether critical knowledge is concentrated in departing people, whether key agreements are assignable/renewable and
whether operations survive a simulated owner-absence period.
Do not implement a boolean HasManager => Transferable rule.





                                                   8

## TECH-XIV — original PDF page 118 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART VIII - DYNAMIC LIQUIDITY & TREASURY READ MODELS

8.1 Liquidity-needs calculator
Add a derived read model that estimates near-term protected liquidity from authoritative obligations and policy. Inputs may
include:
  • payroll
  • accounts payable
  • debt service
  • taxes
  • committed purchasing
  • project draws
  • inventory replacement needs
  • transport/animal costs
  • minimum operating cash handling
  • seasonal reserve policy
  • contingency allowance

8.2 Distinct outputs
Prefer separate concepts:
  • actual cash
  • restricted/committed cash
  • projected required liquidity
  • protected reserve target
  • deployable/discretionary cash
None of these except actual cash is a new money pool.

8.3 Event-driven recalculation
Recalculate when obligations, policies, project commitments, seasonal plans or cash position change rather than every frame.





                                                   9

## TECH-XIV — original PDF page 119 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART IX - LINE OF CREDIT AUTHORITY

9.1 CreditFacility entity
Add a persistent credit-facility model distinct from installment loans. Suggested state:
  • CreditFacilityId
  • borrower
  • lender
  • facility type
  • authorized limit
  • outstanding principal
  • undrawn availability (derived)
  • interest/discount terms
  • collateral/security
  • eligible purpose / borrowing base where used
  • maturity/renewal
  • covenants
  • lender discretion/status
  • draw history
  • repayment history
  • suspension/reduction/renewal state

9.2 Accounting locks
Undrawn availability does not increase Cash and does not count as Debt. A draw creates cash and corresponding liability.
Repayment reduces both cash and outstanding liability. Interest accrues from actual outstanding use according to the contract.





                                                  10

## TECH-XIV — original PDF page 120 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART X - SUPPLIER, LANDED COST & INVENTORY TURNOVER

10.1 Product-supplier relationship
Supplier eligibility and terms should be expressible at item/category level rather than only business-wide.

10.2 Landed cost
Landed cost should combine invoice cost with attributable freight, handling, duty/tax where applicable, spoilage/shrink
expectations where justified and financing/carrying burden where the report requires it.

10.3 Consolidated shipment allocation
Freight cost allocation should use mass/volume/handling/time or another defensible burden rule. Avoid equal split by line item
unless that happens to be the chosen accounting policy and is disclosed as such.

10.4 Product economics read model
Support fields such as:
  • acquisition cost
  • replacement quote
  • freight allocation
  • financing cost
  • current selling price
  • realized margin
  • replacement margin
  • inventory age
  • sales velocity
  • receivable timing/customer terms





                                                  11

## TECH-XIV — original PDF page 121 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XI - SHARED ASSET SCHEDULING ACROSS DIVISIONS

11.1 One availability authority
Shared wagons, teams, horses, equipment and other assets require one authoritative assignment state even when several
controlled businesses can request them.

11.2 Cost attribution
Legal owner, maintenance payer and operating consumer may differ. Preserve internal attribution without double-counting
consolidated profit.





                                                  12

## TECH-XIV — original PDF page 122 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XII - CORPORATE CAP TABLE & GOVERNANCE

12.1 Corporate entity extension
A stock corporation should maintain persistent legal/economic state separate from ordinary sole-proprietor ownership.

12.2 CapTable
Suggested concepts:
  • authorized share classes
  • issued shares
  • outstanding shares
  • shareholder identities
  • holdings
  • issue/subscription history
  • transfer history
  • economic rights
  • voting rights
  • dividends/distributions
  • treasury shares if later required

12.3 Dilution preview
Before issuing shares, UI should show current holder percentage, expected percentage after issuance and required pro-rata
participation where relevant.

12.4 Governance
Support relationships for:
  • directors/board seats
  • officers
  • voting/proxy arrangements where modeled
  • reserved board/shareholder powers
  • management appointment/removal
  • approval thresholds
Do not infer board control from ownership percentage or vice versa.

12.5 Corporate cash boundary
Corporate treasury writes/reads through the corporate/business economy authority. Shareholders cannot directly spend it.
Dividends, compensation, loans or other transfers must be explicit transactions with legal/authority validation.





                                                  13

## TECH-XIV — original PDF page 123 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XIII - BONDS & LONG-TERM CORPORATE DEBT

13.1 BondIssue entity
Suggested state:
  • BondIssueId
  • issuer
  • face amount authorized/issued
  • coupon
  • issue date
  • maturity
  • security/mortgage/collateral
  • sinking/repayment terms if used
  • issuance discount/premium
  • issuance costs
  • net proceeds
  • holder/market abstraction as needed
  • covenants/default state

13.2 Enterprise value reporting
Corporate reports must not present debt assumption as shareholder cash. Keep enterprise value, debt and equity value
separate.





                                                  14

## TECH-XIV — original PDF page 124 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XIV - RAILWAY CORRIDOR & SEGMENT ARCHITECTURE

14.1 RailwayCorridor
Add a persistent planning entity representing an intended corridor independent of completed track. Suggested concepts:
  • CorridorId
  • sponsor/company
  • origin/destination intent
  • candidate alignments
  • traffic study references
  • development status
  • future node sequence
  • strategic objective

14.2 RailwaySegmentProject
Each staged extension can be its own Project/Segment with:
  • SegmentId
  • corridor link
  • start node/end node
  • surveyed mileage
  • route/alignment geometry
  • grade/drainage/structure facts
  • land/right requirements
  • traffic commitments/forecast
  • capital estimate
  • financing plan
  • construction packages
  • station/facility requirements
  • equipment implication
  • completion/service status

14.3 Track truth
Surveyed, optioned or financed mileage is not operating track. Operating network state updates only when physical
track/facilities and required legal/connection conditions are complete.

14.4 Segment activation
A completed segment joins routing/logistics as soon as service is opened, even if the larger corridor remains incomplete.





                                                  15

## TECH-XIV — original PDF page 125 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XV - RAILWAY ECONOMIC NODES

15.1 EconomicNode
Represent meaningful service nodes as persistent locations linked to geography and market evidence rather than decorative
station names.
Potential node data:
  • NodeId
  • settlement/road/farm/industrial links
  • reachable catchment
  • traffic evidence by commodity/customer
  • facility state
  • station/service status
  • future-development pressure

15.2 Negative node result
A study may conclude that an intermediate node is not economically viable. Do not force every candidate to become
buildable/valuable.





                                                  16

## TECH-XIV — original PDF page 126 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XVI - RAILWAY LAND STATUS & RIGHTS

16.1 Route parcel/land overlay
Rail route evaluation should intersect candidate alignments with authoritative legal geography. Route segments may cross:
  • private parcels
  • federal public land
  • grant land
  • restricted/reservation/trust land
  • existing railway/property rights
  • roads/public infrastructure
  • other special regimes

16.2 PropertyRight / RailRight variants
Support distinct rights such as:
  • owned right-of-way
  • easement/right-of-way
  • crossing right
  • interchange agreement
  • terminal access
  • trackage rights
  • joint-use agreement
  • lease
  • connection agreement
Avoid one generic RailPermission boolean.

16.3 No geometry-to-authority inference
Visual overlap or physical track connection cannot create legal operating authority.





                                                  17

## TECH-XIV — original PDF page 127 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XVII - RAILWAY CAPACITY & BOTTLENECK DIAGNOSTICS

17.1 Separate capacity dimensions
Track separately:
  • locomotive availability/utilization
  • freight-car availability
  • crew availability
  • track/yard route capacity
  • customer/station receiving capacity
  • interchange capacity/timing
  • maintenance downtime

17.2 Read models
Useful operating diagnostics include:
  • peak utilization
  • average utilization
  • queue duration
  • late movements
  • lost/deferred profitable traffic
  • empty/repositioning moves
  • maintenance downtime
  • foreign-car dependence
  • siding/yard occupancy

17.3 Bottleneck recommendation
CapitalRequest/ActionCandidate logic should identify the most likely constraint and candidate remedies rather than always
recommending another asset of the most visible type.





                                                  18

## TECH-XIV — original PDF page 128 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XVIII - FINANCEABILITY & ECONOMIC VIABILITY READ MODELS

18.1 FinanceabilityReadModel
Possible outputs:
  • total project funding requirement
  • committed equity
  • committed debt
  • retained cash authorized
  • undrawn but conditional financing
  • funding gap
  • conditions precedent
  • closing risk

18.2 EconomicViabilityReadModel
Possible outputs:
  • traffic/demand assumptions
  • rate/revenue assumptions
  • operating cost
  • maintenance/replacement burden
  • financing burden
  • working-capital effect
  • expected contribution/return
  • uncertainty/confidence
  • downside cases
Keep these outputs separate even when one workflow displays both.





                                                  19

## TECH-XIV — original PDF page 129 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XIX - HISTORICAL EVENT AUTHORITY

19.1 DatedHistoricalEvent
Add a centralized event authority/table for material historical changes that modify simulation rules or availability.
Suggested concepts:
  • HistoricalEventId
  • effective date/time
  • geographic scope
  • domain/category
  • legal/technology/infrastructure change
  • prerequisites/local applicability
  • state transition/action applied
  • player-facing notice template
  • acknowledgement/read state
  • persistence/versioning

19.2 System integration
Historical events can modify or make available legal regimes, network technologies, institution types, tax rules, market
connectivity or other dated state. The event notice is generated because the authoritative state changed.

19.3 Standard Railway Time
The time authority should be capable of representing the Nov. 18, 1883 railway standard-time transition where the design
requires local-time versus railway-time distinction. Implementation should avoid scattering one-off date checks through timetable
code.

19.4 Save/load
Events must apply once and reconstruct deterministically. Loading a save after the effective date must produce the correct post-
event regime without duplicate notifications or missed state transitions.





                                                  20

## TECH-XIV — original PDF page 130 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XX - DATED LEGAL / TAX REGIME EXTENSIONS FOR RAILWAYS

20.1 Central legal authority
Extend the existing dated legal-regime service rather than embedding railroad dates in railway scripts.
Candidate additions:
  • federal public-land railway right-of-way eligibility/procedure
  • territorial/local corporate-aid restrictions beginning July 30, 1886
  • railway-specific gross-earnings taxation where applicable
  • crossing/interchange rights as research matures
  • mail-service contract rules

20.2 Legal status versus enforcement posture
Generalize the existing distinction for illegal/tolerated activity. Legal status answers what law permits/prohibits. Enforcement
posture answers what authorities are actually prioritizing/tolerating. The two may differ.





                                                  21

## TECH-XIV — original PDF page 131 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXI - POSTAL SERVICE AGREEMENT

21.1 MailContract
Represent postal carriage as a specialized Agreement using shared contract/logistics/task infrastructure.
Possible state:
  • postal counterparty/authority
  • route/service points
  • required frequency/timetable
  • compensation method
  • handling/security requirements
  • start/end/renewal
  • missed-service deductions or breach terms
  • service history

21.2 Physical work
Mail service creates actual handoff, documentation, secure carriage and transfer tasks. It does not simply add a monthly income
modifier.





                                                  22

## TECH-XIV — original PDF page 132 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXII - GENERAL STORE RETAIL EXECUTION

22.1 Customer service task flow
General Store sale execution should be able to create or reserve clerk work for:
  • customer wait/queue
  • order communication
  • merchandise retrieval
  • weighing/measuring/cutting where applicable
  • packaging/wrapping
  • cash/credit/barter settlement
  • stock decrement and transaction creation
Not every product needs every subtask; use product/service metadata.

22.2 Queue and competing work
Customer-facing tasks compete with restocking, receiving, bookkeeping, ordering, cleaning, delivery preparation,
telephone/message work and other store tasks. A one-clerk store remains valid but can accumulate queues and deferred non-
customer work under load.

22.3 Staffing emerges
Do not encode GeneralStore.RequiredEmployees = 2. WorkDemand and available compatible labor remain authoritative.





                                                  23

## TECH-XIV — original PDF page 133 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXIII - BARTER & NONCASH TRANSACTIONS

23.1 Transaction consideration model
Extend transaction grammar so consideration can include multiple components:
  • cash
  • receivable/store account
  • goods-in-kind
  • service/labor-in-kind where supported
  • other contractually valid consideration

23.2 Physical transfer
Goods received in barter create actual ownership/location changes and enter inventory/asset state. Only the cash component
changes Cash.

23.3 Agreed valuation
Record the agreed economic value used for transaction/ledger purposes separately from physical quantity and later realized
resale value.

23.4 Quality/perishability
Acceptance and valuation can depend on actual/known quality, perishability, storage capacity and merchant policy.





                                                  24

## TECH-XIV — original PDF page 134 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXIV - TELEPHONE / PUBLIC ACCESS IMPLEMENTATION

24.1 Network connection remains authoritative
Telephone use requires an actual local network/exchange/private-line relationship as already directed by Revision I.

24.2 SubscriberRole / PublicAccess capability
A connected business can expose a subscriber/public-access role with policy for:
  • business calls
  • customer/public calls
  • message taking
  • fees where historically supported
  • opening hours/access
  • clerk assignment

24.3 Staff time
Incoming/outgoing calls and message handling can create work demand. Do not model telephone as a free instant-information
buff.

24.4 Local availability event
When network infrastructure reaches a settlement, generate ActionCandidates for eligible businesses to subscribe rather than
auto-connecting them.





                                                  25

## TECH-XIV — original PDF page 135 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXV - SALOON / GAMBLING ARCHITECTURE

25.1 Shared business shell
A Saloon should extend the ordinary Business model: premises, cash, inventory, employees, schedules, suppliers, agreements,
reputation, legal exposure and tasks.

25.2 GamblingOperation
If implemented, represent gambling through reusable assets/tasks/transactions rather than an isolated minigame. Conceptual
pieces:
  • table/game definition
  • physical equipment/table asset
  • dealer assignment
  • game operating schedule
  • bankroll/house exposure
  • player/customer stakes transactions
  • house win/loss outcome
  • cheating/fraud/security events where supported
  • legal status/enforcement link
Exact game mathematics remain authored/calibrated data.

25.3 Drink-solicitation employment
Use ordinary Person/Employment/Compensation architecture. Support commission or mixed wage/commission compensation
without creating a separate hidden worker species.

25.4 Prostitution / house-of-ill-fame technical status
Revision II changes the technical interpretation of the earlier parked scope:
  • The Canon now considers historically documented player-facing operation eligible for future design even when illegal.
  • This does not make it immediate implementation priority.
  • If implemented, reuse Person, Employment/Agreement, Premises/Occupancy, Transaction, LegalRegime, Enforcement,
   Health/Risk and Information authorities.
  • Do not create a magic Vice meter or Brothel License state.
  • Coercive recruitment is not a normal authorized player mechanic.





                                                  26

## TECH-XIV — original PDF page 136 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXVI - REWARD / PRIVATE DETECTIVE CASE ARCHITECTURE

26.1 Case entity
A persistent Case/Bounty entity can support both private detective work and reward pursuit while retaining subtype/legal
distinctions.
Suggested state:
  • CaseId
  • case type / legal basis
  • client/source
  • target PersonId or described unknown target
  • reward/fee terms
  • success conditions
  • known evidence/leads
  • jurisdictions
  • assigned agents
  • expenses/advance
  • travel state
  • investigation/pursuit state
  • risk/injury events
  • outcome
  • payment/settlement
  • history

26.2 Agent parity
Agents remain ordinary Persons with capabilities, employment/contract relationships, travel, fatigue, injury and persistent identity.
Off-map resolution should not create a separate fake bounty-hunter unit type.

26.3 Payment conditions
Reward/fee is earned only when the actual contract/reward conditions are met. Failure, partial success or dispute can produce
different settlement.





                                                  27

## TECH-XIV — original PDF page 137 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXVII - SUBSIDIARIES, TOWNSITES & RAIL-ADJACENT BUSINESSES

27.1 Corporate-purpose validation
Before corporate treasury funds an acquisition/project, validate corporate authority according to charter/legal/governance data
rather than only owner permission.

27.2 Subsidiary entity
A subsidiary remains a distinct legal/economic entity unless actually merged. Preserve its cash, debt, employees, contracts,
assets and ownership links.

27.3 Townsite options
Use ordinary Option/Agreement/PropertyRight concepts to represent land control without completed ownership. Track expiry,
deposits, exercise price, conditions and parcel targets.

27.4 Site lease / siding agreement
Independent station businesses such as elevators, fuel dealers, hotels or merchants should connect to the railway through real
property/lease/service agreements. Their success creates actual shipments and demand; do not apply a direct +Traffic bonus
merely because the building exists.





                                                  28

## TECH-XIV — original PDF page 138 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXVIII - CORPORATE SALE / MERGER WORKFLOW

28.1 Corporate disposition workflow
Add a workflow capable of:
  • board authorization to explore transaction
  • valuation/diligence
  • buyer/merger proposals
  • negotiation
  • debt assumption/refinancing terms
  • treatment of cash/property/subsidiaries/contracts
  • required board/shareholder votes
  • creditor/contract consents
  • closing conditions
  • pre-close distribution if authorized
  • transfer of ownership/control
  • officer/management transition

28.2 Transaction values
Keep separate:
  • enterprise value
  • equity purchase consideration
  • debt assumed/refinanced
  • transaction expenses
  • pre-close distributions
  • player's realized proceeds based on actual holdings

28.3 Employees
Buyer employment offers create new/continued Employment relationships according to worker choice. Do not transfer employees
as owned assets.





                                                  29

## TECH-XIV — original PDF page 139 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXIX - PERSISTENCE ADDITIONS

Revision II expands the minimum persistent set from Revision I to include where implemented:
  • TaskInstances and resource reservations
  • ManagerDecisionEvents
  • ActionCandidate source state only where necessary; most ActionCandidates should be regenerated from authoritative state
  • CreditFacilities
  • corporate cap tables/holdings
  • board/officer/governance relationships
  • BondIssues
  • RailwayCorridors and SegmentProjects
  • railway land/right agreements
  • DatedHistoricalEvent application/read state
  • postal contracts
  • barter transaction components
  • telephone subscriber/public-access state
  • gambling operation assets/agreements if implemented
  • detective/reward Cases
  • townsite options/subsidiaries
  • corporate sale/merger workflows in progress





                                                  30

## TECH-XIV — original PDF page 140 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXX - UI READ MODEL ADDITIONS

Recommended new read-model families:
  • What Can I Do Next / Action Discovery
  • Manager Activity
  • Enterprise Evaluation
  • Dynamic Liquidity / Deployable Cash
  • Credit Facility / Line of Credit
  • Cap Table / Dilution Preview
  • Board & Governance
  • Railway Corridor / Segment Development
  • Railway Capacity & Bottleneck
  • Financeability
  • Economic Viability
  • Historical Event Notice / World Changes
  • General Store Customer Queue / Workload
  • Barter Consideration
  • Telephone Access / Message Work
  • Corporate Transaction / Sale-Merger





                                                  31

## TECH-XIV — original PDF page 141 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXXI - TESTING & REGRESSION REQUIREMENTS

31.1 Task persistence
Save during an in-progress 37-minute task. Reload. Confirm the same task, worker, reserved equipment/materials and progress
resume without duplication or reset.

31.2 Parallel/exclusive resources
Two workers may perform independent work simultaneously. One exclusive machine/vehicle/animal/person cannot be double-
booked.

31.3 Rework
Force a recoverable task failure. Confirm the appropriate follow-up task is created and original inputs/costs are not magically
restored.

31.4 Manager authority
Create a routine decision inside authority and confirm no owner escalation. Create an otherwise similar decision outside
threshold and confirm escalation.

31.5 Manager Activity
Confirm a logged decision links to the real policy/actor and any resulting transaction/task rather than a synthetic summary only.

31.6 Dynamic liquidity
Change payroll, project commitment or debt due state and confirm protected/deployable cash read models update without
moving actual cash.

31.7 Line of Credit
Confirm undrawn facility availability creates neither cash nor liability. Draw and repay while validating transaction/liability
changes.

31.8 Supplier landed cost
Consolidate multiple products into one shipment and confirm freight allocation affects landed cost according to configured burden
rule.

31.9 Share dilution
Issue new shares and verify every holder's percentage updates correctly. Preview and post-close state must match.

31.10 Corporate-cash boundary
A minority shareholder cannot directly spend company treasury on a personal transaction.

31.11 Railway staged operation
Open Segment 1 while Segment 2 remains unbuilt. Confirm routing, traffic, revenue and maintenance recognize only completed
operational track.

31.12 Rail-rights authority
Create a physical track connection without interchange/trackage-rights Agreement. Confirm trains cannot legally use the foreign
railway beyond permitted movement.

31.13 Bottleneck diagnosis
Create a yard scheduling constraint and confirm an operating/siding solution can reduce delay without requiring a locomotive
purchase.

31.14 Government land
Route a proposed segment across mixed private/public land. Confirm land/right workflow and cost differ by legal status.




                                                  32

## TECH-XIV — original PDF page 142 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

31.15 Historical event
Advance across Nov. 18, 1883. Confirm Standard Railway Time event applies once, changes relevant timetable/time regime and
persists correctly through save/load.

31.16 General Store queue
Create customer demand while one clerk is doing restocking/bookkeeping. Confirm customer service consumes labor and other
work waits or queues.

31.17 Barter
Execute a mixed goods + cash + receivable sale. Confirm physical goods move, cash changes only by cash component,
receivable changes correctly and ledger valuation remains auditable.

31.18 Telephone
A business in 1890 without a network connection cannot place a telephone call merely because the date is late enough. A
connected business can.

31.19 Corporate sale
A player holding 2% cannot unilaterally transfer 100% of shares/assets. Confirm required governance approvals and actual
proceeds calculation.

31.20 Off-map case identity
Send a detective agent off-map on a Case, save/load and return. Confirm same Person/Case identity, expenses, injuries and
outcome persist.





                                                  33

## TECH-XIV — original PDF page 143 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXXII - PERFORMANCE GUIDANCE

  • TaskInstances should be event/schedule driven, not frame-polled globally.
  • ManagerActivity logging should be append/event based with bounded aggregation for UI.
  • ActionCandidates should update when relevant source state changes; avoid scanning the whole world each frame.
  • Long-run railway traffic and off-map detective work may use multi-resolution simulation while preserving identity and
    authoritative commitments.
  • Historical events are sparse scheduled events and should not require daily full-table scans if the world-time scheduler can
   dispatch the next effective event.
  • Cap-table percentages should be derived from holdings rather than stored redundantly on every holder unless
    profiling/serialization strategy requires cached values.





                                                  34

## TECH-XIV — original PDF page 144 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXXIII - PRODUCTION PRIORITY & STATUS

Revision II does not authorize simultaneous implementation of every newly validated mechanic. Suggested dependency order
after repository audit and the completed architecture-planning phases:
  1. stable IDs / relationship-safe persistence and canonical authority migrations;
  2. shared Task/Work execution foundations;
  3. dynamic liquidity/credit facility and transaction correctness;
  4. manager authority + Manager Activity + Action Discovery read models;
  5. project/contractor execution deepening;
  6. corporate cap table/governance foundations before player-owned railway corporations;
  7. railway corridor/segment/right/economic-read-model foundations;
  8. Historical Event / dated-law integration;
  9. retail queue + barter + telephone public-access extensions;
 10. later saloon/gambling/vice and reward/private-detective business depth after shared foundations exist.
Implementation status remains repository-dependent. None of the above is Production Ready by documentation alone.





                                                  35

## TECH-XIV — original PDF page 145 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXXIV - TECHNICAL RESEARCH HOLDS

Repository holds from Revision I remain active.
New design/data holds:
  • exact task granularity by business and which micro-actions should remain presentation only;
  • exact contractor payment/change-order structures by project type;
  • precise historical legal details for 1880s Dakota rail crossing/interchange procedure;
  • exact railway tax applicability by company history;
  • exact historical saloon gambling game/rate/enforcement data;
  • prostitution business implementation scope and health/legal detail;
  • bounty/private-detective legal authority/compensation by jurisdiction;
  • early telephone public-call/message pricing;
  • exact barter category prevalence and valuation practices;
  • corporate charter powers for specific authored railway companies;
  • performance budgets for large TaskInstance/ManagerDecisionEvent histories.





                                                  36

## TECH-XIV — original PDF page 146 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

PART XXXV - SUPERSESSION MAP

Revision II supersedes or deepens the following Revision I interpretations:
  • Revision I workload guidance is deepened with persistent TaskDefinition/TaskInstance execution.
  • Revision I shared business administration is deepened with Manager Activity and action-discovery read models.
  • Revision I reserves are deepened by dynamic protected-liquidity calculation; no fixed reserve field should be interpreted as
    universal truth.
  • Revision I Financial Obligation is extended with CreditFacility and BondIssue distinctions.
  • Revision I rail profile is substantially deepened with Corridor, Segment, economic-node, right, capacity and
    financeability/viability models.
  • Revision I dated legal regime is deepened with Historical Event authority and railway-specific dated rules.
  • Revision I General Store profile is deepened with clerk-mediated service/queue workload and barter consideration.
  • Revision I communications profile is deepened with subscriber/public-access/message labor.
  • Future vice/business implementation must follow Canon XIX's historical-inclusion rule; illegality is modeled through
   LegalRegime/Enforcement rather than used as a technical reason to forbid the system.





                                                  37

## TECH-XIV — original PDF page 147 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

APPENDIX II-A - CANON XIX TO TECHNICAL II TRACEABILITY

CAN XIX High Action Visibility -> TECH II 4, 5, 30
CAN XIX Task Granularity -> TECH II 2
CAN XIX Manager Activity -> TECH II 6
CAN XIX Dynamic Liquidity / LOC -> TECH II 8-9
CAN XIX Item-Level Supplier Economics -> TECH II 10
CAN XIX Corporate Equity / Dilution -> TECH II 12-13
CAN XIX Railway Capital & Staging -> TECH II 14-18
CAN XIX Government / Historical Events -> TECH II 19-21
CAN XIX General Store Clerk Labor / Barter -> TECH II 22-23
CAN XIX Telephone Hub -> TECH II 24
CAN XIX Saloon / Gambling / Vice -> TECH II 25
CAN XIX Reward / Detective Agency -> TECH II 26
CAN XIX Townsite / Subsidiary -> TECH II 27
CAN XIX Corporate Sale / Merger -> TECH II 28





                                                  38

## TECH-XIV — original PDF page 148 of 170

                         LAND & LEDGERS / REVISION II / TECHNICAL IMPLEMENTATION BIBLE

APPENDIX II-B - REVISION II IMPLEMENTATION LOCK SUMMARY

  • Persistent scene-independent TaskInstances are the preferred authority for economically meaningful small work.
  • Delegation must be auditable through real actor/authority/decision events.
  • Action discovery is a read model over real state, not a quest generator.
  • Fast-forward must preserve underlying causal simulation and stop on material exceptions.
  • Dynamic liquidity is derived; only cash is money.
  • Undrawn credit availability is neither cash nor debt.
  • Corporate cap table, governance and treasury are first-class authorities before deep stock-company gameplay.
  • Railway planned corridor, legal rights, completed track and operating capacity must remain separate.
  • Financeability and Economic Viability require separate read models.
  • Historical events and dated law belong in centralized authorities, not scattered date checks.
  • Retail customer service consumes real labor; barter moves real goods.
  • Telephone service requires real connection and may create clerk work.
  • Saloon gambling, vice and detective systems must reuse shared entities rather than isolated minigames.
  • Corporate sale/merger must preserve debt, equity, governance, contracts, employees and control truth.
END OF REVISION II INTEGRATION





                                                  39

## TECH-XIV — original PDF page 149 of 170

LAND & LEDGERS

TECHNICAL IMPLEMENTATION BIBLE

Unity Architecture, Data, Systems & Production Reference


REVISION I

Initial split from Canonical Project Bible Revision XII Canon compatibility target: Canonical Project Bible Revision XIII

DOCUMENT CONTROL

Studio: Deadscripts Interactive Project: Land & Ledgers Document authority: Technical implementation planning
reference Revision date: August 29, 2026 Source baseline: Technical material separated from Canonical Project Bible
Revision XII Canon compatibility: Revision XIII Status: Active technical planning reference - repository audit required
before implementation claims

PURPOSE

  This document answers HOW the Canon should be implemented and maintained in Unity. It translates Canon
  requirements into technical authorities, persistent data, relationships, system responsibilities, cadence, scene
  representation, validation and production sequencing. It is deliberately separate from historical research and
  game-mechanic justification.

AUTHORITY MODEL

The Canon defines intended behavior. This Technical Bible defines the preferred implementation architecture. The
current Unity repository defines what actually exists. No paragraph in this document should be treated as proof that a
particular class, manager, prefab, scene or serialized field already exists. Before changing code, inspect the repository
and reconcile the technical plan with implemented reality.

When the three authorities disagree:

 • Canon vs Technical Bible: Canon wins on intended player-facing behavior and historical/gameplay rules.

 • Technical Bible vs repository: audit first. Preserve stronger working systems where they satisfy the Canon.

 • Repository vs Canon: record the divergence, determine whether it is temporary, intentional or a defect, then decide
   whether code or Canon changes.

 • Technical constraint vs Canon: raise a design decision; never silently redefine the game through code convenience.

TECHNICAL WRITING RULE

Technical sections should cite a Canon authority, for example CAN-12.6H, then describe implementation. Do not
duplicate long historical arguments. Stable data should be separated from runtime manifestations. Avoid one-off
bespoke systems when a shared authority can serve multiple industries without erasing their distinct behavior.

CONTENTS

I. Technical Authority & Governance II. Repository Baseline & Audit Discipline III. Core Simulation Architecture IV. World
Representation, Time & Simulation Resolution V. Core Entity & Relationship Models VI. Shared Simulation Systems VII.
UI Implementation Architecture VIII. Persistence, Save Compatibility & Determinism IX. Simulation Cadence &
Performance X. Testing, Validation & Regression XI. Production Roadmap & Implementation Status Appendix A.
Canon-to-Technical Traceability Appendix B. Implementation Status Legend Appendix C. Repository Audit Checklist
Appendix D. Technical Research Holds





                                                                                               TECHNICAL IMPLEMENTATION BIBLE 1

## TECH-XIV — original PDF page 150 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART I


 Technical Authority & Governance

 1.1 Canon compatibility

 Every major technical system should name the Canon sections it implements. The technical document can recommend
 internal abstractions, but it cannot substitute a different mechanic. For example, CAN-9.3A requires important local
 freight to obey physical travel. A valid implementation may use multiple simulation resolutions, but it cannot resolve
 delivery at order time merely because the destination is off camera.

 1.2 Repository-first implementation

 Before a coding pass, establish the current repository baseline: relevant assemblies, scenes, prefabs,
 ScriptableObjects/data assets, runtime managers/services, persistence paths, UI controllers, tests and known
 production constraints. Record what already works, what is prototype only and what materially diverges from Canon.
 Prefer targeted extension over replacement when the existing system already satisfies the required behavior.

 1.3 Shared-authority rule

 When several mechanics are structurally the same, prefer one shared authority with specialized data. Examples
 include work orders, agreements, ownership links, employment links, residential occupancy, assets, inventory
 locations, financial obligations and scheduled world jobs. A blacksmith repair and a mill machinery repair may use the
 same work-order lifecycle while requiring different capability, location, material and failure data.

 1.4 Data / manifestation separation

 Persistent simulation state should not depend on a particular scene object remaining alive. Scene objects represent
 authoritative simulation entities when locally manifested. Destroying or unloading a visual GameObject must not erase
 the person, shipment, business, asset or household it represents. Conversely, spawning a decorative object must not
 create economic reality unless the simulation explicitly creates the corresponding entity.





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 2

## TECH-XIV — original PDF page 151 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART II


 Repository Baseline & Audit Discipline

 2.1 Baseline status

 Revision I intentionally does not claim a current Unity class inventory. The Canon has advanced materially since the
 last implementation planning pass. The first serious Land & Ledgers coding session after this split should therefore
 perform a read-only repository audit before architecture changes.

 2.2 Required audit outputs

 The repository audit should produce:

  • Unity version, package baseline and assembly layout.

  • Startup/bootstrap flow and authoritative world-state owner.

  • Time/calendar authority and save/load entry points.

  • Terrain, grid, road, parcel, building and pathing authorities.

  • Person/household/business models and current identity/ID strategy.

  • Business management UI and existing shared-shell behavior.

  • Inventory, supplier, transaction, logistics and freight paths.

  • Property/acquisition/loan workflow implementations.

  • Existing AI/NPC decision logic.

  • Current tests, editor tools and debug visualizations.

  • Known performance constraints and expensive update loops.

  • A divergence table mapping implemented behavior against Canon Revision XIII.

 2.3 Pre-edit ledger

 For substantial implementation work, record the files expected to change, their pre-edit purpose and any authority
 they currently own. Avoid broad rewrites until the existing ownership graph is understood. When replacing an
 authority, migrate all call sites and persistence dependencies deliberately rather than leaving two partially active
 sources of truth.





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 3

## TECH-XIV — original PDF page 152 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART III


 Core Simulation Architecture

 3.1 Canon authorities implemented

 Primary Canon references: CAN-1.1B Authority Truth, CAN-1.2A Physical Truth, CAN-3.1 world structure, CAN-6 shared
 business simulation, CAN-9 logistics, CAN-12 people/households, CAN-18 finance and CAN-15 interface rules.

 3.2 Data-first authority

 The simulation should maintain persistent world entities in data structures independent of scene lifetime. Unity
 GameObjects, rendered terrain tiles and active agents are projections of that state. Systems may cache references for
 performance, but save/load and simulation logic should be able to reconstruct the local world from persistent identities
 and state.

 3.3 System responsibility boundaries

 Prefer explicit responsibilities rather than a single world god-object. A practical decomposition can include:

  • World/time authority - date, time speed, season, sunrise/sunset and scheduled-event dispatch.

  • Geography authority - parcels, roads, settlement nodes, terrain references and legal geography.

  • Entity authority - stable IDs and lookup for people, households, businesses, assets and agreements.

  • Economy authority - transactions, business cash, owner cash, pricing inputs and net-worth aggregation.

  • Labor authority - employment relationships, task availability, workload and absence.

  • Logistics authority - shipments, routes, vehicles, draft animals, loading/unloading and off-map legs.

  • Property authority - ownership, occupancy, tenancy, acquisition and project state.

  • Condition/work-order authority - asset wear, inspection, defects, maintenance and repair jobs.

  • Information authority - Actual, Recorded/Observed and Forecast state.

  • Opportunity & Pressure authority - event-fed economic signals and synthesis.

  • Persistence authority - versioned save state, reconstruction and migration. These may be separate services,
   managers or modules depending on the audited repository. The responsibility boundary matters more than a
   mandatory class name.

 3.4 Stable identity

 Any entity that can survive a scene unload, participate in a relationship, appear in a save file or be referenced by a
 long-running job needs a stable identity. IDs should not be regenerated on ordinary load. References should be
 validated during reconstruction, with missing targets reported rather than silently replaced by unrelated entities.

 3.5 Relationship-first model

 Many Canon systems are relationships, not ownership fields on one object. Employment connects a person to a
 business; residential occupancy connects a household/person to accommodation; kinship connects people; ownership
 connects an owner to property; an agreement connects counterparties; a loan connects borrower, lender, collateral
 and payment schedule. Model the relationship when the terms or lifecycle matter independently of either endpoint.

 3.6 Actual, Recorded and Forecast state

 Implement CAN-1.7C and CAN-15.3D by keeping three conceptual layers:

  • Actual State - simulation reality.

  • Recorded / Observed State - what a business/person legitimately knows through transactions, counts, inspections
   and reports.

  • Forecast State - derived estimates from evidence, history and expertise. A forecast never overwrites Actual State. A
   bookkeeping error changes recorded knowledge, not physical stock. Reconciliation can make recorded state catch up
    to a newly observed physical count.

 16.1 Architecture principles





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 4

## TECH-XIV — original PDF page 153 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE



 The source of truth should live in data, not scene objects. Manager-driven architecture should coordinate simulation,
 world generation, roads, plots, buildings, population, pathing, businesses, economy, time, save/load, and UI. Roads,
 plots, and anchors should live on a hidden two-meter grid. NPCs and carts should use grid-based pathing rather than
 NavMesh authority. Interiors can remain abstract in VP with door entry resolving to an inside state. The preferred
 structure for Opportunity & Pressure is one coordinator supported by specialized analyzers for inventory and trade,
 labor, housing and settlement, services, land and frontage, and freight or infrastructure. Avoid one giant polling script
 that repeatedly scans every entity every few seconds. Prefer event-driven counters, rolling summaries, and scheduled
 synthesis passes.

16.1A Tiled terrain, chunked vegetation, and visible transport agents

 The current data-first and hidden-grid rules remain authoritative, but the visual world should explicitly support tiled
 terrain, chunked vegetation and physically faithful agents. Terrain tiles render landform, hydrology, textures and
 scenic detail. Vegetation chunks let the game represent large forests cheaply at distance while activating real cuttable
 trees and richer dressing near active worksites. People, horses, carts and wagons should visually correspond to
 authoritative jobs and trips when they are locally simulated rather than spawning as unrelated decoration. The
 playable map should be materially larger than the player's normal field of view so simulation-resolution boundaries are
 not visible as world edges. Agents traveling away should continue along real routes beyond ordinary view before
 transitioning at a hidden boundary into coarse/off-map simulation; returning agents re-enter from the correct
 boundary with the same identity, cargo and state. Scene objects remain manifestations of authoritative data, not the
 source of truth.

16.1B Continuous clock, seasonal daylight, and job-specific usable work
windows

 The existing continuous 24-hour clock remains the universal temporal authority. Sunrise, sunset and usable daylight
 should vary by date and regional latitude instead of using one fixed daylight block. Other systems query that world
 state according to their real needs. Outdoor logging, field work, construction and travel can be visibility-dependent;
 fixed businesses can continue after dark when labor and period lighting support it; underground mines are structurally
 different again. Lighting does not erase every other constraint, and long scheduled hours do not imply equal
 productivity in every hour. Exact historical workday and shift patterns remain occupation-specific research/calibration
 rather than one universal rule.

 16.2 Visibility rules

 Backend-first simulation remains acceptable for calculations that do not require a local physical interaction, but
 Physical Truth is a hard exception. If a purchase, work action, service, pickup, delivery or other local event requires a
 person, vehicle, animal, inventory item or destination, the relevant prerequisites must actually be available and the
 agent must reach the required location before the event resolves. A scheduled shopper who never reaches the store
 does not complete a purchase. A wagon already away on another job cannot haul a second load. Commutes, shopping,
 deliveries and travel should therefore have authoritative departure, movement and arrival state even when distant
 movement uses reduced simulation resolution. Many analytical systems - preference, Opportunity & Pressure, pricing,
 newcomer profiles - can still be backend-first as long as their consequences remain visible and physically consistent.
 Hidden simulation that contradicts what the world shows should be treated as a defect, not an acceptable abstraction.

 16.3 Persistence and authored assets

 Save/load should preserve seed, date, plots, ownership, businesses, stock, households, people, jobs, debt, policy
 settings, delegated authority, merchandise allocations, operating schedules, recurring orders, reputation state, notices
 state, active local/off-map trip state, transport assignments, and player state. Opportunity & Pressure should also
 preserve active opportunities, active pressures, recent trend state, and rival-intent state where supported. On the
 authored-asset side, plot footprint authority and visual prefab scale authority remain separate. Buildings should not be
 silently rescaled to fit the grid. That rule protects both visual authenticity and implementation clarity.





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 5

## TECH-XIV — original PDF page 154 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART IV


 World Representation, Time & Simulation Resolution

 4.1 World layers

 The authoritative region should separate macro geography from visible rendering. Core layers are terrain/hydrology,
 road and route graph, parcel/legal geography, settlement nodes, building/site anchors, world entities and active visual
 manifestations. The exact Unity component layout is repository-dependent.

 4.2 Grid and pathing

 Revision XII carried a preferred hidden two-meter grid and grid-based pathing direction. Before implementation, audit
 the current terrain and navigation stack. If the existing system already provides deterministic parcel/road/path
 authority with equivalent behavior, do not replace it solely to match the wording. The non-negotiable behavior is that
 paths, parcels, building footprints and physically represented travel use a coherent authoritative geography.

 4.3 Multi-resolution agents

 Locally important people, horses, carts and wagons should be physically manifested when the player can observe or
 interact with them. Long journeys may transition to coarse simulation beyond a hidden boundary, but identity, cargo,
 assigned job, route and timing persist. On return, the same entity re-enters from a plausible boundary and resumes
 physical movement. The resolution transition must never duplicate cargo, free a busy wagon for another trip or allow a
 traveler to be in two locations.

 4.4 Continuous time

 Use one authoritative continuous clock. Systems query date, season, day of week and local solar/daylight state as
 needed. Do not give every business its own incompatible time source. Long-running work should store meaningful
 start/expected-completion state rather than relying only on a coroutine that disappears when an object unloads.

 4.5 Scene boundaries

 Scene loading/unloading must not be an economic event. Local representation can appear/disappear as areas
 activate, but simulation entities retain identity and state. Interiors can remain abstract where Canon permits; entering
 an abstract interior should still create a valid location state for the person and satisfy Physical Truth.





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 6

## TECH-XIV — original PDF page 155 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART V


 Core Entity & Relationship Models

 5.1 Person

 Implements CAN-12.1 and CAN-12.6. Minimum persistent direction: PersonId; identity/name; birth date; sex; parent
 references where known; spouse/relationship reference where applicable; current household membership;
 life/dependency state; health; employment links; skill/expertise/familiarity state where supported; current
 residence/location; migration/origin history; succession/estate relevance; fertility/pregnancy state where applicable.
 Exact schema follows repository audit.

 5.2 Household

 Implements CAN-12.2 and CAN-12.6A/H. Household is not a building. Persistent direction: HouseholdId; member links;
 shared economic state/reserves; obligations; domestic workload/pressure; primary domestic anchor; current
 accommodation relationships; relevant household credit and consumption memory. Do not require a HouseId field as
 the identity of the household.

 5.3 Kinship

 Store direct relationships sufficient to derive useful kin: parent-child and spouse/partner links where known. Derived
 sibling, grandparent, aunt/uncle and other relationships should not require redundant lists unless profiling proves a
 need. Support lightweight off-map kin records when the full person is not locally simulated.

 5.4 Residential Occupancy

 Implements CAN-12.6H-L. Use a relationship object capable of representing: OccupancyId; person/household endpoint;
 accommodation-space endpoint when physical; arrangement type; start date; end/expected duration;
 rent/board/payment terms; host household or employer relationship where relevant; security/precarity state;
 termination reason. Two households can reference the same accommodation without merging. One household can
 have person-level secondary residence assignments.

 5.5 Accommodation Space / Building / Parcel

 Accommodation Space stores economically meaningful shelter capability and belongs to a Building or other physical
 shelter representation. Building stores physical structure and functional spaces. Parcel stores legal geography,
 ownership/transfer status, frontage/access and site facts. Do not collapse these layers merely because one early
 prefab contains one household.

 5.6 Dependency Resolution Case

 Implements CAN-12.6C. When the last responsible adult disappears, create a persistent case rather than immediately
 rewriting household membership. Case direction: dependent IDs, sibling group, current shelter, known kin candidates,
 estate/guardian references, temporary care state, search/resolution status and final placement. Care placement must
 not transfer inherited ownership.

 5.7 Business

 Implements CAN-6. Shared business state should contain identity, owner, managing authority, parcel/site, business
 cash, debt exposure, workers/roles, operating schedule, workload, policies, reputation, inventory/storage interfaces,
 assets/condition, agreements and business-specific extension data. Specialized industries extend this shared model
 rather than duplicating cash, employment, ownership and policy infrastructure.

 5.8 Asset

 Persistent asset identity should support class/type, owner/business, location, usable state, condition domains, known
 defects, maintenance history, assigned work/trip and collateral/valuation relevance where needed. A wagon, boiler,
 saw, windmill or building system can share common lifecycle fields while carrying class-specific condition components.

 5.9 Agreement / Contract

 Implements CAN-9.2/9.2A and future counterparty work. Store counterparties, subject goods/service, quantity or
 share, cadence, pricing method, hauling responsibility, priority/reserve rules, start/end/renewal, performance history,




                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 7

## TECH-XIV — original PDF page 156 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE



 breach state and termination. Actual shipments/work orders remain execution objects linked to the standing
 agreement.

 5.10 Shipment / Trip

 Persistent direction: ShipmentId/TripId, source, destination, cargo lots, loading status, route/legs, assigned
 carrier/vehicle/team, driver/labor, departure, current leg/progress, arrival/unloading, fees/costs, failure/delay state.
 Destination inventory changes on physical arrival or a justified completed off-map leg, not at order creation.

 5.11 Financial obligation

 Loans, rent arrears, supplier credit and other scheduled obligations should share useful payment concepts but retain
 domain-specific law. Persistent fields may include creditor/debtor, principal/balance, rate/price terms, schedule, due
 amounts, collateral/security references, delinquency state, workout/restructure status and enforcement path.





                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 8

## TECH-XIV — original PDF page 157 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART VI


 Shared Simulation Systems

 6.1 Business administration

 Implement owner/manager policy as explicit state. Ordinary workers execute tasks but do not silently change
 merchandise allocation, pricing strategy, supplier policy, customer credit, operating hours or capital use. Delegation
 should be permission-based with approval thresholds, not an on/off autoplay switch.

 6.2 Workload-based labor

 Business-specific activity generates work demand. Employees contribute compatible labor availability constrained by
 schedule, health, travel, skill and occupation. Avoid one-employee-per-task architecture. A small store may let one
 clerk cover several task families until simultaneous workload exceeds practical capacity.

 6.3 Asset condition and maintenance

 Implements CAN-6.5. Internal condition may use normalized numeric values for tunability, but values are per
 component domain and asset class, not one universal durability bar. Wear inputs should include use, load,
 environment, age, storage, operating practice and unresolved defects. Known defects are separate from underlying
 actual condition. Inspection changes knowledge; maintenance changes the asset.

 6.4 Shared work-order authority

 A WorkOrder should carry customer/owning business, target asset/component, reported symptom, diagnosed defect
 where known, required capability, location, urgency, materials/parts, labor estimate, queue priority, assigned
 provider/worker, status, quoted/agreed terms, completion and payment. The same lifecycle can serve blacksmith
 work, wagon repair, building maintenance, machinery service and later utility/rail maintenance.

 6.5 Bookkeeping and reconciliation

 Implement actual transactions separately from ledger completeness. The transaction layer records authoritative
 cash/inventory/receivable events. Recordkeeping determines whether the business has a correct entry and current
 reconciled count. Reconciliation jobs compare recorded expectation to physical/actual observation and create
 explainable discrepancies. Do not randomize a known invoice into a range.

 6.6 Expertise and information

 Experience events should be meaningful, domain-tagged and subject to diminishing learning value. Expertise affects
 forecasts, diagnosis, warning quality and decision support. It must not directly multiply sales, production, durability or
 negotiation outcomes. Specialist employees can provide organizational information quality without making the owner
 personally expert.

 6.7 Opportunity & Pressure

 Use event-fed counters or ledgers for relevant signals: transactions, failed purchases, stockouts, missed orders, failed
 hires, vacancies, housing mismatch, boarding saturation, route delay, business closure and similar events. A
 scheduled synthesis layer converts recent history into opportunity/pressure objects with cause, severity, trend,
 affected node/system, evidence and player-facing explanation. Avoid continuous brute-force scanning of every entity
 unless profiling proves it necessary.

 6.8 Supply and inventory

 Inventory lives at physical locations with capacity pools. Track Total, Allocated/Reserved and Occupied capacity where
 Canon requires it. Routine stocking changes Occupied state inside existing allocation; administrative policy changes
 allocation. Supplier orders create inbound obligations/shipments rather than instantly mutating destination stock.

 6.9 Logistics

 Validate source stock, reserve policy, carrier capacity, vehicle/team availability, loading, route, labor and destination
 receiving capacity. Carrier choice can be self-haul, hired freight, supplier delivery or buyer pickup. Movement
 resolution must preserve Physical Truth. Portfolio summaries may aggregate data for UI but must not create a global
 warehouse.




                                                                                                  TECHNICAL IMPLEMENTATION BIBLE 9

## TECH-XIV — original PDF page 158 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


 6.10 Demographic lifecycle

 Immediate dated events: birth, death, marriage, household split/merge, displacement, eviction/removal, migration
 arrival/departure and other explicit state changes. Low-frequency synthesis is appropriate for conception evaluation,
 life-stage review, relationship formation and similar slow processes. Canon Revision XII preferred approximately
 monthly demographic evaluation; retain that as a starting technical cadence subject to profiling and design calibration.

 6.11 Fertility

 Evaluate specific eligible couples, not households. Inputs can include age-specific fertility curve, pregnancy state,
 spacing/recovery, health, co-residence and calibrated variation. Conception schedules a pregnancy/birth event. Birth
 creates a persistent Person and updates kinship/household relationships. Aggregate test output must be calibrated
 against Canon historical envelopes rather than hard-coding a desired child count per household.

 6.12 Housing and migration

 Migration search should query actual accommodation offers and household requirements. Represent boarding
 beds/rooms, rental units, employer lodging, kin hosting, temporary shelter and no-stable-shelter states distinctly.
 Effective local labor supply depends on workers who can practically reach or secure acceptable accommodation, not
 merely a regional worker count.

 6.13 Health, injury and continuity

 Health events affect person availability and therefore role coverage/workload. Occupation-specific injury pools can
 produce absence, treatment need, lasting impairment or death. Business continuity then derives from available
 authority and qualified role coverage rather than a business-wide health modifier.

 6.14 Property and acquisition

 Property state and seller motive belong to persistent data. Acquisition workflows should assemble diligence results
 from title, condition, books, staffing, supply and demand sources rather than manufacturing one purchase score.
 Closing can fail if financing, title or required conditions fail. Inherited baggage remains attached after transfer.

 6.15 Finance and distress

 Owner cash and business cash stay separate. Loans and credit must create corresponding liabilities. Distress should
 progress through due state, delinquency, workout/restructure, default, enforcement and disposition according to the
 applicable dated legal regime. Commercial Ruin is a portfolio-level evaluation of remaining recovery capacity, not a
 fixed negative-number trigger.

 6.16 Rival / NPC economic decision architecture

 NPC owners and managers use the same policy levers as the player. A decision pass should evaluate current cash,
 reserves, obligations, expected return, uncertainty, risk tolerance, commitments, relationships, expertise, time horizon
 and feasible opportunity set. Avoid hidden NPC-only production or price bonuses. Different owners can hold different
 policy preferences and information while remaining subject to the same economic rules.

 6.17 General Store implementation profile

 Implements CAN-7.1. The General Store should extend the shared business model with merchandise-category policy
 and multiple capacity pools. Product definitions should provide size/capacity consumption, valid storage pools,
 perishability where relevant, supplier eligibility, seasonal demand metadata and price/cost history hooks. Store state
 should maintain display allocation, storage allocation, bulk/yard allocation, stock targets, reorder rules,
 preferred/backup supplier references, protected cash reserve, seasonal profile and customer-credit policy. Sale
 execution consumes real stock and creates a transaction; replenishment creates an order and later shipment rather
 than directly replacing the sold unit.

 Reallocation is administrative state. A sale frees Occupied capacity but does not automatically release Allocated
 capacity. Routine staff can refill from backstock when workload permits. A manager may alter allocation only within
 delegated authority. This distinction should be visible in both data and UI so the player can tell the difference between
 empty display space reserved for an existing line and genuinely unallocated capacity.

 6.18 Repair / fabrication service implementation profile





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 10

## TECH-XIV — original PDF page 159 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE



 Implements CAN-7.2A-H. Repair providers expose capability tags derived from workers, tools/facilities and materials. A
 shop may combine smithing, horseshoeing, wagon/wheel, harness/tack and later machine-work capability instead of
 being hard-coded to one class. Customer requests become WorkOrders. Diagnosis may update the diagnosed
 component and required parts without rewriting the original reported symptom. Rush/priority changes queue order but
 should consume real provider capacity.

 On-site jobs create a service-trip requirement for the worker/tools. Shop jobs require the asset or removable
 component to reach the shop. A disabled wagon can use a temporary field-repair branch that restores limited mobility,
 then a proper shop job. Repair history should update the target Asset and feed future diligence/resale.

 6.19 Rural production implementation profile

 Implements CAN-7.4A-R. Rural production should attach operating state to fields/land groups and livestock cohorts
 rather than one farm-wide output number. Field state needs area, crop/use, moisture/water context, crop condition,
 current operation, progress, access and ownership. Seasonal work creates jobs with deadlines/windows and consumes
 compatible labor, draft animals and machinery. Realized yield is bounded by biological potential and completed work.

 Livestock should use economically meaningful cohorts such as breeding animals, young stock and market animals
 rather than simulating every animal when cohort resolution is sufficient. Cohorts still require location/range,
 forage/water access, condition, purchases/sales and mortality. Hay/forage inventory links field production to winter
 feed consumption. Fencing, wells, pumps and windmills are physical assets with condition and spatial effect. Custom
 agricultural work uses the same agreement/work-order/trip infrastructure as other services.

 6.20 Mining implementation profile

 Implements CAN-21.1-21.7R. A mine extends the shared business model with claim/property rights, deposit/geological
 information, development state, work fronts, extraction/treatment chain, stockpiles, specialized assets and
 management hierarchy. Geological truth and known geological information must remain separate. Assays and
 development improve information; they do not mutate the underlying deposit.

 Work fronts can be represented at a coarse operational level with accessible material, confidence, support/water
 exposure, current allocation and development/production posture. Extracted material enters ore classes/physical lots
 and then treatment/stockpile/logistics. Custom mills and assay offices should be separate businesses using the shared
 Agreement and WorkOrder patterns. Mine shutdown should retain preservation burdens such as pumping, inspection
 or security where relevant instead of simply setting production to zero.

 6.21 Market, pricing and shock propagation implementation profile

 Implements CAN-9.6 and CAN-14. Prices should be derived from supplier quotes, landed cost, local availability, buyer
 willingness, substitution and market reach rather than direct event multipliers. Shock objects should first affect the
 physical/economic driver: crop yield, livestock availability, route capacity, worker availability, inventory or outside
 supply. Pricing systems then observe the changed state.

 Keep shock geographic scope explicit: supplier/business, local/settlement or regional/off-map. Buffers such as stock,
 alternative suppliers, spare route capacity, substitution and reserves reduce consequence through real state. Existing
 inventory cost basis should remain distinct from replacement cost so merchants can hold cheap old stock while new
 quotes rise.

 6.22 Fire and physical adversity implementation profile

 Implements CAN-3.2E-F and CAN-6.5H. Fire risk should arise from ignition sources, building/material characteristics,
 heating/industrial equipment, known defects, weather/wind, spacing and available suppression. Do not represent fire
 as a town-wide random percentage loss. A fire incident should identify origin, affected structures/contents, spread
 candidates, response resources and resulting damage.

 Destroyed or unusable accommodation should invalidate occupancy without deleting households. Destroyed business
 assets should create condition, inventory, continuity, insurance/finance and employment consequences. Organized
 fire-service progression can add response resources and coverage, but must remain spatial and capacity-limited.

 6.23 Rail and corridor implementation profile

 Implements CAN-20 and CAN-2.4. Separate regional common-carrier rail, private/industrial rail and internal mine track.
 Regional rail availability follows chronology and corporate corridor decisions. Local rail projects should be
 corridor/project entities with rights, route, facilities, financing, construction state and service status rather than
 freehand track placement.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 11

## TECH-XIV — original PDF page 160 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE



 A depot or siding is a physical logistics node. When service begins, shipment routing can shift long-haul legs to rail and
 retain wagon drayage/feeder legs. Bypass decisions affect node attractiveness through access and freight economics,
 not a global town bonus. Rail-company ownership, if implemented later, should extend finance, project, asset, labor
 and logistics systems rather than create a separate strategy-game economy.

 6.24 Telegraph, telephone and information-network implementation
 profile

 Implements CAN-22. Communications networks change information delay and contact capability, not goods
 movement. Represent access as a dated, spatial/network relationship: indirect access through courier to a remote
 node, local telegraph office/private line, telephone subscriber connection, exchange/mutual network and later
 extensions. Messages or information queries may use abstraction, but the network state should determine whether
 rapid communication is available.

 Telephone and telegraph are parallel systems with different infrastructure and commercial models. Do not convert one
 into a linear upgrade of the other. Business UI can expose faster quote/market/contact information when a legitimate
 connection exists.

 6.25 Electricity and utility-network implementation profile

 Implements CAN-23 and civic infrastructure sections. Utilities require source/generation, distribution network,
 capacity, connection and condition. A company/franchise record does not imply physical service. Buildings/businesses
 receive service only when connected to a network segment with adequate capacity and operating source.

 Electric generation can include private industrial generation and later public-facing utility service. Water/sewer should
 likewise separate source/storage/mains/hydrants/property connection or disposal/sewer connection. Network
 construction is a capital project with route, materials, labor and finance. Failures should occur at components/nodes
 and affect the service area they actually supply.

 6.26 Executive and portfolio implementation profile

 Implements CAN-19. Portfolio management should aggregate read models while preserving business-level cash,
 assets and decisions. Delegated managers receive permission bands for hiring, pricing, supplier selection,
 maintenance spending, credit and routine operations. Major debt, sale, acquisition, capital project or exceptional
 contract can require owner approval.

 Auditors/inspectors should query existing records, physical counts, contracts, staffing and condition to produce
 reports. They should not be magic omniscience APIs. Portfolio staff can improve reporting throughput and oversight
 while remaining real employees with employment, cost and continuity risk.

 6.27 Dated legal-regime implementation

 Implements CAN-1.6B-C, CAN-12.6C/D and CAN-18.7. Use a small dated legal-regime service/table for rules that
 materially affect gameplay: territorial versus state period, interest ceiling regime, bankruptcy availability,
 mortgage/chattel enforcement stages, redemption, protected property, municipal debt capacity and later researched
 guardianship/tenancy rules. Domain systems query the current regime when creating or enforcing an obligation.

 Do not encode legal chronology as scattered date checks across unrelated scripts. Centralize validated dates/rules so
 later research can correct one authority. The legal service should return gameplay-relevant permissions/parameters,
 not require simulation of every filing or court procedure.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 12

## TECH-XIV — original PDF page 161 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART VII


 UI Implementation Architecture

 7.1 UI boundary

 The Canon defines what the player should understand. UI implementation should consume authoritative read models
 rather than directly own simulation truth. UI writes should dispatch validated commands/policy changes to the
 relevant authority, not mutate unrelated fields inside view controllers.

 7.2 Shared business shell

 Reuse a common management shell for identity, cash, employees, operating hours, policy, condition, logistics,
 agreements and reports. Industry panels extend the shell. Do not copy an entire independent UI stack for every
 business.

 7.3 Workflow controllers

 Acquisition, loan, large contract, major project and similar multi-step actions should have dedicated workflow state
 independent of transient screen navigation. Closing a UI panel must not cancel a legally/economically active process
 unless the player explicitly cancels it.

 7.4 Read models

 Recommended read-model families:

  • Business summary - cash, revenue/cost, employees, operating status, alerts.

  • Inventory/capacity - allocation, occupied, stock targets, inbound/outbound.

  • Logistics - trips, assigned assets, delay, route and carrier.

  • Property - ownership, occupancy, rent, condition, debt and project state.

  • Household - membership, income/reserves, obligations, accommodation and pressure.

  • Information - Actual/Known values, Forecast/Estimated ranges and confidence.

  • Opportunity & Pressure - cause, trend, urgency, affected systems and actions available.

 7.5 Alerts

 Alerts should be generated from authoritative system conditions and de-duplicated/coalesced by cause. Avoid one
 alert per transaction. The UI should show what changed, why it matters, deadline/urgency where relevant and a route
 to the management surface that can respond.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 13

## TECH-XIV — original PDF page 162 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART VIII


 Persistence, Save Compatibility & Determinism

 8.1 Save authority

 Preserve all economically persistent entities and relationships needed to resume the same world. Minimum categories
 include seed/date/time, parcels and ownership, buildings/spaces, businesses, stock, people, households, kinship,
 occupancy, employment, assets/condition, debt, policy/delegation, agreements, trips/shipments, active projects,
 notices/opportunities/pressures and player portfolio state.

 8.2 Stable IDs and references

 Save references by stable ID, not scene instance pointer. During load, reconstruct entities, then resolve
 cross-references in a controlled pass. Missing references should produce diagnostics and domain-appropriate fallback
 only where safe. Never silently point an orphaned reference at a random entity.

 8.3 Versioning

 Every save schema should carry a version. When a field or relationship model changes, provide explicit migration or
 mark the change as save-breaking during development. The Technical Bible should record save-affecting architectural
 changes so production does not discover late that a major update invalidates long campaign saves.

 8.4 In-progress state

 Persist in-progress work that has economic consequence: trips, shipments, pregnancies, repair/work orders,
 construction projects, loan workflows where binding, rent/credit arrears, active negotiations/contracts where relevant,
 business closure/foreclosure stages and temporary dependency cases.

 8.5 Determinism

 Deterministic seed handling is valuable for world generation, debugging and regression. Do not assume every runtime
 outcome must be perfectly deterministic if asynchronous presentation or floating simulation makes that impractical,
 but repeated tests should be able to recreate defined scenarios and control randomness through seeded authorities
 where possible.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 14

## TECH-XIV — original PDF page 163 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART IX


 Simulation Cadence & Performance

 9.1 Cadence doctrine

 Do not run every economic system every frame. Use the slowest cadence that preserves correct causal behavior and
 player feedback.

 9.2 Suggested starting cadence

 Immediate/event-driven: transactions, inventory movement, departure/arrival, damage/failure, birth/death, job loss,
 ownership transfer, payment, fire ignition/major incident, contract breach and player policy changes. Hourly or
 work-slice: active local movement, task/work progression, production where continuous resolution is needed, asset
 use. Daily: business rollups, payroll/due checks as appropriate, household consumption planning summaries, basic
 opportunity counters. Weekly: broader market/opportunity synthesis, staffing/retention review, settlement pressure
 summaries where daily precision is unnecessary. Monthly: demographic conception/lifecycle review, rent cycle where
 historically calibrated, lender/portfolio reporting, slow maintenance inspection schedules, migration trend synthesis.
 Seasonal/annual: agricultural state transitions, long-range historical calibration, tax/legal events where applicable,
 campaign summaries. These are starting technical targets, not historical claims. Profile and revise without changing
 Canon-visible behavior.

 9.3 Performance budgets

 Profile by active entities, manifested agents, route calculations, UI refresh cost, event volume and save size. Prefer
 dirty/event-driven recalculation over full-world scans. Cache derived summaries when inputs have not changed. Keep
 expensive historical/calibration analytics out of ordinary frame loops.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 15

## TECH-XIV — original PDF page 164 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART X


 Testing, Validation & Regression

 10.1 Test layers

 Unit tests validate calculations and state transitions. Integration tests validate interactions between authorities.
 Scenario tests validate Canon behavior in a controlled world. Long-run simulations validate aggregate
 economy/demography. Persistence tests validate save/load continuity. Visual/scene tests validate Physical Truth and
 authored representation.

 10.2 Physical Truth tests

 Examples:

  • A shopper who never reaches the store cannot complete the local purchase.

  • A wagon assigned to Trip A cannot simultaneously haul Trip B.

  • Destination stock does not increase before unloading/arrival.

  • A broken fixed machine cannot be repaired at a distant shop without a valid component/service movement path.

  • Scene unload/reload does not duplicate a person or cargo.

 10.3 Household / kinship tests

  • One adult can form a household without a conventional dwelling.

  • Two households can share one accommodation without merging finances.

  • Household identity persists through a move.

  • A child losing the last responsible adult creates a dependency case.

  • Kin search can find maternal relatives with a different surname.

  • Care placement does not transfer inherited property.

  • Save/load preserves parent/child/spouse and occupancy relationships.

 10.4 Demographic calibration

 Across many 1880-1905 runs, export births per woman, surviving children, first-marriage age, household-size
 distribution, one-person households, boarders, renters/owners, widowhood, splits/mergers, orphan/dependency cases,
 migration and total growth. Compare against Canon envelopes. Fix causal parameters rather than imposing hidden
 household-size caps or fertility bonuses.

 10.5 Business/economy integration tests

  • Supplier failure produces stock pressure through actual missing supply.

  • Store credit increases receivables and household purchasing capacity without creating cash.

  • Repair downtime reduces the capacity of the affected asset, not every business globally.

  • Business acquisition preserves inherited debt/condition/stock/reputation where applicable.

  • Borrowing cash creates an offsetting liability and does not inflate Net Worth.

  • Commercial Ruin does not trigger while a viable recovery foothold remains.

 10.6 Regression

 Every bug fix that exposed an authority conflict should receive a regression scenario if practical. High-risk regressions
 include duplicated stable IDs, teleported cargo, save/load relationship loss, UI directly mutating simulation state, scene
 unload deleting economic entities and two systems simultaneously owning the same field.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 16

## TECH-XIV — original PDF page 165 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


PART XI


 Production Roadmap & Implementation Status

 17.1 Phase bands and chronology envelope

 VP Core includes the General Store deep slice, deal-flow acquisition, reserve shopping, continuous-clock workday
 rhythm, notices, calendar visibility, business cash authority and Liquid Cash plus Net Worth HUD. Active VP Expansion
 includes boarding and settlement growth, workflow acquisition and loan UI, Business Reputation, recurring local
 orders, heating/fuel pressure, doctor labor-loss logic, survey/terrain groundwork, trip-based logistics and adversity
 proof. Post-VP deepens broader businesses, property markets, workload-based staffing, manager policies, rural chains,
 settlement-node logic and remote production. Rail Era begins regionally in the mid-1880s but local service remains a
 corporate/corridor outcome. Late Capital Era includes banks, mines, railway companies, utility investment, executive
 staff and portfolio administration after player wealth and delegation make them viable. Long-Term protects deeper
 utility operations and richer regional systems without implying immediate implementation priority. The normal
 historical campaign envelope is spring 1880 through 1905. This is a capability horizon, not a deadline requiring all
 networks to appear. Automobiles and gasoline tractors are rare/cutting-edge background at the far edge of the period,
 not core replacement systems. Widespread motor freight, tractor agriculture, universal rural electricity and
 automobile-oriented road development remain outside the intended normal campaign.

 17.2 Updated priority order

 The priority order for crossover systems remains: General Store survival and working-capital health; deal-flow
 acquisition and post-close integration; net worth and liquid cash clarity with portfolio authority; Business Reputation
 and local choice weighting; boarding and settlement infrastructure; doctor care and labor-loss handling; recurring local
 orders and local trade relationships; small sawmill and lumber- yard groundwork with owned-versus-hired hauling
 decisions; clearer Opportunity & Pressure sensing, escalation, and explanation; rival causality and continuity; and later
 rail threshold logic, telegraph follow-through, and electricity-threshold rollout. When identifying gaps, prefer fixing
 weak structures over adding isolated features. Typical missing structures include labor absorption, distress recovery,
 visible decline, district rise, settlement transition, supplier dependency, and seriousness or credibility. New mechanics
 should enter production when they clearly strengthen the current ownership loop, town readability, or consequence
 structure. A feature should usually exist to strengthen a structure, not decorate the backlog. Mechanics should also be
 designed around when they matter. Ask when a condition is first noticed, first painful, first profitable, and first
 impossible to ignore. Ask whether a mechanic belongs mainly to survival, growth, portfolio management, or
 institutional power. Prefer mechanics that transform across phases instead of staying static. The new late-game
 additions in this edition do not override that order. They exist to clarify the destination and help present a coherent
 long arc. The game should not sacrifice early and mid-game quality just to surface a bank, mine, or railway menu
 sooner than the town has earned it. Late-capital systems are destination-shaping reference, not implied production
 permission.

17.2A New implementation-minded priority additions

 In addition to the current crossover priorities, the project should now treat several implementation-minded foundations
 as active canon work. Survey and parcel visibility, tiled terrain groundwork, node-aware settlement growth,
 supply-agreement UI, trip-based logistics, transport assets inside the business shell, and freight-driven support burden
 are not optional polish systems. They are structural supports for the new regional direction and should be developed in
 ways that strengthen the ownership loop rather than floating as isolated technical experiments. Business-agency
 depth is also a structural priority: new business work should preserve a clear owner/manager decision layer, realistic
 multi-task staffing, meaningful operating policy, and player/NPC parity rather than adding businesses that mostly run
 themselves.

 REVISION V INTEGRATED

17.2B MVP adversity proof requirement

 MVP PROOF The ownership economy is not proven until meaningful adversity and recoverable distress are
 mechanically possible.

 Before the basic ownership economy is treated as proven, the game should demonstrate that rivals can exploit
 profitable openings; supply can genuinely tighten; production shocks can propagate; labor loss can weaken
 operations; businesses can experience liquidity stress; the player can permanently lose individual assets or
 opportunities; serious distress can be recovered from; and Commercial Ruin is mechanically possible.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 17

## TECH-XIV — original PDF page 166 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


 17.3 Codex implementation lane guidance

 Future Codex work should be split into four focused lanes: mining, logging/sawmills, business-to-business logistics, and
 Livery/Freight. Each lane should first ask targeted questions about the current scripts and implemented behavior
 before attempting implementation. Prompts should preserve the existing business UI and shared shell, avoid
 dependency hunting, and implement the smallest grounded version that strengthens the ownership loop without
 breaking current working systems. Part VI - Capital Command, Finance, Rail, Industry, and Utilities

REFERENCE APPENDICES

 Fast routing, lock checking, and implementation reference. The main body remains authoritative.

 11.1 Revision I interpretation

 The split itself does not authorize a broad rewrite. Before serious implementation, update this part from repository
 audit. Canon Revision XIII may continue to grow independently of this Technical Bible; only technical changes require a
 Technical revision.

 11.2 High-value shared foundations

 Current architectural priorities with strong crossover value: 1. Stable entity IDs and relationship-safe persistence. 2.
 Clear Actual / Recorded / Forecast information authority. 3. Shared asset condition + work-order infrastructure. 4.
 Household / accommodation / kinship relationship separation. 5. Agreement + shipment/trip distinction for B2B trade.
 6. Event-fed Opportunity & Pressure. 7. Shared business administration/delegation model. 8. Repository-grounded UI
 command/read-model separation.

 11.3 Implementation gate

 No system is Production Ready merely because it is described here. Production Ready requires implementation,
 relevant tests, save/load coverage where persistent, performance review and confirmed Canon behavior.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 18

## TECH-XIV — original PDF page 167 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


APPENDIX A


 Canon-to-Technical Traceability

 CAN-1.1B Authority Truth -> TECH 3.3, 5.7, 6.1, 7.1 - commands must originate from real
 ownership/management/contract authority. CAN-1.2A Physical Truth -> TECH 4.3, 6.9, 10.2 - local economic actions
 require real prerequisites and arrival. CAN-3.1 World structure -> TECH 4.1-4.5 - geography, route and manifestation
 layers. CAN-3.2 Opportunity & Pressure -> TECH 6.7, 9.2 - event-fed signal synthesis. CAN-5.3 Authored building scale
 -> TECH 4.1, persistence/asset guidance - do not distort authored structures to satisfy parcel logic. CAN-6.1 Shared
 business model -> TECH 5.7, 6.1, 7.2. CAN-6.5 Maintenance -> TECH 5.8, 6.3-6.4, 10.5. CAN-6.6 Records -> TECH 3.6,
 6.5, 7.4. CAN-9.2 Supply agreements -> TECH 5.9, 6.8-6.9. CAN-9.3A Trip-based logistics -> TECH 5.10, 6.9, 10.2.
 CAN-12.6 Kinship/households -> TECH 5.1-5.6, 6.10-6.12, 10.3-10.4. CAN-12.7 Tenancy -> TECH 5.4-5.5, 6.12, 8.1.
 CAN-15 UI / forecasts -> TECH 3.6, 7.1-7.5. CAN-18 Finance / distress -> TECH 5.11, 6.15, 10.5. CAN-19 Delegation ->
 TECH 6.1, 7.2. CAN-20/22/23 networks -> TECH 4 and system-specific future modules; network implementation
 remains repository/audit dependent.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 19

## TECH-XIV — original PDF page 168 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


APPENDIX B


 Implementation Status Legend

 NOT AUDITED - Technical Bible contains a target design but current repository state is unknown. DESIGN ONLY - Canon
 behavior exists; technical architecture not yet committed. PLANNED - Technical approach is specified but not
 implemented. PROTOTYPE - Exists for proof but may lack persistence, tests, scale or Canon completeness. PARTIAL -
 Production path exists but important Canon behavior remains absent. IMPLEMENTED - Main behavior exists in the
 runtime. DIVERGES FROM CANON - Implemented behavior is intentionally or accidentally different and requires
 decision. NEEDS REFACTOR - Function exists but authority/performance/maintenance risk warrants redesign. TESTED -
 Relevant automated/manual validation exists. PRODUCTION READY - Implemented, tested, persistent where required,
 performant and Canon-aligned.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 20

## TECH-XIV — original PDF page 169 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


APPENDIX C


 Repository Audit Checklist

 Startup and authority:

  • Locate bootstrap/startup sequence and world-state lifetime.

  • Locate authoritative time/calendar and random-seed ownership.

  • Locate save/load entry points and save schema/versioning. World:

  • Terrain provider, generation pipeline, parcel/grid/road/pathing authorities.

  • Building placement/footprint authority and authored scale handling.

  • Active-region / off-map simulation boundaries. Entities:

  • Person, household, business, asset and property identity models.

  • Stable ID generation, duplicate repair and reference resolution. Economy:

  • Cash, net worth, inventory, pricing, supplier, transaction and receivable authorities.

  • Business administration, staffing, managers and delegation. Movement:

  • Vehicle/animal/person travel jobs, loading/unloading and route state. UI:

  • Shared management shell, command pathways, data binding and workflow state. Persistence:

  • Long-running jobs, relationships, projects, loans, occupancy, contracts and trips. Testing:

  • Existing unit/integration tests, debug tools, deterministic scenario harnesses and performance profiling. Output:

  • Current-state map, divergence table, high-risk authority conflicts and recommended smallest next implementation
    pass.





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 21

## TECH-XIV — original PDF page 170 of 170

LAND & LEDGERS / REVISION I / TECHNICAL IMPLEMENTATION BIBLE


APPENDIX D


 Technical Research Holds

 Repository holds: current Unity class names, manager/service topology, persistence format, scene architecture,
 navigation stack, existing tests, implemented terrain/provider authority and current business UI must be audited
 rather than assumed.

 Calibration holds: exact demographic constants, workday/shift patterns, repair labor times, asset wear rates,
 merchandise capacities, household spending cadence, rent cadence, market response and many historical quantities
 remain Canon research/calibration holds. Keep these data-driven/configurable where practical so validated values can
 replace placeholders without architecture rewrites.

 Legal holds: detailed 1880 guardianship procedure, tenancy/removal practice, lien priority and other period-law edge
 cases should be modeled only to the depth Canon research supports.

 Performance holds: entity counts, active-agent distance, pathfinding budget, update cadence and save size must be
 profiled in the real project. Do not lock premature optimization assumptions into Canon behavior.

 END OF TECHNICAL IMPLEMENTATION BIBLE - REVISION I





                                                                                                 TECHNICAL IMPLEMENTATION BIBLE 22