# Review 2 Vertical Slice Pass D3 — Review

Pass D3 is the third and last vertical slice pass. It finishes the
decision-free vertical slice Roslyn work left open by D2 and hands
everything remaining cleanly to the second scripts pass. Base: D2 tip
`3c8f97a921476afc5619b2ae6f56f2acd6f6aae7`
(`muse/review2-vertical-D2`). This branch: `muse/review2-vertical-D3`.

What D3 claims, in one sentence: D2's in-memory horse-feed
registrations now survive a save/restore boundary, and the fuel,
freight, and hiring chains are traced end to end on production
driver/adapter classes with exact cash/stock reconciliation.
What D3 does NOT claim: Unity compilation, Test Runner, PlayMode,
scene wiring, or the final Unity import GUID check. New GUIDs are
checked only via `git grep` (mutually distinct, once each).

## 1. What changed (file paths)

New files (fresh GUIDs, never copied `.meta`):

- `Assets/Scripts/Infrastructure/Persistence/Review2VerticalD3Save.cs`
  (+ `.meta`, GUID `0b568b4ebfbf46c99113035d99209337`) — the D3 save
  section: `Review2D3RuntimeItemDto`, `Review2D3StoreFeedDto`,
  `Review2VerticalD3SaveDto`, and `Review2VerticalD3SaveState`
  (Capture deep copy, Restore clear-then-load, idempotent).
- `Assets/Scripts/Tests/Editor/Economy/Review2VerticalD3Tests.cs`
  (+ `.meta`, GUID `28bed9b1ed27490981f325bbb447b532`) — 8 new tests.
- `Assets/Scripts/Docs/Review2VerticalD3Review.md` (this file)
  (+ `.meta`, GUID `cbf28bcdc5994a808b31b2bd1d6f67ea`).

Edited files, additive only:

- `Assets/Scripts/Domains/Economy/Core/Review2StoreHorseFeed.cs` —
  one additive method only: `Review2StoreFeedRegistry.Clear()`,
  used by save Restore. No other behaviour changed.
- `Assets/Scripts/Infrastructure/Persistence/PassXlSliceSaveDto.cs` —
  one additive field only: `review2VerticalD3`, null on old saves.
  No existing field renamed, removed, or reordered.
- `Assets/Scripts/Infrastructure/Persistence/PassXlSliceSaveCoordinator.cs` —
  one additive Capture overload and one additive Restore overload
  (the D3 section). All earlier overloads unchanged in behaviour.
- `Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs` —
  one additive property (`Review2VerticalD3SaveState`, following the
  Pass 10 property pattern exactly) and the two call sites updated
  to the new overloads. This edit is Unity-side: Unity compilation
  of `SaveLoadManager` is Codex / Unity AI work, NOT claimed by the
  Roslyn harness (which covers the coordinator overloads and the
  D3 state class). The property is null-safe: a null state captures
  a null section and restores empty with a diagnostic.

No `.asset` deletions, no fixture authoring, no repository
visibility/permission changes, no Main Scene / prefab /
ProjectSettings changes. No named-person fixture data anywhere:
every person in the D3 tests is a generic test person.

## 2. First priority, done: D3 save adoption (D2's open item)

D2 left runtime item registrations and store feed registrations
in-memory only: a restored world fell back to null / unknown-item
with diagnostics, never phantom feed. D3 closes that gap:

- Persisted values are exactly the caller-supplied registration
  values, verbatim. No feed item id, price, or unit is invented or
  canonised by persisting it. Prices are NOT in this section:
  prices live at the selling store's offer, as in D2.
- Capture reads store feeds from the registry, not from the mirror
  list, so a feed item added straight through
  `Review2StoreHorseFeed.AddFeedItem` is seen even if the driver
  never touched the state lists. `RegisterRuntimeItem` on the state
  registers in the static catalog AND records the DTO in one call,
  so Capture cannot miss a registration the driver made.
