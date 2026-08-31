using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Valuation;
using LandLedgers.Reputation;
using UnityEngine;

namespace LandLedgers.Economy.DealTerms
{
    public static class ExpandedDealTermAdapters
    {
        public static bool HasExplicitExpandedTerms(AcquisitionOfferTerms offerTerms)
        {
            return offerTerms.expandedDealTerms.HasAnyTerms;
        }

        public static ExpandedDealTerms GetEffectiveDealTerms(AcquisitionOfferTerms offerTerms)
        {
            if (offerTerms.expandedDealTerms.HasAnyTerms)
            {
                return offerTerms.expandedDealTerms.Sanitized(offerTerms.offerPriceCents);
            }

            if (!offerTerms.sellerFinancingRequested)
            {
                return default;
            }

            return BuildLegacySellerFinancingRequestBridge(offerTerms.offerPriceCents);
        }

        public static AcquisitionOfferTerms WithExpandedDealTerms(AcquisitionOfferTerms offerTerms, ExpandedDealTerms expandedTerms)
        {
            offerTerms.expandedDealTerms = expandedTerms.Sanitized(offerTerms.offerPriceCents);
            // The legacy offer layer only has one seller-financing bool. Keep it true whenever the
            // expanded structure actively uses seller financing so older call sites do not miss an
            // offered-but-not-requested financing arrangement.
            offerTerms.sellerFinancingRequested = offerTerms.expandedDealTerms.sellerFinancing.UsesStructuredSellerFinancing;
            return offerTerms;
        }

        private static ExpandedDealTerms BuildLegacySellerFinancingRequestBridge(int purchasePriceCents)
        {
            // A legacy "sellerFinancingRequested" flag means the offer is using an older bridge path,
            // not that the paperwork is already weak. Leave documentation at zero so ExpandedDealTerms.Sanitized
            // can assign its normal default documentation quality instead of forcing a fake documentation penalty.
            return new ExpandedDealTerms
            {
                sellerFinancing = new SellerFinancingTerms
                {
                    requested = true
                },
                documentationQuality01 = 0f
            }.Sanitized(purchasePriceCents);
        }

        public static ExpandedDealTermEvaluationContext BuildValuationContext(
            AcquisitionOfferTerms offerTerms,
            SellerProfile seller,
            SellerPressureResult sellerPressure,
            BuyerOfferProfile buyer,
            int estimatedAssetValueCents)
        {
            int offerPriceCents = Mathf.Max(0, offerTerms.offerPriceCents);
            ExpandedDealTerms effectiveTerms = GetEffectiveDealTerms(offerTerms);
            int buyerAvailableCashCents = EstimateBuyerAvailableCashForEvaluation(
                offerPriceCents,
                estimatedAssetValueCents,
                effectiveTerms,
                buyer.cashCertainty01);

            return new ExpandedDealTermEvaluationContext
            {
                purchasePriceCents = offerPriceCents,
                estimatedAssetValueCents = estimatedAssetValueCents,
                buyerAvailableCashCents = buyerAvailableCashCents,
                sellerPressure01 = sellerPressure.Pressure01,
                sellerCashNeed01 = seller.cashNeed01,
                sellerAttachment01 = seller.attachment01,
                sellerRelationshipSensitivity01 = sellerPressure.RelationshipSensitivity01,
                buyerReputation01 = buyer.reputation01,
                buyerCashCertainty01 = buyer.cashCertainty01
            }.Sanitized();
        }

        public static bool TryBuildSellerFinancingLoanTerm(
            ExpandedDealTerms expandedTerms,
            int purchasePriceCents,
            out LoanTermStructure loanTerm)
        {
            ExpandedDealTerms sanitized = expandedTerms.Sanitized(purchasePriceCents);
            SellerFinancingTerms sellerFinancing = sanitized.sellerFinancing;
            int principalCents = sellerFinancing.GetFinancedPrincipalCents(purchasePriceCents);
            if (!sellerFinancing.offered || principalCents <= 0)
            {
                loanTerm = default;
                return false;
            }

            loanTerm = new LoanTermStructure
            {
                principalCents = principalCents,
                downPaymentCents = Mathf.Clamp(sellerFinancing.downPaymentCents, 0, Mathf.Max(0, purchasePriceCents)),
                annualInterestRateBps = sellerFinancing.annualInterestRateBps <= 0 ? 800 : sellerFinancing.annualInterestRateBps,
                termMonths = sellerFinancing.termMonths <= 0 ? 60 : sellerFinancing.termMonths,
                amortizationMonths = sellerFinancing.amortizationMonths <= 0
                    ? (sellerFinancing.termMonths <= 0 ? 60 : sellerFinancing.termMonths)
                    : sellerFinancing.amortizationMonths,
                repaymentFrequency = sellerFinancing.paymentCadence == SellerFinancingPaymentCadence.Monthly
                    ? RepaymentFrequency.Monthly
                    : RepaymentFrequency.Weekly,
                balloonPaymentCents = sellerFinancing.balloonPaymentCents
            }.Sanitized();

            return true;
        }

