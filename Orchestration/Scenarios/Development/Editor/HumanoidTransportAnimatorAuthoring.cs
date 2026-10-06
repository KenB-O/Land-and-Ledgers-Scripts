#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.Development.Editor
{
    /// <summary>Adds the replaceable transport states to the canonical Man/Woman controllers.</summary>
    public static class HumanoidTransportAnimatorAuthoring
    {
        private const string DrivingAssetPath = "Assets/Characters/Animations/X Bot@Driving.fbx";
        private static readonly string[] ControllerPaths =
        {
            "Assets/Characters/Animations/Man Anim Controller.controller",
            "Assets/Characters/Animations/Woman Anim Controller.controller"
        };

        [MenuItem("Land & Ledgers/Characters/Author Transport States")]
        public static void AuthorTransportStates()
        {
            AnimationClip drivingClip = FindDrivingClip();
            if (drivingClip == null)
            {
                throw new InvalidOperationException($"No animation clip was imported from '{DrivingAssetPath}'.");
            }

            foreach (string controllerPath in ControllerPaths)
            {
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
                if (controller == null)
                {
                    throw new InvalidOperationException($"Missing humanoid controller '{controllerPath}'.");
                }

                AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
                AddStateIfMissing(stateMachine, "DrivingWagon", drivingClip, new Vector3(260f, 80f));
                AddStateIfMissing(stateMachine, "MountedHorse", drivingClip, new Vector3(260f, 150f));
                EditorUtility.SetDirty(controller);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TransportAnimation] Authored DrivingWagon and MountedHorse into {ControllerPaths.Length} humanoid controllers using '{drivingClip.name}'.");
        }

        private static AnimationClip FindDrivingClip()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(DrivingAssetPath);
            return assets.OfType<AnimationClip>().FirstOrDefault(clip =>
                clip != null && clip.name.IndexOf("driv", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? assets.OfType<AnimationClip>().FirstOrDefault();
        }

        private static void AddStateIfMissing(AnimatorStateMachine stateMachine, string stateName, AnimationClip clip, Vector3 position)
        {
            AnimatorState existing = stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => string.Equals(state.name, stateName, StringComparison.Ordinal));
            if (existing != null)
            {
                existing.motion = clip;
                return;
            }

            AnimatorState state = stateMachine.AddState(stateName, position);
            state.motion = clip;
            state.writeDefaultValues = true;
        }
    }
}
#endif
