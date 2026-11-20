# Household Food Security and Physical Shopping Recovery

Date: 2026-11-19
Branch: `codex/player-economic-participation-2026-11-18`
HEAD: `8537f52fb7dea263fa3ba62da6cbd4d6b5aefbf8`
Unity: 6000.4.2f1

## Status

**SOURCE CHANGES PRESENT; UNITY UNVERIFIED; NOT READY TO CLAIM FOOD RECOVERY.**

Current read-only Windows measurements show 3.01 GB free physical RAM and 49.82 GB committed of a 54.96 GB commit limit. Five Unity processes are present; the target Unity 6000.4.2f1 Editor is responsive and its Main Scene is in Edit Mode. The visible Console buffer is retained runtime output, not a live simulation. No compiler, Editor test, PlayMode test, save/continue, or new simulation was launched because the machine remains resource constrained and multiple Unity processes are active. The completed 30-day run below predates the changes described here and cannot verify them.

## Evidence reviewed

The latest available integrated baseline is `Reports/Playtests/Runs/20261119T082642Z-117e519b`. It completed 30/30 days at 250x on seed 1886, but is explicitly marked `CALENDAR_COMPLETE_REQUIRES_REVIEW`. It reported 828 meals eaten and 194 missed (last ten days: 248 eaten, 80 missed), and a 14-resident authored roster. H1's `trip-2-77` for P1 remained Outbound in daily evidence through day 30. It recorded 351 purchased units / 1,797c across 17 days, but no direct meal-path purchases; it does not prove a completed physical home receipt or subsequent consumption. The run metadata identifies source SHA `77a34133ed2df72b9d468072d8efe253a72ed95e`, not the current HEAD or current working tree.

A read-only check of the open Unity window showed Edit Mode, Main Scene loaded, and a retained Console buffer with 349 logs, 131 warnings, and 0 errors. Visible day-5 records include H1 at building 6 with two members and zero provided sleeping places, and a domestic `fuel-handling` deferral for P7 because no Firewood-type storage location is authored on that home. The local Natural Wood prefab contains stove and porch/drop-off points but no Firewood point. This confirms a current authoring/navigation blocker for fuel handling in that home; it does not establish that P7 is the H1 resident or that the warning belongs to the 30-day run above. No Editor state was changed.

Repository `AGENTS.md` was reviewed. The supplied working directory does not contain the named `Planning Sources` directory; a versioned staging folder appears empty and an older backup folder contains Canon XXXI and Technical XIV PDFs. The attached November 19 Third Muse Audit PDF was not found in the available local attachment directory. Findings below rely on current source inspection and the supplied assignment, not on an unavailable audit PDF.

## Implemented in the current working tree

### Shopping trip liveness

`ShoppingTripDriver.SweepActivePhysicalRoutes` checks persisted route deadlines and no-progress timeouts independently of due-event ownership. A missing `hasPhysicallyArrived` callback can no longer skip expiry: physical arrival is only credited when the callback exists and confirms it. Invalid route estimates do not refresh progress. Failed outbound trips keep the need open, cancel the active task/reservations, and attempt a real return route; paid goods remain in custody for recovery rather than being discarded.

Added/extended coverage in `ShoppingTripDriverTests`: non-progressing routes, missing/invalid estimates, outbound expiry without an arrival callback, and interrupted-save restoration paths. The liveness bounds currently use a six-hour no-progress threshold and a 24-hour absolute deadline. These are implementation constants requiring Unity travel-time review before tuning.

This is a recovery mechanism, not proof of the old P1 blockage's runtime cause. The 30-day export does not include mover route position/remaining-distance samples for P1, so it cannot establish whether the old trip was held by geometry, route ownership, a callback, or task scheduling.

### Physical counter service and settlement

Production household shopping now sets `RequirePhysicalCounterService`. A queued sale waits for a `TendCustomer` task; settlement is deferred until the task is complete and the customer is still the queue head. The General Store clerk resolver accepts the owner-operator slot through `TryGetServiceClerkPersonId`, then checks physical presence at that store building. The bakery now has a separate live queue, resolves its owner and active employees at the actual bakery premises, and uses the same task authority. A missing queue now fails closed in production mode; it cannot fall through to direct settlement. A customer with no serving clerk waits under the existing finite patience rule, then walks home empty-handed with the need open and no cash or goods moved.

Added a missing-queue no-transaction regression, the bakery queue save/restore-before-binding regression, and a production-binding source assertion. Tests have **not** been executed. Bakery opening-hour policy remains unavailable in the adapter/runtime; service still requires a physically present, task-available person, but no formal bakery trading calendar is currently bound.

### Food transaction authority and SKU identity

The physical Phase C food path is marked authoritative for the General Store. The legacy reserve sales/depletion path is gated off in that mode. Item offers require identified item-level stock, so pooled `staple_food` stock cannot be represented as bread without a bread lot. The existing carried/arranged receipt path continues to mirror an actual lot receipt into the reserve once; lot-backed meal consumption mirrors the actual lot deduction back once. Added `HouseholdReserveInventoryTests.PhysicalFoodAuthoritySkipsLegacyReserveConsumptionAndSales`.

This source path still needs a production transaction reconciliation test covering source lot decrease, one household debit, one seller credit, unique batch/receipt, one home inventory credit, one reserve mirror, and subsequent lot consumption. The old 30-day run's category totals are not evidence for those invariants.

### Meal schedule

