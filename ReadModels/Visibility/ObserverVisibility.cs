using System;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass IV (Worker 4, Phase Q): the observer-visibility rule, centralized.
    /// Read models describe the living world, but they must NEVER leak hidden
    /// NPC knowledge to the player: the player learns what their viewpoint
    /// legitimately permits, nothing more.
    ///
    /// Rules (conservative — when in doubt, hide):
    /// - A person sees their own state fully.
    /// - A person sees another person's CURRENT activity only when they share
    ///   a household, share a location right now, or the location is public
    ///   (a market square is observable; a farmhouse kitchen is not).
    /// - A person sees their own household's members more fully (activity +
    ///   purpose) than strangers (activity only, no purpose/employer/wages).
    /// - Business FINANCIALS (cash, margins, wages, liabilities) are visible
    ///   only to the owner and delegated managers of that business. Work
    ///   activity on the premises is visible to anyone physically present.
    /// - Equipment CONDITION and OCCUPANCY at a location are visible to anyone
    ///   present there or to the owner; who holds a reservation is visible
    ///   only to the owner/manager (others see "occupied", anonymized).
    /// - Histories are bounded (read models carry the last N events, never a
    ///   tick log) and redact exactly like the live fields.
    /// </summary>
    public static class ObserverVisibility
    {
        /// <summary>
        /// True when the observer is physically at the location, or the
        /// location is public enough to observe from afar.
        /// </summary>
        public static bool CanObserveLocation(
            int observerPersonId,
            string observerLocationId,
            string subjectLocationId,
            Func<string, bool> isPublicLocation)
        {
            if (string.IsNullOrEmpty(subjectLocationId))
            {
                return false;
            }

            if (string.Equals(observerLocationId, subjectLocationId, StringComparison.Ordinal))
            {
                return true;
            }

            return isPublicLocation != null && isPublicLocation(subjectLocationId);
        }

        /// <summary>
        /// True when the observer may see the subject's CURRENT activity
        /// (what they are doing right now).
        /// </summary>
        public static bool CanSeePersonActivity(
            int observerPersonId,
            int subjectPersonId,
            int observerHouseholdId,
            int subjectHouseholdId,
            string observerLocationId,
            string subjectLocationId,
            Func<string, bool> isPublicLocation)
        {
            if (observerPersonId == subjectPersonId)
            {
                return true;
            }

            if (observerHouseholdId >= 0 && observerHouseholdId == subjectHouseholdId)
            {
                return true;
            }

            return CanObserveLocation(observerPersonId, observerLocationId, subjectLocationId, isPublicLocation);
        }

        /// <summary>
        /// True when the observer may see WHY the subject acts (purpose,
        /// employer, task details) — a narrower circle than bare activity.
        /// </summary>
        public static bool CanSeePersonPurpose(
            int observerPersonId,
            int subjectPersonId,
            int observerHouseholdId,
            int subjectHouseholdId)
        {
            if (observerPersonId == subjectPersonId)
            {
                return true;
            }

            return observerHouseholdId >= 0 && observerHouseholdId == subjectHouseholdId;
        }

        /// <summary>
        /// True when the observer may see a business's financials
        /// (cash, wages, margins, liabilities).
        /// </summary>
        public static bool CanSeeBusinessFinancials(
            int observerPersonId,
            string businessInstanceId,
            Func<string, int> ownerPersonIdOf,
            Func<string, int, bool> isDelegatedManagerOf)
        {
            if (ownerPersonIdOf != null && ownerPersonIdOf(businessInstanceId) == observerPersonId)
            {
                return true;
            }

            return isDelegatedManagerOf != null && isDelegatedManagerOf(businessInstanceId, observerPersonId);
        }

        /// <summary>
        /// True when the observer may see work activity AT the business
        /// premises (tasks in progress, workers present) — anyone physically
        /// present, plus owner/manager anywhere.
        /// </summary>
        public static bool CanSeeBusinessFloor(
            int observerPersonId,
            string businessInstanceId,
            string observerLocationId,
            string businessLocationId,
            Func<string, int> ownerPersonIdOf,
            Func<string, int, bool> isDelegatedManagerOf)
        {
            if (CanSeeBusinessFinancials(observerPersonId, businessInstanceId, ownerPersonIdOf, isDelegatedManagerOf))
            {
                return true;
            }

            return string.Equals(observerLocationId, businessLocationId, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the observer may see WHO holds an equipment reservation.
        /// Others see only that the equipment is occupied.
        /// </summary>
        public static bool CanSeeReservationHolder(
            int observerPersonId,
            string equipmentId,
            Func<string, int> ownerPersonIdOfEquipment,
            Func<string, int, bool> isDelegatedManagerOfEquipment)
        {
            // Equipment "owner" here is the owning business; the observer must
            // own or manage that business. The caller resolves business→person.
            if (ownerPersonIdOfEquipment != null && ownerPersonIdOfEquipment(equipmentId) == observerPersonId)
            {
                return true;
            }

            return isDelegatedManagerOfEquipment != null && isDelegatedManagerOfEquipment(equipmentId, observerPersonId);
        }
    }
}
