using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Market.RegionalTrade
{
    /// <summary>
    /// T3A: one hand-authored settlement. Canon master doctrine: "Settlement
    /// role can outweigh size. Agricultural service town, railhead,
    /// county/administrative center, mining node, river center and industrial
    /// node generate different demand even at similar resident population."
    ///
    /// Settlements are scenario-authored (like FarmDefinition), never
    /// generated here — the generator core is held for Kennedy's decision.
    /// ResidentCount is authored data, not a synthesized counter.
    /// </summary>
    [Serializable]
    public sealed class Settlement
    {
        public string SettlementId = string.Empty;
        public string Name = string.Empty;
        public string LocationId = string.Empty; // JRN location
        public SettlementRole Role = SettlementRole.Unspecified;
        public int ResidentCount;
        public bool HasRailAccess;

        public Settlement() { }
    }

    /// <summary>
    /// T3A: a price OBSERVED at a real merchant. The trade service NEVER
    /// invents prices — callers pass observations from real merchants' price
    /// lists. BuyCents = what the merchant pays us (we sell to them);
    /// SellCents = what the merchant charges us (we buy from them).
    /// </summary>
    [Serializable]
    public sealed class PriceObservation
    {
        public string SettlementId = string.Empty;
        public string MerchantName = string.Empty; // the real merchant observed
        public string GoodKind = string.Empty;
        public int BuyCents;
        public int SellCents;
        public int DayIndex;

        public PriceObservation() { }
    }

    /// <summary>
    /// T3A: a rail connection between two rail-served points. Rail is cheaper
    /// per mile than wagon (calibration) but only runs where track exists —
    /// Tech X notes station businesses (elevators, fuel dealers, merchants)
    /// connecting to the railway through real infrastructure.
    /// </summary>
    [Serializable]
    public sealed class RailConnection
    {
        public string FromLocationId = string.Empty;
        public string ToLocationId = string.Empty;
        public float Miles;
        public int CentsPerMile; // calibration: rail freight rate

        public RailConnection() { }
    }

    /// <summary>
    /// T3A: an honest arbitrage opportunity derived from real observations.
    /// Every unit physically travels — no teleporting goods.
    /// </summary>
    [Serializable]
    public sealed class ArbitrageOpportunity
    {
        public string GoodKind = string.Empty;
        public string BuySettlementId = string.Empty;  // buy here at merchant sell price
        public string SellSettlementId = string.Empty; // sell here at merchant buy price
        public string BuyMerchant = string.Empty;
        public string SellMerchant = string.Empty;
        public int Units;
        public int GrossMarginCents;
        public int FreightCents;
        public int NetCents;
        public int TravelMinutes;
        public bool UsesRail;

        public ArbitrageOpportunity() { }
    }

    /// <summary>
    /// T3A: regional trade across hand-authored settlements. Builds on the
    /// T2E MarketTerritory catchment queries (competitive exclusion applied
    /// across towns), the JRN journey model (real routes, real miles), and
    /// the BIZ-3 freight flow (shipments carry real cargo).
    ///
    /// Canon locks honored: no invented prices, no teleporting goods, no
    /// synthetic demand. Arbitrage is a DERIVED read over real observations.
    /// </summary>
    public sealed class RegionalTradeService
    {
        /// <summary>
        /// Calibration: wagon freight rate, cents per unit per mile.
        /// Marked calibration per Canon Part XV, not doctrine.
        /// </summary>
        public int WagonCentsPerUnitMile = 2;

        private readonly List<Settlement> settlements = new List<Settlement>();
        private readonly List<RailConnection> railConnections = new List<RailConnection>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<Settlement> Settlements => settlements;

        public string RegisterSettlement(Settlement settlement, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (settlement == null || string.IsNullOrWhiteSpace(settlement.SettlementId))
                return "RegionalTradeService.RegisterSettlement: a settlement id is required.";
            if (string.IsNullOrWhiteSpace(settlement.LocationId))
                return $"RegionalTradeService.RegisterSettlement: settlement '{settlement.SettlementId}' needs a journey location.";
            foreach (Settlement existing in settlements)
            {
                if (string.Equals(existing.SettlementId, settlement.SettlementId, StringComparison.Ordinal))
                    return $"RegionalTradeService.RegisterSettlement: settlement '{settlement.SettlementId}' already registered.";
            }
            settlements.Add(settlement);
            diag.Add($"RegionalTradeService: settlement '{settlement.Name}' registered as {settlement.Role}" +
                (settlement.HasRailAccess ? " (rail-served)." : "."));
            return null;
        }

        public void AddRailConnection(RailConnection connection)
        {
            if (connection == null) return;
            railConnections.Add(connection);
        }

        /// <summary>
        /// T3A: per-settlement catchment with every other settlement as a
        /// competitor — T2E competitive exclusion applied across towns.
        /// </summary>
        public Dictionary<string, CatchmentQuery.CatchmentResult> ComputeCatchments(
            List<RuralUnit> ruralUnits, JourneyModel journeys, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var results = new Dictionary<string, CatchmentQuery.CatchmentResult>(StringComparer.Ordinal);
            var query = new CatchmentQuery();
            foreach (Settlement home in settlements)
            {
                var competitors = new List<string>();
                foreach (Settlement other in settlements)
                {
                    if (!string.Equals(other.SettlementId, home.SettlementId, StringComparison.Ordinal)
                        && !string.IsNullOrWhiteSpace(other.LocationId))
                        competitors.Add(other.LocationId);
                }
                results[home.SettlementId] = query.Compute(
                    home.LocationId, ruralUnits, competitors, journeys, diag);
            }
            return results;
        }

        /// <summary>
        /// T3A: honest freight cost between two settlements. Rail is used only
        /// when BOTH ends are rail-served and a rail connection exists; the
        /// wagon legs to/from the railheads are still priced. Otherwise the
        /// full JRN wagon route is priced per unit-mile.
        /// </summary>
        public int FreightCostCents(
            string fromLocationId, string toLocationId, int units,
            JourneyModel journeys, List<string> diag, out bool usesRail, out int travelMinutes)
        {
            diag = diag ?? diagnostics;
            usesRail = false;
            travelMinutes = 0;
            if (journeys == null || units <= 0) return 0;

            Settlement from = FindByLocation(fromLocationId);
            Settlement to = FindByLocation(toLocationId);

            // Rail leg: both rail-served and a connection exists.
            if (from != null && to != null && from.HasRailAccess && to.HasRailAccess)
            {
                RailConnection rail = FindRail(fromLocationId, toLocationId);
                if (rail != null && rail.Miles > 0f)
                {
                    usesRail = true;
                    travelMinutes = JourneyModel.MinutesForMiles(rail.Miles, TravelMode.Wagon) / 3; // rail is faster (calibration)
                    int cost = Mathf.Max(0, Mathf.RoundToInt(rail.Miles * rail.CentsPerMile)) * units;
                    diag.Add($"RegionalTradeService: rail haul {from.Name} → {to.Name}, {rail.Miles:F1} mi, {cost}c for {units} units.");
                    return cost;
                }
            }

            JourneyRoute route = journeys.FindRoute(fromLocationId, toLocationId, TravelMode.Wagon);
            if (!route.Found)
            {
                diag.Add($"RegionalTradeService: no wagon route {fromLocationId} → {toLocationId} — no freight cost computable ({route.Diagnostic}).");
                return -1;
            }
            travelMinutes = route.TotalMinutes;
            float miles = route.TotalMinutes / 60f * JourneyModel.SpeedMph(TravelMode.Wagon);
            int wagonCost = Mathf.Max(0, Mathf.RoundToInt(miles * WagonCentsPerUnitMile)) * units;
            diag.Add($"RegionalTradeService: wagon haul {fromLocationId} → {toLocationId}, {miles:F1} mi / {travelMinutes} min, {wagonCost}c for {units} units.");
            return wagonCost;
        }

        /// <summary>
        /// T3A: arbitrage as a DERIVED read. For every good with observations
        /// at two settlements, margin = (sell-side merchant buy price) minus
        /// (buy-side merchant sell price), net of real freight. Only positive
        /// net opportunities are returned. Empty observations → no
        /// opportunities, never invented ones.
        /// </summary>
        public List<ArbitrageOpportunity> FindArbitrage(
            List<PriceObservation> observations, int units,
            JourneyModel journeys, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var opportunities = new List<ArbitrageOpportunity>();
            if (observations == null || observations.Count == 0 || units <= 0)
            {
                diag.Add("RegionalTradeService.FindArbitrage: no price observations — no opportunities, none invented.");
                return opportunities;
            }

            // Latest observation per (settlement, good, merchant).
            var latest = new Dictionary<string, PriceObservation>(StringComparer.Ordinal);
            foreach (PriceObservation obs in observations)
            {
                if (obs == null || string.IsNullOrWhiteSpace(obs.SettlementId)
                    || string.IsNullOrWhiteSpace(obs.GoodKind)
                    || string.IsNullOrWhiteSpace(obs.MerchantName))
                    continue;
                string key = obs.SettlementId + "|" + obs.GoodKind + "|" + obs.MerchantName;
                if (!latest.TryGetValue(key, out PriceObservation prior) || obs.DayIndex >= prior.DayIndex)
                    latest[key] = obs;
            }

            var byGood = new Dictionary<string, List<PriceObservation>>(StringComparer.Ordinal);
            foreach (PriceObservation obs in latest.Values)
            {
                if (!byGood.TryGetValue(obs.GoodKind, out var list))
                {
                    list = new List<PriceObservation>();
                    byGood[obs.GoodKind] = list;
                }
                list.Add(obs);
            }

            foreach (var pair in byGood)
            {
                List<PriceObservation> quotes = pair.Value;
                for (int i = 0; i < quotes.Count; i++)
                {
                    for (int j = 0; j < quotes.Count; j++)
                    {
                        if (i == j) continue;
                        PriceObservation buySide = quotes[i];  // we BUY here (pay merchant sell price)
                        PriceObservation sellSide = quotes[j]; // we SELL here (receive merchant buy price)
                        if (string.Equals(buySide.SettlementId, sellSide.SettlementId, StringComparison.Ordinal))
                            continue;
                        int grossPerUnit = sellSide.BuyCents - buySide.SellCents;
                        if (grossPerUnit <= 0) continue;

                        int freight = FreightCostCents(
                            SettlementLocation(buySide.SettlementId),
                            SettlementLocation(sellSide.SettlementId),
                            units, journeys, diag, out bool usesRail, out int minutes);
                        if (freight < 0) continue; // unroutable — honestly skipped

                        int gross = grossPerUnit * units;
                        int net = gross - freight;
                        if (net <= 0) continue;

                        opportunities.Add(new ArbitrageOpportunity
                        {
                            GoodKind = pair.Key,
                            BuySettlementId = buySide.SettlementId,
                            SellSettlementId = sellSide.SettlementId,
                            BuyMerchant = buySide.MerchantName,
                            SellMerchant = sellSide.MerchantName,
                            Units = units,
                            GrossMarginCents = gross,
                            FreightCents = freight,
                            NetCents = net,
                            TravelMinutes = minutes,
                            UsesRail = usesRail,
                        });
                    }
                }
            }

            opportunities.Sort((a, b) => b.NetCents.CompareTo(a.NetCents));
            diag.Add($"RegionalTradeService: {opportunities.Count} honest arbitrage opportunities derived from {latest.Count} price observations.");
            return opportunities;
        }

        private Settlement FindByLocation(string locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId)) return null;
            foreach (Settlement s in settlements)
            {
                if (string.Equals(s.LocationId, locationId, StringComparison.Ordinal)) return s;
            }
            return null;
        }

        private string SettlementLocation(string settlementId)
        {
            foreach (Settlement s in settlements)
            {
                if (string.Equals(s.SettlementId, settlementId, StringComparison.Ordinal)) return s.LocationId;
            }
            return string.Empty;
        }

        private RailConnection FindRail(string fromLocationId, string toLocationId)
        {
            foreach (RailConnection rail in railConnections)
            {
                if ((string.Equals(rail.FromLocationId, fromLocationId, StringComparison.Ordinal)
                        && string.Equals(rail.ToLocationId, toLocationId, StringComparison.Ordinal))
                    || (string.Equals(rail.FromLocationId, toLocationId, StringComparison.Ordinal)
                        && string.Equals(rail.ToLocationId, fromLocationId, StringComparison.Ordinal)))
                    return rail;
            }
            return null;
        }
    }
}
