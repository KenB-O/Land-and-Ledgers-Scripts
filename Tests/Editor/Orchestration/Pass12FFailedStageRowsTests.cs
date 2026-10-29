using System.Collections.Generic;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// Pass XII Worker F (D15): a Failed acquisition deal must not mark
    /// stages it never reached as "Failed". Stages before the failure
    /// stage are Complete, the failure stage itself is Failed, and
    /// later stages are Upcoming (unreached).
    /// </summary>
    [TestFixture]
    public sealed class Pass12FFailedStageRowsTests
    {
        private static AcquisitionDealReadModel.Model BuildFailed(
            AcquisitionDealReadModel.DealStageKey failedFromStage)
        {
            return AcquisitionDealReadModel.Build(
                "L1", "Doctor's Business", "Business",
                AcquisitionDealReadModel.DealStageKey.Failed,
                failedFromStage,
                false, false, false,
                AcquisitionDealReadModel.LeadKindKey.PublicListing,
                string.Empty, string.Empty,
                new List<string>(), string.Empty);
        }

        private static string RowState(AcquisitionDealReadModel.Model model,
            AcquisitionDealReadModel.DealStageKey stage)
        {
            foreach (AcquisitionDealReadModel.StageRow row in model.Stages)
            {
                if (row.Stage == stage) return row.State;
            }
            return "<missing>";
        }

        [Test]
        public void FailedAtDiligence_MarksReachedComplete_FailedStageFailed_UnreachedUpcoming()
        {
            AcquisitionDealReadModel.Model model = BuildFailed(
                AcquisitionDealReadModel.DealStageKey.DiligenceComplete);
            Assert.AreEqual("Complete", RowState(model, AcquisitionDealReadModel.DealStageKey.Inquiry));
            Assert.AreEqual("Complete", RowState(model, AcquisitionDealReadModel.DealStageKey.EarnestCommitted));
            Assert.AreEqual("Failed", RowState(model, AcquisitionDealReadModel.DealStageKey.DiligenceComplete));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.TentativeAgreement));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.Closed));
        }

        [Test]
        public void FailedWithUnknownStage_FailsAtInquiryOnly()
        {
            AcquisitionDealReadModel.Model model = BuildFailed(
                AcquisitionDealReadModel.DealStageKey.None);
            Assert.AreEqual("Failed", RowState(model, AcquisitionDealReadModel.DealStageKey.Inquiry));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.EarnestCommitted));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.DiligenceComplete));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.TentativeAgreement));
            Assert.AreEqual("Upcoming", RowState(model, AcquisitionDealReadModel.DealStageKey.Closed));
        }
    }
}
