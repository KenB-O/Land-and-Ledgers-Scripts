# Codex Handoff — Real People, Households & Shared Credit (Phases B–H)

Branch: `muse/real-people-shared-credit` @ `fad195e`. All domain logic is
source-complete and harness-verified (261/262 Roslyn tests; the 1 failure is
pre-existing at `7b1ca29` — see `Docs/RealPeopleSharedCredit_PhaseI_RegressionSweep.md`).
Everything below is Unity/scene/prefab work that can only be done in-engine.
Nothing here is claimed as working in Unity.

Conventions used below: "harness-faked" = a test fake exists in `Tests/` and
proves the contract; Codex writes the production implementation.

---

## 1. Scene / Inspector wiring required (per system)

### Household cash (Phase B)
- `SimulationDrivers` (MonoBehaviour, scene singleton) must have its `hub`
  (`SimulationSystemsHub`) reference set; `WireHouseholdCashAuthorities()` (line
  ~205) resolves `HouseholdLedgers` from the hub. If the hub reference is missing,
  `GeneralStoreRuntimeManager` and doctor services silently fall back to the
  retired `spendingMoneyCents` wallet — check the Inspector, do not rely on the
  fallback in production scenes.
- `GeneralStoreRuntimeManager.ResolvedHouseholdLedgers` must resolve to
  `hub.HouseholdLedgers` (currently resolves via the drivers wiring; verify in
  the general-store scene).

### Shopping loop (Phase C)
- `SimulationDrivers.RegisterGeneralStoreSupplier(...)` must be called at boot
  for every general-store scene instance so the store is registered as an
  `IGoodsSupplier` in the `SupplierDirectory` used by
  `HouseholdShoppingLoop.ExecuteNeed`. Without registration, needs stay unmet
  (loud diagnostics, no fake purchases).
- `HouseholdShoppingLoop.RegisterTaskDefinitions(TaskAuthority)` must run once
  at boot (registers the `shopping` work-task; person-time reservations cite it).

### Rent collection (Phase D)
- A production `IRentCashSink` adapter on the boarding-house runtime manager
  (see §3). The scene's boarding-house business must supply its
  `BusinessInstanceId`, `BusinessName`, and route `RecordCashInflow` into
  `BusinessRuntimeState.AddCashCents`.
- `RentCollectionService` instance must live on (or be reachable from) the
  boarding-house manager; call `SettleDues` weekly (see §5).

### Housing search (Phase D)
- A production `IBoarderCheckInPort` adapter on the boarding-house manager
  (see §3): `CheckIn(personId, dayIndex, diagnostics)` against the real
  `BoardingHouseBoarderRegister` + `BoardingRoomInventory`.

### Construction (Phase D/F)
- Production `IConstructionMaterialSource` adapter: `AvailableUnits` /
  `TryConsume` against real material lots (lumber yard stock / household
  inventory, depending on who commissions). `IHouseholdToolCustody` adapter:
  `HouseholdHoldsTool` against the commissioning household's real
  `HouseholdInventory` (harness-faked in the Phase H slice).
- `BuildingDesignCatalog`: Codex registers each authored `BuildingDesign` AND
  calls `RegisterAvailablePrefab(prefabId)` for every prefab that exists in the
  Unity project. Any design whose prefab is not registered files a
  `PrefabHandoffRecord` at runtime and the project proceeds as data-only
  (`VisualPlacementPending = true`) — this is by design, not an error.

### Credit (Phase E)
- `CreditCashBridge`: Codex registers one `IRealCashStore` per participant via
  `Register(owner, store)` before any credit workflow runs:
  - households → `HouseholdCashStore(householdId, hub.HouseholdLedgers.GetOrCreate(householdId))`
  - private purses → `PurseCashStore(ownerName, openingBalanceCents, openingSource)` (opening source REQUIRED if balance > 0)
  - NPC businesses → `NpcBusinessCashStore(businessInstanceId, port)` with a production `INpcBusinessCashPort` (see §3)
  - the bank → vault `PurseCashStore` + `BankCreditParticipant`
- **Bank deposit-ledger backing**: `BankCreditParticipant.DepositsOwedCents` is
  a plain field Codex must feed from the bank runtime's real deposit ledger
  (`BankRuntime` → `BankDepositLedger.DepositsOwedCents()`; see
  `Domains/Economy/Businesses/Bank/BankDeposits.cs`). The reserve math
  (`ReserveFloorCents`) is only honest if this is kept in sync — refresh it
  whenever deposits change, at minimum on the weekly boundary.

### NPC business formation (Phase G)
- `NpcBusinessCreationAdapter` (source-complete, written-for-Unity): Codex
  instantiates it with the scene's `EntityIdRegistry` and sets
  `PremisesAssigner` (premises kind → real building id, -1 when none) and
  optionally `AuthoredProfileLookup` (falls back to code-built profiles for any
  of the 28 `BusinessType`s per Canon §3.1).
