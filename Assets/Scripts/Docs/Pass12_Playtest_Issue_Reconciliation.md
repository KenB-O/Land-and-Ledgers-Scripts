# Pass XII — October 9 Playtest Issue Reconciliation (Worker F)

Date: 2026-10-28. Baseline: Pass XI final `02aca56`. Integrated Pass XII
source reviewed: `eafe29b` plus Worker F fixes on `muse/pass12-f-review`.

Disposition taxonomy (Pass XII brief §5, verbatim labels):
- Source defect fixed
- Source contract ready; Unity authoring needed
- Already implemented, source verified; Unity validation pending
- Deferred by owner policy
- Confirmed unresolved

Honesty rule applied throughout: no October 9 observation was made outside
Unity, so no item is called fully fixed while its confirmation step is a
Unity scene check. That is why no item below carries "Source defect
fixed": every source fix here still needs its Unity confirmation, and
saying otherwise would repeat the exact error the brief forbids. Items
whose source work is complete and harness-tested carry "Already
implemented, source verified; Unity validation pending"; items whose
deliverable is a binding contract carry "Source contract ready".

## LL-PLAY-001 — Ghost player-owned Businesses — Already implemented, source verified; Unity validation pending

Observed Oct 9: a clean game displayed player-owned General Store (and
possibly Renting & Boarding) entries the player had not acquired.
Source defects found and fixed in Pass XII:
- `Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`: the store
  runtime's auto-create flag was cleared only when a ScenarioDirector
  existed, so a director-less scene auto-formed `player_general_store`
  on the store runtime's Start and flipped an NPC building to
  player-owned. Worker A now clears it unconditionally for every
  startup intent before any runtime Start (bootstrapper execution
  order 240, store runtime 260 — both set by attribute).
- Worker F (M2): `Domains/Economy/Core/BusinessInstanceState.cs` — the
  serialized owner field initialized to `Player()`, so any state
  materialized without `Create`/`FromSaveDto` read as player-owned.
  The default is now `Town()`; every production creation path sets the
  owner explicitly (`Create` resolves null to Town, `FromSaveDto`
  resolves a missing owner DTO to Town, `SetOwner(null)` to Town).
  Enum order unchanged. Tests: `Pass12BusinessOwnershipTests` (8/0),
  `Pass12FDefaultOwnershipTests` (4/0).
- Worker F: `Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs`
  — `createPlayerBusinessOnInitialize` default flipped true to false.
  The explicit player-owned-building formation path is unaffected
  (it requires a player-owned building plus site fit). This file is
  outside the Roslyn harness subset, so this half is Unity-verified
  only, not harness verified.
Codex action: audit scene instances for a stored Player owner or a
serialized true auto-create flag (already-serialized values survive
both default flips); start a clean New Game and screenshot the
player portfolio showing no player business before any formation.
Acceptance: clean New Game portfolio is empty of player businesses.

## LL-PLAY-002 — Runtime-only root managers — Source contract ready; Unity authoring needed

Observed Oct 9: numerous persistent-looking managers appeared only
during Play Mode. Source work (Worker A):
- `Orchestration/Bootstrap/SingleInstanceAuthorityGuard.cs` (pure C#,
  harness-tested): process-wide claim registry; a second
  `SimulationDrivers` instance never holds the claim, never subscribes
  to the clock, never wires a business runtime. Claims release on
  disable/destroy; `ResetAll` is test-only and never called in
  production.
- `SimulationDrivers` claims in Awake, re-claims on enable, refuses
  `AttachBusinessRuntime` and `Rebind` when suppressed. This file is
  harness-excluded, so the drivers-side claim wiring is Unity-verified
  only; the guard semantics themselves are harness-tested
  (`Pass12BootstrapAuthorityTests`).
- `FirstLedgerSliceBootstrapper.ReportDuplicateSceneAuthorities`
  audits SimulationSystemsHub, SimulationDrivers, CivicFoundation,
  ScenarioDirector and the bootstrapper itself, logging an error
  naming every duplicate. Detection only — it does not remove objects.
Codex action: author persistent configurable managers in the scene
where appropriate; remove duplicate GameObjects the audit names;
keep genuinely transient entities runtime-created. Do not remove
runtime creation indiscriminately. Acceptance: Edit Mode hierarchy
shows the authored managers; Play Mode adds no unexpected persistent
roots and logs no duplicate-authority error.

## LL-PLAY-003 — Duplicate Civic Foundation — Source contract ready; Unity authoring needed

