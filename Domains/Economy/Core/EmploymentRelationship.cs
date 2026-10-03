using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Primitives;

namespace LandLedgers.Economy
{
    /// <summary>Employment lifecycle (3A-D26). Ended history is retained, not deleted.</summary>
    public enum EmploymentLifecycleState
    {
        PendingStart = 0,
        Active = 1,
        OnLeave = 2,
        Suspended = 3,
        Ended = 4,
    }

    /// <summary>Why an employment is suspended. Kept separate from end reasons (3A-D26).</summary>
    public enum EmploymentSuspensionReason
    {
        None = 0,
        EmployerPaymentDefault = 1,
        LegacyUnpaid = 2,
        ByAgreement = 3,
    }

    /// <summary>Why an employment ended. Retained as history (3A-D26).</summary>
    public enum EmploymentEndReason
    {
        None = 0,
        EndedByAgreement = 1,
        Dismissed = 2,
        Resigned = 3,
        MissedPayroll = 4,
    }

    /// <summary>
    /// Wage-employment kind. Seasonal/fixed/casual use the same EmploymentRelationship
    /// architecture as permanent employment - no parallel employment systems (3A-D24).
    /// </summary>
    public enum EmploymentKind
    {
        Permanent = 0,
        SeasonalOrCasual = 1,
    }

    /// <summary>Where an employment record came from.</summary>
    public enum EmploymentSource
    {
        Authored = 0,
        ProjectedFromWorkerSlot = 1,
        Manual = 2,
    }

    /// <summary>
    /// Agreed compensation terms. Per 3A-D21, agreed compensation lives ONLY on
    /// EmploymentRelationship: payroll and household summaries must read from this single
    /// source. PersonState.wage (WageSnapshot) is the household-side mirror, not authority;
    /// WorkerSlotState.weeklyWageCents remains the LIVE payroll source until the payroll
    /// cutover (see EmploymentRelationshipRegistry docs) and must converge to this.
    /// </summary>
    [Serializable]
    public struct CompensationTerms
    {
        public int AgreedWeeklyWageCents;
        public string Notes;

