# Audited precision supplement — Technical Implementation Bible

**Edition:** 2026-10-08 second-pass source comparison; provisional source-recovery addendum
**Authority:** This supplement clarifies requirements already present in the frozen original Technical XIV; it does not supersede those originals and does not promote the separately pending 106–145 proposals.
**Evidence:** Source PDF pages are 1-indexed physical PDF pages; each anchor was matched against the named source PDF.

## TECH-RET-001 — Economic route distance remains 1:1

Keep authoritative geographic positions, path distances and route-time economics consistent across loaded scenes, floating-origin offsets and coarsened simulation; streaming must not shrink travel cost or make an off-map journey artificially short.

**Source:** Technical Implementation Bible XIV, PDF page 78; locate phrase: “1:1 economic distance”.
**Existing draft section(s):** `TECH-CE1-T026`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-002 — Generate world causally

Prefer initialization dependency order geography/rights -> rural farms/households/output -> route access -> settlement catchment -> institutions/transients -> demand -> businesses/capabilities -> staffing; avoid generating business rosters and then inventing demand to justify them.

**Source:** Technical Implementation Bible XIV, PDF page 44; locate phrase: “causal generation order”.
**Existing draft section(s):** `TECH-CE1-T024`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-003 — Use correlated livestock exposure

Animal health and mortality incidents can affect multiple animals through shared exposure to location, herd, feed, weather or disease; avoid independent identical per-animal dice rolls for a common outbreak.

**Source:** Technical Implementation Bible XIV, PDF page 76; locate phrase: “Weather and disease events must”.
**Existing draft section(s):** `TECH-CE1-T020`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-004 — Health episodes preserve longitudinal state

Persist onset, symptoms/known diagnoses, underlying disease or injury where modeled, severity, contagion as appropriate, work-availability effects, intervention, follow-up, recovery/impairment/death outcomes and evidence links rather than a transient absence modifier.

**Source:** Technical Implementation Bible XIV, PDF page 15; locate phrase: “HealthEpisode / InjuryEvent”.
**Existing draft section(s):** `TECH-CE1-T020`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-005 — Claims and medicine outcomes stay distinct

For medicines, retain manufacturer, formulation, advertised claims, actual modeled effect/risk, legal authority, lot state and supply history; marketing text does not mechanically change treatment efficacy.

**Source:** Technical Implementation Bible XIV, PDF page 15; locate phrase: “Advertising claims cannot directly set treatment effectiveness”.
**Existing draft section(s):** `TECH-CE1-T007 / T022`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-006 — Bank policy must be real component decisions

BankPolicyProfile must cover liquidity buffers, loan pricing and eligibility, collateral/appraisals, concentration and connected borrowers, maturities, collections/enforcement, dividends, deposit terms, approval authority and reconciliation controls rather than a global Risk setting.

**Source:** Technical Implementation Bible XIV, PDF page 14; locate phrase: “target liquidity and correspondent balance”.
**Existing draft section(s):** `TECH-CE1-T030`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-007 — Bank panic processes depositor claims

Test a panic by submitting withdrawals on actual DepositAccounts and settling available bank cash, correspondent funds, liquidation, outside borrowing and dated suspension rules; Reputation cannot discharge cash liabilities.

**Source:** Technical Implementation Bible XIV, PDF page 14; locate phrase: “panic fixture should create actual withdrawal requests”.
**Existing draft section(s):** `TECH-CE1-T030`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-008 — Public-land entry is a persistent case

Persist claimant, tract, law/program, evidence of residence and improvements, filing deadlines, contests, proof, fees and patent/forfeiture separately from fee-simple ownership; do not infer patent from a submitted application.

**Source:** Technical Implementation Bible XIV, PDF page 24; locate phrase: “LandEntryCase”.
**Existing draft section(s):** `TECH-CE1-T015`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-009 — Farm tenure is its own relationship

Represent cash tenancy, share tenancy, owner-operation and partial ownership using distinct property and farming relationships with real crops/shares and agreements; tenant production does not itself post wages.

