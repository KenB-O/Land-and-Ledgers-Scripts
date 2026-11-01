using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass IX WS6 (A1-06): dispute claims are recorded at acquisition —
    /// a non-Clean title chain yields a Verified claim raised by the new
    /// owner, a displaced non-owner occupant yields a Reported, Contested
    /// claim — and quick diligence flags Open/Contested claims without
    /// leaking claimant identity.
    /// </summary>
    [TestFixture]
    public sealed class Pass9PropertyDisputeDiligenceTests
    {
        private static TitleAuthority TitlesWithBrokenChain(string parcelId)
        {
            var titles = new TitleAuthority();
            var diag = new List<string>();
            // Root with unstated basis = a chain break (the PlayerTitleBridge
            // first-sighting pattern), then a proper purchase transfer.
            Assert.IsNotNull(titles.RegisterParcel(parcelId, "plot", 1f, "Seller",
                TitleBasis.Unspecified, 0, diag));
            Assert.IsNull(titles.TransferTitle(null, parcelId, "Buyer",
                TitleBasis.Purchase, "deed-1", 10, "purchase", diag));
            return titles;
        }

        private static TitleAuthority TitlesWithCleanChain(string parcelId)
        {
            var titles = new TitleAuthority();
            var diag = new List<string>();
            Assert.IsNotNull(titles.RegisterParcel(parcelId, "plot", 1f, "Seller",
                TitleBasis.Grant, 0, diag));
            Assert.IsNull(titles.TransferTitle(null, parcelId, "Buyer",
                TitleBasis.Purchase, "deed-1", 10, "purchase", diag));
            return titles;
        }

        [Test]
        public void Acquisition_BrokenChain_RecordsVerifiedClaimRaisedByNewOwner()
        {
            TitleAuthority titles = TitlesWithBrokenChain("parcel-broken");
            var disputes = new PropertyDisputeService();
            List<DisputeClaim> recorded = disputes.RecordAcquisitionDisputes(
                titles, "parcel-broken", "Buyer", null, 20, new List<string>());
            Assert.AreEqual(1, recorded.Count);
            Assert.AreEqual(KnowledgeCertainty.Verified, recorded[0].Certainty);
            Assert.AreEqual("Buyer", recorded[0].RaisedBy);
            Assert.AreEqual(DisputeClaimStatus.Open, recorded[0].Status);
        }

        [Test]
        public void Acquisition_CleanChain_RecordsNoChainClaim()
        {
            TitleAuthority titles = TitlesWithCleanChain("parcel-clean");
            var disputes = new PropertyDisputeService();
            List<DisputeClaim> recorded = disputes.RecordAcquisitionDisputes(
                titles, "parcel-clean", "Buyer", null, 20, new List<string>());
            Assert.AreEqual(0, recorded.Count);
            Assert.AreEqual(string.Empty, QuickDiligenceService.BuildDisputeClue("parcel-clean", disputes));
        }

        [Test]
        public void Acquisition_DisplacedOccupant_RecordsReportedContestedClaim()
        {
            TitleAuthority titles = TitlesWithCleanChain("parcel-occ");
            var disputes = new PropertyDisputeService();
            List<DisputeClaim> recorded = disputes.RecordAcquisitionDisputes(
                titles, "parcel-occ", "Buyer", "tenant-household-3", 20, new List<string>());
            Assert.AreEqual(1, recorded.Count);
            Assert.AreEqual(KnowledgeCertainty.Reported, recorded[0].Certainty);
            Assert.AreEqual(DisputeClaimStatus.Contested, recorded[0].Status);
            Assert.AreEqual("tenant-household-3", recorded[0].ClaimantId);
        }

        [Test]
        public void Diligence_FlagsOpenContestedClaims_WithoutLeakingClaimantIdentity()
        {
            TitleAuthority titles = TitlesWithBrokenChain("parcel-leak");
            var disputes = new PropertyDisputeService();
            disputes.RecordAcquisitionDisputes(titles, "parcel-leak", "BuyerNameSecret", "OccupantNameSecret", 20, new List<string>());
            string clue = QuickDiligenceService.BuildDisputeClue("parcel-leak", disputes);
            Assert.IsTrue(clue.Contains("dispute claim"), "diligence must flag the claims: " + clue);
            Assert.IsFalse(clue.Contains("BuyerNameSecret"), "diligence must not leak the claimant (new owner)");
            Assert.IsFalse(clue.Contains("OccupantNameSecret"), "diligence must not leak the displaced occupant");
            Assert.IsTrue(QuickDiligenceService.HasDisputeClue("parcel-leak", disputes));
        }

        [Test]
        public void Recording_MirrorsIntoSinglePropertyInterestStore()
        {
            var store = new PropertyInterestStore();
            var disputes = new PropertyDisputeService(store);
            DisputeClaim claim = disputes.RecordClaim("parcel-mirror", "Claimant", "Claimant",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Open, ClaimBasis.HonestBoundaryError,
                5, "boundary dispute", new List<string>());
            Assert.IsNotNull(claim);
            IReadOnlyList<PropertyInterest> interests = store.ActiveForParcel("parcel-mirror");
            Assert.AreEqual(1, interests.Count);
            Assert.AreEqual(InterestKind.DisputeClaim, interests[0].Kind);
            Assert.AreEqual("Claimant", interests[0].HolderId);
        }

        [Test]
        public void Recording_AnonymousClaimRefused_AndSaveRoundTrips()
        {
            var disputes = new PropertyDisputeService();
            Assert.IsNull(disputes.RecordClaim("parcel-x", "", "raiser",
                KnowledgeCertainty.Reported, DisputeClaimStatus.Open, ClaimBasis.DisputedRights,
                5, "anon", new List<string>()));
            disputes.RecordClaim("parcel-x", "Named", "Named",
                KnowledgeCertainty.Verified, DisputeClaimStatus.Open, ClaimBasis.DefectiveDocumentation,
                5, "chain break", new List<string>());
            PropertyDisputeService.PropertyDisputeSaveDto dto = disputes.CaptureSaveDto();
            var restored = new PropertyDisputeService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Claims.Count);
            Assert.AreEqual(KnowledgeCertainty.Verified, restored.Claims[0].Certainty);
        }
    }
}
