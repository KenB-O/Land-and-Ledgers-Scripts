// Campaign C (DEF-048), package 9: pure, zero-Unity-dependency wording
// for a refused physical freight origin arrival.
//
// What was verified (Nov 15, 2026, maintenance branch + this lane):
// when CoupledTransportMovementController reaches the town return point
// and SimulationDrivers.ConfirmPhysicalFreightOriginArrival refuses, the
// leg dies (legActive = false) and NOTHING re-drives it — the journey
// stays active/unsettled, the shipment stays live/unsettled, Ethan stays
// aboard, and the wagon sits at staging. The daily return driver
// (DrivePhysicalFreightReturns) retries only the return START, whose
// position gate (team within 8 m of the Millbrook destination) can never
// hold once the team is already home.
//
// The refusal itself is NOT transient-retryable, and no retry loop is
// added here — unlike the destination side, whose trigger event can still
// occur later, every origin pre-check cause is permanent once the return
// leg has started:
//  * destination confirmation has exactly one production caller, the
//    controller's OnRegionalDestinationTrigger, which cannot fire again
//    once the team is home;
//  * the confirmation flags are write-once (set true only, never reset);
//  * origin/destination/gateway ids are set at journey setup, never
//    changed afterwards;
//  * journey cancellation (OffMapJourneyAuthority.CancelJourney) is only
//    ever called from the departure path, before a leg exists.
// A tick/day retry of the confirmation would therefore re-fail forever.
// The disposition after a refusal — retry the leg, re-drive the return,
// or declare the haul failed with its cargo released — is A-to-Z Check 10
// question 5, an owner decision, and is NOT invented here.
//
// What IS fixed here is diagnostic, in two layers:
//  (1) The pre-check used to return false SILENTLY, so every refusal
//      looked identical in the Console and the cause (missing journey,
//      mismatched id, unconfirmed destination, ...) was invisible. This
//      class names the first failing check, in the same order the driver
//      method evaluates them; the driver logs it once, at the refusal.
//      The controller calls the confirmation once per arrival attempt,
//      so one line per refused arrival, never per tick. The
//      destination-side method is deliberately untouched: its retry loop
//      calls it every tick, so per-call logging there would spam.
//  (2) The controller records refused origin journey ids (runtime-only),
//      so the daily driver can tell a team that already came home from
//      one still waiting at the supplier to begin its return, and log a
//      distinct line instead of the misleading "waiting to begin" line.
//
// This class only names the reason; it changes no gate, schedule, price,
// or movement semantics, and it never decides the disposition.
using System;

namespace LandLedgers.Economy.Transport
{
    public static class OriginArrivalRefusalReason
    {
        /// <summary>Snapshot of one journey record's confirmation state,
        /// as read by the driver method. The traveler kind is carried as
        /// its enum name ("Vehicle" / "Person") so this file stays free of
        /// the journey-authority type and its Unity dependencies.</summary>
        public readonly struct JourneyState
        {
            public JourneyState(string journeyId, string travelerKind, int personId,
                bool requiresPhysicalArrivalConfirmation,
                bool physicalDestinationArrivalConfirmed,
                string originLocationId, string destinationLocationId,
                string gatewayLocationId)
            {
                JourneyId = journeyId;
                TravelerKind = travelerKind;
                PersonId = personId;
                RequiresPhysicalArrivalConfirmation = requiresPhysicalArrivalConfirmation;
                PhysicalDestinationArrivalConfirmed = physicalDestinationArrivalConfirmed;
                OriginLocationId = originLocationId;
                DestinationLocationId = destinationLocationId;
                GatewayLocationId = gatewayLocationId;
            }

            public string JourneyId { get; }
            public string TravelerKind { get; }
            public int PersonId { get; }
            public bool RequiresPhysicalArrivalConfirmation { get; }
            public bool PhysicalDestinationArrivalConfirmed { get; }
            public string OriginLocationId { get; }
            public string DestinationLocationId { get; }
            public string GatewayLocationId { get; }
        }

