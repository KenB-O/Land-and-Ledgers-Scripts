using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// Pass IV (Living World Foundation): ONE coherent reservation authority.
    ///
    /// Person time, equipment, workstations, workspaces, inventory lots, motive power,
    /// animals, materials, time windows and processing capacity are all reserved through
    /// this single contract. Different task stages may reserve different resources; the
    /// same task releases what it reserved — never another task's reservation.
    ///
    /// This PROMOTES the existing EquipmentReservationService key-space pattern to the
    /// universal authority (Worker 2 implements this interface, preferably by extending
    /// that service). It does NOT create a second reservation system.
    ///
    /// Correctness requirements (Worker 2 must satisfy all):
    /// - No double allocation; no negative available capacity.
    /// - Atomic multi-resource acquisition: all items or none, with safe rollback.
    /// - Deterministic conflict resolution (first valid reservation wins; refusals name
    ///   the conflicting holder).
    /// - Correct cancellation and release; no deadlock or indefinite hold from partial
    ///   acquisition.
    /// - Idempotent save/load: reservations persist; a restored game never double-books
    ///   and never silently drops a live reservation.
    /// - No subsystem bypasses reservations because it runs off-screen.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8 (shared resource
    /// reservations), preserved Tech VI Part III §§3.1-3.11 (one reservation authority).
    /// </summary>
    public enum ReservationDomain
    {
        Unspecified = 0,
        PersonTime = 1,
        EquipmentAsset = 2,
        ToolKit = 3,
        Workstation = 4,
        Workspace = 5,
        InventoryLot = 6,
        MotivePower = 7,
        Animal = 8,
        ProcessingCapacity = 9,
        RouteCapacity = 10,
    }

    /// <summary>
    /// Pass IV: resource-key conventions. One key space across all domains so a single
    /// authority can detect conflicts. Keys are stable across save/load.
    /// </summary>
    public static class ReservationKeyConventions
    {
        public const string AssetPrefix = "asset:";
        public const string KitPrefix = "kit:";
        public const string WorkstationPrefix = "workstation:";
        public const string PersonPrefix = "person:";
        public const string LotPrefix = "lot:";
        public const string WorkspacePrefix = "workspace:";
        public const string AnimalPrefix = "animal:";
        public const string MotivePowerPrefix = "motive:";

        public static string Asset(string assetId) { return AssetPrefix + assetId; }
        public static string Kit(string kitId) { return KitPrefix + kitId; }
        public static string Workstation(string workstationId) { return WorkstationPrefix + workstationId; }
        public static string Person(string personEntityIdString) { return PersonPrefix + personEntityIdString; }
        public static string Lot(string lotId) { return LotPrefix + lotId; }
        public static string Workspace(string workspaceId) { return WorkspacePrefix + workspaceId; }
        public static string Animal(string animalId) { return AnimalPrefix + animalId; }
        public static string MotivePower(string sourceId) { return MotivePowerPrefix + sourceId; }

        /// <summary>Parses the domain from a key prefix; Unspecified when unrecognized.</summary>
        public static ReservationDomain DomainOf(string resourceKey)
        {
            if (string.IsNullOrEmpty(resourceKey)) return ReservationDomain.Unspecified;
            if (resourceKey.StartsWith(AssetPrefix, StringComparison.Ordinal)) return ReservationDomain.EquipmentAsset;
            if (resourceKey.StartsWith(KitPrefix, StringComparison.Ordinal)) return ReservationDomain.ToolKit;
            if (resourceKey.StartsWith(WorkstationPrefix, StringComparison.Ordinal)) return ReservationDomain.Workstation;
            if (resourceKey.StartsWith(PersonPrefix, StringComparison.Ordinal)) return ReservationDomain.PersonTime;
            if (resourceKey.StartsWith(LotPrefix, StringComparison.Ordinal)) return ReservationDomain.InventoryLot;
            if (resourceKey.StartsWith(WorkspacePrefix, StringComparison.Ordinal)) return ReservationDomain.Workspace;
            if (resourceKey.StartsWith(AnimalPrefix, StringComparison.Ordinal)) return ReservationDomain.Animal;
            if (resourceKey.StartsWith(MotivePowerPrefix, StringComparison.Ordinal)) return ReservationDomain.MotivePower;
            return ReservationDomain.Unspecified;
        }
    }

    /// <summary>Pass IV: one item in an atomic reservation request. Plain data.</summary>
    [Serializable]
    public sealed class ReservationItem
    {
        [SerializeField]
        private string resourceKey = string.Empty;

        /// <summary>
        /// Quantity for partial-capacity resources (container/throughput). 1 = exclusive
        /// unit. Must be positive; the authority refuses over-capacity.
        /// </summary>
        [SerializeField]
        private float quantity = 1f;

        /// <summary>Absolute game seconds the reservation starts (TimeManager timeline).</summary>
        [SerializeField]
        private float startGameSeconds;

        /// <summary>Absolute game seconds the reservation ends. Must exceed start.</summary>
        [SerializeField]
        private float endGameSeconds;

        public string ResourceKey => resourceKey;
        public float Quantity => quantity;
        public float StartGameSeconds => startGameSeconds;
        public float EndGameSeconds => endGameSeconds;

        public ReservationItem()
        {
        }

        public ReservationItem(string resourceKey, float quantity, float startGameSeconds, float endGameSeconds)
        {
            this.resourceKey = resourceKey ?? string.Empty;
            this.quantity = Math.Max(0f, quantity);
            this.startGameSeconds = startGameSeconds;
            this.endGameSeconds = endGameSeconds;
        }
    }

    /// <summary>Pass IV: an atomic multi-resource reservation request. Plain data.</summary>
    [Serializable]
    public sealed class ReservationRequest
    {
        [SerializeField]
        private string ownerTaskId = string.Empty;

        [SerializeField]
        private string ownerLabel = string.Empty;

        [SerializeField]
        private List<ReservationItem> items = new List<ReservationItem>();

        public string OwnerTaskId => ownerTaskId;
        public string OwnerLabel => ownerLabel;
        public List<ReservationItem> Items => items;

        public ReservationRequest()
        {
        }

        public ReservationRequest(string ownerTaskId, string ownerLabel)
        {
            this.ownerTaskId = ownerTaskId ?? string.Empty;
            this.ownerLabel = ownerLabel ?? string.Empty;
        }
    }

    /// <summary>Pass IV: handle for a granted atomic reservation. Plain data; persisted.</summary>
    [Serializable]
    public sealed class ReservationHandle
    {
        [SerializeField]
        private string reservationId = string.Empty;

        [SerializeField]
        private string ownerTaskId = string.Empty;

        [SerializeField]
        private List<ReservationItem> items = new List<ReservationItem>();

        public string ReservationId => reservationId;
        public string OwnerTaskId => ownerTaskId;
        public List<ReservationItem> Items => items;

        public ReservationHandle()
        {
        }

        public ReservationHandle(string reservationId, string ownerTaskId, List<ReservationItem> items)
        {
            this.reservationId = reservationId ?? string.Empty;
            this.ownerTaskId = ownerTaskId ?? string.Empty;
            if (items != null) this.items = items;
        }
    }

    /// <summary>
    /// Pass IV: the unified reservation authority contract. One implementation serves
    /// every domain — task execution, production stages, freight, construction, and
    /// household work all reserve through this interface.
    /// </summary>
    public interface IUnifiedReservationAuthority
    {
        /// <summary>
        /// Atomically reserves every item in the request. Returns null on success and
        /// sets handle; returns a refusal reason naming the conflict on failure, in
        /// which case NOTHING is reserved (safe rollback, no partial hold).
        /// </summary>
        string TryReserveAll(ReservationRequest request, out ReservationHandle handle);

        /// <summary>Releases a reservation by id. No-op when unknown (idempotent).</summary>
        void Release(string reservationId);

        /// <summary>Releases every reservation owned by a task (cancellation path).</summary>
        void ReleaseAllForTask(string ownerTaskId);

        /// <summary>
        /// Pass VII: releases every live reservation holding the given resource
        /// key, regardless of owner task. Used when the resource itself changes
        /// hands (sale) or is retired: the new holder must not inherit the old
        /// holder's commitments, and stale holds must not block legitimate use.
        /// </summary>
        void ReleaseAllForResource(string resourceKey);

        /// <summary>Whether the resource key is reserved at the given absolute game seconds.</summary>
        bool IsReserved(string resourceKey, float atGameSeconds);

        /// <summary>
        /// Remaining reservable quantity for a partial-capacity resource at the given
        /// time. Exclusive resources return 1 when free, 0 when held.
        /// </summary>
        float AvailableCapacity(string resourceKey, float atGameSeconds);

        /// <summary>All live reservations for an owner task (diagnostics, read models).</summary>
        List<ReservationHandle> ReservationsForTask(string ownerTaskId);

        /// <summary>Number of live reservations (bounded diagnostics).</summary>
        int LiveReservationCount { get; }
    }
}
