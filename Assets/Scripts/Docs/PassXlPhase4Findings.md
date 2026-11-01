# Pass XL Phase 4 Findings — read-only pass at 2591ba (Stage 1 record)

Base: `muse/passxl-slice-phase3` at `2591ba250359f9b7e030c45656abdeeb8069212d`.
Scope: the full tree outside `Planning Sources`, plus the phase 1–3 gap lists,
plus governing docpkg v3.0 where it bears on a finding (governing originals
Canonical Bible XXXI and Technical Bible XIV, both 2026-10-07, unchanged).
This list was written BEFORE any Stage 2 fix. Stage 2 did not delete or
rewrite it: Stage 2 only filled in the Disposition column and appended the
implementation notes and Codex handoff sections at the end.

Severity vocabulary: blocks play / blocks slice / integrity risk / dead code /
test gap / Codex-only / consistency (stale comment or doc/code contradiction).

Confirmed means verified by reading the code at 2591ba (grep + file read).
Suspected means the evidence points that way but the runtime path could not
be verified from C# alone (Unity scene, asset, or PlayMode state).

## Findings

| ID | Finding | File(s) and evidence | Severity | Confirmed / suspected | What the fix should be | Disposition |
|----|---------|----------------------|----------|------------------------|------------------------|-------------|
| F01 | FamilyTreeBuilder is never called by production code. No caller of EnsureCompleteTree or MaterialiseOffMapRelative exists outside tests; PopulationGenerator.Generate and PopulationManager.GeneratePopulationSnapshot do not call it. Consequence: in production, residents have no off-map relatives, so RecruitmentConversation always returns zero leads. | Domains/Population/FamilyTreeBuilder.cs; Domains/Population/PopulationGenerator.cs (Generate, GenerateDevelopmentFixture — no call); Domains/Population/PopulationManager.cs GeneratePopulationSnapshot (lines ~209–260 — no call). Phase 3 docstring itself says "OPT-IN, deliberately not called by PopulationGenerator". | blocks play | Confirmed | Add a tested seam (ApplyAfterGeneration wrapper) + one Codex call site in the bootstrapper after GeneratePopulationSnapshot, before first save, with world seed and scenario start year. Do NOT call it inside generation: that would change on-map people counts in existing generation tests (off-map records share PopulationState.people). | Seam fixed in this pass; call site Codex-only |
| F02 | ArrivalSettlement driver steps missing. ArrivalSettlement.Settle is a gate+record only: it does NOT write HouseholdMembershipRegistry, does NOT create the HouseholdState for a new household, does NOT assign housing/residence, does NOT create an EmploymentRelationship. No production driver runs any of those steps. | Domains/Population/ArrivalSettlement.cs lines ~13–42 (docstring names the three missing driver steps); zero production callers of ArrivalSettlement outside its own file and tests. HouseholdLifecycleManager.FormHousehold / AddMember and EmploymentRelationshipRegistry.Register exist and are usable. | blocks play | Confirmed | Implement a plain-C# ArrivalSettlementDriver that applies a settled record: flip isOffMap, set household pointers, AddMember (existing household) or FormHousehold + AddMember (new household), optionally Register employment when caller-supplied compensation terms exist, refuse duplicates. Residence assignment stays Codex (HousingResidence runtime; record.HousingUnitId is a string, PersonState.homeBuildingId is an int — no safe mapping in C#). | Fixed in this pass (membership, household, employment); residence Codex-only |
| F03 | RecruitmentConversation postal posting not wired. BuildLetterRequest builds a letter request; nothing in production hands it to PostalService.PostItem. No production caller of BuildLetterRequest exists outside tests. | Domains/Population/RecruitmentConversation.cs lines ~33–36 (docstring: "Posting itself belongs to PostalService.PostItem"); zero production callers. | blocks play | Confirmed | Implement a plain-C# RecruitmentLetterPoster adapter: validate the request, call the payer overload of PostalService.PostItem with MailKind.Letter, real office ids, and a real sender cash authority; refuse when postage cannot be paid (no free carriage). | Fixed in this pass |
| F04 | PostalService has zero production office registrations. RegisterOffice is called nowhere in production code, so PostItem would refuse every letter in production ("unknown origin office"), even after F03 is bridged. RecruitmentLetterRequest.Origin "general-store" is not a registered office anywhere in production. | Domains/Economy/Businesses/PostOffice/PostalService.cs (RegisterOffice, PostItemCore origin check); grep for RegisterOffice across Domains/ Orchestration/ Infrastructure/ returns only PostalService.cs itself (tests register their own offices). | blocks play | Confirmed | Codex-only: register the General Store office (and any destination offices) in Main Scene bootstrap before any posting; the bridge must take office ids as caller-supplied and validate registration instead of inventing an office. Off-map destinations use PostalService.OffMapOfficeId ("offmap"). | Codex-only (unchanged) |
| F05 | Supplier quotes are not wired into orders. OffMapSupplierCatalog.QuoteAll has no production caller; OffMapNamedSkuProcurementService.PlaceOrder takes raw costs, not a quote. There is no quote-to-order path, so a player cannot act on a quoted price in code. | Domains/Economy/Businesses/GeneralStore/OffMapSupplierCatalog.cs (QuoteAll); Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs (PlaceOrder); grep for QuoteAll/RegisterSupplier outside tests returns nothing. | blocks play | Confirmed | Implement a plain-C# OffMapQuoteToOrder bridge: validate a quote (totals recompute, divisible goods, known supplier via the market), refuse SelfCollect (journey not modelled), then PlaceOrder with the quote's exact goods + freight. FreightBusiness quotes pass freight through as a surcharge only — they do NOT create or commission a freight business (see F26 note). | Fixed in this pass |
| F06 | Multi-SKU procurement not extended. PlaceOrder is single-SKU by design; there is no batch API, and no batch ledger to make a multi-SKU order idempotent. A naive retry loop would double-order. | Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs (PlaceOrder, one skuId per order). | blocks play | Confirmed | Deferred with reason: a safe batch needs a persisted batch id + order-id ledger (additive state on the service + ExportState/ImportState coverage). A stateless loop would violate the no-double-order rule, so it was NOT faked. Design is recorded in the Codex handoff; single-SKU guard untouched. | Deferred with reason (no batch ledger faked) |
| F07 | ItemDefinitions for new tier ids are not in the GeneralStoreBusinessDefinition asset. Tier ids live in code (HouseholdItemCatalog, GeneralStoreItemTiers); the ScriptableObject definition in-repo is an empty subclass, and the asset itself is outside this repo. | Domains/Economy/Businesses/GeneralStore/GeneralStoreBusinessDefinition.cs (empty subclass of BusinessProfileDefinition); Domains/Economy/Businesses/GeneralStore/GeneralStoreItemTiers.cs; Domains/Population/HouseholdInventory.cs (catalog, incl. phase 3 sugar/coffee/tea fix). | Codex-only | Confirmed | Codex-only: add the tier ids to the GeneralStoreBusinessDefinition asset in the Unity project; verify in Editor. Nothing to fake from C#. | Codex-only (unchanged) |
| F08 | Outmigration post-departure handling is not run by any driver. Outmigration.Depart returns PostDepartureHandlingRequired (household/property/debt handling, return loop, reputation feed) but no production code reads that list or executes it. | Domains/Population/Outmigration.cs lines ~129–137, ~216–218; zero production callers. | blocks play | Confirmed | Deferred with reason: executing post-departure handling needs a production driver that owns household dissolution, property, and debt settlement — each with its own authority. Listing the steps is done; executing them without that driver would fake settlement. Recorded in the Codex handoff with exact names. | Deferred with reason (no executing driver) |
| F09 | CONFLICT: MigrationDriver.EvaluateMonth still runs departures when AllowNewArrivals is false. The slice gate suppresses arrivals only; the departure loop above the gate runs first, so six-person slice residents can still leave on distress (2%) or opportunity (0.2%) draws. The new Outmigration channel disagrees: its AllowDepartures gate says no departures in the slice. | Domains/Population/FamilyFormationDrivers.cs MigrationDriver.EvaluateMonth (departure loop, then the AllowNewArrivals arrivals gate); Domains/Population/Outmigration.cs lines ~69–77 (flags the disagreement, driver NOT changed); Domains/Population/DemographicDrivers.cs DemographicDriverContext.AllowNewArrivals docstring (reserves departure gating as a Kennedy decision). | integrity risk | Confirmed | Mechanism implemented in Stage 2 (AllowDepartures flag, default true, driver respects it, tested) but NOT wired to the slice gate: activating it for the slice is a Kennedy decision (see Design forks). Existing behaviour is unchanged until he confirms. | Mechanism fixed in this pass; activation = Kennedy decision |
| F10 | Old 4% monthly arrival roll still present alongside the new channels. MonthlyArrivalProbability = 0.04 and MigrateIn (smallest household, host surname, no job/housing/reputation check) remain the live arrival path whenever AllowNewArrivals is true. | Domains/Population/FamilyFormationDrivers.cs MigrationDriver (MonthlyArrivalProbability, MigrateIn); Domains/Population/ThroughTraveler.cs docstring calls the traveler channel a "REPLACEMENT for the old dice-roll" — but the old roll was not removed. | integrity risk | Confirmed | Kennedy decision: retire MigrateIn only on his word. Retiring it changes existing tests (Pass9PeopleLifecycleWiringTests, SixPersonNoImmigrationTests seed search) and the ordinary-scenario arrival rate. NOT silently changed. | Kennedy decision (unchanged) |
| F11 | Old out-migration draw still present alongside the new channel. MigrationDriver departure draws (distressed 0.02, opportunity 0.002) run independently of Outmigration.Depart, so two departure paths exist with different rules. | Domains/Population/FamilyFormationDrivers.cs MigrationDriver.EvaluateMonth; Domains/Population/Outmigration.cs (separate channel, no shared path). | integrity risk | Confirmed | Kennedy decision: unify on one departure path only on his word; see F09/F12. NOT silently changed. | Kennedy decision (unchanged) |
| F12 | Old-path departures are untracked to a destination. MigrationDriver.MigrateOut produces a MigrationOutcome with no destination; Outmigration.FindDepartedAtDestination only works with new-channel records. A legacy departure therefore cannot be found, contacted, or recruited back, breaking the "person remains contactable/recruitable" requirement for that path. | Domains/Population/FamilyFormationDrivers.cs MigrateOut; Domains/Population/Outmigration.cs FindDepartedAtDestination (takes OutmigrationRecord list). | integrity risk | Confirmed | Deferred with reason: a destination cannot be invented for a legacy departure (never randomised, per the outmigration contract). Fix requires Kennedy to choose: route legacy departures through Outmigration.Depart with a caller-supplied destination, or accept untracked legacy departures. Recorded in Design forks. | Kennedy decision (unchanged) |
| F13 | D1 (phase 3 audit): LogisticsTransportPresentationController is complete but has zero production references; it is not in authoring or the bootstrapper, so wagon visuals do not run. | Domains/Economy/Logistics/LogisticsTransportPresentationController.cs (MonoBehaviour, DefaultExecutionOrder 266, AutoWire to LogisticsRuntimeManager); grep returns only its own file. | Codex-only | Confirmed | Codex decision: add the controller to a Main Scene GameObject with its LogisticsRuntimeManager + wagon prefab references, or retire it on Kennedy's word. C# cannot decide this. | Codex decision (unchanged) |
| F14 | D2 (phase 3 audit): LLFeedbackService self-creates a GameObject in PlayMode when absent (Instance getter), so the service runs without scene placement and without Inspector-configured clips. | Presentation/UI/LLFeedbackService.cs Instance getter (FindAnyObjectByType, else new GameObject + AddComponent); callers in AcquisitionSellerMeetingController, OwnershipWorkflowController, GeneralStorePanelController, SaveLoadManager. | Codex-only | Confirmed | Codex decision: place LLFeedbackService on a Main Scene GameObject with clips configured, or keep self-creation deliberately. Changing the getter in C# would silence UI sounds everywhere the service is absent — a behaviour change that belongs to Codex/Kennedy, not a silent fix. | Codex decision (unchanged) |
| F15 | D3 (phase 3 audit): FindUnique bootstrapper targets are silently skipped when absent. SetReference only assigns when the found value is non-null, with no warning, so a missing TownWorldController, PopulationManager, store runtime, etc. fails silently at authoring time. | Editor/PersistentRuntimeHierarchyAuthoring.cs SetReference (assigns only if value != null); FindUnique returns null on multiple matches (with an error) but null on absence (silent). | Codex-only | Confirmed | Codex/Editor task: log a warning naming the missing component type and property when a FindUnique target is absent, so authoring gaps are visible. Editor-only code, not Roslyn-testable. | Codex/Editor task (unchanged) |
| F16 | Bakery staged-to-sellable transfer still missing. BakeryStagedProductionChain holds BakeryStagedBreadLot (string LotId "bread-<batch>") on its BreadShelf; BakeryShopRuntime sells BakeryBreadLot (EntityId LotId) from its own breadShelf. No public Receive/Add API exists on BakeryShopRuntime, and no caller connects the two. Field mapping is otherwise 1:1 (ProductId, BakedDayIndex, Units/UnitCount, InputProvenance). | Domains/Economy/Businesses/Bakery/BakeryStagedProductionChain.cs lines ~71–84, ~711–720; Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs (breadShelf private, BreadShelf read-only, BakeBatch creates lots internally at ~line 729, no Receive method). | blocks play | Confirmed | Deferred with reason: a transfer needs (a) a public Receive method on BakeryShopRuntime with duplicate-lot rejection, and (b) a Main Scene owner/driver holding both the staged chain and the shop runtime. BakeryShopRuntime pulls Equipment/Workstations dependencies and is not in the Roslyn harness; adding the Receive method without a Unity compile check would be an unverified production-runtime change. Exact mapping + method design recorded in the Codex handoff. | Deferred with reason (needs Receive API + scene owner) |
| F17 | Bakery flour feed missing. No caller feeds Phase 1 off-map flour procurement into BakeryFlourStock, and the procurement service has no generic consume API (only SellToHousehold consumes lots, FIFO). Routing a procurement lot to both household sale and bakery stock would double-spend it. | Domains/Economy/Businesses/Bakery/BakeryFlourLots.cs (BakeryFlourStock.ReceiveLot requires miller or import order/origin — mappable from a procurement lot's OrderId/ReceiptId/Supplier, but no bridge exists); Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs (no Consume API besides SellToHousehold). | blocks play | Confirmed | Deferred with reason: needs an additive ConsumeForProduction API on the procurement service (quantity, lot, provenance, no double-spend) plus a flour bridge into BakeryFlourStock, plus a driver owning the bakery's ordering. Design recorded in the Codex handoff; nothing faked. | Deferred with reason (needs consume API + driver) |
| F18 | SaveLoadManager / FirstLedgerSliceBootstrapper Unity compilation still unconfirmed by Roslyn. Both are MonoBehaviour files outside the Roslyn harness; phase 2 call sites were code-reviewed only. | Infrastructure/Persistence/SaveLoadManager.cs; Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs; Roslyn harness file list (neither file included). | test gap | Confirmed | Unity-only: Codex must compile in Editor and run Unity Test Runner / PlayMode. Cannot be closed from C# tests. | Unity-only (unchanged) |
| F19 | SaveLoadManager.PassXlProcurementService is never set by production code. The property exists and is read at capture (line ~750) and restore (line ~427), but no production setter exists: if Codex does not register the production procurement service, procurement persists empty and restores empty, with only a diagnostic. | Infrastructure/Persistence/SaveLoadManager.cs lines ~134, ~427, ~750; grep for PassXlProcurementService returns only SaveLoadManager.cs. | blocks slice | Confirmed | Codex-only: register the production OffMapNamedSkuProcurementService instance with SaveLoadManager in Main Scene bootstrap before first save. The diagnostic already names this; it cannot be fixed from the Scripts repo side without a scene owner. | Codex-only (unchanged) |
| F20 | The 3 pre-existing Phase H failures persist as the Roslyn baseline (to be re-verified in the phase 4 full run): HouseholdVerticalSlicePhaseHTests InsufficientFunds, Hardship missed meals, Restock 60 vs 59. | Tests/Editor/Orchestration/HouseholdVerticalSlicePhaseHTests.cs (EndToEndTrace_InsufficientFunds_FailsThenRecovers, Secondary_Hardship_MissedMeals_NoInvisibleReplenishment, Secondary_Restock_ProcurementChainRefillsThenSaleSucceeds); phase 3 full-set result 296 passed / 3 failed. | test gap | Confirmed (baseline; re-verify in phase 4 run) | Not fixed in this pass: Phase H is a full vertical-slice orchestration whose failures pre-date Pass XL and were not diagnosed to a single safe fix. Any fix must come with its own regression proof; left for a dedicated Phase H pass. | Pre-existing, re-verified unchanged |
| F21 | ConstructionInputSupportTests 1800s timeout still has no verdict. 109 tests in 3161 lines; the file is NOT in the Roslyn harness, so the timeout cannot be reproduced or diagnosed from Roslyn at all. | Tests/Editor/Economy/ConstructionInputSupportTests.cs (109 test methods, 3161 lines, no Timeout attribute); Roslyn harness file list (file absent). | test gap | Confirmed | Unity-only: Codex must run it in the Unity Test Runner with per-test timing to find the pathological test(s). Not a Roslyn task. | Unity-only (unchanged) |
| F22 | Umbrella: all phase 3 decision models have zero production callers. TownReputationModel, ThroughTravelerImmigration, ArrivalSettlement, Outmigration, RecruitmentConversation, OffMapSupplierCatalog are referenced only by their own files and by PassXlPhase3FamilyImmigrationItemsTests. The models are tested but the game never runs them. | Grep across Domains/ Orchestration/ Infrastructure/ for each type (production hits: none); Tests/Editor/Population/PassXlPhase3FamilyImmigrationItemsTests.cs is the sole caller. | blocks play | Confirmed | Stage 2 bridges close F02/F03/F05 at the C# level; the remaining caller gap (who ticks these models every day) is a SimulationDrivers / bootstrapper driver task for Codex, recorded in the handoff. | Partially fixed (bridges done; daily tick driver Codex) |
| F23 | TownReputation inputs have no production reader. Nothing aggregates housing vacant/total units, open/filled job slots, growth, or good-opinion scores from live runtimes into TownReputationInputs; all four are caller-supplied, and growth/opinion have no production source at all. | Domains/Population/TownReputation.cs (TownReputationInputs: HousingVacantUnits, HousingTotalUnits, OpenJobSlots, FilledJobSlots, GrowthScore01 caller-supplied, GoodOpinionScore01 caller-supplied); zero production callers of TownReputationModel.Compute. | blocks play | Confirmed | Codex/driver task: a reputation reader that pulls housing from HousingResidence/HousingFoundation, jobs from the employment registry/slot states, and opinion from a defined source — with weights still caller-supplied test parameters until Kennedy sets them (see Design forks). NOT faked with invented weights. | Codex/driver task (unchanged) |
| F24 | Through-traveler visits are never generated. No scheduler or driver creates ThroughTravelerVisit records or calls ThroughTravelerImmigration.EvaluateVisit; traveler frequency is caller-supplied and no caller supplies it. | Domains/Population/ThroughTraveler.cs (EvaluateVisit, WordOfMouthProducesLaterArrival); zero production callers. | blocks play | Confirmed | Codex/driver task: a visit generator (route, frequency, boarding vacancies from BoardingHouseShopRuntime) feeding EvaluateVisit, then ArrivalSettlement for stays. Frequency is a Kennedy decision (see Design forks). | Codex/driver task (unchanged) |
| F25 | SixPersonSliceDiagnostics has no production caller. It is referenced only by its own test file; console diagnostics for the slice are never emitted by a driver. | Orchestration/Player/SixPersonSliceDiagnostics.cs; Tests/Editor/Orchestration/SixPersonSliceDiagnosticsTests.cs is the sole referrer. | dead code | Confirmed | No action: test utility by design. Wiring it into SimulationDrivers would change driver output behaviour; leave until Kennedy wants slice console diagnostics. Informational only. | No action (test utility by design) |
| F26 | Duplicate procurement authorities. StoreProcurementChain (category stock, used by the Phase H slice path) and OffMapNamedSkuProcurementService (standalone lot ledger, used by the Pass XL slice) are two order systems with separate ledgers and no shared guard against ordering the same need twice. | Domains/Economy/Businesses/GeneralStore/StoreProcurementChain.cs (delivers into store.CategoryStockUnits via ReceiveStock, paid via SpendCash); Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs (standalone lots, SellToHousehold/SellAndDeliverToInventory). The General Store single-SKU guard itself is intact (staple_food refused as a single-item offer). | integrity risk | Confirmed | No code change in this pass: the two systems serve different paths and do not share a ledger, so there is no double-stock today — but there is no guard against double-ordering the same need. A shared need/dedupe key is a design decision for Kennedy; recorded in Design forks. | No code change; design decision for Kennedy |
| F27 | Doc/code contradiction: ThroughTraveler docstring says the traveler channel is a "REPLACEMENT for the old dice-roll in-migration", but the old 4% roll is still live (F10). A reader trusting the docstring would believe the old path is gone. | Domains/Population/ThroughTraveler.cs docstring lines ~8–12 vs Domains/Population/FamilyFormationDrivers.cs MigrationDriver (live MonthlyArrivalProbability). | consistency | Confirmed | Fixed in Stage 2 only if the docstring is corrected to "intended replacement, old roll still live pending Kennedy decision" — a comment-only fix, no behaviour change. (See implementation notes.) | Fixed in this pass (comment-only) |
| F28 | Checked, NOT a finding: FamilyTreeBuilder results would survive save/load once run. PopulationSaveDto carries people (including off-map records) and kinshipEdges; HouseholdMembershipRegistry has a save DTO. The F01 gap is purely the missing call site, not persistence. | Infrastructure/Persistence/SaveDtos.cs PopulationSaveDto; Domains/Population/PopulationManager.cs CaptureSaveDto (people loop, kinshipEdges loop, householdMemberships DTO). | — | Confirmed | None. Recorded so a later pass does not re-investigate save wiring for family trees. | No action (verified non-finding) |
| F29 | Checked, informational: phase 3 decision records are transient by design. TownReputationSnapshot, OutmigrationRecord, TravelerOutcome, RecruitmentLead have no save DTOs; a lead or visit in progress is lost on reload. The underlying relatives still exist and can be re-asked, so this is acceptable, not a data-loss bug. | Domains/Population/TownReputation.cs, Outmigration.cs, ThroughTraveler.cs, RecruitmentConversation.cs (no DTO, no Infrastructure reference). | — | Confirmed | None, unless Kennedy wants lead/visit history persisted (then it needs an additive DTO + coordinator coverage like phase 2). | No action (informational) |
| F30 | FirstLedgerSliceBootstrapper treatPlayerAsNpcPlaytest Inspector value is Codex-only. The code default is false with OR logic (true forces NPC mode, false never clears a saved true); Codex must set it in Main Scene Inspector for playtest runs. | Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs lines ~111–112, ~1080–1092; Orchestration/Player/PlayerNpcPlaytestForwarding.cs. | Codex-only | Confirmed | Codex-only: set the Inspector bool for playtest scenes; verify Continue OR behaviour in Editor. | Codex-only (unchanged) |
| F31 | Stale comment: DemographicDriverContext.AllowNewArrivals docstring says the flag "is NOT persisted into saves, so a Continue currently falls back to ordinary immigration — Codex must persist/restore it". Phase 2 already persists it (PassXlSliceSaveDto.suppressMigrantArrivals, restored onto PopulationManager via PassXlSliceSaveCoordinator). The comment is out of date. | Domains/Population/DemographicDrivers.cs AllowNewArrivals docstring vs Infrastructure/Persistence/PassXlSliceSaveCoordinator.cs Restore + Infrastructure/Persistence/SaveLoadManager.cs restore call; PopulationManager implements IPassXlMigrationFlagHolder. | consistency | Confirmed | Comment-only fix in Stage 2: correct the docstring to describe phase 2 persistence. Zero behaviour change. | Fixed in this pass (comment-only) |
| F32 | Checked, NOT a finding: General Store single-SKU guard intact. OffMapNamedSkuProcurementService refuses staple_food as a single-item offer, and the slice flour path uses the standalone lot ledger without touching category stock. | Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs docstring + guard; phase 1/2 reports. | — | Confirmed | None. Guard must stay intact in any later multi-SKU work (F06). | No action (verified non-finding) |
| F33 | Phase 1–3 code introduces no new cash-writer violation on spot check. OffMap orders settle through IBusinessCashPort / ITradeCreditPort (FinancialObligationAuthority-backed trade credit) and household delivery through the phase 2 atomic path; no new direct household-cash mutation was found in the phase 1–3 files. | Domains/Economy/Businesses/GeneralStore/OffMapNamedSkuProcurementService.cs (PlaceOrder, ReceiveArrivals, SellAndDeliverToInventory); Infrastructure/Persistence/PassXlSliceSaveCoordinator.cs. | — | Confirmed (spot check, not a full-tree audit) | None. A full-tree cash-writer audit remains a larger pass, not claimed here. | No action (spot check) |
| F34 | Experience formation rate nearly invisible over realistic timescales (phase 3 carryover). Flagged in phase 3 as needing playtest-feel review; no rate change was made or investigated here. | Phase 3 report (carried forward); no new evidence gathered in this pass. | blocks play | Suspected (playtest-feel, not code-proven) | Playtest-feel review by Kennedy; rate changes are canon-sensitive and NOT made here. | Kennedy/playtest review (unchanged) |
| F35 | MigrationDriver.MigrateIn contradicts the phase 3 arrival conditions whenever the gate is off. It joins the smallest household with no job check, no housing check, and no reputation check, and invents a birth date (documented as GeneratedExact). This is the live arrival path for all ordinary scenarios. | Domains/Population/FamilyFormationDrivers.cs MigrateIn (PickHostHousehold = smallest household; no employment/housing/reputation inputs). | integrity risk | Confirmed | Kennedy decision, tied to F10: either retire MigrateIn or gate it behind the phase 3 conditions (housing + work + reputation + ArrivalSettlement). NOT silently changed. | Kennedy decision (unchanged) |
| F36 | RecruitmentConversation is family-only by construction: no friend graph exists in code, so friends can never be offered as leads. Documented in the code, but it bounds the phase 4 item (2) mechanic ("ask if they know anyone") to relatives only. | Domains/Population/RecruitmentConversation.cs Ask (kinship parents/children/siblings/spouses/grandparents only; diagnostic states "Friends are not modelled yet"). | blocks play | Confirmed | Kennedy scope decision: build a friend graph, or accept family-only recruitment. NOT faked with invented friends. | Kennedy scope decision (unchanged) |
| F37 | Production supplier catalog is empty. OffMapSupplierCatalog has no production registrations: RegisterSupplier is called only in tests, and all distance/price examples (100/200/250/300 miles, cigarettes 600c vs 800c for 10 units) are test parameters only, never canon. | Domains/Economy/Businesses/GeneralStore/OffMapSupplierCatalog.cs; grep for RegisterSupplier outside tests returns nothing. | blocks play | Confirmed | Codex/data task: register real suppliers (ids, distances, offered SKUs, goods prices) from a Kennedy-approved source; freight rates (cents/mile, commission) are Kennedy decisions, not canon. Bridge (F05 fix) takes prices from the quote, never invents them. | Codex/data task (unchanged) |
| F38 | SelfCollect procurement journey not modelled. OffMapFreightQuote marks SelfCollect as RequiresJourney, but no journey/travel execution exists for a procurement pickup; ordering a SelfCollect quote as a market order would teleport goods. | Domains/Economy/Businesses/GeneralStore/OffMapSupplierCatalog.cs (RequiresJourney on SelfCollect quotes); no production journey caller for procurement. | integrity risk | Confirmed | Bridge (F05 fix) refuses SelfCollect quotes with a diagnostic. A real SelfCollect path needs player/NPC travel execution — a driver task for Codex, not faked here. | Fixed in this pass (bridge refuses); journey = Codex task |
| F39 | Slice gate stays intact (positive verification). SuppressMigrantArrivals / AllowNewArrivals still suppress arrivals for the slice, and ThroughTravelerImmigration respects allowNewArrivals (passes on, word of mouth recorded but no arrival). No Stage 2 change turns immigration, traveler arrivals, or outmigration on for the slice. | Domains/Population/PopulationManager.cs (SuppressMigrantArrivals); Domains/Population/DemographicDrivers.cs (AllowNewArrivals); Domains/Population/ThroughTraveler.cs (allowNewArrivals branch); Domains/Population/Outmigration.cs (AllowDepartures gate, default behaviour unchanged). | — | Confirmed | None. Any activation is a Kennedy decision (see Design forks). | No action (gate verified intact) |

## Checked and cleared (not findings)

- Six-person slice composition (6 people, 3 households, 3 businesses) is unchanged by
  this pass; no Stage 2 change alters fixture generation.
- Old saves still load with passXlSlice flags false and empty procurement
  (phase 2 coordinator contract, unchanged).
- Phase 2 atomic SellAndDeliverToInventory (store lot + cash + HouseholdInventory
  lot in one transaction, AddLot rejection rolls back, duplicate delivery returns
  AlreadyDelivered) is unchanged.
- No scene, asset, prefab, or .unity changes are in this repo or this pass.

## Stage 2 implementation notes

Stage 2 ran immediately after Stage 1, in priority order: integrity risks
and slice blockers first, then unwired drivers and persistence gaps that
could be wired safely in C#, then consistency fixes, then test gaps.
No Stage 2 change alters existing tested behaviour silently: the only
behaviour-adjacent change is the new `AllowDepartures` flag, which
defaults to true, and every existing test passes unchanged.

### DONE

- F02 — `Domains/Population/ArrivalSettlementDriver.cs` (new).
  Applies a settled `ArrivalSettlementRecord`: joins an existing
  household via `HouseholdLifecycleManager.AddMember`, or creates the
  `HouseholdState` with the record's PRE-ALLOCATED id (never
  `FormHousehold`, which would allocate a second id) and then joins it;
  registers an `EmploymentRelationship` only when caller-supplied
  compensation terms exist, otherwise reports `EmploymentPending`
  without inventing terms; always reports `HousingAssignmentPending`
  (residence is not done here). Re-applying a record duplicates nothing
  (membership first-wins, employment first-wins); a conflicting active
  membership is refused, never silently transferred; a join rejection
  after a household creation removes the empty household again.
  Evidence: 5 new Roslyn tests (apply-once, new-household id, unsettled
  refusal, no-compensation pending, conflicting-membership refusal).
