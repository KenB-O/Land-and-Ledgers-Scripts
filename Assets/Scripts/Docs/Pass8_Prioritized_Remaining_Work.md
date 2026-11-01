# Pass VIII Prioritized Remaining Work

Ranked backlog as of the end of Muse Pass VIII. Stable IDs preserved: RUN-001..012, LL-PLAY-001..019, PASS7-001..008 all survive in `Docs/Pass6_Deferred_Development_Master_Register.md`; new Pass VIII items use PASS8-xxx IDs. This document ranks; the register records.

## P1 — Do first

- **PASS8-001 — Wire the detailed mine ore chain.** `MineProductionCycle.RecordBreakOreShift` and the assay/shipment/settlement chain are built and tested but have zero production callers; the abstract weekly path is live. Build the weekly driver converting worked break-ore minutes into the detailed chain and retire or reconcile the abstract path so the two cannot double-produce. Large.
- **PASS8-002 — Production freight tick drive.** The revenue collection scan exists and is tested, but `FreightCompanyRuntime` has zero production instances; no driver ticks freight companies in live play. Wiring a drive means designing freight dispatch production ownership (currently scene-side). Medium-large. Unity-adjacent.
- **PASS8-003 — Production appraisal source for asset sales.** The NPC owner-resolution policy is proven, but the live appraisal seam is null, so all production sale requests are conservatively denied. A real appraisal authority (or documented policy valuations) would activate the approve path. Medium. May need a Canon-adjacent policy decision.

## P2 — High value

- **PASS8-004 — Childcare, caregiving, recovery person-time.** Dependents bind no one's schedule. Add domestic task definitions driven by household composition and active illness, append `PersonActivityKind` values, and schedule through the existing domestic driver. All seams exist. Medium.
- **PASS8-005 — Wire individual detailed business runtimes.** Twelve runtimes are registered-unwired (`Docs/UnwiredSystems.md`). Each wiring is a separate scoped item: choose the runtime, drive it from the production tick, retire the generic path for that type without double-simulation, prove with tests. Large per runtime; start with one (hotel vs the live boarding house is the cleanest comparison).
- **PASS8-006 — Migration driver and journey integration.** `LaborMigrationLinker` evaluates but moves nobody; no integrator maps intents to journeys. Drive evaluation for seeking persons and map accepted intents to `OffMapJourneyAuthority` journeys with real travel time and cost. Medium.
- **PASS8-007 — Remaining social-knowledge chains.** Tenant hearing a full boarding house, farmer equipment sharing, wage-driven migration: each needs its real-state authority and decision consumer on the shared evaluator pattern. Small-medium each.
- **PASS8-008 — Service profile calibration.** The demand gate works; visit caps and price calibration per service type are untuned. Small. Needs live-play observation.
- **PASS8-009 — Unbounded schedule/packet list remediation.** Structurally identified (linear scans on daily hot paths, monotonic growth); needs campaign-scale measurement before tuning, then pruning or indexing by person and day. Small-medium. Must preserve truth: no consumer may lose history it reads.
- **PASS8-010 — Player construction input-support harness coverage.** The only tests need `UnityEditor`; extract the availability rules into a Unity-free seam. Medium.

## P3 — Worth doing

- **PASS8-011 — Player UI for pending owner requests.** NPC policy resolves NPC businesses; player-owned requests wait for a decision UI. Unity work.
- **PASS8-012 — Full foreclosure-auction sale of seized collateral.** Enforcement today is satisfaction-in-kind at appraised value; auction with bidders and title transfer is the fuller mechanic. Needs legal-rule design.
- **PASS8-013 — Household and personal lending through the bank.** Business loans are served; household borrowing stays with existing directories. Feasible later addition.
- **PASS8-014 — Due-event scheduler hub-level clear.** Stale events for purged persons are consumed harmlessly today, but a hub-level new-game clear would be cleaner than the reset-port call. Small.
- **PASS8-015 — Remove vestigial seams** (`ManagerExecutionServices.FindAsset`, dead compat paths). Trivial hygiene.
- **PASS8-016 — Sanitation and water.** Both deferred on Canon policy with unblock criteria documented (`Docs/Pass8_Water_Verdict.md`). Do not implement until the policy decisions land.

## Explicitly not planned

Deletions of registered-unwired systems (registry only, by coordinator decision). Rewiring the shared weekly mine path without scene and Canon sign-off. Any manufactured-revenue mechanism. Any second cash writer or debt authority. Player-facing UI generally (Codex domain).

## Suggested Pass IX review topics

The independent review should verify: that the new lifecycle driver actually closes businesses in long scenarios; that freight/postal/rental revenue flows in live play once drives exist; that the service demand gate behaves under real household spending; that no new unwired systems landed without registry entries; circulation under longer adverse runs; the G-14 performance question with measurements; and whether the P1 items above are still the right P1s.
