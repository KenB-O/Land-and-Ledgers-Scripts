# LAND & LEDGERS
## TECHNICAL IMPLEMENTATION BIBLE — INTEGRATED WORKING EDITION v0.4

**Current documentation library:** v2.3 · **Updated:** 31 October 2026 · **Scope:** target architecture, transitions and executable acceptance requirements.

**Main technical reference:** This is the *one active working implementation specification*. THE FIRST VEIN lessons are placed in their proper architectural domains, not a separate handoff or addon. The companion Canonical Bible describes desired gameplay; actual Unity repository code and passing tests prove implementation status.

**Authority/verification boundary:** Preserved Technical XIV remains a source baseline for earlier details omitted in consolidation. Full clause-level semantic no-loss certification is still open. This edition governs intended integration of the newest design requirements; do not assume any named class, field, code fix or scenario exists in the live repository. Keep proposed requirements distinct from implemented features.

**Mandatory P0 gate:** Inspect `CreateBlockedShipment(...)` / shipment failures in the actual repository, establish unique cargo state and exactly-once settlement, and test failure, retry, cancellation and save/load before declaring conservation safe.

**Decision status:** 001–105 approved; 106–145 pending as independent proposals. THE FIRST VEIN is a *conceptual* playtest and its fictional 6/7 score is never an executed test result.

---

# Part I — Universal technical invariants

1. **Single writer per authoritative fact.** Every mutable economic or physical truth has an identified owner; read models and UI may summarize, never compete.
2. **Conservation and exactly-once settlement.** Physical inventory, cargo, money, shares and other claims change only through authoritative transitions with durable IDs.
3. **Rights and physical feasibility are independent.** A descriptive Business or occupation label may not deny a feasible act; worker consent, legality and ownership authority remain separate checks.
4. **One Person, one physical time budget.** Work, travel, household tasks and management requests cannot consume impossible simultaneous time.
5. **Information is perspective-specific.** Private simulation truth does not automatically enter an observer's knowledge.
6. **Events drive reports.** Market demand, health, stress, rivalry, opportunity and historical event UI cannot invent transactions.
7. **Versioned persistence and reproduction.** Save, reload and fast-forward should preserve identity, causal provenance and outstanding obligations without double firing.
8. **Date and jurisdiction matter.** Historical prices, law, technology, routes and services are authored records with effective scope, not scattered global constants.

# Part 2 — Sources, boundaries and definitions

Technical XIV provides target responsibilities; Canon XXXI controls intended gameplay. A conceptual class name is a proposed role, not evidence of current Unity implementation. Each domain must identify a single authoritative owner of mutable state, its derived read models, commands or intents, emitted economic/physical events, persistence schema, and invariant-oriented tests. Historical, legal and calibration data are authoring inputs with time, geography and evidence metadata rather than code constants.

## TECH-CE1-T001 — Business authority

**Intended authority:** BusinessState (conceptual).

**Responsibility:** Business identity/governance/cash links/contracts.

**Normative invariant:** BusinessOperation / BusinessType must not grant permissions.

**Acceptance fixture:** Generic business: add retail/forge under one identity, rename label no effect.

**Canonical link:** `CAN-CE1-02.1`. **Original reference trail:** TECH XIV pp.2–3. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T008 — Cash balances

**Intended authority:** BusinessCash/OwnerCash/BankCash.

**Responsibility:** Separate owner, Business, corporation, customer deposit balances.

**Normative invariant:** No report/UI cash as second posting authority.

**Acceptance fixture:** Transfer owner contribution and book business expense.

**Canonical link:** `CAN-CE1-02.3`. **Original reference trail:** TECH XIV preserved I/II/XIII. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T009 — Economic events and exact settlement

**Intended authority:** EconomicEvent/Transaction.

**Responsibility:** Transactions, invoices, claims, corrections.

**Normative invariant:** Known amounts exact and immutable; correction explicit.

**Acceptance fixture:** Retry identical payment/save/load no double settlement.

**Canonical link:** `CAN-CE1-02.4`. **Original reference trail:** TECH XIV p.6 + preserved IV. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Internal contribution analytics may allocate real wages, inventory, asset utilization and project burden with disclosed assumptions; segment reports cannot post extra consolidated revenue or owner equity.

*Retained source precision: TECH-RET Segment read models cannot mutate ledger truth.*

**Additional transaction contracts (First Vein):** Preserve IDs and state for offer -> order -> reservation -> custody transfer -> physical movement -> acceptance/assay -> invoice -> payment/remittance or rejection. Distinguish beneficial owner, legal owner, physical custodian, payer and obligated principal; retry/save/load cannot duplicate economic events, receipts, goods or offsets. The same authorities serve gold ore, retail, consignment and customer-owned custom processing. *Design evidence: First Vein 2026-10-29 Parts 4–6.*

# Part 3 — Person, employment, time and action

Person identity is stable across age, job, residence, kinship, travel, health and knowledge. EmploymentRelationship is an employer/wage/terms relationship, not all family work, contract services or owner labor; person activity/time and task assignment must be exclusive across domain clients. Production feasibility derives from actual method prerequisites and worker agency independently resolves willingness and contract compliance. Parallel work requires independent Persons, physical work stations and reasonable transit.

