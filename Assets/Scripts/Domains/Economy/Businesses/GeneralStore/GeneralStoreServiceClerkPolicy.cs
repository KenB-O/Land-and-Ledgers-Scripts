using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Resolves the people who may be considered for physical General Store
    /// counter service. A paid clerk assignment takes precedence; the owner
    /// is also a legitimate owner-operator when no clerk slot is assigned or
    /// that assigned clerk is not physically available. Physical presence
    /// and TaskAuthority availability remain the caller's responsibility.
    /// </summary>
    public static class GeneralStoreServiceClerkPolicy
    {
        public static List<int> CandidatePersonIds(int assignedClerkPersonId, int ownerPersonId)
        {
            var candidates = new List<int>(2);
            AddIfValid(candidates, assignedClerkPersonId);
            AddIfValid(candidates, ownerPersonId);
            return candidates;
        }

        public static bool IsCandidate(int personId, int assignedClerkPersonId, int ownerPersonId)
        {
            return personId >= 0
                && (personId == assignedClerkPersonId || personId == ownerPersonId);
        }

        private static void AddIfValid(List<int> candidates, int personId)
        {
            if (personId >= 0 && !candidates.Contains(personId))
                candidates.Add(personId);
        }
    }
}
