// Series 2 Pass 8 (freight + fuel chain): the lot-to-reserve link
// for bought household fuel. It is deliberately small, because the
// link itself is small: a household that bought fuel_wood units
// (through the Pass 7 fuel adapter in the shopping loop, or
// delivered by a carrier leg whose receiving callback calls this
// class) must see those units in its fuel_wood reserve, or the
// heating evaluator will keep reporting a need the household just
// paid to fill.
//
// Conversion: household units -> reserve units is a caller
// parameter (ReserveUnitsPerHouseholdUnit, >= 1), NEVER a decided
// game rate. In particular it must not be read as canonising Pass
// 7's adapter default (1 intake unit = 1 household unit): that
// default is intake -> household unit, this parameter is household
// unit -> reserve unit, and the two conversions are independent
// until Kennedy decides either one. A test that credits with a
// factor of 2 proves the parameter is honoured; it does not prove
// that 2 is the game rate.
//
// What this class does NOT do: it does not buy fuel, does not
// dispense it, does not touch cash, and does not create lots. The
// shopping loop / adapter / carrier leg already moved the fuel and
// the money; this class only mirrors a delivered quantity into
// the reserve the heating system reads. If the household has no
// fuel_wood reserve (GetReserve returns null after initialisation),
// it refuses loudly instead of inventing one. Plain C#,
// Roslyn-testable.
using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.FreightFuel
{
    /// <summary>Series 2 Pass 8: bought household fuel -> fuel_wood reserve, conversion as a caller parameter.</summary>
    public static class Series2HouseholdFuelReserveLink
    {
        /// <summary>
        /// Credits a delivered household fuel quantity into the
        /// household's fuel_wood reserve. Returns the reserve units
        /// credited, or -1 with a diagnostic when the household, the
        /// reserve, or the quantity cannot support the credit.
        /// Nothing is credited on refusal.
        /// </summary>
        public static int CreditReserve(
            HouseholdState household, int householdUnits,
            int reserveUnitsPerHouseholdUnit, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (household == null)
            {
                diag.Add("Series2HouseholdFuelReserveLink: no household; nothing credited.");
                return -1;
            }
            if (householdUnits <= 0)
            {
                diag.Add("Series2HouseholdFuelReserveLink: a positive delivered quantity is required; nothing credited.");
                return -1;
            }
            int factor = Math.Max(1, reserveUnitsPerHouseholdUnit);
            HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            if (reserve == null)
            {
                diag.Add("Series2HouseholdFuelReserveLink: household has no fuel_wood reserve after initialisation; nothing credited, no reserve invented.");
                return -1;
            }
            int reserveUnits = householdUnits * factor;
            reserve.currentUnits += reserveUnits;
            diag.Add($"Series2HouseholdFuelReserveLink: credited {reserveUnits} fuel_wood reserve unit(s) " +
                $"from {householdUnits} delivered household unit(s) (factor {factor}, caller parameter, not a decided rate) on day {dayIndex}; reserve now {reserve.currentUnits}u.");
            return reserveUnits;
        }
    }
}
