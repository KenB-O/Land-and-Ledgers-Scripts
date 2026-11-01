# LAND & LEDGERS
## CANONICAL PROJECT BIBLE — INTEGRATED WORKING EDITION v0.4

**Current documentation library:** v2.3 · **Updated:** 31 October 2026 · **Historical window:** Dakota Territory / South Dakota, 1870–1905

**Purpose:** This is the *one active, consolidated gameplay design reference* for day-to-day ChatGPT and Codex work. The design findings from THE FIRST VEIN are integrated into the relevant subject sections alongside all prior scenario lessons; they are not an additional competing addendum. Existing scenarios are documented together in `08_Scenarios_and_Playtests`.

**Authority and verification boundary:** The source baseline is Canon XXXI (7 Oct 2026). This current edition sets out intended design as revised, but **comprehensive semantic no-loss certification against all preserved earlier Canon layers remains incomplete**. For prior detail not addressed here, consult the preserved Canon XXXI source PDF; explicit newer decisions in this edition control where they conflict. No Unity implementation or executable test result is established by this document. Historical observations and hypothetical playtest output remain evidence, not universal parameters.

**Locked design principles:** Generic Business; ordinary tasks physically attemptable without occupation-class gates; one conserved source of economic/physical truth; real Persons, property, time and agreements; causal markets and historical geography; optional full strategic delegation; lawful/unlawful distinction and Person agency; scenario goals must observe real state and never spawn success.

**Decision status:** Previously approved numbered proposals 001–105 remain approved. Separate numbered proposals 106–145 remain **pending** unless an already-authoritative earlier provision covers the same behavior. Explicit player choices from a particular scenario are *scenario policy*, not globally forced gameplay.

---

# Part I — Vision, player freedom and campaign structure

## CAN-CE1-01.1 Identity and historical frame
Land & Ledgers is a realism-first, systems-driven frontier commercial and ownership simulation. Its normal Open Frontier historical envelope begins in **spring 1870** and extends to **1905**; authored scenarios may start later, end earlier or focus on a narrower geography or industry. The earlier spring-1880 base wording is superseded. The world is not a primitive technological blank slate in 1870: established hand, animal, water, wind and steam technologies already exist, while their local availability, finance, transport and support networks vary. The player may begin as an owner-operator, become an employer, manage a group of enterprises, hold portfolio capital, develop infrastructure or eventually delegate virtually every ordinary task. There is no imposed optimal business sequence.

## CAN-CE1-01.2 Three production standards and five realism checks
Every intended mechanic must survive historical plausibility, economic plausibility, social plausibility, lifecycle plausibility and gameplay tolerance. The standing design filters are historical accuracy, genuine player freedom and a premium systemic experience. A physically and economically plausible decision is generally allowed to be unwise, unprofitable, badly timed or strategically ruinous; do not ban it merely because the designer expects it to fail. Conversely, physical limitations, contractual obligations, real legal restrictions, knowledge gaps, worker agency and other people's decisions must have meaning. Research unclear history before promoting speculation to certainty. Complexity should preserve important causal relationships while abstracting needless repetitive inputs.

## CAN-CE1-01.3 Cause and consequence over modifiers
A crop failure removes harvest or capacity; scarcity, purchases, debt and local pricing then react. A bridge improvement changes route geometry, crossing reliability, loads and available journeys; no `FreightBonus` is applied simply because the bridge exists. A new manager improves outcomes through better actual decisions rather than a `ManagementEfficiency` stat. An advertising campaign changes who hears and responds rather than granting blanket sales. Vertical integration works only because actual internal labor, output, storage or transport replaces an outside transaction. Aggregate market, prosperity, business-health and reputation descriptions are useful read models, never primary economic events.

## CAN-CE1-01.4 Failure, risk, game modes and victory
Commercial Ruin is a genuine strategic condition when reasonable recovery avenues have been exhausted, not a single negative-cash or negative-profit threshold. The player can lose a contract, property, worker, claim, route, credit facility, line of business or individual settlement opportunity without the game declaring immediate total defeat. Open Frontier is not forced into victory by a calendar date; scenarios may use dated, binary accomplishments and optional maintained conditions. Major historical or authored objectives are observations of the world, not engines creating the required customers, firms or resources. Player and NPC competitors face the same classes of ordinary opportunity, constraint and risk.

**Principal source trail:** CAN XXXI PDF pp.1–3, 7–8; CAN XXV section pp.72–82; Canon XIV Part I pp.240–252; Technical XIV pp.1–2.

---

# Part II — Source-of-truth economy and commercial identity

## CAN-CE1-02.1 One ordinary Business identity, composed activity
`Create Business` establishes one economic/legal Business identity, governance and the relevant ledger relationship; it does not require selecting General Store, Blacksmith, Construction Company, Ferry Company, Pharmacy or another hard permission class. Capabilities emerge from actual Persons, stock, tools, buildings, usable space, legal/contractual relationships, transport access, finance, time and trade. A firm can simultaneously manufacture, repair, retail, lease property, provide meals, haul, store cargo, cross a river or supply financial services where each operation is in fact supportable. Descriptive `BusinessOperation`, tags, industry names and setup templates aid UI, reporting and initial configuration only. Renaming or deleting a label must not remove a physically possible action.

## CAN-CE1-02.2 Identity, status and operation are separate
Formation is not opening. A finished building is not an operating business; rented space is not an employee; an open business is not automatically profitable. Business forming, capable-but-not-trading, operating, suspended and closed are descriptions derived from necessary resources, current activities, access, authority and commerce, not effects caused by a hidden business-type or `Capability=true` flag. Owners may pause a line, add adjacent service, change assortment or combine activities without making a fresh Business. A temporary loss of cash or capacity is not always dissolution; formal closure, liquidation, reorganization and sale have distinct consequences.

## CAN-CE1-02.3 Economic authority does not collapse into portfolio UI
A Business, an owner, a household, a partially owned corporation, an estate and an external financial institution are not one bank account. Business cash, owner cash, corporate treasury, deposits owed to others, receivables, debts and restricted/committed cash classifications remain meaningfully separate. Grouping several firms as one enterprise or division is administrative; a true merger needs actual transfers, assumption/consent of agreements and obligations, employee transitions, predecessor history and changed title/control. Shared ownership does not permit creating consolidated profit by paying oneself through internal billing.

## CAN-CE1-02.4 Events, books, reports and valuation
An economic event records what occurred: product delivered, money paid, work completed, debt incurred, asset transferred or loss realized. Books may be absent, stale or mistaken without rewriting the underlying event. Reconciling a ledger requires correction evidence rather than silently replacing history. Separate exact known transaction facts from forecasts and appraisals. Business value, gross enterprise value, owner's equity, book basis, supported transferable market estimate and realized sale proceeds are different facts. An asset appreciating on paper is not cash or sale revenue. A manager report may compress many facts but cannot become a competing authoritative balance.

## CAN-CE1-02.5 Rights and roles are specific
Ownership, custody, occupancy, leasehold, security interest, management, employment, membership, supplier priority, easement and representation create distinct obligations and powers. A landlord may own the building but not the tenant's business, stock or accounts; a corporate officer may direct authorized expenditures but does not personally own corporate cash. Customer-owned materials being stored or processed must not become ordinary purchased stock. Business-to-business obligations belong to the actual counterparties. Genuine legal institutional differences can constrain transactions independently of the firm's descriptive industry label.

**Principal source trail:** CAN XXXI pp.1–3, 7; CAN XXX pp.10–16; CAN XXIII pp.95–107; CAN XIV pp.259–264, 302–305; Technical XIV pp.1–2, 9–10.

---

# Part III — People, persons, relationships and time

## CAN-CE1-03.1 Persistent Personhood
People are not anonymous traffic particles, vacancy fillers or headcount purchased by a business. Each represented Person has persistent identity, time, age/lifecycle, household and kin links, health, location, economic roles, knowledge, experience and relevant personal history. A change of employer, residence, occupation, health, assets or jurisdiction must preserve identity and the economically meaningful past. A household, institution or Business can direct or fund an activity, while a particular Person actually travels, works, communicates or buys. Off-map representation may lower detail but does not manufacture or delete a person's identity.

## CAN-CE1-03.2 Work is a capability/action, not a class unlock
Occupation is a readable summary of livelihood, history and likely fit, not a hard character class. Skill, aptitude, experience, domain familiarity and observed evidence are distinct. A teamster may load, repair, handle animals, negotiate freight and operate a small shop when competent and properly equipped; a carpenter can build, repair or supervise subject to actual task requirements. Ordinary production and repair can generally be attempted even by novices where the method and physical prerequisites exist, with duration, failure, injury, waste, supervision and output quality affected by competence. Truly impossible or specialist-sensitive tasks retain genuine underlying constraints rather than progression labels.

