# UBC B1 — NPC Business Autonomy Audit (Workstream B)

Date: 2026-11-17. Lane B1, base integration branch
`muse/universal-business-construction-2026-11-17` at `1cba10e5`
(E1, S2, D1, A1, S3, G1 merged). Harness baseline in this tree:
5049 passed / 1 failed / 5050 total (the single failure is
Pass9RegistryAssemblyScanTests, the 7 VetServices types S4 is
classifying — not in this lane's scope).

Scope: NPC business autonomy — formation, lifecycle
(struggle / pivot / close), credit, property, hiring, pricing,
capability expansion, and the boarding-house-plus-meals worked
example. Method: read the production call chain for every
decision authority (not the planning docs), confirm each
caller/callee from disk, and reproduce the break in the code
before naming it. No finding below is speculative: every break
names the caller that fails to call, the callee that is never
reached, and the exact lines.

## What IS wired (verified, not assumed)

- W-1 — Weekly lifecycle evaluation runs. `SimulationDrivers`
  calls `DriveNpcBusinessLifecycleWeek` (line 4220), which drives
  `NpcBusinessLifecycleDriver.EvaluateWeek` for every live
  NPC-owned business with a real cash port, obligations,
  employments, founder ledgers, formation assets, the production
  liquidation port, events, ids, and the creditor cash bridge,
  evaluated against the shared calibrated survival reserve
  (payroll + reorder based, per-type table as fallback).
  `NpcBusinessLifecycleService.EvaluateWeek`
  (`Domains/Economy/Creation/NpcBusinessLifecycle.cs:253-360`)
  marks struggle, recovers after 2 healthy weeks, and spends
  real money seeking customers.
- W-2 — Entrepreneurship observation and investigation run.
  `SimulationDrivers` calls `DriveEntrepreneurshipWeek` (line
  4217), which drives `NpcEntrepreneurshipDriver.EvaluateWeek`
  (`Domains/Economy/Creation/NpcEntrepreneurshipDriver.cs:85`):
  real unfulfilled needs the person witnessed, recognition
  thresholds on the log itself, real person time reserved for
  investigation, real supplier lookup, real financing check
  against the founder household's own ledger. No quotas, no
  manufactured demand.
- W-3 — Formation through the adapter is wired.
  `SimulationSystemsHub.NpcBusinessCreation`
  (`Orchestration/Systems/SimulationSystemsHub.cs:1110`)
  constructs `NpcBusinessCreationAdapter`; `SimulationDrivers`
  binds `TypedPremisesAssigner` to
  `runtime.TryAssignNpcBusinessPremises` and `BusinessCreated`
  to adoption via `TryAdoptNpcBusiness`
  (`Orchestration/Systems/SimulationDrivers.cs:3887-3895`).
  Formation goes through the same generic BIZ-1 authority the
  player uses.
- W-4 — NPC weekly payroll, reorder, and restock run through
  the shared business runtime (established by lane A1: the NPC
  restock driver spends real business cash; the shared weekly
  pass resolves payroll and reorder).
- W-5 — Boarding-house-plus-meals, the worked example, is
  wired at the operational level. `DriveBoardingHouseDays`
  (`SimulationDrivers.cs:6161`, called at :4651) calls
  `BoardingHouseShopRuntime.ExecuteDay`, which serves today's
  board meals through `BoardingHouseKitchen.ExecuteDay`
  (kitchen, food lots with provenance, cordwood fuel per meal,
  board-included entitlement per Canon 2.5).
  `DriveBoardingHouseWeeks` (:6174) settles weekly rent through
  `RentCollectionService`. Lodging plus meals already operate
  as one business; the gap is ADDING a capability to an
  operating business, which is B1-2.
- W-6 — `CloseOrderly` is complete and tested
  (`NpcBusinessLifecycle.cs:500-700`): settle obligations from
  real cash, explicitly assume leftovers by the founder (never
  stranded, never deleted), liquidate formation assets to real
  buyers through the production liquidation port, release staff
  through the employment authorities, return residual cash to
  the founder's household. Tests:
  `PhaseGNpcBusinessLifeTests.CloseDown_*`.
- W-7 — `Pivot` is complete and tested
  (`NpcBusinessLifecycle.cs:435-494`): new capability ids plus
  a real re-tooling cost charged through the cash port, state
  Pivoting, completion on a healthy week. Test:
  `PhaseGNpcBusinessLifeTests.Pivot_PaysRealRetoolingCost`.

## B1-1 — NPC formation is impossible in production (TOP BREAK, NOT repaired here)

Expected: an NPC who observes sustained unfulfilled demand, has
capital, and finds a viable shop can form a business through the
autonomous pipeline.

Observed: every production investigation walks away, forever.

Evidence chain:

1. Caller: `SimulationDrivers.DriveEntrepreneurshipWeek`
   (`Orchestration/Systems/SimulationDrivers.cs:4232-4272`)
   builds the `EntrepreneurshipContext` with `PremisesFinder`
   and `EquipmentFinder` left unbound (null). Comment at
   :4248-4254: "PremisesFinder and EquipmentFinder intentionally
   remain unbound until production can return actual property
   rights and an actual stocked/sold equipment asset. A free
   building id plus fixed rent, or a provenance string without
   a transaction, is not evidence that the Person can lawfully
   open a Business."
2. Callee: `NpcEntrepreneurshipDriver.InvestigateAndMaybeForm`
   (`Domains/Economy/Creation/NpcEntrepreneurshipDriver.cs:236-243`)
   calls both finders when present; with both null, `premises`
   and `equipment` are null and the evidence carries empty
   option lists.
3. Callee: `NpcBusinessInvestigation.Conclude`
   (`Domains/Economy/Creation/NpcBusinessInvestigation.cs:454-517`)
   counts viable premises (tenure set + a parcel/building ref)
   and proven equipment (a provenance string). Zero viable
   premises adds the "no premises" reason (:470); zero proven
   equipment adds the "no equipment" reason (:516). Any reason
   forces `WalkAway` (:528-535); `Proceed` requires every area
   to support it.
4. Consequence: `FormBusiness` (driver :377+) is unreachable
   in production. The pipeline observes and investigates every
   week and can never form. The town's NPC business population
   can only shrink — and, before this lane's repair, could not
   even do that (B1-2).

Reproduction: in the development town, take any adult NPC with
full labor-market access whose household has an unfulfilled need
older than 7 days and at least 2000c savings. Each week the
driver records the observation, recognizes the opportunity,
investigates, and concludes WalkAway with "no premises" and
"no equipment". No formation intent is ever produced.

Why this lane does not repair B1-1: there is no production
source for the missing evidence, and inventing one is
forbidden. Commercial lease listings do not exist:
`NpcPropertyDecisions` (`Domains/World/Property/NpcPropertyDecisions.cs`)
and `NpcPropertyAcquisition`
(`Domains/Economy/Financing/NpcPropertyAcquisition.cs`) are
housing-only (a household buying a home, with seller finance
and lender offers). The used-equipment market book
(`UsedEquipmentMarketBook`,
`Domains/Economy/Equipment/UsedEquipmentMarket.cs:326`) has
zero production references — it is unwired and unpopulated,
so no live listing can supply equipment provenance. Building
a commercial lease authority plus market wiring plus live
listings is a multi-subsystem job, not a one-shot repair. It
is recorded here as the follow-up; this lane repairs B1-2
instead, which is small, uses existing authorities, and
unblocks the other half of the population dynamic (businesses
can fail and close).

## B1-2 — NPC businesses can never pivot or close in production (REPAIRED IN THIS LANE)

Expected: a business that cannot cover its survival reserve
for months closes orderly; a struggling business with a viable
new line pivots to an additional legitimate capability
(brief section 18 scenario F).

Observed: NPC businesses are immortal strugglers.

Evidence:

- `NpcBusinessLifecycleService.Pivot`
  (`NpcBusinessLifecycle.cs:435`) and `CloseOrderly` (:500)
  have zero production callers. A repo-wide grep over
  production files finds only test callers
  (`PhaseGNpcBusinessLifeTests`, `Pass8AdverseCirculationTests`,
  `Pass11Ws4SettlementProof`).
- `NpcBusinessLifecycleService.EvaluateWeek` (:253-360) never
  calls either. Below the reserve it marks the record
  Struggling, records a cost-cut event, borrows only when a
  borrow context is supplied, and spends on customer-seeking.
  `ConsecutiveStruggleWeeks` grows without any consequence;
  there is no sustained-struggle exit anywhere in the method.
- The production driver even passes the liquidation port into
  the lifecycle context with the comment "(EvaluateWeek does
  not consume it; CloseOrderly does.)"
  (`NpcBusinessLifecycleDriver.cs`, context construction) —
  the wiring anticipates a call that never comes.

