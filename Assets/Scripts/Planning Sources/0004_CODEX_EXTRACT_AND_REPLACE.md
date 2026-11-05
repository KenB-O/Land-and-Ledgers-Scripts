# Codex extraction and replacement — Windows planning folder

**Target:** Rolling v3.1 documentation deployment only; no Unity changes.

**Planning folder**: `F:\Land & Ledgers\Planning Sources` (previously confirmed in v2.1 installation report).

1. Read `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md`, and this file **inside the ZIP without executing game code**. Confirm target path exists and is the previous managed documentation installation, else stop and investigate.
2. Create a timestamped backup **outside** the target (`Planning Sources_backup_before_v3.1_YYYYMMDD_HHMMSS`). Do not delete/overwrite the previous backup. Record old managed files and identify unrelated `.txt`, temp/lock and any user-created files.
3. Extract the ZIP into a separate staging directory. Reject absolute, `..`, drive-root or symlink-traversal paths, ZIP duplicate names and collisions before any deletion. Ensure staging has direct root `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md` and exactly the manifest paths.
4. Validate every `MANIFEST_SHA256.csv` entry against uncompressed staged bytes (size and SHA-256). Validate 346-page and 170-page PDF originals, matching hash values in the source manifest, and existence of router paths.
5. Only after staging passes, move old **managed documentation files** out of the active target into backup; keep unrelated files intact. Deploy staging's **contents**, not the ZIP nor a new intermediate parent folder, to the exact target.
6. Re-run file size/SHA-256 manifest checks and router existence checks in the active folder. Confirm the PDF originals match the v3 release source hashes and that old loose legacy PDFs are not competing in the active folder.
7. Preserve repository-level AGENTS.md elsewhere, and do not edit Unity source as part of the deployment.
8. Report exact destination, backup, installed file count, missing/extra files, verification results and any unresolved locks. On any failure, roll back previous managed files from backup rather than leaving a partially installed library.

**PowerShell verification sketch** (after ZIP extraction into `$root`):
```powershell
$root = 'F:\Land & Ledgers\Planning Sources'
Import-Csv (Join-Path $root 'MANIFEST_SHA256.csv') | ForEach-Object {
  $f = Join-Path $root ($_.Path -replace '/', '\')
  if (!(Test-Path -LiteralPath $f)) { throw "Missing $f" }
  if ((Get-Item -LiteralPath $f).Length -ne [long]$_.Bytes) { throw "Size mismatch $f" }
  if ((Get-FileHash -Algorithm SHA256 -LiteralPath $f).Hash.ToLowerInvariant() -ne $_.SHA256.ToLowerInvariant()) { throw "Hash mismatch $f" }
}
```

## November 4 v3.1 safety additions
- Detect and replace only managed files from the previous package, preserving unrelated files and the previous backup outside the destination. Do not blindly wipe the parent `Land & Ledgers` directory.
- Do not start code changes simply because the ZIP includes a `07_CODEX_IMPLEMENTATION_HANDOFF` folder; those are future scoped engineering references.
- Ensure the new November 4 decision register and the 24-value non-Canon exclusion file are installed, present in the router, and readable.
- Confirm that original Canon XXXI and Technical XIV PDFs match the governing source hash table exactly.