Observed Oct 9: two Civic Foundation instances during Play Mode.
Source work: the duplicate audit above includes CivicFoundation, and
the guard registry accepts a CivicFoundation claim — but no
MonoBehaviour claims it yet, so a duplicate civic foundation is
DETECTED, not suppressed. Worker F verified the audit counts live
scene instances (including inactive) and names each object.
Codex action: inspect the Main Scene hierarchy, determine whether the
two instances are a genuinely duplicated manager or legitimately
separate civic entities, keep exactly one authoritative foundation
where the design requires one, and remove the other. Acceptance: one
civic foundation operates; the audit logs no duplicate; civic effects
are applied once (no double uplift).

## LL-PLAY-004 — Obsolete First Session Guidance — Source contract ready; Unity authoring needed

Observed Oct 9: guidance such as "Hire your first store worker" that
did not fit the selected scenario. Source work (Worker D):
`Orchestration/Scenarios/Generation/ScenarioGuidanceGate.cs` — guidance
is shown only when it matches authoritative scenario state; obsolete
tutorial lines are demoted/suppressed where they compete with real
scenario objectives. Harness-tested (`Pass12ScenarioPresentationTests`).
Codex action: bind the gate to the guidance panel; verify per
scenario that no obsolete line appears. Acceptance: guidance shown
always reflects the selected scenario's objectives.

## LL-PLAY-005 — Missing scenario HUD — Source contract ready; Unity authoring needed

Observed Oct 9: scenario, objectives, progress and deadlines were not
clearly visible. Source work: Pass XI scenario start/evaluation,
dated qualifiers and completion/failure latching, plus Worker D's
`ReadModels/Scenario/ScenarioHudReadModel.cs` and
`Orchestration/Scenarios/Generation/ScenarioHudSnapshotBuilder.cs`
(selected scenario identity, authentic objectives, actual progress,
deadlines, completion and failure status, clean Sandbox model with no
objectives and no failure state). The snapshot builder consumes a
`ScenarioFailureNoticeBoard` when supplied. Harness-tested.
Codex action: build the HUD panel from the snapshot builder; wire the
failure board into the live panel (PASS11-006, still open as scene
work); verify Sandbox shows the clean model. Acceptance: HUD shows the
correct scenario, objectives, progress, deadlines and failure state
for a started scenario and the clean Sandbox model otherwise.

## LL-PLAY-006 — Management panel layout — Confirmed unresolved

Observed Oct 9: the Player Blacksmith/management panel began too low
with substantial unused space; scroll area and top alignment were
unsatisfactory. Pass XII delivered no layout change: Worker D's panel
contract (LL-PLAY-007) covers modal ownership, sorting, focus and
close behavior, not panel position, scroll rect or alignment. No
source component positions the panel.
Codex action: Unity layout work with screenshots (panel top
alignment, scroll area, unused space) against the live management
panel. Acceptance: screenshots show the panel correctly aligned with
a usable scroll area. No source claim is made for this item.

## LL-PLAY-007 — Seller meeting layering — Source contract ready; Unity authoring needed

Observed Oct 9: seller meetings appeared behind or alongside the
management panel. Source work (Worker D):
`Presentation/UI/PanelModalCoordinator.cs` — coherent modal ownership,
sorting, focus and close behavior for panels (registered in
`Docs/UnwiredSystems.md` as a scene-binding-pending contract; it has
no production caller until Codex binds it). Harness-tested.
Codex action: bind the coordinator to the management panel and the
seller-meeting surface; verify Canvas sorting and EventSystem focus.
Acceptance: a seller meeting opens above the management panel, takes
focus, and closes cleanly without stranding the panel.

## LL-PLAY-008 — Acquisition button clutter — Source contract ready; Unity authoring needed

Observed Oct 9: a confusing sequence (Open Inquiry, Commit Earnest,
Continue Diligence repeatedly, Agree Terms, Close Acquisition) with no
clear next legitimate action. Source work (Worker D):
`Domains/Economy/Acquisitions/Presentation/AcquisitionDealReadModel.cs`
— the next legitimate action, accessible deal details, and the deal's
current legal/financial state, without collapsing separate states:
financing, diligence, agreements, payments and title transfer remain
real and sequential. Harness-tested.
Codex action: rebuild the acquisition interface on the read model so
exactly one next action is prominent and deal details are reachable.
Acceptance: a player can follow a deal from inquiry to close with the
next action always clear, and no required step is bypassable from the
UI. No transaction step was shortened in source.

## LL-PLAY-009 — Business-for-sale information provenance — Source contract ready; Unity authoring needed

Observed Oct 9: opportunities such as a Doctor's Business for sale
with no explanation of how the player knew. Source work (Worker D):
the acquisition read model carries a provenance label per opportunity
and renders "Source not established — treat details as unverified"
when no legitimate channel (seller, public listing, newspaper,
advertisement, broker, conversation) is recorded, plus a market-context
label ("not recorded" when absent). Never manufactures privileged
information. Harness-tested.
Codex action: display the provenance label on every opportunity in
the acquisition interface; audit the live opportunity feed and confirm
every listed opportunity carries a real source label or the explicit
unverified marker. Acceptance: no opportunity appears without its
provenance shown.

