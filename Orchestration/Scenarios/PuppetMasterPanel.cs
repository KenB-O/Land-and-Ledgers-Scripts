using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-2: the runtime puppet master. An IMGUI panel (zero canvas setup — it works the
    /// moment play mode starts) through which Kennedy drives the simulation like a puppet
    /// master: switch scenarios, inspect any entity by id, inject tasks, tweak values,
    /// and control time.
    ///
    /// Toggle with F8 (configurable). Add via the "Land &amp; Ledgers/Scenario/Add Puppet
    /// Master To Scene" menu item. This is development tooling: it reads the same
    /// ScenarioService/TaskAuthority/TimeManager the game uses, and every action it takes
    /// is audit-logged by those services.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuppetMasterPanel : MonoBehaviour
    {
        [SerializeField]
        private KeyCode toggleKey = KeyCode.F8;

        [SerializeField]
        private bool startVisible;

        private enum Tab
        {
            Scenarios,
            Entities,
            Tasks,
            Values,
            Time,
        }

        private bool visible;
        private Tab currentTab;
        private Vector2 scrollPosition;

        // Entities tab state.
        private string entityIdInput = "P0";
        private string entityLookupMessage = string.Empty;
        private object inspectedTarget;
        private string inspectedDisplayName = string.Empty;
        private EntityId inspectedId = EntityId.Invalid;

        // Tasks tab state.
        private string taskDefinitionId = string.Empty;
        private string taskOwnerInput = string.Empty;
        private string taskMessage = string.Empty;

        // Values tab state.
        private string tunableKeyInput = string.Empty;
        private string tunableValueInput = string.Empty;
        private bool tunablePersistToAsset;
        private string tunableMessage = string.Empty;

        // Scenarios tab state.
        private string scenarioMessage = string.Empty;
        private string newObjectiveId = string.Empty;
        private string newObjectiveText = string.Empty;

        private void Awake()
        {
            visible = startVisible;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
            }
        }

        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            GUILayout.Window(
                98701,
                new Rect(10, 10, 460, Mathf.Min(640, Screen.height - 20)),
                DrawWindow,
                "Puppet Master (F8)");
        }

        private void DrawWindow(int windowId)
        {
            ScenarioDirector director = ScenarioDirector.Instance;

            currentTab = (Tab)GUILayout.Toolbar((int)currentTab, new[] { "Scenarios", "Entities", "Tasks", "Values", "Time" });

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);

            switch (currentTab)
            {
                case Tab.Scenarios:
                    DrawScenariosTab(director);
                    break;
                case Tab.Entities:
                    DrawEntitiesTab();
                    break;
                case Tab.Tasks:
                    DrawTasksTab(director);
                    break;
                case Tab.Values:
                    DrawValuesTab(director);
                    break;
                case Tab.Time:
                    DrawTimeTab();
                    break;
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        // ---- Scenarios ----

        private void DrawScenariosTab(ScenarioDirector director)
        {
            if (director == null)
            {
                GUILayout.Label("No ScenarioDirector in the scene.");
                return;
            }

            GUILayout.Label($"Active: {(director.HasActiveScenario ? director.ActiveScenarioId : "<none>")}");

            List<string> ids = director.Service.ListScenarioIds();
            foreach (string id in ids)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(id, GUILayout.Width(220));
                if (GUILayout.Button("Switch", GUILayout.Width(80)))
                {
                    scenarioMessage = director.SwitchScenario(id)
                        ? $"Switched to '{id}'."
                        : $"Switch to '{id}' failed — see console.";
                }

                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(scenarioMessage))
            {
                GUILayout.Label(scenarioMessage);
            }

            if (director.HasActiveScenario)
            {
                GUILayout.Space(8);
                GUILayout.Label("Goals (click to toggle):");
                foreach (ScenarioGoal goal in director.Service.ActiveState.Asset.Goals)
                {
                    bool done = director.Service.ActiveState.IsGoalCompleted(goal.GoalId);
                    if (GUILayout.Button($"[{(done ? "x" : " ")}] {goal.GoalId}: {goal.Text}"))
                    {
                        director.SetGoalCompleted(goal.GoalId, !done);
                    }
                }

                GUILayout.Space(8);
                GUILayout.Label("Add objective on the fly:");
                newObjectiveId = GUILayout.TextField(newObjectiveId);
                newObjectiveText = GUILayout.TextField(newObjectiveText);
                if (GUILayout.Button("Add Objective"))
                {
                    scenarioMessage = director.AddObjective(newObjectiveId, newObjectiveText)
                        ? $"Objective '{newObjectiveId}' added."
                        : "Add failed — see console.";
                    newObjectiveId = string.Empty;
                    newObjectiveText = string.Empty;
                }

                GUILayout.Space(8);
                GUILayout.Label("Audit log (last 10):");
                IReadOnlyList<string> audit = director.Service.AuditLog;
                for (int i = Mathf.Max(0, audit.Count - 10); i < audit.Count; i++)
                {
                    GUILayout.Label("- " + audit[i]);
                }
            }
        }

        // ---- Entities ----

        private void DrawEntitiesTab()
        {
            GUILayout.Label("Find ANY entity by id (e.g. P12, H3, BLD7):");
            GUILayout.BeginHorizontal();
            entityIdInput = GUILayout.TextField(entityIdInput, GUILayout.Width(200));
            if (GUILayout.Button("Inspect", GUILayout.Width(80)))
            {
                InspectEntity();
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(entityLookupMessage))
            {
                GUILayout.Label(entityLookupMessage);
            }

            if (inspectedTarget != null)
            {
                GUILayout.Space(6);
                GUILayout.Label($"Id: {inspectedId}");
                GUILayout.Label($"Name: {inspectedDisplayName}");
                GUILayout.Label($"Type: {inspectedTarget.GetType().FullName}");
                GUILayout.Label($"ToString: {inspectedTarget}");

                if (inspectedTarget is Object unityObject)
                {
                    // Runtime-safe ping: in the editor, clicking this log entry in the
                    // Console pings the object in the Hierarchy. No UnityEditor API here —
                    // editor-only selection lives in PuppetMasterWindow (DEV-2).
                    if (GUILayout.Button("Ping in Console"))
                    {
                        Debug.Log($"[PuppetMaster] Ping {inspectedId} ({inspectedDisplayName}). " +
                                  "Click this log entry to highlight it in the Hierarchy.", unityObject);
                    }
                }
            }

            GUILayout.Space(8);
            GUILayout.Label($"Registered entities: {PuppetEntityLookup.Shared.Count}");
        }

        private void InspectEntity()
        {
            inspectedTarget = null;
            inspectedDisplayName = string.Empty;
            inspectedId = EntityId.Invalid;

            if (!EntityIdInputParser.TryParse(entityIdInput, out EntityId id))
            {
                entityLookupMessage = $"Could not parse '{entityIdInput}'. Use forms like P12, H3, BLD7.";
                return;
            }

            if (PuppetEntityLookup.Shared.TryResolve(id, out object target, out string displayName))
            {
                inspectedTarget = target;
                inspectedDisplayName = displayName;
                inspectedId = id;
                entityLookupMessage = $"Found {id}.";
            }
            else
            {
                entityLookupMessage = $"{id} is not registered in the puppet lookup. " +
                                      "Entities register via PuppetEntityLookup (DEV-3 registrar does this automatically).";
            }
        }

        // ---- Tasks ----

        private void DrawTasksTab(ScenarioDirector director)
        {
            if (director == null || director.TaskAuthority == null)
            {
                GUILayout.Label("TaskAuthority is not injected on the ScenarioDirector.");
                return;
            }

            GUILayout.Label("Inject a task on the fly (TTS-2):");
            GUILayout.Label("TaskDefinition id:");
            taskDefinitionId = GUILayout.TextField(taskDefinitionId);
            GUILayout.Label("Owner entity id (e.g. H3; blank = unassigned pool):");
            taskOwnerInput = GUILayout.TextField(taskOwnerInput);

            if (GUILayout.Button("Enqueue Task"))
            {
                InjectTask(director);
            }

            if (!string.IsNullOrEmpty(taskMessage))
            {
                GUILayout.Label(taskMessage);
            }

            GUILayout.Space(8);
            GUILayout.Label("Open tasks for owner filter (blank = all known owners):");
            // Kept simple: the authority owns the queues; the panel just injects.
            GUILayout.Label("Use the Entities tab to find owner ids.");
        }

        private void InjectTask(ScenarioDirector director)
        {
            taskMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(taskDefinitionId))
            {
                taskMessage = "Enter a TaskDefinition id.";
                return;
            }

            TaskDefinition definition = director.TaskAuthority.GetDefinition(taskDefinitionId.Trim());
            if (definition == null)
            {
                taskMessage = $"Unknown TaskDefinition '{taskDefinitionId}'.";
                return;
            }

            EntityId ownerId = EntityId.Invalid;
            if (!string.IsNullOrWhiteSpace(taskOwnerInput))
            {
                if (!EntityIdInputParser.TryParse(taskOwnerInput, out ownerId))
                {
                    taskMessage = $"Could not parse owner id '{taskOwnerInput}'.";
                    return;
                }
            }

            int dayIndex = TimeManager.Instance != null ? TimeManager.Instance.CurrentAbsoluteDayIndex : 0;
            try
            {
                WorkTask task = director.TaskAuthority.CreateTask(taskDefinitionId.Trim(), ownerId, dayIndex);
                taskMessage = $"Enqueued task {task.TaskId} ('{taskDefinitionId}').";
            }
            catch (System.Exception ex)
            {
                taskMessage = "Enqueue failed: " + ex.Message;
            }
        }

        // ---- Values ----

        private void DrawValuesTab(ScenarioDirector director)
        {
            if (director == null || !director.HasActiveScenario)
            {
                GUILayout.Label("No active scenario.");
                return;
            }

            GUILayout.Label("Scenario tunables (effective values):");
            foreach (TunableValue tunable in director.Service.ActiveState.Asset.Tunables)
            {
                TunableValue effective = director.Service.GetEffectiveTunable(tunable.Key);
                GUILayout.Label($"{tunable.Key} = {effective}  ({tunable.Description})");
            }

            GUILayout.Space(8);
            GUILayout.Label("Set tunable:");
            GUILayout.Label("Key:");
            tunableKeyInput = GUILayout.TextField(tunableKeyInput);
            GUILayout.Label("Value (float/int/text):");
            tunableValueInput = GUILayout.TextField(tunableValueInput);
            tunablePersistToAsset = GUILayout.Toggle(tunablePersistToAsset, "Persist to ASSET (default: runtime override only)");

            if (GUILayout.Button("Apply"))
            {
                ApplyTunable(director);
            }

            if (!string.IsNullOrEmpty(tunableMessage))
            {
                GUILayout.Label(tunableMessage);
            }
        }

        private void ApplyTunable(ScenarioDirector director)
        {
            tunableMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(tunableKeyInput))
            {
                tunableMessage = "Enter a tunable key.";
                return;
            }

            string key = tunableKeyInput.Trim();
            TunableValue value;
            if (float.TryParse(tunableValueInput, out float f))
            {
                value = new TunableValue(key, f);
            }
            else if (int.TryParse(tunableValueInput, out int i))
            {
                value = new TunableValue(key, i);
            }
            else
            {
                value = new TunableValue(key, tunableValueInput);
            }

            tunableMessage = director.SetTunable(key, value, tunablePersistToAsset)
                ? (tunablePersistToAsset ? $"Tunable '{key}' written to ASSET." : $"Tunable '{key}' set (runtime override).")
                : "Set failed — see console.";
        }

        // ---- Time ----

        private void DrawTimeTab()
        {
            TimeManager time = TimeManager.Instance;
            if (time == null)
            {
                GUILayout.Label("No TimeManager in the scene.");
                return;
            }

            GUILayout.Label($"Date: {time.CurrentDayName}, {time.CurrentMonthName} {time.CurrentYear}");
            GUILayout.Label($"Time of day: {time.TimeOfDay01:0.00} | Speed: {time.CurrentSpeed} | Paused: {time.IsPaused}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(time.IsPaused ? "Resume" : "Pause"))
            {
                time.TogglePaused();
            }

            if (GUILayout.Button("1x"))
            {
                time.SetSpeed(SimulationSpeed.Speed1x);
            }

            if (GUILayout.Button("10x"))
            {
                time.SetSpeed(SimulationSpeed.Speed10x);
            }

            if (GUILayout.Button("100x"))
            {
                time.SetSpeed(SimulationSpeed.Speed100x);
            }

            GUILayout.EndHorizontal();

            GUILayout.Label("Time controls drive the shared 24-hour clock (TTS-1 books work minutes against it).");
        }
    }
}
