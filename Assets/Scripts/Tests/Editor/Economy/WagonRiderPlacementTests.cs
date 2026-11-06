// Slice R1 (2026-11-06, Objective 22): harness tests for the pure
// board gate + dismount cell centre (WagonRiderPlacement).
//
// SCOPE NOTE — read before citing these tests as coverage of the
// production wiring: PopulationPathingDirector.cs, the coupled
// controller, and the presentation controller are harness-EXCLUDED, so
// the SetParent calls, the localRotation = Euler(0, 90, 0)
// composition, the Animator CrossFade, and the hide/show ordering are
// source-reviewed only; no test in this file invokes them. What IS
// harness-tested here is the exact gate logic those edits rely on
// (WagonRiderPlacement.TryAllowBoarding) and the dismount ground
// position (WagonRiderPlacement.CellCenter), the F2/F3 precedent.
using System.Collections.Generic;
using LandLedgers.Economy.Transport;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class WagonRiderPlacementTests
    {
        private const float CellSize = 2f;
        private static readonly Vector3 Origin = new Vector3(0f, 0.08f, 0f);

        private static void AssertVector(Vector3 expected, Vector3 actual, float eps, string what)
        {
            Assert.IsTrue(Mathf.Abs(expected.x - actual.x) < eps
                && Mathf.Abs(expected.y - actual.y) < eps
                && Mathf.Abs(expected.z - actual.z) < eps,
                $"{what}: expected {expected}, got {actual}");
        }

        [Test]
        public void Boarding_AllowsWhenCloseIdleAndVisible()
        {
            Assert.IsTrue(WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(11f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string reason));
            Assert.IsTrue(string.IsNullOrEmpty(reason), $"allowed boarding must carry no refusal reason, got '{reason}'");
        }

        [Test]
        public void Boarding_RefusesBeyondFiveMetres_NamesReason()
        {
            Assert.IsTrue(!WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(16f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string reason));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reason), "a refusal must name its reason");
            Assert.IsTrue(reason.Contains("6.00"), $"the reason must name the 6 m distance, got '{reason}'");
        }

        [Test]
        public void Boarding_RefusesAtExactlyFiveMetresPlusEpsilon()
        {
            // The tolerance is inclusive: a rider exactly 5 m away
            // boards; 5.01 m refuses.
            Assert.IsTrue(WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(15f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out _));
            Assert.IsTrue(!WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: false,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(15.01f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string reason));
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reason), "a refusal must name its reason");
        }

        [Test]
        public void Boarding_RefusesWhenRouteActive()
        {
            Assert.IsTrue(!WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: true, isHidden: false,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(10f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string reason));
            Assert.IsTrue(reason.Contains("route"), $"the reason must name the active route, got '{reason}'");
        }

        [Test]
        public void Boarding_RefusesWhenHidden()
        {
            Assert.IsTrue(!WagonRiderPlacement.TryAllowBoarding(
                isRouteActive: false, isHidden: true,
                seatWorldPosition: new Vector3(10f, 1.25f, 5f),
                riderWorldPosition: new Vector3(10f, 0f, 5f),
                boardToleranceMetres: WagonRiderPlacement.BoardToleranceMetres,
                out string reason));
            Assert.IsTrue(reason.Contains("hidden"), $"the reason must name the hidden rider, got '{reason}'");
        }

        [Test]
        public void CellCenter_MatchesPathEndSample()
        {
            // Fixture path mirrors CoupledTransportProgressTests
            // (2 m cells, origin at the 0.08 m presentation height).
            var path = new List<GridCoord>();
            for (int i = 0; i < 6; i++) path.Add(new GridCoord(2 + i, 2));
            Assert.IsTrue(CoupledTransportProgress.TryTotalLength(path, CellSize, Origin, out float totalMetres));
            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, totalMetres, CellSize, Origin,
                out _, out Vector3 pathEnd, out _, out float remaining));
            Assert.IsTrue(remaining <= 0f, $"advancing the full length must leave 0 remaining, got {remaining}");
            Vector3 centre = WagonRiderPlacement.CellCenter(path[path.Count - 1], CellSize, Origin);
            AssertVector(pathEnd, centre, 1e-4f, "dismount cell centre must equal the path-end sample");
        }

        [Test]
        public void CellCenter_UsesPresentationHeight()
        {
            Vector3 centre = WagonRiderPlacement.CellCenter(new GridCoord(3, 4), CellSize, Origin);
            Assert.IsTrue(Mathf.Abs(centre.y - Origin.y) < 1e-6f,
                $"cell centre y must be the presentation height {Origin.y}, got {centre.y}");
            AssertVector(new Vector3(7f, 0.08f, 9f), centre, 1e-4f, "cell centre for (3, 4)");
        }
    }
}