## TECH-CE1-T002 — Person identity

**Intended authority:** PersonState.

**Responsibility:** Person/household/work-history continuity.

**Normative invariant:** Age/occupation/job changes preserve same PersonId.

**Acceptance fixture:** Job change then save/load; all history intact.

**Canonical link:** `CAN-CE1-03.1`. **Original reference trail:** TECH XIV preserved VIII/IX. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T003 — Employment

**Intended authority:** EmploymentRelationship.

**Responsibility:** Single conventional employment relationship.

**Normative invariant:** No split independent wage/employer/job truth; nonwage work separate.

**Acceptance fixture:** Change wage and assignment, verify one employer authority.

**Canonical link:** `CAN-CE1-03.4`. **Original reference trail:** TECH XIV preserved IX and XIII. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T004 — Work attempt and response

**Intended authority:** Task/Person.

**Responsibility:** ProductionMethod + TaskAssignment + PersonResponse.

**Normative invariant:** Physical attempt distinct from skill/occupation; Person can refuse.

**Acceptance fixture:** Novice at forge; unsafe worker refusal.

**Canonical link:** `CAN-CE1-03.2`. **Original reference trail:** TECH XIV pp.2–3. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T005 — Physical and person time

**Intended authority:** PersonActivity/Task/Travel.

**Responsibility:** Exclusive Person time and location scheduling.

**Normative invariant:** No incompatible simultaneous labor, commute, shopping or management.

**Acceptance fixture:** Attempt two concurrent jobs and physical travel.

**Canonical link:** `CAN-CE1-03.3`. **Original reference trail:** TECH XIV preserved VI/IX. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T006 — Shared resource reservations

**Intended authority:** Reservation/Task.

**Responsibility:** Exclusive worker, machine, animal, space and inventory lot reservations.

**Normative invariant:** No cross-domain double booking.

**Acceptance fixture:** Forge+nails+customer repair with one smith.

**Canonical link:** `CAN-CE1-06.1`. **Original reference trail:** TECH XIV p.3. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

# Part 4 — Production, inventory and procurement

Producible objects list physical ProductionMethods and can be made by any Business that can obtain the required materials, equipment, usable space, time, workers, energy and access. An input lot becomes output batches and byproducts with conserved, versioned provenance; batch transformation never cleans spoiled or unsafe history. Offers are separate from on-hand lots, reserved inventory and physical receiving space. Supplier quotations, packages, prices, credit/deposit terms, shipped goods and landed cost come from actual economic counterparties.

## TECH-CE1-T007 — Production methods

**Intended authority:** ProductionMethod/ProcessingBatch.

**Responsibility:** Input/method/transform/output/byproduct state.

**Normative invariant:** No recipe whitelist from BusinessType.

**Acceptance fixture:** Use saw in mixed merchant for lumber lots.

**Canonical link:** `CAN-CE1-06.2`. **Original reference trail:** TECH XIV pp.2–3. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

For medicines, retain manufacturer, formulation, advertised claims, actual modeled effect/risk, legal authority, lot state and supply history; marketing text does not mechanically change treatment efficacy.

*Retained source precision: TECH-RET Claims and medicine outcomes stay distinct.*

## TECH-CE1-T011 — Inventory location and offers

**Intended authority:** InventoryLot/RetailOffer.

**Responsibility:** Lots by physical site, allocation, offers and custody.

**Normative invariant:** Cannot sell absent stock or duplicate display+warehouse capacity.

**Acceptance fixture:** Mixed shop sells own nails and resells external stock.

**Canonical link:** `CAN-CE1-07.1`. **Original reference trail:** TECH XIV p.2 + preserved IX. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T012 — Supplier procurement

**Intended authority:** SupplierOffer/ProcurementNeed.

**Responsibility:** Supplier quote, terms, landed cost, shipping.

**Normative invariant:** No anonymous item->infinite instant stock.

**Acceptance fixture:** Order distant goods and trace carrier+payment.

**Canonical link:** `CAN-CE1-07.2`. **Original reference trail:** TECH XIV preserved IX. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T013 — Storage capacity

**Intended authority:** FunctionalSpace/StorageReservation.

**Responsibility:** Physical capacity and custody grants.

**Normative invariant:** Warehouse identity does not create empty storage.

**Acceptance fixture:** Carriage firm without warehouse cannot warehouse cargo.

**Canonical link:** `CAN-CE1-07.3`. **Original reference trail:** TECH XIV p.5. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T014 — Ownership vs custody

**Intended authority:** CustodyAgreement/WarehouseReceipt.

**Responsibility:** Property interest and custody and receipt records.

**Normative invariant:** Holding customer grain does not transfer title.

**Acceptance fixture:** Deposit, pledge and withdraw grain.

**Canonical link:** `CAN-CE1-07.6`. **Original reference trail:** TECH XIV preserved XII. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

**Toll processing:** A mill can possess and consume input ore in an agreed production method without purchasing its customer's ore or recovered bullion. Track customer-owner and mill-custodian through transport, processing, recovered-product disposition, payable fees and waste; record commission income separately from gross merchandise sale. *First Vein source: Part 5.3.*

