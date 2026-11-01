# LAND & LEDGERS — THE FIRST VEIN
## Completed Conceptual Playtest, Findings Register, and Canon/Technical Handoff

**Prepared:** 2026-10-29  
**Scenario span:** 1877-05-01 through 1890-12-31  
**Last pre-finale checkpoint:** `First_Vein_Checkpoint_1878-09-27.json`  
**Final autonomous continuation:** 1878-09-28 through 1890-12-31  
**Final result:** **6 of 7 revised objectives met** in one explicitly illustrative continuation  
**Scope:** A handoff to a separate model/editor for *considered* incorporation in Canon and Technical documents. **Not an automatic Canon update or an implementation report.**

> **Critical epistemic restriction.** This was a long-form *conceptual* simulation conducted in conversation. No actual Unity game, AI manager scheduler, live NPC economy, latent seeded geology, historical claims register, legal records database, or save/load integration was run here. NPCs, quotations, ore lots, deeds and future events are generated fictional scenario state, not sourced historical individuals or proven installed game features. Numerical ledger reconciliation in this document is arithmetic integrity for this scenario branch, **not** a claim of complete accounting software or actual geological truth. Separate authorial intent, verified Canon, provisional technical designs, actual repository implementation, and fictional playtest output at every step.

---

# PART 1 — EXECUTIVE DECISION SUMMARY

## 1.1 What the user wants the actual game to be

**The player's role should evolve from active founder to portfolio owner.** In the beginning, the player may choose sellers, workers, freight equipment, credit terms, and location. Once managers are hired and trusted, the player delegates routine hiring searches, mine work, transport scheduling, quotations, procurement, assays, sales within policy, and collections. The *simulation* remains detailed and physical, but the *interface* becomes short and strategic. Do not equate complexity with repetitive clicks.

The first ~18 months of this playtest were extremely slow because every small act became a separate conversational decision: tool quotes, carrying timber up a path, interviewing a teamster, two separate assay requests, every customer quote, every three-day investigation, and even the decision to keep mining for another week. The user explicitly reported declining enjoyment and eventually ended the conversational playtest for that reason. **This is a high-priority UX finding, not an incidental stylistic comment.**

Core user decisions and preferences established repeatedly:

1. **Own 100% of Juniper No. 1 and preferably 100% of the milling company.** Do not offer dilution as the default financing solution.
2. **Buy the mill land, rather than lease it, whenever feasible.** The player wants durable ownership and protection from eviction/displacement after constructing valuable improvements. Prefer a valid deed at closing secured by a *purchase-money mortgage/seller note*. Recorded claims or possible patents are not interchangeable with fee title.
3. **Prefer lower down payments, seller notes, chattel security and staged financing** so productive operations keep cash available. Debt has real maturities, security, compound risk, enforcement and foreclosure consequences. No free finance or duplicated funds.
4. **The mine should run autonomously six days per week when safe, funded and productive.** It should sell ordinary commercial rock without another approval once the player grants that authority. Player reserves final control over permanent hires, major capital, land, extraordinary borrowing and chosen exceptional contracts.
5. **Profit margins may be thin.** Raw-ore sales must be able to sustain a favourable well-run mine. Owning freight should provide limited, real efficiency savings; owning a mill may add value if recovery/throughput justify it, but should not be an automatic multipler. An unprofitable deposit must remain possible.
6. **Any NPC can engage in supported commercial conversations** about referrals, work, deals, supplies, businesses and logistics. NPCs have the same action possibilities, information restrictions and consequences as the player.
7. **No mandatory LLM.** Free-text/keyword search should discover a large registry of defined actions; a structured proposal builder expresses flexible contracts. Unsupported unconstrained natural-language acts are a research idea, not a shipping requirement.
8. **Explain specialist terms in ordinary language via tooltips**, especially quartz/gold-bearing rock, assays, ore, tailings, claims, financing and title.
9. **Objectives should be measurable and unambiguous**, and the full seven-objective list was requested in each playtest report. The prior $400 profit/6-month milestone was **explicitly removed**.
10. **Advance 30–90 in-game days where possible**. Surface only a genuinely material choice, with compact numbers-first Unity-style dashboards and optional drill-downs.

## 1.2 Highest-priority Canon/Technical decisions for the editor

| Priority | Change | Why |
|---|---|---|
| P0 | Continuous autonomous business operation + exception-only escalation | Without it, realistic simulation becomes tedious and unplayable |
| P0 | Safe role/authority separation: operator versus manager versus owner | Prevent manager bypass of wages, loans, hiring, sales policies, and land purchases |
| P0 | Physically conserved inventory and financially settled events | No teleported ore, phantom gold, duplicated receivables, internal-transfer profit, or unearned production |
| P0 | Realistic mining profitability conditional on grade, waste ratio, handling, labour, extraction/milling and buyer spreads | Old playtest prices/productivity created artificial bankruptcy despite an apparently promising mine |
| P0 | Keep land title, possessory rights, mineral claim, mill-site entry/patent, water rights and tailings rights separate | A locally recorded paper is not automatically a fee-simple deed |
| P1 | Human-readable tooltips + contextual searches/dialogue | Core discoverability without requiring an LLM |
| P1 | Reusable negotiation/agreement/contract system with verbal, written, physical evidence and enforcement | Enables open trade, legal conflict, recordkeeping and imperfect trust |
| P1 | Seller financing, liens, purchase-money mortgages, borrower/equity governance and refinancing | Capital preservation is major player strategy |
| P1 | Animal and freight capacity by species/terrain/season; specialist regional ecology research | Freight, agriculture, mine logistics and stockpile damage need real physical inputs |
| P1 | Dynamic independent NPC ventures + immigration, housing, employment, services | Settlement goals must emerge causally rather than spawn businesses/population |
| P2 | Full mine-to-mill-to-gold product chain, custom milling and buyer alternatives | Adds meaningful vertical integration and a route to recovered-and-sold ounces |
| P2 | Optional property-improvement buyout/removal/right-of-first-refusal clauses | Makes leasing a genuine negotiable alternative to land ownership |

---

# PART 2 — FULL AUTONOMOUS FINALE (1878–1890)

## 2.1 Rules for this ending

Starting point is the September 27, 1878 JSON checkpoint, NOT a new 1877 initialization. To fulfill the user's request to *finish the rest yourself*, this section authorizes fictional owner-level decisions inside the one-off continuation **solely as simulation choices**. Those decisions are **not** standing permission for Codex to change a real game save, source code, finance account or Canon. The choices follow the user's previously established priorities: no equity dilution; ownership of titled land if possible; seller notes with small down payments; delegated routine operations; no hidden free goods; a reasonable cash floor; every real sale requires payment and physical performance.

Financial and geological outcome are a **representative designed branch**. Nothing in the existing checkpoint proves that this geology, future immigration, independent entrepreneurs or patent result must occur. The continuation intentionally includes bad geological developments and a failed final objective. Gold price uses the period monetary reference of **$20.67/troy oz** in its simplified annual cash model. Exact costs beyond September 1878 are deliberately *simulated calibration inputs*, not historical invoices.

## 2.2 Opening position — 1878-09-27 (preserved, not retconned)

- Owner cash **$439.10**; independent Mercer Freight & Cartage cash **$208.75**; **combined $647.85**. Historical owner personal-living-expense reconciliation **not solved**.
- Five mine workers after Rafael Soto departed: **Amos Polk $4.50/day**, **Jonas Calder $3.25/day**, **Calvin Briggs $3/day**, **Ezra Harlan $3/day**, **Miles Fenwick $3/day**. Six-day wages **$100.50/week**. Amos runs normal mining and ordinary buyer-collected raw-ore sales.
- Freight: owner-personally-titled wagon + **two mules** + harness; employed teamster/acting manager **Eli Ward, $3/day**, under separate 100%-owner Business. Ownership and secured loan still legally distinct from business operations. Eli has not hired another driver.
- Two notes: wagon renewed face amount **$238.27**, contracted maturity **$281.16** on **1879-01-26**; mining-claim renewed face **$254**, maturity **$299.72** on **1879-09-17**. Combined face balances **$492.27**, scheduled matured payments **$580.88**. Never double-count embedded old interest.
- Mine claim **Juniper No. 1**, an unpatented possessory mineral claim interest, not underlying land ownership. Earlier grade measurements and ore sales do not prove deposit size. **Deposit size: Not enough information.**
- Total excavated **1,067,100 lb**; **537,772 lb** remain in separate mixed/waste stockpiles; **529,148 lb** raw rock previously sold; **160 lb** samples and **20 lb** personal recovery experiment. Approx **0.0044 oz** personally recovered, **none sold**; recovered-and-sold score **0**.
- Pine Bend 1.3-acre site has preliminary recorded mill-site location interest, **no patent**, no ownership deed, no binding water or tailings arrangements. Merrick two-stamp machinery quote $1,480, $290 down and $1,190 seller note, but no installation contract.
- Scenario objectives **2/7 complete** (first prospect/assay and first paid ore sale).

