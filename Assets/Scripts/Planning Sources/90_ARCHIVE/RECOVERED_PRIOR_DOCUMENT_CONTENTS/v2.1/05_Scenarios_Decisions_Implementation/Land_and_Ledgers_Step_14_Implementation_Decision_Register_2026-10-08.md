# LAND & LEDGERS
## IMPLEMENTATION ROADMAP AND DECISION REGISTER
### STEP 14 — WORKFLOW TRACKER

**Edition:** 2026-10-08 | **Status:** Ready for development use; not evidence of Unity implementation.

## I. Authorization and decision status
- 001–105: **approved design direction** from the user’s earlier explicit agreement.
- 106–145: **pending distinct gameplay approval**; document them without silently amending Canon for genuinely new mechanics.
- User delegated completion of documentation workflow and waived interim review; this does not transform unverified repository state or missing historical documents into facts.
- Current authority for gameplay is frozen Canon XXXI; for technology planning Technical XIV, until both no-loss and code/source crosschecks are complete.
- The baseline historical references remain immutable under refreshed compatibility sheets.

## II. Implementation groups and dependencies
| Group | Domain | Priority | Must depend on | Acceptance/test gate |
|---|---|---|---|---|
| DEV-A | Person/Business/Property/Ownership single authorities | P0 | Repository schema audit | One authority, no labels as permission |
| DEV-B | EconomicEvent/finance/inventory atomic transfers | P0 | DEV-A | Exactly-once transaction and shipment lineage, save/load |
| DEV-C | Generic Business + ProductionMethod + tasks | P0 | DEV-A/B | Change retail/forge services without type switch |
| DEV-D | Labor/time/occupation/worker response | P0 | DEV-A/C | No duplicate Person time or identity |
| DEV-E | Rights/agreements/finance/banks | P1 | DEV-A/B | Owner cash distinct; contingent guarantee and deposits |
| DEV-F | Routes/freight/stores/rail | P1 | DEV-B/C | Physical cargo, route reopening, service vs track |
| DEV-G | Markets/NPCAI/migration/households | P1 | DEV-A–F | No objective-spawned population or demand |
| DEV-H | Scenarios/reporting/historical event toggle | P1 | DEV-A–G | Reconciled evidence and repeated time/save stress |
| DEV-I | Specialty historic datasets/calibration | Rolling | Document references | Jurisdiction/date/source confidence and unit tests |

## III. Proposal register
**Full 145-row register:** Companion `Step_14_..._145_Proposals.csv` with source, status, provisional crosswalk and development gate.

## IV. Open release blockers
| ID | Severity | Blocker | Gate |
|---|---|---|---|
| REL-001 | CRITICAL | Canon no-loss certification | Canon XXXI remains gameplay authority |
| REL-002 | CRITICAL | Technical no-loss certification | Technical XIV remains implementation authority |
| REL-003 | CRITICAL | Unity repository evidence | Do not claim code migrated |
| REL-004 | CRITICAL | Shipment duplicate cargo | QA release blocker for provenance objectives |
| REL-005 | HIGH | Scenario runnable definitions | Scenario objective packs are not certified |
| REL-006 | HIGH | Historical statutory tables | Maintain dated legal holds |
| REL-007 | HIGH | Historical calibration coverage | No universal fabricated rates |
| REL-008 | MEDIUM | Research compatibility | Compatibility addenda issued; originals preserved |
| REL-009 | HIGH | Pending 106–145 design directions | Do not silently promote new ideas |
| REL-010 | MEDIUM | Cross-document ID stability | Keep provisional version prefixes |

## V. Operational follow-on checklist
- Obtain a Unity repository source snapshot and authoritative baseline commit before declaring migration items complete.
- Resolve a blocked cargo shipment without duplicate loaded lots after save/load before trusting provenance-based scenario outcomes.
- Improve the 3,069/1,880 source-anchor concordances into manually verified semantic dispositions for all retained rules.
- Finalize remaining 106–145 proposals only through actual gameplay decision authority rather than treating documentation publication as automatic approval.
- Maintain per-reference history, evidence/uncertainty, checksum and revision date, and never silently overwrite prior revisions.

## VI. Completion boundary
Step 14 tracks documentation decisions and implementation readiness; it does not perform or claim Unity code changes.
