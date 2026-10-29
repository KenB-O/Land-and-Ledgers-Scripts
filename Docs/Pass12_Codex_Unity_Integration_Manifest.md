# Pass XII — Codex Unity Integration Manifest

This manifest is for the separately authorized Codex session. Muse did NOT
perform any of the steps below, did NOT modify the real Unity project, and
claims NO Unity compilation, Test Runner, PlayMode, or scene-wiring result.

## 1. Exact source to integrate

- Branch: `muse/continuous-development`
- Final commit: see `Docs/Pass12_Final_Source_Handoff.md` (full 40-character SHA,
  bundle, checksum, link, expiry, remote status).
- Ancestry: the branch descends from the last verified Unity Scripts baseline
  `6d4186003d288e8a203d81174875a22613419f24`
  (`integration/pass6-living-settlement-gameplay`) through Pass IX
  (`bb2e4bc`), Pass X (`cc59bec`), and Pass XI (`02aca56`). Verify this
  ancestry yourself with `git merge-base --is-ancestor` before integrating;
  do not assume the Unity project has already received Pass XI or XII.

## 2. Pull strategy (in order)

1. Verify the actual Unity project and Scripts checkout again (branch, HEAD,
   clean vs dirty tree) — the last verified state was
   `integration/pass6-living-settlement-gameplay` @ `6d41860`, clean, no
   uncommitted/untracked/local-only changes, Unity 6000.4.2f1, HDRP.
2. Back up the COMPLETE Unity project first: scenes, prefabs, terrain, assets,
   ProjectSettings — not just Scripts.
3. Verify the final Muse branch and commit from the manifest/handoff.
4. Confirm the branch descends from the active Unity Scripts source.
5. Preserve any newer local Unity changes (do not overwrite them silently).
6. Fetch the complete source history.
7. Integrate the final source WITHOUT force-resetting or replacing the Unity
   project.
8. Preserve source and asset `.meta` identities: existing GUIDs unchanged;
   new files arrive with fresh GUIDs (listed in §3). Never regenerate or copy
   a `.meta`.
9. Compile the full project, including the four harness-excluded files and all
   Pass XII edits (see §4).
10. Correct legitimate Unity integration errors only (no gameplay redesign).
11. Run the Unity Test Runner, including the M1/M2 bootstrap tests (PASS11-008).
12. Author and repair the scene components in §5.
13. Validate existing assets and Inspector references.
14. Run meaningful PlayMode tests, culminating in the RUN-004 Main Scene 30-day
    autonomous proof.
15. Review player-facing presentation against the LL-PLAY acceptance tests.
16. Produce an evidence-backed readiness report (gates in
    `Docs/Pass12_Pre_Playtest_Acceptance_Checklist.md`).

## 3. Source changes since the Unity baseline (summary)

Pass XII diff vs Pass XI (`02aca56`): 123 files total across workers + review
(80 worker files + 43 review files, with overlap in the count — the
authoritative diff is the final branch vs `02aca56`; use
`git diff --stat 02aca56..<final>` in the session). Categories:

- New production code: `SingleInstanceAuthorityGuard`, payroll exactly-once
  guards + manual payment records (in `BusinessRuntimeState`), settled-sale
  stock refusal, copy-on-load clones (load methods in HouseholdLedger,
  EmploymentRelationship, FinancialObligationAuthority, MoneyOrderService,
  BankDeposits, BusinessLenderService, Housing, TaskAuthority, Construction
  executor, CreditWorkflow, ResidenceCondition, and others listed in the
  Independent Review), bootstrapper suppression + duplicate census,
  scenario DTO `startingActivationIssued`, manual wage DTOs.
- New read models / contracts (source only, scene binding still required):
  `ScenarioHudReadModel`, `ScenarioHudSnapshotBuilder`, `ScenarioGuidanceGate`,
  `AcquisitionDealReadModel`, `PanelModalCoordinator`, `StartupNoticeGate`,
  `SettlementCivicReadModel`, `ResourceObservationReadModel`,
  `SettlementCausalExplanation`, `PersonInspectionReadModel`,
  `WorkloadStaffingReadModel`, report builders/DTOs (rent/tax categories,
  Yearly/AllTime rollups).
- Changed defaults (source only — scene instances keep stored values, so
  Codex MUST audit scene instances): `BusinessInstanceState` owner default
  Player → Town; `GeneralStoreRuntimeManager.createPlayerBusinessOnInitialize`
  default true → false.