## 2.3 Autonomous management events — the 12-year ending

### Late 1878 to 1879 — Recover money, buy real land, begin toll milling

1. Amos continues limited shallow mining and buyer-collected sales with five workers, checking actual safety and liquidity. Eli runs paying freight and mine support using the same physical wagon time budget. No mass ore teleportation.
2. On January 26, 1879, the **$281.16 wagon loan is paid and the lien discharged** only on confirmed settlement. On September 17, the **$299.72 mining-loan renewal is paid** and the claim pledge released. The prior notes are not carried forward after settlement.
3. A new fictional titled parcel, **Cedar Flat Mill Yard**, is found close enough to make ore handling viable. The seller supplies an issued-patent-based title chain and accepts a deed-at-closing conveyance funded by a **$60 down payment and seller-held purchase-money mortgage**. Any associated water entitlement is negotiated separately; settling ponds and downstream protection are not implicit in land ownership. This is a successful *simulated* deed, not a claim that such property is available in historical Black Hills records.
4. The Pine Bend preliminary entry remains recorded as an alternative interest, **not converted into patented fee ownership by narrative fiat**. It is not used to justify building an expensive permanent works.
5. The player retains 100% mining ownership, uses short-lived **non-equity working capital** only after real creditor approval, and begins processing selected rich ore in an existing **custom mill**. A custom mill processes ore *for a fee*, leaving the mine with the settlement proceeds and recovered metal under the service agreement. Rock sold outright is distinguished from rock treated for the owner's account.
6. A real first recovered-gold sale is booked only after weighing/assaying actual recovered bullion/concentrate, lawful transfer, and buyer payment. **30 troy oz recovered and sold in 1879**. No raw-ore transaction counts as recovered gold.
7. Open issues tested: lien release, land deed versus mortgage, senior water rights, periodic use fees, contract and physical inventory after custom treatment, small-parcel recovery, source-level title to concentrates/tailings, and property-improvement protections.

### 1880–1882 — Start the owned mill; first large gold milestone

1. The player authorizes an **owned, modest mechanical stamp works**, built on acquired titled land. Merrick and other suppliers provide financed machine components, a verified power source, foundations, a building, settling facilities and maintenance arrangements. Existing contract quotes are **reconfirmed or renegotiated** at commitment rather than treated as perpetual prices. Machinery title/security and land/mortgage are kept separate. Historical capital figures below are calibration estimates, not archival vendor invoices.
2. The mill begins service gradually, after actual building completion, equipment delivery, safe commissioning, water agreement and trained staffing. The 2–3 ton/day two-stamp proposal **cannot process unlimited ore**; Amos sells some parcels raw, reserves particularly suited feed for the mill, and may custom-mill overflow. Internal ore transfers do not create sales revenue or duplicate stock.
3. Recovery varies materially by ore lot and processing method. Some quartz proves amenable to mechanical concentration; some free gold is trapped or lost to tailings. The separate 1877 hand-panning test is not treated as a universally applicable recovery factor.
4. Workers are added **only with capital, real candidate acceptance, safe duties and owner approval within this simulated finale**. Mine recruitment is by travel, notices, referrals and NPC competition. Housing, food, water and haulage limit how quickly operations can scale.
5. Recovered-and-sold output: **110 oz (1880), 225 oz (1881), 330 oz (1882)**. The cumulative total reaches **695 oz by December 31, 1882**, satisfying objective 3 (650 oz). No assumption of a fully known reserve was made ahead of excavation.

### 1883–1884 — Independent private-sector growth

1. The growing mine/mill and regional freight activity create demand for local repair, barrels/storage, lodging, washing and food. Merchants respond to actual prices, material inputs, hiring and competition, not a scripted quest reward.
2. Two new NPC enterprises pass independence and performance tests by December 31, 1884:
   - **Nordin Cooperage**, independently owned/financed and operated by fictional newcomer **Jacob Nordin**; real premises, tools, cask materials, customers, staff/time and financial risk. It has operated for at least 90 days and has **more than five paid transactions with external unrelated customers**.
   - **Juniper Washhouse**, independently owned and operated by fictional newcomer **Eliza Hart**; its building, water supply, labour and household customer payments are genuine. It likewise meets **90 days and five distinct paid external transactions**.
3. Neither venture is owned by the player, the mine, or Mercer Freight. A bookkeeping-only division or manager-run player subsidiary does **not** satisfy this goal.
4. Growth costs are real: cabins, roads, water, merchant stock, fuel and food. New persons are not instantly permanent residents merely by passing through town. Settlement population increases based on persistent household decisions and available work/housing.
5. Gold recovered and sold: **450 oz in 1883 and 600 oz in 1884**; cumulative **1,745 oz** by end of 1884. Operating and equipment expansion continues through the owner-controlled mill business.

### 1885–1886 — Production peak and the 850 oz annual objective

1. Selective additional staff, mine-face preparation, better handling and **upgraded crushing capacity** allow larger and more consistent output. The player retains 100% of the mine and mill; additional capital is raised through non-equity debt and retained earnings with real security and repayment.
2. Mine throughput is limited by ore grade/face width, available mining labour, mill feed capacity, animal/road haulage, energy, equipment downtime and water. The owned wagon is useful for supplies, mail, people and short runs, **not** an infinite bulk-haul solution. Some bulk haulage needs contracted pack animals/carts or other genuinely available transport.
3. Total recovered-and-sold gold is **730 oz in 1885 and 880 oz during January–December 1886**. In the 1886 period, integrated *mine plus mill* operating receipts exceed operating expenses by a verifiably positive amount in the simulated annual subledger; debt financing and extraordinary capital expenditure are accounted separately. This satisfies the existing objective 5: **at least 850 oz recovered and sold in a consecutive 12-month interval with positive operating profit**.
4. A multi-year ramp from rough sample to industrial mine is **conditional** on developing sufficiently rich mineable zones. The numbers are not inferred from a single 1877 composite assay.

### 1887–1888 — Geological setbacks and population achievement

1. A promising surface section pinches out; fractured/wet ground and harder transport undermine the prior high-grade feed rate. The mine uses previously authorized safe methods, supports and selective exploration; no deep shafts, explosives or pumps are introduced without specific equipment, training and capital decisions within this autonomous authorial scenario.
2. Production falls to **620 oz in 1887** and **420 oz in 1888**. This is a deliberate negative result: adding more workers does not manufacture ore or reverse grade decline.
3. Despite reduced mining output, established stores, services and freight links support households in town. Permanent population reaches **94** by spring 1888 and stays **at least 90** for the complete consecutive 60-day window ending **July 9, 1888**. Thus objective 6 passes. Migrants, births, departures and resident death/aging are event-driven; transient travellers and seasonal crews do not silently count as permanent residents.
4. The player can no longer rely on undifferentiated mine expansion for consistent profit. Creditors require payments; the mill becomes underutilized at some times. These are real risks, not new player microtasks.

### 1889–1890 — Scenario close, an intentionally missed target

1. Further development, purchases of occasional third-party feedstock and controlled maintenance help sustain operations, but the economics of deeper or more hazardous extraction no longer justify unlimited hiring and capital expense.
2. Gold recovered and sold is **330 oz in 1889** and **210 oz in 1890**, leaving a cumulative **4,935 troy oz** by December 31, 1890.
3. The final gold target of **5,500 oz is missed by 565 oz**. The hypothetical financial model leaves **$6,758.42 combined cash**, but that is **not** a reconciled measure of owner equity; the starting owner-living-expense/opening-equity question remains unresolved and assets have depreciation and debt-accounting complexities. Because objective 7 requires **both** 5,500 oz **and** $18,000 reconciled equity, it **fails unequivocally on the gold criterion** regardless of equity.
4. The mine and mill survive the scenario, but at reduced scale. Owned titled land remains subject to any remaining valid obligations; the user retains all equity in the mine, freight and mill entities. Population at final date is 89 permanent residents—lower than at the completed 1888 milestone, which remains permanently completed.
5. The conclusion supports the game's design: good managerial delegation, early profitability and vertical integration can achieve many goals, **without implying perfect information, guaranteed ore continuity or guaranteed all-objective victory**.

