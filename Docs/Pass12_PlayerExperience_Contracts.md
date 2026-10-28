# Pass XII Worker D - Player-Experience Source Contracts

Scope: LL-PLAY-004/005/006/007/008/009/014/015/018, brief 6.3 (causal
reporting), brief 6.4 (person inspection), PASS11-006 (scenario failure
presentation). Source only. Nothing in this document claims Unity
compilation, Test Runner success, PlayMode success, or scene wiring.
The live scene panel is NOT wired by Worker D. Codex must still do
every Unity-side step listed below.

## 1. Scenario HUD contract (LL-PLAY-005, PASS11-006)

Source model: `ReadModels/Scenario/ScenarioHudReadModel.cs`
(namespace `LandLedgers.Reporting`), populated by exactly one adapter:
`Orchestration/Scenarios/Generation/ScenarioHudSnapshotBuilder.cs`.
The adapter reads the headline from `ScenarioObjectiveService`, the
latched set from the persisted run state, and failure notices from
`ScenarioFailureNoticeBoard`. It never re-evaluates predicates and
never invents objectives. Goal display texts come from the caller
(the scenario director/preset), because the objective service holds
qualifiers, not display text.

Field-by-field panel binding (Codex):

- Panel title: `Title`. When `HasActiveScenario` is false, show
  `EmptyStateText` ("Sandbox - no scenario objectives...") and hide
  the objectives list, the deadline column, and any failure banner.
- Status line: `StatusText` ("In progress", "Complete", "Failed",
  or "No scenario active"). Never derive status from the goal list
  alone; a failed run must not render as merely unfinished.
- Progress line: `CompletionText` ("2 of 5 objectives complete").
- One row per `Goals` entry: checkmark when `IsLatched`, empty box
  otherwise; `Text`; `DeadlineText` when non-empty (empty means no
  deadline - do not render "No deadline" as a deadline). A row with
  `IsFailed` true renders in the failure style AND the banner below
  still shows; a row with `IsOverdue` true renders overdue.
- Failure banner: `FailureBanner`, shown only when non-empty. It
  names the failed goal and its deadline day.
- Failure notices: one dismissible notice per `FailureNotices`
  entry, using `DisplayText`. Notices are deduplicated by
  scenario+goal at the board. When the run has no active scenario,
  notices are dropped by the model - a stale board from a previous
  run must never paint failure onto Sandbox. Codex must still clear
  the board on New Game (call `ScenarioFailureNoticeBoard.Clear()`
  in the New Game path) so stale notices do not accumulate.
- Refresh cadence: rebuild the model on every HUD refresh from the
  scene's live `ScenarioObjectiveService` + board instances. Do not
  cache a built model across days; deadlines and latch state change.

Pinned semantics (A-14 UNCHANGED): a latched goal stays complete
even if its predicate later turns false; an unlatched goal past its
deadline stays failed; failure is sticky for the rest of the run.
Deadline text boundary matches `Evaluate` exactly: the deadline day
itself is "Due today", never "Overdue".

What Codex must still do: author/rewire the scenario HUD panel
itself, bind the fields above, verify the panel in the Main Scene
with a real scenario (Saloon Circuit timed goal), and screenshot
the HUD in live, failed, complete, and Sandbox states. Source tests
proving the model are in
`Tests/Editor/Orchestration/Pass12ScenarioPresentationTests.cs`.

## 2. First-session guidance gate (LL-PLAY-004)

Audit finding: `FirstSessionGuidanceManager` ran its First Ledger
loop unconditionally whenever guidance was enabled. It never
consulted the active scenario, so a non-First-Ledger scenario still
showed steps such as "Hire your first store worker".

Source fix: `ScenarioGuidanceGate.ShouldShowFirstSessionGuidance`
in `Orchestration/Scenarios/Generation/ScenarioGuidanceGate.cs`.
Show only when the active scenario id is empty (legacy fail-open:
no scenario context bound yet) or exactly "the-first-ledger".
Suppress for every other active scenario id; the scenario HUD is
authoritative there. The manager consults the gate in
`RefreshGuidance` via its new `ScenarioContextId` property
(`Orchestration/Guidance/FirstSessionGuidanceManager.cs`).

Codex must still do: bind `ScenarioContextId` from
`ScenarioCoordinator.ActiveScenarioId` (or the scene's scenario
service equivalent) on New Game, on Load, and on scenario change;
verify in the Main Scene that a Saloon Circuit run shows NO
first-session strip while the scenario HUD shows; verify a First
Ledger run still shows the strip. Note: the guidance manager file
is NOT in the Roslyn harness source selection, so the manager hook
is compile-verified only by the Unity build (Codex gate), while
the pure gate itself is harness-tested. Guidance step TEXT is
unchanged: "Hire your first store worker" is legitimate inside the
First Ledger. Any stale strip text baked into a prefab is a scene
asset Codex must inspect, not a source defect.

