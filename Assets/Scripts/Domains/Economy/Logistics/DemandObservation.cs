// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice 6 — demand observation (directive P3, P9 plan Slice 6,
// Priority F). A business-level record of what households attempted to buy,
// what they actually bought, and what went unfilled — read from what the
// authorities already hold, never invented, never forecast.
//
// The book is fed two ways, and ONLY two ways, in this slice:
//   (a) explicit Record calls from a caller that holds the attempt (the
//       tests are the caller here); and
//   (b) derivation from the F3 custody log (FromCustodyLog), which carries
//       completed retail-purchase transfers with CauseKind and CauseId.
// No live flow is instrumented in this slice. The requested-vs-supplied
// split and the shelf-empty flag are NOT in the F3 log, so they cannot be
// derived from it; the site where a live feed could be added is the one
// place the F3 log already records the same event — HouseholdShoppingLoop
// .SettleAtCounter (HouseholdShoppingLoop.cs:1150), which records the
// retail-purchase transfer — and adding that call is downstream work, not
// a restructuring done here.
//
// Rules, pinned by the tests:
//   - Completed sales, observed demand, and stockout unmet demand are
//     recorded SEPARATELY. A store that sells nothing because shelves are
//     empty must NOT infer customers want nothing: zero sales with a
//     stockout records unmet demand, never zero demand.
//   - The same unresolved household need is NOT counted repeatedly as a
//     new purchase attempt. Aggregation groups attempts by the need's own
//     id (household:<id>/need:<sequence>, from HouseholdPurchasingNeed
//     .NeedSequence, which HouseholdNeedRegistry assigns uniquely per
//     registry) where the attempt carries one; where it does not, by the
//     attempt's own id (the F3 receipt id for log-derived attempts); and
//     where neither exists, by household plus item plus business plus day.
//     A group's demand is its FIRST attempt's requested units; later
//     retries add sales but no new demand, and the group's outstanding
//     unmet demand is first-requested minus all supplied so far, counted
//     once. The audit trail itself keeps every attempt; only the
//     aggregates dedup.
//   - A group's outstanding unmet demand is a stockout exactly when the
//     latest attempt in the group that fell short did so with empty
//     shelves (StockWasEmpty); otherwise it is other unmet demand (for
//     example stock-limited partial fills or affordability downsizing).
//   - History is recorded fact, never forecast: daily, weekly (seven-day
//     blocks from day 0), and monthly (thirty-day blocks from day 0)
//     histories sum what was recorded, and the trend describes what
//     happened over the observed window — it never extrapolates beyond
//     it, and it is Unknown when the window holds too little history.
//   - Day-of-week indexing is ((day % 7) + 7) % 7, the same indexing the
//     shopping loop uses (HouseholdShopping.cs:208). Averages cover only
//     days on which demand was actually observed; an unobserved day is
//     not zero demand.
//
// GAPS, recorded not papered over:
//   - CategoryStockState (BusinessRuntimeState.cs:150-182, 273-304) holds
//     LastDailyUnitsSold and WeekToDateUnitsSold per CATEGORY, with no
//     item breakdown and no day attribution beyond last-daily and
//     week-to-date. Those counters cannot be converted into per-item
//     daily history, so this book does not consume them; completed sales
//     per item per day come from the F3 log's retail-purchase records.
//   - The F3 log carries no requested-vs-supplied split, no household
//     id (it names the shopper person, not the household), and no shelf
//     state. FromCustodyLog therefore records observed demand as the
//     sales floor (requested = supplied), household -1, and no stockout
//     — and names each gap in diagnostics. Partial fills and stockouts
//     cannot be derived from the log.
//   - No authority records a household need that never reaches a store
//     (no trip attempted, no counter reached). Such demand is invisible
//     to this book until a site records it; the book never invents it.
//
// UNWIRED BY DESIGN: plain data plus pure queries. No flow consults this
// book, no driver is activated, no shopping loop, Bakery driver, or live
// flow is touched. The book holds its own append-only record list, in the
// shape of the F3 log (bounded store, PROVISIONAL cap, oldest-first
// eviction counted, sequence never reused, duplicate refusal).

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// One household's attempt to buy an item from one business on one
    /// day. RequestedUnits is what the household attempted to buy in THIS
    /// attempt (the need's remaining units at the attempt, for a retry);
    /// SuppliedUnits is what the business actually sold. StockWasEmpty
    /// says the shelves were empty at the attempt — the shortfall is then
    /// a stockout, not a stock-limited partial fill. NeedId is the need's
    /// own id (see <see cref="DemandObservation.NeedKey"/>) where the
    /// attempt carries one; AttemptId is the attempt's own id (the F3
    /// receipt id for log-derived attempts) where it carries one; at least
    /// one of the two, or the household/item/business/day fallback, is
    /// what the book deduplicates on. A struct, like the other small
    /// record types in this layer: the book stamps RecordId on its own
    /// copy when it accepts the attempt.
    /// </summary>
    public struct DemandAttemptRecord
    {
        public DemandAttemptRecord(int dayIndex, string businessId, string itemId, int householdId,
            string needId, string attemptId, int requestedUnits, int suppliedUnits,
            bool stockWasEmpty, string note)
        {
            DayIndex = dayIndex;
            BusinessId = businessId ?? string.Empty;
            ItemId = itemId ?? string.Empty;
            HouseholdId = householdId;
            NeedId = needId ?? string.Empty;
            AttemptId = attemptId ?? string.Empty;
            RequestedUnits = requestedUnits;
            SuppliedUnits = suppliedUnits;
            StockWasEmpty = stockWasEmpty;
            Note = note ?? string.Empty;
        }

        /// <summary>Assigned by the book on acceptance; empty until then.</summary>
        public string RecordId { get; internal set; } = string.Empty;

        public int DayIndex { get; }
        public string BusinessId { get; }
        public string ItemId { get; }
        public int HouseholdId { get; }
        public string NeedId { get; }
        public string AttemptId { get; }
        public int RequestedUnits { get; }
        public int SuppliedUnits { get; }
        public bool StockWasEmpty { get; }
        public string Note { get; }

        /// <summary>Units wanted but not supplied in this attempt.</summary>
        public int UnmetUnits => Math.Max(0, RequestedUnits - SuppliedUnits);

        /// <summary>Some, but not all, of the requested units were supplied.</summary>
        public bool IsPartialFill => SuppliedUnits > 0 && SuppliedUnits < RequestedUnits;

        /// <summary>The attempt fell short with empty shelves.</summary>
        public bool IsStockoutAttempt => StockWasEmpty && UnmetUnits > 0;
    }

    /// <summary>One day of demand for one business plus item.</summary>
    public struct DailyDemandPoint
    {
        public DailyDemandPoint(int dayIndex, int requestedUnits, int suppliedUnits,
            int unmetUnits, int stockoutUnmetUnits)
        {
            DayIndex = dayIndex;
            RequestedUnits = requestedUnits;
            SuppliedUnits = suppliedUnits;
            UnmetUnits = unmetUnits;
            StockoutUnmetUnits = stockoutUnmetUnits;
        }

        public int DayIndex { get; }
        public int RequestedUnits { get; }
        public int SuppliedUnits { get; }
        public int UnmetUnits { get; }
        public int StockoutUnmetUnits { get; }
    }

    /// <summary>
    /// One week (seven-day block from day 0) or month (thirty-day block
    /// from day 0) of demand for one business plus item. The block
    /// boundaries are simulation-calendar blocks from day 0: no authored
    /// calendar exists, so no weekday-aligned week or named month is
    /// claimed.
    /// </summary>
    public struct DemandHistoryPoint
    {
        public DemandHistoryPoint(int periodIndex, int startDayIndex, int requestedUnits,
            int suppliedUnits, int unmetUnits, int stockoutUnmetUnits)
        {
            PeriodIndex = periodIndex;
            StartDayIndex = startDayIndex;
            RequestedUnits = requestedUnits;
            SuppliedUnits = suppliedUnits;
            UnmetUnits = unmetUnits;
            StockoutUnmetUnits = stockoutUnmetUnits;
        }

        public int PeriodIndex { get; }
        public int StartDayIndex { get; }
        public int RequestedUnits { get; }
        public int SuppliedUnits { get; }
        public int UnmetUnits { get; }
        public int StockoutUnmetUnits { get; }
    }

    /// <summary>Which way recorded demand moved over the observed window.</summary>
    public enum DemandTrendDirection
    {
        Unknown = 0,
        Rising = 1,
        Falling = 2,
        Flat = 3,
    }

    /// <summary>
    /// The direction and magnitude of demand change over an observed
    /// window, computed from recorded history only. Magnitude is the
    /// second-half average daily demand minus the first-half average, in
    /// units per day. Unknown means the window holds too little history
    /// to show a direction — the reason names the gap — and no value is
    /// extrapolated beyond the window.
    /// </summary>
    public struct DemandTrend
    {
        public DemandTrend(int windowStartDay, int windowEndDay, int daysObserved,
            double firstHalfAverageUnitsPerDay, double secondHalfAverageUnitsPerDay,
            DemandTrendDirection direction, double magnitudeUnitsPerDay, string reason)
        {
            WindowStartDay = windowStartDay;
            WindowEndDay = windowEndDay;
            DaysObserved = daysObserved;
            FirstHalfAverageUnitsPerDay = firstHalfAverageUnitsPerDay;
            SecondHalfAverageUnitsPerDay = secondHalfAverageUnitsPerDay;
            Direction = direction;
            MagnitudeUnitsPerDay = magnitudeUnitsPerDay;
            Reason = reason ?? string.Empty;
        }

        public static DemandTrend Unknown(int windowStartDay, int windowEndDay, string reason) =>
            new DemandTrend(windowStartDay, windowEndDay, 0, 0.0, 0.0,
                DemandTrendDirection.Unknown, 0.0, reason ?? string.Empty);

        public int WindowStartDay { get; }
        public int WindowEndDay { get; }
        public int DaysObserved { get; }
        public double FirstHalfAverageUnitsPerDay { get; }
        public double SecondHalfAverageUnitsPerDay { get; }
        public DemandTrendDirection Direction { get; }
        public double MagnitudeUnitsPerDay { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Append-only demand observation book, one record per household
    /// purchase attempt. Producers call
    /// <see cref="Record(DemandAttemptRecord, out string)"/>; everyone
    /// else reads the aggregates. The book never decides anything: no
    /// flow reads it, and a recording failure never fails the purchase it
    /// records. Aggregates dedup by need (see the file header); the audit
    /// trail itself keeps every accepted attempt.
    /// </summary>
    public sealed class DemandObservation
    {
        /// <summary>
        /// PROVISIONAL calibration, mirroring the F3 log: the bounded
        /// store holds at most this many records; the oldest is evicted
        /// first when a new record arrives past the cap. 512 is a guess
        /// until a session's real attempt volume is measured — an owner
        /// decision, not a measured limit.
        /// </summary>
        public const int MaxRecords = 512;

        /// <summary>Household id when the attempt's household is unknown (log-derived attempts).</summary>
        public const int UnknownHouseholdId = -1;

        /// <summary>
        /// The need's own id: "household:&lt;householdId&gt;/need:&lt;needSequence&gt;".
        /// NeedSequence is assigned uniquely per registry by
        /// HouseholdNeedRegistry, so this key names one need.
        /// </summary>
        public static string NeedKey(int householdId, int needSequence) =>
            "household:" + householdId + "/need:" + needSequence;

        /// <summary>
        /// Day-of-week index for a simulation day: ((day % 7) + 7) % 7,
        /// the same indexing the shopping loop uses
        /// (HouseholdShopping.cs:208).
        /// </summary>
        public static int DayOfWeekIndex(int dayIndex) => ((dayIndex % 7) + 7) % 7;

        private readonly List<DemandAttemptRecord> records = new List<DemandAttemptRecord>();
        private readonly List<string> diagnostics = new List<string>();

        // Exactly-once index: dedup key plus day -> the RecordId that
        // recorded it. Kept past eviction so a re-record after the
        // original was evicted is still refused; it holds keys, not
        // records, so the 512-record bound still holds for the store.
        private readonly Dictionary<string, string> seenAttemptKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private int nextSequence = 1;
        private int evictedCount;

        /// <summary>Read view of every retained attempt, oldest first (a fresh snapshot each call).</summary>
        public IReadOnlyList<DemandAttemptRecord> Records =>
            new List<DemandAttemptRecord>(records).AsReadOnly();

        public int Count => records.Count;

        /// <summary>Records evicted oldest-first past the cap, since this book was created.</summary>
        public int EvictedCount => evictedCount;

        /// <summary>Refusal, eviction, and derivation-gap notes, in order.</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();

        /// <summary>Appends a missing-data or context note to diagnostics.</summary>
        public void Note(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                diagnostics.Add(message);
            }
        }

        private static string DedupGroupKey(DemandAttemptRecord record)
        {
            if (!string.IsNullOrWhiteSpace(record.NeedId)) return "need:" + record.NeedId;
            if (!string.IsNullOrWhiteSpace(record.AttemptId)) return "attempt:" + record.AttemptId;
            return "household:" + record.HouseholdId
                + "|item:" + record.ItemId
                + "|business:" + record.BusinessId;
        }

        /// <summary>
        /// Appends one attempt. Returns false with a diagnostic — and
        /// records nothing — when the record is null, names no business or
        /// item, requests no positive quantity, supplies a negative
        /// quantity or more than requested, carries no day, or repeats an
        /// attempt already recorded (same dedup key plus day). On
        /// acceptance the record's RecordId is stamped "demand-{sequence}"
        /// (the sequence never reuses a number, even across eviction) and,
        /// past the cap, the oldest record is evicted first.
        /// </summary>
        public bool Record(DemandAttemptRecord? record, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (record == null)
            {
                diagnostic = "DemandObservation: null record refused — nothing recorded.";
                diagnostics.Add(diagnostic);
                return false;
            }
            DemandAttemptRecord value = record.Value;
            if (string.IsNullOrWhiteSpace(value.BusinessId))
            {
                diagnostic = "DemandObservation: record refused — BusinessId is empty, so the attempt is not attributable to a business.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (string.IsNullOrWhiteSpace(value.ItemId))
            {
                diagnostic = "DemandObservation: record refused — ItemId is empty, so the attempt is not attributable to an item.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (value.RequestedUnits <= 0)
            {
                diagnostic = $"DemandObservation: record refused — the attempt requests {value.RequestedUnits} units; an attempt requests a positive quantity.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (value.SuppliedUnits < 0 || value.SuppliedUnits > value.RequestedUnits)
            {
                diagnostic = $"DemandObservation: record refused — the attempt supplies {value.SuppliedUnits} of {value.RequestedUnits} requested; a sale cannot supply more than the attempt requested.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (value.DayIndex < 0)
            {
                diagnostic = "DemandObservation: record refused — DayIndex is negative, so the attempt cannot be placed in history.";
                diagnostics.Add(diagnostic);
                return false;
            }

            string attemptKey = DedupGroupKey(value) + "\n" + value.DayIndex;
            if (seenAttemptKeys.TryGetValue(attemptKey, out string originalRecordId))
            {
                diagnostic = $"DemandObservation: duplicate attempt refused — already recorded as {originalRecordId}; the book never double-records an attempt.";
                diagnostics.Add(diagnostic);
                return false;
            }

            value.RecordId = "demand-" + nextSequence;
            nextSequence++;
            seenAttemptKeys[attemptKey] = value.RecordId;

            if (records.Count >= MaxRecords)
            {
                DemandAttemptRecord evicted = records[0];
                records.RemoveAt(0);
                evictedCount++;
                diagnostics.Add($"DemandObservation: evicted oldest record {evicted.RecordId} past the {MaxRecords}-record cap; {evictedCount} evicted so far.");
            }

            records.Add(value);
            return true;
        }

        // One need's attempts, in record order: the first attempt's
        // requested units are the need's demand; later retries add sales
        // but no new demand.
        private sealed class DemandGroup
        {
            public DemandAttemptRecord First;
            public int SuppliedTotal;
            public DemandAttemptRecord LatestShortfall;
            public bool HasShortfall;
        }

        private List<DemandGroup> GroupsFor(string businessId, string itemId)
        {
            var groups = new List<DemandGroup>();
            var byKey = new Dictionary<string, DemandGroup>(StringComparer.Ordinal);
            foreach (DemandAttemptRecord record in records)
            {
                if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                string key = DedupGroupKey(record);
                if (!byKey.TryGetValue(key, out DemandGroup group))
                {
                    group = new DemandGroup { First = record };
                    byKey[key] = group;
                    groups.Add(group);
                }
                group.SuppliedTotal += record.SuppliedUnits;
                if (record.RequestedUnits > record.SuppliedUnits)
                {
                    group.LatestShortfall = record;
                    group.HasShortfall = true;
                }
            }
            return groups;
        }

        private static int GroupUnmet(DemandGroup group) =>
            Math.Max(0, group.First.RequestedUnits - group.SuppliedTotal);

        private static int GroupStockoutUnmet(DemandGroup group)
        {
            int unmet = GroupUnmet(group);
            if (unmet <= 0) return 0;
            if (!group.HasShortfall) return 0;
            return group.LatestShortfall.StockWasEmpty ? unmet : 0;
        }

        /// <summary>Units actually sold on one day (every attempt's supplied units, summed).</summary>
        public int CompletedSales(string businessId, string itemId, int dayIndex)
        {
            int total = 0;
            foreach (DemandAttemptRecord record in records)
            {
                if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                if (record.DayIndex != dayIndex) continue;
                total += record.SuppliedUnits;
            }
            return total;
        }

        /// <summary>Units actually sold across all recorded days.</summary>
        public int CompletedSalesTotal(string businessId, string itemId)
        {
            int total = 0;
            foreach (DemandAttemptRecord record in records)
            {
                if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                total += record.SuppliedUnits;
            }
            return total;
        }

        /// <summary>
        /// Units households attempted to buy on one day. Each need counts
        /// once, on the day of its first attempt — a retry on a later day
        /// adds no new demand.
        /// </summary>
        public int ObservedDemand(string businessId, string itemId, int dayIndex)
        {
            int total = 0;
            foreach (DemandGroup group in GroupsFor(businessId, itemId))
            {
                if (group.First.DayIndex == dayIndex) total += group.First.RequestedUnits;
            }
            return total;
        }

        /// <summary>Units households attempted to buy across all recorded days (each need once).</summary>
        public int ObservedDemandTotal(string businessId, string itemId)
        {
            int total = 0;
            foreach (DemandGroup group in GroupsFor(businessId, itemId))
            {
                total += group.First.RequestedUnits;
            }
            return total;
        }

        /// <summary>Units wanted but not supplied because shelves were empty, on one day.</summary>
        public int StockoutUnmetDemand(string businessId, string itemId, int dayIndex)
        {
            int total = 0;
            foreach (DemandGroup group in GroupsFor(businessId, itemId))
            {
                if (group.First.DayIndex == dayIndex) total += GroupStockoutUnmet(group);
            }
            return total;
        }

        /// <summary>Units wanted but not supplied because shelves were empty, across all recorded days.</summary>
        public int StockoutUnmetDemandTotal(string businessId, string itemId)
        {
            int total = 0;
            foreach (DemandGroup group in GroupsFor(businessId, itemId))
            {
                total += GroupStockoutUnmet(group);
            }
            return total;
        }

        /// <summary>
        /// Units wanted but not supplied for a reason other than empty
        /// shelves (stock-limited partial fills, affordability downsizing),
        /// across all recorded days.
        /// </summary>
        public int OtherUnmetDemandTotal(string businessId, string itemId)
        {
            int total = 0;
            foreach (DemandGroup group in GroupsFor(businessId, itemId))
            {
                total += GroupUnmet(group) - GroupStockoutUnmet(group);
            }
            return total;
        }

        /// <summary>Attempts that filled fewer units than requested, in record order.</summary>
        public IReadOnlyList<DemandAttemptRecord> PartialFills(string businessId, string itemId)
        {
            var fills = new List<DemandAttemptRecord>();
            foreach (DemandAttemptRecord record in records)
            {
                if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                if (record.IsPartialFill) fills.Add(record);
            }
            return fills.AsReadOnly();
        }

        /// <summary>
        /// One point per day with at least one attempt, oldest first.
        /// Requested and unmet are attributed to the day a need was first
        /// observed; supplied is attributed to the day the sale happened.
        /// A retry-only day therefore shows zero requested and zero unmet
        /// — the demand was already counted on its first day.
        /// </summary>
        public IReadOnlyList<DailyDemandPoint> DailyHistory(string businessId, string itemId)
        {
            var days = new SortedSet<int>();
            foreach (DemandAttemptRecord record in records)
            {
                if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                days.Add(record.DayIndex);
            }
            var points = new List<DailyDemandPoint>();
            foreach (int day in days)
            {
                int requested = 0;
                int supplied = 0;
                int unmet = 0;
                int stockoutUnmet = 0;
                foreach (DemandGroup group in GroupsFor(businessId, itemId))
                {
                    if (group.First.DayIndex == day)
                    {
                        requested += group.First.RequestedUnits;
                        unmet += GroupUnmet(group);
                        stockoutUnmet += GroupStockoutUnmet(group);
                    }
                }
                foreach (DemandAttemptRecord record in records)
                {
                        if (!string.Equals(record.BusinessId, businessId, StringComparison.Ordinal)) continue;
                    if (!string.Equals(record.ItemId, itemId, StringComparison.Ordinal)) continue;
                    if (record.DayIndex == day) supplied += record.SuppliedUnits;
                }
                points.Add(new DailyDemandPoint(day, requested, supplied, unmet, stockoutUnmet));
            }
            return points.AsReadOnly();
        }

        private IReadOnlyList<DemandHistoryPoint> GroupedHistory(string businessId, string itemId, int blockDays)
        {
            var blocks = new SortedDictionary<int, DemandHistoryPoint>();
            foreach (DailyDemandPoint day in DailyHistory(businessId, itemId))
            {
                int index = day.DayIndex / blockDays;
                if (!blocks.TryGetValue(index, out DemandHistoryPoint point))
                {
                    point = new DemandHistoryPoint(index, index * blockDays, 0, 0, 0, 0);
                }
                blocks[index] = new DemandHistoryPoint(
                    point.PeriodIndex, point.StartDayIndex,
                    point.RequestedUnits + day.RequestedUnits,
                    point.SuppliedUnits + day.SuppliedUnits,
                    point.UnmetUnits + day.UnmetUnits,
                    point.StockoutUnmetUnits + day.StockoutUnmetUnits);
            }
            var result = new List<DemandHistoryPoint>();
            foreach (KeyValuePair<int, DemandHistoryPoint> entry in blocks)
            {
                result.Add(entry.Value);
            }
            return result.AsReadOnly();
        }

        /// <summary>Demand summed per seven-day block from day 0, oldest block first.</summary>
        public IReadOnlyList<DemandHistoryPoint> WeeklyHistory(string businessId, string itemId) =>
            GroupedHistory(businessId, itemId, 7);

        /// <summary>Demand summed per thirty-day block from day 0, oldest block first.</summary>
        public IReadOnlyList<DemandHistoryPoint> MonthlyHistory(string businessId, string itemId) =>
            GroupedHistory(businessId, itemId, 30);

        /// <summary>
        /// Average requested units per day-of-week index, over days on
        /// which demand was actually observed. An index with no observed
        /// day averages 0.0 — no observation, not observed-zero demand.
        /// </summary>
        public double[] DayOfWeekPattern(string businessId, string itemId)
        {
            var sums = new double[7];
            var counts = new int[7];
            foreach (DailyDemandPoint day in DailyHistory(businessId, itemId))
            {
                // Only days where a need was first observed carry new
                // demand; a retry-only day contributes nothing here.
                if (day.RequestedUnits <= 0) continue;
                int index = DayOfWeekIndex(day.DayIndex);
                sums[index] += day.RequestedUnits;
                counts[index]++;
            }
            var pattern = new double[7];
            for (int i = 0; i < 7; i++)
            {
                pattern[i] = counts[i] > 0 ? sums[i] / counts[i] : 0.0;
            }
            return pattern;
        }

        /// <summary>
        /// The trend over the window ending at asOfDay (inclusive):
        /// the window splits into two halves by day, and the averages
        /// cover only days on which demand was first observed — an
        /// unobserved day is not zero demand. Unknown when the window is
        /// shorter than two days, when fewer than two days were observed,
        /// or when either half holds no observed day.
        /// </summary>
        public DemandTrend Trend(string businessId, string itemId, int windowDays, int asOfDay)
        {
            int start = asOfDay - windowDays + 1;
            int end = asOfDay;
            if (windowDays < 2)
            {
                return DemandTrend.Unknown(start, end,
                    "trend needs a window of at least two days; the window holds " + windowDays + ".");
            }
            int split = start + windowDays / 2;
            double firstSum = 0.0;
            int firstDays = 0;
            double secondSum = 0.0;
            int secondDays = 0;
            foreach (DailyDemandPoint day in DailyHistory(businessId, itemId))
            {
                if (day.DayIndex < start || day.DayIndex > end) continue;
                if (day.RequestedUnits <= 0) continue;
                if (day.DayIndex < split)
                {
                    firstSum += day.RequestedUnits;
                    firstDays++;
                }
                else
                {
                    secondSum += day.RequestedUnits;
                    secondDays++;
                }
            }
            int observed = firstDays + secondDays;
            if (observed < 2 || firstDays == 0 || secondDays == 0)
            {
                return DemandTrend.Unknown(start, end,
                    $"trend needs history in both halves of the window; {observed} day(s) observed in days {start}-{end}.");
            }
            double firstAverage = firstSum / firstDays;
            double secondAverage = secondSum / secondDays;
            double magnitude = secondAverage - firstAverage;
            DemandTrendDirection direction = magnitude > 0.0
                ? DemandTrendDirection.Rising
                : magnitude < 0.0 ? DemandTrendDirection.Falling : DemandTrendDirection.Flat;
            return new DemandTrend(start, end, observed, firstAverage, secondAverage,
                direction, magnitude,
                $"recorded demand averaged {firstAverage:0.##} units/day in days {start}-{split - 1} " +
                $"and {secondAverage:0.##} units/day in days {split}-{end}.");
        }

        /// <summary>
        /// Average requested units per day over the window ending at
        /// asOfDay, counting only days on which demand was first observed.
        /// False when the window holds no observed day — no observation is
        /// not zero demand.
        /// </summary>
        public bool TryAverageDailyDemand(string businessId, string itemId, int windowDays,
            int asOfDay, out double averageUnitsPerDay)
        {
            averageUnitsPerDay = 0.0;
            if (windowDays <= 0) return false;
            int start = asOfDay - windowDays + 1;
            double sum = 0.0;
            int days = 0;
            foreach (DailyDemandPoint day in DailyHistory(businessId, itemId))
            {
                if (day.DayIndex < start || day.DayIndex > asOfDay) continue;
                if (day.RequestedUnits <= 0) continue;
                sum += day.RequestedUnits;
                days++;
            }
            if (days == 0) return false;
            averageUnitsPerDay = sum / days;
            return true;
        }

        /// <summary>
        /// Population standard deviation of daily requested units over the
        /// window ending at asOfDay, counting only days on which demand
        /// was first observed. False when fewer than two days were
        /// observed — a spread cannot be measured from one day.
        /// </summary>
        public bool TryDailyDemandStdDev(string businessId, string itemId, int windowDays,
            int asOfDay, out double stdDevUnitsPerDay)
        {
            stdDevUnitsPerDay = 0.0;
            if (windowDays <= 0) return false;
            int start = asOfDay - windowDays + 1;
            var daily = new List<double>();
            foreach (DailyDemandPoint day in DailyHistory(businessId, itemId))
            {
                if (day.DayIndex < start || day.DayIndex > asOfDay) continue;
                if (day.RequestedUnits <= 0) continue;
                daily.Add(day.RequestedUnits);
            }
            if (daily.Count < 2) return false;
            double mean = 0.0;
            foreach (double value in daily) mean += value;
            mean /= daily.Count;
            double squared = 0.0;
            foreach (double value in daily) squared += (value - mean) * (value - mean);
            stdDevUnitsPerDay = Math.Sqrt(squared / daily.Count);
            return true;
        }

        /// <summary>
        /// Builds an observation book from the F3 custody log's completed
        /// retail-purchase transfers: each dated purchase becomes one
        /// attempt with requested equal to supplied (the log carries no
        /// requested-vs-supplied split, so observed demand is the sales
        /// floor), household unknown, and no stockout (the log carries no
        /// shelf state). The attempt id is the purchase's receipt id, so
        /// each purchase is its own group. Records that are not purchases,
        /// and purchases with no day, are skipped and the skip is named in
        /// diagnostics, along with the three derivation gaps.
        /// </summary>
        public static DemandObservation FromCustodyLog(CustodyTransferLog log)
        {
            var book = new DemandObservation();
            if (log == null)
            {
                book.Note("DemandObservation: no custody log supplied — the book holds no derived sales.");
                return book;
            }
            foreach (CustodyTransferRecord record in log.Records)
            {
                if (!string.Equals(record.CauseKind, CustodyTransferLog.CauseRetailPurchase, StringComparison.Ordinal))
                {
                    continue;
                }
                if (record.DayIndex < 0)
                {
                    book.Note($"DemandObservation: skipped retail-purchase {record.CauseId} — it carries no day, so it cannot be placed in history.");
                    continue;
                }
                string businessId = record.FromCustodian ?? string.Empty;
                const string businessPrefix = "business:";
                if (businessId.StartsWith(businessPrefix, StringComparison.Ordinal))
                {
                    businessId = businessId.Substring(businessPrefix.Length);
                }
                if (string.IsNullOrWhiteSpace(businessId))
                {
                    book.Note($"DemandObservation: skipped retail-purchase {record.CauseId} — it names no selling business.");
                    continue;
                }
                book.Record(new DemandAttemptRecord(
                    record.DayIndex, businessId, record.ItemId, UnknownHouseholdId,
                    string.Empty, "receipt:" + record.CauseId,
                    record.Units, record.Units, false,
                    "derived from custody log retail-purchase " + record.CauseId),
                    out string diagnostic);
                if (!string.IsNullOrEmpty(diagnostic))
                {
                    book.Note(diagnostic);
                }
            }
            book.Note("DemandObservation: the custody log carries no requested-vs-supplied split, so derived observed demand is the sales floor (requested recorded as supplied); partial fills cannot be derived from it.");
            book.Note("DemandObservation: the custody log names the shopper person, not the household, so derived attempts carry household -1 (unknown).");
            book.Note("DemandObservation: the custody log carries no shelf state, so no stockout unmet demand can be derived from it.");
            return book;
        }
    }
}