## 2.4 Final score — all seven objectives

| # | Deadline | Objective | Final outcome |
|---|---|---|---|
| 1 | 1877-12-31 | Legal control of prospect; 2 in-situ samples; independent assay | **PASS** — May 20, 1877 |
| 2 | 1878-12-31 | First actual paid mineral or recovered-gold sale | **PASS** — August 2, 1877 |
| 3 | 1882-12-31 | 650 cumulative recovered-and-sold troy ounces | **PASS** — 695 oz at end 1882 |
| 4 | 1884-12-31 | Two genuinely independent NPC-owned businesses, each 90 days in operation and 5 external paid sales | **PASS** — Nordin Cooperage; Juniper Washhouse |
| 5 | 1886-12-31 | 850 recovered-and-sold oz within 12 consecutive months AND positive mining operating profit | **PASS** — 880 oz during 1886; positive simulated integrated mine/mill operating result |
| 6 | 1888-12-31 | At least 90 permanent residents continuously for 60 days | **PASS** — qualifying window ended July 9, 1888 |
| 7 | 1890-12-31 | 5,500 cumulative recovered-and-sold oz AND $18,000 reconciled owner equity | **FAIL** — 4,935/5,500 oz; equity not certified |

**Final: 6/7 achieved.** In the model, the qualifying 1886 mine-plus-mill operating contribution is **+$5,499.60** after direct operating costs allocated to those activities (before capital investment, debt service and private owner draws); this is an illustrative annual attribution, not an audited Unity accounting result. The original removed $400/6-month profitability milestone is **not** reintroduced. Objective 5 retains its pre-existing positive-operating-profit condition until the user explicitly chooses to simplify it.

## 2.5 Annual gold, residents and cash — inspectable *illustrative* simulation ledger

The linked CSV supplies every annual receipt/cost/loan/capital/draw field. Amounts shown below are **USD in the simplified fictional scenario**. Gold selling price is held at $20.67/troy oz for transparent calculations; actual transactions would require buyer-specific quality, refining, weight and market rules. This is **one calibrated branch**, not a verified historical financial statement.

| Period | Recovered **and sold** gold oz | Cumulative oz | Permanent residents | Combined cash at period end |
|---|---:|---:|---:|---:|
| 1878 Q4 | 0 | 0 | 22 | $747.85 |
| 1879 | 30 | 30 | 27 | $947.07 |
| 1880 | 110 | 140 | 34 | $1,420.77 |
| 1881 | 225 | 365 | 39 | $1,896.52 |
| 1882 | 330 | 695 | 47 | $2,717.62 |
| 1883 | 450 | 1,145 | 58 | $3,759.12 |
| 1884 | 600 | 1,745 | 65 | $6,381.12 |
| 1885 | 730 | 2,475 | 75 | $8,300.22 |
| 1886 | 880 | 3,355 | 82 | $10,569.82 |
| 1887 | 620 | 3,975 | 87 | $9,295.22 |
| 1888 | 420 | 4,395 | 94 | $8,626.62 |
| 1889 | 330 | 4,725 | 91 | $7,697.72 |
| 1890 | 210 | **4,935** | 89 | **$6,758.42** |

**Cash roll-forward contract:** Each period's close equals opening combined cash + physically collected gold receipts + paid raw-ore receipts + external freight receipts + actual new cash financing − external operating costs − paid capital/land costs − debt/interest cash repayments − owner personal withdrawals. Cross-entity distributions and internal freight fees cancel. The CSV reconciles 13 consecutive periods to the cent, opening $647.85 from the 1878-09-27 source. Its modeled ending split is **$4,379.67 owner cash** and **$2,378.75 freight-Business cash**, totalling $6,758.42. This forward aggregate integrity **does not** close the inherited historical owner-expense gap or claim per-lot shipment audit evidence that was never actually generated. Income statement/equity valuation requires a further accountant-quality pass.

## 2.6 Findings of the completed ending

- A good game can have **6 of 7 objectives succeed** without forced victory. The final high cumulative-gold objective is a useful stress test for ore continuity and financing.
- **Business growth must be endogenous:** profitable work creates supplier demand, entrepreneur entry, migration, housing stress, and competition. NPC independents remain NPC-owned, not player subsidiaries renamed for the objective.
- **Production capacity is more than worker count:** ore quality, access, waste ratio, face stability, feed transport, water/steam power, mill capacity, downtime and marginal costs are real bottlenecks.
- **Legal ownership is worth modeling**; otherwise the central player motive to buy mill land would be meaningless. A deed plus mortgage offers a credible ownership-with-low-down-payment path, with default risk.
- **Raw ore and owner-recovered gold are separate counters.** Custom treatment can produce owner's gold; outright ore agency sales do not.
- **Delegation matters more as scale grows.** Past the first few months, the player should receive consolidated forecasts, exceptions and annual reports, not 100 clicks for 100 shipments.
- **Any explicit player preference that conflicts with an NPC quote** (e.g., no equity dilution, full titled land) must be carried forward across turns.

---

# PART 3 — COMPLETE PRIOR PLAYTEST RECORD (1877–SEPTEMBER 1878)

## 3.1 Initial world / scenario

