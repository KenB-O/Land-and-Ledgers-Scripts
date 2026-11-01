using LandLedgers.CameraSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace LandLedgers.Tests.Camera
{
    public sealed class StrategyCameraPitchBindingTests
    {
        [Test]
        public void DefaultSettingsBindRToHorizonAndFToMoreTopDown()
        {
            StrategyCameraSettings settings = AssetDatabase.LoadAssetAtPath<StrategyCameraSettings>(
                "Assets/Core/Camera/DefaultStrategyCameraSettings.asset");

            Assert.IsNotNull(settings, "The Main Scene camera settings asset must be present.");
            Assert.AreEqual(Key.R, settings.pitchUpKey, "R tilts the camera toward the horizon.");
            Assert.AreEqual(Key.F, settings.pitchDownKey, "F tilts the camera to a more top-down angle.");
        }
    }
}