- F03 — `Domains/Population/RecruitmentLetterPoster.cs` (new). Hands a
  `RecruitmentLetterRequest` to `PostalService.PostItem` (payer
  overload, `MailKind.Letter`): one letter per call, postage debited
  before acceptance, refused on unregistered origin, broke payer, or
  null payer. Off-map contacts default to `PostalService.OffMapOfficeId`.
  Evidence: 4 new Roslyn tests (posted + debited once, unregistered
  origin refused with no charge, broke payer refused, null payer refused).
- F05 — `Domains/Economy/Businesses/GeneralStore/OffMapQuoteToOrder.cs`
  (new). Validates a quote (totals recompute, divisible goods, known
  supplier via the market) and places the exact order through
  `OffMapNamedSkuProcurementService.PlaceOrder`. SelfCollect refused
  (journey not modelled); FreightBusiness freight passes through as a
  surcharge only and creates no business. Evidence: 6 new Roslyn tests
  (SellerFreight exact order, FreightBusiness surcharge, tampered total,
  SelfCollect, non-divisible goods, unknown supplier — all refusals
  order nothing and move no supply).
- F01 (seam) — `Domains/Population/FamilyTreeWiring.cs` (new).
  `ApplyAfterGeneration` wraps `FamilyTreeBuilder.EnsureCompleteTree`
  with a diagnostic; idempotency proven by test (second call: 0 parents,
  0 grandparents, 0 sibling edges, people and edge counts unchanged).
  The Codex call site is still open (see handoff).
