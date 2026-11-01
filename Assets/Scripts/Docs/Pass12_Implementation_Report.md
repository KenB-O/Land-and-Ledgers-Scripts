# Pass XII — Implementation Report (coordinator summary)

Worker reports and the independent review are the detailed records; this
document is the coordinator's verified summary. All counts below are Roslyn
harness counts on the stated tree, clean build (0 errors), unless noted.

## Branch progression

- Start: `02aca56f177c6334e6a84568e6f1cb8e1bb495d2` (Pass XI), 1404/3.
- A merged: `70c57ae` — bootstrap guards, ghost-ownership suppression,
  restore-activation idempotence, copy-on-load (6 load methods).
- C merged: `79c9341` — exactly-once wages, settled-sale refusal, report
  aggregation, workload staffing.
- B merged: `df7b078` — business matrix + Bakery/smithy proofs (no production
  changes, deliberately).
- D merged: `a04ed1f` — player-experience read models + contracts.
- E merged: `eafe29b` — settlement expansion tests only. Integrated: 1511/3.
- F merged: final HEAD (see `Docs/Pass12_Final_Source_Handoff.md`).
  Final harness on F's code tree: **1533 passed, 0 failed** (coordinator
  re-verification on the exact final tree is in
  `Docs/Pass12_Final_Regression_Results.md`).

## What each worker actually delivered

### A — Bootstrap and production (28 new tests)
- LL-PLAY-001 M1 fixed: the bootstrapper now unconditionally suppresses the
  store runtime's auto-create-player-business flag, covering NewGame/Manual/
  Continue (execution order 240 < 260 protects the with-director case).
- Single-instance: `SimulationDrivers` claims a `SingleInstanceAuthorityGuard`
  in Awake (loud refusal on a second claim); the bootstrapper runs a duplicate
  census (SystemsHub, Drivers, CivicFoundation, ScenarioDirector, Bootstrapper)
  on Start and after restore, logging duplicates. Hub/Civic/Director duplicates
  are detected, not suppressed — scene hygiene remains Codex work.
- Restore-activation duplication fixed: `startingActivationIssued` on the
  scenario DTO suppresses re-running household/task activation on load
  (old saves re-issue once on first Pass XII load, then stay true).
- `SimulationDrivers.Rebind` gained its first real production caller
  (`MarkRestoredFromSave`, post-restore rebind).
- `ScenarioBootstrapFactory.TryCreateStart` honestly left unwired (no genuine
  trigger exists; the start panel uses `SwitchScenario`).
- PASS11-004 left unwired with its three gaps documented (parcel mapping,
  boarding trigger rule, building identity) — no data invented.
- LL-PLAY-019: `LLMainMusicPlayer` contract verified correct
  (duplicate self-destroys, `DontDestroyOnLoad` when persistent); disappearance
  from normal Edit Mode roots in Play Mode is expected, not a defect.
- E-DEFECT-02 fixed: copy-on-import at six load methods; `ImportState` moved
  to replace (clear-then-load per household) semantics, fixing a live
  double-load hazard in the SaveLoadManager path.

### B — Businesses and tasks (4 new tests, no production changes)
- Matrix: 29 `BusinessType` members (28 typed + Generic), **19 LIVE / 10 PARTIAL**
  (corrected from 18/10 in review round 2 — Mine is LIVE via its driver).
  Activation list: exactly 17 labels behind only four live mechanisms
  (generic method path, shared archetype path, dedicated drivers,
  settlement/payroll only). Creation panel: Generic + 3 templates.
- All 13 registry runtimes + staged chains (Bakery, Forge) gated, not wired:
  wiring them would double-produce against the live generic/archetype paths.
  Safe wiring needs a per-business path flag retiring the generic operation,
  a driver caller, a DTO save seam, and outlet retirement — none exists.
- Bakery proof through the live generic path: 1 flour + 1 fuel → exactly 1 loaf
  over 3 driver days, baker released during the passive bake, mid-bake restore
  to exactly 1 loaf, oven reservation blocking a second loaf, start refused
  without iron (smithy). Farm, sawmill, wagon loading, General Store
  receiving/sales verified live through existing coverage.
- Equipment: existing models already cover exclusive/container/workstation/
  throughput/passive/transport envelopes, but generic production consults only
  its own per-id reservation, not `EquipmentCapacityService` (mass/volume/wear
  not checked on the generic path; one id = effectively capacity 1).
  Recommendation (not implemented): keep `GenericConfiguration` as reservation
  truth, consult capacity descriptors as advisors, author one id per
  independently reservable unit.
- Tasks terminology corrected: `TaskInstance`/`ActivityInstance`/generic
  `WorkOrder` do not exist in production; real composition is
  `TaskDefinition` → `WorkTask` via `TaskAuthority`, generic phases via
  `GenericProductionTaskBridge`, farm from completed field tasks, construction
  via dependency-ordered `WorkPackage`s under `NpcConstructionDriver`.
- LL-PLAY-010: six-stage construction/formation contract (title/parcel →
  design catalog → construction packages → premises acquisition → formation →
  equipment/operation), including the equipment-installation gate the generic
  path does not surface.

### C — Economic integrity (33 new tests)
- E-DEFECT-01 confirmed and fixed at the authority: `ResolveSettledSale` now
  refuses (no mutation) when stock < units. Both production callers already
  pre-check stock, so their behavior is unchanged; the hole is closed for any
  other/future caller. Partial delivery is not offered — a caller wanting less
  must request less.
- PASS11-003: manual `PayAgreedWage` after lookup recovery could double-pay an
  accrued liability — now mechanically blocked (preserved-liability guard,
  manual-pay dedup key, weekly netting of manual cash, persistence).
- PASS11-007: legacy-slot throwing lookup now accrues as a named operating
  liability, mirroring the employment path (settlement still requires real cash).
