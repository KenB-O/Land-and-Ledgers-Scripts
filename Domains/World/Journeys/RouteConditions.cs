using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Integration;
using UnityEngine;

namespace LandLedgers.World.Journeys
{
    /// <summary>
    /// NX-2C: a provider that conditions journey edges — weather, road surface,
    /// river state, ferry availability. The JourneyModel consults it during
    /// routing; closed edges are routed around, never teleported through.
    /// </summary>
    public interface IJourneyConditionProvider
    {
        /// <summary>
        /// Returns true when a condition is known for this edge (either direction).
        /// timeMultiplier scales travel time (1.0 = normal); a non-empty
        /// closureReason means the edge is CLOSED (route around it).
        /// </summary>
        bool TryGetEdgeCondition(
            string fromLocationId, string toLocationId, TravelMode mode,
            out float timeMultiplier, out string closureReason);
    }

    /// <summary>NX-2C: per-edge physical attributes the weather acts on.</summary>
    [Serializable]
    public sealed class JourneyEdgeAttributes
    {
        public string FromLocationId = string.Empty;
        public string ToLocationId = string.Empty;
        public bool Unpaved = true;        // frontier roads are dirt unless stated
        public bool HasRiverCrossing;      // ford — vulnerable to water state
        public bool IsFerry;               // ferry — vulnerable to ice/out-of-service

        public JourneyEdgeAttributes() { }
    }

    /// <summary>NX-2C: one day's weather (seeded; all values calibration per Canon Part XV).</summary>
    [Serializable]
    public sealed class WeatherState
    {
        public int DayIndex;
        public FarmSeason Season;
        public float TempF;
        public float Precipitation01; // 0 = dry … 1 = storm
        public bool IsSnow;           // precipitation falls as snow
        public float WindMph;
        public float Dryness01;       // consecutive-dry buildup — feeds NX-2B fire weather
        public float SnowCover01;     // accumulated snow on the ground

        public WeatherState() { }
    }

    /// <summary>NX-2C: save data for the route condition service.</summary>
    [Serializable]
    public sealed class RouteConditionSaveDto
    {
        public List<JourneyEdgeAttributes> edgeAttributes = new List<JourneyEdgeAttributes>();
        public WeatherState current = new WeatherState();
        public int seed;
    }

    /// <summary>
    /// NX-2C: weather and route conditions over the journey model.
    /// Canon 4.4: "Road surface, mud, snow, river ice/high/low water, ferry
    /// availability and weather can alter travel time/capacity or close a route.
    /// ... The architecture must allow route availability and capacity to change
    /// without inventing or deleting cargo."
    ///
    /// The service generates seeded daily weather, derives per-edge conditions
    /// (mud, snow, high water, ice, ferry outages, blizzard closures), and
    /// serves them to JourneyModel.FindRoute through IJourneyConditionProvider.
    /// Freight planning, perishable aging, and delivery promises all read the
    /// conditioned times — one source of truth. All thresholds and multipliers
    /// are calibration (Canon Part XV); the canon fixes that conditions MATTER.
    ///
    /// Historical basis (researched): Dakota blizzards closed roads for days;
    /// spring thaw turned dirt roads to impassable mud ("breakup"); rivers were
    /// forded at low water, ferried otherwise, and frozen rivers served as winter
    /// roads — the model follows that physical logic.
    /// </summary>
    public sealed class RouteConditionService : IJourneyConditionProvider
    {
        // Calibration constants (Canon Part XV).
        public const float MudWagonMultiplier = 1.6f;
        public const float MudOtherMultiplier = 1.3f;
        public const float SnowCoverMultiplier = 1.8f;
        public const float FerryHighWaterMultiplier = 1.5f;
        public const float BlizzardWindMph = 25f;
        public const float BlizzardPrecip01 = 0.7f;
        public const float IceTempF = 25f;

        private readonly Dictionary<string, JourneyEdgeAttributes> attributes =
            new Dictionary<string, JourneyEdgeAttributes>(StringComparer.OrdinalIgnoreCase);
        private readonly WeatherState current = new WeatherState();
        private int seed;

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public WeatherState Current => current;

        private static string EdgeKey(string a, string b)
        {
            // Direction-agnostic: the same physical road both ways.
            int cmp = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return cmp <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }

        /// <summary>Declares a road's physical attributes (unpaved, ford, ferry).</summary>
        public void SetEdgeAttributes(JourneyEdgeAttributes attr, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (attr == null || string.IsNullOrWhiteSpace(attr.FromLocationId) || string.IsNullOrWhiteSpace(attr.ToLocationId))
            {
                diag.Add("RouteConditionService.SetEdgeAttributes: edge endpoints are required.");
                return;
            }
            attributes[EdgeKey(attr.FromLocationId, attr.ToLocationId)] = attr;
        }

        private bool TryGetAttributes(string fromLocationId, string toLocationId, out JourneyEdgeAttributes attr)
        {
            return attributes.TryGetValue(EdgeKey(fromLocationId, toLocationId), out attr);
        }

