using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.World.Journeys;

namespace LandLedgers.World.Regional
{
    /// <summary>
    /// Slice 15 (2026-11-05): delivered cost and multi-stop collection.
    ///
    /// Delivered cost decides, not the goods price alone (brief §Prices):
    /// goods + freight + loading time, per unit, for the order as if it
    /// ships alone. Computing it per order as if solo is conservative —
    /// a grouped shipment only ever costs LESS per unit, so the comparison
    /// never favors a supplier on freight it will not pay. The freight
    /// quote comes from a rate card; the default card is the opening
    /// freight company's authored rates (base 25c, 1c/mile, round trip
    /// charged — DevelopmentTownConfiguration.asset). Those are THAT
    /// carrier's rates, not universal freight law: any carrier may quote
    /// its own card, and the calculator takes the card as a parameter.
    ///
    /// The multi-stop planner turns one day's orders from one town into a
    /// single journey manifest: home -> gateway -> town centre -> each
    /// supplier site in turn -> centre -> gateway -> home, with per-stop
    /// custody (supplier, orders, units, site). It PLANS; it does not
    /// execute. Horse/wagon prep, departure, loading, and custody transfer
    /// belong to the freight layer, and visible departure needs Unity
    /// verification. Wagon capacity is a parameter; the default 600 units
    /// is the freight pool's default (FreightResourcePool.capacityUnits),
    /// not a new invention.
    /// </summary>
    public struct RegionalFreightRateCard
    {
        public int BaseFeeCents;
        public int PerMileCents;
        public bool ChargeRoundTrip;

        /// <summary>The opening freight company's authored rates.</summary>
        public static readonly RegionalFreightRateCard OpeningCompany = new RegionalFreightRateCard
        {
            BaseFeeCents = 25,
            PerMileCents = 1,
            ChargeRoundTrip = true,
        };
    }

    public static class RegionalDeliveredCost
    {
        /// <summary>Freight cents for a solo shipment over one-way miles.</summary>
        public static int FreightCentsForSoloShipment(
            float oneWayMiles, RegionalFreightRateCard card)
        {
            float billedMiles = card.ChargeRoundTrip ? oneWayMiles * 2f : oneWayMiles;
            return Math.Max(0, card.BaseFeeCents) +
                (int)Math.Ceiling(Math.Max(0f, billedMiles) * Math.Max(0, card.PerMileCents));
        }

        /// <summary>
        /// Delivered cents per unit for an order: (goods + solo freight) /
        /// units, rounded UP so the cost is never understated. Returns 0
        /// when the inputs cannot price the order (no supplier, no units,
        /// no quote).
        /// </summary>
        public static int DeliveredCentsPerUnit(
            IUpstreamGoodsSupplier supplier, string categoryId, int units,
            RegionalFreightRateCard card)
        {
            if (supplier == null || string.IsNullOrWhiteSpace(categoryId) || units <= 0) return 0;
            int goods = Math.Max(0, supplier.QuoteUnitCostCents(categoryId));
            if (goods <= 0) return 0;
            var regional = supplier as RegionalSupplierAdapter;
            if (regional == null) return goods; // No freight data for this supplier: goods-only, honestly.
            RegionalTownRegistry.RegionalTownDefinition town = null;
            for (int i = 0; i < RegionalTownRegistry.All.Length; i++)
                if (string.Equals(RegionalTownRegistry.All[i].TownId, regional.TownId,
                    StringComparison.OrdinalIgnoreCase))
                    town = RegionalTownRegistry.All[i];
            if (town == null) return goods;
            int freight = FreightCentsForSoloShipment(RegionalTownRegistry.HomeToCentreMiles(town), card);
            int total = goods * units + freight;
            return (total + units - 1) / units; // ceiling division
        }
    }

    /// <summary>One order waiting on a multi-stop collection.</summary>
    public sealed class RegionalCollectionOrder
    {
        public string OrderId = string.Empty;
        public string SupplierId = string.Empty;
        public string CategoryId = string.Empty;
        public int Units;
    }

    /// <summary>One pickup stop on a multi-stop journey.</summary>
    public sealed class RegionalCollectionStop
    {
        public string SupplierId = string.Empty;
        public string SupplierName = string.Empty;
        public string SiteLocationId = string.Empty;
        public List<string> OrderIds = new List<string>();
        public int Units;
    }

    /// <summary>One town's collection journey: stops, miles, minutes, freight.</summary>
    public sealed class RegionalMultiStopPlan
    {
        public string TownId = string.Empty;
        public List<RegionalCollectionStop> Stops = new List<RegionalCollectionStop>();
        public float TotalMiles;
        public int TotalMinutes;
        public int FreightCents;
        public List<string> Diagnostics = new List<string>();
    }

