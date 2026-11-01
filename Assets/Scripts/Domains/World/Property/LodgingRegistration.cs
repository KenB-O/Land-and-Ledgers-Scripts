using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass VIII Worker 2: one business's lodging product as the housing
    /// authority sees it. A boarding house (or any lodging business)
    /// registers each rentable room here BEFORE it can sell beds in it:
    /// the registration binds the business-side room id to a real
    /// AccommodationSpace in the HousingAuthority, and the space's sleeping
    /// capacity must equal the room's bed count — actual beds, never
    /// invented. Append-only lifecycle (active / ended).
    /// </summary>
    [Serializable]
    public sealed class LodgingRegistration
    {
        public string RegistrationId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public string RoomId = string.Empty; // the business-side room id (e.g. boarding inventory room)
        public string SpaceId = string.Empty; // the housing-side accommodation space
        public int BedCount;
        public int RateCentsPerNight;
        public int RateCentsPerWeek;
        public AccommodationArrangement Arrangement = AccommodationArrangement.BoardingOrLodging;
        public int SinceDayIndex;
        public int UntilDayIndex = -1; // -1 = active

        public bool IsActive => UntilDayIndex < 0;

        public LodgingRegistration() { }
    }

    /// <summary>
    /// Pass VIII Worker 2: the registry of lodging a business is entitled
    /// to sell. Registration is the gate between "this business runs rooms"
    /// and "these rooms are real accommodation": the bridge refuses
    /// check-in to an unregistered room, and refuses registration when the
    /// space is unknown, already registered to another seller, or sized
    /// differently from the room's real bed count.
    /// </summary>
    public sealed class LodgingRegistry
    {
        private readonly Dictionary<string, LodgingRegistration> registrations =
            new Dictionary<string, LodgingRegistration>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Registers one room's lodging. Refuses unknown spaces, bed-count
        /// mismatches, double registration of a space, and duplicate active
        /// registration of the same business room. Returns null on success,
        /// the refusal otherwise.
        /// </summary>
        public string RegisterLodging(
            string businessInstanceId, string roomId, int bedCount, string spaceId,
            int rateCentsPerNight, int rateCentsPerWeek,
            int dayIndex, HousingAuthority housing, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
                return "LodgingRegistry.RegisterLodging: a business instance id is required.";
            if (string.IsNullOrWhiteSpace(roomId))
                return "LodgingRegistry.RegisterLodging: a room id is required.";
            if (bedCount <= 0)
                return $"LodgingRegistry.RegisterLodging: a room needs at least one real bed (got {bedCount}).";
            if (housing == null)
                return "LodgingRegistry.RegisterLodging: no housing authority — rooms register against real spaces.";
            AccommodationSpace space = housing.FindSpace(spaceId);
            if (space == null)
                return $"LodgingRegistry.RegisterLodging: unknown space '{spaceId}' — lodging registers against real accommodation, never a bare room id.";
            if (space.SleepingCapacity != bedCount)
                return $"LodgingRegistry.RegisterLodging: room '{roomId}' has {bedCount} bed(s) but space '{spaceId}' sleeps {space.SleepingCapacity} — beds and spaces must agree.";
            foreach (LodgingRegistration existing in registrations.Values)
            {
                if (existing == null || !existing.IsActive) continue;
                if (string.Equals(existing.SpaceId, spaceId, StringComparison.Ordinal))
                    return $"LodgingRegistry.RegisterLodging: space '{spaceId}' is already registered to '{existing.BusinessInstanceId}' room '{existing.RoomId}' — one seller per space.";
                if (string.Equals(existing.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal)
                    && string.Equals(existing.RoomId, roomId, StringComparison.OrdinalIgnoreCase))
                    return $"LodgingRegistry.RegisterLodging: room '{roomId}' of '{businessInstanceId}' is already registered.";
            }
            var registration = new LodgingRegistration
            {
                RegistrationId = $"lod-{sequence++}",
                BusinessInstanceId = businessInstanceId,
                RoomId = roomId,
                SpaceId = spaceId,
                BedCount = bedCount,
                RateCentsPerNight = Math.Max(0, rateCentsPerNight),
                RateCentsPerWeek = Math.Max(0, rateCentsPerWeek),
                SinceDayIndex = dayIndex,
            };
            registrations[registration.RegistrationId] = registration;
            diag.Add($"LodgingRegistry: '{businessInstanceId}' room '{roomId}' registered as '{registration.RegistrationId}' " +
                     $"on space '{spaceId}' ({bedCount} bed(s), {rateCentsPerNight}c/night, {rateCentsPerWeek}c/week).");
            return null;
        }

        /// <summary>The active registration for one business room, if any.</summary>
        public LodgingRegistration FindActiveRegistration(string businessInstanceId, string roomId)
        {
            if (string.IsNullOrWhiteSpace(businessInstanceId) || string.IsNullOrWhiteSpace(roomId)) return null;
            foreach (LodgingRegistration registration in registrations.Values)
            {
                if (registration != null && registration.IsActive
                    && string.Equals(registration.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal)
                    && string.Equals(registration.RoomId, roomId, StringComparison.OrdinalIgnoreCase))
                    return registration;
            }
            return null;
        }

        /// <summary>The active registration selling one space, if any.</summary>
        public LodgingRegistration FindActiveRegistrationForSpace(string spaceId)
        {
            if (string.IsNullOrWhiteSpace(spaceId)) return null;
            foreach (LodgingRegistration registration in registrations.Values)
            {
                if (registration != null && registration.IsActive
                    && string.Equals(registration.SpaceId, spaceId, StringComparison.Ordinal))
                    return registration;
            }
            return null;
        }

        /// <summary>Every active registration for one business, in registration order.</summary>
        public List<LodgingRegistration> ActiveRegistrationsForBusiness(string businessInstanceId)
        {
            var result = new List<LodgingRegistration>();
            foreach (LodgingRegistration registration in registrations.Values)
            {
                if (registration != null && registration.IsActive
                    && string.Equals(registration.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                    result.Add(registration);
            }
            result.Sort((a, b) => string.Compare(a.RegistrationId, b.RegistrationId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>Every active registration, in registration-id order.</summary>
        public List<LodgingRegistration> AllActiveRegistrations()
        {
            var result = new List<LodgingRegistration>();
            foreach (LodgingRegistration registration in registrations.Values)
            {
                if (registration != null && registration.IsActive)
                    result.Add(registration);
            }
            result.Sort((a, b) => string.Compare(a.RegistrationId, b.RegistrationId, StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// Keeps the registration's bed count in lockstep with a real room
        /// resize. The bridge calls this only after both the business
        /// inventory and the housing space accepted the change.
        /// </summary>
        public string SyncBedCount(string registrationId, int newBedCount, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!registrations.TryGetValue(registrationId, out LodgingRegistration registration) || registration == null)
                return $"LodgingRegistry.SyncBedCount: unknown registration '{registrationId}'.";
            if (newBedCount <= 0)
                return $"LodgingRegistry.SyncBedCount: a room needs at least one real bed (got {newBedCount}).";
            registration.BedCount = newBedCount;
            diag.Add($"LodgingRegistry: registration '{registrationId}' bed count synced to {newBedCount}.");
            return null;
        }

        /// <summary>
        /// Ends one registration (business closure, room retired). History
        /// is kept; the space itself is untouched.
        /// </summary>
        public string EndRegistration(string registrationId, int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!registrations.TryGetValue(registrationId, out LodgingRegistration registration) || registration == null)
                return $"LodgingRegistry.EndRegistration: unknown registration '{registrationId}'.";
            if (!registration.IsActive)
            {
                diag.Add($"LodgingRegistry: registration '{registrationId}' already ended — no double end.");
                return null;
            }
            registration.UntilDayIndex = dayIndex;
            diag.Add($"LodgingRegistry: registration '{registrationId}' ('{registration.BusinessInstanceId}' room '{registration.RoomId}') ended day {dayIndex} — {reason}.");
            return null;
        }

        #region Save / Load
        [Serializable]
        public sealed class LodgingRegistrySaveDto
        {
            public List<LodgingRegistration> Registrations = new List<LodgingRegistration>();
            public int Sequence;
        }

        public LodgingRegistrySaveDto CaptureSaveDto()
        {
            var dto = new LodgingRegistrySaveDto { Sequence = sequence };
            foreach (LodgingRegistration registration in registrations.Values)
            {
                if (registration != null) dto.Registrations.Add(registration);
            }
            return dto;
        }

        /// <summary>Clear-then-load: restored state replaces, never merges.</summary>
        public void LoadFromSaveDto(LodgingRegistrySaveDto dto)
        {
            registrations.Clear();
            sequence = 0;
            if (dto == null) return;
            sequence = Math.Max(0, dto.Sequence);
            if (dto.Registrations == null) return;
            foreach (LodgingRegistration registration in dto.Registrations)
            {
                if (registration == null || string.IsNullOrWhiteSpace(registration.RegistrationId)) continue;
                if (registrations.ContainsKey(registration.RegistrationId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate registration '{registration.RegistrationId}' skipped.");
                    continue;
                }
                registrations[registration.RegistrationId] = registration;
            }
        }
        #endregion
    }
}