- The adapter runs NPC intents through the REAL `BusinessCreationAuthority`
  (full Unity creation closure: `BusinessInstanceState`, ScriptableObject
  profiles) — formation orchestration over the port is harness-tested via a fake
  port; the adapter itself is Codex's Unity-run surface.

### Person-time (Phase C)
- One `PersonScheduleTracker` per simulation, shared by construction, shopping,
  and NPC investigation. Not yet hub-wired (see §6 of the save/load doc) —
  Codex owns a scene-level holder for it until hub wiring lands.

---

## 2. Prefab dependencies (Phase D `PrefabHandoffRecord`s)

Prefab handoffs are filed **at runtime**, not as a static list: whenever
`ConstructionProjectExecutor.CreateProject` runs for a design whose
`RequiredPrefabId` was never registered via
`BuildingDesignCatalog.RegisterAvailablePrefab`, a `PrefabHandoffRecord` is
filed (`RecordId` like `pho-0`, `DesignId`, `RequiredPrefabId`,
`ExposedContract`, `Status = PendingCodex`) and the project proceeds as DATA
(`VisualPlacementPending = true`). To enumerate the current pending handoffs in
a running game, read `BuildingDesignCatalog.Handoffs`.

What each prefab must expose (the `ExposedContract` Codex must satisfy per
record): **footprint anchors, door/window sockets, snap points** — the exact
contract list is on the record (`PrefabHandoffRecord.ExposedContract`).

To clear a handoff: build the prefab exposing the contracted sockets, place it
for the completed project, and mark the record `AcknowledgedByCodex`. The data
pipeline (work packages → materials → labor → completion → accommodation spaces
→ occupancy) never blocks on visuals.

---

## 3. Adapters to implement (production)

### `IRentCashSink` (`Domains/Economy/Businesses/BoardingHouse/RentCollection.cs`)
```csharp
string BusinessInstanceId { get; }
string BusinessName { get; }
int CashCents { get; }
void RecordCashInflow(int cents, string label, int dayIndex);
```
Implement on the boarding-house runtime manager. `RecordCashInflow` MUST route
into `BusinessRuntimeState.AddCashCents` (the single business-cash writer) —
never a side ledger. Harness-faked in `BoardingRentCollectionTests`.

### `IBoarderCheckInPort` (`Domains/World/Property/HousingSearch.cs`)
```csharp
string CheckIn(int personId, int dayIndex, List<string> diagnostics);
```
Implement on the boarding-house runtime manager against the real
`BoardingHouseBoarderRegister` + `BoardingRoomInventory`. Returns null on
success, a refusal reason otherwise. Harness-faked in `HousingSearchTests`.

### `IConstructionMaterialSource` (`Domains/Economy/Businesses/Construction/NpcHomeConstruction.cs`)
```csharp
int AvailableUnits(ConstructionMaterialRequirement requirement);
string TryConsume(ConstructionMaterialRequirement requirement, int units,
    string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel);
```
`TryConsume` consumes REAL lots and returns the provenance label of the consumed
lots; on refusal it returns a reason and consumes NOTHING. Harness-faked in
`NpcHomeConstructionTests` / the Phase H slice.

### `IHouseholdToolCustody` (same file)
```csharp
bool HouseholdHoldsTool(int householdId, string toolItemId);
```
Reads the commissioning household's real `HouseholdInventory`. Self-build labor
is refused when the household does not hold the required tools.

### `INpcBusinessCashPort` (`Domains/Economy/Creation/NpcBusinessLifecycle.cs`)
```csharp
int ReadCashCents(string businessInstanceId);
string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose);
string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason);
```
Backs `NpcBusinessCashStore` (an `IRealCashStore`) so NPC business cash moves
through the same bridge as household/bank cash. Route onto the real
`BusinessRuntimeState` cash methods.

### `NpcBusinessCreationAdapter` (`Domains/Economy/Creation/NpcBusinessCreationAdapter.cs`)
Source-complete; Codex instantiates it in Unity and sets `PremisesAssigner`
(and optionally `AuthoredProfileLookup`). Runs NPC formation intents through the
real `BusinessCreationAuthority`. Unity-run surface — verify in-engine.

### Phase E bank deposit-ledger backing
`BankCreditParticipant` (`Domains/Economy/Financing/CreditLenderPolicy.cs`):
`Vault` = the bank's real specie (`PurseCashStore`); `DepositsOwedCents` must be
fed from `BankRuntime`'s `BankDepositLedger` (the comment on the class says the
deposit ledger "is owned by the bank runtime (Codex integration)"). Without
this feed the bank's reserve floor is computed against a stale number.

---

## 4. Visual / UI work reserved (§26 read models — source level, bind a UI to these)

