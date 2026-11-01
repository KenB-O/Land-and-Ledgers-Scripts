# Pass XVI — Unity/Bee recovery

Date: 2026-10-29

## Baseline

The Scripts repository began on `integration/pass12-unity-readiness` at
`38b22ac3e8aeb93e9acd4ec73225d7c66c61aa1b`, with a clean working tree. The
Unity project is `F:/Land & Ledgers` and `ProjectSettings/ProjectVersion.txt`
identifies Unity `6000.4.2f1`.

## Recovery evidence

The minimal EditMode filter used was
`LandLedgers.EditorTests.Economy.Farming.CropModelTests`.

Three bounded attempts were made against the real project:

1. Existing Bee cache: no NUnit XML was written; the editor reached IL post-processing.
2. Existing cache with `-burst-disable-compilation`: no NUnit XML was written; the same ILPP stage remained active.
3. Regenerated `Library/Bee`: package assemblies were regenerated, but the run stopped after the log ceased advancing while processing `Unity.TextMeshPro.Editor.dll` through Jobs and Burst IL post-processors.

The final run had a Unity batch process and an ILPP child associated with this
project, no C# compiler diagnostic, no result XML, and no log growth for more
than three minutes. Only that verified process tree was stopped. No unrelated
Unity project was touched.

The prior Bee cache was preserved at:
`F:/Land & Ledgers/Library/Bee_Pass16_stale_20261029_093500`.

Diagnostic logs and incomplete result locations are preserved under:
`F:/Land & Ledgers_Pass16_Backups`.

## Disposition

The runner is not recovered. This is an ILPP/Bee-stage environment blocker,
not evidence of a C# compile failure or a gameplay failure. No Pass XVI
agricultural implementation was added while Unity verification was unavailable.

