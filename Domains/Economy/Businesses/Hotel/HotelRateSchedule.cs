using System;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: the hotel's rate schedule as data. Canon §8.1D: "The owner can
    /// set ... weekly or longer-stay terms, transient rates, ... reservation
    /// of rooms for expected contract or commercial customers." The hotel
    /// sells room nights (nightly stay) and weekly terms; the nightly rate
    /// and the weekly rate are both proprietor policy, settable per hotel.
    /// Money moves only through ledger authorities, never in this schedule.
    ///
    /// Default calibration (Canon Part XV): the hotel sits above the
    /// boarding house (transient 25¢ room-only) because §8.1C's hotel guests
    /// are "higher-paying transient guests" — commercial travelers pay for
    /// the private room. Weekly terms run about 5.5 nightly rates (a modest
    /// staying-the-week discount, period custom).
    /// </summary>
    [Serializable]
    public sealed class HotelRateSchedule
    {
        [SerializeField, Min(0)]
        private int singleNightlyCents = 40;
        [SerializeField, Min(0)]
        private int doubleNightlyCents = 60;
        [SerializeField, Min(0)]
        private int parlorSuiteNightlyCents = 100;
        [SerializeField, Min(0)]
        private int singleWeeklyCents = 220;
        [SerializeField, Min(0)]
        private int doubleWeeklyCents = 330;
        [SerializeField, Min(0)]
        private int parlorSuiteWeeklyCents = 550;

        public HotelRateSchedule() { }

        public HotelRateSchedule(int singleNightlyCents, int doubleNightlyCents, int parlorSuiteNightlyCents,
            int singleWeeklyCents, int doubleWeeklyCents, int parlorSuiteWeeklyCents)
        {
            this.singleNightlyCents = Math.Max(0, singleNightlyCents);
            this.doubleNightlyCents = Math.Max(0, doubleNightlyCents);
            this.parlorSuiteNightlyCents = Math.Max(0, parlorSuiteNightlyCents);
            this.singleWeeklyCents = Math.Max(0, singleWeeklyCents);
            this.doubleWeeklyCents = Math.Max(0, doubleWeeklyCents);
            this.parlorSuiteWeeklyCents = Math.Max(0, parlorSuiteWeeklyCents);
        }

        public int SingleNightlyCents => Math.Max(0, singleNightlyCents);
        public int DoubleNightlyCents => Math.Max(0, doubleNightlyCents);
        public int ParlorSuiteNightlyCents => Math.Max(0, parlorSuiteNightlyCents);
        public int SingleWeeklyCents => Math.Max(0, singleWeeklyCents);
        public int DoubleWeeklyCents => Math.Max(0, doubleWeeklyCents);
        public int ParlorSuiteWeeklyCents => Math.Max(0, parlorSuiteWeeklyCents);

        /// <summary>Per-night rate for a room class (nightly stay).</summary>
        public int NightlyRateCents(HotelRoomClass roomClass)
        {
            switch (roomClass)
            {
                case HotelRoomClass.DoubleRoom: return DoubleNightlyCents;
                case HotelRoomClass.ParlorSuite: return ParlorSuiteNightlyCents;
                default: return SingleNightlyCents;
            }
        }

        /// <summary>Per-week rate for a room class (weekly term).</summary>
        public int WeeklyRateCents(HotelRoomClass roomClass)
        {
            switch (roomClass)
            {
                case HotelRoomClass.DoubleRoom: return DoubleWeeklyCents;
                case HotelRoomClass.ParlorSuite: return ParlorSuiteWeeklyCents;
                default: return SingleWeeklyCents;
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelRateScheduleSaveDto
        {
            public int SingleNightlyCents;
            public int DoubleNightlyCents;
            public int ParlorSuiteNightlyCents;
            public int SingleWeeklyCents;
            public int DoubleWeeklyCents;
            public int ParlorSuiteWeeklyCents;
        }

        public HotelRateScheduleSaveDto CaptureSaveDto()
        {
            return new HotelRateScheduleSaveDto
            {
                SingleNightlyCents = SingleNightlyCents,
                DoubleNightlyCents = DoubleNightlyCents,
                ParlorSuiteNightlyCents = ParlorSuiteNightlyCents,
                SingleWeeklyCents = SingleWeeklyCents,
                DoubleWeeklyCents = DoubleWeeklyCents,
                ParlorSuiteWeeklyCents = ParlorSuiteWeeklyCents,
            };
        }

        public void LoadFromSaveDto(HotelRateScheduleSaveDto dto)
        {
            if (dto == null) return;
            singleNightlyCents = Math.Max(0, dto.SingleNightlyCents);
            doubleNightlyCents = Math.Max(0, dto.DoubleNightlyCents);
            parlorSuiteNightlyCents = Math.Max(0, dto.ParlorSuiteNightlyCents);
            singleWeeklyCents = Math.Max(0, dto.SingleWeeklyCents);
            doubleWeeklyCents = Math.Max(0, dto.DoubleWeeklyCents);
            parlorSuiteWeeklyCents = Math.Max(0, dto.ParlorSuiteWeeklyCents);
        }
        #endregion
    }
}
