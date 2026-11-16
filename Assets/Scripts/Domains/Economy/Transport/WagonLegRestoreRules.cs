// Campaign C (DEF-047): pure restore rules for a persisted wagon leg.
//
// The controller (CoupledTransportMovementController, a MonoBehaviour) walks
// the leg in real Unity and cannot run in the Roslyn harness, so the rules
// that decide whether a saved leg may resume — and how its distances clamp
// — live here as plain System-only math, exactly like CoupledTransportProgress
// owns the path-distance math so the harness can pin it. The controller's
// RestoreLeg calls these and performs the Unity-side work (views, clock,
// placement) itself.
//
// A saved leg resumes from its snapshot alone: the shipment/journey link is
// NOT re-validated at resume time. The leg was valid when saved, the shipment
// restores in the same load, and the arrival gates
// (ConfirmPhysicalFreightOriginArrival / ConfirmPhysicalFreightDestinationArrival)
// still run when the leg completes and refuse if the journey authority no
// longer agrees — the existing safety net, not a new policy. A return leg
// carries no journey binding at all (BeginCoupledReturn starts it without
// journey/destination/vehicle ids), so a shipment check could not cover
// every leg kind.
using System;

namespace LandLedgers.Economy.Transport
{
    public static class WagonLegRestoreRules
    {
        /// <summary>
        /// Whether a persisted leg snapshot may resume at all: a leg was
        /// active, its path has at least two cells, the movement parameters
        /// are usable (same refusals as CoupledTransportMovementController
        /// .StartLeg), and a driver identity is recorded. Regional timer
        /// state is clamped separately and never refuses a resume on its own.
        /// </summary>
        public static bool IsRestorable(
            bool isActive,
            int pathCellCount,
            float cellSizeMetres,
            float wagonSpeedMetresPerSecond,
            float pairSeparationMetres,
            int ethanPersonId)
        {
            return isActive
                && pathCellCount >= 2
                && cellSizeMetres > 0f
                && wagonSpeedMetresPerSecond > 0f
                && pairSeparationMetres >= 0f
                && ethanPersonId >= 0;
        }

        /// <summary>
        /// Clamps a persisted travelled distance onto the leg's path. A
        /// distance at or past the total length is kept (the next tick's
        /// advance completes the leg through the normal arrival path);
        /// negative or NaN distances restart at the path start.
        /// </summary>
        public static float ClampDistanceTravelled(float distanceTravelledMetres, float totalLengthMetres)
        {
            if (float.IsNaN(distanceTravelledMetres) || distanceTravelledMetres < 0f) return 0f;
            if (totalLengthMetres <= 0f) return 0f;
            return Math.Min(distanceTravelledMetres, totalLengthMetres);
        }

        /// <summary>
        /// Clamps a persisted regional-travel elapsed time onto its
        /// duration. An elapsed time at or past the duration is kept (the
        /// next tick completes the regional leg); negative or NaN elapsed
        /// times restart the timer.
        /// </summary>
        public static float ClampRegionalElapsed(float elapsedSeconds, float durationSeconds)
        {
            if (float.IsNaN(elapsedSeconds) || elapsedSeconds < 0f) return 0f;
            if (durationSeconds <= 0f) return 0f;
            return Math.Min(elapsedSeconds, durationSeconds);
        }
    }
}
