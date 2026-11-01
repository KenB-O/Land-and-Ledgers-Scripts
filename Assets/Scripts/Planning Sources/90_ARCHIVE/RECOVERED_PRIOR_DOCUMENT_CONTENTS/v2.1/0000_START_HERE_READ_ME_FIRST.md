# LAND & LEDGERS — START HERE / DOCUMENTATION INSTRUCTIONS

**Package edition:** 2.1 (rolling) · **Prepared:** 2026-10-08 · **Mode:** self-contained offline ZIP.  
**First file to read:** `0000_START_HERE_READ_ME_FIRST.md` (this document).  
**Codex instruction file:** `AGENTS.md` (at the extracted package root).  
**Kens-PC install/update instructions:** `0004_CODEX_LOCAL_DEPLOYMENT_AND_REPLACEMENT.md` (read BEFORE removing old planning documents).  
**Machine-readable routing:** `0001_DOCUMENT_ROUTER.csv`.  
**Full paths and checksums:** `0002_ALL_FILES_INDEX.csv` and `MANIFEST_SHA256.csv`.

> **IMPORTANT FOR CHATGPT AND CODEX:** This package holds several editions and archival reports. **Do not choose the highest-version file as the authority automatically.** The original complete Canon XXXI and Technical XIV remain the governing design references until full semantic no-loss verification certifies their replacements. Read the routing and status below before changing rules, interpreting archived documents or implementing code.


## 0. Two-location storage and update workflow (important)

This **rolling documentation package** is distributed to **two independent locations**:

1. **ChatGPT → Land & Ledgers → Project Sources:** store the most recent **single ZIP** as the project source. When a newer edition is uploaded, replace the earlier project ZIP. ChatGPT can read members from ZIP when the project runtime exposes it.
2. **Kens-PC → pre-existing Land & Ledgers planning-documents folder:** give Codex the **same ZIP** and have it **extract the package tree**, replacing the older managed docs in that folder. The screenshot of the earlier **“Move planning PDFs”** conversation is **not an absolute path**. Codex must recover the actual destination from its workspace/history before destructive actions. Do not guess.

For the complete safe update procedure, Codex **MUST READ `0004_CODEX_LOCAL_DEPLOYMENT_AND_REPLACEMENT.md` BEFORE replacing the old folder**. The new package root should contain this guide and `AGENTS.md` directly, not a nested version folder. Old managed documents should no longer compete at the live root; put rollback backups outside the live folder. **No automatic sync exists between ChatGPT Project Sources and Kens-PC.**

Version numbers such as **v2.1** refer to this continuously updated **package edition**, not to a certified new governing Canon/Technical Bible. Do not name future packages **Final** merely because they are the latest.

## 1. Mandatory first-use procedure (AI assistants)

1. **Access the ZIP.** If file contents are unavailable, extract/open the ZIP first. Do **not** claim you read a document solely from a filename or a search snippet; do not assume uploading a ZIP automatically causes every assistant to read its contents.
2. **Read this root guide and `AGENTS.md`** before selecting substantive source documents. When working in Codex, place an actual copy of `AGENTS.md` at the project/repository root, or extract the ZIP there while checking for pre-existing instructions. **Codex is not guaranteed to open a ZIP automatically, nor to auto-discover `AGENTS.md` inside a compressed ZIP.**
3. **Resolve document ID and exact ZIP-relative path** using `0001_DOCUMENT_ROUTER.csv` below. Favor `.md` for text search/editing, `.pdf` for immutable originals and visual/layout verification, `.docx` for editorial delivery, `.csv` for structured registers.
4. **Read only relevant documents fully where needed,** consulting the controlling original Bible and the matching provisional consolidated subject section. Read specific specialist references for factual or period calibration. The search result alone may omit supersession context.
5. **Keep categories separate:** authoritative design rule, technical target, implemented code evidence, historical evidence, scenario instruction, calibration estimate, proposed change, superseded revision and personal interpretation are not interchangeable.
6. **Cite traceable evidence:** document ID, exact path, page or section/rule ID, date/version and whether the passage is governing, provisional, archived or merely researched.
7. **Before changing a rule:** check the newest governing sections and relevant supersession notes; verify consequences for other domains; update the correct master plus register and regression requirements; record gaps instead of fabricating historical specifics or tests.
8. **If an instruction conflicts:** user instructions in the live conversation govern the requested workflow; this package establishes internal document precedence. Do not silently promote unapproved proposals to Canon or claim repository code has been inspected if it has not.

