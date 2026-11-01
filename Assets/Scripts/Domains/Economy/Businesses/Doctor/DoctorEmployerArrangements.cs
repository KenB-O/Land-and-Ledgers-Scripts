using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// D1A: an employer's service arrangement with the practice. Canon §13.3C:
    /// "A mine or large employer can create dependable business through a
    /// service arrangement while also consuming priority during a serious
    /// accident or illness cluster." Terms are parameterized — retainer shape
    /// and per-visit rates are calibration, not canon constants.
    /// </summary>
    [Serializable]
    public sealed class EmployerServiceArrangement
    {
        public string ArrangementId = string.Empty;
        public string EmployerBusinessId = string.Empty;
        public string EmployerName = string.Empty;

        /// <summary>True = every worker of the employer is covered; false = only CoveredWorkerIds.</summary>
        public bool AllEmployeesCovered = true;
        public List<EntityId> CoveredWorkerIds = new List<EntityId>(); // EntityKind.Person

        /// <summary>Retainer per period in cents; 0 = no retainer, per-visit billing only.</summary>
        public int RetainerCentsPerPeriod;
        public int PeriodDays = 30;

        /// <summary>Per-visit charge billed to the employer when a covered worker is treated.</summary>
        public int PerVisitCents;

        /// <summary>Canon §13.3C: the arrangement consumes priority during a serious cluster.</summary>
        public bool PriorityDuringCluster = true;

        public int StartDayIndex;
        public int EndDayIndex; // 0 = open-ended

        public EmployerServiceArrangement() { }

        public bool IsActive(int dayIndex) =>
            dayIndex >= StartDayIndex && (EndDayIndex <= 0 || dayIndex <= EndDayIndex);

        public bool CoversWorker(EntityId personId)
        {
            if (AllEmployeesCovered) return personId.IsValid && personId.Kind == EntityKind.Person;
            foreach (var id in CoveredWorkerIds)
            {
                if (id.Equals(personId)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// D1A: an institutional support arrangement. Canon §13.3C: care "supported
    /// through a specific institutional arrangement where historically
    /// justified" — a named institution (church, county, fraternal society)
    /// paying a stipend that underwrites care for named households.
    /// </summary>
    [Serializable]
    public sealed class InstitutionalArrangement
    {
        public string ArrangementId = string.Empty;
        public string InstitutionName = string.Empty;
        public int StipendCentsPerPeriod;
        public int PeriodDays = 30;

        /// <summary>Empty = the stipend underwrites the practice generally; otherwise only these households.</summary>
        public List<string> CoveredHouseholdIds = new List<string>();

        public int StartDayIndex;
        public int EndDayIndex; // 0 = open-ended

        public InstitutionalArrangement() { }

        public bool IsActive(int dayIndex) =>
            dayIndex >= StartDayIndex && (EndDayIndex <= 0 || dayIndex <= EndDayIndex);

        public bool CoversHousehold(string householdId)
        {
            if (CoveredHouseholdIds == null || CoveredHouseholdIds.Count == 0) return true;
            return !string.IsNullOrWhiteSpace(householdId) && CoveredHouseholdIds.Contains(householdId);
        }
    }

    /// <summary>
    /// D1A: the practice's register of employer and institutional
    /// arrangements. Data and lookup only — invoicing goes through
    /// <see cref="DoctorReceivablesLedger"/> with
    /// <see cref="DoctorPaymentMode"/> EmployerArrangement/Institutional.
    /// </summary>
    public sealed class DoctorServiceArrangementRegister
    {
        private readonly List<EmployerServiceArrangement> employerArrangements = new List<EmployerServiceArrangement>();
        private readonly List<InstitutionalArrangement> institutionalArrangements = new List<InstitutionalArrangement>();

        public IReadOnlyList<EmployerServiceArrangement> EmployerArrangements => employerArrangements;
        public IReadOnlyList<InstitutionalArrangement> InstitutionalArrangements => institutionalArrangements;

        /// <summary>Registers an employer arrangement; ids must be unique and non-empty. Returns a rejection string, or null.</summary>
        public string AddEmployerArrangement(EmployerServiceArrangement arrangement, List<string> diag)
        {
            if (arrangement == null) return "DoctorServiceArrangementRegister: null employer arrangement refused.";
            if (string.IsNullOrWhiteSpace(arrangement.ArrangementId))
                return "DoctorServiceArrangementRegister: employer arrangement refused — an arrangement id is required.";
            if (string.IsNullOrWhiteSpace(arrangement.EmployerBusinessId))
                return "DoctorServiceArrangementRegister: employer arrangement refused — the employer business must be named.";
            foreach (var existing in employerArrangements)
            {
                if (string.Equals(existing.ArrangementId, arrangement.ArrangementId, StringComparison.Ordinal))
                    return $"DoctorServiceArrangementRegister: employer arrangement '{arrangement.ArrangementId}' already registered.";
            }

            employerArrangements.Add(arrangement);
            diag?.Add($"DoctorServiceArrangementRegister: employer arrangement '{arrangement.ArrangementId}' with '{arrangement.EmployerName}' — " +
                $"retainer {arrangement.RetainerCentsPerPeriod}c/{arrangement.PeriodDays}d, per-visit {arrangement.PerVisitCents}c, " +
                $"priority-during-cluster {(arrangement.PriorityDuringCluster ? "yes" : "no")}.");
            return null;
        }

        /// <summary>Registers an institutional arrangement. Returns a rejection string, or null.</summary>
        public string AddInstitutionalArrangement(InstitutionalArrangement arrangement, List<string> diag)
        {
            if (arrangement == null) return "DoctorServiceArrangementRegister: null institutional arrangement refused.";
            if (string.IsNullOrWhiteSpace(arrangement.ArrangementId))
                return "DoctorServiceArrangementRegister: institutional arrangement refused — an arrangement id is required.";
            if (string.IsNullOrWhiteSpace(arrangement.InstitutionName))
                return "DoctorServiceArrangementRegister: institutional arrangement refused — the institution must be named.";
            foreach (var existing in institutionalArrangements)
            {
                if (string.Equals(existing.ArrangementId, arrangement.ArrangementId, StringComparison.Ordinal))
                    return $"DoctorServiceArrangementRegister: institutional arrangement '{arrangement.ArrangementId}' already registered.";
            }

            institutionalArrangements.Add(arrangement);
            diag?.Add($"DoctorServiceArrangementRegister: institutional arrangement '{arrangement.ArrangementId}' with '{arrangement.InstitutionName}' — " +
                $"stipend {arrangement.StipendCentsPerPeriod}c/{arrangement.PeriodDays}d.");
            return null;
        }

        /// <summary>Finds the active employer arrangement covering a worker, or null.</summary>
        public EmployerServiceArrangement FindEmployerArrangement(string employerBusinessId, EntityId workerId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(employerBusinessId)) return null;
            foreach (var arrangement in employerArrangements)
            {
                if (!arrangement.IsActive(dayIndex)) continue;
                if (!string.Equals(arrangement.EmployerBusinessId, employerBusinessId, StringComparison.Ordinal)) continue;
                if (arrangement.CoversWorker(workerId)) return arrangement;
            }

            return null;
        }

        /// <summary>Finds the active institutional arrangement covering a household, or null.</summary>
        public InstitutionalArrangement FindInstitutionalArrangement(string householdId, int dayIndex)
        {
            foreach (var arrangement in institutionalArrangements)
            {
                if (!arrangement.IsActive(dayIndex)) continue;
                if (arrangement.CoversHousehold(householdId)) return arrangement;
            }

            return null;
        }

        #region Save / Load
        [Serializable]
        public sealed class DoctorArrangementsSaveDto
        {
            public List<EmployerServiceArrangement> EmployerArrangements = new List<EmployerServiceArrangement>();
            public List<InstitutionalArrangement> InstitutionalArrangements = new List<InstitutionalArrangement>();
        }

        public DoctorArrangementsSaveDto CaptureSaveDto()
        {
            var dto = new DoctorArrangementsSaveDto();
            dto.EmployerArrangements.AddRange(employerArrangements);
            dto.InstitutionalArrangements.AddRange(institutionalArrangements);
            return dto;
        }

        public void LoadFromSaveDto(DoctorArrangementsSaveDto dto)
        {
            employerArrangements.Clear();
            institutionalArrangements.Clear();
            if (dto == null) return;
            if (dto.EmployerArrangements != null) employerArrangements.AddRange(dto.EmployerArrangements);
            if (dto.InstitutionalArrangements != null) institutionalArrangements.AddRange(dto.InstitutionalArrangements);
        }
        #endregion
    }
}
