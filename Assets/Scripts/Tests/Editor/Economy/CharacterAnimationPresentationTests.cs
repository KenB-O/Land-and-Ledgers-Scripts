using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LandLedgers.Pathing;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class CharacterAnimationPresentationTests
    {
        private const string ManPath = "Assets/Characters/Man.prefab";
        private const string WagonPath = "Assets/Characters/Wagon.prefab";

        public static string RunIsolatedChecks()
        {
            var tests = new CharacterAnimationPresentationTests();
            new TransportFoundationTests().ManAnimController_HasDrivingWagonSeatedState();
            tests.ImportedClipsAreHumanoidLoopingAndRootBaked();
            tests.ImportedWalkOwnsBonesAndPlaybackFollowsVelocity();
            tests.DriverPoseSurvivesUpdatesAndReturnsToLocomotion();
            tests.UnriggedActorsRetainProceduralFallback();
            return "PASS: existing seated-state test plus 4 isolated presentation regression tests (Edit Mode, not live gameplay).";
        }

        [Test]
        public void ImportedClipsAreHumanoidLoopingAndRootBaked()
        {
            foreach (string path in new[] { "Assets/Characters/Animations/X Bot@Walking.fbx", "Assets/Characters/Animations/X Bot@Driving.fbx" })
            {
                AnimationClip clip = GetClip(path);
                Assert.That(clip.humanMotion, Is.True, path);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.That(settings.loopTime, Is.True, path);
                Assert.That(settings.loopBlendPositionXZ, Is.True, path);
                Assert.That(settings.loopBlendPositionY, Is.True, path);
            }
        }

        [Test]
        public void ImportedWalkOwnsBonesAndPlaybackFollowsVelocity()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject host = new GameObject("Isolated Person");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            try
            {
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ManPath), scene);
                visual.transform.SetParent(host.transform, false);
                AgentMover mover = host.AddComponent<AgentMover>();
                mover.ConfigureVisualRoot(visual.transform);
                Animator animator = visual.GetComponent<Animator>();
                animator.Rebind();
                animator.Update(0f);
                mover.ShowAtCurrentLocation();
                var serialized = new SerializedObject(mover);
                serialized.FindProperty("state").enumValueIndex = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Set(mover, "measuredMovementSpeed", 1.4f);
                for (int i = 0; i < 120; i++)
                {
                    Call(mover, "UpdateAnimatorState", false);
                    animator.SetFloat("Blend", 1f);
                    animator.Update(1f / 60f);
                    Quaternion before = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation;
                    Call(mover, "LateUpdate");
                    Assert.That(Quaternion.Angle(before, animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).localRotation), Is.LessThan(0.001f), "Fallback must never overwrite imported bones.");
                }
                float stride = 1.46f * animator.humanScale * Mathf.Abs(animator.transform.lossyScale.y);
                Assert.That(animator.speed, Is.EqualTo(1.4f / stride).Within(0.01f));
                Assert.That((bool)Get(mover, "animatorCanDriveLocomotion"), Is.True);
                Assert.That(animator.applyRootMotion, Is.False);
                Assert.That(host.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(1f), "Continuous animation must not restart each update.");
                host.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                Call(mover, "LateUpdate");
                Assert.That(Vector3.Dot(visual.transform.forward, host.transform.forward), Is.GreaterThan(0.999f));
                mover.ShowAtCurrentLocation();
                animator.Update(0.2f);
                Assert.That(animator.GetFloat("Blend"), Is.EqualTo(0f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void DriverPoseSurvivesUpdatesAndReturnsToLocomotion()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject host = new GameObject("Same Persistent Person View");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            try
            {
                GameObject wagon = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WagonPath), scene);
                Transform seat = wagon.GetComponent<LandLedgers.Economy.Transport.WagonWorldView>().DriverSeat;
                GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ManPath), scene);
                visual.transform.SetParent(host.transform, false);
                AgentMover mover = host.AddComponent<AgentMover>();
                mover.ConfigureVisualRoot(visual.transform);
                Animator animator = visual.GetComponent<Animator>();
                animator.Rebind(); animator.Update(0f);
                mover.ShowAtCurrentLocation();
                Vector3 originalScale = visual.transform.lossyScale;
                host.transform.SetParent(seat, true);
                host.transform.localPosition = Vector3.zero;
                host.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                Assert.That(mover.TrySetPresentationAnimation("DrivingWagon", seat), Is.True);
                for (int i = 0; i < 180; i++)
                {
                    wagon.transform.position += Vector3.right * 0.01f;
                    wagon.transform.rotation = Quaternion.Euler(0f, i * 0.25f, 0f);
                    animator.Update(1f / 60f);
                    Call(mover, "LateUpdate");
                    Assert.That(host.transform.parent, Is.SameAs(seat));
                    Assert.That(Vector3.Distance(visual.transform.lossyScale, originalScale), Is.LessThan(0.001f));
                }
                int drivingHash = Animator.StringToHash("DrivingWagon");
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(drivingHash));
                Assert.That(Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.Hips).position, seat.position + seat.up * 0.04f), Is.LessThan(0.002f));
                Transform thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
                Transform knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                float bend = 180f - Vector3.Angle(thigh.position - knee.position, foot.position - knee.position);
                Assert.That(bend, Is.GreaterThan(55f), "Driver knees must actually bend, not merely select a named state.");
                Assert.That(Vector3.Distance(foot.position, seat.position - host.transform.right * 0.08f
                    - seat.up * 0.23f + host.transform.forward * 0.33f), Is.LessThan(0.003f));
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                Assert.That(Vector3.Distance(rightFoot.position, seat.position + host.transform.right * 0.08f
                    - seat.up * 0.23f + host.transform.forward * 0.33f), Is.LessThan(0.003f));
                Quaternion pose = thigh.localRotation;
                Assert.That(mover.TrySetPresentationAnimation("DrivingWagon", seat), Is.True);
                Call(mover, "LateUpdate");
                Assert.That(Quaternion.Angle(pose, thigh.localRotation), Is.LessThan(0.001f));
                host.transform.SetParent(null, true);
                host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                mover.ClearPresentationAnimation();
                mover.ShowAtCurrentLocation();
                animator.Update(0.3f);
                Call(mover, "LateUpdate");
                Assert.That(mover.HasPresentationAnimation, Is.False);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.EqualTo(Animator.StringToHash("Locomotion")));
                Assert.That(visual.transform.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(scene.rootCount, Is.EqualTo(2), "No extra person should be created during attachment or release.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void UnriggedActorsRetainProceduralFallback()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var host = new GameObject("Fallback Person");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            try
            {
                var visual = new GameObject("Visual");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(visual, scene);
                visual.transform.SetParent(host.transform, false);
                AgentMover mover = host.AddComponent<AgentMover>();
                mover.ConfigureVisualRoot(visual.transform);
                var serialized = new SerializedObject(mover);
                serialized.FindProperty("state").enumValueIndex = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Call(mover, "UpdateProceduralVisualMotion", 0.1f);
                Assert.That(visual.transform.localPosition.sqrMagnitude, Is.GreaterThan(0f));
                Assert.That(mover.TrySetPresentationAnimation("DrivingWagon"), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); EditorSceneManager.ClosePreviewScene(scene); }
        }

        public static void EvaluatePresentationForPreview(AgentMover mover)
        {
            Call(mover, "LateUpdate");
        }

        private static AnimationClip GetClip(string path)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal)) return clip;
            throw new InvalidOperationException("Missing animation: " + path);
        }

        private static void Call(object target, string method, params object[] args)
        {
            Type[] types = new Type[args.Length];
            for (int i = 0; i < args.Length; i++) types[i] = args[i].GetType();
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, null, types, null).Invoke(target, args);
        }
        private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