No UI was built in Phases B–H (per the brief). The read models below are
source-complete and UI-ready:

- `HouseholdShoppingReadModel` (Phase C): `DescribePersonActivity(personId,
  dayIndex, minuteOfDay)` (what is this person doing right now),
  `PersonJourney(personId)` (journey legs of their latest trip),
  `PersonCustody(personId)` (goods in transit custody),
  `TripDecisionSummary(needSequence)` (the choice + every rejected alternative).
- `FinancialObligationReadModel` (`Domains/Economy/Financing/`): obligation
  schedules, balances, payment history projections.
- `SettlementEconomyReadModel` (`Domains/Population/`): built-up/rural/effective
  market population, wage employees, proprietor counts.
- `WorkforceStaffingReadModel` (`ReadModels/Reporting/`): FTE/headcount derived
  staffing (slots are NOT the authority).
- `EnterpriseValuationReadModel` (`ReadModels/Valuation/`): owner equity
  (Canon §11.3/11.4) — the First Ledger goal ladder binds to this, never to cash.
- `BusinessReportBuilder` / `PortfolioReportBuilder` (`ReadModels/Reporting/`):
  period business/portfolio reports.
- `NpcBusinessEventLog` (`Domains/Economy/Creation/NpcBusinessEvents.cs`):
  the source-level event log from Phase G — a UI timeline of NPC observations,
  investigations, formations, and life events binds here.
- Diagnostics lists on every service (`Diagnostics`, `diag` parameters) are the
  intended "why did this refuse" UI strings — they are written as human-readable
  refusal reasons, not codes.

---

## 5. SimulationDrivers wiring (daily/weekly services not yet driven)

`SimulationDrivers.OnDayChanged` / `OnWeekChanged` currently drive NONE of the
new services. Codex must add (all entry points are source-complete and
harness-tested):

**Weekly (`OnWeekChanged`):**
- Payroll: already runs via `SharedBusinessRuntimeManager`'s weekly cycle
  (`ResolveWeeklyPayroll` → `RecordWagePayment`) — verify it fires in scenes.
- `RentCollectionService.SettleDues(dues, rentCashSink, hub.HouseholdLedgers,
  memberships, hub.FinancialObligations, hub.Ids, dayIndex, diag)` — dues from
  `BoardingHouseBoarderRegister.RentDueWeekly/RentDueMonthly`.
- `NpcBusinessLifecycle.EvaluateWeek(...)` — struggle/borrow/pivot/close-down
  evaluation for every live NPC business record.
- `TradeCreditBook.EvaluateDay(...)` — actually runs per-day (see below); at
  minimum weekly for arrears/collection-stage transitions.

**Daily (`OnDayChanged`):**
- `DailyNeedsService.ExecuteDay` — ALREADY wired (NX-2C). Keep.
- `HouseholdShoppingLoop.ExecuteNeed(need, dayIndex, options)` — for each open
  `HouseholdPurchasingNeed` (from `hub.PurchasingNeeds`); needs are produced by
  the shortage monitor (`DriveHouseholdShortageMonitor` — already wired).
- `TradeCreditBook.EvaluateDay(hub.FinancialObligations, dayIndex, diag)` —
  invoice terms, arrears, collection stage per Tech §6.4.
- `ConstructionProjectExecutor.AdvancePackage(project, package, dayIndex,
  diag)` — advance in-progress work packages (materials + person-time already
  committed by the executor).
- `NpcPropertyLife` maintenance/tax/delinquency processing
  (`ProcessTaxDelinquency(...)` et al.) for NPC-held properties.

**Event-driven (no driver tick needed, but must be called from the flows that
create the events):**
- `NpcOpportunityObservation.RecordObservation(observation, eventLog)` — call
  when an NPC witnesses unmet demand (market/shortage events).
- `NpcBusinessInvestigation.Start(...)` — when an opportunity crosses the
  recognition threshold.
- `NpcBusinessFormation` / `NpcBusinessCreationAdapter.TryCreateBusiness` —
  when an investigation concludes "proceed".
- `HousingSearchExecutor.Execute(...)` — when a household needs housing
  (autonomous search, Phase D).
- `CreditCashBridge` windows must be opened/committed around every credit
  workflow call (see the `OpenWindow` → mutate → `CommitWindow` pattern in
  `CreditLenderPolicy`/`NpcCreditDecisions` — copy it, do not invent a new one).

---

## 6. Save/load wiring still open (from the Phase I save/load doc)

