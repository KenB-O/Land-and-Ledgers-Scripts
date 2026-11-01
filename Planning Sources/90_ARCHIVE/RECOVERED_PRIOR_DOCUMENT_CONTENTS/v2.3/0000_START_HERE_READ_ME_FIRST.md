# LAND & LEDGERS — START HERE / DOCUMENT ROUTER

**Documentation package:** v2.3 (31 October 2026) — rolling edition, not final.
**Read this first.** This is the complete CURRENT documentation package, not a First Vein add-on.
**Main documents:** 9 peer references in `01_ACTIVE_DOCUMENTS`. No active appendix/addendum folder competes with them. Each text-authored document has **Markdown, Word and full PDF** formats; the refreshed historical workforce and construction reports are full PDFs.

## Instructions for ChatGPT and Codex

1. Open this file, then `AGENTS.md`, then only the active document relevant to the task.
2. Canon `01_Canonical_Project_Bible` governs intended gameplay. Technical `02_Technical_Implementation_Bible` governs target architecture, not proof of code. Consult sources in `90_ARCHIVE/SOURCE_BASELINES` only for earlier detail or unresolved conflicts.
3. Scenario/playtest findings belong in `08_Scenarios_and_Playtests` and relevant topic sections of the two Bibles. **Do not read archived First Vein checkpoints as separate gameplay authorities.** Other playtests (Crossing, Market Town, Contractor, Saloon Circuit, Enterprise Ladder, etc.) have equal status in the scenario reference.
4. Historical facts and dated numerical calibration belong in specialist docs 03–07. Do not promote fictional playtest numbers into history.
5. Previous 105 numbered proposals are approved; 106–145 remain pending independent decisions. This package integrates separately documented gameplay findings from conceptual playtesting but does not assert Unity execution.
6. **CURRENT WORKING DESIGN v0.4 IS NOT COMPLETE NO-LOSS-CERTIFIED** against all preserved earlier revision layers. Original Canon XXXI and Technical XIV remain archival fallback for prior details; the updated consolidated text states the latest intended design where explicitly addressed.
7. Inspect current Unity code/commit and run tests before claiming implementation. P0: shipment cargo conservation/`CreateBlockedShipment(...)`, atomic money/lot movement, save/load uniqueness, no BusinessType/occupation hard permission gates.
8. Codex should use the active **Implementation Decisions and Roadmap**, not a stand-alone 'First Vein implementation handoff' as the default work queue. Focus on shared universal systems and scenario-neutral acceptance tests.
9. Avoid rewriting game architecture from conceptual examples. Date/geography-specific law, measured historical rates and uncertain mineral economics require evidence.

## Current nine-document directory

| # | Document | Use for |
|---|---|---|
| 01 | `01_ACTIVE_DOCUMENTS/01_Canonical_Project_Bible.pdf` | Current integrated gameplay design and rules |
| 02 | `01_ACTIVE_DOCUMENTS/02_Technical_Implementation_Bible.pdf` | Current integrated technical architecture and test requirements |
| 03 | `01_ACTIVE_DOCUMENTS/03_Historical_Events_and_Timeline.pdf` | Dated events, historical regimes and source holds |
| 04 | `01_ACTIVE_DOCUMENTS/04_Historical_Law_Land_and_Institutions.pdf` | Dated jurisdictions, institutions, rights and research holds |
| 05 | `01_ACTIVE_DOCUMENTS/05_Occupations_Demography_and_Workforce.pdf` | 112 descriptive occupations and demographic calibration |
| 06 | `01_ACTIVE_DOCUMENTS/06_Historical_Construction_and_Infrastructure.pdf` | Historical work methods, project timing and productivity |
| 07 | `01_ACTIVE_DOCUMENTS/07_Historical_Economy_and_Prices.pdf` | Dated prices, source evidence, missing mining calibration |
| 08 | `01_ACTIVE_DOCUMENTS/08_Scenarios_and_Playtests.pdf` | All peer scenarios, exact seven First Vein goals and test fixtures |
| 09 | `01_ACTIVE_DOCUMENTS/09_Implementation_Decisions_and_Roadmap.pdf` | Implementation priority, approvals, audit gates and handoff |

## Folder and version rules

`01_ACTIVE_DOCUMENTS` only contains the nine current references and alternate document formats. `02_REGISTERS` contains machine-readable catalogues, tests and decisions. `90_ARCHIVE` preserves pre-consolidation primary sources and conceptual raw playtest evidence, **not competing live authorities**. No 'latest drafts', 'first vein changes', old step-numbered roadmap or duplicate loose PDF is in active navigation.

## Two synchronized deployment destinations

- **ChatGPT Project Sources:** upload this ZIP alone, replacing the prior ZIP.
- **Kens-PC:** Codex extracts the same ZIP into `F:\Land & Ledgers\Planning Sources`. Backup old managed files outside active folder, stage/validate hashes, remove previous managed files, deploy extracted contents (not an extra parent directory), preserve unrelated user files and repository-level `AGENTS.md`, and report installation counts/checksums.

**Do not assume file alphabetical order forces an agent to read instructions.** Explicitly open `0000_START_HERE_READ_ME_FIRST.md` and `AGENTS.md` in Codex. ZIP members are also ordered with those first for convenience.
