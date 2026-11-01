using System;
using UnityEngine;

namespace LandLedgers.World
{
    [Serializable]
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int x;
        public readonly int z;

        public GridCoord(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public static GridCoord operator +(GridCoord a, GridCoord b)
        {
            return new GridCoord(a.x + b.x, a.z + b.z);
        }

        public static GridCoord operator -(GridCoord a, GridCoord b)
        {
            return new GridCoord(a.x - b.x, a.z - b.z);
        }

        public bool Equals(GridCoord other)
        {
            return x == other.x && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, z);
        }

        public override string ToString()
        {
            return $"({x}, {z})";
        }

        public Vector2Int ToVector2Int()
        {
            return new Vector2Int(x, z);
        }
    }
}