- F09 (mechanism) — `DemographicDriverContext.AllowDepartures`
  (default true) + `MigrationDriver.EvaluateMonth` respects it with a
  diagnostic. Evidence: 3 new Roslyn tests (default true, gated run
  suppresses a departure that fires ungated on the same seed, arrivals
  gate unaffected). PopulationManager is NOT wired to it (see forks).
- F27 — ThroughTraveler docstring corrected to "intended replacement;
  old roll still live pending Kennedy decision" (comment-only).
- F31 — `DemographicDriverContext.AllowNewArrivals` docstring corrected
  to describe phase 2 persistence (comment-only).

### PARTIAL

- F01 — seam done and tested; the one-line Codex call site
  (bootstrapper after `GeneratePopulationSnapshot`, before first save)
  is not done and cannot be done from C# alone.
- F02 — membership, household, and employment steps done; housing /
  residence assignment is not done (string unit id vs int building id;
  needs the HousingResidence runtime).
- F22 — the C# bridges for settlement, posting, and quote-to-order are
  done; the daily driver that TICKS TownReputation, ThroughTraveler,
  Outmigration, and RecruitmentConversation in Main Scene is not done.

### NOT DONE (with reason, not faked)

- F06 multi-SKU batch: needs a persisted batch id + order-id ledger;
  a stateless retry loop would double-order. Design only (see handoff).
