# Pass VI Multi-Use Asset Audit

This document audits assets that serve multiple competing uses, proving whether the simulation enforces genuine scarcity and opportunity cost. The flagship is the one-horse audit (brief section 10, scenarios A through E). The findings generalize to wagons, land, buildings, tools, workers, equipment, cash, and knowledge (brief section 11).

## 1. The one-horse audit (flagship)

### Setup
One horse, one stable identity, needed for competing uses: draft work (plowing), riding (mount), rental, sale, and collateral. The question: does the simulation enforce that one horse does one job at a time, with real condition costs and genuine opportunity cost?

### Scenario A: Draft versus mount on the same day
- Draft path: DraftPowerService.TryReserveAnimals (unified-authority reservation).
- Mount path: HorseAuthority.TryReserveUse (static service, CurrentUse).
- Verdict: DOUBLE-BOOKING CONFIRMED. The two mechanisms do not cross-check. A horse reserved for plowing can simultaneously be reserved for riding. One horse, two jobs, no refusal, no queue.
- Classification: BROKEN. P1.

### Scenario B: Condition and eligibility
- IsDraftCapable checks species and IsActive only. Condition is ignored.
- A lame horse is draft-eligible. Verdict: BROKEN. P1.

### Scenario C: Condition lifecycle
- HorseFoundation.ApplyCare has no production callers. The feed-loop horse path has no production callers. Only ApplyWork is called in production, with zero cost.
- Horses never tire, never need care, never degrade from work in production. Verdict: SOURCE-ONLY lifecycle. P1.

### Scenario D: Sale with live reservations
- HorseTrade.ExecuteSale does not release live reservations.
- A sold horse can remain reserved for work it will never do. Verdict: BROKEN. P2.

### Scenario E: Rental
- Horse rental has no production path at all. Verdict: ABSENT. P2.

### Additional findings
- Journey travel (TravelMode.Horseback) uses a speed constant with no animal binding. Phantom horses. P2.
- Worker time is double-bookable: the draft-driver hold is invisible to WorkScheduler. P2.
- Animals cannot be pledged as collateral. P3.
- Employment settles wages without verifying work was performed (as coded; design question). P2.

### Flagship verdict
The one-horse audit FAILS. The identity is stable but availability is not unified, condition is not modeled in production, and the economic consequences (rental income, sale clearing reservations, travel binding) are absent. The horse is the clearest case of the audit's central tension: a well-modeled asset with no enforced scarcity.

## 2. Generalization: multi-use assets

### Wagons
- Freight wagons accrue wear live (FreightResourcePool) and unusable wagons block work. RentalFleet provides the rental alternative. Wagon capacity is triple-recorded (legacy seam) but internally consistent.
- Verdict: PRODUCTION. The best-connected shared asset after equipment.

### Land
- Parcels are owned, taxed, and transferred through the property system. Agricultural use (CropChain) is source-only. Land as a multi-use input (farming versus grazing versus building) has no allocation mechanism.
- Verdict: PARTIAL.

### Buildings
- Buildings have occupancy (households, boarders, businesses) but no economic occupancy capacity. A building cannot be "full" in economic terms. NpcCommissionedBuild has no production callers.
- Verdict: PARTIAL. P3.

### Tools and equipment
- Wear accrues live; unusable equipment blocks real work; blacksmith repair is a real transaction; EquipmentReservationService provides hub-owned reservations with loud refusals.
- Verdict: PRODUCTION. The model for how shared assets should work.

### Workers (labor as an asset)
- Worker time is double-bookable across the draft-driver hold and WorkScheduler. Employment wages settle without verifying work. PersonJobDecisions (candidate-side judgment) has no production callers.
- Verdict: PARTIAL. P2.

### Cash
- Business, household, and owner cash are meaningfully separate. Movements go through real transactions (payroll, purchasing, hardship credit, obligations). No phantom money found except the town-pulse revenue defect (Document C, chain 24).
- Verdict: PRODUCTION, with one P1 exception.

### Knowledge
- Information with provenance exists (contacts, letters, reputation). But there is no general belief store, no NPC-to-NPC conversation, and no mechanism for knowledge to be a contested multi-use asset (e.g., a foreman's observation reaching competing buyers).
- Verdict: PARTIAL. P1 (the foreman chain).

## 3. The pattern

Assets the household economy touches directly (food, tools, equipment, cash, wagons) are well-connected. Assets that require cross-system arbitration (horses between draft and mount, workers between scheduler and draft, buildings between housing and business, knowledge between observers) are partially connected or broken. The missing piece is consistently **arbitration**: a decision-maker that weighs competing uses. Reservations exist; adjudication does not.

## 4. Priority

1. P1: Unify horse availability (one reservation truth). Design decision required on the canonical mechanism.
2. P1: Give horses a production condition lifecycle (care, feeding, work cost).
3. P1: Replace town-pulse manufactured revenue with real household demand.
4. P2: Bind journey travel to real animals. Clear sale reservations on trade. Build the rental path.
5. P2: Make worker time single-bookable across scheduler and draft.
6. P2: Add economic occupancy capacity to buildings.
7. P3: Animal collateral. Land-use allocation.
