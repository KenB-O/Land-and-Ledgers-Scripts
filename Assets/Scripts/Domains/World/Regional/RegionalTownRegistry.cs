using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;

namespace LandLedgers.World.Regional
{
    /// <summary>
    /// Slice 12 (2026-11-05): the authored off-map regional towns Ashford
    /// and Redfield, and their business sites, as pure data.
    ///
    /// Coordinates are MILES on the JourneyModel's 1:1 scale (Canon §13.1
    /// CANON LOCK), measured from the home town centre (0,0). Route miles
    /// come from EDGES, not from coordinates: home centre -> gateway is
    /// 49.5 / 99.5 miles, gateway -> town centre is 0.5, so the
    /// town-centre to town-centre totals are exactly 50 / 100 miles.
    /// Centre coordinates are 50/sqrt(2) and 100/sqrt(2) on the 45-degree
    /// diagonals; the float rounding there (~0.00001 mi) is a display
    /// artifact only and never feeds a route.
    ///
    /// Ashford (50 mi NE, "Millbrook Road") is where Millbrook Grain
    /// Company operates: the business id, the 50-mile pickup, and the
    /// definition asset are preserved exactly — this registry only gives
    /// the company a site on Ashford's Mill Street. Redfield (100 mi SW,
    /// "Redfield Road") is on the opposite bearing on its own named road;
    /// there is NO direct Ashford–Redfield edge, so routes between them
    /// go via the home town.
    ///
    /// Sites are deterministic and authored (PositionAuthored = true).
    /// Business inventory, offers, and prices are NOT here — Slice 13.
    /// The registry is static authored data, like the tier lists: it is
    /// re-applied on load, so old saves gain the towns with no migration.
    /// </summary>
    public static class RegionalTownRegistry
    {
        /// <summary>One business property inside a regional town.</summary>
        public sealed class RegionalBusinessSite
        {
            public string BusinessId = string.Empty;
            public string DisplayName = string.Empty;
            /// <summary>Business kind string (grain_mill, general_store, butcher, livery_freight, blacksmith, sawmill, dry_goods, apothecary, ironworks, textiles, fuel_yard). Not an enum: regional kinds are authored here, not in the town's business-type enum.</summary>
            public string BusinessKind = string.Empty;
            public string Street = string.Empty;
            /// <summary>Miles east of the town centre.</summary>
            public float LocalEastMiles;
            /// <summary>Miles north of the town centre.</summary>
            public float LocalNorthMiles;
            public JourneyLocationKind LocationKind = JourneyLocationKind.TownBuilding;
        }

        /// <summary>One persistent off-map regional town.</summary>
        public sealed class RegionalTownDefinition
        {
            public string TownId = string.Empty;
            public string DisplayName = string.Empty;
            public float CentreEastMiles;
            public float CentreNorthMiles;
            public string RoadName = string.Empty;
            /// <summary>Miles from the home town centre to this town's gateway (arrival point). Centre is a further 0.5 miles in.</summary>
            public float GatewayMilesFromHome;
            public string[] Streets = Array.Empty<string>();
            public RegionalBusinessSite[] Sites = Array.Empty<RegionalBusinessSite>();
        }

        private static RegionalBusinessSite Site(
            string businessId, string displayName, string kind, string street,
            float eastMiles, float northMiles, JourneyLocationKind locationKind)
        {
            return new RegionalBusinessSite
            {
                BusinessId = businessId,
                DisplayName = displayName,
                BusinessKind = kind,
                Street = street,
                LocalEastMiles = eastMiles,
                LocalNorthMiles = northMiles,
                LocationKind = locationKind,
            };
        }

