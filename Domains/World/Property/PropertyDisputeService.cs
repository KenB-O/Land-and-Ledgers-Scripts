using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass IX WS6 (A1-06): how well a dispute claim is known. APPEND-ONLY.
    /// Verified = grounded in the title chain itself (a chain break the
    /// recording party can point at); Reported = asserted by an affected
    /// party (a displaced occupant) without chain proof.
    /// </summary>
    public enum KnowledgeCertainty
    {
        Unknown = 0,
        Reported = 1,
        Verified = 2,
    }

    /// <summary>Pass IX WS6 (A1-06): lifecycle of a recorded dispute claim. APPEND-ONLY.</summary>
    public enum DisputeClaimStatus
    {
        Open = 0,
        Contested = 1,
        Resolved = 2,
        Withdrawn = 3,
    }

    /// <summary>
    /// Pass IX WS6 (A1-06): one dispute claim recorded against a parcel.
    /// Recording a claim never moves title (Canon §2.3); it makes the
    /// dispute visible to diligence and to the parcel's interest store.
    /// </summary>
    [Serializable]
    public sealed class DisputeClaim
    {
        public string ClaimId = string.Empty;
        public string ParcelId = string.Empty;
        /// <summary>The party the claim is recorded for (claimant / displaced occupant).</summary>
        public string ClaimantId = string.Empty;
        /// <summary>The party who raised the claim into the record.</summary>
        public string RaisedBy = string.Empty;
        public KnowledgeCertainty Certainty = KnowledgeCertainty.Unknown;
        public DisputeClaimStatus Status = DisputeClaimStatus.Open;
        public ClaimBasis Basis = ClaimBasis.Unspecified;
        public int DayIndex;
        public string Note = string.Empty;

        public DisputeClaim() { }

        public bool IsOpenOrContested =>
            Status == DisputeClaimStatus.Open || Status == DisputeClaimStatus.Contested;
    }

    /// <summary>
    /// Pass IX WS6 (A1-06): records dispute claims at acquisition and serves
    /// them to diligence. Before this pass PropertyDisputes.cs (T3D) was a
    /// read-only diagnostic with zero production references and the hub's
    /// PropertyInterestStore DisputeClaim kind had no production writer.
    /// This service is that writer: at an acquisition it (a) analyses the
    /// parcel's title chain through the T3D troubleshooting service and
    /// records a Verified claim, raised by the new owner, when the chain is
    /// not Clean, and (b) records a Reported, Contested claim for a
    /// non-owner occupant displaced by the acquisition. Claims are also
    /// mirrored into the single PropertyInterestStore (InterestKind.
    /// DisputeClaim) when one is supplied — no second rights registry.
    /// </summary>
    public sealed class PropertyDisputeService
    {
        private readonly List<DisputeClaim> claims = new List<DisputeClaim>();
        private readonly List<string> diagnostics = new List<string>();
        private readonly IPropertyInterestStore interestStore;
        private readonly PropertyTroubleshootingService troubleshooting = new PropertyTroubleshootingService();
        private int sequence;

        public PropertyDisputeService(IPropertyInterestStore interestStore = null)
        {
            this.interestStore = interestStore;
        }

        public IReadOnlyList<DisputeClaim> Claims => claims;
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Records one claim. Anonymous claims are refused (T3D doctrine).
        /// Pass XI WS3 (PASS9-003): (parcel, claimant, basis) dedup — an
        /// already Open or Contested claim with the same parcel, claimant
        /// and basis is returned instead of recording a second claim, and
        /// nothing is mirrored to the interest store a second time. A
        /// Resolved or Withdrawn claim does not block a fresh claim on the
        /// same triple: the old dispute is closed, the new one is real.
        /// Recording a claim never moves title (Canon §2.3).
        /// </summary>
        public DisputeClaim RecordClaim(
            string parcelId, string claimantId, string raisedBy,
            KnowledgeCertainty certainty, DisputeClaimStatus status, ClaimBasis basis,
            int dayIndex, string note, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(parcelId) || string.IsNullOrWhiteSpace(claimantId))
            {
                diag.Add("PropertyDisputeService.RecordClaim: a parcel and a named claimant are required — anonymous claims refused.");
                return null;
            }
            foreach (DisputeClaim existing in claims)
            {
                if (existing != null && existing.IsOpenOrContested
                    && string.Equals(existing.ParcelId, parcelId, StringComparison.Ordinal)
                    && string.Equals(existing.ClaimantId, claimantId, StringComparison.Ordinal)
                    && existing.Basis == basis)
                {
                    diag.Add($"PropertyDisputeService.RecordClaim: duplicate (parcel '{parcelId}', claimant '{claimantId}', basis {basis}) " +
                        $"refused — existing claim '{existing.ClaimId}' stands; no second claim, no second interest record.");
                    return existing;
                }
            }
            var claim = new DisputeClaim
            {
                ClaimId = $"dispute-{sequence++}",
                ParcelId = parcelId,
                ClaimantId = claimantId,
                RaisedBy = string.IsNullOrWhiteSpace(raisedBy) ? claimantId : raisedBy,
                Certainty = certainty,
                Status = status,
                Basis = basis,
                DayIndex = dayIndex,
                Note = note ?? string.Empty,
            };
            claims.Add(claim);
            interestStore?.Record(new PropertyInterest
            {
                Id = PropertyInterestStore.NewInterestId(),
                ParcelId = parcelId,
                Kind = InterestKind.DisputeClaim,
                HolderId = claimantId,
                StartDay = dayIndex,
                Notes = $"dispute claim {claim.ClaimId} ({certainty}, {status}): {claim.Note}",
            });
            diag.Add($"PropertyDisputeService: claim '{claim.ClaimId}' recorded on '{parcelId}' for '{claimantId}' ({certainty}, {status}). Title unchanged.");
            return claim;
        }

        /// <summary>
        /// The acquisition choke-point recording: call after title moves to
        /// <paramref name="newOwner"/>. Returns the claims recorded (0, 1 or 2).
        /// </summary>
        public List<DisputeClaim> RecordAcquisitionDisputes(
            TitleAuthority titles, string parcelId, string newOwner,
            string displacedOccupantId, int dayIndex, List<string> diag = null)
        {
            diag = diag ?? diagnostics;
            var recorded = new List<DisputeClaim>();
            if (string.IsNullOrWhiteSpace(parcelId) || string.IsNullOrWhiteSpace(newOwner))
            {
                diag.Add("PropertyDisputeService.RecordAcquisitionDisputes: parcel and new owner are required.");
                return recorded;
            }

            // (a) Chain not Clean → Verified claim raised by the new owner.
            if (titles != null)
            {
                ParcelDisputeReport report = troubleshooting.AnalyzeParcel(parcelId, titles, diag);
                if (report != null && report.Status != DisputeStatus.Clear)
                {
                    DisputeClaim claim = RecordClaim(parcelId, newOwner, newOwner,
                        KnowledgeCertainty.Verified, DisputeClaimStatus.Open,
                        report.AdverseClaims.Count > 0 ? ClaimBasis.DisputedRights : ClaimBasis.DefectiveDocumentation,
                        dayIndex,
                        $"title chain is {report.Status} at acquisition: {string.Join("; ", report.ChainBreaks)}",
                        diag);
                    if (claim != null) recorded.Add(claim);
                }
            }

            // (b) A non-owner occupant displaced by the acquisition → Reported, Contested claim.
            if (!string.IsNullOrWhiteSpace(displacedOccupantId)
                && !string.Equals(displacedOccupantId, newOwner, StringComparison.Ordinal))
            {
                DisputeClaim claim = RecordClaim(parcelId, displacedOccupantId, displacedOccupantId,
                    KnowledgeCertainty.Reported, DisputeClaimStatus.Contested,
                    ClaimBasis.DisputedRights, dayIndex,
                    "non-owner occupant displaced by the acquisition", diag);
                if (claim != null) recorded.Add(claim);
            }
            return recorded;
        }

        /// <summary>All claims recorded against one parcel.</summary>
        public List<DisputeClaim> ClaimsForParcel(string parcelId)
        {
            var result = new List<DisputeClaim>();
            foreach (DisputeClaim claim in claims)
                if (claim != null && string.Equals(claim.ParcelId, parcelId ?? string.Empty, StringComparison.Ordinal))
                    result.Add(claim);
            return result;
        }

        /// <summary>Open or Contested claims against one parcel (the diligence surface).</summary>
        public List<DisputeClaim> OpenOrContestedForParcel(string parcelId)
        {
            var result = new List<DisputeClaim>();
            foreach (DisputeClaim claim in ClaimsForParcel(parcelId))
                if (claim.IsOpenOrContested) result.Add(claim);
            return result;
        }

        #region Save / Load

        [Serializable]
        public sealed class PropertyDisputeSaveDto
        {
            public List<DisputeClaim> Claims = new List<DisputeClaim>();
        }

        public PropertyDisputeSaveDto CaptureSaveDto()
        {
            return new PropertyDisputeSaveDto { Claims = new List<DisputeClaim>(claims) };
        }

        public void LoadFromSaveDto(PropertyDisputeSaveDto dto)
        {
            claims.Clear();
            sequence = 0;
            if (dto?.Claims == null) return;
            foreach (DisputeClaim claim in dto.Claims)
            {
                if (claim == null || string.IsNullOrWhiteSpace(claim.ClaimId)) continue;
                claims.Add(claim);
                sequence++;
            }
        }

        #endregion
    }
}
