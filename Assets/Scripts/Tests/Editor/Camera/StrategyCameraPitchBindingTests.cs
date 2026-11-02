using LandLedgers.CameraSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace LandLedgers.Tests.Camera
{
    public sealed class StrategyCameraPitchBindingTests
    {
        [Test]
        public void DefaultSettingsBindRToRaiseAndFToLower()
        {
            StrategyCameraSettings settings = AssetDatabase.LoadAssetAtPath<StrategyCameraSettings>(
                "Assets/Core/Camera/DefaultStrategyCameraSettings.asset");

            Assert.IsNotNull(settings, "The Main Scene camera settings asset must be present.");
            Assert.AreEqual(Key.R, settings.pitchUpKey, "R raises the view toward the horizon.");
            Assert.AreEqual(Key.F, settings.pitchDownKey, "F lowers the view toward the ground.");
        }

        [TestCase(true, false, -1f)]
        [TestCase(false, true, 1f)]
        [TestCase(true, true, 0f)]
        [TestCase(false, false, 0f)]
        public void RaiseAndLowerInputProducesExpectedPitchDirection(bool raisePressed, bool lowerPressed, float expected)
        {
            Assert.AreEqual(expected, StrategyCameraController.EvaluateRaiseLowerPitchInput(raisePressed, lowerPressed));
        }

        [Test]
        public void RuntimeHudShowsRaiseRAndLowerFHelp()
        {
            UnityEngine.GameObject root = new("Camera Controls HUD Test", typeof(UnityEngine.RectTransform));
            try
            {
                LandLedgers.UI.LandLedgersHUDController hud = root.AddComponent<LandLedgers.UI.LandLedgersHUDController>();
                StringAssert.Contains("Raise R", hud.CurrentControlHintText);
                StringAssert.Contains("Lower F", hud.CurrentControlHintText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
