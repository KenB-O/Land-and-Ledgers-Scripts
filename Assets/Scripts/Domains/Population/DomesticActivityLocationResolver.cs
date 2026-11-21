// Lane 9c (M-R1, Agent M §7): the location-id decision for domestic
// movement, extracted from SimulationDrivers.ResolveDomesticActivityMovement
// so the harness can pin it. SimulationDrivers.cs is too Unity-bound for
// the Roslyn closure, so the decision lives here (pure static, no Unity
// types) and the driver calls it; the production wiring itself is pinned
// by a source-read test in CampaignCLane9cMovementAccessTests.
//
// Meal prep and fuel-handling leg 1 route to the stove registry's
// per-stove activity id (B-01/B-19: the binding already carries the
// correct id; the caller used the literal "stove", which no Git asset
// authors). The literal survives only as the fallback for a household
// whose registry has no stove, so the Main Scene keeps its current
// behaviour there instead of failing in a new way.
//
// Fuel-handling leg 0 routes to the authored Firewood point when one
// exists (fix #1, A4); with none authored anywhere today it falls back
// to the stove as a single leg with a logged reason, so the task can
// proceed instead of parking on capability-wait forever.
//
// Chores resolve by activity type through the existing type-based
// resolver TryFindReachableActivityLocation (B-02/B-04/M-02), Cleaning
// first, then Porch. The legacy literal ids ("cleaning" /
// "porch-dropoff", by day parity) survive only as the fallback, so a
// Main Scene prefab that authors those literal ids keeps working.
// The resolver itself is NOT rewritten here; this helper only decides
// which id the driver asks the director to route to.
using System;

namespace LandLedgers.Population
{
    public static class DomesticActivityLocationResolver
    {
        /// <summary>Activity-type names the type resolver is asked for, in order, for chores.</summary>
        public const string CleaningTypeName = "Cleaning";
        public const string PorchTypeName = "Porch";

        /// <summary>Legacy literal id used when no stove registry id is known.</summary>
        public const string LegacyStoveLocationId = "stove";

        /// <summary>
        /// Meal-prep / fuel-handling leg 1 destination: the household stove's
        /// registry activity id when one is known, else the legacy literal.
        /// </summary>
        public static string ResolveStoveLocationId(string stoveActivityLocationId)
        {
            return string.IsNullOrWhiteSpace(stoveActivityLocationId)
                ? LegacyStoveLocationId
                : stoveActivityLocationId;
        }

        /// <summary>
        /// Fuel-handling leg 0 destination (fix #1, A4 fallback): the
        /// authored Firewood activity location id when one exists, else the
        /// stove id as a single-leg fallback with a reason. No Firewood
        /// point is authored anywhere today, so without the fallback every
        /// home's domestic.fuel-handling task parks forever on a
        /// capability-wait refusal. The fallback routes the actor straight
        /// to the stove (leg 1 resolves to the same id and the director
        /// reports Arrived immediately when already there), so fuel work
        /// can proceed without inventing coordinates and without aliasing
        /// the porch point. Authoring real Firewood points in the
        /// owner-local prefabs remains the Codex morning item; until then
        /// the caller logs the fallback reason day-keyed.
        /// Returns the location id to route to; fallbackReason is null
        /// when the authored Firewood id was used.
        /// </summary>
        public static string ResolveFuelLeg0LocationId(Func<string> tryResolveFirewoodLocationId, string stoveLocationId, out string fallbackReason)
        {
            fallbackReason = null;
            if (tryResolveFirewoodLocationId != null)
            {
                string firewood = tryResolveFirewoodLocationId();
                if (!string.IsNullOrWhiteSpace(firewood)) return firewood;
            }
            fallbackReason = "no Firewood activity location is authored for this household; "
                + "fuel-handling leg 0 falls back to the stove as a single leg (A4). "
                + "Author Firewood points in the owner-local prefabs to restore the two-leg route.";
            return ResolveStoveLocationId(stoveLocationId);
        }

        /// <summary>
        /// Chores destination: the first reachable authored point of type
        /// Cleaning, then Porch, via the supplied type resolver (which
        /// returns the resolved activity location id, or null/empty when
        /// that type has no reachable authored point). When neither type
        /// resolves, the legacy literal ids by day parity, so a Main Scene
        /// prefab authoring those ids keeps working.
        /// </summary>
        public static string ResolveChoreLocationId(int dayIndex, Func<string, string> tryResolveByTypeName)
        {
            if (tryResolveByTypeName != null)
            {
                string cleaning = tryResolveByTypeName(CleaningTypeName);
                if (!string.IsNullOrWhiteSpace(cleaning)) return cleaning;
                string porch = tryResolveByTypeName(PorchTypeName);
                if (!string.IsNullOrWhiteSpace(porch)) return porch;
            }
            return (dayIndex & 1) == 0 ? "cleaning" : "porch-dropoff";
        }
    }
}
