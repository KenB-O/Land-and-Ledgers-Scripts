# Pass V — Tier One Implementation Plan

Date: 2026-10-12 · Baseline: `bd76bc98`. Companion: `Docs/Pass5_TierOne_Causality_Audit.md`.
Rule: fix P0 before speculative features. Small high-leverage connections over new
subsystems. Never create a second Person, age, scenario, money, ownership, or time
authority.

## P0 — Authority and identity (Worker 2)

P0-1 Gregorian calendar migration.
- `SimulationTimeSettings`: add explicit Gregorian anchor (absoluteDayIndex 0 maps to
  a real Gregorian date; default 1880-03-01). `BuildDateFromAbsoluteDay` becomes true
  Gregorian math: real month lengths, leap years (1880 leap, 1900 not, 1904 leap),
  weekday alignment to history. Remove the 336-day derivation.
- Find every consumer of `DaysPerMonth` (28), `weeksPerMonth` (4), and 336-day-year
  assumptions (payroll, rent, tax year, generation). Correct them; do not shim.
- Save compatibility: absoluteDayIndex stays the persisted truth; derived fields
  recompute. Additive migration only.
- Tests: weekday alignment vs known historical dates, leap boundaries, month lengths,
  Feb 29 handling.

P0-2 Birth-date and age authority.
- `PersonState`: add authoritative birth-date representation with provenance
  (exact / generated-exact / estimated / partial). No independently incremented age
  field survives as authority; `age` becomes a derived read (completed calendar
  years: current Gregorian date minus birth date). Remove or repurpose `AgePerson`.
- `RecordBirth` stamps the birth date. PopulationGenerator assigns generated-exact
  dates with scenario provenance. Existing save migration: assign estimated dates
  consistent with current `age` (documented as estimates, never presented as fact).
- Birthday scheduling on the hub `DueEventScheduler` with stable event ids:
  next-birthday events per person, idempotent across save/load, no double-aging,
  deceased persons excluded, off-map persons included.
- Age-band derivation from true age. Leap-day convention documented once and used
  everywhere (Feb 28 anniversary in non-leap years; legal vs social kept separate
  where relevant).

P0-3 Conservation re-verification.
- After P0-1/P0-2, re-run cash/goods/time/equipment reconciliation. Authority
  violations are release-critical (brief section 30).

## P1 — Broken causal connections

P1-1 Scenario preset architecture (Worker 1).
- Extend `ScenarioAsset`/definition with starting-world configuration: scenario id,
  definition version, historical start date, settlement setting, world-gen parameters,
  population constraints (ranges honoring the 55-95 Canon calibration), demographic
  calibration, existing persons/households/businesses, player start, ownership and
  property state, buildings/infrastructure, equipment/animals, inventory, employment,
  cash/deposits/credit, debts/agreements, migration relationships, external markets,
  historical-event settings, objectives/deadlines, authored overrides, controlled
  randomness, validation rules.
- `ScenarioPreset`: versioned, serializable (use existing project data conventions;
  ScriptableObject-friendly), load/validate/version/reuse/copy/derive, deterministic
  preview, diagnostics, backward compatibility.
- `ScenarioCoordinator`: ONE orchestrator for scenario identity, configuration,
  generation, initialization, validation, lifecycle. Commands existing services;
  stores no Person/Household/Business/money data itself.
- Controlled randomness: partitioned RNG streams per generation concern; same
  definition version + seed + start date + inputs = identical world; generation
  failure (constraint violation) retries within bounded policy or reports actionably
  (never invents money/houses/businesses to force validity).
- Lifecycle semantics: New Game (generate), Load Game (restore, no regeneration),
  Restart Scenario (documented same-seed/new-seed choice), Replay (fresh from
  preset), Continue-Failed (preserve), Edit Preset (future generations only, with
  migration workflow). Ghost-state prevention verified by test: no inherited
  ownership, memberships, employment, objectives, manager decisions, businesses,
  banking state, or stale reservations.

P1-2 Demographic runtime drivers (Worker 2).
- Birth scheduling: legitimate conceptions/births through lifecycle authority
  (no births to hit targets, no pregnancy from household composition alone).
- Mortality driver: deaths from supported health/demographic conditions wired to
  `RecordDeath` and `EstateService` (estate, dependents, property, succession).
  No debt-threshold suicide event.