## LL-PLAY-010 — Obsolete construction controls — Source contract ready; Unity authoring needed

Observed Oct 9: controls such as Build Shell, Next Shell, Previous
Business, with no meaningful distinction between owning land,
designing, constructing, acquiring premises, establishing a business,
and equipping/operating it. Source work (Worker B, matrix §10): a
generic construction/formation interface contract distinguishing
those stages, grounded in the design catalog, parcel authority and
building/premises relationship; NPC construction runs through the
production driver (one step/day, site stockpile as freight receiving
authority). No menu-spawned building replaces real construction.
Codex action: build the generic construction/formation interface on
that contract; remove or demote the obsolete shell controls.
Acceptance: each stage (land, design, construction, premises,
formation, equipping) is separately reachable and honestly gated on
materials, labor and title.

## LL-PLAY-011 — Approximately sixteen rigid Business options — Source contract ready; Unity authoring needed

Observed Oct 9: roughly sixteen Business labels with unclear depth.
Source work (Worker B, `Docs/Pass12_Business_Completeness_Matrix.md`):
exact enumeration from source — `BusinessType` has 28 values plus
`Generic = 255`; the October 9 count resolves to
`AcquisitionMarketManager.ActivationBusinessTypes` (17 entries), while
the creation panel offers only `Generic` plus three templates, and
behind the labels are four live mechanisms, not 17 distinct
simulations. Classification: 19 LIVE (tick executes production and
settles through real counterparties — the original §1 table listed
18 and omitted Mine, corrected by Worker F in round 2; §4 already
had Mine as LIVE), 10 PARTIAL (weekly settlement/
payroll runs but the trade has no tick production operation), 0
source-only as a whole type, 0 intentionally dormant, Lawyer
unsupported as a trade model (services exist, no runtime, no
production operation). Total: 29 enum members (28 typed + Generic). The 13 registry runtimes are re-verified
unwired; Worker F added ButcherRuntime, BlacksmithRuntime and
TanneryRuntime to the registry (16 runtimes total), each with no
production instance ever created. Bakery and smithy live-path proofs
are harness-tested; no runtime was wired merely to shrink the list.
Codex action: present Business identity from actual premises,
equipment, labor, production methods, inventory, skills, agreements
and demand; do NOT present the 17 labels as 17 distinct simulation
depths; flag unfinished choices as not fully functional in the
interface. Acceptance: the interface's Business list matches the
matrix's LIVE/PARTIAL/unsupported classification.

## LL-PLAY-012 — Unclear staffing — Source contract ready; Unity authoring needed

Observed Oct 9: figures such as "Staff 1/2" with no explanation of
what the number meant or why the business needed that staffing.
Source work (Worker C): `ReadModels/Reporting/WorkloadStaffingReadModel.cs`
— real worker assignments, task workload, available Person-hours,
required skills, schedule conflicts, station/equipment constraints,
labor demand, unfilled shifts and wage obligations, with FTE scale
and vacancy clarified (no blanket quotas). Harness-tested
(`Pass12CWorkloadStaffingTests`). The read model is classified
feature-internal in the assembly scan (consumed by builders and
presentation), not a standalone unwired system.
Codex action: replace the "Staff x/y" display with the workload
read model (demand, filled, unfilled, skills, constraints).
Acceptance: every staffing figure on screen traces to a workload
input the player can inspect.

## LL-PLAY-013 — Implausible financial reports — Already implemented, source verified; Unity validation pending

Observed Oct 9: confusing Today/Week/Month/Year/All-Time profits,
including suspiciously rapid gains. Source work (Worker C): report
aggregation integrity — revenue from real customers and
counterparties only, cash movement separated from operating profit,
financing inflows never counted as operating revenue, inventory
purchases, labor, rent and tax bridged, yearly/all-time rollups built
from monthly summaries (no double-counting across periods), and the
settled-sale stock refusal (E-DEFECT-01) closing a phantom-revenue
hole where a sale larger than stock minted revenue for undelivered
goods. Harness-tested (`Pass12CReportingIntegrityTests`,
`Pass12CReportingExtensionTests`, `Pass12CSettledSaleStockTests`).
Codex action: display the corrected reports and compare live panel
figures against ledger events for a played week. Acceptance: no
period shows profit inconsistent with the ledger; loan proceeds never
appear as operating revenue.

## LL-PLAY-014 — Unhelpful Civic tab — Source contract ready; Unity authoring needed

