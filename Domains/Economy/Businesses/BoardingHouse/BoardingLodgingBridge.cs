using System;
using System.Collections.Generic;
using LandLedgers.World.Property;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// Pass VIII Worker 2: the lodging side of a boarding business as the
    /// housing bridge sees it. The production
    /// <see cref="BoardingHouseShopRuntime"/> implements this; tests fake
    /// it with the real room inventory, boarder register and rate
    /// schedule. Keeps the bridge decoupled from the shop runtime's
    /// kitchen/shop machinery (the IBoarderCheckInPort precedent).
    /// </summary>
    public interface IBoardingHouseLodgingSide
    {
        string BusinessInstanceId { get; }
        string BusinessName { get; }
        BoardingRoomInventory RoomInventory { get; }
        BoardingHouseBoarderRegister BoarderRegister { get; }
        BoardingRateSchedule RateSchedule { get; }
    }

    /// <summary>
    /// Pass VIII Worker 2: the live connection between a boarding business
    /// and the housing authority. A boarding house's rooms are real beds in
    /// its own inventory AND real accommodation spaces in the
    /// HousingAuthority — the bridge keeps the two in lockstep:
    ///
    /// - RegisterHouse: every physical room becomes an AccommodationSpace
    ///   (capacity = bed count) plus a LodgingRegistration. Until a room
    ///   is registered, its beds cannot be sold.
    /// - CheckIn: one bed assignment + one boarder agreement + one housing
    ///   occupancy under a recorded boarding agreement. Bed-level capacity
    ///   (no double-booking, turnover before re-letting) and space-level
    ///   capacity (no renting beyond beds) both bite; any failure rolls
    ///   back the bed so no phantom stay survives.
    /// - CheckOut: the bed is freed and soiled, the housing occupancy ends
    ///   (departure frees the space), and the boarding agreement closes.
    /// - CloseHouse: every boarder is checked out and every registration
    ///   ended — closure never strands an occupant.
    ///
    /// Money boundary: boarding rent keeps flowing through
    /// RentCollectionService (ledger to business cash, arrears as real
    /// obligations). The housing-side boarding agreement carries NO
    /// payment terms, so the tenancy payment loop never double-charges a
    /// boarder. The agreement is the accommodation-rights record, not the
    /// bill.
    /// </summary>
    public sealed class BoardingLodgingBridge
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Registers every room of the house's inventory as real
        /// accommodation. Idempotent per room: already-registered rooms are
        /// skipped. Returns null on success, the first refusal otherwise.
        /// </summary>
        public string RegisterHouse(
            IBoardingHouseLodgingSide house,
            string parcelId,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (house == null)
                return "BoardingLodgingBridge.RegisterHouse: no boarding house.";
            if (housing == null || registry == null)
                return "BoardingLodgingBridge.RegisterHouse: housing authority and lodging registry are required.";
            if (string.IsNullOrWhiteSpace(parcelId))
                return "BoardingLodgingBridge.RegisterHouse: the house stands on a real parcel — parcel id required.";

            string businessId = house.BusinessInstanceId;
            string buildingId = "bld-boarding-" + businessId;
            if (housing.FindBuilding(buildingId) == null)
            {
                Building building = housing.RegisterBuilding(
                    buildingId, parcelId, "boarding-house", dayIndex,
                    $"boarding lodging registration for '{businessId}'", diag);
                if (building == null)
                    return $"BoardingLodgingBridge.RegisterHouse: building '{buildingId}' could not be registered.";
            }

            foreach (BoardingRoom room in house.RoomInventory.Rooms)
            {
                if (room == null) continue;
                if (registry.FindActiveRegistration(businessId, room.RoomId) != null)
                {
                    diag.Add($"BoardingLodgingBridge: room '{room.RoomId}' already registered — skipped.");
                    continue;
                }
                AccommodationSpace space = housing.DefineSpace(buildingId, room.BedCount, diag);
                if (space == null)
                    return $"BoardingLodgingBridge.RegisterHouse: space for room '{room.RoomId}' could not be defined.";
                string refusal = registry.RegisterLodging(
                    businessId, room.RoomId, room.BedCount, space.SpaceId,
                    house.RateSchedule.TransientNightlyRateCents(false),
                    house.RateSchedule.WeeklyRateCents(room.RoomType, false),
                    dayIndex, housing, diag);
                if (refusal != null) return refusal;
            }
            diag.Add($"BoardingLodgingBridge: '{businessId}' lodging registered on building '{buildingId}' (parcel '{parcelId}').");
            return null;
        }

        /// <summary>
        /// Checks a real person into a registered room: bed, boarder
        /// agreement, housing agreement, housing occupancy. Returns null on
        /// success, the refusal otherwise (with the bed rolled back).
        /// </summary>
        public string CheckIn(
            IBoardingHouseLodgingSide house,
            int personId, int householdId,
            string roomId, int bedIndex,
            BoarderStayKind stayKind, bool boardIncluded,
            int transientNights,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag,
            string reservationId = null)
        {
            diag = diag ?? diagnostics;
            if (house == null || housing == null || registry == null)
                return "BoardingLodgingBridge.CheckIn: house, housing authority and lodging registry are required.";
            LodgingRegistration registration = registry.FindActiveRegistration(house.BusinessInstanceId, roomId);
            if (registration == null)
                return $"BoardingLodgingBridge.CheckIn: room '{roomId}' is not registered as lodging for '{house.BusinessInstanceId}' — " +
                       "a business cannot sell beds it never registered.";
            BoardingRoom room = house.RoomInventory.FindRoom(roomId);
            if (room == null)
                return $"BoardingLodgingBridge.CheckIn: room '{roomId}' is registered but missing from the house inventory — registration drift, refusing.";

            int weeklyRate = house.RateSchedule.WeeklyRateCents(room.RoomType, boardIncluded);
            int nightlyRate = house.RateSchedule.TransientNightlyRateCents(boardIncluded);
            int monthlyRate = house.RateSchedule.MonthlyRateCents(room.RoomType, boardIncluded);

            // Bed + boarder agreement (the register assigns the bed itself).
            string refusal = house.BoarderRegister.CheckIn(
                personId, roomId, bedIndex, stayKind, boardIncluded,
                weeklyRate, nightlyRate, dayIndex, transientNights,
                house.RoomInventory, diag, monthlyRate, reservationId);
            if (refusal != null) return refusal;

            // Housing-side rights record: the boarding agreement (no
            // payment terms — rent flows through RentCollectionService).
            string package = boardIncluded ? "room and board" : "room only";
            PropertyAgreement agreement = housing.RecordAgreement(
                "boarding", house.BusinessName, $"P{personId}",
                registration.SpaceId,
                $"{stayKind} {package}, bed {bedIndex} '{roomId}' — rate locked at check-in; rent via the boarding rent collection",
                dayIndex, -1, string.Empty, diag);
            if (agreement == null)
            {
                house.BoarderRegister.CheckOut(personId, house.RoomInventory, diag, soilBed: false);
                return "BoardingLodgingBridge.CheckIn: the boarding agreement could not be recorded — bed rolled back, no phantom stay.";
            }

            refusal = housing.TryOccupyUnderAgreement(
                personId, householdId, registration.SpaceId,
                AccommodationArrangement.BoardingOrLodging, dayIndex,
                agreement.AgreementId, diag, out _);
            if (refusal != null)
            {
                housing.EndAgreement(agreement.AgreementId, dayIndex, endLinkedOccupancies: false,
                    "boarding check-in rolled back", diag);
                house.BoarderRegister.CheckOut(personId, house.RoomInventory, diag, soilBed: false);
                return $"BoardingLodgingBridge.CheckIn: housing occupancy refused — {refusal} Bed rolled back, no phantom stay.";
            }

            diag.Add($"BoardingLodgingBridge: P{personId} (H{householdId}) checked into '{house.BusinessInstanceId}' " +
                     $"bed {bedIndex} '{roomId}' ({stayKind}, {package}) — bed, boarder agreement '{agreement.AgreementId}' and housing occupancy all recorded.");
            return null;
        }

        /// <summary>
        /// Checks a boarder out: the bed is freed (and soiled for turnover),
        /// the housing occupancy ends (departure frees the space), and the
        /// boarding agreement closes. Returns null on success, the refusal
        /// otherwise.
        /// </summary>
        public string CheckOut(
            IBoardingHouseLodgingSide house,
            int personId,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (house == null || housing == null)
                return "BoardingLodgingBridge.CheckOut: house and housing authority are required.";
            BoarderRecord record = house.BoarderRegister.FindRecord(personId);
            if (record == null)
                return $"BoardingLodgingBridge.CheckOut: person {personId} holds no boarding agreement here.";

            var agreementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ResidentialOccupancy occ in housing.CurrentOccupanciesForPerson(personId))
            {
                if (occ != null && !string.IsNullOrWhiteSpace(occ.AgreementId))
                    agreementIds.Add(occ.AgreementId);
            }

            string refusal = house.BoarderRegister.CheckOut(personId, house.RoomInventory, diag);
            if (refusal != null) return refusal;

            housing.EndOccupanciesForPerson(personId, dayIndex, diag);
            foreach (string agreementId in agreementIds)
            {
                housing.EndAgreement(agreementId, dayIndex, endLinkedOccupancies: false,
                    $"boarder P{personId} checked out", diag);
            }
            diag.Add($"BoardingLodgingBridge: P{personId} checked out of '{house.BusinessInstanceId}' — bed freed, housing occupancy ended, {agreementIds.Count} agreement(s) closed.");
            return null;
        }

        /// <summary>
        /// The absence-sweep hook: vacates one person from this house when
        /// the sweep resolves them (bed freed, occupancy ended). People who
        /// never boarded here just get their housing occupancy ended.
        /// </summary>
        public void VacatePersonFromHouse(
            IBoardingHouseLodgingSide house,
            int personId,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (house == null || housing == null) return;
            if (house.BoarderRegister.FindRecord(personId) != null)
            {
                string refusal = CheckOut(house, personId, dayIndex, housing, registry, diag);
                if (refusal != null)
                    diag.Add($"BoardingLodgingBridge.VacatePersonFromHouse: {refusal}");
            }
            else
            {
                housing.EndOccupanciesForPerson(personId, dayIndex, diag);
            }
        }

        /// <summary>
        /// Closes the house: every boarder is checked out (beds freed,
        /// occupancies ended, agreements closed) and every lodging
        /// registration ends. Closure never strands an occupant.
        /// </summary>
        public string CloseHouse(
            IBoardingHouseLodgingSide house,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (house == null || housing == null || registry == null)
                return "BoardingLodgingBridge.CloseHouse: house, housing authority and lodging registry are required.";
            var boarderIds = new List<int>();
            foreach (BoarderRecord record in house.BoarderRegister.Boarders)
            {
                if (record != null && record.PersonId > 0) boarderIds.Add(record.PersonId);
            }
            string firstRefusal = null;
            foreach (int personId in boarderIds)
            {
                string refusal = CheckOut(house, personId, dayIndex, housing, registry, diag);
                if (refusal != null && firstRefusal == null) firstRefusal = refusal;
            }
            foreach (LodgingRegistration registration in registry.ActiveRegistrationsForBusiness(house.BusinessInstanceId))
            {
                if (registration == null) continue;
                string refusal = registry.EndRegistration(registration.RegistrationId, dayIndex, $"boarding house '{house.BusinessInstanceId}' closed", diag);
                if (refusal != null && firstRefusal == null) firstRefusal = refusal;
            }
            diag.Add($"BoardingLodgingBridge: '{house.BusinessInstanceId}' closed — {boarderIds.Count} boarder(s) checked out, registrations ended.");
            return firstRefusal;
        }

        /// <summary>
        /// Resizes a room's real bed count in lockstep: the business
        /// inventory and the housing space must both accept the change, or
        /// neither applies (the inventory change is rolled back). Shrinking
        /// below occupied beds or current occupants is refused on both
        /// sides. Returns null on success, the refusal otherwise.
        /// </summary>
        public string ChangeRoomCapacity(
            IBoardingHouseLodgingSide house,
            string roomId,
            int newBedCount,
            int dayIndex,
            HousingAuthority housing,
            LodgingRegistry registry,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (house == null || housing == null || registry == null)
                return "BoardingLodgingBridge.ChangeRoomCapacity: house, housing authority and lodging registry are required.";
            BoardingRoom room = house.RoomInventory.FindRoom(roomId);
            if (room == null)
                return $"BoardingLodgingBridge.ChangeRoomCapacity: no room '{roomId}'.";
            LodgingRegistration registration = registry.FindActiveRegistration(house.BusinessInstanceId, roomId);
            if (registration == null)
                return $"BoardingLodgingBridge.ChangeRoomCapacity: room '{roomId}' has no active lodging registration — register it before resizing.";
            int oldCount = room.BedCount;

            string refusal = house.RoomInventory.ChangeRoomBedCount(roomId, newBedCount, diag);
            if (refusal != null) return refusal;

            refusal = housing.ChangeSpaceCapacity(registration.SpaceId, newBedCount, dayIndex, diag);
            if (refusal != null)
            {
                string rollback = house.RoomInventory.ChangeRoomBedCount(roomId, oldCount, diag);
                if (rollback != null)
                    diag.Add($"BoardingLodgingBridge.ChangeRoomCapacity: ROLLBACK FAILED for room '{roomId}': {rollback} — inventory and space disagree, quarantined loudly.");
                return refusal;
            }

            refusal = registry.SyncBedCount(registration.RegistrationId, newBedCount, diag);
            if (refusal != null) return refusal;
            diag.Add($"BoardingLodgingBridge: room '{roomId}' resized {oldCount} -> {newBedCount} bed(s) — inventory, space and registration agree.");
            return null;
        }
    }
}