- F08 post-departure execution: needs a driver owning household
  dissolution, property, and debt settlement. Steps listed, not executed.
- F16 bakery transfer: needs a public Receive method on
  `BakeryShopRuntime` (no Receive/Add API exists; shelf is private) plus
  a Main Scene owner holding both the staged chain and the shop runtime.
  Field mapping is 1:1 except LotId (staged string "bread-&lt;batch&gt;"
  vs sellable `EntityId` — allocate via `EntityIdRegistry` like
  `BakeBatch` does, or report unregistered like the no-registry path).
  `BakeryShopRuntime` is not in the Roslyn harness (Equipment /
  Workstations dependencies), so the Receive method was not added
  without a Unity compile check.
- F17 bakery flour feed: needs an additive ConsumeForProduction API on
  the procurement service (SellToHousehold is the only consumer today)
  plus a bridge into `BakeryFlourStock.ReceiveLot` (a procurement lot's
  OrderId/ReceiptId/Supplier map to ImportOrderId/OriginName; the
  miller-or-import-order check passes on import order alone) plus a
  driver owning bakery ordering. Routing one lot to both household sale
  and bakery stock would double-spend it, so no bridge was faked.

### Roslyn results (harness `harness-passxl-phase4.csproj`, outside repo)

- Phase 3 tests: 18 passed, 0 failed (all
  `PassXlPhase3FamilyImmigrationItemsTests`).