## TECH-CE1-T037 — Geological evidence, extraction and assay

**Intended authority:** MineralDeposit/Observation + Sample/AssayResult + MiningWorkPackage + InventoryLot; names conceptual, reuse existing source authorities when found.

**Required behavior:** Geologic potential is locally variable and partially unknown. Selected specimen, in-situ representative sample and mined feed are independent events with source/provenance, coordinates, grade distribution and confidence; sampling never directly creates sellable ore. Workers and equipment extract physical lots through supported safe access and time, including loss, tailings, unusable rock and potential unprofitable deposit.

**Acceptance:** High-grade specimen beside low-grade deposit does not manufacture mineable grade; two independent in-situ samples plus assayer event meet only their exact proof predicate; equipment/labor shortages suspend extraction. **Status:** UNVERIFIED IN UNITY. *Source: First Vein 2026-10-29 Parts 4.3, 5.3 and 6.*

## TECH-CE1-T038 — Recovered metal identity and accounting

**Intended authority:** InventoryLot + ProcessingJob/ProductionMethod + Agreement + EconomicEvent.

**Required behavior:** Mass/grade and precious-metal units are conserved from extraction through buyer raw-ore disposition or toll/owned milling, recovery, tailings, bullion, actual sale and collection. A processor's cash is service fee unless it actually buys the commodity. Recovered-and-sold owner troy ounces are recorded only after both recovery and legally effective completed sales by the eligible owner, under objective-specific recognition semantics.

**Acceptance:** Raw ore sale contributes zero owner recovered-and-sold ounces; duplicate recovery or sale replay is rejected; processing returns byproducts and customer-owned metal without cloning assets. **Status:** UNVERIFIED IN UNITY. *Source: First Vein Parts 2.4, 5.3.*

## TECH-CE1-T039 — Agreement evidence, fixtures and dispute chain

**Intended authority:** Agreement/Performance/Notice/LegalCase/PropertyInterest records, not hard-coded oral-void/signed-valid booleans.

**Required behavior:** Store offered oral/written terms, parties and representatives, witness/communication evidence, performance, amendments, recorded priorities, leased improvements, permissible fixture removal/compensation, service of notice and remedies. Applicable dated law determines enforceability and procedure independently of whether a formal paper attachment exists.

**Acceptance:** Enforceable agreement may rely on corroborated conduct; recorded lien cannot be erased when another actor occupies space; improper ownership claims are challengeable, not secretly granted. **Status:** UNVERIFIED IN UNITY. *Source: First Vein Parts 4.3, 5.5–5.7.*

## TECH-CE1-T040 — Scenario-specific governance and nonbinding preferences

**Intended authority:** ScenarioDefinition/StandingInstruction/OwnerPolicy plus normal Business governance.

**Required behavior:** THE FIRST VEIN may initialize owner 100% of Juniper No. 1 and a preference for owned mill and fee land, seller-financed low down payment when offered, six-day mine operations when safe, major-loan and new-title owner reservations, ordinary ore-sale delegation and long simulation jumps. Do not hard-code these into all campaigns or silently sell equity to solve financing.

**Acceptance:** Owners may change policies, others may choose leases or partners, no routine weekly approvals once delegated, and no manager crosses owner-locked threshold. **Status:** UNVERIFIED IN UNITY. *Source: First Vein Parts 1 and 4.2.*

# Part 5 — Cargo conservation and transport

Cargo shipment creation must either reserve and consume source stock atomically upon successful loading or remain a nonloaded request; it may not both retain loaded cargo and restore source stock. Blocked or delayed shipment continues as an obligation/intent with explicit physical lot location and status. Journey, shipment and information routing can use a common real geographic topology but each retains its own participants, commodities and payment truth. Multimodal transfers require physically meaningful handling, storage/custody and scheduled carrier capacity.

## TECH-CE1-T010 — Shipment integrity

**Intended authority:** LogisticsShipment/InventoryLot.

**Responsibility:** Shipment, cargo lots, staged/loading state.

**Normative invariant:** No fabricated loaded cargo with restored seller stock.

**Acceptance fixture:** CreateBlockedShipment, reload, check physical conservation.

**Canonical link:** `CAN-CE1-07.8`. **Original reference trail:** TECH XIV preserved IV p.9. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

A blocked shipment must not hold a fabricated loaded copy of cargo while the origin stock is restored; test all failure paths and save/load persistence before declaring the bug repaired.

*Retained source precision: TECH-RET Blocked shipment fix remains P0 and untested.*

**Mineral shipment fixture:** A prospect lot, stockpile, loaded wagon, outbound trip, buyer acceptance and returned/rejected shipment refer to one conserved mass and durable lot identity. A blocked dispatch must never restore stock and leave a second loaded cargo alive. Test loss, failure, reroute, seller stock restoration, cancel and replay. *Source: First Vein 2026-10-29 Parts 4.3 and 6; original Technical XIV preserved Rev IV.*

## TECH-CE1-T026 — Transport graph

**Intended authority:** RouteEdge/TransportService.

