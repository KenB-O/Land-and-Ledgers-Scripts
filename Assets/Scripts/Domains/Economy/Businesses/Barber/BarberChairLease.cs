using EntityId = LandLedgers.Primitives.EntityId;

using System;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// D1B: one chair leased to an independent barber operator. Canon §8.5:
    /// a landlord may lease a furnished room, station/chair or other functional
    /// commercial space to an independent operator who keeps customer revenue
    /// and pays rent — premises use, not disguised employment. Canon §8.6: the
    /// tenant remains an independent business (tenant recruitment is not
    /// hiring). The exact historical prevalence of individual barber-chair
    /// rental in 1870s Dakota is a canon research hold — this models the
    /// general space-use agreement, and makes no claim about how common the
    /// custom was.
    ///
    /// The shop runtime never schedules the tenant's customers: the operator
    /// runs their own services and keeps that revenue. The runtime models the
    /// landlord side only — the chair leaves the shop's pool while leased, and
    /// rent accrues as obligations the caller settles through ledger
    /// authorities (money never moves here).
    /// </summary>
    [Serializable]
    public sealed class BarberChairLease
    {
        /// <summary>Stable lease id, e.g. "barb-biz-1-lease-0".</summary>
        public string LeaseId = string.Empty;

        public int ChairIndex;

        /// <summary>The independent operator (tenant). EntityKind.Person.</summary>
        public EntityId OperatorPersonId = EntityId.Invalid;

        /// <summary>Cents of rent per day the operator pays the shop.</summary>
        public int RentCentsPerDay;

        /// <summary>First day the lease covers (inclusive).</summary>
        public int StartDayIndex;

        /// <summary>First day the lease no longer covers (exclusive).</summary>
        public int EndDayIndexExclusive;

        public string Notes = string.Empty;

        public BarberChairLease() { }

        /// <summary>True when the lease covers the given day.</summary>
        public bool CoversDay(int dayIndex)
        {
            return dayIndex >= StartDayIndex && dayIndex < EndDayIndexExclusive;
        }

        /// <summary>True when two leases on the same chair overlap in time.</summary>
        public bool Overlaps(BarberChairLease other)
        {
            if (other == null) return false;
            return StartDayIndex < other.EndDayIndexExclusive
                && other.StartDayIndex < EndDayIndexExclusive;
        }
    }

    /// <summary>
    /// D1B: one day's rent owed by a chair tenant. DATA for the caller — the
    /// caller settles these as ordinary ledger transfers; money moves only
    /// through ledger authorities, never as a runtime side effect.
    /// </summary>
    [Serializable]
    public sealed class BarberChairRentObligation
    {
        public string LeaseId = string.Empty;
        public string LandlordBusinessInstanceId = string.Empty;
        public int ChairIndex;
        public EntityId OperatorPersonId = EntityId.Invalid; // EntityKind.Person
        public int DayIndex;
        public int RentCents;

        public BarberChairRentObligation() { }
    }
}