Training, mentoring, apprenticeships and practice consume real instructor/trainee time and produce learning from relevant work; they do not grant an unexplained productivity multiplier, and an assessment increases what an observer knows rather than instantly improving the worker.

*Retained source precision: CAN-RET Training is earned work.*

## CAN-CE1-03.3 People decide whether they perform work
Feasibility of a task is separate from an employee's willingness and contractual obligations. A Person can accept, refuse, negotiate, leave, abandon, request assistance, report unsafe conditions or pursue another job. Agency depends on actual compensation, need, obligations, dangerous conditions, commute, relationship, fatigue, family duty, credible alternatives and available information. Good pay may not overcome unsafe work or domestic obligations; unwillingness does not mean the work method itself is impossible.

## CAN-CE1-03.4 One time budget and realistic staffing
Employment scheduling, travel, sleep, care, family enterprise labor, domestic work, shopping, education, illness and civic participation draw on the same Person's available time. One worker may cover many task families sequentially, but cannot occupy different sites or perform incompatible tasks simultaneously. Workload comes from actual concurrent transactions, production, unloading, keeping records, service requests, maintenance and management demands, not prescribed job slots per Business type. Family and household labor are real finite work even if no separate market wage is paid; family/work relationships need not be recoded as a conventional payroll transaction. Training, apprenticeship and experience require real learning work and elapsed time, not instant level advancement.

## CAN-CE1-03.5 Labor market and personnel evidence
Hiring begins from people that can actually be approached through known contacts, referrals, advertisement, messenger, newspaper, mail, telegraph or other legitimate channel; a button must not present an omniscient global candidate sheet. A Person's recorded jobs, statements, observations, work products, references and rumors yield varying confidence; hidden aptitude or future retention remains unknown. Offers establish real terms (pay, duties, schedule, board/housing, start, authority, commitments); response and information exchange consume time. Employers compete via wages, conditions, housing, hours, prospects, continuity and reputation from actual treatment. Losing one critical qualified operator can matter more than losing an interchangeable worker.

## CAN-CE1-03.6 Fatigue, care, households and consequences
Long hours, poor sleep, travel, nutrition adequacy, household care, injury and sickness can create reduced availability or specific errors rather than a generic universal sick or morale percentage. Responsibility for children, older kin, disabled dependents and household production is assigned from actual circumstances, not blanket gender stereotypes. A wage-free spouse or adult child helping the shop cannot be in two places at once. Worker retention and household resilience arise from relationships and options, not a permanent loyalty bar.

**Principal source trail:** CAN XXXI pp.2–5; CAN XVII system audits pp.157–201; CAN XXIII pp.95–107; CAN XIV pp.285–296; OCC report pp.1–18. The 112 occupation profile list remains a supplemental classification suggestion, not a Canon permission catalogue.

---

# Part IV — Households, accommodation and migration

## CAN-CE1-04.1 Household, kin and residence remain separate
A Household is an economic/domestic unit, not a synonym for house, nuclear family or address. Parent-child and spouse relationships do not force shared residence; unrelated boarders can live together without merging finances or kinship. The normal relationship is Person → Household → Residential Occupancy → Accommodation Space → Building/Shelter → Parcel. A Person may relocate, board elsewhere or marry while retaining stable identity. Birth and death affect real Persons; marriage, household split/merger, guardianship and estate matters change relationships instead of generating or consuming free population counts.

## CAN-CE1-04.2 Housing is suitability, not a universal quality tier
Owner occupation, renting, lodging, boarding, shared rooms, kin hospitality, employer accommodation, camps and precarious shelter are arrangements, not linear upgrade levels. Space provides actual shelter, sleep capacity, privacy, warmth, cooking, storage, water, sanitation, safety and access. Suitability is evaluated for a particular household in a particular season at a price it can afford. Emergency crowding can occur with costs, risks and eventual motivation to leave; a vacant single-person bunk does not solve a family-housing shortage. Rent is a contract rather than a floating instant spot price imposed on an occupied household.

## CAN-CE1-04.3 Migration is an informed commitment and physical journey
Migration arises from employment, business possibilities, kin, land, public information, contracts and hardship, then requires knowledge of the opportunity, a choice, realistic travel, accommodation and eventual stabilization. An empty new house does not spawn a new family. A laborer may move first while family follows; a family may refuse a wage offer because no suitable housing exists. Temporary miners, rail crews and travelers alter daytime/transient demand without all becoming permanent residents. A boom can build camps before houses and services, or collapse leaving vacancies and debt. Population and labor-market read models distinguish residents from transients and reachable rural population.

## CAN-CE1-04.4 Consumption and domestic work
Consumption arises from household people, reserves, income/cash/credit, preferred goods, affordability, urgency, season, travel and information, not an abstract population sales multiplier. Need, willingness and purchasing power are separate. Food, heating fuel, water, clothing, medicines, schooling and lodging consume actual resources; household production and family help also consume actual work. Merchant credit shifts timing and risk rather than creating income. A household can endure, downshift purchases, negotiate arrears, move, seek work or use kin support as actual circumstances permit.

## CAN-CE1-04.5 Continuity after death, displacement and loss of guardianship
When an adult dies, disappears or is incapacitated, kinship, dependents, household membership, residence, employment and estate ownership remain persistent and separately resolvable. Dependents may require guardianship or temporary care; moving a dependent into another home does not automatically transfer inherited property or erase historical identity. Death is not a command to invent a new household of replacement Persons.

**Source trail:** CAN XXX pp.17-19; preserved CAN XIV household lifecycle; TECH XIV preserved population guidance.

**Principal source trail:** CAN XVII system audits pp.157–201; CAN XXIII pp.95–107; CAN XXX pp.10–20; CAN XIV pp.285–296, 341–342; Technical XIV pp.9–10.

---

# Part V — Land, parcels, buildings and real estate

## CAN-CE1-05.1 Geography is partly fixed and partly emergent
The historically meaningful skeleton—major rivers, regional market centers, broad terrain, treaty/reservation/federal geography, major rail approaches and governmental chronology—must respect the scenario's historical envelope. Local roads, plausible towns, farmsteads, ferry sites, enterprises and lesser projects may vary procedurally. A settlement's commercial character is an observed role such as river crossing, mine service center, rail-platted agricultural town or administrative node, not a universal TownLevel granting permissions. Infrastructure or geography makes activity more attractive or difficult through actual travel, access and costs.

## CAN-CE1-05.2 Parcel, building, space and use are distinct
Parcels have title/legal status, survey facts, frontage, access, area, topography, water and development context. Buildings have condition, design, functional spaces and systems. FunctionalSpaces expose physical characteristics and suitability; a use relationship gives a Person or Business legitimate occupancy/operation rights. The commercial function of a building does not determine what kind of Business its occupant may operate. Mixed-use structures and shared buildings—including shop under living quarters, multiple independent tenants and flexible workshops—are normal where physically and legally feasible. Conversion requires actual work and costs where necessary, not a category-flip bonus.

Property can be occupied, improved or used without the relevant legal permission when physical access exists; such use does not fabricate title or lease rights and may produce trespass, landlord, creditor or state action under proper evidence and dated procedure.

*Retained source precision: CAN-RET Unauthorized use remains physically possible.*

## CAN-CE1-05.3 Land and business acquisition are transactions with willing counterparties
An interesting parcel is not automatically listed or for sale. Offers can be made, but owners may refuse, negotiate, seek another buyer, doubt financing, demand speed, retain family land, require consents or have encumbered title. Discovery comes from owner contact, listings, agents, public/estate/creditor events, direct inquiry, investigation and local knowledge. Actions include purchase, lease, option, mortgage/security, easement, seller financing, parcel division, partnership or another historically supported arrangement. Auctions, tax sales, foreclosure and bankruptcy preserve their different rights, uncertainties and redemption possibilities.

## CAN-CE1-05.4 Public land, rights, Indigenous geography and legal provenance
Visual emptiness is not ownership vacancy. Filing, claim, residence/improvement, proof, patent and transfer are different states; railroad grants, federal holdings, treaty/reservation boundaries and other legal interests constrain what can be acquired, possessed or developed. Land authority depends on dated jurisdiction and actual claims, not a homestead button that creates perfect fee-simple title. Indigenous communities, land rights and commercial relationships require researched geography and historically respectful specificity; do not turn restrictions into a generic unclaimed-land pool.

**Mineral and millsite interests.** Prospecting rights, mineral claim location, unpatented claim, permitted millsite entry, patent, deeded fee land, lease, purchase option, lender security, water access and tailings use do not become interchangeable titles. Ownership of improvements, rights of removal and compensation depend on relevant agreements, notice, jurisdiction, property interest and dated law; a desirable seller-financed purchase remains subject to an actually willing seller. *Source: First Vein 2026-10-29 Parts 1, 4 and 5.5; legal particulars require dated verification.*

