// Slice F1 (2026-11-06, Objectives 16-17): freight staging-cell
// selection tests.
//
// Harness-tested here is the pure selector seam
// (FreightStagingCellSelector.TrySelectStagingCell) — anchor wins over
// plot, anchor-not-road falls back to plot, refusal names both
// candidates, null candidates refuse, determinism.
//
// SCOPE NOTE: LogisticsRuntimeManager.cs and SimulationDrivers.cs are
// harness-EXCLUDED, so the LRM method
// TryPlanFreightExitRouteForCarrier and the registration-time wiring in
// TryRegisterAcceptedFreightShipment are source-reviewed only; no test in
// this file invokes them. What IS harness-tested here is the exact
// selector seam that wiring calls.
using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Pathing;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.World
{
    [TestFixture]
    public sealed class FreightStagingCellSelectorTests
    {
        private static TownGrid FreshGrid()
        {
            return new TownGrid(20, 20, 2f, Vector3.zero);
        }

        private static void SetRoad(TownGrid grid, int x, int z)
        {
            GridCoord coord = new GridCoord(x, z);
            TownCell cell = grid.GetCell(coord);
            cell.occupancy |= CellOccupancy.Road;
            grid.SetCell(coord, cell);
        }

        private static Func<GridCoord, bool> UsableRoadCell(TownGrid grid)
        {
            return coord => grid.IsInBounds(coord) && grid.GetCell(coord).IsRoad;
        }

        [Test]
        public void ExitPathRecovery_RequiresAtLeastOneRealEdge()
        {
            Assert.IsFalse(FreightStagingCellSelector.HasUsableExitPath(null));
            Assert.IsFalse(FreightStagingCellSelector.HasUsableExitPath(Array.Empty<GridCoord>()));
            Assert.IsFalse(FreightStagingCellSelector.HasUsableExitPath(new[] { new GridCoord(1, 1) }));
            Assert.IsTrue(FreightStagingCellSelector.HasUsableExitPath(
                new[] { new GridCoord(1, 1), new GridCoord(2, 1) }));
        }

        [Test]
        public void PhysicalStagingLocation_DoesNotRequireCommercialAssignment()
        {
            Assert.IsTrue(FreightStagingCellSelector.TrySelectPhysicalStagingBuilding(
                12, 12, 7, 7, out int selected, out string source, out string validReason), validReason);
            Assert.AreEqual(string.Empty, validReason);
            Assert.AreEqual(12, selected);
            Assert.AreEqual("carrier-location", source);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectPhysicalStagingBuilding(
                -1, -1, 7, 7, out selected, out source, out string homeReason), homeReason);
            Assert.AreEqual(7, selected);
            Assert.AreEqual("operator-home-location", source);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectPhysicalStagingBuilding(
                12, -1, 7, 7, out selected, out source, out string unresolvedAssignedReason), unresolvedAssignedReason);
            Assert.AreEqual(7, selected, "an unresolved carrier assignment falls back to the operator's actual home location");
            Assert.AreEqual("operator-home-location", source);

            Assert.IsFalse(FreightStagingCellSelector.TrySelectPhysicalStagingBuilding(
                -1, -1, -1, -1, out selected, out source, out string missingReason));
            StringAssert.Contains("No assigned carrier building or operator home", missingReason);
        }

        [Test]
        public void VehicleGround_RejectsRoadBuildingOutOfParcelAndBlockedCells()
        {
            Assert.IsTrue(FreightStagingCellSelector.IsClearVehicleGroundCell(
                true, true, 4, 4, false, false, false, false, true, 2f, 8f, out string validReason), validReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCell(
                true, true, 4, 4, true, false, false, false, true, 0f, 8f, out string roadReason));
            StringAssert.Contains("road", roadReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCell(
                true, true, 4, 4, false, true, false, false, true, 0f, 8f, out string buildingReason));
            StringAssert.Contains("building footprint", buildingReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCell(
                true, false, 4, 4, false, false, false, false, true, 0f, 8f, out string parcelReason));
            StringAssert.Contains("assigned parcel", parcelReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCell(
                true, true, 4, 4, false, false, true, false, true, 0f, 8f, out string blockedReason));
            StringAssert.Contains("blocked ground", blockedReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCell(
                false, false, -1, 4, false, false, false, false, false, 0f, 8f, out string boundsReason));
            StringAssert.Contains("town grid", boundsReason);
        }

        [Test]
        public void VehicleGroundAnywhere_DoesNotRequireParcelButKeepsPhysicalSafetyChecks()
        {
            Assert.IsTrue(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, false, false, false, false, true, 3f, 8f, out string openGroundReason), openGroundReason);

            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, true, false, false, false, true, 0f, 8f, out string roadReason));
            StringAssert.Contains("road", roadReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, false, true, false, false, true, 0f, 8f, out string buildingReason));
            StringAssert.Contains("building footprint", buildingReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, false, false, false, true, true, 0f, 8f, out string anchorReason));
            StringAssert.Contains("access anchor", anchorReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, false, false, false, false, false, 0f, 8f, out string terrainReason));
            StringAssert.Contains("terrain", terrainReason);
            Assert.IsFalse(FreightStagingCellSelector.IsClearVehicleGroundCellAnywhere(
                true, false, false, false, false, true, 12f, 8f, out string slopeReason));
            StringAssert.Contains("steep", slopeReason);
        }

        [Test]
        public void RoadsideGroundCandidates_AreNearestFirstBoundedAndParcelIndependent()
        {
            TownGrid grid = FreshGrid();
            SetRoad(grid, 2, 2);
            GridCoord first = new(3, 2);
            GridCoord second = new(2, 3);
            var candidates = FreightStagingCellSelector.CollectNearbyRoadsideGroundCandidates(
                grid, new GridCoord(2, 2), 8, coord => coord.Equals(first) || coord.Equals(second));

            Assert.AreEqual(2, candidates.Count);
            Assert.AreEqual(first, candidates[0].GroundCoord);
            Assert.AreEqual(new GridCoord(2, 2), candidates[0].AdjacentRoadCoord);
            Assert.AreEqual(new GridCoord(1, 0), candidates[0].OutwardStep);
            Assert.AreEqual(second, candidates[1].GroundCoord);

            var limited = FreightStagingCellSelector.CollectNearbyRoadsideGroundCandidates(
                grid, new GridCoord(2, 2), 1, coord => coord.Equals(first) || coord.Equals(second));
            Assert.AreEqual(1, limited.Count);
            Assert.AreEqual(first, limited[0].GroundCoord);
        }

        [Test]
        public void RoadsideGroundCandidates_RejectGroundWithoutAnAdjacentRoad()
        {
            TownGrid grid = FreshGrid();
            Assert.IsEmpty(FreightStagingCellSelector.CollectNearbyRoadsideGroundCandidates(
                grid, new GridCoord(2, 2), 4, coord => coord.Equals(new GridCoord(3, 2))));
        }

        [Test]
        public void RoadsideVehicleSetback_ClearsProjectedWagonWidthFromRoadCell()
        {
            // One adjacent 2 m clear cell provides only a 2 m center offset.
            // A 1.5 m projected wagon radius therefore needs an extra 0.6 m
            // to clear the road-cell half-width plus the 0.1 m safety margin.
            Assert.AreEqual(0.6f,
                FreightStagingCellSelector.RoadsideVehicleSetbackMeters(2f, 2f, 1.5f, 0.1f), 0.0001f);
            Assert.AreEqual(0f,
                FreightStagingCellSelector.RoadsideVehicleSetbackMeters(2f, 2f, 0.7f, 0.1f), 0.0001f);
            Assert.IsTrue(float.IsPositiveInfinity(
                FreightStagingCellSelector.RoadsideVehicleSetbackMeters(2f, 0f, 1f)));
        }

        [Test]
        public void RoadsideGroundCandidates_CanSearchPastTheFormerFortyEightCandidateLimit()
        {
            TownGrid grid = new TownGrid(120, 8, 2f, Vector3.zero);
            for (int x = 2; x < 112; x += 2) SetRoad(grid, x, 3);

            // The first 48 safe apron cells are intentionally considered
            // unusable by the caller's physical vehicle check. The selector
            // can continue farther along the actual road network and expose
            // later candidates without inventing a location.
            IReadOnlyList<FreightStagingCellSelector.RoadsideGroundCandidate> candidates =
                FreightStagingCellSelector.CollectNearbyRoadsideGroundCandidates(
                    grid, new GridCoord(2, 3), grid.Width * grid.Depth,
                    coord => coord.z == 2 && coord.x >= 1 && coord.x <= 112);

            Assert.Greater(candidates.Count, 48);
            Assert.AreEqual(new GridCoord(2, 2), candidates[0].GroundCoord);
            Assert.IsTrue(candidates.Any(candidate => candidate.GroundCoord.x > 48),
                "the full finite grid search should retain valid later roadside candidates");
        }

        [Test]
        public void Select_AnchorRoadCell_WinsOverPlot()
        {
            TownGrid grid = FreshGrid();
            SetRoad(grid, 12, 10);
            SetRoad(grid, 13, 10);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(12, 10), new GridCoord(13, 10), UsableRoadCell(grid),
                out GridCoord cell, out string source, out string reason), reason);
            Assert.AreEqual(string.Empty, reason);
            Assert.AreEqual(new GridCoord(12, 10), cell);
            Assert.AreEqual("anchor", source);
        }

        [Test]
        public void Select_AnchorNotRoad_FallsBackToPlot()
        {
            TownGrid grid = FreshGrid();
            // Anchor cell (12, 10) is in-bounds but not a road cell; the
            // plot cell is.
            SetRoad(grid, 13, 10);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(12, 10), new GridCoord(13, 10), UsableRoadCell(grid),
                out GridCoord cell, out string source, out string reason), reason);
            Assert.AreEqual(new GridCoord(13, 10), cell);
            Assert.AreEqual("plot", source);
        }

        [Test]
        public void Select_AnchorOutOfBounds_FallsBackToPlot()
        {
            TownGrid grid = FreshGrid();
            SetRoad(grid, 13, 10);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(25, 10), new GridCoord(13, 10), UsableRoadCell(grid),
                out GridCoord cell, out string source, out string reason), reason);
            Assert.AreEqual(new GridCoord(13, 10), cell);
            Assert.AreEqual("plot", source);
        }

        [Test]
        public void Select_NoRoadCandidate_RefusesNamingBoth()
        {
            TownGrid grid = FreshGrid();
            // Anchor in-bounds but not a road cell; plot out of bounds.
            SetRoad(grid, 5, 5);

            Assert.IsTrue(!FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(12, 10), new GridCoord(25, 10), UsableRoadCell(grid),
                out GridCoord cell, out string source, out string reason));
            Assert.AreEqual(default(GridCoord), cell);
            Assert.AreEqual(string.Empty, source);
            StringAssert.Contains("(12, 10)", reason);
            StringAssert.Contains("(25, 10)", reason);
        }

        [Test]
        public void Select_NullCandidates_Refuse()
        {
            TownGrid grid = FreshGrid();
            SetRoad(grid, 12, 10);

            Assert.IsTrue(!FreightStagingCellSelector.TrySelectStagingCell(
                null, null, UsableRoadCell(grid),
                out GridCoord cell, out string source, out string reason));
            Assert.AreEqual(default(GridCoord), cell);
            StringAssert.Contains("absent", reason);
        }

        [Test]
        public void Select_IsDeterministic()
        {
            TownGrid grid = FreshGrid();
            SetRoad(grid, 12, 10);
            SetRoad(grid, 13, 10);

            Assert.IsTrue(FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(12, 10), new GridCoord(13, 10), UsableRoadCell(grid),
                out GridCoord firstCell, out string firstSource, out string firstReason), firstReason);
            Assert.IsTrue(FreightStagingCellSelector.TrySelectStagingCell(
                new GridCoord(12, 10), new GridCoord(13, 10), UsableRoadCell(grid),
                out GridCoord secondCell, out string secondSource, out string secondReason), secondReason);
            Assert.AreEqual(firstCell, secondCell);
            Assert.AreEqual(firstSource, secondSource);
        }
    }
}
