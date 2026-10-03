using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Market
{
    /// <summary>
    /// T2E: settlement role (Canon Part I master doctrine — "Settlement role
    /// can outweigh size. Agricultural service town, railhead,
    /// county/administrative center, mining node, river center and industrial
    /// node generate different demand even at similar resident population.").
    /// </summary>
    public enum SettlementRole
    {
        Unspecified = 0,
        AgriculturalServiceTown = 1,
        Railhead = 2,
        CountyCenter = 3,
        MiningNode = 4,
        RiverCenter = 5,
        IndustrialNode = 6,
    }

    /// <summary>T2E: one rural unit — a farm or rural household with a location.</summary>
    [Serializable]
    public sealed class RuralUnit
    {
        public string UnitId = string.Empty;
        public string LocationId = string.Empty;
        public string HouseholdId = string.Empty;
        public bool IsFarm;
        public int ResidentCount;

        public RuralUnit() { }
    }

    /// <summary>
    /// T2E: the derived catchment of a settlement. Canon §1.4: "A settlement
    /// catchment is shaped by farms and households, roads and river crossings,
    /// travel time, wagon economics, railway access, nearby competing towns...
    /// Do not implement a fixed radius or multiplier merely because two
    /// settlements have the same resident population."
    ///
    /// The query is DERIVED: it runs over real farms, real routes, and real
    /// competing centers every time. Nothing is cached as an independent counter.
    /// </summary>
    public sealed class CatchmentQuery
    {
        /// <summary>
        /// Maximum one-way wagon travel for a trade relationship (calibration:
        /// a day's wagon trip). This is a travel-TIME bound, not a radius —
        /// a far farm on a fast road beats a near farm with no road.
        /// </summary>
        public const int MaxCatchmentMinutesWagon = 600;

        public sealed class CatchmentResult
        {
            public List<RuralUnit> UnitsInCatchment = new List<RuralUnit>();
            public List<string> Diagnostics = new List<string>();
        }

        /// <param name="competingCenterLocationIds">
        /// Other settlements' locations. A rural unit strictly closer (by wagon
        /// travel time) to a competitor belongs to the competitor — market
        /// territory is competitive (Canon §1.4).
        /// </param>
        public CatchmentResult Compute(
            string settlementLocationId,
            List<RuralUnit> ruralUnits,
            List<string> competingCenterLocationIds,
            JourneyModel journeys,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new CatchmentResult();
            if (journeys == null || string.IsNullOrWhiteSpace(settlementLocationId) || ruralUnits == null)
            {
                diag.Add("CatchmentQuery: missing journeys, settlement, or rural units — empty catchment, not a guess.");
                return result;
            }

            foreach (RuralUnit unit in ruralUnits)
            {
                if (unit == null || string.IsNullOrWhiteSpace(unit.LocationId)) continue;
                JourneyRoute route = journeys.FindRoute(settlementLocationId, unit.LocationId, TravelMode.Wagon);
                if (!route.Found)
                {
                    result.Diagnostics.Add($"'{unit.UnitId}': no wagon route — outside the catchment ({route.Diagnostic}).");
                    continue;
                }
                if (route.TotalMinutes > MaxCatchmentMinutesWagon)
                {
                    result.Diagnostics.Add($"'{unit.UnitId}': {route.TotalMinutes} min by wagon exceeds a day's trip — outside the catchment.");
                    continue;
                }
                if (IsCloserToCompetitor(unit.LocationId, route.TotalMinutes, competingCenterLocationIds, journeys))
                {
                    result.Diagnostics.Add($"'{unit.UnitId}': strictly closer to a competing center — excluded (competitive territory).");
                    continue;
                }
                result.UnitsInCatchment.Add(unit);
            }
            return result;
        }

        private bool IsCloserToCompetitor(
            string unitLocationId, int minutesToHome,
            List<string> competitors, JourneyModel journeys)
        {
            if (competitors == null) return false;
            foreach (string competitor in competitors)
            {
                if (string.IsNullOrWhiteSpace(competitor)) continue;
                JourneyRoute route = journeys.FindRoute(competitor, unitLocationId, TravelMode.Wagon);
                if (route.Found && route.TotalMinutes < minutesToHome) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// T2E: the market territory aggregate for one settlement — its role, its
    /// derived catchment, and its population views. Works with Kennedy's
    /// hand-authored scenarios: the caller supplies the world state; nothing
    /// here generates anything.
    /// </summary>
    public sealed class MarketTerritory
    {
        public string SettlementLocationId = string.Empty;
        public SettlementRole Role = SettlementRole.Unspecified;
        public List<RuralUnit> CatchmentUnits = new List<RuralUnit>();
        public int BuiltUpPopulation;
        public int RuralServicePopulation;
        public int TransientVisitorsPerDay;
        public int EffectiveMarketPopulation =>
            Math.Max(0, BuiltUpPopulation) + Math.Max(0, RuralServicePopulation) + Math.Max(0, TransientVisitorsPerDay);
    }

    /// <summary>
    /// T2E: where the demand-driven town generator plugs in. HELD for
    /// Kennedy's generator-vs-authored decision — DO NOT implement the
    /// generator here. The causal order is: physical rural settlement →
    /// routes/travel → catchment (derived) → demand drivers → opportunities
    /// → [GENERATOR] → businesses. Everything before the bracket is built;
    /// the bracket is his call.
    /// </summary>
    public static class GeneratorSeam
    {
        public static string HeldReason =>
            "The demand-driven town generator is HELD for Kennedy's generator-vs-authored decision. " +
            "Demand drivers produce BusinessOpportunity lists (the generator's input); " +
            "the generator itself — turning opportunities into placed businesses — is not built.";
    }
}
