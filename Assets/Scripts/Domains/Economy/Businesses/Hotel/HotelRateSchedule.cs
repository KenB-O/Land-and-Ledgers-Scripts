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
    /// staying-the-week discount, period custom); D2A monthly terms run
    /// 4× weekly (the boarding-house D1F ratio).
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
        [SerializeField, Min(0)]
        private int singleMonthlyCents = 880;
        [SerializeField, Min(0)]
        private int doubleMonthlyCents = 1320;
        [SerializeField, Min(0)]
        private int parlorSuiteMonthlyCents = 2200;

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
        /// <summary>D2A: monthly rates — the §8.1D longer-stay term.</summary>
        public int SingleMonthlyCents => Math.Max(0, singleMonthlyCents);
        public int DoubleMonthlyCents => Math.Max(0, doubleMonthlyCents);
        public int ParlorSuiteMonthlyCents => Math.Max(0, parlorSuiteMonthlyCents);

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

        /// <summary>D2A: per-month rate for a room class (monthly term).</summary>
        public int MonthlyRateCents(HotelRoomClass roomClass)
        {
            switch (roomClass)
            {
                case HotelRoomClass.DoubleRoom: return DoubleMonthlyCents;
                case HotelRoomClass.ParlorSuite: return ParlorSuiteMonthlyCents;
                default: return SingleMonthlyCents;
            }
        }

        private static int MonthlyOrDefault(int monthlyCents, int weeklyCents)
        {
            monthlyCents = Math.Max(0, monthlyCents);
            if (monthlyCents == 0 && weeklyCents > 0) return 4 * weeklyCents;
            return monthlyCents;
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
            public int SingleMonthlyCents;
            public int DoubleMonthlyCents;
            public int ParlorSuiteMonthlyCents;
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
                SingleMonthlyCents = SingleMonthlyCents,
                DoubleMonthlyCents = DoubleMonthlyCents,
                ParlorSuiteMonthlyCents = ParlorSuiteMonthlyCents,
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
            // D2A migration: saves written before monthly terms existed
            // carry zero monthly rates. A zero monthly rate with a
            // positive weekly rate means "unset" — fall back to the 4×
            // weekly calibration default rather than a free room.
            singleMonthlyCents = MonthlyOrDefault(dto.SingleMonthlyCents, singleWeeklyCents);
            doubleMonthlyCents = MonthlyOrDefault(dto.DoubleMonthlyCents, doubleWeeklyCents);
            parlorSuiteMonthlyCents = MonthlyOrDefault(dto.ParlorSuiteMonthlyCents, parlorSuiteWeeklyCents);
        }
        #endregion
    }
}
