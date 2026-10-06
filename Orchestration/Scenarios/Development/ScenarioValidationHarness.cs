using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.Development
{
    [Serializable]
    public sealed class BusinessValuationDiagnostic
    {
        public string BusinessInstanceId { get; internal set; }
        public string DisplayName { get; internal set; }
        public int BusinessCashCents { get; internal set; }
        public int AllTimeNetCents { get; internal set; }
        public int RecordedWeekCount { get; internal set; }
        public int TransferableAssetValueCents { get; internal set; }
        public int LiabilitiesCents { get; internal set; }
        public int CapitalReplacementNeedCents { get; internal set; }
        public int GrossGoingConcernValueCents { get; internal set; }
        public int OwnerEquityValueCents { get; internal set; }
        public int EmploymentRecordCount { get; internal set; }
        public int ActiveEmploymentRecordCount { get; internal set; }
        public string EmploymentStateSummary { get; internal set; }
        public string DerivationNotes { get; internal set; }
    }

    /// <summary>
    /// Development-only editorial surface over the production scenario authorities.
    /// It may establish source state for predicate probing, but it never completes a
    /// goal, objective, or scenario directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioValidationHarness : MonoBehaviour
    {
        public readonly struct ObjectiveReport
        {
            public ObjectiveReport(string id, string description, string target, string current, bool complete, string source)
            {
                Id = id ?? string.Empty;
                Description = description ?? string.Empty;
                Target = target ?? string.Empty;
                Current = current ?? string.Empty;
                Complete = complete;
                Source = source ?? string.Empty;
            }

            public string Id { get; }
            public string Description { get; }
            public string Target { get; }
            public string Current { get; }
            public bool Complete { get; }
            public string Source { get; }
        }

        private readonly List<string> sessionLog = new();
        private ScenarioDirector director;
        private FirstLedgerBootstrap firstLedger;
        private SharedBusinessRuntimeManager businesses;
        private PlayerPortfolioManager portfolio;
        private TimeManager timeManager;
        private SimulationSystemsHub systemsHub;

        public static ScenarioValidationHarness Instance { get; private set; }
        public IReadOnlyList<string> SessionLog => sessionLog;
        public IReadOnlyList<string> ScenarioIds
        {
            get
            {
                ResolveAuthorities();
                return director != null ? director.Service.ListScenarioIds() : Array.Empty<string>();
            }
        }

        public string ActiveScenarioId
        {
            get
            {
                ResolveAuthorities();
                return director != null ? director.ActiveScenarioId : string.Empty;
            }
        }

        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveAuthorities();
#else
            enabled = false;
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void ResolveAuthorities()
        {
            // These are scene-owned authorities. Refresh them after every scene
            // transition so the persistent development harness never holds a stale
            // reference to the previous scene's director or registries.
            director = FindAnyObjectByType<ScenarioDirector>();
            firstLedger = FindAnyObjectByType<FirstLedgerBootstrap>();
            businesses = FindAnyObjectByType<SharedBusinessRuntimeManager>();
            portfolio = FindAnyObjectByType<PlayerPortfolioManager>();
            timeManager = TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            systemsHub = FindAnyObjectByType<SimulationSystemsHub>();

            // The authored start panel normally registers assets during its own
            // startup. The editorial surface must also work after a scene reload or
            // save restore, so it discovers already-loaded authored assets through
            // the same ScenarioDirector registry without creating a second registry.
            if (director != null)
            {
                foreach (ScenarioAsset asset in Resources.FindObjectsOfTypeAll<ScenarioAsset>())
                {
                    if (asset != null && !string.IsNullOrWhiteSpace(asset.ScenarioId))
                    {
                        director.RegisterScenarioAsset(asset);
                    }
                }
            }
        }

        public bool StartScenario(string scenarioId)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResolveAuthorities();
            bool started = director != null && director.SwitchScenario(scenarioId);
            Record(started ? $"Started scenario '{scenarioId}'." : $"Scenario start refused: '{scenarioId}'.");
            return started;
#else
            return false;
#endif
        }

        public bool RestartScenario()
        {
            return !string.IsNullOrWhiteSpace(ActiveScenarioId) && StartScenario(ActiveScenarioId);
        }

        public bool ReevaluateObjectives()
        {
            ResolveAuthorities();
            bool complete = firstLedger != null && firstLedger.EvaluateNow();
            Record($"Re-evaluated authoritative objectives; scenario complete = {complete}.");
            return complete;
        }

        public IReadOnlyList<ObjectiveReport> GetObjectiveReports()
        {
            ResolveAuthorities();
            var reports = new List<ObjectiveReport>();
            ScenarioRuntimeState state = director != null ? director.Service.ActiveState : null;
            if (state == null)
            {
                return reports;
            }

            FirstLedgerGoalEvaluator evaluator = firstLedger != null ? firstLedger.Evaluator : null;
            IReadOnlyList<ScenarioGoal> authoredGoals = state.Asset.Goals;
            IReadOnlyList<ScenarioObjective> authoredObjectives = state.Asset.Objectives;
            // The First Ledger asset is the currently authored entry point. Keep the
            // code-defined declaration as a compatibility fallback for older scene
            // bundles whose serialized asset lists predate the scenario schema; the
            // completion state still comes only from ScenarioRuntimeState.
            if ((authoredGoals == null || authoredGoals.Count == 0)
                && string.Equals(state.Asset.ScenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal))
            {
                authoredGoals = FirstLedgerScenario.BuildGoals();
                authoredObjectives = FirstLedgerScenario.BuildObjectives();
                Record("Scenario asset exposed no serialized goals; using the authored First Ledger declaration for diagnostics only.");
            }

            foreach (ScenarioGoal goal in authoredGoals ?? Array.Empty<ScenarioGoal>())
            {
                bool complete = state.IsGoalCompleted(goal.GoalId);
                string current = BuildGoalCurrent(goal.GoalId, evaluator);
                reports.Add(new ObjectiveReport(
                    goal.GoalId,
                    goal.Text,
                    goal.TargetText,
                    current,
                    complete,
                    "ScenarioRuntimeState + FirstLedgerBootstrap live authorities"));
            }

            foreach (ScenarioObjective objective in authoredObjectives ?? Array.Empty<ScenarioObjective>())
            {
                bool complete = state.IsObjectiveCompleted(objective.ObjectiveId)
                    || (!string.IsNullOrWhiteSpace(objective.LinkedGoalId) && state.IsGoalCompleted(objective.LinkedGoalId));
                reports.Add(new ObjectiveReport(
                    objective.ObjectiveId,
                    state.GetObjectiveText(objective.ObjectiveId),
                    objective.LinkedGoalId,
                    complete ? "Linked goal complete" : "Linked goal incomplete",
                    complete,
                    "ScenarioRuntimeState"));
            }

            return reports;
        }

        /// <summary>Creates through the canonical player-business formation authority.</summary>
        public bool TryCreateBusiness(BusinessType type, string displayName, out BusinessInstanceState business, out string message)
        {
            ResolveAuthorities();
            business = null;
            message = "Shared business runtime unavailable.";
            bool created = businesses != null && businesses.TryCreatePlayerBusiness(type, displayName, out business, out message);
            Record(created
                ? $"Created real player Business '{business.RuntimeDisplayName}' through production formation."
                : $"Business formation refused: {message}");
            return created;
        }

        /// <summary>
        /// Adds owner liquidity through the real owner-cash ledger. This is explicitly
        /// development capital, never revenue, profit, loan proceeds, or objective state.
        /// </summary>
        public bool InjectOwnerCash(int cents)
        {
            ResolveAuthorities();
            if (portfolio == null || cents <= 0)
            {
                Record("Owner-cash injection refused: missing portfolio or non-positive amount.");
                return false;
            }

            portfolio.AddOwnerCash(cents, "Development Injection / Editorial Validation");
            Record($"Injected {cents} cents as Development Injection / Debug Capital; no objective was changed.");
            return true;
        }

        /// <summary>Normal-mode funding action over the production owner/business ledger.</summary>
        public bool FundBusiness(BusinessInstanceState business, int cents, int protectedReserveCents = 0)
        {
            ResolveAuthorities();
            if (portfolio == null || business == null || cents <= 0)
            {
                Record("Business funding refused: real business, portfolio, and positive amount are required.");
                return false;
            }

            bool funded = portfolio.TryTransferOwnerBusinessCash(
                business,
                cents,
                Mathf.Max(0, protectedReserveCents),
                out string message);
            Record(funded
                ? $"Funded real Business '{business.InstanceId}' with {cents} cents through the owner/business ledger."
                : $"Business funding refused: {message}");
            return funded;
        }

        /// <summary>Normal-mode hiring action using a real Person and EmploymentRelationship.</summary>
        public bool HireCandidate(BusinessInstanceState business, int candidateIndex = 0)
        {
            ResolveAuthorities();
            if (businesses == null || business == null)
            {
                Record("Hiring refused: shared business authority or business unavailable.");
                return false;
            }

            bool hired = businesses.TryAssignCandidateToOpenSlot(
                business,
                Mathf.Max(0, candidateIndex),
                out string message);
            Record(hired
                ? $"Hired a real Person into Business '{business.InstanceId}' through production recruitment."
                : $"Hiring refused: {message}");
            return hired;
        }

        /// <summary>
        /// Predicate-isolation probe: records a real transferable-asset valuation
        /// input for an actually player-owned business. It never writes an objective,
        /// victory flag, revenue entry, or cash balance.
        /// </summary>
        public bool ProbeBusinessAssetValue(BusinessInstanceState business, int assetValueCents)
        {
            ResolveAuthorities();
            if (systemsHub == null || business == null || business.Owner == null
                || business.Owner.OwnerKind != BusinessOwnerKind.Player || assetValueCents <= 0)
            {
                Record("Valuation probe refused: real player-owned Business and positive asset value are required.");
                return false;
            }

            systemsHub.Valuation.RegisterBusiness(business.InstanceId, "player", true);
            systemsHub.Valuation.RecordTransferableAssets(business.InstanceId, assetValueCents);
            Record($"Recorded {assetValueCents} cents of transferable asset evidence for real Business '{business.InstanceId}' as an editorial predicate probe.");
            return true;
        }

        public int ProbeAllPlayerBusinessAssets(int assetValueCents)
        {
            ResolveAuthorities();
            if (businesses == null)
            {
                return 0;
            }

            int count = 0;
            foreach (BusinessInstanceState business in businesses.Businesses)
            {
                if (ProbeBusinessAssetValue(business, assetValueCents))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Read-only side-by-side evidence for normal-versus-probe validation.
        /// This intentionally exposes the valuation authority's inputs and result
        /// without offering any mutation operation.
        /// </summary>
        public IReadOnlyList<BusinessValuationDiagnostic> GetBusinessValuationDiagnostics()
        {
            ResolveAuthorities();
            var result = new List<BusinessValuationDiagnostic>();
            if (businesses == null || systemsHub == null)
            {
                return result;
            }

            foreach (BusinessInstanceState business in businesses.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    continue;
                }

                ValuationEvidence evidence = systemsHub.Valuation.GetEvidence(business.InstanceId);
                EnterpriseValuationResult valuation = systemsHub.Valuation.GetValuation(business.InstanceId);
                List<EmploymentRelationship> employment = systemsHub.Employments.GetByEmployer(business.InstanceId);
                int activeEmployment = 0;
                var employmentStates = new List<string>();
                foreach (EmploymentRelationship relationship in employment)
                {
                    if (relationship != null && relationship.IsActive)
                    {
                        activeEmployment++;
                    }

                    if (relationship != null)
                    {
                        employmentStates.Add($"{relationship.EmployeePersonId}:{relationship.LifecycleState}");
                    }
                }
                result.Add(new BusinessValuationDiagnostic
                {
                    BusinessInstanceId = business.InstanceId,
                    DisplayName = business.RuntimeDisplayName,
                    BusinessCashCents = business.RuntimeState != null ? business.RuntimeState.CurrentCashCents : 0,
                    AllTimeNetCents = business.RuntimeState != null ? business.RuntimeState.AllTimeNetCents : 0,
                    RecordedWeekCount = evidence != null ? evidence.RecordedWeekCount : 0,
                    TransferableAssetValueCents = evidence != null ? evidence.TransferableAssetValueCents : 0,
                    LiabilitiesCents = evidence != null ? evidence.BusinessSpecificLiabilitiesCents : 0,
                    CapitalReplacementNeedCents = evidence != null ? evidence.CapitalReplacementNeedCents : 0,
                    GrossGoingConcernValueCents = valuation != null ? valuation.GrossGoingConcernValueCents : 0,
                    OwnerEquityValueCents = valuation != null ? valuation.OwnerEquityValueCents : 0,
                    EmploymentRecordCount = employment.Count,
                    ActiveEmploymentRecordCount = activeEmployment,
                    EmploymentStateSummary = string.Join(", ", employmentStates),
                    DerivationNotes = valuation != null ? valuation.DerivationNotes : "No valuation result."
                });
            }

            return result;
        }

        public void SetTimeSpeed(SimulationSpeed speed)
        {
            ResolveAuthorities();
            timeManager?.SetSpeed(speed);
            Record($"Set authoritative simulation speed to {speed}.");
        }

        public void AdvanceHours(int hours)
        {
            ResolveAuthorities();
            if (timeManager == null)
            {
                Record("Advance refused: TimeManager unavailable.");
                return;
            }

            int safeHours = Mathf.Max(0, hours);
            timeManager.AdvanceForValidation(safeHours * 3600f);
            Record($"Advanced authoritative time by {safeHours} hour(s).");
        }

        public void Record(string message)
        {
            string entry = $"[{DateTime.UtcNow:O}] {message}";
            sessionLog.Add(entry);
            Debug.Log($"[ScenarioValidation] {message}", this);
        }

        /// <summary>
        /// Keeps the authoritative fixed-point value visible for diagnostics while
        /// making the player-facing currency interpretation unambiguous. Scenario
        /// money is stored in cents throughout the production authorities.
        /// </summary>
        public static string FormatCentsForDisplay(int cents)
        {
            return $"${cents / 100m:N2} ({cents:N0} cents)";
        }

        private string BuildGoalCurrent(string goalId, FirstLedgerGoalEvaluator evaluator)
        {
            if (firstLedger == null)
            {
                return "Unavailable";
            }

            return goalId switch
            {
                "acquire-first-business" or "own-two-businesses" or "own-three-businesses" =>
                    $"Owned businesses: {firstLedger.PlayerOwnedBusinessCount}",
                "hire-first-employee" or "three-employees" =>
                    $"Active employees: {firstLedger.ActivePlayerEmployeeCount}",
                "equity-5000" or "equity-10000" or "equity-15000" =>
                    $"Owner equity: {FormatCentsForDisplay(firstLedger.PlayerOwnerEquityCents)}",
                _ => evaluator != null && evaluator.CompletedGoalIds.Contains(goalId) ? "Complete" : "Incomplete"
            };
        }
    }
}