**Responsibility:** Road/rail/water route edges and transfer nodes.

**Normative invariant:** No geometric teleport or capacity-free offmap.

**Acceptance fixture:** Intermodal cargo wagon->depot->rail->wagon.

**Canonical link:** `CAN-CE1-10.1`. **Original reference trail:** TECH XIV preserved VIII. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Keep authoritative geographic positions, path distances and route-time economics consistent across loaded scenes, floating-origin offsets and coarsened simulation; streaming must not shrink travel cost or make an off-map journey artificially short.

*Retained source precision: TECH-RET Economic route distance remains 1:1.*

## TECH-CE1-T027 — Rail lifecycle and corporate ownership

**Intended authority:** RailSegment/Company/Service.

**Responsibility:** Segment, works, access rights, service, operating company.

**Normative invariant:** Track laid != commercially open service.

**Acceptance fixture:** Laid track but no terminal/train keeps route closed.

**Canonical link:** `CAN-CE1-10.3`. **Original reference trail:** TECH XIV preserved VIII. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

# Part 6 — Property, space and construction

A parcel is legal/spatial land identity; a Building is a physical constructed object; FunctionalSpace is physically usable subarea, AccommodationSpace is a livable unit or shelter, and UseRelationship/Occupancy/Lease is the authority for actually using it. Mixed use and multi-site Businesses must not duplicate rooms, employees or capital. Construction Projects are collections of ordered WorkPackages and scheduled Tasks; payment claims, weather delay, material stockpiles, worker time, inspection and opening are different transitions.

## TECH-CE1-T015 — Land rights

**Intended authority:** Parcel/PropertyInterest.

**Responsibility:** Parcel, claim, patent, mortgage, easement, restriction.

**Normative invariant:** Unoccupied parcel not free title.

**Acceptance fixture:** File then prove claim under dated law.

**Canonical link:** `CAN-CE1-05.4`. **Original reference trail:** TECH XIV preserved XII. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Persist claimant, tract, law/program, evidence of residence and improvements, filing deadlines, contests, proof, fees and patent/forfeiture separately from fee-simple ownership; do not infer patent from a submitted application.

*Retained source precision: TECH-RET Public-land entry is a persistent case.*

Represent cash tenancy, share tenancy, owner-operation and partial ownership using distinct property and farming relationships with real crops/shares and agreements; tenant production does not itself post wages.

*Retained source precision: TECH-RET Farm tenure is its own relationship.*

Property opportunities refer to actual parcel/property and owners or explicit unknown/disputed facts, discovery source, discovered date, asking terms and restrictions; market listings must not invent a fictitious seller or vacant title.

*Retained source precision: TECH-RET Property listing discovery must have provenance.*

**Property and mineral interests:** Distinguish legal claim location/maintenance, unpatented mineral claim, millsite claim and use, patent/fee title, recorded deed, seller-financed purchase, lease, easement, access, fixtures, removable improvements, water/tailings rights and recorded mortgages. Recording and evidence vary by jurisdiction/year. An unpatented site does not automatically grant unrestricted fee title; seller willingness and funds required for closing are actual state. *First Vein source: Parts 1, 5.5.*

## TECH-CE1-T016 — Buildings, premises and use

**Intended authority:** Building/Space/Agreement.

**Responsibility:** Building -> FunctionalSpace -> UseRelationship.

**Normative invariant:** Building type does not dictate business identity.

**Acceptance fixture:** Store below rental lodging in same structure.

**Canonical link:** `CAN-CE1-05.2`. **Original reference trail:** TECH XIV pp.4–5. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T017 — Construction project lifecycle

**Intended authority:** Project/WorkPackage.

**Responsibility:** Project, WorkPackage, Task, material staging.

**Normative invariant:** Not one building-type timer, and completion separate from use.

**Acceptance fixture:** Pause for funding, resume with preserved built work.

**Canonical link:** `CAN-CE1-06.4`. **Original reference trail:** TECH XIV preserved XI. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

After contractor failure, issue new Agreements/WorkPackages to replacement providers while preserving the original project baseline, performed work, damaged materials, site custody, unsettled liabilities and disputed claims.

*Retained source precision: TECH-RET Contractor replacement preserves site history.*

Internal construction may retain true labor/material/freight/specialist/allocated-overhead costs in the receiving asset basis; intercompany billing eliminates at consolidation and never creates manufactured profit.

*Retained source precision: TECH-RET Partial projects need real cost capitalization.*

## TECH-CE1-T018 — Asset wear and repair

**Intended authority:** Asset/WorkOrder.

**Responsibility:** Component condition, defects, evidence and WorkOrder.

**Normative invariant:** Condition and known defects not one universal HP.

**Acceptance fixture:** Inspect wagon, work order repair, preserve defect.

**Canonical link:** `CAN-CE1-06.3`. **Original reference trail:** TECH XIV preserved I. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

# Part 7 — Households, residence, demography and migration

