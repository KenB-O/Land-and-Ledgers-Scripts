# Agent B — Package 10 checkpoint (B-lane queue audit)

- **Branch:** `muse/campaign-b-economy-2026-11-15`
- **Commit:** `4c3d98e71f19a255867068780f11c2c59468d60b`
- **Parent:** `cc19849d` (DEF-006 MakeOffer half, package 9)
- **Base:** `4ee829a9` (maintenance pass)
- **Files changed (repo):** 2 files, documentation only —
  `Assets/Scripts/Docs/CampaignBQueueAudit2026-11-15.md` and its `.meta`.
  **Zero production files changed. Zero test files changed.**
- **Correction (package 11, E checkpoint 19):** header previously recorded the
  superseded pre-amend SHA `6228bc0d` (not an ancestor, not in the B log, not
  on the remote — the amend changed the hash, so the file could not record its
  own amended SHA) and the pre-copy file list; corrected here to the actual
  amended commit and the actual 2 files.
- **Harness:** no run — nothing executable changed. Prior suites stand at 102 tests
  (9+6+12+7+9+13+16+14+16) from package 9.

## Method

Read the current defect register (`DEFECT_REGISTER.md`, 3,371 lines, update 11)
and listed every entry with base assignment Agent B whose status is CONFIRMED or
AWAITING OWNER DECISION: **25 entries** — 7 AWAITING OWNER DECISION, 18 CONFIRMED.
For each, the entry's own evidence was taken as the starting point and one
targeted source check was run in the B tree (grep / call-site count / read of
the named lines) — no re-audit from scratch. Each entry is classified below as
(a) mechanical fix possible without inventing a policy, price, wage, conversion
factor, or business rule, (b) blocked on an owner decision / v3.2 / a live Unity
run / another entry, or (c) already characterized with nothing further to code.

## Bottom line

**Yes — the B lane has further authorized mechanical work.** Six Tier-1 items
below need no owner decision and invent nothing. The lane is NOT waiting on
Kennedy for its next package. It IS waiting on Kennedy (and on v3.2, and on the
Codex Unity run) for the eleven Tier-3/Tier-4 items and for the live-run proof
of DEF-005.

**Recommended next package: DEF-018 (verify-then-fix).** It answers Check 6
U-2 — whether the weekly off-map reorder covers shelf flour — and either
refutes the defect or fixes a Medium defect in the vertical slice's flour
supply. After that: DEF-053 (food half), then DEF-055 (clamp diagnostics),
then DEF-037 + DEF-036 (comment corrections, one commit), then DEF-019
(wiring, with proof deferred to DEF-003).

## Tier 1 — Mechanical and authorized (do next, in priority order)

### 1. DEF-018 — Store-shelf flour has no replenishment path (Medium, CONFIRMED)
- **Evidence line:** `StoreRestockDriver.cs:7` — daily review runs with the
  supplier list EMPTY ("P3's regional businesses register themselves here");
  whether the weekly off-map reorder (`StoreProcurementChain.cs:679-717`)
  covers shelf flour is Check 6 U-2, still not established.
- **Package:** verify-then-fix. Read the weekly reorder and determine whether
  flour is covered. If covered, DEF-018 is refuted (document + pin with a
  test). If not, add flour to the weekly reorder under the existing authored
  Millbrook terms (48c/sack) — the only flour supplier in the tree.
- **Judgment call to confirm with the coordinator/manager before wiring:**
  that applying the existing Millbrook flour terms to the store's shelf is a
  reuse of authored terms, not a new price. If the coordinator rules it a new
  term, the package becomes verify-then-document and DEF-018 waits on an
  owner decision instead.

### 2. DEF-053 — Reserve-to-lot seed duplicates food; seeded fuel lots exist twice forever (Medium, CONFIRMED)
- **Evidence line:** `HouseholdInventoryReserveBridge.SeedCategory` reads
  `reserve.currentUnits`, calls `inventory.AddLot(itemId, units, ...)` and
  never decrements the reserve (verified in the B tree); sole production
  caller `DailyNeedsService.cs:367`; `MirrorLotConsumptionToReserve` clamps
  at 0 (`Math.Max(0, ...)`), so a seed-decrement is safe for food.
