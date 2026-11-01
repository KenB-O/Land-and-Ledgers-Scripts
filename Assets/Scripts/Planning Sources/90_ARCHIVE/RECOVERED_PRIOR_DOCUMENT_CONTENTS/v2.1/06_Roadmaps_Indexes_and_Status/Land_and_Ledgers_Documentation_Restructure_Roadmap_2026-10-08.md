# LAND & LEDGERS — DOCUMENTATION RESTRUCTURE ROADMAP
**Roadmap version:** 1.0 | **Prepared:** 8 October 2026 | **Status:** Active working plan

**Objective:** A controlled migration from four source PDFs and cumulative revision layers into two consolidated authoritative Bibles, five specialist historical references and two development/reference documents; original sources remain archived and every migration decision remains traceable.

## A. Scope and current position
- **Baseline:** Canon XXXI (346 pages), Technical XIV (170 pages), Occupation/Demography report (18 pages), Historical Work/Construction report (18 pages).
- **Game-design decisions:** proposals 1–105 approved; proposals 106–145 pending explicit review.
- **Initial achievement:** Step 01 source intake, hashes and packaged baseline archive completed; Step 02 is next.
- **No automatic authority promotion:** the consolidated Canon and Technical draft editions replace the current versions only after the relevant approval gates.
- **No Unity code changes are performed by this documentation roadmap.** Technical implementation audits depend on actual repository evidence.

## B. Authority, preservation and review rules
**Authority and precedence.** Canon governs intended gameplay; Technical governs implementation responsibilities and is subordinate to Canon; historical references govern evidence and calibration but create no new mechanic; scenario files govern scenario-specific predicates; the registry governs workflow and decision status, not game rules; repository evidence alone can confirm what is implemented.

**Preservation before simplification.** Never erase older material merely to shorten a file: each rule must be retained, merged, moved to a named destination, explicitly superseded with traceable evidence or flagged for decision; preserve quotations, sources, dates and original research uncertainty.

**Approval discipline.** Review one numbered step at a time; deliver its artifact, open decisions and pass/fail checklist; advance only after user accepts the step; draft editions do not replace XXXI/XIV before sign-off.

**Proposal status.** Items 1–105 are approved design direction; items 106–145 are proposals awaiting explicit review and must not silently enter authoritative Canon or Technical editions.

**Current code truth.** A document cannot prove a matching Unity class or mechanism exists; use repository inspection and tests to distinguish implemented, partial, absent, obsolete and uncertain.

**Historical calibration.** 1870–1905 geography, jurisdiction, period, measurement units and evidence quality travel with historical claims; document evidence does not authorize global constants or free economic effects.

## C. Target library: nine active documents
| ID | Target document | Scheduled release | Function |
|---|---|---|---|
| ACTIVE-01 | Canonical Project Bible — Consolidated Edition 1 | After Step 4 | Gameplay authority |
| ACTIVE-02 | Technical Implementation Bible — Consolidated Edition 1 | After Step 7 | Architecture authority |
| REF-01 | Historical Timeline, Events & Economic Regimes | Step 8 | Period/reference data |
| REF-02 | Historical Law, Land Rights & Institutions | Step 9 | Period/reference data |
| REF-03 | Occupation & Demography Calibration — Updated | Step 10 | Calibration |
| REF-04 | Historical Work & Construction — Updated | Step 11 | Calibration |
| REF-05 | Historical Economy, Prices & Materials Calibration | Step 12 | Calibration |
| DEV-01 | Scenario & Playtest Reference | Step 13 | Scenarios/tests |
| DEV-02 | Implementation & Decision Register | Step 14 | Development tracker |

**Archival rule:** Original numbered revisions, raw playtest outputs and dated research remain stored separately as provenance, not treated as additional active authorities.

## D. Numbered step-by-step roadmap
### Phase A — Freeze and reconcile
#### 01. Baseline intake and preservation
**Status:** Complete: source PDFs verified and checksum archive created  
**Deliverable:** Four-source register with SHA-256 and baseline ZIP; preserve current revisions unchanged.  
**Approval gate:** All four files are readable; hashes match archive contents; no claim that this is a repository archive.

#### 02. Rule register and supersession audit
**Status:** Next  
**Deliverable:** Inventory all operative rules from preserved revisions; capture newer overrides, duplicate phrasing, conflicts, source locations and unresolved questions.  
**Approval gate:** Every source rule receives a Keep / Merge / Move / Superseded / Research Hold / Decision Needed disposition; no silent deletion.

### Phase B — Consolidate the two core Bibles
#### 03. Consolidated Canon draft
**Status:** Queued  
**Deliverable:** Create new coherent outline and draft a clean Canon from the reconciled rule register, including approved proposals 1–105.  
**Approval gate:** Gameplay meaning is preserved; 106–145 remain pending; specialized history and calibration move only with references.

#### 04. Canon verification and sign-off
**Status:** Queued  
**Deliverable:** Perform a section-by-section no-loss trace against XXXI and previous layers; resolve flagged contradictions with the user.  
**Approval gate:** Every kept rule is traceable; no unapproved redesign; user approves the replacement Canon before it becomes active.

#### 05. Technical architecture and code-evidence crosswalk
**Status:** Queued  
**Deliverable:** Map Canon rules to authority owners, data relationships, state transitions and acceptance tests; distinguish intended target design from confirmed Unity code.  
**Approval gate:** No invented implementation claims; repository checks explicitly marked pending unless code evidence is supplied.

#### 06. Consolidated Technical draft
**Status:** Queued  
**Deliverable:** Draft one coherent Technical Bible XIV-successor from resolved technical guidance, including compatibility and migration notes.  
**Approval gate:** Only one state authority per fact; valid Canon behavior is implementable; obsolete approaches clearly retired.

