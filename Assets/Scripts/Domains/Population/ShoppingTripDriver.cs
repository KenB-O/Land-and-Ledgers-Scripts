using System;
using System.Collections.Generic;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Logistics;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass IV (Worker 4): a shopping trip planned through the real
    /// HouseholdShoppingLoop but executed through real time by the
    /// ShoppingTripDriver. One trip = one purchasing need, one real shopper,
    /// three real task legs (travel out / in-store / travel back) each with its
    /// own person-time window, settlement at the counter, legitimate custody,
    /// and physical receipt at home. Save-persisted via the DTO (legs
    /// re-resolve through the TaskAuthority by EntityId; the supplier
    /// re-resolves through the SupplierDirectory by business id).
    /// </summary>
    [Serializable]
    public sealed class PlannedShoppingTrip
    {
        public enum TripState
        {
            Planned = 0,
            Outbound = 1,
            AtStore = 2,
            InStore = 3,
            Returning = 4,
            Done = 5,
            Failed = 6,
            RecoveryPlanned = 7,
            // Slice 17: arrived at the store, service task started,
            // settlement waiting for the clerk's TendCustomer service to
            // complete. Appended (serialized enums are append-only); the
            // DTO persists State as an int and old saves never carry 8.
            AwaitingService = 8,
        }

        public string TripId = string.Empty;
        public HouseholdPurchasingNeed Need;
        public int HouseholdId = -1;
        public int ShopperPersonId = -1;
        public int DayIndex;
        public int DepartureMinuteOfDay;
        public string HomeLocationId = string.Empty;
        public ShoppingLoopOptions Options;
        public ShoppingTripResult Result;
        public TripState State = TripState.Planned;
        public string FailureReason = string.Empty;
        public CarriedGoodsBatch Batch;
        public int UnitsSettled;
        public int DeliveryMinutes;
        public List<string> WindowReservationIds = new List<string>();

        // Route liveness is trip state, not callback state. These values are
        // persisted so a missing due-event map entry or Save/Continue cannot
        // reset an outbound shopper's patience window indefinitely.
        public int ActiveRouteLegKind = -1;
        public float ActiveRouteStartedGameSeconds = -1f;
        public float ActiveRouteDeadlineGameSeconds = -1f;
        public float ActiveRouteLastProgressGameSeconds = -1f;
        public float ActiveRouteLastRemainingSeconds = -1f;
        public bool ActiveRouteHasProgressSample;
        // An empty-handed return may be delayed briefly by an unrelated
        // exclusive task. Keep the trip identity and assigned return leg
        // alive while that task finishes; retry is bounded and saveable.
        public bool ReturnStartPending;
        public float ReturnStartRetryAfterGameSeconds = -1f;
        public float ReturnStartDeadlineGameSeconds = -1f;

        /// <summary>Leg tasks (live form). Re-resolved from LegTaskIds after load.</summary>
        public HouseholdShoppingLoop.PlannedTrip Legs;

        /// <summary>Chosen supplier (live form). Rebuilt from CandidateDto after load.</summary>
        internal HouseholdShoppingLoop.ShoppingCandidate Candidate;

        internal EntityId OutboundTaskId;
        internal EntityId InStoreTaskId;
        internal EntityId ReturnTaskId;

        /// <summary>Plan 16: the TendCustomer task serving this trip's
        /// shopper, and its skill-scaled planned minutes (the due time is
        /// scheduled from this, never from the 4-minute base). Slice 19
        /// (f2): persisted in PlannedShoppingTripSaveDto; on rehydrate the
        /// due-event map entry is re-registered so a trip saved mid-wait
        /// resumes its wait (the persisted due fires at its original
        /// absolute time — the due is never re-scheduled, unlike legs
        /// (ScheduleLegDueAfterLoad, :961)).</summary>
        internal EntityId ServiceTaskId;
        internal int ServiceTaskPlannedMinutes;
        public float ServiceWaitDeadlineGameSeconds = -1f;
        internal PlannedShoppingTripSaveDto CandidateDto;

        public bool IsActive => State == TripState.Planned || State == TripState.Outbound ||
            State == TripState.AtStore || State == TripState.InStore || State == TripState.Returning || State == TripState.RecoveryPlanned || State == TripState.AwaitingService;
    }

    /// <summary>
    /// Pass IV: save data for one planned shopping trip. The supplier and legs
    /// re-resolve through their authorities — the DTO carries stable ids, never
    /// live object graphs.
    /// </summary>
    [Serializable]
    public sealed class PlannedShoppingTripSaveDto
    {
        public string TripId = string.Empty;
        public HouseholdPurchasingNeed Need;
        public int HouseholdId = -1;
        public int ShopperPersonId = -1;
        public int DayIndex;
        public int DepartureMinuteOfDay;
        public string HomeLocationId = string.Empty;
        public int ResultNeedSequence;
        public int State;
        // Save/Continue (this slice, F2): true when the trip was waiting
        // in the driver's returnRecoveries list (a paid return whose
        // recovery had not been planned yet). Additive; old saves read
        // false and take the active-trip path.
        public bool InReturnRecovery;
        public string FailureReason = string.Empty;
        public int UnitsSettled;
        public int DeliveryMinutes;
        public List<string> WindowReservationIds = new List<string>();
        public EntityId OutboundTaskId;
        public EntityId InStoreTaskId;
        public EntityId ReturnTaskId;
        // Slice 19 (f2): the TendCustomer service task serving this trip's
        // shopper, and its skill-scaled planned minutes — so a trip saved
        // mid-wait can resume its wait on rehydrate instead of settling
        // on restore. Additive; old saves read the defaults (invalid id,
        // 0 minutes) and take the fallback path.
        public EntityId ServiceTaskId;
        public int ServiceTaskPlannedMinutes;
        public float ServiceWaitDeadlineGameSeconds = -1f;
        public int ActiveRouteLegKind = -1;
        public float ActiveRouteStartedGameSeconds = -1f;
        public float ActiveRouteDeadlineGameSeconds = -1f;
        public float ActiveRouteLastProgressGameSeconds = -1f;
        public float ActiveRouteLastRemainingSeconds = -1f;
        public bool ActiveRouteHasProgressSample;
        public bool ReturnStartPending;
        public float ReturnStartRetryAfterGameSeconds = -1f;
        public float ReturnStartDeadlineGameSeconds = -1f;
        // Candidate scalars (supplier re-resolves by business id).
        public string SupplierBusinessId = string.Empty;
        public string SupplierName = string.Empty;
        public string SupplierLocationId = string.Empty;
        public int UnitsPlanned;
        public int UnitPriceCents;
        public int TravelMinutesOneWay;
        public int TotalCostCents;
        public int DeliveryFeeCents;
        public bool UseDelivery;
        public int SettlementMode;
        // P28: the carrier bound at evaluation time for this trip's
        // supplier. Additive; old saves read string.Empty, so a restored
        // trip with a fee and no carrier refuses at the counter instead of
        // charging the fee to the store.
        public string CarrierBusinessInstanceId = string.Empty;
        // Custody batch scalars (null BatchId = no settlement yet).
        public string BatchId = string.Empty;
        public string BatchItemId = string.Empty;
        public int BatchUnits;
        public int BatchUnitPriceCents;
        public string BatchReceiptId = string.Empty;
        public int BatchCustody;
        public string BatchProvenance = string.Empty;
        public int BatchPurchaseDayIndex;
        // Priority U-3: the batch's own package size and seller, as
        // settled — so a restored batch still knows both when its
        // candidate no longer exists (additive; old saves read 0/empty
        // and fall back to 1 / the rebuilt candidate).
        public int BatchHouseholdUnitsPerStoreUnit;
        public string BatchSupplierName = string.Empty;
        /// <summary>
        /// Priority spoilage integration: the batch's attached
        /// perishable condition state at settle time, as a value
        /// snapshot (additive; old saves read the default DTO with
        /// BatchHasPerishableCondition false, which means "no state",
        /// never a default Fresh). A restored batch carries the state
        /// into custody and on to deposit.
        /// </summary>
        public PerishableLotState.SaveDto BatchPerishableCondition;
        public bool BatchHasPerishableCondition;
    }

    /// <summary>
    /// Pass IV (Worker 4): drives planned shopping trips through REAL time.
    /// Shopping legs previously completed via instant RecordWork calls; now each
    /// leg is a real task started at its window with a TaskCompletionDue event,
    /// settlement happens at the counter at arrival, and goods enter household
    /// inventory only on physical receipt (carried) or door delivery (freight
    /// runs its own real minutes via JourneyStageDue). The real ledger flows are
    /// kept: RecordOutflow at settlement, refunds via RecordInflow with
    /// provenance, trade-credit obligations through the single debt authority.
    ///
    /// Event-driven, so 1-minute ticks and 100x jumps produce identical
    /// consequential outcomes. The caller pops the due-event scheduler once per
    /// tick and dispatches to HandleDueEvent (single pop site per tick).
    /// </summary>
    public sealed class ShoppingTripDriver
    {
        // These are town-scale pedestrian errands. A stale or damaged mover
        // estimate must not turn a shopping task into a multi-week lock.
        private const float MaxExpectedShoppingRouteSeconds = 6f * 3600f;
        private const float MaxShoppingRouteNoProgressSeconds = 6f * 3600f;
        private const float MaxShoppingRouteDeadlineSeconds = 24f * 3600f;
        private const float OccupiedReturnRetrySeconds = 15f * 60f;
        private const float OccupiedReturnDeadlineSeconds = 6f * 3600f;

        private enum LegKind
        {
            Outbound = 0,
            InStore = 1,
            Return = 2,
        }

        private sealed class InFlightLeg
        {
            public string TripId = string.Empty;
            public LegKind Kind;
            public bool HasProgressSample;
            public float RemainingSeconds;
            public float LastProgressGameSeconds;
            public float StartedGameSeconds = -1f;
            public float DeadlineGameSeconds = -1f;
            public float NoProgressTimeoutSeconds;
        }

        private readonly HouseholdShoppingLoop loop;
        private readonly Func<int, string, bool> startPhysicalRoute;
        private readonly Func<int, string, bool> hasPhysicallyArrived;
        private readonly Func<int, string, bool?> hasActivePhysicalRoute;
        private readonly Func<int, string, float?> remainingPhysicalRouteSeconds;
        private readonly Action<int, string> cancelPhysicalRoute;
        private readonly Func<string> physicalRouteRefusalProvider;
        private readonly float routeObservationIntervalSeconds;
        private readonly List<PlannedShoppingTrip> activeTrips = new List<PlannedShoppingTrip>();
        private readonly List<PlannedShoppingTrip> returnRecoveries = new List<PlannedShoppingTrip>();
        private readonly Dictionary<string, InFlightLeg> legsByTaskIdString =
            new Dictionary<string, InFlightLeg>(StringComparer.Ordinal);
        private int occupancyReplanDayIndex = -1;
        private readonly HashSet<string> occupancyReplannedNeedsToday =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Plan 16: TendCustomer service tasks awaiting their
        /// completion due event, task-id string to trip id. Separate from
        /// legsByTaskIdString: a service task is not a leg, and its due
        /// event pops the store queue rather than completing a leg.</summary>
        private readonly Dictionary<string, string> serviceTaskIdStringToTripId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>Slice 18 Tier 1: game-seconds watermark for the customer-queue
        /// patience tick (Plan 21 §1.3). Live-only, never persisted; -1 until the
        /// first AdvanceTick call. Whole minutes only, forward motion only, so a
        /// repeated or paused tick can never double-decay.</summary>
        private float lastQueueTickGameSeconds = -1f;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PlannedShoppingTrip> ActiveTrips => activeTrips;

        /// <summary>
        /// Slice 25: true once LoadFromSaveDto has loaded at least one
        /// trip that RehydrateAfterLoad has not yet rehydrated. The
        /// production rehydrate seam (SimulationDrivers.
        /// ApplyRestoredShoppingTrips, called from Rebind) runs only
        /// while this is set, so New Game rebinds and second rebinds
        /// are no-ops. Cleared by RehydrateAfterLoad.
        /// </summary>
        public bool NeedsRehydrateAfterLoad { get; private set; }
        public HouseholdShoppingLoop Loop => loop;
        public bool HasActiveTripForNeed(int needSequence) => HasActiveTripFor(needSequence);
        public bool HasDepartedTripForPerson(int personId)
        {
            foreach (PlannedShoppingTrip trip in activeTrips)
                if (trip != null && trip.ShopperPersonId == personId && trip.IsActive
                    && trip.State != PlannedShoppingTrip.TripState.Planned
                    && trip.State != PlannedShoppingTrip.TripState.RecoveryPlanned)
                    return true;
            return false;
        }

        /// <summary>Returns the durable identity of the active physical trip currently owned by a shopper.</summary>
        public string DepartedTripIdForPerson(int personId)
        {
            foreach (PlannedShoppingTrip trip in activeTrips)
                if (trip != null && trip.ShopperPersonId == personId && trip.IsActive
                    && trip.State != PlannedShoppingTrip.TripState.Planned
                    && trip.State != PlannedShoppingTrip.TripState.RecoveryPlanned)
                    return trip.TripId ?? string.Empty;
            return string.Empty;
        }

        public ShoppingTripDriver(HouseholdShoppingLoop loop,
            Func<int, string, bool> startPhysicalRoute = null,
            Func<int, string, bool> hasPhysicallyArrived = null,
            Func<int, string, bool?> hasActivePhysicalRoute = null,
            Func<int, string, float?> remainingPhysicalRouteSeconds = null,
            Action<int, string> cancelPhysicalRoute = null,
            float routeObservationIntervalSeconds = 900f,
            Func<string> physicalRouteRefusalProvider = null)
        {
            this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
            this.startPhysicalRoute = startPhysicalRoute;
            this.hasPhysicallyArrived = hasPhysicallyArrived;
            this.hasActivePhysicalRoute = hasActivePhysicalRoute;
            this.remainingPhysicalRouteSeconds = remainingPhysicalRouteSeconds;
            this.cancelPhysicalRoute = cancelPhysicalRoute;
            this.physicalRouteRefusalProvider = physicalRouteRefusalProvider;
            this.routeObservationIntervalSeconds = Math.Max(1f, routeObservationIntervalSeconds);
        }

        public bool OwnsDueEvent(DueEvent ev)
        {
            if (ev == null) return false;
            if (ev.Kind == DueEventKind.TaskCompletionDue)
            {
                string payload = ev.PayloadRef ?? string.Empty;
                if (legsByTaskIdString.ContainsKey(payload)
                    || serviceTaskIdStringToTripId.ContainsKey(payload)) return true;

                // Trip/task IDs are the durable ownership record. The lookup
                // dictionaries are runtime indexes and may be absent after a
                // partial restore or a lost callback. Rebuild the index before
                // the generic TaskAuthority dispatcher can complete travel
                // without checking physical arrival.
                return TryRebuildDueEventOwnership(payload);
            }
            return ev.Kind == DueEventKind.JourneyStageDue
                && !string.IsNullOrWhiteSpace(ev.PayloadRef)
                && ev.PayloadRef.StartsWith("delivery:", StringComparison.Ordinal)
                && FindTrip(ev.PayloadRef.Substring("delivery:".Length)) != null;
        }

        private static bool TryResolveCurrentTravelLeg(PlannedShoppingTrip trip, string taskId,
            out LegKind kind, out WorkTask task)
        {
            kind = LegKind.Outbound;
            task = null;
            if (trip == null || string.IsNullOrWhiteSpace(taskId)) return false;
            switch (trip.State)
            {
                case PlannedShoppingTrip.TripState.Outbound:
                    kind = LegKind.Outbound;
                    task = trip.Legs?.Outbound;
                    return trip.OutboundTaskId.IsValid
                        && string.Equals(trip.OutboundTaskId.ToString(), taskId, StringComparison.Ordinal);
                case PlannedShoppingTrip.TripState.InStore:
                    kind = LegKind.InStore;
                    task = trip.Legs?.InStore;
                    return trip.InStoreTaskId.IsValid
                        && string.Equals(trip.InStoreTaskId.ToString(), taskId, StringComparison.Ordinal);
                case PlannedShoppingTrip.TripState.Returning:
                    if (trip.ReturnStartPending) return false;
                    kind = LegKind.Return;
                    task = trip.Legs?.Return;
                    return trip.ReturnTaskId.IsValid
                        && string.Equals(trip.ReturnTaskId.ToString(), taskId, StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static InFlightLeg CreateInFlightLeg(PlannedShoppingTrip trip, LegKind kind) => new()
        {
            TripId = trip?.TripId ?? string.Empty,
            Kind = kind,
            StartedGameSeconds = trip?.ActiveRouteLegKind == (int)kind ? trip.ActiveRouteStartedGameSeconds : -1f,
            DeadlineGameSeconds = trip?.ActiveRouteLegKind == (int)kind ? trip.ActiveRouteDeadlineGameSeconds : -1f,
            LastProgressGameSeconds = trip?.ActiveRouteLegKind == (int)kind ? trip.ActiveRouteLastProgressGameSeconds : -1f,
            RemainingSeconds = trip?.ActiveRouteLegKind == (int)kind ? trip.ActiveRouteLastRemainingSeconds : -1f,
            HasProgressSample = trip?.ActiveRouteLegKind == (int)kind && trip.ActiveRouteHasProgressSample,
        };

        private bool TryRebuildDueEventOwnership(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return false;
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip == null || !trip.IsActive) continue;
                if (trip.State == PlannedShoppingTrip.TripState.AwaitingService
                    && trip.ServiceTaskId.IsValid
                    && string.Equals(trip.ServiceTaskId.ToString(), payload, StringComparison.Ordinal))
                {
                    serviceTaskIdStringToTripId[payload] = trip.TripId;
                    return true;
                }

                if (TryResolveCurrentTravelLeg(trip, payload, out LegKind kind, out _))
                {
                    legsByTaskIdString[payload] = CreateInFlightLeg(trip, kind);
                    return true;
                }
            }

            return false;
        }

        public bool OwnsOpenTask(WorkTask task) => task != null
            && (legsByTaskIdString.ContainsKey(task.TaskId.ToString())
                || serviceTaskIdStringToTripId.ContainsKey(task.TaskId.ToString()));

        /// <summary>
        /// Campaign C (DEF-049): true when a live trip (active, or in paid-return
        /// recovery) still holds this task id as a leg or service task. Leg tasks
        /// complete mid-trip while the trip is still live, and RehydrateAfterLoad
        /// aborts a trip whose leg/service id fails to resolve — so the daily
        /// terminal-task prune must never remove a task this method flags.
        /// Completed trips leave both lists, so their ids stop being flagged the
        /// moment the trip ends.
        /// </summary>
        public bool IsTaskReferencedByLiveTrip(EntityId taskId)
        {
            if (!taskId.IsValid)
            {
                return false;
            }

            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip == null)
                {
                    continue;
                }

                if (trip.OutboundTaskId == taskId || trip.InStoreTaskId == taskId
                    || trip.ReturnTaskId == taskId || trip.ServiceTaskId == taskId)
                {
                    return true;
                }
            }

            foreach (PlannedShoppingTrip trip in returnRecoveries)
            {
                if (trip == null)
                {
                    continue;
                }

                if (trip.OutboundTaskId == taskId || trip.InStoreTaskId == taskId
                    || trip.ReturnTaskId == taskId || trip.ServiceTaskId == taskId)
                {
                    return true;
                }
            }

            return serviceTaskIdStringToTripId.ContainsKey(taskId.ToString());
        }

        /// <summary>
        /// Day boundary: plans a real-time trip for every open need of the
        /// household that does not already have an active trip. Needs nobody
        /// can serve stay open and are reported loudly by the loop.
        /// </summary>
        public void PlanForHousehold(
            int householdId,
            HouseholdNeedRegistry needs,
            int dayIndex,
            ShoppingLoopOptions options,
            List<string> diag,
            IDictionary<string, string> failureByNeed = null)
        {
            diag = diag ?? diagnostics;
            if (needs == null)
            {
                diag.Add("ShoppingTripDriver: no need registry — no trips planned.");
                return;
            }

            foreach (PlannedShoppingTrip recovery in new List<PlannedShoppingTrip>(returnRecoveries))
            {
                if (recovery.HouseholdId != householdId) continue;
                if (recovery.Batch == null || recovery.Batch.Custody != GoodsCustodyState.InHand)
                {
                    returnRecoveries.Remove(recovery);
                    continue;
                }

                string home = recovery.HomeLocationId ?? string.Empty;
                if (hasPhysicallyArrived != null && hasPhysicallyArrived(recovery.ShopperPersonId, home))
                {
                    string receiptError = loop.ReceiveCarriedBatch(recovery.Batch.BatchId, dayIndex, diag);
                    if (receiptError == null)
                    {
                        returnRecoveries.Remove(recovery);
                        diag.Add($"ShoppingTripDriver: recovered paid batch {recovery.Batch.BatchId} at home after an interrupted return; household receipt completed once.");
                    }
                    else diag.Add($"ShoppingTripDriver: paid batch {recovery.Batch.BatchId} remains in custody at home; receipt refused: {receiptError}");
                    continue;
                }

                string seller = recovery.Candidate?.Supplier?.LocationId;
                if (hasPhysicallyArrived != null
                    && hasPhysicallyArrived(recovery.ShopperPersonId, seller ?? string.Empty))
                {
                    if (loop.TryPlanPaidReturnRecovery(recovery, dayIndex, options, diag))
                    {
                        returnRecoveries.Remove(recovery);
                        activeTrips.Add(recovery);
                        continue;
                    }
                }

                if (hasActivePhysicalRoute?.Invoke(recovery.ShopperPersonId, home) == true) continue;
                if (!string.IsNullOrWhiteSpace(home)
                    && startPhysicalRoute?.Invoke(recovery.ShopperPersonId, home) == true)
                {
                    loop.MarkShopperReturningHomeAfterInterruptedTrip(recovery);
                    diag.Add($"ShoppingTripDriver: paid batch {recovery.Batch.BatchId} remains with P{recovery.ShopperPersonId}; restarted physical travel home for later receipt.");
                    continue;
                }

                diag.Add($"ShoppingTripDriver: paid batch {recovery.Batch.BatchId} remains in custody with P{recovery.ShopperPersonId}; no home route is available, so the household need remains blocked without another charge.");
            }

            List<HouseholdPurchasingNeed> open = needs.GetOpenNeeds(householdId);
            foreach (HouseholdPurchasingNeed need in open)
            {
                if (need == null || HasActiveTripFor(need.NeedSequence))
                {
                    continue;
                }

                CarriedGoodsBatch pendingCustody = null;
                foreach (CarriedGoodsBatch batch in loop.ActiveCustodyBatches)
                    if (batch != null && batch.HouseholdId == householdId && batch.Units > 0
                        && batch.Custody != GoodsCustodyState.ReceivedAtHousehold
                        && string.Equals(batch.ItemId, need.ItemId, StringComparison.OrdinalIgnoreCase))
                        { pendingCustody = batch; break; }
                if (pendingCustody != null)
                {
                    string reason = $"Paid batch {pendingCustody.BatchId} for need {need.NeedSequence} remains in {pendingCustody.Custody} custody with P{pendingCustody.HolderPersonId}; physical recovery and household receipt are required.";
                    if (failureByNeed != null) failureByNeed[householdId + ":" + need.ItemId] = reason;
                    diag.Add($"ShoppingTripDriver: H{householdId} {reason}");
                    continue;
                }

                PlannedShoppingTrip trip = loop.PlanTrip(need, dayIndex, options);
                if (trip.State == PlannedShoppingTrip.TripState.Planned)
                {
                    activeTrips.Add(trip);
                    diag.Add($"ShoppingTripDriver: trip {trip.TripId} planned for need {need.NeedSequence} ('{need.ItemId}' x{need.UnitsNeeded}).");
                }
                else
                {
                    if (failureByNeed != null)
                        failureByNeed[householdId + ":" + need.ItemId] = trip.FailureReason;
                    diag.Add($"ShoppingTripDriver: trip planning failed for need {need.NeedSequence}: {trip.FailureReason}");
                }
            }
        }

        private void SweepActivePhysicalRoutes(float nowGameSeconds, int dayIndex,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            if (!IsFiniteNonNegative(nowGameSeconds)) return;
            var snapshot = new List<PlannedShoppingTrip>(activeTrips);
            foreach (PlannedShoppingTrip trip in snapshot)
            {
                if (trip == null || !trip.IsActive) continue;
                if (trip.ReturnStartPending) continue;
                LegKind kind;
                WorkTask leg;
                string destination;
                if (trip.State == PlannedShoppingTrip.TripState.Outbound)
                {
                    kind = LegKind.Outbound;
                    leg = trip.Legs?.Outbound;
                    destination = trip.Candidate?.Supplier?.LocationId ?? string.Empty;
                }
                else if (trip.State == PlannedShoppingTrip.TripState.Returning)
                {
                    kind = LegKind.Return;
                    leg = trip.Legs?.Return;
                    destination = trip.HomeLocationId ?? string.Empty;
                }
                else continue;

                EntityId persistedTaskId = kind == LegKind.Outbound
                    ? trip.OutboundTaskId
                    : trip.ReturnTaskId;
                if ((leg == null && persistedTaskId.IsValid)
                    || (leg != null && !leg.TaskId.IsValid))
                {
                    RecoverStalledRoute(trip, kind, destination,
                        $"physical {kind} journey has a persisted task identity but its live task is missing",
                        dayIndex, dueEvents, diag);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(destination))
                {
                    RecoverStalledRoute(trip, kind, destination,
                        $"physical {kind} journey has no resolved destination",
                        dayIndex, dueEvents, diag);
                    continue;
                }

                InFlightLeg tracking = EnsureRouteWatch(trip, kind, leg, nowGameSeconds);
                ObserveRouteProgress(trip, tracking, destination, nowGameSeconds);

                // Arrival confirmation is optional diagnostic evidence; it is
                // not the liveness authority. If the callback was not rebound
                // after load, the persisted deadline must still release the
                // task instead of allowing an Outbound trip to live forever.
                bool physicallyArrived = hasPhysicallyArrived != null
                    && hasPhysicallyArrived(trip.ShopperPersonId, destination);
                if (leg == null)
                {
                    // Same-location travel has no TaskAuthority leg. It still
                    // requires physical arrival, and the persisted route watch
                    // must recover it if the arrival callback never succeeds.
                    if (physicallyArrived)
                    {
                        if (kind == LegKind.Outbound)
                        {
                            ArriveNow(trip, nowGameSeconds, dayIndex, dueEvents, diag);
                        }
                        else
                        {
                            string receiptError = loop.ReceiveAtHome(trip, dayIndex, diag);
                            if (receiptError != null)
                            {
                                RecoverStalledRoute(trip, kind, destination,
                                    $"same-location return arrived but household receipt failed: {receiptError}",
                                    dayIndex, dueEvents, diag);
                            }
                            else
                            {
                                activeTrips.Remove(trip);
                                ClearRouteWatch(trip);
                            }
                        }
                        continue;
                    }
                }

                float workDueAt = tracking.StartedGameSeconds
                    + Math.Max(1, leg != null ? leg.PlannedMinutes : 0) * 60f;
                if (physicallyArrived && nowGameSeconds >= workDueAt)
                {
                    // A missed event callback cannot strand a completed
                    // physical route. Completion still requires the actual
                    // arrival and elapsed task duration; no work is awarded
                    // merely because this recovery sweep ran.
                    HandleLegDue(new DueEvent("shopping-recovery:" + trip.TripId + ":" + kind,
                        DueEventKind.TaskCompletionDue, nowGameSeconds, leg.TaskId.ToString(), 0),
                        dayIndex, nowGameSeconds, dueEvents, diag);
                    continue;
                }

                bool hardDeadline = nowGameSeconds >= tracking.DeadlineGameSeconds;
                bool stalled = tracking.NoProgressTimeoutSeconds > 0f
                    && nowGameSeconds - tracking.LastProgressGameSeconds >= tracking.NoProgressTimeoutSeconds;
                if (physicallyArrived || (!hardDeadline && !stalled)) continue;

                string reason = hardDeadline
                    ? $"physical {kind} journey exceeded its absolute liveness deadline without confirmed arrival"
                    : $"physical {kind} journey made no measurable progress for {tracking.NoProgressTimeoutSeconds / 3600f:0.##} game hours";
                RecoverStalledRoute(trip, kind, destination, reason, dayIndex, dueEvents, diag);
            }
        }

        private void SweepDeferredEmptyReturns(float nowGameSeconds, int dayIndex,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            var snapshot = new List<PlannedShoppingTrip>(activeTrips);
            foreach (PlannedShoppingTrip trip in snapshot)
            {
                if (trip == null || !trip.ReturnStartPending
                    || trip.State != PlannedShoppingTrip.TripState.Returning
                    || nowGameSeconds < trip.ReturnStartRetryAfterGameSeconds)
                    continue;

                string home = trip.HomeLocationId ?? string.Empty;
                if (nowGameSeconds >= trip.ReturnStartDeadlineGameSeconds)
                {
                    trip.ReturnStartPending = false;
                    RecoverStalledRoute(trip, LegKind.Return, home,
                        "empty-handed return task remained occupied beyond its bounded recovery window",
                        dayIndex, dueEvents, diag);
                    continue;
                }

                string error = loop.BeginReturnLeg(trip, dayIndex, "return-empty-retry", diag);
                if (error != null)
                {
                    if (IsConcurrentTaskStartRefusal(error))
                    {
                        trip.ReturnStartRetryAfterGameSeconds = nowGameSeconds + OccupiedReturnRetrySeconds;
                        continue;
                    }

                    trip.ReturnStartPending = false;
                    RecoverStalledRoute(trip, LegKind.Return, home,
                        $"deferred empty-handed return could not start: {error}",
                        dayIndex, dueEvents, diag);
                    continue;
                }

                trip.ReturnStartPending = false;
                trip.ReturnStartRetryAfterGameSeconds = -1f;
                trip.ReturnStartDeadlineGameSeconds = -1f;
                if (startPhysicalRoute != null && !startPhysicalRoute(trip.ShopperPersonId, home))
                {
                    RecoverStalledRoute(trip, LegKind.Return, home,
                        "deferred empty-handed return task started but its physical route was refused",
                        dayIndex, dueEvents, diag);
                    continue;
                }

                ScheduleLegDue(trip, LegKind.Return, trip.Legs?.Return,
                    nowGameSeconds, dueEvents, diag);
                diag.Add($"ShoppingTripDriver: trip {trip.TripId} resumed its empty-handed return after the blocking task released P{trip.ShopperPersonId}.");
            }
        }

        private void ObserveRouteProgress(PlannedShoppingTrip trip, InFlightLeg tracking,
            string destination, float nowGameSeconds)
        {
            if (trip == null || tracking == null) return;
            float? estimate = remainingPhysicalRouteSeconds?.Invoke(trip.ShopperPersonId, destination);
            if (estimate.HasValue && IsFiniteNonNegative(estimate.Value))
            {
                if (!tracking.HasProgressSample || estimate.Value < tracking.RemainingSeconds - 1f)
                {
                    tracking.HasProgressSample = true;
                    tracking.RemainingSeconds = estimate.Value;
                    tracking.LastProgressGameSeconds = nowGameSeconds;
                }
                else if (tracking.RemainingSeconds < 0f)
                {
                    tracking.RemainingSeconds = estimate.Value;
                }
            }
            PersistRouteWatch(trip, tracking);
        }

        private void RecoverStalledRoute(PlannedShoppingTrip trip, LegKind kind,
            string failedDestination, string reason, int dayIndex,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            if (trip == null || !trip.IsActive) return;
            cancelPhysicalRoute?.Invoke(trip.ShopperPersonId, failedDestination ?? string.Empty);
            WorkTask activeLeg = kind == LegKind.Outbound ? trip.Legs?.Outbound : trip.Legs?.Return;
            EntityId activeTaskId = kind == LegKind.Outbound ? trip.OutboundTaskId : trip.ReturnTaskId;
            if (activeTaskId.IsValid)
            {
                dueEvents?.CancelForPayload(activeTaskId.ToString());
                legsByTaskIdString.Remove(activeTaskId.ToString());
            }
            else if (activeLeg != null && activeLeg.TaskId.IsValid)
            {
                dueEvents?.CancelForPayload(activeLeg.TaskId.ToString());
                legsByTaskIdString.Remove(activeLeg.TaskId.ToString());
            }
            else
            {
                string arrivalCheckPayload = $"arrival-check:{trip.TripId}:{kind}";
                dueEvents?.CancelForPayload(arrivalCheckPayload);
                legsByTaskIdString.Remove(arrivalCheckPayload);
            }

            if (kind == LegKind.Outbound && trip.Batch == null)
                loop.RecordTripRefusal(trip, reason, dayIndex);
            loop.AbortTrip(trip, reason, dayIndex, diag);
            activeTrips.Remove(trip);

            if (trip.Batch != null && trip.Batch.Custody == GoodsCustodyState.InHand
                && !returnRecoveries.Contains(trip))
                returnRecoveries.Add(trip);

            string home = trip.HomeLocationId ?? string.Empty;
            loop.MarkShopperReturningHomeAfterInterruptedTrip(trip);
            if (!string.IsNullOrWhiteSpace(home)
                && (hasPhysicallyArrived == null || !hasPhysicallyArrived(trip.ShopperPersonId, home)))
            {
                bool alreadyReturning = hasActivePhysicalRoute?.Invoke(trip.ShopperPersonId, home) == true;
                bool restarted = alreadyReturning || startPhysicalRoute?.Invoke(trip.ShopperPersonId, home) == true;
                diag.Add(restarted
                    ? $"ShoppingTripDriver: trip {trip.TripId} released its failed {kind} task and reservations; P{trip.ShopperPersonId} is physically returning home. Need remains open."
                    : $"ShoppingTripDriver: trip {trip.TripId} released its failed {kind} task and reservations; no physical home route could start for P{trip.ShopperPersonId}. Need and any paid cargo remain outstanding.");
            }
            else
            {
                diag.Add($"ShoppingTripDriver: trip {trip.TripId} released its failed {kind} task and reservations; P{trip.ShopperPersonId} is already home. Need and any paid cargo remain outstanding.");
            }
            ClearRouteWatch(trip);
        }

        private static void ClearRouteWatch(PlannedShoppingTrip trip)
        {
            if (trip == null) return;
            trip.ActiveRouteLegKind = -1;
            trip.ActiveRouteStartedGameSeconds = -1f;
            trip.ActiveRouteDeadlineGameSeconds = -1f;
            trip.ActiveRouteLastProgressGameSeconds = -1f;
            trip.ActiveRouteLastRemainingSeconds = -1f;
            trip.ActiveRouteHasProgressSample = false;
        }

        /// <summary>
        /// Tick advancement: departs trips whose window has arrived and
        /// immediately arrives zero-travel trips. Due-event handling does the
        /// rest. Does NOT pop the scheduler (single pop site per tick).
        /// </summary>
        public void AdvanceTick(
            float currentGameSeconds,
            int dayIndex,
            IDueEventScheduler dueEvents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dueEvents == null)
            {
                return;
            }

            if (occupancyReplanDayIndex != dayIndex)
            {
                occupancyReplanDayIndex = dayIndex;
                occupancyReplannedNeedsToday.Clear();
            }

            int minuteOfDay = MinuteOfDay(currentGameSeconds);
            // Snapshot: HandleDueEvent is not called here, but trips may abort.
            var snapshot = new List<PlannedShoppingTrip>(activeTrips);
            foreach (PlannedShoppingTrip trip in snapshot)
            {
                if (trip == null || (trip.State != PlannedShoppingTrip.TripState.Planned
                    && trip.State != PlannedShoppingTrip.TripState.RecoveryPlanned))
                {
                    continue;
                }

                if (trip.DayIndex < dayIndex)
                {
                    if (trip.State == PlannedShoppingTrip.TripState.RecoveryPlanned)
                    {
                        loop.AbortTrip(trip, "Missed paid-goods recovery departure.", dayIndex, diag);
                        QueuePaidReturnRecovery(trip);
                    }
                    else loop.ExpireUndepartedTrip(trip, dayIndex, diag);
                    activeTrips.Remove(trip);
                    continue;
                }

                if (trip.DayIndex != dayIndex || minuteOfDay < trip.DepartureMinuteOfDay)
                {
                    continue;
                }

                if (trip.State == PlannedShoppingTrip.TripState.RecoveryPlanned)
                {
                    string recoveryError = loop.BeginReturnLeg(trip, dayIndex, "paid-goods recovery", diag);
                    if (recoveryError == null && startPhysicalRoute != null
                        && !startPhysicalRoute(trip.ShopperPersonId, trip.HomeLocationId))
                        recoveryError = $"No physical route reaches home '{trip.HomeLocationId}'.";
                    if (recoveryError != null)
                    {
                        loop.AbortTrip(trip, recoveryError, dayIndex, diag);
                        activeTrips.Remove(trip);
                        QueuePaidReturnRecovery(trip);
                    }
                    else ScheduleLegDue(trip, LegKind.Return, trip.Legs.Return, currentGameSeconds, dueEvents, diag);
                    continue;
                }

                string storeLocation = trip.Candidate?.Supplier?.LocationId ?? string.Empty;
                string error = loop.BeginOutboundLeg(trip, dayIndex, diag);
                bool physicalRouteAttempted = false;
                string physicalRouteRefusal = string.Empty;
                if (error == null && startPhysicalRoute != null)
                {
                    physicalRouteAttempted = true;
                    if (!startPhysicalRoute(trip.ShopperPersonId, storeLocation))
                    {
                        physicalRouteRefusal = physicalRouteRefusalProvider?.Invoke() ?? string.Empty;
                        error = $"No physical route reaches seller location '{storeLocation}'"
                            + (string.IsNullOrWhiteSpace(physicalRouteRefusal) ? "." : $": {physicalRouteRefusal}.");
                    }
                }
                if (error != null)
                {
                    string needKey = trip.HouseholdId + ":" + trip.Need?.NeedSequence;
                    bool taskStartConflict = trip.State == PlannedShoppingTrip.TripState.Planned
                        && IsConcurrentTaskStartRefusal(error);
                    bool movementOwnershipConflict = physicalRouteAttempted
                        && IsPhysicalMovementOwnershipRefusal(physicalRouteRefusal);
                    if ((taskStartConflict || movementOwnershipConflict)
                        && occupancyReplannedNeedsToday.Add(needKey))
                    {
                        // TaskAuthority can refuse before the trip starts, or
                        // a separate physical task can claim the mover after
                        // scheduling but before this route starts. Cancel the
                        // shopping legs and plan once against live schedule
                        // and route ownership. A route refusal occurs before
                        // travel progress, so cancellation awards no work,
                        // purchase, or need fulfillment.
                        loop.AbortTrip(trip, error, dayIndex, diag);
                        activeTrips.Remove(trip);
                        PlannedShoppingTrip replacement = loop.PlanTrip(
                            trip.Need, dayIndex, trip.Options ?? new ShoppingLoopOptions());
                        if (replacement.State == PlannedShoppingTrip.TripState.Planned)
                        {
                            activeTrips.Add(replacement);
                            diag.Add($"ShoppingTripDriver: need {needKey} was replanned once after a concurrent task occupied P{trip.ShopperPersonId}; P{replacement.ShopperPersonId} is now responsible.");
                        }
                        else
                        {
                            diag.Add($"ShoppingTripDriver: need {needKey} remains open after its one same-day occupancy replan failed: {replacement.FailureReason}");
                        }
                        continue;
                    }

                    loop.AbortTrip(trip, error, dayIndex, diag);
                    activeTrips.Remove(trip);
                    continue;
                }

                if (trip.Legs != null && trip.Legs.Outbound != null)
                {
                    ScheduleLegDue(trip, LegKind.Outbound, trip.Legs.Outbound, currentGameSeconds, dueEvents, diag);
                }
                else
                {
                    // A zero-duration logical leg does not prove that the
                    // visible actor is at the counter. Try the arrival gate;
                    // if a route is still active, poll it on simulated time.
                    ArriveNow(trip, currentGameSeconds, dayIndex, dueEvents, diag);
                }
            }

            SweepDeferredEmptyReturns(currentGameSeconds, dayIndex, dueEvents, diag);
            SweepActivePhysicalRoutes(currentGameSeconds, dayIndex, dueEvents, diag);
            SweepAwaitingCustomerService(currentGameSeconds, dayIndex, dueEvents, diag);

            // Slice 18 Tier 1: patience tick for the store queues of trips
            // currently waiting at the store (AtStore or InStore). Elapsed is
            // watermarked whole minutes from forward motion only — the
            // signature does not change and the decay is a function of total
            // elapsed game minutes, never of tick count.
            if (lastQueueTickGameSeconds < 0f)
            {
                lastQueueTickGameSeconds = currentGameSeconds;
            }
            else
            {
                float elapsedSeconds = currentGameSeconds - lastQueueTickGameSeconds;
                if (elapsedSeconds > 0f)
                {
                    int minutes = (int)(elapsedSeconds / 60f);
                    if (minutes > 0)
                    {
                        float previousQueueTickGameSeconds = lastQueueTickGameSeconds;
                        lastQueueTickGameSeconds += minutes * 60f;
                        TickCustomerQueues(dayIndex, currentGameSeconds,
                            previousQueueTickGameSeconds, dueEvents, diag);
                    }
                }
            }
        }

        private void SweepAwaitingCustomerService(float nowGameSeconds, int dayIndex,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            if (!IsFiniteNonNegative(nowGameSeconds) || dueEvents == null) return;
            foreach (PlannedShoppingTrip trip in new List<PlannedShoppingTrip>(activeTrips))
            {
                if (trip == null || trip.State != PlannedShoppingTrip.TripState.AwaitingService) continue;
                if (trip.ServiceTaskId.IsValid)
                {
                    WorkTask serviceTask = loop.FindServiceTask(trip.ServiceTaskId);
                    if (serviceTask != null && serviceTask.Status == TaskStatus.InProgress) continue;
                    if (serviceTask != null && serviceTask.Status == TaskStatus.Complete)
                    {
                        serviceTaskIdStringToTripId.Remove(trip.ServiceTaskId.ToString());
                        CustomerQueue completedQueue = loop.ResolveCustomerQueue?.Invoke(
                            trip.Candidate?.Supplier?.SupplierBusinessId ?? string.Empty);
                        string servedRef = completedQueue?.ServeNext();
                        string expectedRef = $"shop:{trip.HouseholdId}:{trip.Need?.NeedSequence ?? -1}";
                        if (!string.Equals(servedRef, expectedRef, StringComparison.Ordinal))
                        {
                            ReturnEmpty(trip, $"Completed clerk task did not match the queue head ({servedRef ?? "empty"}).",
                                "counter service identity mismatch", dayIndex, nowGameSeconds, dueEvents, diag);
                            continue;
                        }
                        string settlementError = loop.SettleAfterService(trip, dayIndex, diag);
                        if (settlementError != null)
                        {
                            ReturnEmpty(trip, settlementError, "settle after recovered service", dayIndex,
                                nowGameSeconds, dueEvents, diag);
                            continue;
                        }
                        StartInStoreLeg(trip, dayIndex, nowGameSeconds, dueEvents, diag);
                        continue;
                    }
                    serviceTaskIdStringToTripId.Remove(trip.ServiceTaskId.ToString());
                    trip.ServiceTaskId = default;
                    trip.ServiceTaskPlannedMinutes = 0;
                }

                if (trip.ServiceWaitDeadlineGameSeconds >= 0f
                    && nowGameSeconds >= trip.ServiceWaitDeadlineGameSeconds)
                {
                    ReturnEmpty(trip, "No physically present clerk completed service within the customer's finite waiting window.",
                        "counter service expired", dayIndex, nowGameSeconds, dueEvents, diag);
                    continue;
                }

                if (loop.TryStartCustomerService(trip, dayIndex, null, out _))
                {
                    string taskId = trip.ServiceTaskId.ToString();
                    dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                        nowGameSeconds + Math.Max(1, trip.ServiceTaskPlannedMinutes) * 60f,
                        taskId, out string dueId);
                    serviceTaskIdStringToTripId[taskId] = trip.TripId;
                    diag.Add($"ShoppingTripDriver: queued trip {trip.TripId} began TendCustomer task {taskId}; due {dueId}.");
                }
            }
        }

        /// <summary>
        /// Campaign C (DEF-073): re-arm the queue patience tick at the
        /// restored clock after a load, so decay resumes from the restored
        /// time. The watermark is live-only: on a fresh scene it starts at
        /// -1 and the first post-load tick sets it with no decay (up to one
        /// short-tick interval, 15 game-min, skipped), and on an in-session
        /// load the driver is reused so the watermark stays ahead of the
        /// rewound clock and decay pauses until the clock replays past it.
        /// The patience values themselves are restored from the queue DTO;
        /// this only re-arms the decay clock. Called from
        /// SimulationDrivers.Rebind after the driver is (re)created; on a
        /// New Game the clock is at the scenario start so the re-arm is a
        /// no-op.
        /// </summary>
        public void RearmQueueTickWatermark(float restoredGameSeconds)
        {
            lastQueueTickGameSeconds = restoredGameSeconds;
        }

        /// <summary>
        /// Handles one due event for a trip leg (TaskCompletionDue) or an
        /// arranged delivery (JourneyStageDue). Records real worked minutes on
        /// each finished leg — travel and shopping occupy real budgeted time.
        /// </summary>
        public void HandleDueEvent(
            DueEvent ev,
            int dayIndex,
            float currentGameSeconds,
            IDueEventScheduler dueEvents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ev == null || dueEvents == null)
            {
                return;
            }

            if (ev.Kind == DueEventKind.TaskCompletionDue)
            {
                // OwnsDueEvent is the normal dispatcher gate. Rebuild here too
                // so direct recovery callers cannot accidentally fall through
                // to generic task completion after a lost runtime index.
                if (!legsByTaskIdString.ContainsKey(ev.PayloadRef ?? string.Empty)
                    && !serviceTaskIdStringToTripId.ContainsKey(ev.PayloadRef ?? string.Empty))
                {
                    TryRebuildDueEventOwnership(ev.PayloadRef ?? string.Empty);
                }
                HandleLegDue(ev, dayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }

            if (ev.Kind == DueEventKind.JourneyStageDue &&
                ev.PayloadRef != null && ev.PayloadRef.StartsWith("delivery:", StringComparison.Ordinal))
            {
                PlannedShoppingTrip trip = FindTrip(ev.PayloadRef.Substring("delivery:".Length));
                if (trip != null && trip.IsActive)
                {
                    string deliveryError = loop.ReceiveDelivery(trip, dayIndex, diag);
                    if (deliveryError != null)
                    {
                        diag.Add("ShoppingTripDriver: " + deliveryError);
                    }
                }
            }
        }

        // ---------- internals ----------

        private void HandleLegDue(
            DueEvent ev, int dayIndex, float currentGameSeconds,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            // Plan 16: TendCustomer service completion. Checked FIRST, before
            // the legs lookup below: a service task is not a leg, and its due
            // event pops the store queue rather than completing a leg.
            // (Coordinator review correction: the legs lookup is in this
            // method, not in HandleDueEvent, which only delegates here; use
            // return, never break.)
            if (serviceTaskIdStringToTripId.TryGetValue(ev.PayloadRef ?? string.Empty, out string serviceTripId))
            {
                serviceTaskIdStringToTripId.Remove(ev.PayloadRef);
                PlannedShoppingTrip serviceTrip = FindTrip(serviceTripId);
                if (serviceTrip == null)
                {
                    diag.Add($"ShoppingTripDriver: service task {ev.PayloadRef} has no trip; dropped.");
                    return;
                }
                WorkTask serviceTask = loop.FindServiceTask(serviceTrip.ServiceTaskId);
                CustomerQueue serviceQueue = loop.ResolveCustomerQueue?.Invoke(serviceTrip.Candidate?.Supplier?.SupplierBusinessId ?? string.Empty);
                string expectedCustomerRef = $"shop:{serviceTrip.HouseholdId}:{serviceTrip.Need?.NeedSequence ?? -1}";
                if (serviceQueue == null || serviceQueue.Waiting.Count == 0
                    || !string.Equals(serviceQueue.Waiting[0].CustomerRef, expectedCustomerRef, StringComparison.Ordinal))
                {
                    loop.CancelServiceTask(serviceTask, dayIndex, diag);
                    ReturnEmpty(serviceTrip, "The customer was no longer at the head of the store queue when service completed.",
                        "counter queue ownership changed", dayIndex, currentGameSeconds, dueEvents, diag);
                    return;
                }

                string serviceError = serviceTask != null && serviceTask.Status == TaskStatus.Complete
                    ? null
                    : loop.CompleteServiceTask(serviceTask, dayIndex, diag);
                if (serviceError != null)
                {
                    loop.CancelServiceTask(serviceTask, dayIndex, diag);
                    ReturnEmpty(serviceTrip, $"TendCustomer did not complete: {serviceError}",
                        "counter service failed", dayIndex, currentGameSeconds, dueEvents, diag);
                    return;
                }

                string servedRef = serviceQueue.ServeNext();
                if (!string.Equals(servedRef, expectedCustomerRef, StringComparison.Ordinal))
                {
                    ReturnEmpty(serviceTrip, $"Counter served unexpected queue customer '{servedRef ?? "empty"}'.",
                        "counter service identity mismatch", dayIndex, currentGameSeconds, dueEvents, diag);
                    return;
                }
                diag.Add($"ShoppingTripDriver: TendCustomer service complete for trip {serviceTripId}; served '{servedRef}'.");

                // Slice 17: the service-completion branch is the settle
                // trigger — settlement runs here, then the in-store leg.
                string settleError = loop.SettleAfterService(serviceTrip, dayIndex, diag);
                if (settleError != null)
                {
                    ReturnEmpty(serviceTrip, settleError, "settle after service failed", dayIndex, currentGameSeconds, dueEvents, diag);
                    return;
                }

                StartInStoreLeg(serviceTrip, dayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }

            InFlightLeg leg;
            if (!legsByTaskIdString.TryGetValue(ev.PayloadRef ?? string.Empty, out leg) || leg == null)
            {
                return; // not one of ours
            }

            PlannedShoppingTrip trip = FindTrip(leg.TripId);
            if (trip == null || !trip.IsActive)
            {
                legsByTaskIdString.Remove(ev.PayloadRef);
                return;
            }

            string expectedLocation = leg.Kind == LegKind.Outbound
                ? trip.Candidate?.Supplier?.LocationId
                : leg.Kind == LegKind.Return ? trip.HomeLocationId : null;
            if (!string.IsNullOrWhiteSpace(expectedLocation)
                && hasPhysicallyArrived != null
                && !hasPhysicallyArrived(trip.ShopperPersonId, expectedLocation))
            {
                ScheduleArrivalRetry(trip, leg.Kind, dayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }
            legsByTaskIdString.Remove(ev.PayloadRef);

            switch (leg.Kind)
            {
                case LegKind.Outbound:
                    CompleteLeg(trip.Legs != null ? trip.Legs.Outbound : null, trip, dayIndex, diag);
                    if (trip.State == PlannedShoppingTrip.TripState.Failed)
                    {
                        activeTrips.Remove(trip);
                        break;
                    }

                    ArriveNow(trip, currentGameSeconds, dayIndex, dueEvents, diag);
                    if (!trip.IsActive)
                    {
                        activeTrips.Remove(trip);
                    }

                    break;

                case LegKind.InStore:
                    CompleteLeg(trip.Legs != null ? trip.Legs.InStore : null, trip, dayIndex, diag);
                    if (trip.State == PlannedShoppingTrip.TripState.Failed)
                    {
                        activeTrips.Remove(trip);
                        // Campaign C (DEF-022): settlement already ran, so the
                        // shopper may be holding paid goods — queue the paid
                        // return recovery instead of stranding the batch in
                        // loop custody. No-op when nothing was bought.
                        QueuePaidReturnRecovery(trip);
                        break;
                    }

                    // Slice 18 Tier 1: the leg is done and the customer leaves the
                    // store — remove the queue entry silently. A served customer
                    // was already popped by ServeNext, so this is a no-op for
                    // them; an unserved one must not linger as a ghost.
                    RemoveStaleQueueEntry(trip);
                    {
                        string homeLocation = trip.HomeLocationId ?? string.Empty;
                        string error = loop.BeginReturnLeg(trip, dayIndex, "return", diag);
                        if (error == null && startPhysicalRoute != null
                            && !startPhysicalRoute(trip.ShopperPersonId, homeLocation))
                            error = $"No physical route reaches home '{homeLocation}'.";
                        if (error != null)
                        {
                            loop.AbortTrip(trip, error, dayIndex, diag);
                            activeTrips.Remove(trip);
                            QueuePaidReturnRecovery(trip);
                            break;
                        }

                        ScheduleLegDue(trip, LegKind.Return,
                            trip.Legs != null ? trip.Legs.Return : null,
                            currentGameSeconds, dueEvents, diag);
                    }

                    break;

                case LegKind.Return:
                    CompleteLeg(trip.Legs != null ? trip.Legs.Return : null, trip, dayIndex, diag);
                    {
                        string error = loop.ReceiveAtHome(trip, dayIndex, diag);
                        if (error != null)
                        {
                            loop.AbortTrip(trip, error, dayIndex, diag);
                        }
                        else if (trip.Batch != null && trip.Batch.Custody == GoodsCustodyState.ReceivedAtHousehold)
                        {
                            trip.FailureReason = string.Empty;
                            trip.Result.FailureReason = string.Empty;
                        }

                        activeTrips.Remove(trip);
                    }

                    break;
            }
        }

        /// <summary>Arrival at the store. Slice 17: when a TendCustomer
        /// service task was started, settlement waits for service completion
        /// — the in-store leg starts from the service-completion branch,
        /// after SettleAfterService. Otherwise (no service task) settle at
        /// the counter here and start the in-store leg, as before.</summary>
        private void ArriveNow(
            PlannedShoppingTrip trip, float currentGameSeconds, int dayIndex,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            string storeLocation = trip.Candidate?.Supplier?.LocationId ?? string.Empty;
            if (hasPhysicallyArrived != null
                && !hasPhysicallyArrived(trip.ShopperPersonId, storeLocation))
            {
                ScheduleArrivalRetry(trip, LegKind.Outbound, dayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }
            string error = loop.ArriveAtStoreAndSettle(
                trip, dayIndex, MinuteOfDay(currentGameSeconds), diag, currentGameSeconds);
            if (error != null)
            {
                // Closed store or failed settlement: walk home empty-handed
                // through the REAL return leg — the need stays open.
                ReturnEmpty(trip, error, "arrival failed", dayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }

            // Plan 16: the service task completes on its due event, like a leg.
            if (trip.ServiceTaskId.IsValid)
            {
                if (trip.ServiceWaitDeadlineGameSeconds < 0f)
                    trip.ServiceWaitDeadlineGameSeconds = currentGameSeconds
                        + CustomerQueue.DefaultPatienceMinutes * 60f;
                string serviceTaskIdString = trip.ServiceTaskId.ToString();
                string serviceDueId;
                dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                    currentGameSeconds + Math.Max(1, trip.ServiceTaskPlannedMinutes) * 60f,
                    serviceTaskIdString, out serviceDueId);
                serviceTaskIdStringToTripId[serviceTaskIdString] = trip.TripId;
                diag.Add($"ShoppingTripDriver: TendCustomer service due in {trip.ServiceTaskPlannedMinutes} min ({serviceDueId}).");
                // Slice 17: settlement waits for the service — the leg
                // starts from the service-completion branch, after
                // SettleAfterService. Do not start it here.
                return;
            }

            if (trip.State == PlannedShoppingTrip.TripState.AwaitingService)
            {
                if (trip.ServiceWaitDeadlineGameSeconds < 0f)
                    trip.ServiceWaitDeadlineGameSeconds = currentGameSeconds
                        + CustomerQueue.DefaultPatienceMinutes * 60f;
                // No clerk is physically present yet. Keep the customer in
                // the finite patience queue; never settle on arrival.
                return;
            }

            StartInStoreLeg(trip, dayIndex, currentGameSeconds, dueEvents, diag);
        }

        /// <summary>Slice 17: shared return-empty path — the ArriveNow error
        /// path and the settle-after-service failure. Cancels the unstarted
        /// in-store leg, records the refusal (need stays open), removes the
        /// stale queue entry, and walks home through the real return leg.
        /// The context names the failure honestly in the diag line.</summary>
        private void ReturnEmpty(
            PlannedShoppingTrip trip, string error, string context, int dayIndex,
            float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            loop.CancelUnstartedInStoreLeg(trip, dayIndex, diag);
            loop.RecordTripRefusal(trip, error, dayIndex);
            // Slice 18 Tier 1: the trip is going home empty-handed, but its
            // queue entry was made before settlement — remove it silently
            // (no lost sale: a settle failure is not a balk).
            RemoveStaleQueueEntry(trip);
            diag.Add($"ShoppingTripDriver: trip {trip.TripId} need {trip.Need?.NeedSequence}: {context} ({error}) — returning empty-handed.");
            trip.State = PlannedShoppingTrip.TripState.Returning;
            string returnError = loop.BeginReturnLeg(trip, dayIndex, "return-empty", diag);
            if (returnError != null && IsConcurrentTaskStartRefusal(returnError)
                && trip.Legs?.Return != null && trip.Legs.Return.IsOpen)
            {
                trip.ReturnStartPending = true;
                trip.ReturnStartRetryAfterGameSeconds = currentGameSeconds + OccupiedReturnRetrySeconds;
                trip.ReturnStartDeadlineGameSeconds = currentGameSeconds + OccupiedReturnDeadlineSeconds;
                diag.Add($"ShoppingTripDriver: trip {trip.TripId} return is deferred because P{trip.ShopperPersonId} is occupied; the open need stays outstanding and the physical return will retry for up to 6 game hours.");
                return;
            }
            if (returnError == null && startPhysicalRoute != null
                && !startPhysicalRoute(trip.ShopperPersonId, trip.HomeLocationId ?? string.Empty))
                returnError = $"No physical route reaches home '{trip.HomeLocationId}'.";
            if (returnError != null)
            {
                loop.AbortTrip(trip, returnError, dayIndex, diag);
                activeTrips.Remove(trip);
                return;
            }

            ScheduleLegDue(trip, LegKind.Return,
                trip.Legs != null ? trip.Legs.Return : null,
                currentGameSeconds, dueEvents, diag);
        }

        /// <summary>Slice 17: starts the in-store leg exactly as ArriveNow
        /// did before the gate — BeginInStoreLeg (abort + remove on error),
        /// the delivery due, then the InStore leg due. Returns false when
        /// the trip was aborted. Shared by ArriveNow (no-service path) and
        /// the service-completion branch (after SettleAfterService).</summary>
        private bool StartInStoreLeg(
            PlannedShoppingTrip trip, int dayIndex, float currentGameSeconds,
            IDueEventScheduler dueEvents, List<string> diag)
        {
            string inStoreError = loop.BeginInStoreLeg(trip, dayIndex, diag);
            if (inStoreError != null)
            {
                loop.AbortTrip(trip, inStoreError, dayIndex, diag);
                activeTrips.Remove(trip);
                // Campaign C (DEF-022): settlement already ran, so the shopper
                // is holding paid goods at the store — queue the paid return
                // recovery instead of stranding the batch in loop custody.
                // No-op when nothing was bought.
                QueuePaidReturnRecovery(trip);
                return false;
            }

            if (trip.DeliveryMinutes > 0)
            {
                string dueId;
                dueEvents.Schedule(DueEventKind.JourneyStageDue,
                    currentGameSeconds + trip.DeliveryMinutes * 60f,
                    "delivery:" + trip.TripId, out dueId);
                string carrierId = trip.Candidate != null ? trip.Candidate.CarrierBusinessInstanceId ?? string.Empty : string.Empty;
                string carrierSuffix = string.IsNullOrEmpty(carrierId) ? string.Empty : $" via carrier '{carrierId}'";
                diag.Add($"ShoppingTripDriver: delivery freight scheduled ({trip.DeliveryMinutes} min){carrierSuffix}.");
            }

            ScheduleLegDue(trip, LegKind.InStore,
                trip.Legs != null ? trip.Legs.InStore : null,
                currentGameSeconds, dueEvents, diag);
            return true;
        }

        /// <summary>Slice 18b: ticks the store queue of every supplier with
        /// a trip currently waiting at the store. The method first sweeps
        /// settled (InStore/AtStore) entries silently, so Tick can never
        /// write a QueueBalk lost sale for a customer who already paid;
        /// then it ticks the queue. A balk now means a genuinely unsettled
        /// (AwaitingService) customer, who is released via ReturnEmpty with
        /// the QueueBalk standing; the (pre-Slice 17) settled-balk case no
        /// longer exists. A balked customer whose trip cannot be found, or
        /// whose trip is not awaiting service, is skipped with a diag line
        /// and the trip is unaffected.</summary>
        private void TickCustomerQueues(int dayIndex, float currentGameSeconds,
            float previousGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            var businessIds = new List<string>();
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip == null
                    || (trip.State != PlannedShoppingTrip.TripState.AtStore
                        && trip.State != PlannedShoppingTrip.TripState.InStore
                        && trip.State != PlannedShoppingTrip.TripState.AwaitingService))
                {
                    continue;
                }

                string businessId = trip.Candidate?.Supplier?.SupplierBusinessId ?? string.Empty;
                if (!string.IsNullOrEmpty(businessId) && !businessIds.Contains(businessId))
                {
                    businessIds.Add(businessId);
                }
            }

            foreach (string businessId in businessIds)
            {
                CustomerQueue queue = loop.ResolveCustomerQueue?.Invoke(businessId);
                if (queue == null)
                {
                    continue;
                }

                // Slice 18b: sweep settled (InStore/AtStore) entries silently
                // BEFORE Tick — the lost sale is written inside Tick, so a
                // settled entry must be gone before it runs.
                foreach (PlannedShoppingTrip trip in activeTrips)
                {
                    if (trip == null) continue;
                    if (trip.Candidate?.Supplier?.SupplierBusinessId != businessId) continue;
                    if (trip.State == PlannedShoppingTrip.TripState.InStore
                        || trip.State == PlannedShoppingTrip.TripState.AtStore)
                    {
                        RemoveStaleQueueEntry(trip);
                    }
                }

                queue.TickAt(currentGameSeconds, previousGameSeconds, dayIndex);
                foreach (WaitingCustomer balked in queue.TakeBalked())
                {
                    ReleaseBalkedCustomer(balked, dayIndex, currentGameSeconds, dueEvents, diag);
                }
            }
        }

        /// <summary>Slice 18b: releases one balked customer. Only an
        /// AwaitingService trip that has not settled is released: the
        /// service task is cancelled, the stale service due is deregistered
        /// from the driver's map, and the trip returns empty-handed through
        /// ReturnEmpty (need stays open; the QueueBalk lost sale Tick
        /// already recorded stands). A balked customer with no active trip,
        /// or whose trip is settled or not awaiting service, is skipped
        /// with a diag line — the sweep should have removed settled entries
        /// before Tick, but this guard is what makes the release safe if it
        /// ever misses.</summary>
        private void ReleaseBalkedCustomer(WaitingCustomer balked, int dayIndex, float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            int waited = balked.PatienceMinutesInitial - balked.PatienceMinutesRemaining;
            PlannedShoppingTrip trip = FindTripByRef(balked.CustomerRef);
            if (trip == null)
            {
                diag.Add($"ShoppingTripDriver: customer {balked.CustomerRef} balked after {waited} min waiting; no active trip (stale entry), nothing released.");
                return;
            }

            if (trip.State != PlannedShoppingTrip.TripState.AwaitingService || trip.UnitsSettled != 0 || trip.Batch != null)
            {
                diag.Add($"ShoppingTripDriver: customer {balked.CustomerRef} balked after {waited} min waiting; trip {trip.TripId} is {trip.State}, entry removed, trip unaffected.");
                return;
            }

            WorkTask serviceTask = loop.FindServiceTask(trip.ServiceTaskId);
            string cancelError = loop.CancelServiceTask(serviceTask, dayIndex, diag);
            if (cancelError != null)
            {
                diag.Add($"ShoppingTripDriver: {cancelError}");
            }

            // Deregister unconditionally, even when the cancel errored: the
            // stale service due must not reach the service-completion branch
            // for a trip that has walked home (note 84 Finding 1 — the
            // CancelTask due-scheduler seam is not installed in production,
            // so this deregistration is load-bearing, not belt-and-braces).
            serviceTaskIdStringToTripId.Remove(trip.ServiceTaskId.ToString());
            ReturnEmpty(trip, $"Balked: patience exhausted after waiting {waited} min.", "balked", dayIndex, currentGameSeconds, dueEvents, diag);
        }

        private static bool IsConcurrentTaskStartRefusal(string error) =>
            !string.IsNullOrWhiteSpace(error)
            && error.IndexOf("already occupied by task", StringComparison.Ordinal) >= 0;

        internal static bool IsPhysicalMovementOwnershipRefusal(string reason) =>
            !string.IsNullOrWhiteSpace(reason)
            && (reason.IndexOf("carrying freight to receiving building", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("active task or scheduled route", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("active task-directed route", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("approaching an assigned freight wagon", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("aboard a wagon", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Slice 18b: finds the active trip for a queue customer
        /// ref, beside FindTrip (by TripId). The release needs a by-ref
        /// lookup because TakeBalked yields CustomerRefs, not TripIds.</summary>
        private PlannedShoppingTrip FindTripByRef(string customerRef)
        {
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip != null && string.Equals(HouseholdShoppingLoop.ServiceCustomerRefFor(trip), customerRef, StringComparison.Ordinal))
                {
                    return trip;
                }
            }

            return null;
        }

        /// <summary>Slice 18 Tier 1: removes a trip's queue entry silently (no
        /// lost sale, no refusal) when the trip exits the store. A ref that was
        /// never enqueued — or was already served or balked — is a no-op.</summary>
        private void RemoveStaleQueueEntry(PlannedShoppingTrip trip)
        {
            if (trip == null)
            {
                return;
            }

            CustomerQueue queue = loop.ResolveCustomerQueue?.Invoke(trip.Candidate?.Supplier?.SupplierBusinessId ?? string.Empty);
            if (queue != null) queue.TryRemove(HouseholdShoppingLoop.ServiceCustomerRefFor(trip));
        }

        private void CompleteLeg(WorkTask leg, PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (leg == null) return; // Same-location legs have no task to complete.
            string error = loop.CompleteLegTask(leg, dayIndex, diag);
            if (error != null)
            {
                loop.AbortTrip(trip, error, dayIndex, diag);
            }
        }

        private void ScheduleLegDue(
            PlannedShoppingTrip trip, LegKind kind, WorkTask leg,
            float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            if (leg == null)
            {
                ScheduleArrivalRetry(trip, kind, trip.DayIndex, currentGameSeconds, dueEvents, diag);
                return;
            }

            string taskIdString = leg.TaskId.ToString();
            string dueId;
            InFlightLeg tracking = EnsureRouteWatch(trip, kind, leg, currentGameSeconds);
            // A duplicate callback may observe a leg already completed by
            // the task authority. Preserve that terminal task state and let
            // the usual physical-arrival gate advance the trip immediately;
            // never award another planned duration for a completed leg.
            float dueGameSeconds = leg.Status == TaskStatus.Complete
                ? currentGameSeconds
                : currentGameSeconds + Math.Max(1, leg.PlannedMinutes) * 60f;
            dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                dueGameSeconds,
                taskIdString, out dueId);
            legsByTaskIdString[taskIdString] = tracking;
            diag.Add($"ShoppingTripDriver: {kind} leg due in {leg.PlannedMinutes} min ({dueId}).");
        }

        private InFlightLeg EnsureRouteWatch(PlannedShoppingTrip trip, LegKind kind,
            WorkTask leg, float nowGameSeconds)
        {
            string taskId = leg != null ? leg.TaskId.ToString() : string.Empty;
            if (!string.IsNullOrEmpty(taskId)
                && legsByTaskIdString.TryGetValue(taskId, out InFlightLeg existing)
                && existing != null && existing.TripId == trip.TripId && existing.Kind == kind)
            {
                if (existing.NoProgressTimeoutSeconds <= 0f)
                    existing.NoProgressTimeoutSeconds = ResolveNoProgressTimeoutSeconds(
                        trip, kind, leg, routeObservationIntervalSeconds);
                return existing;
            }

            InFlightLeg tracking = CreateInFlightLeg(trip, kind);
            if (!IsFinitePositive(tracking.DeadlineGameSeconds)
                || !IsFiniteNonNegative(tracking.StartedGameSeconds))
            {
                float expectedSeconds = ResolveExpectedRouteSeconds(trip, kind, leg);
                float noProgressSeconds = expectedSeconds > 0f
                    ? Math.Max(3600f, expectedSeconds * 2f + routeObservationIntervalSeconds * 2f)
                    : Math.Max(4f * 3600f, routeObservationIntervalSeconds * 4f);
                float hardLimitSeconds = expectedSeconds > 0f
                    ? Math.Max(6f * 3600f, expectedSeconds * 4f + 2f * 3600f)
                    : 12f * 3600f;
                noProgressSeconds = Math.Min(MaxShoppingRouteNoProgressSeconds, noProgressSeconds);
                hardLimitSeconds = Math.Min(MaxShoppingRouteDeadlineSeconds, hardLimitSeconds);
                tracking.StartedGameSeconds = nowGameSeconds;
                tracking.DeadlineGameSeconds = nowGameSeconds + hardLimitSeconds;
                tracking.LastProgressGameSeconds = nowGameSeconds;
                tracking.NoProgressTimeoutSeconds = noProgressSeconds;
                // The initial estimate sizes the allowed travel window, but
                // the no-progress clock starts with the first post-departure
                // observation. Comparing against a departure-time sample can
                // falsely time out a route before its planned travel task ends.
                tracking.RemainingSeconds = -1f;
                tracking.HasProgressSample = false;
                PersistRouteWatch(trip, tracking);
            }
            else
            {
                tracking.NoProgressTimeoutSeconds = ResolveNoProgressTimeoutSeconds(
                    trip, kind, leg, routeObservationIntervalSeconds);
            }

            if (!string.IsNullOrEmpty(taskId)) legsByTaskIdString[taskId] = tracking;
            return tracking;
        }

        private float ResolveExpectedRouteSeconds(PlannedShoppingTrip trip, LegKind kind, WorkTask leg)
        {
            float planned = leg != null && leg.PlannedMinutes > 0 ? leg.PlannedMinutes * 60f : 0f;
            float candidate = trip?.Candidate != null && trip.Candidate.TravelMinutesOneWay > 0
                ? trip.Candidate.TravelMinutesOneWay * 60f : 0f;
            float observed = ReadRemainingRouteSeconds(trip, kind);
            return Math.Min(MaxExpectedShoppingRouteSeconds,
                Math.Max(planned, Math.Max(candidate, observed)));
        }

        private float ResolveNoProgressTimeoutSeconds(PlannedShoppingTrip trip, LegKind kind,
            WorkTask leg, float observationIntervalSeconds)
        {
            float expected = ResolveExpectedRouteSeconds(trip, kind, leg);
            float interval = Math.Max(1f, observationIntervalSeconds);
            float observed = ReadRemainingRouteSeconds(trip, kind);
            // A live route estimate is a remaining-time estimate, not the
            // total allowance for a task whose planned travel may be longer.
            // Keep the timeout tied to the planned/expected journey as well,
            // otherwise a short initial estimate can expire a legitimate
            // outbound task before its own completion due event.
            float timeout = expected > 0f
                ? Math.Max(interval * 2f, expected * 2f + interval * 2f)
                : Math.Max(4f * 3600f, interval * 4f);
            if (observed >= 0f)
                timeout = Math.Max(timeout, observed + interval * 2f);
            return Math.Min(MaxShoppingRouteNoProgressSeconds, timeout);
        }

        private float ReadRemainingRouteSeconds(PlannedShoppingTrip trip, LegKind kind)
        {
            if (trip == null || kind == LegKind.InStore || remainingPhysicalRouteSeconds == null) return -1f;
            string destination = kind == LegKind.Outbound
                ? trip.Candidate?.Supplier?.LocationId
                : trip.HomeLocationId;
            float? value = remainingPhysicalRouteSeconds(trip.ShopperPersonId, destination ?? string.Empty);
            return value.HasValue && IsFiniteNonNegative(value.Value) ? value.Value : -1f;
        }

        private static void PersistRouteWatch(PlannedShoppingTrip trip, InFlightLeg tracking)
        {
            if (trip == null || tracking == null) return;
            trip.ActiveRouteLegKind = (int)tracking.Kind;
            trip.ActiveRouteStartedGameSeconds = tracking.StartedGameSeconds;
            trip.ActiveRouteDeadlineGameSeconds = tracking.DeadlineGameSeconds;
            trip.ActiveRouteLastProgressGameSeconds = tracking.LastProgressGameSeconds;
            trip.ActiveRouteLastRemainingSeconds = tracking.RemainingSeconds;
            trip.ActiveRouteHasProgressSample = tracking.HasProgressSample;
        }

        private static bool IsFiniteNonNegative(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

        private static bool IsFinitePositive(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        private void ScheduleArrivalRetry(PlannedShoppingTrip trip, LegKind kind, int dayIndex,
            float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            string destination = kind == LegKind.Outbound ? trip.Candidate?.Supplier?.LocationId : trip.HomeLocationId;
            WorkTask leg = kind == LegKind.Outbound ? trip.Legs?.Outbound : trip.Legs?.Return;
            string taskIdString = leg != null
                ? leg.TaskId.ToString()
                : $"arrival-check:{trip.TripId}:{kind}";
            InFlightLeg tracking = EnsureRouteWatch(trip, kind, leg, currentGameSeconds);
            if (kind != LegKind.InStore && hasActivePhysicalRoute != null
                && (hasPhysicallyArrived == null || !hasPhysicallyArrived(trip.ShopperPersonId, destination))
                && hasActivePhysicalRoute(trip.ShopperPersonId, destination) == false)
            {
                RecoverStalledRoute(trip, kind, destination,
                    $"physical {kind} route to '{destination}' ended before arrival was confirmed",
                    dayIndex, dueEvents, diag);
                return;
            }

            ObserveRouteProgress(trip, tracking, destination, currentGameSeconds);
            bool hardDeadline = currentGameSeconds >= tracking.DeadlineGameSeconds;
            bool stalled = tracking.NoProgressTimeoutSeconds > 0f
                && currentGameSeconds - tracking.LastProgressGameSeconds >= tracking.NoProgressTimeoutSeconds;
            if (hardDeadline || stalled)
            {
                RecoverStalledRoute(trip, kind, destination,
                    hardDeadline
                        ? $"physical {kind} journey exceeded its absolute liveness deadline without confirmed arrival"
                        : $"physical {kind} journey made no measurable progress for {tracking.NoProgressTimeoutSeconds / 3600f:0.##} game hours",
                    dayIndex, dueEvents, diag);
                return;
            }
            dueEvents.Schedule(DueEventKind.TaskCompletionDue, currentGameSeconds + 60f,
                taskIdString, out _);
            legsByTaskIdString[taskIdString] = tracking;
            PersistRouteWatch(trip, tracking);
        }

        private void RebuildInFlightLeg(PlannedShoppingTrip trip)
        {
            WorkTask leg = null;
            ShoppingTripDriver.LegKind kind = ShoppingTripDriver.LegKind.Outbound;
            switch (trip.State)
            {
                case PlannedShoppingTrip.TripState.Outbound:
                    leg = trip.Legs != null ? trip.Legs.Outbound : null;
                    kind = ShoppingTripDriver.LegKind.Outbound;
                    break;
                case PlannedShoppingTrip.TripState.InStore:
                    leg = trip.Legs != null ? trip.Legs.InStore : null;
                    kind = ShoppingTripDriver.LegKind.InStore;
                    break;
                case PlannedShoppingTrip.TripState.Returning:
                    leg = trip.Legs != null ? trip.Legs.Return : null;
                    kind = ShoppingTripDriver.LegKind.Return;
                    break;
                default:
                    return; // Planned/AtStore/Done/Failed: no leg airborne.
            }

                if (leg != null && leg.IsOpen && !trip.ReturnStartPending)
            {
                legsByTaskIdString[leg.TaskId.ToString()] = CreateInFlightLeg(trip, kind);
            }
        }

        private void ScheduleLegDueAfterLoad(
            PlannedShoppingTrip trip, float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            // The scheduler itself was restored and still holds the original
            // in-store due event; scheduling replaces it idempotently by event
            // id, so exactly one remains.
            WorkTask leg = trip.Legs != null ? trip.Legs.InStore : null;
            if (leg == null || (!leg.IsOpen && leg.Status != TaskStatus.Complete) || dueEvents == null)
            {
                return;
            }

            string taskIdString = leg.TaskId.ToString();
            string dueId;
            float dueGameSeconds = leg.Status == TaskStatus.Complete
                ? currentGameSeconds
                : currentGameSeconds + Math.Max(1, leg.MinutesRemaining) * 60f;
            dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                dueGameSeconds,
                taskIdString, out dueId);

            legsByTaskIdString[taskIdString] =
                new InFlightLeg { TripId = trip.TripId, Kind = LegKind.InStore };
            diag.Add($"ShoppingTripDriver: resumed trip {trip.TripId} at in-store leg ({dueId}).");
        }

        private PlannedShoppingTrip FindTrip(string tripId)
        {
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip != null && string.Equals(trip.TripId, tripId, StringComparison.Ordinal))
                {
                    return trip;
                }
            }

            return null;
        }

        private bool HasActiveTripFor(int needSequence)
        {
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip != null && trip.IsActive && trip.Need != null && trip.Need.NeedSequence == needSequence)
                {
                    return true;
                }
            }

            return false;
        }

        private void QueuePaidReturnRecovery(PlannedShoppingTrip trip)
        {
            if (trip?.Batch == null || trip.Batch.Custody != GoodsCustodyState.InHand
                || hasPhysicallyArrived == null
                || !hasPhysicallyArrived(trip.ShopperPersonId, trip.Candidate?.Supplier?.LocationId)) return;
            if (!returnRecoveries.Contains(trip)) returnRecoveries.Add(trip);
        }

        private static int MinuteOfDay(float gameSeconds)
        {
            int day = (int)Math.Floor(gameSeconds / 86400f);
            return (int)Math.Floor((gameSeconds - day * 86400f) / 60f);
        }

        /// <summary>Save: active trips (owning runtime class holds its DTO).
        /// Save/Continue (this slice, F2): trips waiting in returnRecoveries
        /// are captured too, flagged InReturnRecovery — they carry paid goods
        /// in loop custody, and dropping them strands the batch with the need
        /// open and no recovery ever planned.</summary>
        public List<PlannedShoppingTripSaveDto> CaptureSaveDto()
        {
            var dtos = new List<PlannedShoppingTripSaveDto>();
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip != null)
                {
                    dtos.Add(ToDto(trip));
                }
            }
            foreach (PlannedShoppingTrip trip in returnRecoveries)
            {
                if (trip != null)
                {
                    PlannedShoppingTripSaveDto dto = ToDto(trip);
                    dto.InReturnRecovery = true;
                    dtos.Add(dto);
                }
            }

            return dtos;
        }

        /// <summary>
        /// Save: clear-then-load. Leg tasks and suppliers re-resolve through
        /// their authorities — call RehydrateAfterLoad once those are restored.
        /// Save/Continue (this slice, F2): DTOs flagged InReturnRecovery go
        /// back into returnRecoveries, not activeTrips — they wait for the
        /// next day's PlanForHousehold to retry the paid return, exactly as
        /// before the save. Their batch, trip-id sequence, and need are
        /// attached exactly like an active trip's; their candidate is rebuilt
        /// in RehydrateAfterLoad, where the supplier directory is available.
        /// </summary>
        public void LoadFromSaveDto(List<PlannedShoppingTripSaveDto> dtos)
        {
            activeTrips.Clear();
            returnRecoveries.Clear();
            legsByTaskIdString.Clear();
            serviceTaskIdStringToTripId.Clear();
            if (dtos == null)
            {
                NeedsRehydrateAfterLoad = false;
                return;
            }

            foreach (PlannedShoppingTripSaveDto dto in dtos)
            {
                if (dto == null)
                {
                    continue;
                }

                PlannedShoppingTrip trip = FromDto(dto);
                if (dto.InReturnRecovery)
                {
                    returnRecoveries.Add(trip);
                }
                else
                {
                    activeTrips.Add(trip);
                }
                // Priority U-3: register the restored batch in the loop's
                // custody, so the trip's batch and the loop's custody are
                // one object (the read model reports it, the receipt
                // deposits it exactly once). Idempotent: a second load of
                // the same DTO re-points at the already-held batch.
                AttachTripBatchToLoop(trip);
                // QA-20 (Plan 17): raise the loop's trip-id sequence past this
                // restored trip's id, so a later plan cannot re-issue it. Never
                // backward; a null loop is safe (loop is readonly and set in the
                // ctor, but guard like AttachTripBatchToLoop does).
                if (loop != null)
                {
                    loop.SeedTripSequenceFromTripId(trip.TripId);
                }
                AttachTripNeedToRegistry(trip);
            }

            NeedsRehydrateAfterLoad = activeTrips.Count > 0 || returnRecoveries.Count > 0;
        }

        /// <summary>
        /// Priority U-3: registers a restored trip's batch in the loop's
        /// custody so the trip's batch and the loop's custody are one
        /// object. Called on load and again on rehydrate: whichever of the
        /// loop custody DTO and the driver DTO loads last, the trip ends
        /// up pointing at the loop-owned instance.
        /// </summary>
        private void AttachTripBatchToLoop(PlannedShoppingTrip trip)
        {
            if (trip == null || trip.Batch == null || loop == null) return;
            CarriedGoodsBatch owned = loop.AttachRestoredBatch(trip.Batch);
            if (owned != null)
            {
                trip.Batch = owned;
            }
        }

        /// <summary>
        /// QA-19: re-points a restored trip's need at the registry-owned
        /// instance for the same (HouseholdId, NeedSequence), via the loop.
        /// Called on load and again on rehydrate: whichever of the needs
        /// registry and the driver DTO loads last, the trip ends up pointing
        /// at the registry instance. Idempotent: a second call returns the
        /// same instance. Returns true when the trip was re-pointed.
        /// A miss on load is silent (the registry may not be restored yet —
        /// the fixture order loads the driver first); a miss on rehydrate
        /// is diagnosed by the caller (see RehydrateAfterLoad).
        /// </summary>
        private bool AttachTripNeedToRegistry(PlannedShoppingTrip trip)
        {
            if (trip == null || trip.Need == null || loop == null) return false;
            HouseholdPurchasingNeed owned = loop.AttachRestoredNeed(trip.Need);
            if (owned != null)
            {
                trip.Need = owned;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Re-resolves leg tasks and suppliers after a clear-then-load restore.
        /// Must be called after the TaskAuthority and SupplierDirectory are
        /// restored; trips whose legs no longer resolve are aborted loudly.
        /// Production call site: SimulationDrivers.ApplyRestoredShoppingTrips,
        /// via Rebind after a load.
        /// Trips persisted mid-settlement (AtStore) resume at the in-store leg
        /// — settlement is never re-run (no double charge). Trips persisted
        /// mid-wait (AwaitingService) resume the wait when the service task
        /// still lives (Slice 19 (f2) — the link persists and the due-event
        /// map entry is re-registered); only when the service task is gone
        /// or no longer running do they settle on restore (Slice 17 (f1)),
        /// then resume at the in-store leg.
        /// </summary>
        public void RehydrateAfterLoad(
            TaskAuthority tasks, Func<string, IGoodsSupplier> supplierByBusinessId,
            int dayIndex, float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var dead = new List<PlannedShoppingTrip>();
            foreach (PlannedShoppingTrip trip in activeTrips)
            {
                if (trip == null)
                {
                    continue;
                }

                // Priority U-3: re-attach the batch to the loop-owned
                // custody instance — the loop may have loaded its custody
                // DTO after this driver loaded, replacing the instance this
                // trip points at. Attaching is idempotent, and a trip that
                // turns out dead below still keeps its batch in loop
                // custody (preserved, depositable via ReceiveCarriedBatch).
                AttachTripBatchToLoop(trip);
                // QA-20 (Plan 17): raise the loop's trip-id sequence past this
                // restored trip's id, so a later plan cannot re-issue it. Never
                // backward; a null loop is safe (loop is readonly and set in the
                // ctor, but guard like AttachTripBatchToLoop does). Same
                // load-order reason as the re-attach above: whichever of the
                // custody DTO and the driver DTO loads last, the counter ends
                // up raised past every restored active trip id. Idempotent
                // (Max-only), so calling it in both places is safe.
                if (loop != null)
                {
                    loop.SeedTripSequenceFromTripId(trip.TripId);
                }

                // QA-19: re-point the restored trip's need at the registry
                // instance — the registry may have loaded after this driver,
                // replacing the instance this trip points at. Attaching is
                // idempotent. A miss is diagnosed loudly but the trip is not
                // aborted: it can still complete, and the receipt will
                // decrement the DTO copy instead of the registry need.
                if (!AttachTripNeedToRegistry(trip))
                {
                    diag.Add($"ShoppingTripDriver: trip {trip.TripId} household {trip.HouseholdId} need {trip.Need?.NeedSequence} has no registry instance — the receipt will decrement the DTO copy, not the registry need.");
                }

                // Campaign C (DEF-022): the candidate re-resolves before the
                // leg check, so a trip that dies on its legs still carries
                // its supplier — the paid-return recovery needs the store
                // location, and the supplier-lost check below needs the
                // rebuilt candidate either way. Sale-time numbers (units,
                // price) were fixed at planning.
                trip.Candidate = RebuildCandidate(trip.CandidateDto, supplierByBusinessId);

                var legs = new HouseholdShoppingLoop.PlannedTrip();
                bool ok = true;
                if (trip.OutboundTaskId.IsValid)
                {
                    legs.Outbound = tasks != null ? tasks.FindTask(trip.OutboundTaskId) : null;
                    ok = legs.Outbound != null;
                }

                if (ok && trip.InStoreTaskId.IsValid)
                {
                    legs.InStore = tasks != null ? tasks.FindTask(trip.InStoreTaskId) : null;
                    ok = legs.InStore != null;
                }

                if (ok && trip.ReturnTaskId.IsValid)
                {
                    legs.Return = tasks != null ? tasks.FindTask(trip.ReturnTaskId) : null;
                    ok = legs.Return != null;
                }

                if (!ok)
                {
                    diag.Add($"ShoppingTripDriver: trip {trip.TripId} lost its leg tasks across save/load — aborted, need stays open.");
                    dead.Add(trip);
                    continue;
                }

                trip.Legs = legs;
                if (trip.State != PlannedShoppingTrip.TripState.Planned && trip.Candidate == null)
                {
                    diag.Add($"ShoppingTripDriver: trip {trip.TripId} lost its supplier across save/load — aborted, need stays open.");
                    dead.Add(trip);
                    continue;
                }

                // Rebuild the in-flight leg map from the trip state: the due
                // event that is still scheduled names the leg's task id, and
                // the state says which leg is airborne.
                RebuildInFlightLeg(trip);

                // Save/Continue (this slice, F1): a trip saved mid-route
                // has no physical route in this session — the pathing
                // director's routes are live scene state and are never
                // persisted. Re-issue the route here, while the leg due
                // still has time to fire: the arrival gate then sees a
                // live route and retries on progress (the live slow-route
                // path), instead of aborting at the first due on "no
                // longer active". Only when all three delegates are bound
                // (production; null in the harness) and the shopper is
                // neither arrived nor already routed. A refused restart
                // is diagnosed loudly; the trip then aborts at its leg
                // due exactly as before this fix — never silently, never
                // worse.
                if ((trip.State == PlannedShoppingTrip.TripState.Outbound
                    || (trip.State == PlannedShoppingTrip.TripState.Returning && !trip.ReturnStartPending))
                    && startPhysicalRoute != null && hasPhysicallyArrived != null
                    && hasActivePhysicalRoute != null)
                {
                    string destination = trip.State == PlannedShoppingTrip.TripState.Outbound
                        ? trip.Candidate?.Supplier?.LocationId ?? string.Empty
                        : trip.HomeLocationId ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(destination)
                        && !hasPhysicallyArrived(trip.ShopperPersonId, destination)
                        && hasActivePhysicalRoute(trip.ShopperPersonId, destination) == false
                        && !startPhysicalRoute(trip.ShopperPersonId, destination))
                    {
                        diag.Add($"ShoppingTripDriver: trip {trip.TripId} could not restart its physical route to '{destination}' after save/load — it will abort at its leg due.");
                    }
                }

                if (trip.State == PlannedShoppingTrip.TripState.AtStore)
                {
                    // Settlement already ran (batch/custody persisted); resume
                    // at the in-store leg without re-settling.
                    string inStoreError = loop.BeginInStoreLeg(trip, dayIndex, diag);
                    if (inStoreError != null)
                    {
                        diag.Add($"ShoppingTripDriver: trip {trip.TripId} could not resume at the in-store leg — {inStoreError}");
                        dead.Add(trip);
                        continue;
                    }

                    ScheduleLegDueAfterLoad(trip, currentGameSeconds, dueEvents, diag);
                }
                else if (trip.State == PlannedShoppingTrip.TripState.AwaitingService)
                {
                    // Slice 19 (f2): the service linkage survived the save —
                    // resume the wait when the service task still lives. The
                    // persisted service due fires at its original absolute
                    // time; only the due-event map entry is re-registered
                    // (MAP-ONLY, note 87 Q1 — never re-schedule the due).
                    WorkTask serviceTask = trip.ServiceTaskId.IsValid
                        ? tasks?.FindTask(trip.ServiceTaskId) : null;
                    if (serviceTask != null && serviceTask.Status == TaskStatus.InProgress)
                    {
                        serviceTaskIdStringToTripId[trip.ServiceTaskId.ToString()] = trip.TripId;
                        diag.Add($"ShoppingTripDriver: trip {trip.TripId} resumed mid-wait; service due re-registered.");
                        continue;
                    }
                    if (serviceTask != null && serviceTask.Status == TaskStatus.Complete)
                    {
                        // Preserve the completed task identity. The next live
                        // sweep reconciles the FIFO queue and settles once.
                        serviceTaskIdStringToTripId[trip.ServiceTaskId.ToString()] = trip.TripId;
                        diag.Add($"ShoppingTripDriver: trip {trip.TripId} restored with completed counter work; reconciling service before settlement.");
                        continue;
                    }

                    // A missing/cancelled task is not evidence that service
                    // occurred. Keep the saved queue entry and let the live
                    // service sweep retry within its persisted patience
                    // deadline. Legacy saves without a deadline receive one
                    // bounded compatibility window from restore time.
                    string reason = !trip.ServiceTaskId.IsValid ? "waiting for a clerk"
                        : serviceTask == null ? "service task not found"
                        : $"service task {serviceTask.Status}";
                    trip.ServiceTaskId = default;
                    trip.ServiceTaskPlannedMinutes = 0;
                    if (trip.ServiceWaitDeadlineGameSeconds < 0f)
                        trip.ServiceWaitDeadlineGameSeconds = currentGameSeconds
                            + CustomerQueue.DefaultPatienceMinutes * 60f;
                    diag.Add($"ShoppingTripDriver: trip {trip.TripId} remains unsettled after restore ({reason}); no goods or money moved.");
                }
            }

            foreach (PlannedShoppingTrip trip in dead)
            {
                loop.AbortTrip(trip, "lost across save/load", dayIndex, diag);
                activeTrips.Remove(trip);
                // Campaign C (DEF-022): a load-restored trip has Legs == null
                // (FromDto never sets it), but TryPlanPaidReturnRecovery
                // plans the return leg into trip.Legs and refuses a null
                // container. The old leg tasks are gone; give the dead trip
                // a fresh container so the recovery can plan new return legs
                // into it. Nothing here resurrects the old legs.
                if (trip.Legs == null) trip.Legs = new HouseholdShoppingLoop.PlannedTrip();
                RecoverStrandedPaidBatch(trip, dayIndex, diag);
            }

            // Save/Continue (this slice, F2): rehydrate the paid-return
            // recovery trips restored into returnRecoveries — same
            // attachments as the active trips above (batch, trip-id
            // sequence, need, candidate), but no leg resolution: their
            // legs were cancelled by the abort, and TryPlanPaidReturnRecovery
            // plans fresh return legs into trip.Legs (which must exist).
            // A recovery whose supplier cannot be rebuilt can never
            // recover; drop it loudly — the batch stays in loop custody,
            // the need stays open, and the diag names the loss, exactly
            // like a dead active trip.
            var deadRecoveries = new List<PlannedShoppingTrip>();
            foreach (PlannedShoppingTrip trip in returnRecoveries)
            {
                if (trip == null) continue;
                AttachTripBatchToLoop(trip);
                if (loop != null)
                {
                    loop.SeedTripSequenceFromTripId(trip.TripId);
                }
                if (!AttachTripNeedToRegistry(trip))
                {
                    diag.Add($"ShoppingTripDriver: recovery trip {trip.TripId} household {trip.HouseholdId} need {trip.Need?.NeedSequence} has no registry instance — the receipt will decrement the DTO copy, not the registry need.");
                }
                trip.Candidate = RebuildCandidate(trip.CandidateDto, supplierByBusinessId);
                if (trip.Candidate == null)
                {
                    diag.Add($"ShoppingTripDriver: recovery trip {trip.TripId} lost its supplier across save/load — dropped, paid batch stays in custody, need stays open.");
                    deadRecoveries.Add(trip);
                    continue;
                }
                if (trip.Legs == null) trip.Legs = new HouseholdShoppingLoop.PlannedTrip();
            }
            foreach (PlannedShoppingTrip trip in deadRecoveries)
            {
                returnRecoveries.Remove(trip);
            }

            NeedsRehydrateAfterLoad = false;
        }

        /// <summary>
        /// Campaign C (DEF-022): a trip that died across save/load may hold
        /// paid goods in loop custody. The holder is usually still at the
        /// store — queue the physical paid return recovery. When the holder
        /// is already home, deposit the batch directly through
        /// ReceiveCarriedBatch (its first production caller); no travel
        /// needs planning for goods already at the door. A batch that fits
        /// neither stays in custody with the need open and is diagnosed
        /// loudly; PlanForHousehold's pending-custody block prevents a
        /// duplicate purchase for that need. Arranged-delivery batches are
        /// not handled here — ReceiveCarriedBatch refuses them by design.
        /// </summary>
        private void RecoverStrandedPaidBatch(PlannedShoppingTrip trip, int dayIndex, List<string> diag)
        {
            if (trip?.Batch == null || trip.Batch.Custody != GoodsCustodyState.InHand) return;
            if (hasPhysicallyArrived != null
                && hasPhysicallyArrived(trip.ShopperPersonId, trip.HomeLocationId ?? string.Empty))
            {
                string receiptError = loop.ReceiveCarriedBatch(trip.Batch.BatchId, dayIndex, diag);
                if (receiptError == null) return;
                diag.Add($"ShoppingTripDriver: trip {trip.TripId} paid batch {trip.Batch.BatchId} could not be received at home after save/load — {receiptError}");
                return;
            }

            QueuePaidReturnRecovery(trip);
            if (returnRecoveries.Contains(trip)) return;
            diag.Add($"ShoppingTripDriver: trip {trip.TripId} paid batch {trip.Batch.BatchId} stays in custody after save/load — holder P{trip.ShopperPersonId} is neither at the store nor at home; need stays open, no new trip will be planned for it until the batch is received.");
        }

        // ---------- DTO mapping ----------

        private static PlannedShoppingTripSaveDto ToDto(PlannedShoppingTrip trip)
        {
            var dto = new PlannedShoppingTripSaveDto
            {
                TripId = trip.TripId,
                Need = trip.Need,
                HouseholdId = trip.HouseholdId,
                ShopperPersonId = trip.ShopperPersonId,
                DayIndex = trip.DayIndex,
                DepartureMinuteOfDay = trip.DepartureMinuteOfDay,
                HomeLocationId = trip.HomeLocationId ?? string.Empty,
                ResultNeedSequence = trip.Result != null ? trip.Result.NeedSequence : -1,
                State = (int)trip.State,
                FailureReason = trip.FailureReason ?? string.Empty,
                UnitsSettled = trip.UnitsSettled,
                DeliveryMinutes = trip.DeliveryMinutes,
                WindowReservationIds = new List<string>(trip.WindowReservationIds ?? new List<string>()),
                OutboundTaskId = trip.OutboundTaskId,
                InStoreTaskId = trip.InStoreTaskId,
                ReturnTaskId = trip.ReturnTaskId,
                ServiceTaskId = trip.ServiceTaskId,
                ServiceTaskPlannedMinutes = trip.ServiceTaskPlannedMinutes,
                ServiceWaitDeadlineGameSeconds = trip.ServiceWaitDeadlineGameSeconds,
                ActiveRouteLegKind = trip.ActiveRouteLegKind,
                ActiveRouteStartedGameSeconds = trip.ActiveRouteStartedGameSeconds,
                ActiveRouteDeadlineGameSeconds = trip.ActiveRouteDeadlineGameSeconds,
                ActiveRouteLastProgressGameSeconds = trip.ActiveRouteLastProgressGameSeconds,
                ActiveRouteLastRemainingSeconds = trip.ActiveRouteLastRemainingSeconds,
                ActiveRouteHasProgressSample = trip.ActiveRouteHasProgressSample,
                ReturnStartPending = trip.ReturnStartPending,
                ReturnStartRetryAfterGameSeconds = trip.ReturnStartRetryAfterGameSeconds,
                ReturnStartDeadlineGameSeconds = trip.ReturnStartDeadlineGameSeconds,
            };
            if (trip.Candidate != null)
            {
                dto.SupplierBusinessId = trip.Candidate.Supplier != null ? trip.Candidate.Supplier.SupplierBusinessId ?? string.Empty : string.Empty;
                dto.SupplierName = trip.Candidate.Supplier != null ? trip.Candidate.Supplier.SupplierName ?? string.Empty : string.Empty;
                dto.SupplierLocationId = trip.Candidate.Supplier != null ? trip.Candidate.Supplier.LocationId ?? string.Empty : string.Empty;
                dto.UnitsPlanned = trip.Candidate.UnitsPlanned;
                dto.UnitPriceCents = trip.Candidate.UnitPriceCents;
                dto.TravelMinutesOneWay = trip.Candidate.TravelMinutesOneWay;
                dto.TotalCostCents = trip.Candidate.TotalCostCents;
                dto.DeliveryFeeCents = trip.Candidate.DeliveryFeeCents;
                dto.UseDelivery = trip.Candidate.UseDelivery;
                dto.SettlementMode = (int)trip.Candidate.SettlementMode;
                dto.CarrierBusinessInstanceId = trip.Candidate.CarrierBusinessInstanceId ?? string.Empty;
            }

            if (trip.Batch != null)
            {
                dto.BatchId = trip.Batch.BatchId ?? string.Empty;
                dto.BatchItemId = trip.Batch.ItemId ?? string.Empty;
                dto.BatchUnits = trip.Batch.Units;
                dto.BatchUnitPriceCents = trip.Batch.UnitPriceCents;
                dto.BatchReceiptId = trip.Batch.ReceiptId ?? string.Empty;
                dto.BatchCustody = (int)trip.Batch.Custody;
                dto.BatchProvenance = trip.Batch.Provenance ?? string.Empty;
                dto.BatchPurchaseDayIndex = trip.Batch.PurchaseDayIndex;
                dto.BatchHouseholdUnitsPerStoreUnit = Math.Max(1, trip.Batch.HouseholdUnitsPerStoreUnit);
                dto.BatchSupplierName = trip.Batch.SupplierName ?? string.Empty;
                dto.BatchPerishableCondition = trip.Batch.PerishableCondition;
                dto.BatchHasPerishableCondition = trip.Batch.HasPerishableCondition;
            }

            return dto;
        }

        private static PlannedShoppingTrip FromDto(PlannedShoppingTripSaveDto dto)
        {
            var trip = new PlannedShoppingTrip
            {
                TripId = dto.TripId,
                Need = dto.Need,
                HouseholdId = dto.HouseholdId,
                ShopperPersonId = dto.ShopperPersonId,
                DayIndex = dto.DayIndex,
                DepartureMinuteOfDay = dto.DepartureMinuteOfDay,
                HomeLocationId = dto.HomeLocationId,
                State = (PlannedShoppingTrip.TripState)dto.State,
                FailureReason = dto.FailureReason,
                UnitsSettled = dto.UnitsSettled,
                DeliveryMinutes = dto.DeliveryMinutes,
                WindowReservationIds = new List<string>(dto.WindowReservationIds ?? new List<string>()),
                OutboundTaskId = dto.OutboundTaskId,
                InStoreTaskId = dto.InStoreTaskId,
                ReturnTaskId = dto.ReturnTaskId,
                ServiceTaskId = dto.ServiceTaskId,
                ServiceTaskPlannedMinutes = dto.ServiceTaskPlannedMinutes,
                ServiceWaitDeadlineGameSeconds = dto.ServiceWaitDeadlineGameSeconds,
                ActiveRouteLegKind = dto.ActiveRouteLegKind,
                ActiveRouteStartedGameSeconds = dto.ActiveRouteStartedGameSeconds,
                ActiveRouteDeadlineGameSeconds = dto.ActiveRouteDeadlineGameSeconds,
                ActiveRouteLastProgressGameSeconds = dto.ActiveRouteLastProgressGameSeconds,
                ActiveRouteLastRemainingSeconds = dto.ActiveRouteLastRemainingSeconds,
                ActiveRouteHasProgressSample = dto.ActiveRouteHasProgressSample,
                ReturnStartPending = dto.ReturnStartPending,
                ReturnStartRetryAfterGameSeconds = dto.ReturnStartRetryAfterGameSeconds,
                ReturnStartDeadlineGameSeconds = dto.ReturnStartDeadlineGameSeconds,
                CandidateDto = dto,
                Result = new ShoppingTripResult
                {
                    TripId = dto.TripId ?? string.Empty,
                    NeedSequence = dto.ResultNeedSequence,
                    HouseholdId = dto.HouseholdId,
                    ShopperPersonId = dto.ShopperPersonId,
                },
            };
            if (!string.IsNullOrEmpty(dto.BatchId))
            {
                trip.Batch = new CarriedGoodsBatch
                {
                    BatchId = dto.BatchId,
                    HouseholdId = dto.HouseholdId,
                    HolderPersonId = dto.ShopperPersonId,
                    ItemId = dto.BatchItemId,
                    Units = dto.BatchUnits,
                    UnitPriceCents = dto.BatchUnitPriceCents,
                    ReceiptId = dto.BatchReceiptId,
                    Custody = (GoodsCustodyState)dto.BatchCustody,
                    Provenance = dto.BatchProvenance,
                    PurchaseDayIndex = dto.BatchPurchaseDayIndex,
                    HouseholdUnitsPerStoreUnit = dto.BatchHouseholdUnitsPerStoreUnit > 0
                        ? dto.BatchHouseholdUnitsPerStoreUnit : 1,
                    SupplierName = dto.BatchSupplierName ?? string.Empty,
                    PerishableCondition = dto.BatchPerishableCondition,
                    HasPerishableCondition = dto.BatchHasPerishableCondition,
                };
            }

            return trip;
        }

        private static HouseholdShoppingLoop.ShoppingCandidate RebuildCandidate(
            PlannedShoppingTripSaveDto dto, Func<string, IGoodsSupplier> supplierByBusinessId)
        {
            if (dto == null || string.IsNullOrEmpty(dto.SupplierBusinessId) || supplierByBusinessId == null)
            {
                return null;
            }

            IGoodsSupplier supplier = supplierByBusinessId(dto.SupplierBusinessId);
            var itemSupplier = supplier as IItemResolvingSupplier;
            if (supplier == null || itemSupplier == null)
            {
                return null;
            }

            return new HouseholdShoppingLoop.ShoppingCandidate
            {
                Supplier = supplier,
                ItemSupplier = itemSupplier,
                UnitsPlanned = dto.UnitsPlanned,
                UnitPriceCents = dto.UnitPriceCents,
                TravelMinutesOneWay = dto.TravelMinutesOneWay,
                TravelMilesOneWay = 0f,
                TotalCostCents = dto.TotalCostCents,
                DeliveryFeeCents = dto.DeliveryFeeCents,
                UseDelivery = dto.UseDelivery,
                CarrierBusinessInstanceId = dto.CarrierBusinessInstanceId ?? string.Empty,
                SettlementMode = (ShoppingSettlementMode)dto.SettlementMode,
            };
        }
    }
}
