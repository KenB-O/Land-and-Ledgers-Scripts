using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III foundation contracts: legal land identity.
    /// Canon: XXXI Part VI §§6.1-6.8; preserved Rev XIV §3.1A-3.1B; Rev XXVI §§2.1-2.7;
    /// Rev XXIX Part II §§2.1-2.3; Tech XIV Part V §§5.1-5.7; Rev XXIII §1.1 (one authority per fact).
    ///
    /// Distinctions (do not collapse):
    ///   Grid cell  = physical location (LandLedgers.World.GridCoord, 2 m grid — already exists).
    ///   Parcel     = legal unit of land (this file).
    ///   Holding    = economic/administrative grouping of parcels (no title merge).
    ///   Building   = physical improvement (PlacedBuilding).
    ///   Rights     = legally distinct interests/permissions (PropertyInterest).
    /// </summary>

    /// <summary>
    /// Legal land status. Append-only; do not renumber.
    /// Canon: Rev XXIX §2.2 land-status mosaic; XXXI §6.4 public-land interest distinct from title.
    /// </summary>
    public enum LandStatus
    {
        Unknown = 0,
        PrivateTitled = 1,
        PublicDomain = 2,
        Government = 3,
        RailroadCorporate = 4,
        UnpatentedClaim = 5,
        TreatyReservation = 6,
        RestrictedAllotment = 7,
        EstateTrust = 8,
        Disputed = 9,
        SpecialUse = 10,
        CorridorRightOfWay = 11,
    }

    /// <summary>
    /// Persistent legal parcel identity, independent of owner.
    /// A parcel changing owner must NOT create a new parcel identity.
    /// Legal ownership truth lives in TitleAuthority's chain; this record carries
    /// the legal-geography dimension TitleAuthority keys off.
    /// ID convention: "parcel-" + 32 lowercase hex chars (no dashes).
    /// </summary>
    [Serializable]
    public class ParcelRecord
    {
        public string Id;
        public string WorldId;
        /// <summary>Legal footprint as grid-cell ranges. Boundaries need not follow cell edges
        /// exactly; BoundaryNote records any rounding. Never silently gain/lose area.</summary>
        public List<GridRect> Footprint = new List<GridRect>();
        public string BoundaryNote;
        public string SurveyDescription;
        public double AreaSquareMeters;
        public string ParcelUse;
        public string Jurisdiction;
        public LandStatus Status = LandStatus.Unknown;
        /// <summary>Subdivision lineage: ids of parcels this one was split/merged from. Empty for original surveys.</summary>
        public List<string> ParentParcelIds = new List<string>();
        public string RootSurveyId;
        public List<string> BuildingIds = new List<string>();
        public string AccessNotes;
        /// <summary>True once superseded by subdivision/consolidation. History is preserved, never deleted.</summary>
        public bool IsRetired;
        /// <summary>Optional migration link to the physical town plot record. Not a second ownership truth.</summary>
        public string PlotId;
    }

    /// <summary>
    /// Economic/administrative grouping of parcels (farm, ranch, townsite company,
    /// railroad portfolio, commercial block, estate). Grouping only — the Holding
    /// never merges legal titles and ownership may differ per parcel.
    /// ID convention: "holding-" + 32 lowercase hex chars.
    /// </summary>
    [Serializable]
    public class HoldingRecord
    {
        public string Id;
        public string Name;
        public List<string> ParcelIds = new List<string>();
        public string Notes;
    }
}