#### 07. Technical verification and sign-off
**Status:** Queued  
**Deliverable:** Audit each technical section against approved Canon and original Technical XIV; create executable acceptance-test specifications.  
**Approval gate:** No parallel cash/inventory/Person/property truths; save/load coverage; user approves the technical replacement.

### Phase C — Publish specialist historical references
#### 08. Historical Timeline, Events and Regimes
**Status:** Queued  
**Deliverable:** Create an event catalog for 1870–1905 with dated sources, place/exposure, event type, mechanisms and ON/OFF authoring rules.  
**Approval gate:** Never equate authored historical events with endogenous incidents; no magic percentage effects.

#### 09. Historical Law, Land Rights and Institutions
**Status:** Queued  
**Deliverable:** Extract legal timelines and research holds for Dakota Territory, South Dakota, federal and Indigenous land regimes, property, finance and institutions.  
**Approval gate:** Every legal statement has effective dates, jurisdiction, source quality and unresolved caveats.

#### 10. Refresh Occupation and Demography report
**Status:** Queued  
**Deliverable:** Retain the 112-profile historical baseline, weight tables, uncertainty labels and sources while aligning references to current generic work and occupation rules.  
**Approval gate:** The 112 profiles are not job-permission classes or fixed town quotas; researched estimates stay distinguishable from Canon.

#### 11. Refresh Work and Construction calibration
**Status:** Queued  
**Deliverable:** Preserve dated source anchors and productivity ranges while updating compatibility and mapping estimates to ProductionMethod/WorkPackage inputs.  
**Approval gate:** Active work, elapsed time, queue and interruption are distinct; exceptional performance never becomes a default.

#### 12. Historical Economy, Prices and Materials
**Status:** Queued  
**Deliverable:** Extract period prices/wages/yields/tariffs, units, grades and seasonal observations, then research and document evidence gaps.  
**Approval gate:** Every number carries date, geography, unit, evidence class and whether observation versus model assumption.

### Phase D — Scenarios and implementation readiness
#### 13. Scenario and Playtest reference
**Status:** Queued  
**Deliverable:** Collect scenario setup and objective definitions, maintained-versus-event predicates, playtest outcomes, regression fixtures and provenance.  
**Approval gate:** Scenario goals only observe economic state; playtest sample numbers do not become universal rules.

#### 14. Implementation and Decision Register
**Status:** Queued  
**Deliverable:** Create an owner-facing tracker of decisions, 145 proposed changes, repository migration items, priority, dependencies, tests and current status.  
**Approval gate:** 1–105 approved; 106–145 pending review; no claim that a design decision is already coded.

#### 15. Cross-library release and maintenance gate
**Status:** Queued  
**Deliverable:** Validate all document links, IDs, citation/source maps and archive links; publish active edition index and maintenance rules.  
**Approval gate:** One canonical answer for every operative rule; archived revisions remain accessible; all unresolved holds visible.

## E. Risks and safeguards
- **False simplification:** Do not remove genuine legal categories, rights, domain-specific constraints or historical variation merely because similar descriptive business categories became generic.
- **Supersession ambiguity:** Keep an old-to-new rule map; latest valid rule wins only where it genuinely supersedes prior guidance; unresolved semantic changes require review.
- **Lost historical anchors:** Keep primary/secondary sources, quoted measurement conditions and evidence confidence in specialist references.
- **Research and design conflation:** Mark documented history, inference, implementation calibration, example and binding Canon distinctly.
- **Invented repository state:** Implementation statuses can only be confirmed by source inspection, tests or build evidence.
- **Source-hash mismatch:** Do not overwrite baseline files in-place; any revised source must have a new version and checksum.
- **Bespoke industries:** Do not create new business-specific authority classes when shared Person/Business/Task/Agreement/Asset/Property systems suffice.

## F. Step review protocol
1. Begin the lowest-numbered unfinished step; present its actual draft/registry and findings.
2. Label source-derived facts, inferences, recommended changes and unresolved research separately.
3. Provide specific changes and a traceability map, not just a summary of work completed.
4. Ask for approval or targeted corrections before advancing to the next numbered step.
5. Update the roadmap status and keep the last approved edition unchanged until the next artifact is approved.

## G. Step 01 source manifest — completed
| ID | Source | Pages | SHA-256 |
|---|---|---:|---|
| SRC-01 | Canon XXXI | 346 | `aa3f6cd55cc501cd3c8fe2e1dcfa38f84e8e1179e740d1b7730cb74bb13e7b42` |
| SRC-02 | Technical XIV | 170 | `01b475ccb3fcb12361779ef265caa2634a55b01d18e2e7a4fa671f0ea012b3ef` |
| SRC-03 | Occupation report | 18 | `54a8418988c8e8cf0fd1cba457c8baebe9b535f22c43ef1bf16c70fb54cb2a1e` |
| SRC-04 | Construction report | 18 | `0467606eb876604b1b9dfc3a403657807dea5d2da4af68b0a9dfa9e113c4d8c4` |

**Source trust:** the PDFs are preserved as supplied; page counts and checksums attest only to this uploaded baseline, not to whether all historical or architectural claims within them are correct.

## H. Immediate next step
**Step 02 — Rule Register and Supersession Audit.** Extract and index the operative Canon and Technical rules, detect newer/older contradictions, assign destinations and preserve research provenance before any content is shortened or moved. Its first deliverable is a reviewable mapping ledger, not a rewritten Bible.

**Decision for the user:** reply “Continue” to proceed to Step 02; otherwise note desired changes to this roadmap.
