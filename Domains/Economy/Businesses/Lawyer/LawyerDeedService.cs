using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>
    /// W9B: deed forms the practice can draft. 1870s-western instruments:
    /// a warranty deed warrants title; a quitclaim conveys only whatever
    /// interest the grantor actually has (Canon §2.4: color of title is not
    /// title).
    /// </summary>
    public enum DeedKind
    {
        Unspecified = 0,
        WarrantyDeed = 1,
        QuitclaimDeed = 2,
    }

    /// <summary>W9B: deed lifecycle. Draft != record: only Recorded moves title, and only through T2F.</summary>
    public enum DeedStatus
    {
        Unspecified = 0,
        Draft = 1,
        Finalized = 2,  // sealed and locked; ready to record
        Recorded = 3,   // recorded through the T2F TitleAuthority
        Superseded = 4, // replaced by a newer draft before recording
    }

    /// <summary>
    /// W9B: one drafted deed. The deed is a CONVEYANCE INSTRUMENT, not a
    /// transfer — the T2F TitleAuthority performs every title move, keyed on
    /// this deed's InstrumentId. The grantor and grantee are real persons.
    /// </summary>
    [Serializable]
    public sealed class DraftedDeed
    {
        public string DeedId = string.Empty;
        public string MatterId = string.Empty;
        public DeedKind Kind = DeedKind.Unspecified;
        public int GrantorPersonId = -1;
        public string GrantorName = string.Empty;
        public int GranteePersonId = -1;
        public string GranteeName = string.Empty;
        public string ParcelId = string.Empty;
        public string ParcelDescription = string.Empty;
        public int ConsiderationCents;
        public string InstrumentId = string.Empty; // EntityKind.Contract; consumed by T2F
        public bool Sealed;
        public DeedStatus Status = DeedStatus.Draft;
        public int DraftedDayIndex;
        public int FinalizedDayIndex = -1;
        public int RecordedDayIndex = -1;
        public string Note = string.Empty;

        public DraftedDeed() { }
    }

    /// <summary>
    /// W9B: deed drafting that feeds the T2F title-chain system. The lawyer
    /// drafts and seals; the T2F TitleAuthority still performs the transfer
    /// (TitleBasis.Purchase requires a conveyance instrument — the deed's
    /// InstrumentId). A deed that is never recorded conveys nothing: paper
    /// is not title (Canon §2.4: legal right and physical possibility are
    /// distinct; unauthorized use can exist without granting title).
    ///
    /// The grantor's recorded-holding is CHECKED at record time but does not
    /// block recording: territorial recording systems record questionable
    /// instruments too, and the resulting defect is exactly what T3D calls
    /// "color of title" (ClaimBasis.ColorOfTitle). The warning is logged.
    /// </summary>
    public sealed class LawyerDeedService
    {
        private readonly LawyerOfficeRequirements office;
        private readonly Dictionary<string, DraftedDeed> deeds =
            new Dictionary<string, DraftedDeed>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public LawyerDeedService(LawyerOfficeRequirements office)
        {
            this.office = office ?? new LawyerOfficeRequirements();
        }

        /// <summary>
        /// Drafts a deed for an open matter. Both parties must be real, named,
        /// distinct persons; the parcel must be named.
        /// </summary>
        public DraftedDeed DraftDeed(
            EntityIdRegistry ids, LawyerPracticeRuntime practice, string matterId,
            DeedKind kind, int grantorPersonId, string grantorName,
            int granteePersonId, string granteeName,
            string parcelId, string parcelDescription, int considerationCents,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (practice == null || practice.GetMatter(matterId) == null)
            {
                diag.Add("LawyerDeedService.DraftDeed: deeds are drafted only for open practice matters.");
                return null;
            }
            LegalMatter matter = practice.GetMatter(matterId);
            if (matter.Status != MatterStatus.Open)
            {
                diag.Add($"LawyerDeedService.DraftDeed: matter {matterId} is {matter.Status} — draft only on open matters.");
                return null;
            }
            if (kind == DeedKind.Unspecified)
            {
                diag.Add("LawyerDeedService.DraftDeed: the deed form must be stated (warranty or quitclaim).");
                return null;
            }
            if (grantorPersonId < 0 || string.IsNullOrWhiteSpace(grantorName))
            {
                diag.Add("LawyerDeedService.DraftDeed: the grantor must be a real, named person.");
                return null;
            }
            if (granteePersonId < 0 || string.IsNullOrWhiteSpace(granteeName))
            {
                diag.Add("LawyerDeedService.DraftDeed: the grantee must be a real, named person.");
                return null;
            }
            if (grantorPersonId == granteePersonId)
            {
                diag.Add("LawyerDeedService.DraftDeed: a person cannot convey to themselves.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                diag.Add("LawyerDeedService.DraftDeed: the parcel must be named.");
                return null;
            }
            if (considerationCents < 0)
            {
                diag.Add("LawyerDeedService.DraftDeed: consideration cannot be negative.");
                return null;
            }

            var deed = new DraftedDeed
            {
                DeedId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MatterId = matterId,
                Kind = kind,
                GrantorPersonId = grantorPersonId,
                GrantorName = grantorName.Trim(),
                GranteePersonId = granteePersonId,
                GranteeName = granteeName.Trim(),
                ParcelId = parcelId.Trim(),
                ParcelDescription = parcelDescription ?? string.Empty,
                ConsiderationCents = considerationCents,
                InstrumentId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                Status = DeedStatus.Draft,
                DraftedDayIndex = dayIndex,
            };
            deeds[deed.DeedId] = deed;
            diag.Add($"LawyerDeedService: deed {deed.DeedId} drafted ({kind}) — '{deed.GrantorName}' to '{deed.GranteeName}', parcel '{deed.ParcelId}', instrument {deed.InstrumentId} (NOT recorded).");
            return deed;
        }

        /// <summary>Applies the office seal. Requires the office to have one (Canon office list).</summary>
        public string SealDeed(string deedId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!deeds.TryGetValue(deedId, out DraftedDeed deed))
                return $"SealDeed: unknown deed '{deedId}'.";
            if (deed.Status != DeedStatus.Draft)
                return $"SealDeed: deed '{deedId}' is {deed.Status} — only drafts take a seal.";
            if (!office.HasSeal)
                return $"SealDeed: the office has no seal (Canon office list) — the deed cannot be sealed.";

            deed.Sealed = true;
            diag.Add($"LawyerDeedService: deed {deedId} sealed.");
            return null;
        }

        /// <summary>Locks the draft so it can be recorded. A sealed deed is required.</summary>
        public string FinalizeDeed(string deedId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!deeds.TryGetValue(deedId, out DraftedDeed deed))
                return $"FinalizeDeed: unknown deed '{deedId}'.";
            if (deed.Status != DeedStatus.Draft)
                return $"FinalizeDeed: deed '{deedId}' is {deed.Status} — only drafts finalize.";
            if (!deed.Sealed)
                return $"FinalizeDeed: deed '{deedId}' is unsealed — seal first.";

            deed.Status = DeedStatus.Finalized;
            deed.FinalizedDayIndex = dayIndex;
            diag.Add($"LawyerDeedService: deed {deedId} finalized — ready to record (still not recorded).");
            return null;
        }

        /// <summary>
        /// Records a finalized deed through the T2F TitleAuthority. This is the
        /// ONLY step that moves title, and the authority performs it. The
        /// grantor's recorded holding is checked and a mismatch is warned —
        /// recording a questionable instrument yields color of title (T3D),
        /// not an ownership fact.
        /// </summary>
        public string RecordDeed(
            string deedId, TitleAuthority titles, EntityIdRegistry ids,
            int dayIndex, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!deeds.TryGetValue(deedId, out DraftedDeed deed))
                return $"RecordDeed: unknown deed '{deedId}'.";
            if (deed.Status != DeedStatus.Finalized)
                return $"RecordDeed: deed '{deedId}' is {deed.Status} — only finalized deeds record.";
            if (titles == null)
                return $"RecordDeed: no title authority given — the lawyer cannot move title alone.";

            string recordedHolder = titles.CurrentHolder(deed.ParcelId);
            if (recordedHolder == null)
            {
                diag.Add($"LawyerDeedService.RecordDeed: parcel '{deed.ParcelId}' is unknown to the title authority — recording refused, not invented.");
                return $"RecordDeed: parcel '{deed.ParcelId}' is unknown to the title authority.";
            }
            if (!string.Equals(recordedHolder, deed.GrantorName, StringComparison.Ordinal))
            {
                diag.Add($"LawyerDeedService.RecordDeed: WARNING — recorded holder of '{deed.ParcelId}' is '{recordedHolder}', " +
                    $"not the grantor '{deed.GrantorName}'. Recorded as color of title (T3D ClaimBasis.ColorOfTitle); validity is for the dispute system.");
            }

            string err = titles.TransferTitle(ids, deed.ParcelId, deed.GranteeName,
                TitleBasis.Purchase, deed.InstrumentId, dayIndex, note ?? string.Empty, diag);
            if (err != null)
            {
                diag.Add($"LawyerDeedService.RecordDeed: the title authority refused: {err}");
                return err;
            }

            deed.Status = DeedStatus.Recorded;
            deed.RecordedDayIndex = dayIndex;
            diag.Add($"LawyerDeedService: deed {deedId} RECORDED — parcel '{deed.ParcelId}' now '{deed.GranteeName}' per the title authority.");
            return null;
        }

        /// <summary>Replaces an unrecorded draft with a fresh one (the old draft is kept, marked superseded).</summary>
        public string SupersedeDeed(string deedId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (!deeds.TryGetValue(deedId, out DraftedDeed deed))
                return $"SupersedeDeed: unknown deed '{deedId}'.";
            if (deed.Status == DeedStatus.Recorded)
                return $"SupersedeDeed: deed '{deedId}' is already recorded — the chain keeps it; draft a corrective deed instead.";
            if (deed.Status == DeedStatus.Superseded)
                return $"SupersedeDeed: deed '{deedId}' is already superseded.";

            deed.Status = DeedStatus.Superseded;
            diag.Add($"LawyerDeedService: deed {deedId} superseded before recording — it never conveyed anything.");
            return null;
        }

        public DraftedDeed GetDeed(string deedId)
        {
            return deeds.TryGetValue(deedId, out DraftedDeed d) ? d : null;
        }

        #region Save / Load

        [Serializable]
        public sealed class LawyerDeedSaveDto
        {
            public List<DraftedDeed> Deeds = new List<DraftedDeed>();
        }

        public LawyerDeedSaveDto CaptureSaveDto()
        {
            var dto = new LawyerDeedSaveDto();
            foreach (var deed in deeds.Values) dto.Deeds.Add(deed);
            return dto;
        }

        public void LoadFromSaveDto(LawyerDeedSaveDto dto)
        {
            deeds.Clear();
            if (dto == null) return;
            foreach (var deed in dto.Deeds)
            {
                if (deed == null) continue;
                deeds[deed.DeedId] = deed;
            }
        }

        #endregion
    }
}
