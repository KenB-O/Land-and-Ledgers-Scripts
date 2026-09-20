using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.DealTerms
{
    public sealed class ExpandedDealTermEvaluator
    {
        public ExpandedDealTermEvaluationResult Evaluate(ExpandedDealTerms terms, ExpandedDealTermEvaluationContext context)
        {
            ExpandedDealTermEvaluationContext sanitizedContext = context.Sanitized();
            int purchasePriceCents = Mathf.Max(1, sanitizedContext.purchasePriceCents);
            ExpandedDealTerms sanitizedTerms = terms.Sanitized(purchasePriceCents);
            List<ExpandedDealTermReasonCode> reasons = new List<ExpandedDealTermReasonCode>();

            int sellerFinancedPrincipalCents = sanitizedTerms.sellerFinancing.GetFinancedPrincipalCents(purchasePriceCents);
            int retainedLandValueCents = GetRetainedLandValueCents(sanitizedTerms.retainedLandLease, purchasePriceCents);
            // Retained-land / business-only structures can lower effective value, but they do not lower the
            // cash required to close the authored price. Financing need stays tied to the price actually funded.
            int thirdPartyNeedCents = GetThirdPartyNeedCents(
                purchasePriceCents,
                sellerFinancedPrincipalCents,
                sanitizedContext.buyerAvailableCashCents);
            float buyerBurden = Mathf.Clamp01(thirdPartyNeedCents / (float)purchasePriceCents) * 0.35f;
            float attractiveness = 0.5f;
            float risk = 0f;
            float sellerWillingnessDelta = 0f;
            int effectiveValueDeltaCents = 0;

            ApplySellerFinancing(
                sanitizedTerms.sellerFinancing,
                sanitizedContext,
                purchasePriceCents,
                sellerFinancedPrincipalCents,
                reasons,
                ref attractiveness,
                ref risk,
                ref sellerWillingnessDelta,
                ref buyerBurden);

            ApplyInventoryTerms(
                sanitizedTerms.inventoryTransfer,
                purchasePriceCents,
                reasons,
                ref attractiveness,
                ref risk,
                ref sellerWillingnessDelta,
                ref buyerBurden,
                ref effectiveValueDeltaCents);

            ApplyDelayedPossession(
                sanitizedTerms.delayedPossession,
                sanitizedContext,
                purchasePriceCents,
                reasons,
                ref attractiveness,
                ref risk,
                ref sellerWillingnessDelta,
                ref buyerBurden,
                ref effectiveValueDeltaCents);

            ApplyRetainedLandLease(
                sanitizedTerms.retainedLandLease,
                sanitizedContext,
                purchasePriceCents,
                retainedLandValueCents,
                reasons,
                ref attractiveness,
                ref risk,
                ref sellerWillingnessDelta,
                ref buyerBurden,
                ref effectiveValueDeltaCents);

            ApplyDocumentationQuality(
                sanitizedTerms,
                reasons,
                ref attractiveness,
                ref risk,
                ref buyerBurden);

            if (risk >= 0.45f)
            {
                reasons.Add(ExpandedDealTermReasonCode.RiskRaised);
            }

            if (buyerBurden >= 0.55f)
            {
                reasons.Add(ExpandedDealTermReasonCode.BuyerBurdenRaised);
            }

            if (sanitizedTerms.HasAnyTerms && risk <= 0.25f && buyerBurden <= 0.35f && sellerWillingnessDelta >= -0.02f)
            {
                reasons.Add(ExpandedDealTermReasonCode.CleanExpandedTerms);
            }

            return new ExpandedDealTermEvaluationResult(
                attractiveness,
                risk,
                sellerWillingnessDelta,
                buyerBurden,
                effectiveValueDeltaCents,
                thirdPartyNeedCents,
                Distinct(reasons));
        }

        private static void ApplySellerFinancing(
            SellerFinancingTerms terms,
            ExpandedDealTermEvaluationContext context,
            int purchasePriceCents,
            int sellerFinancedPrincipalCents,
            List<ExpandedDealTermReasonCode> reasons,
            ref float attractiveness,
            ref float risk,
            ref float sellerWillingnessDelta,
            ref float buyerBurden)
        {
            if (!terms.HasTerms)
            {
                return;
            }

            if (terms.NeedsStructureClarification)
            {
                reasons.Add(ExpandedDealTermReasonCode.FinancingStructureUnclear);
                attractiveness -= 0.04f;
                risk += 0.1f + context.sellerPressure01 * 0.04f;
                buyerBurden += 0.06f;
                sellerWillingnessDelta -= context.sellerCashNeed01 * 0.08f;

                if (terms.balloonPaymentCents > 0)
                {
                    reasons.Add(ExpandedDealTermReasonCode.BalloonPaymentRisk);
                    risk += 0.06f;
                    buyerBurden += 0.04f;
                }

                if (context.sellerCashNeed01 >= 0.4f || context.sellerPressure01 >= 0.55f)
                {
                    reasons.Add(ExpandedDealTermReasonCode.SellerCashNeedConflict);
                }
            }

            float principalRatio = Mathf.Clamp01(sellerFinancedPrincipalCents / (float)purchasePriceCents);

            if (terms.requested)
            {
                reasons.Add(ExpandedDealTermReasonCode.SellerFinancingRequested);
            }

            if (terms.offered && sellerFinancedPrincipalCents > 0)
            {
                reasons.Add(ExpandedDealTermReasonCode.SellerFinancingOffered);
                reasons.Add(ExpandedDealTermReasonCode.SellerFinancingReducesBuyerBurden);
                reasons.Add(ExpandedDealTermReasonCode.SellerFinancingAddsSellerRisk);

                attractiveness += 0.08f + principalRatio * 0.1f;
                buyerBurden -= principalRatio * Mathf.Lerp(0.12f, 0.24f, context.buyerCashCertainty01);
                risk += principalRatio * Mathf.Lerp(0.08f, 0.24f, 1f - context.buyerReputation01);
                risk -= terms.sellerRiskTolerance01 * 0.04f;
                sellerWillingnessDelta += 0.05f + terms.sellerRiskTolerance01 * 0.04f - context.sellerCashNeed01 * 0.08f;

                if (terms.balloonPaymentCents > 0)
                {
                    float balloonRatio = Mathf.Clamp01(terms.balloonPaymentCents / (float)Mathf.Max(1, sellerFinancedPrincipalCents));
                    reasons.Add(ExpandedDealTermReasonCode.BalloonPaymentRisk);
                    attractiveness -= 0.03f + balloonRatio * 0.04f;
                    buyerBurden += 0.04f + balloonRatio * 0.08f;
                    risk += 0.06f + balloonRatio * 0.1f;
                    sellerWillingnessDelta += balloonRatio * 0.02f;
                }

                if (context.sellerCashNeed01 >= 0.65f)
                {
                    reasons.Add(ExpandedDealTermReasonCode.SellerCashNeedConflict);
                }

                return;
            }

            if (terms.requested)
            {
                attractiveness -= 0.08f;
                risk += 0.08f + context.sellerPressure01 * 0.06f;
                sellerWillingnessDelta -= 0.08f + context.sellerCashNeed01 * 0.14f + context.sellerPressure01 * 0.08f;

                if (context.sellerCashNeed01 >= 0.4f || context.sellerPressure01 >= 0.55f)
                {
                    reasons.Add(ExpandedDealTermReasonCode.SellerCashNeedConflict);
                }
            }
        }

        private static void ApplyInventoryTerms(
            InventoryTransferTerms terms,
            int purchasePriceCents,
            List<ExpandedDealTermReasonCode> reasons,
            ref float attractiveness,
            ref float risk,
            ref float sellerWillingnessDelta,
            ref float buyerBurden,
            ref int effectiveValueDeltaCents)
        {
            if (!terms.HasTerms)
            {
                return;
            }

            if (terms.NeedsStructureClarification)
            {
                reasons.Add(ExpandedDealTermReasonCode.InventoryScopeUnclear);
                attractiveness -= 0.03f;
                buyerBurden += 0.05f;
                risk += 0.07f + terms.verificationRisk01 * 0.05f;
                return;
            }

            float valueRatio = Mathf.Clamp01(terms.estimatedInventoryValueCents / (float)purchasePriceCents);
            switch (terms.transferMode)
            {
                case InventoryTransferMode.Included:
                    reasons.Add(ExpandedDealTermReasonCode.InventoryIncluded);
                    effectiveValueDeltaCents += terms.estimatedInventoryValueCents;
                    attractiveness += 0.06f + terms.operatingContinuity01 * 0.08f + valueRatio * 0.12f;
                    buyerBurden -= terms.operatingContinuity01 * 0.08f;
                    risk += terms.verificationRisk01 * 0.08f;
                    sellerWillingnessDelta -= valueRatio * 0.03f;
                    break;
                case InventoryTransferMode.Excluded:
                    reasons.Add(ExpandedDealTermReasonCode.InventoryExcluded);
                    reasons.Add(ExpandedDealTermReasonCode.InventoryRestartBurden);
                    effectiveValueDeltaCents -= terms.estimatedInventoryValueCents;
                    attractiveness -= 0.06f + terms.restockBurden01 * 0.08f + valueRatio * 0.08f;
                    buyerBurden += 0.1f + terms.restockBurden01 * 0.18f;
                    risk += 0.06f + terms.restockBurden01 * 0.1f;
                    sellerWillingnessDelta += valueRatio * 0.03f;
                    break;
                case InventoryTransferMode.Partial:
                    reasons.Add(ExpandedDealTermReasonCode.PartialInventoryTransfer);
                    effectiveValueDeltaCents += Mathf.RoundToInt(terms.estimatedInventoryValueCents * 0.5f);
                    attractiveness += 0.03f + terms.operatingContinuity01 * 0.04f - terms.restockBurden01 * 0.04f;
                    buyerBurden += terms.restockBurden01 * 0.08f;
                    risk += 0.04f + terms.verificationRisk01 * 0.06f;

                    if (terms.estimatedInventoryValueCents <= 0)
                    {
                        reasons.Add(ExpandedDealTermReasonCode.InventoryScopeUnclear);
                        attractiveness -= 0.03f;
                        buyerBurden += 0.03f;
                        risk += 0.05f;
                    }
                    break;
            }
        }

        private static void ApplyDelayedPossession(
            DelayedPossessionTerms terms,
            ExpandedDealTermEvaluationContext context,
            int purchasePriceCents,
            List<ExpandedDealTermReasonCode> reasons,
            ref float attractiveness,
            ref float risk,
            ref float sellerWillingnessDelta,
            ref float buyerBurden,
            ref int effectiveValueDeltaCents)
        {
            if (!terms.HasTerms || !terms.delayed)
            {
                return;
            }

            float delay01 = Mathf.Clamp01(terms.AverageDelayDays / 90f);
            reasons.Add(ExpandedDealTermReasonCode.DelayedPossession);
            reasons.Add(ExpandedDealTermReasonCode.DelayedPossessionBurden);

            attractiveness -= 0.05f + delay01 * 0.1f;
            risk += 0.08f + delay01 * 0.24f;
            buyerBurden += 0.08f + delay01 * 0.28f;
            effectiveValueDeltaCents -= Mathf.RoundToInt(purchasePriceCents * delay01 * 0.025f);

            if (terms.maximumDelayDays <= 0)
            {
                reasons.Add(ExpandedDealTermReasonCode.PossessionWindowUnclear);
                attractiveness -= 0.03f;
                risk += 0.06f;
                buyerBurden += 0.04f;
            }

            if (terms.AverageDelayDays >= 45)
            {
                reasons.Add(ExpandedDealTermReasonCode.LongPossessionDelay);
                risk += 0.05f;
                buyerBurden += 0.04f;
            }

            if (terms.source == DelayedPossessionSource.SellerRequested || terms.source == DelayedPossessionSource.Mutual)
            {
                reasons.Add(ExpandedDealTermReasonCode.SellerPossessionBenefit);
                sellerWillingnessDelta += 0.04f + terms.sellerContinuityNeed01 * 0.12f + delay01 * 0.08f;
            }
            else if (terms.source == DelayedPossessionSource.BuyerRequested)
            {
                sellerWillingnessDelta -= 0.04f + delay01 * 0.06f + context.sellerPressure01 * 0.04f;
            }
        }

        private static void ApplyRetainedLandLease(
            RetainedLandLeaseTerms terms,
            ExpandedDealTermEvaluationContext context,
            int purchasePriceCents,
            int retainedLandValueCents,
            List<ExpandedDealTermReasonCode> reasons,
            ref float attractiveness,
            ref float risk,
            ref float sellerWillingnessDelta,
            ref float buyerBurden,
            ref int effectiveValueDeltaCents)
        {
            if (!terms.HasTerms)
            {
                return;
            }

            if (terms.NeedsStructureClarification)
            {
                reasons.Add(ExpandedDealTermReasonCode.LeaseEconomicsUnclear);
                reasons.Add(ExpandedDealTermReasonCode.RetainedLandControlRisk);
                attractiveness -= 0.05f;
                buyerBurden += 0.06f;
                risk += 0.1f;
                sellerWillingnessDelta += context.sellerAttachment01 * 0.04f;
                return;
            }

            if (terms.retainedLandLease)
            {
                reasons.Add(ExpandedDealTermReasonCode.RetainedLandLease);
                reasons.Add(ExpandedDealTermReasonCode.RetainedLandControlRisk);
                effectiveValueDeltaCents -= retainedLandValueCents;

                float annualLeaseToPrice01 = Mathf.Clamp01(terms.weeklyLeasePaymentCents * 52f / purchasePriceCents);
                float renewalRisk = terms.renewalSecurity01 <= 0f ? 0.55f : 1f - terms.renewalSecurity01;
                float controlRisk = terms.sellerControlRights01 * 0.18f + renewalRisk * 0.12f;
                float optionRelief = terms.purchaseOptionValue01 * 0.05f;
                float shortLeasePressure = terms.leaseTermMonths > 0 && terms.leaseTermMonths <= 18
                    ? Mathf.InverseLerp(18f, 0f, terms.leaseTermMonths)
                    : 0f;

                if (terms.leaseTermMonths > 0 && terms.leaseTermMonths <= 18)
                {
                    reasons.Add(ExpandedDealTermReasonCode.ShortLeaseTerm);
                }

                if (terms.renewalSecurity01 <= 0f || terms.renewalSecurity01 < 0.4f)
                {
                    reasons.Add(ExpandedDealTermReasonCode.WeakRenewalSecurity);
                }

                if (terms.sellerControlRights01 > 0.65f)
                {
                    reasons.Add(ExpandedDealTermReasonCode.HeavySellerControl);
                }

                if (terms.NeedsLeaseEconomicsClarification)
                {
                    reasons.Add(ExpandedDealTermReasonCode.LeaseEconomicsUnclear);
                    attractiveness -= 0.04f;
                    buyerBurden += 0.05f;
                    risk += 0.08f;
                }

                attractiveness -= 0.04f + controlRisk * 0.5f + annualLeaseToPrice01 * 0.08f + shortLeasePressure * 0.08f;
                attractiveness += optionRelief;
                buyerBurden += annualLeaseToPrice01 * 0.18f + shortLeasePressure * 0.08f;
                risk += 0.1f + controlRisk - optionRelief + shortLeasePressure * 0.12f;
                sellerWillingnessDelta += 0.08f
                    + context.sellerAttachment01 * 0.12f
                    + context.sellerRelationshipSensitivity01 * 0.05f
                    + terms.sellerControlRights01 * 0.04f;
            }

            if (terms.businessOnlyTransfer)
            {
                reasons.Add(ExpandedDealTermReasonCode.BusinessOnlyTransfer);
                if (!terms.retainedLandLease)
                {
                    reasons.Add(ExpandedDealTermReasonCode.RetainedLandControlRisk);
                    effectiveValueDeltaCents -= retainedLandValueCents;
                    attractiveness -= 0.09f;
                    risk += 0.14f + context.sellerAttachment01 * 0.04f;
                    buyerBurden += 0.1f;
                    sellerWillingnessDelta += 0.07f + context.sellerAttachment01 * 0.08f;

                    if (terms.NeedsOccupancyEconomicsClarification)
                    {
                        reasons.Add(ExpandedDealTermReasonCode.LeaseEconomicsUnclear);
                        attractiveness -= 0.04f;
                        risk += 0.08f;
                        buyerBurden += 0.06f;
                    }
                }
            }
        }

        private static void ApplyDocumentationQuality(
            ExpandedDealTerms terms,
            List<ExpandedDealTermReasonCode> reasons,
            ref float attractiveness,
            ref float risk,
            ref float buyerBurden)
        {
            if (!terms.HasAnyTerms || terms.documentationQuality01 >= 0.65f)
            {
                return;
            }

            // Documentation weakness is weighted more heavily on structurally awkward deals,
            // because retained control / delayed possession / partial handoff are harder to unwind at close.
            float weight = 1f;
            if (terms.delayedPossession.delayed || terms.delayedPossession.NeedsStructureClarification)
            {
                weight += 0.2f;
            }

            if (terms.retainedLandLease.retainedLandLease || terms.retainedLandLease.businessOnlyTransfer || terms.retainedLandLease.NeedsStructureClarification)
            {
                weight += 0.3f;
            }

            if (terms.sellerFinancing.UsesStructuredSellerFinancing || terms.sellerFinancing.NeedsStructureClarification)
            {
                weight += 0.15f;
            }

            if (terms.inventoryTransfer.transferMode == InventoryTransferMode.Partial || terms.inventoryTransfer.NeedsStructureClarification)
            {
                weight += 0.1f;
            }

            float weakness01 = Mathf.InverseLerp(0.65f, 0f, terms.documentationQuality01);
            reasons.Add(ExpandedDealTermReasonCode.DocumentationWeak);
            attractiveness -= weakness01 * 0.08f * weight;
            risk += weakness01 * 0.12f * weight;
            buyerBurden += weakness01 * 0.06f * weight;
        }

        private static int GetThirdPartyNeedCents(
            int purchasePriceCents,
            int sellerFinancedPrincipalCents,
            int buyerAvailableCashCents)
        {
            int price = Mathf.Max(0, purchasePriceCents);
            int financed = Mathf.Clamp(sellerFinancedPrincipalCents, 0, price);
            int buyerCash = Mathf.Clamp(buyerAvailableCashCents, 0, price);
            return Mathf.Max(0, price - financed - buyerCash);
        }

        private static int GetRetainedLandValueCents(RetainedLandLeaseTerms terms, int purchasePriceCents)
        {
            if (!terms.retainedLandLease && !terms.businessOnlyTransfer)
            {
                return 0;
            }

            if (terms.estimatedRetainedLandValueCents > 0)
            {
                return Mathf.Clamp(terms.estimatedRetainedLandValueCents, 0, purchasePriceCents);
            }

            return Mathf.RoundToInt(purchasePriceCents * (terms.businessOnlyTransfer && !terms.retainedLandLease ? 0.35f : 0.28f));
        }

        private static IReadOnlyList<ExpandedDealTermReasonCode> Distinct(List<ExpandedDealTermReasonCode> reasons)
        {
            List<ExpandedDealTermReasonCode> distinct = new List<ExpandedDealTermReasonCode>();
            for (int i = 0; i < reasons.Count; i++)
            {
                if (reasons[i] != ExpandedDealTermReasonCode.None && !distinct.Contains(reasons[i]))
                {
                    distinct.Add(reasons[i]);
                }
            }

            return distinct;
        }
    }
}
