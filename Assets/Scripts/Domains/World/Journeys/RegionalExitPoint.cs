// Slice S7 (2026-11-05, Objective 24): the regional exit-point interface —
// a typed, harness-testable answer to "where does this haul leave the map".
// One record plus one pure journey-side resolver. The record names, for one
// (origin, destination-site) pair: the gateway location, the gateway leg and
// the beyond-gateway leg (miles, minutes, days each), the full route (the S4
// cross-check), the destination label, the gateway bearing from the origin
// (east/north miles, for grid-side selection in Objective 25), the round-trip
// day offset the off-map authority will use, and an optional grid-exit cell
// that is UNSET until the grid side fills it (Objective 25). The resolver
// invents nothing: every number comes from authored edges, authored
// coordinates, or the existing MinutesForMiles / TransitDaysForMinutes
// conversions. It never touches the grid, never logs (callers log), and
// persists nothing — the record is re-derived from the live JourneyModel.
// Note-7: per-leg minutes (742 + 12 = 754) and whole-route minutes (755)
// differ by 1 minute of per-leg rounding; both are carried, neither is
// "corrected". Note-8: the record carries both RouteMinutes (755, same-day
// state transit) and DueReturnDayOffset (2, journey round trip) and
// reconciles neither — Objectives 26/28 own that mapping.
using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.World.Journeys
{
    /// <summary>
    /// Slice S7: one haul's exit from the map, journey side. All fields are
    /// set by <see cref="RegionalExitPointResolver"/>; none is invented at
    /// construction. <see cref="GridExitCoord"/> / <see cref="HasGridExit"/>
    /// stay at their defaults until a grid-owning caller (Objective 25)
    /// fills them — the journey resolver never invents a grid cell.
    /// </summary>
    [Serializable]
    public sealed class RegionalExitPoint
    {
        public string OriginLocationId = string.Empty;
        public string DestinationLocationId = string.Empty;
        /// <summary>Destination JourneyLocation.DisplayName (e.g. "Millbrook Grain Co.").</summary>
        public string DestinationLabel = string.Empty;

        public string GatewayLocationId = string.Empty;
        public float GatewayRouteMiles;
        public int GatewayRouteMinutes;
        public int GatewayTravelDays;

        /// <summary>Gateway → destination leg (miles, minutes, days).</summary>
        public float OffMapLegMiles;
        public int OffMapLegMinutes;
        public int OffMapLegDays;

        /// <summary>Full origin → destination route (the S4 cross-check).</summary>
        public float RouteMiles;
        public int RouteMinutes;
        public List<string> RouteLegLocationIds = new List<string>();

        /// <summary>Gateway coords minus origin coords — the bearing the grid side selects on.</summary>
        public float GatewayEastMiles;
        public float GatewayNorthMiles;

        /// <summary>Grid-exit cell; default until Objective 25 fills it.</summary>
        public GridCoord GridExitCoord;
        public bool HasGridExit;

        /// <summary>
        /// The off-map authority's round-trip formula
        /// (OffMapJourneyAuthority: dueReturn = dayIndex + 2 * (gatewayDays + legDays)),
        /// exposed so Objective 26 does not recompute it (and cannot compute it differently).
        /// </summary>
        public int DueReturnDayOffset => 2 * (GatewayTravelDays + OffMapLegDays);
    }

    /// <summary>
    /// Slice S7: pure static resolver for <see cref="RegionalExitPoint"/>.
    /// Gateway selection follows the OffMapJourneyAuthority.FindNearestGateway
    /// scan shape (route each OffMapGateway candidate, keep the shortest)
    /// with the criterion the exit point needs: the shortest TOTAL haul
    /// (origin -> gateway -> destination), so the gateway is per-destination
    /// — the authority's origin-nearest rule would send a Redfield haul out
    /// through the Ashford gateway and back through town. Refusals name the
    /// missing input, the unknown id, or carry the route diagnostic; passage
    /// is never invented.
    /// </summary>
    public static class RegionalExitPointResolver
    {
        public static bool TryResolve(
            JourneyModel journeys,
            string originLocationId,
            string destinationLocationId,
            out RegionalExitPoint exitPoint,
            out string reason)
        {
            exitPoint = null;
            reason = string.Empty;

            if (journeys == null)
            {
                reason = "RegionalExitPointResolver: no journey model supplied for exit-point resolution.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(originLocationId))
            {
                reason = "RegionalExitPointResolver: no origin location id supplied for exit-point resolution.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(destinationLocationId))
            {
                reason = "RegionalExitPointResolver: no destination location id supplied for exit-point resolution.";
                return false;
            }

            JourneyLocation origin = journeys.GetLocation(originLocationId);
            if (origin == null)
            {
                reason = $"RegionalExitPointResolver: unknown origin location '{originLocationId}'.";
                return false;
            }
            JourneyLocation destination = journeys.GetLocation(destinationLocationId);
            if (destination == null)
            {
                reason = $"RegionalExitPointResolver: unknown destination location '{destinationLocationId}'.";
                return false;
            }

            // Gateway on the shortest origin -> destination path: for each
            // gateway candidate, route origin -> gateway and gateway ->
            // destination and keep the shortest TOTAL haul. This is the
            // authority's scan shape (OffMapJourneyAuthority.FindNearestGateway
            // routes each gateway and keeps the shortest) with the criterion
            // the exit point needs: per-destination, not origin-nearest.
            //
            // DEVIATION from plan 07 §4.1, recorded, not silent: §4.1 as
            // written keeps the shortest origin -> gateway leg (the
            // authority's exact rule). That picks "ashford-gateway" for a
            // Redfield haul (49.5 mi from town vs 99.5 mi) even though the
            // haul then backtracks through town-center (beyond leg ~150 mi)
            // — while §4.3 test 4 pins "redfield-gateway" for the same haul
            // ("nearest-gateway is per-destination, not a hardcoded
            // Ashford"). The two cannot both hold: the authority's rule is
            // origin-nearest because Depart only routes origin -> gateway
            // (the beyond leg is caller-supplied); the exit resolver routes
            // THROUGH the gateway to the destination, so its gateway must be
            // the one on the shortest full path. For the production Millbrook
            // haul both rules agree on "ashford-gateway". The authority
            // itself is unchanged (Objective 26 must note the two selections
            // agree for Millbrook and diverge for a hypothetical Redfield
            // freight departure).
            JourneyLocation gateway = null;
            JourneyRoute gatewayRoute = null;
            JourneyRoute beyondRoute = null;
            float bestTotalMiles = float.PositiveInfinity;
            foreach (var location in journeys.AllLocations())
            {
                if (location == null || location.Kind != JourneyLocationKind.OffMapGateway) continue;
                JourneyRoute candidateGateway = journeys.FindRoute(originLocationId, location.LocationId, TravelMode.Wagon);
                if (candidateGateway == null || !candidateGateway.Found) continue;
                JourneyRoute candidateBeyond = journeys.FindRoute(location.LocationId, destinationLocationId, TravelMode.Wagon);
                if (candidateBeyond == null || !candidateBeyond.Found) continue;
                float totalMiles = candidateGateway.TotalMiles + candidateBeyond.TotalMiles;
                if (totalMiles < bestTotalMiles)
                {
                    bestTotalMiles = totalMiles;
                    gateway = location;
                    gatewayRoute = candidateGateway;
                    beyondRoute = candidateBeyond;
                }
            }
            if (gateway == null)
            {
                reason = $"RegionalExitPointResolver: no off-map gateway reachable from '{originLocationId}'.";
                return false;
            }

            JourneyRoute fullRoute = journeys.FindRoute(originLocationId, destinationLocationId, TravelMode.Wagon);
            if (fullRoute == null || !fullRoute.Found)
            {
                reason = "RegionalExitPointResolver: " + (fullRoute != null ? fullRoute.Diagnostic : "no route to the destination.");
                return false;
            }

            exitPoint = new RegionalExitPoint
            {
                OriginLocationId = originLocationId,
                DestinationLocationId = destinationLocationId,
                DestinationLabel = destination.DisplayName ?? string.Empty,
                GatewayLocationId = gateway.LocationId,
                GatewayRouteMiles = gatewayRoute.TotalMiles,
                GatewayRouteMinutes = gatewayRoute.TotalMinutes,
                GatewayTravelDays = JourneyTravel.TransitDaysForMinutes(gatewayRoute.TotalMinutes),
                OffMapLegMiles = beyondRoute.TotalMiles,
                OffMapLegMinutes = beyondRoute.TotalMinutes,
                OffMapLegDays = JourneyTravel.TransitDaysForMinutes(beyondRoute.TotalMinutes),
                RouteMiles = fullRoute.TotalMiles,
                RouteMinutes = fullRoute.TotalMinutes,
                RouteLegLocationIds = new List<string>(fullRoute.LegLocationIds),
                GatewayEastMiles = gateway.XMiles - origin.XMiles,
                GatewayNorthMiles = gateway.ZMiles - origin.ZMiles,
                GridExitCoord = default(GridCoord),
                HasGridExit = false,
            };
            return true;
        }
    }
}