## 3. Acquisition interface contract (LL-PLAY-008, LL-PLAY-009)

Source model: `Domains/Economy/Acquisitions/Presentation/AcquisitionDealReadModel.cs`
(namespace `LandLedgers.Economy`). It is deliberately decoupled
from `AcquisitionMarketManager` (own stage/lead enums mirroring the
manager's by name) so it compiles standalone; a thin adapter at the
panel must map `AcquisitionDealStage` to `DealStageKey` 1:1 and
`AcquisitionLeadCategory` to `LeadKindKey` 1:1.

Panel binding (Codex):

- Primary action button: exactly ONE button bound to
  `NextLegitimateAction` ("Open Inquiry", "Present Proof of
  Funds", "Commit Earnest", "Continue Diligence", "Agree Terms",
  "Close Acquisition", "Closed", "Review Failure", or
  "Reopen Inquiry"). Labels mirror the manager's conversation
  action labels, including the proof-of-funds branch at Inquiry
  and the formal-diligence branch at EarnestCommitted.
- Below the button, the full `Stages` list (all five real states
  in order: Inquiry, Earnest committed, Diligence complete, Terms
  agreed, Closed), each with its State (Complete, Current,
  Upcoming, or Failed). This is the accessible deal detail: the
  presentation is simplified to one action, but financing,
  diligence, agreements, payments, and title transfer remain
  distinct states. Do NOT collapse them into fewer buttons.
- Deal details section: every `DealDetails` entry (price, earnest,
  diligence layers, financing) supplied by the panel adapter from
  the live deal. If the adapter has no detail, show nothing rather
  than placeholder figures.
- Provenance line: `ChannelDisplay`, always shown. When
  `IsChannelKnown` is false, show the unknown text verbatim
  ("Source not established - treat details as unverified") and do
  NOT substitute a plausible channel. Known mappings: PublicListing
  ("Public listing (acquisition board)"), RumorLead ("Rumor /
  conversation"), EstateSale ("Estate sale (public sale process)").
  QuietOpportunity, DistressSale, StrategicHoldout, and Unset are
  Unknown. An explicit established-channel record (a real newspaper
  advertisement asset, broker record, or seller record) passed as
  `establishedChannel` wins over the category mapping; the panel
  adapter must only pass records that actually exist.
- Market context line: `MarketContextLabel` ("Market context:
  owner turnover", etc.), labeled as market context. A listing's
  generation cause is NOT how the player learned of it; never
  present it as the information channel.

Provenance audit (source model, `AcquisitionMarketManager`):
listings are created with `source` set at four sites - InitialMarket
(default), PressureRelease / ServiceShortage (opportunity windows),
OwnerTurnover (business listings) - plus a `sourceReason` string.
`ResolveLeadCategory` maps those causes to lead categories, with a
default of PublicListing. The gap was presentational, not a
privilege leak: the panel never surfaced any channel at all. This
model surfaces the channel honestly and flags the rest Unknown.
No buyer, seller, or channel is manufactured anywhere in this pass.

What Codex must still do: build/rewire the acquisition panel to
this model, wire the adapter mapping, and verify with a Doctor's
Business opportunity that the provenance line is present and that
a QuietOpportunity lead shows the unknown-source text.

## 4. Panel-control contract (LL-PLAY-006, LL-PLAY-007)

LL-PLAY-006 (management panel layout) is visual work. Source
findings for Codex inspection:

- Component: `Presentation/UI/ManagementPanelView.cs`
  (`ManagementPanelView`, namespace `LandLedgers.UI`). Shell field
  `panelRoot` (fallback: the component's own RectTransform); tab
  content roots `propertiesContentRoot`, `financesContentRoot`,
  `acquisitionsContentRoot`, `governmentContentRoot`,
  `resourcesContentRoot`; property detail scroll pair
  `propertyDetailBodyScrollRoot` / `propertyDetailBodyContent`.
- The October 9 complaint (panel starting too low, unused space,
  unsatisfactory scroll/top alignment) is a RectTransform
  authoring issue: Codex must inspect `panelRoot` anchorMin /
  anchorMax / pivot / anchoredPosition / sizeDelta in the Main
  Scene prefab, check the scroll content's top alignment and
  content-size fitter settings, and screenshot before/after.
  Worker D claims NO visual fix here. No source dependency blocks
  a layout correction: the view exposes the roots above and the
  controller (`GeneralStorePanelController`) only writes text
  into them.
- Related: `Presentation/UI/LandLedgersHUDController.cs` guidance
  strip constants (`FirstSessionGuidanceTopGap`,
  `FirstSessionGuidanceAnchoredPosition`,
  `FirstSessionGuidanceSize`) are the strip's layout authority;
  the strip is a separate surface from the management panel and
  must not be repositioned as a fix for panel layout.

LL-PLAY-007 (seller-meeting layering) source contract:
`Presentation/UI/PanelModalCoordinator.cs` (pure, harness-tested).
Rules: exactly one modal owner at a time; a second owner is
refused, never stacked; only the active owner may close; closing
returns the focus-return target (normally the management panel);
double-close is safe. Components involved:
`Domains/Economy/Acquisitions/Workflows/AcquisitionSellerMeetingController.cs`
(ExecutionOrder 326; Escape closes, Enter advances, Back button
"SellerMeeting_Close" closes) and
`Domains/Economy/Acquisitions/Presentation/AcquisitionSellerMeetingView.cs`
(Root, PrimaryActionButton, CloseButton, ResponseButtons).

Codex must still do: own ONE coordinator instance at scene level;
route every modal open (seller meeting, and any other modal)
through `RequestModal`/`CloseModal`; set the seller-meeting
Canvas sortingOrder above the management panel Canvas; verify
the EventSystem focus returns to the management panel on close;
verify no second modal can open while the meeting is open;
screenshot the stacked state (meeting over panel) and the
post-close focus state. The old behaviour (meeting behind or
alongside the panel) is a Canvas/EventSystem authoring issue
until those checks pass.

## 5. Civic tab contract (LL-PLAY-014)

Source model: `ReadModels/Civic/SettlementCivicReadModel.cs`.
Panel binding: TitleText, SummaryText, EffectsText,
OwnerImplicationsText; focus button visible/interactable only
when `FocusAvailable` (authority present AND town hall built).

Source change made in this pass
(`GeneralStorePanelController.UpdateGovernment`,
`BuildGovernmentSummaryText`,
`BuildGovernmentOwnerImplicationsBodyText`): the title no longer
carries a "Not Built" suffix and the summary says "no town hall
yet" instead of "Not built". All numbers still come from the
civic foundation authority (`CivicFoundationManager`,
`TownHallState`, `TownHallContribution`). No public institution
is invented: with no authority, the tab reports no civic uplift
(all four effects +0%) and that town confidence remains
private-market led. With a town hall record but no building, it
reports steward, status label, and the inactive anchor. With a
built town hall, it reports plot/building ids as before.

Codex must still do: verify the Civic tab in the Main Scene in
all three states (no authority, record-but-unbuilt, built) and
screenshot each. The panel controller file is NOT in the Roslyn
harness source selection, so the wording change is verified by
the Unity build and by inspection, not by harness tests; the
read model itself IS harness-tested.

## 6. Resources tab contract (LL-PLAY-015)

Source model: `ReadModels/Civic/ResourceObservationReadModel.cs`.
Panel binding: SummaryText; one row per `Districts` entry
(KindLabel, DistrictCount, StrengthText, FreightPressureText,
SettlementPressureText, SpatialNote, OwnershipNote,
UncertaintyNote); one row per `ProtoSites` entry; always show
`UncertaintyBanner`.

Hard rules for the panel: percentages are 0-1 generated
prospectivity/pressure indices, NEVER tonnes or any physical
unit; there is no reserve field on the model, so the panel cannot
display a reserve; mineral rights are always "not established"
because no claim or right is recorded; a kind with no district
renders "no district generated", never a zero that reads as
depleted; proto-sites are labeled prospective, never proven.
The old panel text ("strongest 72%") is replaced by
"strongest 72% generated prospectivity" plus the ownership and
uncertainty notes. The underlying records
(`Domains/World/Regional/RegionalResourceScaffold.cs`:
`MineralDistrictRecord`, `RemoteIndustrySiteRecord`,
`RegionalResourceSnapshot`) carry no surveyed reserve, tonnage,
claim, or rights data, and none is added here - inventing it
would violate the brief.

Codex must still do: rebind the Resources tab to this model,
verify the Coal/2-districts/72% case renders with the
prospectivity label and the rights note, and screenshot it.

## 7. Startup notices (LL-PLAY-018)

Source classifier: `Presentation/UI/StartupNoticeGate.cs`
(precedence: ghost ownership, then debug-only, then premature
initialization, then tutorial remnant, then show).

Trace of the October 9 startup messages to source:

- Operations line ("Staff x/y | Stock ... | Reorder needed") in
  `GeneralStorePanelController`: LEGITIMATE live condition from
  the store runtime. Keep visible. It is a real staffing/stock
  state, not noise.
- "Startup unavailable" / "Startup planning is unavailable"
  blockers: LEGITIMATE blockers from shell/startup readiness.
  Keep visible.
- The many "... unavailable" fallbacks (store ledger, finances,
  property/business details, bank loan records, owner cash):
  PREMATURE-INITIALIZATION fallbacks that render when a runtime
  source is not bound yet. They are missing bindings, not
  economic conditions. Codex should gate them behind runtime
  readiness (show a neutral loading state until bound, then
  re-evaluate) rather than deleting the fallback text - a bound
  runtime that genuinely lacks records must still say so.
- First-session guidance strip: TUTORIAL, now gated by section 2.
- Notices about businesses the player does not legitimately own:
  GHOST OWNERSHIP (LL-PLAY-001 family). Never render as live
  conditions; report them. Ownership legitimacy itself is Worker
  A / coordinator scope (New Game bootstrap, portfolio grants),
  not settled by this classifier.

Codex must still do: apply the gate at the panel call sites for
the premature-init fallbacks, and verify startup with a clean
New Game that no "unavailable" fallback flashes before runtimes
bind while real blockers still show.

## 8. Causal reporting (brief 6.3)

Source model: `ReadModels/Causal/SettlementCausalExplanation.cs`.
Feed it ONLY from real records (employment registry, obligation
authority, freight records, business lifecycle). Each line cites
one event (day + kind + caller detail, verbatim). The verdict is
a pure count summary - "Positive signs", "Under strain", "Mixed",
or "No recorded events" - and `VerdictBasis` always lists the
counts, so the verdict is checkable against the lines. Neutral
kinds (freight delay, property change) count as neither positive
nor distress. With no events the model says so; it never invents
a narrative. Do not upgrade labels in the panel ("thriving" from
a single purchase would be fabrication).

Codex must still do: bind the model to a settlement/business
inspection surface fed by the real record sources above, and
verify one explanation against its underlying events.

## 9. Person inspection (brief 6.4)

Source model: `ReadModels/Person/PersonInspectionReadModel.cs`.
It composes identity, household, work, current activity/task,
location, destination, schedule, housing, finances, needs, and
recent events, applying `ObserverVisibility` per field:

- Identity (name, alive/deceased, derived age): everyone.
- Household, work, housing, finances, history: self + household
  members only. A stranger sees none of these (household id -1,
  empty names), with the reason in `AccessNotes`.
- Current activity + location + destination: self, household
  members, or an observer at the same location or a public
  location. Anyone else sees activity "unknown" and no location.
- Purpose, current task, employer, blockers: self + household
  members only, even when activity itself is visible.
- Business financials are NOT in this model: they stay under
  `ObserverVisibility.CanSeeBusinessFinancials` (owner + delegated
  managers) on the business surface.
- Needs: only the caller-supplied summary is shown (feed from
  `HouseholdNeedRegistry`); when none is supplied the field says
  "Needs: not recorded in this view." - never invented.

Codex must still do: clicking, selection, camera, and HUD
presentation; bind the click handler to this model with a real
`ObserverContext` (player person id, household id, location,
public-location predicate); verify the four observer cases
(self, household member, stranger present, stranger elsewhere)
in the Main Scene.

## 10. Construction / formation interface contract (LL-PLAY-010)

Interface contract ONLY - production behaviour belongs to
Worker B, and this contract must be reconciled with B's at
integration. If B's contract disagrees with the stage list below,
the coordinator resolves; Worker D does not publish a rival
contract. Stages the panel must keep distinct, in order:

1. Owning land - a titled parcel in the player's portfolio.
   No building, no business.
2. Designing a building - choosing a plan/shell for that parcel.
   Design is not construction and not ownership of a business.
3. Constructing a building - material supply, labor, and
   progression of a real construction project on the parcel.
   No menu-spawned building: inputs must be procured and the
   project must progress through its states.
4. Acquiring premises - obtaining an existing building/premises
   (purchase or equivalent legitimate transfer), distinct from
   building one.
5. Establishing a Business - forming the business entity, distinct
   from owning its premises.
6. Equipping and operating - premises, assets, labor, inputs, and
   real production/service execution for that business.

Panel fields per stage (Codex): current stage, what is owned vs
designed vs built vs operating, blocking inputs with quantities
and sources, and the single next legitimate action for the
stage (same one-action pattern as section 3). The old controls
"Build Shell", "Next Shell", "Previous Business" are stage-2/3
and stage-6 controls respectively; they must be labeled with
their stage and must not appear on a parcel/business where
their stage does not apply. Scene-text remnants of those labels
outside their stage are Codex cleanup, listed here, not claimed
as source-fixed.

## 11. Per-issue source-vs-Unity split (Worker D items)

- LL-PLAY-004: SOURCE - guidance gate + manager hook (tests pass
  for the gate; manager hook needs Unity compile). UNITY -
  bind ScenarioContextId, verify strip suppression per scenario,
  clean prefab strip text.
- LL-PLAY-005: SOURCE - HUD read model + adapter + failure-board
  surfacing (tests pass). UNITY - author/bind the HUD panel,
  screenshot live/failed/complete/Sandbox states.
- LL-PLAY-006: SOURCE - component/dependency identification only.
  UNITY - RectTransform authoring + screenshots. NOT source-fixed.
- LL-PLAY-007: SOURCE - modal-ownership contract (tests pass).
  UNITY - single coordinator instance, Canvas sorting, focus
  return, screenshots. NOT visually fixed at source.
- LL-PLAY-008: SOURCE - single-next-action model + stage rows
  (tests pass). UNITY - panel rewire + adapter mapping +
  verification. Real deal states unchanged.
- LL-PLAY-009: SOURCE - provenance model + audit (tests pass);
  Unknown flagged, never invented. UNITY - panel rewire +
  verification with a real opportunity.
- LL-PLAY-014: SOURCE - civic read model + panel wording change
  (wording needs Unity compile; model tests pass). UNITY -
  three-state tab verification + screenshots.
- LL-PLAY-015: SOURCE - resources observation model (tests pass).
  UNITY - tab rebind + screenshot of the 72% case.
- LL-PLAY-018: SOURCE - notice classifier + trace (tests pass).
  UNITY - gate the premature-init fallbacks at panel call sites,
  verify clean startup. Real failures stay visible.
- PASS11-006: SOURCE - failure board now has a HUD consumer
  contract (adapter + notice rows + banner). UNITY - the live
  scene panel itself remains UNWIRED. This is explicitly not
  claimed as done.
- Brief 6.3: SOURCE - causal explanation model (tests pass).
  UNITY - bind to an inspection surface fed by real records.
- Brief 6.4: SOURCE - inspection model + access rules (tests
  pass). UNITY - click/selection/camera/HUD presentation.

## 12. Files added or changed by Worker D

Added (each with a fresh-guid .meta):

- `ReadModels/Scenario/ScenarioHudReadModel.cs`
- `Orchestration/Scenarios/Generation/ScenarioHudSnapshotBuilder.cs`
- `Orchestration/Scenarios/Generation/ScenarioGuidanceGate.cs`
- `Domains/Economy/Acquisitions/Presentation/AcquisitionDealReadModel.cs`
- `Presentation/UI/PanelModalCoordinator.cs`
- `Presentation/UI/StartupNoticeGate.cs`
- `ReadModels/Civic/SettlementCivicReadModel.cs`
- `ReadModels/Civic/ResourceObservationReadModel.cs`
- `ReadModels/Causal/SettlementCausalExplanation.cs`
- `ReadModels/Person/PersonInspectionReadModel.cs`
- `Tests/Editor/Orchestration/Pass12ScenarioPresentationTests.cs`
- `Tests/Editor/Orchestration/Pass12PlayerExperienceReadModelTests.cs`
- `Docs/Pass12_PlayerExperience_Contracts.md` (this file)
- Folder metas: `ReadModels/Scenario.meta`,
  `ReadModels/Causal.meta`, `ReadModels/Civic.meta`

Changed (surgical only):

- `Orchestration/Guidance/FirstSessionGuidanceManager.cs` -
  `ScenarioContextId` + gate check in `RefreshGuidance`.
- `Domains/Economy/Businesses/GeneralStore/Presentation/GeneralStorePanelController.cs` -
  Civic title/summary wording only (three strings).

Shared files touched (coordinator attention): the two changed
files above are also in other workers' potential scope
(Guidance is not assigned to another worker in the foundation;
the GeneralStore panel controller is a feature Presentation file
owned here for this change). No financial authority, business
production method, or Bootstrap core state was touched. No
serialized type, field, or namespace was renamed. No test was
deleted or weakened. No Canon policy was invented.
