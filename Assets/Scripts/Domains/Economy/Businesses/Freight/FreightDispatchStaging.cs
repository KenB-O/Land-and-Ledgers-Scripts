// Slice S5 (2026-11-05): pure, harness-testable staging rules for the
// freight dispatch leg (Objective 19). The dispatch task authorizes a
// shipment, so it must start — and authorize — only with Ethan visually at
// staging (his home building, the code's existing staging definition).
// This helper owns the arrival-driven travel task's customer ref and the
// needs-travel decision; SimulationDrivers calls it rather than
// duplicating the ref string, so a typo breaks a harness test instead of
// silently creating an unmatchable task.
//
// No Unity types, no hub, no TaskAuthority — pure strings/bools, so it
// compiles in the Roslyn harness. SCOPE NOTE: SimulationDrivers.cs is
// harness-excluded, so the Ensure / starter / gate / handler wiring that
// calls this helper is source-reviewed only; what IS harness-tested here
// is the exact ref seam that wiring uses (round-trip, cross-talk
// rejection, decision table, save stability).
//
// Ref shape: "freight-dispatch:travel:<agreementId>:<shipmentId>".
// Production ids carry a namespace colon ("freight-agreement:request-1",
// "freight-shipment:request-1" — see FreightServiceAgreements, which
// builds agreement ids as "freight-agreement:" + RequestId), so the
// remainder after the prefix splits into four parts for the production
// form. The parser accepts that namespaced form and the simple two-part
// form (ids without colons), and rejects everything else — including the
// plain dispatch ref ("freight-dispatch:<agreement>:<shipment>") and the
// hitch travel ref ("development-hitch:travel:<shipment>") — so the three
// task kinds can never cross-talk through this classifier.
using System;

namespace LandLedgers.Economy.Freight
{
    public static class FreightDispatchStaging
    {
        public const string TravelRefPrefix = "freight-dispatch:travel:";

        private const string ShipmentIdMarker = ":freight-shipment:";

        /// <summary>Stable customer ref for the dispatch staging travel task.</summary>
        public static string TravelCustomerRef(string agreementId, string shipmentId)
        {
            return TravelRefPrefix + agreementId + ":" + shipmentId;
        }

        /// <summary>Parse-based classifier: true only for a dispatch travel ref.</summary>
        public static bool IsDispatchTravelRef(string customerRef)
        {
            return TryParseTravelRef(customerRef, out _, out _);
        }

        /// <summary>
        /// Exact inverse of TravelCustomerRef. False for any other ref,
        /// including the plain dispatch ref and the hitch travel ref.
        /// </summary>
        public static bool TryParseTravelRef(string customerRef, out string agreementId, out string shipmentId)
        {
            agreementId = string.Empty;
            shipmentId = string.Empty;
            if (string.IsNullOrEmpty(customerRef)
                || !customerRef.StartsWith(TravelRefPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string rest = customerRef.Substring(TravelRefPrefix.Length);
            if (string.IsNullOrEmpty(rest))
            {
                return false;
            }

            // Production form: the shipment id starts with its namespace
            // tag, so the separator is the colon immediately before it.
            // This accepts any agreement id (including namespaced ones and
            // request ids containing colons) without guessing a split.
            int marker = rest.IndexOf(ShipmentIdMarker, StringComparison.Ordinal);
            if (marker > 0)
            {
                agreementId = rest.Substring(0, marker);
                shipmentId = rest.Substring(marker + 1);
                return !string.IsNullOrEmpty(agreementId)
                    && shipmentId.StartsWith("freight-shipment:", StringComparison.Ordinal)
                    && shipmentId.Length > "freight-shipment:".Length;
            }

            // Simple form: ids without colons split into exactly two parts.
            // A bare namespace tag ("freight-agreement" / "freight-shipment")
            // is never a whole id — it is the first half of a namespaced id
            // whose second half is missing — so it is rejected here rather
            // than misread as an agreement or shipment id.
            string[] parts = rest.Split(':');
            if (parts.Length == 2
                && !string.IsNullOrEmpty(parts[0])
                && !string.IsNullOrEmpty(parts[1])
                && !IsNamespaceTag(parts[0])
                && !IsNamespaceTag(parts[1]))
            {
                agreementId = parts[0];
                shipmentId = parts[1];
                return true;
            }

            return false;
        }

        private static bool IsNamespaceTag(string value)
        {
            return string.Equals(value, "freight-agreement", StringComparison.Ordinal)
                || string.Equals(value, "freight-shipment", StringComparison.Ordinal);
        }

        /// <summary>
        /// Decision table for the start gate: travel is needed only when
        /// Ethan is not visually at staging and no travel task is already
        /// open. (At staging, any) -> false; (away, open travel) -> false.
        /// </summary>
        public static bool NeedsStagingTravel(bool isVisuallyAtStaging, bool hasOpenTravelTask)
        {
            return !isVisuallyAtStaging && !hasOpenTravelTask;
        }

        /// <summary>No home building means no staging exists to travel to.</summary>
        public static bool CanCreateTravel(int homeBuildingId)
        {
            return homeBuildingId >= 0;
        }
    }
}
