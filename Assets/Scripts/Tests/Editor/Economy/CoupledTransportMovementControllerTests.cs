// Unity-only: CoupledTransportMovementController is not in the Roslyn harness
// (sources.txt includes only CoupledTransportProgress.cs and
// TransportPairPlacement.cs from Domains/Economy/Transport; the controller
// pulls in PopulationPathingDirector, HorseWorldView and WagonWorldView).
// This test runs in the Unity Test Runner only. See also
// CoupledTransportProgressTests, which keeps the pure-helper progress tests
// in the harness.
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Transport;
using LandLedgers.Time;
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

        [TestCase(1f, 3)]
        [TestCase(10f, 2)]
        [TestCase(500f, 1)]
        public void ActiveRoute_AdvancesFromSuppliedSimulationSecondsWithoutViews(
            float nominalSpeedMultiplier, int frameCount)
        {
            // Different clock multipliers provide different processed game
            // deltas. The transport consumes that delta directly and never
            // multiplies real frame time by the selected speed again.
            const float totalGameSeconds = 3f;
            var host = new GameObject(nameof(ActiveRoute_AdvancesFromSuppliedSimulationSecondsWithoutViews));
            try
            {
                CoupledTransportMovementController controller = host.AddComponent<CoupledTransportMovementController>();
                SetPrivate(controller, "path", new List<GridCoord>
                {
                    new GridCoord(0, 0), new GridCoord(1, 0),
                    new GridCoord(2, 0), new GridCoord(3, 0)
                });
                SetPrivate(controller, "cellSizeMetres", 1f);
                SetPrivate(controller, "gridOrigin", new Vector3(50000f, 0f, 50000f));
                SetPrivate(controller, "wagonSpeedMetresPerSecond", 1f);
                SetPrivate(controller, "pairSeparationMetres", 1f);
                SetPrivate(controller, "turnSpeedDegreesPerSecond", 720f);
                SetPrivate(controller, "legActive", true);
                SetPrivate(controller, "hideOnArrival", false);
                SetPrivate(controller, "authoritativeHorsePosition", new Vector3(50000.5f, 0f, 50000.5f));
                SetPrivate(controller, "horseView", null);
                SetPrivate(controller, "wagonView", null);

                float frameGameSeconds = totalGameSeconds / frameCount;
                SimulationSpeed speed = nominalSpeedMultiplier >= 500f ? SimulationSpeed.Speed500x
                    : nominalSpeedMultiplier >= 10f ? SimulationSpeed.Speed10x : SimulationSpeed.Speed1x;
                MethodInfo advance = typeof(CoupledTransportMovementController).GetMethod(
                    "OnSimulationTimeAdvanced", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(advance);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    var context = new SimulationFrameContext(
                        new SimulationDate(0, 0, 1, 1, 1, 1, 1897), speed,
                        nominalSpeedMultiplier, 0.016f, frameGameSeconds, 0f, 0f);
                    advance.Invoke(controller, new object[] { context });
                }

                Assert.IsFalse(controller.IsLegActive,
                    $"A valid route must arrive after {totalGameSeconds} simulated seconds at nominal {nominalSpeedMultiplier}x even with no render views.");
                Assert.That((float)GetPrivate(controller, "distanceTravelledMetres"),
                    Is.EqualTo(totalGameSeconds).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected movement state field '{name}'.");
            field.SetValue(target, value);
        }

        private static object GetPrivate(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected movement state field '{name}'.");
            return field.GetValue(target);
        }
    }
}
