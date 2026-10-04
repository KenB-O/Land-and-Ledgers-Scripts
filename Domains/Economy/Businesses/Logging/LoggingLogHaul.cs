using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>W4C: where a log haul sits in its journey.</summary>
    public enum LogHaulStatus
    {
        Unspecified = 0,
        Planned = 1,
        Loading = 2,   // wagon loading at the landing
        InTransit = 3, // on the road — the lot is HERE, not at the landing or the mill
        Arrived = 4,   // at the mill yard, awaiting intake
        Delivered = 5, // custody transferred to the mill's log stock
        Failed = 6,    // refused at planning — the lot was never moved
    }

    /// <summary>
    /// W4C: one journey of one LogLot from a timber-stand landing to a
    /// sawmill log yard. This is the "extraction/skidding/hauling -> log
    /// landing or pond -> mill intake" leg of the Canon §8.5A causal chain.
    ///
    /// Physical honesty (MR-P001: physical movement is what counts, never
    /// booked intent):
    /// - the haul HOLDS the physical lot from dispatch until delivery — the
    ///   lot is in exactly one place at every moment (never duplicated,
    ///   never teleported);
    /// - travel time is REAL: the JRN-1 journey model routes the landing to
    ///   the mill in real miles at wagon speed, and an unroutable destination
    ///   refuses the haul loudly at planning time instead of assuming free
    ///   travel (cf. JourneyTravel.EstimateDrive);
    /// - the W4A provenance gate runs AGAIN at delivery
    ///   (SawmillLogStock.ReceiveLogLot refuses anonymous lots loudly), so a
    ///   lot can only enter a mill yard carrying its stand provenance.
    ///
    /// Save support follows the CLN-1 pattern; the DTO lives inside this
    /// class.
    /// </summary>
    [Serializable]
    public sealed class LogHaul
    {
        /// <summary>Calibration: minutes of loading work per log at the landing.</summary>
        public const int LoadingMinutesPerLog = 2;

        public EntityId HaulId = EntityId.Invalid;
        public LogLot Lot; // the physical lot in transit — held here, nowhere else
        public string OriginStandId = string.Empty;
        public string OriginLocationId = string.Empty; // JRN-1 location of the landing
        public string MillBusinessId = string.Empty;
        public string MillLocationId = string.Empty; // JRN-1 location of the mill
        public float RouteMiles;
        public int LoadingMinutesRemaining;
        public int TransitMinutesRemaining;
        public LogHaulStatus Status = LogHaulStatus.Unspecified;
        public string BlockedReason = string.Empty;

        public LogHaul() { }

        public bool IsActive => Status == LogHaulStatus.Loading || Status == LogHaulStatus.InTransit;

        /// <summary>
        /// Plans a haul: validates the lot's provenance, routes the journey,
        /// and returns a Loading haul holding the lot. Unroutable, anonymous,
        /// or empty lots are refused LOUDLY — the lot is never moved on
        /// refusal, and nothing is teleported.
        /// </summary>
        public static LogHaul PlanHaul(
            EntityIdRegistry idRegistry,
            LogLot lot,
            string millBusinessId,
            string millLocationId,
            JourneyModel journeyModel,
            string originLocationId,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (idRegistry == null)
            {
                diag.Add("LogHaul: no EntityIdRegistry — haul not planned.");
                return null;
            }
            if (lot == null)
            {
                diag.Add("LogHaul: no log lot offered — hauls are not conjured.");
                return null;
            }
            if (lot.LogUnits <= 0)
            {
                diag.Add($"LogHaul: lot {lot.LotId} carries no logs — nothing to haul.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(lot.StandId))
            {
                diag.Add($"LogHaul: REFUSED lot {lot.LotId} — no timber stand named. Anonymous logs " +
                    "cannot enter a mill yard (W4A provenance gate); the lot stays where it is.");
                return null;
            }
            if (journeyModel == null)
            {
                diag.Add("LogHaul: no journey model — travel time cannot be established; haul not planned.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(millBusinessId) || string.IsNullOrWhiteSpace(millLocationId))
            {
                diag.Add("LogHaul: the destination mill needs a business id and a journey location.");
                return null;
            }

            JourneyRoute route = journeyModel.FindRoute(
                originLocationId, millLocationId, TravelMode.Wagon);
            if (!route.Found)
            {
                diag.Add("LogHaul: REFUSED — " + route.Diagnostic +
                    " The lot stays at the landing; travel is not assumed free.");
                return null;
            }

            var haul = new LogHaul
            {
                HaulId = idRegistry.Allocate(EntityKind.Shipment),
                Lot = lot,
                OriginStandId = lot.StandId,
                OriginLocationId = originLocationId ?? string.Empty,
                MillBusinessId = millBusinessId,
                MillLocationId = millLocationId,
                RouteMiles = route.TotalMiles,
                LoadingMinutesRemaining = Math.Max(1, lot.LogUnits * LoadingMinutesPerLog),
                TransitMinutesRemaining = route.TotalMinutes,
                Status = LogHaulStatus.Loading,
            };
            diag.Add($"LogHaul {haul.HaulId}: planned — lot {lot.LotId} ({lot.LogUnits} logs, stand '{lot.StandId}') " +
                $"from '{haul.OriginLocationId}' to mill '{millBusinessId}' ({route.TotalMiles:F1} mi, " +
                $"{route.TotalMinutes} min wagon time).");
            return haul;
        }

        /// <summary>Advances the haul by whole minutes: Loading -> InTransit -> Arrived.</summary>
        public void AdvanceMinutes(int minutes, List<string> diag)
        {
            diag = diag ?? new List<string>();
            int remaining = Math.Max(0, minutes);
            while (remaining > 0 && IsActive)
            {
                if (Status == LogHaulStatus.Loading)
                {
                    int step = Math.Min(remaining, LoadingMinutesRemaining);
                    LoadingMinutesRemaining -= step;
                    remaining -= step;
                    if (LoadingMinutesRemaining <= 0)
                    {
                        Status = LogHaulStatus.InTransit;
                        diag.Add($"LogHaul {HaulId}: loaded — on the road ({TransitMinutesRemaining} min to the mill).");
                    }
                }
                else if (Status == LogHaulStatus.InTransit)
                {
                    int step = Math.Min(remaining, TransitMinutesRemaining);
                    TransitMinutesRemaining -= step;
                    remaining -= step;
                    if (TransitMinutesRemaining <= 0)
                    {
                        Status = LogHaulStatus.Arrived;
                        diag.Add($"LogHaul {HaulId}: arrived at mill '{MillBusinessId}' yard — awaiting intake.");
                    }
                }
            }
        }

        /// <summary>
        /// Delivers the held lot into the mill's log stock — the W4A
        /// provenance gate runs here (anonymous lots are refused loudly).
        /// Returns the refusal, or null on delivery. On refusal the haul stays
        /// Arrived holding the lot; the lot is never dropped.
        /// </summary>
        public string DeliverToMill(SawmillLogStock millStock, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (Status != LogHaulStatus.Arrived)
            {
                return $"LogHaul.DeliverToMill: haul {HaulId} is {Status}, not Arrived — no delivery.";
            }
            if (millStock == null)
                return $"LogHaul.DeliverToMill: no mill log stock named — the lot stays on the wagon.";
            if (Lot == null)
                return $"LogHaul.DeliverToMill: haul {HaulId} holds no lot — nothing to deliver.";

            string refusal = millStock.ReceiveLogLot(Lot, diag);
            if (refusal != null)
            {
                diag.Add($"LogHaul {HaulId}: DELIVERY REFUSED — {refusal} The lot stays on the wagon (Arrived); it is never dropped.");
                return refusal;
            }

            diag.Add($"LogHaul {HaulId}: delivered {Lot.LogUnits} logs (lot {Lot.LotId}, stand '{Lot.StandId}') " +
                $"into mill '{MillBusinessId}' log yard.");
            Lot = null; // custody transferred — the mill owns it now
            Status = LogHaulStatus.Delivered;
            return null;
        }

        /// <summary>W4C save contract: lives inside the owning haul class.</summary>
        [Serializable]
        public sealed class LogHaulSaveDto
        {
            // EntityId is a plain serializable struct — stored directly.
            public EntityId HaulId = EntityId.Invalid;
            public LogLot Lot; // null once delivered
            public string OriginStandId = string.Empty;
            public string OriginLocationId = string.Empty;
            public string MillBusinessId = string.Empty;
            public string MillLocationId = string.Empty;
            public float RouteMiles;
            public int LoadingMinutesRemaining;
            public int TransitMinutesRemaining;
            public LogHaulStatus Status = LogHaulStatus.Unspecified;
            public string BlockedReason = string.Empty;
        }

        public LogHaulSaveDto CaptureSaveDto()
        {
            return new LogHaulSaveDto
            {
                HaulId = HaulId,
                Lot = Lot == null ? null : new LogLot
                {
                    LotId = Lot.LotId,
                    LogUnits = Lot.LogUnits,
                    StandId = Lot.StandId,
                    Species = Lot.Species,
                    FelledBy = Lot.FelledBy,
                    FelledDayIndex = Lot.FelledDayIndex,
                },
                OriginStandId = OriginStandId,
                OriginLocationId = OriginLocationId,
                MillBusinessId = MillBusinessId,
                MillLocationId = MillLocationId,
                RouteMiles = RouteMiles,
                LoadingMinutesRemaining = Math.Max(0, LoadingMinutesRemaining),
                TransitMinutesRemaining = Math.Max(0, TransitMinutesRemaining),
                Status = Status,
                BlockedReason = BlockedReason ?? string.Empty,
            };
        }

        public static LogHaul FromSaveDto(LogHaulSaveDto dto)
        {
            var haul = new LogHaul();
            if (dto == null) return haul;
            haul.HaulId = dto.HaulId;
            haul.Lot = dto.Lot;
            haul.OriginStandId = dto.OriginStandId ?? string.Empty;
            haul.OriginLocationId = dto.OriginLocationId ?? string.Empty;
            haul.MillBusinessId = dto.MillBusinessId ?? string.Empty;
            haul.MillLocationId = dto.MillLocationId ?? string.Empty;
            haul.RouteMiles = Math.Max(0f, dto.RouteMiles);
            haul.LoadingMinutesRemaining = Math.Max(0, dto.LoadingMinutesRemaining);
            haul.TransitMinutesRemaining = Math.Max(0, dto.TransitMinutesRemaining);
            haul.Status = dto.Status;
            haul.BlockedReason = dto.BlockedReason ?? string.Empty;
            return haul;
        }
    }
}