Repair (this lane): `EvaluateWeek` now invokes `CloseOrderly`
when a record reaches sustained struggle — see "Repair"
below. The pivot half stays open: `Pivot` needs a
capability-demand policy (which capability, on what demand
evidence, at what re-tooling cost) that no production caller
currently supplies. That policy is recorded as a follow-up,
not invented here.

## B1-3 — The "cut costs" struggle response is theater

`EvaluateWeek` response 1
(`NpcBusinessLifecycle.cs:322-324`) records only an event:
"the owner trims orders and defers maintenance." No reorder
threshold, payroll, or spending change follows anywhere in
the method — no real cost is cut. Expected: a real spending
reduction. Observed: a diary entry. Not repaired in this
lane; the B1-2 repair bounds how long the theater runs
(eight weeks below reserve, then the business closes).

## B1-4 — The NPC credit loop is dead in production

`NpcCreditDecisions`
(`Domains/Economy/Financing/NpcCreditDecisions.cs`) has zero
production references (repo-wide grep, production files
only). The lifecycle driver passes a null borrow context
(`NpcBusinessLifecycleDriver.cs:107-110`): "Borrow context
is null in this pass: a struggling business still cuts costs
and seeks customers with real money; the real credit-loop
borrow path (BorrowForBusiness) stays available for a later
pass with a production lender wired." Expected: a struggling
business can borrow through the real credit loop
(`NpcBusinessLifecycleService.BorrowForBusiness`, :360).
Observed: `BorrowForBusiness` is unreachable in production.
Follow-up: wire a production lender (the credit engine,
lender profiles, and `NpcCreditDecisionEngine` exist and are
tested; only the production call is missing).

