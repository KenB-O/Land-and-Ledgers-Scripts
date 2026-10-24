using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XI WS1: one persisted dated-qualifier registration. The live
    /// qualifier predicate is a Func and can never be persisted, so the
    /// run state stores everything needed to REBUILD it after load: the
    /// goal id, its deadline, and a stable predicate key (scenario id
    /// plus goal id, never a delegate or a display string). The scenario
    /// bootstrap rebinds the live predicate from its evaluator on load;
    /// a registration with no rebindable predicate is reported by
    /// RebuildQualifiers, never silently dropped.
    /// </summary>
    [Serializable]
    public sealed class ScenarioQualifierRegistration
    {
        public string GoalId = string.Empty;
        public int DeadlineDayIndex;
        public string PredicateKey = string.Empty;

        public ScenarioQualifierRegistration Clone()
        {
            return new ScenarioQualifierRegistration
            {
                GoalId = GoalId ?? string.Empty,
                DeadlineDayIndex = DeadlineDayIndex,
                PredicateKey = PredicateKey ?? string.Empty,
            };
        }
    }

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
        /// <summary>
        /// Pass XI WS1 (additive): persisted dated-qualifier registrations
        /// (see <see cref="ScenarioQualifierRegistration"/>). Old saves load
        /// with this list empty and rebuild it from live scenario
        /// definitions on first load.
        /// </summary>
        public List<ScenarioQualifierRegistration> QualifierRegistrations = new List<ScenarioQualifierRegistration>();
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
            var clone = new ScenarioRunState
            {
                ScenarioId = ScenarioId ?? string.Empty,
                FailedScenarioIds = new List<string>(FailedScenarioIds ?? new List<string>()),
                FailedGoalIds = new List<string>(FailedGoalIds ?? new List<string>()),
                LatchedCompleteGoalIds = new List<string>(LatchedCompleteGoalIds ?? new List<string>()),
                QualifierRegistrations = new List<ScenarioQualifierRegistration>(),
            };
            foreach (ScenarioQualifierRegistration registration in QualifierRegistrations ?? new List<ScenarioQualifierRegistration>())
            {
                if (registration != null) clone.QualifierRegistrations.Add(registration.Clone());
            }
            return clone;
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
        /// <summary>
        /// Pass XI WS5 (player-facing): the goal whose missed deadline
        /// caused the failure, so a panel can name it instead of showing
        /// every goal as merely incomplete. Empty when not failed.
        /// Additive: older readers ignore it.
        /// </summary>
        public string FailedGoalId = string.Empty;
        /// <summary>
        /// Pass XI WS5: the failed goal's deadline day; -1 when the goal
        /// has no deadline in the run state. Lets a panel say "missed its
        /// day-N deadline" from the snapshot alone.
        /// </summary>
        public int FailedDeadlineDayIndex = -1;
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
            // Pass XI WS1: the registration persists; the predicate does
            // not. Re-registering the same goal updates its persisted
            // deadline and key instead of duplicating the registration,
            // so the pre-existing re-register-after-load pattern keeps
            // working and stays a single registration per goal.
            state.QualifierRegistrations ??= new List<ScenarioQualifierRegistration>();
            foreach (ScenarioQualifierRegistration existing in state.QualifierRegistrations)
            {
                if (existing != null && string.Equals(existing.GoalId, goalId, StringComparison.Ordinal))
                {
                    existing.DeadlineDayIndex = deadlineDayIndex;
                    existing.PredicateKey = PredicateKeyFor(goalId);
                    return;
                }
            }
            state.QualifierRegistrations.Add(new ScenarioQualifierRegistration
            {
                GoalId = goalId,
                DeadlineDayIndex = deadlineDayIndex,
                PredicateKey = PredicateKeyFor(goalId),
            });
        }

        /// <summary>Stable predicate key: scenario id plus goal id, never a delegate or display string.</summary>
        public string PredicateKeyFor(string goalId)
        {
            return (state.ScenarioId ?? string.Empty) + ":" + (goalId ?? string.Empty);
        }

        /// <summary>Persisted registrations (live predicates are rebuilt, never stored).</summary>
        public IReadOnlyList<ScenarioQualifierRegistration> QualifierRegistrations =>
            state.QualifierRegistrations ?? new List<ScenarioQualifierRegistration>();

        /// <summary>
        /// Pass XI WS1: adopts fresh qualifier definitions ONLY when the
        /// run state carries no persisted registrations (old saves, first
        /// load). Persisted registrations always win over definitions, so
        /// a later calibration change cannot silently move a running
        /// scenario's deadlines. Returns true when definitions were adopted.
        /// </summary>
        public bool EnsureQualifierRegistrations(IEnumerable<ScenarioQualifierRegistration> definitions)
        {
            state.QualifierRegistrations ??= new List<ScenarioQualifierRegistration>();
            if (state.QualifierRegistrations.Count > 0) return false;
            if (definitions == null) return false;
            foreach (ScenarioQualifierRegistration definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.GoalId)) continue;
                state.QualifierRegistrations.Add(definition.Clone());
            }
            return true;
        }

        /// <summary>
        /// Pass XI WS1: rebuilds the live qualifier list from the persisted
        /// registrations and the caller's live predicates, keyed by goal
        /// id. The caller is the scenario bootstrap, which owns the
        /// evaluator the predicates read. Returns the goal ids that could
        /// NOT be rebuilt (no live predicate supplied): the caller must
        /// surface those loudly, because an unrebuilt goal can neither
        /// latch nor fail and Evaluate would silently ignore it.
        /// Rebuilding is idempotent and never un-latches or un-fails.
        /// </summary>
        public List<string> RebuildQualifiers(IDictionary<string, Func<bool>> livePredicates)
        {
            var unrebuilt = new List<string>();
            qualifiers.Clear();
            state.QualifierRegistrations ??= new List<ScenarioQualifierRegistration>();
            foreach (ScenarioQualifierRegistration registration in state.QualifierRegistrations)
            {
                if (registration == null || string.IsNullOrEmpty(registration.GoalId)) continue;
                Func<bool> predicate = null;
                if (livePredicates != null) livePredicates.TryGetValue(registration.GoalId, out predicate);
                if (predicate == null)
                {
                    unrebuilt.Add(registration.GoalId);
                    continue;
                }
                qualifiers.Add(new DatedQualifier
                {
                    GoalId = registration.GoalId,
                    DeadlineDayIndex = registration.DeadlineDayIndex,
                    Predicate = predicate,
                });
            }
            return unrebuilt;
        }

        /// <summary>
        /// Pass XI WS1: merges latched goal ids into the run state without
        /// ever removing one (reconciliation never un-completes a goal)
        /// and without touching the failed sets (failure stays sticky).
        /// Returns how many ids were newly added.
        /// </summary>
        public int MergeLatchedGoals(IEnumerable<string> goalIds)
        {
            state.LatchedCompleteGoalIds ??= new List<string>();
            int added = 0;
            if (goalIds == null) return added;
            foreach (string goalId in goalIds)
            {
                if (string.IsNullOrEmpty(goalId)) continue;
                if (state.LatchedCompleteGoalIds.Contains(goalId)) continue;
                state.LatchedCompleteGoalIds.Add(goalId);
                added++;
            }
            return added;
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
            int deadline = DeadlineDayIndexForGoal(nextGoalId);
            string failedGoalId = string.Empty;
            int failedDeadline = -1;
            if (state.FailedGoalIds != null && state.FailedGoalIds.Count > 0)
            {
                failedGoalId = state.FailedGoalIds[0] ?? string.Empty;
                failedDeadline = DeadlineDayIndexForGoal(failedGoalId);
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
                FailedGoalId = failedGoalId,
                FailedDeadlineDayIndex = failedDeadline,
            };
        }

        /// <summary>
        /// Pass XI WS5: one deadline lookup for live qualifiers and
        /// persisted registrations. int.MaxValue is the no-deadline
        /// sentinel used by qualifier definitions (every First Ledger
        /// goal uses it); it reports as -1 on BOTH paths. Before this
        /// fix the live path reported the raw sentinel (2147483647)
        /// while only the post-load fallback normalized it, so an
        /// in-session headline showed an absurd deadline.
        /// </summary>
        private int DeadlineDayIndexForGoal(string goalId)
        {
            if (string.IsNullOrEmpty(goalId)) return -1;
            for (int i = 0; i < qualifiers.Count; i++)
            {
                if (qualifiers[i].GoalId == goalId)
                {
                    int live = qualifiers[i].DeadlineDayIndex == int.MaxValue
                        ? -1 : qualifiers[i].DeadlineDayIndex;
                    // A live -1 still falls through to the registration,
                    // preserving the pre-WS5 precedence (registration wins
                    // when the live list carries no deadline).
                    if (live >= 0) return live;
                    break;
                }
            }
            foreach (ScenarioQualifierRegistration registration in state.QualifierRegistrations ?? new List<ScenarioQualifierRegistration>())
            {
                if (registration != null && registration.GoalId == goalId)
                {
                    return registration.DeadlineDayIndex == int.MaxValue
                        ? -1 : registration.DeadlineDayIndex;
                }
            }
            return -1;
        }
    }
}
