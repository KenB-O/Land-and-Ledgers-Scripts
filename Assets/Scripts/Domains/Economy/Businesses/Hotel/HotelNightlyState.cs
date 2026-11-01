using System;
using LandLedgers.Economy.Businesses.BoardingHouse;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: one guest's resolved nightly state — the settlement-wide
    /// answer to the Canon audit question "Where does this Person sleep
    /// tonight?" (Canon Part II §2.1; Canon §2.4 "hotel room").
    ///
    /// This EXTENDS the W2C pattern rather than duplicating it: it reuses
    /// the <see cref="BoardingNightlyState"/> vocabulary (whose
    /// <see cref="BoardingNightlyState.HotelRoom"/> member W2C explicitly
    /// reserved for this package) and mirrors
    /// <see cref="BoardingNightlyPlacement"/> with guest-flavored detail —
    /// the room number, bed, nightly rate, and stay term. Tech §2.10: a
    /// hotel guest sleeps locally without becoming a resident.
    /// </summary>
    [Serializable]
    public sealed class HotelNightlyPlacement
    {
        public int PersonId;
        public BoardingNightlyState NightlyState = BoardingNightlyState.HotelRoom;
        public string RoomNumber = string.Empty;
        public int BedIndex = -1;
        public string BusinessInstanceId = string.Empty;
        public HotelRoomClass RoomClass = HotelRoomClass.SingleRoom;
        public HotelStayKind StayKind = HotelStayKind.Nightly;
        public int LockedNightlyRateCents;

        public HotelNightlyPlacement() { }

        public HotelNightlyPlacement(int personId, string roomNumber, int bedIndex,
            string businessInstanceId, HotelRoomClass roomClass, HotelStayKind stayKind,
            int lockedNightlyRateCents)
        {
            PersonId = personId;
            NightlyState = BoardingNightlyState.HotelRoom;
            RoomNumber = roomNumber ?? string.Empty;
            BedIndex = bedIndex;
            BusinessInstanceId = businessInstanceId ?? string.Empty;
            RoomClass = roomClass;
            StayKind = stayKind;
            LockedNightlyRateCents = Math.Max(0, lockedNightlyRateCents);
        }
    }
}
