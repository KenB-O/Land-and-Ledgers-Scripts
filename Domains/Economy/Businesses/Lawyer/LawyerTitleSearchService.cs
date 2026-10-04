using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Lawyer
{
    /// <summary>D3D: the verdict of a title examination.</summary>
    public enum TitleAbstractStatus
    {
        Unspecified = 0,
        Clear = 1,         // unbroken chain, no adverse claims
        ChainDefects = 2,   // chain gaps or weak links found, no adverse claims
        AdverseClaims = 3,  // at least one adverse claim recorded (dominates defects)
    }

    /// <summary>
    /// D3D: one chain link as the searcher found it. Every field traces to a
    /// real T2F TitleRecord — the abstract summarizes records; it invents none.
    /// </summary>
    [Serializable]
    public sealed class ChainLinkSummary
    {
        public int LinkIndex;
        public string HolderName = string.Empty;
        public string Basis = string.Empty;
        public string InstrumentId = string.Empty;
        public int DayIndex;
        public string Note = string.Empty;

        public ChainLinkSummary() { }
    }

    /// <summary>D3D: one adverse claim as cited in the abstract (traces to a real T3D AdverseClaim).</summary>
    [Serializable]
    public sealed class AbstractAdverseClaimNote
    {
        public string ClaimId = string.Empty;
        public string ClaimantName = string.Empty;
        public string Basis = string.Empty;
        public string BasisDocument = string.Empty;
        public int DayIndex;

        public AbstractAdverseClaimNote() { }
    }

    /// <summary>
    /// D3D: one produced title abstract. An abstract is an EXAMINATION of the
    /// recorded chain, not a conveyance: it moves nothing, clears nothing,
    /// and settles nothing. It reports what the records show — clear, chain
    /// defects, or adverse claims — so a buyer, lender, or the practice's own
    /// deed work knows the title risk before money moves.
    /// </summary>
    [Serializable]
    public sealed class TitleAbstract
    {
        public string AbstractId = string.Empty;
        public string MatterId = string.Empty;
        public string ParcelId = string.Empty;
        public int ExaminedDayIndex;
        public List<ChainLinkSummary> Links = new List<ChainLinkSummary>();
        public List<string> Findings = new List<string>();
        public List<AbstractAdverseClaimNote> AdverseClaimNotes = new List<AbstractAdverseClaimNote>();
        public TitleAbstractStatus Status = TitleAbstractStatus.Unspecified;
        public string ExaminerName = string.Empty;
        public int SearchFeeCents;

        public TitleAbstract() { }
    }

    /// <summary>
    /// D3D: the abstract / title-search business. Canon Part II §2.4 lists
    /// "record searches" among the supported property-troubleshooting paths,
    /// and the candidate expertise domain "Survey, Title &amp; Property Records"
    /// covers survey descriptions, title concerns and parcel-history
    /// interpretation — this is the law practice's product for that work.
    ///
    /// Upstream provenance, enforced:
    /// - the parcel must be registered with the T2F TitleAuthority — an
    ///   unknown parcel is refused, not abstracted from thin air;
    /// - every chain link summarizes a real TitleRecord (T2F is the chain
    ///   authority);
    /// - adverse-claim findings come from the T3D troubleshooting service's
    ///   own records, never invented here;
    /// - the search fee accrues to a real open practice matter, so the money
    ///   traces to a real client.
    ///
    /// The abstract records; the title authority, the dispute system, and the
    /// world decide. Reading a chain never repairs it.
    /// </summary>
    public sealed class LawyerTitleSearchService
    {
        private readonly Dictionary<string, TitleAbstract> abstracts =
            new Dictionary<string, TitleAbstract>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Examines the recorded chain for a parcel and produces an abstract.
        /// The disputes service is optional: with it, chain breaks and adverse
        /// claims come from T3D's own analysis; without it, the abstract
        /// covers the raw chain only and says so.
        /// </summary>
        public TitleAbstract ProduceAbstract(
            EntityIdRegistry ids, LawyerPracticeRuntime practice, string matterId,
            string parcelId, TitleAuthority titles,
            PropertyTroubleshootingService disputes, string examinerName,
            int searchFeeCents, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (practice == null || practice.GetMatter(matterId) == null)
            {
                diag.Add("LawyerTitleSearchService.ProduceAbstract: abstracts are produced only for open practice matters.");
                return null;
            }
            if (practice.GetMatter(matterId).Status != MatterStatus.Open)
            {
                diag.Add($"LawyerTitleSearchService.ProduceAbstract: matter {matterId} is {practice.GetMatter(matterId).Status} — search only on open matters.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                diag.Add("LawyerTitleSearchService.ProduceAbstract: the parcel must be named.");
                return null;
            }
            if (titles == null)
            {
                diag.Add("LawyerTitleSearchService.ProduceAbstract: no title authority — abstracts are made from records, not invented.");
                return null;
            }
            string holder = titles.CurrentHolder(parcelId);
            if (holder == null)
            {
                diag.Add($"LawyerTitleSearchService.ProduceAbstract: parcel '{parcelId}' is unknown to the title authority — refused, not invented.");
                return null;
            }
            if (searchFeeCents < 0)
            {
                diag.Add("LawyerTitleSearchService.ProduceAbstract: the search fee cannot be negative.");
                return null;
            }

            var abstractRecord = new TitleAbstract
            {
                AbstractId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N"),
                MatterId = matterId,
                ParcelId = parcelId.Trim(),
                ExaminedDayIndex = dayIndex,
                ExaminerName = examinerName ?? string.Empty,
                SearchFeeCents = searchFeeCents,
            };

            IReadOnlyList<TitleRecord> chain = titles.ChainOf(parcelId);
            for (int i = 0; i < chain.Count; i++)
            {
                TitleRecord record = chain[i];
                abstractRecord.Links.Add(new ChainLinkSummary
                {
                    LinkIndex = i,
                    HolderName = record.HolderName ?? string.Empty,
                    Basis = record.Basis.ToString(),
                    InstrumentId = record.InstrumentId ?? string.Empty,
                    DayIndex = record.DayIndex,
                    Note = record.Note ?? string.Empty,
                });
            }

            ParcelDisputeReport report = null;
            if (disputes != null)
            {
                report = disputes.AnalyzeParcel(parcelId, titles, diag);
                foreach (string knownFact in report.KnownFacts)
                    abstractRecord.Findings.Add("Record: " + knownFact);
                foreach (string chainBreak in report.ChainBreaks)
                    abstractRecord.Findings.Add("Chain break: " + chainBreak);
                foreach (AdverseClaim claim in report.AdverseClaims)
                {
                    abstractRecord.AdverseClaimNotes.Add(new AbstractAdverseClaimNote
                    {
                        ClaimId = claim.ClaimId ?? string.Empty,
                        ClaimantName = claim.ClaimantName ?? string.Empty,
                        Basis = claim.Basis.ToString(),
                        BasisDocument = claim.BasisDocument ?? string.Empty,
                        DayIndex = claim.DayIndex,
                    });
                    abstractRecord.Findings.Add(string.Format(
                        "Adverse claim: '{0}' asserts {1} (document: {2}).",
                        claim.ClaimantName, claim.Basis,
                        string.IsNullOrWhiteSpace(claim.BasisDocument) ? "(none stated)" : claim.BasisDocument));
                }
            }
            else
            {
                abstractRecord.Findings.Add("No dispute-service analysis available — abstract covers the raw recorded chain only.");
            }

            if (abstractRecord.AdverseClaimNotes.Count > 0)
                abstractRecord.Status = TitleAbstractStatus.AdverseClaims;
            else if (report != null && report.ChainBreaks.Count > 0)
                abstractRecord.Status = TitleAbstractStatus.ChainDefects;
            else
                abstractRecord.Status = TitleAbstractStatus.Clear;

            abstractRecord.Findings.Add(string.Format(
                "Examined {0} chain link(s); recorded holder '{1}'; verdict: {2}.",
                abstractRecord.Links.Count, holder, abstractRecord.Status));

            abstracts[abstractRecord.AbstractId] = abstractRecord;

            if (searchFeeCents > 0)
            {
                string feeErr = practice.AccrueFee(matterId,
                    string.Format("Title search / abstract of '{0}'", parcelId), searchFeeCents, diag);
                if (feeErr != null)
                {
                    diag.Add($"LawyerTitleSearchService.ProduceAbstract: the abstract was produced but the fee did not accrue: {feeErr}");
                }
            }

            diag.Add(string.Format(
                "LawyerTitleSearchService: abstract {0} produced for parcel '{1}' — {2} link(s), verdict {3}. It records; it decides nothing.",
                abstractRecord.AbstractId, parcelId, abstractRecord.Links.Count, abstractRecord.Status));
            return abstractRecord;
        }

        public TitleAbstract GetAbstract(string abstractId)
        {
            return abstracts.TryGetValue(abstractId, out TitleAbstract a) ? a : null;
        }

        #region Save / Load

        [Serializable]
        public sealed class LawyerTitleSearchSaveDto
        {
            public List<TitleAbstract> Abstracts = new List<TitleAbstract>();
        }

        public LawyerTitleSearchSaveDto CaptureSaveDto()
        {
            var dto = new LawyerTitleSearchSaveDto();
            foreach (var abstractRecord in abstracts.Values) dto.Abstracts.Add(abstractRecord);
            return dto;
        }

        public void LoadFromSaveDto(LawyerTitleSearchSaveDto dto)
        {
            abstracts.Clear();
            if (dto == null) return;
            foreach (var abstractRecord in dto.Abstracts)
            {
                if (abstractRecord == null) continue;
                abstracts[abstractRecord.AbstractId] = abstractRecord;
            }
        }

        #endregion
    }
}
