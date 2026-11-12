// Unity-only: CoupledTransportMovementController is not in the Roslyn harness
// (sources.txt includes only CoupledTransportProgress.cs and
// TransportPairPlacement.cs from Domains/Economy/Transport; the controller
// pulls in PopulationPathingDirector, HorseWorldView and WagonWorldView).
// This test runs in the Unity Test Runner only. See also
// CoupledTransportProgressTests, which keeps the pure-helper progress tests
// in the harness.
using System.Collections.Generic;
using LandLedgers.Economy.Transport;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CoupledTransportMovementControllerTests
    {
        // Authored value the production caller reads: 2.2 m/s from
        // PathingSettings.logisticsLoadedWagonSpeedMetersPerSecond.
        private const float WagonSpeed = 2.2f;

        [Test]
        public void MovingTransportUsesAuthoredTerrainInsteadOfDebugRoadHeight()
        {
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(32f, 10f, 32f) };
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
                for (int x = 0; x < 33; x++) heights[z, x] = 0.25f;
            data.SetHeights(0, 0, heights);
            var terrain = Terrain.CreateTerrainGameObject(data);
            var transport = new GameObject("Transport ground regression");
            try
            {
                terrain.transform.position = new Vector3(10000f, 12f, 10000f);
                var controller = transport.AddComponent<CoupledTransportMovementController>();
                var method = typeof(CoupledTransportMovementController).GetMethod("GroundPosition",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var grounded = (Vector3)method.Invoke(controller, new object[] { new Vector3(10004f, 0.08f, 10006f) });
                Assert.That(grounded.y, Is.EqualTo(14.5f).Within(0.001f));
                Assert.That(grounded.x, Is.EqualTo(10004f));
                Assert.That(grounded.z, Is.EqualTo(10006f));
            }
            finally
            {
                Object.DestroyImmediate(transport);
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void StartLeg_RefusesWhenAssignedRiderIsUnresolved()
        {
            var host = new GameObject(nameof(StartLeg_RefusesWhenAssignedRiderIsUnresolved));
            try
            {
                CoupledTransportMovementController controller = host.AddComponent<CoupledTransportMovementController>();
                bool started = controller.StartLeg(
                    new List<GridCoord> { new GridCoord(2, 2), new GridCoord(3, 2) },
                    1f, Vector3.zero, WagonSpeed, 1f, 720f,
                    director: null, ethanPersonId: -1,
                    legLabel: "test freight leg", hideOnArrival: true);

                Assert.IsFalse(started, "A freight team must not start moving without the assigned driver authority and identity.");
                Assert.IsFalse(controller.IsLegActive);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