## B1-5 — NpcPropertyAcquisition.CloseAcquisition has zero production callers

Carried open item, confirmed by grep: the only
`CloseAcquisition` hits in production are
`AcquisitionMarketManager`'s player-flow method
(`Domains/Economy/Acquisitions/AcquisitionMarketManager.cs:8614`).
NPC property purchase cannot close. Follow-up: give the
property-acquisition flow a production caller (the decision
engine in `NpcPropertyDecisions.cs` produces decisions; nothing
executes them).

## B1-6 — No NPC hiring driver

No NPC hiring or recruitment driver for NPC businesses was
found (grep for `NpcHiring`, NPC `HireWorker`, NPC recruit:
empty). Employment exists through the registries and weekly
payroll, and `RecruitmentService` exists in the hub, but no
NPC-business caller was identified in this pass. NPC owners
never initiate hires autonomously; a business that needs
hands can only wait for the player or for formation-time
staffing. Follow-up, not repaired here.

## B1-7 — No NPC pricing decisions

No NPC pricing or price-decision authority was found (grep
for `NpcPric`, price decisions: empty; the only "price" hit
is property negotiation in `NpcPropertyAcquisition.cs`).
NPC prices come from config and price ports; demand, cash,
competition, and margins never feed an NPC pricing decision.
Follow-up, not repaired here.

## Repair — sustained struggle closes the business orderly (B1-2)

Change (one production file):
`Domains/Economy/Creation/NpcBusinessLifecycle.cs`,
`NpcBusinessLifecycleService.EvaluateWeek`. After the three
struggle responses (cut costs, borrow when a context exists,
seek customers), when `record.ConsecutiveStruggleWeeks`
reaches `CloseAfterConsecutiveStruggleWeeks` (8), the method
invokes the existing `CloseOrderly` with a real-life reason
("cash below the survival reserve for 8 consecutive weeks")
and returns. `CloseOrderly` itself is untouched: obligations
settle from real cash or are explicitly assumed by the
founder, formation assets liquidate to real buyers through
the production liquidation port already present in the
context, staff are released through the employment
authorities, residual cash returns to the founder's
household, and the record ends `Closed` with a `CloseComplete`
event. A record that recovers before week 8 never closes:
the healthy branch resets `ConsecutiveStruggleWeeks` to 0
before the struggle branch is reached, and the existing
early return for `Closed` / `Closing` / `Acquired` makes the
close idempotent.

