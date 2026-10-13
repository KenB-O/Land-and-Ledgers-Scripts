using System;
using System.Collections.Generic;

namespace LandLedgers.Animals
{
    /// <summary>
    /// Pass VII Workstream B: the single availability truth for an animal.
    /// HorseAuthority keeps evaluating suitability (species, training, health,
    /// tack, condition). This authority alone decides whether the animal is
    /// free. Backed by the Pass IV IUnifiedReservationAuthority under the
    /// "animal:" key space, so draft, mount, rental, and travel all contend
    /// on the same reservation record. One horse, one truth.
    /// </summary>
    public interface IAnimalAvailabilityAuthority
    {
        /// <summary>
        /// Returns null and sets reservationId on success; returns a refusal
        /// reason naming the conflict on failure.
        /// </summary>
        string TryReserve(
            string animalId, string useKind, string assignmentId,
            float fromGameSeconds, float toGameSeconds,
            out string reservationId);

        /// <summary>Releases a reservation by id. No-op when unknown (idempotent).</summary>
        void Release(string reservationId);

        /// <summary>Whether the animal is free at the given absolute game seconds.</summary>
        bool IsAvailable(string animalId, float atGameSeconds);
    }
}
