# Technical-facing implementation addendum — THE FIRST VEIN (v2.2 / 2026-10-29)

**Status:** requirements/acceptance specification for Codex to compare against the actual repo; original Technical XIV remains the architecture planning authority. Source: First Vein conceptual report Part 4–9 and supplemental files. **Do not mark anything implemented without Unity code and passing tests.**

## Interfaces, invariants and state transitions

FV-TECH-001 **Authority graph**: Person, Business, owner/manager authority, property and security interests, economic accounts, employment/independent contracts, agreements, tasks and information have one source of truth each. BusinessRole, occupation and operation descriptors do not act as hidden hard gates.

FV-TECH-002 **Task scheduling**: production and commerce compete for actual workers, equipment, materials, animal/vehicle capacity, safe workspace, site access, finance and calendar slots. Manager policies schedule resources and escalate exceptions; they never mint capacity.

FV-TECH-003 **Owner/manager governance**: standing policies explicitly define hiring, borrowing, equipment, spending, selling, land, construction, capital changes and reporting thresholds. Owner-reserved actions remain owner-reserved; avoid global pause every time a routine transaction could occur.

FV-TECH-004 **Ledger and transaction atomics**: order, reservation, physical movement, acceptance, sale, collection, commission, remittance, refund and ownership transfer have explicit states and exactly-once settlement, including save/load and retry. Inspect `CreateBlockedShipment(...)` for duplicated cargo risk and add multiple failure fixtures.

FV-TECH-005 **Ore lot lifecycle**: sample and in-situ source -> extraction lot -> transport/stock -> assay (selected or representative) -> raw ore sale OR milling -> concentrate/tailings/recovered metal -> sold owner-recovered troy ounces; keep ownership, custody, quantities, units and recoverable value distinct. Generate assay evidence; do not infer head grade from one selected specimen.

FV-TECH-006 **Mineral economics**: actual ore mass, feed grade, losses, mill extraction/recovery, smelter/buyer terms, transportation, payroll, lease/royalty/claims, debt, fuel, water, maintenance and time consume real resources. Profitable and unprofitable seeds must both be supported.

FV-TECH-007 **Title and rights**: property graph distinguishes locatable mineral interest, unpatented claim, millsite entry, patent/fee title, lease, easement, access, water/tailings, improvements, mortgages and chattel security; consent and applicable 1877–1890 jurisdiction rules are data. Do not auto-promote claim to deed.

FV-TECH-008 **Agreement evidence**: oral and written terms, communications, performance, collateral, fixtures, transfers, notice and breach/remedy are durable records. Enforceability/priority depends on dated jurisdiction and evidence, not paperwork presence alone.

FV-TECH-009 **NPC information symmetry**: contactable Persons and observed business data only, not omniscient financials. A searchable action registry and structured proposal builder allow flexible deals without mandatory LLM. NPC firms operate independent real inventory and money.

FV-TECH-010 **Scenario objective predicates**: exact seven goals tracked from authoritative events and snapshots; enforce all deadlines, continuity durations, no-double-counting, full ownership filters, owner equity provenance, no hidden profit creation or milestone spawns.

FV-TECH-011 **Presentation and clock**: optional long time advance, compact exception summaries, consistent game 1:1 / accelerated time rules as independently approved; do not invent a single mandated tick rate from the conversational vignette. Explain action feasibility and unknown information honestly.

FV-TECH-012 **Regression minimum**: lost/blocked cargo; identical callbacks; late shipping; under/oversold ore; independent assay; milling vs raw sale; debt service/default; manager borrowing boundary; title/claims; owner-controlled dilution; subcontract/toll processing; NPC independence; 90/60 day population continuity; 12-month rolling recovery+profit; save-load at each stage; poor geology and market shock seeds.

## Issue triage

P0 current source inventory + conservation/transaction bugs + save/load + authority gates. P1 autonomous management, ore/transport/title/economic systems. P2 scenario fixtures, UI, historical calibration and multi-seed playtest. Historical numbers and jurisdiction-specific legal assertions in supplied report require independent research before hard-coded values.
