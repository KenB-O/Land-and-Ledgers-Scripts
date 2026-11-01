using System.Collections.Generic;
using LandLedgers.Economy.Recruitment;

namespace LandLedgers.Economy.Businesses.Newspaper
{
    /// <summary>
    /// NX-3A: adapts a real <see cref="NewspaperRuntime"/> to the recruitment
    /// service's <see cref="INewspaperAdMarket"/> — this is the wire that
    /// closes T2B's upstream hole. Ads are placed as real transactions in the
    /// newspaper's placement book; the recruitment effort's cost is the
    /// market's actual rate.
    /// </summary>
    public sealed class NewspaperAdMarket : INewspaperAdMarket
    {
        private readonly NewspaperRuntime runtime;
        private readonly string paperName;

        public NewspaperAdMarket(NewspaperRuntime runtime, string paperName)
        {
            this.runtime = runtime;
            this.paperName = string.IsNullOrWhiteSpace(paperName) ? "the newspaper" : paperName;
        }

        public string MarketName => paperName;

        public int RateForSizeCents(string sizeClass)
        {
            NewspaperAdSize size = ParseSize(sizeClass);
            if (size == NewspaperAdSize.Unspecified) return -1;
            return NewspaperRuntime.RateForSize(size);
        }

        public string PlaceAd(string advertiserBusinessId, string adText, string sizeClass,
            int insertions, int dayIndex, List<string> diag)
        {
            if (runtime == null)
            {
                if (diag != null) diag.Add("NewspaperAdMarket: no newspaper runtime — ad refused.");
                return null;
            }
            NewspaperAdSize size = ParseSize(sizeClass);
            if (size == NewspaperAdSize.Unspecified)
            {
                if (diag != null) diag.Add($"NewspaperAdMarket: size '{sizeClass}' not offered — ad refused.");
                return null;
            }
            AdPlacement placement = runtime.PlaceAd(
                advertiserBusinessId, advertiserBusinessId, adText, size, insertions, dayIndex, diag);
            return placement != null ? placement.PlacementId : null;
        }

        private static NewspaperAdSize ParseSize(string sizeClass)
        {
            if (string.IsNullOrWhiteSpace(sizeClass)) return NewspaperAdSize.Unspecified;
            switch (sizeClass.Trim().ToLowerInvariant())
            {
                case "notice": return NewspaperAdSize.Notice;
                case "business-card": case "businesscard": return NewspaperAdSize.BusinessCard;
                case "quarter-column": case "quartercolumn": return NewspaperAdSize.QuarterColumn;
                case "half-column": case "halfcolumn": return NewspaperAdSize.HalfColumn;
                default: return NewspaperAdSize.Unspecified;
            }
        }
    }
}
