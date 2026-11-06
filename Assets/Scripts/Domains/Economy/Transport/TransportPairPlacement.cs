// Slice S6 (2026-11-06, Objective 20): in-line coupled placement of the
// opening horse-and-wagon pair, from the authored geometry.
//
// The controller used to place both views with one shared `facing`
// rotation, 2.8 m apart along `facing * Vector3.forward` — but the horse's
// native forward is +Z while the wagon's length runs along its local +X,
// so the pair stood perpendicular, the wagon ahead of the horse's nose.
// The correctly aligned arrangement already exists as Kennedy's combined
// prefab `Assets/Characters/Horse and Wagon.prefab` (wagon at local
// x = -2.373619, i.e. 2.37 m behind the horse along its facing axis);
// this helper pins that placement. It is Vector3-only by design: the
// harness shim stubs Quaternion.Euler / composition to identity, so the
// rotation composition (restoring the horse's baked Y90) lives in the
// controller, which runs in real Unity — see
// OpeningTransportPresentationController.Present.
//
// Constants, all sourced, none invented:
//   PairSeparationMetres — combined prefab wagon local x (-2.373619).
//   HorseRoadOffsetMetres — the controller's existing 1.8 m literal,
//     axis corrected, value kept.
//   HitchPostOffsetMetres — the controller's existing 1.1 m literal,
//     axis corrected, value kept.
using UnityEngine;

namespace LandLedgers.Economy.Transport
{
    public static class TransportPairPlacement
    {
        public const float PairSeparationMetres = 2.3736f;
        public const float HorseRoadOffsetMetres = 1.8f;
        public const float HitchPostOffsetMetres = 1.1f;

        public static void Resolve(
            Vector3 accessPoint,
            Vector3 pairLateral,
            Vector3 travelAxis,
            out Vector3 horsePosition,
            out Vector3 wagonPosition,
            out Vector3 hitchPostQuery)
        {
            horsePosition = accessPoint - pairLateral * HorseRoadOffsetMetres;
            wagonPosition = horsePosition - travelAxis * PairSeparationMetres;
            hitchPostQuery = horsePosition - pairLateral * HitchPostOffsetMetres;
        }
    }
}