- New tests (all Roslyn-harness verified; Unity Test Runner still required):
  A 28, C 33, B 4, D 22, E 20, F 19, plus the 3 RUN-011 corrections.
- Docs: Pass XII matrix, player-experience contracts, playtest reconciliation,
  independent review, final regression results, deferred owner decisions, and
  this manifest + checklist + handoff.
- New `.meta` GUIDs: every new `.cs`/`.md` file above ships with a fresh GUID
  in its companion `.meta`. Existing `.meta` GUIDs are unchanged.
- Serialized compatibility: no class/struct/enum/field renames; enums
  append-only (`ReportPeriodKind` gained Yearly=2, AllTime=3, appended).
  Save/load migrations are additive: `startingActivationIssued` (old saves
  read false → one legacy re-issue, then true), `manualWagePayments`
  (absent on old saves → empty), construction ExecutorSequence and freight
  dispatch lists (Pass XI, same pattern).

## 4. Compilation gates

- Full-project compile including the harness-excluded files: `SimulationDrivers`,
  `SharedBusinessRuntimeManager` (+ partials), `SchoolhouseModels`,
  `SimulationSystemsHub` (PASS9-014, narrowed in Pass XI, still open for Unity),
  plus all Pass XII edits to those files and to `FirstSessionGuidanceManager`
  and `GeneralStorePanelController` (harness-excluded; Pass XII edits there are
  inspection-only and MUST be Unity-compiled).
- Boarding/hotel reservation clones (F-02) are outside the harness subset —
  Unity-compile and verify them explicitly.
- The harness `sources.txt` additions are a Muse-side test-selection artifact,
  not a Unity requirement; do not copy them into the Unity project.

## 5. Scene authoring requirements (exact, in dependency order)

Do not invent scene hierarchy paths not inspected. Where a GameObject/prefab
name is unknown, the responsible script is named and the inspection is stated.

1. **Main Scene bootstrap**: author persistent configurable managers in the
   scene (SystemsHub, Drivers, CivicFoundation, StoreRuntime, SharedBusiness,
   SaveLoad, TimeManager, Population, TownWorld) vs legitimately transient
   objects (dev-only validation harness, EventSystem fallback). The
   bootstrapper's duplicate census logs errors naming duplicates on Start and
   after restore — use it as your check.
2. **Single authoritative SystemsHub + single active SimulationDrivers**:
   Drivers now refuses a second authority claim loudly; Hub/Civic/Director/
   Bootstrapper duplicates are logged, not suppressed — remove extras.
3. **Duplicate Civic Foundation (LL-PLAY-003)**: remove the extra Civic
   Foundation GameObject; `CivicFoundationManager` has no Instance guard, so
   scene hygiene is the fix; verify exactly one operates and civic readouts
   are stable.
4. **TimeManager**: do NOT implement the simulation-time proposal (1:1, 5 min/day
   at 1x, etc.). It remains a proposal; preserve the Pass XI test/Codex plan
   in `Docs/Pass11_Simulation_Time_Review.md` and the Pass XII decision doc.
5. **Scenario director + start panel**: bind the scenario start panel to the
   director; bind `FirstSessionGuidanceManager.ScenarioContextId` from
   `ScenarioCoordinator.ActiveScenarioId`; wire `ScenarioFailureNoticeBoard`
   and `ScenarioObjectivePresentation` into the panel (PASS11-006) via the
   `ScenarioHudSnapshotBuilder` contract (D's doc gives field-by-field bindings).
   CalendarAnchor: verify the scene anchor matches `DaysBeforeAnchorYear` 3652.
6. **Scenario HUD (LL-PLAY-005)**: author/bind the HUD panel from
   `ScenarioHudReadModel` (identity, objectives, progress, deadlines,
   completion, failure, Sandbox clean state); screenshot live/failed/complete/
   Sandbox states.
7. **Player-owned bank binding**: in the scene, bind the player-owned bank's
   ledger EXACTLY ONCE via `SimulationDrivers.BindBankLedger(businessInstanceId,
   ledger, diag)` → `SimulationSystemsHub.BankLedgerBindings`; never both
   ledgers for one bank; first bind wins, second distinct bind refused
   fail-loud; underwriting must call `TryReadDepositPosition` immediately
   before deciding. Do NOT wire BankRuntime into underwriting.
8. **Freight company runtime**: verify the freight runtime is present and
   driven through `SimulationDrivers` (Pass IX driver), not a second instance.
9. **Boarding-house parcel mapping (PASS11-004, still open)**: do NOT invent a
   mapping. The missing inputs are: a production adapter mapping
   `BusinessInstanceState.AssignedBuildingId` (int index into town buildings)
   to the housing parcel-id string, a boarding trigger rule, and the building
   identity linking the bridge's synthesized housing id to the world
   `PlacedBuilding`. Resolve with the owner if needed; until then boarding
   bed capacity (via `BoardingHouseShopRuntime`) is the live path and
   `RegisterHouse`/`CheckIn` remain ungated-but-refusing.
10. **NPC construction registration**: verify NPC construction requests flow
    through `NpcConstructionDriver` with real supplier inventory/custody; do
    not add a second construction driver.
11. **Person selection / Business inspection / Household inspection / Property
    interaction**: bind D's read models (`PersonInspectionReadModel` with its
    per-field visibility rules; `WorkloadStaffingReadModel` for staffing —
    display demand/assigned/coverage/unfilled shifts + Explanation, never a
    bare slot fraction; causal explanations with event citations). Clicking,
    selection, camera, and HUD presentation are Codex work.