        public static FinancingApplicantProfile BuildFinancingApplicantProfile(
            string applicantId,
            ReputationInfluenceProfile reputation,
            int availableCashCents,
            int weeklyNetCashFlowCents,
            int existingDebtPaymentCents,
            int ownedAssetValueCents)
        {
            return new FinancingApplicantProfile
            {
                applicantId = applicantId ?? string.Empty,
                reputation01 = reputation.HeadlineReputation01,
                lenderTrust01 = reputation.LenderTrust01,
                operationalReliability01 = reputation.OperationalReliability01,
                availableCashCents = Mathf.Max(0, availableCashCents),
                weeklyNetCashFlowCents = weeklyNetCashFlowCents,
                existingDebtPaymentCents = Mathf.Max(0, existingDebtPaymentCents),
                ownedAssetValueCents = Mathf.Max(0, ownedAssetValueCents),
                priorFailedFinancingCount = 0
            }.Sanitized();
        }

        public static ReputationEvent BuildSellerFinancingReputationEvent(
            ExpandedDealTerms expandedTerms,
            int purchasePriceCents,
            SellerFinancingPerformance performance,
            string counterpartyId = "",
            string assetId = "")
        {
            ExpandedDealTerms sanitized = expandedTerms.Sanitized(purchasePriceCents);
            int price = Mathf.Max(1, purchasePriceCents);
            int principalCents = sanitized.sellerFinancing.GetFinancedPrincipalCents(price);
            if (principalCents <= 0)
            {
                principalCents = Mathf.Clamp(sanitized.sellerFinancing.principalCents, 0, price);
            }

            float principalRatio = Mathf.Clamp01(principalCents / (float)price);
            return new ReputationEvent
            {
                eventType = performance switch
                {
                    SellerFinancingPerformance.Honored => ReputationEventType.SellerFinancingHonored,
                    SellerFinancingPerformance.Missed => ReputationEventType.SellerFinancingMissed,
                    _ => ReputationEventType.None
                },
                sourceSystem = "expanded_deal_terms",
                counterpartyId = counterpartyId ?? string.Empty,
                assetId = assetId ?? string.Empty,
                severity01 = performance == SellerFinancingPerformance.Missed
                    ? Mathf.Lerp(0.55f, 1f, principalRatio)
                    : Mathf.Lerp(0.25f, 0.65f, principalRatio),
                confidence01 = 1f,
                note = performance == SellerFinancingPerformance.Missed
                    ? "Seller financing payment missed."
                    : performance == SellerFinancingPerformance.Honored
                        ? "Seller financing payment honored."
                        : "No seller financing reputation event."
            }.Sanitized();
        }

        public static BuyerOfferProfile BuildBuyerProfileFromReputation(
            string buyerId,
            BuyerKind buyerKind,
            ReputationInfluenceProfile reputation,
            int availableCashCents,
            int offerPriceCents)
        {
            float cashCertainty = offerPriceCents <= 0
                ? 0f
                : Mathf.Clamp01(availableCashCents / (float)offerPriceCents);

            return new BuyerOfferProfile
            {
                buyerId = buyerId ?? string.Empty,
                buyerKind = buyerKind,
                reputation01 = reputation.HeadlineReputation01,
                localTrust01 = reputation.LocalSocialTrust01,
                priorDealReliability01 = reputation.DealTrust01,
                strategicThreat01 = 0f,
                communityFit01 = Mathf.Clamp01(reputation.LocalSocialTrust01 * 0.65f + reputation.OperationalReliability01 * 0.35f),
                cashCertainty01 = Mathf.Clamp01(cashCertainty * 0.7f + reputation.LenderTrust01 * 0.3f),
                relationshipWithSeller01 = Mathf.Clamp01(reputation.NegotiationRelationshipHandling01 * 0.7f + reputation.OperationalReliability01 * 0.3f)
            };
        }

