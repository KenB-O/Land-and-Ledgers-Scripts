// Lane 9c (B-7 + M-R13, Agents B §5.7 / M §7): startup access
// validation for buildings, as pure gap text. The decision (which
// required activity points are missing or unreachable from
// main-entrance) lives here so the harness can pin it; the Unity
// glue (DevelopmentTownFurnishingInitializer for homes,
// DevelopmentBusinessInteriorInitializer for businesses) computes
// each check from the live BuildingFootprintAuthority via the
// type-based resolver TryFindReachableActivityLocation and logs the
// gaps as warnings at startup, instead of leaving them to surface as
// silent task failures.
//
// Homes require Stove, Cleaning, Firewood and Dining. Businesses
// require a Counter (service point) and a Receiving point. Interior
// types must be reachable from main-entrance; Firewood is an
// exterior pile, so presence is the whole requirement there (the
// interior resolver does not apply to it — same distinction as the
// fuel-handling leg-0 comment in SimulationDrivers).
// A missing point and an authored-but-unreachable point are distinct
// gaps: the resolver's reason is carried so the next reader knows
// which one it is.
using System.Collections.Generic;

namespace LandLedgers.World
{
    public static class StartupAccessValidator
    {
        /// <summary>One required activity point for one building.</summary>
        public sealed class ActivityAccessCheck
        {
            /// <summary>Activity-type label, e.g. "Stove", "Counter", "Receiving".</summary>
            public string TypeLabel = string.Empty;
            /// <summary>Resolved activity location id when one was found; null when none is authored.</summary>
            public string LocationId;
            /// <summary>True when a point of this type is authored on the building.</summary>
            public bool Found;
            /// <summary>True when the point is reachable from main-entrance. Ignored when RequiresInteriorRoute is false.</summary>
            public bool ReachableFromMainEntrance;
            /// <summary>True for interior points (route required); false for exterior points such as the firewood pile (presence suffices).</summary>
            public bool RequiresInteriorRoute = true;
            /// <summary>Resolver reason when the point is missing or unreachable; empty otherwise.</summary>
            public string Reason = string.Empty;
        }

        /// <summary>
        /// Appends one gap line per missing or unreachable required point.
        /// Returns the gaps (same list, for convenience). Empty when the
        /// building is clean.
        /// </summary>
        public static List<string> AppendAccessGaps(
            string buildingLabel, IReadOnlyList<ActivityAccessCheck> checks, List<string> gaps)
        {
            if (gaps == null) gaps = new List<string>();
            if (checks == null) return gaps;
            foreach (ActivityAccessCheck check in checks)
            {
                if (check == null) continue;
                string typeLabel = string.IsNullOrWhiteSpace(check.TypeLabel) ? "activity" : check.TypeLabel;
                if (!check.Found)
                {
                    gaps.Add($"{buildingLabel} has no {typeLabel} activity point authored" +
                        (string.IsNullOrWhiteSpace(check.Reason) ? "." : $": {check.Reason}"));
                    continue;
                }
                if (check.RequiresInteriorRoute && !check.ReachableFromMainEntrance)
                {
                    gaps.Add($"{buildingLabel} {typeLabel} point '{check.LocationId}' is not reachable from main-entrance" +
                        (string.IsNullOrWhiteSpace(check.Reason) ? "." : $": {check.Reason}"));
                }
            }
            return gaps;
        }
    }
}
