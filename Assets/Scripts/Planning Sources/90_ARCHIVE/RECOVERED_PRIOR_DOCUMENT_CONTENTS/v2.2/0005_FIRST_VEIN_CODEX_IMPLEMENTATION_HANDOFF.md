# THE FIRST VEIN — CODEX IMPLEMENTATION HANDOFF (v2.2 / 2026-10-29)

**Critical: This is a SOURCE-BACKED DESIGN/PLANNING handoff, NOT a verified Unity playtest or a certificate that code exists.** Begin with root `0000_START_HERE_READ_ME_FIRST.md` and `AGENTS.md`, then the detailed report in `06_First_Vein_Conceptual_Playtest_and_Source_Evidence/`.

## Implementation goal

Implement the established systemic simulation direction so THE FIRST VEIN and other scenarios can run on actual authoritative game state, including delegated mining, freight, contracting, property/rights, transactions, NPC commerce and independently validated scenario goals. The interactive experience must preserve depth **without requiring repetitive player approvals**.

## P0 — Inspect, protect and unblock foundations

1. Identify actual Unity project, current commit/branch, latest build/test results and existing systems. Separate planned concepts from actual classes and fixtures.
2. Inspect/fix/verify `CreateBlockedShipment(...)`: never both restore seller stock and preserve duplicated physical cargo. Add failure, cancel, retry, save/load and exactly-once transfer tests.
3. Confirm singular authority for Person, Business, assets, ore lots, money, debt, physical transport capacity, PropertyInterest and Agreements; block duplicative events and phantom inventories across saves.
4. Audit occupation/BusinessType and BusinessOperation flags: no hidden business-class or profession-gated ordinary action menus, but preserve physical, legal and employee-response restrictions.
5. Create explicit shared Task/Agreement/Transaction transitions for managers and players; no manager ability to generate labor, capital, inventory or geology ex nihilo.

## P1 — Strategic autonomy and clear boundaries

1. Routine mining should operate six days/week **when safe, funded, viable and staffed**; owner sets recurring work, procurement, sale, hiring-search and risk policies. Manager may perform ordinary tasks inside actual cash/labor/resource limits.
2. Surface only material exceptions (new major debt, land acquisition, equity dilution, permanent hire if owner-reserved, unsafe conditions, out-of-policy equipment/construction and extraordinary deals). Permit owner choice of how much authority to delegate.
3. Player **retains 100% ownership of Juniper No. 1**. Prefer 100%-owned future mill and purchased fee land rather than leasing; seller-financed low-down-payment deals are preferred **if legitimately offered and supportable**. This is a player policy/preference, not a universal forced choice.
4. Provide practical time progression where 30–90-day jumps can happen without intervention if no material unresolved exceptions; maintain continuous NPC economics and preserve full traceable transactions.

## P1 — Material mining, settlement and economic truth

1. Persist geological source/uncertainty; distinguish selected assay specimen, in-situ sample, extracted grade, processed feed, concentrate, tailings, recovered gold, paid ore sale and recovered-and-sold troy ounces. Do not count raw ore sales toward recovered gold goals.
2. Reconcile mine output against tools, crews, work packages, ventilation/access, animal haul, fuel, equipment, power/water, working days, route/transport capacity and real loss/byproducts. Do not create arbitrary resource nodes by milestone.
3. Make paid freight, bought/rented haulage and actual customer ore transport flow through trips, queues and legal cargo custody; no duplicate cargo or free teleportation. Real buying/selling prices may reflect assay, terms, moisture/grade, transport and counterparty risk.
4. Implement dated and jurisdiction-specific legal interest workflows: prospect/claim, unpatented millsite, mineral/land interests, deeded title, purchase-money mortgage, seller note, lease, water and tailings rights; no filed location = automatic fee title.
5. Implement signed/oral agreements with evidence, notice, breach, remedies, dispute resolution, transfer of fixtures and contractually specified compensation as legally supported. Missing paper is an evidence issue, not automatic zero enforceability.
6. NPCs can start independent business activities using the SAME entity/physical/credit/information constraints. Do not count player subsidiaries as independent businesses; trace actual operating durations and paid third-party sales.

## P2 — UX, scenarios, QA and calibration

1. Searchable Action Registry + constrained proposal builder for deals, jobs, referrals and supplies, not mandatory LLM or thousands of hard-coded dialogue trees; give normal-language definitions in tooltips.
2. Compact dashboards with numbers-first exception summaries, always-visible unambiguous objective progress, and drill-down to source/ledger only when wanted.
3. Create THE FIRST VEIN scenario fixture using **exactly seven objectives** in `07_First_Vein_Documentation_Updates/FIRST_VEIN_SEVEN_OBJECTIVES_AND_TEST_FIXTURES_v2.2.md`. Do not restore removed `$400/6-month` milestone.
4. Run at least favourable and poor seeds on real code, exercise manager/task, death/inheritance, abandoned claims, defaults, disputes, no sale, stock shortages and unprofitable geology, and verify autosave/manual save/load no-duplication. Keep evidence of unique lot IDs, event IDs, cash journals, owner equity, payroll and population.
5. Keep historic gold price, geology, legal enforceability, wages, stamp mill recovery and transport costs as source-backed research/calibration holds where not verified. A mine must be able to lose money.

## Evidence and decision boundary

`06_First_Vein_Conceptual_Playtest_and_Source_Evidence/THE_FIRST_VEIN_FINAL_PLAYTEST_AND_CANON_HANDOFF_2026-10-29.md` is the principal finding source. The 1878–1890 sequence and 6/7 are illustrative simulation, NOT passed tests. Full report's Part 8 contains unresolved historical/financial decisions. The old v0.3 drafts are NOT authoritative replacements for the original Canon XXXI / Technical XIV. Crosswalk updates as requirements, verify current repo before implementation claims, record changed files and evidence.

## How Codex should report completion

- What was actually present in current repo before changes (commit/source references).
- Proposed and executed changes, affected systems, migration and backward compatibility.
- Tests run (commands + totals), failures, source/asset conservation cases and save/load results.
- Unresolved research/architecture issues with exact source paths and requirements.
- New documentation version only after source-backed update and manifest verification.
