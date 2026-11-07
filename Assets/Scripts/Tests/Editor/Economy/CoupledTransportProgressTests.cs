// Slice F4 (2026-11-06, Objective 23): pure coupled-transport progress.
//
// Fixture path mirrors GridExitSelectorTests (small TownGrid, 2 m cells,
// origin zero). The speeds and separation are the authored values the
// production caller reads — 2.2 m/s from
// PathingSettings.logisticsLoadedWagonSpeedMetersPerSecond and 2.3736 m
// from TransportPairPlacement.PairSeparationMetres — passed in, never
// re-declared here.
using System.Collections.Generic;
using LandLedgers.Economy.Transport;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CoupledTransportProgressTests
    {
        private const float CellSize = 2f;
        private const float WagonSpeed = 2.2f;
        private static readonly Vector3 Origin = Vector3.zero;

        private static List<GridCoord> StraightPath(int cells)
        {
            var path = new List<GridCoord>();
            for (int i = 0; i < cells; i++) path.Add(new GridCoord(2 + i, 2));
            return path;
        }

        private static void AssertVector(Vector3 expected, Vector3 actual, float eps, string what)
        {
            Assert.IsTrue(Mathf.Abs(expected.x - actual.x) < eps
                && Mathf.Abs(expected.y - actual.y) < eps
                && Mathf.Abs(expected.z - actual.z) < eps,
                $"{what}: expected {expected}, got {actual}");
        }

        [Test]
        public void Advance_MovesAtAuthoredSpeed()
        {
            List<GridCoord> path = StraightPath(6);

            // The planner's duration: visibleTravelCells (Path.Count - 1)
            // * cellMeters / wagonSpeed * penalty (1.0 on an all-road path).
            float plannerSeconds = (path.Count - 1) * CellSize / WagonSpeed;
            Assert.IsTrue(CoupledTransportProgress.TryTotalLength(path, CellSize, Origin, out float totalMetres));
            float helperSeconds = totalMetres / WagonSpeed;
            Assert.IsTrue(Mathf.Abs(plannerSeconds - helperSeconds) < 1e-4f,
                $"distance ÷ 2.2 m/s must equal the planner's visibleTravelGameSeconds: planner {plannerSeconds}, helper {helperSeconds}");

            // Halfway: 5 m travelled of 10 m, 5 m remaining.
            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, 5f, CellSize, Origin,
                out int segment, out Vector3 position, out Vector3 heading, out float remaining));
            Assert.AreEqual(2, segment, "5 m along a 2 m-cell path is on segment 2 (cells 4→5)");
            Assert.IsTrue(Mathf.Abs(remaining - 5f) < 1e-4f, $"remaining must be 5 m, got {remaining}");
            AssertVector(new Vector3(5f * CellSize, 0f, 2.5f * CellSize), position, 1e-4f, "position at 5 m");
            AssertVector(Vector3.right, heading, 1e-4f, "heading on a straight east path");
        }

        [Test]
        public void Advance_WagonTrailsHorseByPairSeparation()
        {
            List<GridCoord> path = StraightPath(8);
            float separation = TransportPairPlacement.PairSeparationMetres;

            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, 6f, CellSize, Origin,
                out _, out Vector3 horse, out _, out _));
            Assert.IsTrue(CoupledTransportProgress.TrailingPosition(path, 6f, separation, CellSize, Origin, out Vector3 wagon));
            float gap = Vector3.Distance(horse, wagon);
            Assert.IsTrue(Mathf.Abs(gap - separation) < 1e-4f,
                $"the wagon must trail exactly one pair separation behind the horse along the path: gap {gap}, separation {separation}");

            // Early in the leg the wagon is still clamped at staging.
            Assert.IsTrue(CoupledTransportProgress.TrailingPosition(path, 1f, separation, CellSize, Origin, out Vector3 earlyWagon));
            AssertVector(new Vector3(2.5f * CellSize, 0f, 2.5f * CellSize), earlyWagon, 1e-4f,
                "before the horse is a full separation ahead, the wagon waits at the first waypoint");
        }

        [Test]
        public void Advance_HeadingFollowsPathDirection()
        {
            var path = new List<GridCoord>
            {
                new GridCoord(2, 2), new GridCoord(3, 2), new GridCoord(4, 2), new GridCoord(5, 2),
                new GridCoord(5, 3), new GridCoord(5, 4), new GridCoord(5, 5),
            };

            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, 1f, CellSize, Origin,
                out _, out _, out Vector3 eastHeading, out _));
            AssertVector(Vector3.right, eastHeading, 1e-4f, "heading before the corner is east");

            // 6 m in is past the corner (corner at 6 m: 3 cells × 2 m).
            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, 7f, CellSize, Origin,
                out int segment, out _, out Vector3 northHeading, out _));
            Assert.AreEqual(3, segment, "7 m along the L-path is on segment 3 (cells (5,2)→(5,3))");
            AssertVector(Vector3.forward, northHeading, 1e-4f, "heading after the corner is north");
        }

        [Test]
        public void Advance_EmptyPath_Refuses()
        {
            Assert.IsFalse(CoupledTransportProgress.TryAdvance(null, 0f, CellSize, Origin,
                out _, out _, out _, out _), "a null path must refuse");
            Assert.IsFalse(CoupledTransportProgress.TryAdvance(new List<GridCoord>(), 0f, CellSize, Origin,
                out _, out _, out _, out _), "an empty path must refuse");
            Assert.IsFalse(CoupledTransportProgress.TryAdvance(
                new List<GridCoord> { new GridCoord(2, 2) }, 0f, CellSize, Origin,
                out _, out _, out _, out _), "a single-cell path has no direction to travel and must refuse");
            Assert.IsFalse(CoupledTransportProgress.TrailingPosition(null, 0f, 1f, CellSize, Origin, out _),
                "TrailingPosition must refuse a null path too");
            Assert.IsFalse(CoupledTransportProgress.TryAdvance(StraightPath(4), 0f, 0f, Origin,
                out _, out _, out _, out _), "a non-positive cell size must refuse");
        }

        [Test]
        public void Advance_ArrivalAtFinalWaypoint_Stops()
        {
            List<GridCoord> path = StraightPath(6);
            Assert.IsTrue(CoupledTransportProgress.TryTotalLength(path, CellSize, Origin, out float totalMetres));
            Assert.IsTrue(Mathf.Abs(totalMetres - 10f) < 1e-4f, $"6 cells × 2 m is 10 m of travel, got {totalMetres}");

            // Overshooting the end clamps at the final waypoint, never past it.
            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, totalMetres + 50f, CellSize, Origin,
                out int segment, out Vector3 position, out _, out float remaining));
            Assert.AreEqual(path.Count - 2, segment, "arrival is on the final segment");
            AssertVector(new Vector3(7.5f * CellSize, 0f, 2.5f * CellSize), position, 1e-4f,
                "arrival position is the final waypoint centre");
            Assert.IsTrue(Mathf.Abs(remaining) < 1e-4f, $"remaining must be 0 on arrival, got {remaining}");

            Assert.IsTrue(CoupledTransportProgress.TryAdvance(path, totalMetres, CellSize, Origin,
                out _, out Vector3 exact, out _, out float exactRemaining));
            AssertVector(new Vector3(7.5f * CellSize, 0f, 2.5f * CellSize), exact, 1e-4f,
                "exact arrival lands on the final waypoint centre");
            Assert.IsTrue(Mathf.Abs(exactRemaining) < 1e-4f, "exact arrival has 0 remaining");
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