    public static class RegionalMultiStopPlanner
    {
        /// <summary>Default wagon capacity in units (FreightResourcePool.capacityUnits).</summary>
        public const int DefaultWagonCapacityUnits = 600;

        /// <summary>
        /// Plans one collection journey per town for the given orders.
        /// Orders that do not fit the wagon (alone or after earlier stops)
        /// are left unplanned and named in the diagnostics — never silently
        /// dropped, never overloaded. Returns one plan per town that has at
        /// least one planned stop.
        /// </summary>
        public static List<RegionalMultiStopPlan> Plan(
            JourneyModel model,
            IReadOnlyList<RegionalCollectionOrder> orders,
            int wagonCapacityUnits,
            RegionalFreightRateCard card,
            out List<RegionalCollectionOrder> unplanned)
        {
            unplanned = new List<RegionalCollectionOrder>();
            var plans = new List<RegionalMultiStopPlan>();
            if (model == null || orders == null) return plans;
            int capacity = Math.Max(1, wagonCapacityUnits);

            var byTown = new Dictionary<string, List<RegionalCollectionOrder>>(StringComparer.OrdinalIgnoreCase);
            foreach (RegionalCollectionOrder order in orders)
            {
                if (order == null || string.IsNullOrWhiteSpace(order.SupplierId) || order.Units <= 0)
                {
                    if (order != null) unplanned.Add(order);
                    continue;
                }
                string townId = TownIdForSupplier(order.SupplierId);
                if (string.IsNullOrEmpty(townId))
                {
                    unplanned.Add(order);
                    continue;
                }
                if (!byTown.TryGetValue(townId, out List<RegionalCollectionOrder> list))
                {
                    list = new List<RegionalCollectionOrder>();
                    byTown[townId] = list;
                }
                list.Add(order);
            }

            foreach (KeyValuePair<string, List<RegionalCollectionOrder>> pair in byTown)
            {
                RegionalMultiStopPlan plan = PlanTown(model, pair.Key, pair.Value, capacity, card, unplanned);
                if (plan != null) plans.Add(plan);
            }
            return plans;
        }

        private static RegionalMultiStopPlan PlanTown(
            JourneyModel model, string townId,
            List<RegionalCollectionOrder> orders, int capacity,
            RegionalFreightRateCard card, List<RegionalCollectionOrder> unplanned)
        {
            RegionalTownRegistry.RegionalTownDefinition town = null;
            for (int i = 0; i < RegionalTownRegistry.All.Length; i++)
                if (string.Equals(RegionalTownRegistry.All[i].TownId, townId,
                    StringComparison.OrdinalIgnoreCase))
                    town = RegionalTownRegistry.All[i];
            if (town == null) { unplanned.AddRange(orders); return null; }

            // Group the town's orders by supplier, nearest site first.
            var bySupplier = new Dictionary<string, RegionalCollectionStop>(StringComparer.OrdinalIgnoreCase);
            foreach (RegionalCollectionOrder order in orders)
            {
                if (!bySupplier.TryGetValue(order.SupplierId, out RegionalCollectionStop stop))
                {
                    RegionalBusinessRegistry.RegionalBusinessDefinition business =
                        BusinessForSupplier(order.SupplierId);
                    stop = new RegionalCollectionStop
                    {
                        SupplierId = order.SupplierId,
                        SupplierName = business != null ? business.DisplayName : order.SupplierId,
                        SiteLocationId = RegionalTownRegistry.SiteLocationId(town,
                            SiteForSupplier(town, order.SupplierId)),
                    };
                    bySupplier[order.SupplierId] = stop;
                }
                stop.OrderIds.Add(order.OrderId);
                stop.Units += order.Units;
            }

            var stops = new List<RegionalCollectionStop>(bySupplier.Values);
            stops.Sort((a, b) =>
            {
                float da = DistanceFromCentre(town, a.SupplierId);
                float db = DistanceFromCentre(town, b.SupplierId);
                int byDistance = da.CompareTo(db);
                return byDistance != 0
                    ? byDistance
                    : string.Compare(a.SupplierId, b.SupplierId, StringComparison.OrdinalIgnoreCase);
            });

            var plan = new RegionalMultiStopPlan { TownId = townId };
            int loaded = 0;
            string current = "town-center";
            float totalMiles = 0f;

            // Outbound: home -> gateway -> centre.
            foreach (string next in new[] { RegionalTownRegistry.GatewayLocationId(town),
                RegionalTownRegistry.CentreLocationId(town) })
            {
                JourneyRoute leg = model.FindRoute(current, next, TravelMode.Wagon);
                if (!leg.Found)
                {
                    plan.Diagnostics.Add($"RegionalMultiStop: no route {current} -> {next}; town {town.DisplayName} unplanned.");
                    unplanned.AddRange(orders);
                    return null;
                }
                totalMiles += leg.TotalMiles;
                current = next;
            }

            // Stops in order, then back to the centre, gateway, home.
            foreach (RegionalCollectionStop stop in stops)
            {
                if (loaded + stop.Units > capacity)
                {
                    plan.Diagnostics.Add($"RegionalMultiStop: {stop.SupplierName} ({stop.Units}u) does not fit " +
                        $"the wagon ({loaded}u loaded of {capacity}u); its orders wait for the next journey.");
                    foreach (string orderId in stop.OrderIds)
                    {
                        foreach (RegionalCollectionOrder order in orders)
                            if (order.OrderId == orderId) unplanned.Add(order);
                    }
                    continue;
                }
                JourneyRoute leg = model.FindRoute(current, stop.SiteLocationId, TravelMode.Wagon);
                if (!leg.Found)
                {
                    plan.Diagnostics.Add($"RegionalMultiStop: no route to {stop.SupplierName}; its orders wait.");
                    foreach (string orderId in stop.OrderIds)
                    {
                        foreach (RegionalCollectionOrder order in orders)
                            if (order.OrderId == orderId) unplanned.Add(order);
                    }
                    continue;
                }
                totalMiles += leg.TotalMiles;
                current = stop.SiteLocationId;
                loaded += stop.Units;
                plan.Stops.Add(stop);
            }

            if (plan.Stops.Count == 0)
            {
                plan.Diagnostics.Add($"RegionalMultiStop: nothing in {town.DisplayName} fits the wagon; no journey planned.");
                return null;
            }

            foreach (string next in new[] { RegionalTownRegistry.CentreLocationId(town),
                RegionalTownRegistry.GatewayLocationId(town), "town-center" })
            {
                JourneyRoute leg = model.FindRoute(current, next, TravelMode.Wagon);
                if (!leg.Found)
                {
                    plan.Diagnostics.Add($"RegionalMultiStop: no return route {current} -> {next}; plan is incomplete.");
                    return plan;
                }
                totalMiles += leg.TotalMiles;
                current = next;
            }

            plan.TotalMiles = totalMiles;
            plan.TotalMinutes = JourneyModel.MinutesForMiles(totalMiles, TravelMode.Wagon);
            plan.FreightCents = FreightCentsForJourney(totalMiles, card);
            plan.Diagnostics.Add($"RegionalMultiStop: {town.DisplayName} collection, {plan.Stops.Count} stop(s), " +
                $"{loaded}u loaded, {totalMiles:F1} miles round trip, {plan.TotalMinutes} minutes, freight {plan.FreightCents}c.");
            return plan;
        }

