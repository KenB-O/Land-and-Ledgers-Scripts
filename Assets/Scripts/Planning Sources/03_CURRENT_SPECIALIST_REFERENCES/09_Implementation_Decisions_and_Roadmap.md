# LAND & LEDGERS
## IMPLEMENTATION ROADMAP AND DECISION REGISTER
### CURRENT INTEGRATED EDITION · 31 OCTOBER 2026

**Edition:** 2026-10-31 | **Status:** Ready for development use; not evidence of Unity implementation.

## I. Authorization and decision status
- 001–105: **approved design direction** from the user’s earlier explicit agreement.
- 106–145: **pending distinct gameplay approval**; document them without silently amending Canon for genuinely new mechanics.
- User delegated completion of documentation workflow and waived interim review; this does not transform unverified repository state or missing historical documents into facts.
- Active reading: integrated Canon v0.4 / Technical v0.4; preserved Canon XXXI / Technical XIV remain baselines for omitted historical detail while semantic no-loss certification is open. Neither reference proves implemented Unity code.
- The baseline historical references remain immutable under refreshed compatibility sheets.

## II. Implementation groups and dependencies

**DEV-A — Person/Business/Property/Ownership single authorities**

- **Priority:** P0
- **Must depend on:** Repository schema audit
- **Acceptance/test gate:** One authority, no labels as permission

**DEV-B — EconomicEvent/finance/inventory atomic transfers**

- **Priority:** P0
- **Must depend on:** DEV-A
- **Acceptance/test gate:** Exactly-once transaction and shipment lineage, save/load

**DEV-C — Generic Business + ProductionMethod + tasks**

- **Priority:** P0
- **Must depend on:** DEV-A/B
- **Acceptance/test gate:** Change retail/forge services without type switch

**DEV-D — Labor/time/occupation/worker response**

- **Priority:** P0
- **Must depend on:** DEV-A/C
- **Acceptance/test gate:** No duplicate Person time or identity

**DEV-E — Rights/agreements/finance/banks**

- **Priority:** P1
- **Must depend on:** DEV-A/B
- **Acceptance/test gate:** Owner cash distinct; contingent guarantee and deposits

**DEV-F — Routes/freight/stores/rail**

- **Priority:** P1
- **Must depend on:** DEV-B/C
- **Acceptance/test gate:** Physical cargo, route reopening, service vs track

**DEV-G — Markets/NPCAI/migration/households**

- **Priority:** P1
- **Must depend on:** DEV-A–F
- **Acceptance/test gate:** No objective-spawned population or demand

**DEV-H — Scenarios/reporting/historical event toggle**

- **Priority:** P1
- **Must depend on:** DEV-A–G
- **Acceptance/test gate:** Reconciled evidence and repeated time/save stress

**DEV-I — Specialty historic datasets/calibration**

- **Priority:** Rolling
- **Must depend on:** Document references
- **Acceptance/test gate:** Jurisdiction/date/source confidence and unit tests


## III. Proposal register
**Full 145-row register:** Companion `02_REGISTERS/Approved_and_Pending_145_Proposals.csv` with source, status, provisional crosswalk and development gate.

## IV. Open release blockers

**REL-001 — CRITICAL**

- **Blocker:** Canon no-loss certification
- **Gate:** Canon XXXI remains gameplay authority

**REL-002 — CRITICAL**

- **Blocker:** Technical no-loss certification
- **Gate:** Technical XIV remains implementation authority

**REL-003 — CRITICAL**

- **Blocker:** Unity repository evidence
- **Gate:** Do not claim code migrated

**REL-004 — CRITICAL**

- **Blocker:** Shipment duplicate cargo
- **Gate:** QA release blocker for provenance objectives

**REL-005 — HIGH**

- **Blocker:** Scenario runnable definitions
- **Gate:** Scenario objective packs are not certified

**REL-006 — HIGH**

- **Blocker:** Historical statutory tables
- **Gate:** Maintain dated legal holds

**REL-007 — HIGH**

- **Blocker:** Historical calibration coverage
- **Gate:** No universal fabricated rates

**REL-008 — MEDIUM**

- **Blocker:** Research compatibility
- **Gate:** Compatibility addenda issued; originals preserved

**REL-009 — HIGH**

- **Blocker:** Pending 106–145 design directions
- **Gate:** Do not silently promote new ideas

**REL-010 — MEDIUM**