Observed Oct 9: "Civic count hall not built" told the player nothing.
Source work (Worker D): `ReadModels/Civic/SettlementCivicReadModel.cs`
— when no town hall exists the honest content is no civic uplift
(all four effects +0%) with town confidence private-market led; no
public institution is invented, and the focus action is simply
unavailable rather than a blocker message. The live panel's title and
summary text were changed the same way ("Not Built" to "no town hall
yet") in `GeneralStorePanelController` — that file is outside the
Roslyn harness subset, so the panel text change itself is
Unity-verified only; the read model is harness-tested. The read model
is registered in `Docs/UnwiredSystems.md` as scene-binding-pending.
Codex action: bind the read model to the Civic tab; verify the
not-built case shows useful settlement information, not a blocker.
Acceptance: Civic tab is informative with and without a town hall.

## LL-PLAY-015 — Confusing Resources tab — Source contract ready; Unity authoring needed

Observed Oct 9: resource information such as coal, two districts and
72% without explanation. Source work (Worker D):
`ReadModels/Civic/ResourceObservationReadModel.cs` — actual known
reserves or observations distinguished from surveys, with
confidence, units, spatial context, accessibility, ownership/mineral
rights and uncertainty. No omniscient hidden-resource information is
revealed merely to populate the menu. Harness-tested. Registered in
`Docs/UnwiredSystems.md` as scene-binding-pending.
Codex action: bind the read model to the Resources tab; verify every
figure shows its basis (observed vs surveyed, confidence, units).
Acceptance: no resource figure appears without its provenance and
uncertainty.

## LL-PLAY-016 — Completely flat terrain — Confirmed unresolved

Observed Oct 9: the Main Scene appeared unnaturally flat. Pass XII
delivered no terrain source change and no world-generation contract
change: no worker diff touched terrain, elevation, drainage, roads,
settlement placement or parcel coordinates. A bounded Codex plan was
not produced in source because no worker audited the
world-generation/authored-Terrain integration contracts in this pass.
Codex action (for the integration session, not claimed as done):
inventory existing terrain assets, parcel coordinates, building
anchors, navigation and freight routes; author plausible elevation,
drainage, roads and settlement placement preserving all of them and
deterministic world state; do not generate a competing terrain
authority or destructively rebuild the world. Acceptance:
screenshots show varied terrain with parcels, anchors, navigation
and freight routes intact. No source claim is made for this item.

## LL-PLAY-017 — Calendar/weekday inconsistency — Already implemented, source verified; Unity validation pending

Observed Oct 9: historical dates and weekdays may not have agreed.
Source state: Gregorian date conversion, leap years including the
non-leap year 1900, anchor offsets, day rollover, scenario dates,
save/load and HUD read models were verified in Pass IX/XI; Pass XII
changed nothing in the calendar path, and the Pass XII full harness
(including calendar and scenario suites) passes on the final tree.
No Pass XII source defect was found here.
Codex action: display a known historical date in the Main Scene HUD
and confirm the weekday matches Gregorian mathematics. Acceptance:
displayed date and weekday agree for spot-checked historical dates.

## LL-PLAY-018 — Confusing General Store startup notices — Source contract ready; Unity authoring needed

Observed Oct 9: startup messages about unavailable items, restocking,
cashiers or visibility that did not seem relevant. Source work:
Worker A's ghost-ownership suppression (LL-PLAY-001) removes the
ghost-business notice source; Worker D's
`Presentation/UI/StartupNoticeGate.cs` filters premature-initialization
fallbacks, tutorial remnants, debug display and ghost-business
notices while preserving legitimate live-business conditions and
actual economic failures (nothing concealed). Harness-tested.
Codex action: inspect the live startup notice list on a clean New
Game and on a loaded save; confirm no misleading notice survives and
no real failure is hidden. Acceptance: startup notices on a clean
game are only legitimate live conditions.

## LL-PLAY-019 — MainMusicPlayer lifecycle — Confirmed unresolved

Observed Oct 9: MainMusicPlayer disappeared from normal Edit Mode
roots during Play Mode. Pass XII delivered no source work on this
item: `Presentation/Audio/LLMainMusicPlayer.cs` (singleton instance,
recovery logging, AudioListener check) is untouched by the Pass XII
diff, and no worker traced its DontDestroyOnLoad, scene-transition,
duplicate-guard or destruction behavior. Do not assume migration to a
persistent scene is itself a defect.
Codex action: trace the player in Edit vs Play hierarchies; check
DontDestroyOnLoad handling, scene transitions, duplicate-instance
guards and intentional destruction; correct only if the trace shows
an actual defect. Acceptance: hierarchy inspection shows the audio
manager's lifecycle is intentional and music survives scene
transitions as designed, or the defect is fixed with evidence.
No source claim is made for this item.
