using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.DraftPower
{
    /// <summary>Statuses for a horse rental agreement (plain strings, no enum churn).</summary>
    public static class HorseRentalStatus
    {
        public const string Active = "active";
        public const string Returned = "returned";
        public const string TerminatedSale = "terminated-sale";
    }

    /// <summary>
    /// One horse rental agreement. Plain data; persisted by the owning service.
    /// Owner and custodian stay distinct for the whole term: ownership never
    /// moves, custody does.
    /// </summary>
    [Serializable]
    public sealed class HorseRentalRecord
    {
        public string RentalId = string.Empty;
        public string AnimalId = string.Empty;
        public string OwnerKind = string.Empty;
        public string OwnerId = string.Empty;
        public string BorrowerKind = string.Empty; // "person" | "household" | "business"
        public string BorrowerId = string.Empty;
        public string BorrowerName = string.Empty;
        public int FromDayIndex;
        public int ToDayIndex; // inclusive
        public int FeeCents;
        public string TermsNote = string.Empty;
        public string ReservationId = string.Empty;
        public float DeliveryHealth01 = 1f;
        public float DeliveryFatigue01;
        public float ReturnHealth01 = -1f; // -1 = not yet returned
        public float ReturnFatigue01 = -1f;
        public string DamageNote = string.Empty;
        public string Status = HorseRentalStatus.Active;
        public string EndedReason = string.Empty;
        public int ReturnedDayIndex = -1;
        public string FeeObligationId = string.Empty; // set when the fee went the credit route
    }

    [Serializable]
    public sealed class HorseRentalServiceSaveDto
    {
        public List<HorseRentalRecord> rentals = new List<HorseRentalRecord>();
    }

    /// <summary>
    /// The inputs for renting out a horse. The rental is authorized by the
    /// horse's current owner (checked against the registry); the borrower is a
    /// real person, household, or business identity supplied by the caller.
    /// </summary>
    public sealed class HorseRentalRequest
    {
        public AnimalRegistry Registry;
        public EntityId HorseId;
        public AnimalOwnerKind AuthorizingOwnerKind;
        public string AuthorizingOwnerId = string.Empty;
        public string BorrowerKind = string.Empty; // "person" | "household" | "business"
        public string BorrowerId = string.Empty;
        public string BorrowerName = string.Empty;
        public int FromDayIndex;
        public int ToDayIndex; // inclusive
        public int FeeCents;
        public string TermsNote = string.Empty;
        public IAnimalAvailabilityAuthority Availability;
        public float GameSecondsPerDay = 86400f;
        public int DayIndex;
        public HouseholdLedger BorrowerLedger; // cash route: both ledgers required when FeeCents > 0
        public HouseholdLedger OwnerLedger; // cash route
        public FinancialObligationAuthority Obligations; // credit route
        public EntityIdRegistry Ids; // credit route
    }

    /// <summary>
    /// Pass VII Workstream B (P2-14): the horse rental path, built on existing
    /// service and contract authorities only — no new registries, no second
    /// reservation system, no invented law.
    ///
    /// Owner, borrower, and animal identity come from the AnimalRegistry. The
    /// rental is authorized by the current owner. The period is a real
    /// reservation in the animal availability authority, so a rented horse
    /// refuses conflicting owner use and a committed horse refuses rental.
    /// Custody moves to the borrower (HorseAuthority.SetCustody) while
    /// ownership stays put. The fee moves borrower-to-owner through household
    /// ledgers (cash route, atomic) or becomes a Payable financial obligation
    /// (credit route). Delivery and return snapshot condition; a decline in the
    /// borrower's care is recorded with the agreed terms and left as an open
    /// hold — the service measures and records, it does not adjudicate
    /// liability (no invented historical law).
    /// </summary>
    public sealed class HorseRentalService
    {
        private readonly List<HorseRentalRecord> rentals = new List<HorseRentalRecord>();
        private int sequence;

        public IReadOnlyList<HorseRentalRecord> Rentals => rentals;

        public HorseRentalRecord ActiveRentalFor(EntityId horseId)
        {
            if (!horseId.IsValid) return null;
            string key = horseId.ToString();
            foreach (HorseRentalRecord rental in rentals)
            {
                if (rental != null && rental.Status == HorseRentalStatus.Active
                    && string.Equals(rental.AnimalId, key, StringComparison.Ordinal))
                    return rental;
            }
            return null;
        }

        /// <summary>
        /// Rents out a horse for [FromDayIndex, ToDayIndex]. Returns the record
        /// on success, null with diagnostics on refusal. A refusal reserves
        /// nothing, moves no custody, and moves no money.
        /// </summary>
        public HorseRentalRecord RentOut(HorseRentalRequest request, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (request == null)
            {
                diagnostics.Add("HorseRentalService: no rental request supplied.");
                return null;
            }
            AnimalRegistry registry = request.Registry;
            if (registry == null)
            {
                diagnostics.Add("HorseRentalService: no animal registry — the horse has no identity.");
                return null;
            }
            AnimalState horse = registry.GetAnimal(request.HorseId);
            if (horse == null)
            {
                diagnostics.Add($"HorseRentalService: animal {request.HorseId} not found in the registry.");
                return null;
            }
            if (!horse.IsActive || horse.Species != AnimalSpecies.Horse)
            {
                diagnostics.Add($"HorseRentalService: animal {request.HorseId} is not an active horse — it cannot be rented.");
                return null;
            }
            // Authorization: the rental must be initiated by the current owner.
            if (horse.OwnerKind != request.AuthorizingOwnerKind
                || !string.Equals(horse.OwnerId ?? string.Empty, request.AuthorizingOwnerId ?? string.Empty, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    $"HorseRentalService: rental of {request.HorseId} is not authorized by its owner " +
                    $"({horse.OwnerKind}:{horse.OwnerId}) — authorization refused.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(request.BorrowerKind)
                || !(request.BorrowerKind == "person" || request.BorrowerKind == "household" || request.BorrowerKind == "business"))
            {
                diagnostics.Add("HorseRentalService: borrower kind must be person, household, or business — no anonymous borrowers.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(request.BorrowerId))
            {
                diagnostics.Add("HorseRentalService: borrower id is required.");
                return null;
            }
            if (request.ToDayIndex < request.FromDayIndex)
            {
                diagnostics.Add("HorseRentalService: the rental period ends before it starts.");
                return null;
            }
            if (request.FromDayIndex < request.DayIndex)
            {
                diagnostics.Add("HorseRentalService: rentals cannot start in the past.");
                return null;
            }
            if (request.FeeCents < 0)
            {
                diagnostics.Add("HorseRentalService: the fee cannot be negative.");
                return null;
            }
            if (request.Availability == null)
            {
                diagnostics.Add("HorseRentalService: no availability authority — the rental cannot hold the horse.");
                return null;
            }
            if (!(request.GameSecondsPerDay > 0f))
            {
                diagnostics.Add("HorseRentalService: game seconds per day must be positive.");
                return null;
            }
            // A horse handed over unfit is a dishonest rental: the borrower is
            // promised a working animal.
            if (!HorseAuthority.IsWorkReady(horse))
            {
                diagnostics.Add(
                    $"HorseRentalService: horse {request.HorseId} is not fit for work — it cannot be rented out in this condition.");
                return null;
            }
            if (ActiveRentalFor(request.HorseId) != null)
            {
                diagnostics.Add($"HorseRentalService: horse {request.HorseId} is already rented out — no double-booking.");
                return null;
            }

            sequence++;
            string rentalId = $"HR-{request.DayIndex}-{sequence}";
            float gsPerDay = request.GameSecondsPerDay;
            string refusal = request.Availability.TryReserve(
                request.HorseId.ToString(), "rental", rentalId,
                request.FromDayIndex * gsPerDay, (request.ToDayIndex + 1) * gsPerDay,
                out string reservationId);
            if (refusal != null)
            {
                diagnostics.Add($"HorseRentalService: rental refused — {refusal}");
                return null;
            }

            // Custody moves to the borrower; ownership stays with the owner.
            string custodyProblem = HorseAuthority.SetCustody(horse, request.BorrowerKind, request.BorrowerId);
            if (custodyProblem != null)
            {
                request.Availability.Release(reservationId);
                diagnostics.Add($"HorseRentalService: rental refused — {custodyProblem}");
                return null;
            }

            // Payment: cash route (atomic ledger transfer) or credit route
            // (a Payable obligation). No phantom money either way.
            string feeObligationId = string.Empty;
            if (request.FeeCents > 0)
            {
                string payProblem = SettleFee(request, rentalId, horse, diagnostics, out feeObligationId);
                if (payProblem != null)
                {
                    HorseAuthority.SetCustody(horse, horse.OwnerKind.ToString().ToLowerInvariant(), horse.OwnerId);
                    request.Availability.Release(reservationId);
                    diagnostics.Add($"HorseRentalService: rental refused — {payProblem}");
                    return null;
                }
            }
            else
            {
                diagnostics.Add($"HorseRentalService: {rentalId} is a free loan — no fee.");
            }

            var record = new HorseRentalRecord
            {
                RentalId = rentalId,
                AnimalId = request.HorseId.ToString(),
                OwnerKind = horse.OwnerKind.ToString(),
                OwnerId = horse.OwnerId ?? string.Empty,
                BorrowerKind = request.BorrowerKind,
                BorrowerId = request.BorrowerId,
                BorrowerName = request.BorrowerName ?? string.Empty,
                FromDayIndex = request.FromDayIndex,
                ToDayIndex = request.ToDayIndex,
                FeeCents = Math.Max(0, request.FeeCents),
                TermsNote = request.TermsNote ?? string.Empty,
                ReservationId = reservationId ?? string.Empty,
                DeliveryHealth01 = horse.Health01,
                DeliveryFatigue01 = horse.Fatigue01,
                FeeObligationId = feeObligationId,
            };
            rentals.Add(record);
            diagnostics.Add(
                $"HorseRentalService: horse {record.AnimalId} rented to {record.BorrowerName} " +
                $"({record.BorrowerKind}:{record.BorrowerId}) for days {record.FromDayIndex}-{record.ToDayIndex}, " +
                $"fee {record.FeeCents}c. Owner {record.OwnerKind}:{record.OwnerId} keeps ownership; " +
                $"custody is now the borrower's.");
            return record;
        }

        private static string SettleFee(
            HorseRentalRequest request, string rentalId, AnimalState horse,
            List<string> diagnostics, out string feeObligationId)
        {
            feeObligationId = string.Empty;
            string horseKey = request.HorseId.ToString();
            if (request.BorrowerLedger != null && request.OwnerLedger != null)
            {
                string problem = request.BorrowerLedger.TryTransferTo(
                    request.OwnerLedger, request.DayIndex, request.FeeCents,
                    $"Horse rental {rentalId}: {horseKey} to {request.BorrowerName ?? request.BorrowerId} (days {request.FromDayIndex}-{request.ToDayIndex})",
                    HouseholdIncomeSource.ServiceIncome,
                    horseKey,
                    $"horse rental fee for {horseKey} ({rentalId})",
                    $"{horse.OwnerKind}:{horse.OwnerId}");
                if (problem != null) return problem;
                diagnostics.Add($"HorseRentalService: fee {request.FeeCents}c moved borrower -> owner (cash).");
                return null;
            }
            if (request.Obligations != null && request.Ids != null)
            {
                FinancialObligation obligation = request.Obligations.Create(
                    request.Ids, FinancialObligationKind.Payable,
                    $"{request.BorrowerKind}:{request.BorrowerId}",
                    $"{horse.OwnerKind}:{horse.OwnerId}",
                    request.FeeCents, request.DayIndex,
                    request.TermsNote ?? string.Empty,
                    $"Horse rental fee {rentalId} for {horseKey}",
                    rentalId,
                    maturityDayIndex: request.ToDayIndex);
                if (obligation == null)
                    return "the fee obligation could not be recorded — no credit without a recorded obligation.";
                feeObligationId = obligation.ObligationId;
                diagnostics.Add(
                    $"HorseRentalService: fee {request.FeeCents}c recorded as Payable obligation {feeObligationId} " +
                    $"(due day {request.ToDayIndex}) — credit, not cash.");
                return null;
            }
            return "the fee needs a payment path: borrower + owner ledgers (cash) or an obligation authority (credit).";
        }

        /// <summary>
        /// Returns a rented horse: releases the availability hold, restores
        /// custody to the owner, and snapshots condition. A decline in the
        /// borrower's care is measured against the delivery snapshot and the
        /// agreed terms, then recorded as an open hold — the service does not
        /// adjudicate liability.
        /// </summary>
        public string ReturnHorse(
            AnimalRegistry registry, EntityId horseId, int dayIndex,
            IAnimalAvailabilityAuthority availability, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            HorseRentalRecord record = ActiveRentalFor(horseId);
            if (record == null)
                return $"HorseRentalService: horse {horseId} has no active rental to return.";
            AnimalState horse = registry != null ? registry.GetAnimal(horseId) : null;
            if (horse == null)
                return $"HorseRentalService: horse {horseId} not found in the registry.";
            if (availability == null)
                return "HorseRentalService: no availability authority — the rental hold cannot be released.";

            record.ReturnHealth01 = horse.Health01;
            record.ReturnFatigue01 = horse.Fatigue01;
            float healthDecline = record.DeliveryHealth01 - horse.Health01;
            if (healthDecline > 0.05f)
            {
                record.DamageNote =
                    $"Condition declined in the borrower's care: health {record.DeliveryHealth01:0.00} -> {horse.Health01:0.00} " +
                    $"(days {record.FromDayIndex}-{record.ToDayIndex}). Agreed terms: '{record.TermsNote}'. " +
                    "Liability for the decline is an open hold between owner and borrower — recorded, not adjudicated.";
                diagnostics.Add($"HorseRentalService: {record.DamageNote}");
            }
            else
            {
                diagnostics.Add(
                    $"HorseRentalService: horse {horseId} returned in comparable condition " +
                    $"(health {record.DeliveryHealth01:0.00} -> {horse.Health01:0.00}).");
            }

            availability.Release(record.ReservationId);
            record.ReservationId = string.Empty;
            string custodyProblem = HorseAuthority.SetCustody(horse, record.OwnerKind.ToLowerInvariant(), record.OwnerId);
            if (custodyProblem != null)
                diagnostics.Add($"HorseRentalService: custody restore noted a problem — {custodyProblem}");
            record.Status = HorseRentalStatus.Returned;
            record.ReturnedDayIndex = dayIndex;
            record.EndedReason = $"returned by {record.BorrowerName} on day {dayIndex}";
            diagnostics.Add($"HorseRentalService: rental {record.RentalId} closed — custody restored to {record.OwnerKind}:{record.OwnerId}.");
            return null;
        }

        /// <summary>
        /// Ends the active rental because the horse was sold. The renter's
        /// remaining term is cut short — a real consequence, recorded loudly.
        /// Any prepaid-fee adjustment is an open hold between the former owner
        /// and the renter; custody passes to the buyer via the sale itself.
        /// </summary>
        public void TerminateForSale(EntityId horseId, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            HorseRentalRecord record = ActiveRentalFor(horseId);
            if (record == null) return;
            record.Status = HorseRentalStatus.TerminatedSale;
            record.ReturnedDayIndex = dayIndex;
            record.EndedReason = $"terminated by sale on day {dayIndex} — the renter's term ended early";
            record.DamageNote = string.IsNullOrWhiteSpace(record.DamageNote)
                ? "Open hold: the renter prepaid part of the term; any fee adjustment is between the former owner and the renter."
                : record.DamageNote + " Open hold: the renter prepaid part of the term; any fee adjustment is between the former owner and the renter.";
            diagnostics.Add(
                $"HorseRentalService: rental {record.RentalId} terminated by sale of {record.AnimalId} on day {dayIndex} — " +
                $"renter {record.BorrowerName} loses the remaining term (days {dayIndex}-{record.ToDayIndex}).");
        }

        // ---------- save/load (DTO lives on the owning service) ----------

        public HorseRentalServiceSaveDto CaptureSaveDto()
        {
            var dto = new HorseRentalServiceSaveDto();
            foreach (HorseRentalRecord record in rentals)
            {
                if (record == null) continue;
                dto.rentals.Add(new HorseRentalRecord
                {
                    RentalId = record.RentalId,
                    AnimalId = record.AnimalId,
                    OwnerKind = record.OwnerKind,
                    OwnerId = record.OwnerId,
                    BorrowerKind = record.BorrowerKind,
                    BorrowerId = record.BorrowerId,
                    BorrowerName = record.BorrowerName,
                    FromDayIndex = record.FromDayIndex,
                    ToDayIndex = record.ToDayIndex,
                    FeeCents = record.FeeCents,
                    TermsNote = record.TermsNote,
                    ReservationId = record.ReservationId,
                    DeliveryHealth01 = record.DeliveryHealth01,
                    DeliveryFatigue01 = record.DeliveryFatigue01,
                    ReturnHealth01 = record.ReturnHealth01,
                    ReturnFatigue01 = record.ReturnFatigue01,
                    DamageNote = record.DamageNote,
                    Status = record.Status,
                    EndedReason = record.EndedReason,
                    ReturnedDayIndex = record.ReturnedDayIndex,
                    FeeObligationId = record.FeeObligationId,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(HorseRentalServiceSaveDto dto)
        {
            rentals.Clear();
            sequence = 0;
            if (dto?.rentals == null) return;
            foreach (HorseRentalRecord record in dto.rentals)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.RentalId)) continue;
                bool duplicate = false;
                foreach (HorseRentalRecord existing in rentals)
                {
                    if (existing != null && string.Equals(existing.RentalId, record.RentalId, StringComparison.Ordinal))
                    { duplicate = true; break; }
                }
                if (duplicate) continue;
                rentals.Add(record);
            }
        }
    }
}
