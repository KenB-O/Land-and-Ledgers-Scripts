using System;
using UnityEngine;

namespace LandLedgers.World
{
    [Serializable]
    public readonly struct GridRect
    {
        public readonly int xMin;
        public readonly int zMin;
        public readonly int width;
        public readonly int depth;

        public GridRect(int xMin, int zMin, int width, int depth)
        {
            this.xMin = xMin;
            this.zMin = zMin;
            this.width = Mathf.Max(0, width);
            this.depth = Mathf.Max(0, depth);
        }

        public int xMaxInclusive => xMin + width - 1;
        public int zMaxInclusive => zMin + depth - 1;
        public int Area => width * depth;
        public bool IsValid => width > 0 && depth > 0;
        public GridCoord Center => new GridCoord(xMin + width / 2, zMin + depth / 2);

        public bool Contains(GridCoord coord)
        {
            return coord.x >= xMin
                && coord.x <= xMaxInclusive
                && coord.z >= zMin
                && coord.z <= zMaxInclusive;
        }

        public bool Overlaps(GridRect other)
        {
            return IsValid
                && other.IsValid
                && xMin <= other.xMaxInclusive
                && xMaxInclusive >= other.xMin
                && zMin <= other.zMaxInclusive
                && zMaxInclusive >= other.zMin;
        }

        public override string ToString()
        {
            return $"[{xMin},{zMin} {width}x{depth}]";
        }
    }
}