## CAN-CE1-05.5 Property operation and development
Landlords decide rents, improvements, maintenance, sales, building use and financing; tenants decide whether terms and spaces fit. Development requires site control, engineering or design as necessary, materials, contractors, labor, finance, work, usable completed spaces, recruitment and actual occupancy. Vacancy, bad access, expensive debt and uncertain future demand should inhibit further building through real economics. The same owner can operate multiple properties, sell some, lease others or authorize managers to pursue eligible sites within governed spending and financing boundaries.

## CAN-CE1-05.6 Public land and Indigenous geography remain distinct
Federal public-domain land, railroad land, land under Indigenous treaty or reservation status, an unperfected private entry claim, a patented title, a leasehold and a disputed or encumbered private parcel are not interchangeable. A terrain tile that looks unoccupied does not prove it is legally available; claims and changes in control require the dated geographic/legal process, evidence and affected parties. Research precise jurisdiction and geographic chronology rather than treating all frontier territory as unrestricted market inventory.

**Source trail:** CAN XXIX pp.22-27; CAN XXV pp.72-76; CAN XXXI p.5.

**Principal source trail:** CAN XXXI pp.5–6; CAN XXV pp.72–82; CAN XXIX pp.21–37; CAN XIV pp.253–259, 326–330, 338; Technical XIV pp.4–5.

---

# Part VI — Task execution, production, repair and projects

## CAN-CE1-06.1 A common work grammar
Work begins when an economic or physical need exists. A ProductionMethod or service/work method describes inputs, equipment, space, prerequisites, work sequence, labor/time, tolerances, outputs, byproducts, failure and rework. Tasks consume actual Person and equipment availability and advance on the authoritative continuous clock; independent work fronts can proceed concurrently where resources permit. Methods are connected to objects and work, not granted by shop or occupation class. Plans, methods, execution, quality and completion have distinct evidence.

## CAN-CE1-06.2 Stock production, made-to-order work and customer work
A Business may hold target stock, maximum stock, reserved inputs, make-to-order policy, customer-order priority, internal-project priority and other ordinary instructions. Those settings create scheduling choices, not output or magical throughput. One forge or saw cannot perform two incompatible jobs at once; purchased inputs and actual inventory must be present. A smith may manufacture nails for sale, repair a customer's tool and provide internal work, with the operator or manager deciding which commitment to fulfill first. If the customer owns the item under repair, its custody and return are not a sale of that item to the workshop.

## CAN-CE1-06.3 Condition, diagnosis and repair
Durability is not one universal health bar. Machines, wagons, tools, animals, buildings and infrastructure have meaningful condition domains, faults, operating limitations and maintenance histories appropriate to each object. Wear is driven by actual workload, environment, storage and use. Inspections change knowledge; repair changes the asset. A failed task can generate delayed service, defect, rework, damage, injury, contaminated output, wasted stock or a new repair job as its domain warrants. A merchant or manager may defer maintenance and bear the consequences.

## CAN-CE1-06.4 Projects are decomposed physical commitments
Construction and infrastructure are Projects containing meaningful WorkPackages, tasks, material requirements, workers, specialists, equipment and dependency chains. Completion of construction, physical usability, occupancy, commercial opening and profitability are distinct facts. A permanent new building cannot appear because its named prefab timer elapsed, and a contractor does not earn skill or access by completing an abstract ContractorLevel. Capability, resources, prior work, reputation evidence, financing, relationships and capacity influence winning and performing jobs.

A building may be usable or occupied despite construction defects, disputed inspection, unpaid subcontractors or outstanding final payment; contractual acceptance does not certify every claim settled, and repairs, retainage or dispute remain real obligations.

*Retained source precision: CAN-RET Acceptance, usability and defects are separate.*

## CAN-CE1-06.5 Construction duration and contracting
Active task time, elapsed project time, lead/queue time and interruption time must not be conflated. Materials may require procurement and freight; a finished roof cannot precede its physical supporting work, and fast track laying on prepared grade is not the duration of the whole railway. Weather, season, soil, crews, labor mix, specialist access, change orders, inspections, accidents, rework and cash delays can change elapsed delivery. A builder may accept multiple overlapping contracts, self-perform, hire, subcontract, prioritize internal work or risk overcommitment; contract performance and actual work intervals matter more than signatures.

A project may be built by owner labor, directly hired trades, a general or prime contractor, or mixed arrangements; responsibility, financing, coordination, procurement and liability follow those agreements instead of a predetermined construction tier.

*Retained source precision: CAN-RET Building delivery structures are composable.*

## CAN-CE1-06.6 Construction claims, risk allocation and performance security
Contractor default need not erase completed physical construction: remaining work can be reassigned while defects, title, unpaid bills, claims and disputes persist. Liens, bonds, risk-of-loss clauses, public procurement, inspected acceptance, change orders and cure terms must be resolved under the applicable agreement and dated law. A performance bond creates contingent security, not instantaneous project completion.

**Source trail:** CAN XXVIII construction project integration p.46; Work calibration pp.5-7.

An owner paying a prime contractor does not automatically settle the prime contractor's workers, suppliers or subcontractors; those separate obligations and potential lien or claim rights persist until independently resolved.

*Retained source precision: CAN-RET Paying the prime does not pay everyone.*

## CAN-CE1-06.7 Work-package timing and project calendars
Meaningful construction estimates distinguish active labor or machine time, calendar time to usable/accepted completion, lead/queue delay and interruption time. Task quantities, parallel fronts, tool/material staging, weather, contractor scheduling, funding and rework determine elapsed completion; record-setting performances are not default rates. Project reporting should preserve planned active work, current elapsed forecast and actual elapsed result.

**Source trail:** WORK reference pp.2-3 and 15; CAN XXVIII construction integration.

## CAN-CE1-06.8 Physical output under financial distress
A stalled or abandoned project retains survey, excavation, worksite, supplied materials, part-built structures, defective workmanship and legal claims that actually exist. Replacement contracts, financing, liquidation or rework consume or transfer that material state rather than restarting the project from a clean abstract percentage.

**Source trail:** CAN XXVIII construction integration p.46; CAN XXX 4.3.

**Principal source trail:** CAN XXXI pp.3, 5; CAN XXVIII pp.38–46; CAN XXII pp.108–112; CAN XIX pp.125–156; CAN XIV pp.259–264, 335, 339; WORK reference pp.2–16.

---

# Part VII — Products, stock, purchasing and trade

## CAN-CE1-07.1 A retail transaction needs a real offer
Retail arises from goods offered for sale by an actor with legitimate inventory/custody, price and terms, an accessible channel or location, adequate labor/time and any necessary legal requirements. Grocery, dry goods, feed, hardware, books, farm produce and other merchandise are inventory assortments, not class-permitted lists. A storefront is one customer-facing arrangement; lawful sale may also occur at a market stall, workshop, landing, wagon, home, warehouse or rural producer site. Merchants may offer a broad mix while retaining one Business identity.

## CAN-CE1-07.2 Supplier relationships are specific
Goods come from actual producers, wholesalers/jobbers, other local businesses, regional channels, own production or off-map sources with dates, price/terms, quantities, quality, transport responsibilities and lead time. Purchasing stock without an identified supplier/channel is not ordinary replenishment. Supplier terms can vary by product and customer relationship, including minimum quantity, bulk discounts, samples, deposits, credit, reserves, seasonal availability and delivery schedules. Local businesses and player-owned divisions do not receive free stock because they share ownership.

## CAN-CE1-07.3 Inventory is physical and capable of aging
Stock belongs to a specific owner and occupies a physical location or legitimate in-transit/custody state. Quantities, lots, allocations, display/storage/bulk/cold capacity and reservations must reflect real goods, not a global warehouse abstract. Perishables can deteriorate, become unsafe, shrink, be wasted or be transformed by preservation; a new label or mixing with fresh stock does not reset their physical history. Quality and grade should carry the meaningful item-specific information required for pricing, use and provenance. Finished manufacturing and physical disposal change lots only through supported operations.

## CAN-CE1-07.4 Demand, ordering and revenue remain distinct
An order, supply agreement, reserved stock, shipment, service booking and actual completed purchase are different facts. A household needing flour may fail to buy because of price, travel, credit, stockout, bad service or a better alternative. A business cannot treat unfilled demand as revenue. Sales may occur on account and create a receivable, not immediate cash. Returns, disputes, late deliveries and unacceptable quality preserve the actual agreement terms and work/stock consequences. Accounting must not reward a false sale or duplicate a delivered lot.

