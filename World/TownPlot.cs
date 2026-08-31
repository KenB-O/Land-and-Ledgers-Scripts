using System;
using UnityEngine;

namespace LandLedgers.World
{
    [Serializable]
    public sealed class TownPlot
    {
        public int id;
        public PlotZone zone;
        public GridRect bounds;
        public GridRect candidateFootprint;
        public Vector2Int siteSizeCells;
        public Vector2Int intendedBuildingFootprintCells;
        public AgriculturalSiteRole agriculturalSiteRole = AgriculturalSiteRole.None;
        public PublicSiteRole publicSiteRole = PublicSiteRole.None;
        public int frontageCells;
        public int depthCells;
        public GridDirection roadFrontageDirection;
        public GridCoord roadAccessCell;
        public int buildingId = -1;
        public bool reservedForLandSale;
        public bool playerOwned;
    }
}
