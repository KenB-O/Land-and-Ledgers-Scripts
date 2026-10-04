using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>W9B: which side of a property dispute the lawyer appears for.</summary>
    public enum RepresentationSide
    {
        Unspecified = 0,
        Claimant = 1,     // appears for the adverse claimant
        RecordHolder = 2, // appears for the recorded holder defending the chain
    }

    /// <summary>W9B: status of one representation engagement.</summary>
    public enum RepresentationStatus
    {
        Unspecified = 0,
        Active = 1,
        Withdrawn = 2,  // counsel withdrew; the dispute continues without them
        Concluded = 3,  // the dispute resolved (by the dispute mechanics); engagement closed
    }

    /// <summary>
    /// W9B: one engagement to represent a real client in a T3D property
    /// dispute. The engagement RECORDS the lawyer's appearance; it never
    /// decides the dispute. Outcomes — settlement, judgment, enforcement —
    /// belong to the T3D mechanics (Canon Part IX §9.1: a legal right does
    /// not automatically produce an outcome). The lawyer's job is notice,
    /// proof, expertise and procedure; the ruling is someone else's.
    /// </summary>
    [Serializable]
    public sealed class RepresentationEngagement
    {
        public string EngagementId = string.Empty;
        public string MatterId = string.Empty;
        public string ClaimId = string.Empty;   // the T3D AdverseClaim
        public string ParcelId = string.Empty;
        public int ClientPersonId = -1;
        public string ClientName = string.Empty;
        public RepresentationSide Side = RepresentationSide.Unspecified;
        public RepresentationStatus Status = RepresentationStatus.Active;
        public int OpenedDayIndex;
        public int ClosedDayIndex = -1;
        public string OutcomeNote = string.Empty;

        public RepresentationEngagement() { }
    }

    /// <summary>
    /// W9B: dispute representation linking into T3D property disputes.
    /// The service takes T3D's own claim records as input (it never invents
    /// claims) and books the lawyer's appearance. Concluding an engagement
    /// RECORDS the outcome the dispute mechanics reported — it does not move
    /// title (only T2F does) and does not settle by decree.
    /// </summary>
    public sealed class DisputeRepresentationService
    {
        private readonly Dictionary<string, RepresentationEngagement> engagements =
            new Dictionary<string, RepresentationEngagement>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Engages counsel for a client on an existing T3D adverse claim.
        /// The claim must come from the dispute system; the client must be a
        /// real, named person matching the side engaged. For the record-holder
        /// side, the titles authority confirms the client is the recorded
        /// holder — counsel does not appear for strangers.
        /// </summary>
        public RepresentationEngagement EngageRepresentation(
            EntityIdRegistry ids, LawyerPracticeRuntime practice, string matterId,
            AdverseClaim claim, RepresentationSide side,
            int clientPersonId, string clientName, int dayIndex,
            TitleAuthority titles, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (practice == null || practice.GetMatter(matterId) == null ||
                practice.GetMatter(matterId).Status != MatterStatus.Open)
            {
                diag.Add("DisputeRepresentationService.EngageRepresentation: representation engages only for open practice matters.");
                return null;
            }
            if (claim == null || string.IsNullOrWhiteSpace(claim.ClaimId))
            {
                diag.Add("DisputeRepresentationService.EngageRepresentation: the T3D claim must exist first — counsel does not invent disputes.");
                return null;
            }
            if (side == RepresentationSide.Unspecified)
            {
                diag.Add("DisputeRepresentationService.EngageRepresentation: the side represented must be stated.");
                return null;
            }
            if (clientPersonId < 0 || string.IsNullOrWhiteSpace(clientName))
            {
                diag.Add("DisputeRepresentationService.EngageRepresentation: the client must be a real, named person.");
                return null;
            }
            if (side == RepresentationSide.Claimant && !string.Equals(clientName, claim.ClaimantName, StringComparison.Ordinal))
            {
                diag.Add($"DisputeRepresentationService.EngageRepresentation: engaged for the claimant, but the client '{clientName}' " +
                    $"is not the claimant '{claim.ClaimantName}' — sides must match.");
                return null;
            }
            if (side == RepresentationSide.RecordHolder && titles != null)
            {
                string holder = titles.CurrentHolder(claim.ParcelId);
                if (!string.Equals(clientName, holder, StringComparison.Ordinal))
                {
                    diag.Add($"DisputeRepresentationService.EngageRepresentation: engaged for the record holder, but the client '{clientName}' " +
                        $"is not the recorded holder '{holder ?? "(unknown parcel)"}' of '{claim.ParcelId}' — counsel does not appear for strangers.");
                    return null;
                }
            }

            var engagement = new RepresentationEngagement
            {
                EngagementId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MatterId = matterId,
                ClaimId = claim.ClaimId,
                ParcelId = claim.ParcelId,
                ClientPersonId = clientPersonId,
                ClientName = clientName.Trim(),
                Side = side,
                Status = RepresentationStatus.Active,
                OpenedDayIndex = dayIndex,
            };
            engagements[engagement.EngagementId] = engagement;
            diag.Add($"DisputeRepresentationService: counsel engaged — '{engagement.ClientName}' appears for the {side} side " +
                $"on claim {claim.ClaimId} (parcel '{claim.ParcelId}'). The dispute itself is undecided.");
            return engagement;
        }

        /// <summary>Withdraws counsel. The dispute continues; withdrawal decides nothing.</summary>
        public string WithdrawRepresentation(string engagementId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!engagements.TryGetValue(engagementId, out RepresentationEngagement engagement))
                return $"WithdrawRepresentation: unknown engagement '{engagementId}'.";
            if (engagement.Status != RepresentationStatus.Active)
                return $"WithdrawRepresentation: engagement '{engagementId}' is {engagement.Status}.";

            engagement.Status = RepresentationStatus.Withdrawn;
            engagement.ClosedDayIndex = dayIndex;
            diag.Add($"DisputeRepresentationService: counsel withdrew from claim {engagement.ClaimId} (day {dayIndex}) — the dispute proceeds without them.");
            return null;
        }

        /// <summary>
        /// Closes an engagement with the outcome the T3D mechanics reported
        /// (settlement, judgment, withdrawal of the claim — Canon §9.1).
        /// Records only; moves no title, settles nothing by decree.
        /// </summary>
        public string ConcludeRepresentation(string engagementId, string outcomeNote, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!engagements.TryGetValue(engagementId, out RepresentationEngagement engagement))
                return $"ConcludeRepresentation: unknown engagement '{engagementId}'.";
            if (engagement.Status != RepresentationStatus.Active)
                return $"ConcludeRepresentation: engagement '{engagementId}' is {engagement.Status}.";
            if (string.IsNullOrWhiteSpace(outcomeNote))
                return $"ConcludeRepresentation: the outcome as decided by the dispute mechanics must be stated — counsel does not author it.";

            engagement.Status = RepresentationStatus.Concluded;
            engagement.ClosedDayIndex = dayIndex;
            engagement.OutcomeNote = outcomeNote.Trim();
            diag.Add($"DisputeRepresentationService: engagement {engagementId} concluded (day {dayIndex}): {engagement.OutcomeNote} Title movement, if any, went through T2F.");
            return null;
        }

        public RepresentationEngagement GetEngagement(string engagementId)
        {
            return engagements.TryGetValue(engagementId, out RepresentationEngagement e) ? e : null;
        }

        #region Save / Load

        [Serializable]
        public sealed class DisputeRepresentationSaveDto
        {
            public List<RepresentationEngagement> Engagements = new List<RepresentationEngagement>();
        }

        public DisputeRepresentationSaveDto CaptureSaveDto()
        {
            var dto = new DisputeRepresentationSaveDto();
            foreach (var engagement in engagements.Values) dto.Engagements.Add(engagement);
            return dto;
        }

        public void LoadFromSaveDto(DisputeRepresentationSaveDto dto)
        {
            engagements.Clear();
            if (dto == null) return;
            foreach (var engagement in dto.Engagements)
            {
                if (engagement == null) continue;
                engagements[engagement.EngagementId] = engagement;
            }
        }

        #endregion
    }
}