**Commercial fulfillment.** A quote, invoice, consignment, shipped ore lot or buyer's promise is not automatically accepted or paid revenue. Sale transactions preserve payer, seller, beneficiary, physical commodity, ownership, delivery, commission and collection evidence; milestone predicates count only the exact defined completed events. *Source: First Vein 2026-10-29 Parts 2.4, 5.3, 6.*

## CAN-CE1-07.5 Barter, credit and exchange
Where period commerce supports it, a general merchant may accept eggs, produce, hides or another item in trade against other goods or a customer account, with real custody, valuation and resale/consumption consequences. Store accounts and supplier credit shift timing and default risk without minting money. Wholesale, retail, agent/consignment and own-production transactions must preserve which party actually earns a sale, pays a fee, carries stock risk or has a claim on goods. Packaging, handling, freight and finance influence landed and replacement cost.

## CAN-CE1-07.6 Grain grading, elevator custody and warehouse receipts
Grain ownership, physical custody, measured grade, weight, storage charges, depositor claims and final sale are different facts. An elevator may buy grain outright or store grain for customers. Warehouse receipts can evidence a claim and, when applicable law supports it, be transferable or pledged; neither paper issuance nor movement within storage automatically creates an outright sale.

**Source trail:** CAN XXIX pp.27-29; CAN XIV p.207.

## CAN-CE1-07.7 Commission sale, forwarding and consignment
A producer can consign physical goods or livestock to an agent or commission merchant who organizes transport, inspection, yard/storage handling, marketing and settlement for agreed fees. The agent's custody, selling authority, cash collections and owner compensation remain distinct from title and the gross sale price. A commission relationship improves access but cannot guarantee a higher price or a buyer.

**Source trail:** CAN XXIX p.89; CAN XXXI composed commercial identity.

## CAN-CE1-07.8 Conservation, blocked shipments and authoritative settlement
Important inventory cannot be simultaneously restored to origin stock and retained as a separate loaded shipment; fulfillment and settlement must be supported by the same durable physical/economic events. A failed, partially fulfilled or blocked shipment must not count as completed delivery or qualifying provenance. This is an existing Physical Truth and ledger-integrity requirement; the specific known CreateBlockedShipment defect remains a P0 implementation follow-up, not a claim of repair.

**Source trail:** TECH XIV preserved Rev IV p.9; CAN XXXI 10.1; CAN physical freight doctrine.

**Principal source trail:** CAN XXXI pp.2–3; CAN XXVI pp.53–71; CAN XXIX pp.21–37; CAN XVII system audits pp.157–201; CAN XIV pp.265–268, 280–284; OCC pp.8–9.

---

# Part VIII — Rural production, land use and biological economies

## CAN-CE1-08.1 Farms are assembled physical enterprises
A farm is a combination of lawful land use and tenure, usable acreage and soil, weather/water, crops, seed, buildings, storage, draft power, tools, people, household labor, contracts, transport and finance rather than a FarmLevel. Owner operation, tenancy, share arrangements, family enterprise and hired work have different rights and costs. Seasonal crop and animal tasks compete for scarce people, field windows, equipment and transport. A farm can diversify, fail, lease equipment or hire custom services according to the same market logic as other enterprises.

Owner-operated, cash-rented, share-rented and partly owned farms are distinct tenure relationships; a share tenant operates under lease or cropping terms and does not become a wage employee unless a separate agreement creates employment.

*Retained source precision: CAN-RET Farm tenants are operators, not employees by default.*

## CAN-CE1-08.2 Output is not free money
Growth, breeding, seed propagation, lactation, eggs, firewood, produce, wool, hides and similar physical outputs do not directly post owner cash or arbitrary mark-to-market profit. They may be consumed at home, stored, traded, processed or sold through ordinary events. Slaughter, processing, preservation, byproducts and disposal need material conservation. Product weight, grade, spoilage and preservation condition matter to what can be sold or delivered. Internal farm-to-store or farm-to-saloon supply still uses inventory, Person work, transport and agreed costs.

## CAN-CE1-08.3 Animals have continuity
Working animals need feed, water, rest, appropriate tack/harness, handling, treatment and ownership identity. Health, age, condition, breeding and death influence productive capacity. Poultry in particular preserve source/parentage when relevant, fertility, incubation, hatch, juvenile growth, sex, laying/breeding allocation and eventual disposition. Non-laying males are not silently deleted; policy may retain, sell, slaughter or otherwise handle them. Exact fertility curves and optimal flock thresholds are not universal Canon numbers.

Livestock pregnancy, birth and calf survival require distinct causal stages such as late pregnancy, birth, immediate neonatal survival, pre-weaning growth and weaning; herd management, nutrition, weather, disease and individual condition should drive outcomes rather than one universal mortality roll.

*Retained source precision: CAN-RET Calf loss must be stage-specific.*

## CAN-CE1-08.4 Specialized rural networks
Custom threshing, implement dealing, seed supply, feed milling, creameries, grain elevators, stockyards, livestock consignment, branding, estray recovery and field services emerge from equipment and real relationships. Grain custody, grade, ownership, warehouse receipts, sale and transport are separable; an elevator can store someone else's grain or acquire it. Regional dealers and commission markets influence price/lead time without replacing physical production. Rural side-economies such as bone collection and timber extraction deplete finite resources instead of respawning forever.

## CAN-CE1-08.5 Historical context over global modifiers
Drought, grasshoppers, hail, blizzard, long winters, livestock disease, market shifts and new machinery alter actual yields, survival, reserves, routes, prices, finance and replacement needs. One failed ranch should not mechanically shift the entire regional market unless its consequences plausibly propagate. Research crop calendar, soil, historic machinery and regional land/water practice before hard-coding their parameters.

## CAN-CE1-08.6 Agricultural and livestock seasonality
Crop stages, pasture, winter forage, feed costs, animal condition, water, weather, transport, staffing and peak harvest work determine seasonal production and marketing choices. There is no universal monthly commodity-price bonus. Owners may retain, sell, breed, finish, process or stockpile goods as credible forecast, land use and working capital permit.

**Source trail:** CAN XXIX livestock commerce p.89; CAN XXV historical climate regime.

**Principal source trail:** CAN XXXI pp.6–7; CAN XXIX pp.21–37; CAN XXV pp.72–82; CAN XIV pp.268–279; Technical XIV pp.5–6.

---

# Part IX — Craft, food, services, extraction and mining

## CAN-CE1-09.1 Business depth comes from distinctive causal processes
An activity is worth being fully player-operable when it creates meaningful decisions about information, physical capacity, labor, input supply, products, customers, agreements, money, condition, time, authority and regional consequences—not merely because a historically named occupation existed. Distinct processes may reuse common work/transaction authorities rather than becoming bespoke minigames. A mixed workshop can repair wagon hardware, shoe horses, make farm tools and stock nails; its capacity comes from actual forge, labor, material and suitable method, not a `BlacksmithBusiness` licence.

## CAN-CE1-09.2 Hospitality, boarding, saloons and concessions
Meals consume purchased or produced ingredients and food work; lodging uses real accommodation capacity, cleaning, heating, water, laundry and staff time. A saloon can sell drink and food, host poker, offer other lawful or intentionally unlawful period activity, rent gaming space or contract with an independent faro banker. Free poker has no direct gaming receipt unless actual fees/rake are agreed; faro exposes real bankroll risk and possible concession revenue. Independent entertainers, gamblers or service providers may be tenants/contractors rather than employees. Customer dwell, attraction, related purchases and nuisance consequences should emerge through Persons and transactions, not a SaloonSynergy modifier.

## CAN-CE1-09.3 Processing chains preserve identity
A butcher buys or processes livestock, allocates usable cuts, offal, fat, hides, bones and other outputs, faces perishability and refrigeration constraints, and may sell retail or wholesale. Tanning is a separate time-consuming transformation; saddlery/leatherwork requires its own tools and skilled work. Bakery, dairy, milling, brewing, preservation and other processing use actual inputs, batches, equipment, work fronts, safety risks and lead time. A physical batch may change what it is while retaining source ancestry; a Business can be cash-starved during long work in progress even when a prospective sale is attractive.

**Custom/toll milling and recovery.** A mill can process a customer's ore without purchasing the ore or recovered metal. Record its fees, material custody, throughput, recovered concentrates/metal, tailings and losses separately; retained customer title does not become mill revenue. Selling raw ore, delivering ore for processing, recovering gold and receiving payment for recovered-and-sold owner gold are distinct economic states. Do not infer profit from ownership of mill or freight assets. *Source: First Vein 2026-10-29 Parts 4.1, 5.3.*

## CAN-CE1-09.4 Health, professional and knowledge services
A physician, midwife, nurse, dentist, druggist, veterinary specialist, barber, teacher, lawyer, surveyor, printer, reporter, assayer or agent supplies domain-specific work only where real skill, information, facilities, contracts and dated legality permit. Pharmacy can combine compounding/dispensing and ordinary retail; medical consultation can be run separately or together where rights and staff permit. Barbering does not automatically include dentistry. Education, investigation, legal drafting and medical care consume Person time and can produce better information or recovery without promising perfection or magical productivity.

