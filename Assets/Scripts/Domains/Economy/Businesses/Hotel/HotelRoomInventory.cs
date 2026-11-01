using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: the room classes a hotel sells, as period-grounded products.
    /// Canon §8.1A: a lodging operation sells "beds in shared rooms, private
    /// rooms ... and, where physically suitable, family rooms" as real
    /// accommodation, never a generic occupancy percentage. For the hotel
    /// the frontier products are the advertised single and double rooms,
    /// plus the front parlor suite the era reserved for commercial
    /// travelers and contract customers (Canon §8.1C: "a hotel in a mature
    /// node can draw commercial travelers and higher-paying transient
    /// guests"; §8.1D: "reservation of rooms for expected contract or
    /// commercial customers").
    /// </summary>
    public enum HotelRoomClass
    {
        /// <summary>One bed, one guest — the period "single".</summary>
        SingleRoom = 0,
        /// <summary>Two beds — the period "double", sleeps two.</summary>
        DoubleRoom = 1,
        /// <summary>Front parlor suite, two beds — the premium product.</summary>
        ParlorSuite = 2,
    }

    /// <summary>
    /// W3A: one hotel room as rentable inventory — a physical room with a
    /// fixed class and bed count, and an explicit bed-to-guest assignment.
    /// Bed assignments are the authority for who sleeps where; occupancy is
    /// counted from assigned beds, never estimated (Canon §8.1A).
    /// </summary>
    [Serializable]
    public sealed class HotelRoom
    {
        public string RoomNumber = string.Empty;
        public HotelRoomClass RoomClass = HotelRoomClass.SingleRoom;
        public int BedCount;

        /// <summary>
        /// D2A: the proprietor's own household occupies this room — part
        /// of the building without becoming identical to the boarders
        /// (Canon §8.1A). Proprietor-occupied rooms are real beds but not
        /// sellable: no check-in, no reservation hold, never counted as
        /// open. They still count as physical beds.
        /// </summary>
        public bool ProprietorOccupied;

        // bedIndex -> personId. Only occupied beds are recorded.
        public List<HotelRoomBedAssignment> OccupiedBeds = new List<HotelRoomBedAssignment>();

        public HotelRoom() { }

        public int OccupiedBedCount => OccupiedBeds != null ? OccupiedBeds.Count : 0;

        public int OpenBedCount => ProprietorOccupied ? 0 : Math.Max(0, BedCount - OccupiedBedCount);

        public bool HasPerson(int personId)
        {
            if (OccupiedBeds == null || personId <= 0) return false;
            for (int i = 0; i < OccupiedBeds.Count; i++)
                if (OccupiedBeds[i] != null && OccupiedBeds[i].PersonId == personId)
                    return true;
            return false;
        }

        public bool TryFindOpenBed(out int bedIndex)
        {
            bedIndex = -1;
            for (int i = 0; i < BedCount; i++)
            {
                if (!IsBedOccupied(i)) { bedIndex = i; return true; }
            }
            return false;
        }

        public bool IsBedOccupied(int bedIndex)
        {
            if (OccupiedBeds == null) return false;
            for (int i = 0; i < OccupiedBeds.Count; i++)
                if (OccupiedBeds[i] != null && OccupiedBeds[i].BedIndex == bedIndex)
                    return true;
            return false;
        }

        public int OccupantOfBed(int bedIndex)
        {
            if (OccupiedBeds == null) return 0;
            for (int i = 0; i < OccupiedBeds.Count; i++)
                if (OccupiedBeds[i] != null && OccupiedBeds[i].BedIndex == bedIndex)
                    return OccupiedBeds[i].PersonId;
            return 0;
        }
    }

    /// <summary>W3A: one occupied hotel bed — the nightly-state fact.</summary>
    [Serializable]
    public sealed class HotelRoomBedAssignment
    {
        public int BedIndex;
        public int PersonId;

        public HotelRoomBedAssignment() { }

        public HotelRoomBedAssignment(int bedIndex, int personId)
        {
            BedIndex = bedIndex;
            PersonId = personId;
        }
    }

    /// <summary>
    /// W3A: per-instance room inventory for one hotel. Rooms are added by
    /// the proprietor (physical rooms in the building); beds are assigned
    /// to real persons. Refusals are loud — a bed is never double-assigned
    /// and a person never holds two beds. Mirrors the W2C
    /// BoardingRoomInventory pattern without sharing its types: a hotel
    /// room and a boarding bed are different products.
    /// </summary>
    public sealed class HotelRoomInventory
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelRoom> rooms = new List<HotelRoom>();
        private int nextRoomSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelRoom> Rooms => rooms;

        /// <summary>
        /// Adds a physical room. Bed counts are product invariants: a
        /// single holds exactly one guest, a double exactly two, a parlor
        /// suite exactly two. Returns the refusal, or null.
        /// </summary>
        public string AddRoom(HotelRoomClass roomClass, int bedCount, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (roomClass == HotelRoomClass.SingleRoom && bedCount != 1)
                return "HotelRoomInventory.AddRoom: a single room holds exactly one bed — nothing conjured.";
            if (roomClass == HotelRoomClass.DoubleRoom && bedCount != 2)
                return "HotelRoomInventory.AddRoom: a double room holds exactly two beds — nothing conjured.";
            if (roomClass == HotelRoomClass.ParlorSuite && bedCount != 2)
                return "HotelRoomInventory.AddRoom: a parlor suite holds exactly two beds — nothing conjured.";
            if (bedCount <= 0)
                return "HotelRoomInventory.AddRoom: a room needs at least one bed.";

            var room = new HotelRoom
            {
                RoomNumber = $"hotel-room-{nextRoomSequence++}",
                RoomClass = roomClass,
                BedCount = bedCount,
            };
            rooms.Add(room);
            diag.Add($"HotelRoomInventory: added {roomClass} '{room.RoomNumber}' with {bedCount} bed(s).");
            return null;
        }

        public HotelRoom FindRoom(string roomNumber)
        {
            if (string.IsNullOrWhiteSpace(roomNumber)) return null;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && string.Equals(rooms[i].RoomNumber, roomNumber, StringComparison.OrdinalIgnoreCase))
                    return rooms[i];
            return null;
        }

        public bool PersonHoldsAnyBed(int personId)
        {
            if (personId <= 0) return false;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].HasPerson(personId))
                    return true;
            return false;
        }

        /// <summary>
        /// Assigns the first open bed of the requested room class to the
        /// guest. Null roomClass = any open bed. Returns the refusal, or
        /// null on success.
        /// </summary>
        public string AssignBed(int personId, HotelRoomClass? roomClass, out string roomNumber, out int bedIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            roomNumber = string.Empty;
            bedIndex = -1;
            if (personId <= 0)
                return "HotelRoomInventory.AssignBed: a bed needs a real person id — anonymous sleepers are not assigned.";
            if (PersonHoldsAnyBed(personId))
                return $"HotelRoomInventory.AssignBed: person {personId} already holds a bed — one person, one bed.";

            for (int i = 0; i < rooms.Count; i++)
            {
                HotelRoom room = rooms[i];
                if (room == null) continue;
                if (roomClass.HasValue && room.RoomClass != roomClass.Value) continue;
                if (room.ProprietorOccupied) continue;
                if (room.TryFindOpenBed(out int open))
                {
                    room.OccupiedBeds.Add(new HotelRoomBedAssignment(open, personId));
                    roomNumber = room.RoomNumber;
                    bedIndex = open;
                    diag.Add($"HotelRoomInventory: person {personId} takes bed {open} in {room.RoomClass} '{room.RoomNumber}'.");
                    return null;
                }
            }

            string classNote = roomClass.HasValue ? $" of class {roomClass.Value}" : string.Empty;
            return $"HotelRoomInventory.AssignBed: no open bed{classNote} — capacity is real beds, never invented.";
        }

        /// <summary>Assigns a specific bed — used by the guest register after choosing the room.</summary>
        public string AssignSpecificBed(int personId, string roomNumber, int bedIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelRoomInventory.AssignSpecificBed: a bed needs a real person id.";
            if (PersonHoldsAnyBed(personId))
                return $"HotelRoomInventory.AssignSpecificBed: person {personId} already holds a bed.";
            HotelRoom room = FindRoom(roomNumber);
            if (room == null)
                return $"HotelRoomInventory.AssignSpecificBed: no room '{roomNumber}'.";
            if (room.ProprietorOccupied)
                return $"HotelRoomInventory.AssignSpecificBed: '{roomNumber}' is proprietor household space (Canon §8.1A) — not sellable.";
            if (bedIndex < 0 || bedIndex >= room.BedCount)
                return $"HotelRoomInventory.AssignSpecificBed: bed {bedIndex} does not exist in '{roomNumber}' ({room.BedCount} bed(s)).";
            if (room.IsBedOccupied(bedIndex))
                return $"HotelRoomInventory.AssignSpecificBed: bed {bedIndex} in '{roomNumber}' is already occupied — no double-booking.";

            room.OccupiedBeds.Add(new HotelRoomBedAssignment(bedIndex, personId));
            diag.Add($"HotelRoomInventory: person {personId} takes bed {bedIndex} in '{roomNumber}'.");
            return null;
        }

        /// <summary>
        /// D2A: marks a room as proprietor-household space (or returns it
        /// to the sellable stock). Canon §8.1A: the proprietor's own
        /// household can occupy part of the building. An occupied room
        /// cannot become proprietor space — guests are never displaced by
        /// the flag.
        /// </summary>
        public string SetProprietorUse(string roomNumber, bool proprietorOccupied, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelRoom room = FindRoom(roomNumber);
            if (room == null)
                return $"HotelRoomInventory.SetProprietorUse: no room '{roomNumber}'.";
            if (proprietorOccupied && room.OccupiedBedCount > 0)
                return $"HotelRoomInventory.SetProprietorUse: '{roomNumber}' has {room.OccupiedBedCount} guest(s) — guests are never displaced by the flag.";
            room.ProprietorOccupied = proprietorOccupied;
            diag.Add(proprietorOccupied
                ? $"HotelRoomInventory: '{roomNumber}' set aside as proprietor-household space (Canon §8.1A) — not sellable."
                : $"HotelRoomInventory: '{roomNumber}' returned to the sellable stock.");
            return null;
        }

        public bool ReleaseBedByPerson(int personId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0) return false;
            for (int i = 0; i < rooms.Count; i++)
            {
                HotelRoom room = rooms[i];
                if (room?.OccupiedBeds == null) continue;
                for (int j = 0; j < room.OccupiedBeds.Count; j++)
                {
                    if (room.OccupiedBeds[j] != null && room.OccupiedBeds[j].PersonId == personId)
                    {
                        room.OccupiedBeds.RemoveAt(j);
                        diag.Add($"HotelRoomInventory: person {personId} vacated bed in '{room.RoomNumber}'.");
                        return true;
                    }
                }
            }
            return false;
        }

        public int TotalBeds()
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++) total += rooms[i] != null ? rooms[i].BedCount : 0;
            return total;
        }

        public int OccupiedBeds()
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++) total += rooms[i] != null ? rooms[i].OccupiedBedCount : 0;
            return total;
        }

        public int OpenBeds(HotelRoomClass roomClass)
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].RoomClass == roomClass)
                    total += rooms[i].OpenBedCount;
            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelRoomInventorySaveDto
        {
            public int NextRoomSequence = 1;
            public List<HotelRoom> Rooms = new List<HotelRoom>();
        }

        public HotelRoomInventorySaveDto CaptureSaveDto()
        {
            var dto = new HotelRoomInventorySaveDto { NextRoomSequence = nextRoomSequence };
            foreach (HotelRoom room in rooms)
            {
                if (room == null) continue;
                var copy = new HotelRoom
                {
                    RoomNumber = room.RoomNumber,
                    RoomClass = room.RoomClass,
                    BedCount = room.BedCount,
                    ProprietorOccupied = room.ProprietorOccupied,
                };
                if (room.OccupiedBeds != null)
                    foreach (HotelRoomBedAssignment a in room.OccupiedBeds)
                        if (a != null) copy.OccupiedBeds.Add(new HotelRoomBedAssignment(a.BedIndex, a.PersonId));
                dto.Rooms.Add(copy);
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelRoomInventorySaveDto dto)
        {
            rooms.Clear();
            nextRoomSequence = 1;
            if (dto == null) return;
            nextRoomSequence = Math.Max(1, dto.NextRoomSequence);
            if (dto.Rooms == null) return;
            foreach (HotelRoom room in dto.Rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.RoomNumber)) continue;
                var copy = new HotelRoom
                {
                    RoomNumber = room.RoomNumber,
                    RoomClass = room.RoomClass,
                    BedCount = Math.Max(0, room.BedCount),
                    ProprietorOccupied = room.ProprietorOccupied,
                };
                if (room.OccupiedBeds != null)
                    foreach (HotelRoomBedAssignment a in room.OccupiedBeds)
                    {
                        if (a == null || a.PersonId <= 0) continue;
                        if (a.BedIndex < 0 || a.BedIndex >= copy.BedCount) continue;
                        if (copy.IsBedOccupied(a.BedIndex)) continue;
                        copy.OccupiedBeds.Add(new HotelRoomBedAssignment(a.BedIndex, a.PersonId));
                    }
                rooms.Add(copy);
            }
        }
        #endregion
    }
}
