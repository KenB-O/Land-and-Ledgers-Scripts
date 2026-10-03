using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Economy.Retail
{
    /// <summary>
    /// T1C: why a purchase intent failed (Tech X §5.4). Every failed intent writes
    /// evidence — queues, stockouts and closures become diagnosable instead of silent.
    /// </summary>
    public enum LostSaleReason
    {
        Unspecified = 0,
        Closure = 1,        // merchant closed when the intent arrived
        Stockout = 2,       // wanted goods not in stock
        QueueBalk = 3,      // customer left the queue or refused to join it
        Affordability = 4,  // customer could not afford the price
        Distance = 5,       // no reachable merchant
        Rejection = 6,      // merchant refused (credit refused, etc.)
        NoFeasibleChoice = 7, // feasible set empty for other/compound reasons
    }

    /// <summary>
    /// T1C: one lost sale / abandoned intent as evidence (Tech X §5.4). Sequence is
    /// log-assigned; these are diagnostics, not entities, so no HF-1 id is minted.
    /// </summary>
    [Serializable]
    public sealed class LostSaleEvent
    {
        public int Sequence;
        public int DayIndex;
        public string MerchantBusinessId = string.Empty;
        public string MerchantName = string.Empty;
        public int ActingPersonId = -1;
        public string CategoryId = string.Empty;
        public int UnitsWanted;
        public int PriceCentsPerUnit;
        public LostSaleReason Reason = LostSaleReason.Unspecified;
        public string Detail = string.Empty;

        public LostSaleEvent() { }
    }

    /// <summary>
    /// T1C: the merchant diagnostics log (Tech X §5.4). Records lost-sale events and
    /// renders per-merchant summaries the owner can act on ("the queue is costing us
    /// sales" is now evidence, not a feeling).
    /// </summary>
    public sealed class LostSaleLog
    {
        private readonly List<LostSaleEvent> events = new List<LostSaleEvent>();
        private int nextSequence;

        public int Count => events.Count;

        public LostSaleEvent Record(
            int dayIndex,
            string merchantBusinessId,
            string merchantName,
            int actingPersonId,
            string categoryId,
            int unitsWanted,
            int priceCentsPerUnit,
            LostSaleReason reason,
            string detail)
        {
            var evt = new LostSaleEvent
            {
                Sequence = nextSequence++,
                DayIndex = dayIndex,
                MerchantBusinessId = merchantBusinessId ?? string.Empty,
                MerchantName = merchantName ?? string.Empty,
                ActingPersonId = actingPersonId,
                CategoryId = categoryId ?? string.Empty,
                UnitsWanted = Math.Max(0, unitsWanted),
                PriceCentsPerUnit = Math.Max(0, priceCentsPerUnit),
                Reason = reason,
                Detail = detail ?? string.Empty,
            };
            events.Add(evt);
            return evt;
        }

        public List<LostSaleEvent> EventsFor(string merchantBusinessId)
        {
            var results = new List<LostSaleEvent>();
            foreach (LostSaleEvent evt in events)
            {
                if (string.Equals(evt.MerchantBusinessId, merchantBusinessId, StringComparison.Ordinal))
                {
                    results.Add(evt);
                }
            }

            return results;
        }

        /// <summary>
        /// Human-readable merchant diagnostics: lost sales by reason with the revenue
        /// left on the table. Feeds O&P review (Tech X §5.4).
        /// </summary>
        public string BuildDiagnostics(string merchantBusinessId, string merchantName)
        {
            List<LostSaleEvent> mine = EventsFor(merchantBusinessId);
            if (mine.Count == 0)
            {
                return $"{merchantName}: no lost-sale events recorded.";
            }

            var byReason = new Dictionary<LostSaleReason, int>();
            var unitsByReason = new Dictionary<LostSaleReason, int>();
            long valueLeftCents = 0;
            foreach (LostSaleEvent evt in mine)
            {
                if (!byReason.ContainsKey(evt.Reason)) byReason[evt.Reason] = 0;
                if (!unitsByReason.ContainsKey(evt.Reason)) unitsByReason[evt.Reason] = 0;
                byReason[evt.Reason]++;
                unitsByReason[evt.Reason] += evt.UnitsWanted;
                valueLeftCents += (long)evt.UnitsWanted * evt.PriceCentsPerUnit;
            }

            var builder = new StringBuilder();
            builder.Append($"{merchantName}: {mine.Count} lost sales ({valueLeftCents}c of demand unmet) — ");
            bool first = true;
            foreach (var kvp in byReason)
            {
                if (!first) builder.Append("; ");
                first = false;
                builder.Append($"{kvp.Key}: {kvp.Value} ({unitsByReason[kvp.Key]}u)");
            }

            return builder.ToString();
        }
    }
}
