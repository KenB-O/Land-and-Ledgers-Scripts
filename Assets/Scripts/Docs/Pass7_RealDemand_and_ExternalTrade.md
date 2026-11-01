# Pass VII Workstream A: Real Demand and External Trade

## What already existed

Businesses could receive revenue through `SharedBusinessRuntimeManager.TrySellTownPulseDemand` and `TrySellTownPulseDemandFromBusiness` (Domains/Economy/Core/SharedBusinessRuntimeManager.cs). The town-pulse pathway credited business cash without any buyer, purchase, or counterparty. `BusinessRuntimeState.ResolveDailySalesPlaceholder` and the live caller `GeneralStoreRuntimeManager.ResolveTownPulseDemand` depended on it. Pass VI (Document C chain 24, P1-1) confirmed this violated the project's own anti-manufacturing rule and invalidated economic analysis.

Real purchase infrastructure already existed: HouseholdLedger as the single cash writer, FinancialObligationAuthority as the single debt authority, inventory lots with provenance, and the Pass IV task and reservation authorities.

## What was disconnected

The town-pulse path bypassed every real economic authority. Household needs existed but did not produce purchases. Businesses had no legitimate demand source for support-industry goods. External trade had no modeled counterparty.

## What was changed

Town-pulse retirement (worker 1, merge 14588e1):
- `TrySellTownPulseDemand` now returns 0 units with `revenueCents = 0`. It no longer mutates any business financial state.
- `TrySellTownPulseDemandFromBusiness` records buyerless demand as unfulfilled in the operation summary (`"; town-pulse demand for {n} {category} unfulfilled (no buyer)"`) so managers can see and respond to it, without manufacturing revenue.
- `BusinessRuntimeState` and `GeneralStoreRuntimeManager` updated to route through the real demand source.

RealDemandSource (Domains/Economy/Core/RealDemandSource.cs, 483 lines):
- Connects Household needs (food, fuel, clothing, household goods, animal feed) and Business needs (raw materials, tools, fuel, transport, maintenance, storage, labor, equipment) to actual purchases.
- Every purchase follows the 10-step legitimate path: buyer identity, actual need, buyer knowledge, supplier access, available inventory, valid price and terms, purchasing power or legitimate credit, fulfillment, settlement or debt recognition, goods transfer.
- No money is created because a demand calculation was positive. A customer without funds or credit cannot complete a paid transaction. A credit purchase records proper debt and receivable.

ExternalMarketAuthority (Domains/Economy/Core/ExternalMarketAuthority.cs, 513 lines):
- Models external counterparties with bounded budgets, real demand or orders, actual prices and terms, shipment fulfillment, and cash or receivable settlement.
- External inflows are explicitly accounted at the modeled boundary. Local cash conservation is not claimed across external inflows.
- External purchasers do not have infinite budgets, unlimited demand, or instantaneous fulfillment.

## Why the correction follows Canon

Canon XXXI requires real counterparties for economic transfers and forbids manufactured revenue. The town-pulse path was a direct violation. The replacement uses only the existing single cash writer and debt authority, preserving the one-truth-per-asset rule.

## Authoritative systems reused

HouseholdLedger, FinancialObligationAuthority, inventory lots with provenance, Pass IV IUnifiedReservationAuthority and task framework. No new ledger, no new inventory authority, no new task engine.

## Source entry points

- `RealDemandSource.CollectDemand`: gathers household and business needs into purchase intents.
- `RealDemandSource.RegisterBusinessInputNeed` and `ClearBusinessInputNeeds`: business demand registration.
- `BusinessInstanceSellerAdapter.Sell`: the actual sale execution through the business inventory.
- `ExternalMarketAuthority.RecordExternalSale` and `RecordExternalPurchase`: boundary transactions with provenance.
- `ExternalMarketAuthority.GetBoundaryReconciliation`: inflow, outflow, and net at the modeled boundary.
- `SharedBusinessRuntimeManager.TrySellTownPulseDemand`: now a no-revenue unfulfilled-demand recorder.

## Tests that prove it

- RealDemandTests (18 passed): SettledSale_EmptyBuyer_Refused_NothingMoves, SettledSale_WithBuyer_MovesStockAndCashExactlyOnce, SliceA_GenuineCommercialRevenue_FullChain, SliceA_NoDemand_NoIntents_NoSale.
- ExternalTradeTests (10 passed): ExternalTrade_ProvenanceSurvivesSaveLoad, SaveLoad_CannotDuplicatePurchases, SliceB_BoundaryReconciliation_InflowsOutflowsNet, SliceB_ExternalSale_BoundedBudget_LocalSettlementWithProvenance.

## What remains source-only

The demand source is wired to the purchase path and proven in the harness. Ordinary Main Scene activation of the demand evaluation cadence (how often households evaluate needs in live runtime) is a Codex integration item.

## What Codex must connect in Unity

- Schedule RealDemandSource evaluation in the live daily tick at an appropriate cadence.
- Wire ExternalMarketAuthority boundary accounting to the live economic reporting UI.
- Validate the no-demand case in PlayMode: businesses with no customers must show zero sales revenue over multiple days.
