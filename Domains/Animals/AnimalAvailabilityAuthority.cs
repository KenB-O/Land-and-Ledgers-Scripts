using System;
using LandLedgers.Economy.Equipment;

namespace LandLedgers.Animals
{
    /// <summary>
    /// Pass VII Workstream B (P1-4): the IAnimalAvailabilityAuthority implementation.
    /// A thin gateway over the Pass IV IUnifiedReservationAuthority: every animal
    /// availability decision (draft, mount, rental, travel) contends on the same
    /// "animal:{id}" reservation record. This class adds no availability truth of
    /// its own — suitability (species, training, tack, health, condition, location)
    /// stays with HorseAuthority, which must never override a reservation.
    /// One horse, one identity, one availability truth.
    /// </summary>
    public sealed class AnimalAvailabilityAuthority : IAnimalAvailabilityAuthority
    {
        private readonly IUnifiedReservationAuthority inner;

        public AnimalAvailabilityAuthority(IUnifiedReservationAuthority inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        /// <summary>The underlying unified authority (single key space).</summary>
        public IUnifiedReservationAuthority Inner => inner;

        /// <inheritdoc />
        public string TryReserve(
            string animalId, string useKind, string assignmentId,
            float fromGameSeconds, float toGameSeconds,
            out string reservationId)
        {
            reservationId = null;
            if (string.IsNullOrWhiteSpace(animalId))
                return "AnimalAvailabilityAuthority: animal id is required.";
            if (string.IsNullOrWhiteSpace(assignmentId))
                return "AnimalAvailabilityAuthority: assignment id is required.";
            if (!(toGameSeconds > fromGameSeconds))
                return $"AnimalAvailabilityAuthority: the reservation window for '{animalId}' ends before it starts.";
            var request = new ReservationRequest(assignmentId, useKind ?? string.Empty);
            request.Items.Add(new ReservationItem(
                ReservationKeyConventions.Animal(animalId), 1f, fromGameSeconds, toGameSeconds));
            string refusal = inner.TryReserveAll(request, out ReservationHandle handle);
            if (refusal != null || handle == null)
                return refusal ?? $"AnimalAvailabilityAuthority: reservation for '{animalId}' failed.";
            reservationId = handle.ReservationId;
            return null;
        }

        /// <inheritdoc />
        public void Release(string reservationId)
        {
            inner.Release(reservationId);
        }

        /// <inheritdoc />
        public bool IsAvailable(string animalId, float atGameSeconds)
        {
            if (string.IsNullOrWhiteSpace(animalId)) return false;
            return !inner.IsReserved(ReservationKeyConventions.Animal(animalId), atGameSeconds);
        }
    }
}
