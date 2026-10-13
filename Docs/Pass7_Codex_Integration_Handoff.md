# Pass VII Codex Integration Handoff

## Branch and commits

- Feature branch: muse/pass7-economic-cohesion-living-society (local only; Kennedy pushes via bundle).
- Foundation: b37dbb4 on top of the Pass VI audit head 4173a26.
- Worker merges (--no-ff, in dependency order): worker 1 real demand, worker 2 animal authority, worker 3 estate continuity, worker 4 manager execution and information.
- The four Pass VI fixes (ghost split, death ends employment, sim-born labor access, recruitment skips dead) are in the ancestry and must stay intact.

## What was built (source-complete, harness-proven)

1. RealDemandSource and ExternalMarketAuthority (Domains/Economy/Core/). Town-pulse manufactured revenue retired; buyerless demand recorded as unfulfilled for manager response.
2. IAnimalAvailabilityAuthority unifying HorseAuthority and DraftPowerService on one reservation record; horse condition lifecycle; HorseRental production path.
3. EstateLifecycleDriver: Opened to Inventoried to ClaimsAdjudicated to Distributing to Closed through TitleAuthority, FinancialObligationAuthority, CreditRegistry.
4. Seven manager decision executors with delegation-scope enforcement (Do Once, Manager Judgment, Owner Locked).
5. KnowledgeTransmissionStore (hub-registered), ConversationOpportunity validation, SocialVisitPlanner.

Full harness: 948 passed, 4 failed (the 4 historical RUN-011 items, untouched).

## Unity integration required

### Real demand cadence
- Schedule `RealDemandSource` household and business demand evaluation in the live daily tick. Choose a cadence that matches the game's economic rhythm (daily evaluation is the tested assumption).
- Wire the unfulfilled-demand operation summary into the manager UI so the procurement response is visible.
- PlayMode validation: run several days with no customers and confirm zero sales revenue; then introduce a buying household and confirm a real purchase with reconciled cash and inventory.

### External trade UI
- Surface ExternalMarketAuthority boundary transactions in the economic reporting UI with provenance (counterparty, terms, settlement).
- Validate that external inflows are explicitly labeled and do not inflate local conservation figures.

### Animal scheduling
- Route live draft and mount scheduling through `IAnimalAvailabilityAuthority` in the scene drivers. The harness proves the authority; the scene drivers must call it.
- Connect the rental UI to the `HorseRental` lifecycle (authorize, reserve, transfer custody, return, settle).
- PlayMode validation: assign a horse to plowing, then attempt a journey in the same window, and confirm refusal. Rent the horse and confirm the owner cannot use it during the rental.

### Estate processing
- Add a daily estate-processing step that advances open estates through the driver stages. The driver is complete and tested but currently advances only when driven by tests.
- Wire person-keyed business ownership transfer in the live business manager (BusinessInstanceState is still OwnerKind-keyed).
- PlayMode validation: a provider death over several days produces estate opening, claim settlement, distribution, and closure, with the surviving household's finances reflecting the loss.

### Manager execution
- Schedule manager decision evaluation in the live daily tick.
- Wire the delegation-scope UI: owners set Do Once, Manager Judgment, Owner Locked per decision area.
- PlayMode validation: a store with declining inventory produces a procurement decision, a real purchase order, delivery, and restocked shelves over several days. A blocked procurement shows a pending state.

### Social and information
- Drive social-visit planning from the live daily schedule for persons with social motives.
- Connect the saloon scene runtime to `ConversationOpportunity` validation and the transmission store.
- PlayMode validation: a foreman with mine knowledge visits the saloon, converses, the bartender's knowledge changes, and the player learns of it only through legitimate conversation.

## Scene-owned dependencies (do not implement in source)

- Saloon interior and patron routing (conversation locations).
- Rental and delegation UI panels.
- Estate and probate UI (or debug views).
- External trade reporting views.
- Manager decision review UI.

## Expected conflicts with unpublished work

- The unpublished Codex checkpoint 0d7b8d9 predates Pass VII. If it touches SharedBusinessRuntimeManager, SimulationDrivers, SimulationSystemsHub, or SystemsSaveDtos, reconcile against the Pass VII versions, which carry the town-pulse retirement, manager executors, delegation registration, and save DTO additions.
- Worker 4 modified SimulationDrivers.cs (+166), SimulationSystemsHub.cs (+23), and SystemsSaveDtos.cs (+6). Review these diffs before merging any Codex-side changes to the same files.

## Known gaps remaining source-side

- Estate runtime auto-advancement needs the daily step described above (PASS7-001).
- Social-visit auto-scheduling needs the live driver described above.
- Water system is designed but not implemented (PASS7-002).
- Person-keyed business ownership is designed but not implemented.

## What NOT to do

- Do not reintroduce manufactured revenue through scene-side shortcuts. Every sale needs a real buyer.
- Do not create a second animal registry or a second reservation system in Unity code.
- Do not bypass delegation scopes in UI-initiated manager actions.
- Do not merge to main without Kennedy's explicit authorization.