Settlement populations derive from persistent Person identities and authoritative residence/occupancy records; transient presence, displaced persons and temporary accommodation remain distinguishable read models. Family work and domestic duties compete for real Person time without creating fictitious wage employment. Migration requires exposure/awareness, evaluation, travel, housing/job fit, and later stabilization, never direct adjustment of a town population counter. Birth, marriage, divorce, death, guardianship and estate succession preserve relationship and property history.

## TECH-CE1-T019 — Household and accommodation

**Intended authority:** Person/Household/ResidentialOccupancy.

**Responsibility:** Person, Household, Occupancy, AccommodationSpace.

**Normative invariant:** No independent settlement resident counter.

**Acceptance fixture:** Displace family after fire; residents recompute.

**Canonical link:** `CAN-CE1-04.1`. **Original reference trail:** TECH XIV preserved V/VI. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T020 — Demographic lifecycle

**Intended authority:** Person/Kinship/Event.

**Responsibility:** Birth, death, marriage, dependency and kinship case.

**Normative invariant:** Death does not erase estate/assets/dependents.

**Acceptance fixture:** Guardian reassignment without inheritance transfer.

**Canonical link:** `CAN-CE1-04.5`. **Original reference trail:** TECH XIV preserved VIII. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Animal health and mortality incidents can affect multiple animals through shared exposure to location, herd, feed, weather or disease; avoid independent identical per-animal dice rolls for a common outbreak.

*Retained source precision: TECH-RET Use correlated livestock exposure.*

Persist onset, symptoms/known diagnoses, underlying disease or injury where modeled, severity, contagion as appropriate, work-availability effects, intervention, follow-up, recovery/impairment/death outcomes and evidence links rather than a transient absence modifier.

*Retained source precision: TECH-RET Health episodes preserve longitudinal state.*

## TECH-CE1-T021 — Migration

**Intended authority:** Journey/Residence.

**Responsibility:** Awareness, decision, travel, stabilization and residence.

**Normative invariant:** Housing never spawns migrant Persons.

**Acceptance fixture:** Recruit migrant -> real journey -> resident count.

**Canonical link:** `CAN-CE1-04.3`. **Original reference trail:** TECH XIV preserved V/IX. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

# Part 8 — Information, contacts and markets

Information/Evidence records contain source, observed subject, observer, date received, medium, confidence and possible staleness. Recruitment derives from actual contact paths and communications and no arbitrary applicant pool can materialize Persons on request. Opportunity and market-pressure reports synthesize witnessed refusals, stockouts, housing mismatch, effective market reach, supplier disruptions and planned pipeline capacity; they neither post revenue nor spawn rival Businesses. Economic sales require actual buyer/seller decisions, payments, labor and stock.

## TECH-CE1-T022 — Information and knowledge

**Intended authority:** Information/Evidence.

**Responsibility:** Source/observer/time confidence records.

**Normative invariant:** No reveal of unobserved true skills/intent.

**Acceptance fixture:** Cold contact candidate hidden until credible lead.

**Canonical link:** `CAN-CE1-13.4`. **Original reference trail:** TECH XIV p.3. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

**Interaction discovery:** Provide an indexed ActionDefinition/ActionRegistry read model, filtered by observer evidence, relationships, access, contract terms and physically supportable behavior. A structured proposal builder supplies feasible terms, counter-offers, acceptance and failure without requiring a general-purpose LLM for every interaction. Unknown partners remain unknown until a real encounter, advertisement, messenger, letter or referral establishes contact. *First Vein Part 5.6.*

## TECH-CE1-T023 — Recruiting contact graph

**Intended authority:** ContactRelationship/Recruitment.

**Responsibility:** Known contacts, referrals, channels, offers.

**Normative invariant:** No omniscient applicant pool.

**Acceptance fixture:** Letter offer arrives after carrier/message delay.

**Canonical link:** `CAN-CE1-03.5`. **Original reference trail:** TECH XIV p.3. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T024 — Demand and Opportunity

**Intended authority:** OpportunityPressure (read model).

**Responsibility:** Event-fed refused sale, vacancy, transport load evidence.

**Normative invariant:** No direct spawning based on town score.

**Acceptance fixture:** Existing store grows before challenger entry.

**Canonical link:** `CAN-CE1-11.3`. **Original reference trail:** TECH XIV preserved X. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Prefer initialization dependency order geography/rights -> rural farms/households/output -> route access -> settlement catchment -> institutions/transients -> demand -> businesses/capabilities -> staffing; avoid generating business rosters and then inventing demand to justify them.

*Retained source precision: TECH-RET Generate world causally.*

## TECH-CE1-T025 — Market transaction embodiment

**Intended authority:** Activity/Transaction.

**Responsibility:** Customer Person activity and Business labor, cash, stock.

**Normative invariant:** No revenue from settlement population multiplier.

**Acceptance fixture:** Growth creates queue then real staff and purchase.

**Canonical link:** `CAN-CE1-07.4`. **Original reference trail:** TECH XIV preserved X. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

**Assay and mineral market:** Record sample method (selected vs representative), batch, laboratory/assayer independence, observed result and uncertainty; do not infer orebody grade from a specimen. Buyer quotations, grade/tare, discounts, milling fee, transport terms and risk must be kept separate from executed prices and final paid amounts. Raw ore sale does not count as recovered-and-sold owner gold. *First Vein Parts 2.4 and 5.3.*