## 2. Authority order and known limitations

| Rank | Material | Status / how to use |
|---|---|---|
| 1 | Original Canonical Project Bible **Revision XXXI** | **Governing gameplay/design Canon**, including its integrated earlier layers except where later governing text supersedes them. |
| 2 | Original Technical Implementation Bible **Revision XIV** | **Governing implementation planning**, subordinate to Canon for gameplay; conceptual classes are *not* proof of implemented Unity code. |
| 3 | Latest consolidated Canon + Technical **v0.3** | **Provisional modernization drafts**, useful topical summaries/targets, but **NOT certified no-loss replacements**. |
| 4 | Historical and calibration references | Source-qualified evidence, methods and estimates; no new universal rules, permissions, hard rates or event effects by themselves. |
| 5 | Scenario, tests, decision register and traceability | Scenario-specific definitions, planned QA and design status; not proof executable Unity tests passed. |
| 6 | Earlier draft/archive folders | Historical provenance only; do not promote older rules due to keyword search order. |

**Approval state:** proposals **001–105 approved design direction**; proposals **106–145 pending as distinct proposals** unless separately approved later. Waiving review checkpoints for documentation production did not grant authority to invent historical facts or certify unverifiable tests. Where a pending proposal restates a pre-existing Canon rule, that pre-existing Canon rule remains governing for its own reason.

**Important open gates:** exhaustive semantic no-loss verification for both Bibles; direct audit of the *current* Unity repository; shipment/inventory `CreateBlockedShipment(...)` duplication defect; runnable scenario QA; dated local-law and quantitative calibration research. **Keep existing originals authoritative until these release gates close.**

## 3. Exact current-document locations (paths are relative to ZIP root)

### Two **governing originals** — consult for consequential decisions

- **CAN-ORIG** — `01_Original_Source_Documents/Land_and_Ledgers_Canonical_Project_Bible_Revision_XXXI_2026-10-07(1).pdf`
- **TECH-ORIG** — `01_Original_Source_Documents/Land_and_Ledgers_Technical_Implementation_Bible_Revision_XIV_2026-10-07(1).pdf`

### The nine target/reference workstreams

| ID | Purpose | Exact recommended current file | Status |
|---|---|---|---|
| CAN-DRAFT | Current consolidated gameplay working draft | `02_Latest_Consolidated_Bibles_v0.3/Land_and_Ledgers_Consolidated_Canon_v0.3_Provisional_2026-10-08.md` | Provisional only |
| TECH-DRAFT | Current consolidated technical working draft | `02_Latest_Consolidated_Bibles_v0.3/Land_and_Ledgers_Consolidated_Technical_v0.3_Provisional_2026-10-08.md` | Provisional only |
| HIST-EVENTS | Named events, historical regimes and chronology | `04_Historical_and_Calibration_References/Land_and_Ledgers_Step_08_Historical_Events_Research_Reference_2026-10-08.md` | First edition; holds flagged |
| HIST-LAW | Land, jurisdiction, legal change, institutions | `04_Historical_and_Calibration_References/Land_and_Ledgers_Step_09_Historical_Law_Land_Institutions_Reference_2026-10-08.md` | Partial verification; source holds |
| OCC-REF | Workforce/occupation evidence and 112-profile taxonomy | `04_Historical_and_Calibration_References/Land_and_Ledgers_Step_10_Refreshed_Calibration_Compatibility_2026-10-08.pdf` | Compatibility-cover edition; research |
| WORK-REF | Construction/infrastructure time and output evidence | `04_Historical_and_Calibration_References/Land_and_Ledgers_Step_11_Refreshed_Calibration_Compatibility_2026-10-08.pdf` | Compatibility-cover edition; research |
| ECON-REF | Wages, prices, rates, material economics | `04_Historical_and_Calibration_References/Land_and_Ledgers_Step_12_Historical_Economy_Calibration_First_Edition_2026-10-08.md` | Seed evidence; **not** full price database |
| SCENARIOS | Scenarios, playtests and regression fixtures | `05_Scenarios_Decisions_Implementation/Land_and_Ledgers_Step_13_Scenarios_Playtests_Reference_2026-10-08.md` | Planning/test reference |
| DEV-DECISIONS | Proposal approval state, blockers, implementation roadmap | `05_Scenarios_Decisions_Implementation/Land_and_Ledgers_Step_14_Implementation_Decision_Register_2026-10-08.md` | Tracking only |

