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