- PASS11-005: no legitimate distress-sale buyer source exists (liquidation port,
  HousingSearch, tax/foreclosure bid strings, and the phantom-label
  `TrySellPlayerBusiness` path all re-confirmed unsuitable). Honest refusal kept.
- LL-PLAY-013: report aggregation fixed (period containment, chronological
  sorting, business+week-span dedup, partial-overlap flags, rent as operating
  expense, tax as cash expense outside operating profit, cash bridge with
  unknown-inflow term, Yearly/AllTime rollups from monthly summaries only,
  liability as ending-state stock never summed). The Today/Week/Month/Year/
  All-Time period selector UI does not exist in source — Codex work.
- LL-PLAY-012: workload-derived staffing read model delivered (demand from open
  task minutes, assigned/unassigned, coverage, unfilled shifts, per-worker
  utilization, wage obligations split current vs arrears, skill evidence
  honestly marked unknown). Quota causal gates (`TargetWorkerCount`,
  80/20 efficiency, opening gate, hiring cap) deliberately NOT rewritten —
  they are gameplay gates needing `LaborDemandPlan` wiring (B/coordinator).
- Banking: deposits are true liabilities, FOA is the single debt authority,
  no live double-count; capacity is fragmented across three caps and
  related-party is a flag without consequence — owner-policy territory, not fixed.
  The player-bank binding step for Codex is exactly one `BindBankLedger` call
  per bank, never both ledgers.

### D — Player experience (22 new tests)
- Scenario HUD contract: `ScenarioHudReadModel` + `ScenarioHudSnapshotBuilder`
  from authoritative director state (identity, objectives, deadlines, progress,
  completion/failure, failure notices, clean Sandbox). A-14 unchanged; expired
  objectives stay failed; latched goals stay complete. The live scene panel is
  NOT wired (PASS11-006 remains scene work); the builder itself has zero
  production callers, so the HUD chain is unwired end-to-end.
- Guidance gate: First Ledger guidance only for empty/legacy context or
  `the-first-ledger`; suppressed for any other active scenario. Step text
  unchanged (it is legitimate inside the First Ledger).
- Acquisition: one `NextLegitimateAction` mirroring manager labels exactly,
  five real deal stages kept distinct; provenance known for PublicListing,
  RumorLead→Conversation, EstateSale — QuietOpportunity, DistressSale,
  StrategicHoldout, Unset are flagged Unknown verbatim, never upgraded.
- Panel modal contract: one modal owner, second owner refused, only the active
  owner closes, focus returns, double-close safe. Management-panel layout is
  visual-only (components identified, no visual fix claimed).
- Civic: title drops "Not Built"; numbers still from `CivicFoundationManager`;
  no institution invented. Resources: strength labeled "generated
  prospectivity", never tonnes/reserves; mineral rights always "not
  established". Startup notices: precedence ghost → debug → premature-init →
  tutorial remnant → show; legitimate blockers stay visible.
- Causal explanations: every line cites one real event; verdict is a pure count
  summary with its basis shown. Person inspection: per-field `ObserverVisibility`
  (identity for all; household/work/finances/history self+household only;
  activity co-located/public; business financials excluded).
- Caveat: `FirstSessionGuidanceManager.cs` and `GeneralStorePanelController.cs`
  are outside the harness subset — those two edits are inspection-only;
  Unity compilation is a Codex gate.

### E — Simulation testing (20 new tests, zero production changes)
- WS4 30-day proof preserved exactly (cash 2647500 + 4100 − 2275 = 2649325;
  grain 720, flour 270, bread 32, ore 0, in-transit 0; bread-120 objective FAILED
  at day 25 and stayed failed).
- Small fixture (3 households / 2 businesses): cash 11500 → 11500 exactly;
  genuine lack of demand + unemployment (a household starts and ends at 0c,
  buys nothing). Large fixture (9 households / 7 businesses): cash
  97500 → 97500 exactly; supplier ordering, farm→mill freight (60 grain, once),
  service payments exercised; both fixtures determinism-twin hash-identical.
- 90-day large-town restore-at-45 hash-identical to uninterrupted.
- Adversarial: closure, supplier shortage, credit default, empty bank
  liquidity, worker/manager absence, construction interruption, expired
  objectives, pending cargo on restore, double restore (independent snapshots),
  compatible equipment reservations, single-instance bootstrap, lack of demand
  — all pass with conservation asserted.
- Performance (measurement only): large-town 30-day 31 ms (first run),
  reporting 1000× + hash 44 ms, WS4 save/load at day 15 63 ms, scenario
  feedback 1000× 21 ms, warmed 90-day 2 ms. No optimization attempted.
- E's large-town fixture note (review round 2): it adds 40 grain/week from
  "unmodeled land" — a disclosed stand-in for farm production, not a
  stabilizer — so any "no injected goods" claim is literally inaccurate for
  that fixture; goods conservation is asserted only for small-town bread,
  cash conservation holds in both.

### F — Independent review and cleanup (19 new tests + 3 corrections)
See `Docs/Pass12_Independent_Review.md` for the full record. Headlines:
RUN-011 all three test-wrong (production untouched); copy-on-load sweep
(9 areas round 1 + 5 areas round 2); 3 registry runtimes added (16 total);
default ownership Town + store flag false; ASD accounting fix; Failed stage
rows fix; matrix correction 19/10. One own-regression round (F-05) caught by
the full run and fixed before final.

## Test arithmetic (verified at each step)

1404 (Pass XI) + A28 + C33 + B4 + D22 + E20 = 1511 (integrated, 3 failed)
→ + F19 new tests = 1530, + 3 RUN-011 failures turned to passes = **1533/0**
on F's code tree. No test deleted, no assertion weakened.