- **Blocker:** Cross-document ID stability
- **Gate:** Keep provisional version prefixes


## V. Operational follow-on checklist
- Obtain a Unity repository source snapshot and authoritative baseline commit before declaring migration items complete.
- Resolve a blocked cargo shipment without duplicate loaded lots after save/load before trusting provenance-based scenario outcomes.
- Improve the 3,069/1,880 source-anchor concordances into manually verified semantic dispositions for all retained rules.
- Finalize remaining 106–145 proposals only through actual gameplay decision authority rather than treating documentation publication as automatic approval.
- Maintain per-reference history, evidence/uncertainty, checksum and revision date, and never silently overwrite prior revisions.

## VI. Completion boundary
Step 14 tracks documentation decisions and implementation readiness; it does not perform or claim Unity code changes.

## VII. Integrated findings from the mining/autonomy playtest (cross-scenario work)

The user requested that First Vein decisions be placed in the ordinary design and implementation references rather than managed as another active authority layer. The current Canon and Technical sections now own those specifications. These work items remain **not verified in current Unity**. This is a documentation handoff, not evidence that code was changed.


**Audit exact repository branch, asset schema, code and baseline tests — P0**

- **Main authority:** Technical T001, MIG-01
- **Ship gate:** Commit/test evidence

**Shipment and cash conservation with blocked/cancelled/retried dispatch — P0**

- **Main authority:** Technical T009/T010/T035
- **Ship gate:** No duplicates on retry/save/load

**Shared Task, mineral extraction and evidence-limited assay — P0/P1**

- **Main authority:** Technical T004/T037
- **Ship gate:** Actual lot provenance and poor seed tests

**Owner delegation, manager limits and exception-led strategic pacing — P1**

- **Main authority:** Canon 13 and Technical T032/T040
- **Ship gate:** No routine owner prompts or improper loan/purchase/dilution

**Recovering, toll processing, raw ore, bullion and paid-sale recognition — P1**

- **Main authority:** Technical T014/T038
- **Ship gate:** Mass/money/owner reconciliation

**Claims, title, improvements, water and dated contract evidence — P1**

- **Main authority:** Canon 05/14 and Technical T015/T039
- **Ship gate:** Research and rights-correct workflow

**Independent NPC business and population continuity — P1**

- **Main authority:** Canon 11/16 and Technical T034
- **Ship gate:** 90/60 day continuous state with unique IDs

**Seven-goal First Vein executable fixture, negative tests, UX definitions — P2**

- **Main authority:** Scenarios SCN-009, QA-017 to QA-029
- **Ship gate:** Unity test evidence, not conceptual score


**Research holds:** 1877 Black Hills gold geology/prices, assay variability, freight, milling recovery, seller-financed property records, territorial millsite rights, livestock handling and updated statutory terms. Do not convert fictional simulated prices or 1890 outcome into calibration data.

**Document-authority gate:** Working v0.4 consolidation still requires comprehensive source-by-source no-loss signoff before retiring preserved original baselines. There is no automatic approval of numbered proposals 106–145.


## November 4, 2026 owner priority and implementation holds (documentation only)

Priority is an **autonomous, cyclical and physically conserved** economy: Bakery flour need → credible freight terms → actual loaded shipment → regional arrival and return → unloading and receipt → freight settlement → fuel and production inputs → bread → customer sale → household consumption → repeat. Report each separately verified implementation stage, never a single conceptual test as complete.

After this foundation, the owner prioritizes economic agency and depth such as NPC formation/competition, asset trading and leasing, real-estate and business transfers, credit, failure/recovery, functional space and staffing. Detailed succession law remains lower priority. The ordering guides resources, not an inflexible unlock chain.

**Reported implementation observations requiring live verification:** `DevelopmentTownConfiguration` consolidation; nine residents/five households/four businesses; Millbrook sample 50-mile one-way journey and 125-cent rate card; startup entering First Ledger; NPC moving unusually quickly; NPC clicking selects a Building; prefab horse/wagon transforms; camera zoom limits; crowded town UI. Do not treat any as verified by this documentation release.

Full scoped decisions: `02_APPROVED_AND_SOURCE_BACKED_UPDATES/05_POST_CANON_USER_DESIGN_DECISIONS_2026-11-04.md`. Reproduction list: `06_SCENARIOS_AND_TESTS/NOV04_HUMAN_PLAYTEST_OBSERVATIONS_REPRO_LIST.csv`.