- New phase 4 tests: 19 passed, 0 failed
  (`PassXlPhase4FindingsFixesTests`: 5 settlement driver, 1 family-tree
  wiring, 6 quote-to-order, 4 letter poster, 3 departure gate).
- Full set: 315 passed, 3 failed — the 3 failures are exactly the
  pre-existing `HouseholdVerticalSlicePhaseHTests` failures from the
  phase 3 baseline (InsufficientFunds, Hardship missed meals, Restock
  60 vs 59). No new failure. Phase 3 baseline was 296 passed / 3
  failed; 296 + 19 new = 315.
- Harness note: the phase 4 harness adds `DemographicDrivers.cs`,
  `FamilyFormationDrivers.cs`, `MigrationOutcome.cs`, and
  `BirthdayService.cs` from the repo, plus a harness-only
  `Shims/EstateServiceShim.cs` (the real `EstateService.cs` drags
  WillContest/Probate/Lawyer/Will into the subset; no phase 4 test
  touches estates or mortality, and the shim documents that the
  mortality/estate path is untested here, not proven).
- NOT claimed: Unity compilation, Unity Test Runner, PlayMode, scene /
  prefab / Inspector wiring, Main Scene visual behaviour, or
  full-regression status beyond the Roslyn full-set result above.

## Codex handoff (Editor / scene / asset tasks)