        public static AcquisitionOfferTerms ProjectRivalOfferTerms(
            AcquisitionOfferTerms baseline,
            ExpandedDealTerms expandedTerms,
            float rivalCashCertainty01)
        {
            ExpandedDealTerms sanitizedTerms = expandedTerms.Sanitized(baseline.offerPriceCents);
            baseline.expandedDealTerms = sanitizedTerms;
            baseline.sellerFinancingRequested = sanitizedTerms.sellerFinancing.UsesStructuredSellerFinancing;

            float sanitizedCashCertainty01 = Mathf.Clamp01(rivalCashCertainty01);
            ExpandedDealTermEvaluationContext context = BuildRivalProjectionContext(
                baseline.offerPriceCents,
                sanitizedTerms,
                sanitizedCashCertainty01);
            ExpandedDealTermEvaluationResult evaluation = new ExpandedDealTermEvaluator().Evaluate(sanitizedTerms, context);

            float lowCashPressure01 = 1f - sanitizedCashCertainty01;
            float formalReviewPressure01 = GetFormalReviewPressure01(evaluation, sanitizedTerms);
            float contingencyIncrease01 = evaluation.Risk01 * 0.2f
                + evaluation.BuyerBurden01 * 0.08f
                + formalReviewPressure01 * Mathf.Lerp(0.02f, 0.08f, lowCashPressure01);

            if (evaluation.NeedsClosingCare)
            {
                contingencyIncrease01 += 0.08f;
            }
            else if (evaluation.NeedsFormalWorkflow)
            {
                contingencyIncrease01 += 0.06f;
            }
            else if (evaluation.NeedsEnhancedDiligence)
            {
                contingencyIncrease01 += 0.04f;
            }

            float concessionIncrease01 = Mathf.Max(0f, evaluation.SellerWillingnessDelta) * 0.35f;
            if ((evaluation.TagFlags & ExpandedDealTagFlags.FinancingReview) != 0)
            {
                // Cash-thin rivals lean harder on seller financing and should carry more visible non-price
                // structure than a rival that can credibly close with mostly cash or lender-backed funds.
                float financingReliance01 = sanitizedTerms.sellerFinancing.offered || sanitizedTerms.sellerFinancing.requested
                    ? Mathf.Lerp(0.02f, 0.07f, lowCashPressure01)
                    : 0.03f + formalReviewPressure01 * 0.03f;
                concessionIncrease01 += financingReliance01;
            }

            if ((evaluation.TagFlags & ExpandedDealTagFlags.LandControlReview) != 0)
            {
                concessionIncrease01 += Mathf.Lerp(0.01f, 0.04f, sanitizedTerms.retainedLandLease.sellerControlRights01);
            }

            if ((evaluation.TagFlags & ExpandedDealTagFlags.ClarificationReview) != 0)
            {
                contingencyIncrease01 += 0.03f;
            }

            baseline.contingencyBurden01 = Mathf.Clamp01(baseline.contingencyBurden01 + contingencyIncrease01);
            baseline.nonPriceConcessions01 = Mathf.Clamp01(baseline.nonPriceConcessions01 + concessionIncrease01);
            return baseline;
        }

        private static float GetFormalReviewPressure01(
            ExpandedDealTermEvaluationResult evaluation,
            ExpandedDealTerms terms)
        {
            float pressure01 = 0f;
            if (evaluation.NeedsFormalWorkflow)
            {
                pressure01 += 0.25f;
            }

            if (evaluation.NeedsClosingCare)
            {
                pressure01 += 0.25f;
            }

            if ((evaluation.TagFlags & ExpandedDealTagFlags.ClarificationReview) != 0)
            {
                pressure01 += 0.2f;
            }

            if (terms.sellerFinancing.NeedsStructureClarification)
            {
                pressure01 += 0.15f;
            }

            if (terms.HasExplicitWeakDocumentation)
            {
                pressure01 += 0.1f;
            }

            return Mathf.Clamp01(pressure01);
        }

