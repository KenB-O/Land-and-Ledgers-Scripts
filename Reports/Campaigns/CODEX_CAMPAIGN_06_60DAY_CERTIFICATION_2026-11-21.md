# Codex Campaign 06 — 60-Day Simulation Certification

Date: 2026-11-21  
Branch: `codex/muse-unity-integration-2026-11-21`  
HEAD: `b822455a645506b21a3b0f72a1bc5edaf813df64`  
Working tree: dirty; Campaigns 02–05 and earlier owner work remain present and were preserved.  
Certification run: **NOT STARTED**

## Decision

No 60-day Main Scene run was started. The required current-source Unity compile, essential current-source regressions, short Main Scene smoke test, and 5-to-10-day 10x evidence are not available for this dirty integration checkout. The only open Unity Editor identified is attached to the separate owner project at `F:\Land & Ledgers`; two Unity asset-database worker processes are also active. No second Editor or batch run was launched.

The resource snapshot was approximately 16,238 MB visible RAM, 4,075 MB available physical memory, and 94% committed memory use (51,696,013,312 bytes committed of 54,466,043,904 bytes limit; about 2,770 MB commit headroom). Unity process private memory at the snapshot: PID 23156 about 25.7 GB; worker PIDs 15504 and 30572 about 4.7 GB and 4.3 GB. This is insufficient headroom for safely importing/compiling another project and attempting the extended run while preserving the owner's session.

No Campaign 06 run directory, Console capture, daily snapshots, or partial simulation artifacts were created. Existing run artifacts were left untouched.

## Pre-run gates

| Gate | Status | Evidence / exact blocker |
|---|---|---|
| Current-source Unity compilation | BLOCKED | No Unity Editor is attached to this integration checkout. The open Editor is on the separate owner project; current dirty integration assemblies have not been rebuilt. |
| Essential focused regressions | BLOCKED | No current-source Unity Test Runner pass in this checkout. |
| `FreightServiceAgreementTests` | NOT EXECUTED | Fixture exists; current-source execution is unavailable until a safe Editor checkpoint. |
| F7-2 price / transfer / cap pins | NOT EXECUTED | Existing tests: `F7_ProductionTerms_WholesalePriceIs32Cents`, `F7_TransferAtProductionTerms_MovesWholeLotOnce_Settles32PerUnit`, `F7_WeeklyCapQuantity_TransfersInFull`. The 40-unit test asserts 1,280 cents and no second transfer. The 80-unit test verifies an 80-unit transfer, but source inspection shows no explicit 81-unit or accumulated-week-over-cap rejection test. None were executed against this source. |
| G3 default-driver regression | NOT EXECUTED | `FurnitureStorePurchaseG3Tests.DefaultDriver_RefusesRatherThanInstallingSynchronously` exists; not run. |
| Domestic fallback regressions | NOT EXECUTED | `DomesticLaborDriverTests.MissingFirewoodAnchor_WithStoveFallback_DoesNotParkTask` and `DomesticLaborDriverTests.MissingFirewoodAnchor_ParkedTaskResumesWhenStoveFallbackAppears` exist; neither was run. |
| Thermal / persistence regressions reviewed | SOURCE REVIEWED; NOT EXECUTED | `ExteriorTemperatureTests`, `HouseholdThermalModelTests`, and `HouseholdReserveInventoryTests.HeatingAndThermalStateRoundTripsThroughPopulationSave` are present. |
| Short Main Scene smoke | NOT EXECUTED | No current-source integration Editor available. |
| 5-to-10-day 10x evidence | NOT EXECUTED | The retained five-day run `20261120T205228Z-13c30187` belongs to an earlier source/branch state; it is not a current-source gate pass. |
| Critical startup integrity | INCONCLUSIVE | No current-source compile or startup was performed. |
| Safe resource headroom | BLOCKED | 94% commit use, about 2.7 GB remaining, and active Unity/AssetDatabase processes. No process was terminated. |
| Spillover design decision | OWNER DECISION PENDING | The two historical spillover fixtures remain unresolved; no test expectation was changed. |

## Proposed survival bars (not owner-approved)

The source labels all eight bars PROPOSED. No acceptance verdict based on them is authorized until Kennedy confirms or revises them:

| Bar | Current proposed threshold |
|---|---|
| P-1 | Days 1–7: 42 of 42 meal slots eaten every day, zero missed. |
| P-2 | Days 11–15 average at least 38 of 42 meal slots per day (about 90%). |
| P-3 | Zero missed meals when household inventory held a full serving at that meal time. |
| P-4 | No household has three consecutive days with zero meals eaten. |
| P-5 | At least one completed purchase-to-receipt trip per household per three-day window while a need is open. |
| P-6 | Zero trips stuck in Planned over 24 game hours, zero no-route aborts, and zero counter-service expiries. |
| P-7 | Household lot units do not decline from day 11 to day 15. |
| P-8 | No negative household cash and no purchase above available cash or authorized credit. |

