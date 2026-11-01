using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Lawyer;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D3D: the abstract / title-search business and canon §9.1 stage
    /// recording.
    /// - Abstracts examine REAL T2F chains and REAL T3D claims — unknown
    ///   parcels are refused, never invented; the abstract moves no title.
    /// - The search fee accrues to a real open matter (upstream provenance).
    /// - Dispute stage recording (FormalProceeding = the court appearance, as
    ///   far as the canon describes it) records what the dispute mechanics
    ///   report; it never decides the case.
    /// </summary>
    [TestFixture]
    public sealed class LawyerTitleSearchTests
    {
        private List<string> diag;
        private EntityIdRegistry ids;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            ids = new EntityIdRegistry();
        }

        private static LawyerPracticeRuntime FitPractice(string id = "law-abstract")
        {
            var practice = new LawyerPracticeRuntime(id);
            practice.SetOffice(new LawyerOfficeRequirements
            {
                HasDesk = true,
                HasLawBooks = true,
                HasWritingSupplies = true,
                HasDocumentForms = true,
                HasSeal = true,
                HasSecureRecords = true,
            });
            return practice;
        }

        private static TitleAuthority TwoLinkChain()
        {
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-abs", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, new List<string>());
            return titles;
        }

        [Test]
        public void Abstract_RefusesUnknownParcel_NeverInventsRecords()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = new TitleAuthority();
            var search = new LawyerTitleSearchService();

            Assert.IsNull(search.ProduceAbstract(ids, practice, matter.MatterId, "p-ghost", titles,
                null, "Counsel", 300, 11, diag),
                "No records, no abstract.");
        }

        [Test]
        public void Abstract_ClearChain_ReportsEveryLink_AndBillsTheMatter()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = TwoLinkChain();
            var disputes = new PropertyTroubleshootingService();
            var search = new LawyerTitleSearchService();

            TitleAbstract abstractRecord = search.ProduceAbstract(ids, practice, matter.MatterId, "p-abs", titles,
                disputes, "Counsel", 300, 11, diag);
            Assert.IsNotNull(abstractRecord);
            Assert.AreEqual(TitleAbstractStatus.Clear, abstractRecord.Status);
            Assert.AreEqual(1, abstractRecord.Links.Count, "The registered chain has one link.");
            Assert.AreEqual("Kennedy Baldwin-ooms", abstractRecord.Links[0].HolderName);
            Assert.AreEqual("HomesteadClaim", abstractRecord.Links[0].Basis);
            Assert.AreEqual(matter.MatterId, abstractRecord.MatterId);

            var invoice = practice.IssueInvoice(ids, matter.MatterId, 12, diag);
            Assert.IsNotNull(invoice);
            Assert.AreEqual(300, invoice.TotalCents, "The search fee is real billing against the real client.");
            Assert.AreEqual("Kennedy Baldwin-ooms", invoice.ClientName);
        }

        [Test]
        public void Abstract_AdverseClaim_ReportedFromRealT3DRecords()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 2, "Shannon Cole", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = TwoLinkChain();
            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-abs", "Ada Hawkins",
                ClaimBasis.ColorOfTitle, "deed-1882", 30, "old deed surfaces", diag));

            var search = new LawyerTitleSearchService();
            TitleAbstract abstractRecord = search.ProduceAbstract(ids, practice, matter.MatterId, "p-abs", titles,
                disputes, "Counsel", 0, 31, diag);
            Assert.IsNotNull(abstractRecord);
            Assert.AreEqual(TitleAbstractStatus.AdverseClaims, abstractRecord.Status);
            Assert.AreEqual(1, abstractRecord.AdverseClaimNotes.Count);
            Assert.AreEqual("Ada Hawkins", abstractRecord.AdverseClaimNotes[0].ClaimantName);
            Assert.AreEqual("ColorOfTitle", abstractRecord.AdverseClaimNotes[0].Basis);
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-abs"),
                "Reading the chain moves nothing.");
        }

        [Test]
        public void Abstract_ChainDefect_Flagged_WhenLinkBasisUnstated()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = new TitleAuthority();
            // Unspecified basis slips past registration validation — the search must flag it.
            titles.RegisterParcel("p-brk", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.Unspecified, 5, diag);
            var disputes = new PropertyTroubleshootingService();

            var search = new LawyerTitleSearchService();
            TitleAbstract abstractRecord = search.ProduceAbstract(ids, practice, matter.MatterId, "p-brk", titles,
                disputes, "Counsel", 250, 11, diag);
            Assert.IsNotNull(abstractRecord);
            Assert.AreEqual(TitleAbstractStatus.ChainDefects, abstractRecord.Status);
            Assert.IsTrue(abstractRecord.Findings.Count > 0);
        }

        [Test]
        public void Abstract_RequiresOpenMatter()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = TwoLinkChain();
            var search = new LawyerTitleSearchService();

            Assert.IsNull(practice.AbandonMatter(matter.MatterId, 11, diag));
            Assert.IsNull(search.ProduceAbstract(ids, practice, matter.MatterId, "p-abs", titles,
                null, "Counsel", 300, 12, diag),
                "Abandoned matters take no search work.");
        }

        [Test]
        public void Abstract_SaveRoundTrip_PreservesRecord()
        {
            var practice = FitPractice();
            var matter = practice.OpenMatter(null, 1, "Kennedy Baldwin-ooms", LegalMatterKind.TitleAbstract, "abstract", 10, diag: diag);
            var titles = TwoLinkChain();
            var search = new LawyerTitleSearchService();
            TitleAbstract abstractRecord = search.ProduceAbstract(ids, practice, matter.MatterId, "p-abs", titles,
                null, "Counsel", 300, 11, diag);

            var dto = search.CaptureSaveDto();
            var restored = new LawyerTitleSearchService();
            restored.LoadFromSaveDto(dto);

            TitleAbstract back = restored.GetAbstract(abstractRecord.AbstractId);
            Assert.IsNotNull(back);
            Assert.AreEqual("p-abs", back.ParcelId);
            Assert.AreEqual(TitleAbstractStatus.Clear, back.Status);
            Assert.AreEqual(1, back.Links.Count);
            Assert.AreEqual(300, back.SearchFeeCents);
        }

        // ---------- Canon §9.1 stage recording ----------

        [Test]
        public void StageRecording_TracksCanonLifecycle_AndRecordsNothingMore()
        {
            var practice = FitPractice("law-stage");
            var matter = practice.OpenMatter(null, 7, "Shannon Cole", LegalMatterKind.DisputeRepresentation, "dispute", 10, diag: diag);
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-stage", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);
            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-stage", "Shannon Cole",
                ClaimBasis.DisputedRights, "affidavit-9", 30, "line dispute", diag));
            AdverseClaim claim = disputes.AnalyzeParcel("p-stage", titles, diag).AdverseClaims[0];

            var representation = new DisputeRepresentationService();
            var engagement = representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.Claimant, 7, "Shannon Cole", 31, titles, diag);
            Assert.IsNotNull(engagement);

            Assert.IsNull(representation.RecordStage(engagement.EngagementId,
                DisputeStageKind.NegotiationSettlement, "parties met at the claim office", 45, diag));
            Assert.AreEqual(DisputeStageKind.NegotiationSettlement, engagement.CurrentStage);
            Assert.AreEqual(45, engagement.StageDayIndex);

            // The court appearance, as far as the canon describes it: the
            // formal-proceeding stage is recorded, not conducted.
            Assert.IsNull(representation.RecordStage(engagement.EngagementId,
                DisputeStageKind.FormalProceeding, "counsel appeared; proceeding opened (T3D mechanics)", 60, diag));
            Assert.AreEqual(DisputeStageKind.FormalProceeding, engagement.CurrentStage);
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-stage"),
                "Recording a stage moves no title and decides nothing.");

            Assert.IsNotNull(representation.RecordStage(engagement.EngagementId,
                DisputeStageKind.Unspecified, "?", 61, diag),
                "The stage must be stated.");
        }

        [Test]
        public void StageRecording_OnlyForActiveEngagements()
        {
            var practice = FitPractice("law-stage2");
            var matter = practice.OpenMatter(null, 7, "Shannon Cole", LegalMatterKind.DisputeRepresentation, "dispute", 10, diag: diag);
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-stage2", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);
            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-stage2", "Shannon Cole",
                ClaimBasis.DisputedRights, "affidavit-10", 30, "line dispute", diag));
            AdverseClaim claim = disputes.AnalyzeParcel("p-stage2", titles, diag).AdverseClaims[0];

            var representation = new DisputeRepresentationService();
            var engagement = representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.Claimant, 7, "Shannon Cole", 31, titles, diag);
            Assert.IsNotNull(representation.WithdrawRepresentation(engagement.EngagementId, 40, diag));

            Assert.IsNotNull(representation.RecordStage(engagement.EngagementId,
                DisputeStageKind.FormalProceeding, "too late", 50, diag),
                "Withdrawn engagements take no stage entries.");
            Assert.IsNotNull(representation.RecordStage("no-such-engagement",
                DisputeStageKind.FormalProceeding, "?", 50, diag));
        }
    }
}