12. **General Store presentation**: rebind startup notices through
    `StartupNoticeGate` precedence (ghost → debug → premature-init → tutorial
    remnant → show); legitimate blockers stay visible; audit the 3 civic
    strings in `GeneralStorePanelController` in-scene.
13. **Acquisition interface**: rewire the panel to `AcquisitionDealReadModel`
    (one next legitimate action + five distinct stages + provenance per
    opportunity; Unknown provenance rendered verbatim, never upgraded).
14. **Construction interface**: build the six-stage interface (land → design →
    construct → acquire premises → establish → equip/operate) per B's contract;
    keep the 10 PARTIAL business types out of activation UI (or label them
    not-yet-operating); label the 17 activation options by actual mechanism.
15. **Management panels**: fix RectTransform layout + scroll/top alignment
    (LL-PLAY-006, visual only — screenshots required); single modal-coordinator
    instance + Canvas sortingOrder + EventSystem focus return (LL-PLAY-007).
16. **Civic and Resources panels**: Civic 3-state + Resources displays per D's
    models (no invented institutions; resources as generated prospectivity with
    confidence/units/ownership-uncertainty, never hidden reserves as fact).
17. **Terrain/navigation integration (LL-PLAY-016, confirmed unresolved)**:
    bounded plan only — preserve existing terrain assets, parcel coordinates,
    building anchors, navigation, freight routes, deterministic world state,
    and scene references. Do NOT generate a competing terrain authority or
    destructively rebuild the world.
18. **Audio manager lifetime (LL-PLAY-019, confirmed unresolved as a scene
    question)**: verify exactly one `LLMainMusicPlayer` under
    `DontDestroyOnLoad`, Instance non-null, one AudioSource pair playing;
    two scene objects → the later-Awake one self-destroys (execution order
    −9000 runs first). Its Play Mode disappearance from normal roots is
    expected when persistent.

## 6. Tests to run (Unity side)

- Full Unity Test Runner suite.
- M1/M2 bootstrap tests (`Tests/Editor/Scenarios/Pass11Ws6FirstGateBootstrapTests.cs`,
  PASS11-008) — delivered, never run.
- RUN-004 Main Scene 30-day autonomous PlayMode proof (deadline failure must
  stay failed; see §7).
- Old-save loading checks: absent qualifiers, fee fields, ExecutorSequence,
  dispatch lists — each has a defined fallback (see Pass XI + Pass XII docs).
- Scene-instance audit: stored Player owners and serialized true flags
  (defaults changed at source; stored scene values win until audited).

## 7. Known failures / limitations at handoff (do not re-litigate as surprises)

- Final Roslyn harness: 1533/0 (see regression doc). Unity Test Runner results
  are UNKNOWN until Codex runs them.
- RUN-004, RUN-001, RUN-005, RUN-006, RUN-009, RUN-010, RUN-012, PASS9-012,
  PASS9-014, PASS11-006, PASS11-008, and the LL-PLAY set remain Unity-open.
- Capture-side DTO sharing (F-RES-08) is open: in-memory captured snapshots
  share live objects; disk loads are unaffected.
- Direct double-run of `ResolveWeeklyPayroll`, WageSettlementService replay
  read-model inaccuracy, Generic baseline loop (owner call), channel substring
  sniffing, and COGS/reorder producer contract are documented residuals.
- Owner decisions still required: G1 (RealDemandSource), G2 (Class E),
  G3 (foreclosure/household lending), A-14 (late-latch semantics),
  simulation-time proposal, bank capacity consolidation, related-party policy.