Exact names only; nothing here is claimed done.

1. Family-tree call site (F01): in `FirstLedgerSliceBootstrapper`
   (or the equivalent New Game path), immediately after
   `PopulationManager.GeneratePopulationSnapshot` returns and before
   the first save, call `FamilyTreeWiring.ApplyAfterGeneration`
   (population = PopulationManager state, kinship = PopulationManager
   kinship registry, seed = world seed, referenceYear = scenario start
   year from the calendar anchor). Verify in Editor that off-map
   relatives appear and that a second call adds nothing.
2. Residence assignment (F02 remainder): after
   `ArrivalSettlementDriver.Apply` returns, assign the record's
   `HousingUnitId` through the HousingResidence runtime and set the
   person's `homeBuildingId` (currently left at -1 by settlement).
   Until this exists, settled arrivals have household + job but no
   residence.
3. Postal office registration (F04): register the General Store office
   with `PostalService.RegisterOffice` (origin office id
   "general-store" per `RecruitmentLetterRequest.Origin`) in Main Scene
   bootstrap before any `RecruitmentLetterPoster.PostLetter` call, plus
   any destination offices; off-map contacts use
   `PostalService.OffMapOfficeId` ("offmap"). Until this exists, every
   posting is refused in production (proven by test).
4. Production suppliers (F37): populate `OffMapSupplierCatalog` via
   `RegisterSupplier` with real supplier ids, distances, offered SKUs,
   and goods prices from a Kennedy-approved source. The catalog is
   empty in production today; all existing examples are test parameters.
