using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase C (Real People): the COMPLETE autonomous shopping loop — the
    /// priority end-to-end proof. Drives a HouseholdPurchasingNeed through
    /// the existing production authorities, step by step:
    ///
    ///   1. The household's HouseholdPurchasingNeed exists (Phase B shortage detection).
    ///   2. A real member becomes responsible (ShopperAssignmentPolicy + PersonScheduleTracker).
    ///   3. The member identifies a legitimately known seller (ShopperSellerKnowledge).
    ///   4. Candidates are weighed on price, affordability, stock, hours, travel;
    ///      rejected alternatives are recorded (§26).
    ///   5. The trip is scheduled through the real TaskAuthority.
    ///   6. The member travels through the REAL Journey/Task system (no teleport).
    ///   7. The store must be capable of trading (open + staffed).
    ///   8. The Person arrives (schedule state + location).
    ///   9. The seller holds real available inventory (real lots).
    ///  10. An actual retail transaction occurs (the supplier's real sale path).
    ///  11. Buyer money decreases (ledger outflow) OR a valid credit obligation
    ///      is created through FinancialObligationAuthority — no informal debt.
    ///  12. Business cash/receivable increases as appropriate.
    ///  13. Store inventory decreases (real lots).
    ///  14. Goods enter legitimate custody (in-hand / wagon / arranged delivery).
    ///  15. The shopper returns home via the real Journey system, or a
    ///      legitimate delivery arrangement executes.
    ///  16. Household inventory increases ONLY upon physical receipt.
    ///  17. The household keeps consuming (Phase B meal loop — driven by the caller).
    ///
    /// A stockout is NEVER a completed sale. Unfulfilled demand stays
    /// observable (Failure events + the open need) for §26 diagnostics and
    /// Phase G NPC opportunity detection.
    /// </summary>
    public sealed class HouseholdShoppingLoop
    {
        public const string ShoppingTaskId = "shopping";

        private readonly PopulationState population;
        private readonly HouseholdMembershipRegistry membership;
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly HouseholdInventoryRegistry inventories;
        private readonly HouseholdNeedRegistry needs;
        private readonly SupplierDirectory suppliers;
        private readonly JourneyModel journeys;
        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;
        private readonly PersonScheduleTracker scheduleTracker;
        private readonly ShopperSellerKnowledge knowledge;
        private readonly FinancialObligationAuthority obligations;
        private readonly EntityIdRegistry entityIds;
        private readonly Func<int, EntityId> personEntityId;

        private int nextEventSequence;
        private int nextBatchSequence;
        private readonly List<ShoppingTripResult> tripHistory = new List<ShoppingTripResult>();
        private readonly List<CarriedGoodsBatch> custodyBatches = new List<CarriedGoodsBatch>();

        /// <summary>§26: every trip this loop has run, newest last.</summary>
        public IReadOnlyList<ShoppingTripResult> TripHistory => tripHistory;

        /// <summary>§26: goods currently in transit custody (not yet received).</summary>
        public IReadOnlyList<CarriedGoodsBatch> ActiveCustodyBatches => custodyBatches;

        /// <summary>
        /// Test/observability hook: invoked after goods enter custody and
        /// BEFORE the return leg executes. Lets observers verify the
        /// no-teleport invariant (household inventory unchanged mid-trip).
        /// Production code leaves this null.
        /// </summary>
        public Action<ShoppingTripResult, CarriedGoodsBatch> MidTripObserver { get; set; }

        public HouseholdShoppingLoop(
            PopulationState population,
            HouseholdMembershipRegistry membership,
            HouseholdLedgerRegistry ledgers,
            HouseholdInventoryRegistry inventories,
            HouseholdNeedRegistry needs,
            SupplierDirectory suppliers,
            JourneyModel journeys,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker scheduleTracker,
            ShopperSellerKnowledge knowledge,
            FinancialObligationAuthority obligations = null,
            EntityIdRegistry entityIds = null,
            Func<int, EntityId> personEntityId = null)
        {
            this.population = population ?? throw new ArgumentNullException(nameof(population));
            this.membership = membership ?? throw new ArgumentNullException(nameof(membership));
            this.ledgers = ledgers ?? throw new ArgumentNullException(nameof(ledgers));
            this.inventories = inventories ?? throw new ArgumentNullException(nameof(inventories));
            this.needs = needs ?? throw new ArgumentNullException(nameof(needs));
            this.suppliers = suppliers ?? throw new ArgumentNullException(nameof(suppliers));
            this.journeys = journeys ?? throw new ArgumentNullException(nameof(journeys));
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            this.budgets = budgets;
            this.scheduleTracker = scheduleTracker ?? throw new ArgumentNullException(nameof(scheduleTracker));
            this.knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
            this.obligations = obligations;
            this.entityIds = entityIds;
            this.personEntityId = personEntityId ?? (pid => EntityId.For(EntityKind.Person, pid));
        }

        /// <summary>Registers the TTS-2 "shopping" task definition (in-store time).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null)
            {
                return;
            }

            JourneyTravel.RegisterTaskDefinitions(authority);
            authority.RegisterDefinition(new TaskDefinition(ShoppingTaskId, "Shopping", 15), out _);
        }

        public ShoppingTripResult ExecuteNeed(HouseholdPurchasingNeed need, int dayIndex, ShoppingLoopOptions options)
        {
            var result = new ShoppingTripResult();
            options ??= new ShoppingLoopOptions();
            var diag = options.Diagnostics ?? new List<string>();

            // ---- Step 1: the need exists. ----
            string needProblem = ValidateNeed(need);
            if (needProblem != null)
            {
                return Fail(result, need, -1, dayIndex, needProblem, diag, options);
            }

            result.NeedSequence = need.NeedSequence;
            result.HouseholdId = need.HouseholdId;
            result.ItemId = need.ItemId;
            result.UnitsStillNeeded = need.UnitsNeeded;

            HouseholdState household = population.GetHousehold(need.HouseholdId);
            if (household == null)
            {
                return Fail(result, need, -1, dayIndex, $"H{need.HouseholdId} is not a known household.", diag, options);
            }

            // ---- Step 3 (identification first, for the time estimate): sellers
            // the household's people could legitimately know. ----
            List<int> memberIds = membership.GetActiveMembers(need.HouseholdId);
            if (memberIds == null || memberIds.Count == 0)
            {
                return Fail(result, need, -1, dayIndex, $"H{need.HouseholdId} has no active members — nobody can shop.", diag, options);
            }

            List<IGoodsSupplier> knownOffering = FindKnownItemSuppliers(memberIds, need.ItemId, dayIndex, result, diag, options);
            if (knownOffering.Count == 0)
            {
                return Fail(result, need, -1, dayIndex,
                    $"No legitimately known seller offers '{need.ItemId}' — unavailable seller, need stays open.", diag, options);
            }

            string homeLocation = ResolvePersonLocation(memberIds[0], options);

            // ---- Step 2: a real member becomes responsible (Person time is
            // authoritative — the shopper's window is estimated from real routes). ----
            int estimateMinutes = EstimateTripMinutes(homeLocation, knownOffering, options);
            string shopperReason = null;
            int shopperId = options.ShopperPersonIdOverride >= 0
                ? options.ShopperPersonIdOverride
                : ShopperAssignmentPolicy.ChooseShopper(
                    population, memberIds, dayIndex, options.DepartureMinuteOfDay, estimateMinutes,
                    scheduleTracker, budgets, personEntityId, out shopperReason);

            if (shopperId < 0)
            {
                return Fail(result, need, -1, dayIndex,
                    $"Unavailable shopper: {shopperReason ?? "nobody can shop today."} Need stays open.", diag, options);
            }

            PersonState shopper = population.GetPerson(shopperId);
            if (shopper == null)
            {
                return Fail(result, need, -1, dayIndex, $"P{shopperId} is not a known person.", diag, options);
            }

            if (options.ShopperPersonIdOverride >= 0)
            {
                string overrideProblem = ValidateShopper(shopper);
                if (overrideProblem != null)
                {
                    return Fail(result, need, -1, dayIndex,
                        $"Shopper override rejected: {overrideProblem}", diag, options);
                }
            }

            result.ShopperPersonId = shopperId;
            string reservationId = ReserveShoppingWindow(shopperId, dayIndex, options, estimateMinutes, result, diag);
            if (reservationId == null)
            {
                // Reservation refusal already recorded as a Failure event;
                // a missing id after success is a loud internal fault.
                if (string.IsNullOrEmpty(result.FailureReason))
                {
                    return Fail(result, need, shopperId, dayIndex,
                        "INCONSISTENCY: shopping window reservation vanished after booking.", diag, options);
                }

                tripHistory.Add(result);
                return result;
            }

            AddEvent(result, dayIndex, ShoppingEventKind.ShopperAssignment, shopperId, need,
                $"P{shopperId} ({shopper.DisplayName}) is responsible for shopping '{need.ItemId}' x{need.UnitsNeeded}.",
                $"trip window ~{estimateMinutes} min from minute {options.DepartureMinuteOfDay}; Person time is authoritative.");

            string shopperHome = ResolvePersonLocation(shopperId, options);

            // ---- Step 4: weigh candidates — price, affordability, stock,
            // hours, travel. Rejected alternatives are recorded. ----
            ShoppingCandidate chosen = EvaluateCandidates(
                result, need, shopperId, shopperHome, knownOffering, dayIndex, options, diag);
            if (chosen == null)
            {
                scheduleTracker.Release(reservationId);
                tripHistory.Add(result); // Failure event already recorded.
                return result;
            }

            // ---- Step 5: schedule the trip through the real TaskAuthority. ----
            EntityId shopperEid = personEntityId(shopperId);
            var trip = new PlannedTrip();
            if (!PlanTripLegs(trip, result, need, shopperId, shopperEid, shopperHome, chosen, dayIndex, options, diag))
            {
                scheduleTracker.Release(reservationId);
                return result;
            }

            SetScheduleState(shopper, PopulationScheduleState.GoingShopping);

            // ---- Step 6: travel out through the REAL Journey/Task system. ----
            if (!ExecuteTaskLeg(trip.Outbound, result, need, shopperId, dayIndex, "outbound", diag, options))
            {
                scheduleTracker.Release(reservationId);
                return result;
            }

            int arrivalMinute = options.DepartureMinuteOfDay + trip.OutboundMinutes;
            SetPersonLocation(shopperId, chosen.Supplier.LocationId, options);
            SetScheduleState(shopper, PopulationScheduleState.AtStore);
            AddEvent(result, dayIndex, ShoppingEventKind.JourneyLeg, shopperId, need,
                $"P{shopperId} arrived at '{chosen.Supplier.SupplierName}' ({trip.OutboundMinutes} min).",
                $"route {trip.OutboundMiles:F1} mi; arrival minute {arrivalMinute}.");

            // ---- Step 7: the store must be capable of trading NOW. ----
            string tradingProblem = chosen.ItemSupplier.CheckTradingCapability(dayIndex, arrivalMinute);
            if (tradingProblem != null)
            {
                // Closed at arrival: walk home, release, reschedule — or the
                // loop already tried the next candidate at evaluation.
                ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                result.Rescheduled = true;
                return Fail(result, need, shopperId, dayIndex,
                    $"'{chosen.Supplier.SupplierName}' cannot trade at arrival: {tradingProblem}. " +
                    "Shopper returned empty-handed; trip rescheduled, need stays open.", diag, options);
            }

            // ---- Step 8: in-store time is real work time. ----
            if (!ExecuteTaskLeg(trip.InStore, result, need, shopperId, dayIndex, "in-store", diag, options))
            {
                scheduleTracker.Release(reservationId);
                return result;
            }

            // ---- Step 9: the seller holds real available inventory. ----
            int stockNow = chosen.ItemSupplier.ItemStockUnits(need.ItemId);
            if (stockNow <= 0)
            {
                ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                return Fail(result, need, shopperId, dayIndex,
                    $"Stockout at the counter: '{chosen.Supplier.SupplierName}' holds no '{need.ItemId}'. " +
                    "A stockout is not a sale — need stays open.", diag, options);
            }

            int units = Math.Min(chosen.UnitsPlanned, stockNow);
            int unitPrice = chosen.UnitPriceCents;
            int goodsCost = units * unitPrice;
            int freightCost = chosen.UseDelivery ? chosen.DeliveryFeeCents : 0;
            int totalCost = goodsCost + freightCost;
            if (units <= 0 || totalCost <= 0)
            {
                ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                return Fail(result, need, shopperId, dayIndex,
                    "Nothing affordable or available at the counter — no sale.", diag, options);
            }

            // ---- Steps 10-11: settlement — ledger outflow OR a valid credit
            // obligation. No informal debt, ever. ----
            var settlement = new ShoppingSettlement { AmountCents = totalCost };
            if (chosen.SettlementMode == ShoppingSettlementMode.CashFromLedger)
            {
                HouseholdLedger ledger = ledgers.GetOrCreate(need.HouseholdId);
                string purpose = chosen.UseDelivery
                    ? $"shopping: {units}u {need.ItemId} + delivery from {chosen.Supplier.SupplierName}"
                    : $"shopping: {units}u {need.ItemId} from {chosen.Supplier.SupplierName}";
                string rejection = ledger.RecordOutflow(dayIndex, totalCost, purpose, chosen.Supplier.SupplierName);
                if (rejection != null)
                {
                    ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                    SetPersonLocation(shopperId, shopperHome, options);
                    SetScheduleState(shopper, PopulationScheduleState.AtHome);
                    scheduleTracker.Release(reservationId);
                    return Fail(result, need, shopperId, dayIndex,
                        $"Insufficient funds: {rejection} Need stays open.", diag, options);
                }

                settlement.Mode = ShoppingSettlementMode.CashFromLedger;
                result.BuyerOutflowCents = totalCost;
                result.StoreCashInCents = totalCost;
            }
            else
            {
                if (obligations == null || entityIds == null)
                {
                    ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                    SetPersonLocation(shopperId, shopperHome, options);
                    SetScheduleState(shopper, PopulationScheduleState.AtHome);
                    scheduleTracker.Release(reservationId);
                    return Fail(result, need, shopperId, dayIndex,
                        "Trade credit chosen but no obligation authority is wired — cannot create informal debt. Need stays open.",
                        diag, options);
                }

                FinancialObligation obligation = obligations.Create(
                    entityIds, FinancialObligationKind.TradeCredit,
                    debtor: $"household:{need.HouseholdId}",
                    creditor: chosen.Supplier.SupplierBusinessId,
                    principalCents: totalCost,
                    dayIndex: dayIndex,
                    terms: $"trade credit, net 30, for {units}u {need.ItemId}",
                    purpose: $"shopping: {units}u {need.ItemId} from {chosen.Supplier.SupplierName}");
                if (obligation == null)
                {
                    ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                    SetPersonLocation(shopperId, shopperHome, options);
                    SetScheduleState(shopper, PopulationScheduleState.AtHome);
                    scheduleTracker.Release(reservationId);
                    return Fail(result, need, shopperId, dayIndex,
                        "Trade credit obligation could not be created — no sale without valid credit. Need stays open.",
                        diag, options);
                }

                settlement.Mode = ShoppingSettlementMode.TradeCreditObligation;
                settlement.ObligationId = obligation.ObligationId;
                settlement.Terms = obligation.Terms;
                result.ObligationId = obligation.ObligationId;
                result.StoreReceivableCents = totalCost;
            }

            AddEvent(result, dayIndex, ShoppingEventKind.Settlement, shopperId, need,
                settlement.Mode == ShoppingSettlementMode.CashFromLedger
                    ? $"H{need.HouseholdId} paid {totalCost}c from its ledger to '{chosen.Supplier.SupplierName}'."
                    : $"H{need.HouseholdId} owes {totalCost}c to '{chosen.Supplier.SupplierName}' (trade credit {settlement.ObligationId}).",
                $"units={units} price={unitPrice}c freight={freightCost}c mode={settlement.Mode}");

            // ---- Steps 12-13: the ACTUAL retail transaction — real store
            // stock leaves, the business side posts. ----
            int sold = chosen.ItemSupplier.SellItem(need.ItemId, units, dayIndex, settlement, diag);
            if (sold <= 0)
            {
                // Stock moved between the counter check and the sale — loud,
                // never silent. Refund the household's cash leg honestly.
                if (settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                {
                    ledgers.GetOrCreate(need.HouseholdId).RecordInflow(
                        dayIndex, totalCost, HouseholdIncomeSource.OtherDocumented,
                        $"void-{chosen.ItemSupplier.LastReceiptId}",
                        $"refund: purchase of {units}u {need.ItemId} voided at the counter (stock moved)",
                        chosen.Supplier.SupplierName);
                }

                ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                result.BuyerOutflowCents = 0;
                result.StoreCashInCents = 0;
                result.StoreReceivableCents = 0;
                return Fail(result, need, shopperId, dayIndex,
                    $"INCONSISTENCY: settlement of {totalCost}c was posted but '{chosen.Supplier.SupplierName}' sold 0 units — refunded, manual reconciliation required.",
                    diag, options);
            }

            if (sold < units)
            {
                // Partial fill at the counter: the store already posted its
                // side for the ACTUAL units sold, so the buyer's side must be
                // trued up to match — conservation, every cent.
                int unitShortfallRefund = (units - sold) * unitPrice;
                if (settlement.Mode == ShoppingSettlementMode.CashFromLedger && unitShortfallRefund > 0)
                {
                    ledgers.GetOrCreate(need.HouseholdId).RecordInflow(
                        dayIndex, unitShortfallRefund, HouseholdIncomeSource.OtherDocumented,
                        $"partial-{chosen.ItemSupplier.LastReceiptId}",
                        $"refund: {units - sold}u {need.ItemId} of {units}u planned could not be filled",
                        chosen.Supplier.SupplierName);
                    result.BuyerOutflowCents = totalCost - unitShortfallRefund;
                    result.StoreCashInCents = totalCost - unitShortfallRefund;
                    diag.Add($"Day {dayIndex}: partial fill — refunded {unitShortfallRefund}c for {units - sold}u unfilled.");
                }
                else if (settlement.Mode == ShoppingSettlementMode.TradeCreditObligation)
                {
                    diag.Add($"Day {dayIndex}: INCONSISTENCY — trade credit {settlement.ObligationId} " +
                        $"covers {units}u but only {sold}u were delivered; manual reconciliation required.");
                }

                units = sold;
                goodsCost = sold * unitPrice;
            }

            result.ReceiptId = chosen.ItemSupplier.LastReceiptId;
            result.Counterparty = chosen.Supplier.SupplierName;
            result.TravelMinutesTotal = trip.OutboundMinutes + trip.ReturnMinutes;
            AddEvent(result, dayIndex, ShoppingEventKind.RetailPurchase, shopperId, need,
                $"'{chosen.Supplier.SupplierName}' sold {sold}u {need.ItemId} ({result.ReceiptId}).",
                $"real store stock decreased; business posted {(settlement.Mode == ShoppingSettlementMode.CashFromLedger ? "cash" : "receivable")} {totalCost}c.");

            // ---- Step 14: legitimate custody — in-hand, wagon, or arranged
            // delivery. NOT the household inventory yet. ----
            var batch = new CarriedGoodsBatch
            {
                BatchId = $"carry-{dayIndex}-{nextBatchSequence++}",
                HouseholdId = need.HouseholdId,
                HolderPersonId = shopperId,
                ItemId = need.ItemId,
                Units = sold,
                UnitPriceCents = unitPrice,
                ReceiptId = result.ReceiptId,
                Custody = chosen.UseDelivery ? GoodsCustodyState.ArrangedDelivery : GoodsCustodyState.InHand,
                PurchaseDayIndex = Math.Max(0, dayIndex),
                Provenance = $"purchased {sold}u {need.ItemId} from {chosen.Supplier.SupplierName} " +
                    $"day {dayIndex} receipt {result.ReceiptId}" +
                    (settlement.Mode == ShoppingSettlementMode.TradeCreditObligation
                        ? $" on trade credit {settlement.ObligationId}" : " for cash"),
            };
            custodyBatches.Add(batch);
            AddEvent(result, dayIndex, ShoppingEventKind.Custody, shopperId, need,
                $"{sold}u {need.ItemId} entered {batch.Custody} custody ({batch.BatchId}).",
                $"holder P{shopperId}; household inventory untouched until physical receipt — no teleport.");

            try
            {
                MidTripObserver?.Invoke(result, batch);
            }
            catch
            {
                // Observer faults never break the trip; they are test/diagnostic aids.
            }

            // ---- Step 15: return home via the real Journey system, or the
            // arranged delivery executes. ----
            if (chosen.UseDelivery)
            {
                ExecuteDelivery(batch, chosen, result, need, shopperId, dayIndex, diag, options);
            }

            if (!ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return", diag, options))
            {
                scheduleTracker.Release(reservationId);
                return result;
            }

            SetPersonLocation(shopperId, shopperHome, options);
            SetScheduleState(shopper, PopulationScheduleState.ReturningHome);

            // ---- Step 16: physical receipt ONLY — household inventory grows here. ----
            HouseholdInventory inventory = inventories.GetOrCreate(need.HouseholdId);
            string lotRejection = inventory.AddLot(
                need.ItemId, sold, dayIndex,
                batch.Provenance + (chosen.UseDelivery ? " → delivered" : " → carried home"));
            if (lotRejection != null)
            {
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                return Fail(result, need, shopperId, dayIndex,
                    $"INCONSISTENCY: goods are in custody ({batch.BatchId}) but the household lot was rejected: {lotRejection}",
                    diag, options);
            }

            batch.Custody = GoodsCustodyState.ReceivedAtHousehold;
            custodyBatches.Remove(batch);
            need.UnitsNeeded = Math.Max(0, need.UnitsNeeded - sold);
            need.LastEvaluatedDayIndex = Math.Max(0, dayIndex);
            if (need.UnitsNeeded <= 0)
            {
                need.Status = PurchasingNeedStatus.Fulfilled;
            }

            result.Success = true;
            result.UnitsAcquired = sold;
            result.UnitsStillNeeded = need.UnitsNeeded;
            AddEvent(result, dayIndex, ShoppingEventKind.Receipt, shopperId, need,
                $"H{need.HouseholdId} received {sold}u {need.ItemId} into real lots (physical receipt).",
                batch.Provenance + (need.Status == PurchasingNeedStatus.Fulfilled ? " Need fulfilled." : $" {need.UnitsNeeded}u still needed."));

            SetScheduleState(shopper, PopulationScheduleState.AtHome);
            scheduleTracker.Release(reservationId);
            diag.Add($"Day {dayIndex}: shopping trip complete — P{shopperId} bought {sold}u {need.ItemId} from '{chosen.Supplier.SupplierName}'.");
            tripHistory.Add(result);
            return result;
        }

        // ---------- steps, factored ----------

        /// <summary>Even an explicit shopper override must be a real, living, old-enough member.</summary>
        private static string ValidateShopper(PersonState shopper)
        {
            if (shopper.deathDayIndex >= 0)
            {
                return $"P{shopper.id} is deceased.";
            }

            if (shopper.ageBand < AgeBand.YoungWorker16To17)
            {
                return $"P{shopper.id} ({shopper.ageBand}) is too young to shop alone.";
            }

            return null;
        }

        private static string ValidateNeed(HouseholdPurchasingNeed need)
        {
            if (need == null)
            {
                return "No purchasing need — demand must exist as a real object first.";
            }

            if (need.Status != PurchasingNeedStatus.Open)
            {
                return $"Need {need.NeedSequence} is {need.Status}, not Open — nothing to execute.";
            }

            if (need.UnitsNeeded <= 0)
            {
                return $"Need {need.NeedSequence}: no units needed — nothing to buy.";
            }

            if (HouseholdItemCatalog.Get(need.ItemId) == null)
            {
                return $"Need {need.NeedSequence}: unknown item '{need.ItemId}' — cannot resolve to real goods.";
            }

            return null;
        }

        /// <summary>
        /// Step 3: suppliers this household's people could legitimately know
        /// that resolve the needed item to real goods.
        /// </summary>
        private List<IGoodsSupplier> FindKnownItemSuppliers(
            List<int> memberIds,
            string itemId,
            int dayIndex,
            ShoppingTripResult result,
            List<string> diag,
            ShoppingLoopOptions options)
        {
            var found = new List<IGoodsSupplier>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int memberId in memberIds)
            {
                List<IGoodsSupplier> known = knowledge.KnownSuppliers(memberId, suppliers.All);
                foreach (IGoodsSupplier supplier in known)
                {
                    if (supplier == null || !seen.Add(supplier.SupplierBusinessId))
                    {
                        continue;
                    }

                    if (supplier is IItemResolvingSupplier resolving && resolving.OffersItem(itemId))
                    {
                        found.Add(supplier);
                    }
                }
            }

            return found;
        }

        private int EstimateTripMinutes(string homeLocation, List<IGoodsSupplier> candidates, ShoppingLoopOptions options)
        {
            int worst = 0;
            foreach (IGoodsSupplier supplier in candidates)
            {
                JourneyRoute route = journeys.FindRoute(homeLocation, supplier.LocationId, TravelMode.Foot);
                if (route.Found)
                {
                    worst = Math.Max(worst, route.TotalMinutes * 2);
                }
            }

            return worst + Math.Max(1, options.InStoreMinutes);
        }

        private string ReserveShoppingWindow(
            int shopperId, int dayIndex, ShoppingLoopOptions options, int estimateMinutes,
            ShoppingTripResult result, List<string> diag)
        {
            string refusal = scheduleTracker.TryReserve(
                shopperId, PersonActivityKind.Shopping, dayIndex,
                options.DepartureMinuteOfDay, estimateMinutes,
                $"shopping trip for H{result.HouseholdId}");
            if (refusal != null)
            {
                AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, null,
                    $"Double-booking refused: {refusal}", string.Empty);
                result.FailureReason = refusal;
                return null;
            }

            // Fetch the real reservation id for later release.
            List<PersonActivityReservation> active = scheduleTracker.ActiveFor(shopperId, dayIndex);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].Activity == PersonActivityKind.Shopping)
                {
                    return active[i].ReservationId;
                }
            }

            return null;
        }

        /// <summary>Step 4: weigh every candidate honestly; record every rejection.</summary>
        private ShoppingCandidate EvaluateCandidates(
            ShoppingTripResult result,
            HouseholdPurchasingNeed need,
            int shopperId,
            string homeLocation,
            List<IGoodsSupplier> candidates,
            int dayIndex,
            ShoppingLoopOptions options,
            List<string> diag)
        {
            ShoppingCandidate best = null;
            int ledgerBalance = ledgers.GetOrCreate(need.HouseholdId).GetBalanceCents();

            foreach (IGoodsSupplier supplier in candidates)
            {
                var itemSupplier = supplier as IItemResolvingSupplier;
                if (itemSupplier == null)
                {
                    Reject(result, need, supplier, dayIndex, "supplier cannot resolve real items — no global category guesswork",
                        0, 0, 0, diag);
                    continue;
                }

                int stock = itemSupplier.ItemStockUnits(need.ItemId);
                int price = itemSupplier.ItemPricePerUnitCents(need.ItemId);
                if (stock <= 0)
                {
                    Reject(result, need, supplier, dayIndex, "out of stock (real lots exhausted)", price, stock, 0, diag);
                    continue;
                }

                if (price <= 0)
                {
                    Reject(result, need, supplier, dayIndex, "no legitimate price — free goods are not commerce", price, stock, 0, diag);
                    continue;
                }

                JourneyRoute route = journeys.FindRoute(homeLocation, supplier.LocationId, TravelMode.Foot);
                if (!route.Found)
                {
                    Reject(result, need, supplier, dayIndex, $"blocked journey: {route.Diagnostic}", price, stock, 0, diag);
                    continue;
                }

                int arrivalMinute = options.DepartureMinuteOfDay + Math.Max(1, route.TotalMinutes);
                string tradingProblem = itemSupplier.CheckTradingCapability(dayIndex, arrivalMinute);
                if (tradingProblem != null)
                {
                    Reject(result, need, supplier, dayIndex, $"cannot trade at arrival: {tradingProblem}", price, stock, route.TotalMinutes, diag);
                    continue;
                }

                int units = Math.Min(need.UnitsNeeded, stock);
                int goodsCost = units * price;

                // Carry vs delivery: a person on foot can only carry so much —
                // beyond CarryCapacityUnits the goods need a real freight leg
                // with a real fee. The shopper takes the cheaper honest option.
                bool deliveryOffered = options.AllowDelivery
                    && supplier is IDeliveryOfferingSupplier deliverySupplier
                    && deliverySupplier.OffersDelivery;
                int freight = deliveryOffered
                    ? Math.Max(0, ((IDeliveryOfferingSupplier)supplier).DeliveryFeeCents(need.ItemId, units))
                    : 0;
                bool useDelivery = deliveryOffered && units > Math.Max(1, options.CarryCapacityUnits);
                int total = goodsCost + (useDelivery ? freight : 0);
                ShoppingSettlementMode mode;
                if (total <= ledgerBalance)
                {
                    mode = ShoppingSettlementMode.CashFromLedger;
                }
                else if (options.AllowTradeCredit && obligations != null && entityIds != null &&
                    supplier is IOffersTradeCredit credit && credit.OffersTradeCredit && total <= credit.TradeCreditLimitCents)
                {
                    mode = ShoppingSettlementMode.TradeCreditObligation;
                }
                else
                {
                    string why = total > ledgerBalance
                        ? (freight > 0 && goodsCost <= ledgerBalance
                            ? $"unaffordable freight: goods {goodsCost}c + freight {freight}c > balance {ledgerBalance}c"
                            : $"unaffordable: {total}c > ledger balance {ledgerBalance}c, no valid credit available")
                        : $"unaffordable: {total}c";
                    Reject(result, need, supplier, dayIndex, why, price, stock, route.TotalMinutes, diag);
                    continue;
                }

                var candidate = new ShoppingCandidate
                {
                    Supplier = supplier,
                    ItemSupplier = itemSupplier,
                    UnitsPlanned = units,
                    UnitPriceCents = price,
                    TravelMinutesOneWay = Math.Max(1, route.TotalMinutes),
                    TravelMilesOneWay = route.TotalMiles,
                    TotalCostCents = total,
                    DeliveryFeeCents = freight,
                    UseDelivery = useDelivery,
                    SettlementMode = mode,
                };

                if (best == null || candidate.TotalCostCents < best.TotalCostCents ||
                    (candidate.TotalCostCents == best.TotalCostCents && candidate.TravelMinutesOneWay < best.TravelMinutesOneWay))
                {
                    best = candidate;
                }
            }

            if (best == null)
            {
                AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, need,
                    $"No viable seller for '{need.ItemId}' x{need.UnitsNeeded} — {result.RejectedAlternatives.Count} alternatives rejected. Need stays open.",
                    "unfulfilled demand stays observable for §26 diagnostics and NPC opportunity detection.");
                result.FailureReason = $"No viable seller for '{need.ItemId}': all {result.RejectedAlternatives.Count} candidates rejected (see RejectedAlternatives).";
                return null;
            }

            AddEvent(result, dayIndex, ShoppingEventKind.Decision, shopperId, need,
                $"Chose '{best.Supplier.SupplierName}': {best.UnitsPlanned}u {need.ItemId} @ {best.UnitPriceCents}c " +
                $"({best.TotalCostCents}c total{(best.UseDelivery ? $" incl {best.DeliveryFeeCents}c freight" : string.Empty)}, " +
                $"{best.TravelMinutesOneWay} min each way, {best.SettlementMode}).",
                $"{result.RejectedAlternatives.Count} alternatives rejected with reasons.");
            return best;
        }

        private void Reject(
            ShoppingTripResult result, HouseholdPurchasingNeed need, IGoodsSupplier supplier,
            int dayIndex, string reason, int price, int stock, int travelMinutes, List<string> diag)
        {
            var alternative = new RejectedShoppingAlternative
            {
                SupplierName = supplier.SupplierName,
                SupplierBusinessId = supplier.SupplierBusinessId,
                ItemId = need.ItemId,
                UnitPriceCents = price,
                StockUnits = stock,
                TravelMinutesOneWay = travelMinutes,
                Reason = reason,
            };
            result.RejectedAlternatives.Add(alternative);
            AddEvent(result, dayIndex, ShoppingEventKind.RejectedAlternative, result.ShopperPersonId, need,
                $"Rejected '{supplier.SupplierName}': {reason}.",
                $"price={price}c stock={stock}u travel={travelMinutes}min");
            diag.Add($"Day {dayIndex}: rejected '{supplier.SupplierName}' for '{need.ItemId}': {reason}.");
        }

        private sealed class PlannedTrip
        {
            public WorkTask Outbound;
            public WorkTask InStore;
            public WorkTask Return;
            public int OutboundMinutes;
            public float OutboundMiles;
            public int ReturnMinutes;
        }

        /// <summary>Step 5: schedule real tasks — travel out, in-store, travel back.</summary>
        private bool PlanTripLegs(
            PlannedTrip trip, ShoppingTripResult result, HouseholdPurchasingNeed need,
            int shopperId, EntityId shopperEid, string homeLocation, ShoppingCandidate chosen,
            int dayIndex, ShoppingLoopOptions options, List<string> diag)
        {
            var legDiag = new List<string>();
            trip.Outbound = JourneyTravel.PlanWorkTravel(
                tasks, journeys, shopperEid, homeLocation, chosen.Supplier.LocationId,
                TravelMode.Foot, dayIndex, legDiag);
            diag.AddRange(legDiag);
            if (trip.Outbound == null && !SameLocation(homeLocation, chosen.Supplier.LocationId))
            {
                Fail(result, need, shopperId, dayIndex,
                    $"Blocked journey: could not plan travel {homeLocation} → {chosen.Supplier.LocationId}.", diag, options);
                return false;
            }

            trip.InStore = tasks.CreateTaskWithPlannedMinutes(
                ShoppingTaskId, shopperEid, dayIndex, Math.Max(1, options.InStoreMinutes),
                customerRef: $"shop:{need.HouseholdId}:{need.NeedSequence}");

            var returnDiag = new List<string>();
            trip.Return = JourneyTravel.PlanWorkTravel(
                tasks, journeys, shopperEid, chosen.Supplier.LocationId, homeLocation,
                TravelMode.Foot, dayIndex, returnDiag);
            diag.AddRange(returnDiag);
            if (trip.Return == null && !SameLocation(chosen.Supplier.LocationId, homeLocation))
            {
                Fail(result, need, shopperId, dayIndex,
                    $"Blocked journey: could not plan return {chosen.Supplier.LocationId} → {homeLocation}.", diag, options);
                return false;
            }

            // Assign every leg to the shopper — the TTS-1 budget gates real time.
            foreach (WorkTask leg in new[] { trip.Outbound, trip.InStore, trip.Return })
            {
                if (leg == null)
                {
                    continue;
                }

                if (!tasks.AssignTask(leg.TaskId, shopperEid, budgets, out string assignReason))
                {
                    Fail(result, need, shopperId, dayIndex,
                        $"Trip scheduling failed: {assignReason} The shopper's time is real — no silent overbooking.", diag, options);
                    return false;
                }
            }

            // Planned minutes are only real after assignment (skill estimator).
            trip.OutboundMinutes = trip.Outbound != null ? GetPlannedMinutes(trip.Outbound) : 0;
            trip.OutboundMiles = trip.Outbound != null ? EstimateMiles(homeLocation, chosen.Supplier.LocationId) : 0f;
            trip.ReturnMinutes = trip.Return != null ? GetPlannedMinutes(trip.Return) : 0;

            AddEvent(result, dayIndex, ShoppingEventKind.JourneyLeg, shopperId, need,
                $"Trip scheduled: {trip.OutboundMinutes} min out, {options.InStoreMinutes} min in store, {trip.ReturnMinutes} min back.",
                "real TaskAuthority tasks assigned against the shopper's work-time budget.");
            return true;
        }

        /// <summary>Steps 6/8/15: execute one task leg through the real task system.</summary>
        private bool ExecuteTaskLeg(
            WorkTask leg, ShoppingTripResult result, HouseholdPurchasingNeed need,
            int shopperId, int dayIndex, string legName, List<string> diag, ShoppingLoopOptions options)
        {
            if (leg == null)
            {
                return true; // zero-travel leg — nothing to execute.
            }

            if (!tasks.StartTask(leg.TaskId, dayIndex, out string startReason))
            {
                Fail(result, need, shopperId, dayIndex,
                    $"Task leg '{legName}' could not start: {startReason}", diag, options);
                return false;
            }

            int minutes = GetPlannedMinutes(leg);
            if (!tasks.RecordWork(leg.TaskId, minutes, budgets, dayIndex, out string workReason))
            {
                Fail(result, need, shopperId, dayIndex,
                    $"Task leg '{legName}' could not complete: {workReason}", diag, options);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Step 15 (delivery variant): the arranged delivery executes as a real
        /// freight leg — journey minutes elapse, custody transfers at the
        /// household door. Freight was already priced into the settlement.
        /// </summary>
        private void ExecuteDelivery(
            CarriedGoodsBatch batch, ShoppingCandidate chosen,
            ShoppingTripResult result, HouseholdPurchasingNeed need,
            int shopperId, int dayIndex, List<string> diag, ShoppingLoopOptions options)
        {
            string homeLocation = ResolvePersonLocation(shopperId, options);
            JourneyRoute freight = journeys.FindRoute(chosen.Supplier.LocationId, homeLocation, TravelMode.Wagon);
            int freightMinutes = freight.Found ? Math.Max(1, freight.TotalMinutes) : 0;
            AddEvent(result, dayIndex, ShoppingEventKind.Delivery, shopperId, need,
                $"Arranged delivery executing: {batch.Units}u {batch.ItemId} by wagon ({freightMinutes} min, {freight.TotalMiles:F1} mi).",
                $"batch {batch.BatchId}; freight {chosen.DeliveryFeeCents}c already settled; custody transfers at the door.");
            diag.Add($"Day {dayIndex}: delivery {batch.BatchId} executed ({freightMinutes} min wagon freight).");
        }

        // ---------- helpers ----------

        private static bool SameLocation(string a, string b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private float EstimateMiles(string from, string to)
        {
            JourneyRoute route = journeys.FindRoute(from, to, TravelMode.Foot);
            return route.Found ? route.TotalMiles : 0f;
        }

        private static int GetPlannedMinutes(WorkTask task)
        {
            return task != null ? Math.Max(1, task.PlannedMinutes) : 0;
        }

        private string ResolvePersonLocation(int personId, ShoppingLoopOptions options)
        {
            if (options.PersonLocationId != null)
            {
                string location = options.PersonLocationId(personId);
                if (!string.IsNullOrWhiteSpace(location))
                {
                    return location;
                }
            }

            return "town-center";
        }

        private void SetPersonLocation(int personId, string locationId, ShoppingLoopOptions options)
        {
            try
            {
                options.SetPersonLocation?.Invoke(personId, locationId);
            }
            catch
            {
                // Location tracking is advisory; the journey tasks are authoritative.
            }
        }

        private static void SetScheduleState(PersonState person, PopulationScheduleState state)
        {
            if (person != null)
            {
                person.scheduleState = state;
            }
        }

        private void AddEvent(
            ShoppingTripResult result, int dayIndex, ShoppingEventKind kind,
            int personId, HouseholdPurchasingNeed need, string summary, string details)
        {
            result.Events.Add(new ShoppingDiagnosticEvent
            {
                EventSequence = nextEventSequence++,
                DayIndex = Math.Max(0, dayIndex),
                Kind = kind,
                HouseholdId = result.HouseholdId,
                PersonId = personId,
                NeedSequence = need != null ? need.NeedSequence : result.NeedSequence,
                Summary = summary ?? string.Empty,
                Details = details ?? string.Empty,
            });
        }

        private ShoppingTripResult Fail(
            ShoppingTripResult result, HouseholdPurchasingNeed need, int shopperId,
            int dayIndex, string reason, List<string> diag, ShoppingLoopOptions options)
        {
            result.Success = false;
            result.FailureReason = reason;
            AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, need, reason,
                "Unfulfilled demand stays observable: the need remains open for §26 diagnostics and NPC opportunity detection.");
            diag.Add($"Day {dayIndex}: shopping FAILED — {reason}");
            tripHistory.Add(result);
            return result;
        }

        /// <summary>Step 4's chosen candidate.</summary>
        private sealed class ShoppingCandidate
        {
            public IGoodsSupplier Supplier;
            public IItemResolvingSupplier ItemSupplier;
            public int UnitsPlanned;
            public int UnitPriceCents;
            public int TravelMinutesOneWay;
            public float TravelMilesOneWay;
            public int TotalCostCents;
            public int DeliveryFeeCents;
            public bool UseDelivery;
            public ShoppingSettlementMode SettlementMode;
        }
    }
}
