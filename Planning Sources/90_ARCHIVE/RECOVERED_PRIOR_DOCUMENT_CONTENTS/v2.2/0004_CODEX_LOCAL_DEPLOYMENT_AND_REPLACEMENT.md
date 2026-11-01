# Codex Local Documentation Deployment — rolling v2.2 (2026-10-29)

**Known installation target from user-confirmed 2026-10-08 Codex report:** `F:\Land & Ledgers\Planning Sources`.

1. Confirm target exists, review prior deployment report and enumerate existing package-managed plus unrelated files. **Do not touch game source/repository code in a documentation install.**
2. Identify this attached rolling ZIP by its name/manifest; inspect ZIP member paths, reject absolute paths, traversal, symlink escapes or unexpected entries.
3. Extract into a temporary **staging folder outside** the live target. Verify `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md`, `0001_DOCUMENT_ROUTER.csv`, `0002_ALL_FILES_INDEX.csv`, `MANIFEST_SHA256.csv`, `0005_FIRST_VEIN_CODEX_IMPLEMENTATION_HANDOFF.md` and both original Bible PDFs. Verify all recorded sizes/hashes and router paths.
4. Back up old managed documentation to a timestamped sibling, e.g. `F:\Land & Ledgers\Planning Sources_backup_before_v2.2_<timestamp>`, retaining previous backups.
5. Replace **only previous managed package files** in `F:\Land & Ledgers\Planning Sources` with the entire staged extracted tree. Do not delete unrelated `.txt` files, temporary Office files, personally written notes, assets, scripts, or existing Unity/repo `AGENTS.md`. Old managed files not in new manifest should leave the active documentation tree after backup.
6. Recheck manifest and 21+ router paths at the destination (do not hardcode path count). Confirm root `AGENTS.md` and `0000_START_HERE_READ_ME_FIRST.md` directly inside target, NOT a nested ZIP/version directory. Verify that original Canon XXXI and Technical XIV are inside `01_Original_Source_Documents/`, not loose conflicting PDFs.
7. Report destination, backup, version, total installed package files, unrelated retained entries, manifest/router verification and any failures. If verification fails, leave or restore previous docs rather than claim success.
8. Once installed, read `0005_FIRST_VEIN_CODEX_IMPLEMENTATION_HANDOFF.md` for implementation priorities and locate the actual game repository. **Do not infer the Unity repo state from the conceptual playtest ZIP.**

**Do not use the nested evidence folder as another live documentation root.** Only the package's top-level `0000...`, `AGENTS.md` and router are active entry instructions.