**Unmodified historical evidence PDFs:** Occupation report = `01_Original_Source_Documents/Occupation_Demography_and_Workforce_Calibration_Report_2026-09-25_Landed_Ledgers(3).pdf`; work/construction report = `01_Original_Source_Documents/Historical_Work_Construction_Infrastructure_Calibration_Reference_1870-1905_2026-10-05_Land_and_Ledgers(1).pdf`. The compatibility-cover PDFs preserve these underlying reports; do not treat their historical population weights or rate envelopes as universal simulation constants.

**Verification/report evidence:** `03_Latest_Verification_and_Traceability/Land_and_Ledgers_2026-10-08_Continuation_Source_Back_Audit_Report.md`; source-backed row register = `03_Latest_Verification_and_Traceability/Land_and_Ledgers_2026-10-08_Continuation_Source_Back_Audit_Register.csv`. **Full 145 proposals:** `05_Scenarios_Decisions_Implementation/Land_and_Ledgers_Step_14_Implementation_Decision_Register_2026-10-08_145_Proposals.csv`. **Provisional release index:** `06_Roadmaps_Indexes_and_Status/Land_and_Ledgers_Step_15_Library_Index_Release_Gates_2026-10-08.md` (historical Step 15 index; some named draft pointers are superseded by v0.3 paths above).

**Formats:** For `CAN-DRAFT`, `TECH-DRAFT` and other Markdown references, where the package contains matching `.pdf` / `.docx` basenames, they are alternate presentations of the same edition. Prefer Markdown for precise text retrieval; do not treat three formats as three competing authorities.

## 4. Topic router — which files should an AI open?

| User task | First read | Then read / caution |
|---|---|---|
| Business identity, commerce, hiring, production, property, NPCs | CAN-DRAFT | Verify in CAN-ORIG; consult TECH-DRAFT / TECH-ORIG for implementation |
| Unity code refactor, tasks, save/load, resource reservation, finance authority | TECH-DRAFT | Verify TECH-ORIG and CAN-ORIG; **actual codebase** required for implementation claims |
| Historical panic, boom, winter, blizzard, railway-era transition | HIST-EVENTS | CAN-ORIG on event ON/OFF and endogenous incidents; HIST-LAW for compulsory legal changes |
| Land claims, treaty/reservation geography, statehood, banking law, licensing | HIST-LAW | Check date and exact geography; CAN-ORIG / TECH-ORIG for game rules |
| Occupations, demographics, worker weights, 112-profile roster | OCC-REF | Compare original occupation research and CAN-ORIG; occupation never grants action permission |
| Construction time, track laying, infrastructure productivity | WORK-REF | Compare source assumptions, crew, physical scope; CAN-ORIG for task mechanics |
| Wages, rents, commodity/freight/equipment prices | ECON-REF | Preserve units/period/location/grade; research missing series rather than inserting made-up values |
| Scenario objectives, playtest provenance, regression | SCENARIOS | Check governing Canon and scenario source assets; scenario objectives must not spawn their prerequisites |
| Proposals 1–145, development status, blockers | DEV-DECISIONS | Check full proposal CSV and implementation evidence; do not infer user approved 106–145 |

## 5. Essential non-negotiable design context

