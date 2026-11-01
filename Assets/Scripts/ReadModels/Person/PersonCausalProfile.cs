using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass V (Worker 4, P3-1): a developer-facing causal profile for one
    /// person. Every field derives from an authoritative source; nothing is
    /// invented and nothing is cached as a separate "UI age" counter:
    /// <see cref="DerivedAgeYears"/> is computed from the simulation date and
    /// the birth-date authority at build time.
    ///
    /// Sources: PersonState (identity), BirthDate (age + provenance),
    /// HouseholdMembershipRegistry (household + moves), PopulationState
    /// (household names), EmploymentRelationshipRegistry (employment history,
    /// including ended records), FinancialObligationAuthority (obligations),
    /// KinshipRegistry (parents/children).
    ///
    /// Histories are bounded (never tick logs). Call
    /// <see cref="RedactForObserver"/> before showing to anyone but the
    /// subject or their household: strangers see identity and derived age
    /// only.
    /// </summary>
    [Serializable]
    public sealed class PersonCausalProfile
    {
        public const int MaxEmploymentRecords = 10;
        public const int MaxObligations = 10;
        public const int MaxDemographicEvents = 20;

        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public int AsOfDayIndex = -1;

        // --- Age: derived, never a separate counter ---
        /// <summary>
        /// Completed years at AsOfDayIndex, derived from the birth-date
        /// authority. -1 when no birth date is known.
        /// </summary>
        public int DerivedAgeYears = -1;
        public BirthDateProvenance BirthDateProvenance = BirthDateProvenance.Unknown; // default when no birth date
        public string BirthDateSource = string.Empty;

        // --- Origin ---
        /// <summary>How this person entered the simulation, e.g. "authored
        /// scenario", "simulated birth", "immigration". Supplied by the caller;
        /// the profile never guesses.</summary>
        public string ScenarioOrigin = string.Empty;

        // --- Household ---
        public int HouseholdId = -1;
        public string HouseholdName = string.Empty;

        // --- Employment (bounded, history kept) ---
        public List<CausalEmploymentRecord> Employments = new List<CausalEmploymentRecord>();

        // --- Obligations (bounded) ---
        public List<CausalObligationSummary> Obligations = new List<CausalObligationSummary>();

        // --- Demographic history (bounded, oldest first) ---
        public List<CausalDemographicEvent> DemographicHistory = new List<CausalDemographicEvent>();

        /// <summary>
        /// True when the person is deceased; the record is retained (identity
        /// is never rewritten or reused).
        /// </summary>
        public bool IsDeceased;
        public int DeathDayIndex = -1;

        /// <summary>
        /// Removes everything a non-household observer may not see: obligations,
        /// employment details, birth-date source, and demographic history.
        /// Identity and derived age remain.
        /// </summary>
        public void RedactForObserver()
        {
            BirthDateSource = string.Empty;
            ScenarioOrigin = string.Empty;
            HouseholdId = -1;
            HouseholdName = string.Empty;
            Employments.Clear();
            Obligations.Clear();
            DemographicHistory.Clear();
        }
    }

    [Serializable]
    public sealed class CausalEmploymentRecord
    {
        public string EmploymentId = string.Empty;
        public string EmployerBusinessId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public int WeeklyWageCents;
        public EmploymentLifecycleState LifecycleState;
        public int StartDayIndex = -1;
        public int EndDayIndex = -1;
    }

    [Serializable]
    public sealed class CausalObligationSummary
    {
        public string ObligationId = string.Empty;
        public string Kind = string.Empty;
        public string Status = string.Empty;
        public string Counterparty = string.Empty;
    }

    [Serializable]
    public sealed class CausalDemographicEvent
    {
        public int DayIndex = -1;
        /// <summary>birth, death, household-move, employment-start, employment-end.</summary>
        public string EventKind = string.Empty;
        public string Detail = string.Empty;
    }

    /// <summary>Builds <see cref="PersonCausalProfile"/> from the authorities.</summary>
    public static class PersonCausalProfileBuilder
    {
        public static PersonCausalProfile Build(
            PersonState person,
            BirthDate? birthDate,
            int asOfYear, int asOfMonth, int asOfDay, int asOfDayIndex,
            string scenarioOrigin,
            HouseholdMembershipRegistry memberships,
            PopulationState population,
            EmploymentRelationshipRegistry employments,
            FinancialObligationAuthority obligations)
        {
            var profile = new PersonCausalProfile();
            if (person == null)
            {
                return profile;
            }
            profile.PersonId = person.id;
            profile.DisplayName = person.DisplayName ?? ("P" + person.id);
            profile.AsOfDayIndex = asOfDayIndex;
            profile.ScenarioOrigin = scenarioOrigin ?? string.Empty;
            profile.IsDeceased = person.deathDayIndex >= 0;
            profile.DeathDayIndex = person.deathDayIndex;

            if (birthDate.HasValue)
            {
                BirthDate bd = birthDate.Value;
                profile.DerivedAgeYears = AgeAuthority.CompletedYears(bd, asOfYear, asOfMonth, asOfDay);
                profile.BirthDateProvenance = bd.Provenance;
                profile.BirthDateSource = bd.ProvenanceNote ?? string.Empty;
            }

            if (memberships != null)
            {
                int householdId = memberships.GetActiveHouseholdId(person.id);
                profile.HouseholdId = householdId;
                if (population != null && householdId >= 0)
                {
                    HouseholdState household = population.GetHousehold(householdId);
                    if (household != null)
                    {
                        profile.HouseholdName = household.householdName ?? string.Empty;
                    }
                }
                // Moves, oldest first, bounded.
                List<HouseholdMembership> history = new List<HouseholdMembership>();
                foreach (HouseholdMembership m in memberships.AllMemberships())
                {
                    if (m != null && m.PersonId == person.id)
                    {
                        history.Add(m);
                    }
                }
                if (history != null)
                {
                    history.Sort((a, b) => a.StartDayIndex.CompareTo(b.StartDayIndex));
                    foreach (HouseholdMembership m in history)
                    {
                        if (profile.DemographicHistory.Count >= PersonCausalProfile.MaxDemographicEvents)
                        {
                            break;
                        }
                        profile.DemographicHistory.Add(new CausalDemographicEvent
                        {
                            DayIndex = m.StartDayIndex,
                            EventKind = "household-move",
                            Detail = $"joined H{m.HouseholdId} ({m.Source})",
                        });
                    }
                }
            }

            if (employments != null)
            {
                List<EmploymentRelationship> records = employments.GetByEmployee(person.id);
                if (records != null)
                {
                    records.Sort((a, b) => a.StartDayIndex.CompareTo(b.StartDayIndex));
                    foreach (EmploymentRelationship rel in records)
                    {
                        if (profile.Employments.Count >= PersonCausalProfile.MaxEmploymentRecords)
                        {
                            break;
                        }
                        profile.Employments.Add(new CausalEmploymentRecord
                        {
                            EmploymentId = rel.Id,
                            EmployerBusinessId = rel.EmployerBusinessId,
                            RoleDisplayName = rel.RoleDisplayName,
                            WeeklyWageCents = rel.Compensation.AgreedWeeklyWageCents,
                            LifecycleState = rel.LifecycleState,
                            StartDayIndex = rel.StartDayIndex,
                            EndDayIndex = rel.EndDayIndex,
                        });
                        if (profile.DemographicHistory.Count < PersonCausalProfile.MaxDemographicEvents)
                        {
                            profile.DemographicHistory.Add(new CausalDemographicEvent
                            {
                                DayIndex = rel.StartDayIndex,
                                EventKind = "employment-start",
                                Detail = $"{rel.RoleDisplayName} at {rel.EmployerBusinessId}",
                            });
                            if (rel.EndDayIndex >= 0)
                            {
                                profile.DemographicHistory.Add(new CausalDemographicEvent
                                {
                                    DayIndex = rel.EndDayIndex,
                                    EventKind = "employment-end",
                                    Detail = $"{rel.RoleDisplayName} at {rel.EmployerBusinessId} ({rel.LifecycleState})",
                                });
                            }
                        }
                    }
                }
            }

            if (obligations != null)
            {
                string personKey = "person:" + person.id;
                string householdKey = "household:" + profile.HouseholdId;
                foreach (FinancialObligation obligation in obligations.Obligations)
                {
                    if (profile.Obligations.Count >= PersonCausalProfile.MaxObligations)
                    {
                        break;
                    }
                    if (obligation.Debtor != personKey && obligation.Debtor != householdKey)
                    {
                        continue;
                    }
                    profile.Obligations.Add(new CausalObligationSummary
                    {
                        ObligationId = obligation.ObligationId,
                        Kind = obligation.Kind.ToString(),
                        Status = obligation.Status.ToString(),
                        Counterparty = obligation.Creditor ?? string.Empty,
                    });
                }
            }

            if (profile.IsDeceased && profile.DemographicHistory.Count < PersonCausalProfile.MaxDemographicEvents)
            {
                profile.DemographicHistory.Add(new CausalDemographicEvent
                {
                    DayIndex = profile.DeathDayIndex,
                    EventKind = "death",
                    Detail = "record retained; identity never reused",
                });
            }

            profile.DemographicHistory.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));
            return profile;
        }
    }
}
