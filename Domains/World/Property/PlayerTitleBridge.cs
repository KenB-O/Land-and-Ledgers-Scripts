using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// P5: bridges the live acquisition paths to the T2F title chain. The
    /// acquisition manager's purchase flows previously flipped plot.playerOwned
    /// without ever touching the TitleAuthority — and nothing in production ever
    /// instantiated the TitleAuthority at all — so the legal chain of title never
    /// recorded the player's purchases or sales (dead end P5-TITLE).
    ///
    /// Upstream provenance: the seller is whoever the live listing named as the
    /// owner (listing.ownerDisplayName); the player is recorded as "Player", the
    /// same holder name the acquisition flows already use. The seller's own chain
    /// root is NOT invented: on first sighting the parcel is registered under the
    /// seller with an unstated basis and an explicit note, which T3D analysis will
    /// (correctly) surface as a chain break rather than a clean title.
    /// </summary>
    public static class PlayerTitleBridge
    {
        /// <summary>The holder name the acquisition flows already use for the player.</summary>
        public const string PlayerHolderName = "Player";

        /// <summary>Canonical title-parcel key for a town plot id.</summary>
        public static string ParcelKeyForPlot(int plotId)
        {
            return $"plot-{plotId}";
        }

        /// <summary>
        /// Records a player acquisition: registers the parcel under the seller on
        /// first sighting, then transfers to the player on a Purchase basis with the
        /// transaction id as the conveyance instrument. Returns null on success,
        /// or an error string (the economic purchase already happened; a recording
        /// problem is logged, never silently dropped).
        /// </summary>
        public static string RecordPlayerAcquisition(
            TitleAuthority titles, int plotId, string sellerName,
            string instrumentId, int priceCents, int dayIndex, List<string> diag)
        {
            if (titles == null)
                return "PlayerTitleBridge.RecordPlayerAcquisition: no title authority given.";
            string parcelId = ParcelKeyForPlot(plotId);
            if (titles.CurrentHolder(parcelId) == null)
            {
                string seller = string.IsNullOrWhiteSpace(sellerName)
                    ? "Unrecorded seller"
                    : sellerName.Trim();
                titles.RegisterParcel(parcelId, $"Town plot {plotId:000}", 0f, seller,
                    TitleBasis.Unspecified, dayIndex, diag);
                if (diag != null)
                    diag.Add($"PlayerTitleBridge: '{parcelId}' first observed held by '{seller}' — " +
                        "prior chain root not recorded (T3D will flag the unstated basis).");
            }
            string instrument = string.IsNullOrWhiteSpace(instrumentId)
                ? $"player-acquisition-plot-{plotId}-day-{dayIndex}"
                : instrumentId.Trim();
            string note = priceCents > 0
                ? $"player purchase for {priceCents}c"
                : "player acquisition";
            return titles.TransferTitle(null, parcelId, PlayerHolderName,
                TitleBasis.Purchase, instrument, dayIndex, note, diag);
        }

        /// <summary>
        /// Records a player disposal: transfers the parcel to the named buyer on a
        /// Purchase basis. Anonymous grantees are refused (upstream provenance).
        /// </summary>
        public static string RecordPlayerDisposal(
            TitleAuthority titles, int plotId, string buyerName,
            string instrumentId, int priceCents, int dayIndex, List<string> diag)
        {
            if (titles == null)
                return "PlayerTitleBridge.RecordPlayerDisposal: no title authority given.";
            if (string.IsNullOrWhiteSpace(buyerName))
                return "PlayerTitleBridge.RecordPlayerDisposal: the buyer must be named — no anonymous grantees.";
            string parcelId = ParcelKeyForPlot(plotId);
            if (titles.CurrentHolder(parcelId) == null)
            {
                // Disposing of a parcel the chain never saw (e.g. a pre-P5 save):
                // register the player as the first observed holder rather than
                // inventing a prior owner.
                titles.RegisterParcel(parcelId, $"Town plot {plotId:000}", 0f, PlayerHolderName,
                    TitleBasis.Unspecified, dayIndex, diag);
            }
            string instrument = string.IsNullOrWhiteSpace(instrumentId)
                ? $"player-disposal-plot-{plotId}-day-{dayIndex}"
                : instrumentId.Trim();
            string note = priceCents > 0
                ? $"player sale for {priceCents}c"
                : "player disposal";
            return titles.TransferTitle(null, parcelId, buyerName.Trim(),
                TitleBasis.Purchase, instrument, dayIndex, note, diag);
        }
    }
}
