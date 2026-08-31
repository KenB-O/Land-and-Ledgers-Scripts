using System.IO;
using LandLedgers.CameraSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LandLedgers.Editor
{
    public static class StrategyCameraSetupUtility
    {
        private const string SettingsPath = "Assets/Core/Camera/DefaultStrategyCameraSettings.asset";
        private const string PrefabPath = "Assets/Core/Camera/StrategyCameraRig.prefab";

        [MenuItem("Land & Ledgers/Camera/Create Strategy Camera Rig In Scene")]
        public static void CreateStrategyCameraRigInScene()
        {
            StrategyCameraSettings settings = GetOrCreateSettingsAsset();
            GameObject rig = BuildRig(settings, Camera.main);

            Undo.RegisterCreatedObjectUndo(rig, "Create Strategy Camera Rig");
            Selection.activeGameObject = rig;
            EditorSceneManager.MarkSceneDirty(rig.scene);
        }

        [MenuItem("Land & Ledgers/Camera/Create Default Strategy Camera Settings")]
        public static void CreateDefaultSettings()
        {
            Selection.activeObject = GetOrCreateSettingsAsset();
        }

        [MenuItem("Land & Ledgers/Camera/Repair Selected Strategy Camera Rig")]
        public static void RepairSelectedStrategyCameraRig()
        {
            StrategyCameraController controller = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponentInParent<StrategyCameraController>()
                : null;

            if (controller == null)
            {
                Debug.LogWarning("Select a StrategyCameraController rig or one of its children.");
                return;
            }

            RepairRig(controller, GetOrCreateSettingsAsset());
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        [MenuItem("Land & Ledgers/Camera/Create Or Update Strategy Camera Prefab")]
        public static void CreateOrUpdatePrefab()
        {
            StrategyCameraSettings settings = GetOrCreateSettingsAsset();
            GameObject rig = BuildRig(settings, null);

            string directory = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PrefabUtility.SaveAsPrefabAsset(rig, PrefabPath);
            Object.DestroyImmediate(rig);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        public static void BootstrapDefaultSceneAndAssets()
        {
            StrategyCameraSettings settings = GetOrCreateSettingsAsset();
            CreateOrUpdatePrefab();

            string scenePath = File.Exists("Assets/Main Scene.unity")
                ? "Assets/Main Scene.unity"
                : "Assets/OutdoorsScene.unity";
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene(scenePath);
            StrategyCameraController controller = Object.FindAnyObjectByType<StrategyCameraController>();

            if (controller == null)
            {
                BuildRig(settings, Camera.main);
            }
            else
            {
                RepairRig(controller, settings);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        public static StrategyCameraSettings GetOrCreateSettingsAsset()
        {
            StrategyCameraSettings settings = AssetDatabase.LoadAssetAtPath<StrategyCameraSettings>(SettingsPath);
            if (settings != null)
            {
                return settings;
            }

            string directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            settings = ScriptableObject.CreateInstance<StrategyCameraSettings>();
            settings.Sanitize();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return settings;
        }

        public static GameObject BuildRig(StrategyCameraSettings settings, Camera cameraToUse)
        {
            GameObject root = new("Strategy Camera Rig");
            root.transform.position = settings.defaultFocusPosition;

            StrategyCameraBounds bounds = root.AddComponent<StrategyCameraBounds>();
            bounds.SetWorldBounds(settings.fallbackBoundsCenter, settings.fallbackBoundsSize, settings.fallbackSoftMargin);

            StrategyCameraController controller = root.AddComponent<StrategyCameraController>();

            GameObject yaw = new("YawPivot");
            yaw.transform.SetParent(root.transform, false);

            GameObject pitch = new("PitchPivot");
            pitch.transform.SetParent(yaw.transform, false);

            Camera camera = cameraToUse != null ? cameraToUse : CreateNewCamera();
            GameObject cameraObject = camera.gameObject;
            cameraObject.name = "Main Camera";
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(pitch.transform, false);

            camera.fieldOfView = settings.fieldOfView;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1500f;

            if (cameraObject.GetComponent<HDAdditionalCameraData>() == null)
            {
                cameraObject.AddComponent<HDAdditionalCameraData>();
            }

            controller.ConfigureRigReferences(settings, yaw.transform, pitch.transform, camera, bounds);
            return root;
        }

        private static Camera CreateNewCamera()
        {
            GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener), typeof(HDAdditionalCameraData));
            return cameraObject.GetComponent<Camera>();
        }

        private static void RepairRig(StrategyCameraController controller, StrategyCameraSettings settings)
        {
            Transform root = controller.transform;
            StrategyCameraBounds bounds = controller.BoundsSource != null ? controller.BoundsSource : root.GetComponent<StrategyCameraBounds>();
            if (bounds == null)
            {
                bounds = root.gameObject.AddComponent<StrategyCameraBounds>();
            }

            Transform yaw = root.Find("YawPivot");
            if (yaw == null)
            {
                yaw = new GameObject("YawPivot").transform;
                Undo.RegisterCreatedObjectUndo(yaw.gameObject, "Create Yaw Pivot");
                yaw.SetParent(root, false);
            }

            Transform pitch = yaw.Find("PitchPivot");
            if (pitch == null)
            {
                pitch = new GameObject("PitchPivot").transform;
                Undo.RegisterCreatedObjectUndo(pitch.gameObject, "Create Pitch Pivot");
                pitch.SetParent(yaw, false);
            }

            Camera camera = controller.ControlledCamera;
            if (camera == null)
            {
                camera = root.GetComponentInChildren<Camera>(true);
            }

            if (camera == null)
            {
                camera = CreateNewCamera();
                Undo.RegisterCreatedObjectUndo(camera.gameObject, "Create Strategy Camera");
                camera.gameObject.tag = "MainCamera";
            }

            if (camera.transform.parent != pitch)
            {
                Undo.SetTransformParent(camera.transform, pitch, "Move Strategy Camera Under Pitch Pivot");
            }

            camera.transform.localPosition = Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;

            if (camera.GetComponent<HDAdditionalCameraData>() == null)
            {
                camera.gameObject.AddComponent<HDAdditionalCameraData>();
            }

            controller.ConfigureRigReferences(settings, yaw, pitch, camera, bounds);
        }
    }
}