- **Package:** decrement the reserve by the seeded units in `SeedCategory`
  for `staple_food` and `meat` only, with tests (seed moves N units, total
  conserved; lot consumption still mirrors without going negative).
- **Fuel half needs a design call, not code:** heating reads the reserve
  directly (`HouseholdReserves.ResolveDailyHeatingUse`) and nothing consumes
  fuel lots (DEF-010 trigger off), so decrementing the fuel reserve would
  strand the seeded fuel in lots and starve heating immediately. The two
  options are (a) do not seed `fuel_wood` into lots at all (the reserve stays
  the fuel store — nothing reads fuel lots anyway), or (b) seed it and rewire
  heating to lots (DEF-010 territory, bigger). Recommend (a) unless the
  manager says otherwise; the fuel half stays open pending that call.

### 3. DEF-055 — No single cash writer; mutation primitives clamp silently (Medium, CONFIRMED)
- **Evidence line:** `BusinessRuntimeState.SpendCents` spends
  `Min(balance, requested)` with no diagnostic; `SetCurrentCashCents`
  clamps to `Max(0, cents)` with no diagnostic; `AdjustCashCents` routes
  through it. Six production call-site files verified by grep
  (`SharedBusinessRuntimeManager`, `PlayerPortfolioManager`,
  `BusinessLenderService`, `ManagerDecisionExecution`,
  `NpcOwnerRequestResolver`, `SliceWorkAndPay`); every live debit traced
  pre-checks the balance, so the live failure mode is auditability, not
  leakage.
- **Package (diagnostic layer only):** log loudly (DEF-023 pattern) whenever
  a clamp bites — naming the business, the requested amount, and the amount
  actually moved — plus a test proving an over-balance debit is refused or
  logged rather than silently clamped. Do NOT change clamp-to-refuse in this
  package: callers may rely on partial spend, and changing the semantics
  needs a caller-by-caller audit first.
- **Follow-on (not this package):** the entry-level ledger (business cash
  with source/reason/counterparty per entry, like `HouseholdLedger`) is a
  redesign, not a single-package fix.

### 4. DEF-037 — Stale bakery economics comment cites the old "−20c vs 48c" figure (Low, CONFIRMED)
- **Evidence line:** `BakeryWholesaleDailyDriver.cs:530-549` still cites the
  old Plan 10 E5 "68c vs 48c = −20c/batch" figure; the live line uses the 4c
  account price (`AccountPricePerUnit`, verified in the B tree) and the true
  margin is +30c to −4c depending on the live store flour price.
- **Package:** comment-only correction to the live figures. No behaviour
  change. Pair with DEF-036 in one commit.

### 5. DEF-036 — Notions pipeline-depth header says 0 but live wiring passes 14 (Low, CONFIRMED)
- **Evidence line:** header at `BakeryNotionsDailySupply.cs:29` says the
  pipeline-depth default is 0 "until the owner names the policy (B-1)";
  live wiring at `SimulationDrivers.cs:9528` passes
  `NotionsPipelineDepthDays = ImportCatalog.Get(NotionsMaterialId)?.TransitDays ?? 0`
  = 14 (verified in the B tree).
- **Package:** comment-only correction of the header to the live 14.
  **Needs a manager nod first** (per the entry): confirm the live 14 is the
  intended value before editing the header. Pair with DEF-037 in one commit.

### 6. DEF-019 — Household fuel reserve credit link unwired (Medium, CONFIRMED)
- **Evidence line:** `Series2HouseholdFuelReserveLink.CreditReserve()` has
  exactly one production hit — its own definition; only tests call it
  (verified in the B tree). The legacy executor credits the reserve 1:1
  (`EmbodiedPurchaseExecutor.cs:248`). Phase C receipt paths
  `ReceiveAtHome` (`HouseholdShoppingLoop.cs:2725`) and `ReceiveDelivery`
  (`:2807`) book lots only.
