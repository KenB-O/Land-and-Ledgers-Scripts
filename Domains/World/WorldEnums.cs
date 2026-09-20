using System;
using UnityEngine;

namespace LandLedgers.World
{
    public enum WorldGenerationStartupMode
    {
        RandomFreshRuntimeWorld = 0,
        FixedTestingWorld = 1
    }

    public enum WorldInspectableTargetKind
    {
        Unknown = 0,
        Plot = 1,
        Building = 2,
        Residence = 3,
        Business = 4,
        Civic = 5,
        Service = 6
    }

    [DisallowMultipleComponent]
    public sealed class WorldInspectableTarget : MonoBehaviour
    {
        [SerializeField] private WorldInspectableTargetKind kind = WorldInspectableTargetKind.Unknown;
        [SerializeField] private int plotId = -1;
        [SerializeField] private int buildingId = -1;
        [SerializeField] private string serviceId = string.Empty;

        public WorldInspectableTargetKind Kind => kind;
        public int PlotId => plotId;
        public int BuildingId => buildingId;
        public string ServiceId => serviceId ?? string.Empty;

        public void Configure(WorldInspectableTargetKind newKind, int newPlotId, int newBuildingId, string newServiceId = "")
        {
            kind = newKind;
            plotId = newPlotId;
            buildingId = newBuildingId;
            serviceId = newServiceId ?? string.Empty;
        }
    }

    public enum TerrainZone
    {
        Unknown = 0,
        Buildable = 1,
        Steep = 2,
        Blocked = 3
    }

    [Flags]
    public enum CellOccupancy
    {
        None = 0,
        Road = 1 << 0,
        Plot = 1 << 1,
        Building = 1 << 2,
        Anchor = 1 << 3,
        Blocked = 1 << 4
    }

    public enum RoadType
    {
        None = 0,
        MainStreet = 1,
        CrossStreet = 2,
        Spur = 3,
        RegionalExit = 4
    }

    public enum AnchorType
    {
        FrontDoor = 0,
        Service = 1,
        DropOff = 2,

        [Obsolete("Use FrontDoor.")]
        Door = FrontDoor
    }

    public enum BuildingAnchorSide
    {
        Front = 0,
        Back = 1,
        Left = 2,
        Right = 3
    }

    public enum BuildingUseType
    {
        Unspecified = 0,
        Residential = 1,
        Commercial = 2,
        Civic = 3
    }

    public enum BuildingResidentialMode
    {
        None = 0,
        StandaloneHousehold = 1,
        UpperFloorHousehold = 2
    }

    public enum AgriculturalSiteRole
    {
        None = 0,
        CropProductionYard = 1,
        LivestockYard = 2,
        SawmillYard = 3
    }

    public enum PublicSiteRole
    {
        None = 0,
        TownHall = 1,
        Schoolhouse = 2
    }

    public enum PlotZone
    {
        MixedUse = 0,
        Business = 1,
        Residential = 2,
        Agricultural = 3
    }

    public enum GridDirection
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }
}
