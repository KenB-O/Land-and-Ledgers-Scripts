# Pass VIII Person, Household, and Social Cohesion

Phase 2 (housing) and Phase 3 (person/household autonomy, shared assets, social knowledge) of Muse Pass VIII.

## Housing and boarding (Phase 2, Brief 6.1)

Re-audit found the foundations real (`HousingAuthority`, tenancy payment service through the single cash writer and debt authority, housing search over real opportunity sources, a mature boarding package) with genuine gaps: `Occupy` had no capacity check; `EndOccupanciesForPerson` had zero callers, so the dead and departed held accommodation forever; boarding rooms were not accommodation spaces and boarders had no occupancy records (parallel universes); rental search created agreements with no payment terms (free housing by omission); no move service, capacity-change API, closure/transfer resolution, or absence sweep.

Implemented: capacity-guarded `TryOccupyUnderAgreement` (commercial lodging types; private arrangements keep Canon 12.7 freedom-with-consequence); `LodgingRegistry` binding rentable rooms to real spaces with bed-count equality; `BoardingLodgingBridge` unifying boarding inventory, agreements, and housing occupancy with rollback on failure (boarding agreements carry no payment terms so the existing rent collection stays the single money path); `HouseholdMoveService` validating before mutating; `HousingAbsenceSweep` with caller-supplied predicates and a lenient-policy helper; `HousingOpportunityAdapter` over real availability with an `UnmetSleepingPlaces` construction-demand signal; payment terms attaching from search options so the rent loop charges cash or books real arrears; daily strict absence sweep in the drivers; capacity shrink refused below occupant count; agreement transfer preserving obligation linkage; building closure ending occupancies, agreements, and assignments with history kept. Fifteen new tests proving every required property: over-capacity refused, nonexistent accommodation refused, discovery never invents housing, rent/arrears real, departure frees space, capacity changes respected, closure/transfer clean, save/load exactly once, absence sweep with valid-arrangement exemption.

## Person and household autonomy (Phase 3, Brief 9)

First-missing-connection audit per cycle:
- Circumstances to decisions: wired. Decisions to actual work: was missing — `ExecuteSeekIncome` discovered openings into a log nothing consumed. Fixed with `OpportunityPursuitService`: discovered openings become real applications for eligible unemployed adults (an application is an inquiry, never an employment).
- Work to wages: was missing — payroll ran on the employment record plus business cash (automatic attendance). Fixed with attendance-gated payroll: verified presence through a fail-closed seam; present-but-idle workers do upkeep through the real task system; the genuinely absent record nothing and are not paid.
- Wages to budget to consumption to business revenue to response: wired throughout.
- Opportunities and hardship to decisions: wired, with one defect fixed — the job-loss fingerprint was household-scoped but checked per member, misattributing a provider's death to the surviving spouse. Now per-member fingerprints plus departed-earner detection; the survivor moves to `SeekIncome` through the real pressure path.
- Disability had no teeth: `laborCapacityPercent` was computed but consumed nowhere; now multiplies into usable minutes.
- Demographics: the scenario driver mirrored age by hand and never refreshed labor access, freezing scenario children; now uses `RefreshDerivedAge`.
- Dead never offered work: verified beyond recruitment with fail-loud seams at task assignment, journey departure, and scheduling.
- Absent persons: the lenient side of the housing sweep implemented — a valid arrangement is a still-standing agreement or an active off-map journey with expected return; one-way migration, completed or overdue journeys are not; death never is.

Fifty-seven new tests, including 90-day and 3-year reconciliation and save/load identity suites. Not built (recorded findings): childcare, caregiving, and recovery have no scheduled person-time anywhere — a real Canon-everyday-life gap for a later pass.

## Shared assets and opportunity costs (Phase 3, Brief 10)

Proven gap fixed: `RentalFleet.Return` never released the unified-authority hold taken by `RentOut` — a returned rig stayed reserved forever. Verified sound and untouched: horse rental, the freight pool, equipment reservation atomicity, workstation gating. Finding not built: `RentalFleet` had no cash path at all — addressed in Phase 4 with refusal-first renter debit and business credit. No generic asset framework was built; only proven gaps were fixed.

## Social knowledge integration (Phase 3, Brief 11)

The Pass VII foreman chain was a one-off proof: no production decision authority consumed packets. Generalized with a shared honest-evidence assessment plus packet consumption inside three ordinary authorities:
- Layoff rumor to job search: dismissed miners observed directly, telling coworkers at the saloon; the job-security evaluator reads only the hearer's packets and the corroborated firsthand telling produces real `JobSeeking` person-time and a real application through the new `ApplyForEffort` path.
- Supplier trouble to steered orders: a defaulted trade obligation observed and relayed; the procurement executor's corroborated-trouble assessment skips the troubled supplier for the next known supplier with real goods; cash conserved buyer to mill-2.
- Borrower trouble to lender corroboration: two independent observers relay to the lending officer; corroboration reduces lender trust and prices measurably higher through the shared underwriting, disclosed on the credit file with chain roots. One relayed rumor plus clean records is noted as seeking corroboration with pricing exactly unchanged.

Safeguards proven in tests: authorities read only the decider's received packets; duplicate tellings refused with single chain roots; confidence decays along chains; no instant global spread; uncorroborated single rumors never move a decision; rumor alone can never refuse credit. Nine new tests. The lender intake stays unwired in production until the bank has a real officer person (inventing one would be a fake person); the call site is marked. Remaining example chains (tenant hearing a full boarding house, farmer equipment sharing, wage-driven migration) are clean follow-ups on the shared evaluator pattern.
