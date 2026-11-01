using LandLedgers.Orchestration.Systems;
using LandLedgers.World;
using LandLedgers.World.Property;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Orchestration
{
    public sealed class SimulationDriversSceneBindingTests
    {
        [Test]
        public void Rebind_AttachesDiscoveredTownWorldToHubParcelAuthorities()
        {
            GameObject hubObject = new("Scene Binding Hub");
            GameObject worldObject = new("Scene Binding Town World");
            GameObject driversObject = new("Scene Binding Drivers");
            try
            {
                SimulationSystemsHub hub = hubObject.AddComponent<SimulationSystemsHub>();
                worldObject.AddComponent<TownWorldController>();
                SimulationDrivers drivers = driversObject.AddComponent<SimulationDrivers>();

                drivers.Rebind();

                Assert.IsInstanceOf<TownWorldParcelBuildingIndex>(
                    hub.ParcelSubdivisions.BuildingIndex,
                    "A freshly rebound driver must attach the real scene world to parcel subdivision checks.");
            }
            finally
            {
                Object.DestroyImmediate(driversObject);
                Object.DestroyImmediate(worldObject);
                Object.DestroyImmediate(hubObject);
            }
        }
    }
}
