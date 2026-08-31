using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy
{
    public enum RelationshipTrendDirection
    {
        Stable = 0,
        Improving = 1,
        Declining = 2
    }

    public enum SupplierOriginKind
    {
        Internal = 0,
        LocalTown = 1,
        RegionalOffMap = 2
    }

    public enum SupplierRelationshipEventType
    {
        None = 0,
        InvoicePaidOnTime = 1,
        InvoicePaidLate = 2,
        InvoiceMissed = 3,
        OrderFulfilledClean = 20,
        OrderFulfilledShort = 21,
        OrderFailed = 22,
        EmergencyOrderRequested = 40,
        EmergencyOrderFulfilled = 41,
        EmergencyOrderFailed = 42,
        PreferredStatusGranted = 60,
        PreferredStatusLost = 61,
        CooldownWeekPassed = 80
    }

    public enum SupplierRelationshipReasonCode
    {
        None = 0,
        OnTimePayment = 1,
        LatePayment = 2,
        MissedPayment = 3,
        PaymentHistoryStrong = 4,
        PaymentHistoryWeak = 5,
        CleanFulfillment = 20,
        ShortFulfillment = 21,
        FailedFulfillment = 22,
        EmergencyOrderStrain = 40,
        EmergencyOrderRecovered = 41,
        PreferredStatusGranted = 60,
        PreferredStatusLost = 61,
        PreferredSupplier = 62,
        TermsReady = 80,
        TermsAtRisk = 81,
        ReliableSourcing = 100,
        UnreliableSourcing = 101,
        CooldownRecovery = 120
    }

    [Serializable]
    public sealed class SupplierRelationshipState
    {
        public string supplierId = string.Empty;
        public string displayName = string.Empty;
        public SupplierOriginKind originKind;

        [Range(0f, 1f)]
        public float trust01 = 0.5f;

        [Range(0f, 1f)]
        public float reliability01 = 0.5f;

        [Range(0f, 1f)]
        public float paymentHistory01 = 0.5f;

        [Range(0f, 1f)]
        public float emergencyOrderStrain01;

        public bool preferred;
        public int invoicesPaidOnTime;
        public int invoicesPaidLate;
        public int invoicesMissed;
        public int ordersFulfilledClean;
        public int ordersFulfilledShort;
        public int ordersFailed;
        public int emergencyOrdersRequested;
        public int emergencyOrdersFulfilled;
        public int emergencyOrdersFailed;
        public int lastInteractionDayIndex = -1;
        public RelationshipTrendDirection lastTrend;
        public List<SupplierRelationshipReasonCode> lastReasonCodes = new();

        public SupplierRelationshipState SanitizedCopy()
        {
            return new SupplierRelationshipState
            {
                supplierId = supplierId ?? string.Empty,
                displayName = displayName ?? string.Empty,
                originKind = originKind,
                trust01 = Mathf.Clamp01(trust01),
                reliability01 = Mathf.Clamp01(reliability01),
                paymentHistory01 = Mathf.Clamp01(paymentHistory01),
                emergencyOrderStrain01 = Mathf.Clamp01(emergencyOrderStrain01),
                preferred = preferred,
                invoicesPaidOnTime = Mathf.Max(0, invoicesPaidOnTime),
                invoicesPaidLate = Mathf.Max(0, invoicesPaidLate),
                invoicesMissed = Mathf.Max(0, invoicesMissed),
                ordersFulfilledClean = Mathf.Max(0, ordersFulfilledClean),
                ordersFulfilledShort = Mathf.Max(0, ordersFulfilledShort),
                ordersFailed = Mathf.Max(0, ordersFailed),
                emergencyOrdersRequested = Mathf.Max(0, emergencyOrdersRequested),
                emergencyOrdersFulfilled = Mathf.Max(0, emergencyOrdersFulfilled),
                emergencyOrdersFailed = Mathf.Max(0, emergencyOrdersFailed),
                lastInteractionDayIndex = lastInteractionDayIndex,
                lastTrend = lastTrend,
                lastReasonCodes = lastReasonCodes != null
                    ? new List<SupplierRelationshipReasonCode>(lastReasonCodes)
                    : new List<SupplierRelationshipReasonCode>()
            };
        }
    }

    [Serializable]
    public struct SupplierRelationshipEvent
    {
        public string supplierId;
        public SupplierRelationshipEventType eventType;
        public int absoluteDayIndex;
        public float severity01;
        public string note;

        public SupplierRelationshipEvent Sanitized()
        {
            SupplierRelationshipEvent sanitized = this;
            sanitized.supplierId ??= string.Empty;
            sanitized.absoluteDayIndex = Mathf.Max(-1, sanitized.absoluteDayIndex);
            sanitized.severity01 = Mathf.Clamp01(sanitized.severity01 <= 0f ? 1f : sanitized.severity01);
            sanitized.note ??= string.Empty;
            return sanitized;
        }
    }

    [Serializable]
    public readonly struct SupplierTrustChangeProposal
    {
        public SupplierTrustChangeProposal(
            string supplierId,
            SupplierRelationshipEventType eventType,
            float trustDelta01,
            float reliabilityDelta01,
            float paymentHistoryDelta01,
            float emergencyOrderStrainDelta01,
            float projectedTrust01,
            float projectedReliability01,
            float projectedPaymentHistory01,
            float projectedEmergencyOrderStrain01,
            bool preferred,
            RelationshipTrendDirection trend,
            IReadOnlyList<SupplierRelationshipReasonCode> reasonCodes)
        {
            SupplierId = supplierId ?? string.Empty;
            EventType = eventType;
            TrustDelta01 = Mathf.Clamp(trustDelta01, -1f, 1f);
            ReliabilityDelta01 = Mathf.Clamp(reliabilityDelta01, -1f, 1f);
            PaymentHistoryDelta01 = Mathf.Clamp(paymentHistoryDelta01, -1f, 1f);
            EmergencyOrderStrainDelta01 = Mathf.Clamp(emergencyOrderStrainDelta01, -1f, 1f);
            ProjectedTrust01 = Mathf.Clamp01(projectedTrust01);
            ProjectedReliability01 = Mathf.Clamp01(projectedReliability01);
            ProjectedPaymentHistory01 = Mathf.Clamp01(projectedPaymentHistory01);
            ProjectedEmergencyOrderStrain01 = Mathf.Clamp01(projectedEmergencyOrderStrain01);
            Preferred = preferred;
            Trend = trend;
            ReasonCodes = reasonCodes ?? Array.Empty<SupplierRelationshipReasonCode>();
        }

        public string SupplierId { get; }
        public SupplierRelationshipEventType EventType { get; }
        public float TrustDelta01 { get; }
        public float ReliabilityDelta01 { get; }
        public float PaymentHistoryDelta01 { get; }
        public float EmergencyOrderStrainDelta01 { get; }
        public float ProjectedTrust01 { get; }
        public float ProjectedReliability01 { get; }
        public float ProjectedPaymentHistory01 { get; }
        public float ProjectedEmergencyOrderStrain01 { get; }
        public bool Preferred { get; }
        public RelationshipTrendDirection Trend { get; }
        public IReadOnlyList<SupplierRelationshipReasonCode> ReasonCodes { get; }
    }

    [Serializable]
    public readonly struct SupplierTrustStatusSummary
    {
        public SupplierTrustStatusSummary(
            string supplierId,
            SupplierOriginKind originKind,
            float trust01,
            float reliability01,
            float paymentHistory01,
            float emergencyOrderStrain01,
            float sourcingReliability01,
            float termsReadiness01,
            bool preferred,
            bool preferredEligible,
            RelationshipTrendDirection trend,
            IReadOnlyList<SupplierRelationshipReasonCode> reasonCodes)
        {
            SupplierId = supplierId ?? string.Empty;
            OriginKind = originKind;
            Trust01 = Mathf.Clamp01(trust01);
            Reliability01 = Mathf.Clamp01(reliability01);
            PaymentHistory01 = Mathf.Clamp01(paymentHistory01);
            EmergencyOrderStrain01 = Mathf.Clamp01(emergencyOrderStrain01);
            SourcingReliability01 = Mathf.Clamp01(sourcingReliability01);
            TermsReadiness01 = Mathf.Clamp01(termsReadiness01);
            Preferred = preferred;
            PreferredEligible = preferredEligible;
            Trend = trend;
            ReasonCodes = reasonCodes ?? Array.Empty<SupplierRelationshipReasonCode>();
        }

        public string SupplierId { get; }
        public SupplierOriginKind OriginKind { get; }
        public float Trust01 { get; }
        public float Reliability01 { get; }
        public float PaymentHistory01 { get; }
        public float EmergencyOrderStrain01 { get; }
        public float SourcingReliability01 { get; }
        public float TermsReadiness01 { get; }
        public bool Preferred { get; }
        public bool PreferredEligible { get; }
        public RelationshipTrendDirection Trend { get; }
        public IReadOnlyList<SupplierRelationshipReasonCode> ReasonCodes { get; }
    }
}
