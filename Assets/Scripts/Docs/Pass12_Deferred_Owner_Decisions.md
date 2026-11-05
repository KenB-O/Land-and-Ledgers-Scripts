# Pass XII — Deferred Owner Decisions (Worker F draft)

Date: 2026-10-28. These are decisions ONLY Kennedy can make. Nothing
in this document authorizes implementation; each section states what
is blocked, what happens under each option, and what a future pass
must not do meanwhile. No Canon document is modified by this draft.

## G1 — RealDemandSource: wire, replace, or retire (DECISION REQUIRED)

Status: `RealDemandSource` remains registered unwired in
`Docs/UnwiredSystems.md`. It was never wired into the live tick in
Pass XII, deliberately: wiring it merely to claim it active would
risk double-buying, because household needs are already consumed
exactly once by embodied shopping and the older demand-discovery
paths. The Pass XI WS2 decision memo (research only, no code
changed, no executor built) frames four options:

1. Remain a read model. Demand is observed and reported, never
   executed. Consequence: no gameplay effect, no double-buying
   risk, no new authority; the older demand paths keep running.
2. Become an input to the existing purchase executor (the memo's
   recommended option IF demand collection should affect play).
   Consequence: one purchasing authority stays authoritative; the
   intent collection feeds it instead of competing with it.
   Requires an intent-to-purchase mapping that consumes each need
   exactly once and retires or subordinates the specific older
   demand-discovery path it replaces, named in the decision.
3. Replace a specific older demand-discovery path outright.
   Consequence: cleaner architecture, but the replacement must
   prove, by test, that no need is double-counted during the
   transition and that every demand the old path served is still
   served.
4. Not yet necessary. Consequence: the type stays registered
   unwired; revisit when a gameplay gap is demonstrated, not
   assumed.

Worker F recommendation: option 2, and only if Kennedy confirms
that demand collection should affect play; otherwise option 1.
What must NOT happen meanwhile: no wire into the tick, no second
purchasing authority, no intent collection that also executes
purchases, no retirement of an older path without naming which
path and proving its demand coverage is preserved. The wire/
replace/retire choice stays Kennedy's; shared policy-neutral
supporting code that cannot create duplicate transactions may be
added by a future pass, but the choice itself is not delegated.

## G2 — Class E policy questions (DESIGN APPROVAL REQUIRED)

Open, unchanged by Pass XII (PASS9-010). No rule was invented for:
finite mineral reserve (how much ore exists, how depletion is
measured, what happens at exhaustion); severable mineral rights
(who owns subsurface rights separately from surface title, how
they transfer); water rules (rates, consequences, access rights —
see also Docs/Pass8_Water_Verdict.md: Canon sets none of these);
court procedure detail (how disputes are adjudicated beyond the
claim records that exist); bank officer identity (who a bank's
officers are and what authority they hold); childcare and fatigue
designs (how dependent care and exhaustion constrain labor).
Consequence of deciding: each answer becomes a Canon-level rule
that production code and tests must follow; until then, code must
fail closed or leave the behavior honestly unset rather than
inventing a plausible rule. The mine-closure driver plan (PASS7-003)
is gated on the mineral reserve decision and was not built.

## G3 — Foreclosure auction and household lending (DESIGN APPROVAL REQUIRED)

Open, unchanged by Pass XII. No foreclosure law, lending
eligibility rule, or auction settlement procedure was invented:
who may bid, how a distressed parcel is appraised for sale, what
creditor consent is required for note assumption, how auction
proceeds are distributed, and who may borrow against what
collateral are all undecided. This directly blocks PASS11-005
(named-buyer distress sales): Worker C audited and ruled out
business liquidation, HousingSearch negotiations and tax-sale bid
name strings as buyer sources, and no solvent-household buyer
register, appraised parcel sale-price feed or creditor consent
source was built. Consequence of deciding: the named-buyer sale
path, the foreclosure path and the household lending path can
then be built against real rules; until then, distressed sales
stay blocked rather than inventing a buyer.

## A-14 — Scenario late-latch semantics (UNCHANGED, DECISION REQUIRED TO FLIP)

