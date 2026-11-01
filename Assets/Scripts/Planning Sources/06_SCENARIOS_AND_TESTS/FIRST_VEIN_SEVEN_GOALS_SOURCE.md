# THE FIRST VEIN — Seven-goal scenario source of truth (v2.2)

**Source:** `06_First_Vein_Conceptual_Playtest_and_Source_Evidence/THE_FIRST_VEIN_FINAL_PLAYTEST_AND_CANON_HANDOFF_2026-10-29.md`, Part 2.4. **Outcome status in the table is illustrative conceptual playtest only** — NOT Unity code, historical data or validated gameplay. Earlier `$400 / six-month profitability` objective explicitly REMOVED; never reintroduce it. Standard Open Frontier start is still spring 1870; this scenario starts 1877.

## 2.4 Final score — all seven objectives

| # | Deadline | Objective | Final outcome |
|---|---|---|---|
| 1 | 1877-12-31 | Legal control of prospect; 2 in-situ samples; independent assay | **PASS** — May 20, 1877 |
| 2 | 1878-12-31 | First actual paid mineral or recovered-gold sale | **PASS** — August 2, 1877 |
| 3 | 1882-12-31 | 650 cumulative recovered-and-sold troy ounces | **PASS** — 695 oz at end 1882 |
| 4 | 1884-12-31 | Two genuinely independent NPC-owned businesses, each 90 days in operation and 5 external paid sales | **PASS** — Nordin Cooperage; Juniper Washhouse |
| 5 | 1886-12-31 | 850 recovered-and-sold oz within 12 consecutive months AND positive mining operating profit | **PASS** — 880 oz during 1886; positive simulated integrated mine/mill operating result |
| 6 | 1888-12-31 | At least 90 permanent residents continuously for 60 days | **PASS** — qualifying window ended July 9, 1888 |
| 7 | 1890-12-31 | 5,500 cumulative recovered-and-sold oz AND $18,000 reconciled owner equity | **FAIL** — 4,935/5,500 oz; equity not certified |

**Final: 6/7 achieved.** In the model, the qualifying 1886 mine-plus-mill operating contribution is **+$5,499.60** after direct operating costs allocated to those activities (before capital investment, debt service and private owner draws); this is an illustrative annual attribution, not an audited Unity accounting result. The original removed $400/6-month profitability milestone is **not** reintroduced. Objective 5 retains its pre-existing positive-operating-profit condition until the user explicitly chooses to simplify it.


## Actual implementation test cases (not yet executed)

- **G1:** valid claim/control evidence, two independently sourced in-situ samples plus separate assay event by deadline. Claims are not automatically fee title.
- **G2:** receipt of actual paid mineral or recovered-gold transaction by date; mere quote, delivery or invoice is not cash receipt.
- **G3:** exactly 650 cumulative troy ounces of owner **recovered-and-sold** metal by deadline; ordinary raw ore sale does NOT count; custom milling accounting preserves ownership.
- **G4:** two Businesses with eligible non-player control/ownership, each actually operating continuously for at least 90 days and completing at least five external paid sales; no counting subsidiaries or internal-only transfers.
- **G5:** 850 recovered-and-sold troy ounces in **any 12 consecutive months** ending by deadline **and positive mine/mill operating profit for the relevant measurement boundary**. Define accrual/cash, freight, capital/depreciation and finance treatment, do not silently substitute a different easier profit test.
- **G6:** at least 90 actual permanent residents continuously for 60 days by deadline; distinguish visitors/commuters/temporary camps from primary residence and keep Person/Household identity across save/load.
- **G7:** by 1890-12-31 at least 5,500 cumulative owner recovered-and-sold troy ounces plus at least $18,000 independently reconciled owner equity with declared inclusions, exclusions and starting balances; uncertain opening living expenses prevent fictitious audited equity claims.

**QA policy:** Test representative/poor seeds, persistence, exactly-once source events, missing claims/samples, unpaid invoices, ore ownership, cancellation and retry, player-controlled vs independent firms, continuous vs cumulative time, and transparent owner equity. The original illustrative 6/7 is NOT a pass condition of the runnable suite.
