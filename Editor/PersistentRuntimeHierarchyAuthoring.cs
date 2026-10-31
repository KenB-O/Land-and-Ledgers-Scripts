#if UNITY_EDITOR
using System;
using System.Reflection;
using LandLedgers.Civic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Scenarios.Development;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.UI;
using LandLedgers.World;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LandLedgers.EditorTools
{
    /// <summary>
    /// Authors the long-lived authorities and frequently used UI surfaces that
    /// older Main Scene bootstrap paths created as Play Mode-only GameObjects.
    /// Simulation entities remain owned by their scenario/save authorities.
    /// </summary>
    public static class PersistentRuntimeHierarchyAuthoring
    {
        [MenuItem("Land & Ledgers/Author Persistent Main Scene Hierarchy")]
        public static void AuthorMainScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Persistent hierarchy authoring is available only in Edit Mode.");
                return;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Main Scene")
            {
                Debug.LogError("Open Assets/Main Scene.unity before authoring the persistent hierarchy.");
                return;
            }

            GameObject systems = FindRoot(scene, "Game Systems");
            Canvas canvas = FindUnique<Canvas>(scene, "UI Canvas");
            if (systems == null || canvas == null)
            {
                Debug.LogError("Main Scene requires one Game Systems root and one UI Canvas.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(systems, "Author persistent runtime authorities");
            Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Author persistent runtime UI");

            Transform authorities = EnsureGroup(systems.transform, "Persistent Authorities");
            Transform economy = EnsureGroup(authorities, "Economy and Civic");
            Transform simulation = EnsureGroup(authorities, "Simulation and Persistence");
            Transform interaction = EnsureGroup(authorities, "Interaction");
            Transform development = EnsureGroup(authorities, "Development");

            EnsureRuntimeEntityRoot(simulation);

            CivicFoundationManager civic = EnsureComponent<CivicFoundationManager>(economy, "Civic Foundation");
            SharedBusinessRuntimeManager businesses = EnsureComponent<SharedBusinessRuntimeManager>(economy, "Shared Business Runtime");
            LogisticsRuntimeManager logistics = EnsureComponent<LogisticsRuntimeManager>(economy, "Logistics Runtime");
            TownPulseRuntimeManager townPulse = EnsureComponent<TownPulseRuntimeManager>(economy, "Town Pulse Runtime");
            OpportunityPressureRuntimeManager opportunities = EnsureComponent<OpportunityPressureRuntimeManager>(economy, "Opportunity Pressure Runtime");
            PlayerDebtManager debt = EnsureComponent<PlayerDebtManager>(economy, "Player Debt Manager");
            PlayerPortfolioManager portfolio = EnsureComponent<PlayerPortfolioManager>(economy, "Player Portfolio Manager");

            SaveLoadManager saveLoad = EnsureComponent<SaveLoadManager>(simulation, "Save Load Manager");
            PopulationPathingDirector pathing = EnsureComponent<PopulationPathingDirector>(simulation, "Population Pathing Director");
            FirstSessionGuidanceManager guidance = EnsureComponent<FirstSessionGuidanceManager>(simulation, "First Session Guidance");
            WorldInteractionController worldInteraction = EnsureComponent<WorldInteractionController>(interaction, "World Interaction Controller");
            OwnershipWorkflowController ownership = EnsureComponent<OwnershipWorkflowController>(interaction, "Ownership Workflow Controller");
            AcquisitionSellerMeetingController sellerMeeting = EnsureComponent<AcquisitionSellerMeetingController>(interaction, "Acquisition Seller Meeting Controller");
            EnsureComponent<ScenarioValidationHarness>(development, "Scenario Validation Harness");

            EnsureInputSystemEventSystem(scene, interaction);
            EnsurePopulationAgentRoot(pathing);
            EnsureAuthoredMusicRig(scene);
            EnsureAuthoredPreviewCamera(authorities);
            EnsureAuthoredAcquisitionViews(canvas);
            EnsureAuthoredScenarioStartPanel(scene, canvas);
            EnsureHudSurfaces(scene);
            EnsureAuthoredTooltip(canvas);
            EnsureAuthoredMoneyFeedback(canvas);
            EnsureBusinessCreationUi(scene, businesses);
            WireBootstrapper(scene, civic, businesses, logistics, townPulse, opportunities, debt, portfolio, saveLoad, pathing, guidance, worldInteraction);

            EditorUtility.SetDirty(systems);
            EditorUtility.SetDirty(canvas);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SceneAuthoring] Persistent Main Scene authority and UI hierarchy saved. Runtime-created Persons, Businesses, Tasks, shipments and other scenario state continue to be generated by their simulation authorities.", systems);
        }

        private static void WireBootstrapper(
            Scene scene,
            CivicFoundationManager civic,
            SharedBusinessRuntimeManager businesses,
            LogisticsRuntimeManager logistics,
            TownPulseRuntimeManager townPulse,
            OpportunityPressureRuntimeManager opportunities,
            PlayerDebtManager debt,
            PlayerPortfolioManager portfolio,
            SaveLoadManager saveLoad,
            PopulationPathingDirector pathing,
            FirstSessionGuidanceManager guidance,
            WorldInteractionController worldInteraction)
        {
            FirstLedgerSliceBootstrapper bootstrapper = FindUnique<FirstLedgerSliceBootstrapper>(scene, null);
            if (bootstrapper == null) return;

            SerializedObject serialized = new(bootstrapper);
            SetReference(serialized, "civicFoundation", civic);
            SetReference(serialized, "sharedBusinessRuntime", businesses);
            SetReference(serialized, "logisticsRuntime", logistics);
            SetReference(serialized, "townPulseRuntime", townPulse);
            SetReference(serialized, "opportunityPressureRuntime", opportunities);
            SetReference(serialized, "playerDebtManager", debt);
            SetReference(serialized, "playerPortfolioManager", portfolio);
            SetReference(serialized, "saveLoadManager", saveLoad);
            SetReference(serialized, "populationPathingDirector", pathing);
            SetReference(serialized, "firstSessionGuidance", guidance);
            SetReference(serialized, "worldInteractionController", worldInteraction);
            SetReference(serialized, "townWorld", FindUnique<TownWorldController>(scene, null));
            SetReference(serialized, "pathingManager", FindUnique<PathingManager>(scene, null));
            SetReference(serialized, "timeManager", FindUnique<LandLedgers.Time.TimeManager>(scene, null));
            SetReference(serialized, "populationManager", FindUnique<PopulationManager>(scene, null));
            SetReference(serialized, "storeRuntime", FindUnique<GeneralStoreRuntimeManager>(scene, null));
            SetReference(serialized, "acquisitionMarket", FindUnique<AcquisitionMarketManager>(scene, null));
            SetReference(serialized, "storePanel", FindUnique<GeneralStorePanelController>(scene, null));
            SetReference(serialized, "hudController", FindUnique<LandLedgersHUDController>(scene, null));
            SetReference(serialized, "systemsHub", FindUnique<LandLedgers.Orchestration.Systems.SimulationSystemsHub>(scene, null));
            SetReference(serialized, "preAuthoredTerrainProfile", FindUnique<PreAuthoredTerrainWorldProfile>(scene, null));
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrapper);
        }

        private static void EnsureAuthoredAcquisitionViews(Canvas canvas)
        {
            OwnershipWorkflowView ownership = OwnershipWorkflowView.GetOrCreate(canvas);
            if (ownership != null) ownership.SetVisible(false);
            AcquisitionSellerMeetingView meeting = AcquisitionSellerMeetingView.GetOrCreate(canvas);
            if (meeting != null) meeting.SetVisible(false);
        }

        private static void EnsureAuthoredScenarioStartPanel(Scene scene, Canvas canvas)
        {
            ScenarioStartPanelController controller = FindUnique<ScenarioStartPanelController>(scene, null);
            if (controller == null) return;

            MethodInfo author = typeof(ScenarioStartPanelController).GetMethod("AuthorStartPanelForScene", BindingFlags.Instance | BindingFlags.Public);
            GameObject panel = author?.Invoke(controller, null) as GameObject;
            if (panel != null)
            {
                panel.transform.SetAsLastSibling();
                panel.SetActive(false);
                EditorUtility.SetDirty(panel);
                EditorUtility.SetDirty(controller);
            }
        }

        private static void EnsureHudSurfaces(Scene scene)
        {
            LandLedgersHUDController hud = FindUnique<LandLedgersHUDController>(scene, null);
            if (hud == null) return;

            InvokeEnsureMethod(hud, "EnsureControlHintText");
            InvokeEnsureMethod(hud, "EnsureSystemStatusStrip");
            InvokeEnsureMethod(hud, "EnsureTownPulseAlertText");
            EditorUtility.SetDirty(hud);
        }

        private static void EnsureBusinessCreationUi(Scene scene, SharedBusinessRuntimeManager businesses)
        {
            GeneralStorePanelController management = FindUnique<GeneralStorePanelController>(scene, null);
            ManagementPanelView view = FindUnique<ManagementPanelView>(scene, null);
            if (management == null || view == null || businesses == null)
            {
                Debug.LogWarning("[SceneAuthoring] Business-formation UI remains on its existing bootstrap path because the authored management controller/view/runtime trio is incomplete.");
                return;
            }

            BusinessCreationPanelController creation = management.GetComponent<BusinessCreationPanelController>();
            if (creation == null)
            {
                creation = Undo.AddComponent<BusinessCreationPanelController>(management.gameObject);
            }

            Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Author persistent business formation UI");
            creation.Configure(businesses, management, view);
            EditorUtility.SetDirty(creation);
            EditorUtility.SetDirty(view);
        }

        private static void EnsureInputSystemEventSystem(Scene scene, Transform parent)
        {
            EventSystem[] existing = FindAll<EventSystem>(scene);
            EventSystem eventSystem = existing.Length > 0 ? existing[0] : null;
            if (eventSystem == null)
            {
                GameObject go = CreateChild(parent, "UI Event System");
                eventSystem = Undo.AddComponent<EventSystem>(go);
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                Undo.AddComponent<InputSystemUIInputModule>(eventSystem.gameObject);
            }
        }

        private static void EnsureAuthoredPreviewCamera(Transform parent)
        {
            foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera != null && camera.gameObject.scene == parent.gameObject.scene && camera.gameObject.name == "Property Preview Camera")
                {
                    camera.enabled = false;
                    return;
                }
            }

            GameObject go = CreateChild(parent, "Property Preview Camera");
            Camera preview = Undo.AddComponent<Camera>(go);
            preview.enabled = false;
            preview.clearFlags = CameraClearFlags.SolidColor;
            preview.backgroundColor = new Color(0.17f, 0.19f, 0.17f, 1f);
            preview.allowHDR = false;
            preview.allowMSAA = true;
            preview.useOcclusionCulling = false;
        }

        private static void EnsureRuntimeEntityRoot(Transform parent)
        {
            Transform root = parent.Find("Runtime Entities");
            if (root == null)
            {
                root = CreateChild(parent, "Runtime Entities").transform;
            }
        }

        private static void EnsureAuthoredMusicRig(Scene scene)
        {
            LLMainMusicPlayer player = FindUnique<LLMainMusicPlayer>(scene, null);
            if (player == null) return;

            Transform rig = player.transform.Find("Runtime Music Audio Rig");
            if (rig == null)
            {
                rig = CreateChild(player.transform, "Runtime Music Audio Rig").transform;
            }

            EnsureMusicSource(rig, "Music Source A");
            EnsureMusicSource(rig, "Music Source B");
        }

        private static void EnsureMusicSource(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            GameObject sourceObject = child != null ? child.gameObject : CreateChild(parent, name);
            AudioSource source = sourceObject.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(sourceObject);
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            EditorUtility.SetDirty(source);
        }

        private static void EnsureAuthoredTooltip(Canvas canvas)
        {
            UITooltipController tooltip = canvas.GetComponentInChildren<UITooltipController>(true);
            if (tooltip == null)
            {
                GameObject root = new("Runtime_Tooltip", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
                Undo.RegisterCreatedObjectUndo(root, "Author persistent tooltip UI");
                root.transform.SetParent(canvas.transform, false);
                tooltip = Undo.AddComponent<UITooltipController>(root);
            }

            Undo.RegisterFullObjectHierarchyUndo(tooltip.gameObject, "Author persistent tooltip UI");
            MethodInfo build = typeof(UITooltipController).GetMethod("BuildVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            build?.Invoke(tooltip, new object[] { canvas });
            tooltip.gameObject.SetActive(false);
            EditorUtility.SetDirty(tooltip.gameObject);
            EditorUtility.SetDirty(tooltip);
        }

        private static void EnsureAuthoredMoneyFeedback(Canvas canvas)
        {
            LLMoneyFeedbackOverlay overlay = canvas.GetComponentInChildren<LLMoneyFeedbackOverlay>(true);
            if (overlay == null)
            {
                GameObject root = new("LL_MoneyFeedbackOverlay", typeof(RectTransform), typeof(CanvasGroup));
                Undo.RegisterCreatedObjectUndo(root, "Author persistent money feedback UI");
                root.transform.SetParent(canvas.transform, false);
                overlay = Undo.AddComponent<LLMoneyFeedbackOverlay>(root);
            }

            RectTransform rect = overlay.transform as RectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            CanvasGroup group = overlay.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
                group.ignoreParentGroups = true;
            }
            EditorUtility.SetDirty(overlay.gameObject);
            EditorUtility.SetDirty(overlay);
        }

        private static void EnsurePopulationAgentRoot(PopulationPathingDirector director)
        {
            if (director == null) return;
            Transform root = director.transform.Find("PopulationAgentsRoot");
            if (root == null)
            {
                root = CreateChild(director.transform, "PopulationAgentsRoot").transform;
            }

            SerializedObject serialized = new(director);
            SerializedProperty property = serialized.FindProperty("agentRoot");
            if (property != null)
            {
                property.objectReferenceValue = root;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(director);
        }

        private static void InvokeEnsureMethod(MonoBehaviour target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            method?.Invoke(target, null);
        }

        private static Transform EnsureGroup(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            GameObject group = CreateChild(parent, name);
            return group.transform;
        }

        private static T EnsureComponent<T>(Transform parent, string objectName) where T : Component
        {
            T existing = FindUnique<T>(parent.gameObject.scene, null);
            if (existing != null) return existing;
            GameObject go = CreateChild(parent, objectName);
            return Undo.AddComponent<T>(go);
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            GameObject go = new(name);
            Undo.RegisterCreatedObjectUndo(go, $"Author {name}");
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            return null;
        }

        private static T FindUnique<T>(Scene scene, string objectName) where T : Component
        {
            T found = null;
            foreach (T component in FindAll<T>(scene))
            {
                if (component == null || (!string.IsNullOrEmpty(objectName) && component.gameObject.name != objectName)) continue;
                if (found != null)
                {
                    Debug.LogError($"[SceneAuthoring] Multiple {typeof(T).Name} components exist in Main Scene.");
                    return null;
                }
                found = component;
            }
            return found;
        }

        private static T[] FindAll<T>(Scene scene) where T : Component
        {
            T[] all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var sceneComponents = new System.Collections.Generic.List<T>();
            foreach (T component in all)
            {
                if (component != null && component.gameObject.scene == scene) sceneComponents.Add(component);
            }
            return sceneComponents.ToArray();
        }

        private static void SetReference(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property != null && value != null) property.objectReferenceValue = value;
        }
    }
}
#endif
