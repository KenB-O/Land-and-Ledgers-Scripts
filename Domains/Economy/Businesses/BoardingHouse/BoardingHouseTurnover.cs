namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: room-turnover calibration (Canon §8.1E "bed turnover", §8.1G
    /// "clean rooms"). A vacated bed soils its room: the room cannot be
    /// re-let until chamber work turns it (bed stripped, swept, fresh
    /// bedding). All values are TUNING (Canon Part XV) — the canon says
    /// turnover is real labor, not how many minutes it takes.
    ///
    /// Deliberately NOT modeled (routed around): daily tidying of occupied
    /// rooms — the canon never states a frequency, so D1F covers only the
    /// checkout-to-relet turnover the canon unambiguously describes.
    /// </summary>
    public static class BoardingHouseTurnoverPolicy
    {
        /// <summary>TUNING: chamber-work minutes to turn one room with vacated bed(s).</summary>
        public const int MinutesPerRoomTurnover = 20;

        /// <summary>
        /// TUNING: minutes per day the proprietor and family give room
        /// turnover when no chamber staff have ever been named (Canon §8.1E:
        /// "small houses can rely heavily on proprietor and family labor").
        /// </summary>
        public const int ProprietorTurnoverMinutesPerDay = 60;
    }
}
