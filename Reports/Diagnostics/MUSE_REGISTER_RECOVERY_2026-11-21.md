# Muse Issue Register Recovery — 2026-11-21

## Scope and provenance

The Muse PDF and published Muse branch do not contain `register/DEFECT_REGISTER.md` or the pass-home `HANDOFF.md`. A search of the supplied Codex attachment folder, the owner checkout, and the Muse integration checkout found no separately supplied copy. This artifact is therefore a **partial reconstruction**, not the original complete Muse register.

The accompanying CSV reconciles every row in PDF Appendix A against the original 2026-11-19 Console Investigation Checklist. It preserves the original CI IDs and both historical status columns, the consolidated SIG mapping, occurrence count, simulation days, affected entities, source location, exact recorded message, and checklist status. Reconciliation result: 354 unique CI IDs in both sources, 645 total warning occurrences in both sources, and 37 distinct SIG identifiers. No CI identities were missing from either side.

The CSV preserves the Muse PDF's status totals: 237 unresolved, 96 implemented/unverified, 15 blocked, and 6 explained/legitimate. These are Muse-reported statuses, not Unity verification.

## Missing DEF register limitation

The PDF reports 90 DEF entries, but Appendix A is the CI-to-SIG table; the PDF does not supply the complete DEF descriptions, evidence, statuses, or CI-to-DEF mapping needed to reconstruct the 90-entry register. The accessible source contains scattered DEF references and A-to-Z checks, which are not equivalent to that register. No missing DEF entries or descriptions have been invented. Recover the original DEF register and pass `HANDOFF.md` from the Muse pass-home source when available, then reconcile them append-only against this reconstruction.

## Source status and limitations

- The original checklist remains unchanged at `CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md`; later Codex follow-up records remain there and are outside the 354-row Muse Appendix A baseline.
- This CSV is a derived index from the published checklist and Muse PDF Appendix A. It does not replace either source, add Unity evidence, or close any issue.
- The Muse report's single Roslyn failure `DEF-046 DoubleTimeline_ExistsButHasTwoProductionConsumers` (expected 3, actual 7) remains pre-existing. Its assertion and failure evidence must be retained; this reconstruction does not reinterpret it.
- Unity compilation, Unity Test Runner, Editor-window opening, and PlayMode evidence for the Muse checkpoint remain unverified until executed in Unity 6000.4.2f1.

## Append-only update rule

Keep the source CI identities and historical occurrences immutable. Add later observations as dated evidence rows or append-only follow-up records; change current status only with an explicit dated status transition and supporting verification. Never overwrite the 2026-11-19 or Muse-pass status columns.
