# Codex Campaign 03 — Food Economy Checkpoint

Date: 2026-11-21  
Branch: `codex/muse-unity-integration-2026-11-21`  
Baseline: `b822455a645506b21a3b0f72a1bc5edaf813df64`  
Unity verification: **not run**. The only open Unity Editor is on the owner's separate project; opening this integration checkout would create a competing Editor and consume substantial memory.

## Implemented in source

- Added explicit bakery channel choices: Retail, Wholesale, and Retail + Wholesale. The choice is scenario-authored, recorded as a business decision, persisted in the bakery save DTO, and changeable through `BakeryShopRuntime.SetSalesChannels`. Changing to retail-only refuses new wholesale agreements but preserves already accepted agreements and their fulfillment obligations.
- Configured Glenn's development-town bakery as Retail-only, with the existing retail-protection allocation. The development-town opening has no wholesale agreement. Bootstrap now resolves the channel before applying explicitly authored agreements; no fallback agreement is introduced.
- The retail supplier directory now hides the bakery when retail is disabled, and the retail sale, retail availability, and ordinary retail batch planner honor the selected channel.
- Closed a second path into retail production: the staged-batch gate now refuses and names the reason when the bakery's retail channel is disabled.
- In the live `SimulationDrivers` path, disabled the conventional daily retail batch planner. That path still handles accepted wholesale obligations; open household retail demand is left to the existing persistent staged `TaskAuthority` chain. Legacy callers retain the conventional planner by default, and existing conventional in-flight batches remain eligible to finish.
- Closed a product-identity leak in `GeneralStoreSupplierAdapter`: generic `staple_food` balance can no longer be quoted or sold as bread (or another individual staple). An exact item-lot path is required. Unambiguous single-product categories retain their category path; multi-product categories require identified lots. Refusals leave stock and cash untouched.
- Confirmed by source inspection that legacy reserve migration retains unidentified historical food under `UnspecifiedStapleFoodId`; it does not relabel it as bread. The active development-town multi-SKU store offer path uses exact item lots.

## Regression coverage added or extended

- `Lane10aBakeryChannelChoiceTests`: authored retail-only opening/no agreement; distinct channel enablement; retail transaction permission; refusal of new wholesale agreements under retail-only; wholesale-only retail refusal without stock loss; save/load of a changed channel while preserving accepted contracts.
- `BakeryWholesaleDailyDriverTests`: live task-backed mode leaves retail demand and unconsumed inputs for the staged pipeline while the conventional daily path remains the default for legacy callers.
- `SimulationDriversSceneBindingTests`: staged production does not begin when retail is disabled and reports that policy as its refusal reason.
- `SinglePersonShoppingTests`: the exact item-lot staple sale still conserves stock and money; pooled generic `staple_food` cannot sell as bread, settle cash, or create a receipt.
- `BakeryFoodChainEndToEndTests`: the bakery-to-household case explicitly selects Retail-only, checks seller discovery and physical shopping-loop purchase, reconciles household/bakery cash and bakery stock, receives actual bread, and consumes three purchased loaves in later scheduled meals.

These tests have **not been executed**. The test source is not runtime evidence.

## Existing paths checked and remaining status

- Bakery direct retail adapter, seller registration, supplier knowledge, route authority, sale settlement, household receipt, and lot-backed meal consumption already have a focused end-to-end Editor test. That test is stronger than an isolated unit test but does not establish actual Main Scene movement or counter service.
- Shopping task liveness, task recovery, and seller service are already implemented in connected code from earlier work. This pass did not change them absent new runtime evidence.
- Bakery conventional daily production and staged production both exist. The live path now assigns retail batch creation to the staged `TaskAuthority` chain and leaves the conventional daily driver available for wholesale obligations. Active conventional work still blocks a staged batch. Actual worker travel, equipment approach, and production progression remain unverified in Unity.
- No player-facing runtime control for channel changes was found. The scenario setting and persisted runtime decision method exist, but wiring a visible player business-management control remains outstanding if the current product UI is expected to expose this owner choice.
- Sustained household replenishment is not proven. No new long-run simulation was performed; five-day consumption of opening food is not counted as replenishment evidence.

## Verification backlog

1. Compile the current source in Unity 6000.4.2f1 on the integration branch.
2. Run the new/updated channel, staged-production gate, generic-staple identity, bakery food-chain, shopping, and SKU receiving tests.
3. In the Main Scene, capture a complete resident bread purchase: resident and need/trip IDs; seller discovery; physical arrival and reachable counter; physically present server; bread lot ID; household debit and bakery credit; one stock decrement; physical return; household receipt; later consumption of that same SKU.
4. Demonstrate household replenishment before opening pantry exhaustion and record each household's cash, purchases, receipts, and meals. Distinguish poverty, stock shortage, route/time failure, clerk unavailability, and task recovery.
5. Verify Save/Continue with Retail-only selected, and verify no 24-loaf fallback account appears after load.
6. Decide whether channel changes need an in-game player-facing control; currently the domain decision API is persisted and callable but has no visible runtime caller.

## Files changed by this campaign

- `Assets/Core/Population/DevelopmentTownConfiguration.asset`
- `Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs`
- `Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryWholesaleAccounts.cs`
- `Assets/Scripts/Domains/Economy/Businesses/Bakery/Series2BakerySupplierAdapter.cs`
- `Assets/Scripts/Domains/Economy/BreadDrivers/BakeryWholesaleDailyDriver.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreSupplierAdapter.cs`
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs` (corrected stale pooled-stock description)
- `Assets/Scripts/Domains/Economy/Businesses/GeneralStore/Series2TradingPortBuilder.cs` (corrected stale shared-pool description)
- `Assets/Scripts/Domains/Population/DevelopmentTownConfiguration.cs`
- `Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs`
- `Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs`
- `Assets/Scripts/Tests/Editor/Economy/BakeryFoodChainEndToEndTests.cs`
- `Assets/Scripts/Tests/Editor/Economy/BakeryWholesaleDailyDriverTests.cs`
- `Assets/Scripts/Tests/Editor/Economy/Lane10aBakeryChannelChoiceTests.cs`
- `Assets/Scripts/Tests/Editor/Orchestration/SimulationDriversSceneBindingTests.cs`
- `Assets/Scripts/Tests/Editor/Population/SinglePersonShoppingTests.cs`

No commit was created. Earlier Campaign 02 work and unrelated owner-local edits remain in the working tree and were not removed.
