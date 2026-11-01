using System;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker D (LL-PLAY-014): the source contract for the Civic
    /// tab. The October 9 complaint was a blocker-style message
    /// ("Civic count hall not built") that told the player nothing
    /// useful. Audit finding: the current panel already computes real
    /// civic state (town hall record, steward, status label, four
    /// contribution effects), but frames the not-built case as a
    /// blocker ("Civic: &lt;name&gt; Not Built", "Status: Not built").
    /// This model reframes it without inventing anything: when no town
    /// hall exists, the honest content is that there is no civic
    /// uplift — all four effects are +0% — and town confidence remains
    /// private-market led. No public institution is invented, no civic
    /// body is claimed to exist, and the "View Civic Site" focus action
    /// is simply unavailable (FocusAvailable false) instead of being a
    /// blocker message.
    ///
    /// Panel change made with this model (GeneralStorePanelController):
    /// the title no longer carries a "Not Built" suffix and the summary
    /// says "no town hall yet" instead of "Not built". All numbers still
    /// come from the civic foundation authority.
    /// </summary>
    [Serializable]
    public sealed class SettlementCivicReadModel
    {
        public string TitleText = string.Empty;
        public string SummaryText = string.Empty;
        public string EffectsText = string.Empty;
        public string OwnerImplicationsText = string.Empty;
        public bool HasCivicAuthority;
        public bool TownHallBuilt;
        public bool FocusAvailable;

        private static string Pct(float value01)
        {
            if (value01 < 0f) value01 = 0f;
            if (value01 > 1f) value01 = 1f;
            int pct = (int)Math.Round(value01 * 100.0);
            return "+" + pct + "%";
        }

        /// <summary>
        /// Builds the Civic tab model from civic-foundation authority
        /// values. All effect inputs are 0-1 fractions.
        /// </summary>
        public static SettlementCivicReadModel Build(
            bool hasCivicAuthority,
            bool townHallBuilt,
            string displayName,
            string stewardDisplayName,
            string civicStatusLabel,
            int plotId,
            int buildingId,
            float confidence01,
            float maturity01,
            float immigrationPull01,
            float landConfidence01,
            string compactSummary)
        {
            string name = string.IsNullOrWhiteSpace(displayName) ? "Civic" : displayName.Trim();
            string steward = string.IsNullOrWhiteSpace(stewardDisplayName) ? "Town Council" : stewardDisplayName.Trim();
            string status = string.IsNullOrWhiteSpace(civicStatusLabel) ? "Civic" : civicStatusLabel.Trim();
            var model = new SettlementCivicReadModel
            {
                HasCivicAuthority = hasCivicAuthority,
                TownHallBuilt = townHallBuilt,
                FocusAvailable = hasCivicAuthority && townHallBuilt,
            };

            if (!hasCivicAuthority)
            {
                model.TitleText = "Civic";
                model.SummaryText = "Civic records unavailable. Town Hall authority has not been established in this save.";
                model.EffectsText = "Confidence +0% | Town maturity +0% | Immigration pull +0% | Land confidence +0%";
                model.OwnerImplicationsText = "Implication: No civic uplift is improving land confidence, migration, or town-center prestige yet.";
                return model;
            }

            model.TitleText = "Civic: " + name;
            model.EffectsText = "Confidence " + Pct(confidence01) + " | Town maturity " + Pct(maturity01)
                + "\nImmigration pull " + Pct(immigrationPull01) + " | Land confidence " + Pct(landConfidence01);
            string implication = string.IsNullOrWhiteSpace(compactSummary)
                ? "Town Hall: no town hall yet | Civic confidence +0% | Maturity +0%"
                : compactSummary.Trim();
            model.OwnerImplicationsText = implication.StartsWith("Implication:", StringComparison.OrdinalIgnoreCase)
                ? implication
                : "Implication: " + implication;

            if (!townHallBuilt)
            {
                model.SummaryText = "Status: no town hall yet | Steward: " + steward + " | " + status
                    + "\nPublic civic anchor inactive; town confidence remains mostly private-market led.";
            }
            else
            {
                model.SummaryText = "Status: Built | Steward: " + steward + " | " + status
                    + "\nPublic civic site: Plot " + plotId.ToString("000") + " | Building " + buildingId.ToString("000");
            }
            return model;
        }
    }
}
