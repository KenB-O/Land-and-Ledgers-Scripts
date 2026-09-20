using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World
{
    public struct TownSiteSelectionRequest
    {
        public Vector2 townSizeMeters;
        public float minimumEdgeDistanceMeters;
        public float maximumTownSlopeDegrees;
        public float maximumRoadSlopeDegrees;
        public int candidateCount;
        public int validationGridResolution;
    }

    public readonly struct TownSiteSelectionResult
    {
        public TownSiteSelectionResult(Vector3 position, float score, int evaluatedCandidates, int validCandidates)
        {
            Position = position;
            Score = score;
            EvaluatedCandidates = evaluatedCandidates;
            ValidCandidates = validCandidates;
        }

        public Vector3 Position { get; }
        public float Score { get; }
        public int EvaluatedCandidates { get; }
        public int ValidCandidates { get; }
    }

    public static class TownSiteSelector
    {
        private readonly struct Candidate
        {
            public Candidate(Vector3 position, float score)
            {
                Position = position;
                Score = score;
            }

            public Vector3 Position { get; }
            public float Score { get; }
        }

        public static bool TrySelect(
            IWorldSurfaceProvider provider,
            TownSiteSelectionRequest request,
            int seed,
            bool verbose,
            out TownSiteSelectionResult result)
        {
            result = default;
            if (provider == null)
            {
                Debug.LogError("[TownSiting] No world surface provider is available.");
                return false;
            }

            Bounds bounds = provider.WorldBounds;
            float halfWidth = Mathf.Max(1f, request.townSizeMeters.x * 0.5f);
            float halfDepth = Mathf.Max(1f, request.townSizeMeters.y * 0.5f);
            float edge = Mathf.Max(0f, request.minimumEdgeDistanceMeters);
            float minX = bounds.min.x + halfWidth + edge;
            float maxX = bounds.max.x - halfWidth - edge;
            float minZ = bounds.min.z + halfDepth + edge;
            float maxZ = bounds.max.z - halfDepth - edge;
            if (minX > maxX || minZ > maxZ)
            {
                Debug.LogError($"[TownSiting] Authored terrain is too small for a {request.townSizeMeters.x:0}m x {request.townSizeMeters.y:0}m town footprint plus {edge:0}m edge clearance.");
                return false;
            }

            int candidateCount = Mathf.Max(8, request.candidateCount);
            System.Random random = new(seed);
            List<Candidate> valid = new();
            IWorldSurfaceCandidateZones zoneProvider = provider as IWorldSurfaceCandidateZones;
            IReadOnlyList<Bounds> explicitZones = zoneProvider != null ? zoneProvider.TownCandidateZones : null;
            Dictionary<string, int> failures = verbose ? new Dictionary<string, int>() : null;

            for (int i = 0; i < candidateCount; i++)
            {
                Vector3 candidate = explicitZones != null && explicitZones.Count > 0
                    ? SampleZoneCandidate(explicitZones, random, bounds.center.y)
                    : new Vector3(
                        Mathf.Lerp(minX, maxX, (float)random.NextDouble()),
                        bounds.center.y,
                        Mathf.Lerp(minZ, maxZ, (float)random.NextDouble()));
                candidate.x = Mathf.Clamp(candidate.x, minX, maxX);
                candidate.z = Mathf.Clamp(candidate.z, minZ, maxZ);

                if (explicitZones != null
                    && explicitZones.Count > 0
                    && zoneProvider != null
                    && !zoneProvider.IsInsideTownCandidateZone(candidate))
                {
                    if (failures != null)
                    {
                        failures.TryGetValue("candidate zone shape", out int count);
                        failures["candidate zone shape"] = count + 1;
                    }
                    continue;
                }

                if (!TryScoreCandidate(provider, request, candidate, out Vector3 grounded, out float score, out string failure))
                {
                    if (failures != null)
                    {
                        failures.TryGetValue(failure, out int count);
                        failures[failure] = count + 1;
                    }
                    continue;
                }

                valid.Add(new Candidate(grounded, score));
            }

            if (valid.Count == 0)
            {
                string failureSummary = failures == null ? string.Empty : " Failures: " + string.Join(", ", FormatFailures(failures)) + ".";
                Debug.LogError($"[TownSiting] No valid opening town site was found after {candidateCount} authored-terrain candidates.{failureSummary}");
                return false;
            }

            valid.Sort((left, right) => right.Score.CompareTo(left.Score));
            int topCount = Mathf.Min(8, valid.Count);
            int selectedIndex = PositiveHash(seed * 486187739 + valid.Count * 31) % topCount;
            Candidate selected = valid[selectedIndex];
            result = new TownSiteSelectionResult(selected.Position, selected.Score, candidateCount, valid.Count);
            return true;
        }

        public static bool TryValidateFixed(
            IWorldSurfaceProvider provider,
            TownSiteSelectionRequest request,
            Vector3 authoredAnchor,
            out TownSiteSelectionResult result,
            out string error)
        {
            result = default;
            error = string.Empty;
            if (provider == null)
            {
                error = "no world surface provider is available";
                return false;
            }

            if (!IsFinite(authoredAnchor))
            {
                error = "the authored anchor contains NaN or infinite coordinates";
                return false;
            }

            Bounds bounds = provider.WorldBounds;
            float halfWidth = Mathf.Max(1f, request.townSizeMeters.x * 0.5f);
            float halfDepth = Mathf.Max(1f, request.townSizeMeters.y * 0.5f);
            float edge = Mathf.Max(0f, request.minimumEdgeDistanceMeters);
            if (authoredAnchor.x - halfWidth - edge < bounds.min.x
                || authoredAnchor.x + halfWidth + edge > bounds.max.x
                || authoredAnchor.z - halfDepth - edge < bounds.min.z
                || authoredAnchor.z + halfDepth + edge > bounds.max.z)
            {
                error = $"the authored anchor is outside the usable terrain bounds or violates the {edge:0.##}m edge margin";
                return false;
            }

            if (!provider.TrySampleSurface(authoredAnchor, out WorldSurfaceSample center))
            {
                error = "the authored anchor is outside every configured terrain tile or could not be sampled";
                return false;
            }

            if (center.isWater)
            {
                error = "the authored anchor is in water";
                return false;
            }

            if (center.isBlocked)
            {
                error = "the authored anchor is inside a Blocked mask";
                return false;
            }

            if (provider is PreAuthoredTerrainSurfaceProvider authoredProvider
                && authoredProvider.HasPlacementMask(center.position, WorldPlacementMaskType.NoTown))
            {
                error = "the authored anchor is inside a NoTown mask";
                return false;
            }

            if (provider is IWorldSurfaceCandidateZones candidateZones
                && candidateZones.TownCandidateZones.Count > 0
                && !candidateZones.IsInsideTownCandidateZone(center.position))
            {
                error = "the authored anchor is outside the configured TownCandidateZone collider shapes";
                return false;
            }

            if (!TryScoreCandidate(provider, request, authoredAnchor, out Vector3 grounded, out float score, out string failure))
            {
                error = failure switch
                {
                    "town footprint" => "the required town footprint crosses invalid terrain, water, NoTown/Blocked masks, or excessive slope",
                    "road spine" => "the required starting road spine crosses invalid terrain, water, NoRoad/Blocked masks, or excessive slope",
                    _ => $"the authored anchor failed {failure} validation"
                };
                return false;
            }

            result = new TownSiteSelectionResult(grounded, score, 1, 1);
            return true;
        }

        private static bool TryScoreCandidate(
            IWorldSurfaceProvider provider,
            TownSiteSelectionRequest request,
            Vector3 candidate,
            out Vector3 grounded,
            out float score,
            out string failure)
        {
            grounded = candidate;
            score = 0f;
            failure = string.Empty;
            if (!provider.TrySampleSurface(candidate, out WorldSurfaceSample centerSample))
            {
                failure = "outside terrain";
                return false;
            }

            grounded.y = centerSample.height;
            int resolution = Mathf.Clamp(request.validationGridResolution, 3, 9);
            float slopeTotal = 0f;
            int sampleCount = 0;
            PlacementQuery townQuery = new()
            {
                maximumSlopeDegrees = Mathf.Max(0.1f, request.maximumTownSlopeDegrees),
                forTown = true,
                forBuilding = true
            };
            RoadQuery roadQuery = new()
            {
                maximumSlopeDegrees = request.maximumRoadSlopeDegrees > 0f
                    ? request.maximumRoadSlopeDegrees
                    : Mathf.Max(0.1f, request.maximumTownSlopeDegrees)
            };

            for (int z = 0; z < resolution; z++)
            {
                float zOffset = Mathf.Lerp(-request.townSizeMeters.y * 0.5f, request.townSizeMeters.y * 0.5f, z / (float)(resolution - 1));
                for (int x = 0; x < resolution; x++)
                {
                    float xOffset = Mathf.Lerp(-request.townSizeMeters.x * 0.5f, request.townSizeMeters.x * 0.5f, x / (float)(resolution - 1));
                    Vector3 point = new(candidate.x + xOffset, candidate.y, candidate.z + zOffset);
                    if (!provider.IsBuildable(point, townQuery)
                        || !provider.TrySampleSurface(point, out WorldSurfaceSample sample))
                    {
                        failure = "town footprint";
                        return false;
                    }

                    slopeTotal += sample.slopeDegrees;
                    sampleCount++;
                }
            }

            int roadCompatible = 0;
            for (int i = 0; i < resolution; i++)
            {
                float zOffset = Mathf.Lerp(-request.townSizeMeters.y * 0.5f, request.townSizeMeters.y * 0.5f, i / (float)(resolution - 1));
                if (provider.IsRoadCompatible(new Vector3(candidate.x, candidate.y, candidate.z + zOffset), roadQuery))
                {
                    roadCompatible++;
                }
            }

            if (roadCompatible < resolution)
            {
                failure = "road spine";
                return false;
            }

            float averageSlope = slopeTotal / Mathf.Max(1, sampleCount);
            float flatness = 1f - Mathf.Clamp01(averageSlope / Mathf.Max(0.1f, request.maximumTownSlopeDegrees));
            Bounds bounds = provider.WorldBounds;
            float edgeDistance = Mathf.Min(
                Mathf.Min(candidate.x - bounds.min.x, bounds.max.x - candidate.x),
                Mathf.Min(candidate.z - bounds.min.z, bounds.max.z - candidate.z));
            float edgeScore = Mathf.Clamp01(edgeDistance / Mathf.Max(1f, Mathf.Max(request.townSizeMeters.x, request.townSizeMeters.y)));
            score = flatness * 0.78f + edgeScore * 0.22f;
            return true;
        }

        private static Vector3 SampleZoneCandidate(IReadOnlyList<Bounds> zones, System.Random random, float y)
        {
            Bounds zone = zones[random.Next(0, zones.Count)];
            return new Vector3(
                Mathf.Lerp(zone.min.x, zone.max.x, (float)random.NextDouble()),
                y,
                Mathf.Lerp(zone.min.z, zone.max.z, (float)random.NextDouble()));
        }

        private static IEnumerable<string> FormatFailures(Dictionary<string, int> failures)
        {
            foreach (KeyValuePair<string, int> pair in failures)
            {
                yield return $"{pair.Key}={pair.Value}";
            }
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return (int)(hash & 0x7fffffff);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }
    }
}