## CAN-CE1-09.5 Mineral rights and mines
Prospecting, claims, survey, assays, excavation, shaft/tunnel development, timbering, ventilation, dewatering, extraction, ore sorting, hauling, crushing, concentrating, smelting and sales are physically and financially distinct decisions. Mining work fronts may have different material grade, support, water and knowledge; knowledge from an assay does not reveal unexamined rock. High-grade/low-grade stockpiles, dumps, tailings, salvage and lease/investment arrangements can acquire new value as transport, treatment and price change. Claims, partial ownership, outside capital, service contracts, custom milling and mining supply can be separate opportunities without all requiring ownership of a producing mine.

**Principal source trail:** CAN XXXI pp.3–6, 8; CAN XXX pp.15–18; CAN XXIX pp.21–37; CAN XVII pp.157–201; CAN XIV pp.265–279, 297–301; Technical XIV pp.2–5.

---

**Mining, sampling and geology.** A prospect is not a prefilled profit node. Prospecting, location, selected specimens, independently obtained in-situ samples, independent assays, extraction, underground access, ventilation, waste, grade uncertainty, mineral recovery and sale all preserve distinct causes and evidence. A single rich surface specimen must never be assumed to represent all mineable rock. A location or unpatented mineral claim is an interest subject to law, not automatically a fee-simple deed. The mine may be worthless or operate at a loss. Methods and historically plausible equipment vary by location and year. *Source: First Vein conceptual handoff 2026-10-29, Parts 1, 4, 5.3; original Canon XXXI minerals/property doctrine.*

# Part X — Freight, transportation, rail, routes and communications

## CAN-CE1-10.1 Transport as a connected physical system
Routes are actual terrain/rights/infrastructure connections with capacity, cost, season, grade, surface, crossings, tolls and operating services. Wagons, stagecoaches, ferries, riverboats, rail and local drayage can form multi-leg journeys. A transport contract does not create vehicles, teams, operators or a crossing. Important cargo remains located, owned or held in custody somewhere through loading, transit, transfer, unloading and acceptance. Coarse off-map movement may lower path detail but must preserve identity, time, cargo and legitimate transport economics.

## CAN-CE1-10.2 Freight is not teleportation
An actual Shipment or Trip requires source, cargo, loading, available transport/handling, route or service, destination and unloading. Animals, workers, wagons, vessels and handling space are finite. A business can self-haul, pay a carrier, require pickup, charter service or subcontract under contract. Warehousing needs appropriate storage space, legal custody and handling capacity; calling a business a freight firm does not create unlimited storage. Contracts may overbook legitimately and result in delay or renegotiation, but unfulfilled shipments cannot magically count as physically delivered.

**Mine and mineral freight.** A loaded wagon must have real team, animal, road, load, time, capacity and cargo custody; a shipment may be late, abandoned, lost, disputed or rejected without duplicating its source lot. A privately owned freight service charges and consumes ordinary resources even when it serves another company under common ownership. *Source: First Vein 2026-10-29 Parts 4.1 and 5.4.*

## CAN-CE1-10.3 Rail development and service have distinct stages
A railway can be proposed, surveyed, financed, have rights-of-way, grading, rails laid, facilities built and an opened schedule—none of those states automatically imply the later ones. Rail segments may have different builder, owner, lessee, operator and controlling corporation over time. Operating depots, platforms, livestock pens, freight houses, water/fuel, warehouses, connections and stations are separate physical capabilities. Rail may capture long-haul traffic while increasing feeder wagon, farm collection, drayage and last-mile work. Industrial lines can precede general common-carrier services when economics justify them.

## CAN-CE1-10.4 River, ferry and local transport survive network change
Ferrying, private river carriage, forwarding, passenger passage, drayage and livery require different equipment/people and may coexist in one Business. Navigation depends on seasonal ice, water stage, current, flood, wind, channel and vessel condition; an unusable river is not a flat winter revenue debuff. A small teamster can begin freight with a team, wagon, harness and supporting arrangements without building a warehouse. A business may charter river capacity before buying a vessel, gathering genuine demand and cost information rather than following an imposed transport tier ladder.

## CAN-CE1-10.5 Technology is available, obtainable and supportable
Commercial invention date, regional availability, price, dealer/support network, local access, installation, skill requirements, power/fuel, spare parts and actual operation must be distinguished. A town need not pass an arbitrary `TechnologyLevel` to attempt a new machine, but an imported machine without fuel, parts or operators may be unproductive. Telegraph, postal service, newspaper, courier, telephone, road and rail transmit knowledge and goods with different costs and time; physical mail or message information must travel before actors react.

## CAN-CE1-10.6 Period mail, catalog and parcel movement
Catalog trading and correspondence may extend merchant reach, with mail-order businesses operating within the campaign, but an order does not teleport goods or complete payment. Payment, order acceptance, freight/express carriers, rail/depot handling and collection or final delivery remain ordinary obligations. United States Parcel Post begins outside the normal 1870-1905 horizon and must not be projected backward into early local heavy-goods delivery.

**Source trail:** CAN XXV preserved p.77; CAN XXIX communication chronology.

**Principal source trail:** CAN XXXI pp.5–7; CAN XXV pp.72–82; CAN XXIX pp.21–37; CAN XIV pp.280–284, 306–311; WORK pp.8–15; Technical XIV pp.4–5.

---

# Part XI — Settlements, markets, competition and economic pressures

## CAN-CE1-11.1 Settlement growth derives from actual people and opportunities
Settlement population is based on real residents; daytime, transient, temporary-camp and rural-catchment measures serve different analyses. A handful of residents near a ferry or mine camp might support services because of passing teams, farmers and travelers; a larger isolated town may struggle. Roads, property, labor, accommodation, rural farms, market hubs and infrastructure alter reachable demand and firm viability. Settlement descriptors are role summaries, not automatic business lists or tier-based unlocks. A population chart must never post a transaction.

## CAN-CE1-11.2 Customer choice and competition
Households, producers, employers and other buyers have real budgets, needs, relationships, information and alternatives. Price, quality, condition, stock, assortment, credit, travel, hours, reliability, service speed and delivery matter to their decisions. Customers can defect from the player and later return after evidence of improved conduct. Rival owners can invest well, misread demand, go into debt, lose staff, fail, acquire distressed businesses or control scarce sites under the same operating grammar. Rivals are not entitled to spawn merely to restore game balance.

## CAN-CE1-11.3 Opportunity is interpretation of pressure, not a generator
Evidence can include refused sales, queues, poor housing match, insufficient freight capacity, idle equipment, lack of labor, vacant premises, changing margins, delayed orders, credit stress and outside-market prices. An opportunity report compares unmet need, feasible responses, customer reach, competing firms and projects already in the pipeline. A shortage might be met by incumbent expansion, new firm, distant shipment, alternative product, migration or no profitable response. Saturation deserves its own interpretation from vacancy, low utilization, falling margin and unsold stock, not merely `Shortage = false`.

Absent an explicit scenario premise forcing immediate risk, entrepreneurs may investigate traffic, supplier reach, property condition, title and potential customers before spending their scarce capital; research consumes money, time and information access and can prove an opportunity unattractive.

*Retained source precision: CAN-RET Research before commitment.*

**Independent counterparties.** Actual NPCs can establish and run genuine enterprises, supply supplies, do business or decline a proposal using their own capital, knowledge, agreements and labor. Their transactions must be independently funded and supported. A player subsidiary is not an independent NPC enterprise even if a separate manager operates it. Research and referrals can return no willing actor. *Source: First Vein 2026-10-29 Parts 4 and 5.6.*

## CAN-CE1-11.4 Dynamic feedback without scripted correction
Multiple builders or investors may independently construct for a shortage that disappears before completion, creating oversupply and distress. Prices move through actual output, buyer willingness, inventory and delivered cost. Price wars are ordinary purchases/sales at chosen terms and can reward customers or bankrupt overextended sellers. Closures release sites, labor, equipment and relationships; survival may depend on reserves, creditors, alternate products, expectations and willingness to continue. Neither prosperity nor failure is guaranteed by a town population trend.

**Principal source trail:** CAN XXXI pp.7–8; CAN XXVII pp.47–52; CAN XVII pp.157–201; CAN XIV pp.253–256, 297–301.

---

# Part XII — Lending, finance, corporations and distress

