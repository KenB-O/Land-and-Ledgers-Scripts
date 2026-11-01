# Pass VII Workstream B: Animal Resource Authority

## What already existed

Two disconnected availability systems for the same physical animals. `HorseAuthority` (Domains/Animals/) maintained mount and use reservations through its own static state. `DraftPowerService` (Domains/Economy/DraftPower/) maintained draft-animal reservations through the Pass IV unified reservation authority. Neither consulted the other. Pass VI (Document D flagship, P1-4) confirmed a horse could be double-booked for incompatible uses.

Horse condition existed as data (`HorseFoundation`, `HorseCareDriver` computing a water01 care scalar) but had no production lifecycle: `IsDraftCapable` ignored condition, and working a horse accrued no condition cost.

## What was disconnected

The mount path and the draft path contended on different records. A horse plowing a field could simultaneously be reserved for a town journey. Condition was decorative. Rental had no production path. Sale did not clear reservations.

## What was changed

Unified availability (worker 2, merge 414384b):
- `IAnimalAvailabilityAuthority` is now the single availability truth, implemented as `AnimalAvailabilityAuthority`, a thin gateway over the Pass IV `IUnifiedReservationAuthority`.
- `HorseAuthority.TryReserveUse` now reserves through the shared authority. `DraftPowerService` contends on the same `"animal:"` reservation record. The mount path and the draft path can no longer double-book the same animal for incompatible time windows.
- Compatible time windows work. Cancellation releases correctly. Save/load preserves reservations and condition.

Condition lifecycle:
- Working a horse accrues real condition cost (`WorkPlowSession_AccruesRealConditionCost`).
- Care recovers condition through the existing `HorseAuthority.ApplyCare` path.
- Injury or insufficient condition blocks incompatible work. No free replacement animal appears.
- `IsDraftCapable` now respects condition.

Rental and sale:
- `HorseRental.cs` (Domains/Economy/DraftPower/) implements the rental path: owner authorization, animal identity, period, price, custody transfer, reservation, return, condition recording, payment.
- A rental prevents conflicting owner use for the rental period. Return records damage and notifies the owner.
- Sale terminates active rental loudly (no silent continuation, no phantom capacity).
- Owner and custodian remain distinct throughout.

Multi-use operations:
- One horse supports field work, town travel, commercial deliveries, and temporary rental income through the same identity and reservation record.
- Renting creates real opportunity cost: the owner loses compatible access for the period.
- `HorseFeedStockAdapter` (Domains/Economy/Farming/Integration/) connects feed stock to the animal authority.

## Why the correction follows Canon

Canon requires one persistent real animal with one identity, one location, one condition, and one schedule. The unification preserves this by routing all availability decisions through a single reservation record. No FarmHorse, TravelHorse, or RentalHorse duplicates were created.

## Authoritative systems reused

Pass IV `IUnifiedReservationAuthority` and `EquipmentReservationService`. The animal systems evaluate suitability and condition; they do not independently decide availability. No new animal registry, no new reservation system.

## Source entry points

- `IAnimalAvailabilityAuthority`: the single availability gateway.
- `HorseAuthority.TryReserveUse(horse, useKind, purpose, availability, start, end, out reservationId)`: unified reservation.
- `DraftPowerService`: draft reservations through the same record.
- `HorseRental`: rental lifecycle (authorize, reserve, transfer custody, return, settle).
- `HorseFeedStockAdapter`: feed consumption linkage.

## Tests that prove it

- Pass7AnimalAvailabilityTests (25 passed): DraftThenMount and MountThenDraft refusal, SliceC_SharedHorse_CompetingTasks_RentalChangesCustody, SliceD_ImpairedHorse_CommitmentsRespond_NoFreeReplacement, SliceD_InjuredRentedHorse_ReturnRecordsDamage_OwnerToldLoudly, SaveLoad_PreservesReservationAndCondition, Sale_TerminatesActiveRental_Loudly, WorkPlowSession_AccruesRealConditionCost.
- Updated existing tests (TransportFoundationTests, Pass6ProofTests) now assert the unified behavior with strengthened assertions.

## What remains source-only

The unification is proven in the harness. Ordinary Main Scene activation (live daily horse scheduling through the unified authority in normal gameplay) is a Codex integration item. The existing `HorseCareDriver` water01 scalar remains a care-quality input, not a physical water system (see water design).

## What Codex must connect in Unity

- Route live draft and mount scheduling through `IAnimalAvailabilityAuthority` in the scene drivers.
- Validate in PlayMode that a horse committed to plowing cannot be assigned to a journey in the same window.
- Connect the rental UI to `HorseRental` lifecycle methods.
