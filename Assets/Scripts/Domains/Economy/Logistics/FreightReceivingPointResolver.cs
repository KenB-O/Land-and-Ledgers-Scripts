// Lane 9d (M-R5): pure receiving-point resolution for freight deliveries.
//
// The exact-id lookup for "receiving" fails on every building in the dev
// town (no location carries that id), so every delivery falls back to the
// authored front door. The door fallback is legitimate, but it must be the
// exception, not the universal path. This resolver is the pure policy the
// Unity-bound PopulationPathingDirector calls: an exact-id match wins;
// otherwise a Receiving-type authored point wins (e.g. the lumber yard's
// "lumberyard-receiving-approach", activity type 5); otherwise there is no
// receiving station and the caller falls back to the front door explicitly.
//
// Pure strings/ints only, so the Roslyn harness can pin it: the caller maps
// its authored locations to AuthoredReceivingPoint (id + type value) and
// passes the Receiving type value from its own BuildingActivityLocationType
// enum, which this file deliberately does not reference (Unity-bound).
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Logistics
{
    public static class FreightReceivingPointResolver
    {
        /// <summary>One authored activity location, reduced to the two facts the resolution needs.</summary>
        public readonly struct AuthoredReceivingPoint
        {
            public AuthoredReceivingPoint(string activityLocationId, int activityType)
            {
                ActivityLocationId = activityLocationId ?? string.Empty;
                ActivityType = activityType;
            }

            public string ActivityLocationId { get; }
            public int ActivityType { get; }
        }

        public enum ReceivingPointSource
        {
            /// <summary>The requested id is authored exactly.</summary>
            ExactId = 0,
            /// <summary>No exact id; a Receiving-type point was resolved instead.</summary>
            ReceivingType = 1,
            /// <summary>No receiving station is authored at all: the caller must use the explicit front-door fallback.</summary>
            None = 2,
        }

        public readonly struct ReceivingPointResolution
        {
            public ReceivingPointResolution(string activityLocationId, ReceivingPointSource source, string reason)
            {
                ActivityLocationId = activityLocationId ?? string.Empty;
                Source = source;
                Reason = reason ?? string.Empty;
            }

            public string ActivityLocationId { get; }
            public ReceivingPointSource Source { get; }
            public string Reason { get; }
        }

        /// <summary>
        /// Resolves the receiving point for one building. Exact-id first; a
        /// Receiving-type point second; None (with the reason naming the gap)
        /// when the building authors no receiving station at all. Never
        /// invents a location: an empty or duplicated id refuses, and a
        /// duplicated Receiving-type authoring refuses rather than guessing
        /// which station is the receiving point.
        /// </summary>
        public static ReceivingPointResolution Resolve(
            string requestedActivityId,
            IReadOnlyList<AuthoredReceivingPoint> authored,
            int receivingType)
        {
            if (string.IsNullOrWhiteSpace(requestedActivityId))
            {
                return new ReceivingPointResolution(string.Empty, ReceivingPointSource.None,
                    "No receiving activity id was requested.");
            }

            if (authored != null)
            {
                foreach (AuthoredReceivingPoint point in authored)
                {
                    if (point.ActivityLocationId == null) continue;
                    if (!string.Equals(point.ActivityLocationId, requestedActivityId, StringComparison.Ordinal)) continue;
                    return new ReceivingPointResolution(point.ActivityLocationId, ReceivingPointSource.ExactId, string.Empty);
                }

                // Only the literal "receiving" request may resolve by type:
                // any other id names a specific station, and a type match
                // would silently deliver to the wrong point.
                if (string.Equals(requestedActivityId, "receiving", StringComparison.OrdinalIgnoreCase))
                {
                    string typeMatch = null;
                    foreach (AuthoredReceivingPoint point in authored)
                    {
                        if (point.ActivityType != receivingType) continue;
                        if (string.IsNullOrWhiteSpace(point.ActivityLocationId)) continue;
                        if (typeMatch != null)
                        {
                            return new ReceivingPointResolution(string.Empty, ReceivingPointSource.None,
                                "Receiving-type activity is authored more than once; refusing to guess which station is the receiving point.");
                        }
                        typeMatch = point.ActivityLocationId;
                    }
                    if (typeMatch != null)
                    {
                        return new ReceivingPointResolution(typeMatch, ReceivingPointSource.ReceivingType,
                            "No location carries the id 'receiving'; resolved to the authored Receiving-type point '" + typeMatch + "'.");
                    }
                }
            }

            return new ReceivingPointResolution(string.Empty, ReceivingPointSource.None,
                "No receiving station is authored on this building; the front door is the only fallback.");
        }
    }
}