        /// <summary>Freight for a journey whose total miles already include the return leg.</summary>
        public static int FreightCentsForJourney(float totalMiles, RegionalFreightRateCard card)
        {
            float billedMiles = card.ChargeRoundTrip ? totalMiles : totalMiles / 2f;
            return Math.Max(0, card.BaseFeeCents) +
                (int)Math.Ceiling(Math.Max(0f, billedMiles) * Math.Max(0, card.PerMileCents));
        }

        private static string TownIdForSupplier(string supplierId)
        {
            for (int i = 0; i < RegionalBusinessRegistry.All.Length; i++)
                if (string.Equals(RegionalBusinessRegistry.All[i].BusinessId, supplierId,
                    StringComparison.OrdinalIgnoreCase))
                    return RegionalBusinessRegistry.All[i].TownId;
            return string.Empty;
        }

        private static RegionalBusinessRegistry.RegionalBusinessDefinition BusinessForSupplier(string supplierId)
        {
            for (int i = 0; i < RegionalBusinessRegistry.All.Length; i++)
                if (string.Equals(RegionalBusinessRegistry.All[i].BusinessId, supplierId,
                    StringComparison.OrdinalIgnoreCase))
                    return RegionalBusinessRegistry.All[i];
            return null;
        }

        private static RegionalTownRegistry.RegionalBusinessSite SiteForSupplier(
            RegionalTownRegistry.RegionalTownDefinition town, string supplierId)
        {
            for (int i = 0; i < town.Sites.Length; i++)
                if (string.Equals(town.Sites[i].BusinessId, supplierId,
                    StringComparison.OrdinalIgnoreCase))
                    return town.Sites[i];
            return new RegionalTownRegistry.RegionalBusinessSite { BusinessId = supplierId };
        }

        private static float DistanceFromCentre(
            RegionalTownRegistry.RegionalTownDefinition town, string supplierId)
        {
            RegionalTownRegistry.RegionalBusinessSite site = SiteForSupplier(town, supplierId);
            return (float)Math.Sqrt(site.LocalEastMiles * site.LocalEastMiles +
                site.LocalNorthMiles * site.LocalNorthMiles);
        }
    }
}