5. Daily tick driver (F22/F23/F24): a SimulationDrivers / bootstrapper
   driver that, every day, (a) aggregates `TownReputationInputs`
   (housing vacant/total, open/filled job slots, growth, opinion) from
   live runtimes and calls `TownReputationModel.Compute`; (b) generates
   `ThroughTravelerVisit` records (route, boarding vacancies from
   `BoardingHouseShopRuntime`) and calls
   `ThroughTravelerImmigration.EvaluateVisit`, then
   `ArrivalSettlement` + `ArrivalSettlementDriver` for stays; (c) runs
   `Outmigration.Depart` checks and executes its
   `PostDepartureHandlingRequired` list (F08). Reputation weights stay
   caller-supplied until Kennedy sets them.
6. Post-departure execution (F08): a driver that, for each
   `OutmigrationRecord`, settles household membership, property, and
   debts through their own authorities, runs the return loop
   (`FindDepartedAtDestination`), and feeds the reputation penalty into
   `TownReputationModel` as `GoodOpinionScore01`.
7. Bakery transfer (F16): add a public Receive method to
   `BakeryShopRuntime` (duplicate staged-lot rejection, provenance
   carried over, `EntityId` allocated via `EntityIdRegistry` or
   honestly reported unregistered), plus a Main Scene owner/driver
   holding both `BakeryStagedProductionChain` and `BakeryShopRuntime`
   that moves finished staged lots to the sellable shelf; verify the
   transfer in Editor and confirm save wiring for both shelves.
