using System;

namespace LandLedgers.Primitives
{
    /// <summary>
    /// TECH §4.1 (TECH LOCK): the five-type work-relationship taxonomy.
    /// Do NOT force all productive work into EmploymentRelationship. EmploymentRelationship
    /// remains authoritative for wage employment only; the other kinds reference their own
    /// compensation/obligation rules rather than fake payroll.
    /// Cross-domain primitive: referenced by Population (household/family labor) and Economy
    /// (employment, contracts). New types only - no legacy serialization touched.
    /// </summary>
    public enum WorkRelationshipKind
    {
        /// <summary>No work relationship (e.g. an unfilled position projected to a relationship).</summary>
        None = 0,

        /// <summary>
        /// Proprietor/operator of a farm or business. Compensation is profit/equity draw, never payroll.
        /// (TECH §4.1 "Farm/BusinessOperator")
        /// </summary>
        FarmOrBusinessOperator = 1,

        /// <summary>
        /// Family enterprise labor. Updates task time, capability and output through task/activity
        /// history and the household/business relationship - WITHOUT creating a wage
        /// EmploymentRelationship or payroll entries. (TECH §4.1, §4.5; PL-04; Part VII fixture
        /// "FAMILY LABOR != EMPLOYEE")
        /// </summary>
        FamilyEnterpriseLabor = 2,

        /// <summary>Wage employment, permanent. Authoritative record: EmploymentRelationship. (TECH §4.1)</summary>
        PermanentEmployment = 3,

        /// <summary>
        /// Wage employment, seasonal or casual. Same EmploymentRelationship architecture as permanent
        /// employment - no parallel employment system. (TECH §4.1; 3A-D24)
        /// </summary>
        SeasonalOrCasualEmployment = 4,

        /// <summary>
        /// Contract or service work. Compensation/obligation comes from the agreement, not payroll.
        /// (TECH §4.1)
        /// </summary>
        ContractOrServiceWork = 5,
    }

    /// <summary>
    /// Typed endpoint kind for a <see cref="WorkRelationship"/> counterparty.
    /// Per 3A-D02: typed references where the endpoint kind is fixed; polymorphic
    /// references are the exception, not the rule.
    /// </summary>
    public enum WorkRelationshipCounterpartyKind
    {
        None = 0,
        Person = 1,
        Household = 2,
        Business = 3,
    }

    /// <summary>
    /// Typed counterparty for a work relationship. Uses a string stable id because
    /// Person/Household ids are ints while Business instance ids are strings.
    /// </summary>
    [Serializable]
    public struct WorkRelationshipCounterparty
    {
        public WorkRelationshipCounterpartyKind Kind;
        public string StableId;

        public static WorkRelationshipCounterparty ForPerson(int personId)
        {
            return new WorkRelationshipCounterparty
            {
                Kind = WorkRelationshipCounterpartyKind.Person,
                StableId = personId.ToString(),
            };
        }

        public static WorkRelationshipCounterparty ForHousehold(int householdId)
        {
            return new WorkRelationshipCounterparty
            {
                Kind = WorkRelationshipCounterpartyKind.Household,
                StableId = householdId.ToString(),
            };
        }

        public static WorkRelationshipCounterparty ForBusiness(string businessInstanceId)
        {
            return new WorkRelationshipCounterparty
            {
                Kind = WorkRelationshipCounterpartyKind.Business,
                StableId = businessInstanceId ?? string.Empty,
            };
        }

        public bool IsValid => Kind != WorkRelationshipCounterpartyKind.None
            && !string.IsNullOrWhiteSpace(StableId);

        public override string ToString()
        {
            return $"{Kind}:{StableId}";
        }
    }

    /// <summary>
    /// One classified work relationship between a person and a counterparty.
    /// Classification only - compensation authority lives elsewhere:
    /// wage employment on EmploymentRelationship (PKG-6), family labor on task/activity
    /// history (TECH §4.5), contracts on their agreement terms.
    /// </summary>
    [Serializable]
    public struct WorkRelationship
    {
        public WorkRelationshipKind Kind;
        public int PersonId;
        public WorkRelationshipCounterparty Counterparty;
        public string RoleDisplayName;
        public int StartDayIndex;
        public int EndDayIndex; // -1 = open-ended
        public string Notes;

        /// <summary>True for PermanentEmployment and SeasonalOrCasualEmployment only (SD-04).</summary>
        public bool IsWageEmployment =>
            Kind == WorkRelationshipKind.PermanentEmployment
            || Kind == WorkRelationshipKind.SeasonalOrCasualEmployment;

        /// <summary>
        /// Only wage employment creates payroll obligations. Operator draws, family labor and
        /// contract work use their own compensation rules - never fake payroll (PL-04).
        /// </summary>
        public bool CarriesPayrollObligation => IsWageEmployment;

        public bool IsOpen => EndDayIndex < 0;
    }

    /// <summary>
    /// Deterministic guards for the work-relationship taxonomy. Returns null when valid;
    /// returns a diagnostic string when the relationship violates taxonomy rules.
    /// </summary>
    public static class WorkRelationshipGuard
    {
        /// <summary>
        /// PL-04 / Part VII "FAMILY LABOR != EMPLOYEE": family enterprise labor, operators and
        /// contract workers must never carry agreed wage terms. A non-null result names the violation.
        /// </summary>
        public static string ValidateNoWageForNonWageKind(WorkRelationshipKind kind, int agreedWageCents)
        {
            if (agreedWageCents <= 0)
            {
                return null;
            }

            switch (kind)
            {
                case WorkRelationshipKind.FamilyEnterpriseLabor:
                    return $"FamilyEnterpriseLabor must not carry wage terms ({agreedWageCents}c): " +
                           "family work updates task time/capability/output without payroll (TECH §4.5).";
                case WorkRelationshipKind.FarmOrBusinessOperator:
                    return $"FarmOrBusinessOperator must not carry wage terms ({agreedWageCents}c): " +
                           "operator compensation is profit/equity draw, not payroll.";
                case WorkRelationshipKind.ContractOrServiceWork:
                    return $"ContractOrServiceWork must not carry payroll wage terms ({agreedWageCents}c): " +
                           "contract compensation lives on the agreement.";
                default:
                    return null;
            }
        }
    }
}
