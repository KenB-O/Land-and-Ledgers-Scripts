using LandLedgers.CameraSystem;
using NUnit.Framework;
using System.Reflection;
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

        [Test]
        public void WheelZoomStepIsBoundedAtNearAndFarZoomDistances()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.maximumWheelZoomStepPerInput = 3f;
                Assert.AreEqual(3f, settings.LimitWheelZoomStep(24f), 0.001f);
                Assert.AreEqual(-3f, settings.LimitWheelZoomStep(-24f), 0.001f);
                Assert.AreEqual(0.5f, settings.LimitWheelZoomStep(0.5f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CloseViewCanZoomTowardFurnitureAndDoesNotRequireFirstPersonDistance()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.minZoomDistance = 1.5f;
                settings.closeControlStartDistance = 8f;
                settings.Sanitize();

                Assert.AreEqual(1.5f, settings.minZoomDistance, 0.001f);
                Assert.IsTrue(settings.focusViewCenterWhenZoomingClose);
                Assert.AreEqual(0.2f, settings.EvaluateClosePanSensitivity(1.5f), 0.001f);
                Assert.AreEqual(1f, settings.EvaluateClosePanSensitivity(40f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CloseRangePanCanBeMoreResponsiveWithoutChangingWideScaleSpeed()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            try
            {
                settings.closeControlStartDistance = 16f;
                settings.standardControlDistance = 40f;
                settings.closePanSensitivity = 0.3f;
                Assert.AreEqual(0.3f, settings.EvaluateClosePanSensitivity(8f), 0.001f);
                Assert.AreEqual(1f, settings.EvaluateClosePanSensitivity(40f), 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CameraArmStopsAtWallAndRestoresItsRequestedDistanceWhenClear()
        {
            const int obstructionLayer = 31;
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            GameObject rig = new("Camera obstruction test rig");
            GameObject yawObject = new("YawPivot");
            GameObject pitchObject = new("PitchPivot");
            GameObject cameraObject = new("Camera");
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                settings.cameraObstructionLayers = 1 << obstructionLayer;
                settings.cameraObstructionStartOffset = 0.35f;
                settings.cameraObstructionPadding = 0.12f;
                settings.cameraObstructionRadius = 0.14f;
                rig.transform.position = Vector3.zero;
                yawObject.transform.SetParent(rig.transform, false);
                pitchObject.transform.SetParent(yawObject.transform, false);
                cameraObject.transform.SetParent(pitchObject.transform, false);
                UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
                StrategyCameraController controller = rig.AddComponent<StrategyCameraController>();
                controller.ConfigureRigReferences(settings, yawObject.transform, pitchObject.transform, camera, null);

                wall.layer = obstructionLayer;
                wall.transform.position = new Vector3(0f, 0f, -4f);
                wall.transform.localScale = new Vector3(4f, 4f, 1f);
                Physics.SyncTransforms();

                MethodInfo resolve = typeof(StrategyCameraController).GetMethod(
                    "ResolveCameraObstruction", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(resolve);
                float obstructed = (float)resolve.Invoke(controller, new object[] { 0f, 0f, 10f });
                Assert.That(obstructed, Is.InRange(3f, 3.5f));

                wall.transform.position = new Vector3(10f, 0f, -4f);
                Physics.SyncTransforms();
                float clear = (float)resolve.Invoke(controller, new object[] { 0f, 0f, 10f });
                Assert.AreEqual(10f, clear, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void CameraArmExtensionAfterObstructionIsBoundedWhileCollisionRetractionIsImmediate()
        {
            Assert.AreEqual(3f,
                StrategyCameraController.LimitCameraArmExtension(8f, 3f, 24f, 0.1f), 0.001f,
                "The camera must retract immediately to remain in front of an obstruction.");
            Assert.AreEqual(5.4f,
                StrategyCameraController.LimitCameraArmExtension(3f, 10f, 24f, 0.1f), 0.001f,
                "After the obstruction clears, the camera must not jump to a distant retained zoom target.");
            Assert.AreEqual(10f,
                StrategyCameraController.LimitCameraArmExtension(3f, 10f, 24f, 1f), 0.001f,
                "A clear target within the allowed recovery distance should be reached in one step.");
        }

        [Test]
        public void ApplyingOrbitPreservesTheAuthoredCameraFieldOfView()
        {
            StrategyCameraSettings settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            GameObject rig = new("Camera field of view preservation rig");
            GameObject yawObject = new("YawPivot");
            GameObject pitchObject = new("PitchPivot");
            GameObject cameraObject = new("Camera");
            try
            {
                yawObject.transform.SetParent(rig.transform, false);
                pitchObject.transform.SetParent(yawObject.transform, false);
                cameraObject.transform.SetParent(pitchObject.transform, false);
                UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
                camera.fieldOfView = 70f;
                StrategyCameraController controller = rig.AddComponent<StrategyCameraController>();
                controller.ConfigureRigReferences(settings, yawObject.transform, pitchObject.transform, camera, null);

                controller.JumpTo(Vector3.zero, 30f, 5f, 35f);

                Assert.AreEqual(70f, camera.fieldOfView, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
