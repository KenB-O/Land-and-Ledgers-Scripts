#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using LandLedgers.Economy.Transport;

namespace LandLedgers.Editor
{
    /// <summary>
    /// One-shot production asset authoring for the transport foundation. It is intentionally
    /// idempotent: rerunning it updates only the generated transport prefabs/controller and
    /// never touches the canonical Man/Woman prefabs or source art.
    /// </summary>
    public static class TransportPrefabAuthoring
    {
        private const string HorseSource = "Assets/Core/Travel/Wagon Cargo Horse.prefab";
        private const string WagonSource = "Assets/Environment/Props/Carriage_a.prefab";
        private const string HorsePrefab = "Assets/Characters/Horse.prefab";
        private const string WagonPrefab = "Assets/Characters/Wagon.prefab";
        private const string ControllerPath = "Assets/Characters/Animations/Transport Anim Controller.controller";
        private const string DrivingFbx = "Assets/Characters/Animations/X Bot@Driving.fbx";

        public static void BuildTransportAssets()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EnsurePrefab(HorseSource, HorsePrefab, true);
            EnsurePrefab(WagonSource, WagonPrefab, false);
            ConfigureDrivingImport();
            BuildController();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("TransportPrefabAuthoring: generated Horse/Wagon prefabs and transport controller.");
        }

        private static void EnsurePrefab(string sourcePath, string targetPath, bool horse)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("Missing transport source prefab: " + sourcePath);

            GameObject instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null) throw new InvalidOperationException("Could not instantiate: " + sourcePath);
            GameObject root = new GameObject(horse ? "Horse" : "Wagon");
            try
            {
                Transform model = horse ? FindNamed(instance.transform, "Horse") : instance.transform;
                if (model == null) throw new InvalidOperationException("Source has no Horse model: " + sourcePath);
                GameObject modelCopy = Object.Instantiate(model.gameObject);
                modelCopy.name = "Model";
                modelCopy.transform.SetParent(root.transform, false);

                if (horse)
                {
                    var view = root.AddComponent<HorseWorldView>();
                    Transform rider = AddAnchor(root.transform, "RiderMount", new Vector3(0f, 1.45f, -0.15f));
                    Transform left = AddAnchor(root.transform, "DismountLeft", new Vector3(-0.65f, 0.05f, 0f));
                    Transform right = AddAnchor(root.transform, "DismountRight", new Vector3(0.65f, 0.05f, 0f));
                    Transform hitch = AddAnchor(root.transform, "HitchAnchor", new Vector3(0f, 0.8f, -1.2f));
                    view.ConfigureAnchors(rider, left, right, hitch);
                }
                else
                {
                    var view = root.AddComponent<WagonWorldView>();
                    Transform driver = AddAnchor(root.transform, "DriverSeat", new Vector3(0f, 1.25f, 0.5f));
                    Transform hitch = AddAnchor(root.transform, "HitchPoint", new Vector3(0f, 0.65f, -1.5f));
                    Transform cargo = AddAnchor(root.transform, "CargoRoot", new Vector3(0f, 0.85f, 0f));
                    Transform bounds = AddAnchor(root.transform, "LoadBounds", new Vector3(0f, 0.85f, 0f));
                    view.ConfigureAnchors(driver, hitch, cargo, bounds);
                }
                Animator animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                animator.applyRootMotion = false;
                root.AddComponent<TransportAnimatorState>();
                EnsureFolder(Path.GetDirectoryName(targetPath).Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(instance);
            }
        }

        private static Transform AddAnchor(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamed(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void ConfigureDrivingImport()
        {
            AssetImporter importer = AssetImporter.GetAtPath(DrivingFbx);
            if (importer is ModelImporter model)
            {
                // Preserve Unity's detected rig when the source requires Generic, but always
                // disable root-motion baking for this visual-only driving clip.
                model.importAnimation = true;
                model.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
                model.SaveAndReimport();
            }
        }

        private static void BuildController()
        {
            EnsureFolder("Assets/Characters/Animations");
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            }
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AddOrGetState(machine, "Idle", null);
            AnimationClip driving = LoadFirstClip(DrivingFbx);
            AddOrGetState(machine, "DrivingWagon", driving);
            AddOrGetState(machine, "MountedHorse", driving);
            EditorUtility.SetDirty(controller);
        }

        private static AnimatorState AddOrGetState(AnimatorStateMachine machine, string name, Motion motion)
        {
            foreach (ChildAnimatorState child in machine.states)
                if (child.state.name == name) { child.state.motion = motion; return child.state; }
            AnimatorState state = machine.AddState(name);
            state.motion = motion;
            return state;
        }

        private static AnimationClip LoadFirstClip(string path)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase)) return clip;
            return null;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