Late-latch semantics are unchanged through Pass XII, as through
Pass XI (both WS1 and WS5 examined them; lenient behavior is
pinned by existing latch tests). Flipping the semantics — so a
late-completed objective no longer latches, or vice versa — is a
design decision, not a bug fix, and if it flips, the latch tests
and the WS5 presentation-boundary comment must be updated
together. Consequence of flipping: scenario completion/failure
history changes for in-progress saves; old saves must be checked
against the new rule. No change was made in Pass XII.

## Simulation time — PROPOSAL ONLY, NOT IMPLEMENTED

The proposal (1:1 = 24 real hours per game day; 1x = 5 min/day;
2x = 2 min 30 s; 5x = 60 s; 10x default = 30 s; 25x = 12 s; 50x = 6 s;
Pause = no progression) remains a proposal. Pass XII did NOT
modify TimeManager, speed settings, or Unity assets. The Pass XI
review found it TECHNICALLY APPROPRIATE with conditions, and those
conditions are preserved here as the implementation plan a future
authorized pass must follow:

- Clock math lives in TimeManager.Update; the base of 300 real
  seconds per game day at 1x maps exactly to the proposed table
  (1x 300 s, 2x 150 s, 5x 60 s, 10x 30 s, 25x 12 s, 50x 6 s).
  Required source change (when authorized): realSecondsPerGameDay
  At1x 60 → 300, plus a settings-asset test.
- The SimulationSpeed enum has values Paused, 1, 2, 4, 6, 10, 25,
  50, 100; value 5 is unused and safe to append for the 5x step.
- 1:1 needs a TimeManager special case (300/86400 ≈ 0.00347
  visual scale); AgentMover's visual scale at 1:1 needs a floor or
  a decouple decision — a person crossing the map in real time
  must not become invisible or frozen-looking.
- HUD speed buttons for the new steps are Unity authoring work.
- The Pass XI review specified 11 mandatory acceptance tests for
  the change; they must be written and passing before the change
  ships, and the old behavior (default 10x at base 300 = a day
  every 30 real seconds) must be compared explicitly.
Owner decision required before any of this is implemented.

## Bank capacity consolidation and related-party policy (OWNER DECISIONS)

Open, unchanged by Pass XII. Two linked questions: (a) what
consolidates a bank's capacity — how deposits, reserves, loans
outstanding and capital combine into a lending limit, and what
happens when it is reached; (b) which related-party transactions
are legitimate — owner loans to their own business, family lending,
and business-to-business transfers between commonly owned
entities. Until decided, the code must not invent a cap or a
related-party rule, and must not silently allow unlimited
related-party credit to stabilize a failing business. These
questions interact with G3 (household lending eligibility) and
with the bank ledger binding contract (one ledger per bank,
fail-loud, deliberately not wired into underwriting to avoid
double-counting).

## Scenario catalog scene remainder (PASS9-012)

Open. Beyond the two coded scenarios, the scenario catalog and the
CalendarAnchor scene-side remainder are Unity scene work: scenario
selection UI, calendar anchoring in the Main Scene, and any
additional scenarios Kennedy wants are not source-complete.
The source side (scenario start wiring, dated qualifiers, rebuild
after load, failure notice board contract) is done and tested;
the scene remainder must not be described as source work.

## November 5, 2026 — Owner design decisions (Muse session)

Date: 2026-11-05. Owner-approved design principles recorded by
Kennedy during the November 5, 2026 Muse development session.
These are design principles, not implementation. Nothing in this
section authorizes code changes; each principle states the rule
and what must not happen meanwhile. No Canon document is modified
by this record. The full brief text lives in the session handoff
record (workspace: land-and-ledgers/regional-2026-11-05/
37-final-handoff-record.md, section 9).

### D1 — Historical realism (APPROVED PRINCIPLE)

Land & Ledgers prioritizes historical plausibility and realism in
its economic and social simulation. The intended opening era is
approximately 1870, with scenarios potentially continuing into the
early twentieth century. Consequence: economy and society design
must stay plausible for that era. What must NOT happen meanwhile:
no anachronistic mechanic justified as convenience.

### D2 — NPC autonomy (APPROVED PRINCIPLE)

