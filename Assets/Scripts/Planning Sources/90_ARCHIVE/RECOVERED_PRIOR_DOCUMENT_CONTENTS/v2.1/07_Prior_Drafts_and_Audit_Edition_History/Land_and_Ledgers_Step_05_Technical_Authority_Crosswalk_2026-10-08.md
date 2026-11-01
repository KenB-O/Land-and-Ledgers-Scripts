# LAND & LEDGERS — STEP 05: TECHNICAL AUTHORITY AND CODE-EVIDENCE CROSSWALK

**Date:** 8 October 2026 | **Status:** Target architecture map; **no live Unity repository review performed**.

## Objective and limits

This stage maps intended Canon behavior onto technical state responsibilities, persistence boundaries and practical regression tests. Its 36 source-traced rows are **requirements** derived from Canon XXXI, Technical XIV and both reference reports; names of authorities are **conceptual**, not claims that equivalent Unity types currently exist. The existing repository-baseline authority audit recorded in Technical XIV is used as a risk input, not silently represented as current verified code.

## Highest-risk authority boundaries

- **Cash and ledger:** Business cash, owner cash, deposit liabilities, debt and economic events must each have one economic truth; reports classify or reconcile rather than create balances.
- **Shipment and inventory:** Source stock, staged cargo, loaded cargo, returned stock and destination receipt cannot count the same goods twice. The documented `CreateBlockedShipment` defect is a P0 verification/fix target.
- **Person labor:** One Person identity and exclusive time allocation; EmploymentRelationship authority distinct from nonwage family work, contracts and owner actions.
- **Property:** Parcel title/rights, Building, FunctionalSpace, lease/occupancy/tenancy and commercial use never collapse into one business-use boolean.
- **Business:** A generic Business's permissible production and selling arise from the actual resources and legal/economic relationships rather than archetype flags.
- **Scenarios:** Objective results and saved checkpoints only read authoritative evidence; historical-event toggles are persistent and do not suppress organic adversity.

## Acceptance workflow

1. Audit repository classes and save formats, then place the **exact code path, commit, test and implementation status** alongside each mapping.
2. For every duplicate authority, choose one writer and define the reconciliation/migration strategy; do not invent new cross-system state pools.
3. Implement and run tests in priority order, especially P0 shipment conservation, cash and physical Person/task time invariants.
4. Reject feature completeness claims that rely only on the presence of conceptual names in Technical XIV.
5. Perform Step 06 only with explicit status markers distinguishing Target, Confirmed, Legacy, Partial, Blocked and Unknown.

## Crosswalk catalog

| ID | Domain | Canon | P | Key invariant |
|---|---|---|---|---|
| T001 | Business authority | CAN-CE1-02.1 | P0 | BusinessOperation / BusinessType must not grant permissions |
| T002 | Person identity | CAN-CE1-03.1 | P0 | Age/occupation/job changes preserve same PersonId |
| T003 | Employment | CAN-CE1-03.4 | P0 | No split independent wage/employer/job truth; nonwage work separate |
| T004 | Work attempt and response | CAN-CE1-03.2 | P0 | Physical attempt distinct from skill/occupation; Person can refuse |
| T005 | Physical and person time | CAN-CE1-03.3 | P0 | No incompatible simultaneous labor, commute, shopping or management |
| T006 | Shared resource reservations | CAN-CE1-06.1 | P0 | No cross-domain double booking |
| T007 | Production methods | CAN-CE1-06.2 | P0 | No recipe whitelist from BusinessType |
| T008 | Cash balances | CAN-CE1-02.3 | P0 | No report/UI cash as second posting authority |
| T009 | Economic events and exact settlement | CAN-CE1-02.4 | P0 | Known amounts exact and immutable; correction explicit |
| T010 | Shipment integrity | CAN-CE1-07.8 | P0 | No fabricated loaded cargo with restored seller stock |
| T011 | Inventory location and offers | CAN-CE1-07.1 | P0 | Cannot sell absent stock or duplicate display+warehouse capacity |
| T012 | Supplier procurement | CAN-CE1-07.2 | P1 | No anonymous item->infinite instant stock |
| T013 | Storage capacity | CAN-CE1-07.3 | P0 | Warehouse identity does not create empty storage |
| T014 | Ownership vs custody | CAN-CE1-07.6 | P1 | Holding customer grain does not transfer title |
| T015 | Land rights | CAN-CE1-05.4 | P0 | Unoccupied parcel not free title |
| T016 | Buildings, premises and use | CAN-CE1-05.2 | P0 | Building type does not dictate business identity |
| T017 | Construction project lifecycle | CAN-CE1-06.4 | P0 | Not one building-type timer, and completion separate from use |
| T018 | Asset wear and repair | CAN-CE1-06.3 | P1 | Condition and known defects not one universal HP |
| T019 | Household and accommodation | CAN-CE1-04.1 | P0 | No independent settlement resident counter |
| T020 | Demographic lifecycle | CAN-CE1-04.5 | P1 | Death does not erase estate/assets/dependents |
| T021 | Migration | CAN-CE1-04.3 | P0 | Housing never spawns migrant Persons |
| T022 | Information and knowledge | CAN-CE1-13.4 | P1 | No reveal of unobserved true skills/intent |
| T023 | Recruiting contact graph | CAN-CE1-03.5 | P0 | No omniscient applicant pool |
| T024 | Demand and Opportunity | CAN-CE1-11.3 | P1 | No direct spawning based on town score |
| T025 | Market transaction embodiment | CAN-CE1-07.4 | P0 | No revenue from settlement population multiplier |
| T026 | Transport graph | CAN-CE1-10.1 | P1 | No geometric teleport or capacity-free offmap |
| T027 | Rail lifecycle and corporate ownership | CAN-CE1-10.3 | P1 | Track laid != commercially open service |
| T028 | Historical technology | CAN-CE1-10.5 | P1 | TechnologyDefinition not global unlock flag |
| T029 | Financial debt and guarantee | CAN-CE1-12.4 | P0 | Selling business does not delete seller guaranty |
| T030 | Bank and deposit liability | CAN-CE1-12.5 | P0 | Deposits not free owner cash |
| T031 | Corporate governance | CAN-CE1-12.3 | P1 | No player treasury grab from minority company |
| T032 | Delegation and owner locks | CAN-CE1-13.2 | P0 | No +efficiency automation, owner-locked sale remains reserved |
| T033 | Historical event settings | CAN-CE1-15.2 | P1 | Off authored event does not block endogenous incident |
| T034 | Scenario objective evidence | CAN-CE1-16.2 | P0 | Goal observes, never manufactures physical state |
| T035 | Save/load and event idempotence | CAN-CE1-16.4 | P0 | No duplicate event on reload or time fast forward |
| T036 | Research references | CAN-CE1-18.5 | P1 | No universal hard-coded wage/quantity from anecdote |

**Evidence status for all 36 rows:** UNVERIFIED against current repository; source-traced intended architecture only.