**Source:** Technical Implementation Bible XIV, PDF page 24; locate phrase: “FarmTenureRelationship”.
**Existing draft section(s):** `TECH-CE1-T015 / T019`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-010 — Historical observations capture promotional bias

Source-qualified historical observations need date, location, citation, type, confidence and promotion/selection bias metadata before being used as simulation-generator input.

**Source:** Technical Implementation Bible XIV, PDF page 24; locate phrase: “source type and potential promotional bias”.
**Existing draft section(s):** `TECH-CE1-T036`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-011 — Credit lines are not installments

Persist CreditFacility with lender, limit, outstanding principal, undrawn capacity as derived, covenant, maturity, collateral and draw/repayment history; unused borrowing authority is neither cash nor existing debt.

**Source:** Technical Implementation Bible XIV, PDF page 119; locate phrase: “CreditFacility entity”.
**Existing draft section(s):** `TECH-CE1-T029`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-012 — Save schema migration is deliberate

Version every persistent schema and require deterministic migration for changed entities, references and relationships or explicitly declare incompatibility; invalid/orphaned links must not be repaired by assigning a random entity.

**Source:** Technical Implementation Bible XIV, PDF page 162; locate phrase: “Every save schema should carry a version”.
**Existing draft section(s):** `TECH-CE1-T035`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-013 — Autonomous scenario QA needs reproducibility

Support deterministic or well-seeded multi-month/year reduced-presentation playtests that export source authority, key event lineage, objective proofs, relationship consistency, debt and inventory reconciliation and save/load checkpoints.

**Source:** Technical Implementation Bible XIV, PDF page 108; locate phrase: “long-run scenario”.
**Existing draft section(s):** `TECH-CE1-T034 / T035`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-014 — Property listing discovery must have provenance

Property opportunities refer to actual parcel/property and owners or explicit unknown/disputed facts, discovery source, discovered date, asking terms and restrictions; market listings must not invent a fictitious seller or vacant title.

**Source:** Technical Implementation Bible XIV, PDF page 56; locate phrase: “Property market provenance”.
**Existing draft section(s):** `TECH-CE1-T015 / T022`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-015 — Contractor replacement preserves site history

After contractor failure, issue new Agreements/WorkPackages to replacement providers while preserving the original project baseline, performed work, damaged materials, site custody, unsettled liabilities and disputed claims.

**Source:** Technical Implementation Bible XIV, PDF page 40; locate phrase: “replacement actor”.
**Existing draft section(s):** `TECH-CE1-T017`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-016 — Segment read models cannot mutate ledger truth

Internal contribution analytics may allocate real wages, inventory, asset utilization and project burden with disclosed assumptions; segment reports cannot post extra consolidated revenue or owner equity.

**Source:** Technical Implementation Bible XIV, PDF page 106; locate phrase: “Segment reporting may explain economics”.
**Existing draft section(s):** `TECH-CE1-T009`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-017 — Blocked shipment fix remains P0 and untested

A blocked shipment must not hold a fabricated loaded copy of cargo while the origin stock is restored; test all failure paths and save/load persistence before declaring the bug repaired.

**Source:** Technical Implementation Bible XIV, PDF page 101; locate phrase: “CreateBlockedShipment”.
**Existing draft section(s):** `TECH-CE1-T010`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-018 — Partial projects need real cost capitalization

Internal construction may retain true labor/material/freight/specialist/allocated-overhead costs in the receiving asset basis; intercompany billing eliminates at consolidation and never creates manufactured profit.

**Source:** Technical Implementation Bible XIV, PDF page 13; locate phrase: “capitalized project basis”.
**Existing draft section(s):** `TECH-CE1-T017`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.

## TECH-RET-019 — Different kinds of events have distinct authorities

Wall-clock presentation, continuous simulation calendar, due-event scheduler and historical authoring triggers must not become independent time truths; transitions resolve idempotently after skip/load with original effective times.

**Source:** Technical Implementation Bible XIV, PDF page 73; locate phrase: “calendar is never a”.
**Existing draft section(s):** `TECH-CE1-T035`. **Disposition:** retain as explicit precision; do not claim implementation without repository evidence.
