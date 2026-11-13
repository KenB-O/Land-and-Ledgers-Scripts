using System;
using System.Collections.Generic;
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

        /// <summary>Leg tasks (live form). Re-resolved from LegTaskIds after load.</summary>
        public HouseholdShoppingLoop.PlannedTrip Legs;

        /// <summary>Chosen supplier (live form). Rebuilt from CandidateDto after load.</summary>
        internal HouseholdShoppingLoop.ShoppingCandidate Candidate;

        internal EntityId OutboundTaskId;
        internal EntityId InStoreTaskId;
        internal EntityId ReturnTaskId;
        internal PlannedShoppingTripSaveDto CandidateDto;

        public bool IsActive => State == TripState.Planned || State == TripState.Outbound ||
            State == TripState.AtStore || State == TripState.InStore || State == TripState.Returning || State == TripState.RecoveryPlanned;
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
        public string FailureReason = string.Empty;
        public int UnitsSettled;
        public int DeliveryMinutes;
        public List<string> WindowReservationIds = new List<string>();
        public EntityId OutboundTaskId;
        public EntityId InStoreTaskId;
        public EntityId ReturnTaskId;
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
        }

        private readonly HouseholdShoppingLoop loop;
        private readonly Func<int, string, bool> startPhysicalRoute;
        private readonly Func<int, string, bool> hasPhysicallyArrived;
        private readonly Func<int, string, bool?> hasActivePhysicalRoute;
        private readonly Func<int, string, float?> remainingPhysicalRouteSeconds;
        private readonly Action<int, string> cancelPhysicalRoute;
        private readonly float routeObservationIntervalSeconds;
        private readonly List<PlannedShoppingTrip> activeTrips = new List<PlannedShoppingTrip>();
        private readonly List<PlannedShoppingTrip> returnRecoveries = new List<PlannedShoppingTrip>();
        private readonly Dictionary<string, InFlightLeg> legsByTaskIdString =
            new Dictionary<string, InFlightLeg>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PlannedShoppingTrip> ActiveTrips => activeTrips;
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

        public ShoppingTripDriver(HouseholdShoppingLoop loop,
            Func<int, string, bool> startPhysicalRoute = null,
            Func<int, string, bool> hasPhysicallyArrived = null,
            Func<int, string, bool?> hasActivePhysicalRoute = null,
            Func<int, string, float?> remainingPhysicalRouteSeconds = null,
            Action<int, string> cancelPhysicalRoute = null,
            float routeObservationIntervalSeconds = 900f)
        {
            this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
            this.startPhysicalRoute = startPhysicalRoute;
            this.hasPhysicallyArrived = hasPhysicallyArrived;
            this.hasActivePhysicalRoute = hasActivePhysicalRoute;
            this.remainingPhysicalRouteSeconds = remainingPhysicalRouteSeconds;
            this.cancelPhysicalRoute = cancelPhysicalRoute;
            this.routeObservationIntervalSeconds = Math.Max(1f, routeObservationIntervalSeconds);
        }

        public bool OwnsDueEvent(DueEvent ev)
        {
            if (ev == null) return false;
            if (ev.Kind == DueEventKind.TaskCompletionDue)
                return legsByTaskIdString.ContainsKey(ev.PayloadRef ?? string.Empty);
            return ev.Kind == DueEventKind.JourneyStageDue
                && !string.IsNullOrWhiteSpace(ev.PayloadRef)
                && ev.PayloadRef.StartsWith("delivery:", StringComparison.Ordinal)
                && FindTrip(ev.PayloadRef.Substring("delivery:".Length)) != null;
        }

        public bool OwnsOpenTask(WorkTask task) => task != null
            && legsByTaskIdString.ContainsKey(task.TaskId.ToString());

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
                string seller = recovery.Candidate?.Supplier?.LocationId;
                if (hasPhysicallyArrived == null || !hasPhysicallyArrived(recovery.ShopperPersonId, seller)) continue;
                if (loop.TryPlanPaidReturnRecovery(recovery, dayIndex, options, diag))
                {
                    returnRecoveries.Remove(recovery);
                    activeTrips.Add(recovery);
                }
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
                if (error == null && startPhysicalRoute != null
                    && !startPhysicalRoute(trip.ShopperPersonId, storeLocation))
                    error = $"No physical route reaches seller location '{storeLocation}'.";
                if (error != null)
                {
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
                        break;
                    }

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

        /// <summary>Arrival at the store: settle at the counter, then start the in-store leg.</summary>
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
            string error = loop.ArriveAtStoreAndSettle(trip, dayIndex, MinuteOfDay(currentGameSeconds), diag);
            if (error != null)
            {
                // Closed store or failed settlement: walk home empty-handed
                // through the REAL return leg — the need stays open.
                loop.CancelUnstartedInStoreLeg(trip, dayIndex, diag);
                loop.RecordTripRefusal(trip, error, dayIndex);
                diag.Add($"ShoppingTripDriver: trip {trip.TripId} need {trip.Need?.NeedSequence}: arrival failed ({error}) — returning empty-handed.");
                trip.State = PlannedShoppingTrip.TripState.Returning;
                string returnError = loop.BeginReturnLeg(trip, dayIndex, "return-empty", diag);
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
                return;
            }

            string inStoreError = loop.BeginInStoreLeg(trip, dayIndex, diag);
            if (inStoreError != null)
            {
                loop.AbortTrip(trip, inStoreError, dayIndex, diag);
                activeTrips.Remove(trip);
                return;
            }

            if (trip.DeliveryMinutes > 0)
            {
                string dueId;
                dueEvents.Schedule(DueEventKind.JourneyStageDue,
                    currentGameSeconds + trip.DeliveryMinutes * 60f,
                    "delivery:" + trip.TripId, out dueId);
                diag.Add($"ShoppingTripDriver: delivery freight scheduled ({trip.DeliveryMinutes} min).");
            }

            ScheduleLegDue(trip, LegKind.InStore,
                trip.Legs != null ? trip.Legs.InStore : null,
                currentGameSeconds, dueEvents, diag);
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
            dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                currentGameSeconds + Math.Max(1, leg.PlannedMinutes) * 60f,
                taskIdString, out dueId);
            legsByTaskIdString[taskIdString] = new InFlightLeg { TripId = trip.TripId, Kind = kind };
            diag.Add($"ShoppingTripDriver: {kind} leg due in {leg.PlannedMinutes} min ({dueId}).");
        }

        private void ScheduleArrivalRetry(PlannedShoppingTrip trip, LegKind kind, int dayIndex,
            float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            string destination = kind == LegKind.Outbound ? trip.Candidate?.Supplier?.LocationId : trip.HomeLocationId;
            WorkTask leg = kind == LegKind.Outbound ? trip.Legs?.Outbound : trip.Legs?.Return;
            string taskIdString = leg != null
                ? leg.TaskId.ToString()
                : $"arrival-check:{trip.TripId}:{kind}";
            if (!legsByTaskIdString.TryGetValue(taskIdString, out InFlightLeg tracking) || tracking == null)
                tracking = new InFlightLeg { TripId = trip.TripId, Kind = kind };
            if (kind != LegKind.InStore && hasActivePhysicalRoute != null
                && (hasPhysicallyArrived == null || !hasPhysicallyArrived(trip.ShopperPersonId, destination))
                && hasActivePhysicalRoute(trip.ShopperPersonId, destination) == false)
            {
                loop.AbortTrip(trip, $"Physical {kind} route to '{destination}' is no longer active; arrival was not confirmed.", dayIndex, diag);
                legsByTaskIdString.Remove(taskIdString);
                activeTrips.Remove(trip);
                return;
            }

            float? remaining = kind != LegKind.InStore && remainingPhysicalRouteSeconds != null
                ? remainingPhysicalRouteSeconds(trip.ShopperPersonId, destination) : null;
            if (remaining.HasValue && !float.IsNaN(remaining.Value) && !float.IsInfinity(remaining.Value) && remaining.Value >= 0f)
            {
                if (!tracking.HasProgressSample || remaining.Value < tracking.RemainingSeconds
                    || currentGameSeconds < tracking.LastProgressGameSeconds)
                {
                    tracking.HasProgressSample = true;
                    tracking.RemainingSeconds = remaining.Value;
                    tracking.LastProgressGameSeconds = currentGameSeconds;
                }
                else if (currentGameSeconds - tracking.LastProgressGameSeconds
                    > tracking.RemainingSeconds + routeObservationIntervalSeconds)
                {
                    cancelPhysicalRoute?.Invoke(trip.ShopperPersonId, destination);
                    loop.AbortTrip(trip, $"Physical {kind} route to '{destination}' made no progress over its remaining route time and observation interval.", dayIndex, diag);
                    legsByTaskIdString.Remove(taskIdString);
                    activeTrips.Remove(trip);
                    return;
                }
            }
            dueEvents.Schedule(DueEventKind.TaskCompletionDue, currentGameSeconds + 60f,
                taskIdString, out _);
            legsByTaskIdString[taskIdString] = tracking;
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

            if (leg != null && leg.IsOpen)
            {
                legsByTaskIdString[leg.TaskId.ToString()] =
                    new InFlightLeg { TripId = trip.TripId, Kind = kind };
            }
        }

        private void ScheduleLegDueAfterLoad(
            PlannedShoppingTrip trip, float currentGameSeconds, IDueEventScheduler dueEvents, List<string> diag)
        {
            // The scheduler itself was restored and still holds the original
            // in-store due event; scheduling replaces it idempotently by event
            // id, so exactly one remains.
            WorkTask leg = trip.Legs != null ? trip.Legs.InStore : null;
            if (leg == null || !leg.IsOpen || dueEvents == null)
            {
                return;
            }

            string taskIdString = leg.TaskId.ToString();
            string dueId;
            dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                currentGameSeconds + Math.Max(1, leg.MinutesRemaining) * 60f,
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

        /// <summary>Save: active trips (owning runtime class holds its DTO).</summary>
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

            return dtos;
        }

        /// <summary>
        /// Save: clear-then-load. Leg tasks and suppliers re-resolve through
        /// their authorities — call RehydrateAfterLoad once those are restored.
        /// </summary>
        public void LoadFromSaveDto(List<PlannedShoppingTripSaveDto> dtos)
        {
            activeTrips.Clear();
            legsByTaskIdString.Clear();
            if (dtos == null)
            {
                return;
            }

            foreach (PlannedShoppingTripSaveDto dto in dtos)
            {
                if (dto == null)
                {
                    continue;
                }

                PlannedShoppingTrip trip = FromDto(dto);
                activeTrips.Add(trip);
                // Priority U-3: register the restored batch in the loop's
                // custody, so the trip's batch and the loop's custody are
                // one object (the read model reports it, the receipt
                // deposits it exactly once). Idempotent: a second load of
                // the same DTO re-points at the already-held batch.
                AttachTripBatchToLoop(trip);
            }
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
        /// Re-resolves leg tasks and suppliers after a clear-then-load restore.
        /// Must be called after the TaskAuthority and SupplierDirectory are
        /// restored; trips whose legs no longer resolve are aborted loudly.
        /// Trips persisted mid-settlement (AtStore) resume at the in-store leg
        /// — settlement is never re-run (no double charge).
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
                // The candidate re-resolves from the supplier directory; the
                // sale-time numbers (units, price) were fixed at planning.
                trip.Candidate = RebuildCandidate(trip.CandidateDto, supplierByBusinessId);
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
            }

            foreach (PlannedShoppingTrip trip in dead)
            {
                loop.AbortTrip(trip, "lost across save/load", dayIndex, diag);
                activeTrips.Remove(trip);
            }
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
                CandidateDto = dto,
                Result = new ShoppingTripResult
                {
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
                SettlementMode = (ShoppingSettlementMode)dto.SettlementMode,
            };
        }
    }
}
