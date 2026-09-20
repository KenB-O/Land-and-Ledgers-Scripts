using UnityEngine;

namespace LandLedgers.World
{
    public sealed class TownGrid
    {
        private readonly TownCell[] cells;

        public TownGrid(int width, int depth, float cellSizeMeters, Vector3 origin)
        {
            Width = Mathf.Max(1, width);
            Depth = Mathf.Max(1, depth);
            CellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
            Origin = origin;

            cells = new TownCell[Width * Depth];
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = TownCell.CreateDefault();
            }
        }

        public int Width { get; }
        public int Depth { get; }
        public float CellSizeMeters { get; }
        public Vector3 Origin { get; }
        public int CellCount => cells.Length;

        public bool IsInBounds(GridCoord coord)
        {
            return coord.x >= 0 && coord.x < Width && coord.z >= 0 && coord.z < Depth;
        }

        public int IndexOf(GridCoord coord)
        {
            return coord.z * Width + coord.x;
        }

        public ref TownCell GetCellRef(GridCoord coord)
        {
            return ref cells[IndexOf(coord)];
        }

        public TownCell GetCell(GridCoord coord)
        {
            return cells[IndexOf(coord)];
        }

        public void SetCell(GridCoord coord, TownCell cell)
        {
            cells[IndexOf(coord)] = cell;
        }

        public GridCoord WorldToCoord(Vector3 worldPosition)
        {
            int x = Mathf.FloorToInt((worldPosition.x - Origin.x) / CellSizeMeters);
            int z = Mathf.FloorToInt((worldPosition.z - Origin.z) / CellSizeMeters);
            return new GridCoord(x, z);
        }

        public Vector3 CoordToWorldCenter(GridCoord coord, float height = 0f)
        {
            return new Vector3(
                Origin.x + (coord.x + 0.5f) * CellSizeMeters,
                height,
                Origin.z + (coord.z + 0.5f) * CellSizeMeters);
        }

        public bool ContainsRect(GridRect rect)
        {
            return rect.IsValid
                && IsInBounds(new GridCoord(rect.xMin, rect.zMin))
                && IsInBounds(new GridCoord(rect.xMaxInclusive, rect.zMaxInclusive));
        }

        public bool RectCellsAreBuildable(GridRect rect, bool allowPlotCells)
        {
            if (!ContainsRect(rect))
            {
                return false;
            }

            foreach (GridCoord coord in EachCoord(rect))
            {
                TownCell cell = GetCell(coord);
                bool plotOk = allowPlotCells && (cell.occupancy & CellOccupancy.Plot) != 0;
                if (cell.terrainZone != TerrainZone.Buildable || cell.blocked || cell.IsRoad || cell.HasBuilding)
                {
                    return false;
                }

                if (!plotOk && (cell.occupancy & CellOccupancy.Plot) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static GridCoord Offset(GridCoord coord, GridDirection direction, int distance)
        {
            return direction switch
            {
                GridDirection.North => new GridCoord(coord.x, coord.z + distance),
                GridDirection.East => new GridCoord(coord.x + distance, coord.z),
                GridDirection.South => new GridCoord(coord.x, coord.z - distance),
                GridDirection.West => new GridCoord(coord.x - distance, coord.z),
                _ => coord
            };
        }

        public static GridCoord RotateLocalFrontOffset(GridCoord frontCenter, GridDirection frontageDirection, Vector2Int offset)
        {
            return frontageDirection switch
            {
                GridDirection.North => new GridCoord(frontCenter.x + offset.x, frontCenter.z + offset.y),
                GridDirection.East => new GridCoord(frontCenter.x + offset.y, frontCenter.z - offset.x),
                GridDirection.South => new GridCoord(frontCenter.x - offset.x, frontCenter.z - offset.y),
                GridDirection.West => new GridCoord(frontCenter.x - offset.y, frontCenter.z + offset.x),
                _ => frontCenter
            };
        }

        public static System.Collections.Generic.IEnumerable<GridCoord> EachCoord(GridRect rect)
        {
            for (int z = rect.zMin; z <= rect.zMaxInclusive; z++)
            {
                for (int x = rect.xMin; x <= rect.xMaxInclusive; x++)
                {
                    yield return new GridCoord(x, z);
                }
            }
        }
    }
}
