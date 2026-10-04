using System;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the nightly states a Person can legitimately hold (Canon §2.4).
    /// "Each Person needs a legitimate nightly state such as household
    /// accommodation, rented room, boarding bed, hotel room, employer bunk,
    /// camp, temporary hosting, travel lodging, precarious shelter or
    /// explicit no-stable-shelter state."
    ///
    /// This runtime only ever assigns <see cref="BoardingBed"/>; the other
    /// members exist so the enum is the single vocabulary for the
    /// settlement-wide nightly-state audit the W3 hotel package builds on.
    /// <see cref="HotelRoom"/> is RESERVED for W3 — no boarding-house code
    /// may produce it.
    /// </summary>
    public enum BoardingNightlyState
    {
        Unknown = 0,
        HouseholdAccommodation = 1,
        RentedRoom = 2,
        BoardingBed = 3,
        /// <summary>Reserved for the W3 hotel package. Never assigned here.</summary>
        HotelRoom = 4,
        EmployerBunk = 5,
        Camp = 6,
        TemporaryHosting = 7,
        TravelLodging = 8,
        PrecariousShelter = 9,
        NoStableShelter = 10,
    }

    /// <summary>
    /// W2C: one person's resolved nightly state — the answer to the Canon
    /// audit question "Where does this Person sleep tonight?" (Canon Part II
    /// §2.1). For boarders this is always a boarding bed in a specific room.
    /// </summary>
    [Serializable]
    public sealed class BoardingNightlyPlacement
    {
        public int PersonId;
        public BoardingNightlyState NightlyState = BoardingNightlyState.Unknown;
        public string RoomId = string.Empty;
        public int BedIndex = -1;
        public string BusinessInstanceId = string.Empty;

        public BoardingNightlyPlacement() { }

        public BoardingNightlyPlacement(int personId, BoardingNightlyState state,
            string roomId, int bedIndex, string businessInstanceId)
        {
            PersonId = personId;
            NightlyState = state;
            RoomId = roomId ?? string.Empty;
            BedIndex = bedIndex;
            BusinessInstanceId = businessInstanceId ?? string.Empty;
        }
    }
}