# Part 9 — Finance, institutions and shareholder governance

Separate owner cash, Business cash, corporate treasury, deposit liabilities, loans held, third-party servicing, contingent guarantees and protected-liquidity calculations. Contractual credit can include interest, maturity, collateral, amendment, subordination, facility availability, default and actual lender diligence. A private finance office may perform multiple lawful activities before taking deposits, but there is no mandatory institution level. Banks respect liquidity, maturity mismatch, withdrawal pressure, correspondent access, deposit claims, insiders and capital. Corporate actions must respect governing agreement, capitalization, owner percentages and creditor/minority rights.

## TECH-CE1-T029 — Financial debt and guarantee

**Intended authority:** Debt/Guaranty/Agreement.

**Responsibility:** Loan, note, secured interest, guaranty, contingent liability.

**Normative invariant:** Selling business does not delete seller guaranty.

**Acceptance fixture:** Buyout lender-funded cash + surviving guaranty.

**Canonical link:** `CAN-CE1-12.4`. **Original reference trail:** TECH XIV preserved IV. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Persist CreditFacility with lender, limit, outstanding principal, undrawn capacity as derived, covenant, maturity, collateral and draw/repayment history; unused borrowing authority is neither cash nor existing debt.

*Retained source precision: TECH-RET Credit lines are not installments.*

## TECH-CE1-T030 — Bank and deposit liability

**Intended authority:** Bank/DepositAccount.

**Responsibility:** Depositor payable and lender assets/liquidity.

**Normative invariant:** Deposits not free owner cash.

**Acceptance fixture:** Deposit withdrawal stress and loan maturity mismatch.

**Canonical link:** `CAN-CE1-12.5`. **Original reference trail:** TECH XIV preserved XIII. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

BankPolicyProfile must cover liquidity buffers, loan pricing and eligibility, collateral/appraisals, concentration and connected borrowers, maturities, collections/enforcement, dividends, deposit terms, approval authority and reconciliation controls rather than a global Risk setting.

*Retained source precision: TECH-RET Bank policy must be real component decisions.*

Test a panic by submitting withdrawals on actual DepositAccounts and settling available bank cash, correspondent funds, liquidation, outside borrowing and dated suspension rules; Reputation cannot discharge cash liabilities.

*Retained source precision: TECH-RET Bank panic processes depositor claims.*

## TECH-CE1-T031 — Corporate governance

**Intended authority:** Corporation/CapTable.

**Responsibility:** Separate cap table, corporate capital/board and duties.

**Normative invariant:** No player treasury grab from minority company.

**Acceptance fixture:** Connected loan owner/corporation elimination only in reporting.

**Canonical link:** `CAN-CE1-12.3`. **Original reference trail:** TECH XIV preserved II. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

# Part 10 — Delegation and strategy

Manager authority is represented by scope-limited rights and obligations, working time, standing policies and OwnerLocked boundaries. ManagerActivity records why a real actor made a decision, which input/evidence they knew, what commitments it caused and whether the owner was consulted. A fully autonomous portfolio may require no routine owner clicks, but the appointed Persons still do the work and cannot borrow, mortgage or dispose beyond legitimate approval limits.

## TECH-CE1-T032 — Delegation and owner locks

**Intended authority:** StandingInstruction/AuthorityAssignment.

**Responsibility:** Delegated authority, policies, approval & management tasks.

**Normative invariant:** No +efficiency automation, owner-locked sale remains reserved.

**Acceptance fixture:** Owner absence 3yr operational run.

**Canonical link:** `CAN-CE1-13.2`. **Original reference trail:** TECH XIV pp.4,6–7. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

**Policy-driven autonomy:** Persist delegation scope and thresholds for routine work, wage spending, purchase limits, standing ore sale, procurement, hiring searches, permanent hiring approval, loan authorization, capital equipment, construction, title transactions, equity transfers and unsafe exceptions. Managers execute allowed Tasks and Agreements through actual resources and named actor authority without a mandatory owner approval on each ordinary action. Owner-reserved actions create explainable escalation, never silent defaults. Six-day operating weeks, 100% owner retention, land-purchase preference and seller-financing preference are *THE FIRST VEIN scenario policy*, not global rules. *First Vein Parts 1, 4.2.*

# Part 11 — Chronology, historical data and regime authoring

Technology availability depends on invention and regional channels separately from what can be purchased, installed, powered, maintained and operated. Historical regimes and named events have date windows, geography, source confidence and causal input pathways. ScenarioHistoricalEventsEnabled defaults true and optional per-event overrides persist. Turning an authored event off leaves ordinary environmental, financial, biological and market incidents possible. No event grants a profit, price or production shortcut. Numeric historical analogues are stored with scope, units, date and uncertainty.

## TECH-CE1-T028 — Historical technology

**Intended authority:** TechnologyAvailability/Asset.

**Responsibility:** Dated availability, acquisition, connection, support.

**Normative invariant:** TechnologyDefinition not global unlock flag.

**Acceptance fixture:** Imported equipment without fuel/parts unable to run.

