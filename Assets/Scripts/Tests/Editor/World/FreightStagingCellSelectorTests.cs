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
        public void CommercialPremises_RequiresAssignedValidBusinessSite()
        {
            Assert.IsTrue(FreightStagingCellSelector.TryValidateCommercialPremises(
                12, 12, true, out string validReason), validReason);
            Assert.AreEqual(string.Empty, validReason);

            Assert.IsFalse(FreightStagingCellSelector.TryValidateCommercialPremises(
                -1, 7, false, out string missingReason));
            StringAssert.Contains("residential operator property", missingReason);

            Assert.IsFalse(FreightStagingCellSelector.TryValidateCommercialPremises(
                12, 13, true, out string mismatchReason));
            StringAssert.Contains("did not resolve", mismatchReason);

            Assert.IsFalse(FreightStagingCellSelector.TryValidateCommercialPremises(
                12, 12, false, out string invalidReason));
            StringAssert.Contains("not a valid operating site", invalidReason);
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
