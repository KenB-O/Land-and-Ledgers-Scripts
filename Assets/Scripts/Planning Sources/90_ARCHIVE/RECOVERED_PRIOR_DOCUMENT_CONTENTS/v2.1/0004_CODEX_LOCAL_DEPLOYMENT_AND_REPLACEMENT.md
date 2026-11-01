# LAND & LEDGERS — CODEX LOCAL DEPLOYMENT / REPLACE PLANNING DOCUMENTS

**Package edition:** 2.1 (rolling) · **Date:** 2026-10-08  
**Audience:** Codex working on **Kens-PC**.  
**Operation:** Replace the older *Land & Ledgers documentation set* in the *existing planning-documents folder* that Codex handled in its earlier **“Move planning PDFs”** task.

## Important destination rule

The screenshot provided to ChatGPT identifies the previous Codex job but **does not expose an absolute Windows folder path**. Never invent the path. Recover the exact target directory from the current Kens-PC workspace, the prior “Move planning PDFs” task, previous action logs or actual files. If more than one candidate exists, or the previous destination cannot be established, **ask the user for the precise folder path before deleting or moving anything**. Do **not** use the Unity repository root, Windows Documents, Downloads or a generic project root as a fallback.

## What goes where

- **ChatGPT Project Sources:** The **ZIP file itself**, uploaded as the single replaceable project source, for reading/updating by ChatGPT. No automatic synchronization with the PC is implied.
- **Kens-PC:** The **uncompressed contents** of the same ZIP, directly inside the verified existing planning-documents directory. The file `0000_START_HERE_READ_ME_FIRST.md` and `AGENTS.md` must be at the **root of that destination directory**, not under an extra version-named subfolder.
- **GitHub:** No GitHub integration or repository commit is required for this documentary update.

## Replacement workflow (do in sequence)

1. Find the **newest user-provided ZIP** named `Land_and_Ledgers_Documentation_Package_v2.1_2026-10-08.zip` (or a later version explicitly supplied for this job). If multiple ZIPs are present, do not guess: identify the one just supplied in the Codex conversation and its release edition.
2. Verify the precise **planning-document destination** used in “Move planning PDFs”. Confirm it is a **dedicated documentation folder**, not the repository root or mixed code/assets folder. Show the resolved absolute path in the final report.
3. Inspect the destination before removal. Build a manifest of its existing items. Preserve any unrelated or user-authored files that are not from the previous Land & Ledgers planning-document package. If unsure about an item, **do not remove it without clarification**.
4. Extract the ZIP into a **new staging directory outside the destination**, on the same filesystem when practical. Safely reject absolute member paths, `..` traversal, links or extraction paths outside staging. Do not nest the ZIP top level inside a folder named after the ZIP.
5. Validate extraction: confirm `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md`, `0001_DOCUMENT_ROUTER.csv`, `0002_ALL_FILES_INDEX.csv` and `MANIFEST_SHA256.csv` are at the staging root; validate all manifest rows (relative paths, byte sizes, SHA-256), confirm all router paths exist and confirm the original Canon XXXI and Technical XIV PDFs exist. The ZIP's manifest does not list itself; do not require a self-hash.
6. **Only after validation**, create a dated pre-replacement backup in a **sibling folder outside the live destination**, e.g. `<planning-folder>_backup_before_v2.1_<timestamp>`. Keep the backup available for recovery but not mixed into the new active document tree.
7. Replace the **previous managed Land & Ledgers documentation contents** in the verified destination with the full staged tree. If inspection proves the destination is entirely dedicated to the managed documentation, a folder swap is acceptable. If it contains mixed or unrelated files, remove only older *managed* documentation files, leave unrelated files intact and report the leftovers. Never recursively wipe the Unity project or parent directory.
8. Ensure the current extracted package files sit **directly at the destination root**, including `0000_START_HERE_READ_ME_FIRST.md` and `AGENTS.md`. Do not leave older Canon XIII / Canon XXX or Technical XIII / draft PDFs loose at that root as competing active references; the package's `07_Prior_Drafts_and_Audit_Edition_History/` is the intentional history area. The frozen Canon XXXI and Technical XIV inside `01_Original_Source_Documents/` remain authoritative.
9. Re-run the manifest and router checks **against the actual destination** after replacement. Count installed files; report any extra unrelated files retained. Check no duplicate older loose planning PDFs are left where the old task put them.
10. Make future Codex sessions aware of the extracted documents: if the Unity repository has its own `AGENTS.md`, **do not overwrite it**. Add or maintain a short pointer to the absolute documentation folder's `0000_START_HERE_READ_ME_FIRST.md` and `AGENTS.md` only if this can be done safely within the user's approved scope; otherwise explicitly tell Codex to open those files for documentation-related tasks. An `AGENTS.md` inside an unrelated directory does not automatically apply to every session.
11. Final response to the user: report the exact installed path, package edition, installed file count, manifest pass/fail, backup location, any unmanaged files retained, and whether the repo-root Codex instruction pointer was updated. **Do not claim success unless files were actually installed and verified on Kens-PC.**

## Authority and scope safeguards

- Read the root `0000_START_HERE_READ_ME_FIRST.md` for topic routing after installation.
- The governing originals remain **Canon XXXI** and **Technical XIV** until semantic no-loss gates close. Consolidated v0.3 Bibles remain **provisional**.
- Historical references are not automatic gameplay authorities. Proposals 001–105 are approved design direction, 106–145 are pending.
- Do not modify the game code, overwrite unrelated `AGENTS.md`, delete unrelated project materials, or silently rewrite any original PDFs as part of this file replacement.
- Future releases are **rolling versions** (v2.2, v2.3, etc.) rather than “final” editions. Treat one release ZIP as the deployable unit; preserve the unmodified source ZIP under ChatGPT Project Sources and refresh the extracted PC documentation folder with each new release.
