using System;
using System.Collections.Generic;
using LandLedgers.Economy.Core;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Economy.Market
{
    /// <summary>
    /// T2E: the three population read models (Canon Part I §1.2, CANON LOCK).
    /// These are READ MODELS over actual world state — persons, households,
    /// farms, routes — never independent counters that can mutate the world.
    /// Resident population is not economic population.
    /// </summary>
    public sealed class PopulationViews
    {
        private readonly PopulationState population;
        private readonly EmploymentRelationshipRegistry employment;

        public PopulationViews(PopulationState population, EmploymentRelationshipRegistry employment)
        {
            this.population = population;
            this.employment = employment;
        }

        /// <summary>
        /// Canon §1.2: Persons whose primary residence lies inside the
        /// settlement. Computed from actual household membership.
        /// </summary>
        public int BuiltUpPopulation(IEnumerable<int> townHouseholdIds)
        {
            if (population == null || townHouseholdIds == null) return 0;
            var townSet = new HashSet<int>(townHouseholdIds);
            int count = 0;
            foreach (PersonState p in population.people)
            {
                if (p != null && townSet.Contains(p.householdId)) count++;
            }
            return count;
        }

        /// <summary>
        /// Canon §1.2: rural Persons/Households economically connected to the
        /// settlement through reachable trade/service relationships — i.e.
        /// the catchment's rural units.
        /// </summary>
        public int RuralServicePopulation(IEnumerable<string> catchmentHouseholdIds)
        {
            if (population == null || catchmentHouseholdIds == null) return 0;
            var catchmentSet = new HashSet<string>(catchmentHouseholdIds);
            int count = 0;
            foreach (PersonState p in population.people)
            {
                if (p != null && catchmentSet.Contains(p.householdId.ToString())) count++;
            }
            return count;
        }

        /// <summary>
        /// Canon §1.2: the combined reachable market relevant to local
        /// commerce — built-up + rural-service + transient traffic.
        /// Transient visitors are a declared calibration input, not a formula.
        /// </summary>
        public int EffectiveMarketPopulation(int builtUp, int ruralService, int transientVisitorsPerDay)
        {
            return Math.Max(0, builtUp) + Math.Max(0, ruralService) + Math.Max(0, transientVisitorsPerDay);
        }

        /// <summary>
        /// Canon §1.3 (CANON LOCK): Residents, Economically Active Persons,
        /// Primary Occupation and Wage Employees are SEPARATE measures.
        /// Employment must not be treated as synonymous with employees.
        /// </summary>
        public sealed class EconomicParticipation
        {
            public int Residents;
            public int EconomicallyActive;   // adults with labor access
            public int WithPrimaryOccupation; // professionId set
            public int WageEmployees;         // active EmploymentRelationships
        }

        public EconomicParticipation MeasureParticipation(IEnumerable<int> householdIds)
        {
            var result = new EconomicParticipation();
            if (population == null || householdIds == null) return result;
            var set = new HashSet<int>(householdIds);
            foreach (PersonState p in population.people)
            {
                if (p == null || !set.Contains(p.householdId)) continue;
                result.Residents++;
                bool adultWithAccess = p.ageBand == AgeBand.Adult18Plus && p.laborAccessLevel != LaborAccessLevel.None;
                if (adultWithAccess) result.EconomicallyActive++;
                if (!string.IsNullOrWhiteSpace(p.professionId)) result.WithPrimaryOccupation++;
            }
            if (employment != null)
            {
                foreach (PersonState p in population.people)
                {
                    if (p == null || !set.Contains(p.householdId)) continue;
                    if (employment.GetActiveByEmployee(p.id).Count > 0) result.WageEmployees++;
                }
            }
            return result;
        }
    }
}