## CAN-CE1-12.1 Finance is an ordinary economy of obligations
Banks, private creditors, merchants, investors, mortgage lenders, loan/exchange/collection agents and outside financiers participate through instruments, accounts, relationships and dated powers. A new finance firm can lend its own money, arrange third-party credit, collect claims or provide permitted services without following a mandatory Loan Office → Bank tech tree. Taking public deposits creates obligations to depositors and material liquidity management; it does not enrich the owner personally. Financial legal forms and powers can differ genuinely by date, place and charter.

**Preserving ownership while financing.** Seller notes, purchase-money mortgages, secured installment arrangements and reasonable down payments can make ownership possible without equity dilution, but only if a genuine counterparty offers enforceable terms. The player may express a preference for 100% ownership and avoiding leases/dilution as a standing owner policy, not a rule denying other players partnerships or leaseholds. Debt service, liens, collateral loss and default remain real. *Source: First Vein 2026-10-29 Parts 1 and 5.5.*

## CAN-CE1-12.2 Credit is information-limited and lender-specific
Underwriting may consider borrower history, reputation evidence, collateral, existing encumbrances, purpose, projected repayment, character/relationships, industry, maturity, available lender funds, portfolio concentrations and other alternatives. A lender is not omniscient and may ask for records, guarantees, inspection, explanation or restrictive covenants. Appraisal value is not cash; high-value illiquid property does not automatically meet today's withdrawal or payroll bill. An undrawn credit facility is neither cash nor principal debt; drawing and repaying create corresponding changes. Credit terms remain dated legal and commercial matters, not a modern credit-score formula.

## CAN-CE1-12.3 Capital and shareholder structures
Ownership percentage, voting control, dividend rights, governance, management authority, legal form and personal exposure must not be collapsed. Corporations hold their own assets, cash, debts, charters, shares and board/manager authority. Majority ownership does not allow unconstrained use of corporate funds for other personally owned enterprises, especially when minorities and creditors exist. Legitimate railway and related corporations may reinvest in traffic-supporting subsidiaries or development subject to charter, finance and governance. Equity dilution and sale consideration change actual ownership and proceeds, not a global empire score.

## CAN-CE1-12.4 Buyouts, notes, pledges and guarantees
An employee, manager or outside purchaser may finance an acquisition through their own lender, with the seller paid cash at closing while separately retaining a contingent guaranty or collateral support when law/terms allow. Third-party borrower debt, retained seller debt, retained equity, collateral pledge, co-borrower status and guaranty are different relationships. A seller with zero remaining equity may still be exposed as a guarantor. Liability arises or is enforced through contract triggers and proceedings; transfer of the Business does not erase surviving obligations. Seller and buyer preferences, certainty, speed, finance and competing offers matter.

## CAN-CE1-12.5 Bank liquidity, confidence and failure
Bank loans are assets that tie capital; customer deposits are payable liabilities. Liquidity posture, maturity mismatch, correspondent balances, loan concentration and depositor behavior matter. Rumors may change behavior through information and existing liabilities; a bank run is accelerated real withdrawal demand, not a random confidence card. Recovery, suspension, asset disposal and creditor arrangements follow actual funds and law. A profitable institution may fail from liquidity shortage while owning valuable noncash assets; holding high reserves may reduce earnings but improve resilience.

## CAN-CE1-12.6 Distress and debt continuity
Nonpayment can lead to negotiation, extension, added collateral, collection, bankruptcy where historically available, foreclosure, auction, receivership, partial salvage, liquidation or a sale. These are separate procedures with costs, delays, legal rules and counterparty choices. Distressed property may remain occupied, pledged or operational after enforcement begins. Commercial ruin considers remaining recovery choices rather than a universal automatic bankruptcy reset; modern bankruptcy mechanisms cannot be projected into periods when they did not exist.

## CAN-CE1-12.7 Federal bankruptcy chronology
Bankruptcy rules are dated: there is a material gap without an operating federal bankruptcy system from 1878 until the federal Bankruptcy Act of 1898 took effect. During that interval, relevant state/territorial creditor remedies, negotiations, assignments, attachments, foreclosure and private liquidation may still operate. Later federal proceedings must not be represented as modern Chapter 7 or 11.

**Source trail:** CAN XXIX pp.30-32; CAN XIV historical finance appendix p.327.

## CAN-CE1-12.8 Cooperatives, mutuals and pooled-capital institutions
Cooperative creameries, grain elevators, mutual insurance, financing and communication services arise where Persons or Businesses pool capital and accept real membership, governance, contributions, obligations and shared assets. A cooperative is an ownership/governance structure and operating relationship rather than a beneficial multiplier conferred by its label.

**Source trail:** CAN XXIX p.30.

## CAN-CE1-12.9 Specific security and debt instruments
Chattel mortgages, real-property mortgages, unsecured merchant balances, notes, receivables, guarantees, corporate shares and pledges remain separate obligations and interests with identifiable parties and collateral. Enforcement depends on instrument terms, maturity, notice, applicable law and prior interests; one generalized Debt flag must not imply identical creditor power over all assets.

**Source trail:** CAN XXIX p.29; CAN XXI financing doctrine.

## CAN-CE1-12.10 Multiple financial intermediaries
Banks, private lenders, merchants extending credit, note agents, building-and-loan associations, cooperatives and outside funders can coexist within the same catchment, each with their own instruments, information, capital, liquidity, governance and real legal authority. Lending and collection services need not follow a fixed institutional upgrade ladder.

**Source trail:** CAN XXIX p.29; CAN XXX pp.14-16.

**Principal source trail:** CAN XXXI pp.7–8; CAN XXX pp.14–16; CAN XXI pp.113–118; CAN XIV pp.302–305, 327–328; Technical XIV pp.9–10, 101 in preserved layer.

---

# Part XIII — Delegation, information and commercial knowledge

## CAN-CE1-13.1 The owner need not be the bottleneck
At the smallest scale a player may do physical labor and books personally. As commitments grow, a capable manager, foreman, general manager, clerk, bookkeeper, agent or supervisor can take work within actual authority, knowledge, time and competence. Managers and senior officials are roles rather than mandatory ladder ranks. One person can cover several units where travel/attention and competence permit, and an owner may deliberately manage zero ordinary businesses. Delegation does not multiply physical capacity or money.

## CAN-CE1-13.2 Standing instructions and meaningful autonomy
A one-time order, ongoing manager-judgment mandate and persistent owner lock are different instructions. Manager judgment can adjust schedules, orders, hiring, small repairs, pricing, customer work, legal/contract compliance and other authorized actions through real tasks and transactions. Important purchases, borrowing, pledges, guarantees, major disposals and ownership changes may require explicit authority or escalation. Limits operate through permissions/agreements and reports, not generic invisible autoplay. Managers can be wrong and may produce bad consequences.

**Exception-led owner governance.** Delegated managers may perform routine purchasing, staffing searches, work scheduling, ore sales within price/reserve bounds, collection and other ongoing operations while the player advances substantially longer time periods. No ordinary weekly work or small supplier quote requires a mandatory owner click after authority is delegated. Reserve material decisions such as out-of-policy credit, land acquisition, ownership dilution, major equipment or capital commitments, unsafe operations and manager authority changes according to *explicit owner policy*; permanent hiring may be owner-reserved if that is the chosen instruction. A profitable or safe state is never guaranteed merely because delegation is enabled. *Source: First Vein 2026-10-29 Part 1 and VEIN-UX-012.*

## CAN-CE1-13.3 Visibility into what managers did
Delegated work should be inspectable through material decisions, source evidence, governing policy/authority, new financial or resource commitments, outcome, exceptions and unresolved choices. Owner attention emerges from a manageable stream of actual matters needing judgment, not an AttentionPoints currency. Shared purchasing, bookkeeping, hiring, maintenance, transport and portfolio research can economize through real centralized people and systems; they do not generate a free corporate-office bonus. Capital allocation, liquidity and skilled manager assignment remain late-game strategic problems.

**Strategic pacing and exceptions.** Show compact, numbers-first summaries of cash, debt, payroll, inventory, operating days, production, sales, changes in risk and manager actions; expand detailed ledgers only on request. Subject to real pending commitments or urgent exceptions, a player may advance 30–90 days without approval-by-approval play. This is an interface goal, not a license to skip simulated worker time, transactions or cash movements. *Source: First Vein 2026-10-29 Parts 1 and 4.2.*

## CAN-CE1-13.4 Information is observer-dependent
Simulation truth, recorded facts, reputation from interactions, information held by a particular Person, rumor, inference and forecast are not identical. Who can know what depends on source, location, correspondence, observation, records, travel and communication time. A player's expertise may make better predictions, identify strong workers or notice financial problems, but it does not reveal unknown geology, hidden rivals or confidential household balances without credible evidence. Known transactions remain exact; uncertain appraisals and forecasts should carry uncertainty and date. Records and trained employees retain organizational memory when owners change, while knowledge can still be lost.

