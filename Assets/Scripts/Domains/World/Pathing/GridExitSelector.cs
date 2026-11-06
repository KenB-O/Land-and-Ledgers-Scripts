// Slice S8 (2026-11-05, Objective 25 grid-exit half): bearing-aware
// selection of the map-edge road cell a haul leaves through.
//
// Pure function of (grid, staging, bearing): the S7 exit point supplies the
// gateway bearing (GatewayEastMiles / GatewayNorthMiles, journey miles), and
// this selector maps it onto grid edges — east to +x, north to +z, grounded
// in TownGrid.GridDirection (North +z, East +x) and CoordToWorldCenter
// (grid +x/+z to world +x/+z). That axis mapping is an assumption, not a
// verified scene fact (Note-9): the union-of-two-edges shape bounds the
// risk, and PlayMode must confirm the selected cell sits on Millbrook Road
// toward the NE edge (Verify-4).
//
// The selector never touches TownWorldController, PathingManager, or the
// journey model, and it never falls back to the bearing-blind nearest edge
// road: when no road cell exists on the bearing-side edges it refuses with
// a reason naming the bearing side and the staging cell.
using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Pathing
{
    public static class GridExitSelector
    {
        // A zero bearing is no bearing; a tolerance, not an exact float
        // equality, is the correct comparison.
        private const float BearingTolerance = 0.001f;

        public static bool TrySelectEdgeRoad(
            TownGrid grid,
            GridCoord staging,
            float gatewayEastMiles,
            float gatewayNorthMiles,
            out GridCoord exitCoord,
            out string reason)
        {
            exitCoord = default;
            reason = string.Empty;

            if (grid == null)
            {
                reason = "GridExitSelector: no town grid supplied — cannot select an exit road.";
                return false;
            }

            if (!grid.IsInBounds(staging))
            {
                reason = $"GridExitSelector: staging cell ({staging.x}, {staging.z}) is outside the town grid.";
                return false;
            }

            bool hasEast = Mathf.Abs(gatewayEastMiles) > BearingTolerance;
            bool hasNorth = Mathf.Abs(gatewayNorthMiles) > BearingTolerance;
            if (!hasEast && !hasNorth)
            {
                reason = "GridExitSelector: no gateway bearing supplied — east and north miles are both zero, so no map edge can be selected.";
                return false;
            }

            string bearingSide = BearingSideName(gatewayEastMiles, gatewayNorthMiles, hasEast, hasNorth);

            // Bearing-side edge cells only, taking the union when both
            // components are nonzero (NE → east edge ∪ north edge; due east
            // → east edge only). One scan, so a corner cell shared by two
            // bearing edges is collected exactly once.
            var candidates = new List<GridCoord>();
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    bool onBearingEdge =
                        (gatewayEastMiles > BearingTolerance && x == grid.Width - 1)
                        || (gatewayEastMiles < -BearingTolerance && x == 0)
                        || (gatewayNorthMiles > BearingTolerance && z == grid.Depth - 1)
                        || (gatewayNorthMiles < -BearingTolerance && z == 0);
                    if (!onBearingEdge)
                    {
                        continue;
                    }

                    GridCoord coord = new GridCoord(x, z);
                    if (grid.GetCell(coord).IsRoad)
                    {
                        candidates.Add(coord);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                reason = $"GridExitSelector: no road cell on the {bearingSide} map edge for staging cell ({staging.x}, {staging.z}) — the live town has no road leaving toward the {bearingSide}.";
                return false;
            }

            // The exact CollectEdgeRoadCandidates comparator
            // (LogisticsRoutePlanner.cs:224-232): Manhattan distance to
            // staging, tie-broken by x then z, so selection is deterministic
            // and consistent with the planner's own ordering.
            candidates.Sort((a, b) =>
            {
                int distanceComparison = ManhattanDistance(a, staging).CompareTo(ManhattanDistance(b, staging));
                if (distanceComparison != 0)
                {
                    return distanceComparison;
                }

                int xComparison = a.x.CompareTo(b.x);
                return xComparison != 0 ? xComparison : a.z.CompareTo(b.z);
            });

            exitCoord = candidates[0];
            return true;
        }

        private static string BearingSideName(float eastMiles, float northMiles, bool hasEast, bool hasNorth)
        {
            if (hasEast && hasNorth)
            {
                if (eastMiles > 0f && northMiles > 0f) return "northeast";
                if (eastMiles < 0f && northMiles > 0f) return "northwest";
                if (eastMiles > 0f && northMiles < 0f) return "southeast";
                return "southwest";
            }

            if (hasEast) return eastMiles > 0f ? "due east" : "due west";
            return northMiles > 0f ? "due north" : "due south";
        }

        private static int ManhattanDistance(GridCoord a, GridCoord b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.z - b.z);
        }
    }
}