        /// <summary>Returns null when the origin arrival would be
        /// confirmed (every pre-check passes); otherwise a one-line reason
        /// naming the first failing check, in the same order
        /// SimulationDrivers.ConfirmPhysicalFreightOriginArrival evaluates
        /// them. Never returns an empty string.</summary>
        public static string Describe(string vehicleId, int operatorPersonId,
            string vehicleJourneyId, string originLocationId,
            JourneyState? vehicleJourney, JourneyState? personJourney)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return "[OffMapJourney] Physical freight return confirmation refused: no vehicle id was supplied.";
            if (operatorPersonId < 0)
                return "[OffMapJourney] Physical freight return confirmation refused: no operator person id was supplied.";
            if (string.IsNullOrWhiteSpace(vehicleJourneyId))
                return "[OffMapJourney] Physical freight return confirmation refused: no vehicle journey id was supplied.";
            if (string.IsNullOrWhiteSpace(originLocationId))
                return "[OffMapJourney] Physical freight return confirmation refused: no origin location id was supplied.";
            if (vehicleJourney == null)
                return "[OffMapJourney] Physical freight return confirmation refused: no active journey for vehicle '" + vehicleId + "'.";
            if (personJourney == null)
                return "[OffMapJourney] Physical freight return confirmation refused: no active journey for operator P" + operatorPersonId + ".";

            JourneyState vehicle = vehicleJourney.Value;
            JourneyState person = personJourney.Value;

            if (!string.Equals(vehicle.JourneyId, vehicleJourneyId, StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: the active vehicle journey '"
                    + vehicle.JourneyId + "' does not match the leg's journey '" + vehicleJourneyId + "'.";
            if (!string.Equals(vehicle.TravelerKind, "Vehicle", StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: journey '"
                    + vehicle.JourneyId + "' is a " + vehicle.TravelerKind + " journey, not a vehicle journey.";
            if (!string.Equals(person.TravelerKind, "Person", StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: journey '"
                    + person.JourneyId + "' is a " + person.TravelerKind + " journey, not a person journey.";
            if (person.PersonId != operatorPersonId)
                return "[OffMapJourney] Physical freight return confirmation refused: operator journey '"
                    + person.JourneyId + "' belongs to P" + person.PersonId + ", not P" + operatorPersonId + ".";
            if (!vehicle.RequiresPhysicalArrivalConfirmation)
                return "[OffMapJourney] Physical freight return confirmation refused: vehicle journey '"
                    + vehicle.JourneyId + "' is not configured for physical arrival confirmation.";
            if (!person.RequiresPhysicalArrivalConfirmation)
                return "[OffMapJourney] Physical freight return confirmation refused: operator journey '"
                    + person.JourneyId + "' is not configured for physical arrival confirmation.";
            if (!vehicle.PhysicalDestinationArrivalConfirmed)
                return "[OffMapJourney] Physical freight return confirmation refused: vehicle journey '"
                    + vehicle.JourneyId + "' has no confirmed destination arrival.";
            if (!person.PhysicalDestinationArrivalConfirmed)
                return "[OffMapJourney] Physical freight return confirmation refused: operator journey '"
                    + person.JourneyId + "' has no confirmed destination arrival.";
            if (!string.Equals(vehicle.OriginLocationId, originLocationId, StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: vehicle journey '"
                    + vehicle.JourneyId + "' origin '" + vehicle.OriginLocationId
                    + "' is not the leg's origin '" + originLocationId + "'.";
            if (!string.Equals(person.OriginLocationId, originLocationId, StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: operator journey '"
                    + person.JourneyId + "' origin '" + person.OriginLocationId
                    + "' is not the leg's origin '" + originLocationId + "'.";
            if (!string.Equals(vehicle.DestinationLocationId, person.DestinationLocationId, StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: vehicle journey '"
                    + vehicle.JourneyId + "' destination '" + vehicle.DestinationLocationId
                    + "' does not match operator journey '" + person.JourneyId
                    + "' destination '" + person.DestinationLocationId + "'.";
            if (!string.Equals(vehicle.GatewayLocationId, person.GatewayLocationId, StringComparison.Ordinal))
                return "[OffMapJourney] Physical freight return confirmation refused: vehicle journey '"
                    + vehicle.JourneyId + "' gateway '" + vehicle.GatewayLocationId
                    + "' does not match operator journey '" + person.JourneyId
                    + "' gateway '" + person.GatewayLocationId + "'.";
            return null;
        }
    }
}
