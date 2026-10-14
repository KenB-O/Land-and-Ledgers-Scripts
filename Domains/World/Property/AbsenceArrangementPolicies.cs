using System;
using LandLedgers.World.Journeys;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass VIII Worker 7: the lenient side of the HousingAbsenceSweep boundary.
    ///
    /// The daily driver runs the sweep strict (no exemptions). This class
    /// designs the lenient arrangement recognition the housing worker left to
    /// the autonomy worker: which temporary absences count as a VALID
    /// arrangement, so the person keeps their bed instead of losing it to the
    /// sweep.
    ///
    /// Two arrangements are recognized, and only these two:
    ///   1. A live agreement still stands (HousingAbsenceSweep.AgreementStillStands):
    ///      a prepaid or contracted tenancy/boarding agreement that has not
    ///      ended keeps the space through the absence.
    ///   2. A legitimate multi-day trip (ActiveReturnJourney): the occupant is
    ///      on a recorded off-map journey WITH an expected return — a freight
    ///      run, a buying trip, travel to kin. The bed is held for them while
    ///      the journey is active. A one-way migration (no return expected)
    ///      is not a valid arrangement: the departure is permanent.
    ///
    /// Death is never a valid arrangement: the optional isDeceased guard lets
    /// the caller keep the sweep's death handling strict even under a lenient
    /// policy. Without it, a decedent mid-journey would wrongly keep a bed.
    ///
    /// The returned predicate captures the day index: the caller rebuilds it
    /// once per sweep day (a stale day would hold beds past a journey's end).
    /// </summary>
    public static class AbsenceArrangementPolicies
    {
        /// <summary>
        /// True when the person is on a legitimate temporary trip: an ACTIVE
        /// off-map person-journey with a recorded expected return
        /// (DueReturnDayIndex >= 0). Freight journeys, buying trips, and
        /// family travel all qualify; one-way migration does not.
        /// </summary>
        public static bool ActiveReturnJourney(
            OffMapJourneyAuthority offMap, int personId, int dayIndex)
        {
            if (offMap == null || personId < 0)
            {
                return false;
            }
            foreach (OffMapJourneyRecord record in offMap.AllJourneys())
            {
                if (record == null)
                {
                    continue;
                }
                if (!record.IsActive)
                {
                    continue;
                }
                if (record.TravelerKind != OffMapTravelerKind.Person)
                {
                    continue;
                }
                if (record.PersonId != personId)
                {
                    continue;
                }
                if (record.DueReturnDayIndex < 0)
                {
                    continue; // one-way migration: no return expected
                }
                if (record.DueReturnDayIndex < dayIndex)
                {
                    continue; // the return is overdue: the trip is no longer a held arrangement
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Builds the lenient hasValidAbsenceArrangement predicate for
        /// HousingAbsenceSweep.Sweep. An occupancy stands when its agreement
        /// still stands OR its occupant is on a legitimate return journey.
        /// The dead are never exempted when isDeceased is supplied.
        /// </summary>
        public static Func<ResidentialOccupancy, bool> LenientPolicy(
            HousingAuthority housing,
            OffMapJourneyAuthority offMap,
            int dayIndex,
            Func<int, bool> isDeceased = null)
        {
            return occupancy =>
            {
                if (occupancy == null || occupancy.PersonId < 0)
                {
                    return false;
                }
                if (isDeceased != null)
                {
                    bool dead;
                    try
                    {
                        dead = isDeceased(occupancy.PersonId);
                    }
                    catch (Exception)
                    {
                        dead = false;
                    }
                    if (dead)
                    {
                        return false; // death ends every arrangement
                    }
                }
                if (HousingAbsenceSweep.AgreementStillStands(housing, occupancy, dayIndex))
                {
                    return true;
                }
                return ActiveReturnJourney(offMap, occupancy.PersonId, dayIndex);
            };
        }
    }
}