        public static readonly RegionalTownDefinition Ashford = new RegionalTownDefinition
        {
            TownId = "ashford",
            DisplayName = "Ashford",
            CentreEastMiles = 35.35534f,
            CentreNorthMiles = 35.35534f,
            RoadName = "Millbrook Road",
            GatewayMilesFromHome = 49.5f,
            Streets = new[] { "Main Street", "Mill Street", "Smithy Row", "Livery Lane" },
            Sites = new[]
            {
                // Millbrook Grain Company: the existing fixture, preserved.
                // Same business id, same definition asset, same 50-mile
                // pickup; this registry only gives it a site.
                Site("millbrook_grain_co", "Millbrook Grain Co.", "grain_mill", "Mill Street",
                    0.30f, 0.10f, JourneyLocationKind.Mill),
                Site("ashford_general_mercantile", "Ashford General Mercantile", "general_store", "Main Street",
                    0.02f, 0.02f, JourneyLocationKind.Store),
                Site("ashford_butcher", "Ashford Butcher & Provisions", "butcher", "Main Street",
                    -0.03f, 0.04f, JourneyLocationKind.ButcherShop),
                Site("ashford_livery_freight", "Ashford Livery & Freight", "livery_freight", "Livery Lane",
                    -0.15f, -0.05f, JourneyLocationKind.TownBuilding),
                Site("ashford_blacksmith", "Ashford Blacksmith", "blacksmith", "Smithy Row",
                    0.08f, -0.06f, JourneyLocationKind.TownBuilding),
                Site("ashford_sawmill", "Ashford Sawmill & Lumber Yard", "sawmill", "Mill Street",
                    0.35f, -0.05f, JourneyLocationKind.TownBuilding),
                Site("ashford_dry_goods", "Ashford Dry Goods & Tailor", "dry_goods", "Main Street",
                    0.05f, -0.02f, JourneyLocationKind.TownBuilding),
                Site("ashford_apothecary", "Ashford Apothecary", "apothecary", "Main Street",
                    -0.02f, -0.05f, JourneyLocationKind.TownBuilding),
            },
        };

        public static readonly RegionalTownDefinition Redfield = new RegionalTownDefinition
        {
            TownId = "redfield",
            DisplayName = "Redfield",
            CentreEastMiles = -70.71068f,
            CentreNorthMiles = -70.71068f,
            RoadName = "Redfield Road",
            GatewayMilesFromHome = 99.5f,
            Streets = new[] { "Main Street", "Market Street", "Mill Street", "Foundry Lane", "Livery Lane", "Yard Road" },
            Sites = new[]
            {
                Site("redfield_grain_feed", "Redfield Grain & Feed Co.", "grain_mill", "Mill Street",
                    0.28f, 0.08f, JourneyLocationKind.Elevator),
                Site("redfield_general_store", "Redfield General Store", "general_store", "Main Street",
                    0.02f, -0.02f, JourneyLocationKind.Store),
                Site("redfield_butcher", "Redfield Butcher", "butcher", "Market Street",
                    -0.04f, 0.03f, JourneyLocationKind.ButcherShop),
                Site("redfield_ironworks", "Redfield Ironworks & Hardware", "ironworks", "Foundry Lane",
                    0.12f, -0.18f, JourneyLocationKind.TownBuilding),
                Site("redfield_textiles", "Redfield Textile Merchant", "textiles", "Main Street",
                    0.06f, 0.05f, JourneyLocationKind.TownBuilding),
                Site("redfield_livery_freight", "Redfield Livery & Freight", "livery_freight", "Livery Lane",
                    -0.18f, -0.08f, JourneyLocationKind.TownBuilding),
                Site("redfield_fuel_yard", "Redfield Coal & Fuel Yard", "fuel_yard", "Yard Road",
                    -0.30f, 0.12f, JourneyLocationKind.TownBuilding),
                Site("redfield_apothecary", "Redfield Apothecary & Sundries", "apothecary", "Market Street",
                    -0.05f, -0.06f, JourneyLocationKind.TownBuilding),
            },
        };

        public static readonly RegionalTownDefinition[] All = { Ashford, Redfield };

        public static string CentreLocationId(RegionalTownDefinition town) => town.TownId;
        public static string GatewayLocationId(RegionalTownDefinition town) => town.TownId + "-gateway";
        public static string SiteLocationId(RegionalTownDefinition town, RegionalBusinessSite site) =>
            town.TownId + "-" + site.BusinessId;

        /// <summary>All location ids this town registers (centre + gateway + sites).</summary>
        public static List<string> LocationIdsFor(RegionalTownDefinition town)
        {
            var ids = new List<string> { CentreLocationId(town), GatewayLocationId(town) };
            for (int i = 0; i < town.Sites.Length; i++)
                ids.Add(SiteLocationId(town, town.Sites[i]));
            return ids;
        }

