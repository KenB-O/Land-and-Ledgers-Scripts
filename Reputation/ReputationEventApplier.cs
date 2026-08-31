using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Reputation
{
    public sealed class ReputationEventApplier
    {
        private readonly ReputationEvaluator evaluator;

        public ReputationEventApplier()
            : this(new ReputationEvaluator())
        {
        }

        public ReputationEventApplier(ReputationEvaluator evaluator)
        {
            this.evaluator = evaluator ?? new ReputationEvaluator();
        }

        public ReputationChangeResult Apply(PlayerReputationState state, ReputationEvent reputationEvent)
        {
            PlayerReputationState target = state ?? new PlayerReputationState();
            target.Clamp();

            ReputationEvent sanitized = reputationEvent.Sanitized();
            PlayerReputationState before = target.Clone();
            float headlineBefore = evaluator.EvaluateHeadline01(before);
            List<ReputationChangeReasonCode> reasons = new List<ReputationChangeReasonCode>();

            ApplyEvent(target, sanitized, reasons);
            target.Clamp();

            PlayerReputationState after = target.Clone();
            return new ReputationChangeResult(
                headlineBefore,
                evaluator.EvaluateHeadline01(after),
                before,
                after,
                BuildDeltas(before, after),
                Distinct(reasons));
        }

        private static void ApplyEvent(PlayerReputationState state, ReputationEvent reputationEvent, List<ReputationChangeReasonCode> reasons)
        {
            float scale = Mathf.Clamp01(reputationEvent.severity01) * Mathf.Clamp01(reputationEvent.confidence01);

            switch (reputationEvent.eventType)
            {
                case ReputationEventType.CleanDealClosed:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, 0.035f, scale, ReputationChangeReasonCode.CleanDealClosed);
                    Add(state, ReputationSubcategory.LocalSocialTrust, 0.012f, scale);
                    break;
                case ReputationEventType.DealClosedLate:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, -0.025f, scale, ReputationChangeReasonCode.DealReliabilityDamaged);
                    break;
                case ReputationEventType.DealCollapsedByPlayer:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, -0.06f, scale, ReputationChangeReasonCode.DealReliabilityDamaged);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.02f, scale);
                    break;
                case ReputationEventType.RenegotiatedAfterAgreement:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, -0.04f, scale, ReputationChangeReasonCode.RenegotiationTrustCost);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.012f, scale);
                    break;
                case ReputationEventType.SellerFinancingHonored:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, 0.03f, scale, ReputationChangeReasonCode.SellerFinancingReliability);
                    Add(state, ReputationSubcategory.LenderTrust, 0.02f, scale);
                    break;
                case ReputationEventType.SellerFinancingMissed:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, -0.06f, scale, ReputationChangeReasonCode.SellerFinancingMissed);
                    Add(state, ReputationSubcategory.LenderTrust, -0.04f, scale);
                    break;
                case ReputationEventType.FailedFinancingAfterCommitment:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, -0.055f, scale, ReputationChangeReasonCode.FinancingCredibilityDamaged);
                    Add(state, ReputationSubcategory.LenderTrust, -0.035f, scale);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.012f, scale);
                    break;
                case ReputationEventType.PostCloseStabilizationHonored:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.DealTrust, 0.028f, scale, ReputationChangeReasonCode.PostCloseStabilization);
                    Add(state, ReputationSubcategory.OperationalReliability, 0.032f, scale);
                    Add(state, ReputationSubcategory.LocalSocialTrust, 0.014f, scale);
                    break;
                case ReputationEventType.LoanPaidOnTime:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LenderTrust, 0.03f, scale, ReputationChangeReasonCode.LenderReliability);
                    break;
                case ReputationEventType.LoanPaymentLate:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LenderTrust, -0.04f, scale, ReputationChangeReasonCode.LoanPaymentConcern);
                    break;
                case ReputationEventType.LoanDefaulted:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LenderTrust, -0.12f, scale, ReputationChangeReasonCode.LoanDefault);
                    Add(state, ReputationSubcategory.DealTrust, -0.025f, scale);
                    break;
                case ReputationEventType.SupplierInvoicePaidOnTime:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.SupplierTrust, 0.028f, scale, ReputationChangeReasonCode.SupplierReliability);
                    break;
                case ReputationEventType.SupplierInvoiceLate:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.SupplierTrust, -0.045f, scale, ReputationChangeReasonCode.SupplierPaymentConcern);
                    break;
                case ReputationEventType.SupplierRelationshipStabilized:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.SupplierTrust, 0.032f, scale, ReputationChangeReasonCode.SupplierRelationshipStabilized);
                    Add(state, ReputationSubcategory.OperationalReliability, 0.012f, scale);
                    break;
                case ReputationEventType.WagesPaidOnTime:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, 0.022f, scale, ReputationChangeReasonCode.LaborReliability);
                    Add(state, ReputationSubcategory.OperationalReliability, 0.012f, scale);
                    break;
                case ReputationEventType.WagesMissed:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, -0.07f, scale, ReputationChangeReasonCode.WagePaymentConcern);
                    Add(state, ReputationSubcategory.OperationalReliability, -0.025f, scale);
                    break;
                case ReputationEventType.EmployerCoveredInjuryRelief:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, 0.035f, scale, ReputationChangeReasonCode.EmployerReliefTrust);
                    Add(state, ReputationSubcategory.OperationalReliability, 0.018f, scale);
                    break;
                case ReputationEventType.EmployerIgnoredWorkplaceInjury:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, -0.07f, scale, ReputationChangeReasonCode.WorkplaceInjuryNeglected);
                    Add(state, ReputationSubcategory.OperationalReliability, -0.035f, scale);
                    break;
                case ReputationEventType.HarshEmploymentPractice:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, -0.045f, scale, ReputationChangeReasonCode.HarshEmploymentPractice);
                    Add(state, ReputationSubcategory.OperationalReliability, -0.012f, scale);
                    break;
                case ReputationEventType.BusinessRecoveredQuickly:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, 0.035f, scale, ReputationChangeReasonCode.OperationalRecovery);
                    Add(state, ReputationSubcategory.SupplierTrust, 0.01f, scale);
                    break;
                case ReputationEventType.BusinessRemainedDisrupted:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, -0.04f, scale, ReputationChangeReasonCode.LingeringOperationalDisruption);
                    break;
                case ReputationEventType.BusinessReopenedReliably:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, 0.04f, scale, ReputationChangeReasonCode.ReliableReopening);
                    Add(state, ReputationSubcategory.LocalSocialTrust, 0.018f, scale);
                    Add(state, ReputationSubcategory.SupplierTrust, 0.012f, scale);
                    break;
                case ReputationEventType.BusinessRepeatedNeglect:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, -0.06f, scale, ReputationChangeReasonCode.RepeatedBusinessNeglect);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.03f, scale);
                    Add(state, ReputationSubcategory.SupplierTrust, -0.02f, scale);
                    break;
                case ReputationEventType.SeverePublicStockout:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, -0.05f, scale, ReputationChangeReasonCode.PublicStockoutDamagedTrust);
                    Add(state, ReputationSubcategory.SupplierTrust, -0.025f, scale);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.018f, scale);
                    break;
                case ReputationEventType.ChronicOverpricing:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, -0.04f, scale, ReputationChangeReasonCode.ChronicOverpricingTrustCost);
                    Add(state, ReputationSubcategory.DealTrust, -0.012f, scale);
                    break;
                case ReputationEventType.SpoiledGoodsPublicFailure:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, -0.055f, scale, ReputationChangeReasonCode.SpoiledGoodsConcern);
                    Add(state, ReputationSubcategory.LocalSocialTrust, -0.03f, scale);
                    Add(state, ReputationSubcategory.SupplierTrust, -0.018f, scale);
                    break;
                case ReputationEventType.StorefrontRefurbished:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.OperationalReliability, 0.026f, scale, ReputationChangeReasonCode.RefurbishmentConfidence);
                    Add(state, ReputationSubcategory.LocalSocialTrust, 0.016f, scale);
                    break;
                case ReputationEventType.FairHiringPractice:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, 0.025f, scale, ReputationChangeReasonCode.FairLocalPractice);
                    break;
                case ReputationEventType.CommunityDisruption:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, -0.05f, scale, ReputationChangeReasonCode.CommunityTrustDamaged);
                    break;
                case ReputationEventType.CommunityContribution:
                    ApplyReasonedDelta(state, reasons, ReputationSubcategory.LocalSocialTrust, 0.032f, scale, ReputationChangeReasonCode.CommunityContribution);
                    Add(state, ReputationSubcategory.DealTrust, 0.01f, scale);
                    break;
            }
        }

        private static void ApplyReasonedDelta(
            PlayerReputationState state,
            List<ReputationChangeReasonCode> reasons,
            ReputationSubcategory subcategory,
            float baseDelta,
            float scale,
            ReputationChangeReasonCode reasonCode)
        {
            Add(state, subcategory, baseDelta, scale);
            AddReason(reasons, reasonCode);
        }

        private static void AddReason(List<ReputationChangeReasonCode> reasons, ReputationChangeReasonCode reasonCode)
        {
            if (reasonCode != ReputationChangeReasonCode.None)
            {
                reasons.Add(reasonCode);
            }
        }

        private static void Add(PlayerReputationState state, ReputationSubcategory subcategory, float baseDelta, float scale)
        {
            state.Add(subcategory, baseDelta * scale);
        }

        private static IReadOnlyList<ReputationSubcategoryDelta> BuildDeltas(PlayerReputationState before, PlayerReputationState after)
        {
            List<ReputationSubcategoryDelta> deltas = new List<ReputationSubcategoryDelta>();
            AddDelta(deltas, before, after, ReputationSubcategory.DealTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.LenderTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.SupplierTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.LocalSocialTrust);
            AddDelta(deltas, before, after, ReputationSubcategory.OperationalReliability);
            return deltas;
        }

        private static void AddDelta(List<ReputationSubcategoryDelta> deltas, PlayerReputationState before, PlayerReputationState after, ReputationSubcategory subcategory)
        {
            float beforeValue = before.Get(subcategory);
            float afterValue = after.Get(subcategory);
            if (!Mathf.Approximately(beforeValue, afterValue))
            {
                deltas.Add(new ReputationSubcategoryDelta(subcategory, beforeValue, afterValue));
            }
        }

        private static IReadOnlyList<ReputationChangeReasonCode> Distinct(List<ReputationChangeReasonCode> reasons)
        {
            List<ReputationChangeReasonCode> distinct = new List<ReputationChangeReasonCode>();
            for (int i = 0; i < reasons.Count; i++)
            {
                if (reasons[i] != ReputationChangeReasonCode.None && !distinct.Contains(reasons[i]))
                {
                    distinct.Add(reasons[i]);
                }
            }

            return distinct;
        }
    }
}
