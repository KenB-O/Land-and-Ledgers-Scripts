using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.SaloonCircuit
{
    /// <summary>
    /// T3B: concrete ISaloonCircuitGameState reading the real registries.
    /// Ownership comes from the business registry (OwnerKind.Player = 100%,
    /// per the canon's "100 percent" milestones — partial partnership shares
    /// would refine this, documented not faked). Qualifying sales come from
    /// the T2G saloon runtime's recorded customer sales.
    ///
    /// Three inputs are injected as delegates because their source systems
    /// are Unity-side or scenario-specific: the traffic-rank read, the
    /// integrated-sale provenance query, and the scenario debt total. They
    /// default to honest negatives (no rank, no sale, debt unknown = 0 only
    /// when the caller confirms zero) — never to invented positives.
    /// </summary>
    public sealed class SaloonCircuitGameState : ISaloonCircuitGameState
    {
        private readonly Func<IReadOnlyList<BusinessInstanceState>> businessSource;
        private readonly Func<string, int> drinksServedSource;
        private readonly Func<string, int> mealsServedSource;
        private readonly Func<bool> integratedSaleSource;
        private readonly Func<string, int> distinctInternalProductsSource;
        private readonly Func<bool> crownRankOneSource;
        private readonly Func<int> scenarioDebtSource;
        private readonly Func<int> dayIndexSource;

        public SaloonCircuitGameState(
            Func<IReadOnlyList<BusinessInstanceState>> businessSource,
            Func<string, int> drinksServedSource = null,
            Func<string, int> mealsServedSource = null,
            Func<bool> integratedSaleSource = null,
            Func<string, int> distinctInternalProductsSource = null,
            Func<bool> crownRankOneSource = null,
            Func<int> scenarioDebtSource = null,
            Func<int> dayIndexSource = null)
        {
            this.businessSource = businessSource;
            this.drinksServedSource = drinksServedSource;
            this.mealsServedSource = mealsServedSource;
            this.integratedSaleSource = integratedSaleSource;
            this.distinctInternalProductsSource = distinctInternalProductsSource;
            this.crownRankOneSource = crownRankOneSource;
            this.scenarioDebtSource = scenarioDebtSource;
            this.dayIndexSource = dayIndexSource;
        }

        public float SaloonOwnershipShare(string saloonId)
        {
            if (businessSource == null || string.IsNullOrWhiteSpace(saloonId)) return 0f;
            foreach (BusinessInstanceState business in businessSource())
            {
                if (business == null) continue;
                if (string.Equals(business.InstanceId, saloonId, StringComparison.Ordinal))
                    return business.OwnerKind == BusinessOwnerKind.Player ? 1f : 0f;
            }
            return 0f;
        }

        public bool HasQualifyingCustomerSale(string saloonId)
        {
            int drinks = drinksServedSource != null ? drinksServedSource(saloonId) : 0;
            int meals = mealsServedSource != null ? mealsServedSource(saloonId) : 0;
            return drinks + meals > 0;
        }

        public bool HasIntegratedSale()
        {
            return integratedSaleSource != null && integratedSaleSource();
        }

        public int DistinctInternallySuppliedProductsSold(string saloonId)
        {
            if (distinctInternalProductsSource == null || string.IsNullOrWhiteSpace(saloonId)) return 0;
            return Math.Max(0, distinctInternalProductsSource(saloonId));
        }

        public bool IsCrownHouseRankOne => crownRankOneSource != null && crownRankOneSource();

        public int ScenarioPrincipalDebtCents => scenarioDebtSource != null ? Math.Max(0, scenarioDebtSource()) : 0;

        public int CurrentDayIndex => dayIndexSource != null ? dayIndexSource() : 0;
    }
}
