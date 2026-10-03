using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Scenarios
{
    /// <summary>
    /// CLN-3: the live FirstLedger game state reads the real registries — business
    /// ownership, active employments, and owner equity from the BIZ-5 valuation
    /// read model (never cash).
    /// </summary>
    [TestFixture]
    public sealed class FirstLedgerGameStateTests
    {
        private static BusinessInstanceState BuildPlayerBusiness(string instanceId)
        {
            var profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            return BusinessInstanceState.Create(
                instanceId, profile, -1, BusinessOwnerIdentity.Player());
        }

        private static FirstLedgerGameState BuildState(
            List<BusinessInstanceState> businesses,
            EmploymentRelationshipRegistry employments,
            EnterpriseValuationReadModel valuation)
        {
            return new FirstLedgerGameState(() => businesses, employments, valuation);
        }

        [Test]
        public void PlayerOwnedBusinessCount_ReadsBusinessRegistry()
        {
            var businesses = new List<BusinessInstanceState>
            {
                BuildPlayerBusiness("biz_001"),
                BuildPlayerBusiness("biz_002"),
            };

            FirstLedgerGameState state = BuildState(
                businesses, new EmploymentRelationshipRegistry(), new EnterpriseValuationReadModel());

            Assert.AreEqual(2, state.PlayerOwnedBusinessCount);
        }

        [Test]
        public void ActivePlayerEmployeeCount_ReadsEmploymentRegistry()
        {
            var businesses = new List<BusinessInstanceState> { BuildPlayerBusiness("biz_001") };
            var employments = new EmploymentRelationshipRegistry();
            Assert.IsNull(employments.Register(new EmploymentRelationship
            {
                Id = "emp_1",
                EmployeePersonId = 7,
                EmployerBusinessId = "biz_001",
                LifecycleState = EmploymentLifecycleState.Active,
            }));
            // Inactive employment does not count.
            Assert.IsNull(employments.Register(new EmploymentRelationship
            {
                Id = "emp_2",
                EmployeePersonId = 8,
                EmployerBusinessId = "biz_001",
                LifecycleState = EmploymentLifecycleState.Ended,
            }));

            FirstLedgerGameState state = BuildState(
                businesses, employments, new EnterpriseValuationReadModel());

            Assert.AreEqual(1, state.ActivePlayerEmployeeCount);
        }

        [Test]
        public void PlayerOwnerEquityCents_ComesFromValuationReadModel()
        {
            var businesses = new List<BusinessInstanceState> { BuildPlayerBusiness("biz_001") };
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz_001", "player", true);
            valuation.RecordWeeklyProfit("biz_001", 12000);

            FirstLedgerGameState state = BuildState(
                businesses, new EmploymentRelationshipRegistry(), valuation);

            Assert.AreEqual(valuation.TotalPlayerOwnerEquityCents(), state.PlayerOwnerEquityCents);
        }

        [Test]
        public void GoalEvaluator_CompletesAgainstLiveState()
        {
            var businesses = new List<BusinessInstanceState> { BuildPlayerBusiness("biz_001") };
            var employments = new EmploymentRelationshipRegistry();
            Assert.IsNull(employments.Register(new EmploymentRelationship
            {
                Id = "emp_1",
                EmployeePersonId = 7,
                EmployerBusinessId = "biz_001",
                LifecycleState = EmploymentLifecycleState.Active,
            }));
            var valuation = new EnterpriseValuationReadModel();
            valuation.RegisterBusiness("biz_001", "player", true);

            FirstLedgerGameState state = BuildState(businesses, employments, valuation);
            var evaluator = new FirstLedgerGoalEvaluator();

            List<string> completed = evaluator.Evaluate(state);

            Assert.Contains("acquire-first-business", completed);
            Assert.Contains("hire-first-employee", completed);
        }
    }
}
