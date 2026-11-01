using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>Maps an authored Animator speed onto the authoritative simulation clock.</summary>
    public static class SimulationAnimationScale
    {
        public static float Resolve(float authoredSpeed, bool isPaused, float simulationSpeedMultiplier)
        {
            return isPaused ? 0f : authoredSpeed * Mathf.Max(0f, simulationSpeedMultiplier);
        }

        public static float ResolveMovingActorSpeed(float authoredSpeed, bool isMoving, float simulationSpeedMultiplier)
        {
            return isMoving
                ? Resolve(authoredSpeed, simulationSpeedMultiplier <= 0f, simulationSpeedMultiplier)
                : 1f;
        }
    }
}