- Marriage formation driver: real persons, real relationships, Canon-calibrated
  distributions (1890 medians m26/f22 as anchors, not triggers).
- Migration driver: employment, household formation, family, distress, opportunity,
  housing. Preserves identity, history, kinship, ownership, debts. Off-map persons
  keep aging.
- Household splitting: adult children leaving home are the same Person, not new ones.

P1-3 Generation order (Worker 2).
- Reorder `PopulationGenerator` toward Canon 12.6G: population envelope and real
  persons first, demographics, kinship and marital relationships, family histories,
  households FROM domestic relationships, accommodation, economic roles/property/
  assets/employment/obligations, validation. Create kinship edges. Keep the 55-95
  calibration untouched. Plausible age distributions (infants through older
  residents), temporally coherent parent/child/spouse ages.

P1-4 Production authority consolidation (Worker 3).
- Map every production caller of `SharedBusinessRuntimeManager` vs the Generic path.
  Route each business through ONE path. Resolve the duplicate inventory truths
  (BusinessRuntimeState category stock vs GenericConfiguration positions).
  Do not rewrite the monolith; connect and retire duplication.

P1-5 Starting economy coherence (Worker 1 + Worker 3).
- Generated worlds get coherent starting conditions: employment, cash/deposits,
  legitimate debts, property, equipment, inventory with provenance. Scenario-authored
  history distinguished from simulated post-start events.

## P2 — Cohesion deficiencies

P2-1 Household coordination (Worker 3).
- Batch errands across needs: one trip serving multiple needs where feasible; plans,
  reservations, and responsibility through existing authorities (no duplicate
  purchases). Household decides, a real available Person executes.
P2-2 Hardship and credit exhaustion (Worker 3).
- Verify the full chain: income loss, savings drawdown, reduced purchases, credit
  use and justified refusal (no money created), arrears, asset sale, cheaper
  accommodation, insecurity, migration. No hidden rescue.
P2-3 Entrepreneurship (Worker 3).
- Wire `NpcBusinessCreationAdapter` to autonomous opportunity evaluation under
  manager judgment and owner restrictions. Businesses succeed or fail on real
  conditions.
P2-4 Authority cleanups (Worker 4 or coordinator).
- Duplicate "what is person doing" (tracker vs scheduleState vs PlayerDirector).
- `MineLaborRegister` legacy second wage path. `PlayerDebtManager` dual-truth loan
  book. Person location fragmentation (unwired `WorkTask.locationId`).
- Only if low-risk; document the rest for Codex.

## P3 — Calibration and presentation

P3-1 Causal read models: age/birth-date provenance, scenario origin, household,
employment, obligations, demographic history (bounded, visibility-respecting).
P3-2 Scenario preset authoring/editor requirements for Codex (Unity-side).
P3-3 Historical research holds: legal majority, schooling, child labor, marriage
eligibility by jurisdiction/year. Explicit holds, never invented thresholds.

## Test plan (all workers)

- Slices A-F (original causal), G-J (additional), H, I, K-M (scenario/aging) per brief
  sections 37-39. Extended 30-day (bounded 90-day where feasible) autonomous
  society sim (section 40). Multi-year lifecycle (section 41).
- Observed/unobserved regression (section 25): same worker camera-on vs off,
  presentation toggling, streamed region save/load, wagon off-map return,
  high-speed, headless scheduling, birthday while unloaded, birth/death/split
  with no mesh.
- Tick-size consistency: small ticks, large ticks, 1x/10x/100x, save/load.
- Performance: birthday scheduling and birth-date queries at hundreds/thousands
  of persons; 30-day sim timing. Report actuals.

## Worker assignments

- Worker 1: P1-1 (scenario architecture) + P1-5 (starting economy) + slice L.
- Worker 2: P0-1 + P0-2 + P1-2 + P1-3 + slices K, M + P0-3 support.
- Worker 3: P1-4 + P2-1 + P2-2 + P2-3 + slices A-F, G, H, I.
- Worker 4: P2-4 + slice J + section 25 regression + 30/90-day sim + read models (P3-1) + multi-year lifecycle support.
- Coordinator: contracts, merges, full regression, handoff, final report.