- **Period:** normal Open Frontier runs **spring 1870–1905**; scenarios can start later. Older preserved spring-1880 material is not the current default.
- **Role describes; real state governs feasibility:** a Business label or Person occupation does not independently grant an action list. Physical, economic, contractual and relevant legal state, plus Person response, determine outcomes. Distinguish *physically possible*, *lawful* and *willing to perform*.
- **One truth per asset and obligation:** Business/owner cash, Person time, land rights, goods and cargo, loans, deposits, guaranties, contracts and household identities cannot become duplicate authoritative balances or entities.
- **Player and NPC parity:** actors use ordinary shared world, market and labor systems; difficulty emerges from resources, information, skill and decisions, not asymmetric arbitrary bonuses.
- **Evidence and uncertainty:** source facts, projections, rough calibration and developer assumptions stay visibly separate.
- **No silent historical-event mechanics:** named authored historical events may be toggled under scenario rules, while endemic simulation events and dated legal history have distinct semantics.
- **No implementation claims without evidence:** conceptual architecture docs do not confirm matching Unity classes, tests or saves.

## 6. How to update and republish the package

1. Preserve the four original PDFs byte-for-byte, along with prior published versions; do not edit the frozen original baseline in place.
2. Edit a single chosen current source file for the affected workstream, with traceable rules, the reasons for change and source/section references; if the governing Bibles remain unchanged, call the result a **provisional addendum** or draft, not a new governing canon revision.
3. Update dependent Technical/Canon interpretations, specialist research entries, scenario QA, the proposal register and a dated changelog where applicable; keep historical **evidence** distinct from gameplay **decisions**.
4. For a future genuine authority promotion, complete a source-by-source semantic no-loss audit, resolve contrary passages, identify effective version and date, and update this guide/index to name **exactly one** governing Canon and Technical edition.
5. Regenerate alternate Word/PDF views where available; verify page rendering, references, archive integrity and file-level SHA-256 checksums; recalculate `MANIFEST_SHA256.csv` after any byte change.
6. Publish a whole replacement ZIP plus separate **START HERE** and **AGENTS** text files when helpful; mark new package version/date; do not assume a ZIP uploads itself to other chats/repositories.

## 7. Codex / repository setup (important)

**The ZIP is an offline documentation container, not a git repository or a Codex tool connection.**

- **Recommended:** extract this ZIP to a directory Codex can access; put/merge the included root `AGENTS.md` at the *actual repository root* or ancestor directory. If another `AGENTS.md` already exists, **merge without overwriting unrelated agent instructions**.
- Codex may discover a repository-root `AGENTS.md` depending on its configuration, but **a copy only inside a ZIP cannot be relied on for automatic discovery**.
- For ChatGPT Project use, keep this ZIP attached; explicitly point an agent to `0000_START_HERE_READ_ME_FIRST.md` when initializing a new context. Retrieval/reading of archive members depends on the tools enabled in that conversation; it is not guaranteed automatic.
- If Codex cannot access the ZIP or fails to see `AGENTS.md`, give it the standalone `AGENTS.md` as a repo file and provide or extract the needed documents; do not substitute external internet copies for this private baseline.

## 8. Package integrity and historical archive notes

- `0001_DOCUMENT_ROUTER.csv`: exact current paths, IDs, source roles and statuses for programmatic lookup.
- `0002_ALL_FILES_INDEX.csv`: all package member paths, groups, formats and SHA-256 hashes.
- `MANIFEST_SHA256.csv`: SHA-256 for **all other members**; its own hash is omitted to avoid self-reference.
- `0003_AUDIT_FINDINGS_AND_CHANGELOG.md`: packaging-level findings, actions and cautions.
- The previous ZIP's `README_FIRST.txt` and `MANIFEST_SHA256.csv` are **preserved byte-for-byte in folder 07 as historical artifacts**; those old navigation instructions are not current.
- Group 07 (previous drafts) and group 08 (bulk source-trace inventories) support auditing but should not be routine first-choice retrieval sources.

**If unsure where to begin: read this file, locate the topic in `0001_DOCUMENT_ROUTER.csv`, then open one authoritative original and its matching current workstream document.**
