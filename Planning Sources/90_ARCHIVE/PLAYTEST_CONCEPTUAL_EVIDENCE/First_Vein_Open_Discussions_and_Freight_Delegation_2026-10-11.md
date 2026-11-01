# LAND & LEDGERS — THE FIRST VEIN: Open Discussions & Delegated Freight
**Source:** Live THE FIRST VEIN playtest, Oct. 11, 2026. **Status:** Scenario design proposals / working playtest policy. **Not automatically approved Canon or implemented Unity functionality.**

## Authority
- Governing CAN-ORIG: Canonical Project Bible XXXI (October 7, 2026), Parts II §§2.1–2.3; IV §§4.1–4.7; V §§5.1–5.3; XI §11.5; preserved Revision XXVI Part IX §§9.1–9.6, Part X §§10.1–10.3; preserved Revision XIX Part VII §§7.1–7.3.
- Governing TECH-ORIG: Technical Implementation Bible XIV (October 7, 2026), Part I §§1.1–1.7 and preserved Revision IX Part VII §§7.1–7.5.
- The current simulation is conceptual; no claim of implemented Unity classes, tested integrations or repository modifications.

## VEIN-DES-005 — Open Discussion / Commercial Proposition interface (new proposal)
**Trigger:** Player wants to ask arbitrary NPCs about hiring, suppliers, freight, business sale, loans, trade, contacts, rumors and custom quantities, including proposing goods not currently owned.
**Proposed behavior:** Selecting a business/Person reveals only legitimately known identity. If unknown, use Visit/Approach/Introduce/Ask Name or an intermediary. Open Discussion offers topic shortcuts, an action search, and a structured proposal builder for actions the simulation actually implements. Unrestricted natural-language interpretation is an optional research direction, **not** a promised base-game capability. Each discussion uses actual Person availability, relationships, information, incentives, risk tolerance, consent and ability to negotiate, refuse, lie, defer, refer or initiate a counteroffer. Discussions consume time and access. No telepathy, omniscience or universal access to private books.

**Topic categories (UI only):** Work & Hiring; Goods & Supply; Freight & Delivery; Property & Business; Loans & Finance; People & Rumours; Search Discussions; Other Matters. Topics do not grant mechanical permission. The player can assemble flexible terms for any supported agreement type, subject to law, practical feasibility, agency, response and later delivery. Unsupported proposals should be described as unsupported, not silently guessed or executed.

**Promise ≠ inventory:** A player with no cattle can propose 10 steers/month to a butcher. The commercial offer can be accepted, negotiated, rejected or conditioned upon proof/security. If accepted, the agreement creates delivery obligations, rights, due dates and potentially damages; it never creates cattle or revenue without real procurement/transfer/sale. Misrepresentation is distinct from innocent failed performance; reputational, legal and collection consequences depend on evidence and dated jurisdictional law.

**Identity and enforcement:** Store identities, asserted aliases, witnesses, documentary evidence, contact paths, location uncertainty and attributed transactions separately. A debtor moving elsewhere may raise tracing/collection costs; it does not erase the claim. Contracts can be oral where law permits; signing is not a universal trigger for legal recourse. Avoid universal 'untraceable alias' or automatic enforcement. Legal investigation, civil process, sheriff jurisdiction, penalties and physical violence require specific dated rules and evidence. Arson or other criminal reprisals are exceptional emergent acts, not a preset reaction to undercutting. Punishments never automatically become hangings.

**Competition and chain offers:** Employees/agents can approach a business seller with low offers (e.g. $800), then the player follow up with $1,000. Persistence: previous proposals, actor identities, relation to player, relevant seller memory, emotional response, rival communication and opportunity cost. No scripted price-anchoring bonus or guaranteed exploitation.

**NPC symmetry:** Other Persons and businesses may approach the player, shop staff or managers through the same mechanisms and rights. Staff may answer or negotiate only inside their actual knowledge and authority. Uncommon entrepreneur approaches should arise from real markets and travel, not a background pop-up timer.

**Technical acceptance tests:** known-vs-unknown owner, failed referral, false rumor, arbitrary offer without stock, signed and oral agreements, default with relocation and alias, cross-town creditor enforcement, undercut rival discovers through real relationship, hired agent proposal and follow-up, NPC-to-player visit, employer absent/delegation escalation, identical options for player/NPC from comparable state, save/load of offer provenance and obligation, privacy boundary for NPC finances.

## VEIN-DES-006 — Autonomy and service-capacity escalation (working policy)
**Business:** Mercer Freight & Cartage, 100% proprietor-owned, formed May 31, 1877 after first two deliveries. Generic Business state, not a FreightBusiness subclass. Historical freight before formation remains an owner/sole-trader segment, not re-created as Business revenue.
**Eli:** Retains $3/workday, 14-day trial. Acting operating dispatcher with routine freight and logistics mandate. One two-mule wagon and harness personally owned by proprietor and subject to a $265 personal secured note; allowed for company operations under explicit usage agreement. No silent asset/debt transfer.
**Delegation:** Solicit and accept ordinary lawful freight offers based on estimated actual margin, dispatch, combine return loads, handle receipts/collections, buy routine feed, maintain equipment within a small approved amount, serve Juniper No.1 mine on priority when goods and staff genuinely available. Track paid driver hours, wagon/mule capacity, cargo/reservations, customer sources, expenses, receivables, debt service and lost opportunity.
**Owner locks:** no second wagon, new animal team, long-term employee, borrowing, pledged collateral, permanent rate-negative contract or business sale without owner approval. Manager Judgment does not compel third-party contracts, teleport goods or override mine purchasing approval. Forecast demand >100% of available hours/load capacity triggers expansion proposal; actual allocation cannot exceed physical 100%.
**Promotion rule:** If a second paid teamster is hired for a genuinely available second wagon/team, Eli becomes manager/foreman at $3.25/workday (+$0.25). The title does not fabricate supervisory capability. He may still drive, constrained by real hours.
**Escalations:** cash shortage/payroll/debt, material loss/accident, claim of fraud, major customer issue, repairs over authority, expected capacity over 100%, personnel problems, request for new equipment or exceptional commitments.