Historical events and public decisions may be true before local Persons learn of them; newspapers, mail, messengers, telegraph, personal travel and correspondence give different arrival times and credibility, and the player-facing event feed must not bypass Information Distance.

*Retained source precision: CAN-RET Historical news travels.*

Records, trusted managers, gossip and visible development can provide different forms of information; an inspected defect and a distant roof visible from the strategic camera are not necessarily the same evidentiary observation.

*Retained source precision: CAN-RET Property use knowledge is graduated.*

## CAN-CE1-13.5 Commercial memory survives differently across its carriers
Personal memories, bilateral dealing experience, recorded accounts and procedural knowledge embodied in staff are different forms of commercial continuity. Losing a proprietor may eliminate personal knowledge without erasing signed records or long-serving workers; a new owner does not automatically erase local stories or inherited customer trust. A universal permanent reputation scalar is not the causal source.

**Source trail:** CAN XIV pp.298-300.

## CAN-CE1-13.6 Awareness and reputational evidence are observer-dependent
A rumor, dated report, a witnessed failed delivery, a private account entry and an advertised claim do not convey identical knowledge or reliability. Player and NPC observers acquire evidence through permitted channels and only know information they plausibly received, subject to age, source and possible misunderstanding.

**Source trail:** CAN XIV pp.298-300; CAN XXIII information authority.

**Principal source trail:** CAN XXXI pp.4–5; CAN XXX pp.10–15; CAN XVII pp.157–201; CAN XIV pp.312–316, 335–337; Technical XIV pp.3–4.

---

# Part XIV — Institutions, laws and intentional risk

## CAN-CE1-14.1 Law is dated, located and specific
Legal regimes differ across Dakota Territory, South Dakota after statehood, federal jurisdiction, town/county institutions, private land interests and the institution's form. Interest limits, liquor trade, land entry, property tax, title transfer, security interests, medical/pharmacy authority, corporate powers, gambling, rail and public works can change with year and locality. A discovered law is part of institutional reality, not a universal ability unlocked by calendar progress. A legal-reference catalog later supplies dates, jurisdictions, sources and uncertainty while Canon retains these governing mechanics.

## CAN-CE1-14.2 Possible, lawful and accepted are distinct questions
Some conduct is physically feasible but legally unauthorized; other conduct requires access, title or institutional powers to perform at all; still other conduct depends on the willing response of employees, sellers and counterparties. Existing Canon allows historically meaningful intentional illegality where action can physically and institutionally occur, with detection, records, enforcement, disputes and reputational effects arising through ordinary systems. This does not mean every illegal act is always performable or that fines and confiscation can occur without evidence, process and appropriate authority. Preserve the distinction pending careful Step 04 wording verification.

Dated law can define a violation immediately, but discovery, notice, service, investigation, travel, negotiation, court process and enforcement take time; an unlawful physical state may persist without the game treating the legal rule as either absent or instantly enforced.

*Retained source precision: CAN-RET Law and enforcement are different clocks.*

Good-faith mistaken claims, unknown title defects and knowing trespass should not be collapsed into one automatic response; what was known, represented and communicated informs negotiation, liability and enforcement.

*Retained source precision: CAN-RET A real legal constraint must not be an RPG lock.*

**Agreement evidence and remedies.** Spoken and written commitments, notice, witnesses, later performance, disputed records, priority, fixtures, removal rights and breach create different kinds of proof. A missing signed document does not universally make an otherwise possible arrangement void; enforceability and remedies depend on applicable dated law, evidence and process. Neither physical occupation nor personal belief creates good title. *Source: First Vein 2026-10-29 Part 5.7; law-specific conclusions require research.*

## CAN-CE1-14.3 Institutions and civic investment
Courts, schools, public records, authorities, roads, crossings, utilities, fire/health responses, rail promotion and local government use actual budgets, duties, legal powers and people. Civic roles can be secondary service, elected/appointed offices or formal employment rather than entirely new worker species. A private enterprise cannot own municipal authority simply because it funds a public project; it may contract, lend, donate land, perform construction or receive authorized concessions. Government borrowing, public aid and regulated utilities depend on dated law and institutional capacity rather than TownLevel.

## CAN-CE1-14.4 Indigenous people and the historical frontier
Indigenous polities and Persons are historically relevant actors and holders of rights, knowledge, lands, trade networks and relationships; they are not a generic hostile obstacle or a mechanic that disappears because settler population rises. Research community, era, jurisdiction, treaty/reservation context and geography before authoring a specific interaction. A scenario's historical skeleton does not let procedural land generation erase existing Indigenous interests, and markets can include lawful exchange and local expertise with proper provenance.

## CAN-CE1-14.5 Illegal but physically possible actions
A physical production method, a lawful institutional authority, and another Person's willingness are separate questions. Where the period system supports attempted unlawful conduct, the player or an NPC may deliberately attempt it without a hidden universal ban, and enforcement, contract invalidity, evidentiary consequences, risk, reputation and counterparties react through dated LegalRegime and ordinary world systems. This does not override genuinely impossible physical acts or automatically require staff cooperation.

**Source trail:** CAN XXX 5.7; CAN XXXI 1.4 and 3.1; TECH XIV preserved XIII legal distinctions.

**Principal source trail:** CAN XXXI pp.1–2, 5, 8; CAN XXX pp.15–16; CAN XXV pp.72–82; CAN XXIX pp.21–37; CAN XVII pp.202–208; CAN XIV pp.243–247, 253–259, 327–328.

---

# Part XV — Environmental chronology, historical change and authored events

## CAN-CE1-15.1 Historical timeline versus game causality
The normal historical window 1870–1905 includes actual federal/territorial/state law, major river and rail geography, outside markets and broad technological chronology. Historical facts may enter as immutable background, dated legal change, availability of routes/technology, information channels, or authored historically named events. They must not force a local railway, enterprise or town to appear if the simulated actor lacks finance, property rights or construction capacity. World and player actions can plausibly change local outcomes without rewriting the date that a federal law took effect.

## CAN-CE1-15.2 Scenario Historical Events setting
Where named historical-event authoring is used, the Historical Events setting defaults ON. OFF prevents authored named events/regimes from injecting date-triggered causal inputs, and advanced scenarios may expose individual override controls. Turning it off does not eliminate recession, flood, drought, fire, disease, bank failure, crop failure or other endogenous outcomes from normal economic/physical rules. Settings persist with scenario/save state and never silently reverse transactions, deaths, projects or contracts after an event has taken effect.

## CAN-CE1-15.3 Event influence uses real channels
The Panic of 1873 may tighten available credit, undermine banks or railway finance, postpone jobs/projects, change migration and shift demand; it must not directly post a blanket revenue penalty. A strong snow/ice season affects transport, animals, heating, work and stocks before prices respond. Similar reasoning governs grasshopper shocks, severe winters, railroad expansion, regional agricultural booms and later financial panics. Exact occurrence, geography, magnitude, duration, local exposure and cited historical evidence belong in the future Historical Timeline & Events reference, not as invented global constants in the core Canon.

## CAN-CE1-15.4 Information and modernization
Technology and political change should be felt through people and markets: a telegraph connection changes the latency of intelligence, rail approaches change feasible markets and infrastructure needs, machinery changes work methods and consumables, and better record networks can change diligence. These developments do not create permanent, universal modernity buffs. Historical access may be early in a well-connected hub and late in an isolated settlement despite identical calendar dates.

**Principal source trail:** CAN XXXI pp.6–7; CAN XXV pp.72–82; CAN XXIX pp.21–37; CAN XIV pp.306–311, 328–329; Technical XIV p.6.

---

# Part XVI — Scenarios, ownership predicates and trustworthy objectives

## CAN-CE1-16.1 Scenario goals measure achievements
A scenario provides starting circumstances, particular time limits, victory/defeat rules and an observational objective list. It must not create buyers, independent entrepreneurs, housing occupants, deliveries, lenders, construction resources or capital simply to ensure success. Difficulty arises from the actual opportunities, competitors, conditions and limits of the same underlying economic world. Scenarios may require hands-on first contract, later hired-labor work, actual new-building completion, independent contractors, portfolio scale, bank ownership, freight volume or lasting settlement condition without turning those targets into strategy permission gates.

Scenario goals should generally describe observable results such as completed work, qualifying freight, settlement durability and deliveries rather than unnecessary asset-shopping checklists; difficulties should arise from world economics and the stated predicate, not designer-imposed strategy restrictions.

*Retained source precision: CAN-RET Objectives measure ends, not prescribe strategy.*

**Precise scenario ownership and accounting.** Every goal specifies period/deadline, actor/ownership perimeter, observable source event, cumulative/current/maintained duration, cash-versus-invoice recognition, unit conversions, exclusions and valuation basis. In THE FIRST VEIN only, the authored scenario has exactly seven milestones; the removed $400-in-six-months milestone must not return. Its 1890 conceptual 6/7 score is illustrative, not a Unity validation. *Source: First Vein 2026-10-29 Part 2.4; separate active Scenarios reference.*