- **Package:** verify-then-wire. First establish which shopping path is live
  (the entry notes no audit has established this); then call `CreditReserve`
  from the live receipt path(s) for fuel items at the legacy 1:1 precedent.
  **Sequenced after DEF-003 for proof:** no fuel can be bought until the
  dealer can sell it, so end-to-end proof (a household buys fuel and the
  heating reserve credits in a run) waits on DEF-003 even though the wiring
  can land now. If the live path turns out to be the legacy executor, the
  link is already live there and DEF-019 is refuted instead.

## Tier 2 — Verify-then-document / characterization (no code fix, pin the state)

### 7. DEF-056 — Three parallel work caps (600 person / 840 bakery / 480 farm) (Medium, CONFIRMED)
- **Evidence line:** `BakeryPersonTimeBudget.cs:12-22` documents the conflict
  explicitly (at capacity 1.00 the person budget binds: 600/180 = 3 batches
  vs the runtime's 840/180 = 4); the bake path uses only the 840 runtime
  budget (DEF-057).
- **Package:** a test pinning which cap binds on the bake path today. The
  one-cap-vs-three question is Check 7 Q4 (manager question, carried as
  question (9)-adjacent) — no code fix without that answer.

### 8. DEF-002 — Notions hard-stop after 60 batches (Critical, AWAITING OWNER DECISION)
- **Evidence line:** total notions ever available without a new supply term
  is 30 (bootstrap) + 30 (finite external order) = 60 batches; "the pipeline
  gives no earlier warning that the end is coming" (register entry).
- **Package (diagnostic slice only):** warn loudly when remaining notions
  fall toward the end of the pipeline, with the threshold derived from
  authored values (14-day transit → the external 30 must be ordered by
  ~day 16 to land by day 30). The standing supply term itself stays blocked
  on the owner decision; this slice only makes the countdown visible before
  it bites.

### 9. DEF-034 — Millbrook wheat/sugar/yeast stocked but unorderable; "18 bakery units" orphan (Low, CONFIRMED)
- **Evidence line:** wheat/sugar/yeast rows have `OrderPriceCents: 0`, no
  order unit, no conversion; flour's `BakeryUnitsPerOrderUnit = 18` is
  consumed nowhere as a quantity (code comments already state this).
- **Package:** documentation/cleanup only — annotate the asset rows as
  stocked-but-unorderable by design (or leave the code comments as the
  record). Authoring order packages for wheat/sugar/yeast would be a new
  economic decision — do not invent. Low value; do it only as filler.

### 10. DEF-052 — Post-acquisition opening cash rewritten by fiat (Medium, CONFIRMED)
- **Evidence line:** `ApplyPostAcquisitionOpeningCash` calls
  `SetCurrentCashCents(openingCash, false)` — `recordNetDelta: false`, so
  not even the net counters move; the seller's balance is erased, not
  transferred.
- **Package (diagnostic slice only):** log the destroyed/created delta loudly
  when the rewrite runs, naming the previous balance, the computed opening
  cash, and the difference. The real fix is blocked on owner decision
  Check 6 Q6 (does the seller's cash transfer, and to which ledger).

## Tier 3 — Blocked on owner decision (no mechanical work)

| Entry | Blocker (owner question) |
|---|---|
| DEF-001 — Bakery fuel cannot be replenished (Critical) | D-2 conversion factor (standing instruction: keep open, do not invent); also D-F1/D-F3/D-F4. Failure is already loud, not silent. |
| DEF-002 — Notions hard-stop, real fix (Critical) | Standing wholesale supply term: supplier, price, lead time, quantity. (Diagnostic slice is Tier 2 #8.) |
| DEF-003 — Fuel Dealer cannot sell fuel wood (Critical) | D-F1 household factor + D-F3 sawmill byproduct price + whether the dev scenario forms a Sawmill. |
| DEF-004 — Construction lumber, no replenishment path (High) | Sawmill/Yard/timber stands in the dev scenario, or off-map lumber supply instead; plus D1/D2 freight. (DEF-044 layer 2 fixed the mechanical half in package 6.) |
| DEF-069 — Forfeited earnest credited to nobody (Medium) | Check 6 Q4: forfeit recipient (seller / town treasury / lost) + whether earnest is held in a real escrow record. |
| DEF-070 — Delayed freight holds wagon/driver/animals (Medium) | Check 6 Q2 / O-2: abandonment disposition — cargo to supplier / buyer / carrier / write-off, and who bears the loss. (Delayed is currently unreachable in production — `DelayShipment` has zero production callers — so this is a disposition owed before Delayed can ever be entered safely.) |
| DEF-071 — Freight agreements/standing orders cannot be cancelled (Medium) | Check 10 Q7: who may cancel what, and what happens to the linked shipment, cargo, and money. `CancelAgreement` sets status strings only and touches no linked state. |
| DEF-057 — Baking requires no worker (Medium) | Check 7 Q5: should baking require a named baker present, and what happens when the baker is absent or dead? |
| DEF-058 — Freight needs exactly one person, Ethan (Medium) | Check 7 Q6: backup operator for freight, or does the haul stall indefinitely on a single operator? Depends on DEF-006 (hiring). |
| DEF-059 — Support-node lumber sale books no seller revenue (Medium) | Check 8 Q9: should the support node be a real seller, or is the player paying into the void by design? |
| DEF-060 — NPC entrepreneurship can never form a typed Sawmill/Yard/Freight Co. (Medium) | Check 8 Q12: should NPC entrepreneurship form typed businesses, or is that player/config-only? (The adapter CAN type from first activity; the driver only ever emits `BusinessType.Generic`.) |

## Tier 4 — Blocked on v3.2

| Entry | Blocker |
|---|---|
| DEF-054 — Four-facts freight model not implemented as data (Medium) | The Nov 13 four-facts design decision (title, custody, liability, payment separate) is NOT in Documentation v3.0 — pending v3.2 reconciliation. Adding title/liability fields is implementation of that decision; the liability assignment itself needs the DEF-070 owner decision. No code until v3.2 is read. |

## Tier 5 — Characterized / complete (nothing further to code)

| Entry | State |
|---|---|
| DEF-005 — Retail bread buyer (High, CONFIRMED) | Package 8: wired end to end at source level, NOT a mechanical break. What remains is a proof gap, not a code gap — no live run has yet shown a loaf bought. Proof instrument: the Codex ~30-day simulation test. E checkpoint 15 agrees (stay CONFIRMED, reframed). |
| DEF-006 — Autonomous hiring (High, CONFIRMED) | Packages 1 + 9: HireFromInquiry half wired; MakeOffer half + candidate-response driver never authored. What remains is the driver + input mapping — owner questions (17), (18), (19). No further code without those answers. |
| DEF-051 — Delegation/manager apparatus inert (High, CONFIRMED) | Package 7: uninvoked apparatus, NOT a mechanical break. What remains is the grant bridge — owner questions (14), (15), (16). No further code without those answers. |
| DEF-035 — Railhead flour import path dead in production (Low, CONFIRMED) | Recorded as dead (comparison-only, `ImportOrderPathExists = false` hardcoded). Wiring it live would be a new supply decision — do not invent. Nothing further to code. |

## B-lane queue summary (25 open entries)

- **Tier 1 — mechanical, authorized:** 6 (DEF-018, DEF-053, DEF-055, DEF-037, DEF-036, DEF-019)
- **Tier 2 — verify-then-document / diagnostic slices:** 4 (DEF-056, DEF-002 slice, DEF-034, DEF-052 slice)
- **Tier 3 — blocked on owner decision:** 11 (DEF-001, DEF-002 real fix, DEF-003, DEF-004, DEF-069, DEF-070, DEF-071, DEF-057, DEF-058, DEF-059, DEF-060)
- **Tier 4 — blocked on v3.2:** 1 (DEF-054)
- **Tier 5 — characterized / complete:** 4 (DEF-005, DEF-006, DEF-051, DEF-035)
- **Total:** 6 + 4 + 11 + 1 + 4 = 26 item-rows (DEF-002 appears in both Tier 2
  as a diagnostic slice and Tier 3 as the real fix — one entry, two layers).

## Open owner questions (precise wording, carried)

**(1)** Player-owned vacant required slot — hire, and at what weekly wage?
**(2)** Slot with no authored wage — what weekly wage? (Path disabled-by-config
until answered.)
**(3)** Casual conversion/notice terms for SeasonalOrCasual hires (notice days
NOT ADDRESSED in v3.0).
**(4)** MakeOffer acceptance rule — REFRAMED: the rule exists
(`PersonJobDecisions.EvaluateOffer`, deterministic, tested); what is missing
is the driver + input mapping — see (17), (18), (19).
**(5)** Bakery fuel D-2/D-F1..D-F4 — still open, do not invent.
**(6)** Notions standing wholesale supply term — bakery hard-stops after 60
batches (~day 60); fuel fails first (~day 40).
**(7)** Retail bread purchasing — REFRAMED: buyer verified wired at source
level (package 8), but no live run has shown a loaf bought; still needed:
live-run proof from the Codex ~30-day simulation test, dev-scene bread price
confirmation, whether bread is meant to be the staple alongside flour.
**(8)** Opening date five-way split / freight rate card authorization /
opening-economy provenance vs Canon §15.2.
**(9)** C1 design call — warn/escalate when a queued task's reason is L8 past
EscalationDays, or accept silent stalling?
**(10)** QA-15/OD-2 shopper policy — highest-value, unblocks QA-24.
**(11)** Staffing model when no clerk exists — who serves, or do sales settle
unserved?
**(12)** Sawmill formation — form Sawmill/LumberYard in the dev scenario, or
off-map supply instead?
**(13)** Sawing gate = blacksmith? — is the 1-unit/week hardware maintenance
requirement the intended choke point, or should remote production have its own
maintenance source?
**(14)** Grant bridge — should ManagerRun ControlState automatically carry a
delegation grant, and if so who is the delegate, with what scopes, purchase
limit, skill gate?
**(15)** Grant issuance — who grants authority, to whom, when; who authors
standing instructions, in which mode, when?
**(16)** Hire act kind — should ManagerBrain gain a Hire act kind, and if so
who decides acceptance on the candidate's behalf, by what rule? (Ties to
(17) and (19).)
**(17)** Consent boundary (proposal 107) — "When may an owner assign a worker
without negotiation, and when is candidate consent required? Proposal 107
(owner assignment vs worker consent) is pending — what is the boundary, and
does the HireFromInquiry path (consent implied by application) stay the model
for applicant hires while negotiated offers cover everyone else?"
**(18)** Candidate-response driver inputs + unknown-reliability trap — "What
supplies CommuteMinutesOneWay, ConditionsScore01, and
EmployerReliability01 for a candidate-response driver — and what should the
driver do when employer reliability is unknown? The evaluator Delays on
unknown reliability, so a driver that defaults reliability to 0 would Delay
every offer until the 14-day expiry. What is the intended fallback — a
default reliability, a reputation source, or a different rule?"
**(19)** Counter handling — "When a candidate counters, who on the employer
side accepts the revised terms, and by what rule — and does the employer get
a daily chance to answer before the 14-day expiry? (AcceptCounterOffer exists
and works when driven; nobody calls it.)"
**(20)** DirectApproach 'wants better terms' (20%) — "When a candidate
approached directly wants better terms first, should that open a negotiation
thread (an offer, a counter, a follow-up), or is dropping the thread the
intended outcome?"

## Notes for the coordinator

- Package 9's report says "Ready for E checkpoint 15" — that is a numbering
  slip in the package-9 handoff; E checkpoint 15 was for package 8 (DEF-005).
  Package 9 (DEF-006, `cc19849d`) needs its own E checkpoint (next number
  17), and this package (documentation-only, no production or test files
  changed) is at the coordinator's discretion — no E check is strictly
  needed for a doc-only commit, but the per-commit protocol has covered
  every B commit so far.
- The register was NOT edited (coordinator-owned). No status changes are
  recommended in this package: DEF-018/DEF-053/DEF-055/DEF-037/DEF-036/
  DEF-019 stay CONFIRMED pending their packages; DEF-002 stays AWAITING
  OWNER DECISION (the diagnostic slice does not change its status).
- Documentation blocker still in force: Documentation v3.2 has not been
  provided; all work above is mechanical or characterization under v3.0, with
  no supplier terms, prices, wages, conversion factors, or policies invented.
