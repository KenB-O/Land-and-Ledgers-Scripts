using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World.Journeys
{
    /// <summary>JRN-1: the kinds of places journeys route between.</summary>
    public enum JourneyLocationKind
    {
        Unspecified = 0,
        Farmstead = 1,      // a farm's yard core (FVS-1 FarmLayout handoff)
        TownBuilding = 2,    // in-town premises
        Mill = 3,
        Elevator = 4,       // grain elevator/warehouse
        Store = 5,          // general store / feed merchant
        ButcherShop = 6,
        Waypoint = 7,       // road junctions, river crossings
        OffMapGateway = 8,  // rail depot / trail head: off-map legs carry authored travel time (Canon §13.1)
    }

    /// <summary>JRN-1: how someone travels. Speeds are calibration (Canon Part XV).</summary>
    public enum TravelMode
    {
        Unspecified = 0,
        Foot = 1,       // 3 mph
        Horseback = 2,  // 7 mph
        Wagon = 3,      // 4 mph loaded (draft team)
    }

    /// <summary>
    /// JRN-1: one routable place. Positions are MILES on a 1:1 world scale
    /// (Canon §13.1 CANON LOCK: a represented mile is a real mile of
    /// economic/travel distance). Kennedy authors fixed points (town, farms);
    /// non-fixed points may take seeded jitter so layouts vary reproducibly.
    /// </summary>
    [Serializable]
    public sealed class JourneyLocation
    {
        public string LocationId = string.Empty; // stable, e.g. "farm-1-stead"
        public JourneyLocationKind Kind;
        public string DisplayName = string.Empty;
        public string FarmId = string.Empty;      // farmsteads: FVS-1 FarmDefinition.FarmId
        public string BusinessId = string.Empty;  // business nodes: business instance id
        public string FarmZoneId = string.Empty;  // FarmLayout zone link (the FVS-1 handoff)
        public float XMiles;  // miles east of the town center
        public float ZMiles;  // miles north of the town center
        public bool PositionAuthored; // true = Kennedy fixed it; false = seeded jitter OK

        public JourneyLocation() { }

        public JourneyLocation(string locationId, JourneyLocationKind kind, string displayName,
            float xMiles, float zMiles, bool positionAuthored = true)
        {
            LocationId = locationId ?? string.Empty;
            Kind = kind;
            DisplayName = displayName ?? string.Empty;
            XMiles = xMiles;
            ZMiles = zMiles;
            PositionAuthored = positionAuthored;
        }
    }

    /// <summary>JRN-1: one road leg between two locations, in real miles.</summary>
    [Serializable]
    public sealed class JourneyEdge
    {
        public string FromLocationId = string.Empty;
        public string ToLocationId = string.Empty;
        public float Miles;
        public string RoadName = string.Empty; // e.g. "river road"

        public JourneyEdge() { }

        public JourneyEdge(string from, string to, float miles, string roadName = "")
        {
            FromLocationId = from ?? string.Empty;
            ToLocationId = to ?? string.Empty;
            Miles = Math.Max(0f, miles);
            RoadName = roadName ?? string.Empty;
        }
    }

    /// <summary>JRN-1: a computed route — legs, miles, and whole minutes.</summary>
    [Serializable]
    public sealed class JourneyRoute
    {
        public List<string> LegLocationIds = new List<string>();
        public float TotalMiles;
        public int TotalMinutes; // TTS-1 minute quantum: whole minutes, floor of 1
        public TravelMode Mode;
        public bool Found;
        public string Diagnostic = string.Empty;
        /// <summary>NX-2C: set when a condition provider altered this route (weather/road/river note).</summary>
        public string ConditionNote = string.Empty;
    }

    /// <summary>
    /// JRN-1: the location &amp; journey model — the structural answer to "how
    /// long does it take to get from A to B". Locations are nodes, roads are
    /// edges in real miles (1:1 scale, Canon §13.1); travel time derives from
    /// mode speed with TTS minute math. The model owns the math; callers
    /// (deliveries, freight, work travel) just ask it.
    ///
    /// This is the foundation future work builds on: NPC pathing, the map UI,
    /// and farm-zone-level routing (FarmLayout zones plug in as FarmZoneId).
    /// Intra-farm zone travel stays abstract until the location model grows
    /// zone-level edges — documented, not faked.
    /// </summary>
    public sealed class JourneyModel
    {
        /// <summary>Calibration: travel speeds in mph (Canon Part XV).</summary>
        public const float FootMph = 3f;
        public const float HorsebackMph = 7f;
        public const float WagonMph = 4f;

        /// <summary>
        /// NX-2C: optional condition provider (weather, road surface, river).
        /// Null = unconditioned routing (previous behavior). Not serialized —
        /// the RouteConditionService owns its own save data.
        /// </summary>
        public IJourneyConditionProvider ConditionProvider { get; set; }

        private readonly Dictionary<string, JourneyLocation> locations =
            new Dictionary<string, JourneyLocation>(StringComparer.OrdinalIgnoreCase);
        private readonly List<JourneyEdge> edges = new List<JourneyEdge>();

        public static float SpeedMph(TravelMode mode)
        {
            switch (mode)
            {
                case TravelMode.Foot: return FootMph;
                case TravelMode.Horseback: return HorsebackMph;
                case TravelMode.Wagon: return WagonMph;
                default: return FootMph;
            }
        }

        /// <summary>Whole minutes for a leg: distance ÷ speed, TTS-1 quantum (floor of 1).</summary>
        public static int MinutesForMiles(float miles, TravelMode mode)
        {
            float mph = Math.Max(0.5f, SpeedMph(mode));
            return Math.Max(1, Mathf.RoundToInt(Math.Max(0f, miles) / mph * 60f));
        }

        public string RegisterLocation(JourneyLocation location)
        {
            if (location == null) return "JourneyModel: no location supplied.";
            if (string.IsNullOrWhiteSpace(location.LocationId))
            {
                return "JourneyModel: locations need a stable LocationId.";
            }
            if (locations.ContainsKey(location.LocationId))
            {
                return $"JourneyModel: location '{location.LocationId}' already registered — ids are never reused.";
            }
            locations[location.LocationId] = location;
            return null;
        }

        public JourneyLocation GetLocation(string locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId)) return null;
            JourneyLocation location;
            return locations.TryGetValue(locationId, out location) ? location : null;
        }

        public IEnumerable<JourneyLocation> AllLocations() => locations.Values;

        /// <summary>Adds a bidirectional road leg in real miles.</summary>
        public string AddEdge(string fromLocationId, string toLocationId, float miles, string roadName = "")
        {
            if (GetLocation(fromLocationId) == null)
            {
                return $"JourneyModel: unknown location '{fromLocationId}'.";
            }
            if (GetLocation(toLocationId) == null)
            {
                return $"JourneyModel: unknown location '{toLocationId}'.";
            }
            if (miles < 0f)
            {
                return "JourneyModel: road miles cannot be negative.";
            }
            edges.Add(new JourneyEdge(fromLocationId, toLocationId, miles, roadName));
            return null;
        }

        /// <summary>
        /// Shortest route by miles (Dijkstra). Returns Found=false with a
        /// diagnostic when the destination is unreachable — callers must handle
        /// that honestly rather than assuming zero travel.
        /// </summary>
        public JourneyRoute FindRoute(string fromLocationId, string toLocationId, TravelMode mode)
        {
            var route = new JourneyRoute { Mode = mode };
            if (GetLocation(fromLocationId) == null)
            {
                route.Diagnostic = $"JourneyModel: unknown origin '{fromLocationId}'.";
                return route;
            }
            if (GetLocation(toLocationId) == null)
            {
                route.Diagnostic = $"JourneyModel: unknown destination '{toLocationId}'.";
                return route;
            }
            if (string.Equals(fromLocationId, toLocationId, StringComparison.OrdinalIgnoreCase))
            {
                route.Found = true;
                route.LegLocationIds.Add(fromLocationId);
                route.TotalMiles = 0f;
                route.TotalMinutes = 0;
                return route;
            }

            // Dijkstra over the edge list. Cost is conditioned miles when a
            // condition provider is set (NX-2C): closed edges are skipped so
            // routes go AROUND closures — never through them.
            var dist = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            var prev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in locations.Keys) dist[id] = float.PositiveInfinity;
            dist[fromLocationId] = 0f;
            bool conditioned = false;

            while (true)
            {
                string current = null;
                float best = float.PositiveInfinity;
                foreach (var kvp in dist)
                {
                    if (!visited.Contains(kvp.Key) && kvp.Value < best)
                    {
                        best = kvp.Value;
                        current = kvp.Key;
                    }
                }
                if (current == null) break; // unreachable remainder
                if (string.Equals(current, toLocationId, StringComparison.OrdinalIgnoreCase)) break;
                visited.Add(current);

                foreach (var edge in edges)
                {
                    string neighbor = null;
                    if (string.Equals(edge.FromLocationId, current, StringComparison.OrdinalIgnoreCase))
                    {
                        neighbor = edge.ToLocationId;
                    }
                    else if (string.Equals(edge.ToLocationId, current, StringComparison.OrdinalIgnoreCase))
                    {
                        neighbor = edge.FromLocationId;
                    }
                    if (neighbor == null || visited.Contains(neighbor)) continue;

                    float edgeCost = edge.Miles;
                    if (ConditionProvider != null &&
                        ConditionProvider.TryGetEdgeCondition(edge.FromLocationId, edge.ToLocationId, mode,
                            out float multiplier, out string closureReason))
                    {
                        if (!string.IsNullOrEmpty(closureReason))
                            continue; // closed — route around, never invent passage
                        edgeCost = edge.Miles * Mathf.Max(0.1f, multiplier);
                        conditioned = true;
                    }

                    float alt = dist[current] + edgeCost;
                    if (alt < dist[neighbor])
                    {
                        dist[neighbor] = alt;
                        prev[neighbor] = current;
                    }
                }
            }

            if (float.IsPositiveInfinity(dist[toLocationId]))
            {
                route.Diagnostic = ConditionProvider != null
                    ? $"JourneyModel: no open route connects '{fromLocationId}' to '{toLocationId}' — closures are routed around, never ignored."
                    : $"JourneyModel: no road connects '{fromLocationId}' to '{toLocationId}' — travel is not assumed free.";
                return route;
            }

            // Reconstruct path.
            var path = new List<string>();
            string step = toLocationId;
            while (step != null)
            {
                path.Add(step);
                prev.TryGetValue(step, out step);
            }
            path.Reverse();

            // Real miles along the path (conditions change time, not distance).
            float realMiles = 0f;
            for (int i = 0; i + 1 < path.Count; i++)
                realMiles += EdgeMiles(path[i], path[i + 1]);

            route.Found = true;
            route.LegLocationIds = path;
            route.TotalMiles = realMiles;
            route.TotalMinutes = MinutesForMiles(dist[toLocationId], mode);
            if (conditioned)
                route.ConditionNote = "Route conditioned by weather/road/river state (Canon 4.4).";
            return route;
        }

        private float EdgeMiles(string fromLocationId, string toLocationId)
        {
            foreach (var edge in edges)
            {
                if ((string.Equals(edge.FromLocationId, fromLocationId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(edge.ToLocationId, toLocationId, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(edge.FromLocationId, toLocationId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(edge.ToLocationId, fromLocationId, StringComparison.OrdinalIgnoreCase)))
                {
                    return edge.Miles;
                }
            }
            return 0f;
        }

        /// <summary>Straight-line miles between two locations (for authored estimates).</summary>
        public float CrowFliesMiles(string fromLocationId, string toLocationId)
        {
            JourneyLocation a = GetLocation(fromLocationId);
            JourneyLocation b = GetLocation(toLocationId);
            if (a == null || b == null) return -1f;
            float dx = a.XMiles - b.XMiles;
            float dz = a.ZMiles - b.ZMiles;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public JourneyModelSaveDto CaptureSaveDto()
        {
            return new JourneyModelSaveDto
            {
                locations = new List<JourneyLocation>(locations.Values),
                edges = new List<JourneyEdge>(edges),
            };
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public void LoadFromSaveDto(JourneyModelSaveDto dto)
        {
            locations.Clear();
            edges.Clear();
            if (dto == null) return;
            if (dto.locations != null)
            {
                foreach (var location in dto.locations)
                {
                    if (location != null && !string.IsNullOrWhiteSpace(location.LocationId))
                    {
                        locations[location.LocationId] = location;
                    }
                }
            }
            if (dto.edges != null) edges.AddRange(dto.edges);
        }
    }

    /// <summary>
    /// JRN-1: world layout builder. Kennedy authors fixed points (town center,
    /// farmsteads, mill sites); non-fixed points take seeded jitter so
    /// scenarios vary reproducibly. The seed is stored — layouts reproduce.
    /// </summary>
    public static class WorldLayoutBuilder
    {
        /// <summary>Calibration: max jitter applied to non-authored positions, in miles.</summary>
        public const float JitterMiles = 0.5f;

        /// <summary>
        /// Builds a journey model from authored locations. Non-authored
        /// positions are jittered by the seeded RNG; authored positions are
        /// used exactly as given.
        /// </summary>
        public static JourneyModel Build(
            List<JourneyLocation> authoredLocations,
            List<JourneyEdge> authoredEdges,
            int seed,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            var model = new JourneyModel();
            var rng = new System.Random(seed);

            if (authoredLocations != null)
            {
                foreach (var location in authoredLocations)
                {
                    if (location == null) continue;
                    if (!location.PositionAuthored)
                    {
                        location.XMiles += (float)(rng.NextDouble() * 2 - 1) * JitterMiles;
                        location.ZMiles += (float)(rng.NextDouble() * 2 - 1) * JitterMiles;
                        diagnostics.Add($"WorldLayoutBuilder: jittered '{location.LocationId}' by seeded layout (seed {seed}).");
                    }
                    string problem = model.RegisterLocation(location);
                    if (problem != null) diagnostics.Add(problem);
                }
            }

            if (authoredEdges != null)
            {
                foreach (var edge in authoredEdges)
                {
                    if (edge == null) continue;
                    string problem = model.AddEdge(edge.FromLocationId, edge.ToLocationId, edge.Miles, edge.RoadName);
                    if (problem != null) diagnostics.Add(problem);
                }
            }

            return model;
        }

        /// <summary>
        /// Convenience: registers a farmstead node from an FVS-1 farm definition
        /// id and its distance to town (the FarmLayout zones plug in later as
        /// FarmZoneId links when zone-level routing exists).
        /// </summary>
        public static JourneyLocation FarmsteadNode(string farmId, string displayName, float distanceToTownMiles)
        {
            // Calibration: place farmsteads north of town along the river road
            // bearing until Kennedy authors real coordinates.
            float miles = Math.Max(0f, distanceToTownMiles);
            return new JourneyLocation("farmstead-" + farmId, JourneyLocationKind.Farmstead,
                displayName, 0.3f * miles, miles, positionAuthored: distanceToTownMiles >= 0f)
            {
                FarmId = farmId ?? string.Empty,
            };
        }
    }

    /// <summary>Save DTO for the journey model (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class JourneyModelSaveDto
    {
        public List<JourneyLocation> locations = new List<JourneyLocation>();
        public List<JourneyEdge> edges = new List<JourneyEdge>();
    }
}
