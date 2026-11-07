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