12 services have `CaptureSaveDto`/`LoadFromSaveDto` ready but are NOT referenced
in `SimulationSystemsHub` or `SaveLoadManager`: `PersonScheduleTracker`,
`RentCollectionService`, `NpcBusinessFormation`, `NpcBusinessLifecycle`,
`NpcBusinessInvestigation`, `NpcOpportunityObservation`, `NpcBusinessEventLog`,
`NpcPropertyLife`, `ConstructionProjectExecutor`, `NpcConstructionCommissioner`,
`TradeCreditBook`, `CreditEventLog`. Until wired, their runtime state resets on
load (money/obligation legs survive; workflow state does not). All `Load`
methods clear before loading, so wiring them is additive and cannot duplicate.

---

## 7. EXACT manual Unity tests

Run in the Unity editor (Play mode) after the wiring above. Each test names the
setup, the steps, and the expected result.

**T1 — Wage crediting end-to-end.**
1. New game, note household H1's ledger balance via `hub.HouseholdLedgers`
   (debug inspector or a temporary debug log line).
2. Employ a member of H1 at the general store (assign a worker slot with a
   weekly wage), run to the weekly payroll boundary.
3. EXPECT: business cash decreased by the wage total; H1's ledger shows a
   `RecordWagePayment` inflow with the employment id as provenance; no
   `DepositWeeklyHouseholdIncome`-style fiat entry exists.

**T2 — Shopping loop with real cash.**
1. Give H1 a funded ledger; create a purchasing need (shortage monitor) for
   flour.
2. Run a day with `ExecuteNeed` driven.
3. EXPECT: a `ShoppingTripResult` in `loop.TripHistory` with JourneyLeg events;
   H1's ledger outflow equals the store's recorded revenue cent-for-cent;
   the store's `CategoryStockState` decreased by the units sold. No
   `spendingMoneyCents` movement.

**T3 — Rent collection and arrears.**
1. Check a boarder into the boarding house (via the `IBoarderCheckInPort`
   adapter); run `SettleDues` with a funded tenant household.
2. EXPECT: tenant ledger outflow == boarding-house `AddCashCents` inflow;
   `RentCollectionService` diagnostics log the conserved amounts.
3. Empty the tenant ledger, run `SettleDues` again.
4. EXPECT: a real `FinancialObligation` (Payable) exists for the remainder with
   debtor `household:<id>`; `RentArrearsRecord.Status == Open`; nothing was
   faked.

**T4 — Housing search execution.**
1. Create a homeless household; run `HousingSearchExecutor.Execute` with a
   rental decision.
2. EXPECT: `HousingAuthority` shows a new occupancy + property agreement; for a
   boarding decision, the boarder register shows the check-in.

**T5 — Credit workflow with real cash.**
1. Register a `HouseholdCashStore` and a lender `PurseCashStore` in the
   `CreditCashBridge`; run a `CreditOfferWorkflow` loan end-to-end.
2. EXPECT: lender store debited and borrower store credited by exactly the
   principal; a `FinancialObligation` + `CreditRegistry` instrument exist;
   repaying reduces the obligation balance (re-read, never a second balance).

**T6 — Construction project data pipeline.**
1. Register a design with a `RequiredPrefabId` that has NO prefab registered.
2. Run `CreateProject` → `CreateWorkPackages` → stage materials → advance
   packages to completion.
3. EXPECT: `PrefabHandoffRecord` filed (`Status == PendingCodex`,
   `ExposedContract` lists footprint/door-window/snap points);
   `project.VisualPlacementPending == true`; building completes as DATA with
   accommodation spaces; materials were consumed from real lots (provenance
   labels); no synthetic stock appeared.

**T7 — NPC business formation.**
1. Record observations of unmet demand for an NPC household until recognition
   fires; run investigation to "proceed"; run formation through
   `NpcBusinessCreationAdapter` with `PremisesAssigner` set.
2. EXPECT: founder cash moved through a real store (delta-committed, conserved);
   the business exists in the creation authority; formation with insufficient
   cash is refused with cash refunded and no partial state.

**T8 — Save/load round trip.**
1. With T3 arrears open, a construction project in progress, and NPC
   observations recorded, save and reload (after completing §6 wiring).
2. EXPECT: identical arrears, projects, observations, event log, life records —
   counts and ids match; no duplicates; ledger balances unchanged by the
   reload; the legacy-cash migrator books nothing on the second load.

**T9 — Vertical slice sanity.**
1. Run the `HouseholdVerticalSlice` orchestration for 7 simulated days.
2. EXPECT: every day's cash movements reconcile (sum of household ledger deltas
   + business cash deltas + obligation balance deltas == 0, up to recorded
   external trade); diagnostics contain no "refused" lines except intentional
   insufficient-funds cases.

**T10 — Bank reserve honesty.**
1. Set `BankCreditParticipant.DepositsOwedCents` from the real deposit ledger,
   then change deposits (new deposit) WITHOUT refreshing the field; observe
   `ReserveFloorCents`.
2. EXPECT (documents the integration contract): the reserve floor is stale until
   Codex refreshes the feed — this test exists to prove the feed is live, not
   to pass on a stale number.