        /// <summary>
        /// Advances the weather one day (seeded). Returns the day's weather.
        /// Dryness and snow cover accumulate honestly across days.
        /// </summary>
        public WeatherState AdvanceDay(int dayIndex, int seed, List<string> diag)
        {
            diag = diag ?? diagnostics;
            this.seed = seed;
            var rng = new System.Random(seed * 1013904223 + dayIndex * 7919);

            FarmSeason season = FarmSeasons.SeasonForDayIndex(dayIndex);
            float baseTemp = season switch
            {
                FarmSeason.Spring => 50f,
                FarmSeason.Summer => 78f,
                FarmSeason.Fall => 52f,
                FarmSeason.Winter => 18f,
                _ => 55f,
            };
            float precipP = season switch
            {
                FarmSeason.Spring => 0.30f,
                FarmSeason.Summer => 0.25f,
                FarmSeason.Fall => 0.20f,
                FarmSeason.Winter => 0.25f,
                _ => 0.25f,
            };

            current.DayIndex = dayIndex;
            current.Season = season;
            current.TempF = baseTemp + (float)(rng.NextDouble() * 20.0 - 10.0);
            current.Precipitation01 = rng.NextDouble() < precipP ? (float)rng.NextDouble() : 0f;
            current.IsSnow = season == FarmSeason.Winter && current.TempF < 32f;
            current.WindMph = 5f + (float)rng.NextDouble() * 20f + (current.Precipitation01 > 0.6f ? 10f : 0f);

            // Dryness builds on dry days, breaks on real rain.
            if (current.Precipitation01 >= 0.4f && !current.IsSnow)
                current.Dryness01 = 0f;
            else
                current.Dryness01 = Mathf.Clamp01(current.Dryness01 + 0.05f);

            // Snow cover accumulates in winter, melts above freezing.
            if (current.IsSnow && current.Precipitation01 > 0.2f)
                current.SnowCover01 = Mathf.Clamp01(current.SnowCover01 + current.Precipitation01 * 0.4f);
            else if (current.TempF > 36f)
                current.SnowCover01 = Mathf.Clamp01(current.SnowCover01 - 0.25f);

            if (IsBlizzard())
                diag.Add($"RouteConditionService: BLIZZARD day {dayIndex} — roads closed (historical Dakota hazard).");
            return current;
        }

        public bool IsBlizzard()
        {
            return current.IsSnow && current.Precipitation01 >= BlizzardPrecip01 && current.WindMph >= BlizzardWindMph;
        }

        /// <summary>Fire weather for the NX-2B prairie-fire model (wind + dryness).</summary>
        public (float windMph, float dryness01) GetFireWeather()
        {
            return (current.WindMph, current.Dryness01);
        }

        public bool TryGetEdgeCondition(
            string fromLocationId, string toLocationId, TravelMode mode,
            out float timeMultiplier, out string closureReason)
        {
            timeMultiplier = 1f;
            closureReason = string.Empty;

            if (IsBlizzard())
            {
                closureReason = "blizzard — road closed";
                return true;
            }

            if (!TryGetAttributes(fromLocationId, toLocationId, out JourneyEdgeAttributes attr))
                return false; // no declared attributes: unconditioned

            float mult = 1f;
            var notes = new List<string>();

            // Snow cover slows everything on the ground.
            if (current.SnowCover01 > 0.3f)
            {
                mult *= 1f + (SnowCoverMultiplier - 1f) * current.SnowCover01;
                notes.Add("snow");
            }

            // Mud: rain on dirt, or spring thaw.
            bool thaw = current.Season == FarmSeason.Spring && current.TempF > 40f;
            bool muddyRain = current.Precipitation01 >= 0.4f && !current.IsSnow;
            if (attr.Unpaved && (muddyRain || thaw))
            {
                mult *= mode == TravelMode.Wagon ? MudWagonMultiplier : MudOtherMultiplier;
                notes.Add(muddyRain ? "mud" : "spring thaw mud");
            }

            // River states.
            bool highWater = (!current.IsSnow && current.Precipitation01 >= 0.7f) || thaw;
            bool frozen = current.Season == FarmSeason.Winter && current.TempF <= IceTempF && current.SnowCover01 > 0.2f;
            bool lowWater = current.Season == FarmSeason.Summer && current.Dryness01 > 0.7f;

            if (attr.IsFerry)
            {
                if (frozen)
                {
                    closureReason = "river frozen — ferry not running (ice road may serve)";
                    return true;
                }
                if (highWater)
                {
                    mult *= FerryHighWaterMultiplier;
                    notes.Add("high water — slow ferry");
                }
            }
            else if (attr.HasRiverCrossing)
            {
                if (highWater)
                {
                    closureReason = "high water — ford impassable";
                    return true;
                }
                if (frozen)
                {
                    notes.Add("ice road"); // frozen rivers served as winter roads (historical)
                }
                else if (lowWater)
                {
                    mult *= 0.9f;
                    notes.Add("low water — easy ford");
                }
            }

            if (notes.Count == 0) return false;
            timeMultiplier = mult;
            return true;
        }

        public RouteConditionSaveDto CaptureSaveDto()
        {
            return new RouteConditionSaveDto
            {
                edgeAttributes = new List<JourneyEdgeAttributes>(attributes.Values),
                current = current,
                seed = seed,
            };
        }

        public void LoadFromSaveDto(RouteConditionSaveDto dto)
        {
            attributes.Clear();
            if (dto == null) return;
            if (dto.edgeAttributes != null)
                foreach (JourneyEdgeAttributes attr in dto.edgeAttributes)
                    if (attr != null)
                        attributes[EdgeKey(attr.FromLocationId, attr.ToLocationId)] = attr;
            if (dto.current != null)
            {
                current.DayIndex = dto.current.DayIndex;
                current.Season = dto.current.Season;
                current.TempF = dto.current.TempF;
                current.Precipitation01 = dto.current.Precipitation01;
                current.IsSnow = dto.current.IsSnow;
                current.WindMph = dto.current.WindMph;
                current.Dryness01 = dto.current.Dryness01;
                current.SnowCover01 = dto.current.SnowCover01;
            }
            seed = dto.seed;
        }
    }
}