8. Bakery flour feed (F17): add an additive ConsumeForProduction API
   to `OffMapNamedSkuProcurementService` (quantity + lot + provenance,
   no double-spend with `SellToHousehold`), a bridge into
   `BakeryFlourStock.ReceiveLot`, and a driver owning bakery ordering.
9. Multi-SKU batch (F06): design + implement a persisted batch ledger
   on the procurement service (batch id + per-SKU order ids in
   ExportState/ImportState) so a retried batch creates nothing twice;
   keep the single-SKU guard intact (staple_food still refused as a
   single-item offer).
10. Scene discrepancies (F13/F14/F15): D1 — place
    `LogisticsTransportPresentationController` on a Main Scene
    GameObject with its `LogisticsRuntimeManager` + wagon prefab
    references, or retire it on Kennedy's word. D2 — place
    `LLFeedbackService` on a Main Scene GameObject with clips
    configured, or keep PlayMode self-creation deliberately. D3 — in
    `Editor/PersistentRuntimeHierarchyAuthoring.cs`, log a warning
    naming the missing component type + property when a `FindUnique`
    target is absent instead of silently skipping it.
11. Procurement service registration (F19): set
    `SaveLoadManager.PassXlProcurementService` to the production
    `OffMapNamedSkuProcurementService` instance in Main Scene bootstrap
    before the first save, or procurement persists and restores empty.
12. Playtest flag (F30): set
    `FirstLedgerSliceBootstrapper.treatPlayerAsNpcPlaytest` in the Main
    Scene Inspector for playtest scenes; verify the Continue OR
    behaviour (true forces NPC mode, false never clears a saved true).
13. Item asset (F07): add the `GeneralStoreItemTiers` ids (Priority1 /
    Secondary / Tertiary, including sugar, coffee, tea) to the
    `GeneralStoreBusinessDefinition` asset in the Unity project;
    verify in Editor.
14. Unity-only verification (F18/F20/F21): compile `SaveLoadManager`
    and `FirstLedgerSliceBootstrapper` in Editor; run Unity Test Runner
    / PlayMode; run `ConstructionInputSupportTests` (109 tests) with
    per-test timing to find the 1800s-timeout pathology; diagnose the 3
    Phase H failures in a dedicated pass (not attempted here).
15. SelfCollect journey (F38 remainder): implement player/NPC travel
    execution for procurement pickup before any SelfCollect quote is
    orderable; the bridge refuses SelfCollect until then.

## Design forks needing Kennedy's decision

Nothing here is guessed; every rate, price, weight, and frequency below
is a test parameter today, never canon.

1. Slice-departure gate (F09): wire `PopulationManager` to set
   `DemographicDriverContext.AllowDepartures = !SuppressMigrantArrivals`
   (one line in `PopulationManager` where `AllowNewArrivals` is already
   mapped), or leave slice departures running? The mechanism is done
   and tested; activation is the decision. Note the existing code
   comment reserved this to Kennedy ("no departures" was not the slice
   request as originally stated).
2. Retire the old 4% arrival roll (F10/F35): delete or gate
   `MigrationDriver.MigrateIn` (4% monthly, smallest household, no
   job/housing/reputation check) in favour of the traveler +
   recruitment + settlement channels? Retiring it changes existing
   tests and the ordinary-scenario arrival rate.
3. Unify departure paths (F11/F12): route legacy
   `MigrationDriver.MigrateOut` departures through
   `Outmigration.Depart` with a caller-supplied destination, or accept
   that legacy departures stay untracked to any destination (and
   therefore unfindable / unrecruitable)? Destinations are never
   randomised; a legacy departure has no destination to record today.
4. Freight rates and supplier prices (F05/F37): cents-per-mile freight,
   freight-business commission, and every supplier goods price. The
   bridge passes quote cents through exactly; it invents none.
5. Reputation weights (F23): housing / work / growth / opinion weights
   for `TownReputationModel` (tests use 0.3 / 0.5 / 0.1 / 0.1, flagged
   test-only). Work is described as the biggest factor; the weight that
   makes it so is Kennedy's call.
6. Destination naming (F12): what counts as a valid destination record
   for a departure (family location, previous home, supplier/route
   town, named new destination + reason) and who supplies it.
7. Traveler frequency (F24): how often through-travelers arrive, by
   route and season if applicable. No generator exists until this is set.
8. D1 GameObject vs retirement (F13): place
   `LogisticsTransportPresentationController` in Main Scene or retire it.
9. Flour lot ledger home (F17): does bakery flour live in the
   procurement service's standalone lot ledger, in `BakeryFlourStock`,
   or in both with a consume bridge? The bridge design above assumes
   consume-from-procurement-into-stock; the ownership call is Kennedy's.
10. Bakery transfer ownership (F16): which Main Scene owner/driver holds
    both the staged chain and the shop runtime, and whether staged lots
    move wholesale or only on bake completion.
11. Double-ordering guard (F26): `StoreProcurementChain` (category stock)
    vs `OffMapNamedSkuProcurementService` (standalone lots) — is a
    shared need/dedupe key wanted, or do the two paths stay independent
    by design?
12. Friend graph (F36): build a friend graph so recruitment can offer
    friends as leads, or accept family-only recruitment?
13. Experience formation rate (F34): playtest-feel review; any rate
    change is canon-sensitive and was not made here.

