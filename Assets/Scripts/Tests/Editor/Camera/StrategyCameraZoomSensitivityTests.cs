using LandLedgers.CameraSystem;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.Tests.Camera
{
    public sealed class StrategyCameraZoomSensitivityTests
    {
        [Test]
        public void DistanceScaleIsPreciseNearBuildingsAndFastForTownViews()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.minZoomDistance = 8f;
                settings.maxZoomDistance = 200f;
                settings.preciseWheelZoomDistance = 16f;
                settings.fastWheelZoomDistance = 40f;
                settings.closeWheelZoomScale = 0.08f;
                settings.preciseWheelZoomScale = 0.25f;
                settings.farWheelZoomScale = 2f;
                settings.maximumWheelZoomScale = 4f;

                float atEight = settings.EvaluateDistanceWheelZoomScale(8f);
                float atTwelve = settings.EvaluateDistanceWheelZoomScale(12f);
                float atSixteen = settings.EvaluateDistanceWheelZoomScale(16f);
                float atTwentyFive = settings.EvaluateDistanceWheelZoomScale(25f);
                float atForty = settings.EvaluateDistanceWheelZoomScale(40f);
                float atOneHundred = settings.EvaluateDistanceWheelZoomScale(100f);

                Assert.AreEqual(0.08f, atEight, 0.001f);
                Assert.That(atTwelve, Is.InRange(atEight, atSixteen));
                Assert.AreEqual(0.25f, atSixteen, 0.001f);
                Assert.That(atTwentyFive, Is.GreaterThan(atSixteen));
                Assert.AreEqual(2f, atForty, 0.001f);
                Assert.That(atOneHundred, Is.GreaterThan(atForty));
                Assert.That(atForty / atTwelve, Is.GreaterThan(10f),
                    "A wheel notch at 40m must move through the overview materially faster than near a building.");
                Assert.AreEqual(4f, settings.EvaluateDistanceWheelZoomScale(200f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void DistanceScaleRemainsContinuousAcrossSensitivityBands()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.minZoomDistance = 8f;
                settings.maxZoomDistance = 200f;
                settings.preciseWheelZoomDistance = 16f;
                settings.fastWheelZoomDistance = 40f;

                const float epsilon = 0.01f;
                float nearPrecise = settings.EvaluateDistanceWheelZoomScale(16f - epsilon);
                float atPrecise = settings.EvaluateDistanceWheelZoomScale(16f);
                float afterPrecise = settings.EvaluateDistanceWheelZoomScale(16f + epsilon);
                float nearFast = settings.EvaluateDistanceWheelZoomScale(40f - epsilon);
                float atFast = settings.EvaluateDistanceWheelZoomScale(40f);
                float afterFast = settings.EvaluateDistanceWheelZoomScale(40f + epsilon);

                Assert.That(Mathf.Abs(atPrecise - nearPrecise), Is.LessThan(0.002f));
                Assert.That(Mathf.Abs(afterPrecise - atPrecise), Is.LessThan(0.002f));
                Assert.That(Mathf.Abs(atFast - nearFast), Is.LessThan(0.002f));
                Assert.That(Mathf.Abs(afterFast - atFast), Is.LessThan(0.002f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CloseRangePanAndRotationAreReducedAndReturnToExistingResponseByFortyMetres()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.closeControlStartDistance = 16f;
                settings.standardControlDistance = 40f;
                settings.closePanSensitivity = 0.2f;
                settings.closeRotationSensitivity = 0.25f;

                Assert.AreEqual(0.2f, settings.EvaluateClosePanSensitivity(8f), 0.001f);
                Assert.AreEqual(0.2f, settings.EvaluateClosePanSensitivity(16f), 0.001f);
                Assert.AreEqual(0.25f, settings.EvaluateCloseRotationSensitivity(12f), 0.001f);
                Assert.That(settings.EvaluateClosePanSensitivity(25f), Is.GreaterThan(0.2f).And.LessThan(1f));
                Assert.That(settings.EvaluateCloseRotationSensitivity(25f), Is.GreaterThan(0.25f).And.LessThan(1f));
                Assert.AreEqual(1f, settings.EvaluateClosePanSensitivity(40f), 0.001f);
                Assert.AreEqual(1f, settings.EvaluateCloseRotationSensitivity(40f), 0.001f);
                Assert.AreEqual(1f, settings.EvaluateClosePanSensitivity(100f), 0.001f);
                Assert.AreEqual(1f, settings.EvaluateCloseRotationSensitivity(100f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