**Canonical link:** `CAN-CE1-10.5`. **Original reference trail:** TECH XIV preserved VIII. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T033 — Historical event settings

**Intended authority:** ScenarioHistoricalEventSettings.

**Responsibility:** Event ON default with per-ID optional override in scenario save.

**Normative invariant:** Off authored event does not block endogenous incident.

**Acceptance fixture:** Panic OFF yet organic lender failure possible.

**Canonical link:** `CAN-CE1-15.2`. **Original reference trail:** TECH XIV p.6. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

## TECH-CE1-T036 — Research references

**Intended authority:** HistoricalObservation/CalibrationData.

**Responsibility:** Source/assumptions by year, geography, units and confidence.

**Normative invariant:** No universal hard-coded wage/quantity from anecdote.

**Acceptance fixture:** Reference 1882 observation only in comparable region.

**Canonical link:** `CAN-CE1-18.5`. **Original reference trail:** OCC/WORK references. **Priority:** P1.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Source-qualified historical observations need date, location, citation, type, confidence and promotion/selection bias metadata before being used as simulation-generator input.

*Retained source precision: TECH-RET Historical observations capture promotional bias.*

# Part 12 — Scenario objectives, history and persistence

Scenario predicates query committed authoritative state, historical economic events and defined ownership/value rules. Completed-event milestones, current-state requirements and maintained-state objectives are distinct. Evidence for the chicken sale, guarantor exposure, independent NPC businesses and construction labor intervals remains traceable across save/load. The update loop, task scheduler and checkpoint replay must be deterministic enough to reject duplicate transaction settlement, duplicated identities, missing guarantees and phantom inventory.

## TECH-CE1-T034 — Scenario objective evidence

**Intended authority:** ScenarioPredicate/History.

**Responsibility:** Event vs current vs maintained evidence ledger.

**Normative invariant:** Goal observes, never manufactures physical state.

**Acceptance fixture:** Sell farm after completed chicken objective remains complete.

**Canonical link:** `CAN-CE1-16.2`. **Original reference trail:** TECH XIV pp.6–7. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Support deterministic or well-seeded multi-month/year reduced-presentation playtests that export source authority, key event lineage, objective proofs, relationship consistency, debt and inventory reconciliation and save/load checkpoints.

*Retained source precision: TECH-RET Autonomous scenario QA needs reproducibility.*

**First Vein fixture contract:** Exactly seven predicates in `08_Scenarios_and_Playtests`, including two distinct in-situ samples + independent assay, actual paid mineral sale, owner recovered-and-sold troy ounces, independent NPC ownership and 90-day operation/five paid sales, any rolling 12-month recovery plus positive profit, permanent residence maintained 60 days and fully reconciled owner equity. The removed $400 profit/6-month milestone must stay absent. Do not count ore invoices, raw-ore sales as owner recovered gold, temporary residents, player subsidiaries or unrealized book gains unless explicitly authorized in the predicate. Objective 5 and 7 accounting boundaries are OPEN until specified from real fixtures. *First Vein Part 2.4.*

## TECH-CE1-T035 — Save/load and event idempotence

**Intended authority:** Save/Load/Reconciliation.

**Responsibility:** Persistence of unique events/identifiers/obligations.

**Normative invariant:** No duplicate event on reload or time fast forward.

**Acceptance fixture:** Save during transport/recruitment/project, restart twice.

**Canonical link:** `CAN-CE1-16.4`. **Original reference trail:** TECH XIV p.7 + preserved IV. **Priority:** P0.

**Implementation/evidence status:** `NOT AUDITED IN CURRENT UNITY REPOSITORY`.

Version every persistent schema and require deterministic migration for changed entities, references and relationships or explicitly declare incompatibility; invalid/orphaned links must not be repaired by assigning a random entity.

*Retained source precision: TECH-RET Save schema migration is deliberate.*

Wall-clock presentation, continuous simulation calendar, due-event scheduler and historical authoring triggers must not become independent time truths; transitions resolve idempotently after skip/load with original effective times.

*Retained source precision: TECH-RET Different kinds of events have distinct authorities.*

**Long-horizon fixtures:** Advance 30, 60 and 90-day unattended intervals and verify manager actions, unique lot/event IDs, interim cash transactions, recorded objections/decisions, genuine staff schedules, payroll, loan service, claim rights and population continuity survive save/load without skips or duplications. Include poor-grade geology, no buyer, rejection, unforeseen finance refusal and manager unauthorized loan tests. *First Vein Parts 5–6.*

# Part XIII — Audit and migration procedure

## TECH-CE1-MIG-01 — Before touching code

Record exact working Git commit and run build/tests; locate the single writer for each Business, Person, Household, Parcel, InventoryLot, Shipment, loan, Account and Project authority. Document every duplicate truth and consumer, especially property ownership flags versus acquisition-ID sets, employment slot/person discrepancies and shipment stock double-accounting. Technical XIV records a prior serious `CreateBlockedShipment(...)` inventory defect; this is a current-audit target, not verified resolved code.

## TECH-CE1-MIG-02 — Replace labels without replacing physical truth