Threshold rationale (PROVISIONAL calibration, for Kennedy to
correct — same standing as the per-type survival-reserve
table, which the driver documents as "calibration, not
canon"): the survival reserve is roughly a month of
operating buffer. A frontier shop that cannot rebuild a
month's buffer for two consecutive months has exhausted the
responses available to it (cost cuts, borrowing where a
lender exists, customer-seeking spend) and closes rather
than trading indefinitely on nothing. Eight weeks is long
enough that a seasonal dip or a single bad month does not
kill a viable business, and short enough that a dead
business does not sit on the books forever.

Consequences, stated plainly:

- The weekly driver evaluates ALL NPC-owned businesses,
  including the seven authored development-town businesses.
  If one of them genuinely sits below its calibrated reserve
  for eight straight weeks in a live run, it will close
  orderly. That is the intended behavior of this system
  (businesses can fail), but it is a behavior change to
  verify in Unity — a closed authored business changes the
  development town roster. No harness test is affected: no
  existing test drives eight struggle weeks (verified by
  inspection of `PhaseGNpcBusinessLifeTests`; the longest
  struggle run is two weeks).
- The pivot half of B1-2 is NOT repaired: `Pivot` still has
  no production caller. Closing bounds the failure mode;
  restructuring still needs a capability-demand policy.
- `CloseOrderly` needs the liquidation port and the founder
  ledger from the context; the production driver already
  supplies both. In test fixtures with a null liquidation
  port, `CloseOrderly` skips liquidation and flags it —
  the close still completes, which the regression tests pin.

Regression tests (new harness-compiled file
`Tests/Editor/Economy/NpcBusinessLifecycleCloseTests.cs` —
the existing `PhaseGNpcBusinessLifeTests.cs` is
harness-excluded because its fixture uses credit types the
harness does not compile; this file uses only
harness-compiled types and the production shape of a null
borrow context):

- `Struggle_SevenWeeks_StaysStruggling_NotClosed` — seven
  consecutive weeks below reserve: still Struggling, not
  Closed, no close events.
- `Struggle_EightWeeks_ClosesOrderly` — the eighth week
  closes: state Closed, `CloseComplete` recorded, an open
  obligation settled from real cash, a formation asset
  liquidated to the fake buyer, residual returned to the
  founder's household, nothing left in the business.
- `Struggle_RecoversBeforeEightWeeks_DoesNotClose` — seven
  struggle weeks then a healthy week resets the count; a
  further seven struggle weeks still do not close.
- `Close_AlreadyClosed_IsIdempotent` — a second evaluation
  of a Closed record changes nothing.

## Follow-ups (recorded, not repaired)

- F-B1-1 — Commercial premises evidence for NPC formation:
  a lease-listing authority for vacant shops (real agreements,
  real rents) and wiring plus live listings for the
  used-equipment market book, so `DriveEntrepreneurshipWeek`
  can bind production `PremisesFinder` / `EquipmentFinder`.
  Until then B1-1 stands: NPCs cannot form businesses.
- F-B1-2 — Pivot trigger policy: which capability a
  struggling business adds, on what real demand evidence, at
  what re-tooling cost; then a production caller for `Pivot`.
  (Brief section 18 scenario F.)
- F-B1-3 — Real cost-cutting response: make the struggle
  "cut costs" response change a real spending lever
  (e.g. defer the week's reorder) instead of recording an
  event.
- F-B1-4 — Wire the NPC credit loop: a production lender for
  `BorrowForBusiness` via `NpcCreditDecisions`.
- F-B1-5 — Production caller for
  `NpcPropertyAcquisition.CloseAcquisition`.
- F-B1-6 — NPC hiring driver.
- F-B1-7 — NPC pricing decisions from real demand, cash,
  competition, and margins.

## Verification

- Baseline in this tree reproduced first: 5049 passed /
  1 failed / 5050 total (log
  `/home/hatch/ll-ubc-b1-run0.log`; the single failure is
  Pass9, S4's scope).
- Post-repair harness run: 5053 passed / 1 failed / 5054
  total, build exit 0 with 0 errors (build log
  `/home/hatch/ll-ubc-b1-build2.log`, run log
  `/home/hatch/ll-ubc-b1-run2.log`). All 4 new
  `NpcBusinessLifecycleCloseTests` pass. The single failure
  is still Pass9 only — FAIL set unchanged from baseline,
  zero new failures. (One intermediate run failed a single
  new test on a test-fixture arithmetic error — the seed cash
  sat above the reserve so the business never struggled;
  fixed by seeding below the reserve and above the test
  obligation; no production change was involved.)
