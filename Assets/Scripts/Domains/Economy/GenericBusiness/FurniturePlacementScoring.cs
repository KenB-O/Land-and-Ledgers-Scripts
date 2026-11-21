// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Lane 10b (I-R1–I-R8, expanded campaign 2026-11-20): furniture
// placement rules — the §4.1 placement table from Agent I, as code.
//
// The install path scored nothing (I-01): TryFindFreePlacement took
// the first matching scene placement. This module is the scoring the
// install path now uses, plus the runtime port of the wall-edge
// geometry (I-R2), the installed-placement validation (I-R3), the
// bakery stage → approach-point map (I-R4), the retail counter
// service-point lookup (I-R5/I-R6), the counter rule as fixture
// validation (I-R7, route (b)), and capability-preferring selection
// (I-R8).
//
// All numbers are proposed values under the Nov 15 delegation
// doctrine (implementation decisions delegated to development
// management; historical realism first), not Canon — Canon and the
// owner decisions are silent on furniture placement (I-15). The
// physical facts they rest on are in code: the fixture/movable
// classification (FurnishingClassification), the shipped-form
// dimensions (FurnitureItemSpecs), the wall-edge geometry (the editor
// alignment tool), and the authored bakery layout (oven + work
// counter against the -Z wall, approach points 1.2 m in front).
//
// Scoring, per candidate (total 0–100, ties break by scene order):
//   wall   0–60: Fixture kinds (shelf, stove, counter) are REFUSED
//                 when their wall gap exceeds WallAdjacentGapMeters;
//                 wall-preferred kinds (cabinet, bed) decay from 60
//                 to 0 over WallPreferredFalloffMeters; free kinds
//                 score 0 on wall.
//   door   0–25: shelf/bed/stove prefer far from the door, crates
//                 prefer near it, everything else scores neutral.
//   match  0/15: a kind-matched placement beats a generic
//                 `furniture.unspecified` slot, so generic slots stay
//                 free for kinds with no matched slot (I-18).
// Blocking is a refusal, not a low score: a footprint covering an
// activity approach point or the doorway is refused outright (I-06).
// Door distance is the straight-line local distance, the cheap proxy
// Agent I specified; interior-route distance is the refinement a
// run can justify, not a second geometry to invent here.
//
// Unity-free (plain state only) so the rules are harness-compiled
// and harness-tested. The Unity side extracts geometry into a
// FurniturePlacementContext and calls in here.

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;

namespace LandLedgers.Economy.GenericBusiness
{
    /// <summary>Lane 10b: the §4.1 placement table, as pure functions.</summary>
    public static class FurniturePlacementScoring
    {
        /// <summary>Back edge (or counter short end) within this gap of a wall edge counts as against the wall. The editor alignment places an item depth/2 + 0.05 m inside the wall, so a wall-placed item measures ~0.05 m; 0.30 m allows authoring slack.</summary>
        public const float WallAdjacentGapMeters = 0.30f;

        /// <summary>Wall-preferred kinds decay from full wall score at the wall to zero at this gap.</summary>
        public const float WallPreferredFalloffMeters = 3.0f;

        /// <summary>Door bias saturates at this straight-line distance from the door.</summary>
        public const float DoorReferenceMeters = 6.0f;

        /// <summary>Clearance added around a candidate footprint for the blocking test (the BuildingEquipmentPlacement default).</summary>
        public const float BlockingClearanceMeters = 0.20f;

        public const float WallScoreMax = 60f;
        public const float DoorScoreMax = 25f;
        public const float KindMatchBonus = 15f;
        public const float NeutralDoorScore = 12.5f;

        /// <summary>Fallback footprint for a kind with no spec row (never an invented fact — a 0.50 m square is the neutral assumption the scorer documents, not a dimension).</summary>
        public const float DefaultDimensionMeters = 0.50f;

        /// <summary>Approach point for the bakery oven: heat-oven / load / unload happen here (I-07 — authored in config, previously unreferenced in code).</summary>
        public const string BakeryOvenApproachActivityId = "bakery-oven-approach";

        /// <summary>Approach point for the bakery work surface: prepare happens here.</summary>
        public const string BakeryWorkSurfaceActivityId = "bakery-work-surface";

        /// <summary>Retail service point for the General Store (the only Counter-type point before this lane).</summary>
        public const string GeneralStoreCounterActivityId = "general-store-counter";