        private static ExpandedDealTermEvaluationContext BuildRivalProjectionContext(
            int offerPriceCents,
            ExpandedDealTerms expandedTerms,
            float rivalCashCertainty01)
        {
            float sanitizedCashCertainty01 = Mathf.Clamp01(rivalCashCertainty01);
            return new ExpandedDealTermEvaluationContext
            {
                purchasePriceCents = Mathf.Max(0, offerPriceCents),
                buyerAvailableCashCents = EstimateBuyerAvailableCashForEvaluation(
                    offerPriceCents,
                    0,
                    expandedTerms,
                    sanitizedCashCertainty01),
                buyerCashCertainty01 = sanitizedCashCertainty01,
                // Rival projection has no direct reputation input in this lane. Scale a conservative buyer-credibility
                // signal from closing confidence instead of leaving every rival frozen at one flat reputation value.
                buyerReputation01 = Mathf.Lerp(0.4f, 0.65f, sanitizedCashCertainty01)
            }.Sanitized();
        }

        private static int EstimateBuyerAvailableCashForEvaluation(
            int offerPriceCents,
            int estimatedAssetValueCents,
            ExpandedDealTerms expandedTerms,
            float buyerCashCertainty01)
        {
            int priceCents = Mathf.Max(0, offerPriceCents);
            if (priceCents <= 0)
            {
                return 0;
            }

            ExpandedDealTerms sanitizedTerms = expandedTerms.Sanitized(priceCents);
            float certainty01 = Mathf.Clamp01(buyerCashCertainty01);
            int explicitDownPaymentCents = sanitizedTerms.sellerFinancing.offered
                ? Mathf.Clamp(sanitizedTerms.sellerFinancing.downPaymentCents, 0, priceCents)
                : 0;

            // buyer.cashCertainty01 is broader than literal liquid cash because it can reflect lender trust and general
            // closeability. Translate it into a conservative own-cash proxy so valuation/evaluation does not mistake
            // a credible financed buyer for an all-cash buyer. Ambiguous structures reserve more cash because they
            // are more likely to need diligence deposits, legal review, or fallback liquidity before closing.
            float ownCashCoverage01 = certainty01 * Mathf.Lerp(0.25f, 0.7f, certainty01);

            if (estimatedAssetValueCents > 0)
            {
                float priceToValueSupport01 = Mathf.Clamp01(estimatedAssetValueCents / (float)priceCents);
                ownCashCoverage01 *= Mathf.Lerp(0.8f, 1f, priceToValueSupport01);
            }

            ownCashCoverage01 *= 1f - GetStructureReserveDrag01(sanitizedTerms);

            int estimatedOwnCashCents = Mathf.RoundToInt(priceCents * ownCashCoverage01);
            estimatedOwnCashCents = Mathf.Clamp(estimatedOwnCashCents, 0, priceCents);

            if (explicitDownPaymentCents > 0)
            {
                estimatedOwnCashCents = Mathf.Max(estimatedOwnCashCents, explicitDownPaymentCents);
            }

            return estimatedOwnCashCents;
        }

        private static float GetStructureReserveDrag01(ExpandedDealTerms terms)
        {
            float drag01 = 0f;
            if (terms.inventoryTransfer.transferMode == InventoryTransferMode.Partial)
            {
                drag01 += 0.04f;

                if (terms.NeedsInventoryScopeClarification)
                {
                    drag01 += 0.03f;
                }
            }
            else if (terms.inventoryTransfer.transferMode == InventoryTransferMode.Excluded)
            {
                drag01 += 0.06f;
            }

            if (terms.delayedPossession.delayed)
            {
                drag01 += 0.04f;

                if (terms.NeedsPossessionWindowClarification)
                {
                    drag01 += 0.03f;
                }
            }

            if (terms.retainedLandLease.retainedLandLease || terms.retainedLandLease.businessOnlyTransfer)
            {
                drag01 += 0.06f;

                if (terms.NeedsLeaseEconomicsClarification)
                {
                    drag01 += 0.05f;
                }
            }

            if (terms.sellerFinancing.NeedsStructureClarification)
            {
                drag01 += 0.08f;
            }
            else if (terms.sellerFinancing.requested && !terms.sellerFinancing.offered)
            {
                drag01 += 0.03f;
            }

            if (terms.sellerFinancing.offered && terms.sellerFinancing.balloonPaymentCents > 0)
            {
                drag01 += 0.03f;
            }

            if (terms.HasExplicitWeakDocumentation)
            {
                drag01 += 0.04f;
            }

            return Mathf.Clamp01(drag01);
    }
}

}