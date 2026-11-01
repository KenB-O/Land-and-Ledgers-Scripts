using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// D4G: building contracts — bid requests, competing bids, acceptance that
    /// creates the written contract, spec change orders, staged acceptance,
    /// defect recording, builder crew capacity, and save/load. Canon Part VI
    /// §7 (Construction &amp; Contracting): §7.1 supported work, §7.2 bidding
    /// and estimating, §7.3 payment timing as contract data, §7.5 organic
    /// scaling through real crew. Audit 04 locks: no contract without two real
    /// parties; no completion without acceptance.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionContractTests
    {
        private static ConstructionContractParty OwnerPerson()
        {
            return ConstructionContractParty.Person(7, "Ada Owner");
        }

        private static ConstructionContractCapacityReading Capacity(string builderId, string name, int activeCrew)
        {
            return new ConstructionContractCapacityReading
            {
                BuilderBusinessId = builderId,
                BuilderDisplayName = name,
                ActiveCrewCount = activeCrew,
                TargetCrewCount = activeCrew + 2,
                SourceLabel = "test worker slots",
            };
        }

        private static ConstructionBidRequest OpenStoreRequest(ConstructionContractBook book, List<string> diag,
            int deadlineDay = 20, List<string> invited = null)
        {
            return book.OpenBidRequest(
                OwnerPerson(),
                ConstructionWorksKind.NewBuilding,
                "24 by 36 ft general store building",
                "Framed store with board-and-batten siding, clear grade finish lumber.",
                60000,
                deadlineDay,
                invited,
                1,
                diag);
        }

        private static ConstructionPaymentTerms StoreTerms()
        {
            return new ConstructionPaymentTerms
            {
                DepositCents = 10000,
                CompletionBalanceCents = 25000,
                Milestones = new List<ConstructionPaymentMilestone>
                {
                    new ConstructionPaymentMilestone
                    {
                        MilestoneId = "MILE-0001",
                        Name = "foundation complete",
                        TriggerDescription = "Stone foundation laid and accepted",
                        AmountCents = 15000,
                    },
                },
            };
        }

        private static ConstructionBid SubmitAlphaBid(ConstructionContractBook book, string requestId,
            List<string> diag, int day = 3)
        {
            return book.SubmitBid(
                requestId,
                "build-1",
                "Alpha Builders",
                ConstructionContractPriceBasis.LumpSum,
                50000,
                5,
                30,
                StoreTerms(),
                new List<ConstructionBidEstimateLine>
                {
                    new ConstructionBidEstimateLine { ComponentName = "labor", AmountCents = 20000 },
                    new ConstructionBidEstimateLine { ComponentName = "materials", AmountCents = 22000 },
                    new ConstructionBidEstimateLine { ComponentName = "contingency", AmountCents = 5000 },
                    new ConstructionBidEstimateLine { ComponentName = "return", AmountCents = 3000 },
                },
                day,
                diag);
        }

        private static ConstructionContract AcceptAlphaContract(ConstructionContractBook book, string bidId,
            List<string> diag, int activeCrew = 4, int day = 4)
        {
            return book.AcceptBid(bidId, Capacity("build-1", "Alpha Builders", activeCrew), null, day, diag);
        }

        // ---------------- bid requests ----------------

        [Test]
        public void BidRequest_Open_RefusesAnonymousOwner()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();

            Assert.IsNull(book.OpenBidRequest(null, ConstructionWorksKind.NewBuilding, "a barn",
                "specs", 60000, 20, null, 1, diag), string.Join("; ", diag));
            Assert.IsNull(book.OpenBidRequest(ConstructionContractParty.Person(-1, ""), ConstructionWorksKind.NewBuilding,
                "a barn", "specs", 60000, 20, null, 1, diag));
            Assert.AreEqual(0, book.RequestCount);
        }

        [Test]
        public void BidRequest_RepairBelowThreshold_Refused()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();

            Assert.IsNull(book.OpenBidRequest(OwnerPerson(), ConstructionWorksKind.Repair,
                "patch the shed roof", "specs", 1500, 20, null, 1, diag),
                "a $15.00 repair is below the written-contract threshold");
            Assert.IsTrue(diag[diag.Count - 1].Contains("repair work order"));

            ConstructionBidRequest request = book.OpenBidRequest(OwnerPerson(), ConstructionWorksKind.Repair,
                "rebuild the collapsed barn wall", "specs", 3000, 20, null, 1, diag);
            Assert.IsNotNull(request);
            Assert.AreEqual("RBID-0001", request.RequestId);
        }

        [Test]
        public void BidRequest_Cancel_RejectsLiveBids_RetainedAsHistory()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);

            Assert.IsNull(book.CancelBidRequest(request.RequestId, 6, diag));
            Assert.AreEqual(ConstructionBidRequestStatus.Cancelled, request.Status);
            Assert.AreEqual(ConstructionBidStatus.Rejected, bid.Status, "cancelled-request bids are retained, not deleted");
            Assert.IsFalse(string.IsNullOrWhiteSpace(bid.RejectionReason));

            Assert.IsNull(book.SubmitBid(request.RequestId, "build-2", "Beta Builders",
                ConstructionContractPriceBasis.LumpSum, 45000, 6, 35, null, null, 7, diag),
                "a cancelled request takes no more bids");
        }

        [Test]
        public void BidRequest_SweepExpired_MarksExpired()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag, deadlineDay: 10);

            Assert.AreEqual(0, book.SweepExpiredRequests(10, diag), "deadline day itself is not expired");
            Assert.AreEqual(1, book.SweepExpiredRequests(11, diag));
            Assert.AreEqual(ConstructionBidRequestStatus.Expired, request.Status);
        }

        // ---------------- bidding ----------------

        [Test]
        public void Bid_Submit_RefusesAnonymousBuilder()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);

            Assert.IsNull(book.SubmitBid(request.RequestId, "", "",
                ConstructionContractPriceBasis.LumpSum, 50000, 5, 30, null, null, 3, diag));
            Assert.IsNull(book.SubmitBid(request.RequestId, null, "Nameless",
                ConstructionContractPriceBasis.LumpSum, 50000, 5, 30, null, null, 3, diag));
            Assert.AreEqual(0, book.BidCount);
        }

        [Test]
        public void Bid_Submit_RefusesLateBid()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag, deadlineDay: 10);

            Assert.IsNull(book.SubmitBid(request.RequestId, "build-1", "Alpha Builders",
                ConstructionContractPriceBasis.LumpSum, 50000, 5, 30, null, null, 11, diag),
                "a bid past the deadline is refused");
            Assert.IsNotNull(book.SubmitBid(request.RequestId, "build-1", "Alpha Builders",
                ConstructionContractPriceBasis.LumpSum, 50000, 5, 30, null, null, 10, diag),
                "a bid on the deadline day is timely");
        }

        [Test]
        public void Bid_Submit_RefusesDuplicateLiveBid()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);

            Assert.IsNotNull(SubmitAlphaBid(book, request.RequestId, diag));
            Assert.IsNull(SubmitAlphaBid(book, request.RequestId, diag),
                "one live bid per builder per request — withdraw before rebidding");
        }

        [Test]
        public void Bid_Submit_HonorsInviteList()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag,
                invited: new List<string> { "build-1" });

            Assert.IsNull(book.SubmitBid(request.RequestId, "build-2", "Beta Builders",
                ConstructionContractPriceBasis.LumpSum, 45000, 6, 35, null, null, 3, diag),
                "an uninvited builder cannot bid on an invited request");
            Assert.IsNotNull(SubmitAlphaBid(book, request.RequestId, diag));
        }

        [Test]
        public void Bid_Withdraw_RetainedAsHistory()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);

            Assert.IsNull(book.WithdrawBid(bid.BidId, 5, diag));
            Assert.AreEqual(ConstructionBidStatus.Withdrawn, bid.Status);
            Assert.IsNull(AcceptAlphaContract(book, bid.BidId, diag),
                "a withdrawn bid cannot be accepted");
            Assert.AreEqual(1, book.BidCount, "the withdrawn bid is retained as history");
        }

        [Test]
        public void Bid_CostPlus_TermsRecordedOnBid()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = book.SubmitBid(request.RequestId, "build-1", "Alpha Builders",
                ConstructionContractPriceBasis.CostPlusPercentage, 60000, 5, 30, null, null, 3, diag);
            Assert.IsNotNull(bid);

            Assert.IsNull(book.SetBidCostPlusTerms(bid.BidId, 50000, 10, 0, diag));
            Assert.AreEqual(50000, bid.EstimatedCostCents);
            Assert.AreEqual(10, bid.CostPlusFeePercent);

            ConstructionBidRequest lumpRequest = book.OpenBidRequest(OwnerPerson(),
                ConstructionWorksKind.Addition, "a woodshed", "specs", 8000, 40, null, 4, diag);
            ConstructionBid lumpBid = SubmitAlphaBid(book, lumpRequest.RequestId, diag);
            Assert.IsNotNull(lumpBid);
            Assert.IsNotNull(book.SetBidCostPlusTerms(lumpBid.BidId, 40000, 10, 0, diag),
                "cost-plus terms do not apply to a lump-sum bid");
        }

        // ---------------- acceptance creates the contract ----------------

        [Test]
        public void AcceptBid_CreatesContract_RejectsRivalBids()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid alpha = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionBid beta = book.SubmitBid(request.RequestId, "build-2", "Beta Builders",
                ConstructionContractPriceBasis.LumpSum, 45000, 6, 35, null, null, 3, diag);
            Assert.AreEqual("BID-0001", alpha.BidId);
            Assert.AreEqual("BID-0002", beta.BidId);

            ConstructionContract contract = AcceptAlphaContract(book, alpha.BidId, diag);
            Assert.IsNotNull(contract, string.Join("; ", diag));
            Assert.AreEqual("CTR-0001", contract.ContractId);
            Assert.AreEqual(ConstructionContractStatus.Executed, contract.Status);
            Assert.IsTrue(contract.OwnerParty.IsReal, "the owner party is real");
            Assert.AreEqual("build-1", contract.BuilderParty.BusinessInstanceId);
            Assert.AreEqual(BusinessType.Builder, contract.BuilderParty.BusinessType);
            Assert.AreEqual(50000, contract.AgreedPriceCents);
            Assert.AreEqual(ConstructionContractPriceBasis.LumpSum, contract.PriceBasis);
            Assert.AreEqual(35, contract.AgreedCompletionDayIndex, "start day 5 + 30 days duration");
            Assert.AreEqual(4, contract.AcceptanceStages.Count, "NewBuilding default stages");
            Assert.AreEqual(ConstructionBidStatus.Accepted, alpha.Status);
            Assert.AreEqual(ConstructionBidStatus.Rejected, beta.Status, "rival bids retained as history");
            Assert.IsFalse(string.IsNullOrWhiteSpace(beta.RejectionReason));
            Assert.AreEqual(ConstructionBidRequestStatus.Awarded, request.Status);
            Assert.AreEqual(alpha.BidId, request.AwardedBidId);
            Assert.AreEqual(2, book.BidCount, "both bids retained as history");

            Assert.AreEqual(1, contract.PaymentObligations.Count, "the deposit obligation is recorded at formation");
            ConstructionPaymentObligation deposit = contract.PaymentObligations[0];
            Assert.AreEqual("deposit", deposit.Kind);
            Assert.AreEqual(10000, deposit.AmountCents);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Recorded, deposit.Status,
                "recorded — never auto-paid");
        }

        [Test]
        public void AcceptBid_RefusesWhenBuilderOverCapacity()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);

            Assert.IsNull(AcceptAlphaContract(book, bid.BidId, diag, activeCrew: 1),
                "1 active worker cannot crew a building contract (2-worker minimum)");
            Assert.AreEqual(ConstructionBidStatus.Submitted, bid.Status, "the refused bid stays live");
            Assert.AreEqual(0, book.ContractCount);
        }

        [Test]
        public void AcceptBid_RefusesCapacityReadingForWrongBuilder()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);

            Assert.IsNull(book.AcceptBid(bid.BidId, Capacity("build-9", "Somebody Else", 8), null, 4, diag),
                "capacity for another builder must not be borrowed");
            Assert.AreEqual(0, book.ContractCount);
        }

        [Test]
        public void Contract_NoFiatParties_FormRefuses()
        {
            var contract = new ConstructionContract();
            var request = new ConstructionBidRequest
            {
                OwnerParty = new ConstructionContractParty(), // blank — not real
                WorksKind = ConstructionWorksKind.NewBuilding,
                WorksDescription = "a barn",
            };
            var bid = new ConstructionBid
            {
                BidId = "BID-0001",
                BuilderBusinessId = "build-1",
                BuilderDisplayName = "Alpha Builders",
                PriceBasis = ConstructionContractPriceBasis.LumpSum,
                PriceCents = 10000,
                OfferedStartDayIndex = 5,
                OfferedDurationDays = 10,
            };
            var diag = new List<string>();

            Assert.IsNotNull(ConstructionContract.FormFromAcceptedBid(contract, request, bid, null, 4, diag),
                "no contract without two real parties");
            Assert.AreEqual(ConstructionContractStatus.Unspecified, contract.Status);
        }

        // ---------------- capacity rules ----------------

        [Test]
        public void Capacity_Rules_FromRealCrew()
        {
            Assert.AreEqual(0, ConstructionContractCapacityRules.MaxConcurrentContracts(null));
            Assert.AreEqual(0, ConstructionContractCapacityRules.MaxConcurrentContracts(
                new ConstructionContractCapacityReading { BuilderBusinessId = "b", ActiveCrewCount = 8 }),
                "no source label — the reading is not trusted");
            Assert.AreEqual(2, ConstructionContractCapacityRules.MaxConcurrentContracts(Capacity("b", "B", 4)));
            Assert.AreEqual(2, ConstructionContractCapacityRules.MaxConcurrentContracts(Capacity("b", "B", 5)));
            Assert.AreEqual(0, ConstructionContractCapacityRules.MaxConcurrentContracts(Capacity("b", "B", 1)));
            Assert.AreEqual(0, ConstructionContractCapacityRules.MaxConcurrentContracts(Capacity("b", "B", 0)));

            Assert.IsNull(ConstructionContractCapacityRules.CheckCapacity(Capacity("b", "B", 4), 1));
            Assert.IsNotNull(ConstructionContractCapacityRules.CheckCapacity(Capacity("b", "B", 4), 2),
                "a 4-person crew holds at most 2 contracts");
            Assert.IsNotNull(ConstructionContractCapacityRules.CheckCapacity(null, 0),
                "no invented crew — no capacity");
        }

        // ---------------- contract lifecycle ----------------

        [Test]
        public void Contract_Lifecycle_Start_Stages_InOrder_FinalAccept_ReleasesBalance()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);
            Assert.IsNotNull(contract);

            Assert.IsNull(contract.StartWork(5, diag));
            Assert.AreEqual(ConstructionContractStatus.InProgress, contract.Status);

            Assert.IsNotNull(contract.AcceptStage("STAGE-0002", 10, "Ada Owner", diag),
                "stages are accepted in order");
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 10, "Ada Owner", diag));
            Assert.AreEqual(ConstructionContractStatus.AcceptanceInProgress, contract.Status);
            Assert.IsNull(contract.AcceptStage("STAGE-0002", 12, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0003", 14, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0004", 16, "Ada Owner", diag));
            Assert.AreEqual(4, contract.AcceptedStageCount());

            Assert.IsNull(contract.FinalAccept(17, "Ada Owner", diag));
            Assert.AreEqual(ConstructionContractStatus.Accepted, contract.Status);
            Assert.IsTrue(contract.IsTerminal);
            Assert.IsFalse(contract.IsActive);

            Assert.AreEqual(2, contract.PaymentObligations.Count);
            ConstructionPaymentObligation balance = contract.PaymentObligations[1];
            Assert.AreEqual("final-balance", balance.Kind);
            Assert.AreEqual(25000, balance.AmountCents);
            Assert.AreEqual(ConstructionPaymentObligationStatus.Released, balance.Status,
                "final acceptance releases the balance as an obligation — recorded, not auto-paid");
            Assert.AreEqual(17, balance.ReleasedDayIndex);
        }

        [Test]
        public void Contract_FinalAccept_RefusedWithoutFullAcceptance()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);

            Assert.IsNotNull(contract.FinalAccept(9, "Ada Owner", diag),
                "no completion without acceptance — work has not even started");
            Assert.IsNull(contract.StartWork(10, diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 11, "Ada Owner", diag));
            Assert.IsNotNull(contract.FinalAccept(12, "Ada Owner", diag),
                "three of four stages are still pending");
            Assert.AreEqual(ConstructionContractStatus.AcceptanceInProgress, contract.Status);
        }

        [Test]
        public void Contract_Defect_BlocksFinalAcceptance_UntilRemedied()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);

            Assert.IsNull(contract.StartWork(5, diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 10, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0002", 12, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0003", 14, "Ada Owner", diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0004", 16, "Ada Owner", diag));

            Assert.IsNull(contract.RecordDefect("STAGE-0002", "window frame out of square", "Ada Owner", 18, diag));
            Assert.AreEqual("DEF-0001", contract.Defects[0].DefectId);
            Assert.AreEqual(1, contract.OutstandingDefectCount());

            Assert.IsNotNull(contract.FinalAccept(19, "Ada Owner", diag),
                "an open defect blocks final acceptance");
            Assert.IsNull(contract.SetDefectStatus("DEF-0001", ConstructionDefectStatus.Acknowledged, 19, diag));
            Assert.IsNotNull(contract.FinalAccept(20, "Ada Owner", diag),
                "an acknowledged-but-unremedied defect still blocks");
            Assert.IsNull(contract.SetDefectStatus("DEF-0001", ConstructionDefectStatus.Remedied, 21, diag));
            Assert.AreEqual(0, contract.OutstandingDefectCount());
            Assert.IsNull(contract.FinalAccept(22, "Ada Owner", diag));
            Assert.AreEqual(ConstructionContractStatus.Accepted, contract.Status);
        }

        [Test]
        public void Contract_ChangeOrder_NeedsBothParties()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);
            Assert.AreEqual(35, contract.AgreedCompletionDayIndex);

            Assert.IsNull(contract.ProposeChangeOrder("add rear lean-to", 5000, 7,
                ConstructionContractSide.Owner, 8, diag));
            Assert.AreEqual("CO-0001", contract.ChangeOrders[0].ChangeOrderId);
            Assert.AreEqual(ConstructionChangeOrderStatus.Proposed, contract.ChangeOrders[0].Status);

            Assert.IsNull(contract.RecordChangeOrderAcceptance("CO-0001", ConstructionContractSide.Owner, 9, diag));
            Assert.AreEqual(ConstructionChangeOrderStatus.Proposed, contract.ChangeOrders[0].Status,
                "one side is not enough");
            Assert.AreEqual(50000, contract.AgreedPriceCents, "price moves only on both-party acceptance");

            Assert.IsNull(contract.RecordChangeOrderAcceptance("CO-0001", ConstructionContractSide.Builder, 10, diag));
            Assert.AreEqual(ConstructionChangeOrderStatus.Accepted, contract.ChangeOrders[0].Status);
            Assert.AreEqual(55000, contract.AgreedPriceCents);
            Assert.AreEqual(42, contract.AgreedCompletionDayIndex, "35 + 7 days");

            Assert.IsNotNull(contract.RejectChangeOrder("CO-0001", 11, diag),
                "an accepted change order cannot be rejected");
        }

        [Test]
        public void Contract_PaymentTerms_D4HSummary()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);

            Assert.AreEqual(50000, contract.PaymentTerms.ScheduledTotalCents(),
                "deposit 10000 + milestone 15000 + completion balance 25000");
            string summary = contract.BuildD4HPaymentSummary();
            Assert.IsTrue(summary.Contains("CTR-0001"));
            Assert.IsTrue(summary.Contains("LumpSum"));
            Assert.IsTrue(summary.Contains("1 milestone(s)"));
        }

        // ---------------- specs as data ----------------

        [Test]
        public void SpecSection_MaterialRequirement_Coherence()
        {
            var anySource = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-0001",
                MaterialKind = ConstructionMaterialKind.Hardware,
                MaterialDisplayName = "cut nails, 20d",
                RequiredUnits = 50,
                UnitLabel = "pounds",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
            };
            Assert.IsTrue(anySource.IsCoherent);

            var namedYard = new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-0002",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "2x4 studs",
                RequiredUnits = 200,
                UnitLabel = "board feet",
                GradeId = "merchantable",
                ProvenanceKind = ConstructionMaterialProvenanceKind.NamedYard,
            };
            Assert.IsFalse(namedYard.IsCoherent, "a named-yard requirement without the yard named is incoherent");
            namedYard.SupplierBusinessId = "yard-1";
            namedYard.SupplierBusinessType = BusinessType.LumberYard;
            namedYard.SupplierDisplayName = "Test Lumber Yard";
            namedYard.LotReferenceId = "yard-lot-42";
            Assert.IsTrue(namedYard.IsCoherent);
        }

        // ---------------- save/load ----------------

        [Test]
        public void Book_SaveLoad_RoundTrip()
        {
            var book = new ConstructionContractBook();
            var diag = new List<string>();
            ConstructionBidRequest request = OpenStoreRequest(book, diag);
            ConstructionBid bid = SubmitAlphaBid(book, request.RequestId, diag);
            ConstructionContract contract = AcceptAlphaContract(book, bid.BidId, diag);
            Assert.IsNull(contract.StartWork(5, diag));
            Assert.IsNull(contract.AcceptStage("STAGE-0001", 10, "Ada Owner", diag));
            Assert.IsNull(contract.RecordDefect("STAGE-0001", "sill plate checks", "Ada Owner", 11, diag));

            ConstructionContractBook.ConstructionContractBookSaveDto dto = book.CaptureSaveDto();
            var restored = new ConstructionContractBook();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.RequestCount);
            Assert.AreEqual(1, restored.BidCount);
            Assert.AreEqual(1, restored.ContractCount);
            ConstructionContract back = restored.FindContract("CTR-0001");
            Assert.IsNotNull(back);
            Assert.AreEqual(ConstructionContractStatus.AcceptanceInProgress, back.Status);
            Assert.AreEqual(50000, back.AgreedPriceCents);
            Assert.AreEqual(1, back.AcceptedStageCount());
            Assert.AreEqual(1, back.OutstandingDefectCount());
            Assert.AreEqual("DEF-0001", back.Defects[0].DefectId);
            Assert.AreEqual(ConstructionContractPriceBasis.LumpSum, back.PriceBasis);
            Assert.AreEqual("build-1", back.BuilderParty.BusinessInstanceId);

            ConstructionBidRequest second = restored.OpenBidRequest(OwnerPerson(),
                ConstructionWorksKind.Addition, "add a woodshed", "specs", 8000, 40, null, 12, diag);
            Assert.AreEqual("RBID-0002", second.RequestId, "request numbering survives the round trip");
            ConstructionBid secondBid = restored.SubmitBid(second.RequestId, "build-2", "Beta Builders",
                ConstructionContractPriceBasis.LumpSum, 7500, 13, 10, null, null, 13, diag);
            Assert.AreEqual("BID-0002", secondBid.BidId, "bid numbering survives the round trip");
        }
    }
}
