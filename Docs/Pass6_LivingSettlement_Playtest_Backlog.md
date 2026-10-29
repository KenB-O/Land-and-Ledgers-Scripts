# Pass VI — Living Settlement / Playtest Backlog

Status recorded from the Pass VI source audit. Visual statuses remain unclaimed
until the Unity Editor can be inspected graphically.

| ID | Issue | Current status | Evidence / next check |
|---|---|---|---|
| LL-PLAY-001 | Incorrect starting ownership / ghost Businesses | Needs Unity recheck | Fresh-world bootstrap explicitly disables automatic player store creation; verify clean Main Scene. |
| LL-PLAY-002 | Excessive PlayMode-generated scene roots | Not tested | Requires Edit Mode vs Play Mode hierarchy inspection. |
| LL-PLAY-003 | Duplicate Civic Foundation | Not tested | Requires Main Scene runtime inspection and authority-count trace. |
| LL-PLAY-004 | Obsolete tutorial guidance | Source covered; visual pending | First-session guidance has dedicated tests; verify current scenario text in Unity. |
| LL-PLAY-005 | Missing scenario HUD | Not tested | Requires UI inspection against live ScenarioCoordinator state. |
| LL-PLAY-006 | Blacksmith management panel layout | Not tested | Visual Editor check required. |
| LL-PLAY-007 | Seller meeting modal behavior | Not tested | UI interaction check required. |
| LL-PLAY-008 | Repetitive acquisition buttons | Not tested | UI interaction check required. |
| LL-PLAY-009 | Missing sale-discovery provenance | Not tested | Read-model/UI inspection required. |
| LL-PLAY-010 | Rigid construction progression | Partially implemented | WorkPackages and construction tasks exist; live presentation needs verification. |
| LL-PLAY-011 | Business labels without real operations | Partially implemented | Generic runtime exists; audit each exposed BusinessType in Main Scene. |
| LL-PLAY-012 | Artificial staffing limits | Source covered; runtime pending | Labor demand is workload-driven in source; verify live report. |
| LL-PLAY-013 | Implausible profits / unclear periods | Not tested | Requires live event-backed report comparison. |
| LL-PLAY-014 | Civic tab language | Not tested | Requires current UI state inspection. |
| LL-PLAY-015 | Resource and district displays | Not tested | Requires current UI state inspection. |
| LL-PLAY-016 | Flat terrain | Not tested | Visual/world-generation assessment deferred until Editor access. |
| LL-PLAY-017 | Calendar weekday mismatch | Source covered; runtime pending | Gregorian tests passed previously; verify displayed Main Scene date/weekday. |
| LL-PLAY-018 | General Store startup/notices | Source partially covered; runtime pending | Fresh bootstrap suppresses automatic player store; inspect current notices. |
| LL-PLAY-019 | MainMusicPlayer lifecycle | Not tested | Requires Edit/Play hierarchy and console inspection. |

## Runtime dependency audit

| Dependency | Source status | Production evidence still required |
|---|---|---|
| Boarding-house registration / housing search | Adapter and hub persistence exist; normal bootstrap caller not found in source audit | Live boarding business must create rooms, register with `SimulationDrivers`, and participate in housing search. |
| NPC construction material sources | Household adapter exists; world supplier registration not found in source audit | NPC project must resolve real supplier inventory and custody. |
| Bank deposit projection | `BankCreditParticipant.RefreshDepositsFrom` exists | Bank runtime must refresh immediately before live underwriting. |
| Duplicate wage/debt/activity authorities | Compatibility systems remain in repository | Confirm actual writers in Main Scene and preserve one authoritative mutation path. |
| Main Scene 30-day proof | Controlled fixtures previously passed | Ordinary New Game path still requires runtime trace and reconciliation. |

Visual validation is currently pending because the available environment is
headless; no screenshot or visual-fix claim is made by this file.

## Pass XV update — 2026-10-29

The opening farm defect was traced to typed premises selection: the formation
context used first-fit buildings without requiring an agricultural site role.
Pass XV added a typed production-context selector and live field registration.
The old four-week First Ledger Victory expectation was corrected to honest
progress against the unchanged $15,000 threshold. Post-fix Unity validation was
incomplete because the Unity/Bee compiler runner stalled before NUnit output;
no visual backlog item is marked fixed from this pass.
