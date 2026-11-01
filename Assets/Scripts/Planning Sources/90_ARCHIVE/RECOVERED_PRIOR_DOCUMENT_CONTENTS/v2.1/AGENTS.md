# Land & Ledgers — AGENTS.md (Codex/AI document routing)

> **READ FIRST:** `0000_START_HERE_READ_ME_FIRST.md` in this same extracted package root. **Before installing/updating documentation on Kens-PC**, also read `0004_CODEX_LOCAL_DEPLOYMENT_AND_REPLACEMENT.md`. For machine-readable paths, open `0001_DOCUMENT_ROUTER.csv`. Do not guess filenames or rely on external copies.

## Two-location deployment rule

This rolling release is provided as a ZIP to **ChatGPT Project Sources**, and as **extracted files** to a dedicated **Kens-PC planning documents** folder. These locations do not synchronize automatically. When a user asks Codex to update the PC folder using a supplied ZIP, follow the safe staging/verification/backup/replace procedure in `0004_CODEX_LOCAL_DEPLOYMENT_AND_REPLACEMENT.md`. Recover the existing destination path from the earlier **“Move planning PDFs”** task; the screenshot alone does not reveal its exact path. **Never delete project code or unrelated files.** Never overwrite another repository `AGENTS.md` without reviewing it.

## How to load these instructions

- This `AGENTS.md` must exist **on disk where Codex can read it**, preferably at the actual repo root or an ancestor. **A ZIP attachment does not guarantee automatic AGENTS.md discovery.** If you extracted this library elsewhere, read it explicitly and merge relevant instructions with any existing repo AGENTS.md; do not overwrite the latter blindly.
- For a project documentation task, identify the workstream and open the exact ZIP-relative source(s) in `0001_DOCUMENT_ROUTER.csv`; a mere search hit is not enough to determine governing priority or supersession.
- No GitHub connection is necessary to read **these documents**. Checking **current implementation** does require the actual Unity repository and revision evidence; do not invent it.

## Governing rule sources

1. **Original Canon XXXI (gameplay):** `01_Original_Source_Documents/Land_and_Ledgers_Canonical_Project_Bible_Revision_XXXI_2026-10-07(1).pdf`
2. **Original Technical XIV (implementation planning, subordinate to Canon):** `01_Original_Source_Documents/Land_and_Ledgers_Technical_Implementation_Bible_Revision_XIV_2026-10-07(1).pdf`
3. **Latest shorter consolidated v0.3 drafts:** `02_Latest_Consolidated_Bibles_v0.3/Land_and_Ledgers_Consolidated_Canon_v0.3_Provisional_2026-10-08.md` and `02_Latest_Consolidated_Bibles_v0.3/Land_and_Ledgers_Consolidated_Technical_v0.3_Provisional_2026-10-08.md`; both are **PROVISIONAL** and **do not supersede** the two originals.
4. The specialist historical, economic, occupational, construction and scenario references are evidence/authoring/supporting docs, not free-standing gameplay authority. See topic router.
5. Previous drafts in `07_...` never outrank the current originals or newer subject drafts merely because search found them first.

## Rules for changes, answers and code

- Normal Open Frontier start is **spring 1870** (not superseded spring-1880 wording); scenarios can specify other starts.
- Descriptive Business/occupation classes are **not** hard action-permission gates; real resources, authority, physical space, agreements, time, Person willingness and legality govern.
- Maintain one authoritative record per real thing; no invented business cash, clone shipment cargo, duplicate owner identity, second Person availability or fabricated opportunity population.
- `CreateBlockedShipment(...)` duplicating cargo is a **documented risk requiring current-repository verification**; do not mark fixed without code and save/load tests.
- Proposals **001–105 are approved design direction**; **106–145 require approval as new proposals**, notwithstanding overlaps with already-existing Canon.
- Use citations such as `CAN-ORIG p.N`, `TECH-ORIG p.N`, stable rule ID and the exact ZIP-relative path. Read later supersession before quoting earlier preserved layers.
- Historical dates, prices, legal powers and occupational distributions require source, geography, year, units and confidence. Mark gaps as **RESEARCH HOLD**, not as facts.
- Do not silently modify original frozen PDFs; drafts, changes and derived data require traceability, version, and QA.
- Documentation approval is not proof of compiled Unity behavior or tested regression fixtures. Do not claim tests passed without executing tests.
- When publishing a new ZIP, update `0000_START_HERE_READ_ME_FIRST.md`, `0001_DOCUMENT_ROUTER.csv`, `0002_ALL_FILES_INDEX.csv`, `0003_AUDIT_FINDINGS_AND_CHANGELOG.md` and `MANIFEST_SHA256.csv`.
