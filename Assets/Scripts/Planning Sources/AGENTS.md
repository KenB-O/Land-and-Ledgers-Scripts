# AGENTS.md — Land & Ledgers (documentation v3.1)

**Before any repository/code action:** Read `0000_START_HERE_READ_ME_FIRST.md` then `0003_AUTHORITY_PRECEDENCE_AND_STATUS.md`. These instructions apply to the extracted *Planning Sources* documentation directory, not any pre-existing repository-level AGENTS.md.

## Authority
- Full immutable Canon XXXI governs established gameplay; full immutable Technical XIV governs target architecture subject to Canon. They include previous revision layers; only explicit later overrides apply.
- Approved numbered directions 001–105 supplement as evidenced; 106–145 remain pending **as numbered proposals**. The owner's dated Nov 4 decisions are separately confirmed where expressly stated: `02_APPROVED_AND_SOURCE_BACKED_UPDATES/05_POST_CANON_USER_DESIGN_DECISIONS_2026-11-04.md`. Do not promote an entire pending proposal just because it overlaps a newer confirmed narrow requirement. Conceptual First Vein direction is source-backed but clause-specific conflicts require decision evidence.
- `04_NAVIGATION_ONLY_CONSOLIDATIONS` contains non-exhaustive shortened drafts. NEVER treat them as permission to drop original requirements.
- `90_ARCHIVE` contains old source/release evidence, **not** active game instructions. Search archive for provenance, not default mechanics.

## Search instructions
- Use `0001_DOCUMENT_ROUTER.csv` for exact relative paths.
- Use `08_SEARCH_INDEXES/SECTION_AND_REVISION_HEADING_INDEX.csv` or full `_FULL_PAGINATED.txt` via `rg`/search. Cite `CAN-XXXI original PDF p.N` / `TECH-XIV original PDF p.N`. Note PDF page is 1-indexed, not necessarily printed revision pagination.
- If source says `CHARTER`/`claim`/`title`/`bank`/`business`, examine source-scoped date, geographical jurisdiction and physical/financial prerequisites before generalizing.

## Repository behavior
- This ZIP does NOT grant code access. Identify the actual Unity repo path, commit, branch and test baseline before changes; inspect scripts before asserting features.
- Check current `CreateBlockedShipment(...)` and cancellation/retry/save/load for duplicate cargo; the old Technical Bible documented a bug, not proof it still exists.
- One source of economic/physical truth per Person, Business, Cash, Inventory, Lot, Property, Agreement, Shipment and Task. Never fabricate resources to meet scenario goals. Generic business actions emerge from state; occupation/business labels are not hard gates for ordinary work.
- Save/load and exactly-once transaction tests precede large architecture changes. Honor owner-delegated policy and independent NPC ownership.
- First Vein 6/7 was a conversational outcome, not a passed Unity test. Implement exact seven objectives separately and test negative seeds.

## Documentation maintenance
- Update source trace, release notes and hashes for every published package. Never delete source PDF or its complete extraction, even when generating new shorter navigation editions.
- Do not promote pending 106–145 or assert full semantic no-loss without independent clause-level proof.
- Installation: stage → verify hashes → backup old managed files outside active folder → replace only managed files → validate routes → report. Preserve unrelated personal files and other AGENTS.md outside the documentation folder.

## Current owner-design register and task boundary (2026-11-04)
- Before designing new economic mechanics, use `05_POST_CANON_USER_DESIGN_DECISIONS_2026-11-04.md` plus complete PDF Canon / Technical. Confirm exact entry classification; do not treat illustrative prices/times, subjective preferences or Unity observations as global locks.
- `NOV04_RECONCILIATION_OUTCOME_A_TO_H.md` lists already covered, clarified, open, conflicts and future implementation implications. This **documentation update** does not authorize code modifications. A separate explicit engineering task is required.
- Validate fixed terms against the 24 negative locks in `05_EXPLICIT_NONCANON_ITEMS_2026-11-04.md`.
- The report that current freight uses Millbrook at 50 miles, 25-cent dispatch and 1 cent/mile is a **development-configuration report, not historical calibration or verified current code**.
- Reject a generic occupation permission gate. A low-skill Person may attempt physically feasible ordinary work under Canon XXXI; lack of skill changes outcome and risks, and actual law/technology can limit specialist work.
- User-authored horse/wagon prefab transforms and anchors must not be overwritten without authorization.