        /// <summary>
        /// Registers one town's locations and edges into the model. The home
        /// town centre ("town-center") must already be registered: the
        /// gateway leg hangs off it, and the town-rebuild rule keeps exactly
        /// such regional legs attached to "town-center". Idempotent: a town
        /// whose locations all already exist is skipped with a diagnostic;
        /// a town whose locations only partly exist is refused, never half
        /// applied. Returns the number of locations registered (0 when
        /// skipped), or -1 with a diagnostic on refusal.
        /// </summary>
        public static int ApplyTown(JourneyModel model, RegionalTownDefinition town, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (model == null) { diag.Add("RegionalTownRegistry: no journey model supplied."); return -1; }
            if (town == null) { diag.Add("RegionalTownRegistry: no town definition supplied."); return -1; }
            if (model.GetLocation("town-center") == null)
            {
                diag.Add($"RegionalTownRegistry: home town centre 'town-center' is not registered; " +
                    $"cannot attach {town.DisplayName} to it.");
                return -1;
            }

            List<string> ids = LocationIdsFor(town);
            int existing = 0;
            for (int i = 0; i < ids.Count; i++)
                if (model.GetLocation(ids[i]) != null) existing++;
            if (existing == ids.Count)
            {
                diag.Add($"RegionalTownRegistry: {town.DisplayName} is already registered; skipped.");
                return 0;
            }
            if (existing > 0)
            {
                diag.Add($"RegionalTownRegistry: {town.DisplayName} is partly registered " +
                    $"({existing} of {ids.Count} locations exist); refusing a partial apply.");
                return -1;
            }

            // Centre, then gateway (0.5 miles inside the boundary on the road
            // from home), then each business site at its authored local offset.
            float gatewayScale = town.GatewayMilesFromHome /
                (float)Math.Sqrt(town.CentreEastMiles * town.CentreEastMiles + town.CentreNorthMiles * town.CentreNorthMiles);
            var centre = new JourneyLocation(CentreLocationId(town), JourneyLocationKind.Waypoint,
                town.DisplayName + " Town Centre", town.CentreEastMiles, town.CentreNorthMiles);
            var gateway = new JourneyLocation(GatewayLocationId(town), JourneyLocationKind.OffMapGateway,
                town.DisplayName + " Gateway", town.CentreEastMiles * gatewayScale, town.CentreNorthMiles * gatewayScale);
            string problem = model.RegisterLocation(centre);
            if (problem != null) { diag.Add(problem); return -1; }
            problem = model.RegisterLocation(gateway);
            if (problem != null) { diag.Add(problem); return -1; }

            int registered = 2;
            for (int i = 0; i < town.Sites.Length; i++)
            {
                RegionalBusinessSite site = town.Sites[i];
                var location = new JourneyLocation(
                    SiteLocationId(town, site), site.LocationKind, site.DisplayName,
                    town.CentreEastMiles + site.LocalEastMiles, town.CentreNorthMiles + site.LocalNorthMiles)
                {
                    BusinessId = site.BusinessId,
                };
                problem = model.RegisterLocation(location);
                if (problem != null) { diag.Add(problem); return -1; }
                registered++;
            }

            // Edges: home centre -> gateway (the long leg), gateway -> centre
            // (0.5 local), centre -> each site (local street distance).
            problem = model.AddEdge("town-center", GatewayLocationId(town), town.GatewayMilesFromHome, town.RoadName);
            if (problem != null) { diag.Add(problem); return -1; }
            problem = model.AddEdge(GatewayLocationId(town), CentreLocationId(town), 0.5f, town.RoadName);
            if (problem != null) { diag.Add(problem); return -1; }
            for (int i = 0; i < town.Sites.Length; i++)
            {
                RegionalBusinessSite site = town.Sites[i];
                float localMiles = (float)Math.Sqrt(site.LocalEastMiles * site.LocalEastMiles + site.LocalNorthMiles * site.LocalNorthMiles);
                problem = model.AddEdge(CentreLocationId(town), SiteLocationId(town, site), localMiles, site.Street);
                if (problem != null) { diag.Add(problem); return -1; }
            }

            return registered;
        }

        /// <summary>Applies every town in <see cref="All"/>. Returns total locations registered, or -1 on the first refusal.</summary>
        public static int ApplyAll(JourneyModel model, List<string> diag)
        {
            diag = diag ?? new List<string>();
            int total = 0;
            for (int i = 0; i < All.Length; i++)
            {
                int registered = ApplyTown(model, All[i], diag);
                if (registered < 0) return -1;
                total += registered;
            }
            return total;
        }

        /// <summary>Route miles from the home town centre to a town's centre (edge total, not coordinate distance).</summary>
        public static float HomeToCentreMiles(RegionalTownDefinition town) => town.GatewayMilesFromHome + 0.5f;
    }
}
