using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Economy.Farming.Crops
{
    /// <summary>
    /// W5C: one hop in a seed lot's upstream provenance chain. The full chain
    /// runs breeder/grower → merchant → farm: every seed lot traces to a real
    /// originator, never a vague "supplier".
    /// </summary>
    [Serializable]
    public sealed class SeedProvenanceHop
    {
        public string Role = string.Empty;       // SeedProvenanceRoles.Breeder/Grower/Merchant/Farm
        public string DisplayName = string.Empty; // person, farm, or business name
        public string BusinessId = string.Empty;  // business id when the hop is a business
        public int DayIndex = -1;                 // day the hop entered the chain

        public SeedProvenanceHop() { }

        public SeedProvenanceHop(string role, string displayName, string businessId, int dayIndex)
        {
            Role = role ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            BusinessId = businessId ?? string.Empty;
            DayIndex = dayIndex;
        }
    }

    /// <summary>W5C: the roles a provenance hop can play, in chain order.</summary>
    public static class SeedProvenanceRoles
    {
        public const string Breeder = "breeder";   // originated the variety (e.g. David Fife)
        public const string Grower = "grower";     // grew this seed lot (a farm)
        public const string Merchant = "merchant"; // handled/retailed it (seed house, general store)
        public const string Farm = "farm";         // the planting farm (terminal hop)
    }

    /// <summary>
    /// W5C: the structured upstream provenance chain of a seed lot. Hops are
    /// appended in order as the lot changes hands; the chain is never rewritten.
    /// Serializable for save/load; rendered for planting records.
    /// </summary>
    [Serializable]
    public sealed class SeedProvenanceChain
    {
        public List<SeedProvenanceHop> Hops = new List<SeedProvenanceHop>();

        public SeedProvenanceChain() { }

        /// <summary>Appends a hop. Returns this for chaining.</summary>
        public SeedProvenanceChain AppendHop(string role, string displayName, string businessId, int dayIndex)
        {
            Hops.Add(new SeedProvenanceHop(role, displayName, businessId, dayIndex));
            return this;
        }

        /// <summary>Deep copy — a sold lot carries its own chain onward.</summary>
        public SeedProvenanceChain Copy()
        {
            var copy = new SeedProvenanceChain();
            foreach (var hop in Hops)
            {
                if (hop == null) continue;
                copy.Hops.Add(new SeedProvenanceHop(hop.Role, hop.DisplayName, hop.BusinessId, hop.DayIndex));
            }
            return copy;
        }

        public bool HasHops => Hops != null && Hops.Count > 0;

        /// <summary>The first hop — the ultimate originator of this seed.</summary>
        public SeedProvenanceHop Originator => HasHops ? Hops[0] : null;

        /// <summary>Renders the full chain, e.g. "breeder: David Fife → merchant: Steele, Briggs Seed Co., Toronto (day 300) → farm-1".</summary>
        public string Render()
        {
            if (!HasHops) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < Hops.Count; i++)
            {
                var hop = Hops[i];
                if (hop == null) continue;
                if (sb.Length > 0) sb.Append(" → ");
                sb.Append(hop.Role).Append(": ").Append(hop.DisplayName);
                if (hop.DayIndex >= 0) sb.Append(" (day ").Append(hop.DayIndex).Append(')');
            }
            return sb.ToString();
        }
    }
}