NPCs behave as independent people and entrepreneurs. They pursue
opportunities, assess risks, and make decisions based on their
knowledge, personality, experience, capital, skills, and
relationships. They may make mistakes, learn from mistakes, and
become wealthy or bankrupt. Their decisions must remain causally
understandable and economically plausible. The simulation must not
guarantee their success. Consequence: systems must allow genuine
NPC failure and success, not scripted outcomes. What must NOT
happen meanwhile: no hidden success guarantee, no NPC behavior
that cannot be explained from its own state and history.

### D3 — Business decisions (APPROVED PRINCIPLE)

Businesses use historical sales, current inventory, projected
demand, lead times, cash, labour capacity, and risk preferences
when making decisions. Stock replenishment should use
forward-looking reserve levels rather than waiting until inventory
is completely exhausted. Businesses may schedule work in advance.
Unexpected events can change their priorities. Repeated failures
caused by insufficient labour may motivate hiring or delegation.
Family succession, retirement, and apprenticeship are legitimate
influences on employment decisions. Consequence: replenishment
and employment systems must be forward-looking and event-aware.
What must NOT happen meanwhile: no wait-until-empty replenishment
presented as the intended design.

### D4 — Business relationships (APPROVED PRINCIPLE)

NPCs should build genuine business and personal relationships
through repeated interactions. Trust, loyalty, grievances,
reputation, family connections, and previous dealings matter. A
supplier may extend credit to a trusted merchant. A long-standing
customer may remain loyal despite a competitor offering a slightly
lower price. Business relationships should emerge from actual
history and actions rather than arbitrary bonuses. Consequence:
relationship effects must be traceable to recorded history.
What must NOT happen meanwhile: no arbitrary relationship bonus
with no recorded cause.

### D5 — Credit and financial hardship (APPROVED PRINCIPLE)

Distinguish ordinary trade credit from formal loans. Households
and businesses may seek assistance from family, friends, private
lenders, and institutions. Debt preferences differ between
individuals. Financial hardship may produce asset sales,
employment changes, business closure, bankruptcy, or ownership
transfers. The economy must not artificially protect businesses
from legitimate failure. Consequence: failure paths (closure,
bankruptcy, transfer) are legitimate outcomes, not errors.
What must NOT happen meanwhile: no artificial stabilizer that
rescues a failing business, and no conflation of trade credit
with formal loans.

### D6 — Player knowledge (APPROVED PRINCIPLE)

The floating camera does not provide omniscience. It provides
visual observation of the playable world. The player may observe
buildings, construction, traffic, businesses opening and closing,
and other publicly visible events. However, clicking an unfamiliar
person or building must not reveal information the player has not
learned. An unmarked unfamiliar building may remain unidentified.
A visible business sign can provide legitimate public information.
Personal knowledge may come from prior visits, conversations,
correspondence, public records, family connections, or other
legitimate sources. The player must not automatically know
private finances, hidden debts, unpublished contracts, personal
relationships, private intentions, unknown identities, or private
inventory information. Trade negotiations and information
discovery should involve the actual player character, legitimate
communication, or authorized delegation. Knowledge acquired
through another NPC should have an identifiable informational
source. NPCs should operate under comparable information
limitations appropriate to their circumstances. Consequence: any
information display must be traceable to a legitimate source.
What must NOT happen meanwhile: no UI that reveals private state
the player has not learned.

### D7 — Player activity (APPROVED PRINCIPLE)

The player should have many meaningful decisions available
throughout a session without excessive notifications or artificial
busywork. The player can customize a business template, select
products, compare suppliers, establish contracts, set prices,
acquire land, hire workers, seek construction quotations, and
pursue expansion. Businesses and market opportunities should
create emergent work rather than relying exclusively on scripted
objectives. Consequence: systems should generate decisions from
the economy, not from a script queue. What must NOT happen
meanwhile: no notification spam or busywork presented as content.

### D8 — NPC competition (APPROVED PRINCIPLE)

