using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
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

        private PopulationState population;
        private HouseholdMembershipRegistry membership;
        private readonly HouseholdLedgerRegistry ledgers;
        private readonly HouseholdInventoryRegistry inventories;
        private readonly HouseholdNeedRegistry needs;
        private readonly SupplierDirectory suppliers;
        private JourneyModel journeys;
        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;
        private readonly PersonScheduleTracker scheduleTracker;
        private readonly ShopperSellerKnowledge knowledge;
        private readonly FinancialObligationAuthority obligations;
        private readonly EntityIdRegistry entityIds;
        private readonly Func<int, EntityId> personEntityId;
        private readonly Func<int, HouseholdSpendingPolicy> spendingPolicyProvider;
        private OffMapJourneyAuthority offMapJourneys;

        /// <summary>
        /// Plan 08 Slice 2 (Q03, AUD-266): the off-map journey authority
        /// handed in by the driver, mirroring
        /// DomesticLaborDriver.OffMapJourneys. Null = fail open (no
        /// off-map guard) until the driver wires it.
        /// </summary>
        public OffMapJourneyAuthority OffMapJourneys
        {
            get => offMapJourneys;
            set => offMapJourneys = value;
        }

        private int nextEventSequence;
        private int nextBatchSequence;
        private int nextTripSequence;
        private readonly List<ShoppingTripResult> tripHistory = new List<ShoppingTripResult>();
        private readonly List<CarriedGoodsBatch> custodyBatches = new List<CarriedGoodsBatch>();

        /// <summary>§26: every trip this loop has run, newest last.</summary>
        public IReadOnlyList<ShoppingTripResult> TripHistory => tripHistory;

        /// <summary>§26: goods currently in transit custody (not yet received).</summary>
        public IReadOnlyList<CarriedGoodsBatch> ActiveCustodyBatches => custodyBatches;

        /// <summary>
        /// Priority U-3: the loop's save surface for in-transit custody.
        /// Captures every batch currently held in transit plus the
        /// batch-id sequence, as COPIES — later settlement never mutates
        /// the captured DTO. Before this surface the batch existed only in
        /// the loop's private list, so a mid-trip save/load dropped the
        /// paid-for goods (F5's U-3 pin).
        /// </summary>
        public HouseholdShoppingCustodySaveDto CaptureCustodySaveDto()
        {
            var dto = new HouseholdShoppingCustodySaveDto { NextBatchSequence = nextBatchSequence };
            foreach (CarriedGoodsBatch batch in custodyBatches)
            {
                if (batch == null) continue;
                dto.Batches.Add(CopyCustodyBatch(batch));
            }

            return dto;
        }

        /// <summary>
        /// Priority U-3: clear-then-load for in-transit custody. Restores
        /// the captured batches as fresh copies (never the DTO's objects —
        /// the E-DEFECT-02 pattern — so a second restore of the same DTO
        /// loads the same state instead of already-run state). Duplicate
        /// batch ids in one DTO load once; already-received batches never
        /// resurrect; a null DTO clears to empty. The batch-id sequence is
        /// restored from the DTO and raised past every restored id, so a
        /// later settle never reuses an id.
        /// </summary>
        public void LoadCustodyFromSaveDto(HouseholdShoppingCustodySaveDto dto)
        {
            custodyBatches.Clear();
            nextBatchSequence = 0;
            if (dto == null) return;
            nextBatchSequence = Math.Max(0, dto.NextBatchSequence);
            if (dto.Batches == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CarriedGoodsBatch batch in dto.Batches)
            {
                if (batch == null || string.IsNullOrEmpty(batch.BatchId)) continue;
                if (batch.Custody == GoodsCustodyState.ReceivedAtHousehold) continue;
                if (!seen.Add(batch.BatchId)) continue;
                custodyBatches.Add(CopyCustodyBatch(batch));
                nextBatchSequence = Math.Max(nextBatchSequence, CustodyBatchSequence(batch.BatchId) + 1);
            }
        }

        /// <summary>
        /// Priority U-3: registers one restored batch in loop custody,
        /// idempotently — returns the loop-owned instance (the existing one
        /// when a batch with the same id is already held). The driver calls
        /// this when it restores a trip so the trip's batch and the loop's
        /// custody are ONE object, never two copies of the same goods: a
        /// receipt removes that one object from custody and deposits it
        /// exactly once.
        /// </summary>
        public CarriedGoodsBatch AttachRestoredBatch(CarriedGoodsBatch batch)
        {
            if (batch == null || string.IsNullOrEmpty(batch.BatchId)) return null;
            if (batch.Custody == GoodsCustodyState.ReceivedAtHousehold) return null;
            foreach (CarriedGoodsBatch existing in custodyBatches)
            {
                if (existing != null && string.Equals(existing.BatchId, batch.BatchId, StringComparison.Ordinal))
                {
                    return existing;
                }
            }

            CarriedGoodsBatch copy = CopyCustodyBatch(batch);
            custodyBatches.Add(copy);
            nextBatchSequence = Math.Max(nextBatchSequence, CustodyBatchSequence(batch.BatchId) + 1);
            return copy;
        }

        /// <summary>
        /// Priority U-3: Continue for a restored (or stranded) in-transit
        /// batch. Deposits one batch the loop still holds in custody into
        /// household storage through the existing receipt path
        /// (ReceiveSettledGoods) — the same path the immediate trip uses —
        /// exactly once: the batch leaves custody when the lot is booked.
        /// Returns null on success, else a loud reason and nothing moves.
        /// An ArrangedDelivery batch is refused here: delivery receipt is
        /// ReceiveDelivery's path (freight arrives at the door). A batch
        /// with no open need is refused: the receipt must close the need
        /// it was bought for, never invent one. No money moves here — the
        /// purchase settled before the save.
        /// </summary>
        public string ReceiveCarriedBatch(string batchId, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            CarriedGoodsBatch batch = null;
            foreach (CarriedGoodsBatch candidate in custodyBatches)
            {
                if (candidate != null && string.Equals(candidate.BatchId, batchId, StringComparison.Ordinal))
                {
                    batch = candidate;
                    break;
                }
            }

            if (batch == null)
            {
                return $"ReceiveCarriedBatch: no batch {batchId} in transit custody — nothing to receive.";
            }

            if (batch.Custody == GoodsCustodyState.ArrangedDelivery)
            {
                return $"ReceiveCarriedBatch: batch {batchId} is an arranged delivery — delivery receipt (ReceiveDelivery) is its path, not this one.";
            }

            if (batch.Custody == GoodsCustodyState.ReceivedAtHousehold)
            {
                return $"ReceiveCarriedBatch: batch {batchId} was already received — refusing a second receipt.";
            }

            HouseholdPurchasingNeed need = needs.GetOpenNeed(batch.HouseholdId, batch.ItemId);
            if (need == null)
            {
                return $"ReceiveCarriedBatch: batch {batchId} has no open need for {batch.ItemId} in H{batch.HouseholdId} — refusing to receive without the need it was bought for.";
            }

            var result = new ShoppingTripResult
            {
                NeedSequence = need.NeedSequence,
                HouseholdId = batch.HouseholdId,
                ShopperPersonId = batch.HolderPersonId,
                ItemId = batch.ItemId,
                ReceiptId = batch.ReceiptId,
                BuyerOutflowCents = batch.Units * batch.UnitPriceCents,
            };
            string receiptProblem = ReceiveSettledGoods(
                result, need, batch, batch.Units, Math.Max(1, batch.HouseholdUnitsPerStoreUnit),
                false, batch.SupplierName, batch.HolderPersonId, dayIndex, diag,
                new ShoppingLoopOptions());
            if (receiptProblem != null)
            {
                return receiptProblem;
            }

            tripHistory.Add(result);
            diag.Add($"Day {dayIndex}: carried batch {batchId} received after Continue — P{batch.HolderPersonId} delivered {result.UnitsAcquired}u {batch.ItemId} to H{batch.HouseholdId}.");
            return null;
        }

        private static CarriedGoodsBatch CopyCustodyBatch(CarriedGoodsBatch batch)
        {
            if (batch == null) return null;
            return new CarriedGoodsBatch
            {
                BatchId = batch.BatchId ?? string.Empty,
                HouseholdId = batch.HouseholdId,
                HolderPersonId = batch.HolderPersonId,
                ItemId = batch.ItemId ?? string.Empty,
                Units = batch.Units,
                UnitPriceCents = batch.UnitPriceCents,
                ReceiptId = batch.ReceiptId ?? string.Empty,
                Provenance = batch.Provenance ?? string.Empty,
                Custody = batch.Custody,
                PurchaseDayIndex = batch.PurchaseDayIndex,
                HouseholdUnitsPerStoreUnit = Math.Max(1, batch.HouseholdUnitsPerStoreUnit),
                SupplierName = batch.SupplierName ?? string.Empty,
                // Priority spoilage integration: the custody copy keeps
                // the batch's condition state verbatim (the SaveDto is a
                // value snapshot; the copy is a new batch object).
                PerishableCondition = batch.PerishableCondition,
                HasPerishableCondition = batch.HasPerishableCondition,
            };
        }

        /// <summary>Priority spoilage integration: the batch's attached condition state, or null when the batch carries none.</summary>
        private static PerishableLotState ConditionOf(CarriedGoodsBatch batch) =>
            batch != null && batch.HasPerishableCondition
                ? PerishableLotState.FromSaveDto(batch.PerishableCondition)
                : null;

        /// <summary>Parses the trailing sequence from a "carry-{day}-{seq}" batch id; -1 when it has none.</summary>
        private static int CustodyBatchSequence(string batchId)
        {
            if (string.IsNullOrEmpty(batchId)) return -1;
            int dash = batchId.LastIndexOf('-');
            if (dash < 0 || dash == batchId.Length - 1) return -1;
            return int.TryParse(batchId.Substring(dash + 1), out int sequence) ? sequence : -1;
        }

        /// <summary>
        /// Test/observability hook: invoked after goods enter custody and
        /// BEFORE the return leg executes. Lets observers verify the
        /// no-teleport invariant (household inventory unchanged mid-trip).
        /// Production code leaves this null.
        /// </summary>
        public Action<ShoppingTripResult, CarriedGoodsBatch> MidTripObserver { get; set; }

        /// <summary>
        /// Priority 4 Slice 4 — timing-observer feed for leg refusals
        /// (plan §5 Slice 4 item 3, §3.2 L6). When set (production: the
        /// hub-installed TaskDurationDiagnostics, wired by
        /// SimulationDrivers), leg AssignTask / StartTask refusals are fed
        /// in via <see cref="ITaskTimingObserver.RecordRefusal"/> so a
        /// repeatedly refused leg classifies instead of vanishing (G7).
        /// Null by default: the loop never refuses a trip for lack of a
        /// diagnostic. Follows the MidTripObserver pattern above.
        /// </summary>
        public ITaskTimingObserver TimingObserver { get; set; }

        /// <summary>
        /// Priority 9 Slice F3 (P9 plan section 4 Slice F3): the custody
        /// transfer log this loop records retail purchases and household
        /// receipts into, when set. Null (the default) means no recording —
        /// today behaviour. The log is an audit trail only: no flow reads
        /// it, and a logging failure never fails a trip.
        /// </summary>
        public CustodyTransferLog CustodyLog { get; set; }

        private PhysicalCapacityEvaluator capacityEvaluator;

        /// <summary>
        /// Priority V1: the F2 capacity evaluator this loop consults at
        /// settle time, before InHand custody is assigned. Always on: when
        /// nothing is set, the loop builds an OnFootPerson-only evaluator
        /// (no equipment, business, zone, or wagon authority is connected
        /// through the loop — the settle-time query is the on-foot leg
        /// only). A driver or test may set a fully-wired evaluator; the
        /// loop's settle consultation uses whichever instance is held.
        /// </summary>
        public PhysicalCapacityEvaluator CapacityEvaluator
        {
            get => capacityEvaluator ?? (capacityEvaluator =
                new PhysicalCapacityEvaluator(null, null, null, null));
            set => capacityEvaluator = value;
        }

        private LotIdentityResolver lotIdentityResolver;

        /// <summary>
        /// Priority V1: the F4 lot identity resolver this loop uses to
        /// answer what lot a receipt deposited. Always on: when nothing
        /// is set, the loop builds a resolver over its own household
        /// inventory registry. A driver or test may set a resolver over
        /// more authorities; the loop's deposit resolution uses whichever
        /// instance is held.
        /// </summary>
        public LotIdentityResolver LotIdentityResolver
        {
            get => lotIdentityResolver ?? (lotIdentityResolver =
                new LotIdentityResolver(inventories));
            set => lotIdentityResolver = value;
        }

        private PersonInventoryReadModel personInventory;

        /// <summary>
        /// Priority V1: the Slice 3 person inventory read model as this
        /// loop's production custody answer — what a person carries, in
        /// the read model's own terms. Always on: when nothing is set,
        /// the loop builds the read model over its own custody batches
        /// (via a HouseholdShoppingReadModel over this loop) and its own
        /// household inventory registry, answering against the live
        /// default carry cap. The receipt paths verify each deposit
        /// through this read model. A driver or test may set a read model
        /// built with different options instead.
        /// </summary>
        public PersonInventoryReadModel PersonInventory
        {
            get => personInventory ?? (personInventory =
                new PersonInventoryReadModel(new HouseholdShoppingReadModel(this, scheduleTracker), inventories));
            set => personInventory = value;
        }

        /// <summary>
        /// Priority V1: the capacity verdict the loop last consulted at
        /// settle time (null before any settle). The verdict is recorded,
        /// never silently passed: a DoesNotFit verdict on an over-cap
        /// load is recorded in diagnostics while the settled purchase
        /// proceeds per the loop's existing enforcement (see
        /// SettleAtCounter).
        /// </summary>
        public PhysicalCapacityVerdict? LastSettleCapacityVerdict { get; private set; }

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
            Func<int, EntityId> personEntityId = null,
            Func<int, HouseholdSpendingPolicy> spendingPolicyProvider = null,
            OffMapJourneyAuthority offMapJourneys = null)
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
            this.spendingPolicyProvider = spendingPolicyProvider;
            this.offMapJourneys = offMapJourneys;
        }

        /// <summary>
        /// Rebinds restored population authorities without discarding trip
        /// history or in-memory custody records owned by this loop.
        /// </summary>
        public void RebindPopulationAuthorities(
            PopulationState currentPopulation,
            HouseholdMembershipRegistry currentMembership,
            JourneyModel currentJourneys)
        {
            population = currentPopulation ?? throw new ArgumentNullException(nameof(currentPopulation));
            membership = currentMembership ?? throw new ArgumentNullException(nameof(currentMembership));
            journeys = currentJourneys ?? throw new ArgumentNullException(nameof(currentJourneys));
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

            HouseholdSpendingPolicy spendingPolicy = spendingPolicyProvider?.Invoke(need.HouseholdId);
            if (spendingPolicy != null && !need.IsNecessity)
            {
                int permitted = (int)Math.Floor(need.UnitsNeeded * Math.Max(0, spendingPolicy.DiscretionaryCapPercent) / 100.0);
                if (permitted <= 0)
                    return Fail(result, need, -1, dayIndex, "Discretionary purchase deferred by household spending policy.", diag, options);
                need.UnitsNeeded = Math.Min(need.UnitsNeeded, permitted);
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
                    DescribeNoKnownItemOffer(memberIds, need.ItemId), diag, options);
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
                    scheduleTracker, budgets, personEntityId, out shopperReason, offMapJourneys);

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

            // ---- Steps 9-14: counter settlement (stock check, cash/credit,
            // real sale with partial-fill true-up, custody batch). One
            // conservation logic shared with the real-time driver.
            SettlementOutcome counter = SettleAtCounter(result, trip, need, chosen, shopperId, dayIndex, diag, options);
            if (!counter.Success)
            {
                ExecuteTaskLeg(trip.Return, result, need, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                return Fail(result, need, shopperId, dayIndex, counter.FailureReason, diag, options);
            }

            CarriedGoodsBatch batch = counter.Batch;
            int sold = counter.UnitsSold;


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
            string receiptProblem = ReceiveSettledGoods(
                result, need, batch, sold, chosen.HouseholdUnitsPerStoreUnit, chosen.UseDelivery, chosen.Supplier.SupplierName,
                shopperId, dayIndex, diag, options);
            if (receiptProblem != null)
            {
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                return Fail(result, need, shopperId, dayIndex, receiptProblem, diag, options);
            }

            SetScheduleState(shopper, PopulationScheduleState.AtHome);
            scheduleTracker.Release(reservationId);
            string boughtText = chosen.HouseholdUnitsPerStoreUnit > 1
                ? $"{sold} package(s) ({sold * chosen.HouseholdUnitsPerStoreUnit}u {need.ItemId})"
                : $"{sold}u {need.ItemId}";
            diag.Add($"Day {dayIndex}: shopping trip complete — P{shopperId} bought {boughtText} from '{chosen.Supplier.SupplierName}'.");
            tripHistory.Add(result);
            return result;
        }

        /// <summary>
        /// Priority 9 Slice F3: log-only recording of the retail purchase.
        /// The category-pool sale carries no retail lot id, so the source
        /// lot key is Empty and the gap is named in the log's diagnostics.
        /// The destination is the carried batch, keyed in the household
        /// inventory space by its batch id. The cause key is the purchase
        /// receipt id (the batch id when the site holds no receipt id). A
        /// logging failure never fails the settlement.
        /// </summary>
        private void RecordRetailPurchase(
            ShoppingTripResult result, ShoppingCandidate chosen,
            HouseholdPurchasingNeed need, CarriedGoodsBatch batch,
            int sold, int shopperId, int dayIndex)
        {
            if (CustodyLog == null) return;
            string receiptId = result.ReceiptId;
            if (string.IsNullOrWhiteSpace(receiptId))
            {
                receiptId = batch.BatchId;
                CustodyLog.Note("custody: retail-purchase for batch " + batch.BatchId +
                    " has no receipt id at this site; the batch id is the cause key instead.");
            }
            var record = new CustodyTransferRecord(
                dayIndex, need.ItemId, sold,
                LotKey.Empty,
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, batch.BatchId),
                CustodyTransferLog.BusinessCustodian(chosen.Supplier.SupplierBusinessId),
                CustodyTransferLog.PersonCustodian(shopperId),
                receiptId, CustodyTransferLog.CauseRetailPurchase);
            if (CustodyLog.Record(record, out _))
            {
                CustodyLog.Note("custody: retail-purchase " + record.RecordId +
                    " has no source lot key — the category-pool sale carries no retail lot id at this site.");
            }
        }

        /// <summary>
        /// Priority 9 Slice F3: log-only recording of a household receipt.
        /// The source is the carried batch; the destination household lot
        /// id is not returned by HouseholdInventory.AddLot, so the
        /// destination key is Empty and the gap is named in the log's
        /// diagnostics. The cause key is the carried batch id, so the
        /// receipt links to its purchase record. <paramref name="fromCustodian"/>
        /// is the pre-receipt holder: the shopper for carried goods, the
        /// arranged-delivery wagon for a delivery — ExecuteDelivery moves
        /// the batch "by wagon" with no asset id, so the delivery
        /// custodian names the delivery itself rather than inventing one.
        /// A logging failure never fails the receipt. Ownership is NOT
        /// recorded here: the lot is booked to the household by the
        /// receipt path itself (OwnerPersonId -1), never to the shopper.
        /// </summary>
        private void RecordHouseholdReceipt(
            CarriedGoodsBatch batch, HouseholdPurchasingNeed need,
            int unitsReceived, int dayIndex, string fromCustodian)
        {
            if (CustodyLog == null) return;
            var record = new CustodyTransferRecord(
                dayIndex, need.ItemId, unitsReceived,
                new LotKey(CustodyTransferLog.HouseholdInventoryAuthority, batch.BatchId),
                LotKey.Empty,
                fromCustodian,
                CustodyTransferLog.HouseholdCustodian(need.HouseholdId),
                batch.BatchId, CustodyTransferLog.CauseHouseholdReceipt);
            if (CustodyLog.Record(record, out _))
            {
                CustodyLog.Note("custody: household-receipt " + record.RecordId +
                    " has no destination lot key — HouseholdInventory.AddLot returns no lot id at this site.");
            }
        }

        /// <summary>
        /// Priority V1: after a receipt deposits a lot, resolve the
        /// deposited lot's identity through the F4 resolver and record
        /// it on the trip result — the lot id and its canonical
        /// provenance key, never a bare id passed onward. The lot is
        /// found by position: AddLot appends exactly one lot on success,
        /// so the lot at the pre-AddLot count is the deposited one. A
        /// resolution gap is recorded in diagnostics, never invented,
        /// and never fails the receipt (the goods are already stored).
        /// </summary>
        private void RecordDepositedLotIdentity(
            HouseholdInventory inventory, int lotIndexBefore,
            ShoppingTripResult result, List<string> diag)
        {
            if (inventory == null || result == null || diag == null) return;
            if (lotIndexBefore < 0 || lotIndexBefore >= inventory.Lots.Count)
            {
                diag.Add($"[V1] deposit identity gap: no lot at index {lotIndexBefore} after receipt for H{result.HouseholdId} — the deposited lot cannot be resolved.");
                return;
            }

            HouseholdItemLot deposited = inventory.Lots[lotIndexBefore];
            if (deposited == null)
            {
                diag.Add("[V1] deposit identity gap: the deposited lot slot is empty after receipt.");
                return;
            }

            LotIdentity identity = LotIdentityResolver.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, deposited.LotId.ToString()));
            if (identity == null)
            {
                diag.Add($"[V1] deposit identity gap: lot {deposited.LotId} in H{result.HouseholdId} could not be resolved — see the resolver diagnostics. No key is invented.");
                return;
            }

            result.ReceivedLotId = deposited.LotId;
            result.ReceivedLotKey = identity.ProvenanceKey;
            diag.Add($"[V1] deposit identity: lot {deposited.LotId} ({identity.ItemId} x{identity.Units}) " +
                $"deposited in H{result.HouseholdId} as {identity.ProvenanceKey}.");
        }

        /// <summary>
        /// Priority V1: after a receipt removes the carried batch from
        /// custody, verify through the loop's production person
        /// inventory read model that the shopper no longer carries that
        /// batch. The read model is the custody answer — the loop does
        /// not re-derive custody inline from its own batch list here.
        /// A disagreement is a loud diagnostic, never a silent pass,
        /// and never fails the receipt (the goods are already stored).
        /// </summary>
        private void VerifyDepositViaPersonInventory(int shopperId, CarriedGoodsBatch batch, List<string> diag)
        {
            if (batch == null || diag == null) return;
            foreach (PersonInventoryReadModel.CarriedItem carried in PersonInventory.CarriedItems(shopperId))
            {
                if (carried != null && string.Equals(carried.BatchId, batch.BatchId, StringComparison.Ordinal))
                {
                    diag.Add($"[V1] INCONSISTENCY: P{shopperId} still carries batch {batch.BatchId} " +
                        "in the person inventory read model after receipt — custody and storage disagree.");
                    return;
                }
            }

            diag.Add($"[V1] deposit verified via PersonInventoryReadModel: P{shopperId} " +
                $"no longer carries batch {batch.BatchId} after receipt.");
        }

        /// <summary>
        /// Pass V P2-1: step 16 as a reusable unit — physical receipt ONLY.
        /// Household inventory grows here, and only here, for carried goods.
        /// Updates the need (fulfilled when nothing remains) and the per-need
        /// result. Returns null on success, else a loud inconsistency reason.
        /// Shared by the single-need path and the batched multi-need trip.
        /// W5: <paramref name="unitsSold"/> is in STORE units (sacks for a
        /// package item); the household is credited
        /// <paramref name="unitsSold"/> ×
        /// <paramref name="householdUnitsPerStoreUnit"/> household units.
        /// </summary>
        private string ReceiveSettledGoods(
            ShoppingTripResult result,
            HouseholdPurchasingNeed need,
            CarriedGoodsBatch batch,
            int unitsSold,
            int householdUnitsPerStoreUnit,
            bool useDelivery,
            string supplierName,
            int shopperId,
            int dayIndex,
            List<string> diag,
            ShoppingLoopOptions options)
        {
            int unitsReceived = unitsSold * Math.Max(1, householdUnitsPerStoreUnit);
            HouseholdInventory inventory = inventories.GetOrCreate(need.HouseholdId);
            int lotIndexBefore = inventory.Lots.Count;
            string lotRejection = inventory.AddLot(
                need.ItemId, unitsReceived, dayIndex,
                batch.Provenance + (useDelivery ? " → delivered" : " → carried home"),
                condition: ConditionOf(batch));
            if (lotRejection != null)
            {
                return $"INCONSISTENCY: goods are in custody ({batch.BatchId}) but the household lot was rejected: {lotRejection}";
            }

            // Plan 08 Slice 1 (Q03, AUD-335): the loop receipt credits
            // the mapped reserve by the units received (household units),
            // clamped to target — the reverse of the meal-consumption
            // mirror — so the reserve deficit closes and the GSRM path
            // does not re-buy the same food. One call site covers the
            // single-need and batched paths alike, carried and delivered
            // alike; rejected lots never reach this line.
            HouseholdInventoryReserveBridge.MirrorLotReceiptToReserve(
                population.GetHousehold(need.HouseholdId), need.ItemId, unitsReceived);

            batch.Custody = GoodsCustodyState.ReceivedAtHousehold;
            custodyBatches.Remove(batch);
            // Priority V1: the deposited lot's identity, resolved
            // through the F4 resolver, and the deposit verified through
            // the loop's production person inventory read model.
            RecordDepositedLotIdentity(inventory, lotIndexBefore, result, diag);
            VerifyDepositViaPersonInventory(shopperId, batch, diag);
            RecordHouseholdReceipt(batch, need, unitsReceived, dayIndex,
                useDelivery
                    ? CustodyTransferLog.WagonCustodian("arranged-delivery")
                    : CustodyTransferLog.PersonCustodian(shopperId));
            need.UnitsNeeded = Math.Max(0, need.UnitsNeeded - unitsReceived);
            need.LastEvaluatedDayIndex = Math.Max(0, dayIndex);
            if (need.UnitsNeeded <= 0)
            {
                need.Status = PurchasingNeedStatus.Fulfilled;
            }

            result.Success = true;
            result.UnitsAcquired = unitsReceived;
            result.UnitsStillNeeded = need.UnitsNeeded;
            AddEvent(result, dayIndex, ShoppingEventKind.Receipt, shopperId, need,
                $"H{need.HouseholdId} received {unitsReceived}u {need.ItemId} into real lots (physical receipt).",
                batch.Provenance + (need.Status == PurchasingNeedStatus.Fulfilled ? " Need fulfilled." : $" {need.UnitsNeeded}u still needed."));
            PersonState buyer = population.GetPerson(shopperId);
            string buyerName = buyer != null ? buyer.DisplayName : "P" + shopperId;
            diag.Add($"[FoodEconomy] purchase received: buyer='{buyerName}', household=H{need.HouseholdId}, " +
                $"seller='{supplierName}', item='{need.ItemId}', units received={unitsSold}, " +
                $"household stock after={inventory.GetAvailableUnits(need.ItemId)}, " +
                $"receipt='{batch.ReceiptId}', custody=household inventory.");
            diag.Add($"Day {dayIndex}: P{shopperId} delivered {unitsSold}u {need.ItemId} to H{need.HouseholdId} " +
                $"from '{supplierName}' (batch {batch.BatchId}).");
            return null;
        }

        /// <summary>
        /// Pass V P2-1: executes a BATCH of needs as ONE trip. The needs are
        /// grouped by winnable supplier (largest single-supplier subset goes
        /// this trip; the rest stay open for a later plan — never duplicated).
        /// Exactly one real member shops; one person-time window is reserved;
        /// each need settles at the counter through the shared settlement unit
        /// (no duplicate purchases: each need's units decrement once, and the
        /// need registry's one-open-need-per-item invariant is preserved).
        /// </summary>
        public BatchShoppingResult ExecuteBatch(
            IReadOnlyList<HouseholdPurchasingNeed> needs,
            int dayIndex,
            ShoppingLoopOptions options,
            string planId = null)
        {
            var batch = new BatchShoppingResult { PlanId = planId ?? string.Empty, DayIndex = Math.Max(0, dayIndex) };
            options ??= new ShoppingLoopOptions();
            var diag = options.Diagnostics ?? new List<string>();

            if (needs == null || needs.Count == 0)
            {
                batch.FailureReason = "Batch has no needs.";
                return batch;
            }

            int householdId = needs[0].HouseholdId;
            batch.HouseholdId = householdId;
            HouseholdState household = population.GetHousehold(householdId);
            if (household == null)
            {
                batch.FailureReason = $"H{householdId} is not a known household.";
                return batch;
            }

            // ---- Validate every need; spending policy applies per need. ----
            var valid = new List<HouseholdPurchasingNeed>();
            foreach (HouseholdPurchasingNeed need in needs)
            {
                if (need == null || need.HouseholdId != householdId) continue;
                var result = NewBatchNeedResult(batch, need);
                string needProblem = ValidateNeed(need);
                if (needProblem != null)
                {
                    Fail(result, need, -1, dayIndex, needProblem, diag, options);
                    continue;
                }

                HouseholdSpendingPolicy spendingPolicy = spendingPolicyProvider?.Invoke(householdId);
                if (spendingPolicy != null && !need.IsNecessity)
                {
                    int permitted = (int)Math.Floor(need.UnitsNeeded * Math.Max(0, spendingPolicy.DiscretionaryCapPercent) / 100.0);
                    if (permitted <= 0)
                    {
                        Fail(result, need, -1, dayIndex, "Discretionary purchase deferred by household spending policy.", diag, options);
                        continue;
                    }

                    need.UnitsNeeded = Math.Min(need.UnitsNeeded, permitted);
                }

                result.UnitsStillNeeded = need.UnitsNeeded;
                valid.Add(need);
            }

            if (valid.Count == 0)
            {
                batch.FailureReason = "No valid open needs in the batch.";
                return batch;
            }

            List<int> memberIds = membership.GetActiveMembers(householdId);
            if (memberIds == null || memberIds.Count == 0)
            {
                batch.FailureReason = $"H{householdId} has no active members — nobody can shop.";
                return batch;
            }

            string homeLocation = ResolvePersonLocation(memberIds[0], options);

            // ---- Light viability pre-check per (need, supplier): groups needs
            // by winnable supplier WITHOUT recording choice events yet. ----
            var viableBySupplier = new Dictionary<string, List<HouseholdPurchasingNeed>>(StringComparer.OrdinalIgnoreCase);
            var viableSuppliers = new Dictionary<string, IGoodsSupplier>(StringComparer.OrdinalIgnoreCase);
            var unservedNeedReasons = new List<string>();
            foreach (HouseholdPurchasingNeed need in valid)
            {
                ShoppingTripResult result = FindBatchNeedResult(batch, need);
                List<IGoodsSupplier> knownOffering = FindKnownItemSuppliers(memberIds, need.ItemId, dayIndex, result, diag, options);
                var rejected = new List<string>();
                if (knownOffering.Count == 0)
                {
                    // Plan 02 Slice 5: the per-need reason must name the
                    // distinction — no seller offers the item at all
                    // (unrecognized), as opposed to a recognized item that
                    // is merely out of stock or closed.
                    rejected.Add("unrecognized item (no seller offers it)");
                }

                bool anyViable = false;
                foreach (IGoodsSupplier supplier in knownOffering)
                {
                    if (supplier == null) continue;
                    var itemSupplier = supplier as IItemResolvingSupplier;
                    if (itemSupplier == null || !SupplierRecognizesItem(supplier, need.ItemId))
                    {
                        rejected.Add($"{supplier.SupplierName}: item identity unavailable");
                        continue;
                    }

                    int stock = itemSupplier.ItemStockUnits(need.ItemId);
                    if (stock <= 0)
                    {
                        // Plan 02 Slice 5: recognized but out of stock is
                        // real demand, not an unrecognized item — name it.
                        rejected.Add($"{supplier.SupplierName}: recognized but out of stock");
                        continue;
                    }

                    int unitPrice = itemSupplier.ItemPricePerUnitCents(need.ItemId);
                    if (unitPrice <= 0)
                    {
                        rejected.Add($"{supplier.SupplierName}: no legitimate price");
                        continue;
                    }

                    JourneyRoute route = journeys.FindRoute(homeLocation, supplier.LocationId, TravelMode.Foot);
                    if (!route.Found)
                    {
                        rejected.Add($"{supplier.SupplierName}: blocked journey ({route.Diagnostic})");
                        continue;
                    }

                    int arrivalMinute = options.DepartureMinuteOfDay + Math.Max(1, route.TotalMinutes);
                    string sellerTradeBlock = itemSupplier.CheckTradingCapability(dayIndex, arrivalMinute);
                    if (sellerTradeBlock != null)
                    {
                        // Plan 02 Slice 5: closed at arrival, with the
                        // capability's own reason kept in parentheses.
                        rejected.Add($"{supplier.SupplierName}: closed at arrival ({sellerTradeBlock})");
                        continue;
                    }

                    anyViable = true;
                    if (!viableBySupplier.TryGetValue(supplier.SupplierBusinessId, out List<HouseholdPurchasingNeed> group))
                    {
                        group = new List<HouseholdPurchasingNeed>();
                        viableBySupplier[supplier.SupplierBusinessId] = group;
                        viableSuppliers[supplier.SupplierBusinessId] = supplier;
                    }

                    group.Add(need);
                }

                if (!anyViable)
                {
                    int shown = Math.Min(3, rejected.Count);
                    string explanation = rejected.Count == 0
                        ? "no supplier passed stock, price, route, and trading checks"
                        : string.Join("; ", rejected.GetRange(0, shown)) +
                          (rejected.Count > shown ? $"; and {rejected.Count - shown} more" : string.Empty);
                    unservedNeedReasons.Add($"{need.ItemId}: {explanation}");
                    Fail(result, need, -1, dayIndex,
                        $"No viable seller for '{need.ItemId}': {explanation}. Need stays open.", diag, options);
                }
            }

            // Winner: the supplier serving the most needs (fewer trips);
            // tie-break on fewest total units to keep the trip small.
            string winnerId = null;
            List<HouseholdPurchasingNeed> batchNeeds = null;
            foreach (KeyValuePair<string, List<HouseholdPurchasingNeed>> entry in viableBySupplier)
            {
                if (batchNeeds == null || entry.Value.Count > batchNeeds.Count)
                {
                    winnerId = entry.Key;
                    batchNeeds = entry.Value;
                }
            }

            if (batchNeeds == null || batchNeeds.Count == 0)
            {
                int shown = Math.Min(3, unservedNeedReasons.Count);
                string detail = shown > 0
                    ? " " + string.Join(" | ", unservedNeedReasons.GetRange(0, shown)) +
                      (unservedNeedReasons.Count > shown ? $" | and {unservedNeedReasons.Count - shown} more need(s)" : string.Empty)
                    : string.Empty;
                batch.FailureReason = "No supplier can serve any need in the batch — needs stay open." + detail;
                return batch;
            }

            IGoodsSupplier winner = viableSuppliers[winnerId];
            foreach (HouseholdPurchasingNeed need in valid)
            {
                if (!batchNeeds.Contains(need))
                {
                    batch.UnbatchedNeeds.Add(need);
                }
            }

            // Most urgent first: the shared ledger budget is claimed in order.
            batchNeeds.Sort((a, b) => b.Urgency01.CompareTo(a.Urgency01));

            // ---- One shopper for the whole trip. ----
            // Plan 02 Slice 1: search departures forward from the requested
            // one (480 = store opening) instead of trying that single slot.
            // The daily planner sprinkles a Resting block over 480-510 on
            // every member, so the old single-departure choice failed every
            // member every day. The search finds the first departure where a
            // member is free or blocked only by Resting (yieldable through
            // the atomic swap in ReserveShoppingWindow); it never preempts
            // Sleeping, Work, Building, Cooking, JobSeeking, Managing or
            // Eating, and it checks budget and store hours per departure.
            // The found departure flows through the single
            // options.DepartureMinuteOfDay so arrival math, leg planning,
            // the reservation and the batch estimate all use the same minute.
            int estimateMinutes = EstimateTripMinutes(homeLocation,
                new List<IGoodsSupplier> { winner }, options)
                + Math.Max(0, batchNeeds.Count - 1) * Math.Max(1, options.InStoreMinutes);
            batch.EstimatedMinutes = estimateMinutes;
            int requestedDeparture = options.DepartureMinuteOfDay;
            string shopperReason = null;
            int shopperId;
            int foundDeparture = requestedDeparture;
            if (options.ShopperPersonIdOverride >= 0)
            {
                shopperId = options.ShopperPersonIdOverride;
            }
            else
            {
                JourneyRoute winnerRoute = journeys.FindRoute(homeLocation, winner.LocationId, TravelMode.Foot);
                int oneWayMinutes = winnerRoute.Found ? Math.Max(1, winnerRoute.TotalMinutes) : 1;
                var winnerItemSupplier = winner as IItemResolvingSupplier;
                shopperId = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                    population, memberIds, dayIndex, requestedDeparture, estimateMinutes,
                    oneWayMinutes, options.InStoreMinutes,
                    arrival => winnerItemSupplier == null
                        ? null
                        : winnerItemSupplier.CheckTradingCapability(dayIndex, arrival),
                    scheduleTracker, budgets, personEntityId, out foundDeparture, out shopperReason,
                    offMapJourneys);
            }

            if (shopperId < 0)
            {
                batch.FailureReason = $"Unavailable shopper: {shopperReason ?? "nobody can shop today."} Needs stay open.";
                diag.Add($"Day {dayIndex}: batch {batch.PlanId} — {batch.FailureReason}");
                // Plan 02 Slice 5: the viable needs must carry the
                // no-shopper reason on their own per-need results, not
                // only on the batch — the daily summary reports the first
                // failure reason per open need. Needs already failed by
                // the pre-check keep their own specific reasons (they are
                // not in batchNeeds), so only the viable group is failed
                // here.
                FailBatchNeeds(batch, batchNeeds, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            PersonState shopper = population.GetPerson(shopperId);
            if (shopper == null)
            {
                batch.FailureReason = $"P{shopperId} is not a known person.";
                return batch;
            }

            if (options.ShopperPersonIdOverride >= 0)
            {
                string overrideProblem = ValidateShopper(shopper);
                if (overrideProblem != null)
                {
                    batch.FailureReason = $"Shopper override rejected: {overrideProblem}";
                    return batch;
                }
            }

            options.DepartureMinuteOfDay = foundDeparture;
            if (foundDeparture != requestedDeparture)
            {
                diag.Add($"Day {dayIndex}: batch {batch.PlanId} — P{shopperId} departs at {foundDeparture} " +
                    $"(window search from {requestedDeparture}); trip window ~{estimateMinutes} min.");
            }

            batch.ShopperPersonId = shopperId;
            string shopperHome = ResolvePersonLocation(shopperId, options);

            // Plan 05 Slice 4 — departure-location consistency: the
            // pre-check, estimate, and window search above all used
            // member-0's homeLocation, but the legs below use the shopper's
            // resolved location. When the chosen shopper resolves elsewhere
            // (e.g. AtWork at a remote workplace), re-estimate from the
            // shopper's location and re-run the search with the corrected
            // trip: the first search's window was priced from the wrong
            // departure point. If the corrected trip cannot fit any window,
            // fail honestly with the corrected reason (same failure path as
            // the first search); if it fits, the corrected departure +
            // shopper stand, and the legs use the same location the
            // estimate used. No teleport is introduced: legs always depart
            // from the shopper's resolved location. (Residual, recorded not
            // repaired: the :568 pre-check route still tests supplier
            // viability from member-0's home — it can only admit a supplier
            // the corrected estimate then refuses, never plan a leg from
            // home.)
            if (!string.Equals(shopperHome, homeLocation, StringComparison.OrdinalIgnoreCase))
            {
                int correctedEstimate = EstimateTripMinutes(shopperHome,
                    new List<IGoodsSupplier> { winner }, options)
                    + Math.Max(0, batchNeeds.Count - 1) * Math.Max(1, options.InStoreMinutes);
                JourneyRoute correctedRoute = journeys.FindRoute(shopperHome, winner.LocationId, TravelMode.Foot);
                int correctedOneWay = correctedRoute.Found ? Math.Max(1, correctedRoute.TotalMinutes) : 1;
                if (correctedEstimate != estimateMinutes)
                {
                    string correctedReason = null;
                    int correctedDeparture;
                    var correctedWinnerSupplier = winner as IItemResolvingSupplier;
                    int correctedShopper = ShopperAssignmentPolicy.ChooseShopperWithWindowSearch(
                        population, memberIds, dayIndex, requestedDeparture, correctedEstimate,
                        correctedOneWay, options.InStoreMinutes,
                        arrival => correctedWinnerSupplier == null
                            ? null
                            : correctedWinnerSupplier.CheckTradingCapability(dayIndex, arrival),
                        scheduleTracker, budgets, personEntityId, out correctedDeparture, out correctedReason,
                        offMapJourneys);
                    if (correctedShopper < 0)
                    {
                        batch.FailureReason = $"Unavailable shopper: {correctedReason ?? "nobody can shop today."} Needs stay open.";
                        diag.Add($"Day {dayIndex}: batch {batch.PlanId} — {batch.FailureReason}");
                        FailBatchNeeds(batch, batchNeeds, correctedShopper, dayIndex, batch.FailureReason, diag, options);
                        return batch;
                    }

                    if (correctedDeparture != foundDeparture || correctedShopper != shopperId)
                    {
                        diag.Add($"Day {dayIndex}: batch {batch.PlanId} — P{correctedShopper} departs at {correctedDeparture} " +
                            $"(location-corrected search from {foundDeparture}; trip ~{correctedEstimate} min from the shopper's location).");
                    }

                    foundDeparture = correctedDeparture;
                    shopperId = correctedShopper;
                    options.DepartureMinuteOfDay = correctedDeparture;
                    batch.ShopperPersonId = shopperId;
                    shopperHome = ResolvePersonLocation(shopperId, options);
                    estimateMinutes = correctedEstimate;
                    batch.EstimatedMinutes = correctedEstimate;
                }
            }

            // ---- Full candidate evaluation per need, sharing one ledger
            // budget in urgency order (no double-spent cash). ----
            var candidates = new Dictionary<HouseholdPurchasingNeed, ShoppingCandidate>();
            int remainingBudget = ledgers.GetOrCreate(householdId).GetBalanceCents();
            foreach (HouseholdPurchasingNeed need in batchNeeds)
            {
                ShoppingTripResult result = FindBatchNeedResult(batch, need);
                result.ShopperPersonId = shopperId;
                ShoppingCandidate candidate = EvaluateCandidates(
                    result, need, shopperId, shopperHome,
                    new List<IGoodsSupplier> { winner }, dayIndex, options, diag, remainingBudget);
                if (candidate == null)
                {
                    batch.UnbatchedNeeds.Add(need);
                    continue;
                }

                candidates[need] = candidate;
                remainingBudget = Math.Max(0, remainingBudget - candidate.TotalCostCents);
            }

            if (candidates.Count == 0)
            {
                batch.FailureReason = "No need in the batch is affordable or servable — needs stay open.";
                return batch;
            }

            // ---- One reservation for the whole trip (person-time is exclusive). ----
            var windowResult = new ShoppingTripResult { HouseholdId = householdId };
            string reservationId = ReserveShoppingWindow(shopperId, dayIndex, options, estimateMinutes, windowResult, diag, allowRestPreemption: true);
            if (reservationId == null)
            {
                batch.FailureReason = windowResult.FailureReason ?? "Trip window reservation refused.";
                diag.Add($"Day {dayIndex}: batch {batch.PlanId} — {batch.FailureReason}");
                FailBatchNeeds(batch, candidates.Keys, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            // ---- One set of legs, to the winning supplier. ----
            EntityId shopperEid = personEntityId(shopperId);
            var trip = new PlannedTrip();
            HouseholdPurchasingNeed firstNeed = null;
            ShoppingCandidate firstCandidate = null;
            foreach (KeyValuePair<HouseholdPurchasingNeed, ShoppingCandidate> entry in candidates)
            {
                firstNeed = entry.Key;
                firstCandidate = entry.Value;
                break;
            }

            var legsResult = new ShoppingTripResult { HouseholdId = householdId, ShopperPersonId = shopperId };
            if (!PlanTripLegs(trip, legsResult, firstNeed, shopperId, shopperEid, shopperHome, firstCandidate, dayIndex, options, diag))
            {
                scheduleTracker.Release(reservationId);
                batch.ReservationId = string.Empty;
                batch.FailureReason = legsResult.FailureReason ?? "Trip leg planning failed.";
                FailBatchNeeds(batch, candidates.Keys, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            SetScheduleState(shopper, PopulationScheduleState.GoingShopping);
            if (!ExecuteTaskLeg(trip.Outbound, legsResult, firstNeed, shopperId, dayIndex, "outbound", diag, options))
            {
                scheduleTracker.Release(reservationId);
                batch.ReservationId = string.Empty;
                batch.FailureReason = legsResult.FailureReason ?? "Outbound leg failed.";
                FailBatchNeeds(batch, candidates.Keys, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            int storeArrivalMinute = options.DepartureMinuteOfDay + trip.OutboundMinutes;
            SetPersonLocation(shopperId, winner.LocationId, options);
            SetScheduleState(shopper, PopulationScheduleState.AtStore);

            string tradingProblem = firstCandidate.ItemSupplier.CheckTradingCapability(dayIndex, storeArrivalMinute);
            if (tradingProblem != null)
            {
                ExecuteTaskLeg(trip.Return, legsResult, firstNeed, shopperId, dayIndex, "return-empty", diag, options);
                SetPersonLocation(shopperId, shopperHome, options);
                SetScheduleState(shopper, PopulationScheduleState.AtHome);
                scheduleTracker.Release(reservationId);
                batch.ReservationId = string.Empty;
                batch.FailureReason = $"'{winner.SupplierName}' closed at arrival: {tradingProblem}. Shopper returned empty-handed.";
                diag.Add($"Day {dayIndex}: batch {batch.PlanId} — {batch.FailureReason}");
                FailBatchNeeds(batch, candidates.Keys, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            if (!ExecuteTaskLeg(trip.InStore, legsResult, firstNeed, shopperId, dayIndex, "in-store", diag, options))
            {
                scheduleTracker.Release(reservationId);
                batch.ReservationId = string.Empty;
                batch.FailureReason = legsResult.FailureReason ?? "In-store leg failed.";
                FailBatchNeeds(batch, candidates.Keys, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            // ---- Settle each need at the counter (shared settlement unit). ----
            var settled = new List<(HouseholdPurchasingNeed need, ShoppingCandidate candidate, CarriedGoodsBatch batch, int units)>();
            foreach (KeyValuePair<HouseholdPurchasingNeed, ShoppingCandidate> entry in candidates)
            {
                HouseholdPurchasingNeed need = entry.Key;
                ShoppingCandidate candidate = entry.Value;
                ShoppingTripResult result = FindBatchNeedResult(batch, need);
                SettlementOutcome counter = SettleAtCounter(result, trip, need, candidate, shopperId, dayIndex, diag, options);
                if (!counter.Success)
                {
                    diag.Add($"Day {dayIndex}: batch {batch.PlanId} — '{need.ItemId}' unsettled: {counter.FailureReason} Need stays open.");
                    batch.UnbatchedNeeds.Add(need);
                    continue;
                }

                if (candidate.UseDelivery)
                {
                    ExecuteDelivery(counter.Batch, candidate, result, need, shopperId, dayIndex, diag, options);
                }

                settled.Add((need, candidate, counter.Batch, counter.UnitsSold));
            }

            if (!ExecuteTaskLeg(trip.Return, legsResult, firstNeed, shopperId, dayIndex, "return", diag, options))
            {
                scheduleTracker.Release(reservationId);
                batch.ReservationId = string.Empty;
                batch.FailureReason = legsResult.FailureReason ?? "Return leg failed — goods remain in custody, needs stay open.";
                var custodyNeeds = new List<HouseholdPurchasingNeed>();
                foreach (var entry in settled) custodyNeeds.Add(entry.need);
                FailBatchNeeds(batch, custodyNeeds, shopperId, dayIndex, batch.FailureReason, diag, options);
                return batch;
            }

            SetPersonLocation(shopperId, shopperHome, options);
            SetScheduleState(shopper, PopulationScheduleState.ReturningHome);

            // ---- Physical receipt per need — household inventory grows here. ----
            foreach (var entry in settled)
            {
                ShoppingTripResult result = FindBatchNeedResult(batch, entry.need);
                string receiptProblem = ReceiveSettledGoods(
                    result, entry.need, entry.batch, entry.units, entry.candidate.HouseholdUnitsPerStoreUnit, entry.candidate.UseDelivery,
                    winner.SupplierName, shopperId, dayIndex, diag, options);
                if (receiptProblem != null)
                {
                    batch.FailureReason = receiptProblem;
                    diag.Add($"Day {dayIndex}: batch {batch.PlanId} — {receiptProblem}");
                    continue;
                }

                batch.UnitsAcquiredTotal += entry.units * Math.Max(1, entry.candidate.HouseholdUnitsPerStoreUnit);
            }

            SetScheduleState(shopper, PopulationScheduleState.AtHome);
            scheduleTracker.Release(reservationId);
            batch.ReservationId = string.Empty;
            batch.TripExecuted = true;
            batch.ServedNeedCount = settled.Count;
            diag.Add($"Day {dayIndex}: batch {batch.PlanId} complete — P{shopperId} served {settled.Count} need(s), " +
                $"{batch.UnitsAcquiredTotal}u acquired, {batch.UnbatchedNeeds.Count} need(s) unbatched.");
            return batch;
        }

        /// <summary>
        /// Pass V P2-1: records a trip-level failure on every batched need's
        /// own result so the §26 audit trail stays per-need. The needs stay open.
        /// </summary>
        private void FailBatchNeeds(
            BatchShoppingResult batch,
            IEnumerable<HouseholdPurchasingNeed> needs,
            int shopperId,
            int dayIndex,
            string reason,
            List<string> diag,
            ShoppingLoopOptions options)
        {
            if (needs == null) return;
            foreach (HouseholdPurchasingNeed need in needs)
            {
                if (need == null) continue;
                ShoppingTripResult result = FindBatchNeedResult(batch, need);
                if (!result.Success)
                {
                    Fail(result, need, shopperId, dayIndex, reason, diag, options);
                }
            }
        }

        private ShoppingTripResult NewBatchNeedResult(BatchShoppingResult batch, HouseholdPurchasingNeed need)
        {
            var result = new ShoppingTripResult
            {
                NeedSequence = need.NeedSequence,
                HouseholdId = need.HouseholdId,
                ItemId = need.ItemId,
                UnitsStillNeeded = need.UnitsNeeded,
            };
            batch.NeedResults.Add(result);
            tripHistory.Add(result);
            return result;
        }

        private ShoppingTripResult FindBatchNeedResult(BatchShoppingResult batch, HouseholdPurchasingNeed need)
        {
            foreach (ShoppingTripResult result in batch.NeedResults)
            {
                if (result != null && result.NeedSequence == need.NeedSequence)
                {
                    return result;
                }
            }

            return NewBatchNeedResult(batch, need);
        }

        /// <summary>
        /// Pass V P2-1: the outcome of one batched multi-need trip. Per-need
        /// results keep the §26 audit trail; unbatched needs stay open without
        /// ever being double-planned.
        /// </summary>
        public sealed class BatchShoppingResult
        {
            public string PlanId = string.Empty;
            public int HouseholdId = -1;
            public int DayIndex;
            public int ShopperPersonId = -1;
            public string ReservationId = string.Empty;
            public int EstimatedMinutes;
            public List<ShoppingTripResult> NeedResults = new List<ShoppingTripResult>();
            public List<HouseholdPurchasingNeed> UnbatchedNeeds = new List<HouseholdPurchasingNeed>();
            public bool TripExecuted;
            public int ServedNeedCount;
            public int UnitsAcquiredTotal;
            public string FailureReason = string.Empty;

            public bool Success => TripExecuted && string.IsNullOrEmpty(FailureReason) && ServedNeedCount > 0;
        }

        /// <summary>
        /// Pass IV: outcome of the shared counter-settlement unit (steps 9-14).
        /// </summary>
        internal sealed class SettlementOutcome
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public CarriedGoodsBatch Batch;
            public int UnitsSold;

            public static SettlementOutcome Succeeded(CarriedGoodsBatch batch, int unitsSold)
            {
                return new SettlementOutcome { Success = true, Batch = batch, UnitsSold = unitsSold };
            }

            public static SettlementOutcome Failed(string reason)
            {
                return new SettlementOutcome { Success = false, FailureReason = reason ?? string.Empty };
            }
        }

        /// <summary>
        /// Pass IV: steps 9-14 of the shopping chain as one reusable unit —
        /// counter stock check, units/cost, settlement (cash outflow or valid
        /// trade-credit obligation — never informal debt), the ACTUAL retail
        /// sale with partial-fill true-up, and legitimate custody batch
        /// creation. The caller owns the return-empty leg and reservation
        /// release on failure. Used by the instant ExecuteNeed path and by the
        /// real-time ShoppingTripDriver — one conservation logic, two timings.
        /// </summary>
        internal SettlementOutcome SettleAtCounter(
            ShoppingTripResult result,
            PlannedTrip trip,
            HouseholdPurchasingNeed need,
            ShoppingCandidate chosen,
            int shopperId,
            int dayIndex,
            List<string> diag,
            ShoppingLoopOptions options)
        {
        // ---- Step 9: the seller holds real available inventory. ----
        int stockNow = chosen.ItemSupplier.ItemStockUnits(need.ItemId);
        if (stockNow <= 0)
        {
            return SettlementOutcome.Failed(
                $"Stockout at the counter: '{chosen.Supplier.SupplierName}' holds no '{need.ItemId}'. " +
                "A stockout is not a sale — need stays open.");
        }

        int units = Math.Min(chosen.UnitsPlanned, stockNow);
        int unitPrice = chosen.UnitPriceCents;
        int goodsCost = units * unitPrice;
        int freightCost = chosen.UseDelivery ? chosen.DeliveryFeeCents : 0;
        int totalCost = goodsCost + freightCost;
        if (units <= 0 || totalCost <= 0)
        {
            return SettlementOutcome.Failed(
                "Nothing affordable or available at the counter — no sale.");
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
                return SettlementOutcome.Failed(
                $"Insufficient funds: {rejection} Need stays open.");
            }

            settlement.Mode = ShoppingSettlementMode.CashFromLedger;
            result.BuyerOutflowCents = totalCost;
            result.StoreCashInCents = totalCost;
        }
        else
        {
            if (obligations == null || entityIds == null)
            {
                return SettlementOutcome.Failed(
                "Trade credit chosen but no obligation authority is wired — cannot create informal debt. Need stays open.");
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
                return SettlementOutcome.Failed(
                "Trade credit obligation could not be created — no sale without valid credit. Need stays open.");
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

            result.BuyerOutflowCents = 0;
            result.StoreCashInCents = 0;
            result.StoreReceivableCents = 0;
            return SettlementOutcome.Failed(
                $"INCONSISTENCY: settlement of {totalCost}c was posted but '{chosen.Supplier.SupplierName}' sold 0 units — refunded, manual reconciliation required.");
        }

        // Priority spoilage integration: the purchase never resets the
        // condition. When the supplier sold from a real lot that carries
        // a state, the taken part's state rides the batch into custody,
        // transferred to the shopper's custody at the purchase day (the
        // split at the sale is already in its history; this appends the
        // purchase transfer). A pool sale carries none, honestly.
        // Read immediately after the sale, before any later step can
        // overwrite the supplier's last-sale state.
        PerishableLotState.SaveDto soldCondition = default;
        bool hasSoldCondition = false;
        if (chosen.ItemSupplier is ILotConditionSupplier conditionSupplier
            && conditionSupplier.TryGetLastSaleCondition(out PerishableLotState.SaveDto lastSale))
        {
            soldCondition = PerishableLotState.FromSaveDto(lastSale)
                .TransferredTo("person:" + shopperId, dayIndex,
                    "purchased " + sold + "u " + need.ItemId + " from " + chosen.Supplier.SupplierName)
                .CaptureSaveDto();
            hasSoldCondition = true;
            diag.Add($"[spoilage] batch condition attached at settle: {need.ItemId} " +
                $"({sold}u, {PerishableLotState.FromSaveDto(soldCondition).PhysicalCondition}) from {chosen.Supplier.SupplierName}.");
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

            // Counter shortfall is a third partial-fill cause, after
            // affordability and stock at evaluation. It only fills
            // the field when evaluation left it empty — that is,
            // when the plan was full and the counter itself fell
            // short — so the first cause still wins.
            if (string.IsNullOrEmpty(result.PartialFillReason))
            {
                result.PartialFillReason = $"settlement-shortfall: seller sold {sold}u of {units}u planned at the counter.";
            }

            units = sold;
            goodsCost = sold * unitPrice;
        }

        result.ReceiptId = chosen.ItemSupplier.LastReceiptId;
        result.Counterparty = chosen.Supplier.SupplierName;
        result.TravelMinutesTotal = trip.OutboundMinutes + trip.ReturnMinutes;
        PersonState settledBuyer = population.GetPerson(shopperId);
        string settledBuyerName = settledBuyer != null ? settledBuyer.DisplayName : "P" + shopperId;
        HouseholdLedger householdLedger = ledgers.Get(need.HouseholdId);
        int stockAfterSale = chosen.ItemSupplier.ItemStockUnits(need.ItemId);
        string householdBalanceAfter = householdLedger != null
            ? householdLedger.GetBalanceCents().ToString()
            : "unknown";
        diag.Add($"[FoodEconomy] purchase settled: buyer='{settledBuyerName}', household=H{need.HouseholdId}, " +
            $"seller='{chosen.Supplier.SupplierName}', item='{need.ItemId}', units={sold}, " +
            $"unit price={unitPrice}c, total settled={totalCost}c, household debit={result.BuyerOutflowCents}c, " +
            $"seller cash credit={result.StoreCashInCents}c, seller receivable={result.StoreReceivableCents}c, " +
            $"household cash after={householdBalanceAfter}c, seller stock after={stockAfterSale}, " +
            $"settlement={settlement.Mode}, receipt='{result.ReceiptId}'.");
        AddEvent(result, dayIndex, ShoppingEventKind.RetailPurchase, shopperId, need,
            $"'{chosen.Supplier.SupplierName}' sold {sold}u {need.ItemId} ({result.ReceiptId}).",
            $"real store stock decreased; business posted {(settlement.Mode == ShoppingSettlementMode.CashFromLedger ? "cash" : "receivable")} {totalCost}c.");

        // ---- Priority V1: consult the F2 capacity evaluator BEFORE
        // InHand custody is assigned, with the live carry cap supplied.
        // The load is what the shopper would carry after this purchase:
        // every batch already in their hand (or wagon) plus the units
        // just sold. The evaluator answers on the units axis only
        // against the live cap; mass and volume stay Unknown (no
        // authored person mass or volume limit exists). The verdict is
        // recorded on LastSettleCapacityVerdict and in diagnostics. It
        // does NOT refuse a settled purchase: settlement already posted
        // above, and the loop's existing enforcement does not refuse an
        // over-cap no-delivery purchase — the cap gates the delivery
        // decision at candidate evaluation and the affordability
        // downsize clamp, so an over-cap verdict here is recorded
        // loudly instead of silently passed (see the V1 over-cap test).
        // Refusing here would change purchase quantities, which is a
        // shopping-rule change, not wiring.
        var capacityLines = new List<(string ItemId, int Units)>();
        foreach (CarriedGoodsBatch carried in custodyBatches)
        {
            if (carried != null && carried.HolderPersonId == shopperId
                && (carried.Custody == GoodsCustodyState.InHand || carried.Custody == GoodsCustodyState.Wagon))
            {
                capacityLines.Add((carried.ItemId, carried.Units));
            }
        }

        capacityLines.Add((need.ItemId, sold));
        var capacityQuery = new PhysicalCapacityQuery(
            PhysicalContainerKind.OnFootPerson, "person:" + shopperId,
            new PhysicalLoad(capacityLines), dayIndex);
        var capacityContext = PhysicalCapacityEvaluationContext.Default;
        int carryCap = Math.Max(1, (options ?? new ShoppingLoopOptions()).CarryCapacityUnits);
        capacityContext.OnFootCarryCapUnits = carryCap;
        PhysicalCapacityVerdict capacityVerdict = CapacityEvaluator.Evaluate(capacityQuery, capacityContext);
        LastSettleCapacityVerdict = capacityVerdict;
        diag.Add($"[V1] capacity consulted at settle: P{shopperId} carrying {capacityVerdict.Load.TotalUnits}u " +
            $"{need.ItemId} (live cap {carryCap}u) — {capacityVerdict.Outcome}: {capacityVerdict.Reason}");

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
            // Priority U-3: the batch carries its own package size and
            // seller, so a restored batch still knows both after save/load.
            HouseholdUnitsPerStoreUnit = Math.Max(1, chosen.HouseholdUnitsPerStoreUnit),
            SupplierName = chosen.Supplier != null ? chosen.Supplier.SupplierName ?? string.Empty : string.Empty,
            // Priority spoilage integration: the batch keeps the
            // lot's condition from the sale above — never reset.
            PerishableCondition = soldCondition,
            HasPerishableCondition = hasSoldCondition,
        };
        custodyBatches.Add(batch);
        RecordRetailPurchase(result, chosen, need, batch, sold, shopperId, dayIndex);
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
            return SettlementOutcome.Succeeded(batch, sold);
        }

        // ---------- Pass IV: real-time trip phases (ShoppingTripDriver) ----------

        /// <summary>
        /// Pass IV: plans a shopping trip through real time WITHOUT executing it.
        /// Runs steps 1–5 of the 17-step chain: need validation, legitimately-known
        /// sellers, shopper choice (a real free member), candidate evaluation, and
        /// leg planning — travel-out / in-store / travel-back tasks created and
        /// assigned against the shopper's work-time budget, plus one person-time
        /// window per leg (Travel, Shopping, Travel) so the trip occupies real
        /// time. The ShoppingTripDriver starts legs and settles through real
        /// time via due events. Never returns null: unplannable needs come back
        /// as Failed trips with a loud reason (the need stays open).
        /// </summary>
        public PlannedShoppingTrip PlanTrip(HouseholdPurchasingNeed need, int dayIndex, ShoppingLoopOptions options)
        {
            var trip = new PlannedShoppingTrip();
            options = options ?? new ShoppingLoopOptions();
            var diag = options.Diagnostics ?? new List<string>();
            trip.Options = options;
            trip.DayIndex = Math.Max(0, dayIndex);
            trip.TripId = $"trip-{trip.DayIndex}-{nextTripSequence++}";
            var result = new ShoppingTripResult();
            trip.Result = result;

            // ---- Step 1: the need exists. ----
            string needProblem = ValidateNeed(need);
            if (needProblem != null)
            {
                return FailPlan(trip, need, -1, needProblem, diag);
            }

            HouseholdSpendingPolicy spendingPolicy = spendingPolicyProvider?.Invoke(need.HouseholdId);
            if (spendingPolicy != null && !need.IsNecessity)
            {
                int permitted = (int)Math.Floor(need.UnitsNeeded * Math.Max(0, spendingPolicy.DiscretionaryCapPercent) / 100.0);
                if (permitted <= 0)
                    return FailPlan(trip, need, -1, "Discretionary purchase deferred by household spending policy.", diag);
                need.UnitsNeeded = Math.Min(need.UnitsNeeded, permitted);
            }

            result.NeedSequence = need.NeedSequence;
            result.HouseholdId = need.HouseholdId;
            result.ItemId = need.ItemId;
            result.UnitsStillNeeded = need.UnitsNeeded;
            trip.HouseholdId = need.HouseholdId;
            trip.Need = need;

            HouseholdState household = population.GetHousehold(need.HouseholdId);
            if (household == null)
            {
                return FailPlan(trip, need, -1, $"H{need.HouseholdId} is not a known household.", diag);
            }

            // ---- Step 3 (identification first): legitimately-known sellers. ----
            List<int> memberIds = membership.GetActiveMembers(need.HouseholdId);
            if (memberIds == null || memberIds.Count == 0)
            {
                return FailPlan(trip, need, -1, $"H{need.HouseholdId} has no active members — nobody can shop.", diag);
            }

            List<IGoodsSupplier> knownOffering = FindKnownItemSuppliers(memberIds, need.ItemId, dayIndex, result, diag, options);
            if (knownOffering.Count == 0)
            {
                return FailPlan(trip, need, -1,
                    DescribeNoKnownItemOffer(memberIds, need.ItemId), diag);
            }

            string homeLocation = ResolvePersonLocation(memberIds[0], options);

            // ---- Step 2: a real member becomes responsible. ----
            int estimateMinutes = EstimateTripMinutes(homeLocation, knownOffering, options);
            string shopperReason = null;
            int shopperId = options.ShopperPersonIdOverride >= 0
                ? options.ShopperPersonIdOverride
                : ShopperAssignmentPolicy.ChooseShopper(
                    population, memberIds, dayIndex, options.DepartureMinuteOfDay, estimateMinutes,
                    scheduleTracker, budgets, personEntityId, out shopperReason, offMapJourneys);
            if (shopperId < 0)
            {
                return FailPlan(trip, need, -1,
                    $"Unavailable shopper: {shopperReason ?? "nobody can shop today."} Need stays open.", diag);
            }

            PersonState shopper = population.GetPerson(shopperId);
            if (shopper == null)
            {
                return FailPlan(trip, need, shopperId, $"P{shopperId} is not a known person.", diag);
            }

            if (options.ShopperPersonIdOverride >= 0)
            {
                string overrideProblem = ValidateShopper(shopper);
                if (overrideProblem != null)
                {
                    return FailPlan(trip, need, shopperId, $"Shopper override rejected: {overrideProblem}", diag);
                }
            }

            result.ShopperPersonId = shopperId;
            trip.ShopperPersonId = shopperId;
            trip.DepartureMinuteOfDay = options.DepartureMinuteOfDay;
            string shopperHome = ResolvePersonLocation(shopperId, options);
            trip.HomeLocationId = shopperHome;
            AddEvent(result, dayIndex, ShoppingEventKind.ShopperAssignment, shopperId, need,
                $"P{shopperId} ({shopper.DisplayName}) is responsible for shopping '{need.ItemId}' x{need.UnitsNeeded}.",
                $"trip window ~{estimateMinutes} min from minute {options.DepartureMinuteOfDay}; Person time is authoritative.");

            // ---- Step 4: weigh candidates. ----
            ShoppingCandidate chosen = EvaluateCandidates(
                result, need, shopperId, shopperHome, knownOffering, dayIndex, options, diag);
            if (chosen == null)
            {
                trip.State = PlannedShoppingTrip.TripState.Failed;
                trip.FailureReason = result.FailureReason ?? "No viable seller.";
                if (!tripHistory.Contains(result))
                {
                    tripHistory.Add(result);
                }

                return trip;
            }

            trip.Candidate = chosen;

            // ---- Step 5: schedule the trip through the real TaskAuthority. ----
            EntityId shopperEid = personEntityId(shopperId);
            var legs = new PlannedTrip();
            if (!PlanTripLegs(legs, result, need, shopperId, shopperEid, shopperHome, chosen, dayIndex, options, diag))
            {
                trip.State = PlannedShoppingTrip.TripState.Failed;
                trip.FailureReason = result.FailureReason ?? "Trip leg planning failed.";
                return trip;
            }

            trip.Legs = legs;
            trip.OutboundTaskId = legs.Outbound != null ? legs.Outbound.TaskId : default(EntityId);
            trip.InStoreTaskId = legs.InStore != null ? legs.InStore.TaskId : default(EntityId);
            trip.ReturnTaskId = legs.Return != null ? legs.Return.TaskId : default(EntityId);

            // One person-time window per leg: the trip occupies real Travel +
            // Shopping time. A refusal rolls back the assigned legs (budget
            // commitments released) — never a half-planned trip.
            if (!ReserveTripWindows(trip, result, need, shopperId, legs, dayIndex, options, diag))
            {
                foreach (WorkTask leg in new[] { legs.Outbound, legs.InStore, legs.Return })
                {
                    if (leg != null && leg.Status == TaskStatus.Assigned)
                    {
                        string ignore;
                        tasks.InterruptTask(leg.TaskId, budgets, out ignore);
                    }
                }

                trip.State = PlannedShoppingTrip.TripState.Failed;
                if (!tripHistory.Contains(result))
                {
                    tripHistory.Add(result);
                }

                return trip;
            }

            trip.State = PlannedShoppingTrip.TripState.Planned;
            diag.Add($"Day {dayIndex}: shopping trip {trip.TripId} planned — P{shopperId} → " +
                $"'{chosen.Supplier.SupplierName}' ({legs.OutboundMinutes} min out, {options.InStoreMinutes} min in store, {legs.ReturnMinutes} min back).");
            return trip;
        }

        /// <summary>
        /// Pass IV: reserves one person-time window per trip leg (Travel out,
        /// Shopping in-store, Travel back). Returns false with the refusal as
        /// the trip failure reason when any window cannot be secured.
        /// </summary>
        private bool ReserveTripWindows(
            PlannedShoppingTrip trip,
            ShoppingTripResult result,
            HouseholdPurchasingNeed need,
            int shopperId,
            PlannedTrip legs,
            int dayIndex,
            ShoppingLoopOptions options,
            List<string> diag)
        {
            var ids = new List<string>();
            int departure = Math.Max(0, options.DepartureMinuteOfDay);
            int inStore = Math.Max(1, options.InStoreMinutes);
            int arrival = departure + legs.OutboundMinutes;
            int leaveStore = arrival + inStore;

            string refusal = TryReserveWindow(shopperId, PersonActivityKind.Travel, dayIndex,
                departure, legs.OutboundMinutes, $"shopping trip {trip.TripId} outbound", ids);
            if (refusal == null && legs.InStore != null)
            {
                refusal = TryReserveWindow(shopperId, PersonActivityKind.Shopping, dayIndex,
                    arrival, inStore, $"shopping trip {trip.TripId} in-store", ids);
            }

            if (refusal == null && legs.Return != null)
            {
                refusal = TryReserveWindow(shopperId, PersonActivityKind.Travel, dayIndex,
                    leaveStore, legs.ReturnMinutes, $"shopping trip {trip.TripId} return", ids);
            }

            if (refusal != null)
            {
                foreach (string id in ids)
                {
                    scheduleTracker.Release(id);
                }

                AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, need,
                    $"Trip window reservation refused: {refusal}", string.Empty);
                result.FailureReason = refusal;
                trip.FailureReason = refusal;
                diag.Add($"Day {dayIndex}: shopping trip {trip.TripId} cannot be scheduled — {refusal}");
                return false;
            }

            trip.WindowReservationIds = ids;
            return true;
        }

        private string TryReserveWindow(
            int shopperId, PersonActivityKind kind, int dayIndex,
            int startMinute, int durationMinutes, string label, List<string> ids)
        {
            if (durationMinutes <= 0)
            {
                return null; // zero-travel leg: no window needed.
            }

            string refusal = scheduleTracker.TryReserve(shopperId, kind, dayIndex, startMinute, durationMinutes, label);
            if (refusal != null)
            {
                return refusal;
            }

            List<PersonActivityReservation> active = scheduleTracker.ActiveFor(shopperId, dayIndex);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] != null && active[i].Activity == kind && active[i].StartMinuteOfDay == Math.Max(0, startMinute))
                {
                    ids.Add(active[i].ReservationId);
                    return null;
                }
            }

            return "INCONSISTENCY: trip window reservation vanished after booking.";
        }

        private PlannedShoppingTrip FailPlan(
            PlannedShoppingTrip trip, HouseholdPurchasingNeed need, int shopperId, string reason, List<string> diag)
        {
            trip.State = PlannedShoppingTrip.TripState.Failed;
            trip.FailureReason = reason;
            AddEvent(trip.Result, trip.DayIndex, ShoppingEventKind.Failure, shopperId, need, reason,
                "Unfulfilled demand stays observable: the need remains open for §26 diagnostics and NPC opportunity detection.");
            trip.Result.FailureReason = reason;
            diag.Add($"Day {trip.DayIndex}: shopping trip planning FAILED — {reason}");
            if (!tripHistory.Contains(trip.Result))
            {
                tripHistory.Add(trip.Result);
            }

            return trip;
        }

        /// <summary>Pass IV: releases every person-time window a trip reserved.</summary>
        public void ReleaseTripWindows(PlannedShoppingTrip trip)
        {
            if (trip == null || trip.WindowReservationIds == null)
            {
                return;
            }

            foreach (string id in trip.WindowReservationIds)
            {
                scheduleTracker.Release(id);
            }

            trip.WindowReservationIds.Clear();
        }

        /// <summary>
        /// Pass IV: starts the outbound travel leg through the real task system.
        /// Returns null on success, else the failure reason.
        /// </summary>
        public string BeginOutboundLeg(PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (trip == null || trip.Legs == null)
            {
                return "BeginOutboundLeg: no planned trip.";
            }

            PersonState shopper = population.GetPerson(trip.ShopperPersonId);
            if (trip.Legs.Outbound != null)
            {
                if (!tasks.StartTask(trip.Legs.Outbound.TaskId, dayIndex, out string rejection))
                {
                    return $"Outbound leg could not start: {rejection}";
                }
            }

            SetScheduleState(shopper, PopulationScheduleState.GoingShopping);
            AddEvent(trip.Result, dayIndex, ShoppingEventKind.JourneyLeg, trip.ShopperPersonId, trip.Need,
                $"P{trip.ShopperPersonId} departed for '{trip.Candidate.Supplier.SupplierName}'.",
                $"outbound {trip.Legs.OutboundMinutes} min; real travel task, real person-time.");
            trip.State = PlannedShoppingTrip.TripState.Outbound;
            return null;
        }

        /// <summary>
        /// Pass IV: arrival at the store — location + schedule state update, the
        /// trading-capability check (step 7), and the shared counter settlement
        /// (steps 9–14). Returns null on success; on failure returns a reason
        /// prefixed STORE_CLOSED or SETTLE_FAILED so the driver can walk the
        /// shopper home empty-handed through the real return leg.
        /// </summary>
        public string ArriveAtStoreAndSettle(PlannedShoppingTrip trip, int arrivalDayIndex, int arrivalMinuteOfDay, List<string> diag)
        {
            if (trip == null || trip.Candidate == null)
            {
                return "SETTLE_FAILED: no planned trip or candidate.";
            }

            diag = diag ?? new List<string>();
            var need = trip.Need;
            var chosen = trip.Candidate;
            int shopperId = trip.ShopperPersonId;
            PersonState shopper = population.GetPerson(shopperId);
            SetPersonLocation(shopperId, chosen.Supplier.LocationId, trip.Options);
            SetScheduleState(shopper, PopulationScheduleState.AtStore);
            AddEvent(trip.Result, arrivalDayIndex, ShoppingEventKind.JourneyLeg, shopperId, need,
                $"P{shopperId} arrived at '{chosen.Supplier.SupplierName}'.",
                $"arrival minute {arrivalMinuteOfDay}; no teleport — the outbound leg ran its minutes.");

            string tradingProblem = chosen.ItemSupplier.CheckTradingCapability(arrivalDayIndex, arrivalMinuteOfDay);
            if (tradingProblem != null)
            {
                return $"STORE_CLOSED: '{chosen.Supplier.SupplierName}' cannot trade at arrival: {tradingProblem}. " +
                    "Shopper returns empty-handed; trip rescheduled, need stays open.";
            }

            SettlementOutcome counter = SettleAtCounter(
                trip.Result, trip.Legs, need, chosen, shopperId, arrivalDayIndex, diag, trip.Options);
            if (!counter.Success)
            {
                return $"SETTLE_FAILED: {counter.FailureReason}";
            }

            trip.Batch = counter.Batch;
            trip.UnitsSettled = counter.UnitsSold;
            trip.State = PlannedShoppingTrip.TripState.AtStore;
            if (chosen.UseDelivery)
            {
                trip.DeliveryMinutes = EstimateDeliveryMinutes(trip, diag);
            }

            return null;
        }

        private int EstimateDeliveryMinutes(PlannedShoppingTrip trip, List<string> diag)
        {
            string home = trip.HomeLocationId ?? string.Empty;
            string store = trip.Candidate != null && trip.Candidate.Supplier != null
                ? trip.Candidate.Supplier.LocationId ?? string.Empty
                : string.Empty;
            JourneyRoute freight = journeys.FindRoute(store, home, TravelMode.Wagon);
            int minutes = freight.Found ? Math.Max(1, freight.TotalMinutes) : 0;
            AddEvent(trip.Result, trip.DayIndex, ShoppingEventKind.Delivery, trip.ShopperPersonId, trip.Need,
                $"Arranged delivery scheduled: {trip.UnitsSettled}u {trip.Need.ItemId} by wagon ({minutes} min).",
                "freight already settled; custody transfers at the door — real time, not instant.");
            return minutes;
        }

        /// <summary>Pass IV: starts the in-store shopping leg (real work time).</summary>
        public string BeginInStoreLeg(PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (trip == null || trip.Legs == null || trip.Legs.InStore == null)
            {
                return "BeginInStoreLeg: no in-store leg planned.";
            }

            if (!tasks.StartTask(trip.Legs.InStore.TaskId, dayIndex, out string rejection))
            {
                return $"In-store leg could not start: {rejection}";
            }

            trip.State = PlannedShoppingTrip.TripState.InStore;
            return null;
        }

        /// <summary>Pass IV: starts the return travel leg (homeward, possibly empty-handed).</summary>
        public string BeginReturnLeg(PlannedShoppingTrip trip, int dayIndex, string legName, List<string> diag)
        {
            if (trip == null || trip.Legs == null)
            {
                return "BeginReturnLeg: no planned trip.";
            }

            PersonState shopper = population.GetPerson(trip.ShopperPersonId);
            SetScheduleState(shopper, PopulationScheduleState.ReturningHome);
            if (trip.Legs.Return != null)
            {
                if (!tasks.StartTask(trip.Legs.Return.TaskId, dayIndex, out string rejection))
                {
                    return $"Return leg '{legName}' could not start: {rejection}";
                }
            }

            trip.State = PlannedShoppingTrip.TripState.Returning;
            return null;
        }

        /// <summary>
        /// Pass IV: records the worked minutes of one finished leg and completes
        /// it — travel and shopping occupy real budgeted person-time.
        /// </summary>
        internal string CompleteLegTask(WorkTask leg, int dayIndex, List<string> diag)
        {
            if (leg == null)
            {
                return null; // zero-travel leg: nothing to complete.
            }

            int remaining = Math.Max(1, leg.MinutesRemaining);
            if (!tasks.RecordWorkProgress(leg.TaskId, remaining, budgets, dayIndex, out bool reached, out string rejection))
            {
                return $"Leg task progress refused: {rejection}";
            }

            if (reached && !tasks.CompleteTask(leg.TaskId, TaskOutcome.Success, dayIndex, budgets))
            {
                return $"Leg task {leg.TaskId} reached its plan but CompleteTask refused.";
            }

            return null;
        }

        /// <summary>
        /// Pass IV: step 16 — physical receipt ONLY. Household inventory grows
        /// here, and only here, for carried goods. Delivery batches are received
        /// by ReceiveDelivery when the freight arrives. Releases the trip's
        /// person-time windows and closes the trip.
        /// </summary>
        public string ReceiveAtHome(PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (trip == null)
            {
                return "ReceiveAtHome: no trip.";
            }

            diag = diag ?? new List<string>();
            var need = trip.Need;
            var batch = trip.Batch;
            var result = trip.Result;
            PersonState shopper = population.GetPerson(trip.ShopperPersonId);
            SetPersonLocation(trip.ShopperPersonId, trip.HomeLocationId, trip.Options);

            if (batch != null && batch.Custody == GoodsCustodyState.InHand)
            {
                // W5: custody holds STORE units (sacks for a package item);
                // the household is credited packages × package size.
                // Priority U-3: the batch carries its own package size from
                // settle time, so a restored trip whose candidate lost it
                // still credits correctly; the candidate's value wins when
                // it is larger (old saves, whose batches carry the 1
                // default, keep the candidate's size).
                int packageSize = Math.Max(Math.Max(1, batch.HouseholdUnitsPerStoreUnit),
                    Math.Max(1, trip.Candidate?.HouseholdUnitsPerStoreUnit ?? 1));
                int unitsReceived = batch.Units * packageSize;
                HouseholdInventory inventory = inventories.GetOrCreate(need.HouseholdId);
                string provenance = batch.Provenance + " → carried home";
                int lotIndexBefore = inventory.Lots.Count;
                string lotRejection = inventory.AddLot(need.ItemId, unitsReceived, dayIndex, provenance,
                    condition: ConditionOf(batch));
                if (lotRejection != null)
                {
                    return $"INCONSISTENCY: goods are in custody ({batch.BatchId}) but the household lot was rejected: {lotRejection}";
                }

                batch.Custody = GoodsCustodyState.ReceivedAtHousehold;
                custodyBatches.Remove(batch);
                // Priority V1: the deposited lot's identity, resolved
                // through the F4 resolver, and the deposit verified
                // through the loop's production person inventory read model.
                RecordDepositedLotIdentity(inventory, lotIndexBefore, result, diag);
                VerifyDepositViaPersonInventory(trip.ShopperPersonId, batch, diag);
                RecordHouseholdReceipt(batch, need, unitsReceived, dayIndex,
                    CustodyTransferLog.PersonCustodian(trip.ShopperPersonId));
                need.UnitsNeeded = Math.Max(0, need.UnitsNeeded - unitsReceived);
                need.LastEvaluatedDayIndex = Math.Max(0, dayIndex);
                if (need.UnitsNeeded <= 0)
                {
                    need.Status = PurchasingNeedStatus.Fulfilled;
                }

                result.Success = true;
                result.UnitsAcquired = unitsReceived;
                result.UnitsStillNeeded = need.UnitsNeeded;
                AddEvent(result, dayIndex, ShoppingEventKind.Receipt, trip.ShopperPersonId, need,
                    $"H{need.HouseholdId} received {unitsReceived}u {need.ItemId} into real lots (physical receipt).",
                    batch.Provenance + (need.Status == PurchasingNeedStatus.Fulfilled ? " Need fulfilled." : $" {need.UnitsNeeded}u still needed."));
            }
            else if (batch != null)
            {
                AddEvent(result, dayIndex, ShoppingEventKind.Receipt, trip.ShopperPersonId, need,
                    $"Shopper home; {batch.Units}u {need.ItemId} still in {batch.Custody} custody ({batch.BatchId}) — delivery receipt is separate.",
                    "no teleport: custody transfers only at the door.");
            }

            SetScheduleState(shopper, PopulationScheduleState.AtHome);
            ReleaseTripWindows(trip);
            trip.State = PlannedShoppingTrip.TripState.Done;
            if (!tripHistory.Contains(result))
            {
                tripHistory.Add(result);
            }

            diag.Add($"Day {dayIndex}: shopping trip {trip.TripId} closed — P{trip.ShopperPersonId} home.");
            return null;
        }

        /// <summary>
        /// Pass IV: the arranged delivery's freight arrived (JourneyStageDue) —
        /// custody transfers at the household door and the lot is booked.
        /// </summary>
        public string ReceiveDelivery(PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (trip == null || trip.Batch == null)
            {
                return "ReceiveDelivery: no delivery batch.";
            }

            diag = diag ?? new List<string>();
            var batch = trip.Batch;
            if (batch.Custody != GoodsCustodyState.ArrangedDelivery)
            {
                return null; // already received or carried — nothing to do.
            }

            var need = trip.Need;
            // W5: custody holds STORE units (sacks for a package item);
            // the household is credited packages × package size.
            // Priority U-3: as in ReceiveAtHome, the batch's own package
            // size from settle time is authoritative when present.
            int packageSize = Math.Max(Math.Max(1, batch.HouseholdUnitsPerStoreUnit),
                Math.Max(1, trip.Candidate?.HouseholdUnitsPerStoreUnit ?? 1));
            int unitsReceived = batch.Units * packageSize;
            HouseholdInventory inventory = inventories.GetOrCreate(need.HouseholdId);
            int lotIndexBefore = inventory.Lots.Count;
            string lotRejection = inventory.AddLot(need.ItemId, unitsReceived, dayIndex, batch.Provenance + " → delivered",
                condition: ConditionOf(batch));
            if (lotRejection != null)
            {
                return $"INCONSISTENCY: delivery {batch.BatchId} arrived but the household lot was rejected: {lotRejection}";
            }

            batch.Custody = GoodsCustodyState.ReceivedAtHousehold;
            custodyBatches.Remove(batch);
            // Priority V1: the deposited lot's identity, resolved through
            // the F4 resolver, and the deposit verified through the
            // loop's production person inventory read model.
            RecordDepositedLotIdentity(inventory, lotIndexBefore, trip.Result, diag);
            VerifyDepositViaPersonInventory(trip.ShopperPersonId, batch, diag);
            RecordHouseholdReceipt(batch, need, unitsReceived, dayIndex,
                CustodyTransferLog.WagonCustodian("arranged-delivery"));
            need.UnitsNeeded = Math.Max(0, need.UnitsNeeded - unitsReceived);
            need.LastEvaluatedDayIndex = Math.Max(0, dayIndex);
            if (need.UnitsNeeded <= 0)
            {
                need.Status = PurchasingNeedStatus.Fulfilled;
            }

            AddEvent(trip.Result, dayIndex, ShoppingEventKind.Delivery, trip.ShopperPersonId, need,
                $"Delivery arrived: {unitsReceived}u {need.ItemId} received at the household door ({batch.BatchId}).",
                "freight ran its real minutes; custody transferred at the door.");
            diag.Add($"Day {dayIndex}: delivery {batch.BatchId} received ({unitsReceived}u {need.ItemId}).");
            return null;
        }

        /// <summary>
        /// Pass IV: aborts a trip loudly — releases windows, records the failure
        /// event, keeps the need open. Never silently drops a trip.
        /// </summary>
        public void AbortTrip(PlannedShoppingTrip trip, string reason, int dayIndex, List<string> diag)
        {
            if (trip == null)
            {
                return;
            }

            diag = diag ?? new List<string>();
            trip.State = PlannedShoppingTrip.TripState.Failed;
            trip.FailureReason = reason ?? "aborted";
            AddEvent(trip.Result, dayIndex, ShoppingEventKind.Failure, trip.ShopperPersonId, trip.Need,
                $"Trip {trip.TripId} aborted: {trip.FailureReason}",
                "need stays open; windows released.");
            trip.Result.FailureReason = trip.FailureReason;
            ReleaseTripWindows(trip);
            if (!tripHistory.Contains(trip.Result))
            {
                tripHistory.Add(trip.Result);
            }

            diag.Add($"Day {dayIndex}: shopping trip {trip.TripId} ABORTED — {trip.FailureReason}");
        }

        // ---------- steps, factored ----------

        /// <summary>Even an explicit shopper override must be a real, living, old-enough member.</summary>
        private string ValidateShopper(PersonState shopper)
        {
            if (shopper.deathDayIndex >= 0)
            {
                return $"P{shopper.id} is deceased.";
            }

            if (shopper.ageBand < AgeBand.YoungWorker16To17)
            {
                return $"P{shopper.id} ({shopper.ageBand}) is too young to shop alone.";
            }

            // Plan 08 Slice 2 (Q03, AUD-266): an explicit override naming
            // an off-map person is refused — the override path bypasses
            // ChooseShopper, so this is its only guard. Null authority
            // fails open.
            if (offMapJourneys != null && offMapJourneys.IsOffMapPerson(shopper.id))
            {
                return $"P{shopper.id} is off-map.";
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

                    if (supplier is IItemResolvingSupplier
                        && SupplierRecognizesItem(supplier, itemId))
                    {
                        found.Add(supplier);
                    }
                }
            }

            return found;
        }

        private static bool SupplierRecognizesItem(IGoodsSupplier supplier, string itemId)
        {
            if (supplier is not IItemResolvingSupplier resolving)
                return false;
            return supplier is IItemCatalogSupplier catalog
                ? catalog.RecognizesItem(itemId)
                : resolving.OffersItem(itemId);
        }

        private string DescribeNoKnownItemOffer(List<int> memberIds, string itemId)
        {
            bool hasKnownSeller = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (memberIds != null)
            {
                foreach (int memberId in memberIds)
                {
                    List<IGoodsSupplier> known = knowledge.KnownSuppliers(memberId, suppliers.All);
                    foreach (IGoodsSupplier supplier in known)
                    {
                        if (supplier != null && seen.Add(supplier.SupplierBusinessId))
                        {
                            hasKnownSeller = true;
                        }
                    }
                }
            }

            return hasKnownSeller
                ? $"Known seller(s) exist, but none currently advertise a purchasable '{itemId}' offer (item mapping, price, or available stock may be missing). Need stays open."
                : $"No legitimately known seller offers '{itemId}' — unavailable seller, need stays open.";
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

        /// <summary>
        /// Reserves the shopping window. With
        /// <paramref name="allowRestPreemption"/> (the ExecuteBatch path only),
        /// overlapping Resting reservations are released atomically with the
        /// reserve (TryReservePreemptingRest) — the window search has already
        /// proven the rest of the window, so a released rest is never lost to
        /// a trip that fails to reserve. The single-need path passes false
        /// and keeps the old strict behaviour.
        /// </summary>
        private string ReserveShoppingWindow(
            int shopperId, int dayIndex, ShoppingLoopOptions options, int estimateMinutes,
            ShoppingTripResult result, List<string> diag, bool allowRestPreemption = false)
        {
            string refusal;
            string releasedRest = string.Empty;
            if (allowRestPreemption)
            {
                refusal = scheduleTracker.TryReservePreemptingRest(
                    shopperId, PersonActivityKind.Shopping, dayIndex,
                    options.DepartureMinuteOfDay, estimateMinutes,
                    $"shopping trip for H{result.HouseholdId}", out releasedRest);
            }
            else
            {
                refusal = scheduleTracker.TryReserve(
                    shopperId, PersonActivityKind.Shopping, dayIndex,
                    options.DepartureMinuteOfDay, estimateMinutes,
                    $"shopping trip for H{result.HouseholdId}");
            }

            if (refusal != null)
            {
                AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, null,
                    $"Double-booking refused: {refusal}", string.Empty);
                result.FailureReason = refusal;
                return null;
            }

            if (!string.IsNullOrEmpty(releasedRest))
            {
                diag.Add($"Day {dayIndex}: P{shopperId} shopping released rest {releasedRest} " +
                    $"for the trip window from minute {options.DepartureMinuteOfDay}.");
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
            List<string> diag,
            int? budgetOverrideCents = null)
        {
            ShoppingCandidate best = null;
            // Pass V P2-1: batched trips plan several needs against one ledger.
            // The override lets the batch planner shrink the affordable budget
            // as earlier needs claim their share — never double-spending cash.
            int ledgerBalance = budgetOverrideCents ?? ledgers.GetOrCreate(need.HouseholdId).GetBalanceCents();

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

                // W5: whole-package purchase. When the supplier sells this
                // item in packages bigger than the household unit (flour:
                // 1 sack = 10 lb), stock and price are in STORE units
                // (sacks), so the plan buys whole packages — ceil(need /
                // package size), capped by sacks in stock — and the receipt
                // multiplies packages back into household units. The
                // affordability downsizing below already divides the ledger
                // by the per-package price, so it stays in store units.
                int packageSize = 1;
                if (itemSupplier is IPackageUnitSupplier packageSupplier)
                    packageSize = Math.Max(1, packageSupplier.HouseholdUnitsPerStoreUnit(need.ItemId));
                int units = packageSize > 1
                    ? Math.Min((need.UnitsNeeded + packageSize - 1) / packageSize, stock)
                    : Math.Min(need.UnitsNeeded, stock);
                if (units <= 0)
                {
                    Reject(result, need, supplier, dayIndex, "no stock to sell", price, stock, route.TotalMinutes, diag);
                    continue;
                }

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
                // Review 2 second scripts pass: why this candidate
                // would fill less than the need, if it would. The
                // cause is carried onto the chosen candidate and
                // then onto the trip result as PartialFillReason,
                // so a need that stays open after a real purchase
                // names its reason instead of leaving callers with
                // an empty FailureReason. Affordability binds
                // tighter than stock here: when both bite, the
                // downsizing branch below names affordability.
                string partialCause = null;
                if (total <= ledgerBalance)
                {
                    mode = ShoppingSettlementMode.CashFromLedger;
                }
                else if (options.AllowTradeCredit && obligations != null && entityIds != null &&
                    supplier is IOffersTradeCredit credit && credit.OffersTradeCredit && total <= credit.TradeCreditLimitCents)
                {
                    mode = ShoppingSettlementMode.TradeCreditObligation;
                }
                else if (price > 0 && ledgerBalance >= price)
                {
                    // Honest downsizing: the household cannot cover the full
                    // need, so it buys what its ledger can actually cover —
                    // a smaller real trip, not a rejected need and never debt.
                    int fullPlannedTotal = total;
                    units = Math.Min(ledgerBalance / price, stock);
                    useDelivery = deliveryOffered && units > Math.Max(1, options.CarryCapacityUnits);
                    if (useDelivery)
                    {
                        // Freight on a shoestring budget: carry what fits.
                        units = Math.Min(units, Math.Max(1, options.CarryCapacityUnits));
                        useDelivery = false;
                    }

                    // Set after the carry cap, so the quoted unit count
                    // is the final planned count, not a pre-cap count.
                    // W5: for a package item the planned count is packages,
                    // so the message names packages and household units.
                    partialCause = $"affordability-downsized: affordable budget is {ledgerBalance}c, not the {fullPlannedTotal}c full planned cost; " +
                        (packageSize > 1
                            ? $"buying {units} package(s) ({units * packageSize}u) instead of {need.UnitsNeeded}u, never debt."
                            : $"buying {units}u instead of {need.UnitsNeeded}u, never debt.");
                    goodsCost = units * price;
                    total = goodsCost;
                    if (units <= 0 || total > ledgerBalance)
                    {
                        string why = $"unaffordable even downsized: {total}c > ledger balance {ledgerBalance}c, no valid credit available";
                        Reject(result, need, supplier, dayIndex, why, price, stock, route.TotalMinutes, diag);
                        continue;
                    }

                    mode = ShoppingSettlementMode.CashFromLedger;
                    AddEvent(result, dayIndex, ShoppingEventKind.Decision, shopperId, need,
                        $"Downsized '{need.ItemId}' to {units}u — the household ledger covers {ledgerBalance}c, not the full need. No debt manufactured.",
                        "affordability is a planning constraint, not a refusal.");
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
                    HouseholdUnitsPerStoreUnit = packageSize,
                    UnitPriceCents = price,
                    TravelMinutesOneWay = Math.Max(1, route.TotalMinutes),
                    TravelMilesOneWay = route.TotalMiles,
                    TotalCostCents = total,
                    DeliveryFeeCents = freight,
                    UseDelivery = useDelivery,
                    SettlementMode = mode,
                    // Stock binds only when affordability did not
                    // already downsize: units was capped at stock
                    // before the affordability chain ran. W5: the
                    // comparison is in household units — a planned
                    // package count is smaller than the need in lb
                    // even when it covers it.
                    PartialFillReason = partialCause ??
                        (units * packageSize < need.UnitsNeeded
                            ? (packageSize > 1
                                ? $"stock-limited: seller holds {stock} package(s) ({stock * packageSize}u {need.ItemId}), need is {need.UnitsNeeded}u."
                                : $"stock-limited: seller holds {stock}u, need is {need.UnitsNeeded}u.")
                            : string.Empty),
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
                // Plan 02 Slice 5: when every candidate was rejected on
                // money, the per-need reason must name the budget, not
                // hide behind the generic "all candidates rejected" — the
                // daily summary reports the first failure reason per open
                // need. Mixed rejections keep the generic reason (the
                // detail stays in RejectedAlternatives).
                bool allUnaffordable = result.RejectedAlternatives.Count > 0;
                foreach (RejectedShoppingAlternative alternative in result.RejectedAlternatives)
                {
                    if (alternative == null || string.IsNullOrEmpty(alternative.Reason)
                        || !alternative.Reason.StartsWith("unaffordable", StringComparison.Ordinal))
                    {
                        allUnaffordable = false;
                        break;
                    }
                }

                result.FailureReason = allUnaffordable
                    ? $"No viable seller for '{need.ItemId}': budget short — {result.RejectedAlternatives[0].Reason}. Need stays open."
                    : $"No viable seller for '{need.ItemId}': all {result.RejectedAlternatives.Count} candidates rejected (see RejectedAlternatives).";
                return null;
            }

            // The chosen candidate's partial-fill cause becomes the
            // trip's cause. A fully planned trip carries an empty
            // reason; a failed trip later clears it in Fail(), so a
            // partial reason only ever survives on a trip that
            // actually bought something.
            result.PartialFillReason = best.PartialFillReason ?? string.Empty;
            // W5: the planned count is in store units; name packages and
            // household units when they differ so the log never silently
            // changes what "u" means.
            string chosenUnitsText = best.HouseholdUnitsPerStoreUnit > 1
                ? $"{best.UnitsPlanned} package(s) ({best.UnitsPlanned * best.HouseholdUnitsPerStoreUnit}u {need.ItemId})"
                : $"{best.UnitsPlanned}u {need.ItemId}";
            AddEvent(result, dayIndex, ShoppingEventKind.Decision, shopperId, need,
                $"Chose '{best.Supplier.SupplierName}': {chosenUnitsText} @ {best.UnitPriceCents}c " +
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

        public sealed class PlannedTrip
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
                    // Priority 4 Slice 4: feed the refusal into the
                    // task-duration diagnostic (L6) where one is wired.
                    // The leg task was created above, so the observer has
                    // a record for it; an unwired loop feeds nothing.
                    TimingObserver?.RecordRefusal(leg.TaskId.ToString(), "AssignTask", assignReason, dayIndex);
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
                // Priority 4 Slice 4: feed the refusal into the
                // task-duration diagnostic (L6) where one is wired.
                TimingObserver?.RecordRefusal(leg.TaskId.ToString(), "StartTask", startReason, dayIndex);
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
            // A failed trip bought nothing, so an evaluation-time
            // partial-fill cause must not survive on it: the
            // failure reason is the whole story. Partial reasons
            // live only on trips with a real purchase.
            result.PartialFillReason = string.Empty;
            AddEvent(result, dayIndex, ShoppingEventKind.Failure, shopperId, need, reason,
                "Unfulfilled demand stays observable: the need remains open for §26 diagnostics and NPC opportunity detection.");
            diag.Add($"Day {dayIndex}: shopping FAILED — {reason}");
            tripHistory.Add(result);
            return result;
        }

        /// <summary>Step 4's chosen candidate.</summary>
        internal sealed class ShoppingCandidate
        {
            public IGoodsSupplier Supplier;
            public IItemResolvingSupplier ItemSupplier;
            public int UnitsPlanned;
            /// <summary>
            /// W5: household units credited per store unit bought (1 for
            /// ordinary items; 10 for a flour sack). UnitsPlanned, the
            /// counter settlement and custody are all in STORE units; the
            /// receipt multiplies by this to credit household units.
            /// </summary>
            public int HouseholdUnitsPerStoreUnit = 1;
            public string PartialFillReason = string.Empty;
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
