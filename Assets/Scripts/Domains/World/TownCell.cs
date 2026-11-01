using System;

namespace LandLedgers.World
{
    [Serializable]
    public struct TownCell
    {
        public TerrainZone terrainZone;
        public CellOccupancy occupancy;
        public RoadType roadType;
        public int plotId;
        public int buildingId;
        public float height;
        public float slopeDegrees;
        public bool blocked;

        public bool IsRoad => (occupancy & CellOccupancy.Road) != 0;
        public bool HasBuilding => (occupancy & CellOccupancy.Building) != 0;
        public bool IsBuildable => terrainZone == TerrainZone.Buildable && !blocked && !IsRoad && !HasBuilding;

        public static TownCell CreateDefault()
        {
            return new TownCell
            {
                terrainZone = TerrainZone.Unknown,
                occupancy = CellOccupancy.None,
                roadType = RoadType.None,
                plotId = -1,
                buildingId = -1,
                height = 0f,
                slopeDegrees = 0f,
                blocked = false
            };
        }
    }
}