NPC businesses may deliberately compete against one another and
the player. They can use legitimate strategies involving prices,
suppliers, customer relationships, hiring, property, and other
economic opportunities. Scenario difficulty may affect competitor
aggressiveness and attention toward the player. It must not give
competitors impossible information, unlimited resources, or
arbitrary advantages. Competitors can fail while attempting
aggressive strategies. Consequence: competition is real but fair;
difficulty tunes attention, never omniscience or resources.
What must NOT happen meanwhile: no competitor advantage that
cannot be explained by its own state.

### D9 — On-map and off-map towns (APPROVED PRINCIPLE)

Scenarios may contain multiple fully playable on-map settlements.
Ashford and Redfield remain off-map economic towns. They should
have persistent businesses and physical economic locations but
are not intended to become explorable 3D settlements. Trade,
credit, and correspondence can connect the player to those towns
without rendering them. Consequence: regional towns are economic
entities with locations, not scenes. What must NOT happen
meanwhile: no 3D settlement work for Ashford or Redfield, and no
claim that they are playable settlements.

### D10 — Simulation speed (FUTURE REVIEW, NOT APPROVED)

Kennedy wants a future review of simulation speeds. The present
fastest practical normal-play settings make individual actions
difficult to observe. Potential normal-play day lengths include
60 seconds and 120 seconds. Real time and accelerated simulation
speeds should be labelled accurately. No final new clock
configuration was approved on November 5, 2026. Do not change
simulation timing during this wrap-up. TimeManager was not
modified on November 5, 2026. Consequence: timing is a future
design and testing priority, not a current change. What must NOT
happen meanwhile: no clock change, no speed relabelling, and no
claim that a new configuration was approved.

## November 5, 2026 — Session decisions (regional economy)

Date: 2026-11-05. Decisions ONLY Kennedy can make, blocking
further work on the November 5 regional economy. Recorded in the
session decision briefs (workspace: land-and-ledgers/
regional-2026-11-05/20-decision-briefs.md). Nothing here
authorizes implementation.

### R1 — Whole-package retail purchase, flour sacks (DECISION REQUIRED)

The conversion number is settled: 1 sack = 10 lb (Series2FlourUnit
Conversion precedent, asset agrees). The open question is ONLY
household purchase semantics. Options: (A) whole sacks only —
purchases round up to sack multiples at 35c per 10 lb received;
(B) broken bulk at 3.5c/lb — invents bulk-breaking and needs a
sack-remainder rule; (C) keep dropped — households cannot buy
flour at all. What must NOT happen meanwhile: no bulk-breaking
rule invented, no sack weight assumed beyond the authored 10 lb.

### R2 — Freight allocation across a day's shipment group (DECISION REQUIRED)

Delivered-cost choice assumes solo freight per order (Slice 15,
documented conservative). Options: (A) keep solo until Codex
freight execution exists; (B) marginal-zero — first order that
day bears full freight, later ones free; (C) split by units —
two-pass collect-then-choose restructure of EvaluateRestock.
What must NOT happen meanwhile: no shared-freight assumption in
the choice, and no re-pricing after cancellation (the chain does
not re-price).

### R3 — Demand model for non-food household goods (DECISION REQUIRED)

Corrected November 5, 2026: the shopping loop is purely
need-driven, and in production needs come only from the shortage
monitor evaluating MealItemPriority (7 foods) plus fuel wood.
Sugar, coffee, tea, salt, soap, and candles — already in the
catalog and stocked by the store — never generate a need and
never sell. Appending the 18 unoffered items' definitions to the
household catalog would make them offerable and holdable and
change exactly zero sales. The real decision is whether to extend
the demand model to non-food household goods (a Canon-adjacent
design choice: consumption rates, need thresholds, whether fresh
beef/bacon/cheese demand displaces preserved_meat). What must NOT
happen meanwhile: no catalog append presented as coverage, and
no demand rate invented.

### R4 — Daily bakery bake policy (DECISION REQUIRED, OPEN SINCE OVERNIGHT)

Product, batch count, fuel, and off-map receipt for the bakery's
daily baking. The bakery driver is unwired in production (null
fuel source; the live bakery never bakes via the driver) and the
daily bake policy is Kennedy's decision, not an agent's to invent.
What must NOT happen meanwhile: no invented bake quantities,
product mix, or fuel consumption rates.
