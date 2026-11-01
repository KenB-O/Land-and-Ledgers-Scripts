using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-1: live state of one scenario run. Completion flags and tunable overrides live
    /// HERE, never in the ScenarioAsset — stopping play mode must never corrupt the authored
    /// definition (DEV-3 stop-play rules). The asset is read at bootstrap; this is written
    /// at runtime.
    /// </summary>
    public sealed class ScenarioRuntimeState
    {
        public ScenarioAsset Asset { get; }

        private readonly Dictionary<string, bool> goalCompleted = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> objectiveCompleted = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> objectiveTextOverrides = new Dictionary<string, string>();
        private readonly Dictionary<string, TunableValue> tunableOverrides = new Dictionary<string, TunableValue>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        private readonly List<string> diagnostics = new List<string>();

        public ScenarioRuntimeState(ScenarioAsset asset)
        {
            Asset = asset ?? throw new ArgumentNullException(nameof(asset));

            foreach (ScenarioGoal goal in asset.Goals)
            {
                goalCompleted[goal.GoalId] = false;
            }

            foreach (ScenarioObjective objective in asset.Objectives)
            {
                objectiveCompleted[objective.ObjectiveId] = false;
            }
        }

        public bool IsGoalCompleted(string goalId)
        {
            return goalCompleted.TryGetValue(goalId, out bool done) && done;
        }

        public bool IsObjectiveCompleted(string objectiveId)
        {
            return objectiveCompleted.TryGetValue(objectiveId, out bool done) && done;
        }

        public List<string> CaptureCompletedGoalIds()
        {
            var completed = new List<string>();
            foreach (KeyValuePair<string, bool> pair in goalCompleted)
            {
                if (pair.Value)
                {
                    completed.Add(pair.Key);
                }
            }

            return completed;
        }

        public List<string> CaptureCompletedObjectiveIds()
        {
            var completed = new List<string>();
            foreach (KeyValuePair<string, bool> pair in objectiveCompleted)
            {
                if (pair.Value)
                {
                    completed.Add(pair.Key);
                }
            }

            return completed;
        }

        internal void RestoreCompletedGoalIds(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            foreach (string id in ids)
            {
                if (!string.IsNullOrWhiteSpace(id) && goalCompleted.ContainsKey(id))
                {
                    goalCompleted[id] = true;
                }
            }
        }

        internal void RestoreCompletedObjectiveIds(IEnumerable<string> ids)
        {
            if (ids == null)
            {
                return;
            }

            foreach (string id in ids)
            {
                if (!string.IsNullOrWhiteSpace(id) && objectiveCompleted.ContainsKey(id))
                {
                    objectiveCompleted[id] = true;
                }
            }
        }

        public string GetObjectiveText(string objectiveId)
        {
            if (objectiveTextOverrides.TryGetValue(objectiveId, out string overrideText))
            {
                return overrideText;
            }

            foreach (ScenarioObjective objective in Asset.Objectives)
            {
                if (objective.ObjectiveId == objectiveId)
                {
                    return objective.Text;
                }
            }

            foreach (ScenarioObjective objective in runtimeObjectives)
            {
                if (objective.ObjectiveId == objectiveId)
                {
                    return objective.Text;
                }
            }

            return string.Empty;
        }

        public void AddDiagnostic(string message)
        {
            diagnostics.Add(message);
        }

        internal void SetGoalState(string goalId, bool completed)
        {
            goalCompleted[goalId] = completed;
        }

        internal void AddObjectiveTextOverride(string objectiveId, string newText)
        {
            objectiveTextOverrides[objectiveId] = newText ?? string.Empty;
        }

        internal bool HasObjective(string objectiveId)
        {
            if (objectiveCompleted.ContainsKey(objectiveId))
            {
                return true;
            }

            return false;
        }

        internal void AddRuntimeObjective(ScenarioObjective objective)
        {
            objectiveCompleted[objective.ObjectiveId] = false;
            runtimeObjectives.Add(objective);
        }

        internal void SetTunableOverride(string key, TunableValue value)
        {
            tunableOverrides[key] = value;
        }

        internal bool TryGetTunableOverride(string key, out TunableValue value)
        {
            return tunableOverrides.TryGetValue(key, out value);
        }

        private readonly List<ScenarioObjective> runtimeObjectives = new List<ScenarioObjective>();
    }

    /// <summary>
    /// DEV-1: the scenario registry and live-edit authority. Pure C# — no MonoBehaviour —
    /// so EditMode tests can exercise registration, validation, goal/objective edits and
    /// the tunable write policy without entering play mode.
    ///
    /// WRITE POLICY (design notes rule 1): live edits apply to the ScenarioRuntimeState
    /// override by default. Writing back to the authored asset requires the explicit
    /// persistToAsset flag, is recorded in the audit log, and never happens silently.
    /// </summary>
    public sealed class ScenarioService
    {
        private readonly Dictionary<string, ScenarioAsset> registry = new Dictionary<string, ScenarioAsset>();
        private readonly List<string> auditLog = new List<string>();

        public IReadOnlyList<string> AuditLog => auditLog;
        public ScenarioRuntimeState ActiveState { get; private set; }
        public bool HasActiveScenario => ActiveState != null;

        /// <summary>
        /// Registers a scenario asset. Duplicate ids are rejected with a reason — the
        /// registry never silently overwrites.
        /// </summary>
        public bool RegisterScenario(ScenarioAsset asset, out string rejectionReason)
        {
            rejectionReason = null;

            if (asset == null)
            {
                rejectionReason = "Cannot register a null scenario asset.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(asset.ScenarioId))
            {
                rejectionReason = "Scenario asset has an empty ScenarioId.";
                return false;
            }

            if (registry.ContainsKey(asset.ScenarioId))
            {
                rejectionReason = $"Duplicate scenario id '{asset.ScenarioId}': already registered. Rename the asset.";
                return false;
            }

            registry[asset.ScenarioId] = asset;
            auditLog.Add($"Registered scenario '{asset.ScenarioId}' ({asset.DisplayName}).");
            return true;
        }

        public ScenarioAsset GetScenario(string scenarioId)
        {
            return registry.TryGetValue(scenarioId, out ScenarioAsset asset) ? asset : null;
        }

        public List<string> ListScenarioIds()
        {
            return new List<string>(registry.Keys);
        }

        /// <summary>
        /// Begins a scenario run: validates the asset (loudly — problems become diagnostics,
        /// invalid assets refuse to bootstrap), warns when no player household is declared
        /// (GHOST-DEF-006), and seeds fresh runtime state.
        /// </summary>
        public bool BeginScenario(string scenarioId, out string rejectionReason)
        {
            rejectionReason = null;

            if (!registry.TryGetValue(scenarioId, out ScenarioAsset asset))
            {
                rejectionReason = $"Unknown scenario id '{scenarioId}'. Registered: {string.Join(", ", registry.Keys)}.";
                return false;
            }

            List<string> problems = asset.Validate();
            if (problems.Count > 0)
            {
                rejectionReason = $"Scenario '{scenarioId}' failed validation: {string.Join(" | ", problems)}";
                return false;
            }

            var state = new ScenarioRuntimeState(asset);

            if (asset.PlayerHousehold == null || !asset.PlayerHousehold.IsDeclared)
            {
                state.AddDiagnostic(
                    $"WARNING: scenario '{scenarioId}' declares no player household founders. " +
                    "GHOST-DEF-006 requires the player household to exist — declare founders on the asset.");
            }

            ActiveState = state;
            auditLog.Add($"Began scenario '{scenarioId}'.");
            return true;
        }

        public void EndScenario(string reason)
        {
            if (ActiveState != null)
            {
                auditLog.Add($"Ended scenario '{ActiveState.Asset.ScenarioId}': {reason}");
                ActiveState = null;
            }
        }

        // ---- Live editing (puppet master) ----

        public bool SetGoalCompleted(string goalId, bool completed, out string rejectionReason)
        {
            rejectionReason = null;

            if (!RequireActive(out rejectionReason))
            {
                return false;
            }

            bool known = false;
            foreach (ScenarioGoal goal in ActiveState.Asset.Goals)
            {
                if (goal.GoalId == goalId)
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                rejectionReason = $"Goal '{goalId}' is not declared by scenario '{ActiveState.Asset.ScenarioId}'.";
                return false;
            }

            // Completion state lives in ScenarioRuntimeState (never in the asset).
            ActiveState.SetGoalState(goalId, completed);
            auditLog.Add($"Goal '{goalId}' marked {(completed ? "COMPLETED" : "reopened")} (runtime override).");
            return true;
        }

        public bool RetargetObjectiveText(string objectiveId, string newText, out string rejectionReason)
        {
            rejectionReason = null;

            if (!RequireActive(out rejectionReason))
            {
                return false;
            }

            bool known = false;
            foreach (ScenarioObjective objective in ActiveState.Asset.Objectives)
            {
                if (objective.ObjectiveId == objectiveId)
                {
                    known = true;
                    break;
                }
            }

            if (!known)
            {
                rejectionReason = $"Objective '{objectiveId}' is not declared by scenario '{ActiveState.Asset.ScenarioId}'.";
                return false;
            }

            ActiveState.AddObjectiveTextOverride(objectiveId, newText);
            auditLog.Add($"Objective '{objectiveId}' reworded at runtime (override; asset untouched).");
            return true;
        }

        public bool AddObjectiveRuntime(string objectiveId, string text, string linkedGoalId, out string rejectionReason)
        {
            rejectionReason = null;

            if (!RequireActive(out rejectionReason))
            {
                return false;
            }

            if (ActiveState.HasObjective(objectiveId))
            {
                rejectionReason = $"Objective '{objectiveId}' already exists in this run.";
                return false;
            }

            ActiveState.AddRuntimeObjective(new ScenarioObjective(objectiveId, text, linkedGoalId));
            auditLog.Add($"Objective '{objectiveId}' ADDED at runtime (override; asset untouched).");
            return true;
        }

        /// <summary>
        /// The tunable write policy. persistToAsset=false (default): the value lands in the
        /// runtime override — the authored asset is untouched and the change evaporates at
        /// stop-play (documented DEV-3 behavior). persistToAsset=true: explicit, audit-logged
        /// write-back to the asset.
        /// </summary>
        public bool SetTunable(string key, TunableValue value, bool persistToAsset, out string rejectionReason)
        {
            rejectionReason = null;

            if (!RequireActive(out rejectionReason))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                rejectionReason = "Tunable key is empty.";
                return false;
            }

            if (persistToAsset)
            {
                ActiveState.Asset.WriteTunableToAsset(key, value, auditLog);
            }
            else
            {
                ActiveState.SetTunableOverride(key, value);
                auditLog.Add($"Tunable '{key}' set to '{value}' (RUNTIME OVERRIDE — asset untouched, reverts at stop-play).");
            }

            return true;
        }

        public TunableValue GetEffectiveTunable(string key)
        {
            if (ActiveState == null)
            {
                return null;
            }

            if (ActiveState.TryGetTunableOverride(key, out TunableValue overrideValue))
            {
                return overrideValue;
            }

            foreach (TunableValue tunable in ActiveState.Asset.Tunables)
            {
                if (tunable.Key == key)
                {
                    return tunable;
                }
            }

            return null;
        }

        private bool RequireActive(out string rejectionReason)
        {
            rejectionReason = null;

            if (ActiveState == null)
            {
                rejectionReason = "No active scenario. Begin a scenario first.";
                return false;
            }

            return true;
        }
    }
}
