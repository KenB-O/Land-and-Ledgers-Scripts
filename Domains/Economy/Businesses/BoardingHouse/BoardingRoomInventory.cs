using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the accommodation products a boarding operation sells. Canon
    /// §8.1A: "it can offer beds in shared rooms, private rooms,
    /// room-and-board packages, longer-stay boarding, short transient
    /// lodging and, where physically suitable, family rooms." Each product
    /// is a real room with a real bed count — never a generic occupancy
    /// percentage.
    /// </summary>
    public enum BoardingRoomType
    {
        /// <summary>One bed in a shared dormitory-style room (strangers may share the room).</summary>
        SharedBed = 0,
        /// <summary>One private room with exactly one bed.</summary>
        PrivateRoom = 1,
        /// <summary>One room with several beds for a single party / family.</summary>
        FamilyRoom = 2,
    }

    /// <summary>
    /// W2C: one rented room as rentable inventory — a physical room with a
    /// fixed bed count and an explicit bed-to-person assignment. Bed
    /// assignments are the authority for who sleeps where; occupancy is
    /// counted from assigned beds, never estimated.
    /// </summary>
    [Serializable]
    public sealed class BoardingRoom
    {
        public string RoomId = string.Empty;
        public BoardingRoomType RoomType = BoardingRoomType.SharedBed;
        public int BedCount;

        // bedIndex -> personId. Only occupied beds are recorded.
        public List<BoardingBedAssignment> OccupiedBeds = new List<BoardingBedAssignment>();

        /// <summary>
        /// D1F: bed indexes vacated since the last turnover (Canon §8.1E bed
        /// turnover). A soiled bed cannot be re-let until chamber work turns
        /// the room. Empty = the room is clean and ready.
        /// </summary>
        public List<int> BedsNeedingTurnover = new List<int>();

        public BoardingRoom() { }

        public int OccupiedBedCount => OccupiedBeds != null ? OccupiedBeds.Count : 0;

        public int OpenBedCount => Math.Max(0, BedCount - OccupiedBedCount);

        public bool HasPerson(int personId)
        {
            if (OccupiedBeds == null || personId <= 0) return false;
            for (int i = 0; i < OccupiedBeds.Count; i++)
                if (OccupiedBeds[i] != null && OccupiedBeds[i].PersonId == personId)
                    return true;
            return false;
        }

        /// <summary>
        /// D1F: finds the first bed that is both unoccupied and clean. Beds
        /// awaiting turnover are not rentable until chamber work turns them.
        /// </summary>
        public bool TryFindOpenBed(out int bedIndex)
        {
            bedIndex = -1;
            for (int i = 0; i < BedCount; i++)
            {
                if (!IsBedOccupied(i) && !BedNeedsTurnover(i)) { bedIndex = i; return true; }
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

        /// <summary>D1F: true when the bed was vacated since the last turnover and cannot be re-let yet.</summary>
        public bool BedNeedsTurnover(int bedIndex)
        {
            if (BedsNeedingTurnover == null) return false;
            for (int i = 0; i < BedsNeedingTurnover.Count; i++)
                if (BedsNeedingTurnover[i] == bedIndex)
                    return true;
            return false;
        }

        /// <summary>D1F: how many beds in this room await turnover.</summary>
        public int SoiledBedCount => BedsNeedingTurnover != null ? BedsNeedingTurnover.Count : 0;
    }

    /// <summary>W2C: one occupied bed — the nightly-state fact.</summary>
    [Serializable]
    public sealed class BoardingBedAssignment
    {
        public int BedIndex;
        public int PersonId;

        public BoardingBedAssignment() { }

        public BoardingBedAssignment(int bedIndex, int personId)
        {
            BedIndex = bedIndex;
            PersonId = personId;
        }
    }

    /// <summary>
    /// W2C: per-instance room inventory for one boarding house. Rooms are
    /// added by the proprietor (physical rooms in the building); beds are
    /// assigned to real persons. Refusals are loud — a bed is never
    /// double-assigned and a person never holds two beds.
    /// </summary>
    public sealed class BoardingRoomInventory
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoardingRoom> rooms = new List<BoardingRoom>();
        private int nextRoomSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingRoom> Rooms => rooms;

        /// <summary>Adds a physical room. Private rooms hold exactly one bed.</summary>
        public string AddRoom(BoardingRoomType roomType, int bedCount, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (roomType == BoardingRoomType.PrivateRoom && bedCount != 1)
                return "BoardingRoomInventory.AddRoom: a private room holds exactly one bed — nothing conjured.";
            if (bedCount <= 0)
                return "BoardingRoomInventory.AddRoom: a room needs at least one bed.";
            if (roomType == BoardingRoomType.FamilyRoom && bedCount < 2)
                return "BoardingRoomInventory.AddRoom: a family room needs at least two beds.";

            var room = new BoardingRoom
            {
                RoomId = $"bh-room-{nextRoomSequence++}",
                RoomType = roomType,
                BedCount = bedCount,
            };
            rooms.Add(room);
            diag.Add($"BoardingRoomInventory: added {roomType} '{room.RoomId}' with {bedCount} bed(s).");
            return null;
        }

        public BoardingRoom FindRoom(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId)) return null;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && string.Equals(rooms[i].RoomId, roomId, StringComparison.OrdinalIgnoreCase))
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
        /// Assigns the first open bed of the requested room type to the
        /// person. Null roomType = any open bed. Returns the refusal, or
        /// null on success.
        /// </summary>
        public string AssignBed(int personId, BoardingRoomType? roomType, out string roomId, out int bedIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            roomId = string.Empty;
            bedIndex = -1;
            if (personId <= 0)
                return "BoardingRoomInventory.AssignBed: a bed needs a real person id — anonymous sleepers are not assigned.";
            if (PersonHoldsAnyBed(personId))
                return $"BoardingRoomInventory.AssignBed: person {personId} already holds a bed — one person, one bed.";

            for (int i = 0; i < rooms.Count; i++)
            {
                BoardingRoom room = rooms[i];
                if (room == null) continue;
                if (roomType.HasValue && room.RoomType != roomType.Value) continue;
                if (room.TryFindOpenBed(out int open))
                {
                    room.OccupiedBeds.Add(new BoardingBedAssignment(open, personId));
                    roomId = room.RoomId;
                    bedIndex = open;
                    diag.Add($"BoardingRoomInventory: person {personId} takes bed {open} in {room.RoomType} '{room.RoomId}'.");
                    return null;
                }
            }

            string typeNote = roomType.HasValue ? $" of type {roomType.Value}" : string.Empty;
            return $"BoardingRoomInventory.AssignBed: no open bed{typeNote} — capacity is real beds, never invented.";
        }

        /// <summary>Assigns a specific bed — used by the boarder register after choosing the room.</summary>
        public string AssignSpecificBed(int personId, string roomId, int bedIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingRoomInventory.AssignSpecificBed: a bed needs a real person id.";
            if (PersonHoldsAnyBed(personId))
                return $"BoardingRoomInventory.AssignSpecificBed: person {personId} already holds a bed.";
            BoardingRoom room = FindRoom(roomId);
            if (room == null)
                return $"BoardingRoomInventory.AssignSpecificBed: no room '{roomId}'.";
            if (bedIndex < 0 || bedIndex >= room.BedCount)
                return $"BoardingRoomInventory.AssignSpecificBed: bed {bedIndex} does not exist in '{roomId}' ({room.BedCount} bed(s)).";
            if (room.IsBedOccupied(bedIndex))
                return $"BoardingRoomInventory.AssignSpecificBed: bed {bedIndex} in '{roomId}' is already occupied — no double-booking.";
            if (room.BedNeedsTurnover(bedIndex))
                return $"BoardingRoomInventory.AssignSpecificBed: bed {bedIndex} in '{roomId}' needs turnover first — " +
                    "chamber work turns vacated rooms before re-letting (Canon §8.1E).";

            room.OccupiedBeds.Add(new BoardingBedAssignment(bedIndex, personId));
            diag.Add($"BoardingRoomInventory: person {personId} takes bed {bedIndex} in '{roomId}'.");
            return null;
        }

        public bool ReleaseBedByPerson(int personId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0) return false;
            for (int i = 0; i < rooms.Count; i++)
            {
                BoardingRoom room = rooms[i];
                if (room?.OccupiedBeds == null) continue;
                for (int j = 0; j < room.OccupiedBeds.Count; j++)
                {
                    if (room.OccupiedBeds[j] != null && room.OccupiedBeds[j].PersonId == personId)
                    {
                        room.OccupiedBeds.RemoveAt(j);
                        diag.Add($"BoardingRoomInventory: person {personId} vacated bed in '{room.RoomId}'.");
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// D1F: soils a vacated bed — the room now needs chamber turnover
        /// before the bed is re-let (Canon §8.1E). Soiling an already-soiled
        /// or out-of-range bed is a no-op, never an error.
        /// </summary>
        public void MarkBedSoiled(string roomId, int bedIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            BoardingRoom room = FindRoom(roomId);
            if (room == null) return;
            if (bedIndex < 0 || bedIndex >= room.BedCount) return;
            if (room.BedNeedsTurnover(bedIndex)) return;
            room.BedsNeedingTurnover.Add(bedIndex);
            diag.Add($"BoardingRoomInventory: bed {bedIndex} in '{roomId}' vacated — room needs turnover before re-letting.");
        }

        /// <summary>
        /// D1F: chamber work turns the room — every soiled bed is cleaned
        /// and the room is ready for re-letting. Returns the number of beds
        /// turned.
        /// </summary>
        public int ClearRoomTurnover(string roomId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            BoardingRoom room = FindRoom(roomId);
            if (room == null || room.BedsNeedingTurnover == null) return 0;
            int turned = room.BedsNeedingTurnover.Count;
            room.BedsNeedingTurnover.Clear();
            if (turned > 0)
                diag.Add($"BoardingRoomInventory: '{roomId}' turned ({turned} bed(s) cleaned) — ready for re-letting.");
            return turned;
        }

        /// <summary>D1F: rooms with at least one bed awaiting turnover, in room order.</summary>
        public List<BoardingRoom> RoomsNeedingTurnover()
        {
            var soiled = new List<BoardingRoom>();
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].SoiledBedCount > 0)
                    soiled.Add(rooms[i]);
            return soiled;
        }

        /// <summary>D1F: total beds across the house awaiting turnover.</summary>
        public int SoiledBedCount()
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null)
                    total += rooms[i].SoiledBedCount;
            return total;
        }

        public int TotalBeds()
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++) total += rooms[i] != null ? rooms[i].BedCount : 0;
            return total;
        }        public int OccupiedBeds()
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++) total += rooms[i] != null ? rooms[i].OccupiedBedCount : 0;
            return total;
        }

        public int OpenBeds(BoardingRoomType roomType)
        {
            int total = 0;
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i] != null && rooms[i].RoomType == roomType)
                    total += rooms[i].OpenBedCount;
            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingRoomInventorySaveDto
        {
            public int NextRoomSequence = 1;
            public List<BoardingRoom> Rooms = new List<BoardingRoom>();
        }

        public BoardingRoomInventorySaveDto CaptureSaveDto()
        {
            var dto = new BoardingRoomInventorySaveDto { NextRoomSequence = nextRoomSequence };
            foreach (BoardingRoom room in rooms)
            {
                if (room == null) continue;
                var copy = new BoardingRoom
                {
                    RoomId = room.RoomId,
                    RoomType = room.RoomType,
                    BedCount = room.BedCount,
                };
                if (room.OccupiedBeds != null)
                    foreach (BoardingBedAssignment a in room.OccupiedBeds)
                        if (a != null) copy.OccupiedBeds.Add(new BoardingBedAssignment(a.BedIndex, a.PersonId));
                if (room.BedsNeedingTurnover != null)
                    foreach (int soiled in room.BedsNeedingTurnover)
                        if (soiled >= 0 && soiled < copy.BedCount && !copy.BedNeedsTurnover(soiled))
                            copy.BedsNeedingTurnover.Add(soiled);
                dto.Rooms.Add(copy);
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingRoomInventorySaveDto dto)
        {
            rooms.Clear();
            nextRoomSequence = 1;
            if (dto == null) return;
            nextRoomSequence = Math.Max(1, dto.NextRoomSequence);
            if (dto.Rooms == null) return;
            foreach (BoardingRoom room in dto.Rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.RoomId)) continue;
                var copy = new BoardingRoom
                {
                    RoomId = room.RoomId,
                    RoomType = room.RoomType,
                    BedCount = Math.Max(0, room.BedCount),
                };
                if (room.OccupiedBeds != null)
                    foreach (BoardingBedAssignment a in room.OccupiedBeds)
                    {
                        if (a == null || a.PersonId <= 0) continue;
                        if (a.BedIndex < 0 || a.BedIndex >= copy.BedCount) continue;
                        if (copy.IsBedOccupied(a.BedIndex)) continue;
                        copy.OccupiedBeds.Add(new BoardingBedAssignment(a.BedIndex, a.PersonId));
                    }
                if (room.BedsNeedingTurnover != null)
                    foreach (int soiled in room.BedsNeedingTurnover)
                    {
                        if (soiled < 0 || soiled >= copy.BedCount) continue;
                        if (copy.IsBedOccupied(soiled)) continue;
                        if (copy.BedNeedsTurnover(soiled)) continue;
                        copy.BedsNeedingTurnover.Add(soiled);
                    }
                rooms.Add(copy);
            }
        }
        #endregion
    }
}
