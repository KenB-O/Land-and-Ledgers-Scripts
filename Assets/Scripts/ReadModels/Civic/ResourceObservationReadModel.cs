using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-015): the source contract for the
    /// Resources tab. The October 9 complaint showed resource text such
    /// as "coal, two districts and 72%" with no explanation. Audit
    /// finding: the underlying records (MineralDistrictRecord) carry
    /// ONLY generated 0-1 indices — strength, suitability, freight
    /// pressure, settlement pressure — plus a centre/bounds and notes.
    /// There is no surveyed reserve, no tonnage, no claim, and no
    /// mineral-rights record anywhere in the snapshot. The old panel
    /// text printed bare percentages ("strongest 72%"), which read as
    /// reserve quantities. This model therefore labels every number
    /// for what it is and states what is NOT known:
    ///
    /// - Strength is a GENERATED PROSPECTIVITY index (0-1), not a
    ///   surveyed reserve and not tonnes. No reserve field exists on
    ///   this model, so a panel bound to it cannot display one.
    /// - Spatial context is the district record itself (district id
    ///   and kind); the panel must not invent coordinates or claims.
    /// - Accessibility is expressed only through the recorded freight
    ///   and settlement pressures.
    /// - Ownership/mineral rights: NOT ESTABLISHED. No claim, owner,
    ///   or right is shown, because none is recorded.
    /// - Uncertainty is explicit on every row and in the banner:
    ///   prospectivity is generated, proto-sites are prospective (not
    ///   proven), and "no district generated" is shown when a kind has
    ///   no district instead of a zero that reads as "depleted".
    /// - Units: percentages are 0-1 index values rendered as %, never
    ///   tonnes, ounces, or any physical unit.
    /// </summary>
    [Serializable]
    public sealed class ResourceObservationReadModel
    {
        [Serializable]
        public sealed class DistrictRow
        {
            public string KindLabel = string.Empty;
            public int DistrictCount;
            public bool HasDistrict;
            /// <summary>Generated prospectivity index, 0-1. Never a reserve.</summary>
            public float StrongestStrength01;
            public string StrengthText = string.Empty;
            public string FreightPressureText = string.Empty;
            public string SettlementPressureText = string.Empty;
            public string SpatialNote = string.Empty;
            public string OwnershipNote = string.Empty;
            public string UncertaintyNote = string.Empty;
        }

        [Serializable]
        public sealed class ProtoSiteRow
        {
            public string Label = string.Empty;
            public string StrengthText = string.Empty;
            public string RelevanceText = string.Empty;
        }

        public bool HasMeaningfulOpportunity;
        public string SummaryText = string.Empty;
        public List<DistrictRow> Districts = new List<DistrictRow>();
        public List<ProtoSiteRow> ProtoSites = new List<ProtoSiteRow>();
        /// <summary>Always present: what these numbers are not.</summary>
        public string UncertaintyBanner = "Prospectivity is a generated 0-1 index, not a surveyed reserve. "
            + "No tonnage, claim, or mineral right is shown because none is recorded.";

        public sealed class DistrictInput
        {
            public string KindLabel = string.Empty;
            public int DistrictCount;
            public bool HasDistrict;
            public float StrongestStrength01;
            public float FreightPressure01;
            public float SettlementPressure01;
        }

        public sealed class ProtoSiteInput
        {
            public string Label = string.Empty;
            public float SiteStrength01;
            public float CampRelevance01;
            public float BoomtownRelevance01;
        }

        private static string Pct(float value01)
        {
            if (value01 < 0f) value01 = 0f;
            if (value01 > 1f) value01 = 1f;
            return ((int)Math.Round(value01 * 100.0)) + "%";
        }

        public static ResourceObservationReadModel Build(
            bool hasMeaningfulOpportunity,
            IReadOnlyList<DistrictInput> districts,
            IReadOnlyList<ProtoSiteInput> protoSites)
        {
            var model = new ResourceObservationReadModel { HasMeaningfulOpportunity = hasMeaningfulOpportunity };
            if (!hasMeaningfulOpportunity)
            {
                model.SummaryText = "Regional resources unavailable. Generate the town shell to inspect mineral districts and remote proto-sites.";
                return model;
            }

            model.SummaryText = "Regional resources: district prospectivity below is generated, not surveyed. "
                + "Percentages are 0-1 prospectivity/pressure indices, not physical quantities.";
            if (districts != null)
            {
                foreach (DistrictInput input in districts)
                {
                    if (input == null) continue;
                    var row = new DistrictRow
                    {
                        KindLabel = input.KindLabel ?? string.Empty,
                        DistrictCount = Math.Max(0, input.DistrictCount),
                        HasDistrict = input.HasDistrict,
                        StrongestStrength01 = input.StrongestStrength01,
                        OwnershipNote = "Mineral rights: not established — no claim or reserve is shown.",
                        UncertaintyNote = "Generated prospectivity, not a surveyed reserve.",
                    };
                    if (!input.HasDistrict)
                    {
                        row.StrengthText = "no district generated";
                        row.FreightPressureText = "—";
                        row.SettlementPressureText = "—";
                        row.SpatialNote = "No district of this kind was generated.";
                    }
                    else
                    {
                        row.StrengthText = "strongest " + Pct(input.StrongestStrength01) + " generated prospectivity";
                        row.FreightPressureText = "freight " + Pct(input.FreightPressure01);
                        row.SettlementPressureText = "settlement " + Pct(input.SettlementPressure01);
                        row.SpatialNote = "District record held by the world snapshot; panel shows district, not a claim.";
                    }
                    model.Districts.Add(row);
                }
            }
            if (protoSites != null)
            {
                foreach (ProtoSiteInput input in protoSites)
                {
                    if (input == null) continue;
                    model.ProtoSites.Add(new ProtoSiteRow
                    {
                        Label = (input.Label ?? string.Empty) + " (prospective site — not a proven reserve)",
                        StrengthText = "site " + Pct(input.SiteStrength01) + " generated prospectivity",
                        RelevanceText = "camp " + Pct(input.CampRelevance01) + " | boomtown " + Pct(input.BoomtownRelevance01),
                    });
                }
            }
            return model;
        }
    }
}