        /// <summary>Retail service point for the bakery (I-R6 — authored by this lane; no retail counter fixture or service point existed before it).</summary>
        public const string BakeryRetailCounterActivityId = "bakery-retail-counter";

        /// <summary>Retail service point for the fuel dealer (M-08 — authored by this lane; the dealer had a queue and clerk candidates but no counter point).</summary>
        public const string FuelDealerCounterActivityId = "fuel-dealer-counter";

        /// <summary>The baking skill id the capability-preferring baker selection reads (BakeryBreadCatalog.BakingSkillId).</summary>
        public const string BakingSkillId = "baking";

        /// <summary>The carpentry skill id the capability-preferring carpenter selection reads.</summary>
        public const string CarpentrySkillId = "carpentry";

        // ----- I-R2: the wall-edge computation, ported to runtime -----

        /// <summary>
        /// I-R2: the nearest-wall-edge computation from the editor's
        /// TryAlignPlacementToNearestWall (BuildingFootprintAuthority),
        /// ported to runtime on plain data. Takes the walkable floor
        /// polygon, finds the nearest wall edge to a query point,
        /// computes the inward normal from the polygon's signed area,
        /// checks the edge is at least the equipment's wall-facing
        /// width + 0.1 m, and returns a position half the equipment
        /// depth + 0.05 m inside the wall with the correct yaw. The
        /// editor button keeps calling this through its own wrapper;
        /// the install scorer and the validators read the same
        /// geometry, so editor and runtime can no longer disagree
        /// about where a wall is. Identical results to the editor
        /// method for the same polygon, query, width and depth.
        /// </summary>
        public static bool TryAlignToNearestWall(
            IReadOnlyList<FurnitureFloorPoint> polygon,
            float queryX, float queryZ,
            float widthM, float depthM,
            out float alignedX, out float alignedZ, out float yawDegrees,
            out string error)
        {
            alignedX = queryX;
            alignedZ = queryZ;
            yawDegrees = 0f;
            error = "No valid walkable polygon or furniture footprint is available.";
            if (polygon == null || polygon.Count < 3 || widthM <= 0f || depthM <= 0f) return false;

            float nearestDistanceSq = float.PositiveInfinity;
            FurnitureFloorPoint bestStart = default, bestEnd = default;
            for (int i = 0; i < polygon.Count; i++)
            {
                FurnitureFloorPoint a = polygon[i];
                FurnitureFloorPoint b = polygon[(i + 1) % polygon.Count];
                float edgeX = b.X - a.X;
                float edgeZ = b.Z - a.Z;
                float edgeLenSq = edgeX * edgeX + edgeZ * edgeZ;
                if (edgeLenSq < 0.01f) continue;
                float t = Clamp01(((queryX - a.X) * edgeX + (queryZ - a.Z) * edgeZ) / edgeLenSq);
                float closestX = a.X + edgeX * t;
                float closestZ = a.Z + edgeZ * t;
                float dx = queryX - closestX;
                float dz = queryZ - closestZ;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq >= nearestDistanceSq) continue;
                nearestDistanceSq = distanceSq;
                bestStart = a;
                bestEnd = b;
            }

            float tangentX = bestEnd.X - bestStart.X;
            float tangentZ = bestEnd.Z - bestStart.Z;
            float tangentLen = (float)Math.Sqrt(tangentX * tangentX + tangentZ * tangentZ);
            if (tangentLen < 0.001f)
            {
                error = "The walkable floor polygon has no usable wall edge.";
                return false;
            }
            tangentX /= tangentLen;
            tangentZ /= tangentLen;
            if (tangentX * tangentX + tangentZ * tangentZ < 0.5f)
            {
                error = "The walkable floor polygon has no usable wall edge.";
                return false;
            }

            float signedArea = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                FurnitureFloorPoint a = polygon[i];
                FurnitureFloorPoint b = polygon[(i + 1) % polygon.Count];
                signedArea += a.X * b.Z - b.X * a.Z;
            }
            float inwardX = signedArea >= 0f ? -tangentZ : tangentZ;
            float inwardZ = signedArea >= 0f ? tangentX : -tangentX;

            yawDegrees = (float)(Math.Atan2(-tangentZ, tangentX) * (180.0 / Math.PI));

