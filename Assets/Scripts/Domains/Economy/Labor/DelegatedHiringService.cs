// Delegated vacancy hiring (Land & Ledgers campaign, Agent B, 2026-11-15).
//
// Wires the existing recruitment workflow into production for the one case
// Canon authorizes without a fresh owner decision: a business with a vacant
// REQUIRED worker slot hires from open inquiries when a real hiring actor
// exists — its NPC owner, or a delegate holding the Staffing scope.
//
// Canon authority (Documentation v3.0; v3.2 was NOT available at build time):
// - Delegation is Canon: Rev XXX Part V §§5.1-5.2, Rev XXI Part IV §§4.1-4.4,
//   Part IX §§9.1-9.5; Technical XIV Part IV §§4.1-4.5. Managers may execute
//   ordinary staffing within granted authority, as real people with finite
//   time, information, resources, and legitimate contact.
// - Hiring is player/owner-decision by default; "The player action remains
//   manual unless delegated" (Canon §6.3). Standing delegation may authorize
//   "routine casual hires when a task genuinely benefits from extra labor"
//   (Canon §6.1); "Ordinary vacancies can be handled by a delegated manager
//   within authority" (Canon §13.4A). Major or long-term (permanent)
//   employment remains an escalation point — so delegated hires are recorded
//   as SeasonalOrCasual, and permanent conversion is an owner decision.
// - Wages are NOT Canon: the Canon LOCK holds that exact wages "remain
//   scenario data until separately justified". The wage used here is the
//   slot's authored baselineWeeklyWageCents from the business profile asset
//   (e.g. GeneralStoreProfile clerk_helper 1200c/week). A slot with no
//   authored wage (0) is disabled-by-config and the precise owner question
//   is recorded — no wage is ever invented.
// - No headcount is mandated (Canon Part XXIX rejects "Every General Store
//   requires two employees"; Tech §22.2-22.3: a one-clerk store is valid,
//   staffing follows workload). Only REQUIRED slots are auto-filled; optional
//   slots stay an owner/manager decision.
//
// What this service deliberately does NOT do:
// - It never hires for a player-owned business unless a Staffing delegate
//   exists (owner decision stays pending, recorded once as a diagnostic).
// - It never posts a paid channel (no newspaper ad, no handbill printing
//   cost): only WordOfMouth, the free channel ExecuteManagerRecruitment
//   already uses, so no expense flow is needed.
// - It never touches MakeOffer: the negotiated-offer lifecycle exists
//   (EmploymentNegotiationService) but no production code plays the
//   candidate side, so an offer made today would sit Offered until expiry.
//   The offer path needs a candidate-response driver first (follow-up
//   package, with the design question documented in the campaign report).
// - It never creates persons: inquiries come only from RecruitmentService,
//   which draws on actual unemployed adults; the hire re-validates the
//   candidate through the driver-supplied predicate at hire time.
//
// Production wiring lives in SimulationDrivers.DriveDelegatedHiring (one
// marked call from OnDayChanged, after DriveAutonomousLabor). This service
// is pure: no UnityEngine, no MonoBehaviour, no scene access — it is
// Roslyn-harness testable. Slot filling (WorkerSlotState.Assign) and the
// person record update happen in the driver's onHired callback, mirroring
// GeneralStoreRuntimeManager.AssignPersonToStoreSlot.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Recruitment;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Basis on which a delegated hire may be made. NPC owners act for their
    /// own business (the same owner-authority rule the hub's offer predicate
    /// uses, and the same NPC-owner auto-resolution pattern G-06 established
    /// for escalated manager requests); a Staffing delegate acts under a
    /// granted DelegationScope.Staffing. Anything else is None.
    /// </summary>
    public enum HiringAuthorityBasis
    {
        None = 0,
        NpcOwner = 1,
        StaffingDelegate = 2,
    }

    /// <summary>Plain-data description of one worker slot that may need a hire.</summary>
    public sealed class HiringSlotNeed
    {
        public string BusinessInstanceId = string.Empty;
        public string SlotId = string.Empty;
        public string SlotDisplayName = string.Empty;
        public int WeeklyWageCents;
        public bool RequiredForOpening;
        public bool IsFilled;
    }

    /// <summary>One completed delegated hire, handed to the driver's onHired callback.</summary>
    public sealed class DelegatedHireResult
    {
        public string BusinessInstanceId = string.Empty;
        public string SlotId = string.Empty;
        public string SlotDisplayName = string.Empty;
        public int PersonId = -1;
        public string PersonDisplayName = string.Empty;
        public int WeeklyWageCents;
        public string EmploymentId = string.Empty;
        public string EffortId = string.Empty;
        public string InquiryId = string.Empty;
        public int ActingPersonId = -1;
        public HiringAuthorityBasis AuthorityBasis = HiringAuthorityBasis.None;
    }

    /// <summary>
    /// Delegated vacancy hiring: service-role posting plus hire-from-inquiry,
    /// one vacant required slot at a time. Stateless except for once-only
    /// diagnostic suppression (advisory only; losing it on load re-logs once).
    /// </summary>
    public sealed class DelegatedHiringService
    {
        private readonly HashSet<string> pendingNoted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> wageDisabledNoted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> noActorNoted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> predicateMissingNoted = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Runs one business for one day. Posts a WordOfMouth effort when a
        /// vacant required slot has hiring authority but no open effort, and
        /// hires the earliest open inquiry (from a still-hireable candidate)
        /// when one exists. Returns the number of hires made (at most one per
        /// vacant required slot). Diagnostics are appended only when something
        /// happens or a persistent blocker is first observed — never daily spam.
        /// </summary>
        public int DriveBusinessDay(
            string businessInstanceId,
            bool isPlayerOwned,
            int actingPersonId,
            HiringAuthorityBasis authorityBasis,
            IReadOnlyList<HiringSlotNeed> slots,
            RecruitmentService recruiting,
            EmploymentRelationshipRegistry employments,
            Func<int, bool> isHireableCandidate,
            Func<int, string> resolvePersonDisplayName,
            Action<DelegatedHireResult> onHired,
            int dayIndex,
            int effortSeed,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(businessInstanceId) || recruiting == null ||
                employments == null || slots == null)
            {
                diag.Add("DelegatedHiringService: business, recruitment service, employment registry, and slots are required.");
                return 0;
            }

            int hires = 0;
            foreach (HiringSlotNeed slot in slots)
            {
                if (slot == null || !slot.RequiredForOpening || slot.IsFilled)
                {
                    continue;
                }

                string key = businessInstanceId + ":" + slot.SlotId;

                // Canon §6.3: the player action remains manual unless delegated.
                if (isPlayerOwned && authorityBasis != HiringAuthorityBasis.StaffingDelegate)
                {
                    if (pendingNoted.Add(key))
                    {
                        diag.Add($"DelegatedHiringService: '{businessInstanceId}' has a vacant required slot " +
                            $"'{slot.SlotDisplayName}' ({slot.WeeklyWageCents}c/week) — no auto-hire: player-owned " +
                            "businesses hire by owner decision. OWNER QUESTION: should the owner hire for this slot, " +
                            "and at what weekly wage (cents)?");
                    }
                    continue;
                }

                if (authorityBasis == HiringAuthorityBasis.None || actingPersonId < 0)
                {
                    if (noActorNoted.Add(key))
                    {
                        diag.Add($"DelegatedHiringService: '{businessInstanceId}' has a vacant required slot " +
                            $"'{slot.SlotDisplayName}' but no hiring actor (no NPC owner, no Staffing delegate) — no auto-hire.");
                    }
                    continue;
                }

                // Wages are scenario data, never invented: a slot with no
                // authored baseline wage cannot hire through this path.
                if (slot.WeeklyWageCents <= 0)
                {
                    if (wageDisabledNoted.Add(key))
                    {
                        diag.Add($"DelegatedHiringService: vacant required slot '{slot.SlotDisplayName}' at " +
                            $"'{businessInstanceId}' has no authored weekly wage — hiring is disabled for this slot. " +
                            "OWNER QUESTION: what weekly wage (cents) should this role pay?");
                    }
                    continue;
                }

                // Service-role posting: reuse an open effort; post WordOfMouth
                // (free, no expense flow) when none exists. A freshly posted
                // effort accrues inquiries from tomorrow via AdvanceDay.
                List<RecruitmentEffort> open = recruiting.OpenEffortsForBusiness(businessInstanceId);
                if (open.Count == 0)
                {
                    RecruitmentEffort effort = recruiting.OpenEffort(
                        businessInstanceId, slot.SlotDisplayName,
                        RecruitmentChannel.WordOfMouth, dayIndex, effortSeed, diag);
                    if (effort == null)
                    {
                        diag.Add($"DelegatedHiringService: could not post a hiring effort for '{slot.SlotDisplayName}' " +
                            $"at '{businessInstanceId}' — see RecruitmentService diagnostics.");
                        continue;
                    }

                    diag.Add($"DelegatedHiringService: posted WordOfMouth hiring effort {effort.EffortId} for " +
                        $"'{slot.SlotDisplayName}' at '{businessInstanceId}' (wage {slot.WeeklyWageCents}c/week).");
                    continue;
                }

                if (isHireableCandidate == null)
                {
                    if (predicateMissingNoted.Add(key))
                    {
                        diag.Add("DelegatedHiringService: no candidate-eligibility check supplied — hiring refused " +
                            "(a missing check cannot silently authorize a hire).");
                    }
                    continue;
                }

                // Earliest open inquiry on an open effort, from a candidate
                // who is still hireable right now. Inquiries on closed efforts
                // are not reused: the effort's closure means hiring for it
                // is done.
                CandidateInquiry best = null;
                foreach (CandidateInquiry inquiry in recruiting.Inquiries)
                {
                    if (inquiry == null || inquiry.Status != InquiryStatus.Open)
                    {
                        continue;
                    }

                    bool onOpenEffort = false;
                    foreach (RecruitmentEffort effort in open)
                    {
                        if (effort != null && string.Equals(effort.EffortId, inquiry.EffortId, StringComparison.Ordinal))
                        {
                            onOpenEffort = true;
                            break;
                        }
                    }
                    if (!onOpenEffort)
                    {
                        continue;
                    }

                    if (!isHireableCandidate(inquiry.PersonId))
                    {
                        continue;
                    }

                    if (best == null || inquiry.ReceivedDayIndex < best.ReceivedDayIndex ||
                        (inquiry.ReceivedDayIndex == best.ReceivedDayIndex &&
                         string.CompareOrdinal(inquiry.InquiryId, best.InquiryId) < 0))
                    {
                        best = inquiry;
                    }
                }

                if (best == null)
                {
                    continue; // Waiting on inquiries; AdvanceDay accrues them.
                }

                // Canon §6.1: delegated hires are routine/casual. Permanent
                // hiring escalates to the owner — see the file header.
                string error = recruiting.HireFromInquiry(
                    best.InquiryId, slot.WeeklyWageCents,
                    EmploymentKind.SeasonalOrCasual, dayIndex, employments, diag);
                if (error != null)
                {
                    diag.Add($"DelegatedHiringService: hire from inquiry {best.InquiryId} refused — {error}");
                    continue;
                }

                string employmentId = $"emp-{best.InquiryId}";
                foreach (EmploymentRelationship rel in employments.GetByEmployee(best.PersonId))
                {
                    if (rel != null && string.Equals(rel.EmployerBusinessId, businessInstanceId, StringComparison.Ordinal))
                    {
                        employmentId = rel.Id;
                        break;
                    }
                }

                var result = new DelegatedHireResult
                {
                    BusinessInstanceId = businessInstanceId,
                    SlotId = slot.SlotId,
                    SlotDisplayName = slot.SlotDisplayName,
                    PersonId = best.PersonId,
                    PersonDisplayName = resolvePersonDisplayName != null
                        ? resolvePersonDisplayName(best.PersonId) ?? string.Empty
                        : string.Empty,
                    WeeklyWageCents = slot.WeeklyWageCents,
                    EmploymentId = employmentId,
                    EffortId = best.EffortId,
                    InquiryId = best.InquiryId,
                    ActingPersonId = actingPersonId,
                    AuthorityBasis = authorityBasis,
                };
                onHired?.Invoke(result);
                hires++;
                diag.Add($"DelegatedHiringService: P{best.PersonId} hired as '{slot.SlotDisplayName}' at " +
                    $"'{businessInstanceId}' at {slot.WeeklyWageCents}c/week (delegated {authorityBasis}, " +
                    $"employment {employmentId}).");
            }

            return hires;
        }
    }
}
