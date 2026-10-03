using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-1: a scenario goal — a durable win/aim condition declared by the scenario asset.
    /// Runtime completion state lives in ScenarioRuntimeState, never in the asset, so
    /// stopping play mode never corrupts the authored definition (DEV-3 rule 1).
    /// </summary>
    [Serializable]
    public sealed class ScenarioGoal
    {
        [SerializeField]
        private string goalId = string.Empty;

        [SerializeField]
        private string text = string.Empty;

        [SerializeField]
        private string targetText = string.Empty;

        [SerializeField]
        private bool optional;

        public string GoalId => goalId;
        public string Text => text;
        public string TargetText => targetText;
        public bool Optional => optional;

        public ScenarioGoal(string goalId, string text, string targetText = "", bool optional = false)
        {
            this.goalId = goalId ?? string.Empty;
            this.text = text ?? string.Empty;
            this.targetText = targetText ?? string.Empty;
            this.optional = optional;
        }
    }

    /// <summary>
    /// DEV-1: a scenario objective — a concrete step, optionally linked to a goal.
    /// Objectives are the live-editable unit: the puppet master can reword, add, or
    /// retire them at runtime (design notes: "change goals or tasks, objectives...
    /// on the fly").
    /// </summary>
    [Serializable]
    public sealed class ScenarioObjective
    {
        [SerializeField]
        private string objectiveId = string.Empty;

        [SerializeField]
        private string linkedGoalId = string.Empty;

        [SerializeField]
        private string text = string.Empty;

        public string ObjectiveId => objectiveId;
        public string LinkedGoalId => linkedGoalId;
        public string Text => text;

        public ScenarioObjective(string objectiveId, string text, string linkedGoalId = "")
        {
            this.objectiveId = objectiveId ?? string.Empty;
            this.text = text ?? string.Empty;
            this.linkedGoalId = linkedGoalId ?? string.Empty;
        }

        public void RetargetText(string newText)
        {
            text = newText ?? string.Empty;
        }
    }

    /// <summary>
    /// DEV-1: which population the scenario counts (Tech X §2.1: "Scenario objectives
    /// must specify inclusion rules"). Population is a read model over Persons — these
    /// flags select which read-model slices count for this scenario's goals.
    /// </summary>
    [Serializable]
    public sealed class PopulationInclusionRules
    {
        [SerializeField]
        private bool includePlayerHousehold = true;

        [SerializeField]
        private bool includeTownResidents;

        [SerializeField]
        private bool includeRuralServicePopulation;

        [SerializeField]
        private bool includeTransients;

        [SerializeField]
        private string notes = string.Empty;

        public bool IncludePlayerHousehold => includePlayerHousehold;
        public bool IncludeTownResidents => includeTownResidents;
        public bool IncludeRuralServicePopulation => includeRuralServicePopulation;
        public bool IncludeTransients => includeTransients;
        public string Notes => notes;
    }

    /// <summary>
    /// DEV-1: a named tunable value on the scenario asset. The puppet master tweaks these
    /// live; the write policy (runtime-override vs asset write-back) is explicit per call
    /// (ScenarioService.SetTunable) — never silent (design notes rule 1).
    /// </summary>
    [Serializable]
    public sealed class TunableValue
    {
        public enum ValueKind
        {
            Float = 0,
            Int = 1,
            Text = 2,
        }

        [SerializeField]
        private string key = string.Empty;

        [SerializeField]
        private ValueKind kind;

        [SerializeField]
        private float floatValue;

        [SerializeField]
        private int intValue;

        [SerializeField]
        private string textValue = string.Empty;

        [SerializeField]
        private string description = string.Empty;

        public string Key => key;
        public ValueKind Kind => kind;
        public string Description => description;

        public float FloatValue => floatValue;
        public int IntValue => intValue;
        public string TextValue => textValue;

        public TunableValue(string key, float value, string description = "")
        {
            this.key = key ?? string.Empty;
            kind = ValueKind.Float;
            floatValue = value;
            this.description = description ?? string.Empty;
        }

        public TunableValue(string key, int value, string description = "")
        {
            this.key = key ?? string.Empty;
            kind = ValueKind.Int;
            intValue = value;
            this.description = description ?? string.Empty;
        }

        public TunableValue(string key, string value, string description = "")
        {
            this.key = key ?? string.Empty;
            kind = ValueKind.Text;
            textValue = value ?? string.Empty;
            this.description = description ?? string.Empty;
        }

        public void SetFloat(float value)
        {
            kind = ValueKind.Float;
            floatValue = value;
        }

        public void SetInt(int value)
        {
            kind = ValueKind.Int;
            intValue = value;
        }

        public void SetText(string value)
        {
            kind = ValueKind.Text;
            textValue = value ?? string.Empty;
        }

        public override string ToString()
        {
            switch (kind)
            {
                case ValueKind.Float: return floatValue.ToString("0.###");
                case ValueKind.Int: return intValue.ToString();
                default: return textValue;
            }
        }
    }

    /// <summary>
    /// DEV-1: declaration of the player household for a scenario (GHOST-DEF-006: the
    /// player household must exist in the simulation). Founder display names are resolved
    /// to PersonIds at bootstrap; the ScenarioDirector is what declares it (design notes).
    /// </summary>
    [Serializable]
    public sealed class PlayerHouseholdDeclaration
    {
        [SerializeField]
        private string scenarioPlayerId = "player";

        [SerializeField]
        private string householdName = "Player Household";

        [SerializeField]
        private List<string> founderDisplayNames = new List<string>();

        public string ScenarioPlayerId => scenarioPlayerId;
        public string HouseholdName => householdName;
        public IReadOnlyList<string> FounderDisplayNames => founderDisplayNames;

        public bool IsDeclared => founderDisplayNames != null && founderDisplayNames.Count > 0;
    }
}