## CAN-CE1-16.2 Event, current and maintained predicates
A completed historical-event goal usually remains achieved even after the qualifying asset is sold, unless the authored goal explicitly requires continuing control or condition. A current ownership or operating goal must reevaluate at the time the objective specifies. Maintained employment, population, shareholding or service predicates must prove actual sustained intervals with persisted evidence, not signatures, rounded UI snapshots or invented narrative continuity. Independent NPC business goals must exclude disallowed player-owned affiliates; 50/50 partnership, wholly owned, majority-controlled and third-party ownership are distinct.

No universal rule currently declares everything visible from the strategy camera fully known to the player; distinguish apparent visible development, information supplied by staff or records, and details requiring direct inspection, while retaining this exact boundary as an unresolved design decision.

*Retained source precision: CAN-RET Strategic-camera knowledge remains an open design hold.*

## CAN-CE1-16.3 Valuation and financial objective precision
Scenario-defined owner equity states explicitly whether owner funds, share value, supplied starting assets, debt, mortgages, securities, personally owned horses/tack, business cash and real estate are included, and on what basis valuation is measured. Loan proceeds without associated liability reduction are not equity. Cost/book basis, appraised value and realized market proceeds must not be interchanged silently. Objective evidence should survive save/load and business sale when historical-event semantics require it; erroneous records need audit-proven reconciliation.

A scenario conclusion should summarize the actual economic journey and achievements, potentially with short cinematic/narrative presentation, without fabricating accomplishments, forcing a historical future or using celebration UI to alter objective evidence.

*Retained source precision: CAN-RET Scenario completion is recognition, not causation.*

## CAN-CE1-16.4 Playtesting is evidence, not new law
Saloon Circuit, Enterprise Ladder, The Contractor, The Market Town, The Crossing and other autonomous or human playtests can reveal rules, defects, credible strategies and missing information. Their generated prices, success timeline, budget, staffing, settlement counts or particular build path do not automatically become mandatory or universal. A scenario/test fixture belongs in a separate reference; the Canon preserves the general principle proven by the test. Narrative claims cannot overwrite unreconciled numbers.

**Principal source trail:** CAN XXXI pp.1, 7–9; CAN XXX pp.18–20; CAN XXII pp.108–112; CAN XXI pp.113–118; CAN XX pp.119–124; CAN XIV pp.240–252, 321–325.

---

# Part XVII — Player interface, legibility and practical scope

## CAN-CE1-17.1 A numbers-first, world-supported interface
Present actionable facts—cash, expenses, wages, debt, stock, available resources, contracts, construction progress, occupancy, demand evidence, opportunity risks—without forcing players into accounting or staffing micromanagement for every routine event. Important events should have a visible world correlate when observed locally: an actual customer enters, a shipment arrives, a worker travels or an asset is used. The UI compresses authoritative truth and should make distinctions such as total cash versus deployable cash, quantity on hand versus reserved, booked versus paid and owned versus leased intelligible.

**Accessible action discovery.** Provide searchable ordinary action terminology and a structured proposal builder for supplies, referrals, services and offers. A mandatory general-purpose language model or bespoke dialogue tree for every possible contract is not required. Explain period terms such as assay, gold-bearing quartz, ore, tailings, claim, title, interest and seller note in plain language and reflect unknown information faithfully. *Source: First Vein 2026-10-29 Parts 1 and 4.2; VEIN-UX-011.*

## CAN-CE1-17.2 Information honesty and forecast uncertainty
Display known exact figures precisely in appropriate units; do not manufacture numerical uncertainty around a posted invoice. Estimates, hidden skill, forecasts, property value, rival intent and market potential should indicate evidence quality. Explain the cause of a rejected action: missing workspace, stock, available worker, permission, route or legal standing—and keep undiscovered private information undisclosed. Alerts should prioritize actionable exceptions, not every person movement or mundane business action. Reporting at portfolio scale should expose what managers decided and which items need owner attention.

Per-site and per-activity views should show contribution from external sales, cost allocation, labor time, machine utilization, working capital, cash constraints and capacity bottlenecks; those are analytical attributions, never parallel sources of posted company profit.

*Retained source precision: CAN-RET Segment reports explain capacity and finance.*

Maintain precise numeric simulation state when useful, but ordinary explanations should describe causes, severity and expected practical consequences instead of presenting every outcome as a floating efficiency or fatigue percentage modifier.

*Retained source precision: CAN-RET Customer-facing feedback avoids naked percentages.*

## CAN-CE1-17.3 Player freedom without busywork
Do not make players place every product on individual shelves, simulate each employee's minute-by-minute sleep or prescribe daily clerical rituals to earn a basic profit. Normalized allocation, standing orders, schedules, competent staff, coarse travel simulation and other abstractions are acceptable if they conserve physical resources, obey time and maintain meaningful choices. Do not make a real railroad dispatch simulator or an unrestricted modern finance simulator a prerequisite for a grounded frontier economy. Physical and social consequences should be rich, while navigation and feedback should remain approachable.

## CAN-CE1-17.4 Different levels of responsibility remain fun
Manual owner labor is a viable early strategy; delegating operations should not be punished by hidden efficiency penalties, nor should the player be forced to retain a business forever after fulfilling a historical accomplishment. A more mature portfolio invites decisions about acquisitions, debt, reserves, development, equity, senior staff, competitors, freight and market positioning. Scenario setup, optional modes and UI workflows may vary, but the underlying commercial logic remains one coherent economy.

**Principal source trail:** CAN XXXI pp.1–8; CAN XVIII audit pp.157–201; CAN XIV pp.312–316, 331–345.

---

# Part XVIII — Boundaries, historical references and retained holds

## CAN-CE1-18.1 Specialist research moves, governing mechanics stay
A separate document may store 112 occupation profiles and workforce weights; another may store work-rate ranges and named 1870–1905 construction examples. A historical-timeline catalog should hold event chronology, evidence, geographic reach and uncertainty. A legal reference should hold dated statutory rules and land-rights specifics. An economy report should hold observations of wages, trade units, prices, materials and grades. Their findings support gameplay but cannot grant or remove Business capabilities. Moving research must preserve the cited source and original uncertainty; it never means deleting the underlying game rule.

An autonomous or textual playtest with unreconciled, contradictory or voided checkpoints cannot silently continue claiming exact cash, inventory, ownership or objective values; replay, recorded correction or explicitly non-authoritative soft continuity is required.

*Retained source precision: CAN-RET Incomplete checkpoint state cannot be promoted.*

## CAN-CE1-18.2 Live issues are explicit
The distinction between feasible, legal and willingly performed needs exact phrasing approval. Finance-office-to-banking-house development is a historically plausible route, not a prescribed advancement ladder. Wage employment should retain one employment authority while family labor, owner labor and independent contracts remain properly distinguishable. The occupation report's 112 profiles are a proposed 1.0 catalog, not a proven immutable limit. Detailed legal powers and period economic numbers remain research holds. Whether actual Unity code complies requires an external repository audit, not the Canon text alone.

## CAN-CE1-18.3 Formal scope of this draft
This document is a **substantive unified gameplay draft**, not the completion of the comprehensive no-loss audit. Rule references and historical details retained only as broad summaries still require paragraph-level comparison in Step 04. Nothing here changes the archived Canon XXXI, modifies Unity code, completes the Technical successor or promotes pending proposals 106–145. The next review should approve section direction and mark omissions/conflicts for verification.

## CAN-CE1-18.4 Coverage and source-selection bias
Census employment and manufacturing tables, county histories, directories, newspapers and surviving contracts record selected observations, not a complete inventory of every wage worker, unpaid family contributor or enterprise. The historical occupation reference's model-derived weights are calibration priors, never compulsory town job quotas or evidence that everyone not gainfully employed was idle.

**Source trail:** OCC reference pp.1-2, 10-11; CAN XXXI calibration doctrine.

## CAN-CE1-18.5 Provenance classes for evidence, estimates and rules
Maintain explicit distinctions among dated historical observations, retrospective sources, comparable-region analogues, model-derived synthesis envelopes, individual playtest numbers, optional scenario inputs and formally adopted canonical mechanics. Preserve the original date, geography, measurement unit, assumptions and uncertainty whenever transferring a numerical claim into a specialist reference.

**Source trail:** WORK reference pp.1-3,15-16; OCC pp.1-2,10; CAN XXXI 1.1.

**Principal source trail:** Canon XXXI all preserved layers pp.1–346; Technical XIV pp.1–170; occupation reference pp.1–18; historical work reference pp.1–18; Step 02 reconciliation crosswalk and decision holds.

---