- Restore is clear-then-load and idempotent: restoring twice yields
  the same lists, never doubles. A null DTO, or a DTO with null
  lists, restores empty with a diagnostic (the old-save default).
  Entries with empty ids, or a store conversion of 0 or less, are
  skipped with a diagnostic, never stored as usable (0 means "not
  supplied", never a silent 1:1).
- A conflicting catalog definition on restore is diagnosed and the
  existing definition wins, while the state list still records what
  the save said, so the divergence is visible, never silently
  overwritten.

Honest limits, stated in the code, not hidden:

- Store category STOCK is not in this section; it lives in the
  business runtime save. The mid-run test keeps the same
  `BusinessRuntimeState` object across the boundary, exactly as a
  runtime save would restore it, and proves the REGISTRATION
  survives. Restoring registrations alone must NOT be claimed to
  restore stock.
- The static `HouseholdItemCatalog` runtime dictionary
  (`Assets/Scripts/Domains/Population/HouseholdInventory.cs:44`,
  `RegisterRuntimeItem` at line 155) is per-process, not per-world.
  Restore re-registers DTO items into it (identical = idempotent,
  conflicting = diagnosed, existing wins). The state lists ARE
  clear-then-load; the catalog simply cannot be cleared by this
  class, and no method here pretends otherwise. A driver running
  two worlds in one process must register identical items in both
  or accept the conflict diagnostic.
- Feed-need origination (who opens a feed need, at what quantity)
  is NOT in this section and NOT decided here. Where a test needs
  a need, the caller constructs it and the test says so.

## 3. A-to-Z traces as run, with exact reconciliation

All traces run on production driver/adapter classes only, never on
an orchestrator as the system under test. New tests (8):

### 3a. Save traces (5 tests)

- `D3Save_MidRunSaveRestore_StoreFeedAndRuntimeItemStillFeed`:
  runtime item `d3-test-horse-feed` + store feed registration
  (category `d3-feed-cat`, 2 store units per feed unit) captured,
  restored into a fresh state, then: store horse fed from 20 store
  units (10 feed units), idle ration draws exactly 4 store units
  (16 left), nutrition does not degrade; person-owned horse fed
  from household inventory (6 units in, idle ration draws 2, 4
  left). `AddLot` rejecting unknown items is the proof the restored
  runtime item is known to the catalog again.
- `D3Save_OldSaveWithoutD3Section_RestoresEmptyWithDiagnostic_AndNoPhantomFeed`:
  null DTO and a DTO without the section both restore 0 runtime
  items, 0 store feeds, registry resolves null, diagnostic names
  "no D3 section in save".
- `D3Save_RestoreTwice_IsIdempotent_AndSkipsInvalidEntries`:
  empty-id runtime item and zero-conversion feed item skipped with
  diagnostics; second restore yields the same 1 + 1 state; the
  restored registration still feeds (20 -> 16 store units).
- `D3Save_CoordinatorOverload_RoundTripsD3Section_AndNullStateDefaultsEmpty`:
  coordinator Capture/Restore carry the section; null state
  captures null and restores empty; present section with no state
  holder is diagnosed, never silently dropped.
- `D3Save_ConflictingCatalogDefinition_IsDiagnosed_AndExistingDefinitionWins`:
  conflicting restore keeps the save record in the state list for
  diagnosis while the existing catalog definition wins.

### 3b. Fuel chain (1 test)

`D3Fuel_DealerToBakery_ThenDealerToHousehold_ExactReconciliation`
chains Pass 8 links into one trace (the join is the new claim, not
any single link). 20 intake units in (two 10-unit sawmill lots,
receipt diagnoses "no sawmill payment", never a silent 0c price):
6 supplied to the bakery fuel store (dealer 14, bakery lot carries
the dealer reference, not an orphan); a 30-unit request against 14
refuses in full before any unit moves (pre-check-first, re-asserted
after stock has been drawn); 4 units sold through the supplier
adapter at the caller-supplied 28c (dealer 10, dealer cash +112c
exactly); those 4 credited to the household fuel reserve at the
caller-supplied factor 2 (8 units). Whole-trace check:
20 = 6 + 4 + 10, no unit created or lost. Null household and zero
quantity both refuse with -1, crediting nothing.

### 3c. Freight legs (1 test)

`D3Freight_CarrierLeg_AcceptanceGatedSettlement_SurvivesSaveRestoreBoundary`
chains the Pass 8 carrier leg with the Pass 9 save decision.
Dealer-to-bakery leg, 10 units, 250c, through the real
`FreightProductionDriver`: completed, 10 units accepted through the
callback exactly once (no double stock). Settlement: payer
1000c -> 750c, carrier 0c -> 250c, exactly the freight charge.
Save/restore boundary: a fresh settlement object rebuilt from the
restored shipments (`RestoreFromShipments`) refuses the same leg,
because the shipment's own `freightRevenueCollected` flag — which
IS in the shipment save DTO — is the settlement authority across
saves, not the in-memory set. Cash unchanged by the refusal.
No double collection, in either direction.

### 3d. Hiring trace (1 test)

`D3Hiring_MissedWork_AssessorToBrain_IntentOnly_EffortIsNotAHire`
proves the chain and where it stops in Roslyn:

1. `LaborDemandAssessor` on missed work (2400 open Production
   minutes, 0 available employee minutes, waiting line 6, 3 recent
   balks): `TotalUnmetMinutes` > 0, `CoverageGap` true,
   `HiringPressure` true. The assessor reports evidence only —
   never a headcount target, quota, or staffing order.
2. `ManagerBrain.DailyCycle` within delegated Staffing scope:
   `Acted`, `ActionKind` = `OpenRecruitment`, `ResultingReference`
   = `intent:recruitment-effort:bakery-1:d1`, outcome wording
   "channel costs and effort creation stay with the integrator".
3. The stop, proved: the intent alone creates 0 efforts in a fresh
   `RecruitmentService` and 0 employment relationships. The
   brain-to-effort link in production is
   `SimulationDrivers.ExecuteManagerRecruitment`
   (`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:3041`),
   which opens a WordOfMouth effort via `hub.Recruiting.OpenEffort`
   — that driver code EXISTS, but it is Unity-side (MonoBehaviour,
   hub wiring, per-day invocation at line 2673) and is NOT
   Roslyn-provable, so it is not claimed here.
4. The integrator step, performed explicitly so its cost is
   visible: `OpenEffort` with the Handbill channel records 150c
   printing on the effort (the caller books that outflow through
   the normal expense flow; no cash movement is claimed here).
   Even after the effort opens, the registry still holds 0
   relationships: an effort is not a hire. A hire needs a real
   candidate inquiry and acceptance, which this trace deliberately
   does not fabricate. No named person is hired anywhere.
   Note: the driver's production channel is WordOfMouth, not
   Handbill; the 150c in this test is the service's recorded cost
   for the Handbill channel, not the driver's path.

## 4. Verification (Roslyn harness only)

- Main harness `harness-D3.csproj` (retargeted from D2, plus the D3
  production file, the D3 test file, `ManagerBrain.cs`, and
  `StandingInstruction.cs`): **123 passed, 0 failed** — D2's 115
  plus the 8 new D3 tests. Series 2, Pass A, Pass C, D1, D2 suites
  all re-run inside that count; no Pass A pin, Pass C ItemSkuMap,
  D1 wholesale driver, or D2 feed behaviour was weakened, and no
  existing test was changed.
- Phase H harness `harness-D3-phaseh.csproj` (freshly built binary
  for this tip, plus the D3 save file and the two D2 feed files the
  coordinator now transitively needs): **86 passed, 3 failed** —
  exactly the 3 baseline failures, unchanged:
  `EndToEndTrace_InsufficientFunds_FailsThenRecovers` (expected
  true); `Secondary_Hardship_MissedMeals_NoInvisibleReplenishment`
  (expected string containing 'unaffordable', got '');
  `Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds`
  (expected 60, got 59). Nothing in D3 fixed or worsened them, and
  they are not re-reported as new.
- `ConstructionInputSupportTests` NOT re-run (unchanged, per task).
- New GUIDs checked via grep (mutually distinct, once each):
  `0b568b4e...` (D3 save), `28bed9b1...` (D3 tests),
  `cbf28bcd...` (this review doc's `.meta`). Final Unity import
  GUID check is still Codex work.
- `SimulationDrivers` branch itself is NOT Roslyn-testable
  (MonoBehaviour, excluded from the harness): the domain resolvers
  it calls ARE tested, including after restore, but the
  registration call site, per-day household adapter construction,
  and CareDay wiring need Unity verification. Say so, and D3 does.

## 5. What was checked and left alone

- Pass A pins (retail WARNING-only cash, bakery DayIndex gating,
  dealer fuel partial supply), Pass C ItemSkuMap and weekly
  converter guard, D1 wholesale driver and shelf consume method,
  D2 feed behaviour (store leg, person leg, runtime registration
  rules): all re-run, all passing, none changed. Any future change
  to them must update their tests explicitly and say why.
- `SimulationDrivers.ResolveHorseFeedStock`
  (`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:2445`):
  third D2 branch after farm/livery, farm/livery untouched, store
  without an added item still null with a diagnostic. Left alone;
  D3 only proves its domain resolvers survive restore.
- `SimulationDrivers.ExecuteManagerRecruitment` (line 3041):
  exists and opens a WordOfMouth effort; left alone. Its Unity-side
  execution is the second scripts pass's to verify, not D3's to
  re-prove in Roslyn.
- Freight driver collection scan vs `Series2CarrierSettlement`:
  both directions guarded by `freightRevenueCollected` (Pass 8/9
  design); D3 chains the settlement side across a restore and
  leaves the driver scan untouched.
- General Store profile-vs-definition dual authority (Pass B/C
  surface): still surfaced, not resolved — deleting or overwriting
  one authority is a decision D3 was told not to make.
- No decision was taken on: flour/fuel conversions as canon,
  item-level stock migration, player-fund reconciliation,
  mortality/work-eligibility thresholds, freight payer/commission,
  seconds, meal/sleep durations, departure gate, the 4% roll,
  double-ordering guard, friend graph, any price/rate as canon, or
  any horse feed item id/price/unit as canon. Every such value in
  D3 tests is a caller-supplied test parameter.

## 6. Precise list for the second scripts pass (bottlenecks still open)

With evidence and file/line references. Nothing here is a D3
regression; each item is a seam D3 traced to and stopped at.

1. **Unity-side D3 save wiring is unwired until a driver assigns
   it.** `SaveLoadManager.Review2VerticalD3SaveState`
   (`Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs:188`,
   call sites lines 481 and 804) is null until a driver sets it
   before saving AND before loading, exactly as for Pass 9/10.
   Until then, saves carry an empty D3 section (or a present
   section does not restore) with a diagnostic — safe, but the
   feature is dormant in-game. The second scripts pass must trace
   which driver owns the D3 state, assign it in the same place
   the Pass 9/10 states are assigned, and verify in Unity that a
   real save/load round-trips a store feed registration.
   Evidence: coordinator null-state test passes, which proves the
   dormant path is diagnosed, not that the live path is wired.
2. **`SimulationDrivers` registration call site, per-day household
   adapter construction, and CareDay wiring need Unity
   verification.** The D2 third branch
   (`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:2445`,
   registry field line 105) and the driver call at line 2434 are
   MonoBehaviour code excluded from the harness. D3 proves the
   domain resolvers (`Review2StoreHorseFeed.FeedStockForHorse`,
   `Review2HouseholdHorseFeed`) including after restore, but
   nobody in Roslyn proves the driver constructs the household
   adapter per day (the adapter's own contract: never reuse one
   across days, since `Consume` records the day index) or that
   CareDay is wired to the restored registry after a load.
   Verify in Unity/PlayMode; a stale adapter across days would
   misdate consumption, and a post-load CareDay wired to a fresh
   empty registry would silently unfed store horses.
3. **Feed-need origination is still undecided (Codex/Kennedy).**
   Nothing in D3 decides who opens a feed need or at what
   quantity; tests construct needs by the caller and label them
   as such. Until origination is decided, a restored registration
   enables feeding but does not by itself cause any horse to be
   fed — the driver must still open the need. This is the largest
   functional gap behind the save work, and it must not be filled
   by silently defaulting a quantity.
4. **Static catalog is per-process, not per-world.**
   `HouseholdItemCatalog` (`Assets/Scripts/Domains/Population/HouseholdInventory.cs:44`)
   holds runtime items in a static dictionary. D3 documents and
   diagnoses the conflict case, but a genuine per-world reset
   (or a scoped catalog) is a design decision for the second
   scripts pass if multi-world-in-one-process ever matters. Do
   NOT "fix" it by clearing the dictionary on restore without
   that decision: a clear would break any other live world
   sharing the process.
5. **Manager recruitment execution is Unity-side only.**
   `ExecuteManagerRecruitment`
   (`Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs:3041`,
   invoked line 2673) opens a WordOfMouth effort and surfaces the
   effort id in diagnostics, but Roslyn cannot prove the hub
   wiring (`hub.Recruiting`), the per-day invocation, or that the
   effort's cost flow (WordOfMouth is cost-free by channel, unlike
   the Handbill 150c in the D3 test) matches the expense booking
   the service's contract requires for paid channels. Trace it in
   Unity: missed work -> assessor -> brain intent -> driver effort
   -> inquiry -> employment, and confirm no step auto-hires a
   person the player never approved.
6. **Freight double-collection across the driver scan.**
   D3 proves the settlement side refuses a restored leg
   (`freightRevenueCollected` flag, shipment save DTO). The other
   direction — a driver tick scan collecting a leg the settlement
   already paid — relies on the same flag in
   `Assets/Scripts/Domains/Economy/Businesses/Freight/FreightProductionDriver.cs`.
   The second scripts pass should trace one leg through BOTH paths
   in sequence in a Unity run and confirm exactly one collection,
   since Roslyn can only prove each path's guard in isolation.
7. **General Store profile-vs-definition dual authority.**
   Surfaced in Pass B, untouched since. The second scripts pass
   should decide (not silently delete) which authority wins when
   a store profile and a store definition disagree, because the
   D3 feed registration reads the runtime category while offers
   may read the profile — a disagreement there would make a
   registered feed item unsellable or a sold item unregistered.
8. **Phase H's 3 baseline failures are still open.**
   Listed in section 4 with exact expectations. They predate D3,
   are unchanged by D3, and belong to the second scripts pass or
   Codex, not to a silent D3 fix. In particular the 60-vs-59
   restock gap smells like a one-unit reconciliation leak in the
   procurement chain and deserves a traced count before any
   "fix" is attempted.
9. **Weekly converter guard and item-level stock migration.**
   Pass C closed a latent mint (up to 36 staple_food/week from
   nothing) with a guard; the guard's long-run interaction with
   real weekly conversion, and any migration from per-category
   pools to per-item stock, remain open design items the second
   scripts pass should trace before touching either.
10. **Player-fund reconciliation and the player-household
    spending-permission setting.** Not started. D3 moves no
    household cash and sets no permission; the second scripts
    pass must not invent a reconciliation rule to make a trace
    balance — if a trace does not balance, the gap is the finding.

## 7. What still needs doing (after D3)

- Second scripts pass (bottleneck tracing A to Z, meaningful
  changes) — items in section 6 are its starting list.
- Second ScriptableObjects pass (descriptive objects, same
  tracing).
- Final verification cleanup pass, to get everything ready to be
  pulled.
- Pull, with the final cleanup branch as the pull target — NOT
  this branch, NOT Pass 11, NOT any intermediate branch.
- Codex / Unity: Unity compilation of the `SaveLoadManager` edit,
  Test Runner, PlayMode, scene/prefab wiring, final Unity import
  GUID check, and the Unity-side verifications in section 6 items
  1, 2, 5, and 6.
- Kennedy decisions still open (unchanged by D3): feed-need
  origination; flour/fuel conversions as canon; item-level stock
  migration; player-fund reconciliation; mortality/work-eligibility
  thresholds; freight payer/commission; seconds; meal/sleep
  durations; departure gate; 4% roll; double-ordering guard;
  friend graph; all prices/rates/weights/frequencies; General Store
  profile-vs-definition authority; horse feed item id/price/unit
  as canon (all D3 values are caller-supplied test parameters).
