# Script Audit Register — Overnight 2026-11-04

Source-read audit of 32 scripts, done against the code on disk (branch
`muse/overnight-economic-stabilization-2026-11-04`, baseline
`5284d21`). No Unity verification is claimed for scene-bound files.
"Tests" counts test files referencing the script name. Line counts are
from this branch.

## Focus findings

### A. SharedBusinessRuntimeManager — authority and dependencies

`Domains/Economy/Core/SharedBusinessRuntimeManager.cs` (10,548 lines,
90 public methods, 27 test files). It is the shared business runtime:
business entity identity registry (ids never reused), employment
wiring, obligation wiring, household-cash wiring, demand collection,
weekly shared operations, valuations, and the town-pulse support leg.

- What it OWNS: business identity, the shared business list, weekly
  shared operations for non-General-Store businesses, business
  valuations, household-reserve seller candidacy, doctor-visit
  resolution, and operation summaries.
- What it WIRES but does not own: cash (HouseholdLedger, one cash writer),
debt (FinancialObligationAuthority, one debt authority), employment
records (EmploymentRelationshipRegistry), and per-business cash
(BusinessRuntimeState). It wires those authorities into businesses;
it does not duplicate their ledgers. That wiring is the correct shape
and must be preserved in any split: do NOT split this file by line
count. If it is ever divided, divide by authority boundary (identity
registry, weekly operations, valuations, reserve/demand), never by
arbitrary chunk.
- Dependency direction is healthy: runtimes (Bakery, Barber, Boarding
  House, Lumber Yard, Freight) call into it; it does not call into
  individual shop runtimes except through BusinessInstanceState and
  profiles. The one exception to watch is GeneralStoreRuntimeManager
  (see B): the two managers reference each other, so changes to
  either one's initialization order can break the other.
