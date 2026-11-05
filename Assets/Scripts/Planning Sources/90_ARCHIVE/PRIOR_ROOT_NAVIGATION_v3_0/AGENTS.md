# AGENTS.md — Land & Ledgers (documentation v3.0)

**Before any repository/code action:** Read `0000_START_HERE_READ_ME_FIRST.md` then `0003_AUTHORITY_PRECEDENCE_AND_STATUS.md`. These instructions apply to the extracted *Planning Sources* documentation directory, not any pre-existing repository-level AGENTS.md.

## Authority
- Full immutable Canon XXXI governs established gameplay; full immutable Technical XIV governs target architecture subject to Canon. They include previous revision layers; only explicit later overrides apply.
- Approved numbered directions 001–105 supplement as evidenced; 106–145 remain pending. Conceptual First Vein direction is source-backed but clause-specific conflicts require decision evidence.
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
