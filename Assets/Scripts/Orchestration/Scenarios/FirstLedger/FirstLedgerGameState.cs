using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.ReadModels.Valuation;

namespace LandLedgers.Orchestration.Scenarios.FirstLedger
{
    /// <summary>
    /// CLN-3: the live <see cref="IFirstLedgerGameState"/> implementation, read from the
    /// real registries — the business registry (SharedBusinessRuntimeManager), the
    /// PKG-6 EmploymentRelationship registry, and the BIZ-5 valuation read model.
    /// Owner equity comes from the valuation read model (Canon §11.3/11.4), never
    /// from cash. The scenario bootstrap constructs this and injects it into the
    /// <see cref="FirstLedgerGoalEvaluator"/>.
    /// </summary>
    public sealed class FirstLedgerGameState : IFirstLedgerGameState
    {
        private readonly Func<IReadOnlyList<BusinessInstanceState>> businessSource;
        private readonly EmploymentRelationshipRegistry employments;
        private readonly EnterpriseValuationReadModel valuation;

        public FirstLedgerGameState(
            Func<IReadOnlyList<BusinessInstanceState>> businessSource,
            EmploymentRelationshipRegistry employments,
            EnterpriseValuationReadModel valuation)
        {
            this.businessSource = businessSource;
            this.employments = employments;
            this.valuation = valuation;
        }

        /// <summary>Businesses the player owns (any ownership share counts).</summary>
        public int PlayerOwnedBusinessCount => PlayerOwnedBusinessIds().Count;

        /// <summary>Active wage employments at the player's businesses.</summary>
        public int ActivePlayerEmployeeCount
        {
            get
            {
                if (employments == null)
                {
                    return 0;
                }

                int count = 0;
                foreach (string businessId in PlayerOwnedBusinessIds())
                {
                    List<EmploymentRelationship> relationships = employments.GetByEmployer(businessId);
                    foreach (EmploymentRelationship relationship in relationships)
                    {
                        if (relationship != null && relationship.IsActive)
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// Player's total owner equity in cents — from the BIZ-5 valuation read model
        /// (OwnerEquityValue), never cash-in-business (Canon §11.4).
        /// </summary>
        public int PlayerOwnerEquityCents => valuation != null ? valuation.TotalPlayerOwnerEquityCents() : 0;

        private List<string> PlayerOwnedBusinessIds()
        {
            var ids = new List<string>();
            if (businessSource == null)
            {
                return ids;
            }

            IReadOnlyList<BusinessInstanceState> businesses = businessSource();
            if (businesses == null)
            {
                return ids;
            }

            foreach (BusinessInstanceState business in businesses)
            {
                if (business == null || business.Owner == null)
                {
                    continue;
                }

                if (business.Owner.OwnerKind == BusinessOwnerKind.Player
                    && !string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    ids.Add(business.InstanceId);
                }
            }

            return ids;
        }
    }
}
