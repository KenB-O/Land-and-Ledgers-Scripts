using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// A2 plan 28 (2026-11-14, note 113 C6): the pure route mechanic for the
    /// log-haul driver. Ensures the landing-to-mill road exists in a journey
    /// model, idempotently: a direct edge is added only when no route exists
    /// at all (Q4 — the daily driver must never force the direct edge when
    /// the network already connects the two). No authoring lives here — the
    /// caller supplies ids, display names, miles, and the road name; miles
    /// ride on the stand (C4), so nothing is invented in this file.
    /// </summary>
    public static class LogHaulRoute
    {
        /// <summary>
        /// Ensures a road exists between the landing and the mill in
        /// <paramref name="journeys"/>. Returns null on success (including
        /// when a route already exists — nothing is re-registered and no
        /// edge is added, because AddEdge does not dedupe). Returns a loud
        /// problem string on failure and records it in <paramref name="diag"/>
        /// when a list is supplied; the caller decides whether to log it
        /// once-only or every time.
        /// </summary>
        public static string EnsureRoute(JourneyModel journeys, string originLocationId, string originDisplayName,
            string millLocationId, string millBusinessId, string millDisplayName, float miles, string roadName,
            List<string> diag)
        {
            string Problem(string problem)
            {
                if (diag != null) diag.Add(problem);
                return problem;
            }

            if (journeys == null)
                return Problem("LogHaulRoute: no journey model — the haul route cannot be ensured.");
            if (string.IsNullOrWhiteSpace(originLocationId))
                return Problem("LogHaulRoute: the landing needs a journey location id.");
            if (string.IsNullOrWhiteSpace(millLocationId))
                return Problem("LogHaulRoute: the mill needs a journey location id.");
            if (string.Equals(originLocationId, millLocationId, StringComparison.OrdinalIgnoreCase))
                return Problem("LogHaulRoute: the landing '" + originLocationId + "' is the mill location — a stand landing at the mill is a config error, not a haul.");
            if (journeys.FindRoute(originLocationId, millLocationId, TravelMode.Wagon).Found)
                return null; // A route already exists — register nothing (Q4).

            // Register each MISSING location only: a location that is
            // already registered is never re-registered (ids are never reused).
            if (journeys.GetLocation(originLocationId) == null)
            {
                string originRefusal = journeys.RegisterLocation(new JourneyLocation(
                    originLocationId, JourneyLocationKind.Waypoint, originDisplayName ?? string.Empty, 0f, 0f, false));
                if (originRefusal != null) return Problem(originRefusal);
            }
            if (journeys.GetLocation(millLocationId) == null)
            {
                var millLocation = new JourneyLocation(
                    millLocationId, JourneyLocationKind.Mill, millDisplayName ?? string.Empty, 0f, 0f, false)
                {
                    BusinessId = millBusinessId ?? string.Empty,
                };
                string millRefusal = journeys.RegisterLocation(millLocation);
                if (millRefusal != null) return Problem(millRefusal);
            }

            string edgeRefusal = journeys.AddEdge(originLocationId, millLocationId, miles, roadName);
            if (edgeRefusal != null) return Problem(edgeRefusal);
            return null;
        }
    }
}