        public static CompensationTerms FromWeeklyWage(int weeklyWageCents, string notes)
        {
            return new CompensationTerms
            {
                AgreedWeeklyWageCents = Math.Max(0, weeklyWageCents),
                Notes = notes ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// PKG-6 (PL-20): EmploymentRelationship is the SOLE authoritative Person &lt;-&gt; Business
    /// employment and compensation record (3A-D18, very high priority). Replaces the duplicate
    /// workplace/employment truth currently spread across PersonState (workplaceBuildingId,
    /// wage mirror) and WorkerSlotState (assigned worker + weeklyWageCents + payroll flags).
    /// Wage and workplace are no longer duplicated: this relationship is the authority, the
    /// others are mirrors or the legacy payroll feed pending cutover.
    /// </summary>
    [Serializable]
    public sealed class EmploymentRelationship
    {
        public string Id = string.Empty;
        public int EmployeePersonId = -1;
        public string EmployerBusinessId = string.Empty;
        public string RoleDisplayName = string.Empty;
        public EmploymentKind Kind = EmploymentKind.Permanent;
        public EmploymentLifecycleState LifecycleState = EmploymentLifecycleState.Active;
        public CompensationTerms Compensation;
        public int StartDayIndex = -1;
        public int EndDayIndex = -1; // -1 = open-ended
        public EmploymentSuspensionReason SuspensionReason = EmploymentSuspensionReason.None;
        public EmploymentEndReason EndReason = EmploymentEndReason.None;
        public EmploymentSource Source = EmploymentSource.Authored;
        public string LegacyNotes = string.Empty;

        public bool IsActive => LifecycleState == EmploymentLifecycleState.Active;
        public bool IsOpen => EndDayIndex < 0;

        public WorkRelationshipKind ToWorkRelationshipKind()
        {
            return Kind == EmploymentKind.SeasonalOrCasual
                ? WorkRelationshipKind.SeasonalOrCasualEmployment
                : WorkRelationshipKind.PermanentEmployment;
        }
    }

    /// <summary>
    /// The LaborRegistry placeholder from 3A-D02 §6.1: EmploymentId -&gt; EmploymentRelationship.
    /// Owns employment lookup and is the single source for agreed compensation (3A-D21).
    /// CUTOVER PLAN (not executed in PKG-6): payroll in SharedBusinessRuntimeManager currently
    /// reads WorkerSlotState.weeklyWageCents. The cutover switches payroll reads to
    /// GetAgreedWeeklyWageCents, with the slot wage kept as a fallback during migration and
    /// removed once the registry is the proven source. PersonState.wage remains a derived
    /// household-side mirror.
    /// </summary>
    public sealed class EmploymentRelationshipRegistry
    {
        private readonly Dictionary<string, EmploymentRelationship> byId =
            new Dictionary<string, EmploymentRelationship>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>
        /// Registers an employment. Duplicate ids are rejected deterministically (first wins).
        /// </summary>
        public string Register(EmploymentRelationship relationship)
        {
            if (relationship == null || string.IsNullOrWhiteSpace(relationship.Id))
            {
                string diagnostic = "Rejected employment registration: null relationship or missing id.";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            if (byId.ContainsKey(relationship.Id))
            {
                string diagnostic =
                    $"Duplicate employment registration rejected for id '{relationship.Id}': first registration wins.";
                diagnostics.Add(diagnostic);
                return diagnostic;
            }

            byId[relationship.Id] = relationship;
            return null;
        }

        public bool TryGetById(string employmentId, out EmploymentRelationship relationship)
        {
            relationship = null;
            return !string.IsNullOrWhiteSpace(employmentId) && byId.TryGetValue(employmentId, out relationship);
        }

        public List<EmploymentRelationship> GetByEmployee(int personId)
        {
            var results = new List<EmploymentRelationship>();
            foreach (EmploymentRelationship relationship in byId.Values)
            {
                if (relationship.EmployeePersonId == personId)
                {
                    results.Add(relationship);
                }
            }

            return results;
        }

        public List<EmploymentRelationship> GetByEmployer(string businessId)
        {
            var results = new List<EmploymentRelationship>();
            foreach (EmploymentRelationship relationship in byId.Values)
            {
                if (string.Equals(relationship.EmployerBusinessId, businessId, StringComparison.Ordinal))
                {
                    results.Add(relationship);
                }
            }

            return results;
        }

        public List<EmploymentRelationship> GetActiveByEmployee(int personId)
        {
            List<EmploymentRelationship> all = GetByEmployee(personId);
            var active = new List<EmploymentRelationship>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsActive)
                {
                    active.Add(all[i]);
                }
            }

            return active;
        }

        /// <summary>
        /// 3A-D21: the single source for agreed compensation. Returns false when the employment
        /// is unknown (callers must not invent a wage).
        /// </summary>
        public bool GetAgreedWeeklyWageCents(string employmentId, out int weeklyWageCents)
        {
            weeklyWageCents = 0;
            if (!TryGetById(employmentId, out EmploymentRelationship relationship))
            {
                return false;
            }

            weeklyWageCents = Math.Max(0, relationship.Compensation.AgreedWeeklyWageCents);
            return true;
        }

        public int Count => byId.Count;

        public IReadOnlyList<string> Diagnostics => diagnostics;
    }

    /// <summary>
    /// Projects legacy WorkerSlot assignments into EmploymentRelationship records (3A-D28 §19).
    /// Slot wage seeds the migrated compensation terms because current payroll uses it
    /// (3A-D28: "WorkerSlot wage wins wage conflicts because it currently drives payroll").
    /// Legacy slots cannot distinguish permanent from seasonal/casual employment, so all
    /// projections are EmploymentKind.Permanent with an explicit ambiguity note.
    /// </summary>
    public static class EmploymentRelationshipProjector
    {
        /// <summary>
        /// Builds employment records for every filled legacy slot. Unfilled slots produce no
        /// relationship (a vacancy is not employment). Synthetic owner: workers are skipped:
        /// they are operator coverage, not wage employment (3A-D29).
        /// </summary>
        public static List<EmploymentRelationship> ProjectFromWorkerSlots(
            string businessId,
            IReadOnlyList<WorkerSlotState> slots,
            int startDayIndex)
        {
            var results = new List<EmploymentRelationship>();
            if (slots == null)
            {
                return results;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                WorkerSlotState slot = slots[i];
                if (slot == null || !slot.IsFilled)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(slot.AssignedWorkerId)
                    && slot.AssignedWorkerId.StartsWith("owner:", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!int.TryParse(slot.AssignedWorkerId, out int personId))
                {
                    continue;
                }

                var relationship = new EmploymentRelationship
                {
                    Id = $"{businessId}:{slot.SlotId}",
                    EmployeePersonId = personId,
                    EmployerBusinessId = businessId ?? string.Empty,
                    RoleDisplayName = slot.SlotDisplayName,
                    Kind = EmploymentKind.Permanent,
                    Compensation = CompensationTerms.FromWeeklyWage(
                        slot.WeeklyWageCents,
                        "Seeded from legacy WorkerSlot wage (3A-D28: slot wage wins because current payroll uses it). " +
                        "Permanent vs seasonal/casual not distinguishable from legacy data."),
                    StartDayIndex = startDayIndex,
                    EndDayIndex = -1,
                    Source = EmploymentSource.ProjectedFromWorkerSlot,
                    LegacyNotes = $"Projected from WorkerSlot '{slot.SlotId}' (PKG-6).",
                };

                if (slot.SuspendedForMissedPayroll)
                {
                    relationship.LifecycleState = EmploymentLifecycleState.Suspended;
                    relationship.SuspensionReason = EmploymentSuspensionReason.EmployerPaymentDefault;
                }
                else if (!slot.IsPaidActive)
                {
                    // 3A-D28: paidActive = false with worker still assigned and employer
                    // corroborated -> Suspended legacy state; not active labor.
                    relationship.LifecycleState = EmploymentLifecycleState.Suspended;
                    relationship.SuspensionReason = EmploymentSuspensionReason.LegacyUnpaid;
                }
                else
                {
                    relationship.LifecycleState = EmploymentLifecycleState.Active;
                }

                results.Add(relationship);
            }

            return results;
        }

        /// <summary>
        /// CLN-1: captures employment state for the save pipeline. Relationships
        /// round-trip; the by-id lookup rebuilds on load.
        /// </summary>
        public EmploymentRegistrySaveDto CaptureSaveDto()
        {
            var dto = new EmploymentRegistrySaveDto();
            foreach (EmploymentRelationship relationship in byId.Values)
            {
                if (relationship != null)
                {
                    dto.relationships.Add(relationship);
                }
            }

            return dto;
        }

        /// <summary>CLN-1: restores employment state from the save pipeline.</summary>
        public void LoadFromSaveDto(EmploymentRegistrySaveDto dto)
        {
            byId.Clear();
            if (dto == null || dto.relationships == null)
            {
                return;
            }

            foreach (EmploymentRelationship relationship in dto.relationships)
            {
                if (relationship != null && !string.IsNullOrWhiteSpace(relationship.Id))
                {
                    byId[relationship.Id] = relationship;
                }
            }
        }
    }
}
