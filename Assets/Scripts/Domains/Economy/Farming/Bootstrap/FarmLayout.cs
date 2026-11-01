using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Farming.Bootstrap
{
    /// <summary>Logical zone kinds on a farmstead.</summary>
    public enum FarmZoneKind
    {
        Unspecified = 0,
        Farmstead = 1,    // house + yard core
        BarnInterior = 2, // milking / stabling space
        Pasture = 3,
        Field = 4,        // hay / crop
        Yard = 5,         // working yard, wagon access
        CoopArea = 6,
    }

    /// <summary>
    /// One logical zone of the farm. Zones are logical, not rendered: they give
    /// every building, plot, and working area a stable id that the future
    /// location/journey model will consume to compute travel legs, route times,
    /// and 1:1 world distances (Tech X §12.1).
    /// </summary>
    [Serializable]
    public sealed class FarmZone
    {
        public string ZoneId = string.Empty; // e.g. "dairy-barn-interior"
        public FarmZoneKind Kind;
        public string Name = string.Empty;
        public string LinkedDefinitionName = string.Empty; // building/plot name from the definition
        public string Notes = string.Empty;

        public FarmZone() { }

        public FarmZone(string zoneId, FarmZoneKind kind, string name, string linkedDefinitionName = "")
        {
            ZoneId = zoneId ?? string.Empty;
            Kind = kind;
            Name = name ?? string.Empty;
            LinkedDefinitionName = linkedDefinitionName ?? string.Empty;
        }
    }

    /// <summary>
    /// FVS-1: the farm's spatial data model. Built from the <see cref="FarmDefinition"/>
    /// at bootstrap; every building and plot becomes an addressable zone, and
    /// adjacency hints record which zones touch (barn ↔ yard ↔ pasture) so the
    /// future location/journey model can compute real travel legs instead of
    /// teleporting goods or labor.
    ///
    /// HANDOFF (documented, not built): the journey/location model consumes
    /// FarmLayout — zone ids, kinds, and adjacency — to route deliveries
    /// (FVS-3) and labor movement. Until it exists, zones are logical and no
    /// travel time is simulated; nothing may assume zero travel cost either.
    /// </summary>
    [Serializable]
    public sealed class FarmLayout
    {
        public string FarmId = string.Empty;
        public List<FarmZone> Zones = new List<FarmZone>();

        /// <summary>
        /// Adjacency as unordered zone-id pairs: "a|b" means a and b touch.
        /// The journey model will expand these into travel legs.
        /// </summary>
        public List<string> AdjacentPairs = new List<string>();

        public FarmLayout() { }

        public FarmLayout(string farmId)
        {
            FarmId = farmId ?? string.Empty;
        }

        public void AddZone(FarmZone zone)
        {
            if (zone == null || string.IsNullOrWhiteSpace(zone.ZoneId)) return;
            Zones.Add(zone);
        }

        public void AddAdjacency(string zoneIdA, string zoneIdB)
        {
            if (string.IsNullOrWhiteSpace(zoneIdA) || string.IsNullOrWhiteSpace(zoneIdB)) return;
            if (string.Equals(zoneIdA, zoneIdB, StringComparison.Ordinal)) return;
            string pair = string.CompareOrdinal(zoneIdA, zoneIdB) < 0
                ? zoneIdA + "|" + zoneIdB
                : zoneIdB + "|" + zoneIdA;
            if (!AdjacentPairs.Contains(pair))
            {
                AdjacentPairs.Add(pair);
            }
        }

        public bool AreAdjacent(string zoneIdA, string zoneIdB)
        {
            if (string.IsNullOrWhiteSpace(zoneIdA) || string.IsNullOrWhiteSpace(zoneIdB)) return false;
            string pair = string.CompareOrdinal(zoneIdA, zoneIdB) < 0
                ? zoneIdA + "|" + zoneIdB
                : zoneIdB + "|" + zoneIdA;
            return AdjacentPairs.Contains(pair);
        }

        public FarmZone GetZone(string zoneId)
        {
            if (Zones == null) return null;
            foreach (var z in Zones)
            {
                if (z != null && string.Equals(z.ZoneId, zoneId, StringComparison.Ordinal))
                {
                    return z;
                }
            }
            return null;
        }

        /// <summary>
        /// Derives zones from a validated definition: one zone per building and
        /// per plot, plus a farmstead core and a working yard. Adjacency wires
        /// barn ↔ yard, pasture ↔ yard, coop ↔ farmstead.
        /// </summary>
        public static FarmLayout BuildFromDefinition(FarmDefinition definition)
        {
            var layout = new FarmLayout(definition != null ? definition.FarmId : string.Empty);
            if (definition == null) return layout;

            string Slug(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "unnamed";
                var chars = new List<char>(s.Length);
                foreach (char c in s.Trim().ToLowerInvariant())
                {
                    if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) chars.Add(c);
                    else if (chars.Count > 0 && chars[chars.Count - 1] != '-') chars.Add('-');
                }
                string slug = new string(chars.ToArray()).Trim('-');
                return string.IsNullOrEmpty(slug) ? "unnamed" : slug;
            }

            var yardZone = new FarmZone("yard", FarmZoneKind.Yard, "Working yard", "yard");
            layout.AddZone(yardZone);

            bool hasFarmhouse = false;
            if (definition.Buildings != null)
            {
                foreach (var b in definition.Buildings)
                {
                    if (b == null) continue;
                    string zoneId = "bldg-" + Slug(b.Name);
                    FarmZoneKind kind = FarmZoneKind.Farmstead;
                    switch (b.Kind)
                    {
                        case FarmBuildingKind.DairyBarn:
                        case FarmBuildingKind.Barn:
                            kind = FarmZoneKind.BarnInterior;
                            break;
                        case FarmBuildingKind.Coop:
                            kind = FarmZoneKind.CoopArea;
                            break;
                        case FarmBuildingKind.Farmhouse:
                            kind = FarmZoneKind.Farmstead;
                            hasFarmhouse = true;
                            break;
                        default:
                            kind = FarmZoneKind.Yard;
                            break;
                    }
                    layout.AddZone(new FarmZone(zoneId, kind, b.Name, b.Name));
                    layout.AddAdjacency(zoneId, "yard");
                    if (kind == FarmZoneKind.CoopArea && hasFarmhouse)
                    {
                        layout.AddAdjacency(zoneId, "bldg-" + Slug("farmhouse"));
                    }
                }
            }

            if (definition.Plots != null)
            {
                foreach (var p in definition.Plots)
                {
                    if (p == null) continue;
                    string zoneId = "plot-" + Slug(p.Name);
                    FarmZoneKind kind = p.Kind == FarmPlotKind.Pasture ? FarmZoneKind.Pasture : FarmZoneKind.Field;
                    layout.AddZone(new FarmZone(zoneId, kind, p.Name, p.Name));
                    layout.AddAdjacency(zoneId, "yard");
                }
            }

            return layout;
        }
    }
}