## VEIN-QA-002 — Starting financial continuity hold
Before autonomous advancement, use the prior May 31 recorded owner cash $1,140.70 (provisional), $265 personal secured note, $30 combined accrued wages, $17.50 personally earned freight receivables (not collected), one wagon/two mules and mining claim. Earlier personal expenses are still unreconciled. Do not describe cumulative owner net worth/cash as audited until a source-backed correction is produced. Business capitalization must move cash once from owner to Business, not create a balancing entry. Formation preserves original cargo delivery and two creditor receivables.

## Supplier/freight policy
Prefer the owned wagon for feasible local and distant road-leg shipments, with mine priorities and opportunity cost recognized. External carriers remain possible when cargo size, timing, terrain, driver availability or economics warrant. Off-map shipments must retain origin, separate real carriers by leg, custody, freight invoices and physical arrival; no teleportation. Owner chooses supplier and mining input quantities; operating dispatcher schedules accepted deliveries without extra clicks.

## State of play
Juniper No.1 requires 2 picks, 2 shovels, pry bar and hammers, wheelbarrow, timber/bracing and ore sacks according to Amos's preliminary recommendations. Prior quoted local basket $49.20; Deadwood goods basket $47.25 before freight, each quote and stock to be confirmed. Outstanding mine development decision: player must choose the specific goods and suppliers; no procurement or excavation automatically committed.

## VEIN-UX-007 — Search Discussions / Action Discovery (new clarification, October 11)
**Trigger:** User reasonably doubts that a non-LLM game can interpret arbitrary typed dialogue reliably. Requests searchable keywords and suggestions instead of a promised unrestricted free-text interpreter.
**Decision:** Favor a deterministic, discoverable **Search Discussions** interface over a misleading universal **Custom Proposal** button. An optional natural-language-like search box may rank supported intents; no open-ended language comprehension guarantee.

### Unity interaction workflow
1. Click a Person or Business; identify an available in-world conversation partner through actual knowledge, presence, travel, staff or delegated contact (do not teleport conversation).
2. Open **Discuss**. Search field placeholder: `What would you like to discuss?` with clickable category shortcuts below.
3. Type a phrase like `sell 10 steers each month`, `who carries your goods`, `buy your shop`, `need a teamster`, or `borrow $200`.
4. Search uses a local **Action/Intent Registry** with manually authored action ID, supported verbs, synonyms, object keywords, prerequisites, dialogue entry points and authority source. Case-folding, word splitting, stemming/plurals, typo tolerance and simple entity/numeric parsing rank suggestions. Intent availability derives from shared state; keywords are discovery only, not permissions.
5. Display 2–5 proposed supported actions including structured descriptions, e.g. `Propose recurring supply agreement`, `Offer to purchase goods`, `Ask about freight carrier`. A tapped result opens a **parameterized offer form**, allowing counterparty, goods/service, quantity, quality, cadence, delivery, price, credit, location, start date and other contextually relevant terms. Only display fields meaningful to the selected agreement type.
6. Player reviews an explicit `Proposed terms` summary and chooses **Submit Offer**. Simulation checks counterpart, delivery feasibility as a future risk, actual ability to agree, knowledge, jurisdiction, authority, NPC incentives and potential counteroffer. Offering stock not yet owned is permitted; signing never mints goods or cancels obligations.
7. If matching confidence is poor, show `No matching discussion found` with suggested categories and supported alternatives. Do **not** execute the closest guess or invent a generic contract. The user can rephrase or browse all actions.

### Proposed implementation constraints
- Start with a modest manually authored library of reusable **action types**, not thousands of hand-scripted conversational branches. Individual deal variants come from structured parameters, not unique dialogue text.
- A phrase may match multiple intents: `sell my cattle` might mean immediate sale, recurring supply or brokering third-party cattle. Let the player select the intended action.
- Parsing a number, unit, animal/good and cadence can prefill fields, but the player must confirm. Parsing must reject or ask about ambiguities (`10 cattle` vs `10 steers`; `each month` vs one-time).
- Personalized options depend on actual known Person/Business, role, access, possession/ownership evidence and scenario legal regime. No omniscient contact directory or private business books.
- NPCs use the same underlying action definitions and Agreements; they need not simulate text searching. They choose intentions from goals, opportunities, contacts, travel and delegated authority.
- Do not depend on an LLM, cloud service or arbitrary unbounded natural-language parser. More sophisticated natural language interpretation can remain a separate optional experiment, contingent on reliability and performance.

### Acceptance examples
- `Who delivers your merchandise?` → `Ask about existing freight arrangements`; NPC may decline, misremember or disclose a contact.
- `I'll bring you 10 steers a month` → `Propose recurring goods supply` with species/quantity/frequency and price fields; player may own no cattle. No imaginary cattle on agreement acceptance.
- `Would you sell your business for $800?` → `Offer to purchase business` with price and terms, subject to correct seller, authority and actual Business ownership.
- `Could you lend me $200?` → `Request loan` with principal, interest, term, collateral; Person may refuse.
- Unsupported or ambiguous text → suggestions or manual categories, not a silent fabricated action.

**Status:** Design proposal / UI and action-discovery implementation guidance. Needs repository audit before referring to any feature as implemented.
