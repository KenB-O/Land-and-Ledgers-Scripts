using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass IX WS6 (A1-03): durable run state for scenario objectives —
    /// which scenario is running and which scenarios have FAILED. Before
    /// this pass a missed deadline was invisible: goals simply stayed
    /// incomplete forever and no surface could say "this run failed".
    /// Persisted in hub state (SystemsSaveDto.scenarioRunState, additive).
    /// </summary>
    [Serializable]
    public sealed class ScenarioRunState
    {
        public string ScenarioId = string.Empty;
        /// <summary>Scenario ids that have failed. A scenario id appears at most once.</summary>
        public List<string> FailedScenarioIds = new List<string>();
        /// <summary>Goal ids whose missed deadline caused the failure(s), in failure order.</summary>
        public List<string> FailedGoalIds = new List<string>();
        /// <summary>
        /// Pass IX review R-08 (additive): goal ids LATCHED complete — a
        /// dated qualifier whose predicate held at any evaluation on or
        /// before its deadline is complete for the rest of the run, even if
        /// the predicate later turns false (assets can be sold after being
        /// owned by the deadline; the goal was still met).
        /// </summary>
        public List<string> LatchedCompleteGoalIds = new List<string>();

        public ScenarioRunState Clone()
        {
            return new ScenarioRunState
            {
                ScenarioId = ScenarioId ?? string.Empty,
                FailedScenarioIds = new List<string>(FailedScenarioIds ?? new List<string>()),
                FailedGoalIds = new List<string>(FailedGoalIds ?? new List<string>()),
                LatchedCompleteGoalIds = new List<string>(LatchedCompleteGoalIds ?? new List<string>()),
            };
        }
    }

    /// <summary>
    /// Pass IX WS6 (A1-03): event sink for scenario failure. Static by
    /// design (a sink, like other event surfaces in this codebase section);
    /// subscribers must unsubscribe or tests must call ClearSubscribers.
    /// </summary>
    public static class ScenarioRunStateEvents
    {
        /// <summary>(scenarioId, goalId) — fired exactly once per failed scenario.</summary>
        public static event Action<string, string> ScenarioFailed;

        public static void EmitScenarioFailed(string scenarioId, string goalId)
        {
            ScenarioFailed?.Invoke(scenarioId ?? string.Empty, goalId ?? string.Empty);
        }

        public static void ClearSubscribers()
        {
            ScenarioFailed = null;
        }
    }

    /// <summary>
    /// Pass IX WS6 (A1-03): headline snapshot for HUD/summary surfaces.
    /// DeadlineDayIndex/IsFailed/FailedCount are the additive failure-state
    /// fields; the rest mirrors the pre-existing headline shape (title,
    /// progress, next goal).
    /// </summary>
    [Serializable]
    public sealed class ScenarioHeadlineSnapshot
    {
        public string ScenarioId = string.Empty;
        public string Title = string.Empty;
        public int CompletedGoals;
        public int TotalGoals;
        public string NextGoalId = string.Empty;
        /// <summary>Deadline day of the next goal; -1 when it has none.</summary>
        public int DeadlineDayIndex = -1;
        /// <summary>True when this scenario has entered the failed set.</summary>
        public bool IsFailed;
        /// <summary>Number of goals whose missed deadline has been recorded.</summary>
        public int FailedCount;
    }

    /// <summary>
    /// Pass IX WS6 (A1-03): evaluates dated scenario qualifiers against the
    /// run state. A qualifier is a goal predicate with a deadline day: when
    /// the current day is past the deadline and the predicate does not hold,
    /// the scenario fails — recorded in <see cref="ScenarioRunState"/>
    /// exactly once and announced through
    /// <see cref="ScenarioRunStateEvents.ScenarioFailed"/> exactly once.
    /// Failure is sticky: a later qualification does not un-fail the run
    /// (ContinueFailed semantics — the run is preserved, not regenerated).
    /// </summary>
    public sealed class ScenarioObjectiveService
    {
        /// <summary>The First Ledger scenario id as recorded by this service.</summary>
        public const string FirstLedgerScenarioId = "the-first-ledger";

        private sealed class DatedQualifier
        {
            public string GoalId = string.Empty;
            public int DeadlineDayIndex;
            public Func<bool> Predicate;
        }

        private readonly List<DatedQualifier> qualifiers = new List<DatedQualifier>();
        private ScenarioRunState state = new ScenarioRunState();

        public ScenarioRunState State => state;
        public string ScenarioId => state.ScenarioId;
        public bool IsFailed =>
            !string.IsNullOrEmpty(state.ScenarioId) && state.FailedScenarioIds.Contains(state.ScenarioId);

        public void BeginScenario(string scenarioId)
        {
            state = new ScenarioRunState { ScenarioId = scenarioId ?? string.Empty };
            qualifiers.Clear();
        }

        public void RegisterDatedQualifier(string goalId, int deadlineDayIndex, Func<bool> predicate)
        {
            if (string.IsNullOrEmpty(goalId) || predicate == null) return;
            qualifiers.Add(new DatedQualifier
            {
                GoalId = goalId,
                DeadlineDayIndex = deadlineDayIndex,
                Predicate = predicate,
            });
        }

        /// <summary>
        /// Evaluates all dated qualifiers at a day. Returns true when this
        /// call newly failed the scenario.
        /// Pass IX review R-08: a qualifier whose predicate holds at ANY
        /// evaluation on or before its deadline is latched complete in the
        /// persisted run state and can never fail afterwards — meeting a
        /// goal by its deadline is a fact about the run, not about the
        /// predicate's value whenever evaluation happens to occur. Only an
        /// unlatched qualifier past its deadline, with its predicate false,
        /// fails the run.
        /// </summary>
        public bool Evaluate(int dayIndex)
        {
            if (string.IsNullOrEmpty(state.ScenarioId) || IsFailed) return false;
            state.LatchedCompleteGoalIds ??= new List<string>();
            for (int i = 0; i < qualifiers.Count; i++)
            {
                DatedQualifier qualifier = qualifiers[i];
                if (state.LatchedCompleteGoalIds.Contains(qualifier.GoalId)) continue;
                bool qualifies;
                try { qualifies = qualifier.Predicate(); }
                catch { qualifies = false; }
                if (qualifies)
                {
                    // Latched on or before the deadline by the rule above;
                    // a qualifier first observed holding after its deadline
                    // is likewise complete now and latches the same way.
                    state.LatchedCompleteGoalIds.Add(qualifier.GoalId);
                    continue;
                }
                if (dayIndex <= qualifier.DeadlineDayIndex) continue;

                state.FailedGoalIds.Add(qualifier.GoalId);
                state.FailedScenarioIds.Add(state.ScenarioId);
                ScenarioRunStateEvents.EmitScenarioFailed(state.ScenarioId, qualifier.GoalId);
                return true;
            }
            return false;
        }

        public ScenarioRunState CaptureRunState()
        {
            return state.Clone();
        }

        /// <summary>Clear-then-load, matching the save conventions of the other hub sections.</summary>
        public void LoadRunState(ScenarioRunState saved)
        {
            state = saved != null ? saved.Clone() : new ScenarioRunState();
        }

        /// <summary>
        /// Builds the headline snapshot, populating the failure fields from
        /// the run state: IsFailed/FailedCount come from the persisted
        /// failed set, DeadlineDayIndex from the next goal's dated qualifier.
        /// </summary>
        public ScenarioHeadlineSnapshot BuildHeadlineSnapshot(
            string title, int completedGoals, int totalGoals, string nextGoalId)
        {
            int deadline = -1;
            for (int i = 0; i < qualifiers.Count; i++)
            {
                if (qualifiers[i].GoalId == nextGoalId)
                {
                    deadline = qualifiers[i].DeadlineDayIndex;
                    break;
                }
            }
            return new ScenarioHeadlineSnapshot
            {
                ScenarioId = state.ScenarioId,
                Title = title ?? string.Empty,
                CompletedGoals = completedGoals,
                TotalGoals = totalGoals,
                NextGoalId = nextGoalId ?? string.Empty,
                DeadlineDayIndex = deadline,
                IsFailed = IsFailed,
                FailedCount = state.FailedGoalIds.Count,
            };
        }
    }
}
