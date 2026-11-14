using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Q33: the single resolved mapping for the freight business. The authored
    /// instance id is a preference, the business type is the requirement.
    /// </summary>
    public static class FreightBusinessResolver
    {
        /// <summary>The authored freight business id; a preference, never a requirement.</summary>
        public const string PreferredFreightBusinessInstanceId = "livery_freight_003";

        /// <summary>
        /// Resolves the freight business from a business list. Pass 1: the preferred id
        /// with type LiveryFreight (case-insensitive, matching FindByInstanceId).
        /// Pass 2: the first business of type LiveryFreight in list order.
        /// Returns false (freight null) when no LiveryFreight business exists at all.
        /// Never throws on a null or empty list.
        /// </summary>
        public static bool TryResolveFreightBusiness(
            IReadOnlyList<BusinessInstanceState> businesses,
            out BusinessInstanceState freight)
        {
            freight = null;
            if (businesses == null) return false;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState candidate = businesses[i];
                if (candidate != null
                    && candidate.BusinessType == BusinessType.LiveryFreight
                    && string.Equals(candidate.InstanceId, PreferredFreightBusinessInstanceId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    freight = candidate;
                    return true;
                }
            }
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState candidate = businesses[i];
                if (candidate != null && candidate.BusinessType == BusinessType.LiveryFreight)
                {
                    freight = candidate;
                    return true;
                }
            }
            return false;
        }
    }
}
