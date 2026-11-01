# Land & Ledgers v3.0 — Codex development handoff (documentation only)

**Order:** Read `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md` and `0003_AUTHORITY_PRECEDENCE_AND_STATUS.md` before editing Unity. Source docs are **planning references, not proof that code exists**.

## P0 — inspect and safeguard, before gameplay additions
1. Identify exact local Unity repository path, branch and commit; record existing tests/build state and save format. Never infer from public repo snapshots that local code matches.
2. Search `CreateBlockedShipment(...)` and adjacent cargo creation/cancellation/retry paths. Older Technical XIV (original PDF p.101) reports a cargo duplication risk (seller stock restoration while blocked shipment retains cargo). **Re-audit current code**; demonstrate exactly one authoritative cargo lifecycle after cancellation, retry and save/load.
3. Inventory person, inventory/lot, money, Business and property authoritative stores; eliminate or guard double writers. Establish idempotent transaction keys and portable save/load round-trip fixtures before rewriting business models.
4. Test removal of BusinessType/occupation presentation labels without removing physically valid action opportunities. Real equipment, labor, space, rights and agreement state remain the gate.

## P1 — shared simulation systems
- Generic Business economics, nonclass commercial actions, contacts and manager activity; avoid parallel activity authorities.
- Physical mining chain: in-situ samples vs assay evidence; ore lots/ownership/custody; toll processing; concentrate/tailings, recovered-and-sold metal, waste and liabilities.
- Loan/contract/property interests: separate fee title, unpatented mining claim, lease, mortgage, millsite, water access, improvement and historical legal restrictions.
- Independent NPC enterprise formation and Person/residence continuity. Owner-lock and escalation semantics: routine progression must not ask the player to approve every worker assignment.

## P2 — scenario and UI validation
- Implement exact First Vein seven-goal scenario definitions from `06_SCENARIOS_AND_TESTS/FIRST_VEIN_SEVEN_GOALS_SOURCE.md`; do not infer Unity PASS from 6/7 conceptual narrative.
- Corrected 16 cross-scenario source fixtures live at `06_SCENARIOS_AND_TESTS/CORRECTED_16_REGRESSION_FIXTURE_REFERENCES.csv`. The old v2.3 table is retained and explicitly marked uncorrected.
- Test no positive profit from debt, no credited gold from raw-ore payments, no NPC subsidiary counting, distinct current/cumulative/continuous objective clocks, negative and volatile seeds.

## Blockers
- Independent review needed for any claim that older full Bibles were exhaustively consolidated.
- Obtain current Unity repository evidence before declaring implementation or risk resolved.
- New First Vein specifics may require period law research before turning example rights/amounts into universal gameplay law.
- Proposals 106–145 remain pending; implementation of already-established original doctrine is separate.
