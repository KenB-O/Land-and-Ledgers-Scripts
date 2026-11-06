// Slice S8 (2026-11-05, Objective 25 grid-exit half): bearing-aware
// grid-exit selection.
//
// Harness-tested here is the pure selector seam
// (GridExitSelector.TrySelectEdgeRoad) — bearing-side union, nearest by
// Manhattan distance with the planner's own tie-break, the refusal
// contract (no bearing-side road, zero bearing, out-of-bounds staging),
// determinism across cell-insertion order, and the shipment route-plan
// save round-trip that persists the selected exit cell as visibleEnd.
//
// SCOPE NOTE — read before citing these tests as coverage of the
// production wiring: LogisticsRuntimeManager.cs and SimulationDrivers.cs
// are harness-EXCLUDED, so the LRM method TryPlanFreightExitRoute and
// the registration-time wiring in TryRegisterAcceptedFreightShipment
// are source-reviewed only; no test in this file invokes them. What IS
// harness-tested here is the exact selector seam that wiring calls, plus
// the save seam the wiring persists through.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.World
{
    [TestFixture]
    public sealed class GridExitSelectorTests
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

        private static void SetRoads(TownGrid grid, IEnumerable<GridCoord> coords)
        {
            foreach (GridCoord coord in coords)
            {
                SetRoad(grid, coord.x, coord.z);
            }
        }

        private static void SetAllEdgeRoads(TownGrid grid)
        {
            for (int i = 0; i < 20; i++)
            {
                SetRoad(grid, i, 0);
                SetRoad(grid, i, 19);
                SetRoad(grid, 0, i);
                SetRoad(grid, 19, i);
            }
        }

        private static void SetNorthAndEastEdgeRoads(TownGrid grid)
        {
            for (int i = 0; i < 20; i++)
            {
                SetRoad(grid, i, 19);
                SetRoad(grid, 19, i);
            }
        }

        private static void SetSouthAndWestEdgeRoads(TownGrid grid)
        {
            // West edge (x == 0) and south edge (z == 0) only, excluding the
            // two corners those edges share with the north/east edges —
            // (0, 19) sits on the north edge and (19, 0) on the east edge,
            // so including them would put a road on a bearing side for a
            // northeast haul and defeat the refusal test.
            for (int i = 0; i < 19; i++)
            {
                SetRoad(grid, 0, i);
                SetRoad(grid, i, 0);
            }
        }

        [Test]
        public void Select_NortheastBearing_PicksNearestBearingSideRoad()
        {
            TownGrid grid = FreshGrid();
            SetAllEdgeRoads(grid);
            GridCoord staging = new GridCoord(12, 10);

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 35.00f, out GridCoord exitCoord, out string reason), reason);
            Assert.AreEqual(string.Empty, reason);
            // East edge, Manhattan 7 — nearer than the north-edge road at
            // (12, 19), Manhattan 9.
            Assert.AreEqual(new GridCoord(19, 10), exitCoord);
        }

        [Test]
        public void Select_NortheastBearing_IgnoresNearerWrongSideRoad()
        {
            TownGrid grid = FreshGrid();
            SetSouthAndWestEdgeRoads(grid);
            // A west-edge road adjacent to staging: nearer, but on the wrong
            // side for a northeast gateway.
            SetRoad(grid, 0, 10);
            // The only bearing-side road, far from staging.
            SetRoad(grid, 19, 19);
            GridCoord staging = new GridCoord(10, 10);

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 35.00f, out GridCoord exitCoord, out string reason), reason);
            Assert.AreEqual(new GridCoord(19, 19), exitCoord);
        }

        [Test]
        public void Select_SouthwestBearing_PicksRedfieldSide()
        {
            TownGrid grid = FreshGrid();
            SetAllEdgeRoads(grid);
            GridCoord staging = new GridCoord(10, 10);

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, -70.71f, -70.71f, out GridCoord exitCoord, out string reason), reason);
            Assert.IsTrue(exitCoord.x == 0 || exitCoord.z == 0,
                $"exit {exitCoord.x}, {exitCoord.z} must be on the west or south edge");
            Assert.IsTrue(exitCoord.x != 19 && exitCoord.z != 19,
                $"exit {exitCoord.x}, {exitCoord.z} must not be on the east or north edge");
        }

        [Test]
        public void Select_DueEastBearing_UsesEastEdgeOnly()
        {
            TownGrid grid = FreshGrid();
            SetNorthAndEastEdgeRoads(grid);
            GridCoord staging = new GridCoord(10, 10);

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 0f, out GridCoord exitCoord, out string reason), reason);
            Assert.AreEqual(19, exitCoord.x);
        }

        [Test]
        public void Select_NoRoadOnBearingSide_Refuses()
        {
            TownGrid grid = FreshGrid();
            SetSouthAndWestEdgeRoads(grid);
            GridCoord staging = new GridCoord(12, 10);

            Assert.IsTrue(!GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 35.00f, out GridCoord exitCoord, out string reason));
            Assert.AreEqual(default(GridCoord), exitCoord);
            StringAssert.Contains("northeast", reason);
            StringAssert.Contains("(12, 10)", reason);
        }

        [Test]
        public void Select_ZeroBearing_Refuses()
        {
            TownGrid grid = FreshGrid();
            SetAllEdgeRoads(grid);
            GridCoord staging = new GridCoord(10, 10);

            Assert.IsTrue(!GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 0f, 0f, out GridCoord exitCoord, out string reason));
            Assert.AreEqual(default(GridCoord), exitCoord);
            StringAssert.Contains("bearing", reason);
        }

        [Test]
        public void Select_StagingOnBearingEdge_ReturnsStaging()
        {
            TownGrid grid = FreshGrid();
            SetAllEdgeRoads(grid);
            GridCoord staging = new GridCoord(19, 10);

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 35.00f, out GridCoord exitCoord, out string reason), reason);
            Assert.AreEqual(staging, exitCoord);
        }

        [Test]
        public void Select_IsDeterministicAcrossCellOrder()
        {
            GridCoord[] roads =
            {
                new GridCoord(19, 3), new GridCoord(5, 19), new GridCoord(19, 10),
                new GridCoord(12, 19), new GridCoord(0, 7), new GridCoord(19, 17),
                new GridCoord(8, 0), new GridCoord(3, 19)
            };
            GridCoord staging = new GridCoord(12, 10);

            TownGrid first = FreshGrid();
            SetRoads(first, roads);
            TownGrid second = FreshGrid();
            for (int i = roads.Length - 1; i >= 0; i--)
            {
                SetRoad(second, roads[i].x, roads[i].z);
            }

            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                first, staging, 35.00f, 35.00f, out GridCoord firstExit, out string firstReason), firstReason);
            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                second, staging, 35.00f, 35.00f, out GridCoord secondExit, out string secondReason), secondReason);
            Assert.AreEqual(firstExit, secondExit);
            Assert.AreEqual(new GridCoord(19, 10), firstExit);
        }

        [Test]
        public void ShipmentRoutePlan_RoundTripsExitCell()
        {
            TownGrid grid = FreshGrid();
            SetAllEdgeRoads(grid);
            GridCoord staging = new GridCoord(12, 10);
            Assert.IsTrue(GridExitSelector.TrySelectEdgeRoad(
                grid, staging, 35.00f, 35.00f, out GridCoord exitCoord, out string reason), reason);

            var path = new List<GridCoord>
            {
                staging,
                new GridCoord(15, 10),
                exitCoord
            };
            var shipment = new LogisticsShipmentState
            {
                routePlan = new LogisticsRoutePlan
                {
                    visibleStart = staging,
                    visibleEnd = exitCoord,
                    visiblePath = path
                }
            };

            LogisticsShipmentSaveDto dto = shipment.CaptureSaveDto();
            LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(dto);

            Assert.IsNotNull(restored);
            Assert.AreEqual(exitCoord, restored.RoutePlan.VisibleEnd);
            Assert.AreEqual(3, restored.RoutePlan.VisiblePath.Count);
            for (int i = 0; i < path.Count; i++)
            {
                Assert.AreEqual(path[i], restored.RoutePlan.VisiblePath[i]);
            }
        }
    }
}