The production lot-backed meal path is now deferred from the daily needs batch and resolved at the scheduled 07:30, 12:00, and 18:30 meal slots by `SimulationDrivers`. A food receipt before a later slot can be consumed in that same day if it arrives within the 60-minute flexible meal window. The person/day/slot meal log is the idempotency key, so repeated ticks and Continue do not consume the same slot twice. Prior-day nutrition is finalized from the per-slot records. Raw flour was removed from the ready-to-eat priority; it must first pass the existing domestic preparation path. Added regressions for same-day receipt, repeated-slot idempotence, flour refusal, and deferral from the daily service. None were run.

The dining movement driver still does not gate the actual inventory deduction on a resident physically reaching a reserved chair; it requests movement and falls back to eating in place on unsupported or failed seat routes. This is currently an explicit fallback, but actual home entry and activity-anchor reachability require Unity verification. The legacy reserve-only needs API remains compatible and is not the production lot-backed meal source.

## Findings not resolved

1. **Opening provisions:** generated households seed an 8-unit staple reserve (with a small household-size adjustment), which becomes an unspecified staple lot. Three meals/day at the current two-units-per-unspecified-staple serving implies this is far below the requested approximate seven-day calibration for most households. No new goods were granted in code: doing so without an authored, idempotent scenario endowment and serialized local configuration would be unsafe. The local authoritative `DevelopmentTownConfiguration.asset` was intentionally not edited.
2. **Proactive replenishment:** the production driver already has 3-day reorder and 7-day target thresholds, but these are planning thresholds and do not prove that purchases can be received, stored, or eaten. Confirm same-day timing and actual lot coverage in Main Scene.
3. **Bakery hours:** the adapter has no formal business-hours/calendar authority; owner/employee presence and task availability gate physical service, but configured closure behavior cannot be confirmed.
4. **H1 route cause:** current source contains a deadline-based recovery, but no post-change live evidence establishes the actual P1 trip is moving, recovering, or successfully completing.
5. **Owner/Emma selection:** the scheduling scorer was changed to inspect reservation overlap in the proposed shopping window, not the entire afternoon. No live comparison of Kennedy and Emma has been performed.
6. **Save/Continue:** trip route and service wait fields are serialized in DTOs, but no real save/load cycle was tested with an outbound trip, a queued shopper, a paid batch, or a service task.
7. **Initial pantry and household purchasing:** opening provisions still migrate from abstract category reserves to unspecified lots. There is no item-level, member-scaled, roughly seven-day New Game pantry authoring/endowment. The owner-edited configuration was preserved. Existing three-day/target-seven-day reorder values are source settings, not evidence of replenishment or stock sufficiency.

## Tests prepared; not executed

- `ShoppingTripDriverTests`: route timeout, invalid estimate, missing arrival callback, physical counter service, missing queue, absent clerk, save/restore of route/service state.
- `CampaignCCustomerPatiencePersistenceTests.BakeryCounterQueue_SaveRestoreBeforeRuntimeBinding_PreservesWaitingAndPatience`.
- `HouseholdReserveInventoryTests.PhysicalFoodAuthoritySkipsLegacyReserveConsumptionAndSales`.
- `DailyNeedsServiceTests.DefaultMealScheduleIsChronologicalAndScarcityDoesNotRemoveSlots`.
- `DailyNeedsServiceTests.LotBackedBread_IsEatenWithoutCooking_AndScarceMiddayIsRecordedAsMissed`.
- `PhaseBHouseholdConsumptionTests.MealPolicy_MiddayMeal_RemainsRequiredWhenStoresAreLow`.
- `GeneralStoreBreadPurchaseIntegrationTests` affected meal expectations.
- When Unity is healthy: also run `HouseholdShoppingLoopTests`, `HouseholdShortageAllocationTests`, `PurchasedRetailLotReceiptTests`, `CampaignBShopperAvailabilityTests`, and `SimulationDriversSceneBindingTests`.

No test result is claimed. The focused suites above, including new bakery-service and chronological-meal regressions, have not been run against this working tree.

## Git and preservation

The working tree contains substantial unrelated and owner-local modifications from prior camera, furnishing, Playtest-window, Main Scene, ScriptableObject, terrain, font, and prefab work. None were staged. A current `git diff --check` attempt is blocked by the known empty loose object at `.git/objects/bc/9c646edf1b1ec757106d66b862282fc959f0af`. It was not repaired. No commit or push was attempted because the mixed dirty tree cannot be safely reviewed or isolated while Git cannot read that object. This pass changed source and tests only; no Unity assets or scenes were modified.

## First verification sequence when safe

1. Restore adequate system commit headroom and use the existing Editor only; compile once.
2. Run the focused Editor suites listed above, including the new route and service tests.
3. In Main Scene at 10x, capture P1's trip ID, route mode, visible mover state, destination, remaining route estimate, assigned task/reservation, and due-event identity at each transition.
4. Observe an open, stocked General Store and physically present Kennedy or employee serving a household purchase. Reconcile exact inventory and ledger deltas before and after home receipt.
5. Observe the same household consume the received item in a later meal and confirm the lot falls once. Capture a closure/empty-shelf/absent-clerk failure and verify need retention, no settlement, return, and reservation release.
6. Compare Kennedy and Emma in the same eligible shopping window, then test Sunday closure and Save/Continue with a live trip.
7. Test Glenn's bakery with real saleable bread, owner/employee arrival and finite wait behavior; establish an approved/authored bakery trading calendar if closures are required. The seven-day item-level opening pantry still needs a safe authoring and New Game endowment implementation. Only after short tests pass should an updated longer playtest be considered.