- Opening May 1, 1877, **Juniper Gulch**, fictional settlement in the northern Black Hills, Dakota Territory; **22 permanent people, 10 households**, excluding the transient player. Regional towns **Deadwood ~11 road miles** and **Lead ~16**, no magical direct railroad to town. Land, Indigenous history and legal access must be date- and place-specific; mine claims are not the same as tribal treaty/title matters.
- Important NPCs/relationships: **Thomas Mercer** (player's brother; owns independent Mercer General Merchandise), **Amos Polk** (experienced lead miner), **Jonas Calder** (original prospecting guide and later worker), **Eli Ward** (teamster), **Harper Forge**, **Bell Boarding House**, **Keen Hauling**. Complete founding 22-Person roster: Thomas Mercer; Gideon, Ruth, Owen, Clara and Emmett Harper; Lydia, Ada and Peter Bell; Nathaniel, Miriam, Edwin and Lottie Finch; Abram, Sarah and Jonas Calder; Isaac and Mae Rourke; Amos Polk; Silas Keen; Edwin Vale; Nell Avery. Ten households. Keep household identity and birthdays/ages consistent in a real persisted scenario. **NPC businesses' private money, debt and stock at cost are hidden** unless learned through legitimate observation or transaction.
- Goal-setting was revised from 11 to 8, then the $400 six-month profit milestone was removed to leave the **seven objectives in Part 2**. The user prioritizes unambiguous completion and always-visible objective reporting.

## 3.2 Early geology and title — May 1877

- Brother Thomas's yellow-specked quartz from Jonas's April 22 creek find triggered scouting; a selected 310 g specimen gave **0.88 oz/short ton equivalent** at an independent assay. This is **selected sample grade**, not commercial head grade or whole-deposit inference.
- May 2: old stakes and 1876 shallow cut found. Recorded holder **Caleb Wainwright**. May 11: user purchased the **unpatented Juniper No. 1 possessory mineral claim** for **$650 plus $9 in records/legal charges**. Correct screen wording must separate ownership of a mining-claim interest from fee-simple surface land.
- May 20: Amos took two distinct in-situ samples, **0.79 and 0.22 oz/short ton**; provenance preserved. The first objective was satisfied; the size and mineability of the deposit remain unproven.
- Amos initially hired at **$4.50/workday**, 6 days/week. Trial period 12 workdays. Equipment needs identified but not automatically bought; staffing and procurement originally reserved for owner.

## 3.3 Freight purchase and formation — May–June 1877

- May 26 Deadwood purchase from fictional **Elias Voss**: **used wagon + two mules + harness = $365**, $100 down and a $265 personal secured note, 16% simple annual for eight months, Jan 26, 1878 maturity **$293.27**. Worn hub, damaged strap and hoof work were disclosed; repaired for **$17.50**. No fictional 'mules implemented in Unity' assumption; animal system availability must be code-verified.
- **Eli Ward, 36**, identified and reference-checked; $3/workday, up to six days/week, $0.75/night employer-paid lodging when required, initial two-week trial. A planned **+$0.25/day** promotion to freight manager when a second teamster/outfit is actually hired. No independent power to hire permanent staff or borrow without owner.
- Two Deadwood-to-Juniper shipments: **420 lb hardware, $10; 280 lb dry goods, $7.50**. Delivery and proof-of-custody precede receivable settlement; freight income not personal merchandise inventory. Actual trip required two days. Remaining claims were collected later, not invented at shipment acceptance.
- May 31: **Mercer Freight & Cartage** established as a generic Business; owner contributed **$75 operating cash**, wagon and note remained **personally titled** under a use agreement. No second creation of wagon value or earlier freight sales. Eli autonomously pursues customers, backhauls, feed, routes, minor repairs, collections and routine freight contracts; forecast load demand >100% raises a request for second outfit, never overloads capacity.
- Owner prefers using this wagon for local and feasible off-map freight legs, with real operating costs and load capacity; outside freight may be superior for large loads. Off-map isn't magically unreachable, but cross-map legs need actual carriers and custody.

## 3.4 Mine supplies and first extraction — June–July 1877

- Initial supplier comparison: local **$49.20** versus Deadwood lower purchase prices; player chose Deadwood goods **$27.25** plus local timber **$16**, total goods **$43.25**. Courier and final delivery **$16.50** more; total **$59.75**, demonstrating **purchase price != landed cost**.
- Tools and 595 lb bracing timber physically staged at creek. **700 lb** had to be moved **0.35 mile uphill** to work face by human labor; never teleported by an 'At Site' flag.
- Amos rehired after trial; Jonas hired at **$3.25/day** after being contacted, without preemptive hire. Crew moved all supplies over two workdays.
- June 13–19 first limited trial: **3.60 short tons** broken, 0.66 t visually mineralized quartz, 0.74 mixed rock, 2.20 waste. Open-cut rock face developed instability; work stopped at boundary, rather than starting unauthorized shafts or blasting. Early composite results **Q001 0.56 oz/t**, **M001 0.07 oz/t**.
- June 28–July 11, next phase: **6.10 t** extracted with ~1.10 t quartz, 0.90 mixed and 4.10 waste; timber stabilization and new face limits. New quartz **Q002 1.92 oz/t**, mixed **M002 0.09 oz/t**; grade isolated to source lot. Sellers offered low net prices and custom stamp minimums, revealing small-parcel transport losses.
- July 21 personally crushed and panned **20 lb** Q002 quartz. Approximately **0.0044 troy oz (~0.14 g)** gold recovered, still unsold; **$1.75** nonrefundable trial expenses, refunded $5 deposit. Process output was 19.6197 lb tailings, 0.3800 lb heavy concentrate and approximately 0.0003 lb of gold (rounded). Technical success but **commercial failure**, not a universal recovery rate. Total source mass conserved.

## 3.5 First sales, workforce scaling, title search — August–December 1877

- **August 2** first paid mineral sale: Q001+Q002 **3,492 lb**, buyer collected; **$13.75** paid. Objective 2 achieved, but **0 recovered-and-sold ounces** from this transaction. Other sale August 11? No: buyer for Q003/Q004 settled **September 7**, **10,200 lb, $42**. Third sale **October 11**, Q005/Q006 **9,592 lb, $39.50**. All these are raw ore sales; cumulative by October 26 **$95.25**, **23,284 lb**.
- Crew grew from Amos/Jonas to **Rafael Soto ($4.25/day)**, **Calvin Briggs ($3/day)**, **Ezra Harlan ($3/day)**, **Miles Fenwick ($3/day)**, a six-person crew costing **$21/day or $126/six-day week**. Candidates, reference strength, acceptance, paid time and supervised work matter. **Rafael left January 1878** for steadier work; one genuinely assigned resignation, not permanent suspension in place.
- The user repeatedly directed **outright mill land ownership**. Initial sites: Finch Creek Bench (possessory claim; $125 candidate), Upper Road Terrace ($140 conditional but title unproven), South Road Bench ($165 seller financing but an entry/application not patent), Willow Crossing Yard ($210/low-down-payment proposal with invalid patent linkage), Pine Bend Mill Ground (1.3 acres, preliminary recorded nonmineral millsite location, **not a deed/patent**). Water rights and tailings were consistently separate blockers.
- Initial small animal-driven **arrastra** quotation ~$238, later bundled $260; ~0.25–0.50 t/day. Not enough to process six-worker mine's full commercial output. Two-stamp ~$1,480 seller-financed proposal appears later, with power/building/water/tailings extra.
- 1877 debt and operating pressure: six-worker mine could not maintain $126/week wages on raw-ore receipts at old, punitive offer prices. Frequent stoppages were exacerbated by a wrongly applied user-level approval model. User explicitly asked for **manager run six-day mining, routine automatic sales, and 30–90-day advances**.

## 3.6 Loans and successful routine operations — 1878

- Sep 17, 1877: claim-secured **$300** loan at 18% annual, $9 closing fee withheld, **$291 actual cash**, maturity **$354** Sep 17 1878. Distinguish collateral over *claim interest* from land title and exclude freight outfit.
- Jan 26 1878: Elias Voss wagon note extended with $60 cash ($55 against old obligation +$5 documentation), **$238.27** rolled into a one-year 18% simple note with **$281.16 due Jan 26 1879**. Recorded old interest not charged again as a new separate liability.
- Sep 17 1878: mining loan refinanced with $106 actual cash ($100 toward old $354 plus $6 documentation), **$254** rolled into a new 18% one-year note with **$299.72 due Sep 17 1879**. No claim equity dilution.
- Mine became an autonomous, marginally profitable raw-ore business: 5 workers × 51 paid days Jan–Mar 1878; new 131.6 t mined, ~69 t gold-bearing raw ore sold for **$1,101**, cash operating contribution **+$25.50** (before depreciation/finance). April–June new 182 t, 96 t raw ore sold for **$1,668**, contribution **+$140** (~8.4%). July–Sep new 174 t, ~87.98 t raw ore sold for **$1,512**, contribution **+$103.50** (~6.85%).
- Fully physical inventory by Sep 27 1878: **1,067,100 lb** excavated = 537,772 retained + 529,148 sold + 160 laboratory sample + 20 hand trial. No recovered-and-sold ounces yet. **Do not retrofit contained gold from purchased raw ore as the player's metal production.**
- Freight ran independently and generated modest receipts, while maintaining one wagon/team (no fake >100% capacity). Owner and business cash were held separately; internal courier charges eliminated in consolidation.

---

# PART 4 — COMPREHENSIVE FINDINGS FOR CANON

The identifiers below follow the conversation's **working VEIN issue labels**, not yet a canonical master issue registry. Some dates/checkpoints reused or inconsistently numbered labels. Before importing, use stable new design decision IDs and a crosswalk; **do not silently deduplicate distinct problems or assign one label to unrelated rules**.

## 4.1 VEIN-DES / gameplay design findings

**DES-001 — Objective/system dependency boundaries.** Scenario objectives observe actual economic facts, not automatically create mineable gold, future residents or independent companies needed to succeed. Challenge may fail.

**DES-002 — Industrial startup must belong to player.** Do not auto-hire a full crew, buy a complete mine, assume developed infrastructure or silently approve spending just because the player says 'start mining'. Initial setup choices are meaningful, then can be delegated.

**DES-003 — Connected procurement and trade.** Compare local, neighbouring-town and off-map suppliers with stock, distance, lead times, landed freight, owned carrier opportunities and origin. No isolated shop dropdown or instant goods.

**DES-004 — Seller-financed equipment and legal security.** Wagon, draft animals, land and mills can be bought with down payment, seller note, collateral, interest, due dates, default and consent requirements. A personal asset used by a Business must not automatically become company title.

**DES-005 — Open Discussions.** Any Person/Business may propose defined real economic acts; NPCs can approach the player or employee through physical/social contact. Category shortcuts: hiring, trade, freight, real estate, loans, referrals/rumours, business purchase, other. NPCs may refuse, lie, negotiate, defer, refer, remember prior offers, misrepresent capacity or experience risk.

**DES-006 — Freight autonomy and scale threshold.** Manager handles customer prospecting, rate quotes, accepted normal jobs, dispatch, animal care, repairs and cash collection; no major debt, hiring or equipment without owner. Trigger an expansion option when legitimate forecast demand exceeds available vehicle/person capacity; the user approved Eli's future promotion +$0.25/workday after actual second outfit/driver hire.

**DES-007 — Player-owned ore processing and vertical integration.** One generic Business can run mine or mill activity. Build arrastra, stamp mill or other historically/date/region appropriate plant via actual workforce, materials, power, water, site, tailings, downtime and security; outsource custom treatment or sell raw ore when better. Do not assume building a mill is automatically profitable.

**DES-008 — Concurrent managed workstreams.** Sales, mine recruitment, site title verification, lender negotiation, project work and freight can proceed at the same calendar time only if different Persons and physical resources can actually handle them. Owner can delegate qualified jobs and receive consolidated outcomes.

**DES-009 — Autonomous Mine Manager.** Amos schedules safe work, routine purchases and assays, mining output, inventory, customer acquisition and contract settlement within budget and explicit authority. No auto-hiring, major loans, capital expenditure or unapproved land acquisition. Owner can explicitly delegate normal raw ore sales while retaining exceptional sale veto.

**DES-010 / DES-011 — Separated enterprise and financing interests.** Mine, mill, land and freight can be controlled by one proprietor yet keep different legal ownership, liabilities and books. An offer for 35% of a mill business is distinct from 35% of a mine; user ultimately demands 100% of both. Creditor lien is not shareholder ownership. Property and machine liens may differ.

**DES-012 — Tenant-improvement rights and buyout clauses.** Model deed/lease/occupancy separately; negotiable right to remove trade fixtures, amortized improvement compensation, right of first refusal, purchase option, early termination damages, appraisal and restoration. A landlord sale normally does not by itself mean automatic lawful eviction from a protected tenancy; exact priorities vary by date/jurisdiction.

**DES-013 — Oral/written/recorded agreements and evidence.** Verbal contracts can be enforceable subject to jurisdiction/statute-of-frauds rules, written agreements can be breached, and losing paper does not erase a real debt. Store underlying obligation as authoritative truth and individual documents, records, testimony, witness knowledge and secondary evidence as separate physical/observer-specific facts. Allow default, dishonesty, aliases, attempts to disappear, litigation and imperfect enforcement, without magic immunity or automatic criminal punishments.

**NEW FINALE DES-014 — Real processing pipeline and ownership of recovered product.** Distinguish: mineralized material -> sampled/assayed lot -> ore allocation -> raw sale **or** custom treatment **or** owned mill -> tailings/concentrate/doré -> refining/assay -> delivered recovered gold -> cash-settled gold sale. Settlement credits goal counters only on qualifying event chain; maintain customer third-party ore title if offering paid milling services.

**NEW FINALE DES-015 — Organic new businesses and migration.** Businesses start only with independent willing founders, capital, workers, premises, stock and customers. Check ownership independence and verified paid external trade. Immigration and long-duration residence depend on actual households, occupation, accommodation, schooling/services, food and hazards.

## 4.2 VEIN-UX / player experience findings

- **UX-001 Privacy boundary:** Do not expose other businesses' exact cash, debt, profit, inventory at cost, hidden contacts or sealed details without a legitimate source. Public prices, offered wages, visible deliveries and voluntary statements may be known.
- **UX-002 Origin/provenance clarity:** Player knows how a claim, sample, rumour, customer offer or creditor identity was discovered; fictional universal knowledge is a leak.
- **UX-003 Contextual Unity actions:** Clicking a Person, building, mine front, stockpile or shipment should expose actions that make sense for known facts, skills, travel and authority.
- **UX-004 / UX-012 / UX-013 / UX-015 / UX-016 Strategic pacing:** Work is fully simulated but not prompted as a new player choice every day. Use 30–90-day portfolio advances; exception-only alerts with meaningful owner-level options; no arbitrary pause at end of a batch, assay or trip. Don't hide true liquidity or safety crises either. Need standing owner policy profiles and escalation rules.
- **UX-005 Property-language clarity:** 'I bought a mining claim' never becomes 'I own all the land'; disclose claim rights, land title, water and occupation separately.
- **UX-006 Person/Business discussion entry:** Selected business exposes known owner/staff and ways to travel, contact and discuss, without forcing each approach through a separate menu microstep.
- **UX-007 Search Discussions:** Local action registry, synonyms, typo tolerance, simple parsing of goods/quantities/prices, ranked supported suggestions, configurable contract builder; no LLM requirement or unbounded understanding claim.
- **UX-008 / UX-010 Roadhead vs workfront:** Purchased, in transit, at roadside staging, at mine, usable at workfront, installed and consumed are different states.
- **UX-009 Evidenced employment screening:** Worker candidate contact, reference provenance, availability and wage terms; allow refusal and competing commitments.
- **UX-011 / UX-014 Plain-language mining and finance:** 'Gold-bearing rock (quartz)', assay, head grade, payable ore, ton vs troy oz, contained vs recoverable/recovered vs sold, tailings, secured debt, title and down payment need short tooltips, optional More, keyboard/controller/touch accessibility; do not expose hidden truth through help text.
- **Forthcoming UX for finale:** Single portfolio screen for Owner Cash, Business Cash, liabilities by maturity, cash runway, actual paid sales, mill utilization, mine production grade/yield, contracts needing owner decision, seven checkable objectives and historical event log. Expand financial/physical audit only on demand.

## 4.3 VEIN-VALID / QA, conservation and truth findings

- **VALID-001/002:** Opening settlement population must derive from persistent Person + Household records; player transient not resident. Family ties/kinship preserved; Thomas is brother but remains independent merchant.
- **VALID-003:** Selected specimen assay and two separate in-situ results must carry source location/sample and not become fixed whole-deposit grade.
- **VALID-004:** Secured wagon purchase correctly creates asset, partial cash payment, liability and collateral without pretending the purchase price is all expense. Feed and repair transactions separately tracked.
- **VALID-005/006:** Cargo identity, supplier origin, capacity, handoff/receipt, physical movement and delayed paid settlement reconciled, including owner versus external Business transfers.
- **VALID-007/008:** Employment acceptance, roster, exact paid days and staging-to-workfront material movements persist; physical feasibility and time are authoritative.
- **VALID-009/010/011/012:** Shallow excavation produces separately tagged quartz, mixed and waste; assay samples consume physical material. Different lot grades do not contaminate another lot; assay prices/transport costs real; hidden deposit size not disclosed.
- **VALID-013/016/019/021:** Mining-claim interest, recorded land entry, patent, deed, possessory site, mill-site location and water/tailings rights are distinct; a land office entry isn't an issued patent. Indigenous dispossession and legal-political setting must be research-backed, not ignored.
- **VALID-014/018/020:** Paid raw mineral sale needs inspection, weight, title/custody transfer and actual cash; does not create recovered-and-sold gold ounces.
- **VALID-015/017:** Six-worker payroll, actual shifts, installed timber, safety state, cash reserves and cost limits reconciled in parallel.
- **VALID-022:** Stale as-of-interest readouts require correction; don't double-accrue renewed note interest. In prior Dec 16 correction the wagon accrued figure was ~$23.70, mining loan ~$13.32, while contractual maturities unchanged.
- **VALID-023/024/025 and finale:** Autonomous operations still need solvency, lien releases, downstream gold quantity proof, monthly and annual books, seasonal time windows and objective provenance.
- **QA-001:** True geological contents should be fixed for a seeded world before prospecting begins, but never revealed by the player-facing sim; this conversation did not truly instantiate those hidden geological data.
- **QA-002:** The very early owner living-expense gap persisted and was flagged throughout. Never invent a balancing transaction or pretend the 1890 owner equity is fully audited without reconstruction.

## 4.4 VEIN-CAL / economic and difficulty calibration findings

- **CAL-001** Milestone difficulty and timescale should reflect real resource bottlenecks, not direct quest rewards.
- **CAL-002/003** Daily mineral and freight logistics need actual capacity; own freight can connect local/regional economies and customer work can subsidize return trips.
- **CAL-004–008** Hand excavation rates depend on support work and ground conditions; small parcels have high handling/shipping/treatment minimums; a better assay doesn't instantly create profit; claim-side buyer-paid pickup may beat a higher delivered bid.
- **CAL-009–011** 20 lb hand-panning can recover trace gold but is not economically scalable; small arrastra's capacity should be much lower than that of stamp installation; mill and land rights have up-front and ongoing cost.
- **CAL-012–017** Mine payroll can wipe out cash; low-margin surface mining and debt refinancing must be possible; NPCs can resign when not scheduled; credit should postpone rather than eliminate insolvency. Avoid repeated user decisions for every payroll week.
- **CAL-018** Correct *conditional* productivity: previous six miners produced ~1.2–1.75 t total per day during development. Later better prepared work front may produce ~14–22 t/week with ~8–12 t payable if geology, transport, crew and tools support it. These are tuning hypotheses, not universal historical minima.
- **CAL-019** Buyer price should reflect independently assayed gold grade, assay error/variance, buyer recovery costs, haulage, deductions, merchant risk and bargaining, subject to an upper bound from realizable metal. Prior sales remain paid facts and should not be rewritten casually; future quote generation is retuned.
- **CAL-020** Owning one wagon saves real small-lot transport/commissioning costs but does not give free bulk hauling. 10 t requires at least 20 full 1,000-lb payloads before roads, animal condition, people, trip time or return loads are considered.
- **CAL-021** Own milling must compete against the raw ore alternative: a stamp mill makes sense only at real crushing throughput, achieved recovery, power/fuel/maintenance, labor, water and tailings costs. A small arrastra is a meaningful first industrial stage but should not solve the whole mine.
- **CAL-024/025/026** Marginal positive contribution example: five workers $16.75/day and collected raw-ore revenues with 2–8% cash operating margins, excluding debt/depreciation; no guaranteed rich geology. Processing comparison should use real *net realized* gold proceeds vs raw ore quote, with clear costs and uncertainty.
- **FINALE-CAL-027 (proposed)** Deepening mine geology can reduce grade and cost productivity, even after upgrades. Scenario milestones can fail despite a healthy town. Balance objectives using seeded geology and multiple scenario seeds; don't force 7/7 with adaptive resource spawning.

## 4.5 VEIN-RES / targeted historical research work

**RES-001 — 1877 Black Hills claims, treaties, possession and patents.** Research exact 1877–1890 local county/territory and federal practice, recorded claims, location/representation requirements, water rights, townsite rights, coexistence with Indigenous land rights and dispossession, land patent process, senior ditch claims and law enforcement. A term such as 'full title' cannot be inferred from local filing alone.

**RES-002 — Animal systems, 1870–1905 United States.** This separate backlog file is available and should be preserved **in full**: horses, mules, donkeys/burros, oxen; freight/harness/terrain/feeding/hoof work; livestock, pets, wildlife, regional fauna, mice/rats/rodents and feed/inventory losses; location/era prevalence and implementation status. Do not misdate plague or assume every animal species already exists in Unity.

**RES-003 — Historical public-land title errors.** Distinguish application/entry from issued patent; examine dated legal route for independent millsite patents of <=5 acres when eligible; understand title defects and timing of recording and federal adjudication.

**RES-004 (new) — Mining ore purchase and recovery costs by region/year.** Verify miner day rates, 1870s Deadwood/Lead quartz grades and actual recovered results, arrastra and stamp battery specs, winter shutoffs, raw-ore buyer schedules, toll milling, bullion assays/discounts and transportation. Avoid drawing a broad universal profit conclusion from a few high-grade sample reports.

**RES-005 (new) — Landlord fixtures and agreement enforceability.** Research territory/date-specific tenant trade fixtures, compensation, early cancellation, successors/recording, purchase options, verbal versus signed contracts, statute-of-frauds exceptions, lost documents, secondary evidence and civil/criminal adjudication. Historical common-law background is not a blanket rule for every state/year.

---

# PART 5 — DETAILED FEATURE AND DATA REQUIREMENTS

## 5.1 One generic Business with composed activities

No hard-coded `MineBusiness`, `FreightBusiness`, `MillBusiness` class permission system. A generic Business owns or lawfully uses facilities, Person labor, physical inventory, contracts, cash and real work processes. Multiple business lines may be maintained. A label never creates assets, workers, goods, legal rights, skills or a process. Jobs describe activity/capability, not fixed RPG character classes.

**Person** identity persists across employer change, independent business creation, migration, aging, household and social relationships. **Physical time** and **work authority** apply to all actors, including managers and the player. Transfer of employer requires actual new payroll/account entries; portfolio display isn't a legal merger.

## 5.2 Management AI operating orders

Suggested order object fields: principal/owner, manager Person ID, governed Business/operation, effective date, allowed action IDs, per-transaction and rolling budgets, customer/price floors, shipping/purchasing priority, target throughput, employment authority, account authorized to pay, cash floor, required evidence, safety policy, permit/legal constraints, termination/escalation conditions and reporting interval. The manager's plan selects actual WorkAttempts, routes, supplier bids, payroll and customer agreements. An owner's instruction doesn't compel a third party to agree.

**Exceptions:** (1) insufficient cash or breached debt covenant; (2) unsafe/illegal work or accident; (3) no necessary resources; (4) capacity above 100%; (5) owner-locked hire/debt/land/share sale; (6) extraordinary contract or material price change; (7) fraud/major competitor conflict; (8) tax/permit or market event. Routine information can remain in an optional audit log while the manager advances.

**Business continuity requirement:** A mine on an authorized rolling budget shouldn't pause after exactly 6 or 12 workdays unless a real limit is reached. Weekly wages accrued and paid by events, cash inflows replenish operating resources; contract collection has its own delay. No change of owner preferences across turns.

## 5.3 Real mineral industry model

### Geological truth and uncertainty

- Seed geology before prospecting: mineralized zones, geometry, depth, variability, veins splitting/pinching, grade distribution, recovery traits, hardness, water and stability, plus any local fixed historical mining condition. **Separate private truth from prospectors' evidence and player knowledge.**
- An assay is a physical sample's test result with provenance, uncertainty and potential selection bias; not magically a minewide grade. Representative composites from a stockpile are stronger evidence about *that pile*, not the entire deposit.
- Proven/commercial ore is not the same as visible quartz or suspect mineral-bearing rock. Grade and economic cut-off may make some rock waste despite gold content. Unassayed mixed rock is not automatic worthless waste or automatic sellable ore.
- Do not reveal finite deposit reserve/remaining gold unless adequately explored and estimated; 'Not enough information' is valid state.

### Work and stock

- Mine work includes access, timber/support, ventilation/drainage if applicable, extraction, mucking, sorting, roadhead transfer, equipment repair, sampling, crush/processing and tailings management. Reconcile by **physical mass**, density/volume where needed, lot ID, grade provenance, location, ownership, custody and stage.
- One operation may control raw rock stock but not directly create bullion; treatment has yield, processing time, loss/retained gold and waste. Golden sample transferred to player custody is distinct from gold sold.
- Mill ownership allows third-party custom milling with explicit **customer-owned ore** and fee agreements. Custody transfer is not title transfer; products returned or retained under agreement.
- Support safety and law are material. Work may fail or halt; a generic task 'done' isn't equivalent to a safe work-front.

### Pricing and profitability

- Buyer quote = willingness to pay based on tested or untested grade, market metal value, processing/recovery expectation, transport/custody, fees, quantity and bargaining. A buyer may reject low grade rock entirely. Rich ore can support positive margin, but mine profitability is not automatic.
- Treat **gold content value ≠ gross gold recovered ≠ refined gold paid**. Recovery depends on mineralogy, plant, process, working conditions and assayer; don't invent metal by using `assay grade × extracted tonnage` as a real inventory receipt.
- Distinguish cash flow, contribution margin, operating income, depreciation, financing expense, owner personal draw, business debt and paid capital asset. Intercompany transport fees cancel in consolidated statements. Preserve all prior legally paid sales as facts unless explicitly performing a labelled retroactive scenario restart.

## 5.4 Freight and animal economy

Vehicle and animal states include legal owner, permitted business user, condition, wear, feed, hoof/harness and driver availability. Assign `payload`, draft effort, trip hours, geographic route, season, road/water crossings, weather and cargo-specific safety. Freight may use separate carrier for each route leg; an external merchant's ore collected at mine is no longer the player's cargo after valid transfer. Own-mule availability for mining power is constrained by freight shifts, not reusable twice simultaneously.

The animal taxonomy and ecological pest mechanics require a period/region research matrix, not an automatic import of all modern US animals everywhere. Rodent damage requires the actual feed/grain/fabric or other susceptible lot, pests and building conditions, not random subtraction from abstract 'money'.

## 5.5 Real property, title, improvements and occupancy

Entities/rights: `Parcel`, `Building`, `FunctionalSpace`, surface tenure/title, mineral claim, separate millsite claim/entry and patent proceeding, water diversion/use priority, wastewater/tailings constraint, easement/road, occupancy/license/lease, deed, mortgage, liens, debtor/creditor and physical improvements.

The user can choose **fee purchase with seller mortgage**, lease with protected fixtures, mineral or public-land location, investment in another person's property, temporary use agreement, business acquisition, or private seller-financed purchase. Each requires actual willing seller or rights authority and specific legal validation. Title chain evidence is not the same as *issued* federal title. Public record searches are fallible; truth, evidence and observer knowledge differ.

**Buyout/fixtures:** If the landlord sells, buyer rights depend on lease priority/recording and applicable local law. Negotiable contract can grant removal rights, residual improvement value, compensation for early termination, purchase rights, right of first refusal, obligations to restore the land or release of liens. The game should not guarantee full recovery of historic building costs by default; the recovery depends on terms, actual condition/value, creditor rights and enforceability.

## 5.6 NPC dialogue, search, proposal building and persuasion

Workflow: select map site/business/Person -> open known contact or discover available interlocutor -> `Discuss` -> choose topic **or** type keyword phrase -> see 2–5 **supported** ranked intents -> open configurable proposal fields -> counterpart decides using true incentives and knowable facts -> response/offer/counteroffer creates dated transaction/negotiation state. Managers can be sent to negotiate and player can later follow up; NPC remembers the previous $800 vs $1000 price approach. NPC may ask player for supplies, freight, employment, finance or buy business, in person or through a staff agent; physical travel and availability still apply.

An offer for 10 monthly steers can be made without current stock. Accepting it creates obligations, not cattle. Later seller can procure, deliver, default, renegotiate, deceive, flee, face civil action or win a dispute based on evidence. No unlimited natural-language inference is necessary; unsupported terms are explained rather than guessed. Competition, undercutting and reprisals should flow from information networks and NPC motives; arson is possible but exceptional, unlawful and evidence-sensitive, never a stereotyped scripted response or automatic execution.

## 5.7 Contracts, bookkeeping and legal dispute evidence

Authoritative Agreement ID/terms exist independent of evidence. Agreement may be oral, written unsigned memorandum, signed instrument, recorded/security instrument, and can contain conflicting copies. Evidence instances: original contract, duplicate copy, ledger entry, signed receipt, witness testimony, clerk record, correspondence, seal or signature, altered/forged paper, physical transfer, partial payment, bank book and hearsay. The Person knows only observed/received evidence, not universal truth. Missing original increases litigation uncertainty; does not erase contract. Historically required writing differs by subject, place and date.

Defaults and collection: enforceability, tracing debtor/aliases, litigation fees, sheriff reach, judicial remedy, bankruptcy, insolvency, security and jurisdiction can complicate outcomes. A signed contract isn't a magical guarantee of collection; verbal agreement isn't magical permission to steal. Crime and punishments need calibrated historical law and evidence.

## 5.8 Settlement and scenario success

Derive permanent residence from Persons actually living/working in settlement over time; household stability, housing, transport and business opportunity. NPC firms must independently source customers and bear risk. Store objective definitions as **evidence predicates**, not imperative spawn commands. Objective 3/5/7 must count actual owner-recovered-and-sold gold settled in money, not raw ore gold content or third-party owned ore. Objective 5's profit condition requires a consistent accounting window and entity perimeter; it must not use an unexplained cash figure. Objectives completed by earlier deadlines stay completed even if population or cash later falls.

---

# PART 6 — TESTS TO REQUIRE OF IMPLEMENTATION

The following are **proposed future regression fixtures**, not passing Unity tests. Prefer deterministic seeds and save/load across every transition.

### A. Prospect and extraction

1. Selected 310g specimen assay differs from in-situ results and from other composite ore lots; doesn't leak fixed reserve size.
2. Old claim holder's interest can transfer at cash+record cost; claim remains unpatented, separate from surface title, recorded proof of transfer persists on save/load.
3. Surface cut ground becomes unstable, foreman stops; player/manager cannot excavate unsafe unsupported face merely by spending money.
4. Worker with no mining skill can move timber under supervision but cannot supervise specialized ground support solely because job title changed.
5. 700 lb supply staging moved 0.35 mile by paid real worker time; no instant 'At Mine = usable' shortcut.
6. 20 lb hand trial returns tailings + concentrate + separated gold (same dry source mass), expends consumables and tracked rental deposit; no gold sale automatically.
7. Different ore lots have own assays/recovery and can be sold, processed or stored without contamination. Sample mass isn't still in sellable pile.
8. Zero/low-grade quartz can receive no buyer offer; high-grade offer constrained by potential gold price, recovery risk, freight and merchant spread.
9. Owned and toll mill produce different obligations, yields and custody; collected recovered gold is distinct from cash-settled sold gold.
10. Insufficient mill throughput accumulates real queue, spoilage/wear/stockpile if applicable; cannot process unlimited bulk in one day.

### B. Business and finance

11. Freight Business capitalization transfers $75 from owner cash; doesn't add external revenue. Wagon personal title and seller lien remain personally registered.
12. Freight internal charge debits mine and credits Business; consolidated profit eliminates it once.
13. Wagon debt creation $100 down + $265 secured note equals $365 asset. Repair $17.50 is actual expense. Interest/balloon correct and not duplicated on refinance.
14. Mine $300 secured loan with $9 withheld disburses exactly $291; does not create $300 cash plus extra $9 expense again.
15. Jan 1878 wagon note refinance and Sep 1878 mine note refinance replace prior obligation; old notes are discharged and current terms restored correctly after save/load.
16. Seller-financed land deed transfers ownership at close, purchase-money mortgage remains; foreclosure only through valid default/enforcement, not arbitrary seller desire.
17. Failing title search blocks full ownership purchase; entry, claim, location and patent statuses cannot overwrite each other.
18. Water agreement cannot convey superior use the grantor lacks; seasonally dry water must stop/limit mill physically.
19. Lost signed contract does not erase underlying debt; third-party record and witness evidence affect dispute resolution.
20. Fake identity or runaway debtor raises identification and collection difficulty but cannot erase real transaction.
21. Owner personal draw reduces consolidated equity; does not count as mine expense. Internal capital contribution changes ownership/cash allocation but isn't outside profit.
22. NPC-owned business counts objective only after 90 days and at least 5 paid external trades, with independent capital/control and valid physical transactions.

### C. Manager autonomy and UI

23. Amos continues safe six-day production across weekly boundaries and sampling/sales without owner prompts if within cash/credit/authority.
24. When liquidity fails he can reduce assigned shifts, ask a lender for offers and send one exception report; no payment without funds or unauthorized new loan.
25. Manager can't hire/terminate/borrow/buy title/exceed capital authority through a generic task or custom dialogue.
26. Eli's single wagon cannot schedule >100% hours, weight, mule capacity; next-outfit request is an alert, not an automatic second animal purchase.
27. Manager must not know a rival's hidden cash/inventory. Public listings/contacts and observable shipments remain knowable.
28. Search `who hauls your goods`, `sell 10 steers monthly`, `buy your shop`, `need miner` resolves to *supported* actions even with synonyms/typos and configurable terms, not made-up actions.
29. NPC can make an offer to owner through shop manager while owner away; staff stays within delegated authority; notification persists.
30. Click tooltip `Gold-bearing rock` explains quartz without revealing hidden deposit size; accessible with keyboard/controller/touch.
31. Annual/twelve-month objective evaluation correctly handles time window and actual profitability; earlier completion remains latched after downturn.
32. Historical prices/material expenses have time/region evidence class; unverified calibration never silently treated as archival fact.
33. Cross-platform/long-run playtest years maintain single source of truth, no money/ore/person duplication and date-scoped obligation maturities across scene changes and saves.

---

# PART 7 — CANON / TECHNICAL ROUTING & SOURCE AUTHORITY

## 7.1 Authority order and required evidence

The supplied ZIP `Land_and_Ledgers_Documentation_Package_v2.1_2026-10-08(2).zip` states that **original Canon XXXI (2026-10-07) and Technical XIV (2026-10-07) govern** until a verified no-loss replacement is signed off. The consolidated v0.3 Markdown/Docx/PDF are **provisional drafts**, useful for routing but cannot independently override originals. The packet also holds historical work, workforce, events, law, Step 12 economy calibration, Step 13 scenarios/playtests and Step 14 decision proposals (not all approved). The next model should read package `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md`, audit status, document router and relevant source clauses **before editing**.

**Relevant provisional Canon sections to crosswalk against original** (not permission to overwrite): CAN-CE1 Parts II (Business authority/accounting), III (Persons/time/work), V (land and title), VI (work projects), VII (products/trade), VIII (animals), IX (mining and milling), X (freight), XI (markets), XII (finance), XIII (delegation/information), XIV (law/illegal actions), XVI (playtesting), XVII (busywork), XVIII (evidence).

**Relevant provisional Technical targets:** TECH-CE1 T001 Business authority, T002 Person identity, T004–T005 work/time, T009 economic events and exactly-once settlement, T014 custody/ownership, T015 land rights, T026 transport graph, T032 delegation/owner locks, T034 objective evidence; related MIG items for provenance, suppliers, property occupancy, finance, scenario tests. The actual C# repository status **has not been audited here**; target entity names in drafts are design candidates, not proof they exist.

## 7.2 Proposed change procedure for the next model

1. Read the ZIP router and original Canon/Technical relevant sections. Build a **crosswalk table per finding**: original clause and page, provisional equivalent, proposed amendment, rationale, potential contradiction, historical source status, proposed Technical contract, regression fixture, implementation readiness.
2. Treat explicit user decisions as **new proposed design requirements** to reconcile, not a blanket license to overwrite every rule. The seven First Vein goals are **scenario-specific**, not necessarily universal Open Frontier win conditions.
3. No action merely named in conceptual playtest should be presented as **already implemented**: mules, mine tasks, stamp mills, separate business accounting, oral-law evidence and broad NPC dialogue all need source verification.
4. No rule should be accepted solely because a simulated favourable economic case worked. Validate multiple geology, price, weather, freight, labour and loan seeds including low-grade losses, bankruptcy, legal title failures and absence of markets.
5. Preserve remaining open questions and evidence confidence. Keep date/geography-specific history citations; avoid anachronisms and romanticized uniform frontier violence.
6. Make **patches and acceptance tests**, not giant unreviewed replacements. Verify no-loss against original Bibles and existing approved proposals.

## 7.3 Other supplied continuation notes that must not be dropped

- `First_Vein_Open_Discussions_and_Freight_Delegation_2026-10-11.md` — complete design, free-text search constraints, NPC reciprocity and freight authority.
- `VEIN_RES_002_Animal_Systems_Research_Backlog.md` — historically regionalized draft animals, livestock, wildlife, rodent pests and research program.
- `VEIN_UX_011_Plain_Language_Tooltips.md` — precise UX/accessibility proposal.
- `VEIN_UX_012_Strategic_Pacing_and_Equity_Preferences.md` — change of pacing, management authority, 100% ownership and seller financing preference.
- `First_Vein_Realism_Economy_and_Contract_Audit_2026-10-19.md` — historical/economic calibration and property/contract distinctions. Its source URLs and evidence limitations must carry into future literature review.
- All `First_Vein_Checkpoint_*.json`, particularly `1878-09-27`, must remain **scenario evidence**, not definitive implementation specifications.

---

# PART 8 — OPEN ISSUES AND EDITOR DECISIONS

The following need an explicit review; don't resolve with invented default mechanics:

1. **Exact existing Canon scope** of verbal agreements, title, contacts, game-wide settlement businesses and NPC-initiated offers: verify before introducing duplicate systems.
2. **Gold milestone semantics:** Does custom milling of player-owned ore count as owner 'recovered and sold'? Proposed yes after owned-product settlement, but original objective/event predicates must confirm. Raw ore sale does not count.
3. **What is the profit test for Objective 5?** Define full mine/mill operating perimeter, accrual versus cash, 12-month start/end and treatment of freight internal transfer, depreciation, capital and financing; the user removed a separate 6-month $400 test but not this existing qualifier.
4. **When does a permanent resident count?** Household home/primary address, duration and actual physical ties, versus visitors, miners travelling between camps, seasonal staff and babies.
5. **Cash reserves versus player risk permission:** Manager financial controls should permit owner-adjusted risk, but never secretly pay wages with non-existent money. Flexible financed credit is real debt and requires permitted authority.
6. **Mill-site statute and fee title:** A 1872-law millsite location cannot be treated as automatic title. Verify size, nonmineral test, actual milling use and any needed patent procedure for the region/year, also Indigenous/cadastral context.
7. **Landlord improvement protections:** Which fixtures are removable, what compensation can be contracted, and what is legally possible by date/territory? Must not promise guaranteed $200 recovery for any building.
8. **Open discussion parser size/performance:** Define a maintainable Action Registry, not thousands of hard-coded bespoke dialogue trees. Need text accessibility/localization and unsupported-action fallback.
9. **Work-package automation in Unity:** Actual implementation status for mine equipment, mules, mill, land, water, hiring, business tasks and NPC daily plans is unknown. Inspect source before scoping.
10. **Historical 1877 gold economics:** Regional buyer price, grade/payable fraction, toll cost, stamps/arrastras and hand-miner throughput require proper evidence ranges, confidence and default tuning with multiple seeds.
11. **The full owner's early ledger:** The unknown pre-June 1877 personal-living-expense opening adjustment must be explicitly researched or left as unverified; a fictional 1890 dollar equity shouldn't be called reconciled without it.
12. **Scenario testing quality:** The branch to 1890 is a story-calibrated model. A genuine test must run the real authoritative code with deterministic seed, ledger and population evidence, compare favourable and unfavourable seeds, and log unique event/lot/agreement IDs.

---

# PART 9 — ACCEPTANCE GATES FOR THE CANON UPDATE

The handoff is successful if the editor produces:

- **A clause-level matrix** of original Canon XXXI and Technical XIV against each relevant finding; contradictions explained and ownership of amended rule clear.
- **An explicit approved/proposed/rejected/unverified status** for each item and a record of what has *not* been implemented in Unity.
- **No missing prior constraints:** one Person/time, one authoritative business/account and material lot, rights separate from possession, non-omniscient NPC knowledge, meaningful contract consequences, historical geography, credit risk and conservation.
- **A small number of coherent features rather than hundreds of dialogue branches:** open action registry, manager policies, physical work, ledger settlement, land agreement/evidence, recovery chain, local economy and accessible UI.
- **An updated THE FIRST VEIN scenario fixture** with exactly seven objectives, verified objective predicates, no resource-creating quest rewards, and a clear provenance statement that the 1890 continuation here is a *hypothetical example*, not a verified procedural world outcome.
- **A regression suite** covering money, ore, time, rights, agreement evidence, NPC authority and population milestones, with case-driven save/load invariants.
- **An evidence-backed economy tuning appendix** that does not guarantee all mines make profit or that every vertical integration is lucrative.

## Closing verdict

**THE FIRST VEIN's most important result is not the fictional 6/7 score.** The enduring lesson is that the game must combine deep, physically consistent economics with **strategic delegation, asymmetric information, actual legal and financial obligations, and a player-facing interface that doesn't demand constant micromanagement**. This should guide any future Canon work far more strongly than the particular ore tonnage, named parcel or fictional sale from this conceptual run.

**End state:** December 31, 1890. Playtest closed. **No remaining player choices.** The PDF version requested later can be generated directly from this Markdown handoff after final review, with the objective table, chronology, findings, technical tests, financial appendix and source authority notes intact.
