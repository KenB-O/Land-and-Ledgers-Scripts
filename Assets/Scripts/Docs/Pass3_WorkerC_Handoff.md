# Pass III Worker C Handoff — Household Decisions, Livelihoods, Domestic Labor

Branch: `muse/p3c-household-livelihoods` · Baseline: `aa59bf7` (Pass III foundation)
Date: 2026-10-11 · Scope: brief §§9–10, 12–13 (Worker C per Pass3_FoundationContracts.md)

## What was built

| # | Commit | Contents |
|---|---|---|
| 1 | `870ce8e` | `HouseholdDecisionService` — pure pressure→decision evaluator implementing the foundation `IHouseholdDecisionService`; additive contract fields on `HouseholdPressureInput` |
| 2 | `c92a08b` | `HouseholdDecisionDriver` + `HouseholdOpportunityLog` — decisions become real operations |
| 3 | `967cc7a` | `HouseholdAutonomyDriver` — NPC §13 autonomous household responses |
| 4 | `c959c7e` | `DomesticTaskCatalog` + `DomesticLaborService` — narrow domestic labor |
| 5 | `bdd8760` | `LivelihoodReadModelBuilder` — source-backed read model with observer visibility |
| 6 | (this) | Hub + save-DTO registration |

All new files carry fresh-GUID `.meta` files. Pass II labor files untouched
(`ContactGraphService`, `PersonJobDecisions`, `EmploymentNegotiationService`
reused as-is). No Unity scenes/prefabs touched.

## Authority reuse (nothing duplicated)

- Cash: `HouseholdLedger` (single writer). Debt: `FinancialObligationAuthority`.
- Person-time: `PersonScheduleTracker` + `WorkTimeBudgetStore` (exclusivity enforced by the tracker).
- Tasks: `TaskAuthority` + estimator seam (no second task authority).
- Employment: `EmploymentRelationshipRegistry` (sole wage authority untouched).
- Recruitment: `ContactGraphService` one-hop documented links (no omniscient lists).
- Tenancy terms: Worker B's `TenancyPaymentTerms`/`RentObligationState` (consumed via provider; no second rent ledger).
- Housing search: `HousingSearchNeedRegistry` (hub-owned instance added).
- Family labor: `FamilyLaborLedger` with `agreedWageCents: 0` (PL-04).

## Codex / integrator wiring still needed

1. **Daily driver**: call `HouseholdAutonomyDriver.EvaluateHouseholdDay(householdId, dayIndex, ctx, diag)`
   for each household once per simulated day. The integrator must supply the
   `AutonomyContext`: `RentObligationsFor` (Worker B's `RentObligationState`
   list per household), `ShelterRiskFor`, `CurrentCondition`, and
   `ExecutionParams` (agreement/landlord/requested-change for NegotiateTerms).
   `Memberships` comes from the existing membership authority.
2. **Spending policy consumption**: `HouseholdDecisionDriver.GetSpendingPolicy(householdId)`
   returns the standing `HouseholdSpendingPolicy` (discretionary cap %). The
   `HouseholdShoppingLoop` does not read it yet — wire the cap into shopping
   decisions or leave the policy as a recorded instruction.
3. **Borrow cash flow**: `ExecuteBorrow` records the liability via
   `FinancialObligationAuthority`. The lender's actual cash payment must be
   recorded by the lender's own action through `HouseholdLedger` — never
   synthesized by this driver.
4. **Negotiation responses**: `TenancyNegotiationRequest` records sit `Pending`;
   landlord accept/refuse is a separate legitimate action (Worker B / NPC side).
5. **Capability summary**: `LivelihoodReadModelBuilder.CapabilitySummaryProvider`
   is an optional `Func<int,string>` — Worker D's capability stores plug in here.
6. **Domestic catalog registration**: call
   `DomesticTaskCatalog.RegisterAll(tasks, diagnostics)` wherever
   `SimulationSystemsHub.RegisterTaskCatalogs` runs (or add it there).
7. **Save/load**: four new sections in `SystemsSaveDtos`
   (`householdDecisions`, `householdDecisionDriver`, `domesticLabor`,
   `householdAutonomy`) are captured/restored by the hub with clear-then-load
   semantics. Old saves load with empty sections — no migration needed.

## Tests

Roslyn harness `roslyn-harness/harness-p3c.csproj` (new; compiles the
`repo-p3c` worktree subset + shims): **397 passed, 1 failed**.
The 1 failure is the pre-existing `MineLaborRegisterTests` failure documented
at the baseline (unrelated, untouched). 40 new Worker C tests, all green:
decision evaluation (13), decision execution (10), autonomy (6), domestic
labor (7), read model (4). Unity Test Runner NOT run (no Unity here — not claimed).

## Genuine forks / judgment calls (recorded, not guessed)

- **SeekIncome with no known openings**: a shortfall household with documented
  contacts but no known openings still chooses SeekIncome — the *search* is the
  legitimate action (brief §13). Households with no contacts, no credit, and no
  lender economize instead.
- **Dependents** = members without active employment (documented simplification;
  refine with ages when PersonState age data is plumbed through).
- **Borrow principal** = obligations − 30-day coverage; terms are intentionally
  sparse (lender, day, purpose) — full credit terms belong to the lending flow.
- **Domestic skill ids** are literal strings matching `SkillIds` values (avoids
  dragging `SkillService` into the catalog); Worker D's experience formation
  routes on the learning tags.