Create compatibility adapters from former BusinessType and WorkerSlot views onto the new authoritative Business and EmploymentRelationship state. Do not change save schema for old campaigns until migration and reversible audit are demonstrated. Test every mixed-activity capability with a removed or renamed description.

## TECH-CE1-MIG-03 — State-transition contracts

For each action define: command actor, rights check, actual resource preconditions, optional worker acceptance, reserved state, event emitted, atomic inventory/money mutations, failure handling, save/load transaction identity and read-model invalidation. A cancellation must not restore a stock lot at source while leaving it loaded in transit.

## TECH-CE1-MIG-04 — Evidence and telemetry

Manager reports, census panels, market dashboards and scenario journals are backed by indexed authoritative events and selective performance counters. When reporting uncertainty the source date, observer and forecast assumptions must be visible; do not obfuscate exact known completed transactions.

## TECH-CE1-MIG-05 — Sign-off requires tests

For every mandatory test ID T001–T036, fill an evidence row with Git commit, class/path, test command, run timestamp, PASS/FAIL/NOT RUN and any observed ledger/entity totals. P0 cargo/money/people conservation defects block release.


## TECH-CE1-MIG-06 — Credit facility and undrawn availability

Keep revolving credit facilities distinct from installment loans and owner cash. A facility tracks authorized limit, outstanding draws, covenants/borrowing base, collateral, suspension and maturity; undrawn availability is a derived ability to seek funding, neither spendable Cash nor current Debt. A draw creates cash plus a liability, repayment retires both and interest derives from actually outstanding funds. **Source:** Technical XIV preserved Revision II, PDF pp.140–141.

## TECH-CE1-MIG-07 — Supplier offer and landed-cost components

Supplier offers/quotes must preserve real seller identity, grade or model, lot/package, minimum quantity, price tiers, payment/credit terms, packing deposits, handling and freight incidence. Freight allocation may use mass, volume, handling or other supported burden logic and must not give all goods the same markup by default. Refundable containers/deposits consume funds but do not become merchandise COGS unless actually forfeited. **Source:** Technical XIV preserved Revisions II and IX, PDF pp.9–10, 141–142 and related procurement sections.

## TECH-CE1-MIG-08 — Apparent facts and exact ledger facts

Public-facing financial uncertainty must distinguish known exact transactions from potentially incomplete books and estimates. A known invoice should not be randomized into a guessed range; reconciled corrections emit an adjustment with original ID, reason, date, source and impacted authority. Forecast or opponent information may be uncertain with evidence provenance. **Source:** Technical XIV preserved Revision IV, PDF pp.100–101 and Revision I bookkeeping doctrine.

## TECH-CE1-MIG-09 — Property, occupancy and business-open state separation

ProjectStatus, physical Building suitability, FunctionalSpace use, residential occupancy, Business operating state and financial profitability may change on different dates and remain separate queries. Completion of a structure must not create a tenant, open a retail service or post profit. **Source:** Technical XIV preserved Revision V, PDF pp.93–95.

## TECH-CE1-MIG-10 — Transfer and custody failure/rollback matrix

Define shipment states before loading, reserved, actually loaded, in transit, blocked before loading, blocked after loading, delivered, rejected and returned as distinct histories when material. Only actual lot location and custody change on loading/unloading; cancellation or failure after load must not restore seller stock while the carrier still holds the same units. Save/load and domain callbacks must not repeat inventory or money mutation. **Source:** Technical XIV preserved Revision IV, PDF p.101; Revision VIII multimodal transport guidance.

## TECH-CE1-MIG-11 — Technology supportability and compatible infrastructure

Technology records distinguish known invention/existence, regional access, acquisition, installation, appropriate power/fuel/parts, trained users, supported operation, maintenance and failure states. Rails or wires may be physically built but still lack operator, connection, compatible signaling/service or contractual access. **Source:** Technical XIV preserved Revision VIII, PDF pp.65–67.

## TECH-CE1-MIG-12 — Exact historical law should remain authored data

LegalRegimeRule entries should include scope of jurisdiction, effective start/end, affected actor/entity type, instrument, action, evidence/source and any explicit known exceptions. A dated change must not automatically retroactively rewrite prior valid debts, estates or property deeds. Unverified historical assertions are dataset holds, never silently encoded as enforcement constants. **Source:** Technical XIV preserved Revision XII, PDF pp.25–33 and chronology datasets.

## TECH-CE1-MIG-13 — Corporate capital and minority protection

For stock corporations preserve class authorization, issued/outstanding shares, holdings, voting and economic rights, treasury shares when required, issue/transfer history and dividends. Parent/controlled status does not rewrite subsidiary debt or allow consolidated internal transfers to manufacture income. **Source:** Technical XIV preserved Revision II, PDF pp.142–143; Canon XXXI governance doctrine.

## TECH-CE1-MIG-14 — Historical research confidence and calibration bounds

Do not read an 1880s source's example site or payroll as a default global constant. HistoricalObservation and ProductionCalibration require region, date, unit, scope, crew/condition and source confidence; game assumptions and source documents are different evidence levels. **Source:** Technical XIV preserved Revision XII authoring, occupation and construction reference reports.
