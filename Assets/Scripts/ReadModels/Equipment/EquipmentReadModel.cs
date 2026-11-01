using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass IV (Worker 4, Phase Q): one equipment asset's live state,
    /// production-backed. Identity, owner, location, condition, occupied vs
    /// available capacity, reservations, pending maintenance — derived from the
    /// unified reservation authority and the equipment's real condition record.
    /// The bounded history keeps the last 16 significant equipment events,
    /// never a tick log.
    ///
    /// Observer-visibility: call <see cref="RedactForObserver"/>. Anyone
    /// present at the equipment's location (or its owner/manager) sees
    /// condition and occupancy. WHO holds a reservation is visible only to
    /// the owner/manager; everyone else sees "occupied".
    /// </summary>
    [Serializable]
    public sealed class EquipmentReadModel
    {
        public const int MaxHistory = 16;

        public string EquipmentId = string.Empty;
        public string DisplayName = string.Empty;
        public string OwnerBusinessId = string.Empty;
        public string LocationId = string.Empty;
        /// <summary>0..1 real condition.</summary>
        public float Condition01 = 1f;
        /// <summary>Available | Occupied | Failed | Maintenance.</summary>
        public string Status = string.Empty;
        /// <summary>Minutes already committed today.</summary>
        public int OccupiedMinutesToday;
        /// <summary>Capacity units free right now (0 = fully booked).</summary>
        public float AvailableCapacityNow = 1f;
        public List<ReservationRow> UpcomingReservations = new List<ReservationRow>();
        public List<string> PendingMaintenance = new List<string>();
        public List<EquipmentEvent> RecentHistory = new List<EquipmentEvent>();
        public bool Redacted;

        /// <summary>Returns a copy filtered by the observer-visibility rule.</summary>
        public EquipmentReadModel RedactForObserver(ObserverContext observer)
        {
            var copy = new EquipmentReadModel
            {
                EquipmentId = EquipmentId,
                DisplayName = DisplayName,
                LocationId = LocationId,
                Condition01 = Condition01,
                Status = Status,
                OccupiedMinutesToday = OccupiedMinutesToday,
                AvailableCapacityNow = AvailableCapacityNow,
            };

            bool present = observer != null && string.Equals(
                observer.ObserverLocationId, LocationId, StringComparison.Ordinal);
            bool ownerOrManager = observer != null && ObserverVisibility.CanSeeReservationHolder(
                observer.ObserverPersonId, EquipmentId,
                _ => OwnerPersonIdOf != null ? OwnerPersonIdOf(OwnerBusinessId) : -1,
                (_, pid) => IsDelegatedManagerOf != null && IsDelegatedManagerOf(OwnerBusinessId, pid));

            if (!present && !ownerOrManager)
            {
                // Not present, not owner: only that it exists and is busy/free.
                copy.Condition01 = -1f; // undisclosed
                copy.OccupiedMinutesToday = -1;
                copy.Redacted = true;
            }
            else
            {
                copy.OwnerBusinessId = ownerOrManager ? OwnerBusinessId : string.Empty;
                copy.PendingMaintenance.AddRange(PendingMaintenance);
            }

            foreach (ReservationRow r in UpcomingReservations)
            {
                if (r == null)
                {
                    continue;
                }

                if (present || ownerOrManager)
                {
                    copy.UpcomingReservations.Add(new ReservationRow
                    {
                        ResourceKey = r.ResourceKey,
                        StartMinuteOfDay = r.StartMinuteOfDay,
                        EndMinuteOfDay = r.EndMinuteOfDay,
                        HolderLabel = ownerOrManager ? r.HolderLabel : "occupied",
                    });
                }
            }

            if (present || ownerOrManager)
            {
                int start = Math.Max(0, RecentHistory.Count - MaxHistory);
                for (int i = start; i < RecentHistory.Count; i++)
                {
                    if (RecentHistory[i] != null)
                    {
                        copy.RecentHistory.Add(RecentHistory[i]);
                    }
                }
            }
            else
            {
                copy.Redacted = true;
            }

            return copy;
        }

        /// <summary>
        /// Injected by the builder: resolves an owning BUSINESS id to its
        /// owner person id for the visibility check.
        /// </summary>
        public Func<string, int> OwnerPersonIdOf;
        /// <summary>Injected by the builder: delegated-manager check (businessId, personId).</summary>
        public Func<string, int, bool> IsDelegatedManagerOf;
    }

    [Serializable]
    public sealed class ReservationRow
    {
        public string ResourceKey = string.Empty;
        public int StartMinuteOfDay;
        public int EndMinuteOfDay;
        /// <summary>Anonymized to "occupied" for non-owners.</summary>
        public string HolderLabel = string.Empty;
    }

    /// <summary>One significant equipment event — bounded, never a tick log.</summary>
    [Serializable]
    public sealed class EquipmentEvent
    {
        public int DayIndex;
        public int MinuteOfDay;
        public string Kind = string.Empty; // reserved | released | failed | repaired | maintained
        public string Summary = string.Empty;
    }

    /// <summary>Builds <see cref="EquipmentReadModel"/> from live authorities.</summary>
    public sealed class EquipmentReadModelBuilder
    {
        public Func<string, string> DisplayNameOf;
        public Func<string, string> OwnerBusinessOf;
        public Func<string, string> LocationOf;
        public Func<string, float> ConditionOf;
        public Func<string, string> StatusOf;
        public Func<string, int, int> OccupiedMinutesTodayOf;
        public Func<string, float> AvailableCapacityNowOf;
        public Func<string, List<ReservationRow>> UpcomingReservationsOf;
        public Func<string, List<string>> PendingMaintenanceOf;
        public Func<string, List<EquipmentEvent>> HistoryOf;
        public Func<string, int> OwnerPersonIdOf;
        public Func<string, int, bool> IsDelegatedManagerOf;

        public EquipmentReadModel Build(string equipmentId, int dayIndex)
        {
            var model = new EquipmentReadModel
            {
                EquipmentId = equipmentId,
                OwnerPersonIdOf = OwnerPersonIdOf,
                IsDelegatedManagerOf = IsDelegatedManagerOf,
            };
            model.DisplayName = DisplayNameOf != null ? DisplayNameOf(equipmentId) ?? string.Empty : string.Empty;
            model.OwnerBusinessId = OwnerBusinessOf != null ? OwnerBusinessOf(equipmentId) ?? string.Empty : string.Empty;
            model.LocationId = LocationOf != null ? LocationOf(equipmentId) ?? string.Empty : string.Empty;
            model.Condition01 = ConditionOf != null ? ConditionOf(equipmentId) : 1f;
            model.Status = StatusOf != null ? StatusOf(equipmentId) ?? string.Empty : string.Empty;
            model.OccupiedMinutesToday = OccupiedMinutesTodayOf != null ? OccupiedMinutesTodayOf(equipmentId, dayIndex) : 0;
            model.AvailableCapacityNow = AvailableCapacityNowOf != null ? AvailableCapacityNowOf(equipmentId) : 1f;

            if (UpcomingReservationsOf != null)
            {
                foreach (ReservationRow r in UpcomingReservationsOf(equipmentId) ?? new List<ReservationRow>())
                {
                    if (r != null)
                    {
                        model.UpcomingReservations.Add(r);
                    }
                }
            }

            if (PendingMaintenanceOf != null)
            {
                model.PendingMaintenance.AddRange(PendingMaintenanceOf(equipmentId) ?? new List<string>());
            }

            List<EquipmentEvent> history = HistoryOf != null ? HistoryOf(equipmentId) : null;
            if (history != null)
            {
                int start = Math.Max(0, history.Count - EquipmentReadModel.MaxHistory);
                for (int i = start; i < history.Count; i++)
                {
                    if (history[i] != null)
                    {
                        model.RecentHistory.Add(history[i]);
                    }
                }
            }

            return model;
        }
    }
}
