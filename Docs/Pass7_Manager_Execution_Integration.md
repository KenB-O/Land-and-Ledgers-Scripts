# Pass VII Workstream D: Manager Execution Integration

## What already existed

`ManagerBrain` (Domains/Economy/Labor/ManagerBrain.cs) could generate seven decision types through `ManagerActKinds`: OpenRecruitment, Schedule, Procure, PriceChange, OrderMaintenance, Borrow, SellAsset. Pass VI (P2-4) confirmed that only OpenRecruitment had a confirmed executing production path in `SimulationDrivers`. The other six decisions were generated and then silently disappeared. A manager could not truly manage.

Delegation authority existed (`Domains/Economy/Delegation/DelegationService.cs`) with the three delegation scopes: Do Once, Manager Judgment, Owner Locked. It was not compiled in the harness and not enforced on manager actions.

## What was disconnected

Six of seven manager decisions had no executor. There was no enforcement of delegated authority on manager actions. Failed decisions had no legitimate pending state. The town-pulse unfulfilled demand (now recorded by Workstream A) had no manager response path.

## What was changed

Decision executors (worker 4, merge b81d471):
- `ManagerDecisionExecution.cs` (Domains/Economy/Labor/) implements one executor per decision kind, each through real production authorities:
  - OpenRecruitment: posts real recruitment through the employment authorities.
  - Schedule: creates legitimate WorkOrders and Tasks after checking workers, materials, equipment, premises, timing, cash, and existing commitments.
  - Procure: issues real procurement requests that flow through the transaction and logistics authorities. Goods do not appear immediately unless a legitimate fulfillment method supports it. Connects to the Workstream A demand source: the manager sees unfulfilled demand in the operation summary and can act on it.
  - PriceChange: adjusts prices through the pricing authority with valid terms.
  - OrderMaintenance: schedules equipment maintenance through the task and reservation authorities.
  - Borrow: seeks financing through the credit authorities within delegated limits.
  - SellAsset: disposes assets through legitimate sale with ownership transfer.
- Delegation enforcement: every executor checks the manager's `DelegatedAuthority` and `DelegationScope`. Do Once actions cannot repeat. Manager Judgment actions proceed within limits. Owner Locked actions produce a legitimate escalation request instead of silently bypassing the restriction.
- Failure handling: a failed action produces a legitimate pending state, waits, tries another supplier, reduces production, replans, or escalates to the owner. No infinite retries. Idempotent execution: no duplicate actions on retry.
- Decision-making cannot generate completed products, inventory, or money.

## Why the correction follows Canon

Canon requires managers to act within delegated authority through ordinary business systems. The executors use the actual transaction, logistics, employment, task, and credit authorities. Owner escalation preserves the Do Once, Manager Judgment, Owner Locked doctrine.

## Authoritative systems reused

ManagerBrain (decision generation), DelegationService (authority), TaskAuthority and WorkOrders (scheduling), transaction and logistics authorities (procurement), employment authorities (recruitment), credit authorities (borrowing). No new manager brain, no new transaction system. The harness source list now includes Domains/Economy/Delegation (previously uncompiled).

## Source entry points

- `ManagerDecisionExecution` executors, one per `ManagerActKinds` value.
- Each executor: `Evaluate` (trigger condition and authority check), `Execute` (production API call), `Track` (execution state and idempotency).
- Delegation check precedes every execution.

## Tests that prove it

- ManagerDecisionExecutionTests (20 passed): each decision type with legitimate trigger, correct authority, correct executor, financial validation, real resulting Task or transaction, success and blocked outcomes, save/load continuity, no duplicates, no invented goods or money.
- ManagerExecutionSliceTests (2 passed): SliceG_ManagerProcurement (declining inventory to detected shortage to procurement decision to supplier with actual goods to delivery to real stock to reconciled accounts) and SliceH_ManagerFailure (blocked action produces legitimate pending state, manager responds without infinite retries or fabricated fulfillment).

## What remains source-only

The executors are proven in the harness. Ordinary Main Scene activation (live daily manager evaluation cadence in normal gameplay) is a Codex integration item. The unfulfilled-demand signal from Workstream A to manager procurement is connected at source level.

## What Codex must connect in Unity

- Schedule manager decision evaluation in the live daily tick.
- Wire the delegation-scope UI so owners can set Do Once, Manager Judgment, and Owner Locked per decision area.
- Validate in PlayMode: a store with declining inventory produces a manager procurement decision, a real purchase order, delivery, and restocked shelves over several days; a blocked procurement produces a visible pending state, not a silent stall.
