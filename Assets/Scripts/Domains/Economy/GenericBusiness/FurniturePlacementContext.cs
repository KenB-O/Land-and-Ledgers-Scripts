// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 10b (I-R1/I-R2, expanded campaign 2026-11-20): the plain-data
// placement context the install path scores against.
//
// The install path used to take the first matching scene placement in
// scene order (FurnitureInstallBinding.TryFindFreePlacement, I-01) with
// no wall, door, orientation, or per-kind rule. This context is the
// geometry that selection now reads: the walkable floor polygon (the
// wall edges), the doorway, the activity approach points that must stay
// clear, and the building-local pose of every candidate placement.
//
// Plain data only — no UnityEngine, no MonoBehaviour — so the scoring
// in FurniturePlacementScoring stays harness-compiled and
// harness-tested. The Unity side (BuildingFootprintAuthority.
// TryBuildFurniturePlacementContext) extracts the same fields from the
// live authority and hands them here; the harness tests build the
// context directly from synthetic geometry.
//
// Degradation is explicit, never silent: when the building has no
// authored walkable polygon, HasWallPolygon is false and the wall
// rules are skipped (door + kind-match scoring still apply); when a
// candidate placement reports no pose, it scores neutral on wall/door
// and participates only on its kind match. A missing polygon never
// invents a wall, and a missing pose never invents a position.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>A point on the building-local floor, X/Z metres.</summary>
    public struct FurnitureFloorPoint
    {
        public float X;
        public float Z;

        public FurnitureFloorPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// The building-local pose of one authored scene placement: its
    /// floor position and its yaw (degrees, Unity Y rotation). The
    /// item's footprint comes from the furniture spec row, not from
    /// the pose — a placement is a slot, the item brings the size.
    /// </summary>
    public struct FurniturePlacementPose
    {
        public float X;
        public float Z;
        public float YawDegrees;

        public FurniturePlacementPose(float x, float z, float yawDegrees)
        {
            X = x;
            Z = z;
            YawDegrees = yawDegrees;
        }
    }

    /// <summary>
    /// One activity approach point that installed furniture must not
    /// cover: the point a person stands on to use an activity
    /// (stove, cleaning, dining seat, counter, oven approach). A
    /// candidate whose footprint covers one is refused (I-06: the
    /// reverse of BlocksInteriorPoint, checked at install time
    /// instead of at route time).
    /// </summary>
    public sealed class FurnitureApproachPoint
    {
        public string ActivityLocationId = string.Empty;
        public float X;
        public float Z;

        public FurnitureApproachPoint(string activityLocationId, float x, float z)
        {
            ActivityLocationId = activityLocationId ?? string.Empty;
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// The geometry one install selection scores against. Built from
    /// plain data by the Unity side
    /// (BuildingFootprintAuthority.TryBuildFurniturePlacementContext)
    /// or directly by tests. Never fabricates geometry: every field
    /// is either supplied or explicitly absent (HasWallPolygon /
    /// HasDoor false).
    /// </summary>
    public sealed class FurniturePlacementContext
    {
        /// <summary>
        /// The walkable floor polygon (vertices in order, building-local
        /// X/Z). Its edges ARE the walls for scoring: "against the wall"
        /// is a distance to the nearest edge. Empty when the building
        /// has no authored walkable polygon.
        /// </summary>
        public List<FurnitureFloorPoint> WallPolygon = new List<FurnitureFloorPoint>();

        /// <summary>True when the polygon has at least 3 vertices.</summary>
        public bool HasWallPolygon => WallPolygon != null && WallPolygon.Count >= 3;

        /// <summary>True when a doorway position was supplied.</summary>
        public bool HasDoor;

        /// <summary>Building-local doorway position (the main-entrance indoor point).</summary>
        public float DoorX;

        /// <summary>Building-local doorway position (the main-entrance indoor point).</summary>
        public float DoorZ;

        /// <summary>Activity approach points that must stay clear.</summary>
        public List<FurnitureApproachPoint> ApproachPoints = new List<FurnitureApproachPoint>();

        /// <summary>Building-local pose per placement id.</summary>
        public Dictionary<string, FurniturePlacementPose> PosesByPlacementId =
            new Dictionary<string, FurniturePlacementPose>(StringComparer.Ordinal);

        /// <summary>True when the context carries a pose for this placement.</summary>
        public bool TryGetPose(string placementId, out FurniturePlacementPose pose)
        {
            pose = default;
            if (string.IsNullOrWhiteSpace(placementId) || PosesByPlacementId == null) return false;
            return PosesByPlacementId.TryGetValue(placementId.Trim(), out pose);
        }

        /// <summary>
        /// Builds a context from plain data. Null lists become empty
        /// ones; a polygon with fewer than 3 vertices is kept but
        /// HasWallPolygon stays false, so wall rules degrade instead
        /// of scoring against a degenerate edge.
        /// </summary>
        public static FurniturePlacementContext Build(
            IReadOnlyList<FurnitureFloorPoint> wallPolygon,
            bool hasDoor, float doorX, float doorZ,
            IEnumerable<FurnitureApproachPoint> approachPoints,
            IDictionary<string, FurniturePlacementPose> posesByPlacementId)
        {
            var context = new FurniturePlacementContext();
            if (wallPolygon != null)
                context.WallPolygon.AddRange(wallPolygon);
            context.HasDoor = hasDoor;
            context.DoorX = doorX;
            context.DoorZ = doorZ;
            if (approachPoints != null)
            {
                foreach (FurnitureApproachPoint point in approachPoints)
                {
                    if (point != null) context.ApproachPoints.Add(point);
                }
            }
            if (posesByPlacementId != null)
            {
                foreach (KeyValuePair<string, FurniturePlacementPose> pair in posesByPlacementId)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                        context.PosesByPlacementId[pair.Key.Trim()] = pair.Value;
                }
            }
            return context;
        }
    }
}
