using System;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// P5: player property-tax calibration. Canon Part XV §17 requires a real annual
    /// assessment cycle with DATED rates and forbids one empire-wide timeless rate
    /// (§17.5: "The system should query the owning ledger, asset type, jurisdiction
    /// and year rather than applying one empire-wide Tax Rate"). The historical
    /// research delivered no Dakota Territory millage table, so every number below is
    /// an explicitly-labeled CALIBRATION placeholder for Kennedy to replace with dated
    /// research — not a historical claim. DESIGN FORK P5-TAX-RATE recorded.
    /// </summary>
    public static class PropertyTaxCalibration
    {
        public const int DaysPerTaxYear = 365;

        /// <summary>CALIBRATION: 150 bps = 1.5% of assessed value per year.</summary>
        public const int LevyMillRateBps = 150;

        /// <summary>CALIBRATION: days after the levy before the bill is due.</summary>
        public const int DueDaysAfterLevy = 60;

        /// <summary>CALIBRATION: 100 bps/month penalty on the base levy once delinquent.</summary>
        public const int PenaltyBpsMonthly = 100;

        /// <summary>CALIBRATION: 600 bps/year interest on base + accrued penalties.</summary>
        public const int InterestBpsAnnual = 600;

        public static int TaxYearForDay(int dayIndex)
        {
            return Math.Max(0, dayIndex) / DaysPerTaxYear;
        }

        public static int ComputeLevyCents(int assessedValueCents)
        {
            if (assessedValueCents <= 0) return 0;
            return Math.Max(1, (int)Math.Round(assessedValueCents * (LevyMillRateBps / 10000.0)));
        }
    }
}
