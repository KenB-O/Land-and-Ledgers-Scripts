using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    public sealed class AcquisitionQuickDiligencePersistenceTests
    {
        [Test]
        public void DealSaveDtoPreservesQuickDiligenceLedgerAndMask()
        {
            AcquisitionDealState deal = new()
            {
                stage = AcquisitionDealStage.Inquiry,
                kind = AcquisitionListingKind.Business,
                listingId = "business_012",
                revealedDiligenceMask = (1 << (int)AcquisitionDiligenceClueKind.VisibleCondition)
                    | (1 << (int)AcquisitionDiligenceClueKind.RoughValue),
                quickDiligenceLedger = "Day 4: Visible condition read.\nDay 5: Rough value read.",
                lastQuickDiligenceDayIndex = 5
            };

            AcquisitionDealSaveDto dto = deal.CaptureSaveDto();
            AcquisitionDealState restored = AcquisitionDealState.FromSaveDto(dto);

            Assert.AreEqual(deal.revealedDiligenceMask, restored.revealedDiligenceMask);
            Assert.AreEqual(deal.quickDiligenceLedger, restored.quickDiligenceLedger);
            Assert.AreEqual(deal.lastQuickDiligenceDayIndex, restored.lastQuickDiligenceDayIndex);
        }



        [Test]
        public void DealFromSaveDtoMissingQuickDiligenceFieldsPreservesRicherContinuityState()
        {
            AcquisitionDealState deal = new()
            {
                stage = AcquisitionDealStage.TentativeAgreement,
                kind = AcquisitionListingKind.Business,
                listingId = "business_continuity",
                seriousness = AcquisitionBuyerSeriousness.Ready,
                integrationStance = AcquisitionIntegrationStance.Stabilize,
                scoutingSummary = "Books look uneven but trade remains recoverable.",
                proofStatus = AcquisitionProofStatus.Presented,
                proofOfFundsSummary = "Bank letter presented.",
                diligenceLayerSummary = "Books, condition, and staffing reviewed.",
                closingRiskSummary = "Closing risk is moderate because supplier trust is still thin.",
                quickDiligenceLedger = "Day 3: Walkthrough noted deferred repairs.",
                lastQuickDiligenceDayIndex = 3,
                revealedDiligenceMask = 1 << (int)AcquisitionDiligenceClueKind.VisibleCondition
            };

            AcquisitionDealSaveDto dto = deal.CaptureSaveDto();
            dto.revealedDiligenceMask = 0;
            dto.quickDiligenceLedger = null;
            dto.lastQuickDiligenceDayIndex = 0;

            AcquisitionDealState restored = AcquisitionDealState.FromSaveDto(dto);

            Assert.AreEqual(AcquisitionDealStage.TentativeAgreement, restored.stage);
            Assert.AreEqual(AcquisitionBuyerSeriousness.Ready, restored.seriousness);
            Assert.AreEqual(AcquisitionIntegrationStance.Stabilize, restored.integrationStance);
            Assert.AreEqual(deal.scoutingSummary, restored.scoutingSummary);
            Assert.AreEqual(deal.proofStatus, restored.proofStatus);
            Assert.AreEqual(deal.proofOfFundsSummary, restored.proofOfFundsSummary);
            Assert.AreEqual(deal.diligenceLayerSummary, restored.diligenceLayerSummary);
            Assert.AreEqual(deal.closingRiskSummary, restored.closingRiskSummary);
            Assert.AreEqual(0, restored.revealedDiligenceMask);
            Assert.IsTrue(string.IsNullOrEmpty(restored.quickDiligenceLedger));
            Assert.LessOrEqual(restored.lastQuickDiligenceDayIndex, 0);
        }

        [Test]
        public void DealFromSaveDtoMissingQuickDiligenceFieldsPreservesNamedFailureContext()
        {
            AcquisitionDealState deal = new()
            {
                stage = AcquisitionDealStage.Failed,
                kind = AcquisitionListingKind.Business,
                listingId = "business_failed",
                seriousness = AcquisitionBuyerSeriousness.Ready,
                closingFailureCause = AcquisitionClosingFailureCause.LenderRetreat,
                closingFailureSummary = "The lender stepped back after a late title concern.",
                closingRiskSummary = "Closing risk was already high because financing was thin.",
                quickDiligenceLedger = "Day 2: Seller disclosed title irregularity.",
                lastQuickDiligenceDayIndex = 2,
                revealedDiligenceMask = 1 << (int)AcquisitionDiligenceClueKind.Reliability
            };

            AcquisitionDealSaveDto dto = deal.CaptureSaveDto();
            dto.revealedDiligenceMask = 0;
            dto.quickDiligenceLedger = null;
            dto.lastQuickDiligenceDayIndex = 0;

            AcquisitionDealState restored = AcquisitionDealState.FromSaveDto(dto);

            Assert.AreEqual(AcquisitionDealStage.Failed, restored.stage);
            Assert.AreEqual(AcquisitionClosingFailureCause.LenderRetreat, restored.closingFailureCause);
            Assert.AreEqual(deal.closingFailureSummary, restored.closingFailureSummary);
            Assert.AreEqual(deal.closingRiskSummary, restored.closingRiskSummary);
            Assert.AreEqual(AcquisitionBuyerSeriousness.Ready, restored.seriousness);
            Assert.AreEqual(0, restored.revealedDiligenceMask);
            Assert.IsTrue(string.IsNullOrEmpty(restored.quickDiligenceLedger));
            Assert.LessOrEqual(restored.lastQuickDiligenceDayIndex, 0);
        }

        [Test]
        public void DealFromSaveDtoDefaultsMissingQuickDiligenceStateSafely()
        {
            AcquisitionDealSaveDto dto = new AcquisitionDealState
            {
                stage = AcquisitionDealStage.Inquiry,
                kind = AcquisitionListingKind.Business,
                listingId = "business_legacy"
            }.CaptureSaveDto();

            dto.revealedDiligenceMask = 0;
            dto.quickDiligenceLedger = null;
            dto.lastQuickDiligenceDayIndex = 0;

            AcquisitionDealState restored = AcquisitionDealState.FromSaveDto(dto);

            Assert.AreEqual(0, restored.revealedDiligenceMask);
            Assert.IsTrue(string.IsNullOrEmpty(restored.quickDiligenceLedger));
            Assert.LessOrEqual(restored.lastQuickDiligenceDayIndex, 0);
        }
    }
}
