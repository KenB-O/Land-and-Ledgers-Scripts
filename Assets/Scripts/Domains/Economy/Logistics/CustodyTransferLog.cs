// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Priority 9 Slice F3 — custody and provenance recording (P9 plan section 2.4,
// section 4 Slice F3, directive Priority D). An append-only audit log of
// physical custody transfers: who holds which lot, and where it came from.
//
// The log is WRITE-ONLY for producers and READ-ONLY for everyone else. No
// flow consults the log to decide anything in this campaign — it is an audit
// trail, not a control. Ownership is NEVER recorded here; ownership stays on
// the lot owning authority (the household inventory, the Bakery bin, the
// retail lots). The log records custody (who holds) and provenance (which
// lot, from where). If a transfer also changes ownership, the owning
// authority records that change in its own fields; the log only records that
// the transfer happened.
//
// Exactly-once discipline: each record's CauseId is the idempotency key of
// the transfer it records, and a second record with the same CauseKind plus
// CauseId is refused — the log never double-records a transfer. The
// duplicate key survives eviction (a small seen-key index, not records), so
// a re-record after the original was evicted is still refused.
//
// Runtime-only in this slice: NOT persisted. The store is bounded at 512
// records (PROVISIONAL calibration — the cap is a guess until a session's
// real transfer volume is measured; see MaxRecords). Eviction is
// oldest-first and counted in diagnostics, like the task-duration
// diagnostics store.
//
// A record's DayIndex is the simulation day when the recording site knows
// it; UnknownDayIndex (-1) when the site holds no day (the freight-commit
// delegate path carries no day — see MillbrookFlourFreightCustodyAdapter).
// A LotKey side is Empty when that side has no lot: the source of a
// creation event (bake, Millbrook order), the destination of a consumption
// or a batch custody (dispense into a dough batch, freight into wagon
// custody), or a key the site does not hold (the retail-purchase source —
// a category-pool sale carries no retail lot id; the household-receipt
// destination — HouseholdInventory.AddLot returns no lot id). An empty side
// is recorded as Empty and the missing key is named in diagnostics; the
// site is never restructured to fetch it.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// The identity of one lot in one lot-id space. Both parts are strings
    /// and a bare lot id is never a key on its own: the five lot-id spaces
    /// never share a key.
    ///   - household lots: authority "household-inventory", lot id the
    ///     household LotId (int, scoped per household) or a carried batch
    ///     id ("carry-&lt;day&gt;-&lt;seq&gt;") while the goods are in transit;
    ///   - retail lots: authority "business:&lt;businessId&gt;", lot id the
    ///     retail lot id ("retail-feed-" + supplier lot id);
    ///   - Millbrook lots: authority "millbrook:&lt;businessId&gt;", lot id
    ///     the cargo lot id ("aidlot-" + BusinessId + ":" + requestId);
    ///   - named-SKU lots: authority "named-sku:&lt;businessId&gt;", lot id
    ///     the procurement lot id ("lot-" + receipt id);
    ///   - Bakery flour and bread lots: authority "bakery:&lt;businessInstanceId&gt;",
    ///     lot id the lot EntityId, or "batch:&lt;batchId&gt;" for a dough
    ///     batch's custody.
    /// In-transit freight custody is keyed "freight:&lt;shipmentId&gt;" where
    /// a lot key is needed for the shipment itself.
    /// </summary>
    public struct LotKey : IEquatable<LotKey>
    {
        public LotKey(string authorityId, string lotId)
        {
            AuthorityId = authorityId ?? string.Empty;
            LotId = lotId ?? string.Empty;
        }

        public string AuthorityId { get; }
        public string LotId { get; }

        /// <summary>No lot on this side of the transfer (creation, consumption, batch/wagon custody, or a key the site does not hold).</summary>
        public static readonly LotKey Empty = new LotKey(string.Empty, string.Empty);

        public bool IsEmpty => string.IsNullOrEmpty(AuthorityId) && string.IsNullOrEmpty(LotId);

        public bool Equals(LotKey other) =>
            string.Equals(AuthorityId, other.AuthorityId, StringComparison.Ordinal)
            && string.Equals(LotId, other.LotId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LotKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(AuthorityId ?? string.Empty);
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(LotId ?? string.Empty);
                return hash;
            }
        }

        public static bool operator ==(LotKey left, LotKey right) => left.Equals(right);
        public static bool operator !=(LotKey left, LotKey right) => !left.Equals(right);

        public override string ToString() => IsEmpty ? "<empty>" : AuthorityId + "/" + LotId;
    }

    /// <summary>
    /// One custody transfer, as recorded. RecordId is assigned by the log on
    /// acceptance ("custody-{sequence}"); callers build the rest from values
    /// the transfer site already holds. Units are in the unit that leg moves
    /// (sacks on a freight commit, pounds on a Bakery receipt, loaves on a
    /// bake) — a leg that changes unit keeps its own unit, and conservation
    /// is judged per leg, never by adding unlike units.
    /// </summary>
    public sealed class CustodyTransferRecord
    {
        public CustodyTransferRecord(
            int dayIndex, string itemId, int units,
            LotKey sourceLotKey, LotKey destinationLotKey,
            string fromCustodian, string toCustodian,
            string causeId, string causeKind)
        {
            DayIndex = dayIndex;
            ItemId = itemId ?? string.Empty;
            Units = units;
            SourceLotKey = sourceLotKey;
            DestinationLotKey = destinationLotKey;
            FromCustodian = fromCustodian ?? string.Empty;
            ToCustodian = toCustodian ?? string.Empty;
            CauseId = causeId ?? string.Empty;
            CauseKind = causeKind ?? string.Empty;
        }

        /// <summary>Assigned by the log on acceptance; empty until then.</summary>
        public string RecordId { get; internal set; } = string.Empty;

        public int DayIndex { get; }
        public string ItemId { get; }
        public int Units { get; }
        public LotKey SourceLotKey { get; }
        public LotKey DestinationLotKey { get; }
        public string FromCustodian { get; }
        public string ToCustodian { get; }
        public string CauseId { get; }
        public string CauseKind { get; }
    }

    /// <summary>
    /// Append-only custody transfer log. Producers call
    /// <see cref="Record(CustodyTransferRecord, out string)"/>; everyone
    /// else reads <see cref="Records"/>, <see cref="LatestForLot(LotKey)"/>
    /// or <see cref="LatestForCause(string, string)"/>. The log never
    /// decides anything: no flow reads it, and a logging failure never fails
    /// the transfer it records (call sites ignore the return except to
    /// diagnostics).
    /// </summary>
    public sealed class CustodyTransferLog
    {
        /// <summary>
        /// PROVISIONAL calibration: the bounded store holds at most this many
        /// records; the oldest is evicted first when a new record arrives
        /// past the cap. 512 is a guess until a session's real transfer
        /// volume is measured — an owner decision, not a measured limit.
        /// </summary>
        public const int MaxRecords = 512;

        /// <summary>DayIndex value when the recording site holds no day.</summary>
        public const int UnknownDayIndex = -1;

        /// <summary>CauseKind values — one per transfer point, matching the P9 plan.</summary>
        public const string CauseFreightCommit = "freight-commit";
        public const string CauseFreightReceive = "freight-receive";
        public const string CauseRetailPurchase = "retail-purchase";
        public const string CauseHouseholdReceipt = "household-receipt";
        public const string CauseBakeryDispense = "bakery-dispense";
        public const string CauseBakeryBake = "bakery-bake";
        public const string CauseMillbrookOrder = "millbrook-order";
        public const string CauseRetailReceipt = "retail-receipt";

        /// <summary>Authority id for household lots and carried batches.</summary>
        public const string HouseholdInventoryAuthority = "household-inventory";

        public static string BusinessAuthority(string businessId) => "business:" + (businessId ?? string.Empty);
        public static string BakeryAuthority(string businessInstanceId) => "bakery:" + (businessInstanceId ?? string.Empty);
        public static string MillbrookAuthority(string businessId) => "millbrook:" + (businessId ?? string.Empty);
        public static string NamedSkuAuthority(string businessId) => "named-sku:" + (businessId ?? string.Empty);
        public static string FreightAuthority(string shipmentId) => "freight:" + (shipmentId ?? string.Empty);

        /// <summary>Custodian naming vocabulary (P9 plan section 2.4).</summary>
        public static string BusinessCustodian(string businessId) => "business:" + (businessId ?? string.Empty);
        public static string HouseholdCustodian(int householdId) => "household:" + householdId;
        public static string PersonCustodian(int personId) => "person:" + personId;
        public static string WagonCustodian(string assetId) => "wagon:" + (assetId ?? string.Empty);
        public static string BatchCustodian(string batchId) => "batch:" + (batchId ?? string.Empty);
        public static string SupplierCustodian(string supplierId) => "supplier:" + (supplierId ?? string.Empty);

        private readonly List<CustodyTransferRecord> records = new List<CustodyTransferRecord>();
        private readonly List<string> diagnostics = new List<string>();

        // Exactly-once index: CauseKind + CauseId -> the RecordId that
        // recorded it. Kept past eviction so a re-record after the original
        // was evicted is still refused; it holds keys, not records, so the
        // 512-record bound still holds for the store itself.
        private readonly Dictionary<string, string> seenCauseKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private int nextSequence = 1;
        private int evictedCount;

        /// <summary>Read view of every retained record, oldest first (a fresh snapshot each call).</summary>
        public IReadOnlyList<CustodyTransferRecord> Records =>
            new List<CustodyTransferRecord>(records).AsReadOnly();

        public int Count => records.Count;

        /// <summary>Records evicted oldest-first past the cap, since this log was created.</summary>
        public int EvictedCount => evictedCount;

        /// <summary>Refusal, eviction, and missing-key notes, in order.</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics.AsReadOnly();

        /// <summary>
        /// Appends a missing-key or context note to diagnostics. Call sites
        /// use this where they hold no diag list of their own.
        /// </summary>
        public void Note(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                diagnostics.Add(message);
            }
        }

        /// <summary>
        /// Appends one record. Returns false with a diagnostic — and records
        /// nothing — when the record is null, has no idempotency key
        /// (empty CauseKind or CauseId), moves no positive units, or repeats
        /// a CauseKind plus CauseId already recorded. On acceptance the
        /// record's RecordId is stamped "custody-{sequence}" (the sequence
        /// never reuses a number, even across eviction) and, past the cap,
        /// the oldest record is evicted first.
        /// </summary>
        public bool Record(CustodyTransferRecord record, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (record == null)
            {
                diagnostic = "CustodyTransferLog: null record refused — nothing recorded.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (string.IsNullOrWhiteSpace(record.CauseKind))
            {
                diagnostic = "CustodyTransferLog: record refused — CauseKind is empty, so the transfer has no kind to deduplicate on.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (string.IsNullOrWhiteSpace(record.CauseId))
            {
                diagnostic = "CustodyTransferLog: record refused — CauseId is empty, so the transfer has no idempotency key.";
                diagnostics.Add(diagnostic);
                return false;
            }
            if (record.Units <= 0)
            {
                diagnostic = $"CustodyTransferLog: record refused — {record.CauseKind}/{record.CauseId} moves {record.Units} units; a transfer moves a positive count.";
                diagnostics.Add(diagnostic);
                return false;
            }

            string causeKey = record.CauseKind + "\n" + record.CauseId;
            if (seenCauseKeys.TryGetValue(causeKey, out string originalRecordId))
            {
                diagnostic = $"CustodyTransferLog: duplicate {record.CauseKind}/{record.CauseId} refused — already recorded as {originalRecordId}; the log never double-records a transfer.";
                diagnostics.Add(diagnostic);
                return false;
            }

            record.RecordId = "custody-" + nextSequence;
            nextSequence++;
            seenCauseKeys[causeKey] = record.RecordId;

            if (records.Count >= MaxRecords)
            {
                CustodyTransferRecord evicted = records[0];
                records.RemoveAt(0);
                evictedCount++;
                diagnostics.Add($"CustodyTransferLog: evicted oldest record {evicted.RecordId} ({evicted.CauseKind}/{evicted.CauseId}) past the {MaxRecords}-record cap; {evictedCount} evicted so far.");
            }

            records.Add(record);
            return true;
        }

        /// <summary>
        /// The latest record touching the lot — as source or destination —
        /// or null when no retained record touches it. Answers "who holds
        /// this lot?" through the returned record's ToCustodian.
        /// </summary>
        public CustodyTransferRecord LatestForLot(LotKey key)
        {
            if (key.IsEmpty) return null;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                CustodyTransferRecord record = records[i];
                if (record == null) continue;
                if (record.SourceLotKey.Equals(key) || record.DestinationLotKey.Equals(key))
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>The record for one transfer's idempotency key, or null when it was never recorded.</summary>
        public CustodyTransferRecord LatestForCause(string causeKind, string causeId)
        {
            if (string.IsNullOrWhiteSpace(causeKind) || string.IsNullOrWhiteSpace(causeId)) return null;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                CustodyTransferRecord record = records[i];
                if (record == null) continue;
                if (string.Equals(record.CauseKind, causeKind, StringComparison.Ordinal)
                    && string.Equals(record.CauseId, causeId, StringComparison.Ordinal))
                {
                    return record;
                }
            }
            return null;
        }
    }
}
