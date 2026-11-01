using System;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Reputation
{
    [Serializable]
    public struct ReputationEvent
    {
        public ReputationEventType eventType;
        public string sourceSystem;
        public string counterpartyId;
        public string businessId;
        public string assetId;
        public SimulationDate date;
        public int absoluteDayIndex;
        public float severity01;
        public float confidence01;
        public string note;

        public ReputationEvent Sanitized()
        {
            ReputationEvent sanitized = this;
            sanitized.sourceSystem ??= string.Empty;
            sanitized.counterpartyId ??= string.Empty;
            sanitized.businessId ??= string.Empty;
            sanitized.assetId ??= string.Empty;
            sanitized.severity01 = Mathf.Clamp01(severity01 <= 0f ? 1f : severity01);
            sanitized.confidence01 = Mathf.Clamp01(confidence01 <= 0f ? 1f : confidence01);
            sanitized.note ??= string.Empty;
            return sanitized;
        }
    }
}
