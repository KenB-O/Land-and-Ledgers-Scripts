// Slice S6 (2026-11-06, Objective 20): pins the in-line coupled
// placement of the opening horse-and-wagon pair.
//
// Regression-guards the perpendicular-pair defect: before S6 the
// controller placed the wagon 2.8 m ahead of the horse along the horse's
// facing direction while the wagon's travel axis ran 90 degrees away, so
// the horse faced the wagon's flank. These tests pin the helper
// (TransportPairPlacement) only — the controller is harness-excluded, so
// its call of the helper plus the horse-rotation composition are
// source-reviewed only, and the rotation composition is pinned by the
// Unity-runner tests in TransportFoundationTests.
using System;
using NUnit.Framework;
using UnityEngine;
using LandLedgers.Economy.Transport;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class TransportPairPlacementTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void Resolve_PlacesHorseBesideRoadCell()
        {
            Vector3 accessPoint = new Vector3(10f, 0f, 5f);
            Vector3 pairLateral = new Vector3(0f, 0f, 1f);
            Vector3 travelAxis = new Vector3(1f, 0f, 0f);

            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out _, out _);

            Vector3 expected = accessPoint - pairLateral * 1.8f;
            Assert.That(horsePosition.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(horsePosition.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(horsePosition.z, Is.EqualTo(expected.z).Within(Tolerance));
        }

        [Test]
        public void Resolve_PlacesWagonBehindHorseAlongTravelAxis()
        {
            Vector3 accessPoint = new Vector3(10f, 0f, 5f);
            Vector3 pairLateral = new Vector3(0f, 0f, 1f);
            Vector3 travelAxis = new Vector3(1f, 0f, 0f);

            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out Vector3 wagonPosition, out _);

            Vector3 expected = horsePosition - travelAxis * 2.3736f;
            Assert.That(wagonPosition.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(wagonPosition.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(wagonPosition.z, Is.EqualTo(expected.z).Within(Tolerance));
            Assert.That(Vector3.Distance(horsePosition, wagonPosition),
                Is.EqualTo(2.3736f).Within(Tolerance));
        }

        [Test]
        public void Resolve_PairAxisIsTravelNotLateral()
        {
            Vector3 accessPoint = new Vector3(10f, 0f, 5f);
            Vector3 pairLateral = new Vector3(0f, 0f, 1f);
            Vector3 travelAxis = new Vector3(1f, 0f, 0f);

            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out Vector3 wagonPosition, out _);

            Vector3 pairAxis = (wagonPosition - horsePosition).normalized;
            Assert.That(Vector3.Dot(pairAxis, travelAxis), Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(Vector3.Dot(pairAxis, pairLateral), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Resolve_RotatedAxes_PreserveDistances()
        {
            // A 180-degree yaw case: travel along -Z, lateral along +X.
            Vector3 accessPoint = new Vector3(10f, 0f, 5f);
            Vector3 pairLateral = new Vector3(1f, 0f, 0f);
            Vector3 travelAxis = new Vector3(0f, 0f, -1f);

            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out Vector3 wagonPosition, out _);

            Assert.That(horsePosition.x, Is.EqualTo(8.2f).Within(Tolerance));
            Assert.That(horsePosition.z, Is.EqualTo(5f).Within(Tolerance));
            Assert.That(Vector3.Distance(horsePosition, wagonPosition),
                Is.EqualTo(2.3736f).Within(Tolerance));
            Vector3 pairAxis = (wagonPosition - horsePosition).normalized;
            Assert.That(Vector3.Dot(pairAxis, travelAxis), Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(Vector3.Dot(pairAxis, pairLateral), Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void Resolve_HitchPostQuery_BesideHorseNotBetweenPair()
        {
            Vector3 accessPoint = new Vector3(10f, 0f, 5f);
            Vector3 pairLateral = new Vector3(0f, 0f, 1f);
            Vector3 travelAxis = new Vector3(1f, 0f, 0f);

            TransportPairPlacement.Resolve(accessPoint, pairLateral, travelAxis,
                out Vector3 horsePosition, out Vector3 wagonPosition, out Vector3 hitchPostQuery);

            Vector3 expected = horsePosition - pairLateral * 1.1f;
            Assert.That(hitchPostQuery.x, Is.EqualTo(expected.x).Within(Tolerance));
            Assert.That(hitchPostQuery.y, Is.EqualTo(expected.y).Within(Tolerance));
            Assert.That(hitchPostQuery.z, Is.EqualTo(expected.z).Within(Tolerance));

            // The post must sit beside the horse, not on the horse-wagon
            // segment (the pair's path): distance from the segment > 0.5 m.
            Vector3 segment = wagonPosition - horsePosition;
            float t = Vector3.Dot(hitchPostQuery - horsePosition, segment) / segment.sqrMagnitude;
            t = Math.Max(0f, Math.Min(1f, t));
            Vector3 closest = horsePosition + segment * t;
            Assert.That(Vector3.Distance(hitchPostQuery, closest), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Resolve_SeparationMatchesCombinedPrefab()
        {
            // Combined prefab `Horse and Wagon.prefab`: wagon local
            // x = -2.373619. If Kennedy re-authors that separation, this
            // test names the drift.
            Assert.That(TransportPairPlacement.PairSeparationMetres,
                Is.EqualTo(2.3736f).Within(Tolerance));
            Assert.That(Math.Abs(TransportPairPlacement.PairSeparationMetres - 2.373619f),
                Is.LessThan(Tolerance));
        }
    }
}
