using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Lawyer;
using LandLedgers.Economy.Estates;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// W9B: the lawyer acts WITHIN systems — no fiat.
    /// - Deed drafting produces instruments CONSUMABLE by the T2F title
    ///   chain; draft != record, and only the TitleAuthority moves title.
    /// - Contracts record what real parties agreed; obligations bind only
    ///   parties; completion/termination are recorded, not decreed.
    /// - Wills go through the NX-3C ProbateService — probate validates,
    ///   the lawyer never does.
    /// - Dispute representation books counsel's appearance on real T3D
    ///   claims; outcomes still belong to the dispute mechanics.
    /// </summary>
    [TestFixture]
    public sealed class LawyerInstrumentDraftingTests
    {
        private List<string> diag;
        private EntityIdRegistry ids;

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            ids = new EntityIdRegistry();
        }

        private static LawyerPracticeRuntime FitPractice(string id = "law-1")
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

        private static LegalMatter OpenMatter(LawyerPracticeRuntime practice, LegalMatterKind kind, int client, string name)
        {
            return practice.OpenMatter(null, client, name, kind, "test matter", 10, diag: new List<string>());
        }

        private static PopulationState People(params (int id, string first, string last)[] defs)
        {
            var population = new PopulationState();
            foreach (var def in defs)
            {
                population.people.Add(new PersonState { id = def.id, firstName = def.first, lastName = def.last });
            }
            return population;
        }

        // ---------- Deeds ----------

        [Test]
        public void DeedLifecycle_DraftSealFinalizeRecord_MovesTitleOnlyThroughT2F()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DeedDrafting, 1, "Kennedy Baldwin-ooms");
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-deed", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);

            var deeds = new LawyerDeedService(practice.Office);
            var deed = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole",
                "p-deed", "40 acres", 10000, 10, diag);
            Assert.IsNotNull(deed);
            Assert.AreEqual(DeedStatus.Draft, deed.Status);
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-deed"),
                "A draft moves nothing.");

            Assert.IsNull(deeds.SealDeed(deed.DeedId, diag));
            Assert.IsNull(deeds.FinalizeDeed(deed.DeedId, 11, diag));
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-deed"),
                "A finalized-but-unrecorded deed moves nothing: draft != record.");

            Assert.IsNull(deeds.RecordDeed(deed.DeedId, titles, ids, 12, "sale", diag));
            Assert.AreEqual(DeedStatus.Recorded, deed.Status);
            Assert.AreEqual("Shannon Cole", titles.CurrentHolder("p-deed"), "Only T2F moved the title.");
            Assert.IsNotEmpty(deed.InstrumentId);
            var chain = titles.ChainOf("p-deed");
            Assert.AreEqual(deed.InstrumentId, chain[chain.Count - 1].InstrumentId,
                "The title record keys on the deed's instrument.");
        }

        [Test]
        public void Deed_RejectedUnlessPartiesReal_Distinct_AndSealed()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DeedDrafting, 1, "Kennedy Baldwin-ooms");
            var deeds = new LawyerDeedService(practice.Office);

            Assert.IsNull(deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                1, "Kennedy Baldwin-ooms", 1, "Kennedy Baldwin-ooms", "p-x", "40 acres", 0, 10, diag),
                "No self-conveyance.");
            Assert.IsNull(deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.Unspecified,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "p-x", "40 acres", 0, 10, diag),
                "Deed form must be stated.");

            var deed = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.QuitclaimDeed,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "p-x", "40 acres", 0, 10, diag);
            Assert.IsNotNull(deeds.FinalizeDeed(deed.DeedId, 11, diag),
                "An unsealed deed does not finalize.");

            var sealess = new LawyerDeedService(new LawyerOfficeRequirements { HasDesk = true });
            Assert.IsNotNull(sealess.SealDeed(deed.DeedId, diag),
                "No seal in the office, no sealing (Canon office list).");
        }

        [Test]
        public void Deed_UnknownParcel_Refused_GrantorMismatch_WarnsAsColorOfTitle()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DeedDrafting, 3, "Arnold Baldwin");
            var titles = new TitleAuthority();
            var deeds = new LawyerDeedService(practice.Office);

            var ghost = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                3, "Arnold Baldwin", 4, "Elaine Hart", "p-ghost", "80 acres", 0, 10, diag);
            Assert.IsNull(deeds.SealDeed(ghost.DeedId, diag));
            Assert.IsNull(deeds.FinalizeDeed(ghost.DeedId, 11, diag));
            Assert.IsNotNull(deeds.RecordDeed(ghost.DeedId, titles, ids, 12, "sale", diag),
                "Recording an unknown parcel is refused, not invented.");

            titles.RegisterParcel("p-real", "80 acres", 80f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);
            var questionable = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                3, "Arnold Baldwin", 4, "Elaine Hart", "p-real", "80 acres", 0, 10, diag);
            Assert.IsNull(deeds.SealDeed(questionable.DeedId, diag));
            Assert.IsNull(deeds.FinalizeDeed(questionable.DeedId, 11, diag));
            Assert.IsNull(deeds.RecordDeed(questionable.DeedId, titles, ids, 12, "sale", diag),
                "Territorial recorders record questionable instruments; the warning is the safeguard.");
            Assert.AreEqual("Elaine Hart", titles.CurrentHolder("p-real"));
            StringAssert.Contains("color of title", string.Join("\n", diag),
                "The mismatch is logged as color of title for the T3D dispute system.");
        }

        // ---------- Contracts ----------

        [Test]
        public void ContractLifecycle_RealParties_Obligations_Execute_Terminate()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.ContractDrafting, 1, "Kennedy Baldwin-ooms");
            var service = new LawyerContractService();

            var contract = service.DraftContract(ids, practice, matter.MatterId,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole",
                "Lumber supply agreement", 40, 10, diag);
            Assert.IsNotNull(contract);

            Assert.IsNotNull(service.AddObligation(contract.ContractId, 9, "deliver lumber", 50, "grade", diag),
                "The lawyer cannot obligate strangers.");
            Assert.IsNull(service.AddObligation(contract.ContractId, 2, "deliver 500 board-ft of pine lumber", 50, "mill-run grade", diag));
            Assert.IsNull(service.AddObligation(contract.ContractId, 1, "pay 800c on accepted delivery", 55, "coin", diag));

            Assert.IsNull(service.ExecuteContract(contract.ContractId, 20, diag));
            Assert.AreEqual(DraftedContractStatus.Executed, contract.Status);
            Assert.AreEqual(2, contract.Obligations.Count);

            Assert.IsNull(service.RecordTermination(contract.ContractId, "buyer repudiated after mill fire — reported by dispute mechanics", 60, diag));
            Assert.AreEqual(DraftedContractStatus.Terminated, contract.Status);
        }

        [Test]
        public void Contract_EmptyPromises_DoNotExecute()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.ContractDrafting, 1, "Kennedy Baldwin-ooms");
            var service = new LawyerContractService();

            var contract = service.DraftContract(ids, practice, matter.MatterId,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "Vague understanding", 40, 10, diag);
            Assert.IsNotNull(service.ExecuteContract(contract.ContractId, 20, diag),
                "A contract obligating nobody is not executed.");
        }

        // ---------- Wills ----------

        [Test]
        public void Will_DraftedThroughProbate_ProbateValidates_NotTheLawyer()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.WillDrafting, 1, "Kennedy Baldwin-ooms");
            var population = People((1, "Kennedy", "Baldwin-ooms"), (2, "Emma", "Baldwin-ooms"),
                (3, "Arnold", "Baldwin"), (4, "Elaine", "Hart"));
            var probate = new ProbateService();
            var service = new LawyerWillDraftingService();

            // One witness only — probate rejects; the lawyer drafts nothing.
            var rejected = service.DraftWill(ids, practice, matter.MatterId, probate,
                1, "Kennedy Baldwin-ooms", 2, new List<int> { 3 }, 10, population, diag);
            Assert.IsNull(rejected, "Probate validation is probate's, and it said no.");

            var will = service.DraftWill(ids, practice, matter.MatterId, probate,
                1, "Kennedy Baldwin-ooms", 2, new List<int> { 3, 4 }, 10, population, diag);
            Assert.IsNotNull(will, "Probate accepted — the will exists.");
            Assert.IsNotNull(service.GetDraftedWill(will.WillId), "The practice holds the drafting ledger entry.");

            var err = service.AddBequest(practice, probate, will.WillId,
                new Bequest { Kind = BequestKind.CashAmount, BeneficiaryPersonId = 2, BeneficiaryName = "Emma Baldwin-ooms", CashCents = 5000 },
                population, diag);
            Assert.IsNull(err, "Probate accepted the bequest.");
            Assert.AreEqual(1, probate.Get(will.WillId).Bequests.Count);
        }

        // ---------- Dispute representation ----------

        [Test]
        public void Representation_BooksAppearance_OnRealT3DClaim_DecidesNothing()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DisputeRepresentation, 7, "Shannon Cole");
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-dispute", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);

            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-dispute", "Shannon Cole",
                ClaimBasis.HonestBoundaryError, "affidavit-7", 30, "fence line off by 20 rods", diag));
            AdverseClaim claim = disputes.AnalyzeParcel("p-dispute", titles, diag).AdverseClaims[0];

            var representation = new DisputeRepresentationService();
            var engagement = representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.Claimant, 7, "Shannon Cole", 31, titles, diag);
            Assert.IsNotNull(engagement);
            Assert.AreEqual(RepresentationStatus.Active, engagement.Status);
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-dispute"),
                "Counsel's appearance moves no title.");

            Assert.IsNull(representation.ConcludeRepresentation(engagement.EngagementId,
                "Claim settled by negotiation — claimant withdrew, boundary resurveyed (T3D mechanics).", 90, diag));
            Assert.AreEqual(RepresentationStatus.Concluded, engagement.Status);
            Assert.AreEqual("Kennedy Baldwin-ooms", titles.CurrentHolder("p-dispute"),
                "The outcome was recorded, not decreed; T2F still holds the chain.");
        }

        [Test]
        public void Representation_SidesMustMatch_TheClaimant()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DisputeRepresentation, 7, "Shannon Cole");
            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-x", "Shannon Cole",
                ClaimBasis.DisputedRights, "affidavit-8", 30, "note", diag));
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-x", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);
            AdverseClaim claim = disputes.AnalyzeParcel("p-x", titles, diag).AdverseClaims[0];

            var representation = new DisputeRepresentationService();
            Assert.IsNull(representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.Claimant, 1, "Kennedy Baldwin-ooms", 31, titles, diag),
                "Cannot appear for the claimant while retained by the record holder's side as claimant.");
        }

        [Test]
        public void Representation_RecordHolderSide_VerifiedAgainstTitleAuthority()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DisputeRepresentation, 1, "Kennedy Baldwin-ooms");
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-rh", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);

            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-rh", "Shannon Cole",
                ClaimBasis.DefectiveDocumentation, "affidavit-10", 30, "note", diag));
            AdverseClaim claim = disputes.AnalyzeParcel("p-rh", titles, diag).AdverseClaims[0];

            var representation = new DisputeRepresentationService();
            Assert.IsNull(representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.RecordHolder, 7, "Shannon Cole", 31, titles, diag),
                "Counsel does not appear for the record holder on behalf of the claimant.");
            var engagement = representation.EngageRepresentation(ids, practice, matter.MatterId,
                claim, RepresentationSide.RecordHolder, 1, "Kennedy Baldwin-ooms", 31, titles, diag);
            Assert.IsNotNull(engagement, "The recorded holder is confirmed by the title authority.");
        }

        [Test]
        public void SaveRoundTrip_PreservesDeeds_Contracts_Engagements()
        {
            var practice = FitPractice();
            var matter = OpenMatter(practice, LegalMatterKind.DeedDrafting, 1, "Kennedy Baldwin-ooms");

            var deeds = new LawyerDeedService(practice.Office);
            var deed = deeds.DraftDeed(ids, practice, matter.MatterId, DeedKind.WarrantyDeed,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "p-s", "40 acres", 10000, 10, diag);
            var restoredDeeds = new LawyerDeedService(practice.Office);
            restoredDeeds.LoadFromSaveDto(deeds.CaptureSaveDto());
            Assert.IsNotNull(restoredDeeds.GetDeed(deed.DeedId));
            Assert.AreEqual("p-s", restoredDeeds.GetDeed(deed.DeedId).ParcelId);

            var contracts = new LawyerContractService();
            var contract = contracts.DraftContract(ids, practice, matter.MatterId,
                1, "Kennedy Baldwin-ooms", 2, "Shannon Cole", "Note", 40, 10, diag);
            var restoredContracts = new LawyerContractService();
            restoredContracts.LoadFromSaveDto(contracts.CaptureSaveDto());
            Assert.IsNotNull(restoredContracts.GetContract(contract.ContractId));

            var disputes = new PropertyTroubleshootingService();
            Assert.IsNull(disputes.RecordAdverseClaim(ids, "p-s", "Shannon Cole",
                ClaimBasis.DisputedRights, "affidavit-9", 30, "note", diag));
            var titles = new TitleAuthority();
            titles.RegisterParcel("p-s", "40 acres", 40f, "Kennedy Baldwin-ooms", TitleBasis.HomesteadClaim, 5, diag);
            var representation = new DisputeRepresentationService();
            var engagement = representation.EngageRepresentation(ids, practice, matter.MatterId,
                disputes.AnalyzeParcel("p-s", titles, diag).AdverseClaims[0],
                RepresentationSide.Claimant, 7, "Shannon Cole", 31, titles, diag);
            var restoredRep = new DisputeRepresentationService();
            restoredRep.LoadFromSaveDto(representation.CaptureSaveDto());
            Assert.IsNotNull(restoredRep.GetEngagement(engagement.EngagementId));

            var willService = new LawyerWillDraftingService();
            var restoredWills = new LawyerWillDraftingService();
            restoredWills.LoadFromSaveDto(willService.CaptureSaveDto());
            Assert.IsNull(restoredWills.GetDraftedWill("nonexistent"));
        }
    }
}
