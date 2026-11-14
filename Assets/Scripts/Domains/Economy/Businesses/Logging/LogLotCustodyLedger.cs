using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using UnityEngine;

// PORT NOTE (F6 Tier 2, 2026-11-13): ported from the forestry branch
// (main repo at 346d8746, 574 lines) onto this line's baseline 2cb4781,
// following the F6 Tier 1 counters port. No logic change except the two
// deviations below.
//
// DEVIATION 1 (record fields): LogLotCustodyRecord gains OwnerId and
// MassKg, placed after LogUnits. This line's LogLot is a superset of the
// forestry shape (TimberChain.cs:38): it carries MassKg and OwnerId
// ("timber-rights holder that owns the felled inventory"), and a record
// that drops them is not a faithful snapshot of the lot it tracks — a
// lot rebuilt from a record without OwnerId would be refused by
// RunSawing for a blank owner (plan 20 §9). OwnerId is frozen at
// registration (the felling holder/owner) while CustodianId moves at
// every transition, so OwnerId must never be read as "current holder".
//
// DEVIATION 2 (RegisterLotFromFields): the overload gains one parameter,
// float massKg, appended after dayIndex (11 parameters total), and sets
// OwnerId = custodianId (plan 20 §9: the registration custodian IS the
// felling holder/owner) and MassKg = massKg. RegisterLot copies both
// fields from the live lot. CopyRecord copies both fields, so the save
// round trip preserves them. The load skip set is UNCHANGED (blank
// OwnerId or zero MassKg never skips a record): the fields are snapshot
// provenance, not load gates.
//
// CORRECTION to the in-body doc comments below: their F2/F3/F4/F5
// references are stale on this line. There is no F2 felling driver, no
// F4/F5 haul driver, and no production caller of the later transitions
// yet. The native delivery path is LogHaul.DeliverToMill plus
// LoggingOperationRuntime.DeliverArrivedHauls, which knows nothing of
// the ledger; production DOES fell lots through
// LoggingOperationRuntime.cs:155 / LoggingStandRegistry.FellLogs, but
// nothing registers a lot from production in Tier 2 — the F6 driver
// calls MarkProcessed at the completion step only, and registration
// from the live path is a follow-up slice (Agent 3 note 96, Q1
// GUARD-ONLY). The stale "future driver" sentences are corrected here,
// not carried as live claims, and the body wording is otherwise kept
// verbatim.

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// F3: custody states for one felled log lot. Created means felled and
    /// staged at the harvest location, not yet collected. Collected means a
    /// carrier has taken custody at the harvest location. Loaded means the
    /// lot is loaded on a wagon. InTransit means the lot is on the road
    /// with the carrier. Delivered means the lot has been received into the
    /// sawmill log stock. Processed means the lot has been consumed by
    /// sawing. There is no failed or cancelled state in this slice: a
    /// refused step leaves the state unchanged.
    /// </summary>
    public enum LogLotCustodyState
    {
        Created = 0,
        Collected = 1,
        Loaded = 2,
        InTransit = 3,
        Delivered = 4,
        Processed = 5,
    }

    /// <summary>
    /// F3: one custody record for one felled LogLot. The ledger is a custody
    /// overlay, not a second owner of the lot objects: it stores snapshot
    /// fields only, never the live LogLot object, so one lot is never in
    /// two ownerships and the ledger can never mutate a lot or mint a
    /// second lot. LogUnits, StandId, Species, FelledBy and FelledDayIndex
    /// are copied from the lot at registration and never changed
    /// afterwards, so provenance is frozen at creation. HarvestLocationId
    /// is supplied by the caller, never a grid coordinate computed by the
    /// ledger and never a teleported position, and it never changes after
    /// registration. CustodianId is a worker or business identifier string:
    /// at registration it is the caller supplied custodian, at collection
    /// it becomes the collector, at transit it becomes the carrier, at
    /// delivery it becomes the receiving mill business id, at processing
    /// it becomes the processor. SourceTreeKey is the F2 tree key when the
    /// lot came from a world tree harvest, and empty when the lot came
    /// from a route with no tree; an empty value is recorded as empty and
    /// never invented. Day indexes default to -1 and are set once when
    /// their transition happens, and are never reset by a later
    /// transition.
    /// </summary>
    [Serializable]
    public sealed class LogLotCustodyRecord
    {
        public string LotId = string.Empty;
        public int LogUnits;
        public string OwnerId = string.Empty;
        public float MassKg;
        public string StandId = string.Empty;
        public string Species = string.Empty;
        public int FelledByKind;
        public int FelledByNumber;
        public int FelledDayIndex = -1;
        public string HarvestLocationId = string.Empty;
        public string CustodianId = string.Empty;
        public LogLotCustodyState State = LogLotCustodyState.Created;
        public string ReservedBy = string.Empty;
        public int ReservedDayIndex = -1;
        public int CollectedDayIndex = -1;
        public int LoadedDayIndex = -1;
        public int InTransitDayIndex = -1;
        public int DeliveredDayIndex = -1;
        public int ProcessedDayIndex = -1;
        public string SourceTreeKey = string.Empty;

        public LogLotCustodyRecord() { }
    }

    /// <summary>
    /// F3: the log lot custody ledger — one custody record per felled lot,
    /// tracking where the lot is, who holds it, what state it is in, and
    /// who has reserved it. What this owns:
    /// - registration from a live LogLot or from fields, so the F2 felling
    ///   driver output can register a lot without returning the live lot
    ///   object, and the two overloads produce identical records for the
    ///   same inputs;
    /// - the exclusive reservation: exactly one reserver holds a Created
    ///   lot, and only that reserver may collect it while it is reserved;
    /// - the exactly-once collection: CollectLot moves Created to
    ///   Collected, an unreserved lot is claimed by the first collector,
    ///   and a retried collection by the same collector returns success
    ///   without creating a second record;
    /// - the later state moves LoadLot, MarkInTransit, MarkDelivered and
    ///   MarkProcessed as guarded single-predecessor transitions, built
    ///   now so F4 and F5 can drive them, but invoked by no F3 caller;
    /// - the save pair, following the smith behaviour: clear then load,
    ///   a null DTO clears to empty, first record wins on duplicate lot
    ///   ids, blank lot ids, out-of-range states and empty lots are
    ///   skipped with a diagnostic.
    ///
    /// What this does NOT own: the lot objects themselves (landing stock
    /// in LoggingOperationRuntime owns staged lots, SawmillLogStock owns
    /// received lots), the felling task (F2 driver), the wagon or the
    /// travel (F5), or the sawing run (F6). MarkDelivered never calls
    /// SawmillLogStock ReceiveLogLot itself and MarkProcessed never calls
    /// RunSawing itself: the caller runs its own authority first and
    /// calls the ledger only when that authority succeeds.
    /// </summary>
    public sealed class LogLotCustodyLedger
    {
        private readonly Dictionary<string, LogLotCustodyRecord> lots =
            new Dictionary<string, LogLotCustodyRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> diagnostics;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int LotCount => lots.Count;

        public LogLotCustodyLedger(List<string> diagnostics)
        {
            this.diagnostics = diagnostics ?? new List<string>();
        }

        private string Refuse(string method, string lotId, string reason)
        {
            string refusal = "LogLotCustodyLedger." + method + ": lot '" + (lotId ?? string.Empty) + "' — " + reason + ".";
            diagnostics.Add(refusal);
            return refusal;
        }

        /// <summary>
        /// Registers one felled lot from the live LogLot. The lot id is the
        /// lot id string from the lot, never a new id, so the ledger never
        /// allocates an id and never renumbers a lot. A duplicate lot id is
        /// refused and the original record is untouched.
        /// </summary>
        public string RegisterLot(LogLot lot, string harvestLocationId, string custodianId, string sourceTreeKey, int dayIndex)
        {
            if (lot == null)
            {
                string refusal = "LogLotCustodyLedger.RegisterLot: no lot offered — lots are registered, never conjured.";
                diagnostics.Add(refusal);
                return refusal;
            }

            string lotId = lot.LotId.ToString();
            if (lot.LotId == EntityId.Invalid)
                return Refuse("RegisterLot", lotId, "anonymous lot — a lot needs a real EntityId");
            if (lot.LogUnits <= 0)
                return Refuse("RegisterLot", lotId, "empty lot — a lot needs a positive log unit count");
            if (string.IsNullOrWhiteSpace(lot.StandId))
                return Refuse("RegisterLot", lotId, "no timber stand — a lot needs a stand id");
            if (string.IsNullOrWhiteSpace(harvestLocationId))
                return Refuse("RegisterLot", lotId, "no harvest location supplied — a lot needs its harvest location");
            if (string.IsNullOrWhiteSpace(custodianId))
                return Refuse("RegisterLot", lotId, "no custodian supplied — a lot needs a custodian");
            if (lots.ContainsKey(lotId))
                return Refuse("RegisterLot", lotId, "duplicate lot — the lot is already registered");

            var record = new LogLotCustodyRecord
            {
                LotId = lotId,
                LogUnits = lot.LogUnits,
                OwnerId = lot.OwnerId,
                MassKg = lot.MassKg,
                StandId = lot.StandId,
                Species = lot.Species ?? string.Empty,
                FelledByKind = (int)lot.FelledBy.Kind,
                FelledByNumber = lot.FelledBy.Id,
                FelledDayIndex = lot.FelledDayIndex,
                HarvestLocationId = harvestLocationId,
                CustodianId = custodianId,
                State = LogLotCustodyState.Created,
                ReservedBy = string.Empty,
                ReservedDayIndex = -1,
                CollectedDayIndex = -1,
                LoadedDayIndex = -1,
                InTransitDayIndex = -1,
                DeliveredDayIndex = -1,
                ProcessedDayIndex = -1,
                SourceTreeKey = sourceTreeKey ?? string.Empty,
            };
            lots[lotId] = record;
            diagnostics.Add($"LogLotCustodyLedger: registered lot '{lotId}' ({lot.LogUnits} logs, stand '{lot.StandId}', " +
                $"harvest location '{harvestLocationId}', custodian '{custodianId}', day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Registers one felled lot from fields. This overload exists because
        /// WorldTreeFellingDriver CompleteFelling never returns the live
        /// LogLot object, it only records SourceLogLotId on the job, so the
        /// production caller builds the registration from FellingJob fields
        /// plus the stand record without needing the lot object. Species
        /// may be empty and is recorded as empty, never invented. FelledBy
        /// may be Invalid and is recorded as Invalid, never invented.
        /// FelledDayIndex may be -1 and is recorded as -1, never invented.
        /// </summary>
        public string RegisterLotFromFields(string lotId, int logUnits, string standId, string species, EntityId felledBy, int felledDayIndex, string harvestLocationId, string custodianId, string sourceTreeKey, int dayIndex, float massKg)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("RegisterLotFromFields", lotId, "no lot id supplied — a lot needs an id");
            if (logUnits <= 0)
                return Refuse("RegisterLotFromFields", lotId, "empty lot — a lot needs a positive log unit count");
            if (string.IsNullOrWhiteSpace(standId))
                return Refuse("RegisterLotFromFields", lotId, "no timber stand — a lot needs a stand id");
            if (string.IsNullOrWhiteSpace(harvestLocationId))
                return Refuse("RegisterLotFromFields", lotId, "no harvest location supplied — a lot needs its harvest location");
            if (string.IsNullOrWhiteSpace(custodianId))
                return Refuse("RegisterLotFromFields", lotId, "no custodian supplied — a lot needs a custodian");
            if (lots.ContainsKey(lotId))
                return Refuse("RegisterLotFromFields", lotId, "duplicate lot — the lot is already registered");

            var record = new LogLotCustodyRecord
            {
                LotId = lotId,
                LogUnits = logUnits,
                OwnerId = custodianId,
                MassKg = massKg,
                StandId = standId,
                Species = species ?? string.Empty,
                FelledByKind = (int)felledBy.Kind,
                FelledByNumber = felledBy.Id,
                FelledDayIndex = felledDayIndex,
                HarvestLocationId = harvestLocationId,
                CustodianId = custodianId,
                State = LogLotCustodyState.Created,
                ReservedBy = string.Empty,
                ReservedDayIndex = -1,
                CollectedDayIndex = -1,
                LoadedDayIndex = -1,
                InTransitDayIndex = -1,
                DeliveredDayIndex = -1,
                ProcessedDayIndex = -1,
                SourceTreeKey = sourceTreeKey ?? string.Empty,
            };
            lots[lotId] = record;
            diagnostics.Add($"LogLotCustodyLedger: registered lot '{lotId}' ({logUnits} logs, stand '{standId}', " +
                $"harvest location '{harvestLocationId}', custodian '{custodianId}', day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Reserves a Created lot for one reserver. A repeat reservation by
        /// the same reserver returns null with no change, idempotent.
        /// </summary>
        public string TryReserveLot(string lotId, string reserverId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("TryReserveLot", lotId, "no lot id supplied — only a named lot can be reserved");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("TryReserveLot", lotId, "unknown lot — only registered lots can be reserved");
            if (string.IsNullOrWhiteSpace(reserverId))
                return Refuse("TryReserveLot", lotId, "anonymous reservation — reservations are never anonymous");
            if (record.State != LogLotCustodyState.Created)
                return Refuse("TryReserveLot", lotId, "is " + record.State + " — only a Created lot can be reserved");
            if (!string.IsNullOrEmpty(record.ReservedBy) && !string.Equals(record.ReservedBy, reserverId, StringComparison.Ordinal))
                return Refuse("TryReserveLot", lotId, "reserved by '" + record.ReservedBy + "' — '" + reserverId + "' cannot reserve it");
            if (string.Equals(record.ReservedBy, reserverId, StringComparison.Ordinal))
                return null;

            record.ReservedBy = reserverId;
            record.ReservedDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' reserved by '{reserverId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Releases a reservation held by the named reserver. Another
        /// carrier reservation is never released. State stays Created.
        /// </summary>
        public string ReleaseReservation(string lotId, string reserverId)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("ReleaseReservation", lotId, "no lot id supplied — only a named lot can be released");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("ReleaseReservation", lotId, "unknown lot — only registered lots can be released");
            if (string.IsNullOrWhiteSpace(reserverId))
                return Refuse("ReleaseReservation", lotId, "no reserver supplied — a reserver must be named");
            if (record.State != LogLotCustodyState.Created)
                return Refuse("ReleaseReservation", lotId, "is " + record.State + " — only a Created lot can be released");
            if (string.IsNullOrEmpty(record.ReservedBy))
                return Refuse("ReleaseReservation", lotId, "nothing to release — the lot holds no reservation");
            if (!string.Equals(record.ReservedBy, reserverId, StringComparison.Ordinal))
                return Refuse("ReleaseReservation", lotId, "reserved by '" + record.ReservedBy + "' — '" + reserverId + "' cannot release it");

            record.ReservedBy = string.Empty;
            record.ReservedDayIndex = -1;
            diagnostics.Add($"LogLotCustodyLedger: reservation on lot '{lotId}' released by '{reserverId}'.");
            return null;
        }

        /// <summary>
        /// Collects a Created lot. An unreserved lot is claimed by the
        /// first collector in this same call, so two carriers racing on one
        /// lot cannot both collect it. A retried collection by the same
        /// collector returns true with a null refusal and no state change
        /// and no second record. A retried collection by a different
        /// collector is refused.
        /// </summary>
        public bool CollectLot(string lotId, string collectorId, int dayIndex, out string refusal)
        {
            refusal = null;
            if (string.IsNullOrWhiteSpace(lotId))
            {
                refusal = Refuse("CollectLot", lotId, "no lot id supplied — only a named lot can be collected");
                return false;
            }
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
            {
                refusal = Refuse("CollectLot", lotId, "unknown lot — only registered lots can be collected");
                return false;
            }
            if (string.IsNullOrWhiteSpace(collectorId))
            {
                refusal = Refuse("CollectLot", lotId, "no collector supplied — a collector must be named");
                return false;
            }
            if (record.State == LogLotCustodyState.Collected && string.Equals(record.CustodianId, collectorId, StringComparison.Ordinal))
            {
                diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' collection retried by '{collectorId}' — already collected, no second record.");
                refusal = null;
                return true;
            }
            if (record.State == LogLotCustodyState.Collected)
            {
                refusal = Refuse("CollectLot", lotId, "held by collector '" + record.CustodianId + "' — '" + collectorId + "' cannot collect it");
                return false;
            }
            if (record.State != LogLotCustodyState.Created)
            {
                refusal = Refuse("CollectLot", lotId, "is " + record.State + " — only a Created lot can be collected");
                return false;
            }
            if (!string.IsNullOrEmpty(record.ReservedBy) && !string.Equals(record.ReservedBy, collectorId, StringComparison.Ordinal))
            {
                refusal = Refuse("CollectLot", lotId, "reserved by '" + record.ReservedBy + "' — '" + collectorId + "' cannot collect it");
                return false;
            }

            record.State = LogLotCustodyState.Collected;
            record.CustodianId = collectorId;
            record.ReservedBy = string.Empty;
            record.ReservedDayIndex = -1;
            record.CollectedDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' collected by '{collectorId}' (day {dayIndex}).");
            return true;
        }

        /// <summary>
        /// Loads a Collected lot onto a wagon. Only the collecting carrier
        /// loads its own lot. No F3 caller invokes this; F4 drives it from
        /// the shared loading task.
        /// </summary>
        public string LoadLot(string lotId, string loaderId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("LoadLot", lotId, "no lot id supplied — only a named lot can be loaded");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("LoadLot", lotId, "unknown lot — only registered lots can be loaded");
            if (string.IsNullOrWhiteSpace(loaderId))
                return Refuse("LoadLot", lotId, "no loader supplied — a loader must be named");
            if (record.State != LogLotCustodyState.Collected)
                return Refuse("LoadLot", lotId, "is " + record.State + " — only a Collected lot can be loaded");
            if (!string.Equals(record.CustodianId, loaderId, StringComparison.Ordinal))
                return Refuse("LoadLot", lotId, "held by custodian '" + record.CustodianId + "' — '" + loaderId + "' cannot load it");

            record.State = LogLotCustodyState.Loaded;
            record.CustodianId = loaderId;
            record.LoadedDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' loaded by '{loaderId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Moves a Loaded lot to InTransit. No F3 caller invokes this; F5
        /// drives it from the haul departure.
        /// </summary>
        public string MarkInTransit(string lotId, string carrierId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("MarkInTransit", lotId, "no lot id supplied — only a named lot can depart");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("MarkInTransit", lotId, "unknown lot — only registered lots can depart");
            if (string.IsNullOrWhiteSpace(carrierId))
                return Refuse("MarkInTransit", lotId, "no carrier supplied — a carrier must be named");
            if (record.State != LogLotCustodyState.Loaded)
                return Refuse("MarkInTransit", lotId, "is " + record.State + " — only a Loaded lot can depart");
            if (!string.Equals(record.CustodianId, carrierId, StringComparison.Ordinal))
                return Refuse("MarkInTransit", lotId, "held by custodian '" + record.CustodianId + "' — '" + carrierId + "' cannot carry it");

            record.State = LogLotCustodyState.InTransit;
            record.CustodianId = carrierId;
            record.InTransitDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' in transit with '{carrierId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Moves an InTransit lot to Delivered. This never calls
        /// SawmillLogStock ReceiveLogLot itself: F5 must call ReceiveLogLot
        /// first and call MarkDelivered only when it returns null, and a
        /// refused intake leaves the lot InTransit on the wagon.
        /// </summary>
        public string MarkDelivered(string lotId, string receiverId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("MarkDelivered", lotId, "no lot id supplied — only a named lot can be delivered");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("MarkDelivered", lotId, "unknown lot — only registered lots can be delivered");
            if (string.IsNullOrWhiteSpace(receiverId))
                return Refuse("MarkDelivered", lotId, "no receiver supplied — a receiver must be named");
            if (record.State != LogLotCustodyState.InTransit)
                return Refuse("MarkDelivered", lotId, "is " + record.State + " — only an InTransit lot can be delivered");

            record.State = LogLotCustodyState.Delivered;
            record.CustodianId = receiverId;
            record.DeliveredDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' delivered to '{receiverId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Moves a Delivered lot to Processed. This never calls RunSawing
        /// itself: F6 must call it only after the sawing run consumes the
        /// lot.
        /// </summary>
        public string MarkProcessed(string lotId, string processorId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(lotId))
                return Refuse("MarkProcessed", lotId, "no lot id supplied — only a named lot can be processed");
            LogLotCustodyRecord record = GetLot(lotId);
            if (record == null)
                return Refuse("MarkProcessed", lotId, "unknown lot — only registered lots can be processed");
            if (string.IsNullOrWhiteSpace(processorId))
                return Refuse("MarkProcessed", lotId, "no processor supplied — a processor must be named");
            if (record.State != LogLotCustodyState.Delivered)
                return Refuse("MarkProcessed", lotId, "is " + record.State + " — only a Delivered lot can be processed");

            record.State = LogLotCustodyState.Processed;
            record.CustodianId = processorId;
            record.ProcessedDayIndex = dayIndex;
            diagnostics.Add($"LogLotCustodyLedger: lot '{lotId}' processed by '{processorId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// Returns the live record for the lot id, or null when the lot id
        /// is blank or unknown. Callers must not mutate the record; the
        /// ledger is the only writer.
        /// </summary>
        public LogLotCustodyRecord GetLot(string lotId)
        {
            if (string.IsNullOrWhiteSpace(lotId)) return null;
            LogLotCustodyRecord record;
            return lots.TryGetValue(lotId, out record) ? record : null;
        }

        public List<LogLotCustodyRecord> LotsInState(LogLotCustodyState state)
        {
            var found = new List<LogLotCustodyRecord>();
            foreach (var record in lots.Values)
            {
                if (record == null) continue;
                if (record.State == state)
                    found.Add(record);
            }
            return found;
        }

        /// <summary>
        /// Returns every record whose HarvestLocationId equals the supplied
        /// id, or an empty list when the id is blank. Used by tests and by
        /// F5 to prove a lot is still at its harvest location before
        /// collection.
        /// </summary>
        public List<LogLotCustodyRecord> LotsAtLocation(string harvestLocationId)
        {
            var found = new List<LogLotCustodyRecord>();
            if (string.IsNullOrWhiteSpace(harvestLocationId)) return found;
            foreach (var record in lots.Values)
            {
                if (record == null) continue;
                if (string.Equals(record.HarvestLocationId, harvestLocationId, StringComparison.OrdinalIgnoreCase))
                    found.Add(record);
            }
            return found;
        }

        /// <summary>F3 save contract: lives inside the owning ledger class.</summary>
        [Serializable]
        public sealed class LogLotCustodyLedgerSaveDto
        {
            public List<LogLotCustodyRecord> Lots = new List<LogLotCustodyRecord>();
        }

        public LogLotCustodyLedgerSaveDto CaptureSaveDto()
        {
            var dto = new LogLotCustodyLedgerSaveDto();
            foreach (var record in lots.Values)
            {
                if (record == null) continue;
                dto.Lots.Add(CopyRecord(record));
            }
            return dto;
        }

        /// <summary>
        /// Clear-then-load. A null DTO clears to empty (smith behaviour,
        /// recorded explicitly — the wheelwright instead leaves state in
        /// place on a null DTO, and this ledger must not be assumed to
        /// match it). A null Lots list inside the DTO is treated as empty.
        /// Records with a blank lot id, duplicate lot ids, a state value
        /// outside 0 to 5, or 0 or fewer log units are skipped with a
        /// diagnostic; the first record for a lot id wins. The ledger holds
        /// no task references, no asset references and no live LogLot
        /// references, so there is nothing to re-attach.
        /// </summary>
        public void LoadFromSaveDto(LogLotCustodyLedgerSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            if (dto.Lots == null) return;
            foreach (var record in dto.Lots)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.LotId))
                {
                    diagnostics.Add("LogLotCustodyLedger.LoadFromSaveDto: skipped a record with no lot id — a custody record needs a lot id.");
                    continue;
                }
                if (lots.ContainsKey(record.LotId))
                {
                    diagnostics.Add($"LogLotCustodyLedger.LoadFromSaveDto: skipped a duplicate record for lot '{record.LotId}' — the first record wins.");
                    continue;
                }
                if ((int)record.State < 0 || (int)record.State > 5)
                {
                    diagnostics.Add($"LogLotCustodyLedger.LoadFromSaveDto: skipped lot '{record.LotId}' — state value {(int)record.State} is outside 0 to 5.");
                    continue;
                }
                if (record.LogUnits <= 0)
                {
                    diagnostics.Add($"LogLotCustodyLedger.LoadFromSaveDto: skipped lot '{record.LotId}' — an empty lot is never restored as stock.");
                    continue;
                }
                lots[record.LotId] = CopyRecord(record);
            }
        }

        private static LogLotCustodyRecord CopyRecord(LogLotCustodyRecord record)
        {
            return new LogLotCustodyRecord
            {
                LotId = record.LotId,
                LogUnits = record.LogUnits,
                OwnerId = record.OwnerId,
                MassKg = record.MassKg,
                StandId = record.StandId,
                Species = record.Species,
                FelledByKind = record.FelledByKind,
                FelledByNumber = record.FelledByNumber,
                FelledDayIndex = record.FelledDayIndex,
                HarvestLocationId = record.HarvestLocationId,
                CustodianId = record.CustodianId,
                State = record.State,
                ReservedBy = record.ReservedBy,
                ReservedDayIndex = record.ReservedDayIndex,
                CollectedDayIndex = record.CollectedDayIndex,
                LoadedDayIndex = record.LoadedDayIndex,
                InTransitDayIndex = record.InTransitDayIndex,
                DeliveredDayIndex = record.DeliveredDayIndex,
                ProcessedDayIndex = record.ProcessedDayIndex,
                SourceTreeKey = record.SourceTreeKey,
            };
        }
    }
}