            float edgeLength = (float)Math.Sqrt(
                (bestEnd.X - bestStart.X) * (bestEnd.X - bestStart.X)
                + (bestEnd.Z - bestStart.Z) * (bestEnd.Z - bestStart.Z));
            float halfWidth = widthM * 0.5f;
            if (edgeLength < widthM + 0.1f)
            {
                error = $"The nearest floor edge is {edgeLength:F2} m long, shorter than the equipment's {widthM:F2} m wall-facing width.";
                return false;
            }

            float along = Clamp((queryX - bestStart.X) * tangentX + (queryZ - bestStart.Z) * tangentZ,
                halfWidth + 0.05f, edgeLength - halfWidth - 0.05f);
            float projectedX = bestStart.X + tangentX * along;
            float projectedZ = bestStart.Z + tangentZ * along;
            alignedX = projectedX + inwardX * (depthM * 0.5f + 0.05f);
            alignedZ = projectedZ + inwardZ * (depthM * 0.5f + 0.05f);
            error = string.Empty;
            return true;
        }

        // ----- I-R1: placement scoring -----

        /// <summary>
        /// I-R1: scores one candidate placement for one furniture kind.
        /// Returns false with a refusal when the candidate is refused
        /// outright (a Fixture kind away from a wall, or a footprint
        /// covering an activity approach point or the doorway). The
        /// caller supplies the item's shipped-form footprint
        /// (LengthM = wall-facing width at yaw 0, WidthM = depth);
        /// unknown kinds use DefaultDimensionMeters — a documented
        /// neutral assumption, never a dimension. kindMatched is true
        /// when the placement's definition names this kind (a generic
        /// `furniture.unspecified` slot scores lower, I-18).
        /// </summary>
        public static bool TryScoreCandidate(
            string kind,
            FurniturePlacementPose pose,
            float lengthM, float widthM,
            bool kindMatched,
            FurniturePlacementContext context,
            out float score,
            out string refusal)
        {
            score = 0f;
            refusal = string.Empty;
            if (string.IsNullOrWhiteSpace(kind))
            {
                refusal = "FurniturePlacementScoring: a placement score needs a furniture kind — refusing a placement for nothing.";
                return false;
            }
            if (context == null)
            {
                refusal = "FurniturePlacementScoring: a placement score needs the building's placement context — the geometry was not supplied.";
                return false;
            }
            string wanted = kind.Trim();
            if (lengthM <= 0f) lengthM = DefaultDimensionMeters;
            if (widthM <= 0f) widthM = DefaultDimensionMeters;

            if (TryBlockingApproachId(pose, lengthM, widthM, context, out string blockedId))
            {
                refusal = $"FurniturePlacementScoring: placement for '{wanted}' would block the approach to '{blockedId}' — a placement whose footprint covers an existing activity point is refused, not ranked last.";
                return false;
            }
            if (context.HasDoor
                && FootprintCoversPoint(pose, lengthM, widthM, BlockingClearanceMeters, context.DoorX, context.DoorZ))
            {
                refusal = $"FurniturePlacementScoring: placement for '{wanted}' would block the doorway/entrance — circulation through the door stays clear.";
                return false;
            }

            float wallScore = 0f;
            if (context.HasWallPolygon)
            {
                if (IsWallRequiredKind(wanted))
                {
                    float gap = WallGapForKind(wanted, pose, lengthM, widthM, context);
                    if (gap > WallAdjacentGapMeters)
                    {
                        refusal = $"FurniturePlacementScoring: '{wanted}' is a wall fixture but this placement sits {gap:F2} m from the nearest wall (wall-adjacent is {WallAdjacentGapMeters:F2} m or less) — fixtures belong against a wall, so this slot is refused, not taken mid-room.";
                        return false;
                    }
                    wallScore = WallScoreMax;
                }
                else if (IsWallPreferredKind(wanted))
                {
                    float gap = BackWallGap(pose, widthM, context);
                    wallScore = WallScoreMax * Clamp01(1f - gap / WallPreferredFalloffMeters);
                }
            }

            float doorScore = NeutralDoorScore;
            if (context.HasDoor)
            {
                float distance = Distance(pose.X, pose.Z, context.DoorX, context.DoorZ);
                switch (DoorBiasForKind(wanted))
                {
                    case DoorBias.Far:
                        doorScore = DoorScoreMax * Clamp01(distance / DoorReferenceMeters);
                        break;
                    case DoorBias.Near:
                        doorScore = DoorScoreMax * Clamp01(1f - distance / DoorReferenceMeters);
                        break;
                    default:
                        doorScore = NeutralDoorScore;
                        break;
                }
            }

            score = wallScore + doorScore + (kindMatched ? KindMatchBonus : 0f);
            return true;
        }

        /// <summary>True when the kind's wall rule is a requirement (refuse away from a wall): shelf, cooking stove, kitchen counter — the Fixture kinds (I-03).</summary>
        public static bool IsWallRequiredKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return false;
            string wanted = kind.Trim();
            return string.Equals(wanted, FurnitureItemSpecs.ShelfId, StringComparison.Ordinal)
                || string.Equals(wanted, FurnishingClassification.CookingStoveKind, StringComparison.Ordinal)
                || string.Equals(wanted, FurnishingClassification.KitchenCounterKind, StringComparison.Ordinal);
        }

        /// <summary>True when the kind prefers a wall but may stand free: cabinets and the bed (I-03 — movable, freestanding, stable against a wall).</summary>
        public static bool IsWallPreferredKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return false;
            string wanted = kind.Trim();
            return string.Equals(wanted, FurnitureItemSpecs.CabinetSmallId, StringComparison.Ordinal)
                || string.Equals(wanted, FurnitureItemSpecs.CabinetLargeId, StringComparison.Ordinal)
                || string.Equals(wanted, FurnitureItemSpecs.BedId, StringComparison.Ordinal);
        }

        /// <summary>
        /// The wall gap the kind's rule measures: for a counter, the
        /// nearer short end's distance to the nearest wall edge (one
        /// short end against a wall — I-R7, expressed as a wall edge,
        /// never a compass direction, because buildings rotate); for
        /// every other wall kind, the back edge's distance to the
        /// nearest wall edge. PositiveInfinity when no wall polygon
        /// is available.
        /// </summary>
        public static float WallGapForKind(string kind, FurniturePlacementPose pose, float lengthM, float widthM,
            FurniturePlacementContext context)
        {
            if (context == null || !context.HasWallPolygon) return float.PositiveInfinity;
            if (string.Equals(kind?.Trim(), FurnishingClassification.KitchenCounterKind, StringComparison.Ordinal))
                return CounterEndWallGap(pose, lengthM, context);
            return BackWallGap(pose, widthM, context);
        }

        /// <summary>Distance from the item's back edge (centre minus forward × depth/2) to the nearest wall edge.</summary>
        public static float BackWallGap(FurniturePlacementPose pose, float depthM, FurniturePlacementContext context)
        {
            if (context == null || !context.HasWallPolygon) return float.PositiveInfinity;
            float forwardX, forwardZ;
            Forward(pose.YawDegrees, out forwardX, out forwardZ);
            float backX = pose.X - forwardX * (depthM * 0.5f);
            float backZ = pose.Z - forwardZ * (depthM * 0.5f);
            return DistanceToNearestWallEdge(backX, backZ, context);
        }

        /// <summary>Distance from the nearer short end of a counter (centre ± right × length/2) to the nearest wall edge.</summary>
        public static float CounterEndWallGap(FurniturePlacementPose pose, float lengthM, FurniturePlacementContext context)
        {
            if (context == null || !context.HasWallPolygon) return float.PositiveInfinity;
            float rightX, rightZ;
            Right(pose.YawDegrees, out rightX, out rightZ);
            float half = lengthM * 0.5f;
            float endAX = pose.X + rightX * half;
            float endAZ = pose.Z + rightZ * half;
            float endBX = pose.X - rightX * half;
            float endBZ = pose.Z - rightZ * half;
            return Math.Min(DistanceToNearestWallEdge(endAX, endAZ, context),
                DistanceToNearestWallEdge(endBX, endBZ, context));
        }

        /// <summary>Distance from a floor point to the nearest wall-polygon edge (point-to-segment, shortest over edges).</summary>
        public static float DistanceToNearestWallEdge(float x, float z, FurniturePlacementContext context)
        {
            if (context == null || !context.HasWallPolygon) return float.PositiveInfinity;
            float nearestSq = float.PositiveInfinity;
            List<FurnitureFloorPoint> polygon = context.WallPolygon;
            for (int i = 0; i < polygon.Count; i++)
            {
                FurnitureFloorPoint a = polygon[i];
                FurnitureFloorPoint b = polygon[(i + 1) % polygon.Count];
                float edgeX = b.X - a.X;
                float edgeZ = b.Z - a.Z;
                float edgeLenSq = edgeX * edgeX + edgeZ * edgeZ;
                if (edgeLenSq < 0.0001f) continue;
                float t = Clamp01(((x - a.X) * edgeX + (z - a.Z) * edgeZ) / edgeLenSq);
                float closestX = a.X + edgeX * t;
                float closestZ = a.Z + edgeZ * t;
                float dx = x - closestX;
                float dz = z - closestZ;
                float distanceSq = dx * dx + dz * dz;
                if (distanceSq < nearestSq) nearestSq = distanceSq;
            }
            return (float)Math.Sqrt(nearestSq);
        }

        private enum DoorBias
        {
            Neutral = 0,
            Far = 1,
            Near = 2,
        }

        private static DoorBias DoorBiasForKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind)) return DoorBias.Neutral;
            switch (kind.Trim())
            {
                case FurnitureItemSpecs.ShelfId:
                case FurnitureItemSpecs.BedId:
                    return DoorBias.Far;
                case FurnitureItemSpecs.CrateId:
                    return DoorBias.Near;
                default:
                    if (string.Equals(kind.Trim(), FurnishingClassification.CookingStoveKind, StringComparison.Ordinal))
                        return DoorBias.Far;
                    return DoorBias.Neutral;
            }
        }

        /// <summary>True when the candidate footprint (plus clearance) covers the approach point named in the out id.</summary>
        public static bool TryBlockingApproachId(FurniturePlacementPose pose, float lengthM, float widthM,
            FurniturePlacementContext context, out string activityLocationId)
        {
            activityLocationId = null;
            if (context?.ApproachPoints == null) return false;
            foreach (FurnitureApproachPoint point in context.ApproachPoints)
            {
                if (point == null) continue;
                if (FootprintCoversPoint(pose, lengthM, widthM, BlockingClearanceMeters, point.X, point.Z))
                {
                    activityLocationId = point.ActivityLocationId;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Point-in-rotated-rectangle test: the item's footprint
        /// (length along local X at yaw 0, width along local Z),
        /// rotated by the pose yaw, expanded by clearance on every
        /// side. Yaw is the Unity Y rotation: forward (0,0,1) rotates
        /// to (sin, cos) in (x, z).
        /// </summary>
        public static bool FootprintCoversPoint(FurniturePlacementPose pose, float lengthM, float widthM,
            float clearanceM, float pointX, float pointZ)
        {
            float halfLength = lengthM * 0.5f + clearanceM;
            float halfWidth = widthM * 0.5f + clearanceM;
            float yawRad = pose.YawDegrees * (float)(Math.PI / 180.0);
            float cos = (float)Math.Cos(yawRad);
            float sin = (float)Math.Sin(yawRad);
            float dx = pointX - pose.X;
            float dz = pointZ - pose.Z;
            // Rotate the point into the item's local frame (inverse yaw).
            float localX = dx * cos - dz * sin;
            float localZ = dx * sin + dz * cos;
            return Math.Abs(localX) <= halfLength && Math.Abs(localZ) <= halfWidth;
        }

        // ----- I-R3: installed-placement validation -----

        /// <summary>
        /// I-R3: scores one INSTALLED placement against the §4.1 table
        /// and returns a diagnostic when it fails, or null when it
        /// passes. A shelf mid-room is a WARNING, not an error — the
        /// game still runs; the caller puts the text where warnings
        /// surface. Null when no wall polygon is available (the wall
        /// rules cannot be evaluated, so they are skipped, never
        /// guessed); the blocking check still runs.
        /// </summary>
        public static string ValidateInstalledPlacement(
            string kind,
            FurniturePlacementPose pose,
            float lengthM, float widthM,
            FurniturePlacementContext context)
        {
            if (string.IsNullOrWhiteSpace(kind) || context == null) return null;
            if (lengthM <= 0f) lengthM = DefaultDimensionMeters;
            if (widthM <= 0f) widthM = DefaultDimensionMeters;
            if (TryBlockingApproachId(pose, lengthM, widthM, context, out string blockedId))
                return $"Installed '{kind.Trim()}' blocks the approach to '{blockedId}' — the activity point is unreachable while this piece sits here.";
            if (context.HasDoor
                && FootprintCoversPoint(pose, lengthM, widthM, BlockingClearanceMeters, context.DoorX, context.DoorZ))
                return $"Installed '{kind.Trim()}' blocks the doorway/entrance — circulation through the door stays clear.";
            if (!context.HasWallPolygon) return null;
            if (IsWallRequiredKind(kind.Trim()))
            {
                float gap = WallGapForKind(kind.Trim(), pose, lengthM, widthM, context);
                if (gap > WallAdjacentGapMeters)
                    return $"Installed '{kind.Trim()}' sits {gap:F2} m from the nearest wall (wall-adjacent is {WallAdjacentGapMeters:F2} m or less) — a {FurnishingClassification.Classify(kind.Trim())} fixture belongs against a wall.";
            }
            return null;
        }

        /// <summary>
        /// I-R3 for business fixtures, whose kinds are fixture records
        /// (fixture-counter, fixture-shelving, oven-chamber, ...), not
        /// furniture kinds. The §4.1 rule follows the fixture's shape:
        /// shelving and the oven range sit back-to-wall like a shelf
        /// and a stove; a counter or work surface needs one short end
        /// to a wall (I-R7, route (b) — counters are fixed fixtures,
        /// so the rule lives here as fixture validation, not in the
        /// furniture catalog). Crates and fuel vessels are free.
        /// Returns a diagnostic, or null when the fixture passes (or
        /// when its rule cannot be evaluated without a wall polygon).
        /// </summary>
        public static string ValidateBusinessFixturePlacement(
            string fixtureKind,
            FurniturePlacementPose pose,
            float lengthM, float widthM,
            FurniturePlacementContext context)
        {
            if (string.IsNullOrWhiteSpace(fixtureKind) || context == null) return null;
            string wanted = fixtureKind.Trim();
            if (lengthM <= 0f) lengthM = DefaultDimensionMeters;
            if (widthM <= 0f) widthM = DefaultDimensionMeters;
            if (TryBlockingApproachId(pose, lengthM, widthM, context, out string blockedId))
                return $"Business fixture '{wanted}' blocks the approach to '{blockedId}' — the activity point is unreachable while this fixture sits here.";
            if (context.HasDoor
                && FootprintCoversPoint(pose, lengthM, widthM, BlockingClearanceMeters, context.DoorX, context.DoorZ))
                return $"Business fixture '{wanted}' blocks the doorway/entrance — circulation through the door stays clear.";
            if (!context.HasWallPolygon) return null;
            switch (wanted)
            {
                case "fixture-shelving":
                case "oven-chamber":
                {
                    float gap = BackWallGap(pose, widthM, context);
                    if (gap > WallAdjacentGapMeters)
                        return $"Business fixture '{wanted}' sits {gap:F2} m from the nearest wall (wall-adjacent is {WallAdjacentGapMeters:F2} m or less) — shelving and the oven range belong against a wall.";
                    return null;
                }
                case "fixture-counter":
                case "fixture-work-surface":
                {
                    float gap = CounterEndWallGap(pose, lengthM, context);
                    if (gap > WallAdjacentGapMeters)
                        return $"Business fixture '{wanted}' has neither short end against a wall (nearest end {gap:F2} m from a wall edge; wall-adjacent is {WallAdjacentGapMeters:F2} m or less) — a counter needs one short end to a wall.";
                    return null;
                }
                default:
                    return null;
            }
        }

        // ----- I-R4: bakery stage → approach point -----

        /// <summary>
        /// I-R4: the REAL 9-stage bakery chain
        /// (BakeryStagedProductionChain: receive, prepare, proof,
        /// heat-oven, load, bake, unload, cool, store — no
        /// cabinet-fetch step and no dough-making-at-counter step
        /// exist in source, and none is invented here). Stage
        /// indices are the chain's own constants (StageReceive 0,
        /// StagePrepare 1, StageProof 2, StageHeatOven 3, StageLoad
        /// 4, StageBake 5, StageUnload 6, StageCool 7, StageStore 8),
        /// written as literals because the chain file is Unity-bound
        /// and outside the harness closure — the constants are
        /// cited here, never redefined. Maps each active stage to
        /// the activity location the baker must physically reach
        /// before that stage's active minutes accrue. Passive stages
        /// (proof, bake, cool) need no arrival — the baker is
        /// released while the rack/oven holds the batch. The store
        /// stage has no shelving activity location authored today,
        /// so it names none rather than faking one (that gap is
        /// open, not filled). Returns false for an unknown stage
        /// index. The live driver gate is NOT attached here: the
        /// staged chain is gated off by DEF-076, and a per-stage
        /// hold attached blind could stall staged baking (passive
        /// stages must not be held). When the chain is ungated, the
        /// gate attaches in StagedBakeryChainDriver /
        /// SimulationDrivers.DriveStagedBakeryTick: hold an active
        /// stage's minutes until the baker has arrived at the
        /// mapped point, and never hold a passive stage.
        /// </summary>
        public static bool TryBakeryStageApproachPoint(int stageIndex,
            out string activityLocationId, out bool requiresArrival)
        {
            activityLocationId = null;
            requiresArrival = false;
            switch (stageIndex)
            {
                case 0: // StageReceive
                    return true;
                case 1: // StagePrepare
                    activityLocationId = BakeryWorkSurfaceActivityId;
                    requiresArrival = true;
                    return true;
                case 2: // StageProof
                    return true;
                case 3: // StageHeatOven
                case 4: // StageLoad
                case 6: // StageUnload
                    activityLocationId = BakeryOvenApproachActivityId;
                    requiresArrival = true;
                    return true;
                case 5: // StageBake
                    return true;
                case 7: // StageCool
                    return true;
                case 8: // StageStore
                    return true;
                default:
                    return false;
            }
        }

        // ----- I-R5 / I-R6: retail counter service points -----

        /// <summary>
        /// I-R5/I-R6: the counter activity location for a retail
        /// business type — the point the clerk must reach before
        /// TendCustomer starts. The General Store's
        /// general-store-counter predates this lane; the bakery and
        /// fuel-dealer points are authored by this lane (I-R6/M-08:
        /// both had a queue and clerk candidates but no counter
        /// point, so direct service had nowhere to happen
        /// physically). False when the business type has no retail
        /// counter — the caller keeps today's behaviour for it and
        /// never guesses a point.
        /// </summary>
        public static bool TryRetailCounterActivityId(BusinessType businessType, out string activityLocationId)
        {
            activityLocationId = null;
            switch (businessType)
            {
                case BusinessType.GeneralStore:
                    activityLocationId = GeneralStoreCounterActivityId;
                    return true;
                case BusinessType.Bakery:
                    activityLocationId = BakeryRetailCounterActivityId;
                    return true;
                case BusinessType.FuelDealer:
                    activityLocationId = FuelDealerCounterActivityId;
                    return true;
                default:
                    return false;
            }
        }

        // ----- I-R8: capability-preferring selection -----

        /// <summary>
        /// I-R8: prefer the highest relevant capability among eligible
        /// candidates; the first-listed candidate wins ties, so an
        /// owner-first list keeps the owner as the tiebreak. No hard
        /// gates (Canon XXXI §3.3 / Tech XIV Part II §2.4: skill
        /// changes outcomes, never generic access — a novice still
        /// can, and is picked only when no better candidate is
        /// available). Returns -1 when no candidate is eligible.
        /// The capability function is the caller's (SkillService.
        /// GetCapability in production); a null capability function
        /// degrades to first-eligible, exactly today's behaviour.
        /// </summary>
        public static int SelectPreferredCandidate(
            IReadOnlyList<int> candidatePersonIds,
            Func<int, bool> isEligible,
            Func<int, double> capabilityOf)
        {
            if (candidatePersonIds == null || isEligible == null) return -1;
            var considered = new HashSet<int>();
            int bestPersonId = -1;
            double bestCapability = double.NegativeInfinity;
            for (int i = 0; i < candidatePersonIds.Count; i++)
            {
                int personId = candidatePersonIds[i];
                if (personId < 0 || !considered.Add(personId)) continue;
                if (!isEligible(personId)) continue;
                double capability = capabilityOf != null ? capabilityOf(personId) : 0.0;
                if (double.IsNaN(capability) || double.IsInfinity(capability)) capability = 0.0;
                if (bestPersonId < 0 || capability > bestCapability)
                {
                    bestPersonId = personId;
                    bestCapability = capability;
                }
            }
            return bestPersonId;
        }

        // ----- helpers -----

        private static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x1 - x0;
            float dz = z1 - z0;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;

        private static void Forward(float yawDegrees, out float x, out float z)
        {
            float rad = yawDegrees * (float)(Math.PI / 180.0);
            x = (float)Math.Sin(rad);
            z = (float)Math.Cos(rad);
        }

        private static void Right(float yawDegrees, out float x, out float z)
        {
            float rad = yawDegrees * (float)(Math.PI / 180.0);
            x = (float)Math.Cos(rad);
            z = -(float)Math.Sin(rad);
        }
    }
}