- Hiring classifications: `minimumHireLaborAccess` defaults to
  `YoungWorker` (Inspector field, documented as "a legal/access
  eligibility filter, not a staffing target"). The levels are
  `None < HouseholdHelper < JuniorLowTrust < YoungWorker <
  FullLaborMarket` (PopulationEnums). The filter only admits
  candidates at or above the floor via
  `NewcomerSettlementEvaluator.IsAvailableForLabor`; the employer
  still chooses among eligible candidates. Keep the legitimate
  age/safety semantics: the floor is an eligibility rule, not a
  hiring decision, and lowering it to admit household helpers or
  junior low-trust workers is a policy decision for Kennedy, not a
  code cleanup. No change made here.

### B. SharedBusinessRuntimeManager — legacy General Store assumptions

Real, documented, and mostly deliberate — but they are assumptions,
so they are recorded, not silently kept:

- The shared weekly operations deliberately EXCLUDE the General
  Store: its payroll/settlement runs through
  `GeneralStoreRuntimeManager.ResolveWeeklyPayrollAndReorder`
  (comments at lines ~1013, ~1067, and the three BusinessType
  checks). Any "shared" weekly rule added here does NOT apply to the
  store unless someone also adds it there. That is the single most
  likely source of store-vs-other-business drift.
- `InitializeIfNeeded` seeds the shared business list from the
  player General Store (`CurrentBusiness`) when present. A town
  without a player store starts from an empty list; nothing breaks,
  but shared demand/valuation code paths that assume a store exists
  must keep their null guards.
- Public supplier knowledge defaults to general-store-only
  (`FindGeneralStoreSupplierId`, used for the supplier id passed at
  lines ~631, ~662). In a town where the store is not the supplier
  anchor, that default misleads household knowledge until a real
  supplier is learned (see overnight Slice 2, which fixed the
  matching store-port visibility gap).
- Recommendation: when a second "shared" behaviour is added, add an
  explicit store-inclusion decision in the comment (applies to store:
  yes/no, and where the store's version lives). Do not unify the two
  payroll paths without Kennedy's call — they are separate
  authorities today by design.

### C. Acquisition rights — what exists, what does not

`Domains/Economy/Acquisitions/AcquisitionMarketManager.cs` (12,620
lines, 116 public methods, 7 test files). Title authority lives in
`Domains/World/Property/PropertyTitle.cs` (TitleAuthority, chain of
title over parcels; TitleBasis: Purchase, HomesteadClaim,
Inheritance, Grant, CourtOrder).

- Rights are split by asset class, not held in one registry: land
  title (TitleAuthority parcel chain), business ownership
  (BusinessInstanceState.Owner), building ownership (PlacedBuilding
  playerOwned flags + owned id sets), equipment ownership
  (TransportAssets owner kind/id), inventory (runtime stock states),
  contracts and debt (FinancialObligationAuthority). That matches
  "one authority per consequential fact", but there is no single
  place that answers "who may sell what" — each sale path checks its
  own class.
- No unsolicited-offer path was found. Player business sales are
  player-initiated only: `TryEstimatePlayerBusinessSaleOffer`
  computes a formula offer (land + operating + staff + quality,
  distress and weak-performance multipliers — no negotiated premium,
  no fixed threshold approved anywhere), and
  `TrySellPlayerBusiness` requires explicit confirmation, then
  transfers via `TryTransferBusinessToTown` to a generated tier
  label ("Capital Buyer", "Local Buyer", "Distress Buyer" plus trade),
  not a real person. A buyer who is nobody cannot later be a debtor,
  a neighbour, or a litigant — that is fine for a formula sale, but
  it is not an NPC offer and must not be described as one.
- No lease or tenancy tenure is modeled in the acquisition manager
  (no lease/tenant/landlord concept found there). Business sale
  clears BOTH the business and its plot (`plot.playerOwned =
  false`) on the assumption that business ownership implies plot
  ownership. That assumption holds today because business premises
  paths require owned shells, but it MUST be re-checked before any
  leasing/occupancy-for-premises feature ships: as written, a tenant
  operator selling "their" business would dispose of the landlord's
  plot. Suggested guard (not implemented here): refuse the sale, or
  split land from business, when the plot holder differs from the
  business owner.
- Title has no lease basis and no business/inventory/equipment title:
  a lease, if added, needs a TitleBasis value or a separate tenure
  record — do not shoehorn tenancy into the parcel chain.

### D. Private credit — not player-only bookkeeping

- `FinancialObligationAuthority` is the single debt authority
  (Create/CreateWithTerms, TotalReceivableFor, novation with
  creditor consent). PlayerDebtManager is the player-facing lender
  purse ("the lender's finite purse", LoanContract, activeLoans).
- Seller finance EXISTS as a real path, not a TODO:
  `Domains/Economy/Financing/SellerFinanceClosing.cs` closes a sale
  into an obligation through FinancialObligationAuthority with the
  seller as creditor, producing a note instrument id and an
  obligation id, with cash-to-seller vs financed split and title
  transfer flags. Supporting pieces: CreditOfferWorkflow,
  CreditRegistry, CreditEventLog, NpcPropertyAcquisition.
- Therefore the brief's "debt must not stay player-only" is already
  structurally answered: do NOT add a second credit manager. The open
  work is verification, not construction: confirm exactly-once
  settlement for seller notes (one obligation per closing, no
  duplicate principal on retry/restore), confirm creditor rights
  survive save/load (obligation + title + note instrument ids round
  trip together), and confirm NPC acquisition obligations settle
  through the same authority rather than a side ledger. Those checks
  were not completed overnight and are flagged, not claimed.

### E. Legacy managers — Town Pulse and Opportunity Pressure

- Town Pulse (`Domains/Economy/Markets/TownPulseRuntimeManager.cs`,
  719 lines): the three scripted defaults ARE present and cycle by
  index on a schedule — Repair Rush (tools/hardware 3), Boarding
  House Bulk Order (staple food 6, meat 2), Mild Illness Wave
  (medicine/remedies 3). BUT the integrity-critical question was
  checked and is clean: pulse demand is measurement, not commerce.
  The store leg returns 0 with no revenue, no stock movement, no
  financial mutation (documented "aggregate demand signal, not a
  buyer"); the support-industry leg (`TrySellTownPulseDemandFrom
  Business`) likewise moves no stock and posts no revenue, only an
  operation-summary note, and its outer method returns 0 regardless.
  All pulse demand lands as recorded unfulfilled demand plus
  pressure signals. Legitimate criticism is narrower: the events are
  scheduled signals regardless of population need, so they can
  manufacture *pressure* (and thereby listing releases) even when
  nobody needs tools, food, or medicine. Mild Illness Wave overlaps
  the real illness system (PopulationManager.ResolveDailyHealth +
  doctor visits) at the demand-signal level only; it does not create
  cases and must not be deleted as if it were the illness mechanic.
- Opportunity Pressure (`Domains/Economy/Markets/
  OpportunityPressureRuntimeManager.cs`, 1,940 lines): pressure
  inputs are real signals — retail lost-demand cents from the store
  runtime, blacksmith hardware-supply presence, doctor presence —
  and `Replace` has no production callers (computation only in
  production, override only in tests). The "fake opportunities"
  concern therefore lands on the RESPONSE rule, not the inputs:
  pressure readings drive land-listing releases ("Town lot released
  for private sale", capped at 3 parcels per release in the code
  read). That rule is a designed generator, not emergent scarcity,
  and should be labeled as such wherever it is surfaced. Do not
  delete the manager: its notice board is the only place that turns
  missed demand into readable advice ("Restock thin categories...",
  "Restore or staff blacksmith supply...").

### F. Business creation UI — checkable diagnosis (not fixed)

Symptom: Businesses tab, Start New Business does not open the
authored `UI Canvas > Management > Create Business Overlay`.
Code path (verified): tab button → `ManagementPanelView.
OpenBusinessFormationFlow` → `FindAnyObjectByType<
BusinessCreationPanelController>` → `TryOpenFormFromManagementTab`
→ `OpenForm`. Failure modes, in order:

1. No BusinessCreationPanelController in the scene → "Business
   formation is not initialized in this session."
2. Panel setup failed before Configure: `GeneralStorePanelController`
   setup returns false when `view.BindFromChildren()` fails
   ("ManagementPanelView has missing required authored references.
   Repair UI Canvas > Main Panel instead of building a fallback
   UI."), and Configure (line ~730) never runs, so `configured`
   stays false → same message as (1).
3. `view.PanelRoot` null → `EnsureControls` returns early, overlay
   and confirmButton stay null → "Business formation controls could
   not be initialized."
4. Note: in the runtime bootstrap path the component is
   AddComponent'd, so its serialized overlay field is null and
   `EnsureControls` BUILDS a runtime overlay (`BuildOverlay`)
   instead of binding the authored one. The authored overlay is only
   used if the component is scene-authored with the field assigned.

Diagnosis for Kennedy/Codex (one run, no code change): press the
button and read the two `[ManagementUI]` log lines (lookup
found/configured/active, then "Formation did not open" with the
message) plus whether the panel-setup error appears. The message
distinguishes cases 1–3 exactly. No source fix was made because the
Canvas is unpublished: the failure is in scene wiring or authored
references, and guessing at it from source alone would risk breaking
a working panel. The money-feedback overlay question was not
reached; it stays open.

## Register — 32 scripts

Priority: P0 = fix before depending on it, P1 = improve soon,
P2 = watch, P3 = fine as is. "Source of truth" means the fact this
file alone may assert; everything else must read it, not recompute
it.

| # | Script (file) | Purpose | Main callers | Source of truth | Overlaps / problems | Tests | Suggested improvement | Priority |
|---|---|---|---|---|---|---|---|---|
| 1 | TownWorldController (Domains/World, 12,905) | Town generation, buildings, plots, roads, terrain compatibility, save validation | CivicFoundation, AcquisitionMarket, OwnershipWorkflow, GeneralStoreRuntime, bootstrapper | Generated town layout; terrain-revision compatibility | God file: generation + placement + validation + save + inspection in one class; validation already correct (clean = info log) | 25 | Split by boundary only (generation vs validation vs inspection), never by line count | P2 |
| 2 | RegionalTerrainTileView (Domains/World/Regional, 3,338) | Regional terrain tile rendering, evidence inspection digests | TownWorldController, bootstrapper, debug overlay | Regional tile visuals + evidence summaries | No class summary; heavy diagnostics surface; read-only visuals, safe | 2 | Add class summary; keep read-only | P3 |
| 3 | PreAuthoredTerrainWorldProfile (Domains/World, 290) | Authored terrain/mask provider config + validation | TownWorldController, bootstrapper, editor setup | Which authored terrains/masks apply | Small and clean; terrain work is read-only per brief | 1 | None; terrain edits stay out of source slices | P3 |
| 4 | PathingManager (Domains/World/Pathing, 879) | Grid pathfinding, door/building anchors, cost | LogisticsRuntime, PopulationPathingDirector, AgentMover, route planner | Paths and anchors | Clean authority; demo controller is the only odd caller | 6 | None | P3 |
| 5 | TownPathingDemoController (Domains/World/Pathing, 431) | Demo route spawning (house-work, house-store) | Itself only | Nothing (demo) | Demo-only script with no production callers; keep out of production paths; candidate for editor-only gating | 0 | Gate to editor/dev or delete when demos retire | P2 |
| 6 | TimeManager (Core/Time, 750) | Campaign clock, calendar epoch, scenario start dates | Everything via Instance; DueEventScheduler | Campaign date/time; timeline anchor | Scenario start-date handling is subtle (own runtime epoch); do not touch without date tests | 7 | None; date changes need regression tests | P3 |
| 7 | PopulationManager (Domains/Population, 2,675) | Population state, households, demographics, health, settlement | CivicFoundation, AcquisitionMarket, GeneralStoreRuntime, SharedBusinessRuntime | Population + household membership; mortality/fertility flags (new, Slice 1) | Large but coherent; demographic day drive is the Slice 1 seam | 15 | None now; keep demographic flags config-driven | P3 |
| 8 | GeneralStoreRuntimeManager (Domains/Economy/Businesses/GeneralStore, 5,667) | General Store runtime: sales, payroll, reorder, trading ports, pulse consumption | AcquisitionMarket, panel controller, supplier adapters, SharedBusinessRuntime | Store cash, stock, payroll, reorder | Payroll/settlement deliberately separate from shared runtime (see B); not harness-compilable (scene-bound) | 16 | Document store-vs-shared rule at every new shared behaviour | P1 |
| 9 | PopulationPathingDirector (Domains/Population, 1,308) | Street actors as views of living persons; work/home/store routing | AgentMover, SimulationDrivers, SaveLoadManager, bootstrapper | Who may be represented on map (living, housed, on-map only) | Identity rule is explicit and correct (record, not GameObject, is authority) | 2 | None | P3 |
| 10 | GeneralStorePanelController (Domains/Economy/Businesses/GeneralStore/Presentation, 8,084) | Management UI shell: properties, finances, acquisitions, businesses tabs; coaching text | WorldInteraction, BusinessCreationPanel, SaveLoadManager, bootstrapper | Management panel state + coaching reads | God file for UI; owns business-creation Configure call (see F); panel-setup failure blocks formation | 3 | Split by tab only if UI work is funded; otherwise leave | P2 |
| 11 | AcquisitionMarketManager (Domains/Economy/Acquisitions, 12,620) | Acquisition market: listings, construction queue, land pricing, business sale, title disposal | Workflows, panel controller, ExpansionReadiness | Market listings; owned id sets; land appreciation | God file; sale assumes business ownership implies plot ownership (see C); pressure-driven listing release is a designed generator | 7 | Add plot-vs-business ownership guard before any leasing feature | P0 for leasing; P2 otherwise |
| 12 | FirstLedgerSliceBootstrapper (Orchestration/Bootstrap, 1,898) | Dev bootstrap: authorities census, world/business/runtime configure, guidance disable, transport presentation | Panel controller, review checklist, pathing | Scene bootstrap order (dev) | First-Ledger-named but scenario-agnostic in behaviour; guidance already disabled here | 3 | Rename only with a migration plan; behaviour is fine | P3 |
| 13 | FirstLedgerBootstrap (Orchestration/Scenarios/FirstLedger, 320) | First Ledger goal evaluator wiring + objective qualifiers | SaveLoadManager, validation harness, SaloonCircuitBootstrap (sibling pattern) | First Ledger objective tracking | Keys off "first-ledger" (correct); small and clean | 4 | None | P3 |
| 14 | SimulationSystemsHub (Orchestration/Systems, 1,317) | Scene home + save wiring for standalone simulation authorities | AcquisitionMarket, GeneralStoreRuntime, SharedBusinessRuntime, panel | Authority instances + save DTO assembly | Hub is the right pattern; keep new authorities here, not in drivers | 12 | None | P3 |
| 15 | SimulationDrivers (Orchestration/Systems, 6,314) | Per-tick drivers forwarding TimeManager ticks to authorities | Freight/Mine drivers, GeneralStoreRuntime, mail service | Nothing (forwarding only, by design) | Large but intentionally thin; freight + demographic + needs drives live here (Slices 1, 4, 5 touched it); not harness-compilable | 20 | Keep thin; logic belongs in authorities, not drivers | P2 |
| 16 | ScenarioDirector (Orchestration/Scenarios, 505) | Scenario registry, switching, save/restore, goals/objectives API | Bootstrapper, guidance manager, validation harness, FirstLedgerBootstrap | Active scenario + runtime state | Clean; do not duplicate; no prerequisite gate (by design, see Slice 7) | 2 | None | P3 |
| 17 | ScenarioStartPanelController (Orchestration/Scenarios, 600) | Start panel + objective HUD, clock hold until selection | Bootstrapper, guidance manager, editor authoring | Start-screen scenario choice | One asset per instance; multi-choice start UI is future work (see Slice 7) | 1 | None now; multi-choice needs Kennedy's UI call | P3 |
| 18 | CivicFoundationManager (Domains/Civic, 1,342) | Town hall, schoolhouse, civic contributions, growth plans | AcquisitionMarket, panel controller, OpportunityPressure, SaveLoadManager | Civic state + notices | FindOrCreate boundary is deliberate (bootstrap/persist order); keep | 2 | None | P3 |
| 19 | BusinessCreationPanelController (Domains/Economy/Creation, 530) | Business formation form: type/template selection, submit | Panel controller, ManagementPanelView, editor authoring | Formation draft + submission | Runtime-built overlay vs authored overlay split (see F); zero test files | 0 | Add a harness-testable formation-validation seam; scene diagnosis first (see F) | P1 |
| 20 | SharedBusinessRuntimeManager (Domains/Economy/Core, 10,548) | Shared business runtime (see A, B) | All shop runtimes, AcquisitionMarket, ExpansionReadiness, panel | Business identity + shared weekly ops + valuations | Store payroll exclusion + store-anchored defaults (see B); mutual refs with store runtime | 27 | Never split by line count; store-inclusion comment rule (see B) | P1 |
| 21 | LogisticsRuntimeManager (Domains/Economy/Logistics, 2,113) | Shipment-to-transport binding, shipment lifecycle summaries | Freight driver, GeneralStoreRuntime, Lumber Yard, SharedBusinessRuntime | Cargo lifecycle + physical transport binding | Clean single-authority statement; capacity/readiness snapshots are the freight UI source | 5 | None | P3 |
| 22 | TownPulseRuntimeManager (Domains/Economy/Markets, 719) | Scheduled demand-signal events + missed-demand tracking | GeneralStoreRuntime, OpportunityPressure, SaveLoadManager, bootstrapper | Active pulse + daily fulfillment records | Scripted cycle regardless of need (see E); but zero commerce impact — measurement only | 3 | Label pulse-driven pressure as designed, not emergent, in UI text | P2 |
| 23 | OpportunityPressureRuntimeManager (Domains/Economy/Markets, 1,940) | Pressure readings + notice board + desk summaries | AcquisitionMarket, panel controller, bootstrapper | Pressure snapshot + notices | Inputs are real signals; listing-release response is a designed rule (see E); Replace is test-only in production | 1 | Add one test file for pressure-from-real-signals (only 1 test file today) | P2 |
| 24 | PlayerDebtManager (Domains/Economy/Financing, 2,622) | Player lender purse, loan applications, acquisition financing | AcquisitionMarket, ExpansionReadiness, workflows, panel, Portfolio | Player loan state + lender capital | Player-facing only by role; seller/NPC credit lives in Financing siblings (see D) — do not merge | 5 | Verify seller-note settlement exactly-once (see D) | P1 |
| 25 | PlayerPortfolioManager (Domains/Economy/Finance, 1,948) | Owner cash ledger, business cash transfers, wealth snapshot | AcquisitionMarket, GeneralStoreRuntime, SharedBusinessRuntime, panel | Owner cash movements (every cent with day/reason/balance) | Clean one-writer ledger discipline; transfer thresholds are the configurable seam | 12 | None | P3 |
| 26 | SaveLoadManager (Infrastructure/Persistence, 2,467) | Save/load slots, manifests, repair notes, authority rebinding | Procurement drivers, Bakery chain, PopulationManager, editor authoring | Save slot integrity + repair log | Repair-note system is the safety net for older saves; guidance rebinding lives here too | 3 | None; save-format changes need migration notes | P3 |
| 27 | FirstSessionGuidanceManager (Orchestration/Guidance, 1,561) | First-session objective strip: progress, facts, save DTO, scenario gate | SaveLoadManager, bootstrapper, editor authoring | Guidance progress (legacy scenes only on Main Scene path) | Gate fixed Slice 7; strip disabled by bootstrapper; architecture preserved, not deleted | 0 | Add gate-level tests (done Slice 7); manager itself needs no change | P3 |
| 28 | WorldInteractionController (Domains/World, 508) | Click/raycast target resolution for world interaction | FamilyTreeBuilder, RecruitmentConversation, editor authoring, bootstrapper | Click target | NPC-vs-building selection priority lives here or in its raycast path — the brief's click-selects-building issue needs a Unity repro before any fix; no source fix made | 2 | Repro first: click NPC standing in front of building, log resolved target | P1 |
| 29 | OwnershipWorkflowController (Domains/Economy/Acquisitions/Workflows, 1,219) | Acquisition/loan/inspection workflow orchestration for UI | Panel controller, editor authoring | Workflow open/close state | Thin orchestrator over market + debt managers; correct layering | 2 | None | P3 |
| 30 | AcquisitionSellerMeetingController (Domains/Economy/Acquisitions/Workflows, 549) | Seller meeting UI for selected/listed acquisitions | OwnershipWorkflow, panel controller, modal coordinator | Meeting open state + per-frame press detection | Zero test files; UI-only; meeting content comes from market workflows | 0 | None now | P3 |
| 31 | ScenarioValidationHarness (Orchestration/Scenarios/Development, 449) | Dev-only scenario probing: start/restart, inject cash, hire, valuations | Bootstrapper, editor window | Nothing (dev probe surface; never completes goals directly, by design) | Correctly fenced as development-only; keep it out of production builds | 1 | None | P3 |
| 32 | LLMainMusicPlayer (Presentation/Audio, 1,043) | Main music playback: playlist, pause/resume, volume | Itself + editor authoring only | Music playback state | Unpublished changes exist (per brief, do not assume); no test files; untouched overnight per brief | 0 | None overnight; any fix needs a demonstrable defect first | P3 |

## Not audited deeply (flagged, not claimed)

- Money-feedback floating overlay callers: not traced overnight.
  Kennedy dislikes it; retiring it needs a caller inventory first
  (search floating/feedback overlay presenters) and a check that
  management-UI feedback survives. Left open.
- NPC horse/wagon purchases, property/building trading beyond the
  sale path, acquisition negotiation steps, apprenticeship/
  qualification gates, leasing/occupancy, room outfitting, asset
  maintenance, NPC entrepreneurship/investment, competition, and
  the Canon/Technical gap review: not covered by this register.
  Each needs its own source pass before any implementation claim.
