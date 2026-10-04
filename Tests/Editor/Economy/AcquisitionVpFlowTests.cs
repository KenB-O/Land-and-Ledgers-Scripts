using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class AcquisitionVpFlowTests
    {
        [Test]
        public void LandAcquisitionRequiresInquiryEarnestDiligenceAgreementBeforeClose()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.Greater(context.AcquisitionMarket.LandListings.Count, 0);
            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];
            int plotId = listing.plotId;
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;
            int initialPlotCount = context.TownWorld.Plots.Count;

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string inquiryMessage), inquiryMessage);
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            Assert.AreEqual(AcquisitionDealStage.Inquiry, deal.stage);
            Assert.AreEqual(ownerCashBefore, context.PlayerPortfolio.OwnerCashCents);
            Assert.IsFalse(context.TownWorld.Plots[plotId].playerOwned);

            Assert.IsTrue(context.AdvanceListingToStage(AcquisitionMarketSection.Land, AcquisitionDealStage.EarnestCommitted, out string earnestMessage), earnestMessage);
            Assert.AreEqual(AcquisitionDealStage.EarnestCommitted, deal.stage);
            Assert.Less(context.PlayerPortfolio.OwnerCashCents, ownerCashBefore);
            Assert.IsFalse(context.TownWorld.Plots[plotId].playerOwned);

            Assert.IsTrue(context.AdvanceListingToStage(AcquisitionMarketSection.Land, AcquisitionDealStage.DiligenceComplete, out string diligenceMessage), diligenceMessage);
            Assert.AreEqual(AcquisitionDealStage.DiligenceComplete, deal.stage);
            Assert.Greater(deal.estimatedValueCents, 0);
            Assert.IsFalse(context.TownWorld.Plots[plotId].playerOwned);

            Assert.IsTrue(context.AdvanceListingToStage(AcquisitionMarketSection.Land, AcquisitionDealStage.TentativeAgreement, out string agreementMessage), agreementMessage);
            Assert.AreEqual(AcquisitionDealStage.TentativeAgreement, deal.stage);
            Assert.Greater(deal.tentativePriceCents, 0);
            Assert.IsFalse(context.TownWorld.Plots[plotId].playerOwned);

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string closeMessage), closeMessage);
            Assert.IsTrue(context.TownWorld.Plots[plotId].playerOwned);
            Assert.IsNull(context.GetDeal(listing.listingId));
            Assert.GreaterOrEqual(context.AcquisitionMarket.OwnedLandCount, 1);
            Assert.That(context.TownWorld.Plots.Count - initialPlotCount, Is.InRange(1, 2));
            Assert.IsTrue(context.AcquisitionMarket.LandListings.Any(candidate => candidate.plotId >= initialPlotCount));
            StringAssert.Contains("closed", closeMessage);
        }

        [Test]
        public void OpportunityWindowStatePersistsPlayerFirstAndAiEligibilityData()
        {
            AcquisitionOpportunityWindowState state = new()
            {
                listingId = "land_12",
                kind = AcquisitionListingKind.Land,
                firstSeenDayIndex = 10,
                playerFirstUntilDayIndex = 24,
                pressure01 = 0.72f,
                sourceReason = "Housing pressure surfaced a parcel",
                aiEligible = true
            };

            AcquisitionOpportunityWindowState restored = AcquisitionOpportunityWindowState.FromSaveDto(state.CaptureSaveDto());

            Assert.NotNull(restored);
            Assert.AreEqual("land_12", restored.listingId);
            Assert.AreEqual(AcquisitionListingKind.Land, restored.kind);
            Assert.AreEqual(10, restored.firstSeenDayIndex);
            Assert.AreEqual(24, restored.playerFirstUntilDayIndex);
            Assert.AreEqual(0.72f, restored.pressure01);
            Assert.IsTrue(restored.aiEligible);
            StringAssert.Contains("Housing pressure", restored.sourceReason);
        }

        [Test]
        public void BusinessCloseTransfersSharedRuntimeOwnershipToPlayer()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.Greater(context.AcquisitionMarket.BusinessListings.Count, 0);
            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            int buildingId = listing.buildingId;

            Assert.IsTrue(context.AdvanceBusinessListingToClose(out string closeMessage), closeMessage);

            PlacedBuilding building = context.TownWorld.Buildings[buildingId];
            BusinessInstanceState business = context.SharedBusinessRuntime.FindByBuildingId(buildingId);
            Assert.IsTrue(building.playerOwned);
            Assert.NotNull(business);
            Assert.NotNull(business.Owner);
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
            Assert.GreaterOrEqual(context.AcquisitionMarket.OwnedBusinessCount, 1);
            StringAssert.Contains("closed", closeMessage);
        }

        [Test]
        public void BusinessCloseKeepsOperatingCashInsideAcquiredBusiness()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.Greater(context.AcquisitionMarket.BusinessListings.Count, 0);
            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            BusinessInstanceState business = context.SharedBusinessRuntime.FindByBuildingId(listing.buildingId);
            Assert.NotNull(business);
            context.SetBusinessCash(business, 100000);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string tentativeMessage), tentativeMessage);
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            int finalPrice = deal.tentativePriceCents;

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string closeMessage), closeMessage);

            Assert.AreEqual(ownerCashBefore - finalPrice, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(100000, business.RuntimeState.CurrentCashCents);
            Assert.NotNull(business.Owner);
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
            StringAssert.Contains("Operating cash remains with the business as working capital.", closeMessage);
        }

        [Test]
        public void DirectBusinessPurchaseKeepsOperatingCashInsideAcquiredBusiness()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.Greater(context.AcquisitionMarket.BusinessListings.Count, 0);
            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            BusinessInstanceState business = context.SharedBusinessRuntime.FindByBuildingId(listing.buildingId);
            Assert.NotNull(business);
            context.SetBusinessCash(business, 100000);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            bool purchased = InvokeDirectPurchase(context.AcquisitionMarket, AcquisitionMarketSection.Businesses, out string message);

            Assert.IsTrue(purchased, message);
            Assert.AreEqual(ownerCashBefore - listing.askingPriceCents, context.PlayerPortfolio.OwnerCashCents);
            Assert.AreEqual(100000, business.RuntimeState.CurrentCashCents);
            Assert.NotNull(business.Owner);
            Assert.AreEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
            StringAssert.Contains("Operating cash remains with the business as working capital.", message);
        }

        [Test]
        public void AcquisitionDefaultRecoversFinancedBusinessBeforeUnrelatedPlot()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionListing unrelatedLand = context.AcquisitionMarket.LandListings[0];
            int unrelatedPlotId = unrelatedLand.plotId;
            Assert.IsTrue(context.AdvanceListingToClose(AcquisitionMarketSection.Land, out string landMessage), landMessage);

            AcquisitionListing financedBusiness = context.AcquisitionMarket.BusinessListings[0];
            int financedBuildingId = financedBusiness.buildingId;
            Assert.IsTrue(context.AdvanceListingToClose(AcquisitionMarketSection.Businesses, out string businessMessage), businessMessage);
            Assert.IsTrue(context.TownWorld.Plots[unrelatedPlotId].playerOwned);
            Assert.IsTrue(context.TownWorld.Buildings[financedBuildingId].playerOwned);

            context.DrainOwnerCash();
            context.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                activeLoan = CreateDefaultableLoan(
                    1000,
                    100,
                    LoanPurpose.Acquisition,
                    new List<string> { $"building_{financedBuildingId:000}" })
            });

            context.DefaultDebtThroughMissedPayments();

            Assert.AreEqual(LoanStatus.Recovered, context.DebtManager.ActiveLoan.status);
            Assert.IsFalse(context.TownWorld.Buildings[financedBuildingId].playerOwned);
            Assert.IsTrue(context.TownWorld.Plots[unrelatedPlotId].playerOwned);
            StringAssert.Contains("Pledged collateral", context.DebtManager.LastRecoverySummary);
        }

        [Test]
        public void WorkingCapitalDefaultFallbackRecoveryDoesNotClearLargeUnsecuredDebt()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToClose(AcquisitionMarketSection.Land, out string landMessage), landMessage);
            Assert.IsTrue(context.AdvanceListingToClose(AcquisitionMarketSection.Businesses, out string businessMessage), businessMessage);
            context.DrainOwnerCash();
            context.DebtManager.LoadFromSaveDto(new PlayerDebtSaveDto
            {
                requestedAmountCents = 10000,
                activeLoan = CreateDefaultableLoan(
                    100000000,
                    1000,
                    LoanPurpose.WorkingCapital,
                    null)
            });

            context.DefaultDebtThroughMissedPayments();

            Assert.AreEqual(LoanStatus.Defaulted, context.DebtManager.ActiveLoan.status);
            Assert.IsTrue(context.DebtManager.HasActiveLoan);
            Assert.IsFalse(context.DebtManager.CanSubmitApplication);
            StringAssert.Contains("Remaining unresolved principal", context.DebtManager.LastRecoverySummary);
        }

        [Test]
        public void AcquisitionSaveRestoresActiveDealAndDefaultsOldSaves()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out _));
            Assert.IsTrue(context.AdvanceListingToStage(AcquisitionMarketSection.Land, AcquisitionDealStage.DiligenceComplete, out string diligenceMessage), diligenceMessage);

            AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            Assert.AreEqual(1, dto.activeDeals.Count);
            Assert.AreEqual(AcquisitionDealStage.DiligenceComplete, dto.activeDeals[0].stage);

            context.AcquisitionMarket.LoadFromSaveDto(dto);
            AcquisitionDealState restored = context.GetDeal(listing.listingId);
            Assert.NotNull(restored);
            Assert.AreEqual(AcquisitionDealStage.DiligenceComplete, restored.stage);
            Assert.Greater(restored.earnestMoneyCents, 0);
            Assert.GreaterOrEqual(restored.lastProgressDayIndex, 0);
            Assert.AreEqual(0, restored.stalledReviewCount);

            context.AcquisitionMarket.LoadFromSaveDto(new AcquisitionSaveDto
            {
                marketSeed = 1902,
                maxLandListings = 4,
                maxBusinessListings = 2
            });
            Assert.AreEqual(0, context.AcquisitionMarket.ActiveDeals.Count);
        }


        [Test]
        public void WatchlistRoundTripsThroughSaveAndAppearsInAcquisitionText()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.Greater(context.AcquisitionMarket.LandListings.Count, 0);

            Assert.IsTrue(context.AcquisitionMarket.ToggleSelectedWatch(AcquisitionMarketSection.Land, out string watchMessage), watchMessage);
            StringAssert.Contains("marked for later", watchMessage);

            AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            Assert.AreEqual(1, dto.watchedListingIds.Count);

            context.AcquisitionMarket.LoadFromSaveDto(dto);
            Assert.IsTrue(context.AcquisitionMarket.IsSelectedListingWatched(AcquisitionMarketSection.Land));

            string text = context.AcquisitionMarket.BuildAcquisitionText(AcquisitionMarketSection.Land);
            StringAssert.Contains("Watchlist:", text);
            StringAssert.Contains("Marked for later", text);
        }

        [Test]
        public void AcquisitionTextIncludesScoutingAndBuyerRead()
        {
            using TestWorld context = TestWorld.Create(10000000);
            string landText = context.AcquisitionMarket.BuildAcquisitionText(AcquisitionMarketSection.Land);
            StringAssert.Contains("Lead:", landText);
            StringAssert.Contains("Proof:", landText);
            StringAssert.Contains("Scouting:", landText);
            StringAssert.Contains("Buyer Read:", landText);

            string businessText = context.AcquisitionMarket.BuildAcquisitionText(AcquisitionMarketSection.Businesses);
            StringAssert.Contains("Lead:", businessText);
            StringAssert.Contains("Proof:", businessText);
            StringAssert.Contains("Scouting:", businessText);
            StringAssert.Contains("Buyer Read:", businessText);
        }

        [Test]
        public void ConversationStateBuildsForSelectedLandAndBusinessListings()
        {
            using TestWorld context = TestWorld.Create(10000000);

            Assert.IsTrue(context.AcquisitionMarket.TryBuildSelectedConversationState(AcquisitionMarketSection.Land, out AcquisitionConversationState landState));
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.ListingId));
            Assert.AreEqual(AcquisitionListingKind.Land, landState.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.FormalActionLabel));
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.ProcessSummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.DialoguePrompt));
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.LeadQualitySummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(landState.ProofOfFundsSummary));
            Assert.That(landState.Options.Count, Is.InRange(1, 4));
            Assert.AreEqual(5, landState.ProcessSteps.Count);
            Assert.IsTrue(landState.Options.TrueForAll(option =>
                !string.IsNullOrWhiteSpace(option.IntentText)
                && !string.IsNullOrWhiteSpace(option.SellerReplyText)
                && !string.IsNullOrWhiteSpace(option.OutcomeTag)));

            Assert.IsTrue(context.AcquisitionMarket.TryBuildSelectedConversationState(AcquisitionMarketSection.Businesses, out AcquisitionConversationState businessState));
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.ListingId));
            Assert.AreEqual(AcquisitionListingKind.Business, businessState.Kind);
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.FormalActionLabel));
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.ProcessSummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.DialoguePrompt));
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.DiligenceSummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(businessState.ClosingRiskSummary));
            Assert.That(businessState.Options.Count, Is.InRange(1, 4));
            Assert.AreEqual(5, businessState.ProcessSteps.Count);
            Assert.IsTrue(businessState.Options.TrueForAll(option =>
                !string.IsNullOrWhiteSpace(option.IntentText)
                && !string.IsNullOrWhiteSpace(option.SellerReplyText)
                && !string.IsNullOrWhiteSpace(option.OutcomeTag)));
        }

        [Test]
        public void DealWorkflowPersistsLeadQualityProofAndClosingRiskRead()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            Assert.AreNotEqual(AcquisitionLeadCategory.Unset, deal.leadCategory);
            Assert.IsFalse(string.IsNullOrWhiteSpace(deal.leadQualitySummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(deal.proofOfFundsSummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(deal.diligenceLayerSummary));
            Assert.IsFalse(string.IsNullOrWhiteSpace(deal.closingRiskSummary));
            Assert.AreNotEqual(AcquisitionProofStatus.NotRequested, deal.proofStatus);
            Assert.AreEqual(deal.requiredDiligenceMask, deal.completedDiligenceMask);

            AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            context.AcquisitionMarket.LoadFromSaveDto(dto);

            AcquisitionDealState restored = context.GetDeal(listing.listingId);
            Assert.NotNull(restored);
            Assert.AreEqual(deal.leadCategory, restored.leadCategory);
            Assert.AreEqual(deal.leadQualitySummary, restored.leadQualitySummary);
            Assert.AreEqual(deal.proofOfFundsSummary, restored.proofOfFundsSummary);
            Assert.AreEqual(deal.diligenceLayerSummary, restored.diligenceLayerSummary);
            Assert.AreEqual(deal.closingRiskSummary, restored.closingRiskSummary);
            Assert.AreEqual(deal.proofStatus, restored.proofStatus);
            Assert.AreEqual(deal.requiredDiligenceMask, restored.requiredDiligenceMask);
            Assert.AreEqual(deal.completedDiligenceMask, restored.completedDiligenceMask);
        }

        [Test]
        public void BusinessInquiryRequiresProofWhenCashIsThin()
        {
            using TestWorld context = TestWorld.Create(10000000);

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string inquiryMessage), inquiryMessage);
            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            Assert.IsTrue(deal.proofRequired);

            context.SetOwnerCash(0);

            Assert.IsFalse(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string proofMessage));

            Assert.AreEqual(AcquisitionDealStage.Inquiry, deal.stage);
            Assert.AreNotEqual(AcquisitionProofStatus.Verified, deal.proofStatus);
            StringAssert.Contains("proof", proofMessage.ToLowerInvariant());
        }

        [Test]
        public void BusinessDiligenceRequiresMultipleFormalLayersBeforeTerms()
        {
            using TestWorld context = TestWorld.Create(10000000);

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string inquiryMessage), inquiryMessage);
            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);

            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string proofMessage), proofMessage);
            Assert.AreEqual(AcquisitionDealStage.Inquiry, deal.stage);
            Assert.AreEqual(AcquisitionProofStatus.Verified, deal.proofStatus);

            Assert.IsTrue(context.AdvanceListingToStage(AcquisitionMarketSection.Businesses, AcquisitionDealStage.EarnestCommitted, out string earnestMessage), earnestMessage);
            Assert.AreEqual(AcquisitionDealStage.EarnestCommitted, deal.stage);

            int diligenceActions = 0;
            while (deal.stage == AcquisitionDealStage.EarnestCommitted && diligenceActions < 12)
            {
                Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string diligenceMessage), diligenceMessage);
                diligenceActions++;
            }

            Assert.AreEqual(AcquisitionDealStage.DiligenceComplete, deal.stage);
            Assert.Greater(diligenceActions, 1);
            Assert.AreEqual(deal.requiredDiligenceMask, deal.completedDiligenceMask);
            StringAssert.Contains("formal review complete", deal.diligenceLayerSummary.ToLowerInvariant());
        }

        [Test]
        public void ClosingFailureStoresNamedCause()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            deal.closingRisk01 = 1f;
            deal.financingNeedCents = Mathf.Max(250000, deal.financingNeedCents);
            deal.proofRequired = true;
            deal.proofStatus = AcquisitionProofStatus.Presented;

            Assert.IsFalse(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string closeMessage));

            Assert.AreEqual(AcquisitionDealStage.Failed, deal.stage);
            Assert.AreEqual(AcquisitionClosingFailureCause.LenderRetreat, deal.closingFailureCause);
            Assert.IsFalse(string.IsNullOrWhiteSpace(deal.closingFailureSummary));
            StringAssert.Contains("lender", closeMessage.ToLowerInvariant());
        }

        [Test]
        public void ConversationAdvanceUsesExistingDealPipeline()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];

            Assert.IsTrue(context.AcquisitionMarket.TryAdvanceConversationDeal(listing.listingId, out string message), message);

            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            Assert.AreEqual(AcquisitionDealStage.Inquiry, deal.stage);
            Assert.IsTrue(context.AcquisitionMarket.TryBuildConversationState(listing.listingId, out AcquisitionConversationState state));
            Assert.AreEqual("Commit Earnest", state.FormalActionLabel);
        }

        [Test]
        public void AcquisitionReadinessForAffordableTentativeLandCanCloseNow()
        {
            using TestWorld context = TestWorld.Create(10000000);

            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Land, out string message), message);
            ExpansionReadinessService readinessService = new(
                context.AcquisitionMarket,
                context.PlayerPortfolio,
                context.DebtManager,
                context.StoreRuntime,
                context.SharedBusinessRuntime);

            ExpansionReadinessResult readiness = readinessService.BuildForSelectedAcquisition(AcquisitionMarketSection.Land);

            Assert.AreEqual(ExpansionReadinessKind.AcquisitionLand, readiness.Kind);
            Assert.Greater(readiness.CashRequiredNowCents, 0);
            Assert.AreEqual(0, readiness.FinancingGapCents);
            StringAssert.Contains("Can close now", readiness.RecommendedAction);
            Assert.IsTrue(readiness.Warnings.Count > 0);
        }

        [Test]
        public void FinancedAcquisitionCloseShowsDebtPaymentAndCollateralWarning()
        {
            using TestWorld context = TestWorld.Create(10000000);

            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string tentativeMessage), tentativeMessage);
            context.DrainOwnerCash();

            bool closed = context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string closeMessage);

            Assert.IsTrue(closed, closeMessage);
            StringAssert.Contains("acquisition loan approved", closeMessage);
            StringAssert.Contains("Next payment", closeMessage);
            StringAssert.Contains("Collateral/default risk", closeMessage);
        }

        [Test]
        public void DiligencePersistsSeriousnessAndIntegrationStanceThroughSave()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AcquisitionMarket.ToggleSelectedWatch(AcquisitionMarketSection.Businesses, out _));
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            Assert.AreNotEqual(AcquisitionBuyerSeriousness.Watching, deal.seriousness);
            Assert.AreNotEqual(AcquisitionIntegrationStance.None, deal.integrationStance);

            AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            context.AcquisitionMarket.LoadFromSaveDto(dto);
            AcquisitionDealState restored = context.GetDeal(listing.listingId);
            Assert.NotNull(restored);
            Assert.AreEqual(deal.seriousness, restored.seriousness);
            Assert.AreEqual(deal.integrationStance, restored.integrationStance);
            Assert.IsFalse(string.IsNullOrWhiteSpace(restored.scoutingSummary));
        }


        [Test]
        public void TentativeDealPersistsThroughSaveLoadAfterMarketRebuild()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);

            AcquisitionBuyerSeriousness seriousnessBefore = deal.seriousness;
            AcquisitionIntegrationStance stanceBefore = deal.integrationStance;
            string scoutingBefore = deal.scoutingSummary;
            string riskBefore = deal.closingRiskSummary;

            AcquisitionDealState restored = SaveReloadAfterMarketRebuild(context, listing.listingId);

            Assert.NotNull(restored);
            Assert.AreEqual(AcquisitionDealStage.TentativeAgreement, restored.stage);
            Assert.AreEqual(seriousnessBefore, restored.seriousness);
            Assert.AreEqual(stanceBefore, restored.integrationStance);
            Assert.AreEqual(scoutingBefore, restored.scoutingSummary);
            Assert.AreEqual(riskBefore, restored.closingRiskSummary);

            string ledger = context.AcquisitionMarket.BuildAcquisitionProcessLedgerSummary();
            StringAssert.Contains("Tentative 1", ledger);
        }

        [Test]
        public void FailedClosingPersistsNamedCauseThroughSaveLoadAfterMarketRebuild()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.BusinessListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);
            deal.closingRisk01 = 1f;
            deal.financingNeedCents = Mathf.Max(250000, deal.financingNeedCents);
            deal.proofRequired = true;
            deal.proofStatus = AcquisitionProofStatus.Presented;

            Assert.IsFalse(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string closeMessage));
            StringAssert.Contains("lender", closeMessage.ToLowerInvariant());

            AcquisitionDealState restored = SaveReloadAfterMarketRebuild(context, listing.listingId);

            Assert.NotNull(restored);
            Assert.AreEqual(AcquisitionDealStage.Failed, restored.stage);
            Assert.AreEqual(AcquisitionClosingFailureCause.LenderRetreat, restored.closingFailureCause);
            Assert.IsFalse(string.IsNullOrWhiteSpace(restored.closingFailureSummary));
            StringAssert.Contains("lender", restored.closingFailureSummary.ToLowerInvariant());

            string ledger = context.AcquisitionMarket.BuildAcquisitionProcessLedgerSummary();
            StringAssert.Contains("Failed 1", ledger);
        }

        [Test]
        public void InquiryDealAndWatchlistPersistTogetherThroughSaveLoadAfterMarketRebuild()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];

            Assert.IsTrue(context.AcquisitionMarket.ToggleSelectedWatch(AcquisitionMarketSection.Land, out string watchMessage), watchMessage);
            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string inquiryMessage), inquiryMessage);

            AcquisitionDealState restored = SaveReloadAfterMarketRebuild(context, listing.listingId);

            Assert.NotNull(restored);
            Assert.AreEqual(AcquisitionDealStage.Inquiry, restored.stage);

            AcquisitionSaveDto reloadedDto = context.AcquisitionMarket.CaptureSaveDto();
            CollectionAssert.Contains(reloadedDto.watchedListingIds, listing.listingId);

            string ledger = context.AcquisitionMarket.BuildAcquisitionProcessLedgerSummary();
            StringAssert.Contains("Inquiry 1", ledger);
        }

        [Test]
        public void WeeklyReview_DowngradesStalledInquirySeriousnessBeforeFailure()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string inquiryMessage), inquiryMessage);

            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);

            deal.seriousness = AcquisitionBuyerSeriousness.Active;
            deal.lastProgressDayIndex = 0;
            context.DrainOwnerCash();

            string summary = context.AcquisitionMarket.DebugRunWeeklyAcquisitionReviewAtDay(8);

            Assert.NotNull(context.GetDeal(listing.listingId));
            Assert.AreEqual(AcquisitionBuyerSeriousness.Exploring, deal.seriousness);
            StringAssert.Contains("cooling", summary.ToLowerInvariant());
        }

        [Test]
        public void WeeklyReview_PromotesUrgentWellFundedTentativeDealToReady()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Land, out string message), message);

            AcquisitionListing listing = context.AcquisitionMarket.LandListings[0];
            AcquisitionDealState deal = context.GetDeal(listing.listingId);
            Assert.NotNull(deal);

            deal.seriousness = AcquisitionBuyerSeriousness.Active;
            int reviewDay = Mathf.Max(0, deal.closingDeadlineDayIndex - 1);
            string summary = context.AcquisitionMarket.DebugRunWeeklyAcquisitionReviewAtDay(reviewDay);

            Assert.AreEqual(AcquisitionBuyerSeriousness.Ready, deal.seriousness);
            StringAssert.Contains("ready", summary.ToLowerInvariant());
        }

        [Test]
        public void FinancedInquirySeriousnessDropsWhenOwnerStandingIsWeak()
        {
            using TestWorld strongContext = TestWorld.Create(60000);
            AcquisitionListing strongListing = strongContext.AcquisitionMarket.BusinessListings[0];
            strongContext.DebtManager.Reputation.dealTrust01 = 0.78f;
            strongContext.DebtManager.Reputation.localSocialTrust01 = 0.72f;
            strongContext.DebtManager.Reputation.operationalReliability01 = 0.74f;

            Assert.IsTrue(strongContext.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string strongMessage), strongMessage);
            AcquisitionDealState strongDeal = strongContext.GetDeal(strongListing.listingId);
            Assert.NotNull(strongDeal);

            using TestWorld weakContext = TestWorld.Create(60000);
            AcquisitionListing weakListing = weakContext.AcquisitionMarket.BusinessListings[0];
            weakContext.DebtManager.Reputation.dealTrust01 = 0.24f;
            weakContext.DebtManager.Reputation.localSocialTrust01 = 0.18f;
            weakContext.DebtManager.Reputation.operationalReliability01 = 0.16f;

            Assert.IsTrue(weakContext.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string weakMessage), weakMessage);
            AcquisitionDealState weakDeal = weakContext.GetDeal(weakListing.listingId);
            Assert.NotNull(weakDeal);

            Assert.AreEqual(AcquisitionBuyerSeriousness.Active, strongDeal.seriousness);
            Assert.AreEqual(AcquisitionBuyerSeriousness.Exploring, weakDeal.seriousness);
        }

        [Test]
        public void AcquisitionTextIncludesPipelineSeriousnessAndStance()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string message), message);

            string text = context.AcquisitionMarket.BuildAcquisitionText(AcquisitionMarketSection.Businesses);
            StringAssert.Contains("Pipeline:", text);
            StringAssert.Contains("Seriousness", text);
            StringAssert.Contains("Post-Close Stance:", text);
            StringAssert.Contains("Liquidity:", text);
        }

        [Test]
        public void SelectedProcessSummaryIncludesStageFundingAndSeriousness()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string inquiryMessage), inquiryMessage);

            string summary = context.AcquisitionMarket.BuildSelectedProcessSummary(AcquisitionMarketSection.Land);
            StringAssert.Contains("Land Acquisition", summary);
            StringAssert.Contains("Stage", summary);
            StringAssert.Contains("Seriousness", summary);
            StringAssert.Contains("Funding", summary);
        }

        [Test]
        public void SeriousnessCycleUpdatesSelectedDealLabel()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Businesses, out string inquiryMessage), inquiryMessage);

            string before = context.AcquisitionMarket.GetSelectedSeriousnessLabel(AcquisitionMarketSection.Businesses);
            Assert.IsTrue(context.AcquisitionMarket.CycleSelectedSeriousness(AcquisitionMarketSection.Businesses, out string cycleMessage), cycleMessage);
            string after = context.AcquisitionMarket.GetSelectedSeriousnessLabel(AcquisitionMarketSection.Businesses);

            Assert.AreNotEqual(before, after);
            StringAssert.Contains("seriousness set", cycleMessage.ToLowerInvariant());
        }


        // Save data should remain the source of truth for in-flight deal continuity even when
        // the live market list is rebuilt between capture and restore.
        private static AcquisitionDealState SaveReloadAfterMarketRebuild(TestWorld context, string listingId)
        {
            Assert.NotNull(context);
            Assert.IsFalse(string.IsNullOrWhiteSpace(listingId));

            AcquisitionSaveDto dto = context.AcquisitionMarket.CaptureSaveDto();
            context.AcquisitionMarket.RebuildMarket();
            context.AcquisitionMarket.LoadFromSaveDto(dto);
            return context.GetDeal(listingId);
        }

        private static LoanContract CreateDefaultableLoan(
            int remainingPrincipalCents,
            int totalDueCents,
            LoanPurpose purpose,
            List<string> collateralIds)
        {
            return new LoanContract
            {
                loanId = "default_exploit_test_loan",
                lenderId = "frontier_local_bank",
                purpose = purpose,
                collateralIds = collateralIds ?? new List<string>(),
                status = LoanStatus.Active,
                remainingPrincipalCents = remainingPrincipalCents,
                nextPaymentDueDate = CreateStartDate(),
                schedule = new LoanPaymentSchedule
                {
                    loanId = "default_exploit_test_loan",
                    totalPrincipalCents = remainingPrincipalCents,
                    totalPaymentCents = totalDueCents,
                    payments = new List<LoanPaymentDue>
                    {
                        new()
                        {
                            paymentNumber = 1,
                            dueDate = CreateStartDate(),
                            dueDayIndex = 0,
                            principalCents = Mathf.Max(0, totalDueCents - 100),
                            interestCents = 100,
                            totalDueCents = totalDueCents,
                            status = LoanPaymentStatus.Scheduled
                        }
                    }
                }
            };
        }

        private static SimulationDate CreateStartDate()
        {
            return new SimulationDate(0, 0, 1, 1, 1, 1, 1);
        }

        private static bool InvokeDirectPurchase(
            AcquisitionMarketManager market,
            AcquisitionMarketSection section,
            out string message)
        {
            MethodInfo method = typeof(AcquisitionMarketManager).GetMethod(
                "TryDirectPurchaseSelected",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            object[] arguments = { section, string.Empty };
            bool result = (bool)method.Invoke(market, arguments);
            message = arguments[1] as string ?? string.Empty;
            return result;
        }

        [Test]
        public void SelectedLeadReadinessIncludesRunwayPressure()
        {
            using TestWorld context = TestWorld.Create(10000000);
            string summary = context.AcquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land);
            StringAssert.Contains("Runway Read", summary);
        }

        private sealed class TestWorld : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            public TownWorldController TownWorld { get; }
            public PopulationManager PopulationManager { get; }
            public GeneralStoreRuntimeManager StoreRuntime { get; }
            public SharedBusinessRuntimeManager SharedBusinessRuntime { get; }
            public AcquisitionMarketManager AcquisitionMarket { get; }
            public PlayerPortfolioManager PlayerPortfolio { get; }
            public PlayerDebtManager DebtManager { get; }

            private TestWorld(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                TownWorld = root.AddComponent<TownWorldController>();
                PopulationManager = root.AddComponent<PopulationManager>();
                StoreRuntime = root.AddComponent<GeneralStoreRuntimeManager>();
                SharedBusinessRuntime = root.AddComponent<SharedBusinessRuntimeManager>();
                AcquisitionMarket = root.AddComponent<AcquisitionMarketManager>();
                PlayerPortfolio = root.AddComponent<PlayerPortfolioManager>();
                DebtManager = root.AddComponent<PlayerDebtManager>();
            }

            public static TestWorld Create(int ownerCashCents)
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                GeneralStoreBusinessDefinition storeDefinition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>("Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
                Assert.NotNull(sourceSettings);
                Assert.NotNull(storeDefinition);

                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                GameObject root = new("Acquisition VP Flow Test World");
                TestWorld context = new(root, settings);
                context.TownWorld.Configure(settings, null, null);
                context.TownWorld.GenerateTownShell();
                context.PopulationManager.Configure(context.TownWorld, null, false);
                context.PopulationManager.GeneratePopulationSnapshot();
                context.PlayerPortfolio.Configure(null, null);
                context.PlayerPortfolio.LoadFromSaveDto(new PlayerPortfolioSaveDto
                {
                    initialized = true,
                    ownerCashCents = ownerCashCents,
                    lastWeeklyDistributionCents = 50000
                });
                context.StoreRuntime.Configure(context.TownWorld, context.PopulationManager, null, storeDefinition, null, null, context.PlayerPortfolio);
                Assert.IsTrue(context.StoreRuntime.InitializeIfNeeded(), context.StoreRuntime.Status);
                context.SharedBusinessRuntime.Configure(context.TownWorld, null, context.PopulationManager, null, context.StoreRuntime, context.PlayerPortfolio);
                context.SharedBusinessRuntime.InitializeIfNeeded(context.StoreRuntime.CurrentBusiness);
                context.AcquisitionMarket.Configure(context.TownWorld, context.StoreRuntime, context.SharedBusinessRuntime, context.PopulationManager, null, null, context.PlayerPortfolio);
                context.DebtManager.Configure(null, context.StoreRuntime, context.AcquisitionMarket, context.SharedBusinessRuntime, context.PlayerPortfolio);
                context.AcquisitionMarket.RebuildMarket();
                return context;
            }

            public AcquisitionDealState GetDeal(string listingId)
            {
                return AcquisitionMarket.ActiveDeals.FirstOrDefault(deal => deal != null && deal.listingId == listingId);
            }

            public bool AdvanceBusinessListingToClose(out string message)
            {
                return AdvanceListingToClose(AcquisitionMarketSection.Businesses, out message);
            }

            public bool AdvanceListingToClose(AcquisitionMarketSection section, out string message)
            {
                message = string.Empty;
                AcquisitionListing selected = GetSelectedListing(section);
                if (selected == null)
                {
                    message = "No selected listing.";
                    return false;
                }

                for (int i = 0; i < 20; i++)
                {
                    if (!AcquisitionMarket.TryPurchaseSelected(section, out message))
                    {
                        return false;
                    }

                    if (GetDeal(selected.listingId) == null)
                    {
                        return true;
                    }
                }

                message = $"Listing {selected.listingId} did not close within the expected workflow steps.";
                return false;
            }

            public bool AdvanceListingToTentativeAgreement(AcquisitionMarketSection section, out string message)
            {
                return AdvanceListingToStage(section, AcquisitionDealStage.TentativeAgreement, out message);
            }

            public bool AdvanceListingToStage(AcquisitionMarketSection section, AcquisitionDealStage targetStage, out string message)
            {
                message = string.Empty;
                AcquisitionListing selected = GetSelectedListing(section);
                if (selected == null)
                {
                    message = "No selected listing.";
                    return false;
                }

                for (int i = 0; i < 20; i++)
                {
                    AcquisitionDealState deal = GetDeal(selected.listingId);
                    if (deal != null && deal.stage >= targetStage)
                    {
                        return true;
                    }

                    if (!AcquisitionMarket.TryPurchaseSelected(section, out message))
                    {
                        return false;
                    }

                    deal = GetDeal(selected.listingId);
                    if (deal != null && deal.stage >= targetStage)
                    {
                        return true;
                    }
                }

                message = $"Listing {selected.listingId} did not reach {targetStage} within the expected workflow steps.";
                return false;
            }

            private AcquisitionListing GetSelectedListing(AcquisitionMarketSection section)
            {
                return section == AcquisitionMarketSection.Businesses
                    ? (AcquisitionMarket.BusinessListings.Count > 0 ? AcquisitionMarket.BusinessListings[0] : null)
                    : (AcquisitionMarket.LandListings.Count > 0 ? AcquisitionMarket.LandListings[0] : null);
            }

            public void DrainOwnerCash()
            {
                if (PlayerPortfolio.OwnerCashCents > 0)
                {
                    Assert.IsTrue(PlayerPortfolio.TrySpendOwnerCash(PlayerPortfolio.OwnerCashCents, "test cash drain", out string message), message);
                }
            }

            public void SetOwnerCash(int cashCents)
            {
                int target = Mathf.Max(0, cashCents);
                int current = PlayerPortfolio.OwnerCashCents;
                if (current > target)
                {
                    Assert.IsTrue(PlayerPortfolio.TrySpendOwnerCash(current - target, "test owner cash reset", out string message), message);
                    return;
                }

                if (target > current)
                {
                    PlayerPortfolio.AddOwnerCash(target - current, "test owner cash seed");
                }
            }

            public void SetBusinessCash(BusinessInstanceState business, int cashCents)
            {
                Assert.NotNull(business);
                Assert.NotNull(business.RuntimeState);
                int target = Mathf.Max(0, cashCents);
                int current = business.RuntimeState.CurrentCashCents;
                if (current > target)
                {
                    business.RuntimeState.SpendCents(current - target);
                    return;
                }

                business.RuntimeState.AddCashCents(target - current);
            }

            public void DefaultDebtThroughMissedPayments()
            {
                DebtManager.ProcessDay(0);
                DebtManager.ProcessDay(7);
                DebtManager.ProcessDay(14);
                DebtManager.ProcessDay(21);
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }
        }

        [Test]
        public void ProcessLedgerSummary_ReportsActiveDealCounts()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AcquisitionMarket.TryPurchaseSelected(AcquisitionMarketSection.Land, out string inquiryMessage), inquiryMessage);
            Assert.IsTrue(context.AdvanceListingToTentativeAgreement(AcquisitionMarketSection.Businesses, out string tentativeMessage), tentativeMessage);

            string summary = context.AcquisitionMarket.BuildAcquisitionProcessLedgerSummary();

            StringAssert.Contains("Process ledger", summary);
            StringAssert.Contains("2 active", summary);
            StringAssert.Contains("Inquiry 1", summary);
            StringAssert.Contains("Tentative 1", summary);
        }

        [Test]
        public void DecisionClimateSummary_IncludesDecisionClimate()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionMarketManager market = context.AcquisitionMarket;
            string text = market.BuildAcquisitionDecisionClimateSummary();
            StringAssert.Contains("Decision climate", text);
        }

        [Test]
        public void SellingPlayerBusinessCreditsOwnerCashAndTransfersOwnershipAway()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceBusinessListingToClose(out string closeMessage), closeMessage);

            BusinessInstanceState business = context.SharedBusinessRuntime.Businesses
                .First(candidate => candidate != null
                    && candidate.BusinessType != BusinessType.GeneralStore
                    && candidate.Owner != null
                    && candidate.Owner.OwnerKind == BusinessOwnerKind.Player);
            int ownerCashBefore = context.PlayerPortfolio.OwnerCashCents;

            Assert.IsTrue(context.AcquisitionMarket.TrySellPlayerBusiness(business.AssignedBuildingId, out string saleMessage), saleMessage);

            Assert.Greater(context.PlayerPortfolio.OwnerCashCents, ownerCashBefore);
            Assert.AreNotEqual(BusinessOwnerKind.Player, business.Owner.OwnerKind);
            StringAssert.Contains("Sold", saleMessage);
        }

        [Test]
        public void DistressedBusinessSaleOfferRunsBelowHealthyBusiness()
        {
            using TestWorld context = TestWorld.Create(10000000);
            Assert.IsTrue(context.AdvanceBusinessListingToClose(out string closeMessage), closeMessage);

            BusinessInstanceState business = context.SharedBusinessRuntime.Businesses
                .First(candidate => candidate != null
                    && candidate.BusinessType != BusinessType.GeneralStore
                    && candidate.Owner != null
                    && candidate.Owner.OwnerKind == BusinessOwnerKind.Player);

            context.SetBusinessCash(business, 30000);
            business.RuntimeState.SetCurrentCashCents(30000);
            business.RuntimeState.ResetWeekToDateSales();
            business.RuntimeState.AddCashCents(12000);
            Assert.IsTrue(context.AcquisitionMarket.TryEstimatePlayerBusinessSaleOffer(business.AssignedBuildingId, out int healthyOffer, out string healthySummary), healthySummary);

            context.SetBusinessCash(business, 500);
            business.RuntimeState.ResetWeekToDateSales();
            business.RuntimeState.SpendCents(Mathf.Max(0, business.RuntimeState.CurrentCashCents - 500));
            Assert.IsTrue(context.AcquisitionMarket.TryEstimatePlayerBusinessSaleOffer(business.AssignedBuildingId, out int distressedOffer, out string distressedSummary), distressedSummary);

            Assert.Less(distressedOffer, healthyOffer);
        }

        [Test]
        public void LandPurchaseExpansionHeuristicAveragesNearPointOnePlots()
        {
            using TestWorld context = TestWorld.Create(10000000);
            MethodInfo method = typeof(TownWorldController).GetMethod(
                "CalculateLandPurchaseExpansionPlotCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            int total = 0;
            const int samples = 100;
            for (int i = 0; i < samples; i++)
            {
                total += (int)method.Invoke(context.TownWorld, new object[] { i, 1, 2 });
            }

            float average = total / (float)samples;
            Assert.That(average, Is.InRange(1.08f, 1.12f));
        }

        [Test]
        public void ActionChecklist_IncludesActionChecklist()
        {
            using TestWorld context = TestWorld.Create(10000000);
            AcquisitionMarketManager market = context.AcquisitionMarket;
            string text = market.BuildAcquisitionReadinessHeadline();
            StringAssert.Contains("Action checklist", text);
        }

        [Test]
        public void SelectedLeadReadinessIncludesExecutionLoad()
        {
            using TestWorld context = TestWorld.Create(ownerCashCents: 120000);
            string summary = context.AcquisitionMarket.BuildSelectedLeadReadinessSummary(AcquisitionMarketSection.Land);
            StringAssert.Contains("Execution Load", summary);
        }
    }
}
