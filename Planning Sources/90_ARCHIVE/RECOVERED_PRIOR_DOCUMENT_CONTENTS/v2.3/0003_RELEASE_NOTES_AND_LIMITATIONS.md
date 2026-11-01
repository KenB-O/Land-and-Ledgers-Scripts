# Documentation package v2.3 — integration and cleanup report

**31 October 2026**

## Corrected issue

Version 2.2 placed a FIRST VEIN handoff and two addenda prominently alongside unchanged Bibles. This split led to conflicting navigation and caused First Vein to appear more authoritative than other playtests. v2.3 puts the findings inside the normal Canon, Technical, Scenario, Economic, Law and Implementation documents instead.

## Active changes

- Rebuilt Canon as `01_Canonical_Project_Bible` (v0.4 integrated working edition): management/autonomy, mine/geology, ore vs recovered metal, toll milling, title and contract evidence, financing and player-owned equity, NPC relationships, event evidence, searchable action/tooltip UX.
- Rebuilt Technical as `02_Technical_Implementation_Bible` (v0.4 integrated working edition): 4 additional contracts on geology, recovery, agreement evidence and scenario preferences; plus insertion into existing transaction, shipment, property, manager, information, objective and save/load authorities.
- All playtests now share one scenario registry; First Vein appears as SCN-009, with seven objectives and 13 new acceptance fixtures.
- Law and Economy references received only **research holds**, not invented historical calibration.
- Removed retired appendices, prior-step status scripts and separate First Vein change addenda from active navigation; original source and full conceptual checkpoint record remain in `90_ARCHIVE`.
- Preserved previous 105 approved proposals, 106–145 pending, original Canon XXXI and Technical XIV for unverified historical detail.

## Verification limitations

- Full semantic no-loss audit of all older Bibles still incomplete; the source baselines are retained.
- Unity repository is not available in this session; *no code was inspected, altered or tested*.
- Historical mining laws and mine-profit numbers in source report not independently verified.
- The original First Vein 1890 6/7 success is a **conceptual** narrative outcome, not a real integration test.
- Scenario G5 profit boundary and G7 opening equity basis remain open until fixture policy explicitly establishes them.

## Scope of replacement

Codex should **replace previous managed documentation folders** rather than merge v2.3 on top of v2.2; stage the archive first, back up prior managed files outside the active path, preserve unrelated files, and install `0000_START_HERE_READ_ME_FIRST.md`, `AGENTS.md` and numbered current docs at the top structure.
