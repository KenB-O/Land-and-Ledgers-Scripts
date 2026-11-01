using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    public sealed class SupplierTrustEvaluator
    {
        private const float PreferredTermsThreshold = 0.68f;
        private const float PreferredSourcingThreshold = 0.62f;
        private const float HighScoreThreshold = 0.64f;
        private const float LowScoreThreshold = 0.38f;

        public SupplierTrustChangeProposal ProposeChange(SupplierRelationshipState state, SupplierRelationshipEvent relationshipEvent)
        {
            SupplierRelationshipState current = GetState(state);
            SupplierRelationshipEvent sanitizedEvent = relationshipEvent.Sanitized();
            List<SupplierRelationshipReasonCode> reasons = new List<SupplierRelationshipReasonCode>();
            float scale = sanitizedEvent.severity01;
            float trustDelta = 0f;
            float reliabilityDelta = 0f;
            float paymentDelta = 0f;
            float strainDelta = 0f;
            bool preferred = current.preferred;

            switch (sanitizedEvent.eventType)
            {
                case SupplierRelationshipEventType.InvoicePaidOnTime:
                    trustDelta += 0.035f * scale;
                    paymentDelta += 0.065f * scale;
                    strainDelta -= 0.01f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.OnTimePayment);
                    break;
                case SupplierRelationshipEventType.InvoicePaidLate:
                    trustDelta -= 0.045f * scale;
                    paymentDelta -= 0.08f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.LatePayment);
                    break;
                case SupplierRelationshipEventType.InvoiceMissed:
                    trustDelta -= 0.09f * scale;
                    paymentDelta -= 0.14f * scale;
                    strainDelta += 0.02f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.MissedPayment);
                    break;
                case SupplierRelationshipEventType.OrderFulfilledClean:
                    trustDelta += 0.025f * scale;
                    reliabilityDelta += 0.07f * scale;
                    strainDelta -= 0.015f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.CleanFulfillment);
                    break;
                case SupplierRelationshipEventType.OrderFulfilledShort:
                    trustDelta -= 0.035f * scale;
                    reliabilityDelta -= 0.085f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.ShortFulfillment);
                    break;
                case SupplierRelationshipEventType.OrderFailed:
                    trustDelta -= 0.07f * scale;
                    reliabilityDelta -= 0.14f * scale;
                    strainDelta += 0.025f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.FailedFulfillment);
                    break;
                case SupplierRelationshipEventType.EmergencyOrderRequested:
                    trustDelta -= 0.005f * scale;
                    strainDelta += 0.08f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderStrain);
                    break;
                case SupplierRelationshipEventType.EmergencyOrderFulfilled:
                    trustDelta += 0.04f * scale;
                    reliabilityDelta += 0.025f * scale;
                    strainDelta += 0.05f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderStrain);
                    AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderRecovered);
                    break;
                case SupplierRelationshipEventType.EmergencyOrderFailed:
                    trustDelta -= 0.08f * scale;
                    reliabilityDelta -= 0.12f * scale;
                    strainDelta += 0.12f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderStrain);
                    AddDistinct(reasons, SupplierRelationshipReasonCode.FailedFulfillment);
                    break;
                case SupplierRelationshipEventType.PreferredStatusGranted:
                    preferred = true;
                    trustDelta += 0.02f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.PreferredStatusGranted);
                    break;
                case SupplierRelationshipEventType.PreferredStatusLost:
                    preferred = false;
                    trustDelta -= 0.04f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.PreferredStatusLost);
                    break;
                case SupplierRelationshipEventType.CooldownWeekPassed:
                    strainDelta -= 0.06f * scale;
                    AddDistinct(reasons, SupplierRelationshipReasonCode.CooldownRecovery);
                    break;
            }

            float projectedTrust = Mathf.Clamp01(current.trust01 + trustDelta);
            float projectedReliability = Mathf.Clamp01(current.reliability01 + reliabilityDelta);
            float projectedPayment = Mathf.Clamp01(current.paymentHistory01 + paymentDelta);
            float projectedStrain = Mathf.Clamp01(current.emergencyOrderStrain01 + strainDelta);

            float actualTrustDelta = projectedTrust - current.trust01;
            float actualReliabilityDelta = projectedReliability - current.reliability01;
            float actualPaymentDelta = projectedPayment - current.paymentHistory01;
            float actualStrainDelta = projectedStrain - current.emergencyOrderStrain01;
            RelationshipTrendDirection trend = EvaluateTrend(actualTrustDelta, actualReliabilityDelta, actualPaymentDelta, actualStrainDelta);

            string supplierId = string.IsNullOrWhiteSpace(sanitizedEvent.supplierId)
                ? current.supplierId
                : sanitizedEvent.supplierId;
            return new SupplierTrustChangeProposal(
                supplierId,
                sanitizedEvent.eventType,
                actualTrustDelta,
                actualReliabilityDelta,
                actualPaymentDelta,
                actualStrainDelta,
                projectedTrust,
                projectedReliability,
                projectedPayment,
                projectedStrain,
                preferred,
                trend,
                reasons);
        }

        public SupplierRelationshipState BuildUpdatedState(SupplierRelationshipState state, SupplierRelationshipEvent relationshipEvent)
        {
            SupplierRelationshipState updated = GetState(state);
            SupplierRelationshipEvent sanitizedEvent = relationshipEvent.Sanitized();
            SupplierTrustChangeProposal proposal = ProposeChange(updated, sanitizedEvent);

            updated.supplierId = string.IsNullOrWhiteSpace(proposal.SupplierId) ? updated.supplierId : proposal.SupplierId;
            updated.trust01 = proposal.ProjectedTrust01;
            updated.reliability01 = proposal.ProjectedReliability01;
            updated.paymentHistory01 = proposal.ProjectedPaymentHistory01;
            updated.emergencyOrderStrain01 = proposal.ProjectedEmergencyOrderStrain01;
            updated.preferred = proposal.Preferred;
            updated.lastInteractionDayIndex = sanitizedEvent.absoluteDayIndex >= 0
                ? sanitizedEvent.absoluteDayIndex
                : updated.lastInteractionDayIndex;
            updated.lastTrend = proposal.Trend;
            updated.lastReasonCodes = new List<SupplierRelationshipReasonCode>(proposal.ReasonCodes);
            RecordEventCounts(updated, sanitizedEvent.eventType);
            return updated;
        }

        public SupplierTrustStatusSummary Summarize(SupplierRelationshipState state)
        {
            SupplierRelationshipState current = GetState(state);
            float sourcingReliability = Mathf.Clamp01(
                current.reliability01 * 0.55f
                + current.trust01 * 0.3f
                + current.paymentHistory01 * 0.15f
                - current.emergencyOrderStrain01 * 0.25f);
            float termsReadiness = Mathf.Clamp01(
                current.trust01 * 0.45f
                + current.paymentHistory01 * 0.35f
                + current.reliability01 * 0.2f
                - current.emergencyOrderStrain01 * 0.3f
                + (current.preferred ? 0.05f : 0f));
            bool preferredEligible = termsReadiness >= PreferredTermsThreshold
                && sourcingReliability >= PreferredSourcingThreshold
                && current.emergencyOrderStrain01 <= 0.35f;

            List<SupplierRelationshipReasonCode> reasons = new List<SupplierRelationshipReasonCode>();
            if (sourcingReliability >= HighScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.ReliableSourcing);
            }
            else if (sourcingReliability <= LowScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.UnreliableSourcing);
            }

            if (current.paymentHistory01 >= HighScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.PaymentHistoryStrong);
            }
            else if (current.paymentHistory01 <= LowScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.PaymentHistoryWeak);
            }

            if (termsReadiness >= HighScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.TermsReady);
            }
            else if (termsReadiness <= LowScoreThreshold)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.TermsAtRisk);
            }

            if (current.emergencyOrderStrain01 >= 0.32f)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderStrain);
            }
            else if (current.emergencyOrdersRequested > 0 && current.emergencyOrderStrain01 <= 0.08f)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.EmergencyOrderRecovered);
            }

            if (current.preferred)
            {
                AddDistinct(reasons, SupplierRelationshipReasonCode.PreferredSupplier);
            }

            return new SupplierTrustStatusSummary(
                current.supplierId,
                current.originKind,
                current.trust01,
                current.reliability01,
                current.paymentHistory01,
                current.emergencyOrderStrain01,
                sourcingReliability,
                termsReadiness,
                current.preferred,
                preferredEligible,
                current.lastTrend,
                reasons);
        }

        private static SupplierRelationshipState GetState(SupplierRelationshipState state)
        {
            return state != null
                ? state.SanitizedCopy()
                : new SupplierRelationshipState().SanitizedCopy();
        }

        private static RelationshipTrendDirection EvaluateTrend(
            float trustDelta,
            float reliabilityDelta,
            float paymentDelta,
            float strainDelta)
        {
            float weightedDelta = trustDelta * 0.45f
                + reliabilityDelta * 0.25f
                + paymentDelta * 0.2f
                - strainDelta * 0.1f;
            if (weightedDelta >= 0.005f)
            {
                return RelationshipTrendDirection.Improving;
            }

            if (weightedDelta <= -0.005f)
            {
                return RelationshipTrendDirection.Declining;
            }

            return RelationshipTrendDirection.Stable;
        }

        private static void RecordEventCounts(SupplierRelationshipState state, SupplierRelationshipEventType eventType)
        {
            switch (eventType)
            {
                case SupplierRelationshipEventType.InvoicePaidOnTime:
                    state.invoicesPaidOnTime++;
                    break;
                case SupplierRelationshipEventType.InvoicePaidLate:
                    state.invoicesPaidLate++;
                    break;
                case SupplierRelationshipEventType.InvoiceMissed:
                    state.invoicesMissed++;
                    break;
                case SupplierRelationshipEventType.OrderFulfilledClean:
                    state.ordersFulfilledClean++;
                    break;
                case SupplierRelationshipEventType.OrderFulfilledShort:
                    state.ordersFulfilledShort++;
                    break;
                case SupplierRelationshipEventType.OrderFailed:
                    state.ordersFailed++;
                    break;
                case SupplierRelationshipEventType.EmergencyOrderRequested:
                    state.emergencyOrdersRequested++;
                    break;
                case SupplierRelationshipEventType.EmergencyOrderFulfilled:
                    state.emergencyOrdersRequested++;
                    state.emergencyOrdersFulfilled++;
                    break;
                case SupplierRelationshipEventType.EmergencyOrderFailed:
                    state.emergencyOrdersRequested++;
                    state.emergencyOrdersFailed++;
                    break;
            }
        }

        private static void AddDistinct(List<SupplierRelationshipReasonCode> reasons, SupplierRelationshipReasonCode reason)
        {
            if (reason != SupplierRelationshipReasonCode.None && !reasons.Contains(reason))
            {
                reasons.Add(reason);
            }
        }
    }
}