## Scenario Lab configuration mismatch

`SurvivalAcceptanceScenario` currently fixes seed 1886, speed 10x, and target duration 15 days. Its evaluator returns a configuration-mismatch / blocked result when passed a 60-day 250x run. The planned certification needs either a separately recorded 15-day 10x Scenario Lab acceptance run, or an explicitly approved evaluator change that scores the first 15 days of the 60-day artifact while retaining its real 250x metadata. No such change or reinterpretation was made. This is an additional verification-tool blocker, not a gameplay result.

## Spillover decision awaiting owner

The two source-documented fixtures are `CrossSystemInterplayRuntimeTests.TownPulseUnmetHardwareDemandSpillsIntoBlacksmith` and `CrossSystemInterplayRuntimeTests.TownPulseMeatDemandCanSpillIntoButcher`. They assert legacy spillover sales (3 hardware units, including two from the blacksmith; and 2 meat units from the butcher). A source comment records that the current Pass VII “no buyer, no sale” rule instead makes `TrySellTownPulseDemand` return zero. This is documented as pre-existing, not a Campaign 06 regression. The owner must decide whether town-pulse unmet demand may purchase from the secondary business or whether those legacy expected values should be retired. No acceptance expectation was changed.

## Health verdicts

No current-source simulation data exists for this campaign. Therefore none of the outcomes below are passed by calendar completion or inferred from historical runs.

| Measure | Result |
|---|---|
| Calendar completion | NOT EXECUTED |
| Unity stability | BLOCKED |
| Task liveness | NOT EXECUTED |
| Physical movement | NOT EXECUTED |
| Household survival | NOT EXECUTED |
| Shopping and retail | NOT EXECUTED |
| Business operation | NOT EXECUTED |
| Production | NOT EXECUTED |
| Freight | NOT EXECUTED |
| Inventory conservation | NOT EXECUTED |
| Money conservation | NOT EXECUTED |
| Heating | NOT EXECUTED |
| Save/Continue | NOT EXECUTED |
| Diagnostics | BLOCKED — source wiring only; no current live incident/tool evidence |
| Performance | BLOCKED — no diagnostic-overhead measurement; machine commit pressure is high |

Economic sustainability for 60 days: **NOT ESTABLISHED**.

## Recheck — 2026-11-21, before any current-source Unity execution

The active Unity Editor is PID 23156 and is attached to the owner's separate `F:\Land & Ledgers` checkout. Its two AssetImportWorker processes (PIDs 15504 and 30572) also belong to that project. The integration checkout remains at the HEAD above with a dirty worktree. I did not open another Editor, interrupt the owner's Editor/workers, or run a batch test.

At this recheck, available physical memory was approximately 3,927 MB of 16,238 MB visible, and system commit was 51,893,669,888 of 54,466,043,904 bytes (about 95.3% used; about 2,385 MB headroom). The owner Editor held about 24,495 MB private memory; its two asset workers held about 4,527 MB and 4,092 MB. This confirms that importing/compiling the separate dirty integration project and then running a 60-day campaign is not a safe action in the current shared session. No Campaign 06 run was started; the historical artifacts remain untouched.

This recheck does not change any gate to PASS. Current-source compile, focused Unity regressions, smoke test, 10x evidence run, and long run remain unexecuted. The specific pending owner decisions remain P-1–P-8 acceptance thresholds and town-pulse spillover behavior. Recheck resources only after a real state change (owner Editor/asset processing safely closed, or materially improved memory/commit headroom); then resume at the first blocked gate, not by repeating already passing unrelated tests.

## Historical context and register

The Muse-reported 60-day run (6,317 warnings, 885 anomalies, zero errors) and retained Codex 5-day run are incomparable source states. They are retained as historical context only; no warning-reduction percentage or regression claim is made. No new run-specific warning signatures, entities, or outcomes could be recorded.

The existing [Console Investigation Checklist](../Diagnostics/CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md) remains authoritative and was not cleared or reclassified. Campaign 05's three new silent diagnostic findings remain `IMPLEMENTED, UNVERIFIED`. The current issue register still needs the missing original Muse 90-DEF register supplied or transparently reconstructed; this report does not claim to replace it.

## Next safe sequence

1. When the owner Editor and asset processing are safely closed or memory headroom recovers, open the exact integration checkout on Unity 6000.4.2f1, compile current source, and run only the blocked required suites first.
2. Run a short current-source Main Scene smoke test, then a fresh 10x evidence run. Compare its complete Console and task/economic evidence before attempting the long campaign.
3. Resolve the Scenario Lab's 15-day/10x versus 60-day/250x evidence policy without falsifying metadata, and obtain owner decisions on P-1–P-8 and town-pulse spillover.
4. Recheck memory/commit headroom immediately before long-run launch. Preserve every partial or failed run and capture all required daily snapshots.
