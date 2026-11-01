using System.Collections.Generic;
using LandLedgers.World.Property;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Pass IX WS6 (A1-06): the dispute clue pass for quick diligence.
    /// Before this pass, diligence never consulted dispute claims at all
    /// (PropertyDisputes.cs had zero production references). This pass
    /// flags that Open/Contested claims are recorded on the parcel under
    /// diligence — and deliberately does NOT leak claimant identity:
    /// diligence learns that a dispute exists and how certain it is,
    /// never who raised it.
    /// </summary>
    public static class QuickDiligenceService
    {
        /// <summary>
        /// Builds the dispute clue line for a parcel, or an empty string
        /// when no Open/Contested claim is recorded. The line contains
        /// counts and certainty only — no claimant names or ids.
        /// </summary>
        public static string BuildDisputeClue(string parcelId, PropertyDisputeService disputes)
        {
            if (disputes == null || string.IsNullOrWhiteSpace(parcelId)) return string.Empty;
            List<DisputeClaim> open = disputes.OpenOrContestedForParcel(parcelId);
            if (open.Count == 0) return string.Empty;
            int verified = 0;
            int contested = 0;
            foreach (DisputeClaim claim in open)
            {
                if (claim.Certainty == KnowledgeCertainty.Verified) verified++;
                if (claim.Status == DisputeClaimStatus.Contested) contested++;
            }
            return $"Dispute clue: {open.Count} open/contested dispute claim(s) recorded on this parcel " +
                $"({verified} chain-verified, {contested} contested) — formal title diligence recommended before commitment.";
        }

        /// <summary>True when the parcel carries any Open/Contested dispute claim.</summary>
        public static bool HasDisputeClue(string parcelId, PropertyDisputeService disputes)
        {
            return !string.IsNullOrEmpty(BuildDisputeClue(parcelId, disputes));
        }
    }
}
