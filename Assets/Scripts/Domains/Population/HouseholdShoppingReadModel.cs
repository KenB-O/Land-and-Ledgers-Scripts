using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase C (§26, source-level only): read models over the shopping loop's
    /// diagnostic events, trip history, custody batches and the person
    /// schedule tracker. A future UI answers from here:
    ///   - what is this Person doing → DescribePersonActivity
    ///   - why → the Decision / ShopperAssignment events for their trip
    ///   - where are they going → the JourneyLeg events
    ///   - what do they own (in custody) → PersonCustody
    ///   - how did they decide → TripDecisionSummary (choice + rejections)
    /// Read-only projections: they never move money or goods.
    /// </summary>
    public sealed class HouseholdShoppingReadModel
    {
        private readonly HouseholdShoppingLoop loop;
        private readonly PersonScheduleTracker scheduleTracker;

        public HouseholdShoppingReadModel(HouseholdShoppingLoop loop, PersonScheduleTracker scheduleTracker)
        {
            this.loop = loop ?? throw new ArgumentNullException(nameof(loop));
            this.scheduleTracker = scheduleTracker ?? throw new ArgumentNullException(nameof(scheduleTracker));
        }

        /// <summary>What is this Person doing right now?</summary>
        public string DescribePersonActivity(int personId, int dayIndex, int minuteOfDay)
        {
            return scheduleTracker.DescribeActivity(personId, dayIndex, minuteOfDay);
        }

        /// <summary>Where is this Person going (their latest trip's journey legs)?</summary>
        public List<ShoppingDiagnosticEvent> PersonJourney(int personId)
        {
            var result = new List<ShoppingDiagnosticEvent>();
            foreach (ShoppingTripResult trip in loop.TripHistory)
            {
                if (trip == null || trip.ShopperPersonId != personId)
                {
                    continue;
                }

                foreach (ShoppingDiagnosticEvent e in trip.Events)
                {
                    if (e != null && e.Kind == ShoppingEventKind.JourneyLeg)
                    {
                        result.Add(e);
                    }
                }
            }

            return result;
        }

        /// <summary>What does this Person currently hold in transit custody?</summary>
        public List<CarriedGoodsBatch> PersonCustody(int personId)
        {
            var result = new List<CarriedGoodsBatch>();
            foreach (CarriedGoodsBatch batch in loop.ActiveCustodyBatches)
            {
                if (batch != null && batch.HolderPersonId == personId)
                {
                    result.Add(batch);
                }
            }

            return result;
        }

        /// <summary>How did this trip decide — the choice and every rejected alternative.</summary>
        public string TripDecisionSummary(int needSequence)
        {
            foreach (ShoppingTripResult trip in loop.TripHistory)
            {
                if (trip == null || trip.NeedSequence != needSequence)
                {
                    continue;
                }

                var lines = new List<string>();
                foreach (ShoppingDiagnosticEvent e in trip.Events)
                {
                    if (e != null && (e.Kind == ShoppingEventKind.Decision || e.Kind == ShoppingEventKind.ShopperAssignment))
                    {
                        lines.Add($"- {e.Summary}");
                    }
                }

                foreach (RejectedShoppingAlternative rejected in trip.RejectedAlternatives)
                {
                    if (rejected != null)
                    {
                        lines.Add($"- rejected '{rejected.SupplierName}': {rejected.Reason}");
                    }
                }

                if (!trip.Success && !string.IsNullOrWhiteSpace(trip.FailureReason))
                {
                    lines.Add($"- FAILED: {trip.FailureReason}");
                }

                return string.Join("\n", lines);
            }

            return $"No trip recorded for need {needSequence}.";
        }

        /// <summary>Why is this purchasing need still open?</summary>
        public string NeedStatusSummary(HouseholdPurchasingNeed need)
        {
            if (need == null)
            {
                return "No need.";
            }

            if (need.Status == PurchasingNeedStatus.Fulfilled)
            {
                return $"Need {need.NeedSequence} ({need.ItemId}) is fulfilled.";
            }

            ShoppingTripResult latest = null;
            foreach (ShoppingTripResult trip in loop.TripHistory)
            {
                if (trip != null && trip.NeedSequence == need.NeedSequence)
                {
                    latest = trip;
                }
            }

            if (latest == null)
            {
                return $"Need {need.NeedSequence} ({need.ItemId} x{need.UnitsNeeded}) is open — no trip attempted yet. Reason: {need.ReasonSummary}";
            }

            if (latest.Success)
            {
                return $"Need {need.NeedSequence} ({need.ItemId}): {latest.UnitsAcquired}u acquired, {latest.UnitsStillNeeded}u still needed.";
            }

            return $"Need {need.NeedSequence} ({need.ItemId} x{need.UnitsNeeded}) is open — last trip failed: {latest.FailureReason}";
        }
    }
}
